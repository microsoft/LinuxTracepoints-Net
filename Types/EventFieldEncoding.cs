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
    /// CArrayFlag indicates that this field is a constant-length array, with the
    /// element count specified as a 16-bit value in the event metadata (must not be
    /// 0).
    /// </item><item>
    /// VArrayFlag indicates that this field is a variable-length array, with the
    /// element count specified as a 16-bit value in the event payload (immediately
    /// before the array elements, may be 0).
    /// </item><item>
    /// ChainFlag indicates that a format byte is present after the encoding byte.
    /// If Chain is not set, the format byte is omitted and is assumed to be 0.
    /// </item></list><para>
    /// Setting both CArray and VArray is invalid (reserved).
    /// </para>
    /// </summary>
    public enum EventFieldEncoding : byte
    {
        /// <summary>
        /// Mask for the base encoding type (low 5 bits).
        /// </summary>
        ValueMask = 0x1F,

        /// <summary>
        /// Mask for the encoding flags: CArrayFlag, VArrayFlag, ChainFlag.
        /// </summary>
        FlagMask = 0xE0,

        /// <summary>
        /// Constant-length array: 16-bit element count in metadata (must not be 0).
        /// </summary>
        CArrayFlag = 0x20,

        /// <summary>
        /// Variable-length array: 16-bit element count in payload (may be 0).
        /// </summary>
        VArrayFlag = 0x40,

        /// <summary>
        /// If present in the field, this flag indicates that an EventFieldFormat
        /// byte follows the EventFieldEncoding byte.
        /// </summary>
        ChainFlag = 0x80,

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
        /// 16-byte value, default format HexBytes.
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
        /// Also used for binary data (format HexBytes).
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

    /// <summary>
    /// Extension methods for <see cref="EventFieldEncoding"/>.
    /// </summary>
    public static class EventFieldEncodingExtensions
    {
        /// <summary>
        /// Returns the encoding without any flags (encoding &amp; ValueMask).
        /// </summary>
        public static EventFieldEncoding BaseEncoding(this EventFieldEncoding encoding) =>
            encoding & EventFieldEncoding.ValueMask;

        /// <summary>
        /// Returns the array flags of the encoding (VArrayFlag or CArrayFlag, if set).
        /// </summary>
        public static EventFieldEncoding ArrayFlags(this EventFieldEncoding encoding) =>
            encoding & (EventFieldEncoding.VArrayFlag | EventFieldEncoding.CArrayFlag);

        /// <summary>
        /// Returns true if any ArrayFlag is present (constant-length or variable-length array).
        /// </summary>
        public static bool IsArray(this EventFieldEncoding encoding) =>
            0 != (encoding & (EventFieldEncoding.VArrayFlag | EventFieldEncoding.CArrayFlag));

        /// <summary>
        /// Returns true if CArrayFlag is present (constant-length array).
        /// </summary>
        public static bool IsCArray(this EventFieldEncoding encoding) =>
            0 != (encoding & EventFieldEncoding.CArrayFlag);

        /// <summary>
        /// Returns true if VArrayFlag is present (variable-length array).
        /// </summary>
        public static bool IsVArray(this EventFieldEncoding encoding) =>
            0 != (encoding & EventFieldEncoding.VArrayFlag);

        /// <summary>
        /// Returns true if ChainFlag is present (format byte is present in event).
        /// </summary>
        public static bool HasChainFlag(this EventFieldEncoding encoding) =>
            0 != (encoding & EventFieldEncoding.ChainFlag);

        /// <summary>
        /// Gets the default format for the encoding, or EventFieldFormat.Default if the encoding is invalid.
        /// <list type="bullet"><item>
        /// Value8, Value16, Value32, Value64: UnsignedInt.
        /// </item><item>
        /// Value128: HexBytes.
        /// </item><item>
        /// String: StringUtf.
        /// </item><item>
        /// Other: Default.
        /// </item></list>
        /// </summary>
        public static EventFieldFormat DefaultFormat(this EventFieldEncoding encoding)
        {
            switch (encoding & EventFieldEncoding.ValueMask)
            {
                case EventFieldEncoding.Value8:
                case EventFieldEncoding.Value16:
                case EventFieldEncoding.Value32:
                case EventFieldEncoding.Value64:
                    return EventFieldFormat.UnsignedInt;
                case EventFieldEncoding.Value128:
                    return EventFieldFormat.HexBytes;
                case EventFieldEncoding.ZStringChar8:
                case EventFieldEncoding.ZStringChar16:
                case EventFieldEncoding.ZStringChar32:
                case EventFieldEncoding.StringLength16Char8:
                case EventFieldEncoding.StringLength16Char16:
                case EventFieldEncoding.StringLength16Char32:
                    return EventFieldFormat.StringUtf;
                default:
                    return EventFieldFormat.Default;
            }
        }
    }
}
