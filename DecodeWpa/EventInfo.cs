namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using System;
    using Debug = System.Diagnostics.Debug;

    internal sealed class EventInfo
    {
        /// <summary>
        /// For non-eventheader events.
        /// Requires: info.Format != null.
        /// </summary>
        public EventInfo(FileInfo fileInfo, PerfSampleEventInfo info, string name)
        {
            this.FileRelativeTime = info.Time;
            this.FileInfo = fileInfo;
            this.Name = name;
            this.Format = info.Format!;
            this.RawData = info.RawDataLength == 0 ? Array.Empty<byte>() : info.RawData.ToArray();

            this.Cpu = info.Cpu;
            this.Pid = info.Pid;
            this.Tid = info.Tid;
        }

        /// <summary>
        /// For eventheader events.
        /// Requires: info.Format != null.
        /// </summary>
        public EventInfo(FileInfo fileInfo, PerfSampleEventInfo info, string name, in EventHeaderEventInfo ehEventInfo)
            : this(fileInfo, info, name)
        {
            this.HasEventHeader = true;
            this.EventHeader = ehEventInfo.Header;
            this.Keyword = ehEventInfo.Keyword;

            Debug.Assert(
                ehEventInfo.ActivityIdLength == 0 ||
                ehEventInfo.ActivityIdLength == 16 ||
                ehEventInfo.ActivityIdLength == 32);
            Debug.Assert(ehEventInfo.ActivityIdStart >= ushort.MinValue);
            Debug.Assert(ehEventInfo.ActivityIdStart <= ushort.MaxValue - this.Format.CommonFieldsSize);
            this.activityIdLength = (byte)ehEventInfo.ActivityIdLength;
            if (this.activityIdLength != 0)
            {
                Debug.Assert(this.Format.CommonFieldsSize + ehEventInfo.ActivityIdStart + this.activityIdLength <= this.RawData.Length);
                this.activityIdStart = (ushort)(this.Format.CommonFieldsSize + ehEventInfo.ActivityIdStart);
            }
        }

        public ulong FileRelativeTime { get; }

        public FileInfo FileInfo { get; }

        public string Name { get; }

        public PerfEventFormat Format { get; }

        public byte[] RawData { get; }

        public UInt32 Cpu { get; }

        public UInt32 Pid { get; }

        public UInt32 Tid { get; }

        public bool HasEventHeader { get; }

        private readonly byte activityIdLength;
        private readonly ushort activityIdStart;

        public EventHeader EventHeader { get; }

        public ulong Keyword { get; }

        public Guid? ActivityId =>
            this.activityIdLength >= 16
            ? PerfConvert.ReadGuidBigEndian(this.RawData.AsSpan(this.activityIdStart))
            : new Guid?();

        public Guid? RelatedId =>
            this.activityIdLength >= 32
            ? PerfConvert.ReadGuidBigEndian(this.RawData.AsSpan(this.activityIdStart + 16))
            : new Guid?();

        public ulong SessionRelativeTime =>
            unchecked((ulong)this.FileInfo.SessionTimestampOffset + this.FileRelativeTime);

        public DateTime DateTime =>
            this.FileInfo.ClockOffset.AddNanoseconds(this.FileRelativeTime).DateTime ?? DateTime.MinValue;
    }
}
