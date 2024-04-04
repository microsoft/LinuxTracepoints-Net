// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
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
        }

        /// <summary>
        /// Seconds since 1970.
        /// </summary>
        public long TvSec { get; }

        /// <summary>
        /// Nanoseconds.
        /// </summary>
        public uint TvNsec { get; }
    }
}
