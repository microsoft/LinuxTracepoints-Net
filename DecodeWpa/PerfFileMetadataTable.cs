namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.Performance.SDK.Processing;
    using System;
    using System.Collections.ObjectModel;

    [Table]
    public sealed class PerfFileMetadataTable
    {
        private readonly ReadOnlyCollection<PerfFileInfo> fileInfos;

        public static readonly TableDescriptor TableDescriptor = new TableDescriptor(
            Guid.Parse("c5d716a9-8501-48ca-bd7f-12e9100862db"),
            "perf.data loaded files",
            "Loaded perf.data files",
            defaultLayout: TableLayoutStyle.Table
            //, isMetadataTable: true
            );

        private static readonly ColumnConfiguration columnFileName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("b2ff6502-b688-4c1a-b74e-86b26990bd33"), "FileName"),
            new UIHints
            {
                IsVisible = true,
                Width = 200,
            });

        private static readonly ColumnConfiguration columnEventCount = new ColumnConfiguration(
            new ColumnMetadata(new Guid("08d197d6-7380-4519-9266-66e2b426eb11"), "EventCount"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnHostName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("a145285d-a0e7-4ef8-a010-888faa775ff0"), "HostName"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnOSRelease = new ColumnConfiguration(
            new ColumnMetadata(new Guid("569edc59-db51-46d4-992a-b0ac135324a4"), "OSRelease"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnArch = new ColumnConfiguration(
            new ColumnMetadata(new Guid("7fd5fc88-070a-475e-be35-cd2d233a8691"), "Arch"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnCpusAvailable = new ColumnConfiguration(
            new ColumnMetadata(new Guid("cca3cf0f-ade2-435f-ab47-ab603d2c96bf"), "CpusAvailable"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnCpusOnline = new ColumnConfiguration(
            new ColumnMetadata(new Guid("70b955ca-3895-4c72-865d-bbbf4eaba1b2"), "CpusOnline"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnBigEndian = new ColumnConfiguration(
            new ColumnMetadata(new Guid("722c85e7-b191-427a-9589-9abc2284a40b"), "BigEndian"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnClockId = new ColumnConfiguration(
            new ColumnMetadata(new Guid("6f325233-7882-4d24-beb4-b428074400c0"), "ClockId"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnClockStart = new ColumnConfiguration(
            new ColumnMetadata(new Guid("efddd717-0ff1-44da-975c-7a580608e559"), "ClockStart"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnFirstEventTime = new ColumnConfiguration(
            new ColumnMetadata(new Guid("5f840aa0-cf50-4678-8bc0-93a5797548b6"), "FirstEventTime"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnLastEventTime = new ColumnConfiguration(
            new ColumnMetadata(new Guid("cc7c61fa-457c-4246-b33b-ad4b221b2f92"), "LastEventTime"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnElapsedSeconds = new ColumnConfiguration(
            new ColumnMetadata(new Guid("0f022cbe-4349-46f2-8d82-9a20afe20b0a"), "ElapsedSeconds"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private PerfFileMetadataTable(ReadOnlyCollection<PerfFileInfo> fileInfos)
        {
            this.fileInfos = fileInfos;
        }

        public static void BuildTable(
            ITableBuilder tableBuilder,
            ReadOnlyCollection<PerfFileInfo> fileInfos)
        {
            var table = new PerfFileMetadataTable(fileInfos);
            var builder = tableBuilder.SetRowCount(fileInfos.Count);
            builder.AddColumn(columnFileName, Projection.Create(table.FileName));
            builder.AddColumn(columnEventCount, Projection.Create(table.EventCount));
            builder.AddColumn(columnHostName, Projection.Create(table.HostName));
            builder.AddColumn(columnOSRelease, Projection.Create(table.OSRelease));
            builder.AddColumn(columnArch, Projection.Create(table.Arch));
            builder.AddColumn(columnCpusAvailable, Projection.Create(table.CpusAvailable));
            builder.AddColumn(columnCpusOnline, Projection.Create(table.CpusOnline));
            builder.AddColumn(columnBigEndian, Projection.Create(table.BigEndian));
            builder.AddColumn(columnClockId, Projection.Create(table.ClockId));
            builder.AddColumn(columnClockStart, Projection.Create(table.ClockStart));
            builder.AddColumn(columnFirstEventTime, Projection.Create(table.FirstEventTime));
            builder.AddColumn(columnLastEventTime, Projection.Create(table.LastEventTime));
            builder.AddColumn(columnElapsedSeconds, Projection.Create(table.ElapsedSeconds));
        }

        public string FileName(int i) => this.fileInfos[i].FileName;

        public uint EventCount(int i) => this.fileInfos[i].EventCount;

        public string HostName(int i) => this.fileInfos[i].HostName;

        public string OSRelease(int i) => this.fileInfos[i].OSRelease;

        public string Arch(int i) => this.fileInfos[i].Arch;

        public uint CpusAvailable(int i) => this.fileInfos[i].CpusAvailable;

        public uint CpusOnline(int i) => this.fileInfos[i].CpusOnline;

        public bool BigEndian(int i) => this.fileInfos[i].ByteReader.FromBigEndian;

        public uint ClockId(int i) => this.fileInfos[i].ClockId;

        public DateTime ClockStart(int i) => this.fileInfos[i].ClockOffset.DateTime ?? DateTime.UnixEpoch;

        public DateTime FirstEventTime(int i) => this.fileInfos[i].FirstEventTimeSpec.DateTime ?? DateTime.UnixEpoch;

        public DateTime LastEventTime(int i) => this.fileInfos[i].LastEventTimeSpec.DateTime ?? DateTime.UnixEpoch;

        public double ElapsedSeconds(int i)
        {
            var info = this.fileInfos[i];
            return info.FirstEventTime <= info.LastEventTime
                ? (info.LastEventTime - info.FirstEventTime) / 1000000000.0
                : 0.0;
        }
    }
}
