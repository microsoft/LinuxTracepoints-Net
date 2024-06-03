// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Diagnostics;
    using BinaryPrimitives = System.Buffers.Binary.BinaryPrimitives;
    using StringBuilder = System.Text.StringBuilder;
    using Text = System.Text;

    /// <summary>
    /// Provides access to the metadata
    /// and content
    /// of a perf event item. An item is a field of the event or an element of an
    /// array field of the event.
    /// <br/>
    /// The item may represent one of the following, determined by the
    /// <c>Metadata.IsScalar</c> and <c>Metadata.TypeSize</c>
    /// properties:
    ///
    /// <list type="bullet">
    ///
    /// <item>
    /// <b>Simple scalar:</b> <c>IsScalar &amp;&amp; TypeSize != 0</c>
    /// <br/>
    /// Non-array field, or one element of an array field.
    /// Value type is simple (fixed-size value).
    /// <br/>
    /// <c>ElementCount</c> is always 1.
    /// <br/>
    /// <c>Format</c> is significant and <c>StructFieldCount</c> should be ignored
    /// (simple type is never <c>Struct</c>).
    /// <br/>
    /// <c>Bytes</c> contains the field's value and
    /// <c>Bytes.Length == TypeSize</c>,
    /// e.g. for a <c>Value32</c>, <c>TypeSize == 4</c> and <c>Bytes.Length == 4</c>.
    /// </item>
    ///
    /// <item>
    /// <b>Complex scalar:</b> <c>IsScalar &amp;&amp; TypeSize == 0</c>
    /// <br/>
    /// Non-array field, or one element of an array field.
    /// Value type is complex (variable-size or struct value).
    /// <br/>
    /// <c>ElementCount</c> is always 1.
    /// <br/>
    /// If <c>Encoding == Struct</c>, this is the beginning or end of a structure,
    /// <c>Format</c> should be ignored, and <c>StructFieldCount</c> is significant.
    /// Otherwise, this is a variable-length value, <c>Format</c> is significant,
    /// and <c>StructFieldCount</c> should be ignored.
    /// <br/>
    /// If <c>Encoding == Struct</c> then <c>Bytes</c> will be empty and you should use
    /// <see cref="EventHeaderEnumerator"/><c>.MoveNext()</c> to visit the struct's member fields.
    /// Otherwise, <c>Bytes</c> will contain the field's variable-length value without any length
    /// prefix or nul-termination suffix.
    /// </item>
    ///
    /// <item>
    /// <b>Simple array:</b> <c>!IsScalar &amp;&amp; TypeSize != 0</c>
    /// <br/>
    /// Array field (array-begin or array-end item).
    /// Array element type is simple (fixed-size element).
    /// <br/>
    /// <c>ElementCount</c> is the number of elements in the array.
    /// <br/>
    /// <c>Format</c> is significant and <c>StructFieldCount</c> should be ignored
    /// (simple type is never <c>Struct</c>).
    /// <br/>
    /// For array-end, <c>Bytes</c> will be empty.
    /// <br/>
    /// For array-begin, <c>Bytes</c> contains the field's values and
    /// <c>Bytes.Length == TypeSize * ElementCount</c>,
    /// e.g. for a <c>Value32</c>, <c>TypeSize == 4</c> and <c>Bytes.Length == 4 * ElementCount</c>.
    /// You may use <see cref="EventHeaderEnumerator"/><c>.MoveNext()</c> to visit the array elements,
    /// or you may process the array values directly and then use
    /// <see cref="EventHeaderEnumerator"/><c>.MoveNextSibling()</c> to skip the array elements.
    /// </item>
    ///
    /// <item>
    /// <b>Complex array:</b> <c>!IsScalar &amp;&amp; TypeSize == 0</c>
    /// <br/>
    /// Array field (array-begin or array-end item).
    /// Array element type is complex (variable-size or struct element).
    /// <br/>
    /// <c>ElementCount</c> is the number of elements in the array.
    /// <br/>
    /// If <c>Encoding == Struct</c>, this is the beginning or end of an array of structures,
    /// <c>Format</c> should be ignored, and <c>StructFieldCount</c> is significant.
    /// Otherwise, this is an array of variable-length values, <c>Format</c> is significant,
    /// and <c>StructFieldCount</c> should be ignored.
    /// <br/>
    /// <c>Bytes</c> will be empty. Use <see cref="EventHeaderEnumerator"/><c>.MoveNext()</c>
    /// to visit the array elements.
    /// </item>
    ///
    /// </list>
    /// </summary>
    public readonly ref struct PerfItemValue
    {
        /// <summary>
        /// Initializes a new instance of the PerfItemValue struct.
        /// These are not normally created directly. You'll normally get instances of this struct from
        /// <see cref="EventHeaderEnumerator"/><c>.GetItemInfo()</c> or
        /// <see cref="PerfFieldFormat"/><c>.GetFieldValue()</c>.
        /// </summary>
        public PerfItemValue(
            ReadOnlySpan<byte> bytes,
            PerfItemMetadata metadata)
        {
            this.Bytes = bytes;
            this.Metadata = metadata;

#if DEBUG
            // If type has known size, validate bytes.Length.
            if (metadata.TypeSize != 0 && !bytes.IsEmpty)
            {
                Debug.Assert(bytes.Length == metadata.ElementCount * metadata.TypeSize);
            }

            if (metadata.Encoding == EventHeaderFieldEncoding.Struct)
            {
                Debug.Assert(bytes.Length == 0);
            }
#endif
        }

        /// <summary>
        /// The content of this item, in event byte order.
        /// This may be empty for a complex item such as a struct, or an array
        /// of variable-size elements, in which case you must access the individual
        /// sub-items using the event's enumerator.
        /// </summary>
        public ReadOnlySpan<byte> Bytes { get; }

        /// <summary>
        /// The metadata of this item. Has properties such as ElementCount, FieldTag, TypeSize, Encoding, Format, etc.
        /// </summary>
        public PerfItemMetadata Metadata { get; }

        /// <summary>
        /// A PerfByteReader that can be used to fix the byte order of this value's data.
        /// This is the same as this.Metadata.ByteReader.
        /// </summary>
        public PerfByteReader ByteReader => this.Metadata.ByteReader;

        /// <summary>
        /// True if this value's data uses big-endian byte order.
        /// This is the same as this.Metadata.ByteReader.FromBigEndian.
        /// </summary>
        public bool FromBigEndian => this.Metadata.ByteReader.FromBigEndian;

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
            return PerfConvert.ReadGuidBigEndian(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Guid and returns the element at the specified index.
        /// </summary>
        public Guid GetGuid(int elementIndex)
        {
            const int SizeOfGuid = 16;
            return PerfConvert.ReadGuidBigEndian(this.Bytes.Slice(elementIndex * SizeOfGuid));
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
        /// Interprets the value as an array of Int32 and returns the first element.
        /// </summary>
        public Int32 GetUnixTime32()
        {
            return this.ByteReader.ReadI32(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Int32 and returns the element at the specified index.
        /// </summary>
        public Int32 GetUnixTime32(int elementIndex)
        {
            return this.ByteReader.ReadI32(this.Bytes.Slice(elementIndex * sizeof(Int32)));
        }

        /// <summary>
        /// Interprets the value as an array of Int32 and returns the first element,
        /// converted to a DateTime using PerfConvert.UnixTime32ToDateTime.
        /// </summary>
        public DateTime GetUnixTime32AsDateTime()
        {
            return PerfConvert.UnixTime32ToDateTime(
                this.ByteReader.ReadI32(this.Bytes));
        }

        /// <summary>
        /// Interprets the value as an array of Int32 and returns the element at the specified index,
        /// converted to a DateTime using PerfConvert.UnixTime32ToDateTime.
        /// </summary>
        public DateTime GetUnixTime32AsDateTime(int elementIndex)
        {
            return PerfConvert.UnixTime32ToDateTime(
                this.ByteReader.ReadI32(this.Bytes.Slice(elementIndex * sizeof(Int32))));
        }

        /// <summary>
        /// Interprets the value as an array of Int64 and returns the first element.
        /// </summary>
        public Int64 GetUnixTime64()
        {
            return this.ByteReader.ReadI64(this.Bytes);
        }

        /// <summary>
        /// Interprets the value as an array of Int64 and returns the element at the specified index.
        /// </summary>
        public Int64 GetUnixTime64(int elementIndex)
        {
            return this.ByteReader.ReadI64(this.Bytes.Slice(elementIndex * sizeof(Int64)));
        }

        /// <summary>
        /// Interprets the value as an array of Int64 and returns the first element,
        /// converted to a DateTime using PerfConvert.UnixTime64ToDateTime.
        /// Returns null if the value's year is outside the range 0001-9999.
        /// </summary>
        public DateTime? GetUnixTime64AsDateTime()
        {
            return PerfConvert.UnixTime64ToDateTime(
                this.ByteReader.ReadI64(this.Bytes));
        }

        /// <summary>
        /// Interprets the value as an array of Int64 and returns the element at the specified index,
        /// converted to a DateTime using PerfConvert.UnixTime64ToDateTime.
        /// Returns null if the value's year is outside the range 0001-9999.
        /// </summary>
        public DateTime? GetUnixTime64AsDateTime(int elementIndex)
        {
            return PerfConvert.UnixTime64ToDateTime(
                this.ByteReader.ReadI64(this.Bytes.Slice(elementIndex * sizeof(Int64))));
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
            switch (this.Metadata.Format)
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
                    switch (this.Metadata.Encoding)
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
        /// Interprets this as a scalar, converts it to a string, and appends it to sb.
        /// Returns sb.
        /// <br/>
        /// Requires TypeSize &lt;= Bytes.Length, Encoding != Struct, Encoding &lt; Max.
        /// <br/>
        /// For example:
        /// <list type="bullet"><item>
        /// If the value is a decimal integer or a finite float, appends a number 123 or -123.456.
        /// </item><item>
        /// If the value is a boolean, appends a bool false (for 0), true (for 1), a string like
        /// BOOL(-123) if convertOptions has BoolOutOfRangeAsString, or a string like -123 otherwise.
        /// </item><item>
        /// If the value is a string, control characters (char values 0..31) are
        /// filtered based on the flags in convertOptions (kept, replaced with space,
        /// or JSON-escaped).
        /// </item></list>
        /// </summary>
        public StringBuilder AppendScalarTo(
            StringBuilder sb,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            Debug.Assert(this.Metadata.TypeSize <= this.Bytes.Length);
            Text.Encoding? encodingFromBom;
            switch (this.Metadata.Encoding)
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventHeaderFieldEncoding.Struct:
                    throw new InvalidOperationException("Invalid encoding for AppendScalarTo.");
                case EventHeaderFieldEncoding.Invalid:
                    sb.Append("null");
                    break;
                case EventHeaderFieldEncoding.Value8:
                    Value8Append(sb, 0, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value16:
                    Value16Append(sb, 0, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value32:
                    Value32Append(sb, 0, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value64:
                    Value64Append(sb, 0, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value128:
                    Value128Append(sb, 0);
                    break;
                case EventHeaderFieldEncoding.ZStringChar8:
                case EventHeaderFieldEncoding.StringLength16Char8:
                    switch (this.Metadata.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            PerfConvert.HexBytesAppend(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.String8:
                            PerfConvert.StringLatin1AppendWithControlChars(sb, this.Bytes, convertOptions);
                            break;
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            PerfConvert.StringAppendWithControlChars(sb, this.Bytes.Slice(encodingFromBom.Preamble.Length), encodingFromBom, convertOptions);
                            break;
                        default:
                        case EventHeaderFieldFormat.StringUtf:
                            PerfConvert.StringAppendWithControlChars(sb, this.Bytes, Text.Encoding.UTF8, convertOptions);
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.ZStringChar16:
                case EventHeaderFieldEncoding.StringLength16Char16:
                    switch (this.Metadata.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            PerfConvert.HexBytesAppend(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            PerfConvert.StringAppendWithControlChars(sb, this.Bytes.Slice(encodingFromBom.Preamble.Length), encodingFromBom, convertOptions);
                            break;
                        default:
                        case EventHeaderFieldFormat.StringUtf:
                            PerfConvert.StringAppendWithControlChars(
                                sb,
                                this.Bytes,
                                this.ByteReader.FromBigEndian ? Text.Encoding.BigEndianUnicode : Text.Encoding.Unicode,
                                convertOptions);
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.ZStringChar32:
                case EventHeaderFieldEncoding.StringLength16Char32:
                    switch (this.Metadata.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            PerfConvert.HexBytesAppend(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            PerfConvert.StringAppendWithControlChars(sb, this.Bytes.Slice(encodingFromBom.Preamble.Length), encodingFromBom, convertOptions);
                            break;
                        default:
                        case EventHeaderFieldFormat.StringUtf:
                            PerfConvert.StringAppendWithControlChars(
                                sb,
                                this.Bytes,
                                this.ByteReader.FromBigEndian ? PerfConvert.EncodingUTF32BE : Text.Encoding.UTF32,
                                convertOptions);
                            break;
                    }
                    break;
            }

            return sb;
        }

        /// <summary>
        /// Interprets this as the beginning of an array of simple type.
        /// Converts the specified element of the array to a string and appends it to sb.
        /// Returns sb.
        /// <br/>
        /// Requires TypeSize != 0 (can only format fixed-length types).
        /// Requires elementIndex &lt;= Bytes.Length / TypeSize.
        /// <br/>
        /// The element is formatted as described for AppendScalarTo.
        /// </summary>
        public StringBuilder AppendSimpleElementTo(
            StringBuilder sb,
            int elementIndex,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            switch (this.Metadata.Encoding)
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventHeaderFieldEncoding.Struct:
                case EventHeaderFieldEncoding.ZStringChar8:
                case EventHeaderFieldEncoding.ZStringChar16:
                case EventHeaderFieldEncoding.ZStringChar32:
                case EventHeaderFieldEncoding.StringLength16Char8:
                case EventHeaderFieldEncoding.StringLength16Char16:
                case EventHeaderFieldEncoding.StringLength16Char32:
                    throw new InvalidOperationException("Invalid encoding for AppendSimpleElementTo.");
                case EventHeaderFieldEncoding.Invalid:
                    sb.Append("null");
                    break;
                case EventHeaderFieldEncoding.Value8:
                    Value8Append(sb, elementIndex, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value16:
                    Value16Append(sb, elementIndex, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value32:
                    Value32Append(sb, elementIndex, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value64:
                    Value64Append(sb, elementIndex, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value128:
                    Value128Append(sb, elementIndex);
                    break;
            }

            return sb;
        }

        /// <summary>
        /// Interprets this as the beginning of an array of simple type.
        /// Converts this to a comma-separated list of items and appends it to sb.
        /// Returns sb.
        /// <br/>
        /// Requires TypeSize != 0 (can only format fixed-length types).
        /// <br/>
        /// Each array element is formatted as described for AppendScalarTo.
        /// </summary>
        public StringBuilder AppendSimpleArrayTo(
            StringBuilder sb,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            Debug.Assert(this.Metadata.TypeSize > 0);
            string separator = convertOptions.HasFlag(PerfConvertOptions.Space) ? ", " : ",";

            int count;
            switch (this.Metadata.Encoding)
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventHeaderFieldEncoding.Struct:
                case EventHeaderFieldEncoding.ZStringChar8:
                case EventHeaderFieldEncoding.ZStringChar16:
                case EventHeaderFieldEncoding.ZStringChar32:
                case EventHeaderFieldEncoding.StringLength16Char8:
                case EventHeaderFieldEncoding.StringLength16Char16:
                case EventHeaderFieldEncoding.StringLength16Char32:
                    throw new InvalidOperationException("Invalid encoding for AppendSimpleArrayTo.");
                case EventHeaderFieldEncoding.Value8:
                    count = this.Bytes.Length;
                    switch (this.Metadata.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UInt32DecimalAppend(sb, this.GetU8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.Int32DecimalAppend(sb, this.GetI8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UInt32HexAppend(sb, this.GetU8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UInt32HexAppend(sb, this.GetU8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.String8:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.Char16AppendWithControlChars(sb, (char)this.GetU8(i), convertOptions);
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value16:
                    count = this.Bytes.Length / sizeof(UInt16);
                    switch (this.Metadata.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UInt32DecimalAppend(sb, this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.Int32DecimalAppend(sb, this.GetI16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UInt32HexAppend(sb, this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UInt32HexAppend(sb, this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.StringUtf:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.Char16AppendWithControlChars(sb, (char)this.GetU16(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Port:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UInt32DecimalAppend(sb, this.GetPort(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value32:
                    count = this.Bytes.Length / sizeof(UInt32);
                    switch (this.Metadata.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UInt32DecimalAppend(sb, this.GetU32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                        case EventHeaderFieldFormat.Pid:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.Int32DecimalAppend(sb, this.GetI32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UInt32HexAppend(sb, this.GetU32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Errno:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.ErrnoAppend(sb, this.GetI32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Time:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UnixTime32Append(sb, this.GetI32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.BooleanAppend(sb, this.GetU32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Float:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.Float32Append(sb, this.GetF32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.StringUtf:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.Char32AppendWithControlChars(sb, this.GetU32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.IPv4:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.IPv4Append(sb, this.GetIPv4(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value64:
                    count = this.Bytes.Length / sizeof(UInt64);
                    switch (this.Metadata.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UInt64DecimalAppend(sb, this.GetU64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.Int64DecimalAppend(sb, this.GetI64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UInt64HexAppend(sb, this.GetU64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Time:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.UnixTime64Append(sb, this.GetI64(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Float:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.Float64Append(sb, this.GetF64(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan64(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value128:
                    count = this.Bytes.Length / 16;
                    switch (this.Metadata.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan128(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Uuid:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.GuidAppend(sb, this.GetGuid(i));
                            }
                            break;
                        case EventHeaderFieldFormat.IPv6:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(separator);
                                PerfConvert.IPv6Append(sb, this.GetIPv6(i));
                            }
                            break;
                    }
                    break;
            }

            return sb;
        }

        /// <summary>
        /// Converts this to a JSON value and appends it to sb. Returns sb.
        /// <br/>
        /// Cannot be used for struct or complex array.
        /// <br/>
        /// For example:
        /// <list type="bullet"><item>
        /// If the value is a scalar decimal integer or a finite float, appends a JSON number
        /// like <c>123</c> or <c>-123.456</c>.
        /// </item><item>
        /// If the value is a scalar boolean, appends a bool <c>false</c> (for 0), <c>true</c> (for 1),
        /// a string like <c>"BOOL(-123)"</c> if convertOptions has BoolOutOfRangeAsString, or a number
        /// like <c>-123</c> otherwise.
        /// </item><item>
        /// If the value is a scalar string, appends a JSON-escaped string like <c>"abc\nxyz"</c>.
        /// </item><item>
        /// If the value is a simple array, appends a JSON array like <c>[ 1, 2, 4, 8 ]</c>.
        /// </item></list>
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// Encoding is not recognized.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Item is a struct.
        /// <br/>
        /// OR
        /// <br/>
        /// Item is an array and encoding is a complex type (variable-length or struct).
        /// </exception>
        public StringBuilder AppendJsonTo(
            StringBuilder sb,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            return this.Metadata.IsScalar
                ? this.AppendJsonScalarTo(sb, convertOptions)
                : this.AppendJsonSimpleArrayTo(sb, convertOptions);
        }

        /// <summary>
        /// Interprets this as a scalar, converts it to a JSON value, and appends it to sb.
        /// Returns sb.
        /// <br/>
        /// Requires TypeSize &lt;= Bytes.Length, Encoding != Struct, Encoding &lt; Max.
        /// <br/>
        /// For example:
        /// <list type="bullet"><item>
        /// If the value is a decimal integer or a finite float, appends a JSON number
        /// like <c>123</c> or <c>-123.456</c>.
        /// </item><item>
        /// If the value is a boolean, appends a bool <c>false</c> (for 0), <c>true</c> (for 1),
        /// a string like <c>"BOOL(-123)"</c> if convertOptions has BoolOutOfRangeAsString, or a number
        /// like <c>-123</c> otherwise.
        /// </item><item>
        /// If the value is a string, appends a JSON-escaped string like <c>"abc\nxyz"</c>.
        /// </item></list>
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// Encoding is not recognized.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Item is a struct.
        /// </exception>
        public StringBuilder AppendJsonScalarTo(
            StringBuilder sb,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            Debug.Assert(this.Metadata.TypeSize <= this.Bytes.Length);
            Text.Encoding? encodingFromBom;
            switch (this.Metadata.Encoding)
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventHeaderFieldEncoding.Struct:
                    throw new InvalidOperationException("Invalid encoding for AppendJsonScalarTo.");
                case EventHeaderFieldEncoding.Invalid:
                    sb.Append("null");
                    break;
                case EventHeaderFieldEncoding.Value8:
                    Value8AppendJson(sb, 0, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value16:
                    Value16AppendJson(sb, 0, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value32:
                    Value32AppendJson(sb, 0, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value64:
                    Value64AppendJson(sb, 0, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value128:
                    Value128AppendJson(sb, 0);
                    break;
                case EventHeaderFieldEncoding.ZStringChar8:
                case EventHeaderFieldEncoding.StringLength16Char8:
                    switch (this.Metadata.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            HexBytesAppendJson(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.String8:
                            PerfConvert.StringLatin1AppendJson(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            PerfConvert.StringAppendJson(sb, this.Bytes.Slice(encodingFromBom.Preamble.Length), encodingFromBom);
                            break;
                        default:
                        case EventHeaderFieldFormat.StringUtf:
                            PerfConvert.StringAppendJson(sb, this.Bytes, Text.Encoding.UTF8);
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.ZStringChar16:
                case EventHeaderFieldEncoding.StringLength16Char16:
                    switch (this.Metadata.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            HexBytesAppendJson(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            PerfConvert.StringAppendJson(sb, this.Bytes.Slice(encodingFromBom.Preamble.Length), encodingFromBom);
                            break;
                        default:
                        case EventHeaderFieldFormat.StringUtf:
                            PerfConvert.StringAppendJson(
                                sb,
                                this.Bytes,
                                this.ByteReader.FromBigEndian ? Text.Encoding.BigEndianUnicode : Text.Encoding.Unicode);
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.ZStringChar32:
                case EventHeaderFieldEncoding.StringLength16Char32:
                    switch (this.Metadata.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            HexBytesAppendJson(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.StringUtfBom:
                        case EventHeaderFieldFormat.StringXml:
                        case EventHeaderFieldFormat.StringJson:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto case EventHeaderFieldFormat.StringUtf;
                            }
                            PerfConvert.StringAppendJson(sb, this.Bytes.Slice(encodingFromBom.Preamble.Length), encodingFromBom);
                            break;
                        default:
                        case EventHeaderFieldFormat.StringUtf:
                            PerfConvert.StringAppendJson(
                                sb,
                                this.Bytes,
                                this.ByteReader.FromBigEndian ? PerfConvert.EncodingUTF32BE : Text.Encoding.UTF32);
                            break;
                    }
                    break;
            }

            return sb;
        }

        /// <summary>
        /// Interprets this as the beginning of an array of simple type.
        /// Converts the specified element of the array to a JSON value and appends it to sb.
        /// Returns sb.
        /// <br/>
        /// Requires TypeSize != 0 (can only format fixed-length types).
        /// Requires elementIndex &lt;= Bytes.Length / TypeSize.
        /// <br/>
        /// The element is formatted as described for AppendJsonScalarTo.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// Encoding is not recognized.
        /// </exception>
        public StringBuilder AppendJsonSimpleElementTo(
            StringBuilder sb,
            int elementIndex,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            switch (this.Metadata.Encoding)
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventHeaderFieldEncoding.Struct:
                case EventHeaderFieldEncoding.ZStringChar8:
                case EventHeaderFieldEncoding.ZStringChar16:
                case EventHeaderFieldEncoding.ZStringChar32:
                case EventHeaderFieldEncoding.StringLength16Char8:
                case EventHeaderFieldEncoding.StringLength16Char16:
                case EventHeaderFieldEncoding.StringLength16Char32:
                    throw new InvalidOperationException("Invalid encoding for AppendJsonSimpleElementTo.");
                case EventHeaderFieldEncoding.Invalid:
                    sb.Append("null");
                    break;
                case EventHeaderFieldEncoding.Value8:
                    Value8AppendJson(sb, elementIndex, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value16:
                    Value16AppendJson(sb, elementIndex, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value32:
                    Value32AppendJson(sb, elementIndex, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value64:
                    Value64AppendJson(sb, elementIndex, convertOptions);
                    break;
                case EventHeaderFieldEncoding.Value128:
                    Value128AppendJson(sb, elementIndex);
                    break;
            }

            return sb;
        }

        /// <summary>
        /// Interprets this as the beginning of an array of simple type.
        /// Converts this to a JSON array and appends it to sb.
        /// Returns sb.
        /// <br/>
        /// Requires TypeSize != 0 (can only format fixed-length types).
        /// <br/>
        /// Each array element is formatted as described for AppendJsonScalarTo.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// Encoding is not recognized.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Encoding is a complex type (variable-length or struct).
        /// </exception>
        public StringBuilder AppendJsonSimpleArrayTo(
            StringBuilder sb,
            PerfConvertOptions convertOptions = PerfConvertOptions.Default)
        {
            Debug.Assert(this.Metadata.TypeSize > 0);
            bool space = convertOptions.HasFlag(PerfConvertOptions.Space);

            int count;
            switch (this.Metadata.Encoding)
            {
                default:
                    throw new NotSupportedException("Unknown encoding.");
                case EventHeaderFieldEncoding.Struct:
                case EventHeaderFieldEncoding.ZStringChar8:
                case EventHeaderFieldEncoding.ZStringChar16:
                case EventHeaderFieldEncoding.ZStringChar32:
                case EventHeaderFieldEncoding.StringLength16Char8:
                case EventHeaderFieldEncoding.StringLength16Char16:
                case EventHeaderFieldEncoding.StringLength16Char32:
                    throw new InvalidOperationException("Invalid encoding for AppendJsonSimpleArrayTo.");
                case EventHeaderFieldEncoding.Value8:
                    sb.Append('[');
                    count = this.Bytes.Length;
                    switch (this.Metadata.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.UInt32DecimalAppend(sb, this.GetU8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.Int32DecimalAppend(sb, this.GetI8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.UInt32HexAppendJson(sb, this.GetU8(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.BooleanAppendJson(sb, this.GetU8(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                HexBytesAppendJson(sb, this.GetSpan8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.String8:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                Char16AppendJson(sb, this.GetU8(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value16:
                    sb.Append('[');
                    count = this.Bytes.Length / sizeof(UInt16);
                    switch (this.Metadata.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.UInt32DecimalAppend(sb, this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.Int32DecimalAppend(sb, this.GetI16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.UInt32HexAppendJson(sb, this.GetU16(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.BooleanAppendJson(sb, this.GetU16(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                HexBytesAppendJson(sb, this.GetSpan16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.StringUtf:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                Char16AppendJson(sb, this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Port:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.UInt32DecimalAppend(sb, this.GetPort(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value32:
                    sb.Append('[');
                    count = this.Bytes.Length / sizeof(UInt32);
                    switch (this.Metadata.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.UInt32DecimalAppend(sb, this.GetU32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                        case EventHeaderFieldFormat.Pid:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.Int32DecimalAppend(sb, this.GetI32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.UInt32HexAppendJson(sb, this.GetU32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Errno:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.ErrnoAppendJson(sb, this.GetI32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Time:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.UnixTime32AppendJson(sb, this.GetI32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.BooleanAppendJson(sb, this.GetU32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Float:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.Float32AppendJson(sb, this.GetF32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                HexBytesAppendJson(sb, this.GetSpan32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.StringUtf:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                Char32AppendJson(sb, this.GetU32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.IPv4:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                IPv4AppendJson(sb, this.GetIPv4(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value64:
                    sb.Append('[');
                    count = this.Bytes.Length / sizeof(UInt64);
                    switch (this.Metadata.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.UInt64DecimalAppend(sb, this.GetU64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.Int64DecimalAppend(sb, this.GetI64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.UInt64HexAppendJson(sb, this.GetU64(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Time:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.UnixTime64AppendJson(sb, this.GetI64(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Float:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                PerfConvert.Float64AppendJson(sb, this.GetF64(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                HexBytesAppendJson(sb, this.GetSpan64(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value128:
                    sb.Append('[');
                    count = this.Bytes.Length / 16;
                    switch (this.Metadata.Format)
                    {
                        default:
                        case EventHeaderFieldFormat.HexBytes:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                HexBytesAppendJson(sb, this.GetSpan128(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Uuid:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                GuidAppendJson(sb, this.GetGuid(i));
                            }
                            break;
                        case EventHeaderFieldFormat.IPv6:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(',');
                                if (space) sb.Append(' ');
                                IPv6AppendJson(sb, this.GetIPv6(i));
                            }
                            break;
                    }
                    break;
            }

            if (space) sb.Append(' ');
            return sb.Append(']');
        }

        /// <summary>
        /// Appends a string representation of this value (suitable for diagnostic use) like
        /// "Type:Value" or "Type:Value1,Value2". Result is the same as from this.ToString(convertOptions).
        /// Returns sb.
        /// </summary>
        public StringBuilder AppendTo(
            StringBuilder sb)
        {
            const PerfConvertOptions convertOptions =
                PerfConvertOptions.Space |
                PerfConvertOptions.FloatNonFiniteAsString |
                PerfConvertOptions.IntHexAsString |
                PerfConvertOptions.UnixTimeWithinRangeAsString |
                PerfConvertOptions.ErrnoKnownAsString |
                PerfConvertOptions.StringControlCharsJsonEscape;
            const string Separator = ", ";
            int count;
            Text.Encoding? encodingFromBom;
            var baseEncoding = this.Metadata.Encoding;
            switch (baseEncoding)
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
                    sb.Append(": ");
                    PerfConvert.HexBytesAppend(sb, this.Bytes);
                    break;
                case EventHeaderFieldEncoding.Value8:
                    count = this.Bytes.Length / sizeof(Byte);
                    switch (this.Metadata.Format)
                    {
                        default:
                            sb.Append(this.Metadata.Format.ToString());
                            goto case EventHeaderFieldFormat.UnsignedInt;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            sb.Append("UInt8: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UInt32DecimalAppend(sb, this.GetU8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            sb.Append("Int8: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.Int32DecimalAppend(sb, this.GetI8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            sb.Append("Hex8: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UInt32HexAppend(sb, this.GetU8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            sb.Append("Bool8: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.BooleanAppend(sb, this.GetU8(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexByte8: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan8(i));
                            }
                            break;
                        case EventHeaderFieldFormat.String8:
                            sb.Append("Char8: ");
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
                    switch (this.Metadata.Format)
                    {
                        default:
                            sb.Append(this.Metadata.Format.ToString());
                            goto case EventHeaderFieldFormat.UnsignedInt;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            sb.Append("UInt16: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UInt32DecimalAppend(sb, this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            sb.Append("Int16: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.Int32DecimalAppend(sb, this.GetI16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            sb.Append("Hex16: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UInt32HexAppend(sb, this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            sb.Append("Bool16: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.BooleanAppend(sb, this.GetU16(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexByte16: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.StringUtf:
                            sb.Append("Char16: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                sb.Append((char)this.GetU16(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Port:
                            sb.Append("Port: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UInt32DecimalAppend(sb, this.GetPort(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.Value32:
                    count = this.Bytes.Length / sizeof(UInt32);
                    switch (this.Metadata.Format)
                    {
                        default:
                            sb.Append(this.Metadata.Format.ToString());
                            goto case EventHeaderFieldFormat.UnsignedInt;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            sb.Append("UInt32: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UInt32DecimalAppend(sb, this.GetU32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Pid:
                            sb.Append("Pid: ");
                            goto SignedInt32;
                        case EventHeaderFieldFormat.SignedInt:
                            sb.Append("Int32: ");
                        SignedInt32:
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.Int32DecimalAppend(sb, this.GetI32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            sb.Append("Hex32: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UInt32HexAppend(sb, this.GetU32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Errno:
                            sb.Append("Errno: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.ErrnoAppend(sb, this.GetI32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Time:
                            sb.Append("Time32: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UnixTime32Append(sb, this.GetI32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Boolean:
                            sb.Append("Bool32: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.BooleanAppend(sb, this.GetU32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Float:
                            sb.Append("Float32: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.Float32Append(sb, this.GetF32(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexByte32: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.StringUtf:
                            sb.Append("Char32: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.Char32Append(sb, this.GetU32(i));
                            }
                            break;
                        case EventHeaderFieldFormat.IPv4:
                            sb.Append("IPv4: ");
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
                    switch (this.Metadata.Format)
                    {
                        default:
                            sb.Append(this.Metadata.Format.ToString());
                            goto case EventHeaderFieldFormat.UnsignedInt;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.UnsignedInt:
                            sb.Append("UInt64: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UInt64DecimalAppend(sb, this.GetU64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.SignedInt:
                            sb.Append("Int64: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.Int64DecimalAppend(sb, this.GetI64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.HexInt:
                            sb.Append("Hex64: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UInt64HexAppend(sb, this.GetU64(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Time:
                            sb.Append("Time64: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.UnixTime64Append(sb, this.GetI64(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.Float:
                            sb.Append("Float64: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.Float64Append(sb, this.GetF64(i), convertOptions);
                            }
                            break;
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexByte64: ");
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
                    switch (this.Metadata.Format)
                    {
                        default:
                            sb.Append(this.Metadata.Format.ToString());
                            goto case EventHeaderFieldFormat.HexBytes;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexByte128: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.HexBytesAppend(sb, this.GetSpan128(i));
                            }
                            break;
                        case EventHeaderFieldFormat.Uuid:
                            sb.Append("Guid: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                sb.Append(PerfConvert.ReadGuidBigEndian(this.GetSpan128(i)).ToString());
                            }
                            break;
                        case EventHeaderFieldFormat.IPv6:
                            sb.Append("IPv6: ");
                            for (int i = 0; i < count; i += 1)
                            {
                                if (i != 0) sb.Append(Separator);
                                PerfConvert.IPv6Append(sb, this.GetSpan128(i));
                            }
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.ZStringChar8:
                    sb.Append('Z');
                    goto case EventHeaderFieldEncoding.StringLength16Char8;
                case EventHeaderFieldEncoding.StringLength16Char8:
                    switch (this.Metadata.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexBytes8: ");
                            PerfConvert.HexBytesAppend(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.String8:
                            sb.Append("String8: ");
                            PerfConvert.StringLatin1AppendWithControlChars(sb, this.Bytes, convertOptions);
                            break;
                        case EventHeaderFieldFormat.StringXml:
                            sb.Append("StringXml8: ");
                            goto UtfBom8;
                        case EventHeaderFieldFormat.StringJson:
                            sb.Append("StringJson8: ");
                            goto UtfBom8;
                        case EventHeaderFieldFormat.StringUtfBom:
                            sb.Append("StringUtfBom8: ");
                        UtfBom8:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto Utf8;
                            }
                            PerfConvert.StringAppendWithControlChars(sb, this.Bytes.Slice(encodingFromBom.Preamble.Length), encodingFromBom, convertOptions);
                            break;
                        default:
                            sb.Append(this.Metadata.Format.ToString());
                            goto case EventHeaderFieldFormat.StringUtf;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.StringUtf:
                            sb.Append("StringUtf8: ");
                        Utf8:
                            PerfConvert.StringAppendWithControlChars(sb, this.Bytes, Text.Encoding.UTF8, convertOptions);
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.ZStringChar16:
                    sb.Append('Z');
                    goto case EventHeaderFieldEncoding.StringLength16Char16;
                case EventHeaderFieldEncoding.StringLength16Char16:
                    switch (this.Metadata.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexBytes16: ");
                            PerfConvert.HexBytesAppend(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.StringXml:
                            sb.Append("StringXml16: ");
                            goto UtfBom16;
                        case EventHeaderFieldFormat.StringJson:
                            sb.Append("StringJson16: ");
                            goto UtfBom16;
                        case EventHeaderFieldFormat.StringUtfBom:
                            sb.Append("StringUtfBom16: ");
                        UtfBom16:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto Utf16;
                            }
                            PerfConvert.StringAppendWithControlChars(sb, this.Bytes.Slice(encodingFromBom.Preamble.Length), encodingFromBom, convertOptions);
                            break;
                        default:
                            sb.Append(this.Metadata.Format.ToString());
                            goto case EventHeaderFieldFormat.StringUtf;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.StringUtf:
                            sb.Append("StringUtf16: ");
                        Utf16:
                            PerfConvert.StringAppendWithControlChars(
                                sb,
                                this.Bytes,
                                this.ByteReader.FromBigEndian ? Text.Encoding.BigEndianUnicode : Text.Encoding.Unicode,
                                convertOptions);
                            break;
                    }
                    break;
                case EventHeaderFieldEncoding.ZStringChar32:
                    sb.Append('Z');
                    goto case EventHeaderFieldEncoding.StringLength16Char32;
                case EventHeaderFieldEncoding.StringLength16Char32:
                    switch (this.Metadata.Format)
                    {
                        case EventHeaderFieldFormat.HexBytes:
                            sb.Append("HexBytes32: ");
                            PerfConvert.HexBytesAppend(sb, this.Bytes);
                            break;
                        case EventHeaderFieldFormat.StringXml:
                            sb.Append("StringXml32: ");
                            goto UtfBom32;
                        case EventHeaderFieldFormat.StringJson:
                            sb.Append("StringJson32: ");
                            goto UtfBom32;
                        case EventHeaderFieldFormat.StringUtfBom:
                            sb.Append("StringUtfBom32: ");
                        UtfBom32:
                            encodingFromBom = PerfConvert.EncodingFromBom(this.Bytes);
                            if (encodingFromBom == null)
                            {
                                goto Utf32;
                            }
                            PerfConvert.StringAppendWithControlChars(sb, this.Bytes.Slice(encodingFromBom.Preamble.Length), encodingFromBom, convertOptions);
                            break;
                        default:
                            sb.Append(this.Metadata.Format.ToString());
                            goto case EventHeaderFieldFormat.StringUtf;
                        case EventHeaderFieldFormat.Default:
                        case EventHeaderFieldFormat.StringUtf:
                            sb.Append("StringUtf32: ");
                        Utf32:
                            PerfConvert.StringAppendWithControlChars(
                                sb,
                                this.Bytes,
                                this.ByteReader.FromBigEndian ? PerfConvert.EncodingUTF32BE : Text.Encoding.UTF32,
                                convertOptions);
                            break;
                    }
                    break;
            }

            return sb;
        }

        /// <summary>
        /// Returns a string representation of this value suitable for diagnostic use like
        /// "Type:Value" or "Type:Value1,Value2". Result is the same as from this.AppendTo().
        /// </summary>
        public override string ToString()
        {
            return this.AppendTo(new StringBuilder()).ToString();
        }

        private void Value8Append(StringBuilder sb, int elementIndex, PerfConvertOptions convertOptions)
        {
            switch (this.Metadata.Format)
            {
                default:
                case EventHeaderFieldFormat.UnsignedInt:
                    PerfConvert.UInt32DecimalAppend(sb, this.GetU8(elementIndex));
                    break;
                case EventHeaderFieldFormat.SignedInt:
                    PerfConvert.Int32DecimalAppend(sb, this.GetI8(elementIndex));
                    break;
                case EventHeaderFieldFormat.HexInt:
                    PerfConvert.UInt32HexAppend(sb, this.GetU8(elementIndex));
                    break;
                case EventHeaderFieldFormat.Boolean:
                    PerfConvert.BooleanAppend(sb, this.GetU8(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.HexBytes:
                    PerfConvert.HexBytesAppend(sb, this.GetSpan8(elementIndex));
                    break;
                case EventHeaderFieldFormat.String8:
                    PerfConvert.Char16AppendWithControlChars(sb, (char)this.GetU8(elementIndex), convertOptions);
                    break;
            }
        }

        private void Value16Append(StringBuilder sb, int elementIndex, PerfConvertOptions convertOptions)
        {
            switch (this.Metadata.Format)
            {
                default:
                case EventHeaderFieldFormat.UnsignedInt:
                    PerfConvert.UInt32DecimalAppend(sb, this.GetU16(elementIndex));
                    break;
                case EventHeaderFieldFormat.SignedInt:
                    PerfConvert.Int32DecimalAppend(sb, this.GetI16(elementIndex));
                    break;
                case EventHeaderFieldFormat.HexInt:
                    PerfConvert.UInt32HexAppend(sb, this.GetU16(elementIndex));
                    break;
                case EventHeaderFieldFormat.Boolean:
                    PerfConvert.BooleanAppend(sb, this.GetU16(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.HexBytes:
                    PerfConvert.HexBytesAppend(sb, this.GetSpan16(elementIndex));
                    break;
                case EventHeaderFieldFormat.StringUtf:
                    PerfConvert.Char16AppendWithControlChars(sb, (char)this.GetU16(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Port:
                    PerfConvert.UInt32DecimalAppend(sb, this.GetPort(elementIndex));
                    break;
            }
        }

        private void Value32Append(StringBuilder sb, int elementIndex, PerfConvertOptions convertOptions)
        {
            switch (this.Metadata.Format)
            {
                default:
                case EventHeaderFieldFormat.UnsignedInt:
                    PerfConvert.UInt32DecimalAppend(sb, this.GetU32(elementIndex));
                    break;
                case EventHeaderFieldFormat.SignedInt:
                case EventHeaderFieldFormat.Pid:
                    PerfConvert.Int32DecimalAppend(sb, this.GetI32(elementIndex));
                    break;
                case EventHeaderFieldFormat.HexInt:
                    PerfConvert.UInt32HexAppend(sb, this.GetU32(elementIndex));
                    break;
                case EventHeaderFieldFormat.Errno:
                    PerfConvert.ErrnoAppend(sb, this.GetI32(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Time:
                    PerfConvert.UnixTime32Append(sb, this.GetI32(elementIndex));
                    break;
                case EventHeaderFieldFormat.Boolean:
                    PerfConvert.BooleanAppend(sb, this.GetU32(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Float:
                    PerfConvert.Float32Append(sb, this.GetF32(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.HexBytes:
                    PerfConvert.HexBytesAppend(sb, this.GetSpan32(elementIndex));
                    break;
                case EventHeaderFieldFormat.StringUtf:
                    PerfConvert.Char32AppendWithControlChars(sb, this.GetU32(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.IPv4:
                    PerfConvert.IPv4Append(sb, this.GetIPv4(elementIndex));
                    break;
            }
        }

        private void Value64Append(StringBuilder sb, int elementIndex, PerfConvertOptions convertOptions)
        {
            switch (this.Metadata.Format)
            {
                default:
                case EventHeaderFieldFormat.UnsignedInt:
                    PerfConvert.UInt64DecimalAppend(sb, this.GetU64(elementIndex));
                    break;
                case EventHeaderFieldFormat.SignedInt:
                    PerfConvert.Int64DecimalAppend(sb, this.GetI64(elementIndex));
                    break;
                case EventHeaderFieldFormat.HexInt:
                    PerfConvert.UInt64HexAppend(sb, this.GetU64(elementIndex));
                    break;
                case EventHeaderFieldFormat.Time:
                    PerfConvert.UnixTime64Append(sb, this.GetI64(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Float:
                    PerfConvert.Float64Append(sb, this.GetF64(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.HexBytes:
                    PerfConvert.HexBytesAppend(sb, this.GetSpan64(elementIndex));
                    break;
            }
        }

        private void Value128Append(StringBuilder sb, int elementIndex)
        {
            switch (this.Metadata.Format)
            {
                default:
                case EventHeaderFieldFormat.HexBytes:
                    PerfConvert.HexBytesAppend(sb, this.GetSpan128(elementIndex));
                    break;
                case EventHeaderFieldFormat.Uuid:
                    PerfConvert.GuidAppend(sb, this.GetGuid(elementIndex));
                    break;
                case EventHeaderFieldFormat.IPv6:
                    PerfConvert.IPv6Append(sb, this.GetIPv6(elementIndex));
                    break;
            }
        }

        private void Value8AppendJson(StringBuilder sb, int elementIndex, PerfConvertOptions convertOptions)
        {
            switch (this.Metadata.Format)
            {
                default:
                case EventHeaderFieldFormat.UnsignedInt:
                    PerfConvert.UInt32DecimalAppend(sb, this.GetU8(elementIndex));
                    break;
                case EventHeaderFieldFormat.SignedInt:
                    PerfConvert.Int32DecimalAppend(sb, this.GetI8(elementIndex));
                    break;
                case EventHeaderFieldFormat.HexInt:
                    PerfConvert.UInt32HexAppendJson(sb, this.GetU8(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Boolean:
                    PerfConvert.BooleanAppendJson(sb, this.GetU8(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.HexBytes:
                    HexBytesAppendJson(sb, this.GetSpan8(elementIndex));
                    break;
                case EventHeaderFieldFormat.String8:
                    Char16AppendJson(sb, this.GetU8(elementIndex));
                    break;
            }
        }

        private void Value16AppendJson(StringBuilder sb, int elementIndex, PerfConvertOptions convertOptions)
        {
            switch (this.Metadata.Format)
            {
                default:
                case EventHeaderFieldFormat.UnsignedInt:
                    PerfConvert.UInt32DecimalAppend(sb, this.GetU16(elementIndex));
                    break;
                case EventHeaderFieldFormat.SignedInt:
                    PerfConvert.Int32DecimalAppend(sb, this.GetI16(elementIndex));
                    break;
                case EventHeaderFieldFormat.HexInt:
                    PerfConvert.UInt32HexAppendJson(sb, this.GetU16(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Boolean:
                    PerfConvert.BooleanAppendJson(sb, this.GetU16(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.HexBytes:
                    HexBytesAppendJson(sb, this.GetSpan16(elementIndex));
                    break;
                case EventHeaderFieldFormat.StringUtf:
                    Char16AppendJson(sb, this.GetU16(elementIndex));
                    break;
                case EventHeaderFieldFormat.Port:
                    PerfConvert.UInt32DecimalAppend(sb, this.GetPort(elementIndex));
                    break;
            }
        }

        private void Value32AppendJson(StringBuilder sb, int elementIndex, PerfConvertOptions convertOptions)
        {
            switch (this.Metadata.Format)
            {
                default:
                case EventHeaderFieldFormat.UnsignedInt:
                    PerfConvert.UInt32DecimalAppend(sb, this.GetU32(elementIndex));
                    break;
                case EventHeaderFieldFormat.SignedInt:
                case EventHeaderFieldFormat.Pid:
                    PerfConvert.Int32DecimalAppend(sb, this.GetI32(elementIndex));
                    break;
                case EventHeaderFieldFormat.HexInt:
                    PerfConvert.UInt32HexAppendJson(sb, this.GetU32(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Errno:
                    PerfConvert.ErrnoAppendJson(sb, this.GetI32(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Time:
                    PerfConvert.UnixTime32AppendJson(sb, this.GetI32(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Boolean:
                    PerfConvert.BooleanAppendJson(sb, this.GetU32(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Float:
                    PerfConvert.Float32AppendJson(sb, this.GetF32(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.HexBytes:
                    HexBytesAppendJson(sb, this.GetSpan32(elementIndex));
                    break;
                case EventHeaderFieldFormat.StringUtf:
                    Char32AppendJson(sb, this.GetU32(elementIndex));
                    break;
                case EventHeaderFieldFormat.IPv4:
                    IPv4AppendJson(sb, this.GetIPv4(elementIndex));
                    break;
            }
        }

        private void Value64AppendJson(StringBuilder sb, int elementIndex, PerfConvertOptions convertOptions)
        {
            switch (this.Metadata.Format)
            {
                default:
                case EventHeaderFieldFormat.UnsignedInt:
                    PerfConvert.UInt64DecimalAppend(sb, this.GetU64(elementIndex));
                    break;
                case EventHeaderFieldFormat.SignedInt:
                    PerfConvert.Int64DecimalAppend(sb, this.GetI64(elementIndex));
                    break;
                case EventHeaderFieldFormat.HexInt:
                    PerfConvert.UInt64HexAppendJson(sb, this.GetU64(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Time:
                    PerfConvert.UnixTime64AppendJson(sb, this.GetI64(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.Float:
                    PerfConvert.Float64AppendJson(sb, this.GetF64(elementIndex), convertOptions);
                    break;
                case EventHeaderFieldFormat.HexBytes:
                    HexBytesAppendJson(sb, this.GetSpan64(elementIndex));
                    break;
            }
        }

        private void Value128AppendJson(StringBuilder sb, int elementIndex)
        {
            switch (this.Metadata.Format)
            {
                default:
                case EventHeaderFieldFormat.HexBytes:
                    HexBytesAppendJson(sb, this.GetSpan128(elementIndex));
                    break;
                case EventHeaderFieldFormat.Uuid:
                    GuidAppendJson(sb, this.GetGuid(elementIndex));
                    break;
                case EventHeaderFieldFormat.IPv6:
                    IPv6AppendJson(sb, this.GetIPv6(elementIndex));
                    break;
            }
        }

        private static void GuidAppendJson(StringBuilder sb, in Guid value)
        {
            sb.Append('"');
            PerfConvert.GuidAppend(sb, value);
            sb.Append('"');
        }

        private static void IPv6AppendJson(StringBuilder sb, ReadOnlySpan<byte> bytes)
        {
            sb.Append('"');
            PerfConvert.IPv6Append(sb, bytes);
            sb.Append('"');
        }

        private static void HexBytesAppendJson(StringBuilder sb, ReadOnlySpan<byte> bytes)
        {
            sb.Append('"');
            PerfConvert.HexBytesAppend(sb, bytes);
            sb.Append('"');
        }

        private static void IPv4AppendJson(StringBuilder sb, UInt32 value)
        {
            sb.Append('"');
            PerfConvert.IPv4Append(sb, value);
            sb.Append('"');
        }

        private static void Char16AppendJson(StringBuilder sb, UInt16 value)
        {
            sb.Append('"');
            PerfConvert.Char16AppendWithControlCharsJsonEscape(sb, (char)value);
            sb.Append('"');
        }

        private static void Char32AppendJson(StringBuilder sb, UInt32 value)
        {
            sb.Append('"');
            PerfConvert.Char32AppendWithControlCharsJsonEscape(sb, value);
            sb.Append('"');
        }
    }
}
