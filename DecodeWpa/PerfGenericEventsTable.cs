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
    [RequiresSourceCooker(PerfSourceParser.SourceParserId, PerfGenericSourceCooker.DataCookerId)]
    public sealed class PerfGenericEventsTable
    {
        private readonly long sessionTimestampOffset;
        private readonly ProcessedEventData<ValueTuple<PerfEventData, PerfFileInfo>> events;
        private readonly string[] fieldsCache; // guarded by lock(sb).
        private readonly StringBuilder sb = new StringBuilder(); // guarded by lock(sb).
        private readonly EventHeaderEnumerator enumerator = new EventHeaderEnumerator(); // guarded by lock(sb).

        public static readonly TableDescriptor TableDescriptor = new TableDescriptor(
            Guid.Parse("84efc851-2466-4c79-b856-3d76d59c4935"),
            "Generic Events",
            "Events loaded from a perf.data file",
            "Linux perf.data");

        private PerfGenericEventsTable(IDataExtensionRetrieval requiredData)
        {
            this.sessionTimestampOffset = requiredData.QueryOutput<long>(PerfGenericSourceCooker.SessionTimestampOffsetOutputPath);
            this.events = requiredData.QueryOutput<ProcessedEventData<ValueTuple<PerfEventData, PerfFileInfo>>>(PerfGenericSourceCooker.EventsOutputPath);
            this.fieldsCache = new string[this.events.Count];
        }

        public static void BuildTable(
            ITableBuilder tableBuilder,
            IDataExtensionRetrieval requiredData)
        {
            var table = new PerfGenericEventsTable(requiredData);

            var builder = tableBuilder.SetRowCount(table.fieldsCache.Length);
            builder.AddColumn(ActivityId_Column, Projection.Create(table.ActivityId));
            builder.AddColumn(AttrType_Column, Projection.Create(table.AttrType));
            builder.AddColumn(CommonFields_Column, Projection.Create(table.CommonFields));
            builder.AddColumn(Count_Column, Projection.Constant(1));
            builder.AddColumn(Cpu_Column, Projection.Create(table.Cpu));
            builder.AddColumn(EventHeaderFlags_Column, Projection.Create(table.EventHeaderFlags));
            builder.AddColumn(EventHeaderName_Column, Projection.Create(table.EventHeaderName));
            builder.AddColumn(EventName_Column, Projection.Create(table.EventName));
            builder.AddColumn(Fields_Column, Projection.Create(table.Fields));
            builder.AddColumn(FileName_Column, Projection.Create(table.FileName));
            builder.AddColumn(GroupName_Column, Projection.Create(table.GroupName));
            builder.AddColumn(HasEventHeader_Column, Projection.Create(table.HasEventHeader));
            builder.AddColumn(Id_Column, Projection.Create(table.Id));
            builder.AddColumn(Keyword_Column, Projection.Create(table.Keyword));
            builder.AddColumn(Level_Column, Projection.Create(table.Level));
            builder.AddColumn(Opcode_Column, Projection.Create(table.Opcode));
            builder.AddColumn(Pid_Column, Projection.Create(table.Pid));
            builder.AddColumn(ProviderName_Column, Projection.Create(table.ProviderName));
            builder.AddColumn(ProviderOptions_Column, Projection.Create(table.ProviderOptions));
            builder.AddColumn(RecordType_Column, Projection.Create(table.RecordType));
            builder.AddColumn(RelatedId_Column, Projection.Create(table.RelatedId));
            builder.AddColumn(Tag_Column, Projection.Create(table.Tag));
            builder.AddColumn(Tid_Column, Projection.Create(table.Tid));
            builder.AddColumn(Timestamp_Column, Projection.Create(table.Timestamp));
            builder.AddColumn(TopLevelFieldCount_Column, Projection.Create(table.TopLevelFieldCount));
            builder.AddColumn(TracepointId_Column, Projection.Create(table.TracepointId));
            builder.AddColumn(TracepointName_Column, Projection.Create(table.TracepointName));
            builder.AddColumn(TracepointSystem_Column, Projection.Create(table.TracepointSystem));
            builder.AddColumn(Version_Column, Projection.Create(table.Version));

            var basicConfig = new TableConfiguration("By Group+Event")
            {
                Columns = new[]
                {
                    AttrType_Column,            // Hidden by default
                    RecordType_Column,          // Hidden by default

                    GroupName_Column,
                    EventName_Column,

                    TracepointId_Column,        // Hidden by default
                    TracepointSystem_Column,    // Hidden by default
                    TracepointName_Column,      // Hidden by default

                    ProviderName_Column,        // Hidden by default
                    EventHeaderName_Column,     // Hidden by default

                    TableConfiguration.PivotColumn,

                    FileName_Column,            // Hidden by default
                    Cpu_Column,
                    Pid_Column,                 // Hidden by default
                    Tid_Column,
                    Level_Column,               // Hidden by default
                    Keyword_Column,             // Hidden by default
                    Id_Column,                  // Hidden by default
                    Version_Column,             // Hidden by default
                    Opcode_Column,              // Hidden by default
                    ActivityId_Column,          // Hidden by default
                    RelatedId_Column,           // Hidden by default
                    Tag_Column,                 // Hidden by default
                    ProviderOptions_Column,     // Hidden by default

                    CommonFields_Column,        // Hidden by default
                    Fields_Column,

                    EventHeaderFlags_Column,    // Hidden by default
                    HasEventHeader_Column,      // Hidden by default
                    TopLevelFieldCount_Column,  // Hidden by default

                    Count_Column,
                    TableConfiguration.GraphColumn,
                    Timestamp_Column,
                },
            };

            //tableBuilder.AddTableConfiguration(basicConfig);
            tableBuilder.SetDefaultTableConfiguration(basicConfig);
        }

        public Guid? ActivityId(int i) => events[i].Item1.ActivityId;

        private static readonly ColumnConfiguration ActivityId_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("ccfff171-c773-43de-80a9-f6e3e0b81090"), "Activity Id", "Event's activity id (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 80,
            });

        public PerfEventAttrType AttrType(int i) => events[i].Item1.EventDesc.Attr.Type;

        private static readonly ColumnConfiguration AttrType_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("cdb2c54f-fee5-4b3b-9c89-7e910ab176af"), "Attr Type",
                "perf_event_attr.type"),
            new UIHints
            {
                IsVisible = false,
                Width = 60,
            });

        public string CommonFields(int i)
        {
            string value;
            var e = this.events[i].Item1;
            lock (this.sb)
            {
                e.AppendCommonFieldsAsJson(this.sb);
                value = sb.ToString();
                sb.Clear();
            }

            return value;
        }

        private static readonly ColumnConfiguration CommonFields_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("ab3cb8ba-5167-4117-82f1-98b34b6e1430"), "Common Fields"),
            new UIHints
            {
                IsVisible = false,
                Width = 120,
            });

        private static readonly ColumnConfiguration Count_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("7f412945-4226-4b33-b688-b353eb8e15c4"), "Count"),
            new UIHints
            {
                IsVisible = true,
                Width = 60,
                AggregationMode = AggregationMode.Sum,
            });

        public uint? Cpu(int i) => events[i].Item1.Cpu;

        private static readonly ColumnConfiguration Cpu_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("a2b02251-64bb-4bbc-9224-cff29423e199"), "Cpu"),
            new UIHints
            {
                IsVisible = true,
                Width = 40,
            });

        public EventHeaderFlags? EventHeaderFlags(int i) => events[i].Item1.EventHeaderFlags;

        private static readonly ColumnConfiguration EventHeaderFlags_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("9987bcc7-5c68-4105-a6b8-32afe6b1cfe2"), "EventHeader Flags", "Provider characteristics (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 60,
            });

        public string EventHeaderName(int i) => events[i].Item1.GetEventHeaderName();

        private static readonly ColumnConfiguration EventHeaderName_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("5a201e42-bca7-4e6a-bf53-59f3e5994869"), "EventHeader Name", "Event name (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 120,
            });

        public string EventName(int i) => events[i].Item1.GetEventName();

        private static readonly ColumnConfiguration EventName_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("79cb48eb-b1f5-4257-bf4f-203b8dce3b0f"), "Event Name", "Tracepoint or Event name"),
            new UIHints
            {
                IsVisible = true,
                Width = 120,
            });

        public string Fields(int i)
        {
            var value = this.fieldsCache[i];
            if (value == null)
            {
                var e = this.events[i].Item1;
                lock (this.sb)
                {
                    e.AppendFieldsAsJson(this.sb, this.enumerator);
                    value = sb.ToString();
                    sb.Clear();
                    this.fieldsCache[i] = value;
                }
            }

            return value;
        }

        private static readonly ColumnConfiguration Fields_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("5f5580b6-15e9-4a57-83ba-9160dc1eae3b"), "Fields"),
            new UIHints
            {
                IsVisible = true,
                Width = 400,
            });

        public string FileName(int i) => events[i].Item2.FileName;

        private static readonly ColumnConfiguration FileName_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("dd543500-56a8-4d19-a605-6a85ff616306"), "File Name"),
            new UIHints
            {
                IsVisible = false,
                Width = 200,
            });

        public string GroupName(int i) => events[i].Item1.GetGroupName();

        private static readonly ColumnConfiguration GroupName_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("01ccf0a4-d1d7-4122-8adb-8697743d602a"), "Group Name", "System or Provider name"),
            new UIHints
            {
                IsVisible = true,
                Width = 120,
                SortPriority = 1,
            });

        public bool HasEventHeader(int i) => events[i].Item1.HasEventHeader;

        private static readonly ColumnConfiguration HasEventHeader_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("0d1c453a-3805-4d99-a64a-29e6ad1602e8"), "Has EventHeader"),
            new UIHints
            {
                IsVisible = false,
                Width = 40,
            });

        public ushort? Id(int i) => events[i].Item1.Id;

        private static readonly ColumnConfiguration Id_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("fbc36c32-a6fc-4ace-b576-bca3ab1fdf21"), "Id", "Event's stable Id (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 40,
            });

        public ulong? Keyword(int i) => events[i].Item1.Keyword;

        private static readonly ColumnConfiguration Keyword_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("7c9071dd-c49d-4db1-852d-76ec31fa1938"), "Keyword", "Event's category bits (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 60,
                CellFormat = "X",
            });

        public EventLevel? Level(int i) => events[i].Item1.Level;

        private static readonly ColumnConfiguration Level_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("9a7f5539-6c9f-4d23-bf0a-aa663e30b48f"), "Level", "Event's severity (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 60,
            });

        public EventOpcode? Opcode(int i) => events[i].Item1.Opcode;

        private static readonly ColumnConfiguration Opcode_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("1cb5cfce-3ace-4d17-84c9-b8af01d3a47a"), "Opcode", "Event's opcode (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 40,
            });

        public uint? Pid(int i) => events[i].Item1.Pid;

        private static readonly ColumnConfiguration Pid_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("3598598d-1e4a-40ad-a463-32b7cfb1b721"), "Pid"),
            new UIHints
            {
                IsVisible = false,
                Width = 40,
            });

        public string ProviderName(int i) => events[i].Item1.ProviderName.ToString();

        private static readonly ColumnConfiguration ProviderName_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("c485fead-48e0-4579-996e-03bd2b1604ec"), "Provider Name", "Provider name (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 120,
            });

        public string ProviderOptions(int i) => events[i].Item1.ProviderOptions.ToString();

        private static readonly ColumnConfiguration ProviderOptions_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("519ee83a-6fbc-461e-9ac8-f27cf309a24b"), "Provider Options", "Provider options (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 80,
            });

        public PerfEventHeaderType RecordType(int i) => events[i].Item1.Header.Type;

        private static readonly ColumnConfiguration RecordType_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("3818190f-8c77-4a4e-bc6f-9a9ee1d1d062"), "Record Type",
                "perf_event_header.type"),
            new UIHints
            {
                IsVisible = false,
                Width = 60,
            });

        public Guid? RelatedId(int i) => events[i].Item1.RelatedId;

        private static readonly ColumnConfiguration RelatedId_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("27c061c5-ff89-4604-85f6-6c27094a62fa"), "Related Id", "Event's related (parent) activity id (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 80,
            });

        public ushort? Tag(int i) => events[i].Item1.Tag;

        private static readonly ColumnConfiguration Tag_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("e638de35-56d8-4234-b204-bb6927994eeb"), "Tag", "Event's tag (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 40,
                CellFormat = "X",
            });

        public uint? Tid(int i) => events[i].Item1.Tid;

        private static readonly ColumnConfiguration Tid_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("cd447f69-0964-4663-bd3e-8763ac5fb15a"), "Tid"),
            new UIHints
            {
                IsVisible = true,
                Width = 40,
            });

        public Timestamp Timestamp(int i) => events[i].Item1.GetTimestamp(this.sessionTimestampOffset);

        private static readonly ColumnConfiguration Timestamp_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("ad35e366-91e9-46e6-8d9d-ebb84ca4b3cf"), "Timestamp"),
            new UIHints
            {
                IsVisible = true,
                Width = 40,
            });

        public ushort TopLevelFieldCount(int i) => events[i].Item1.TopLevelFieldCount;

        private static readonly ColumnConfiguration TopLevelFieldCount_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("e2fe9ba5-7a0f-4cb9-9c3a-4fd84a212d9a"), "# Top-Level Fields"),
            new UIHints
            {
                IsVisible = false,
                Width = 40,
            });

        public string TracepointId(int i) => events[i].Item1.TracepointId.ToString();

        private static readonly ColumnConfiguration TracepointId_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("3e8de5c0-0ee1-4a71-8dfe-2aec347f2074"), "Tracepoint Id", "TracepointSystem:TracepointName"),
            new UIHints
            {
                IsVisible = false,
                Width = 200,
            });

        public string TracepointName(int i) => events[i].Item1.TracepointName.ToString();

        private static readonly ColumnConfiguration TracepointName_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("95296c0c-be46-47d6-bd63-90217b8f4121"), "Tracepoint Name"),
            new UIHints
            {
                IsVisible = false,
                Width = 120,
            });

        public string TracepointSystem(int i) => events[i].Item1.SystemName.ToString();

        private static readonly ColumnConfiguration TracepointSystem_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("ba4ec1dd-6b64-4660-a67f-b59f86d28203"), "Tracepoint System"),
            new UIHints
            {
                IsVisible = false,
                Width = 120,
            });

        public byte? Version(int i) => events[i].Item1.Version;

        private static readonly ColumnConfiguration Version_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("cea693d5-e0ff-4c55-b7d4-4f98f1114a68"), "Version", "Event's version (EventHeader-only)"),
            new UIHints
            {
                IsVisible = false,
                Width = 40,
            });
    }
}
