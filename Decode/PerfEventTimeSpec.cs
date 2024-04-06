// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using DateTime = System.DateTime;
    using CultureInfo = System.Globalization.CultureInfo;

    /// <summary>
    /// Semantics equivalent to struct timespec from time.h.
    /// Time = 1970 + TvSec seconds + TvNsec nanoseconds.
    /// </summary>
    public readonly struct PerfEventTimeSpec
    {
        /// <summary>
        /// Initializes a new instance of the PerfEventTimeSpec struct.
        /// </summary>
        /// <param name="tvSec">Signed value indicating seconds since 1970.</param>
        /// <param name="tvNsec">Nanoseconds.</param>
        public PerfEventTimeSpec(long tvSec, uint tvNsec)
        {
            this.TvSec = tvSec;
            this.TvNsec = tvNsec;
            while (this.TvNsec >= 1000000000)
            {
                if (this.TvSec == long.MaxValue)
                {
                    this.TvNsec = 999999999;
                    break;
                }

                this.TvNsec -= 1000000000;
                this.TvSec += 1;
            }
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
        /// If TvSec is representable as a DateTime, returns that DateTime + TvNsec nanoseconds.
        /// Otherwise returns null.
        /// </summary>
        public DateTime? DateTime
        {
            get
            {
                var maybe = PerfConvert.UnixTime64ToDateTime(this.TvSec);
                if (maybe is DateTime dt)
                {
                    dt.AddTicks(this.TvNsec / 100);
                    return dt;
                }
                else
                {
                    return null;
                }
            }
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
                dt.AddTicks(this.TvNsec / 100);
                return dt.ToString("O");
            }
            else
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}.{1:D9}", TvSec, TvNsec);
            }
        }
    }
}
