// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1720 // Identifier contains type name

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
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
                        if (tokenizer.Value == "long")
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
                        else if (tokenizer.Value == "short")
                        {
                            foundShort = true;
                        }
                        else if (tokenizer.Value == "unsigned")
                        {
                            foundUnsigned = true;
                        }
                        else if (tokenizer.Value == "signed")
                        {
                            foundSigned = true;
                        }
                        else if (tokenizer.Value == "struct")
                        {
                            foundStruct = true;
                        }
                        else if (tokenizer.Value == "__data_loc")
                        {
                            foundDataLoc = true;
                        }
                        else if (tokenizer.Value == "__rel_loc")
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
                        while (iBegin < arrayCount.Length && ParseUtils.IsSpaceOrTab(arrayCount[iBegin]))
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
                            while (iEnd < arrayCount.Length && ParseUtils.IsHexDigit(arrayCount[iEnd]))
                            {
                                iEnd += 1;
                            }
                        }
                        else
                        {
                            iEnd = iBegin;
                            while (iEnd < arrayCount.Length && ParseUtils.IsDecimalDigit(arrayCount[iEnd]))
                            {
                                iEnd += 1;
                            }
                        }

                        fixedArrayCount = 0;
                        if (iEnd > iBegin)
                        {
                            ParseUtils.ParseUInt(arrayCount.Slice(iBegin, iEnd - iBegin), out fixedArrayCount);
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
                        if (tokenizer.Value == "*")
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
            else if (baseType.IsEmpty || baseType == "int")
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
            else if (baseType == "char")
            {
                this.format = foundUnsigned
                    ? PerfFieldFormat.Unsigned
                    : foundSigned
                    ? PerfFieldFormat.Signed
                    : PerfFieldFormat.String;
                this.elementSize = PerfFieldElementSize.Size8;
            }
            else if (baseType == "u8" || baseType == "__u8" || baseType == "uint8_t")
            {
                this.format = PerfFieldFormat.Unsigned;
                this.elementSize = PerfFieldElementSize.Size8;
            }
            else if (baseType == "s8" || baseType == "__s8" || baseType == "int8_t")
            {
                this.format = PerfFieldFormat.Signed;
                this.elementSize = PerfFieldElementSize.Size8;
            }
            else if (baseType == "u16" || baseType == "__u16" || baseType == "uint16_t")
            {
                this.format = PerfFieldFormat.Unsigned;
                this.elementSize = PerfFieldElementSize.Size16;
            }
            else if (baseType == "s16" || baseType == "__s16" || baseType == "int16_t")
            {
                this.format = PerfFieldFormat.Signed;
                this.elementSize = PerfFieldElementSize.Size16;
            }
            else if (baseType == "u32" || baseType == "__u32" || baseType == "uint32_t")
            {
                this.format = PerfFieldFormat.Unsigned;
                this.elementSize = PerfFieldElementSize.Size32;
            }
            else if (baseType == "s32" || baseType == "__s32" || baseType == "int32_t")
            {
                this.format = PerfFieldFormat.Signed;
                this.elementSize = PerfFieldElementSize.Size32;
            }
            else if (baseType == "u64" || baseType == "__u64" || baseType == "uint64_t")
            {
                this.format = PerfFieldFormat.Unsigned;
                this.elementSize = PerfFieldElementSize.Size64;
            }
            else if (baseType == "s64" || baseType == "__s64" || baseType == "int64_t")
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
                while (ParseUtils.IsSpaceOrTab(str[i]) || str[i] == ';')
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
                while (i < str.Length && ParseUtils.IsSpaceOrTab(str[i]))
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
                if (propName == "field" || propName == "field special")
                {
                    field = propValue;
                }
                else if (propName == "offset" && i < str.Length)
                {
                    foundOffset = ParseUtils.ParseUInt(propValue, out offset);
                }
                else if (propName == "size" && i < str.Length)
                {
                    foundSize = ParseUtils.ParseUInt(propValue, out size);
                }
                else if (propName == "signed" && i < str.Length)
                {
                    ushort signedVal;
                    isSigned = ParseUtils.ParseUInt(propValue, out signedVal)
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
        /// Given the event's raw data (e.g. PerfSampleEventInfo::raw_data), return
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
                            i = ParseUtils.ConsumeString(i + 1, this.str, this.str[i]);
                            newType = TokenKind.String;
                            break;
                        case '(':
                            // Return up to closing paren (allow nesting).
                            i = ParseUtils.ConsumeBraced(i + 1, this.str, '(', ')');
                            newType = TokenKind.Parentheses;
                            break;
                        case '[':
                            // Return up to closing brace (allow nesting).
                            i = ParseUtils.ConsumeBraced(i + 1, this.str, '[', ']');
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
