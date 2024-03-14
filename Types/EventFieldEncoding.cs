// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints
{
    /// <summary>
    /// <para>
    /// Values for the Encoding byte of a field definition.
    /// </para><para>
    /// The low 5 bits of the Encoding byte contain the field's encoding. The encoding
    /// indicates how a decoder should determine the size of the field. It also
    /// indicates a default format behavior that should be used if the field has no
    /// format specified or if the specified format is 0, unrecognized, or unsupported.
    /// </para><para>
    /// The top 3 bits of the field encoding byte are flags:
    /// </para><list type="bullet"><item>
    /// FlagCArray indicates that this field is a constant-length array, with the
    /// element count specified as a 16-bit value in the event metadata (must not be
    /// 0).
    /// </item><item>
    /// FlagVArray indicates that this field is a variable-length array, with the
    /// element count specified as a 16-bit value in the event payload (immediately
    /// before the array elements, may be 0).
    /// </item><item>
    /// FlagChain indicates that a format byte is present after the encoding byte.
    /// If Chain is not set, the format byte is omitted and is assumed to be 0.
    /// </item></list><para>
    /// Setting both CArray and VArray is invalid (reserved).
    /// </para>
    /// </summary>
    public enum EventFieldEncoding : byte
    {
        ValueMask = 0x1F,
        FlagMask = 0xE0,

        /// <summary>
        /// Constant-length array: 16-bit element count in metadata (must not be 0).
        /// </summary>
        FlagCArray = 0x20,

        /// <summary>
        /// Variable-length array: 16-bit element count in payload (may be 0).
        /// </summary>
        FlagVArray = 0x40,

        /// <summary>
        /// An EventFieldFormat byte follows the EventFieldEncoding byte.
        /// </summary>
        FlagChain = 0x80,

        /// <summary>
        /// Invalid encoding value.
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// 0-byte value, logically groups subsequent N fields, N = format &amp; 0x7F, N must not be 0.
        /// </summary>
        Struct,

        /// <summary>
        /// 1-byte value, default format UnsignedInt.
        /// </summary>
        Value8,

        /// <summary>
        /// 2-byte value, default format UnsignedInt.
        /// </summary>
        Value16,

        /// <summary>
        /// 4-byte value, default format UnsignedInt.
        /// </summary>
        Value32,

        /// <summary>
        /// 8-byte value, default format UnsignedInt.
        /// </summary>
        Value64,

        /// <summary>
        /// 16-byte value, default format HexBinary.
        /// </summary>
        Value128,

        /// <summary>
        /// zero-terminated uint8[], default format StringUtf.
        /// </summary>
        ZStringChar8,

        /// <summary>
        /// zero-terminated uint16[], default format StringUtf.
        /// </summary>
        ZStringChar16,

        /// <summary>
        /// zero-terminated uint32[], default format StringUtf.
        /// </summary>
        ZStringChar32,

        /// <summary>
        /// uint16 Length followed by uint8 Data[Length], default format StringUtf.
        /// Also used for binary data (format HexBinary).
        /// </summary>
        StringLength16Char8,

        /// <summary>
        /// uint16 Length followed by uint16 Data[Length], default format StringUtf.
        /// </summary>
        StringLength16Char16,

        /// <summary>
        /// uint16 Length followed by uint32 Data[Length], default format StringUtf.
        /// </summary>
        StringLength16Char32,

        /// <summary>
        /// Invalid encoding value. Value will change in future versions of this header.
        /// </summary>
        Max,
    }
}
