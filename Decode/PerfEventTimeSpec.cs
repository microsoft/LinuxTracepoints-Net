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

        public PerfEventTimeSpec(long tvSec, uint tvNsec)
        {
            this.TvSec = tvSec;
            this.TvNsec = tvNsec;
        }
    }
}
