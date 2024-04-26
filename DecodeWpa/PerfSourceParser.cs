// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK.Extensibility.SourceParsing;
    using Microsoft.Performance.SDK.Processing;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Text;
    using CancellationToken = System.Threading.CancellationToken;
    using Debug = System.Diagnostics.Debug;

    public sealed class PerfSourceParser : SourceParser<PerfEventInfo, PerfFileInfo, PerfEventHeaderType>
    {
        private const uint Billion = 1000000000;

        private readonly HashSet<PerfEventHeaderType> requestedDataKeys = new HashSet<PerfEventHeaderType>();
        private readonly List<PerfFileInfo> fileInfos;
        private readonly ReadOnlyCollection<PerfFileInfo> fileInfosReadOnly;
        private readonly string[] filenames;
        private DataSourceInfo? dataSourceInfo;
        private bool allEventsConsumed;

        public PerfSourceParser(string[] filenames)
        {
            this.filenames = filenames;
            this.fileInfos = new List<PerfFileInfo>(filenames.Length);
            this.fileInfosReadOnly = this.fileInfos.AsReadOnly();
        }

        public const string SourceParserId = nameof(PerfSourceParser);

        public override string Id => SourceParserId;

        public ReadOnlyCollection<PerfFileInfo> FileInfos => this.fileInfosReadOnly;

        public override DataSourceInfo DataSourceInfo
        {
            get
            {
                if (this.dataSourceInfo == null)
                {
                    throw new InvalidOperationException("DataSourceInfo is not available until processing is complete.");
                }

                return this.dataSourceInfo;
            }
        }

        public override void PrepareForProcessing(bool allEventsConsumed, IReadOnlyCollection<PerfEventHeaderType> requestedDataKeys)
        {
            this.allEventsConsumed = allEventsConsumed;
            this.requestedDataKeys.Clear();
            this.requestedDataKeys.UnionWith(requestedDataKeys);
        }

        public override void ProcessSource(
            ISourceDataProcessor<PerfEventInfo, PerfFileInfo, PerfEventHeaderType> dataProcessor,
            ILogger logger,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            this.fileInfos.Clear();

            var sessionFirstTimeSpec = PerfTimeSpec.MaxValue;
            using (var reader = new PerfDataFileReader())
            {
                var enumerator = new EventHeaderEnumerator();
                var sb = new StringBuilder();
                PerfEventBytes eventBytes;
                PerfSampleEventInfo sampleEventInfo;

                for (var filesProcessed = 0; filesProcessed < this.filenames.Length; filesProcessed += 1)
                {
                    // For each file:

                    progress.Report((filesProcessed * 100) / this.filenames.Length);
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    var filename = this.filenames[filesProcessed];
                    var fileInfo = new PerfFileInfo(filename);

                    // TODO: Is there any benefit in supporting time-ordered processing here?
                    if (!reader.OpenFile(filename, PerfDataFileEventOrder.File))
                    {
                        logger.Error("Failed to open file: {0}", filename);
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
                                logger.Error("Error {0} reading from file: {1}", result.ToString(), filename);
                            }

                            break; // No more events in this file
                        }

                        if (!this.allEventsConsumed && !this.requestedDataKeys.Contains(eventBytes.Header.Type))
                        {
                            continue;
                        }

                        PerfEventInfo eventInfo;
                        if (eventBytes.Header.Type == PerfEventHeaderType.Sample)
                        {
                            result = reader.GetSampleEventInfo(eventBytes, out sampleEventInfo);
                            if (result != PerfDataFileResult.Ok)
                            {
                                logger.Warn("Skipped event: {0} reading sample event eventInfo from file: {1}", result.ToString(), filename);
                                continue;
                            }

                            var format = sampleEventInfo.Format;
                            if (format == null)
                            {
                                logger.Warn("Skipped event: no format information: {0}", filename);
                                continue;
                            }

                            if (format.Fields.Count >= ushort.MaxValue)
                            {
                                logger.Warn("Skipped event: bad field count: {0}", filename);
                                continue;
                            }

                            if (format.CommonFieldCount > format.Fields.Count)
                            {
                                logger.Warn("Skipped event: bad CommonFieldCount: {0}", filename);
                                continue;
                            }

                            if (format.CommonFieldCount != commonFieldCount)
                            {
                                if (commonFieldCount != ushort.MaxValue)
                                {
                                    logger.Warn("Skipped event: inconsistent CommonFieldCount: {0}", filename);
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
                                eventInfo = new PerfEventInfo(fileInfo, eventBytes.Header, sampleEventInfo);
                            }
                            else
                            {
                                var info = enumerator.GetEventInfo();
                                eventInfo = new PerfEventInfo(fileInfo, eventBytes.Header, sampleEventInfo, info);
                            }
                        }
                        else
                        {
                            result = reader.GetNonSampleEventInfo(eventBytes, out var nonSampleEventInfo);
                            if (result == PerfDataFileResult.Ok)
                            {
                                if (nonSampleEventInfo.Time < firstEventTime)
                                {
                                    firstEventTime = nonSampleEventInfo.Time;
                                }

                                if (nonSampleEventInfo.Time > lastEventTime)
                                {
                                    lastEventTime = nonSampleEventInfo.Time;
                                }

                                eventInfo = new PerfEventInfo(fileInfo, eventBytes.Header, nonSampleEventInfo);
                            }
                            else if (result == PerfDataFileResult.IdNotFound)
                            {
                                eventInfo = new PerfEventInfo(fileInfo, eventBytes);
                            }
                            else
                            {
                                logger.Warn("Skipped event: {0} reading nonsample event eventInfo from file: {1}", result.ToString(), filename);
                                continue;
                            }
                        }

                        dataProcessor.ProcessDataElement(eventInfo, fileInfo, cancellationToken);
                        eventCount += 1;
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
                    fileInfo.SetSourceParserFinished(sessionTimestampOffset);

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
