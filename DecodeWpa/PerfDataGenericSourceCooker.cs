// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.Performance.Toolkit.Plugins.PerfDataExtension
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
    public sealed class PerfDataGenericSourceCooker : SourceDataCooker<PerfDataEvent, PerfDataFileInfo, PerfEventHeaderType>
    {
        private static readonly ReadOnlyHashSet<PerfEventHeaderType> EmptySet = new ReadOnlyHashSet<PerfEventHeaderType>(new HashSet<PerfEventHeaderType>());

        public static readonly DataCookerPath DataCookerPath = DataCookerPath.ForSource(PerfDataSourceParser.SourceParserId, PerfDataGenericSourceCooker.DataCookerId);
        public static readonly DataOutputPath EventsOutputPath = DataOutputPath.ForSource(PerfDataSourceParser.SourceParserId, PerfDataGenericSourceCooker.DataCookerId, nameof(Events));
        public static readonly DataOutputPath SessionTimestampOffsetOutputPath = DataOutputPath.ForSource(PerfDataSourceParser.SourceParserId, PerfDataGenericSourceCooker.DataCookerId, nameof(SessionTimestampOffset));
        public static readonly DataOutputPath MaxTopLevelFieldCountOutputPath = DataOutputPath.ForSource(PerfDataSourceParser.SourceParserId, PerfDataGenericSourceCooker.DataCookerId, nameof(MaxTopLevelFieldCount));

        private PerfDataFileInfo? lastContext;

        public PerfDataGenericSourceCooker()
            : base(DataCookerPath)
        {
            return;
        }

        public const string DataCookerId = nameof(PerfDataGenericSourceCooker);

        public override string Description => "Collects all event data and file info from a perf.data processing session.";

        public override ReadOnlyHashSet<PerfEventHeaderType> DataKeys => EmptySet;

        public override SourceDataCookerOptions Options => SourceDataCookerOptions.ReceiveAllDataElements;

        [DataOutput]
        public ProcessedEventData<ValueTuple<PerfDataEvent, PerfDataFileInfo>> Events { get; } = new ProcessedEventData<ValueTuple<PerfDataEvent, PerfDataFileInfo>>();

        [DataOutput]
        public long SessionTimestampOffset { get; private set; } = long.MinValue;

        [DataOutput]
        public ushort MaxTopLevelFieldCount { get; private set; } = 0;

        public override DataProcessingResult CookDataElement(PerfDataEvent data, PerfDataFileInfo context, CancellationToken cancellationToken)
        {
            this.lastContext = context;
            this.Events.AddEvent(new ValueTuple<PerfDataEvent, PerfDataFileInfo>(data, context));

            var topLevelFieldCount = data.TopLevelFieldCount;
            if (topLevelFieldCount > this.MaxTopLevelFieldCount)
            {
                this.MaxTopLevelFieldCount = topLevelFieldCount;
            }

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
