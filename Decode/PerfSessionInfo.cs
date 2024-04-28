// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Text;

    /// <summary>
    /// Information about a perf event collection session.
    /// </summary>
    public class PerfSessionInfo
    {
        private const uint Billion = 1000000000;
        private static PerfSessionInfo? empty;

        private long clockOffsetSeconds;
        private uint clockOffsetNanoseconds;
        private readonly bool readOnly;

        private PerfSessionInfo()
        {
            this.readOnly = true;
        }

        /// <summary>
        /// Constructs a new PerfSessionInfo instance.
        /// Instances of this class are normally created by the PerfEventReader class.
        /// </summary>
        internal PerfSessionInfo(PerfByteReader byteReader)
        {
            this.ByteReader = byteReader;
        }

        /// <summary>
        /// Returns the empty PerfSessionInfo instance.
        /// </summary>
        public static PerfSessionInfo Empty => empty ?? Utility.InterlockedInitSingleton(
                ref empty, new PerfSessionInfo());

        /// <summary>
        /// Returns true if the the session's event data is formatted in big-endian
        /// byte order. (Use ByteReader to do byte-swapping as appropriate.)
        /// </summary>
        public bool FromBigEndian => this.ByteReader.FromBigEndian;

        /// <summary>
        /// Returns a PerfByteReader configured for the byte order of the events
        /// in this session, i.e. PerfByteReader(FromBigEndian).
        /// </summary>
        public PerfByteReader ByteReader { get; }

        /// <summary>
        /// Returns true if session clock offset is known.
        /// </summary>
        public bool ClockOffsetKnown { get; private set; }

        /// <summary>
        /// Returns the CLOCK_REALTIME value that corresponds to an event timestamp of 0
        /// for this session. Returns 1970 if the session timestamp offset is unknown.
        /// </summary>
        public PerfTimeSpec ClockOffset =>
            new PerfTimeSpec(this.clockOffsetSeconds, this.clockOffsetNanoseconds);

        /// <summary>
        /// Returns the clockid of the session timestamp, e.g. CLOCK_MONOTONIC.
        /// Returns 0xFFFFFFFF if the session timestamp clockid is unknown.
        /// </summary>
        public uint ClockId { get; private set; }

        /// <summary>
        /// From HEADER_CLOCKID. If unknown, use SetClockId(0xFFFFFFFF).
        /// </summary>
        public void SetClockId(uint clockid)
        {
            if (this.readOnly)
            {
                throw new InvalidOperationException("Cannot modify read-only PerfSessionInfo.");
            }

            this.ClockId = clockid;
        }

        /// <summary>
        /// From HEADER_CLOCK_DATA. If unknown, use SetClockData(0xFFFFFFFF, 0, 0).
        /// </summary>
        public void SetClockData(uint clockid, ulong wallClockNS, ulong clockidTimeNS)
        {
            if (this.readOnly)
            {
                throw new InvalidOperationException("Cannot modify read-only PerfSessionInfo.");
            }
            else if (clockid == 0xFFFFFFFF)
            {
                // Offset is unspecified.

                this.clockOffsetSeconds = 0;
                this.clockOffsetNanoseconds = 0;
                this.ClockId = clockid;
                this.ClockOffsetKnown = false;
            }
            else if (clockidTimeNS <= wallClockNS)
            {
                // Offset is positive.

                // wallClockNS = clockidTimeNS + offsetNS
                // offsetNS = wallClockNS - clockidTimeNS
                var offsetNS = wallClockNS - clockidTimeNS;

                // offsetNS = sec * Billion + nsec

                // sec = offsetNS / Billion
                this.clockOffsetSeconds = (long)(offsetNS / Billion);

                // nsec = offsetNS % Billion
                this.clockOffsetNanoseconds = (uint)(offsetNS % Billion);

                this.ClockId = clockid;
                this.ClockOffsetKnown = true;
            }
            else
            {
                // Offset is negative.

                // wallClockNS = clockidTimeNS + offsetNS
                // offsetNS = wallClockNS - clockidTimeNS
                // -negOffsetNS = wallClockNS - clockidTimeNS
                // negOffsetNS = clockidTimeNS - wallClockNS
                var negOffsetNS = clockidTimeNS - wallClockNS;

                // negOffsetNS = (negOffsetNS / Billion) * Billion + (negOffsetNS % Billion)
                // negOffsetNS = (negOffsetNS / Billion) * Billion + (negOffsetNS % Billion) - Billion + Billion
                // negOffsetNS = (negOffsetNS / Billion + 1) * Billion + (negOffsetNS % Billion) - Billion

                // negOffsetNS = negSec * Billion + negNsec
                // negSec = negOffsetNS / Billion + 1
                // negNsec = (negOffsetNS % Billion) - Billion

                // sec = -(negOffsetNS / Billion + 1)
                this.clockOffsetSeconds = -(long)(negOffsetNS / Billion) - 1;

                // nsec = -((negOffsetNS % Billion) - Billion)
                this.clockOffsetNanoseconds = Billion - (uint)(negOffsetNS % Billion);

                // Fix up case where nsec is too large.
                if (this.clockOffsetNanoseconds == Billion)
                {
                    this.clockOffsetSeconds += 1;
                    this.clockOffsetNanoseconds -= Billion;
                }

                this.ClockId = clockid;
                this.ClockOffsetKnown = true;
            }
        }

        /// <summary>
        /// Gets offset values suitable for use in HEADER_CLOCK_DATA.
        /// Note: The returned NS values may be normalized relative to the values provided
        /// to SetClockData, but the difference between them will be the same as the
        /// difference between the values provided to SetClockData.
        /// </summary>
        public void GetClockData(out ulong wallClockNS, out ulong clockidTimeNS)
        {
            if (this.clockOffsetSeconds >= 0)
            {
                wallClockNS = (ulong)this.clockOffsetSeconds * Billion + this.clockOffsetNanoseconds;
                clockidTimeNS = 0;
            }
            else
            {
                wallClockNS = 0;
                clockidTimeNS = (ulong)(-this.clockOffsetSeconds) * Billion - this.clockOffsetNanoseconds;
            }
        }

        /// <summary>
        /// Converts time from session timestamp to real-time (time since 1970):
        /// TimeToTimeSpec = ClockOffset() + time.
        /// If session clock offset is unknown, assumes 1970.
        /// </summary>
        public PerfTimeSpec TimeToTimeSpec(ulong time)
        {
            var sec = (long)(time / Billion);
            var nsec = (uint)(time % Billion);
            sec += this.clockOffsetSeconds;
            nsec += this.clockOffsetNanoseconds;
            if (nsec >= Billion)
            {
                sec += 1;
                nsec -= Billion;
            }
            return new PerfTimeSpec(sec, nsec);
        }

        /// <summary>
        /// Used by PerfSampleEventInfo, PerfNonSampleEventInfo.
        /// </summary>
        internal bool AppendJsonEventInfoTo(
            StringBuilder sb,
            bool addCommaBeforeNextItem,
            PerfInfoOptions infoOptions,
            PerfConvertOptions convertOptions,
            PerfEventAttrSampleType sampleType,
            ulong time,
            uint cpu,
            uint pid,
            uint tid,
            string name)
        {
            var w = new JsonWriter(sb, convertOptions, addCommaBeforeNextItem);

            if (sampleType.HasFlag(PerfEventAttrSampleType.Time) &&
                infoOptions.HasFlag(PerfInfoOptions.Time))
            {
                w.WriteValueNoEscapeName("time");
                if (this.ClockOffsetKnown && this.TimeToTimeSpec(time).DateTime is DateTime dt)
                {
                    sb.Append('"');
                    PerfConvert.DateTimeFullAppend(sb, dt);
                    sb.Append('"');
                }
                else
                {
                    PerfConvert.Float64Append(sb, time / 1000000000.0, convertOptions);
                }
            }

            if (sampleType.HasFlag(PerfEventAttrSampleType.Cpu) &&
                infoOptions.HasFlag(PerfInfoOptions.Cpu))
            {
                w.WriteValueNoEscapeName("cpu");
                PerfConvert.UInt32DecimalAppend(sb, cpu);
            }

            if (sampleType.HasFlag(PerfEventAttrSampleType.Tid))
            {
                if (infoOptions.HasFlag(PerfInfoOptions.Pid))
                {
                    w.WriteValueNoEscapeName("pid");
                    PerfConvert.UInt32DecimalAppend(sb, pid);
                }

                if (infoOptions.HasFlag(PerfInfoOptions.Tid) &&
                    (pid != tid || !infoOptions.HasFlag(PerfInfoOptions.Pid)))
                {
                    w.WriteValueNoEscapeName("tid");
                    PerfConvert.UInt32DecimalAppend(sb, tid);
                }
            }

            if (0 != (infoOptions & (PerfInfoOptions.Provider | PerfInfoOptions.Event)) &&
                !string.IsNullOrEmpty(name))
            {
                var nameSpan = name.AsSpan();
                var colonPos = nameSpan.IndexOf(':');
                ReadOnlySpan<char> providerName, eventName;
                if (colonPos < 0)
                {
                    providerName = default;
                    eventName = nameSpan;
                }
                else
                {
                    providerName = nameSpan.Slice(0, colonPos);
                    eventName = nameSpan.Slice(colonPos + 1);
                }

                if (infoOptions.HasFlag(PerfInfoOptions.Provider) &&
                    !providerName.IsEmpty)
                {
                    w.WriteValueNoEscapeName("provider");
                    PerfConvert.StringAppendJson(sb, providerName);
                }

                if (infoOptions.HasFlag(PerfInfoOptions.Event) &&
                    !eventName.IsEmpty)
                {
                    w.WriteValueNoEscapeName("event");
                    PerfConvert.StringAppendJson(sb, eventName);
                }
            }

            return w.Comma;
        }
    }
}
