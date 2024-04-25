namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK.Extensibility;
    using Microsoft.Performance.SDK.Processing;
    using System;
    using System.Diagnostics.Tracing;
    using System.Text;

    [Table]
    [RequiresSourceCooker(PerfSourceParser.SourceParserId, PerfSourceCooker.DataCookerId)]
    public sealed class PerfGenericEventsTable
    {
        private readonly ProcessedEventData<PerfEventInfo> events;
        private readonly string[] values;
        private readonly EventHeaderEnumerator enumerator = new EventHeaderEnumerator();
        private readonly StringBuilder sb = new StringBuilder();

        public static readonly TableDescriptor TableDescriptor = new TableDescriptor(
            Guid.Parse("84efc851-2466-4c79-b856-3d76d59c4935"),
            "perf.data events",
            "Events loaded from a perf.data file");

        private static readonly ColumnConfiguration columnCount = new ColumnConfiguration(
            new ColumnMetadata(new Guid("7f412945-4226-4b33-b688-b353eb8e15c4"), "Count"),
            new UIHints
            {
                IsVisible = true,
                Width = 80,
                AggregationMode = AggregationMode.Sum,
            });

        private static readonly ColumnConfiguration columnName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("a76305f4-bda2-4c5a-b5e3-73c4ae046d0a"), "Name"),
            new UIHints
            {
                IsVisible = true,
                Width = 200,
            });

        private static readonly ColumnConfiguration columnFilename = new ColumnConfiguration(
            new ColumnMetadata(new Guid("dd543500-56a8-4d19-a605-6a85ff616306"), "Filename"),
            new UIHints
            {
                IsVisible = true,
                Width = 30,
            });

        private static readonly ColumnConfiguration columnTimestamp = new ColumnConfiguration(
            new ColumnMetadata(new Guid("ad35e366-91e9-46e6-8d9d-ebb84ca4b3cf"), "Timestamp"),
            new UIHints
            {
                IsVisible = true,
                Width = 30,
            });

        private static readonly ColumnConfiguration columnTime = new ColumnConfiguration(
            new ColumnMetadata(new Guid("acdd3326-57e0-4271-b8bc-05ef78eddd3a"), "Time"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnCpu = new ColumnConfiguration(
            new ColumnMetadata(new Guid("a2b02251-64bb-4bbc-9224-cff29423e199"), "Cpu"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnPid = new ColumnConfiguration(
            new ColumnMetadata(new Guid("3598598d-1e4a-40ad-a463-32b7cfb1b721"), "Pid"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnTid = new ColumnConfiguration(
            new ColumnMetadata(new Guid("cd447f69-0964-4663-bd3e-8763ac5fb15a"), "Tid"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnHasEventHeader = new ColumnConfiguration(
            new ColumnMetadata(new Guid("0d1c453a-3805-4d99-a64a-29e6ad1602e8"), "HasEventHeader"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnEventHeaderFlags = new ColumnConfiguration(
            new ColumnMetadata(new Guid("9987bcc7-5c68-4105-a6b8-32afe6b1cfe2"), "EventHeaderFlags"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnId = new ColumnConfiguration(
            new ColumnMetadata(new Guid("fbc36c32-a6fc-4ace-b576-bca3ab1fdf21"), "Id"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnVersion = new ColumnConfiguration(
            new ColumnMetadata(new Guid("cea693d5-e0ff-4c55-b7d4-4f98f1114a68"), "Version"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnTag = new ColumnConfiguration(
            new ColumnMetadata(new Guid("e638de35-56d8-4234-b204-bb6927994eeb"), "Tag"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
                CellFormat = ColumnFormats.HexFormat,
            });

        private static readonly ColumnConfiguration columnOpcode = new ColumnConfiguration(
            new ColumnMetadata(new Guid("1cb5cfce-3ace-4d17-84c9-b8af01d3a47a"), "Opcode"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnLevel = new ColumnConfiguration(
            new ColumnMetadata(new Guid("9a7f5539-6c9f-4d23-bf0a-aa663e30b48f"), "Level"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnKeyword = new ColumnConfiguration(
            new ColumnMetadata(new Guid("7c9071dd-c49d-4db1-852d-76ec31fa1938"), "Keyword"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
                CellFormat = ColumnFormats.HexFormat,
            });

        private static readonly ColumnConfiguration columnActivityId = new ColumnConfiguration(
            new ColumnMetadata(new Guid("ccfff171-c773-43de-80a9-f6e3e0b81090"), "ActivityId"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnRelatedId = new ColumnConfiguration(
            new ColumnMetadata(new Guid("27c061c5-ff89-4604-85f6-6c27094a62fa"), "RelatedId"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnValue = new ColumnConfiguration(
            new ColumnMetadata(new Guid("5f5580b6-15e9-4a57-83ba-9160dc1eae3b"), "Value"),
            new UIHints
            {
                IsVisible = true,
                Width = 200,
            });

        private PerfGenericEventsTable(ProcessedEventData<PerfEventInfo> events)
        {
            this.events = events;
            this.values = new string[this.events.Count];
        }

        public static void BuildTable(
            ITableBuilder tableBuilder,
            IDataExtensionRetrieval requiredData)
        {
            var data = requiredData.QueryOutput<ProcessedEventData<PerfEventInfo>>(PerfSourceCooker.EventsOutputPath);
            var table = new PerfGenericEventsTable(data);
            var builder = tableBuilder.SetRowCount(table.values.Length);
            builder.AddColumn(columnName, Projection.Create(table.Name));
            builder.AddColumn(columnValue, Projection.Create(table.Value));
            builder.AddColumn(columnTimestamp, Projection.Create(table.Timestamp));
            builder.AddColumn(columnFilename, Projection.Create(table.FileName));
            builder.AddColumn(columnTime, Projection.Create(table.Time));
            builder.AddColumn(columnCpu, Projection.Create(table.Cpu));
            builder.AddColumn(columnPid, Projection.Create(table.Pid));
            builder.AddColumn(columnTid, Projection.Create(table.Tid));
            builder.AddColumn(columnHasEventHeader, Projection.Create(table.HasEventHeader));
            builder.AddColumn(columnEventHeaderFlags, Projection.Create(table.EventHeaderFlags));
            builder.AddColumn(columnId, Projection.Create(table.Id));
            builder.AddColumn(columnVersion, Projection.Create(table.Version));
            builder.AddColumn(columnOpcode, Projection.Create(table.Opcode));
            builder.AddColumn(columnLevel, Projection.Create(table.Level));
            builder.AddColumn(columnKeyword, Projection.Create(table.Keyword));
            builder.AddColumn(columnActivityId, Projection.Create(table.ActivityId));
            builder.AddColumn(columnRelatedId, Projection.Create(table.RelatedId));
            builder.AddColumn(columnTag, Projection.Create(table.Tag));
            builder.AddColumn(columnCount, Projection.Constant(1));
        }

        public string Name(int i) => events[i].Name;

        public string FileName(int i) => events[i].FileInfo.FileName;

        public ulong Timestamp(int i) => events[i].SessionRelativeTime;

        public DateTime Time(int i) => events[i].DateTime;

        public uint Cpu(int i) => events[i].Cpu;

        public uint Pid(int i) => events[i].Pid;

        public uint Tid(int i) => events[i].Tid;

        public bool HasEventHeader(int i) => events[i].HasEventHeader;

        public Guid? ActivityId(int i) => events[i].ActivityId;

        public Guid? RelatedId(int i) => events[i].RelatedId;

        public EventHeaderFlags? EventHeaderFlags(int i)
        {
            var e = events[i];
            return e.HasEventHeader ? e.EventHeader.Flags : new EventHeaderFlags?();
        }

        public ushort? Id(int i)
        {
            var e = events[i];
            return e.HasEventHeader ? e.EventHeader.Id : new ushort?();
        }

        public byte? Version(int i)
        {
            var e = events[i];
            return e.HasEventHeader ? e.EventHeader.Version : new byte?();
        }

        public ushort? Tag(int i)
        {
            var e = events[i];
            return e.HasEventHeader ? e.EventHeader.Tag : new ushort?();
        }

        public EventOpcode? Opcode(int i)
        {
            var e = events[i];
            return e.HasEventHeader ? e.EventHeader.Opcode : new EventOpcode?();
        }

        public EventLevel? Level(int i)
        {
            var e = events[i];
            return e.HasEventHeader ? e.EventHeader.Level : new EventLevel?();
        }

        public ulong? Keyword(int i)
        {
            var e = events[i];
            return e.HasEventHeader ? e.Keyword : new ulong?();
        }

        public string Value(int i)
        {
            var value = this.values[i];
            if (value == null)
            {
                var e = this.events[i];                
                lock (this.sb)
                {
                    e.AppendValueAsJson(this.enumerator, this.sb);
                    value = sb.ToString();
                    sb.Clear();
                    this.values[i] = value;
                }

            }

            return value;
        }
    }
}
