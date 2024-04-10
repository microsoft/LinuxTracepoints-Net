﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using FlagsAttribute = System.FlagsAttribute;

    /// <summary>
    /// Options for the metadata suffix generated by
    /// EventHeaderEnumerator.AppendJsonEventMetaTo.
    /// </summary>
    [Flags]
    public enum EventHeaderMetaOptions : uint
    {
        /// <summary>
        /// disable the "meta" suffix.
        /// </summary>
        None = 0,

        /// <summary>
        /// Event identity, "n":"provider:event" before the user fields (not in the suffix).
        /// This flag is not used by AppendJsonEventMetaTo, but may be used by the caller to
        /// track whether the caller wants to add the "n" field to the event.
        /// </summary>
        N = 0x1,

        /// <summary>
        /// timestamp (only for sample events).
        /// </summary>
        Time = 0x2,

        /// <summary>
        /// cpu index (only for sample events).
        /// </summary>
        Cpu = 0x4,

        /// <summary>
        /// process id (only for sample events).
        /// </summary>
        Pid = 0x8,

        /// <summary>
        /// thread id (only for sample events).
        /// </summary>
        Tid = 0x10,

        /// <summary>
        /// eventheader id (decimal integer, omitted if 0).
        /// </summary>
        Id = 0x20,

        /// <summary>
        /// eventheader version (decimal integer, omitted if 0).
        /// </summary>
        Version = 0x40,

        /// <summary>
        /// eventheader level (decimal integer, omitted if 0).
        /// </summary>
        Level = 0x80,

        /// <summary>
        /// eventheader keyword (hexadecimal string, omitted if 0).
        /// </summary>
        Keyword = 0x100,

        /// <summary>
        /// eventheader opcode (decimal integer, omitted if 0).
        /// </summary>
        Opcode = 0x200,

        /// <summary>
        /// eventheader tag (hexadecimal string, omitted if 0).
        /// </summary>
        Tag = 0x400,

        /// <summary>
        /// eventheader activity ID (UUID string, omitted if 0).
        /// </summary>
        Activity = 0x800,

        /// <summary>
        /// eventheader related activity ID (UUID string, omitted if not set).
        /// </summary>
        RelatedActivity = 0x1000,

        /// <summary>
        /// provider name or system name (string).
        /// </summary>
        Provider = 0x10000,

        /// <summary>
        /// event name or tracepoint name (string).
        /// </summary>
        Event = 0x20000,

        /// <summary>
        /// eventheader provider options (string, omitted if none).
        /// </summary>
        Options = 0x40000,

        /// <summary>
        /// eventheader flags (hexadecimal string).
        /// </summary>
        Flags = 0x80000,

        /// <summary>
        /// Include the common_* fields before the user fields (only for sample events).
        /// </summary>
        Common = 0x100000,

        /// <summary>
        /// Include n..relatedActivity.
        /// </summary>
        Default = 0xffff,

        /// <summary>
        /// All flags set.
        /// </summary>
        All = ~0u
    }
}
