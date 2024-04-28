// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.Performance.SDK;
    using Microsoft.Performance.SDK.Processing;
    using System;
    using System.Collections.ObjectModel;

    [Table]
    public sealed class PerfFileMetadataTable
    {
        private readonly ReadOnlyCollection<PerfFileInfo> fileInfos;

        public static readonly TableDescriptor TableDescriptor = new TableDescriptor(
            Guid.Parse("c5d716a9-8501-48ca-bd7f-12e9100862db"),
            "File Information",
            "Information about loaded perf.data files",
            "Linux perf.data files"
            //, isMetadataTable: true
            );

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
            builder.AddColumn(Arch_Column, Projection.Create(table.Arch));
            builder.AddColumn(BigEndian_Column, Projection.Create(table.BigEndian));
            builder.AddColumn(ClockId_Column, Projection.Create(table.ClockId));
            builder.AddColumn(ClockStart_ClockStart, Projection.Create(table.ClockStart));
            builder.AddColumn(CpusAvailable_CpusAvailable, Projection.Create(table.CpusAvailable));
            builder.AddColumn(CpusOnline_CpusOnline, Projection.Create(table.CpusOnline));
            builder.AddColumn(Elapsed_Column, Projection.Create(table.Elapsed));
            builder.AddColumn(EventCount_EventCount, Projection.Create(table.EventCount));
            builder.AddColumn(FileName_FileName, Projection.Create(table.FileName));
            builder.AddColumn(FirstEventTime_FirstEventTime, Projection.Create(table.FirstEventTime));
            builder.AddColumn(HostName_HostName, Projection.Create(table.HostName));
            builder.AddColumn(LastEventTime_LastEventTime, Projection.Create(table.LastEventTime));
            builder.AddColumn(OSRelease_Column, Projection.Create(table.OSRelease));

            var basicConfig = new TableConfiguration("Basic")
            {
                Columns = new[]
                {
                    FileName_FileName,

                    TableConfiguration.PivotColumn,

                    HostName_HostName,
                    OSRelease_Column,
                    Arch_Column,
                    CpusOnline_CpusOnline,
                    CpusAvailable_CpusAvailable,// Hidden by default
                    ClockId_Column,             // Hidden by default
                    ClockStart_ClockStart,
                    BigEndian_Column,           // Hidden by default
                    Elapsed_Column,             // Hidden by default

                    EventCount_EventCount,
                    TableConfiguration.GraphColumn,
                    FirstEventTime_FirstEventTime,
                    LastEventTime_LastEventTime,
                },
            };

            basicConfig.AddColumnRole(ColumnRole.StartTime, FirstEventTime_FirstEventTime);
            basicConfig.AddColumnRole(ColumnRole.EndTime, LastEventTime_LastEventTime);
            basicConfig.AddColumnRole(ColumnRole.Duration, Elapsed_Column);

            //tableBuilder.AddTableConfiguration(basicConfig);
            tableBuilder.SetDefaultTableConfiguration(basicConfig);
        }

        public string Arch(int i) => this.fileInfos[i].Arch;

        private static readonly ColumnConfiguration Arch_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("7fd5fc88-070a-475e-be35-cd2d233a8691"), "Arch",
                "Machine of the traced system, usually corresponding to 'uname -m'."),
            new UIHints
            {
                IsVisible = true,
                Width = 40,
            });

        public bool BigEndian(int i) => this.fileInfos[i].ByteReader.FromBigEndian;

        private static readonly ColumnConfiguration BigEndian_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("722c85e7-b191-427a-9589-9abc2284a40b"), "Big-Endian",
                "True if the trace file used big-endian byte order, false if little-endian byte order."),
            new UIHints
            {
                IsVisible = false,
                Width = 60,
            });

        public uint ClockId(int i) => this.fileInfos[i].ClockId;

        private static readonly ColumnConfiguration ClockId_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("6f325233-7882-4d24-beb4-b428074400c0"), "Clock Id",
                "ID of the clock that was used for the trace, e.g. CLOCK_REALTIME (0), CLOCK_MONOTONIC (1), or CLOCK_MONOTONIC_RAW (4)."),
            new UIHints
            {
                IsVisible = false,
                Width = 60,
            });

        public DateTime ClockStart(int i) => this.fileInfos[i].ClockOffset.DateTime ?? DateTime.UnixEpoch;

        private static readonly ColumnConfiguration ClockStart_ClockStart = new ColumnConfiguration(
            new ColumnMetadata(new Guid("efddd717-0ff1-44da-975c-7a580608e559"), "Clock Start",
                "The time corresponding to 0 for the trace's clock. This is frequently the boot time of the system."),
            new UIHints
            {
                IsVisible = true,
                Width = 100,
            });

        public uint CpusAvailable(int i) => this.fileInfos[i].CpusAvailable;

        private static readonly ColumnConfiguration CpusAvailable_CpusAvailable = new ColumnConfiguration(
            new ColumnMetadata(new Guid("cca3cf0f-ade2-435f-ab47-ab603d2c96bf"), "Cpus Available"),
            new UIHints
            {
                IsVisible = false,
                Width = 40,
            });

        public uint CpusOnline(int i) => this.fileInfos[i].CpusOnline;

        private static readonly ColumnConfiguration CpusOnline_CpusOnline = new ColumnConfiguration(
            new ColumnMetadata(new Guid("70b955ca-3895-4c72-865d-bbbf4eaba1b2"), "Cpus Online"),
            new UIHints
            {
                IsVisible = true,
                Width = 60,
            });

        public Timestamp Elapsed(int i)
        {
            var info = this.fileInfos[i];
            return new Timestamp((long)(
                info.FirstEventTime <= info.LastEventTime
                ? info.LastEventTime - info.FirstEventTime
                : 0));
        }

        private static readonly ColumnConfiguration Elapsed_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("0f022cbe-4349-46f2-8d82-9a20afe20b0a"), "Elapsed",
                "Time between the first and last events in the trace."),
            new UIHints
            {
                IsVisible = false,
                Width = 60,
            });

        public uint EventCount(int i) => this.fileInfos[i].EventCount;

        private static readonly ColumnConfiguration EventCount_EventCount = new ColumnConfiguration(
            new ColumnMetadata(new Guid("08d197d6-7380-4519-9266-66e2b426eb11"), "# Events"),
            new UIHints
            {
                IsVisible = true,
                Width = 60,
            });

        public string FileName(int i) => this.fileInfos[i].FileName;

        private static readonly ColumnConfiguration FileName_FileName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("b2ff6502-b688-4c1a-b74e-86b26990bd33"), "File Name"),
            new UIHints
            {
                IsVisible = true,
                Width = 300,
            });

        public Timestamp FirstEventTime(int i)
        {
            var info = this.fileInfos[i];
            var timestamp = info.SessionTimestampOffset + (long)info.FirstEventTime;
            return new Timestamp(timestamp < 0 ? 0 : timestamp);
        }

        private static readonly ColumnConfiguration FirstEventTime_FirstEventTime = new ColumnConfiguration(
            new ColumnMetadata(new Guid("5f840aa0-cf50-4678-8bc0-93a5797548b6"), "First Event Time",
                "Timestamp of the last event in the trace, or 0 if no events were collected."),
            new UIHints
            {
                IsVisible = true,
                Width = 60,
            });

        public string HostName(int i) => this.fileInfos[i].HostName;

        private static readonly ColumnConfiguration HostName_HostName = new ColumnConfiguration(
            new ColumnMetadata(new Guid("a145285d-a0e7-4ef8-a010-888faa775ff0"), "Host Name",
                "Name of the traced system, usually corresponding to 'uname -n'."),
            new UIHints
            {
                IsVisible = true,
                Width = 80,
            });

        public Timestamp LastEventTime(int i)
        {
            var info = this.fileInfos[i];
            var timestamp = info.SessionTimestampOffset + (long)info.LastEventTime;
            return new Timestamp(timestamp < 0 ? 0 : timestamp);
        }

        private static readonly ColumnConfiguration LastEventTime_LastEventTime = new ColumnConfiguration(
            new ColumnMetadata(new Guid("cc7c61fa-457c-4246-b33b-ad4b221b2f92"), "Last Event Time",
                "Timestamp of the last event in the trace, or 0 if no events were collected."),
            new UIHints
            {
                IsVisible = true,
                Width = 60,
            });

        public string OSRelease(int i) => this.fileInfos[i].OSRelease;

        private static readonly ColumnConfiguration OSRelease_Column = new ColumnConfiguration(
            new ColumnMetadata(new Guid("569edc59-db51-46d4-992a-b0ac135324a4"), "OS Release",
                "Information about the kernel of the traced system, usually corresponding to 'uname -r'."),
            new UIHints
            {
                IsVisible = true,
                Width = 100,
            });
    }
}
