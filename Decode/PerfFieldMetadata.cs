// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1720 // Identifier contains type name

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Text;
    using Debug = System.Diagnostics.Debug;

    /// <summary>
    /// The type of the ElementSize property of PerfFieldMetadata.
    /// Size of a fixed-size field. sizeof(field) == 1 << (byte)perfFieldElementSize.
    /// </summary>
    public enum PerfFieldElementSize : byte
    {
        /// <summary>
        /// sizeof(uint8_t)  == 1 << Size8
        /// </summary>
        Size8,

        /// <summary>
        /// sizeof(uint16_t) == 1 << Size16
        /// </summary>
        Size16,

        /// <summary>
        /// sizeof(uint32_t) == 1 << Size32
        /// </summary>
        Size32,

        /// <summary>
        /// sizeof(uint64_t) == 1 << Size64
        /// </summary>
        Size64,
    };

    /// <summary>
    /// The type of the Format property of PerfFieldMetadata.
    /// Base format of a field.
    /// </summary>
    public enum PerfFieldFormat : byte
    {
        /// <summary>
        /// Type unknown (treat as binary blob)
        /// </summary>
        None,

        /// <summary>
        /// u8, u16, u32, u64, etc.
        /// </summary>
        Unsigned,

        /// <summary>
        /// s8, s16, s32, s64, etc.
        /// </summary>
        Signed,

        /// <summary>
        /// unsigned long, pointers
        /// </summary>
        Hex,

        /// <summary>
        /// char, char[]
        /// </summary>
        String,
    };

    /// <summary>
    /// The type of the Array property of PerfFieldMetadata.
    /// Array-ness of a field.
    /// </summary>
    public enum PerfFieldArray : byte
    {
        /// <summary>
        /// e.g. "char val"
        /// </summary>
        None,

        /// <summary>
        /// e.g. "char val[12]"
        /// </summary>
        Fixed,

        /// <summary>
        /// e.g. "__data_loc char val[]", value = (len << 16) | offset.
        /// </summary>
        Dynamic,

        /// <summary>
        /// e.g. "__rel_loc char val[]", value = (len << 16) | relativeOffset.
        /// </summary>
        RelDyn,
    };

    /// <summary>
    /// Stores metadata about a field, parsed from a tracefs "format" file.
    /// </summary>
    public class PerfFieldMetadata
    {
        private static PerfFieldMetadata? empty;

        /// <summary>
        /// deduced from field, e.g. "my_field".
        /// </summary>
        private readonly string name;

        /// <summary>
        /// value of "field:" property, e.g. "char my_field[8]".
        /// </summary>
        private readonly string field;

        /// <summary>
        /// value of "offset:" property.
        /// </summary>
        private readonly ushort offset;

        /// <summary>
        /// value of "size:" property.
        /// </summary>
        private readonly ushort size;

        /// <summary>
        /// deduced from field, size.
        /// </summary>
        private readonly ushort fixedArrayCount;

        /// <summary>
        /// deduced from field, size.
        /// </summary>
        private readonly PerfFieldElementSize elementSize;

        /// <summary>
        /// deduced from field, size, signed.
        /// </summary>
        private readonly PerfFieldFormat format;

        /// <summary>
        /// deduced from field, size.
        /// </summary>
        private readonly PerfFieldArray array;

        /// <summary>
        /// Same as PerfFieldMetadata(false, {}, 0, 0)
        /// </summary>
        private PerfFieldMetadata()
        {
            this.name = "noname";
            this.field = "";
            this.offset = 0;
            this.size = 0;
            this.fixedArrayCount = 0;
            this.elementSize = PerfFieldElementSize.Size8;
            this.format = PerfFieldFormat.None;
            this.array = PerfFieldArray.None;
        }

        /// <summary>
        /// Initializes Field, Offset, and Size properties exactly as specified.
        /// Deduces the other properties. The signed parameter should be null if the
        /// "signed:" property is not present in the format line.
        /// </summary>
        public PerfFieldMetadata(
            bool longSize64, // true if sizeof(long) == 8, false if sizeof(long) == 4.
            string field,
            ushort offset,
            ushort size,
            bool? signed)
        {
            var foundLongLong = false;
            var foundLong = false;
            var foundShort = false;
            var foundUnsigned = false;
            var foundSigned = false;
            var foundStruct = false;
            var foundDataLoc = false;
            var foundRelLoc = false;
            var foundArray = false;
            var foundPointer = false;
            ReadOnlySpan<char> baseType = default;

            // DEDUCE: name, fixedArrayCount

            ReadOnlySpan<char> nameSpan = default;
            ushort fixedArrayCount = 0;

            Tokenizer tokenizer = new Tokenizer(field);
            while (true)
            {
                tokenizer.MoveNext();
                switch (tokenizer.Kind)
                {
                    case TokenKind.None:
                        goto TokensDone;

                    case TokenKind.Ident:
                        if (tokenizer.Value.SequenceEqual("long"))
                        {
                            if (foundLong)
                            {
                                foundLongLong = true;
                            }
                            else
                            {
                                foundLong = true;
                            }
                        }
                        else if (tokenizer.Value.SequenceEqual("short"))
                        {
                            foundShort = true;
                        }
                        else if (tokenizer.Value.SequenceEqual("unsigned"))
                        {
                            foundUnsigned = true;
                        }
                        else if (tokenizer.Value.SequenceEqual("signed"))
                        {
                            foundSigned = true;
                        }
                        else if (tokenizer.Value.SequenceEqual("struct"))
                        {
                            foundStruct = true;
                        }
                        else if (tokenizer.Value.SequenceEqual("__data_loc"))
                        {
                            foundDataLoc = true;
                        }
                        else if (tokenizer.Value.SequenceEqual("__rel_loc"))
                        {
                            foundRelLoc = true;
                        }
                        else if (
                            tokenizer.Value != "__attribute__" &&
                            tokenizer.Value != "const" &&
                            tokenizer.Value != "volatile")
                        {
                            baseType = nameSpan;
                            nameSpan = tokenizer.Value;
                        }
                        break;

                    case TokenKind.Brackets:
                        // [] or [ArrayCount]
                        foundArray = true;

                        var arrayCount = tokenizer.Value;
                        int iBegin = 1; // Skip '['.
                        while (iBegin < arrayCount.Length && Utility.IsSpaceOrTab(arrayCount[iBegin]))
                        {
                            iBegin += 1;
                        }

                        int iEnd;
                        if (arrayCount.Length - iBegin > 2 &&
                            arrayCount[iBegin] == '0' &&
                            (arrayCount[iBegin + 1] == 'x' || arrayCount[iBegin + 1] == 'X'))
                        {
                            iBegin += 2; // Skip "0x".
                            iEnd = iBegin;
                            while (iEnd < arrayCount.Length && Utility.IsHexDigit(arrayCount[iEnd]))
                            {
                                iEnd += 1;
                            }
                        }
                        else
                        {
                            iEnd = iBegin;
                            while (iEnd < arrayCount.Length && Utility.IsDecimalDigit(arrayCount[iEnd]))
                            {
                                iEnd += 1;
                            }
                        }

                        fixedArrayCount = 0;
                        if (iEnd > iBegin)
                        {
                            Utility.ParseUInt(arrayCount.Slice(iBegin, iEnd - iBegin), out fixedArrayCount);
                        }

                        tokenizer.MoveNext();
                        if (tokenizer.Kind == TokenKind.Ident)
                        {
                            baseType = nameSpan;
                            nameSpan = tokenizer.Value;
                        }

                        goto TokensDone;

                    case TokenKind.Parentheses:
                    case TokenKind.String:
                        // Ignored.
                        break;

                    case TokenKind.Punctuation:
                        // Most punctuation ignored.
                        if (tokenizer.Value.SequenceEqual("*"))
                        {
                            foundPointer = true;
                        }
                        break;

                    default:
                        Debug.Fail("Unexpected token kind");
                        goto TokensDone;
                }
            }

        TokensDone:

            this.name = nameSpan.IsEmpty ? "noname" : nameSpan.ToString();
            this.field = field;
            this.offset = offset;
            this.size = size;
            this.fixedArrayCount = fixedArrayCount;

            // DEDUCE: elementSize, format

            bool fixupElementSize = false;

            if (foundPointer)
            {
                this.format = PerfFieldFormat.Hex;
                this.elementSize = longSize64 ? PerfFieldElementSize.Size64 : PerfFieldElementSize.Size32;
            }
            else if (foundStruct)
            {
                this.format = PerfFieldFormat.None;
                this.elementSize = PerfFieldElementSize.Size8;
            }
            else if (baseType.IsEmpty || baseType.SequenceEqual("int"))
            {
                this.format = foundUnsigned
                    ? PerfFieldFormat.Unsigned
                    : PerfFieldFormat.Signed;
                if (foundLongLong)
                {
                    this.elementSize = PerfFieldElementSize.Size64;
                }
                else if (foundLong)
                {
                    this.elementSize = longSize64 ? PerfFieldElementSize.Size64 : PerfFieldElementSize.Size32;
                    if (foundUnsigned)
                    {
                        this.format = PerfFieldFormat.Hex; // Use hex for unsigned long.
                    }
                }
                else if (foundShort)
                {
                    this.elementSize = PerfFieldElementSize.Size16;
                }
                else
                {
                    this.elementSize = PerfFieldElementSize.Size32; // "unsigned" or "signed" means "int".
                    if (baseType.IsEmpty && !foundUnsigned && !foundSigned)
                    {
                        // Unexpected.
                        Debug.WriteLine("No baseType found for \"{}\"",
                            this.field);
                    }
                }
            }
            else if (baseType.SequenceEqual("char"))
            {
                this.format = foundUnsigned
                    ? PerfFieldFormat.Unsigned
                    : foundSigned
                    ? PerfFieldFormat.Signed
                    : PerfFieldFormat.String;
                this.elementSize = PerfFieldElementSize.Size8;
            }
            else if (baseType.SequenceEqual("u8") || baseType.SequenceEqual("__u8") || baseType.SequenceEqual("uint8_t"))
            {
                this.format = PerfFieldFormat.Unsigned;
                this.elementSize = PerfFieldElementSize.Size8;
            }
            else if (baseType.SequenceEqual("s8") || baseType.SequenceEqual("__s8") || baseType.SequenceEqual("int8_t"))
            {
                this.format = PerfFieldFormat.Signed;
                this.elementSize = PerfFieldElementSize.Size8;
            }
            else if (baseType.SequenceEqual("u16") || baseType.SequenceEqual("__u16") || baseType.SequenceEqual("uint16_t"))
            {
                this.format = PerfFieldFormat.Unsigned;
                this.elementSize = PerfFieldElementSize.Size16;
            }
            else if (baseType.SequenceEqual("s16") || baseType.SequenceEqual("__s16") || baseType.SequenceEqual("int16_t"))
            {
                this.format = PerfFieldFormat.Signed;
                this.elementSize = PerfFieldElementSize.Size16;
            }
            else if (baseType.SequenceEqual("u32") || baseType.SequenceEqual("__u32") || baseType.SequenceEqual("uint32_t"))
            {
                this.format = PerfFieldFormat.Unsigned;
                this.elementSize = PerfFieldElementSize.Size32;
            }
            else if (baseType.SequenceEqual("s32") || baseType.SequenceEqual("__s32") || baseType.SequenceEqual("int32_t"))
            {
                this.format = PerfFieldFormat.Signed;
                this.elementSize = PerfFieldElementSize.Size32;
            }
            else if (baseType.SequenceEqual("u64") || baseType.SequenceEqual("__u64") || baseType.SequenceEqual("uint64_t"))
            {
                this.format = PerfFieldFormat.Unsigned;
                this.elementSize = PerfFieldElementSize.Size64;
            }
            else if (baseType.SequenceEqual("s64") || baseType.SequenceEqual("__s64") || baseType.SequenceEqual("int64_t"))
            {
                this.format = PerfFieldFormat.Signed;
                this.elementSize = PerfFieldElementSize.Size64;
            }
            else
            {
                this.format = PerfFieldFormat.Hex;
                fixupElementSize = true;
            }

            // FIXUP: format

            if (this.format == PerfFieldFormat.Unsigned || this.format == PerfFieldFormat.Signed)
            {
                // If valid, signed overrides baseType.
                switch (signed)
                {
                    default: break; // i.e. null
                    case false: this.format = PerfFieldFormat.Unsigned; break;
                    case true: this.format = PerfFieldFormat.Signed; break;
                }
            }

            // DEDUCE: array

            if (foundRelLoc)
            {
                this.array = PerfFieldArray.RelDyn;
                this.fixedArrayCount = 0;
            }
            else if (foundDataLoc)
            {
                this.array = PerfFieldArray.Dynamic;
                this.fixedArrayCount = 0;
            }
            else if (foundArray)
            {
                this.array = PerfFieldArray.Fixed;
                if (fixupElementSize && this.fixedArrayCount != 0 && this.size % this.fixedArrayCount == 0)
                {
                    // Try to deduce element size from size and array count.
                    switch (this.size / this.fixedArrayCount)
                    {
                        default:
                            break;
                        case 1:
                            this.elementSize = PerfFieldElementSize.Size8;
                            fixupElementSize = false;
                            break;
                        case 2:
                            this.elementSize = PerfFieldElementSize.Size16;
                            fixupElementSize = false;
                            break;
                        case 4:
                            this.elementSize = PerfFieldElementSize.Size32;
                            fixupElementSize = false;
                            break;
                        case 8:
                            this.elementSize = PerfFieldElementSize.Size64;
                            fixupElementSize = false;
                            break;
                    }
                }
            }
            else
            {
                this.array = PerfFieldArray.None;
                this.fixedArrayCount = 0;

                // If valid, size overrides element size deduced from type name.
                switch (this.size)
                {
                    default:
                        break;
                    case 1:
                        this.elementSize = PerfFieldElementSize.Size8;
                        fixupElementSize = false;
                        break;
                    case 2:
                        this.elementSize = PerfFieldElementSize.Size16;
                        fixupElementSize = false;
                        break;
                    case 4:
                        this.elementSize = PerfFieldElementSize.Size32;
                        fixupElementSize = false;
                        break;
                    case 8:
                        this.elementSize = PerfFieldElementSize.Size64;
                        fixupElementSize = false;
                        break;
                }
            }

            if (fixupElementSize)
            {
                this.elementSize = PerfFieldElementSize.Size8;
            }
        }

        /// <summary>
        /// Gets the empty PerfFieldMetadata object.
        /// </summary>
        public static PerfFieldMetadata Empty
        {
            get
            {
                var value = empty;
                if (value == null)
                {
                    value = new PerfFieldMetadata();
                    empty = value;
                }

                return value;
            }
        }

        /// <summary>
        /// Returns the field name, e.g. "my_field". Never empty. (Deduced from
        /// "field:".)
        /// </summary>
        public string Name => this.name;

        /// <summary>
        /// Returns the field declaration, e.g. "char my_field[8]".
        /// (Parsed directly from "field:".)
        /// </summary>
        public string Field => this.field;

        /// <summary>
        /// Returns the byte offset of the start of the field data from the start of
        /// the event raw data. (Parsed directly from "offset:".)
        /// </summary>
        public ushort Offset => this.offset;

        /// <summary>
        /// Returns the byte size of the field data. (Parsed directly from "size:".)
        /// </summary>
        public ushort Size => this.size;

        /// <summary>
        /// Returns the number of elements in this field. Meaningful only when
        /// Array() == Fixed. (Deduced from "field:" and "size:".)
        /// </summary>
        public ushort FixedArrayCount => this.fixedArrayCount;

        /// <summary>
        /// Returns the size of each element in this field. (Deduced from "field:"
        /// and "size:".)
        /// </summary>
        public PerfFieldElementSize ElementSize => this.elementSize;

        /// <summary>
        /// Returns the format of the field. (Deduced from "field:" and "signed:".)
        /// </summary>
        public PerfFieldFormat Format => this.format;

        /// <summary>
        /// Returns whether this is an array, and if so, how the array length should
        /// be determined. (Deduced from "field:" and "size:".)
        /// </summary>
        public PerfFieldArray Array => this.array;

        /// <summary>
        /// Parses a line of the "format:" section of an event's "format" file. The
        /// formatLine string will generally look like
        /// "[whitespace?]field:[declaration]; offset:[number]; size:[number]; ...".
        ///
        /// If "field:" is non-empty, "offset:" is a valid unsigned integer, and
        /// "size:" is a valid unsigned integer, returns
        /// PerfFieldMetadata(field, offset, size, signed). Otherwise,  returns null.
        /// </summary>
        public static PerfFieldMetadata? Parse(
            bool longSize64, // true if sizeof(long) == 8, false if sizeof(long) == 4.
            ReadOnlySpan<char> formatLine)
        {
            ReadOnlySpan<char> field = default;
            ushort offset = 0;
            bool foundOffset = false;
            ushort size = 0;
            bool foundSize = false;
            bool? isSigned = null;

            var str = formatLine;
            var i = 0;

            // FIND: field, offset, size

            // Search for " NAME: VALUE;"
            while (i < str.Length)
            {
                // Skip spaces and semicolons.
                while (Utility.IsSpaceOrTab(str[i]) || str[i] == ';')
                {
                    i += 1;
                    if (i >= str.Length)
                    {
                        goto Done;
                    }
                }

                // "NAME:"
                var iPropName = i;
                while (str[i] != ':')
                {
                    i += 1;
                    if (i >= str.Length)
                    {
                        Debug.WriteLine("EOL before ':' in format");
                        goto Done; // Unexpected.
                    }
                }

                var propName = str.Slice(iPropName, i - iPropName);
                i += 1; // Skip ':'

                // Skip spaces.
                while (i < str.Length && Utility.IsSpaceOrTab(str[i]))
                {
                    Debug.WriteLine("Space before propval in format");
                    i += 1; // Unexpected.
                }

                // "VALUE;"
                var iPropValue = i;
                while (i < str.Length && str[i] != ';')
                {
                    i += 1;
                }

                var propValue = str.Slice(iPropValue, i - iPropValue);
                if (propName.SequenceEqual("field") || propName.SequenceEqual("field special"))
                {
                    field = propValue;
                }
                else if (propName.SequenceEqual("offset") && i < str.Length)
                {
                    foundOffset = Utility.ParseUInt(propValue, out offset);
                }
                else if (propName.SequenceEqual("size") && i < str.Length)
                {
                    foundSize = Utility.ParseUInt(propValue, out size);
                }
                else if (propName.SequenceEqual("signed") && i < str.Length)
                {
                    ushort signedVal;
                    isSigned = Utility.ParseUInt(propValue, out signedVal)
                        ? signedVal != 0
                        : (bool?)null;
                }
            }

        Done:

            PerfFieldMetadata? result;
            if (field.Length == 0 || !foundOffset || !foundSize)
            {
                result = null;
            }
            else
            {
                result = new PerfFieldMetadata(longSize64, field.ToString(), offset, size, isSigned);
            }

            return result;
        }

        /// <summary>
        /// Given the event's raw data (e.g. PerfSampleEventInfo::RawData), return
        /// this field's raw data. Returns empty for error (e.g. out of bounds).
        ///
        /// Does not do any byte-swapping. This method uses fileBigEndian to resolve
        /// data_loc and rel_loc references, not to fix up the field data.
        /// 
        /// Note that in some cases, the size returned by GetFieldBytes may be
        /// different from the value returned by Size():
        ///
        /// - If eventRawDataSize < Offset() + Size(), returns {}.
        /// - If Size() == 0, returns all data from offset to the end of the event,
        ///   i.e. it returns eventRawDataSize - Offset() bytes.
        /// - If Array() is Dynamic or RelDyn, the returned size depends on the
        ///   event contents.
        /// </summary>
        public ReadOnlySpan<byte> GetFieldBytes(
            ReadOnlySpan<byte> eventRawData,
            bool fileBigEndian)
        {
            ReadOnlySpan<byte> result;
            if (this.offset + this.size > eventRawData.Length)
            {
                result = default;
            }
            else if (this.size == 0)
            {
                // size 0 means "the rest of the event data"
                result = eventRawData.Slice(this.offset);
            }
            else
            {
                var byteReader = new PerfByteReader(fileBigEndian);
                switch (this.array)
                {
                    default:
                        result = eventRawData.Slice(this.offset, this.size);
                        break;

                    case PerfFieldArray.Dynamic:
                    case PerfFieldArray.RelDyn:
                        result = default;
                        if (this.size == 4)
                        {
                            // 4-byte value is an offset/length pair leading to the real data.
                            var dyn = byteReader.ReadU32(eventRawData.Slice(this.offset));
                            var dynSize = (int)(dyn >> 16);
                            var dynOffset = (int)(dyn & 0xFFFF);
                            if (this.array == PerfFieldArray.RelDyn)
                            {
                                // offset is relative to end of field.
                                dynOffset += this.offset + this.size;
                            }

                            if (dynOffset + dynSize <= eventRawData.Length)
                            {
                                result = eventRawData.Slice(dynOffset, dynSize);
                            }
                        }
                        else if (this.size == 2)
                        {
                            // 2-byte value is an offset leading to the real data, size is strlen.
                            int dynOffset = byteReader.ReadU16(eventRawData.Slice(this.offset));
                            if (this.array == PerfFieldArray.RelDyn)
                            {
                                // offset is relative to end of field.
                                dynOffset += this.offset + this.size;
                            }

                            if (dynOffset < eventRawData.Length)
                            {
                                var dynSize = eventRawData.Slice(dynOffset).IndexOf((byte)0);
                                if (dynSize >= 0)
                                {
                                    result = eventRawData.Slice(dynOffset, dynSize);
                                }
                            }
                        }
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Formats the given bytes using this field's type.
        /// </summary>
        /// <param name="fieldBytes">Field data, e.g. from GetFieldBytes.</param>
        /// <param name="eventBigEndian">true if the event was logged using big-endian byte order.</param>
        /// <param name="jsonQuotes">true to put quotes around strings and hexadecimal numbers.</param>
        /// <returns></returns>
        public string FormatField(ReadOnlySpan<byte> fieldBytes, bool eventBigEndian, bool jsonQuotes = false)
        {
            StringBuilder sb;
            var byteReader = new PerfByteReader(eventBigEndian);
            switch (this.Format)
            {
                default:
                case PerfFieldFormat.None:

                    if (this.Array == PerfFieldArray.None ||
                        this.ElementSize == PerfFieldElementSize.Size8)
                    {
                        return Utility.ToHexString(fieldBytes);
                    }
                    goto case PerfFieldFormat.Hex;

                case PerfFieldFormat.String:

                    var len = fieldBytes.IndexOf((byte)0);
                    if (!jsonQuotes)
                    {
                        if (len == 0)
                        {
                            return "";
                        }
                        else
                        {
                            return Utility.EncodingLatin1.GetString(fieldBytes.Slice(0, len >= 0 ? len : fieldBytes.Length));
                        }
                    }
                    else if (len == 0)
                    {
                        return "\"\"";
                    }
                    else
                    {
                        sb = new StringBuilder(len);
                        sb.Append('"');
                        for (int i = 0; i < len; i += 1)
                        {
                            var ch = (char)fieldBytes[i];
                            if (ch == '\\')
                            {
                                sb.Append('\\');
                                sb.Append('\\');
                            }
                            else if (ch == '"')
                            {
                                sb.Append('\\');
                                sb.Append('"');
                            }
                            else if (ch >= ' ')
                            {
                                sb.Append(ch);
                            }
                            else
                            {
                                sb.Append('\\');
                                switch (ch)
                                {
                                    case '\b': sb.Append('b'); break;
                                    case '\f': sb.Append('f'); break;
                                    case '\n': sb.Append('n'); break;
                                    case '\r': sb.Append('r'); break;
                                    case '\t': sb.Append('t'); break;
                                    default:
                                        sb.Append('u');
                                        sb.Append('0');
                                        sb.Append('0');
                                        sb.Append(Utility.ToHexChar(ch >> 4));
                                        sb.Append(Utility.ToHexChar(ch));
                                        break;
                                }
                            }
                        }
                        sb.Append('"');
                        break;
                    }

                case PerfFieldFormat.Hex:

                    sb = new StringBuilder();
                    if (this.Array == PerfFieldArray.None)
                    {
                        switch (this.ElementSize)
                        {
                            default:
                                return Utility.ToHexString(fieldBytes);
                            case PerfFieldElementSize.Size8:
                                if (fieldBytes.Length < 1)
                                {
                                    return "null";
                                }
                                AppendHexJson(jsonQuotes, sb, fieldBytes[0]);
                                break;
                            case PerfFieldElementSize.Size16:
                                if (fieldBytes.Length < 2)
                                {
                                    return "null";
                                }
                                AppendHexJson(jsonQuotes, sb, byteReader.ReadU16(fieldBytes));
                                break;
                            case PerfFieldElementSize.Size32:
                                if (fieldBytes.Length < 4)
                                {
                                    return "null";
                                }
                                AppendHexJson(jsonQuotes, sb, byteReader.ReadU32(fieldBytes));
                                break;
                            case PerfFieldElementSize.Size64:
                                if (fieldBytes.Length < 8)
                                {
                                    return "null";
                                }
                                AppendHexJson(jsonQuotes, sb, byteReader.ReadU64(fieldBytes));
                                break;
                        }
                    }
                    else
                    {
                        sb.Append('[');
                        switch (this.ElementSize)
                        {
                            default:
                                return Utility.ToHexString(fieldBytes);
                            case PerfFieldElementSize.Size8:
                                if (fieldBytes.Length >= 1)
                                {
                                    AppendHexJson(jsonQuotes, sb, fieldBytes[0]);
                                    for (int i = 1; i < fieldBytes.Length; i += 1)
                                    {
                                        sb.Append(',');
                                        AppendHexJson(jsonQuotes, sb, fieldBytes[i]);
                                    }
                                }
                                break;
                            case PerfFieldElementSize.Size16:
                                if (fieldBytes.Length >= 2)
                                {
                                    AppendHexJson(jsonQuotes, sb, byteReader.ReadU16(fieldBytes));
                                    for (int i = 3; i < fieldBytes.Length; i += 2)
                                    {
                                        sb.Append(',');
                                        AppendHexJson(jsonQuotes, sb, byteReader.ReadU16(fieldBytes.Slice(i - 1)));
                                    }
                                }
                                break;
                            case PerfFieldElementSize.Size32:
                                if (fieldBytes.Length >= 4)
                                {
                                    AppendHexJson(jsonQuotes, sb, byteReader.ReadU32(fieldBytes));
                                    for (int i = 7; i < fieldBytes.Length; i += 4)
                                    {
                                        sb.Append(',');
                                        AppendHexJson(jsonQuotes, sb, byteReader.ReadU32(fieldBytes.Slice(i - 3)));
                                    }
                                }
                                break;
                            case PerfFieldElementSize.Size64:
                                if (fieldBytes.Length >= 8)
                                {
                                    AppendHexJson(jsonQuotes, sb, byteReader.ReadU64(fieldBytes));
                                    for (int i = 15; i < fieldBytes.Length; i += 8)
                                    {
                                        sb.Append(',');
                                        AppendHexJson(jsonQuotes, sb, byteReader.ReadU32(fieldBytes.Slice(i - 7)));
                                    }
                                }
                                break;
                        }
                        sb.Append(']');
                    }
                    break;

                case PerfFieldFormat.Unsigned:

                    sb = new StringBuilder();
                    if (this.Array == PerfFieldArray.None)
                    {
                        switch (this.ElementSize)
                        {
                            default:
                                return Utility.ToHexString(fieldBytes);
                            case PerfFieldElementSize.Size8:
                                if (fieldBytes.Length < 1)
                                {
                                    return "null";
                                }
                                sb.Append(fieldBytes[0]);
                                break;
                            case PerfFieldElementSize.Size16:
                                if (fieldBytes.Length < 2)
                                {
                                    return "null";
                                }
                                sb.Append(byteReader.ReadU16(fieldBytes));
                                break;
                            case PerfFieldElementSize.Size32:
                                if (fieldBytes.Length < 4)
                                {
                                    return "null";
                                }
                                sb.Append(byteReader.ReadU32(fieldBytes));
                                break;
                            case PerfFieldElementSize.Size64:
                                if (fieldBytes.Length < 8)
                                {
                                    return "null";
                                }
                                sb.Append(byteReader.ReadU64(fieldBytes));
                                break;
                        }
                    }
                    else
                    {
                        sb.Append('[');
                        switch (this.ElementSize)
                        {
                            default:
                                return Utility.ToHexString(fieldBytes);
                            case PerfFieldElementSize.Size8:
                                if (fieldBytes.Length >= 1)
                                {
                                    sb.Append(fieldBytes[0]);
                                    for (int i = 1; i < fieldBytes.Length; i += 1)
                                    {
                                        sb.Append(',');
                                        sb.Append(fieldBytes[i]);
                                    }
                                }
                                break;
                            case PerfFieldElementSize.Size16:
                                if (fieldBytes.Length >= 2)
                                {
                                    sb.Append(byteReader.ReadU16(fieldBytes));
                                    for (int i = 3; i < fieldBytes.Length; i += 2)
                                    {
                                        sb.Append(',');
                                        sb.Append(byteReader.ReadU16(fieldBytes.Slice(i - 1)));
                                    }
                                }
                                break;
                            case PerfFieldElementSize.Size32:
                                if (fieldBytes.Length >= 4)
                                {
                                    sb.Append(byteReader.ReadU32(fieldBytes));
                                    for (int i = 7; i < fieldBytes.Length; i += 4)
                                    {
                                        sb.Append(',');
                                        sb.Append(byteReader.ReadU32(fieldBytes.Slice(i - 3)));
                                    }
                                }
                                break;
                            case PerfFieldElementSize.Size64:
                                if (fieldBytes.Length >= 8)
                                {
                                    sb.Append(byteReader.ReadU64(fieldBytes));
                                    for (int i = 15; i < fieldBytes.Length; i += 8)
                                    {
                                        sb.Append(',');
                                        sb.Append(byteReader.ReadU32(fieldBytes.Slice(i - 7)));
                                    }
                                }
                                break;
                        }
                        sb.Append(']');
                    }
                    break;

                case PerfFieldFormat.Signed:

                    sb = new StringBuilder();
                    if (this.Array == PerfFieldArray.None)
                    {
                        switch (this.ElementSize)
                        {
                            default:
                                return Utility.ToHexString(fieldBytes);
                            case PerfFieldElementSize.Size8:
                                if (fieldBytes.Length < 1)
                                {
                                    return "null";
                                }
                                sb.Append(unchecked((sbyte)fieldBytes[0]));
                                break;
                            case PerfFieldElementSize.Size16:
                                if (fieldBytes.Length < 2)
                                {
                                    return "null";
                                }
                                sb.Append(byteReader.ReadI16(fieldBytes));
                                break;
                            case PerfFieldElementSize.Size32:
                                if (fieldBytes.Length < 4)
                                {
                                    return "null";
                                }
                                sb.Append(byteReader.ReadI32(fieldBytes));
                                break;
                            case PerfFieldElementSize.Size64:
                                if (fieldBytes.Length < 8)
                                {
                                    return "null";
                                }
                                sb.Append(byteReader.ReadI64(fieldBytes));
                                break;
                        }
                    }
                    else
                    {
                        sb.Append('[');
                        switch (this.ElementSize)
                        {
                            default:
                                return Utility.ToHexString(fieldBytes);
                            case PerfFieldElementSize.Size8:
                                if (fieldBytes.Length >= 1)
                                {
                                    sb.Append(unchecked((sbyte)fieldBytes[0]));
                                    for (int i = 1; i < fieldBytes.Length; i += 1)
                                    {
                                        sb.Append(',');
                                        sb.Append(unchecked((sbyte)fieldBytes[i]));
                                    }
                                }
                                break;
                            case PerfFieldElementSize.Size16:
                                if (fieldBytes.Length >= 2)
                                {
                                    sb.Append(byteReader.ReadI16(fieldBytes));
                                    for (int i = 3; i < fieldBytes.Length; i += 2)
                                    {
                                        sb.Append(',');
                                        sb.Append(byteReader.ReadI16(fieldBytes.Slice(i - 1)));
                                    }
                                }
                                break;
                            case PerfFieldElementSize.Size32:
                                if (fieldBytes.Length >= 4)
                                {
                                    sb.Append(byteReader.ReadI32(fieldBytes));
                                    for (int i = 7; i < fieldBytes.Length; i += 4)
                                    {
                                        sb.Append(',');
                                        sb.Append(byteReader.ReadI32(fieldBytes.Slice(i - 3)));
                                    }
                                }
                                break;
                            case PerfFieldElementSize.Size64:
                                if (fieldBytes.Length >= 8)
                                {
                                    sb.Append(byteReader.ReadI64(fieldBytes));
                                    for (int i = 15; i < fieldBytes.Length; i += 8)
                                    {
                                        sb.Append(',');
                                        sb.Append(byteReader.ReadI32(fieldBytes.Slice(i - 7)));
                                    }
                                }
                                break;
                        }
                        sb.Append(']');
                    }
                    break;
            }

            return sb.ToString();
        }

        private static void AppendHexJson(bool jsonQuotes, StringBuilder sb, uint value)
        {
            if (!jsonQuotes)
            {
                AppendHex(sb, value);
            }
            else
            {
                sb.Append('"');
                AppendHex(sb, value);
                sb.Append('"');
            }
        }

        private static void AppendHexJson(bool jsonQuotes, StringBuilder sb, ulong value)
        {
            if (!jsonQuotes)
            {
                AppendHex(sb, value);
            }
            else
            {
                sb.Append('"');
                AppendHex(sb, value);
                sb.Append('"');
            }
        }

        private static void AppendHex(StringBuilder sb, uint value)
        {
            sb.Append('0');
            sb.Append('x');

            var a = sb.Length;
            do
            {
                sb.Append(Utility.ToHexChar(unchecked((int)value)));
                value >>= 4;
            } while (value != 0);
            var b = sb.Length - 1;

            while (a < b)
            {
                var ch = sb[b];
                sb[b] = sb[a];
                sb[a] = ch;
                a += 1;
                b -= 1;
            }
        }

        private static void AppendHex(StringBuilder sb, ulong value)
        {
            sb.Append('0');
            sb.Append('x');

            var a = sb.Length;
            do
            {
                sb.Append(Utility.ToHexChar(unchecked((int)value)));
                value >>= 4;
            } while (value != 0);
            var b = sb.Length - 1;

            while (a < b)
            {
                var ch = sb[b];
                sb[b] = sb[a];
                sb[a] = ch;
                a += 1;
                b -= 1;
            }
        }

        private enum TokenKind : byte
        {
            None,
            Ident,         // e.g. MyFile
            Brackets,      // e.g. [...]
            Parentheses,   // e.g. (...)
            String,        // e.g. "asdf"
            Punctuation,   // e.g. *
        }

        private ref struct Tokenizer
        {
            private readonly ReadOnlySpan<char> str;
            private int pos;

            public TokenKind Kind;
            public ReadOnlySpan<char> Value;

            public Tokenizer(ReadOnlySpan<char> str)
            {
                this.str = str;
                this.pos = 0;
                this.Kind = TokenKind.None;
                this.Value = default;
            }

            public void MoveNext()
            {
                TokenKind newType;
                var i = this.pos;

                while (i < this.str.Length && this.str[i] <= ' ')
                {
                    i += 1;
                }

                var iTokenStart = i;

                if (i == this.str.Length)
                {
                    newType = TokenKind.None;
                }
                else if (IsIdentStart(this.str[i]))
                {
                    // Return identifier.
                    i += 1;
                    while (i < this.str.Length && IsIdentContinue(this.str[i]))
                    {
                        i += 1;
                    }

                    newType = TokenKind.Ident;
                }
                else
                {
                    switch (this.str[i])
                    {
                        case '\'':
                        case '\"':
                            // Return up to the closing quote.
                            i = Utility.ConsumeString(i + 1, this.str, this.str[i]);
                            newType = TokenKind.String;
                            break;
                        case '(':
                            // Return up to closing paren (allow nesting).
                            i = Utility.ConsumeBraced(i + 1, this.str, '(', ')');
                            newType = TokenKind.Parentheses;
                            break;
                        case '[':
                            // Return up to closing brace (allow nesting).
                            i = Utility.ConsumeBraced(i + 1, this.str, '[', ']');
                            newType = TokenKind.Brackets;
                            break;
                        default: // Return single character token.
                            i += 1;
                            newType = TokenKind.Punctuation;
                            break;
                    }
                }

                this.pos = i;
                Value = str.Slice(iTokenStart, i - iTokenStart);
                Kind = newType;
            }

            private static bool IsIdentStart(char ch)
            {
                var chLower = (uint)ch | 0x20;
                return ('a' <= chLower && chLower <= 'z') ||
                    ch == '_';
            }

            private static bool IsIdentContinue(char ch)
            {
                var chLower = (uint)ch | 0x20;
                return ('a' <= chLower && chLower <= 'z') ||
                    ('0' <= ch && ch <= '9') ||
                    ch == '_';
            }
        }
    }
}
