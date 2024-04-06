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
    /// Provides access to a field value within a perf event. The value may represent
    /// one of the following, determined by the context that produced this PerfValue:
    /// <list type="bullet">
    /// <item>
    /// Scalar (non-array field, or one element of an array):
    /// Bytes contains the scalar value in event-endian byte order.
    /// ElementCount is 1.
    /// TypeSize is the size of the value's type (Bytes.Length == TypeSize) if
    /// the type has a constant size (e.g. a UInt32), or 0 if the type is variable-size 
    /// (e.g. a string).
    /// Format is significant.
    /// StructFieldCount should be ignored.
    /// </item>
    /// <item>
    /// The beginning or end of a structure (single struct, or one element of an array of struct):
    /// Bytes is empty (the structure's fields are accessed via the event enumerator).
    /// ElementCount is 1.
    /// TypeSize is 0.
    /// Format should be ignored.
    /// StructFieldCount is significant.
    /// </item>
    /// <item>
    /// The beginning of an array of simple type (non-struct, element's type is fixed-size):
    /// Bytes contains the element values in event-endian byte order (Length = ElementCount * TypeSize).
    /// ElementCount is the number of elements in the array.
    /// TypeSize is the size of the element's type.
    /// Format is significant.
    /// StructFieldCount should be ignored.
    /// </item>
    /// <item>
    /// The end of an array of simple type:
    /// Bytes is empty.
    /// ElementCount is the number of elements in the array.
    /// TypeSize is the size of the element's type.
    /// Format is significant.
    /// StructFieldCount should be ignored.
    /// </item>
    /// <item>
    /// The beginning or end of an array of complex elements:
    /// Bytes is empty (array elements are accessed via the event enumerator).
    /// ElementCount is the number of elements in the array.
    /// TypeSize is 0.
    /// Either Format or StructFieldCount is significant, depending on whether the Encoding is Struct.
    /// </item>
    /// </list>
    /// </summary>
    public readonly ref struct PerfValue
    {
        /// <summary>
        /// Initializes a new instance of the PerfValue struct. These are normally created
        /// by EventHeaderEnumerator.GetItemInfo() or by PerfFieldFormat.GetFieldValue().
        /// </summary>
        public PerfValue(
            ReadOnlySpan<byte> bytes,
            PerfByteReader byteReader,
            EventHeaderFieldEncoding encodingAndArrayFlags,
            EventHeaderFieldFormat format,
            byte typeSize,
            ushort elementCount,
            ushort fieldTag = 0)
        {
            // Chain flags must be masked-out by caller.
            Debug.Assert(!encodingAndArrayFlags.HasChainFlag());
            Debug.Assert(!format.HasChainFlag());

#if DEBUG
            // If not an array, elementCount must be 1.
            if ((encodingAndArrayFlags & EventHeaderFieldEncoding.FlagMask) == 0)
            {
                Debug.Assert(elementCount == 1);
            }

            // If type has known size, validate bytes.Length.
            if (typeSize != 0 && !bytes.IsEmpty)
            {
                Debug.Assert(bytes.Length == elementCount * typeSize);
            }

            if (encodingAndArrayFlags.BaseEncoding() == EventHeaderFieldEncoding.Struct)
            {
                Debug.Assert(bytes.Length == 0);
                Debug.Assert(typeSize == 0);
                Debug.Assert(format != 0); // No zero-length structs.
            }
#endif

            this.Bytes = bytes;
            this.ElementCount = elementCount;
            this.FieldTag = fieldTag;
            this.TypeSize = typeSize;
            this.EncodingAndArrayFlags = encodingAndArrayFlags;
            this.Format = format;
            this.ByteReader = byteReader;
        }

        /// <summary>
        /// The content of this value, in event byte order.
        /// This may be empty for a complex value such as a struct, or an array
        /// of variable-size elements, in which case you must access the individual
        /// sub-values using the event's enumerator.
        /// </summary>
        public ReadOnlySpan<byte> Bytes { get; }

        /// <summary>
        /// For begin array or end array, this is number of elements in the array.
        /// For non-array or for element of an array, this is 1.
        /// This may be 0 in the case of a variable-length array of length 0.
        /// </summary>
        public ushort ElementCount { get; }

        /// <summary>
        /// Field tag, or 0 if none.
        /// </summary>
        public ushort FieldTag { get; }

        /// <summary>
        /// For simple encodings (e.g. Value8, Value16, Value32, Value64, Value128),
        /// this is the size of one element in bytes (1, 2, 4, 8, 16). For complex types
        /// (e.g. Struct or string), this is 0.
        /// </summary>
        public byte TypeSize { get; }

        /// <summary>
        /// Value's underlying encoding. The encoding indicates how to determine the value's
        /// size. The Encoding also implies a default formatting that should be used if
        /// the specified format is Default (0), unrecognized, or unsupported. The value
        /// returned by this property does not include any flags.
        /// </summary>
        public EventHeaderFieldEncoding Encoding =>
            this.EncodingAndArrayFlags & EventHeaderFieldEncoding.ValueMask;

        /// <summary>
        /// Contains CArrayFlag or VArrayFlag if the value represents an array begin,
        /// array end, or an element within an array. 0 for a non-array value.
        /// </summary>
        public EventHeaderFieldEncoding ArrayFlags =>
            this.EncodingAndArrayFlags & ~EventHeaderFieldEncoding.ValueMask;

        /// <summary>
        /// Returns Encoding | ArrayFlags.
        /// </summary>
        public EventHeaderFieldEncoding EncodingAndArrayFlags { get; }

        /// <summary>
        /// true if this value represents an array begin, array end, or an element within
        /// an array. false for a non-array value.
        /// </summary>
        public bool IsArrayOrElement =>
            0 != (this.EncodingAndArrayFlags & ~EventHeaderFieldEncoding.ValueMask);

        /// <summary>
        /// Field's semantic type. May be Default, in which case the semantic type should be
        /// determined based on encoding.DefaultFormat().
        /// Meaningful only when Encoding != Struct (aliased with StructFieldCount).
        /// </summary>
        public EventHeaderFieldFormat Format { get; }

        /// <summary>
        /// Number of fields in the struct.
        /// Meaningful only when Encoding == Struct (aliased with Format).
        /// </summary>
        public byte StructFieldCount => (byte)this.Format;

        /// <summary>
        /// A ByteReader that can be used to fix the byte order of this value's data.
        /// This is the same as PerfByteReader(this.FromBigEndian).
        /// </summary>
        public PerfByteReader ByteReader { get; }

        /// <summary>
        /// True if this value's data uses big-endian byte order.
        /// This is the same as this.ByteReader.FromBigEndian.
        /// </summary>
        public bool FromBigEndian => this.ByteReader.FromBigEndian;

        /// <summary>
        /// For Value8: Gets 1-byte span starting at offset 0.
        /// </summary>
        public ReadOnlySpan<byte> GetSpan8() =>
            this.Bytes.Slice(0, 1);

        /// <summary>
        /// For Value8: Gets 1-byte span starting at offset elementIndex * 1.
        /// </summary>
        public ReadOnlySpan<byte> GetSpan8(int elementIndex) =>
            this.Bytes.Slice(elementIndex * 1, 1);

        /// <summary>
        /// For Value16: Gets 2-byte span starting at offset 0.
        /// </summary>
        public ReadOnlySpan<byte> GetSpan16() =>
            this.Bytes.Slice(0, 2);

        /// <summary>
        /// For Value16: Gets 2-byte span starting at offset elementIndex * 2.
        /// </summary>
        public ReadOnlySpan<byte> GetSpan16(int elementIndex) =>
            this.Bytes.Slice(elementIndex * 2, 2);

        /// <summary>
        /// For Value32: Gets 4-byte span starting at offset 0.
        /// </summary>
        public ReadOnlySpan<byte> GetSpan32() =>
            this.Bytes.Slice(0, 4);

        /// <summary>
        /// For Value32: Gets 4-byte span starting at offset elementIndex * 4.
        /// </summary>
        public ReadOnlySpan<byte> GetSpan32(int elementIndex) =>
            this.Bytes.Slice(elementIndex * 4, 4);

        /// <summary>
        /// For Value64: Gets 8-byte span starting at offset 0.
        /// </summary>
        public ReadOnlySpan<byte> GetSpan64() =>
            this.Bytes.Slice(0, 8);

        /// <summary>
        /// For Value64: Gets 8-byte span starting at offset elementIndex * 8.
        /// </summary>
        public ReadOnlySpan<byte> GetSpan64(int elementIndex) =>
            this.Bytes.Slice(elementIndex * 8, 8);

        /// <summary>
        /// For Value128: Gets 16 bytes starting at offset 0.
        /// </summary>
        public ReadOnlySpan<byte> GetSpan128() =>
            this.Bytes.Slice(0, 16);

        /// <summary>
        /// For Value128: Gets 16 bytes starting at offset elementIndex * 16.
        /// </summary>
        public ReadOnlySpan<byte> GetSpan128(int elementIndex) =>
            this.Bytes.Slice(elementIndex * 16, 16);

        /// <summary>
        /// Interprets the value as an array of Byte and returns the first element.
        /// </summary>
        public Byte GetU8()
        {
            return this.Bytes[0];
        }

        /// <summary>
        /// Interprets the value as an array of Byte and returns the element at the specified index.
        /// </summary>
        public Byte GetU8(int elementIndex)
        {
            return this.Bytes[elementIndex];
        }

        /// <summary>
        /// Interprets the value as an array of SByte and returns the first element.
        /// </summary>
        public SByte GetI8()
        {
            return unchecked((SByte)this.Bytes[0]);
        }

        /// <summary>
        /// Interprets the value as an array of SByte and returns the element at the specified index.
        /// </summary>
        public SByte GetI8(int elementIndex)
        {
            return unchecked((SByte)this.Bytes[elementIndex]);
        }

        /// <summary>
        /// Interprets the value as an array of UInt16 and returns the first element.
        /// </summary>
        public UInt16 GetU16()
        {
            return this.ByteReader.ReadU16(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of UInt16 and returns the element at the specified index.
        /// </summary>
        public UInt16 GetU16(int elementIndex)
        {
            return this.ByteReader.ReadU16(this.Bytes.Slice(elementIndex * sizeof(UInt16)));
        }

        /// <summary>
        /// Interprets the value as an array of Int16 and returns the first element.
        /// </summary>
        public Int16 GetI16()
        {
            return this.ByteReader.ReadI16(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Int16 and returns the element at the specified index.
        /// </summary>
        public Int16 GetI16(int elementIndex)
        {
            return this.ByteReader.ReadI16(this.Bytes.Slice(elementIndex * sizeof(Int16)));
        }

        /// <summary>
        /// Interprets the value as an array of UInt32 and returns the first element.
        /// </summary>
        public UInt32 GetU32()
        {
            return this.ByteReader.ReadU32(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of UInt32 and returns the element at the specified index.
        /// </summary>
        public UInt32 GetU32(int elementIndex)
        {
            return this.ByteReader.ReadU32(this.Bytes.Slice(elementIndex * sizeof(UInt32)));
        }

        /// <summary>
        /// Interprets the value as an array of Int32 and returns the first element.
        /// </summary>
        public Int32 GetI32()
        {
            return this.ByteReader.ReadI32(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Int32 and returns the element at the specified index.
        /// </summary>
        public Int32 GetI32(int elementIndex)
        {
            return this.ByteReader.ReadI32(this.Bytes.Slice(elementIndex * sizeof(Int32)));
        }

        /// <summary>
        /// Interprets the value as an array of UInt64 and returns the first element.
        /// </summary>
        public UInt64 GetU64()
        {
            return this.ByteReader.ReadU64(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of UInt64 and returns the element at the specified index.
        /// </summary>
        public UInt64 GetU64(int elementIndex)
        {
            return this.ByteReader.ReadU64(this.Bytes.Slice(elementIndex * sizeof(UInt64)));
        }

        /// <summary>
        /// Interprets the value as an array of Int64 and returns the first element.
        /// </summary>
        public Int64 GetI64()
        {
            return this.ByteReader.ReadI64(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Int64 and returns the element at the specified index.
        /// </summary>
        public Int64 GetI64(int elementIndex)
        {
            return this.ByteReader.ReadI64(this.Bytes.Slice(elementIndex * sizeof(Int64)));
        }

        /// <summary>
        /// Interprets the value as an array of Single and returns the first element.
        /// </summary>
        public Single GetF32()
        {
            return this.ByteReader.ReadF32(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Single and returns the element at the specified index.
        /// </summary>
        public Single GetF32(int elementIndex)
        {
            return this.ByteReader.ReadF32(this.Bytes.Slice(elementIndex * sizeof(Single)));
        }

        /// <summary>
        /// Interprets the value as an array of Double and returns the first element.
        /// </summary>
        public Double GetF64()
        {
            return this.ByteReader.ReadF64(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Double and returns the element at the specified index.
        /// </summary>
        public Double GetF64(int elementIndex)
        {
            return this.ByteReader.ReadF64(this.Bytes.Slice(elementIndex * sizeof(Double)));
        }

        /// <summary>
        /// Interprets the value as an array of Guid and returns the first element.
        /// </summary>
        public Guid GetGuid()
        {
            return Utility.ReadGuidBigEndian(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Guid and returns the element at the specified index.
        /// </summary>
        public Guid GetGuid(int elementIndex)
        {
            const int SizeOfGuid = 16;
            return Utility.ReadGuidBigEndian(this.Bytes.Slice(elementIndex * SizeOfGuid));
        }

        /// <summary>
        /// Interprets the value as an array of big-endian IP ports and returns the first element.
        /// </summary>
        public UInt16 GetPort()
        {
            return BinaryPrimitives.ReadUInt16BigEndian(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of big-endian IP ports and returns the element at the specified index.
        /// </summary>
        public UInt16 GetPort(int elementIndex)
        {
            return BinaryPrimitives.ReadUInt16BigEndian(this.Bytes.Slice(elementIndex * sizeof(UInt16)));
        }

        /// <summary>
        /// Interprets the value as an array of big-endian IPv4 addresses and returns the first element.
        /// </summary>
        public UInt32 GetIPv4()
        {
            return BitConverter.ToUInt32(this.Bytes); // Do not swap byte order.
        }

        /// <summary>
        /// Interprets the value as an array of big-endian IPv4 addresses and returns the element at the specified index.
        /// </summary>
        public UInt32 GetIPv4(int elementIndex)
        {
            return BitConverter.ToUInt32(this.Bytes.Slice(elementIndex * sizeof(UInt32))); // Do not swap byte order.
        }

        /// <summary>
        /// Interprets the value as an array of IPv6 addresses and returns the first element.
        /// </summary>
        public ReadOnlySpan<byte> GetIPv6()
        {
            return this.GetSpan128();
        }

        /// <summary>
        /// Interprets the value as an array of IPv6 addresses and returns the element at the specified index.
        /// </summary>
        public ReadOnlySpan<byte> GetIPv6(int elementIndex)
        {
            return this.GetSpan128(elementIndex);
        }

        /// <summary>
        /// Interprets the value as a string and returns the string's encoded bytes and the
        /// encoding to use to convert the bytes to a string. The encoding is determined
        /// based on the field's Encoding, Format, and a BOM (if present) in the value bytes.
        /// If a BOM was detected, the returned encoded bytes will not include the BOM.
        /// </summary>
        public ReadOnlySpan<byte> GetStringBytes(out Text.Encoding encoding)
        {
            ReadOnlySpan<byte> result;
            switch (this.Format)
            {
                case EventHeaderFieldFormat.String8:
                    result = this.Bytes;
                    encoding = PerfConvert.EncodingLatin1;
                    break;
                case EventHeaderFieldFormat.StringUtfBom:
                case EventHeaderFieldFormat.StringXml:
                case EventHeaderFieldFormat.StringJson:
                    var encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                    if (encodingFromBom == null)
                    {
                        goto case EventHeaderFieldFormat.StringUtf;
                    }
                    result = this.Bytes.Slice(encodingFromBom.Preamble.Length);
                    encoding = encodingFromBom;
                    break;
                case EventHeaderFieldFormat.StringUtf:
                default:
                    result = this.Bytes;
                    switch (this.Encoding)
                    {
                        default:
                            encoding = PerfConvert.EncodingLatin1;
                            break;
                        case EventHeaderFieldEncoding.Value8:
                        case EventHeaderFieldEncoding.ZStringChar8:
                        case EventHeaderFieldEncoding.StringLength16Char8:
                            encoding = Text.Encoding.UTF8;
                            break;
                        case EventHeaderFieldEncoding.Value16:
                        case EventHeaderFieldEncoding.ZStringChar16:
                        case EventHeaderFieldEncoding.StringLength16Char16:
                            encoding = this.FromBigEndian
                                ? Text.Encoding.BigEndianUnicode
                                : Text.Encoding.Unicode;
                            break;
                        case EventHeaderFieldEncoding.Value32:
                        case EventHeaderFieldEncoding.ZStringChar32:
                        case EventHeaderFieldEncoding.StringLength16Char32:
                            encoding = this.FromBigEndian
                                ? PerfConvert.EncodingUTF32BE
                                : Text.Encoding.UTF32;
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
        public string GetString()
        {
            var bytes = this.GetStringBytes(out var encoding);
            return encoding.GetString(bytes);
        }

        /// <summary>
        /// Interprets the value as a scalar.
        /// Requires ElementCount >= 0, Encoding != Struct, Encoding &lt; Max.
        /// Returns the value formatted as a new string based on Encoding and Format.
        /// </summary>
        public string FormatScalar()
        {
            Text.Encoding? encodingFromBom;
            switch (this.EncodingAndArrayFlags.BaseEncoding())
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventHeaderFieldEncoding.Struct:
                    throw new InvalidOperationException("Invalid encoding for FormatScalar.");
                case EventHeaderFieldEncoding.Invalid:
                    return "null";
                case EventHeaderFieldEncoding.Value8:
                    switch (this.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            return PerfConvert.DecimalU32ToString(this.GetU8());
                        case EventHeaderFieldFormat.SignedInt:
                            return PerfConvert.DecimalI32ToString(this.GetI8());
                        case EventHeaderFieldFormat.HexInt:
                            return PerfConvert.HexU32ToString(this.GetU8());
                        case EventHeaderFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU8());
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.GetSpan8());
                        case EventHeaderFieldFormat.String8:
                            return ((char)this.GetU8()).ToString();
                    }
                case EventHeaderFieldEncoding.Value16:
                    switch (this.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            return PerfConvert.DecimalU32ToString(this.GetU16());
                        case EventHeaderFieldFormat.SignedInt:
                            return PerfConvert.DecimalI32ToString(this.GetI16());
                        case EventHeaderFieldFormat.HexInt:
                            return PerfConvert.HexU32ToString(this.GetU16());
                        case EventHeaderFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU16());
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.GetSpan16());
                        case EventHeaderFieldFormat.StringUtf:
                            return ((char)this.GetU16()).ToString();
                        case EventHeaderFieldFormat.Port:
                            return PerfConvert.DecimalU32ToString(this.GetPort());
                    }
                case EventHeaderFieldEncoding.Value32:
                    switch (this.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            return PerfConvert.DecimalU32ToString(this.GetU32());
                        case EventHeaderFieldFormat.SignedInt:
                        case EventHeaderFieldFormat.Pid:
                            return PerfConvert.DecimalI32ToString(this.GetI32());
                        case EventHeaderFieldFormat.HexInt:
                            return PerfConvert.HexU32ToString(this.GetU32());
                        case EventHeaderFieldFormat.Errno:
                            return PerfConvert.ErrnoToString(this.GetI32());
                        case EventHeaderFieldFormat.Time:
                            return PerfConvert.UnixTime32ToString(this.GetI32());
                        case EventHeaderFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU32());
                        case EventHeaderFieldFormat.Float:
                            return this.GetF32().ToString(CultureInfo.InvariantCulture);
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.GetSpan32());
                        case EventHeaderFieldFormat.StringUtf:
                            return PerfConvert.Utf32ToString(this.GetU32());
                        case EventHeaderFieldFormat.IPv4:
                            return PerfConvert.IPv4ToString(this.GetIPv4());
                    }
                case EventHeaderFieldEncoding.Value64:
                    switch (this.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            return PerfConvert.DecimalU64ToString(this.GetU64());
                        case EventHeaderFieldFormat.SignedInt:
                            return PerfConvert.DecimalI64ToString(this.GetI64());
                        case EventHeaderFieldFormat.HexInt:
                            return PerfConvert.HexU64ToString(this.GetU64());
                        case EventHeaderFieldFormat.Time:
                            return PerfConvert.UnixTime64ToString(this.GetI64());
                        case EventHeaderFieldFormat.Float:
                            return this.GetF64().ToString(CultureInfo.InvariantCulture);
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.GetSpan64());
                    }
                case EventHeaderFieldEncoding.Value128:
                    switch (this.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.GetSpan128());
                        case EventHeaderFieldFormat.Uuid:
                            return this.GetGuid().ToString();
                        case EventHeaderFieldFormat.IPv6:
                            return new IPAddress(this.GetSpan128()).ToString();
                    }
                case EventHeaderFieldEncoding.ZStringChar8:
                case EventHeaderFieldEncoding.StringLength16Char8:
                    switch (this.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.Bytes);
                        case EventHeaderFieldFormat.String8:
                            return PerfConvert.EncodingLatin1.GetString(this.Bytes);
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            return encodingFromBom.GetString(this.Bytes.Slice(encodingFromBom.Preamble.Length));
                        default:
                        case EventHeaderFieldFormat.StringUtf:
                            return Text.Encoding.UTF8.GetString(this.Bytes);
                    }
                case EventHeaderFieldEncoding.ZStringChar16:
                case EventHeaderFieldEncoding.StringLength16Char16:
                    switch (this.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.Bytes);
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            return encodingFromBom.GetString(this.Bytes.Slice(encodingFromBom.Preamble.Length));
                        default:
                        case EventHeaderFieldFormat.StringUtf:
                            return this.ByteReader.FromBigEndian
                                ? Text.Encoding.BigEndianUnicode.GetString(this.Bytes)
                                : Text.Encoding.Unicode.GetString(this.Bytes);
                    }
                case EventHeaderFieldEncoding.ZStringChar32:
                case EventHeaderFieldEncoding.StringLength16Char32:
                    switch (this.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.Bytes);
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            return encodingFromBom.GetString(this.Bytes.Slice(encodingFromBom.Preamble.Length));
                        default:
                        case EventHeaderFieldFormat.StringUtf:
                            return this.ByteReader.FromBigEndian
                                ? PerfConvert.EncodingUTF32BE.GetString(this.Bytes)
                                : Text.Encoding.UTF32.GetString(this.Bytes);
                    }
            }
        }

        /// <summary>
        /// Interprets the value as the beginning of a simple array.
        /// Requires elementIndex &lt; ElementCount, TypeSize != 0.
        /// Returns one element of the array formatted as a new string based on Encoding and Format.
        /// </summary>
        public string FormatSimpleArrayElement(int elementIndex)
        {
            switch (this.EncodingAndArrayFlags.BaseEncoding())
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventHeaderFieldEncoding.Invalid:
                case EventHeaderFieldEncoding.Struct:
                case EventHeaderFieldEncoding.ZStringChar8:
                case EventHeaderFieldEncoding.StringLength16Char8:
                case EventHeaderFieldEncoding.ZStringChar16:
                case EventHeaderFieldEncoding.StringLength16Char16:
                case EventHeaderFieldEncoding.ZStringChar32:
                case EventHeaderFieldEncoding.StringLength16Char32:
                    throw new InvalidOperationException("Invalid encoding for FormatSimpleArrayElement.");
                case EventHeaderFieldEncoding.Value8:
                    switch (this.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            return PerfConvert.DecimalU32ToString(this.GetU8(elementIndex));
                        case EventHeaderFieldFormat.SignedInt:
                            return PerfConvert.DecimalI32ToString(this.GetI8(elementIndex));
                        case EventHeaderFieldFormat.HexInt:
                            return PerfConvert.HexU32ToString(this.GetU8(elementIndex));
                        case EventHeaderFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU8(elementIndex));
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.GetSpan8(elementIndex));
                        case EventHeaderFieldFormat.String8:
                            return ((char)this.GetU8(elementIndex)).ToString();
                    }
                case EventHeaderFieldEncoding.Value16:
                    switch (this.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            return PerfConvert.DecimalU32ToString(this.GetU16(elementIndex));
                        case EventHeaderFieldFormat.SignedInt:
                            return PerfConvert.DecimalI32ToString(this.GetI16(elementIndex));
                        case EventHeaderFieldFormat.HexInt:
                            return PerfConvert.HexU32ToString(this.GetU16(elementIndex));
                        case EventHeaderFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU16(elementIndex));
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.GetSpan16(elementIndex));
                        case EventHeaderFieldFormat.StringUtf:
                            return ((char)this.GetU16(elementIndex)).ToString();
                        case EventHeaderFieldFormat.Port:
                            return PerfConvert.DecimalU32ToString(this.GetPort(elementIndex));
                    }
                case EventHeaderFieldEncoding.Value32:
                    switch (this.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            return PerfConvert.DecimalU32ToString(this.GetU32(elementIndex));
                        case EventHeaderFieldFormat.SignedInt:
                        case EventHeaderFieldFormat.Pid:
                            return PerfConvert.DecimalI32ToString(this.GetI32(elementIndex));
                        case EventHeaderFieldFormat.HexInt:
                            return PerfConvert.HexU32ToString(this.GetU32(elementIndex));
                        case EventHeaderFieldFormat.Errno:
                            return PerfConvert.ErrnoToString(this.GetI32(elementIndex));
                        case EventHeaderFieldFormat.Time:
                            return PerfConvert.UnixTime32ToString(this.GetI32(elementIndex));
                        case EventHeaderFieldFormat.Boolean:
                            return PerfConvert.BooleanToString(this.GetU32(elementIndex));
                        case EventHeaderFieldFormat.Float:
                            return this.GetF32().ToString(CultureInfo.InvariantCulture);
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.GetSpan32(elementIndex));
                        case EventHeaderFieldFormat.StringUtf:
                            return PerfConvert.Utf32ToString(this.GetU32(elementIndex));
                        case EventHeaderFieldFormat.IPv4:
                            return PerfConvert.IPv4ToString(this.GetIPv4(elementIndex));
                    }
                case EventHeaderFieldEncoding.Value64:
                    switch (this.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            return PerfConvert.DecimalU64ToString(this.GetU64(elementIndex));
                        case EventHeaderFieldFormat.SignedInt:
                            return PerfConvert.DecimalI64ToString(this.GetI64(elementIndex));
                        case EventHeaderFieldFormat.HexInt:
                            return PerfConvert.HexU64ToString(this.GetU64(elementIndex));
                        case EventHeaderFieldFormat.Time:
                            return PerfConvert.UnixTime64ToString(this.GetI64(elementIndex));
                        case EventHeaderFieldFormat.Float:
                            return this.GetF64(elementIndex).ToString(CultureInfo.InvariantCulture);
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.GetSpan64(elementIndex));
                    }
                case EventHeaderFieldEncoding.Value128:
                    switch (this.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.HexBytes:
                            return PerfConvert.HexBytesToString(this.GetSpan128(elementIndex));
                        case EventHeaderFieldFormat.Uuid:
                            return this.GetGuid(elementIndex).ToString();
                        case EventHeaderFieldFormat.IPv6:
                            return new IPAddress(this.GetSpan128(elementIndex)).ToString();
                    }
            }
        }

        /// <summary>
        /// Appends a string representation of this value like "Type:Value" or "Type:Value1,Value2".
        /// Returns sb.
        /// </summary>
        public Text.StringBuilder AppendAsString(Text.StringBuilder sb)
        {
            const char Separator = ',';
            int count;
            Text.Encoding? encodingFromBom;
            var baseEncoding = this.EncodingAndArrayFlags.BaseEncoding();
            switch (this.EncodingAndArrayFlags.BaseEncoding())
            {
                default:
                    sb.Append(baseEncoding.ToString());
                    goto Nothing;
                case EventHeaderFieldEncoding.Invalid:
                    sb.Append("Invalid");
                    goto Nothing;
                case EventHeaderFieldEncoding.Struct:
                    sb.Append("Struct");
                Nothing:
                    sb.Append(':');
                    PerfConvert.HexBytesAppend(sb, this.Bytes);
                    break;
                case EventHeaderFieldEncoding.Value8:
                    count = this.Bytes.Length / sizeof(Byte);
                    switch (this.Format)
                    {
                        default:
                            sb.Append(this.Format.ToString());
                            goto case EventHeaderFieldFormat.UnsignedInt;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            sb.Append("UInt8:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.DecimalU32Append(sb, this.GetU8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            sb.Append("Int8:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.DecimalI32Append(sb, this.GetI8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            sb.Append("Hex8:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexU32Append(sb, this.GetU8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            sb.Append("Bool8:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.BooleanAppend(sb, this.GetU8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexByte8:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.String8:
                            sb.Append("Char8:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                sb.Append((char)this.GetU8(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value16:
                    count = this.Bytes.Length / sizeof(UInt16);
                    switch (this.Format)
                    {
                        default:
                            sb.Append(this.Format.ToString());
                            goto case EventHeaderFieldFormat.UnsignedInt;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            sb.Append("UInt16:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.DecimalU32Append(sb, this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            sb.Append("Int16:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.DecimalI32Append(sb, this.GetI16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            sb.Append("Hex16:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexU32Append(sb, this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            sb.Append("Bool16:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.BooleanAppend(sb, this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexByte16:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.StringUtf:
                            sb.Append("Char16:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                sb.Append((char)this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Port:
                            sb.Append("Port:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.DecimalU32Append(sb, this.GetPort(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value32:
                    count = this.Bytes.Length / sizeof(UInt32);
                    switch (this.Format)
                    {
                        default:
                            sb.Append(this.Format.ToString());
                            goto case EventHeaderFieldFormat.UnsignedInt;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            sb.Append("UInt32:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.DecimalU32Append(sb, this.GetU32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Pid:
                            sb.Append("Pid:");
                            goto SignedInt32;
                        case EventHeaderFieldFormat.SignedInt:
                            sb.Append("Int32:");
                        SignedInt32:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.DecimalI32Append(sb, this.GetI32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            sb.Append("Hex32:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexU32Append(sb, this.GetU32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Errno:
                            sb.Append("Errno:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.ErrnoAppend(sb, this.GetI32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Time:
                            sb.Append("Time32:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UnixTime32Append(sb, this.GetI32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            sb.Append("Bool32:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.BooleanAppend(sb, this.GetU32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Float:
                            sb.Append("Float32:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                sb.Append(this.GetF32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexByte32:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.StringUtf:
                            sb.Append("Char32:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.Utf32Append(sb, this.GetU32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.IPv4:
                            sb.Append("IPv4:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.IPv4Append(sb, this.GetIPv4(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value64:
                    count = this.Bytes.Length / sizeof(UInt64);
                    switch (this.Format)
                    {
                        default:
                            sb.Append(this.Format.ToString());
                            goto case EventHeaderFieldFormat.UnsignedInt;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            sb.Append("UInt64:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.DecimalU64Append(sb, this.GetU64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            sb.Append("Int64:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.DecimalI64Append(sb, this.GetI64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            sb.Append("Hex64:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexU64Append(sb, this.GetU64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Time:
                            sb.Append("Time64:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UnixTime64Append(sb, this.GetI64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Float:
                            sb.Append("Float64:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                sb.Append(this.GetF64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexByte64:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan64(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value128:
                    count = this.Bytes.Length / 16;
                    switch (this.Format)
                    {
                        default:
                            sb.Append(this.Format.ToString());
                            goto case EventHeaderFieldFormat.HexBytes;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexByte128:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan128(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Uuid:
                            sb.Append("Guid:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                sb.Append(Utility.ReadGuidBigEndian(this.GetSpan128(i)).ToString());
                            }
                            break;
                        case EventHeaderFieldFormat.IPv6:
                            sb.Append("IPv6:");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                sb.Append(new IPAddress(this.GetSpan128(i)).ToString());
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.ZStringChar8:
                    sb.Append('Z');
                    goto case EventHeaderFieldEncoding.StringLength16Char8;
                case EventHeaderFieldEncoding.StringLength16Char8:
                    switch (this.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexBytes8:");
                            PerfConvert.HexBytesAppend(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.String8:
                            sb.Append("String8:");
                            sb.Append(PerfConvert.EncodingLatin1.GetString(this.Bytes));
                            break;
                        case EventHeaderFieldFormat.StringXml:
                            sb.Append("StringXml8:");
                            goto UtfBom8;
                        case EventHeaderFieldFormat.StringJson:
                            sb.Append("StringJson8:");
                            goto UtfBom8;
                        case EventHeaderFieldFormat.StringUtfBom:
                            sb.Append("StringUtfBom8:");
                        UtfBom8:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto Utf8;
                            }
                            sb.Append(encodingFromBom.GetString(this.Bytes.Slice(encodingFromBom.Preamble.Length)));
                            break;
                        default:
                            sb.Append(this.Format.ToString());
                            goto case EventHeaderFieldFormat.StringUtf;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.StringUtf:
                            sb.Append("StringUtf8:");
                        Utf8:
                            sb.Append(Text.Encoding.UTF8.GetString(this.Bytes));
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.ZStringChar16:
                    sb.Append('Z');
                    goto case EventHeaderFieldEncoding.StringLength16Char16;
                case EventHeaderFieldEncoding.StringLength16Char16:
                    switch (this.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexBytes16:");
                            PerfConvert.HexBytesAppend(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.StringXml:
                            sb.Append("StringXml16:");
                            goto UtfBom16;
                        case EventHeaderFieldFormat.StringJson:
                            sb.Append("StringJson16:");
                            goto UtfBom16;
                        case EventHeaderFieldFormat.StringUtfBom:
                            sb.Append("StringUtfBom16:");
                        UtfBom16:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto Utf16;
                            }
                            sb.Append(encodingFromBom.GetString(this.Bytes.Slice(encodingFromBom.Preamble.Length)));
                            break;
                        default:
                            sb.Append(this.Format.ToString());
                            goto case EventHeaderFieldFormat.StringUtf;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.StringUtf:
                            sb.Append("StringUtf16:");
                        Utf16:
                            sb.Append(this.ByteReader.FromBigEndian
                                ? Text.Encoding.BigEndianUnicode.GetString(this.Bytes)
                                : Text.Encoding.Unicode.GetString(this.Bytes));
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.ZStringChar32:
                    sb.Append('Z');
                    goto case EventHeaderFieldEncoding.StringLength16Char32;
                case EventHeaderFieldEncoding.StringLength16Char32:
                    switch (this.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexBytes32:");
                            PerfConvert.HexBytesAppend(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.StringXml:
                            sb.Append("StringXml32:");
                            goto UtfBom32;
                        case EventHeaderFieldFormat.StringJson:
                            sb.Append("StringJson32:");
                            goto UtfBom32;
                        case EventHeaderFieldFormat.StringUtfBom:
                            sb.Append("StringUtfBom32:");
                        UtfBom32:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto Utf32;
                            }
                            sb.Append(encodingFromBom.GetString(this.Bytes.Slice(encodingFromBom.Preamble.Length)));
                            break;
                        default:
                            sb.Append(this.Format.ToString());
                            goto case EventHeaderFieldFormat.StringUtf;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.StringUtf:
                            sb.Append("StringUtf32:");
                        Utf32:
                            sb.Append(this.ByteReader.FromBigEndian
                                ? PerfConvert.EncodingUTF32BE.GetString(this.Bytes)
                                : Text.Encoding.UTF32.GetString(this.Bytes));
                            break;
                    }
                    break;
            }

            return sb;
        }

        /// <summary>
        /// Returns a string representation of this value like "Type:Value" or "Type:Value1,Value2".
        /// </summary>
        public override string ToString()
        {
            return this.AppendAsString(new Text.StringBuilder()).ToString();
        }
    }
}
