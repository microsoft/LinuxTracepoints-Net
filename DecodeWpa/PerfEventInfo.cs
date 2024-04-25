namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK.Extensibility;
    using System;
    using System.Text;
    using Debug = System.Diagnostics.Debug;

    public sealed class PerfEventInfo : IKeyedDataType<UInt32>
    {
        /// <summary>
        /// For sample events.
        /// Requires: info.Format != null.
        /// </summary>
        public PerfEventInfo(UInt32 key, PerfFileInfo fileInfo, PerfSampleEventInfo info, string name)
        {
            this.key = key;
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
        /// For eventheader sample events.
        /// Requires: info.Format != null.
        /// </summary>
        public PerfEventInfo(UInt32 key, PerfFileInfo fileInfo, PerfSampleEventInfo info, string name, in EventHeaderEventInfo ehEventInfo)
            : this(key, fileInfo, info, name)
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

        public PerfFileInfo FileInfo { get; }

        public string Name { get; }

        public PerfEventFormat Format { get; }

        public byte[] RawData { get; }

        public UInt32 Cpu { get; }

        public UInt32 Pid { get; }

        public UInt32 Tid { get; }

        public bool HasEventHeader { get; }

        private readonly byte activityIdLength;
        private readonly ushort activityIdStart;
        private readonly UInt32 key;

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

        public uint GetKey()
        {
            return this.key;
        }

        public bool AppendValueAsJson(
            EventHeaderEnumerator enumerator,
            StringBuilder sb,
            bool addCommaBeforeNextItem = false,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            bool needComma = addCommaBeforeNextItem;
            if (this.Format.DecodingStyle != PerfEventDecodingStyle.EventHeader ||
                !enumerator.StartEvent(this.Format.Name, this.RawData.AsMemory().Slice(this.Format.CommonFieldsSize)))
            {
                var comma = convertOptions.HasFlag(PerfConvertOptions.Space) ? ", " : "";
                var colon = convertOptions.HasFlag(PerfConvertOptions.Space) ? ": " : ":";
                var rawData = this.RawData.AsSpan();
                for (int fieldIndex = this.Format.CommonFieldCount; fieldIndex < this.Format.Fields.Count; fieldIndex += 1)
                {
                    if (needComma)
                    {
                        sb.Append(comma);
                    }

                    needComma = true;

                    var field = this.Format.Fields[fieldIndex];
                    PerfConvert.StringAppendJson(sb, field.Name);
                    sb.Append(colon);

                    var fieldVal = field.GetFieldValue(rawData, this.FileInfo.ByteReader);
                    if (fieldVal.IsArrayOrElement)
                    {
                        fieldVal.AppendJsonSimpleArrayTo(sb, convertOptions);
                    }
                    else
                    {
                        fieldVal.AppendJsonScalarTo(sb, convertOptions);
                    }
                }
            }
            else
            {
                needComma = enumerator.AppendJsonItemToAndMoveNextSibling(sb, needComma, convertOptions);
            }

            return needComma;
        }
    }
}
