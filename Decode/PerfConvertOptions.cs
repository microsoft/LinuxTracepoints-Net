// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;

    /// <summary>
    /// Flags used when formatting a value as a string.
    /// </summary>
    [Flags]
    public enum PerfConvertOptions : UInt32
    {
        /// <summary>
        /// No flags set.
        /// </summary>
        None = 0,

        /// <summary>
        /// Add spaces to the output, e.g. "Name": [ 1, 2, 3 ] instead of "Name":[1,2,3].
        /// </summary>
        Space = 0x01,

        /// <summary>
        /// When formatting with AppendJsonItemToAndMoveNextSibling, include the
        /// "Name": prefix for the root item.
        /// </summary>
        RootName = 0x02,

        /// <summary>
        /// When formatting with AppendJsonItemToAndMoveNextSibling, for items with a
        /// non-zero tag, add a tag suffix to the item's "Name": prefix, e.g.
        /// "Name;tag=0xNNNN": "ItemValue".
        /// </summary>
        FieldTag = 0x04,

        /// <summary>
        /// If set, float will format with "g9" (single-precision) or "g17" (double-precision).
        /// If unset, float will format with "g".
        /// </summary>
        FloatExtraPrecision = 0x10,

        /// <summary>
        /// If set, non-finite float will format as a string like "NaN" or "-Infinity".
        /// If unset, non-finite float will format as a null.
        /// </summary>
        FloatNonFiniteAsString = 0x20,

        /// <summary>
        /// If set, hex integer will format in JSON as a string like "0xF123".
        /// If unset, a hex integer will format in JSON as a decimal like 61731.
        /// </summary>
        IntHexAsString = 0x40,

        /// <summary>
        /// If set, boolean outside 0..1 will format as a string like "BOOL(-123)".
        /// If unset, boolean outside 0..1 will format as a number like -123.
        /// </summary>
        BoolOutOfRangeAsString = 0x80,

        /// <summary>
        /// If set, UnixTime within year 0001..9999 will format as a string like "2024-04-08T23:59:59Z".
        /// If unset, UnixTime within year 0001..9999 will format as a number like 1712620799.
        /// </summary>
        UnixTimeWithinRangeAsString = 0x100,

        /// <summary>
        /// If set, UnixTime64 outside year 0001..9999 will format as a string like "TIME(-62135596801)".
        /// If unset, UnixTime64 outside year 0001..9999 will format as a number like -62135596801.
        /// </summary>
        UnixTimeOutOfRangeAsString = 0x200,

        /// <summary>
        /// If set, Errno within 0..133 will format as a string like "ERRNO(0)" or "ENOENT(2)".
        /// If unset, Errno within 0..133 will format as a number like 0 or 2.
        /// </summary>
        ErrnoKnownAsString = 0x400,

        /// <summary>
        /// If set, Errno outside 0..133 will format as a string like "ERRNO(-1)".
        /// If unset, Errno outside 0..133 will format as a number like -1.
        /// </summary>
        ErrnoUnknownAsString = 0x800,

        /// <summary>
        /// Default flags.
        /// </summary>
        Default =
            Space |
            RootName |
            FieldTag |
            FloatExtraPrecision |
            FloatNonFiniteAsString |
            IntHexAsString |
            BoolOutOfRangeAsString |
            UnixTimeWithinRangeAsString |
            UnixTimeOutOfRangeAsString |
            ErrnoKnownAsString |
            ErrnoUnknownAsString,

        /// <summary>
        /// All flags set.
        /// </summary>
        All = ~0u,
    }
}
