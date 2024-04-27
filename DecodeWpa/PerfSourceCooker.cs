// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK;
    using Microsoft.Performance.SDK.Extensibility;
    using Microsoft.Performance.SDK.Extensibility.DataCooking;
    using Microsoft.Performance.SDK.Extensibility.DataCooking.SourceDataCooking;
    using Microsoft.Performance.SDK.Processing;
    using System;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// Generic cooker for PerfSourceParser.
    /// Collects all event data and file info from a perf.data processing session.
    /// </summary>
    public sealed class PerfSourceCooker : SourceDataCooker<PerfEventData, PerfFileInfo, PerfEventHeaderType>
    {
        private static readonly ReadOnlyHashSet<PerfEventHeaderType> EmptySet = new ReadOnlyHashSet<PerfEventHeaderType>(new HashSet<PerfEventHeaderType>());

        public static readonly DataCookerPath DataCookerPath = DataCookerPath.ForSource(PerfSourceParser.SourceParserId, PerfSourceCooker.DataCookerId);
        public static readonly DataOutputPath EventsOutputPath = DataOutputPath.ForSource(PerfSourceParser.SourceParserId, PerfSourceCooker.DataCookerId, nameof(Events));
        public static readonly DataOutputPath SessionTimestampOffsetOutputPath = DataOutputPath.ForSource(PerfSourceParser.SourceParserId, PerfSourceCooker.DataCookerId, nameof(SessionTimestampOffset));

        private PerfFileInfo? lastContext;

        public PerfSourceCooker()
            : base(DataCookerPath)
        {
            return;
        }

        public const string DataCookerId = nameof(PerfSourceCooker);

        public override string Description => "Collects all event data and file info from a perf.data processing session.";

        public override ReadOnlyHashSet<PerfEventHeaderType> DataKeys => EmptySet;

        public override SourceDataCookerOptions Options => SourceDataCookerOptions.ReceiveAllDataElements;

        [DataOutput]
        public ProcessedEventData<ValueTuple<PerfEventData, PerfFileInfo>> Events { get; } = new ProcessedEventData<ValueTuple<PerfEventData, PerfFileInfo>>();

        [DataOutput]
        public long SessionTimestampOffset { get; private set; } = long.MinValue;

        public override DataProcessingResult CookDataElement(PerfEventData data, PerfFileInfo context, CancellationToken cancellationToken)
        {
            this.lastContext = context;
            this.Events.AddEvent(new ValueTuple<PerfEventData, PerfFileInfo>(data, context));
            return DataProcessingResult.Processed;
        }

        public override void EndDataCooking(CancellationToken cancellationToken)
        {
            this.Events.FinalizeData();
            this.SessionTimestampOffset = this.lastContext != null
                ? this.lastContext.SessionTimestampOffset
                : 0;
        }
    }
}
