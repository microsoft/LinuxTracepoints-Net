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

    public sealed class PerfSourceParser : SourceParser<PerfEventData, PerfFileInfo, PerfEventHeaderType>
    {
        private const uint Billion = 1000000000;

        private readonly HashSet<PerfEventHeaderType> requestedDataKeys = new HashSet<PerfEventHeaderType>();
        private readonly string[] filenames;
        private readonly List<PerfFileInfo> fileInfos;
        private readonly ReadOnlyCollection<PerfFileInfo> fileInfosReadOnly;
        private DataSourceInfo? dataSourceInfo;
        private bool requestedAllEvents;

        public PerfSourceParser(string[] filenames)
        {
            this.filenames = filenames;
            this.fileInfos = new List<PerfFileInfo>(filenames.Length);
            this.fileInfosReadOnly = this.fileInfos.AsReadOnly();
        }

        public const string SourceParserId = nameof(PerfSourceParser);

        public override string Id => SourceParserId;

        public ReadOnlyCollection<PerfFileInfo> FileInfos => this.fileInfosReadOnly;

        public override DataSourceInfo DataSourceInfo => this.dataSourceInfo!;

        public override void PrepareForProcessing(bool allEventsConsumed, IReadOnlyCollection<PerfEventHeaderType> requestedDataKeys)
        {
            this.requestedAllEvents = allEventsConsumed;
            this.requestedDataKeys.Clear();
            this.requestedDataKeys.UnionWith(requestedDataKeys);
        }

        public override void ProcessSource(
            ISourceDataProcessor<PerfEventData, PerfFileInfo, PerfEventHeaderType> dataProcessor,
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
                        logger.Warn("Processing cancelled with {0}/{1} files loaded.",
                            filesProcessed,
                            this.filenames.Length);
                        break;
                    }

                    var filename = this.filenames[filesProcessed];

                    if (!reader.OpenFile(filename, PerfDataFileEventOrder.Time))
                    {
                        logger.Error("Invalid data, cannot open file: {0}",
                            filename);
                        continue;
                    }

                    logger.Info("Opened file: {0}",
                        filename);

                    var byteReader = reader.ByteReader;
                    var fileInfo = new FileInfo(filename, byteReader);

                    var setHeaderAttributes = false;
                    var firstEventTime = ulong.MaxValue;
                    var lastEventTime = ulong.MinValue;
                    var commonFieldCount = ushort.MaxValue;
                    var previousEventTime = ulong.MinValue;
                    var eventCount = 0u;

                    while (true)
                    {
                        // For each event in the file:

                        if (cancellationToken.IsCancellationRequested)
                        {
                            logger.Warn("Processing cancelled with {0}/{1} files loaded; partially loaded file: {2}",
                                filesProcessed,
                                this.filenames.Length,
                                filename);
                            break;
                        }

                        var result = reader.ReadEvent(out eventBytes);
                        if (result != PerfDataFileResult.Ok)
                        {
                            if (result != PerfDataFileResult.EndOfFile)
                            {
                                logger.Error("Error: {0} reading from file: {1}",
                                    result.AsString(),
                                    filename);
                            }

                            break; // No more events in this file
                        }

                        if (!setHeaderAttributes)
                        {
                            if (eventBytes.Header.Type == PerfEventHeaderType.FinishedInit ||
                                eventBytes.Header.Type == PerfEventHeaderType.Sample)
                            {
                                setHeaderAttributes = true;
                                fileInfo.SetHeaderAttributes(reader);
                            }
                        }

                        eventCount += 1; // Include ignored events in the count.

                        if (!this.requestedAllEvents && !this.requestedDataKeys.Contains(eventBytes.Header.Type))
                        {
                            continue;
                        }

                        PerfEventData eventInfo;
                        if (eventBytes.Header.Type == PerfEventHeaderType.Sample)
                        {
                            result = reader.GetSampleEventInfo(eventBytes, out sampleEventInfo);
                            if (result != PerfDataFileResult.Ok)
                            {
                                logger.Warn("Skipped event: {0} resolving sample event from file: {1}",
                                    result.AsString(),
                                    filename);
                                continue;
                            }

                            if (sampleEventInfo.SampleType.HasFlag(PerfEventAttrSampleType.Time))
                            {
                                previousEventTime = sampleEventInfo.Time;
                            }
                            else
                            {
                                sampleEventInfo.Time = previousEventTime;
                            }

                            var format = sampleEventInfo.Format;
                            if (format.IsEmpty)
                            {
                                logger.Warn("Skipped event: no format information for sample event in file: {0}",
                                    filename);
                                continue;
                            }

                            if (format.Fields.Count >= ushort.MaxValue)
                            {
                                logger.Warn("Skipped event: bad field count for sample event in file: {0}",
                                    filename);
                                continue;
                            }

                            if (format.CommonFieldCount > format.Fields.Count)
                            {
                                logger.Warn("Skipped event: bad CommonFieldCount for sample event in file: {0}",
                                    filename);
                                continue;
                            }

                            if (format.CommonFieldCount != commonFieldCount)
                            {
                                if (commonFieldCount != ushort.MaxValue)
                                {
                                    logger.Warn("Skipped event: inconsistent CommonFieldCount for sample event in file: {0}",
                                        filename);
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
                                eventInfo = new PerfEventData(byteReader, eventBytes.Header, sampleEventInfo);
                            }
                            else
                            {
                                var userData = sampleEventInfo.UserDataSpan;
                                var info = enumerator.GetEventInfo(userData);

                                uint topLevelFieldCount = 0;
                                uint structFields = 0;
                                while (enumerator.MoveNextMetadata(userData))
                                {
                                    if (structFields == 0)
                                    {
                                        topLevelFieldCount += 1;
                                    }
                                    else
                                    {
                                        structFields -= 1;
                                    }

                                    var type = enumerator.GetItemType();
                                    if (type.Encoding == EventHeaderFieldEncoding.Struct)
                                    {
                                        structFields += type.StructFieldCount;
                                    }
                                }

                                eventInfo = new PerfEventData(byteReader, eventBytes.Header, sampleEventInfo, info, (ushort)topLevelFieldCount);
                            }
                        }
                        else
                        {
                            result = reader.GetNonSampleEventInfo(eventBytes, out var nonSampleEventInfo);
                            if (result == PerfDataFileResult.Ok)
                            {
                                if (nonSampleEventInfo.SampleType.HasFlag(PerfEventAttrSampleType.Time))
                                {
                                    previousEventTime = nonSampleEventInfo.Time;
                                }
                                else
                                {
                                    nonSampleEventInfo.Time = previousEventTime;
                                }

                                if (nonSampleEventInfo.Time < firstEventTime)
                                {
                                    firstEventTime = nonSampleEventInfo.Time;
                                }

                                if (nonSampleEventInfo.Time > lastEventTime)
                                {
                                    lastEventTime = nonSampleEventInfo.Time;
                                }

                                eventInfo = new PerfEventData(byteReader, eventBytes.Header, nonSampleEventInfo);
                            }
                            else if (result == PerfDataFileResult.IdNotFound)
                            {
                                // Event info not available for this event. Maybe ok.
                                eventInfo = new PerfEventData(byteReader, eventBytes, previousEventTime);
                            }
                            else
                            {
                                logger.Warn("Skipped event: {0} resolving nonsample event from file: {1}",
                                    result.AsString(),
                                    filename);
                                continue;
                            }
                        }

                        dataProcessor.ProcessDataElement(eventInfo, fileInfo, cancellationToken);
                    }

                    fileInfo.SetFileAttributes(firstEventTime, lastEventTime, eventCount);

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

                    logger.Info("Finished file: {0}",
                        filename);

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
                    ((FileInfo)fileInfo).SetSessionAttributes(sessionTimestampOffset);

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

        private sealed class FileInfo : PerfFileInfo
        {
            public FileInfo(string filename, PerfByteReader byteReader)
                : base(filename, byteReader)
            {
                return;
            }

            public new void SetHeaderAttributes(PerfDataFileReader reader)
            {
                base.SetHeaderAttributes(reader);
            }

            public new void SetFileAttributes(ulong firstEventTime, ulong lastEventTime, uint eventCount)
            {
                base.SetFileAttributes(firstEventTime, lastEventTime, eventCount);
            }

            public new void SetSessionAttributes(long sessionTimestampOffset)
            {
                base.SetSessionAttributes(sessionTimestampOffset);
            }
        }
    }
}
