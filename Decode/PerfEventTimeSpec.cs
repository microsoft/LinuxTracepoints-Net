// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1051 // Do not declare visible instance fields

namespace Microsoft.LinuxTracepoints.Decode
{
    /// <summary>
    /// Semantics equivalent to struct timespec from time.h.
    /// Time = 1970 + TvSec seconds + TvNsec nanoseconds.
    /// </summary>
    public struct PerfEventTimeSpec
    {
        /// <summary>
        /// Seconds since 1970.
        /// </summary>
        public long TvSec;

        /// <summary>
        /// Nanoseconds.
        /// </summary>
        public uint TvNsec;
    }
}
