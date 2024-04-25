namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.Performance.SDK;
    using Microsoft.Performance.SDK.Extensibility;
    using Microsoft.Performance.SDK.Extensibility.DataCooking;
    using Microsoft.Performance.SDK.Extensibility.DataCooking.SourceDataCooking;
    using Microsoft.Performance.SDK.Processing;
    using System.Collections.Generic;
    using System.Threading;

    public sealed class PerfSourceCooker : SourceDataCooker<PerfEventInfo, PerfFileInfo, uint>
    {
        private static readonly ReadOnlyHashSet<uint> EmptySet = new ReadOnlyHashSet<uint>(new HashSet<uint>());

        public static readonly DataCookerPath DataCookerPath = DataCookerPath.ForSource(PerfSourceParser.SourceParserId, PerfSourceCooker.DataCookerId);
        public static readonly DataOutputPath EventsOutputPath = DataOutputPath.ForSource(PerfSourceParser.SourceParserId, PerfSourceCooker.DataCookerId, nameof(Events));

        public PerfSourceCooker()
            : base(DataCookerPath)
        {
            return;
        }

        public const string DataCookerId = nameof(PerfSourceCooker);

        public override string Description => "Collects all PerfEventInfo objects";

        public override ReadOnlyHashSet<uint> DataKeys => EmptySet;

        public override SourceDataCookerOptions Options => SourceDataCookerOptions.ReceiveAllDataElements;

        [DataOutput]
        public ProcessedEventData<PerfEventInfo> Events { get; } = new ProcessedEventData<PerfEventInfo>();

        public override DataProcessingResult CookDataElement(PerfEventInfo data, PerfFileInfo context, CancellationToken cancellationToken)
        {
            this.Events.AddEvent(data);
            return DataProcessingResult.Processed;
        }

        public override void EndDataCooking(CancellationToken cancellationToken)
        {
            this.Events.FinalizeData();
        }
    }
}
