namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK.Processing;
    using System;
    using System.Collections.ObjectModel;
    using System.Text;

    [Table]
    public sealed class PerfFilesTable
    {
        private readonly ReadOnlyCollection<FileInfo> fileInfos;

        public static readonly TableDescriptor TableDescriptor = new TableDescriptor(
            Guid.Parse("729af426-f1cf-476b-95b0-94c5b80ea2be"),
            "perf.data files",
            "Information about loaded perf.data files");

        private static readonly ColumnConfiguration columnFileName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("954f7d38-ae52-41b3-a30b-aff065428c6d"), "FileName"),
            new UIHints
            {
                IsVisible = true,
                Width = 200,
            });

        private static readonly ColumnConfiguration columnEventCount = new ColumnConfiguration(
            new ColumnMetadata(new Guid("1b312eed-9e86-4dce-9c9b-f6f29c60c8db"), "EventCount"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnHostName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("9acb7d22-e0bc-4884-8cd6-2864debde647"), "HostName"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnOSRelease = new ColumnConfiguration(
            new ColumnMetadata(new Guid("a27c6c6c-79e3-4670-952c-ec1fc3303007"), "OSRelease"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnArch = new ColumnConfiguration(
            new ColumnMetadata(new Guid("fb1bd12d-3c0e-4502-b6b2-b7ce479df58a"), "Arch"),
            new UIHints
            {
                IsVisible = true,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnCpusAvailable = new ColumnConfiguration(
            new ColumnMetadata(new Guid("2d1e1c95-a5ac-4b7f-806d-5a51f7d13b56"), "CpusAvailable"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnCpusOnline = new ColumnConfiguration(
            new ColumnMetadata(new Guid("e9b4f0e6-603e-4692-bd46-fabb2fe4f8a5"), "CpusOnline"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnBigEndian = new ColumnConfiguration(
            new ColumnMetadata(new Guid("a415adc0-fa0e-47e4-8cfb-aacf4a857841"), "BigEndian"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnClockId = new ColumnConfiguration(
            new ColumnMetadata(new Guid("48519a70-6e31-4eff-af53-4f48ba3d0cf0"), "ClockId"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnClockStart = new ColumnConfiguration(
            new ColumnMetadata(new Guid("07555c0d-e8c4-4a4f-8c88-5a8824bc0b9b"), "ClockStart"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnFirstEventTime = new ColumnConfiguration(
            new ColumnMetadata(new Guid("36f5411c-53f2-4dbf-86ef-900fa26cd90f"), "FirstEventTime"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnLastEventTime = new ColumnConfiguration(
            new ColumnMetadata(new Guid("16b7db78-8e1a-4b9b-9445-8c96c0bd2e72"), "LastEventTime"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        private static readonly ColumnConfiguration columnElapsedSeconds = new ColumnConfiguration(
            new ColumnMetadata(new Guid("744379d6-7a0d-4e6f-b9a7-0eecd55c3a33"), "ElapsedSeconds"),
            new UIHints
            {
                IsVisible = false,
                Width = 20,
            });

        internal PerfFilesTable(ReadOnlyCollection<FileInfo> fileInfos)
        {
            this.fileInfos = fileInfos;
        }

        internal void Build(ITableBuilder tableBuilder)
        {
            var builder = tableBuilder.SetRowCount(this.fileInfos.Count);
            builder.AddColumn(columnFileName, Projection.Create(this.FileName));
            builder.AddColumn(columnEventCount, Projection.Create(this.EventCount));
            builder.AddColumn(columnHostName, Projection.Create(this.HostName));
            builder.AddColumn(columnOSRelease, Projection.Create(this.OSRelease));
            builder.AddColumn(columnArch, Projection.Create(this.Arch));
            builder.AddColumn(columnCpusAvailable, Projection.Create(this.CpusAvailable));
            builder.AddColumn(columnCpusOnline, Projection.Create(this.CpusOnline));
            builder.AddColumn(columnBigEndian, Projection.Create(this.BigEndian));
            builder.AddColumn(columnClockId, Projection.Create(this.ClockId));
            builder.AddColumn(columnClockStart, Projection.Create(this.ClockStart));
            builder.AddColumn(columnFirstEventTime, Projection.Create(this.FirstEventTime));
            builder.AddColumn(columnLastEventTime, Projection.Create(this.LastEventTime));
            builder.AddColumn(columnElapsedSeconds, Projection.Create(this.ElapsedSeconds));
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
