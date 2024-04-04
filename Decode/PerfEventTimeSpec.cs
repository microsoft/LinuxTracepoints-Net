// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1051 // Do not declare visible instance fields

namespace Microsoft.LinuxTracepoints.Decode
{
    /// <summary>
    /// Semantics equivalent to struct timespec from time.h.
    /// Time = 1970 + TvSec seconds + TvNsec nanoseconds.
    /// </summary>
    public readonly struct PerfEventTimeSpec
    {
        /// <summary>
        /// Seconds since 1970.
        /// </summary>
        public readonly long TvSec;

        /// <summary>
        /// Nanoseconds.
        /// </summary>
        public readonly uint TvNsec;

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
    }
}
