// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    /// <summary>
    /// Information about a perf event collection session.
    /// </summary>
    public class PerfEventSessionInfo
    {
        private const uint Billion = 1000000000;
        private static PerfEventSessionInfo? empty;

        private long clockOffsetSeconds;
        private uint clockOffsetNanoseconds;
        private uint clockid = 0xFFFFFFFF;
        bool clockOffsetKnown;
        private readonly PerfByteReader byteReader;

        public PerfEventSessionInfo(PerfByteReader byteReader)
        {
            this.byteReader = byteReader;
        }

        /// <summary>
        /// Returns the empty PerfEventSessionInfo instance.
        /// </summary>
        public static PerfEventSessionInfo Empty
        {
            get
            {
                var value = empty;
                if (value == null)
                {
                    value = new PerfEventSessionInfo(default);
                    empty = value;
                }
                return value;
            }
        }

        /// <summary>
        /// Returns true if the session data is in big-endian byte order.
        /// </summary>
        public bool IsBigEndian => this.byteReader.FromBigEndian;

        /// <summary>
        /// Returns ByteReader(IsBigEndian).
        /// </summary>
        public PerfByteReader ByteReader => this.byteReader;

        /// <summary>
        /// Returns the clockid of the session timestamp, e.g. CLOCK_MONOTONIC.
        /// Returns 0xFFFFFFFF if the session timestamp clockid is unknown.
        /// </summary>
        public uint ClockId
        {
            get => this.clockid;
        }

        /// <summary>
        /// Returns true if session clock offset is known.
        /// </summary>
        public bool ClockOffsetKnown
        {
            get => this.clockOffsetKnown;
        }

        /// <summary>
        /// Returns the CLOCK_REALTIME value that corresponds to an event timestamp of 0
        /// for this session. Returns 1970 if the session timestamp offset is unknown.
        /// </summary>
        public PerfEventTimeSpec ClockOffset
        {
            get => new PerfEventTimeSpec
            {
                TvSec = this.clockOffsetSeconds,
                TvNsec = this.clockOffsetNanoseconds,
            };
        }

        /// <summary>
        /// From HEADER_CLOCKID. If unknown, use SetClockId(0xFFFFFFFF).
        /// </summary>
        public void SetClockId(uint clockid)
        {
            this.clockid = clockid;
        }

        /// <summary>
        /// From HEADER_CLOCK_DATA. If unknown, use SetClockData(0xFFFFFFFF, 0, 0).
        /// </summary>
        public void SetClockData(uint clockid, ulong wallClockNS, ulong clockidTimeNS)
        {
            if (clockid == 0xFFFFFFFF)
            {
                // Offset is unspecified.

                this.clockOffsetSeconds = 0;
                this.clockOffsetNanoseconds = 0;
                this.clockid = clockid;
                this.clockOffsetKnown = false;
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

                this.clockid = clockid;
                this.clockOffsetKnown = true;
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

                this.clockid = clockid;
                this.clockOffsetKnown = true;
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
                clockidTimeNS = 0;
                wallClockNS = (ulong)this.clockOffsetSeconds * Billion + this.clockOffsetNanoseconds;
            }
            else
            {
                wallClockNS = 0;
                clockidTimeNS = (ulong)(-this.clockOffsetSeconds) * Billion - this.clockOffsetNanoseconds;
            }
        }

        /// <summary>
        /// Converts time from session timestamp to real-time (time since 1970):
        /// TimeToRealTime = ClockOffset() + time.
        /// If session clock offset is unknown, assumes 1970.
        /// </summary>
        public PerfEventTimeSpec TimeToRealTime(ulong time)
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
            return new PerfEventTimeSpec { TvSec = sec, TvNsec = nsec };
        }
    }
}
