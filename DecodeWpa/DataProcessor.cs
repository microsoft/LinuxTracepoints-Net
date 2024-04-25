namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK.Processing;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal sealed class DataProcessor : CustomDataProcessor
    {
        private const uint Billion = 1000000000;
        private readonly ProcessedEventData<EventInfo> events = new ProcessedEventData<EventInfo>();
        private readonly List<FileInfo> fileInfos = new List<FileInfo>();
        private readonly ReadOnlyCollection<FileInfo> fileInfosReadOnly;
        private readonly string[] filenames;
        private DataSourceInfo? dataSourceInfo;

        internal DataProcessor(
            string[] filenames,
            ProcessorOptions options,
            IApplicationEnvironment applicationEnvironment,
            IProcessorEnvironment processorEnvironment)
            : base(options, applicationEnvironment, processorEnvironment)
        {
            this.fileInfosReadOnly = this.fileInfos.AsReadOnly();
            this.filenames = filenames;
        }

        public override DataSourceInfo GetDataSourceInfo()
        {
            if (this.dataSourceInfo == null)
            {
                throw new InvalidOperationException("DataSourceInfo is not available until processing is complete.");
            }

            return this.dataSourceInfo;
        }

        protected override void BuildTableCore(TableDescriptor tableDescriptor, ITableBuilder tableBuilder)
        {
            if (tableDescriptor.Guid == PerfGenericEventsTable.TableDescriptor.Guid)
            {
                new PerfGenericEventsTable(this.events).Build(tableBuilder);
            }
            else if (tableDescriptor.Guid == PerfFilesTable.TableDescriptor.Guid)
            {
                new PerfFilesTable(this.fileInfosReadOnly).Build(tableBuilder);
            }
            else
            {
                throw new InvalidOperationException("Unknown table descriptor.");
            }
        }

        protected override Task ProcessAsyncCore(
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            return Task.Run(
                () => { this.ProcessAsyncImpl(progress, cancellationToken); },
                cancellationToken);
        }

        private void ProcessAsyncImpl(IProgress<int> progress, CancellationToken cancellationToken)
        {
            var sessionFirstTimeSpec = PerfTimeSpec.MaxValue;
            using (var reader = new PerfDataFileReader())
            {
                var internedStrings = new HashSet<string>();
                var enumerator = new EventHeaderEnumerator();
                var sb = new StringBuilder();
                PerfEventBytes eventBytes;
                PerfSampleEventInfo sampleEventInfo;

                for (int filesProcessed = 0; filesProcessed < this.filenames.Length; filesProcessed += 1)
                {
                    // For each file:

                    progress.Report((filesProcessed * 100) / this.filenames.Length);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var filename = this.filenames[filesProcessed];
                    var fileInfo = new FileInfo(filename);

                    if (!reader.OpenFile(filename, PerfDataFileEventOrder.File))
                    {
                        Logger.Error("Failed to open file: {0}", filename);
                        continue;
                    }

                    var firstEventTime = ulong.MaxValue;
                    var lastEventTime = ulong.MinValue;
                    var commonFieldCount = ushort.MaxValue;
                    var eventCount = 0u;

                    while (true)
                    {
                        // For each event in the file:

                        if (cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        var result = reader.ReadEvent(out eventBytes);
                        if (result != PerfDataFileResult.Ok)
                        {
                            if (result != PerfDataFileResult.EndOfFile)
                            {
                                Logger.Error("Error {0} reading from file: {1}", result.ToString(), filename);
                            }

                            break; // No more events in this file
                        }

                        if (eventBytes.Header.Type == PerfEventHeaderType.Sample)
                        {
                            result = reader.GetSampleEventInfo(eventBytes, out sampleEventInfo);
                            if (result != PerfDataFileResult.Ok)
                            {
                                Logger.Warn("Skipped event: {0} reading sample event eventInfo from file: {1}", result.ToString(), filename);
                                continue;
                            }

                            var format = sampleEventInfo.Format;
                            if (format == null)
                            {
                                Logger.Warn("Skipped event: no format information: {0}", filename);
                                continue;
                            }

                            if (format.Fields.Count >= ushort.MaxValue)
                            {
                                Logger.Warn("Skipped event: bad field count: {0}", filename);
                                continue;
                            }

                            if (format.CommonFieldCount > format.Fields.Count)
                            {
                                Logger.Warn("Skipped event: bad CommonFieldCount: {0}", filename);
                                continue;
                            }

                            if (format.CommonFieldCount != commonFieldCount)
                            {
                                if (commonFieldCount != ushort.MaxValue)
                                {
                                    Logger.Warn("Skipped event: inconsistent CommonFieldCount: {0}", filename);
                                    continue;
                                }

                                commonFieldCount = format.CommonFieldCount;
                            }

                            if (sampleEventInfo.Time < firstEventTime)
                            {
                                firstEventTime = sampleEventInfo.Time;
                            }

                            if (sampleEventInfo.Time > lastEventTime)
                            {
                                lastEventTime = sampleEventInfo.Time;
                            }

                            if (format.DecodingStyle != PerfEventDecodingStyle.EventHeader ||
                                !enumerator.StartEvent(sampleEventInfo))
                            {
                                var name = sampleEventInfo.Name;
                                this.events.AddEvent(new EventInfo(fileInfo, sampleEventInfo, name));
                            }
                            else
                            {
                                var ehEventInfo = enumerator.GetEventInfo();
                                sb.EnsureCapacity(ehEventInfo.ProviderName.Length + 1 + ehEventInfo.NameLength);
                                sb.Append(ehEventInfo.ProviderName);
                                sb.Append(':');
                                PerfConvert.StringAppend(sb, ehEventInfo.NameBytes, Encoding.UTF8);
                                var newName = sb.ToString();
                                sb.Clear();
                                if (!internedStrings.TryGetValue(newName, out var name))
                                {
                                    internedStrings.Add(newName);
                                    name = newName;
                                }

                                this.events.AddEvent(new EventInfo(fileInfo, sampleEventInfo, name, ehEventInfo));
                            }

                            eventCount += 1;
                        }
                    }

                    fileInfo.SetFromReader(reader, firstEventTime, lastEventTime, eventCount);

                    // Track the wall-clock time of the first event in the session
                    // (but only if the file had one or more time-stamped events).
                    if (firstEventTime <= lastEventTime)
                    {
                        var fileFirstTimeSpec = fileInfo.FirstEventTimeSpec;
                        if (fileFirstTimeSpec < sessionFirstTimeSpec)
                        {
                            sessionFirstTimeSpec = fileFirstTimeSpec;
                        }
                    }

                    this.fileInfos.Add(fileInfo);
                }
            }

            this.events.FinalizeData();

            long sessionFirst;
            long sessionLast;
            if (sessionFirstTimeSpec == PerfTimeSpec.MaxValue)
            {
                // No events were found in any file.
                sessionFirstTimeSpec = PerfTimeSpec.UnixEpoch;
                sessionFirst = 0;
                sessionLast = 0;
            }
            else
            {
                sessionFirst = long.MaxValue;
                sessionLast = long.MinValue;
                foreach (var fileInfo in this.fileInfos)
                {
                    var fileOffsetSpec = fileInfo.ClockOffset;

                    // Compute the difference between session-relative and file-relative timestamps.
                    // sessionFirstTimeSpec + sessionTimestampOffset = fileOffsetSpec.
                    var sessionTimestampOffset =
                        (fileOffsetSpec.TvSec - sessionFirstTimeSpec.TvSec) * Billion
                        + fileOffsetSpec.TvNsec - sessionFirstTimeSpec.TvNsec;

                    // File-relative timestamp + SessionTimestampOffset = session-relative timestamp.
                    fileInfo.SetSessionTimestampOffset(sessionTimestampOffset);

                    var fileFirstFileRelative = fileInfo.FirstEventTime;
                    var fileLastFileRelative = fileInfo.LastEventTime;
                    if (fileFirstFileRelative <= fileLastFileRelative)
                    {
                        var fileFirst = (long)fileFirstFileRelative + sessionTimestampOffset;
                        if (fileFirst < sessionFirst)
                        {
                            sessionFirst = fileFirst;
                        }

                        var fileLast = (long)fileLastFileRelative + sessionTimestampOffset;
                        if (fileLast > sessionLast)
                        {
                            sessionLast = fileLast;
                        }
                    }
                }
            }

            Debug.Assert(sessionFirst == 0);
            Debug.Assert(sessionFirst <= sessionLast);
            this.dataSourceInfo = new DataSourceInfo(
                0,
                sessionLast,
                sessionFirstTimeSpec.DateTime ?? DateTime.UnixEpoch);

            progress.Report(100);
        }
    }
}
