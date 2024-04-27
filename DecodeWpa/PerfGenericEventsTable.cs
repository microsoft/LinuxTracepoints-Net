// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK;
    using Microsoft.Performance.SDK.Extensibility;
    using Microsoft.Performance.SDK.Processing;
    using System;
    using System.Diagnostics.Tracing;
    using System.Text;

    [Table]
    [RequiresSourceCooker(PerfSourceParser.SourceParserId, PerfSourceCooker.DataCookerId)]
    public sealed class PerfGenericEventsTable
    {
        private readonly ProcessedEventData<ValueTuple<PerfEventData, PerfFileInfo>> events;
        private readonly string[] values;
        private readonly long sessionTimestampOffset;
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

        private static readonly ColumnConfiguration columnGroupName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("01ccf0a4-d1d7-4122-8adb-8697743d602a"), "GroupName", "System or Provider name"),
            new UIHints
            {
                IsVisible = true,
                Width = 200,
            });

        private static readonly ColumnConfiguration columnEventName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("79cb48eb-b1f5-4257-bf4f-203b8dce3b0f"), "EventName", "Tracepoint or Event name"),
            new UIHints
            {
                IsVisible = true,
                Width = 200,
            });

        private static readonly ColumnConfiguration columnTracepointId = new ColumnConfiguration(
            new ColumnMetadata(new Guid("3e8de5c0-0ee1-4a71-8dfe-2aec347f2074"), "TracepointId", "SystemName:TracepointName"),
            new UIHints
            {
                IsVisible = false,
                Width = 200,
            });

        private static readonly ColumnConfiguration columnSystemName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("ba4ec1dd-6b64-4660-a67f-b59f86d28203"), "SystemName"),
            new UIHints
            {
                IsVisible = false,
                Width = 200,
            });

        private static readonly ColumnConfiguration columnTracepointName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("95296c0c-be46-47d6-bd63-90217b8f4121"), "TracepointName"),
            new UIHints
            {
                IsVisible = false,
                Width = 200,
            });

        private static readonly ColumnConfiguration columnProviderName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("c485fead-48e0-4579-996e-03bd2b1604ec"), "ProviderName", "Provider name (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 200,
            });

        private static readonly ColumnConfiguration columnProviderOptions = new ColumnConfiguration(
            new ColumnMetadata(new Guid("519ee83a-6fbc-461e-9ac8-f27cf309a24b"), "ProviderOptions", "Provider options (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 200,
            });

        private static readonly ColumnConfiguration columnEventHeaderName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("5a201e42-bca7-4e6a-bf53-59f3e5994869"), "EventHeaderName", "Event name (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
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
            new ColumnMetadata(new Guid("9987bcc7-5c68-4105-a6b8-32afe6b1cfe2"), "EventHeaderFlags", "Provider characteristics (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnId = new ColumnConfiguration(
            new ColumnMetadata(new Guid("fbc36c32-a6fc-4ace-b576-bca3ab1fdf21"), "Id", "Event's stable Id (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnVersion = new ColumnConfiguration(
            new ColumnMetadata(new Guid("cea693d5-e0ff-4c55-b7d4-4f98f1114a68"), "Version", "Event's version (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnTag = new ColumnConfiguration(
            new ColumnMetadata(new Guid("e638de35-56d8-4234-b204-bb6927994eeb"), "Tag", "Event's tag (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
                CellFormat = ColumnFormats.HexFormat,
            });

        private static readonly ColumnConfiguration columnOpcode = new ColumnConfiguration(
            new ColumnMetadata(new Guid("1cb5cfce-3ace-4d17-84c9-b8af01d3a47a"), "Opcode", "Event's opcode (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnLevel = new ColumnConfiguration(
            new ColumnMetadata(new Guid("9a7f5539-6c9f-4d23-bf0a-aa663e30b48f"), "Level", "Event's severity (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnKeyword = new ColumnConfiguration(
            new ColumnMetadata(new Guid("7c9071dd-c49d-4db1-852d-76ec31fa1938"), "Keyword", "Event's category bits (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
                CellFormat = ColumnFormats.HexFormat,
            });

        private static readonly ColumnConfiguration columnActivityId = new ColumnConfiguration(
            new ColumnMetadata(new Guid("ccfff171-c773-43de-80a9-f6e3e0b81090"), "ActivityId", "Event's activity id (EventHeader-only)"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnRelatedId = new ColumnConfiguration(
            new ColumnMetadata(new Guid("27c061c5-ff89-4604-85f6-6c27094a62fa"), "RelatedId", "Event's related (parent) activity id (EventHeader-only)"),
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

        private PerfGenericEventsTable(ProcessedEventData<ValueTuple<PerfEventData, PerfFileInfo>> events, long sessionTimestampOffset)
        {
            this.events = events;
            this.values = new string[this.events.Count];
            this.sessionTimestampOffset = sessionTimestampOffset;
        }

        public static void BuildTable(
            ITableBuilder tableBuilder,
            IDataExtensionRetrieval requiredData)
        {
            var events = requiredData.QueryOutput<ProcessedEventData<ValueTuple<PerfEventData, PerfFileInfo>>>(PerfSourceCooker.EventsOutputPath);
            var sessionTimestampOffset = requiredData.QueryOutput<long>(PerfSourceCooker.SessionTimestampOffsetOutputPath);
            var table = new PerfGenericEventsTable(events, sessionTimestampOffset);
            var builder = tableBuilder.SetRowCount(table.values.Length);
            builder.AddColumn(columnGroupName, Projection.Create(table.GroupName));
            builder.AddColumn(columnEventName, Projection.Create(table.EventName));
            builder.AddColumn(columnTracepointId, Projection.Create(table.TracepointId));
            builder.AddColumn(columnSystemName, Projection.Create(table.SystemName));
            builder.AddColumn(columnTracepointName, Projection.Create(table.TracepointName));
            builder.AddColumn(columnProviderName, Projection.Create(table.ProviderName));
            builder.AddColumn(columnProviderOptions, Projection.Create(table.ProviderOptions));
            builder.AddColumn(columnEventHeaderName, Projection.Create(table.EventHeaderName));
            builder.AddColumn(columnValue, Projection.Create(table.Value));
            builder.AddColumn(columnTimestamp, Projection.Create(table.Timestamp));
            builder.AddColumn(columnFilename, Projection.Create(table.FileName));
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
            //tableBuilder.DefaultConfiguration.AddColumnRole(ColumnRole.StartTime, columnTimestamp.Metadata.Guid);
        }

        public string GroupName(int i) => events[i].Item1.GetGroupName();

        public string EventName(int i) => events[i].Item1.GetEventName();

        public string TracepointId(int i) => events[i].Item1.TracepointId.ToString();

        public string SystemName(int i) => events[i].Item1.SystemName.ToString();

        public string TracepointName(int i) => events[i].Item1.TracepointName.ToString();

        public string ProviderName(int i) => events[i].Item1.ProviderName.ToString();

        public string ProviderOptions(int i) => events[i].Item1.ProviderOptions.ToString();

        public string EventHeaderName(int i) => events[i].Item1.GetEventHeaderName();

        public string FileName(int i) => events[i].Item2.FileName;

        public Timestamp Timestamp(int i) => events[i].Item1.GetTimestamp(this.sessionTimestampOffset);

        public uint? Cpu(int i) => events[i].Item1.Cpu;

        public uint? Pid(int i) => events[i].Item1.Pid;

        public uint? Tid(int i) => events[i].Item1.Tid;

        public bool HasEventHeader(int i) => events[i].Item1.HasEventHeader;

        public Guid? ActivityId(int i) => events[i].Item1.ActivityId;

        public Guid? RelatedId(int i) => events[i].Item1.RelatedId;

        public EventHeaderFlags? EventHeaderFlags(int i) => events[i].Item1.EventHeaderFlags;

        public ushort? Id(int i) => events[i].Item1.Id;

        public byte? Version(int i) => events[i].Item1.Version;

        public ushort? Tag(int i) => events[i].Item1.Tag;

        public EventOpcode? Opcode(int i) => events[i].Item1.Opcode;

        public EventLevel? Level(int i) => events[i].Item1.Level;

        public ulong? Keyword(int i) => events[i].Item1.Keyword;

        public string Value(int i)
        {
            var value = this.values[i];
            if (value == null)
            {
                var e = this.events[i].Item1;                
                lock (this.sb)
                {
                    e.AppendValueAsJson(this.sb, this.enumerator);
                    value = sb.ToString();
                    sb.Clear();
                    this.values[i] = value;
                }

            }

            return value;
        }
    }
}
