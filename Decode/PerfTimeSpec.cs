// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using CultureInfo = System.Globalization.CultureInfo;

    /// <summary>
    /// Semantics equivalent to struct timespec from time.h.
    /// Time = 1970 + TvSec seconds + TvNsec nanoseconds.
    /// </summary>
    public readonly struct PerfTimeSpec : IComparable<PerfTimeSpec>, IEquatable<PerfTimeSpec>
    {
        private const uint Billion = 1000000000;
        private const uint TicksPerSecond = 10000000;
        private const uint NanosecondsPerTick = 100;

        /// <summary>
        /// Initializes a new instance of the PerfTimeSpec struct.
        /// Normalizes TvNsec to the range 0..999999999, i.e. if the tvNsec parameter
        /// exceeds 999,999,999 then an appropriate number of seconds will be added to
        /// TvSec. (Note that this may cause TvSec to overflow, which is not detected.)
        /// </summary>
        /// <param name="tvSec">Signed value indicating seconds since 1970.</param>
        /// <param name="tvNsec">Nanoseconds.</param>
        public PerfTimeSpec(long tvSec, uint tvNsec)
        {
            this.TvSec = tvSec;
            this.TvNsec = tvNsec;
            while (this.TvNsec >= Billion)
            {
                this.TvNsec -= Billion;
                this.TvSec += 1; // May overflow.
            }
        }

        /// <summary>
        /// Returns the Unix epoch, 1970-01-01 00:00:00.
        /// </summary>
        public static PerfTimeSpec UnixEpoch => new PerfTimeSpec(0, 0);

        /// <summary>
        /// Returns the maximum representable value (year 292,277,026,596).
        /// </summary>
        public static PerfTimeSpec MaxValue => new PerfTimeSpec(long.MaxValue, Billion - 1);

        /// <summary>
        /// Returns the minimum representable value (year -292,277,022,656, or BC 292,277,022,657).
        /// </summary>
        public static PerfTimeSpec MinValue => new PerfTimeSpec(long.MinValue, 0);

        /// <summary>
        /// Initializes a new instance of the PerfTimeSpec struct from a DateTime.
        /// </summary>
        public PerfTimeSpec(DateTime dateTime)
        {
            var unixTicks = dateTime.Ticks - System.DateTime.UnixEpoch.Ticks;
            this.TvSec = unixTicks / TicksPerSecond;
            this.TvNsec = (uint)(unixTicks % TicksPerSecond) * NanosecondsPerTick;
        }

        /// <summary>
        /// Seconds since 1970.
        /// </summary>
        public long TvSec { get; }

        /// <summary>
        /// Nanoseconds.
        /// </summary>
        public uint TvNsec { get; }

        /// <summary>
        /// If TvSec is representable as a DateTime, returns that DateTime + TvNsec nanoseconds,
        /// rounded down to the nearest tick (100 nanosecond unit).
        /// Otherwise returns null.
        /// </summary>
        public DateTime? DateTime
        {
            get
            {
                var maybe = PerfConvert.UnixTime64ToDateTime(this.TvSec);
                if (maybe is DateTime dt)
                {
                    return dt.AddTicks(this.TvNsec / NanosecondsPerTick);
                }
                else
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Returns a new PerfTimeSpec that is the sum of this + nanoseconds.
        /// </summary>
        public PerfTimeSpec AddNanoseconds(long nanoseconds)
        {
            var sec = nanoseconds / Billion;
            var nsec = (int)(nanoseconds % Billion);

            // If nanoseconds was negative, nsec may be negative, i.e. nsec is currently
            // in the range (-1Billion, +1Billion). Subtract 1 from sec and add 1Billion
            // to nsec, resulting in (nsec + Billion) in the range (0..+2Billion), and the
            // tvNsec parameter in the range (0..+3Billion). The PerfTimeSpec constructor
            // will normalize this.
            return new PerfTimeSpec(
                this.TvSec + sec - 1,
                this.TvNsec + (uint)(nsec + Billion));
        }

        /// <summary>
        /// Returns a new PerfTimeSpec that is the sum of this + nanoseconds.
        /// </summary>
        public PerfTimeSpec AddNanoseconds(ulong nanoseconds)
        {
            var sec = (long)(nanoseconds / Billion);
            var nsec = (uint)(nanoseconds % Billion);

            // The tvNsec parameter is in the range (0..+2Billion). The PerfTimeSpec constructor
            // will normalize this.
            return new PerfTimeSpec(
                this.TvSec + sec,
                this.TvNsec + nsec);
        }

        /// <summary>
        /// Gets a hash code based on TvSec and TvNsec.
        /// </summary>
        public override int GetHashCode()
        {
            return (this.TvSec.GetHashCode() * 16777619) ^ this.TvNsec.GetHashCode();
        }

        /// <summary>
        /// Returns true if obj is a PerfTimeSpec and is equal to this.
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is PerfTimeSpec other)
            {
                return this.Equals(other);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Returns true if this is equal to other.
        /// </summary>
        public bool Equals(PerfTimeSpec other)
        {
            return this.TvSec == other.TvSec && this.TvNsec == other.TvNsec;
        }

        /// <summary>
        /// Returns negative if this is less than other, 0 if equal, positive if greater.
        /// </summary>
        public int CompareTo(PerfTimeSpec other)
        {
            var result = this.TvSec.CompareTo(other.TvSec);
            if (result == 0)
            {
                result = this.TvNsec.CompareTo(other.TvNsec);
            }
            return result;
        }

        /// <summary>
        /// If TvSec is representable as a DateTime, DateTime.ToString.
        /// Otherwise returns TvSec.TvNsec, e.g. "123456789.123456789".
        /// </summary>
        public override string ToString()
        {
            var maybe = PerfConvert.UnixTime64ToDateTime(this.TvSec);
            if (maybe is DateTime dt)
            {
                return dt.AddTicks(this.TvNsec / NanosecondsPerTick).ToString("O");
            }
            else
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}.{1:D9}", TvSec, TvNsec);
            }
        }

        /// <summary>
        /// Returns left == right.
        /// </summary>
        public static bool operator ==(PerfTimeSpec left, PerfTimeSpec right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Returns left != right.
        /// </summary>
        public static bool operator !=(PerfTimeSpec left, PerfTimeSpec right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Returns left &lt; right.
        /// </summary>
        public static bool operator <(PerfTimeSpec left, PerfTimeSpec right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <summary>
        /// Returns left &lt;= right.
        /// </summary>
        public static bool operator <=(PerfTimeSpec left, PerfTimeSpec right)
        {
            return left.CompareTo(right) <= 0;
        }

        /// <summary>
        /// Returns left > right.
        /// </summary>
        public static bool operator >(PerfTimeSpec left, PerfTimeSpec right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <summary>
        /// Returns left >= right.
        /// </summary>
        public static bool operator >=(PerfTimeSpec left, PerfTimeSpec right)
        {
            return left.CompareTo(right) >= 0;
        }
    }
}
