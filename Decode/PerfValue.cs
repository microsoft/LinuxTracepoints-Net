// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Diagnostics;
    using BinaryPrimitives = System.Buffers.Binary.BinaryPrimitives;
    using CultureInfo = System.Globalization.CultureInfo;
    using IPAddress = System.Net.IPAddress;
    using Text = System.Text;

    /// <summary>
    /// Provides access to a value within a perf event. The value may represent one
    /// of the following, determined by the context that produced this Value:
    /// <list type="bullet">
    /// <item>
    /// Value (single field, or element of an array):
    /// Bytes contains the field value in event-endian byte order.
    /// ArrayCount is 1.
    /// ElementSize is the size of the value's type (Bytes.Length == ElementSize), or 0
    /// if the type is variable-size (e.g. a string).
    /// Format is significant.
    /// StructFieldCount should be ignored.
    /// </item>
    /// <item>
    /// The beginning or end of a structure (single struct, or element of an array of struct):
    /// Bytes is empty (fields are accessed via the event enumerator).
    /// ArrayCount is 1.
    /// ElementSize is 0.
    /// Format should be ignored.
    /// StructFieldCount is significant.
    /// </item>
    /// <item>
    /// The beginning of an array of simple elements:
    /// Bytes contains the element values in event-endian byte order (Length = ArrayCount * ElementSize).
    /// ArrayCount is the number of elements.
    /// ElementSize is the size of the element's type.
    /// Format is significant.
    /// StructFieldCount should be ignored.
    /// </item>
    /// <item>
    /// The end of an array of simple elements:
    /// Bytes is empty.
    /// ArrayCount is the number of elements.
    /// ElementSize is the size of the element's type.
    /// Format is significant.
    /// StructFieldCount should be ignored.
    /// </item>
    /// <item>
    /// The beginning or end of an array of complex elements:
    /// Bytes is empty (array elements are accessed via the event enumerator).
    /// ArrayCount is the number of elements.
    /// ElementSize is 0.
    /// Either Format or StructFieldCount is significant, depending on whether the Encoding is Struct.
    /// </item>
    /// </summary>
    public readonly ref struct PerfValue
    {
        private readonly ReadOnlySpan<byte> bytes;
        private readonly PerfByteReader byteReader;
        private readonly EventFieldEncoding encodingAndArrayFlags;
        private readonly EventFieldFormat format;
        private readonly byte elementSize;
        private readonly ushort arrayCount;
        private readonly ushort fieldTag;

        public PerfValue(
            ReadOnlySpan<byte> bytes,
            PerfByteReader byteReader,
            EventFieldEncoding encodingAndArrayFlags,
            EventFieldFormat format,
            byte elementSize,
            ushort arrayCount,
            ushort fieldTag = 0)
        {
#if DEBUG
            // Chain flags must be masked-out by caller.
            Debug.Assert((encodingAndArrayFlags & EventFieldEncoding.ChainFlag) == 0);
            Debug.Assert((format & EventFieldFormat.ChainFlag) == 0);

            // If not an array, arrayCount must be 1.
            if ((encodingAndArrayFlags & EventFieldEncoding.FlagMask) == 0)
            {
                Debug.Assert(arrayCount == 1);
            }

            // If element has known size, validate bytes.Length.
            if (elementSize != 0 && !bytes.IsEmpty)
            {
                Debug.Assert(bytes.Length == arrayCount * elementSize);
            }

            if ((encodingAndArrayFlags & EventFieldEncoding.ValueMask) == EventFieldEncoding.Struct)
            {
                Debug.Assert(bytes.Length == 0);
                Debug.Assert(elementSize == 0);
                Debug.Assert(format != 0); // No zero-length structs.
            }
#endif

            this.bytes = bytes;
            this.arrayCount = arrayCount;
            this.fieldTag = fieldTag;
            this.elementSize = elementSize;
            this.encodingAndArrayFlags = encodingAndArrayFlags;
            this.format = format;
            this.byteReader = byteReader;
        }

        /// <summary>
        /// The content of this value, in event byte order.
        /// This may be empty for a complex value such as a struct or an array
        /// of variable-size elements, in which case you must access the individual
        /// sub-values using the event's enumerator.
        /// </summary>
        public ReadOnlySpan<byte> Bytes => this.bytes;

        /// <summary>
        /// Array element count. For non-array, this is 1.
        /// This may be 0 in the case of a variable-length array of length 0.
        /// </summary>
        public ushort ArrayCount => this.arrayCount;

        /// <summary>
        /// Field tag, or 0 if none.
        /// </summary>
        public ushort FieldTag => this.fieldTag;

        /// <summary>
        /// For simple encodings (e.g. Value8, Value16, Value32, Value64, Value128),
        /// this is the size of one value in bytes (1, 2, 4, 8, 16). For complex types
        /// (e.g. Struct or string), this is 0.
        /// </summary>
        public byte ElementSize => this.elementSize;

        /// <summary>
        /// Value's underlying encoding. The encoding indicates how to determine the value's
        /// size. The Encoding also implies a default formatting that should be used if
        /// the specified format is Default (0), unrecognized, or unsupported.
        /// </summary>
        public readonly EventFieldEncoding Encoding =>
            this.encodingAndArrayFlags & EventFieldEncoding.ValueMask;

        /// <summary>
        /// Contains CArrayFlag or VArrayFlag if the array begin, array end, or an element
        /// within an array.
        /// </summary>
        public readonly EventFieldEncoding ArrayFlags =>
            this.encodingAndArrayFlags & ~EventFieldEncoding.ValueMask;

        /// <summary>
        /// Returns Encoding | ArrayFlags.
        /// </summary>
        public readonly EventFieldEncoding EncodingAndArrayFlags =>
            this.encodingAndArrayFlags;

        /// <summary>
        /// true if the array begin, array end, or an element within an array.
        /// </summary>
        public readonly bool IsArray =>
            0 != (this.encodingAndArrayFlags & ~EventFieldEncoding.ValueMask);

        /// <summary>
        /// Field's semantic type. May be Default, in which case the semantic type should be
        /// determined based on the default format for the field's encoding.
        /// Meaningful only when Encoding != Struct (aliased with StructFieldCount).
        /// </summary>
        public EventFieldFormat Format => this.format;

        /// <summary>
        /// Number of fields in the struct.
        /// Meaningful only when Encoding == Struct (aliased with Format).
        /// </summary>
        public readonly byte StructFieldCount => (byte)this.format;

        /// <summary>
        /// A ByteReader that can be used to fix the byte order of this value's data.
        /// This is the same as new PerfByteReader(this.EventBigEndian).
        /// </summary>
        public PerfByteReader ByteReader => this.byteReader;

        /// <summary>
        /// True if this value's data uses big-endian byte order.
        /// This is the same as this.ByteReader.FromBigEndian.
        /// </summary>
        public readonly bool EventBigEndian => this.byteReader.FromBigEndian;

        /// <summary>
        /// Gets 1 byte starting at offset 0.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetSpan1() =>
            this.bytes.Slice(0, 1);

        /// <summary>
        /// Gets 1 byte starting at offset elementIndex * 1.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetSpan1(int elementIndex) =>
            this.bytes.Slice(elementIndex * 1, 1);

        /// <summary>
        /// Gets 2 bytes starting at offset 0.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetSpan2() =>
            this.bytes.Slice(0, 2);

        /// <summary>
        /// Gets 2 bytes starting at offset elementIndex * 2.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetSpan2(int elementIndex) =>
            this.bytes.Slice(elementIndex * 2, 2);

        /// <summary>
        /// Gets 4 bytes starting at offset 0.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetSpan4() =>
            this.bytes.Slice(0, 4);

        /// <summary>
        /// Gets 4 bytes starting at offset elementIndex * 4.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetSpan4(int elementIndex) =>
            this.bytes.Slice(elementIndex * 4, 4);

        /// <summary>
        /// Gets 8 bytes starting at offset 0.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetSpan8() =>
            this.bytes.Slice(0, 8);

        /// <summary>
        /// Gets 8 bytes starting at offset elementIndex * 8.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetSpan8(int elementIndex) =>
            this.bytes.Slice(elementIndex * 8, 8);

        /// <summary>
        /// Gets 16 bytes starting at offset 0.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetSpan16() =>
            this.bytes.Slice(0, 16);

        /// <summary>
        /// Gets 16 bytes starting at offset elementIndex * 16.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetSpan16(int elementIndex) =>
            this.bytes.Slice(elementIndex * 16, 16);

        /// <summary>
        /// Interprets the value as an array of Byte and returns the first element.
        /// </summary>
        public readonly Byte GetU8()
        {
            return this.bytes[0];
        }

        /// <summary>
        /// Interprets the value as an array of Byte and returns the element at the specified index.
        /// </summary>
        public readonly Byte GetU8(int elementIndex)
        {
            return this.bytes[elementIndex];
        }

        /// <summary>
        /// Interprets the value as an array of SByte and returns the first element.
        /// </summary>
        public readonly SByte GetI8()
        {
            return unchecked((SByte)this.bytes[0]);
        }

        /// <summary>
        /// Interprets the value as an array of SByte and returns the element at the specified index.
        /// </summary>
        public readonly SByte GetI8(int elementIndex)
        {
            return unchecked((SByte)this.bytes[elementIndex]);
        }

        /// <summary>
        /// Interprets the value as an array of UInt16 and returns the first element.
        /// </summary>
        public readonly UInt16 GetU16()
        {
            return this.ByteReader.ReadU16(this.bytes);
        }

        /// <summary>
        /// Interprets the value as an array of UInt16 and returns the element at the specified index.
        /// </summary>
        public readonly UInt16 GetU16(int elementIndex)
        {
            return this.ByteReader.ReadU16(this.bytes.Slice(elementIndex * sizeof(UInt16)));
        }

        /// <summary>
        /// Interprets the value as an array of Int16 and returns the first element.
        /// </summary>
        public readonly Int16 GetI16()
        {
            return this.ByteReader.ReadI16(this.bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Int16 and returns the element at the specified index.
        /// </summary>
        public readonly Int16 GetI16(int elementIndex)
        {
            return this.ByteReader.ReadI16(this.bytes.Slice(elementIndex * sizeof(Int16)));
        }

        /// <summary>
        /// Interprets the value as an array of UInt32 and returns the first element.
        /// </summary>
        public readonly UInt32 GetU32()
        {
            return this.ByteReader.ReadU32(this.bytes);
        }

        /// <summary>
        /// Interprets the value as an array of UInt32 and returns the element at the specified index.
        /// </summary>
        public readonly UInt32 GetU32(int elementIndex)
        {
            return this.ByteReader.ReadU32(this.bytes.Slice(elementIndex * sizeof(UInt32)));
        }

        /// <summary>
        /// Interprets the value as an array of Int32 and returns the first element.
        /// </summary>
        public readonly Int32 GetI32()
        {
            return this.ByteReader.ReadI32(this.bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Int32 and returns the element at the specified index.
        /// </summary>
        public readonly Int32 GetI32(int elementIndex)
        {
            return this.ByteReader.ReadI32(this.bytes.Slice(elementIndex * sizeof(Int32)));
        }

        /// <summary>
        /// Interprets the value as an array of UInt64 and returns the first element.
        /// </summary>
        public readonly UInt64 GetU64()
        {
            return this.ByteReader.ReadU64(this.bytes);
        }

        /// <summary>
        /// Interprets the value as an array of UInt64 and returns the element at the specified index.
        /// </summary>
        public readonly UInt64 GetU64(int elementIndex)
        {
            return this.ByteReader.ReadU64(this.bytes.Slice(elementIndex * sizeof(UInt64)));
        }

        /// <summary>
        /// Interprets the value as an array of Int64 and returns the first element.
        /// </summary>
        public readonly Int64 GetI64()
        {
            return this.ByteReader.ReadI64(this.bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Int64 and returns the element at the specified index.
        /// </summary>
        public readonly Int64 GetI64(int elementIndex)
        {
            return this.ByteReader.ReadI64(this.bytes.Slice(elementIndex * sizeof(Int64)));
        }

        /// <summary>
        /// Interprets the value as an array of Single and returns the first element.
        /// </summary>
        public readonly Single GetF32()
        {
            return this.ByteReader.ReadF32(this.bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Single and returns the element at the specified index.
        /// </summary>
        public readonly Single GetF32(int elementIndex)
        {
            return this.ByteReader.ReadF32(this.bytes.Slice(elementIndex * sizeof(Single)));
        }

        /// <summary>
        /// Interprets the value as an array of Double and returns the first element.
        /// </summary>
        public readonly Double GetF64()
        {
            return this.ByteReader.ReadF64(this.bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Double and returns the element at the specified index.
        /// </summary>
        public readonly Double GetF64(int elementIndex)
        {
            return this.ByteReader.ReadF64(this.bytes.Slice(elementIndex * sizeof(Double)));
        }

        /// <summary>
        /// Interprets the value as an array of Guid and returns the first element.
        /// </summary>
        public readonly Guid GetGuid()
        {
            return Utility.ReadGuidBigEndian(this.bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Guid and returns the element at the specified index.
        /// </summary>
        public readonly Guid GetGuid(int elementIndex)
        {
            const int SizeOfGuid = 16;
            return Utility.ReadGuidBigEndian(this.bytes.Slice(elementIndex * SizeOfGuid));
        }

        /// <summary>
        /// Interprets the value as an array of big-endian IP ports and returns the first element.
        /// </summary>
        public readonly UInt16 GetPort()
        {
            return BinaryPrimitives.ReadUInt16BigEndian(this.bytes);
        }

        /// <summary>
        /// Interprets the value as an array of UInt8 and returns the element at the specified index.
        /// </summary>
        public readonly UInt16 GetPort(int elementIndex)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(this.bytes.Slice(elementIndex * sizeof(UInt16)));
        }

        /// <summary>
        /// Interprets the value as an array of IPv4 addresses and returns the first element.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetIPv4()
        {
            return this.GetSpan4();
        }

        /// <summary>
        /// Interprets the value as an array of IPv4 addresses and returns the element at the specified index.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetIPv4(int elementIndex)
        {
            return this.GetSpan4(elementIndex);
        }

        /// <summary>
        /// Interprets the value as an array of IPv6 addresses and returns the first element.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetIPv6()
        {
            return this.GetSpan16();
        }

        /// <summary>
        /// Interprets the value as an array of IPv6 addresses and returns the element at the specified index.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetIPv6(int elementIndex)
        {
            return this.GetSpan16(elementIndex);
        }

        /// <summary>
        /// Interprets the value as a string and returns the string's encoded bytes and the
        /// encoding to use to convert the bytes to a string. The encoding is determined
        /// based on the field's Encoding, Format, and a BOM (if present) in the value bytes.
        /// If a BOM was detected, the returned encoded bytes will not include the BOM.
        /// </summary>
        public readonly ReadOnlySpan<byte> GetStringBytes(out Text.Encoding encoding)
        {
            ReadOnlySpan<byte> result;
            switch (this.Format)
            {
                case EventFieldFormat.String8:
                    result = this.bytes;
                    encoding = PerfConvert.EncodingLatin1;
                    break;
                case EventFieldFormat.StringUtfBom:
                case EventFieldFormat.StringXml:
                case EventFieldFormat.StringJson:
                    var encodingFromBom = EncodingFromBom(this.bytes);
                    if (encodingFromBom == null)
                    {
                        goto case EventFieldFormat.StringUtf;
                    }
                    result = this.bytes.Slice(encodingFromBom.Preamble.Length);
                    encoding = encodingFromBom;
                    break;
                case EventFieldFormat.StringUtf:
                default:
                    result = this.bytes;
                    switch (this.Encoding)
                    {
                        default:
                            encoding = PerfConvert.EncodingLatin1;
                            break;
                        case EventFieldEncoding.Value8:
                        case EventFieldEncoding.ZStringChar8:
                        case EventFieldEncoding.StringLength16Char8:
                            encoding = Text.Encoding.UTF8;
                            break;
                        case EventFieldEncoding.Value16:
                        case EventFieldEncoding.ZStringChar16:
                        case EventFieldEncoding.StringLength16Char16:
                            encoding = Text.Encoding.Unicode;
                            break;
                        case EventFieldEncoding.Value32:
                        case EventFieldEncoding.ZStringChar32:
                        case EventFieldEncoding.StringLength16Char32:
                            encoding = Text.Encoding.UTF32;
                            break;
                    }
                    break;
            }

            return result;
        }

        /// <summary>
        /// Interprets the value as an encoded string and returns a new UTF-16 string with
        /// the decoded string value. The encoding is determined based on the field's Encoding,
        /// Format, and a BOM (if present) in the value bytes.
        /// </summary>
        public readonly string GetString()
        {
            var bytes = this.GetStringBytes(out var encoding);
            return encoding.GetString(bytes);
        }

        /// <summary>
        /// Interprets the item as a single field (ArrayCount >= 0, Encoding != Struct).
        /// Returns the item formatted as a new string based on Encoding and Format.
        /// </summary>
        public readonly string FormatValue()
        {
            Text.Encoding? encodingFromBom;
            switch (this.encodingAndArrayFlags & EventFieldEncoding.ValueMask)
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventFieldEncoding.Invalid:
                    return "null";
                case EventFieldEncoding.Struct:
                    throw new InvalidOperationException("Invalid encoding for FormatValue.");
                case EventFieldEncoding.Value8:
                    switch (this.format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            return this.GetU8().ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.SignedInt:
                            return this.GetI8().ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexInt:
                            return string.Format(CultureInfo.InvariantCulture, "0x{0:X}", this.GetU8());
                        case EventFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU8());
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.GetSpan1(0));
                        case EventFieldFormat.String8:
                            return ((char)this.GetU8()).ToString();
                    }
                case EventFieldEncoding.Value16:
                    switch (this.format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            return this.GetU16().ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.SignedInt:
                            return this.GetI16().ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexInt:
                            return string.Format(CultureInfo.InvariantCulture, "0x{0:X}", this.GetU16());
                        case EventFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU16());
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.GetSpan2());
                        case EventFieldFormat.StringUtf:
                            return ((char)this.GetU16()).ToString();
                        case EventFieldFormat.Port:
                            return this.GetPort().ToString(CultureInfo.InvariantCulture);
                    }
                case EventFieldEncoding.Value32:
                    switch (this.format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            return this.GetU32().ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.SignedInt:
                        case EventFieldFormat.Pid:
                            return this.GetI32().ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexInt:
                            return string.Format(CultureInfo.InvariantCulture, "0x{0:X}", this.GetU32());
                        case EventFieldFormat.Errno:
                            return PerfConvert.ErrnoToString(this.GetI32());
                        case EventFieldFormat.Time:
                            return PerfConvert.UnixTime32ToString(this.GetI32());
                        case EventFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU32());
                        case EventFieldFormat.Float:
                            return this.GetF32().ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.GetSpan4());
                        case EventFieldFormat.StringUtf:
                            return this.byteReader.FromBigEndian
                                ? PerfConvert.EncodingUTF32BE.GetString(this.bytes.Slice(0, sizeof(UInt32)))
                                : Text.Encoding.UTF32.GetString(this.bytes.Slice(0, sizeof(UInt32)));
                        case EventFieldFormat.IPv4:
                            return new IPAddress(this.GetIPv4()).ToString();
                    }
                case EventFieldEncoding.Value64:
                    switch (this.format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            return this.GetU64().ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.SignedInt:
                            return this.GetI64().ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexInt:
                            return string.Format(CultureInfo.InvariantCulture, "0x{0:X}", this.GetU64());
                        case EventFieldFormat.Time:
                            return PerfConvert.UnixTime64ToString(this.GetI64());
                        case EventFieldFormat.Float:
                            return this.GetF64().ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.GetSpan8());
                    }
                case EventFieldEncoding.Value128:
                    switch (this.format)
                    {
                        default:
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.bytes.Slice(0, 16));
                        case EventFieldFormat.Uuid:
                            return this.GetGuid().ToString();
                        case EventFieldFormat.IPv6:
                            return new IPAddress(this.GetSpan16()).ToString();
                    }
                case EventFieldEncoding.ZStringChar8:
                case EventFieldEncoding.StringLength16Char8:
                    switch (this.format)
                    {
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.bytes);
                        case EventFieldFormat.String8:
                            return PerfConvert.EncodingLatin1.GetString(this.bytes);
                        case EventFieldFormat.StringUtfBom:
                        case EventFieldFormat.StringXml:
                        case EventFieldFormat.StringJson:
                            encodingFromBom = EncodingFromBom(this.bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventFieldFormat.StringUtf;
                            }
                            return encodingFromBom.GetString(this.bytes.Slice(encodingFromBom.Preamble.Length));
                        default:
                        case EventFieldFormat.StringUtf:
                            return Text.Encoding.UTF8.GetString(this.bytes);
                    }
                case EventFieldEncoding.ZStringChar16:
                case EventFieldEncoding.StringLength16Char16:
                    switch (this.format)
                    {
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.bytes);
                        case EventFieldFormat.StringUtfBom:
                        case EventFieldFormat.StringXml:
                        case EventFieldFormat.StringJson:
                            encodingFromBom = EncodingFromBom(this.bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventFieldFormat.StringUtf;
                            }
                            return encodingFromBom.GetString(this.bytes.Slice(encodingFromBom.Preamble.Length));
                        default:
                        case EventFieldFormat.StringUtf:
                            return this.byteReader.FromBigEndian
                                ? Text.Encoding.BigEndianUnicode.GetString(this.bytes)
                                : Text.Encoding.Unicode.GetString(this.bytes);
                    }
                case EventFieldEncoding.ZStringChar32:
                case EventFieldEncoding.StringLength16Char32:
                    switch (this.format)
                    {
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.bytes);
                        case EventFieldFormat.StringUtfBom:
                        case EventFieldFormat.StringXml:
                        case EventFieldFormat.StringJson:
                            encodingFromBom = EncodingFromBom(this.bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventFieldFormat.StringUtf;
                            }
                            return encodingFromBom.GetString(this.bytes.Slice(encodingFromBom.Preamble.Length));
                        default:
                        case EventFieldFormat.StringUtf:
                            return this.byteReader.FromBigEndian
                                ? PerfConvert.EncodingUTF32BE.GetString(this.bytes)
                                : Text.Encoding.UTF32.GetString(this.bytes);
                    }
            }
        }

        /// <summary>
        /// Interprets the item as the element of a simple array (elementIndex &lt; ArrayCount, ElementSize != 0).
        /// Returns the element formatted as a new string based on Encoding and Format.
        /// </summary>
        public readonly string FormatSimpleArrayValue(int elementIndex)
        {
            switch (this.encodingAndArrayFlags & EventFieldEncoding.ValueMask)
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventFieldEncoding.Invalid:
                case EventFieldEncoding.Struct:
                case EventFieldEncoding.ZStringChar8:
                case EventFieldEncoding.StringLength16Char8:
                case EventFieldEncoding.ZStringChar16:
                case EventFieldEncoding.StringLength16Char16:
                case EventFieldEncoding.ZStringChar32:
                case EventFieldEncoding.StringLength16Char32:
                    throw new InvalidOperationException("Invalid encoding for FormatSimpleArrayValue.");
                case EventFieldEncoding.Value8:
                    switch (this.format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            return this.GetU8(elementIndex).ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.SignedInt:
                            return this.GetI8(elementIndex).ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexInt:
                            return string.Format(CultureInfo.InvariantCulture, "0x{0:X}", this.GetU8(elementIndex));
                        case EventFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU8(elementIndex));
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.GetSpan1(elementIndex));
                        case EventFieldFormat.String8:
                            return ((char)this.GetU8(elementIndex)).ToString();
                    }
                case EventFieldEncoding.Value16:
                    switch (this.format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            return this.GetU16(elementIndex).ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.SignedInt:
                            return this.GetI16(elementIndex).ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexInt:
                            return string.Format(CultureInfo.InvariantCulture, "0x{0:X}", this.GetU16(elementIndex));
                        case EventFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU16(elementIndex));
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.GetSpan2(elementIndex));
                        case EventFieldFormat.StringUtf:
                            return ((char)this.GetU16(elementIndex)).ToString();
                        case EventFieldFormat.Port:
                            return this.GetPort(elementIndex).ToString(CultureInfo.InvariantCulture);
                    }
                case EventFieldEncoding.Value32:
                    switch (this.format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            return this.GetU32(elementIndex).ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.SignedInt:
                        case EventFieldFormat.Pid:
                            return this.GetI32(elementIndex).ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexInt:
                            return string.Format(CultureInfo.InvariantCulture, "0x{0:X}", this.GetU32(elementIndex));
                        case EventFieldFormat.Errno:
                            return PerfConvert.ErrnoToString(this.GetI32(elementIndex));
                        case EventFieldFormat.Time:
                            return PerfConvert.UnixTime32ToString(this.GetI32(elementIndex));
                        case EventFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU32(elementIndex));
                        case EventFieldFormat.Float:
                            return this.GetF32(elementIndex).ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.GetSpan4(elementIndex));
                        case EventFieldFormat.StringUtf:
                            return this.byteReader.FromBigEndian
                                ? PerfConvert.EncodingUTF32BE.GetString(this.bytes.Slice(elementIndex * sizeof(UInt32), sizeof(UInt32)))
                                : Text.Encoding.UTF32.GetString(this.bytes.Slice(elementIndex * sizeof(UInt32), sizeof(UInt32)));
                        case EventFieldFormat.IPv4:
                            return new IPAddress(this.GetIPv4(elementIndex)).ToString();
                    }
                case EventFieldEncoding.Value64:
                    switch (this.format)
                    {
                        default:
                        case EventFieldFormat.UnsignedInt:
                            return this.GetU64(elementIndex).ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.SignedInt:
                            return this.GetI64(elementIndex).ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexInt:
                            return string.Format(CultureInfo.InvariantCulture, "0x{0:X}", this.GetU64(elementIndex));
                        case EventFieldFormat.Time:
                            return PerfConvert.UnixTime64ToString(this.GetI64(elementIndex));
                        case EventFieldFormat.Float:
                            return this.GetF64(elementIndex).ToString(CultureInfo.InvariantCulture);
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.GetSpan8(elementIndex));
                    }
                case EventFieldEncoding.Value128:
                    switch (this.format)
                    {
                        default:
                        case EventFieldFormat.HexBytes:
                            return PerfConvert.ToHexString(this.GetSpan16(elementIndex));
                        case EventFieldFormat.Uuid:
                            return this.GetGuid(elementIndex).ToString();
                        case EventFieldFormat.IPv6:
                            return new IPAddress(this.GetIPv6(elementIndex)).ToString();
                    }
            }
        }

        private static Text.Encoding? EncodingFromBom(ReadOnlySpan<byte> str)
        {
            var cb = str.Length;
            if (cb >= 4 &&
                str[0] == 0xFF &&
                str[1] == 0xFE &&
                str[2] == 0x00 &&
                str[3] == 0x00)
            {
                return Text.Encoding.UTF32;
            }
            else if (cb >= 4 &&
                str[0] == 0x00 &&
                str[1] == 0x00 &&
                str[2] == 0xFE &&
                str[3] == 0xFF)
            {
                return PerfConvert.EncodingUTF32BE;
            }
            else if (cb >= 3 &&
                str[0] == 0xEF &&
                str[1] == 0xBB &&
                str[2] == 0xBF)
            {
                return Text.Encoding.UTF8;
            }
            else if (cb >= 2 &&
               str[0] == 0xFF &&
               str[1] == 0xFE)
            {
                return Text.Encoding.Unicode;
            }
            else if (cb >= 2 &&
                str[0] == 0xFE &&
                str[1] == 0xFF)
            {
                return Text.Encoding.BigEndianUnicode;
            }
            else
            {
                return null;
            }
        }
    }
}
