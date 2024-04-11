// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1720 // Identifier contains type name

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using Debug = System.Diagnostics.Debug;

    /// <summary>
    /// The type of the Array property of PerfFieldFormat.
    /// Array-ness of a field.
    /// </summary>
    public enum PerfFieldArray : byte
    {
        /// <summary>
        /// e.g. "char val; size:1;".
        /// </summary>
        None,

        /// <summary>
        /// e.g. "char val[12]; size:12;".
        /// </summary>
        Fixed,

        /// <summary>
        /// e.g. "char val[]; size:0;".
        /// </summary>
        RestOfEvent,

        /// <summary>
        /// e.g. "__rel_loc char val[]; size:2;".
        /// Value relativeOffset. dataLen is determined via strlen.
        /// </summary>
        RelLoc2,

        /// <summary>
        /// e.g. "__data_loc char val[]; size:2;".
        /// Value is offset. dataLen is determined via strlen.
        /// </summary>
        DataLoc2,

        /// <summary>
        /// e.g. "__rel_loc char val[]; size:4;".
        /// Value is (dataLen LSH 16) | relativeOffset.
        /// </summary>
        RelLoc4,

        /// <summary>
        /// e.g. "__data_loc char val[]; size:4;".
        /// Value is (dataLen LSH 16) | offset.
        /// </summary>
        DataLoc4,
    };

    /// <summary>
    /// Stores decoding information about a field, parsed from a tracefs "format" file.
    /// </summary>
    public class PerfFieldFormat
    {
        /// <summary>
        /// Initializes Field, Offset, and Size properties exactly as specified.
        /// Parses and deduces the other properties. The signed parameter should be null
        /// if the "signed:" property is not present in the format line.
        /// </summary>
        internal PerfFieldFormat(
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

            // PARSE: Name, SpecifiedArrayCount

            ReadOnlySpan<char> nameSpan = default;
            ushort specifiedArrayCount = 0;

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
                        // [] or [ElementCount]
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

                        specifiedArrayCount = 0;
                        if (iEnd > iBegin)
                        {
                            Utility.ParseUInt(arrayCount.Slice(iBegin, iEnd - iBegin), out specifiedArrayCount);
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

            this.Name = nameSpan.IsEmpty ? "noname" : nameSpan.ToString();
            this.Field = field;
            this.Offset = offset;
            this.Size = size;
            this.Signed = signed;
            this.SpecifiedArrayCount = specifiedArrayCount;

            // PARSE: SpecifiedEncoding, SpecifiedFormat

            if (foundPointer)
            {
                this.SpecifiedFormat = EventHeaderFieldFormat.HexInt;
                this.SpecifiedEncoding = longSize64 ? EventHeaderFieldEncoding.Value64 : EventHeaderFieldEncoding.Value32;
            }
            else if (foundStruct)
            {
                this.SpecifiedFormat = EventHeaderFieldFormat.HexBytes; // SPECIAL
                this.SpecifiedEncoding = EventHeaderFieldEncoding.Struct; // SPECIAL
            }
            else if (baseType.IsEmpty || baseType.SequenceEqual("int"))
            {
                this.SpecifiedFormat = foundUnsigned
                    ? EventHeaderFieldFormat.UnsignedInt
                    : EventHeaderFieldFormat.SignedInt;
                if (foundLongLong)
                {
                    this.SpecifiedEncoding = EventHeaderFieldEncoding.Value64;
                }
                else if (foundLong)
                {
                    this.SpecifiedEncoding = longSize64 ? EventHeaderFieldEncoding.Value64 : EventHeaderFieldEncoding.Value32;
                    if (foundUnsigned)
                    {
                        this.SpecifiedFormat = EventHeaderFieldFormat.HexInt; // Use hex for unsigned long.
                    }
                }
                else if (foundShort)
                {
                    this.SpecifiedEncoding = EventHeaderFieldEncoding.Value16;
                }
                else
                {
                    this.SpecifiedEncoding = EventHeaderFieldEncoding.Value32; // "unsigned" or "signed" means "int".
                    if (baseType.IsEmpty && !foundUnsigned && !foundSigned)
                    {
                        // Unexpected.
                        Debug.WriteLine("No baseType found for \"{}\"",
                            this.Field);
                    }
                }
            }
            else if (baseType.SequenceEqual("char"))
            {
                this.SpecifiedFormat = foundUnsigned
                    ? EventHeaderFieldFormat.UnsignedInt
                    : foundSigned
                    ? EventHeaderFieldFormat.SignedInt
                    : EventHeaderFieldFormat.String8; // SPECIAL
                this.SpecifiedEncoding = EventHeaderFieldEncoding.Value8;
            }
            else if (baseType.SequenceEqual("u8") || baseType.SequenceEqual("__u8") || baseType.SequenceEqual("uint8_t"))
            {
                this.SpecifiedFormat = EventHeaderFieldFormat.UnsignedInt;
                this.SpecifiedEncoding = EventHeaderFieldEncoding.Value8;
            }
            else if (baseType.SequenceEqual("s8") || baseType.SequenceEqual("__s8") || baseType.SequenceEqual("int8_t"))
            {
                this.SpecifiedFormat = EventHeaderFieldFormat.SignedInt;
                this.SpecifiedEncoding = EventHeaderFieldEncoding.Value8;
            }
            else if (baseType.SequenceEqual("u16") || baseType.SequenceEqual("__u16") || baseType.SequenceEqual("uint16_t"))
            {
                this.SpecifiedFormat = EventHeaderFieldFormat.UnsignedInt;
                this.SpecifiedEncoding = EventHeaderFieldEncoding.Value16;
            }
            else if (baseType.SequenceEqual("s16") || baseType.SequenceEqual("__s16") || baseType.SequenceEqual("int16_t"))
            {
                this.SpecifiedFormat = EventHeaderFieldFormat.SignedInt;
                this.SpecifiedEncoding = EventHeaderFieldEncoding.Value16;
            }
            else if (baseType.SequenceEqual("u32") || baseType.SequenceEqual("__u32") || baseType.SequenceEqual("uint32_t"))
            {
                this.SpecifiedFormat = EventHeaderFieldFormat.UnsignedInt;
                this.SpecifiedEncoding = EventHeaderFieldEncoding.Value32;
            }
            else if (baseType.SequenceEqual("s32") || baseType.SequenceEqual("__s32") || baseType.SequenceEqual("int32_t"))
            {
                this.SpecifiedFormat = EventHeaderFieldFormat.SignedInt;
                this.SpecifiedEncoding = EventHeaderFieldEncoding.Value32;
            }
            else if (baseType.SequenceEqual("u64") || baseType.SequenceEqual("__u64") || baseType.SequenceEqual("uint64_t"))
            {
                this.SpecifiedFormat = EventHeaderFieldFormat.UnsignedInt;
                this.SpecifiedEncoding = EventHeaderFieldEncoding.Value64;
            }
            else if (baseType.SequenceEqual("s64") || baseType.SequenceEqual("__s64") || baseType.SequenceEqual("int64_t"))
            {
                this.SpecifiedFormat = EventHeaderFieldFormat.SignedInt;
                this.SpecifiedEncoding = EventHeaderFieldEncoding.Value64;
            }
            else
            {
                this.SpecifiedFormat = EventHeaderFieldFormat.HexInt;
                this.SpecifiedEncoding = EventHeaderFieldEncoding.Invalid; // SPECIAL
            }

            // PARSE: Array

            if (this.Size == 0)
            {
                this.Array = PerfFieldArray.RestOfEvent;
            }
            else if (this.Size == 2 && foundRelLoc)
            {
                this.Array = PerfFieldArray.RelLoc2;
            }
            else if (this.Size == 2 && foundDataLoc)
            {
                this.Array = PerfFieldArray.DataLoc2;
            }
            else if (this.Size == 4 && foundRelLoc)
            {
                this.Array = PerfFieldArray.RelLoc4;
            }
            else if (this.Size == 4 && foundDataLoc)
            {
                this.Array = PerfFieldArray.DataLoc4;
            }
            else if (foundArray)
            {
                this.Array = PerfFieldArray.Fixed;
            }
            else
            {
                this.Array = PerfFieldArray.None;
            }

            // DEDUCE: DeducedFormat.

            // Apply the "signed:" property if specified.
            if (this.SpecifiedFormat == EventHeaderFieldFormat.UnsignedInt ||
                this.SpecifiedFormat == EventHeaderFieldFormat.SignedInt)
            {
                // If valid, signed overrides baseType.
                switch (signed)
                {
                    default: this.DeducedFormat = this.SpecifiedFormat; break; // signed == null
                    case false: this.DeducedFormat = EventHeaderFieldFormat.UnsignedInt; break;
                    case true: this.DeducedFormat = EventHeaderFieldFormat.SignedInt; break;
                }
            }
            else
            {
                this.DeducedFormat = this.SpecifiedFormat;
            }

            // DEDUCE: DeducedEncoding, DeducedArrayCount, ElementSizeShift.

            if (this.SpecifiedFormat == EventHeaderFieldFormat.String8)
            {
                Debug.Assert(this.SpecifiedEncoding == EventHeaderFieldEncoding.Value8);
                this.DeducedEncoding = this.Size == 1 ? EventHeaderFieldEncoding.Value8 : EventHeaderFieldEncoding.ZStringChar8;
                this.DeducedArrayCount = 1;
                this.ElementSizeShift = this.Size == 1 ? (byte)0 : byte.MaxValue;
            }
            else if (this.SpecifiedFormat == EventHeaderFieldFormat.HexBytes)
            {
                Debug.Assert(this.SpecifiedEncoding == EventHeaderFieldEncoding.Struct);
                this.DeducedEncoding = this.Size == 1 ? EventHeaderFieldEncoding.Value8 : EventHeaderFieldEncoding.StringLength16Char8;
                this.DeducedArrayCount = 1;
                this.ElementSizeShift = byte.MaxValue;
            }
            else
            {
                switch (this.Array)
                {
                    case PerfFieldArray.None:

                        // Size overrides element size deduced from type name.
                        switch (this.Size)
                        {
                            case 1:
                                this.DeducedEncoding = EventHeaderFieldEncoding.Value8;
                                this.ElementSizeShift = 0;
                                break;
                            case 2:
                                this.DeducedEncoding = EventHeaderFieldEncoding.Value16;
                                this.ElementSizeShift = 1;
                                break;
                            case 4:
                                this.DeducedEncoding = EventHeaderFieldEncoding.Value32;
                                this.ElementSizeShift = 2;
                                break;
                            case 8:
                                this.DeducedEncoding = EventHeaderFieldEncoding.Value64;
                                this.ElementSizeShift = 3;
                                break;
                            default:
                                goto DoHexDump;
                        }

                        this.DeducedArrayCount = 1;
                        break;

                    case PerfFieldArray.Fixed:

                        if (this.SpecifiedArrayCount == 0)
                        {
                            this.DeducedEncoding = this.SpecifiedEncoding | EventHeaderFieldEncoding.CArrayFlag;
                            switch (this.SpecifiedEncoding)
                            {
                                case EventHeaderFieldEncoding.Value8:
                                    this.DeducedArrayCount = this.Size;
                                    this.ElementSizeShift = 0;
                                    break;
                                case EventHeaderFieldEncoding.Value16:
                                    if (this.Size % sizeof(UInt16) != 0)
                                    {
                                        goto DoHexDump;
                                    }
                                    this.DeducedArrayCount = (ushort)(this.Size / sizeof(UInt16));
                                    this.ElementSizeShift = 1;
                                    break;
                                case EventHeaderFieldEncoding.Value32:
                                    if (this.Size % sizeof(UInt32) != 0)
                                    {
                                        goto DoHexDump;
                                    }
                                    this.DeducedArrayCount = (ushort)(this.Size / sizeof(UInt32));
                                    this.ElementSizeShift = 2;
                                    break;
                                case EventHeaderFieldEncoding.Value64:
                                    if (this.Size % sizeof(UInt64) != 0)
                                    {
                                        goto DoHexDump;
                                    }
                                    this.DeducedArrayCount = (ushort)(this.Size / sizeof(UInt64));
                                    this.ElementSizeShift = 3;
                                    break;
                                default:
                                    Debug.Assert(this.SpecifiedEncoding == EventHeaderFieldEncoding.Invalid);
                                    goto DoHexDump;
                            }
                        }
                        else
                        {
                            if (this.Size % this.SpecifiedArrayCount != 0)
                            {
                                goto DoHexDump;
                            }

                            switch (this.Size / this.SpecifiedArrayCount)
                            {
                                case 1:
                                    this.DeducedEncoding = EventHeaderFieldEncoding.Value8 | EventHeaderFieldEncoding.CArrayFlag;
                                    this.ElementSizeShift = 0;
                                    break;
                                case 2:
                                    this.DeducedEncoding = EventHeaderFieldEncoding.Value16 | EventHeaderFieldEncoding.CArrayFlag;
                                    this.ElementSizeShift = 1;
                                    break;
                                case 4:
                                    this.DeducedEncoding = EventHeaderFieldEncoding.Value32 | EventHeaderFieldEncoding.CArrayFlag;
                                    this.ElementSizeShift = 2;
                                    break;
                                case 8:
                                    this.DeducedEncoding = EventHeaderFieldEncoding.Value64 | EventHeaderFieldEncoding.CArrayFlag;
                                    this.ElementSizeShift = 3;
                                    break;
                                default:
                                    goto DoHexDump;
                            }

                            this.DeducedArrayCount = this.SpecifiedArrayCount;
                        }
                        break;

                    default:

                        // Variable-length data.

                        switch (this.SpecifiedEncoding)
                        {
                            case EventHeaderFieldEncoding.Value8:
                                this.ElementSizeShift = 0;
                                break;
                            case EventHeaderFieldEncoding.Value16:
                                this.ElementSizeShift = 1;
                                break;
                            case EventHeaderFieldEncoding.Value32:
                                this.ElementSizeShift = 2;
                                break;
                            case EventHeaderFieldEncoding.Value64:
                                this.ElementSizeShift = 3;
                                break;
                            default:
                                Debug.Assert(this.SpecifiedEncoding == EventHeaderFieldEncoding.Invalid);
                                goto DoHexDump;
                        }

                        this.DeducedEncoding = this.SpecifiedEncoding | EventHeaderFieldEncoding.VArrayFlag;
                        this.DeducedArrayCount = 0;
                        break;

                    DoHexDump:

                        this.DeducedEncoding = EventHeaderFieldEncoding.StringLength16Char8;
                        this.DeducedFormat = EventHeaderFieldFormat.HexBytes;
                        this.DeducedArrayCount = 1;
                        this.ElementSizeShift = byte.MaxValue;
                        break;
                }
            }

#if DEBUG
            Debug.Assert(this.Name.Length > 0);
            Debug.Assert(this.Field.Length > 0);

            var encodingValue = this.DeducedEncoding.BaseEncoding();
            switch (encodingValue)
            {
                case EventHeaderFieldEncoding.Value8:
                    if (this.DeducedArrayCount != 0)
                    {
                        Debug.Assert(this.Size == this.DeducedArrayCount * sizeof(byte));
                    }
                    Debug.Assert(this.ElementSizeShift == 0);
                    break;
                case EventHeaderFieldEncoding.Value16:
                    if (this.DeducedArrayCount != 0)
                    {
                        Debug.Assert(this.Size == this.DeducedArrayCount * sizeof(UInt16));
                    }
                    Debug.Assert(this.ElementSizeShift == 1);
                    break;
                case EventHeaderFieldEncoding.Value32:
                    if (this.DeducedArrayCount != 0)
                    {
                        Debug.Assert(this.Size == this.DeducedArrayCount * sizeof(UInt32));
                    }
                    Debug.Assert(this.ElementSizeShift == 2);
                    break;
                case EventHeaderFieldEncoding.Value64:
                    if (this.DeducedArrayCount != 0)
                    {
                        Debug.Assert(this.Size == this.DeducedArrayCount * sizeof(UInt64));
                    }
                    Debug.Assert(this.ElementSizeShift == 3);
                    break;
                case EventHeaderFieldEncoding.StringLength16Char8:
                    Debug.Assert(this.DeducedArrayCount == 1);
                    Debug.Assert((this.DeducedEncoding & EventHeaderFieldEncoding.FlagMask) == 0);
                    Debug.Assert(this.DeducedFormat == EventHeaderFieldFormat.HexBytes);
                    Debug.Assert(this.ElementSizeShift == byte.MaxValue);
                    break;
                case EventHeaderFieldEncoding.ZStringChar8:
                    Debug.Assert(this.DeducedArrayCount == 1);
                    Debug.Assert((this.DeducedEncoding & EventHeaderFieldEncoding.FlagMask) == 0);
                    Debug.Assert(this.DeducedFormat == EventHeaderFieldFormat.String8);
                    Debug.Assert(this.ElementSizeShift == byte.MaxValue);
                    break;
                default:
                    Debug.Fail("Unexpected DeducedEncoding type");
                    break;
            }

            var encodingFlags = this.DeducedEncoding & EventHeaderFieldEncoding.FlagMask;
            switch (encodingFlags)
            {
                case 0:
                    Debug.Assert(this.DeducedArrayCount == 1);
                    break;
                case EventHeaderFieldEncoding.VArrayFlag:
                    Debug.Assert(this.DeducedArrayCount == 0);
                    break;
                case EventHeaderFieldEncoding.CArrayFlag:
                    Debug.Assert(this.DeducedArrayCount >= 1);
                    break;
                default:
                    Debug.Fail("Unexpected DeducedEncoding flags");
                    break;
            }

            switch (this.DeducedFormat)
            {
                case EventHeaderFieldFormat.UnsignedInt:
                case EventHeaderFieldFormat.SignedInt:
                case EventHeaderFieldFormat.HexInt:
                    Debug.Assert(encodingValue >= EventHeaderFieldEncoding.Value8);
                    Debug.Assert(encodingValue <= EventHeaderFieldEncoding.Value64);
                    break;
                case EventHeaderFieldFormat.HexBytes:
                    Debug.Assert(encodingValue == EventHeaderFieldEncoding.StringLength16Char8);
                    break;
                case EventHeaderFieldFormat.String8:
                    Debug.Assert(
                        encodingValue == EventHeaderFieldEncoding.Value8 ||
                        encodingValue == EventHeaderFieldEncoding.ZStringChar8);
                    break;
                default:
                    Debug.Fail("Unexpected DeducedFormat type");
                    break;
            }
#endif

            return;
        }

        /// <summary>
        /// Name of the field, or "noname" if unable to determine the name.
        /// (Parsed from Field, e.g. if Field = "char my_field[8]" then Name = "my_field".)
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Field declaration in pseudo-C syntax, e.g. "char my_field[8]".
        /// (Value of the format's "field:" property.)
        /// </summary>
        public string Field { get; }

        /// <summary>
        /// The byte offset of the start of the field data from the start of
        /// the event raw data. (Value of the format's "offset:" property.)
        /// </summary>
        public ushort Offset { get; }

        /// <summary>
        /// The byte size of the field data. May be 0 to indicate "rest of event".
        /// (Value of the format's "size:" property.)
        /// </summary>
        public ushort Size { get; }

        /// <summary>
        /// Whether the field is signed; null if unspecified.
        /// (Value of the format's "signed:" property.)
        /// </summary>
        public bool? Signed { get; }

        /// <summary>
        /// The number of elements in this field, as specified in the field property,
        /// or 0 if no array count was found.
        /// (Parsed from Field, e.g. if Field = "char my_field[8]" then SpecifiedArrayCount = 8.)
        /// </summary>
        public ushort SpecifiedArrayCount { get; }

        /// <summary>
        /// The number of elements in this field, as deduced from field and size.
        /// If the field is not being treated as an array (i.e. a single item, or
        /// an array that is being treated as a string or a blob), this will be 1.
        /// If the field is a variable-length array, this will be 0.
        /// </summary>
        public ushort DeducedArrayCount { get; }

        /// <summary>
        /// The encoding of the field's base type, as specified in the field property.
        /// This may be Value8, Value16, Value32, Value64, Struct, or Invalid if no
        /// recognized encoding was found.
        /// (Parsed from Field, e.g. if Field = "char my_field[8]" then base type is
        /// "char" so Encoding = "Value8".)
        /// </summary>
        public EventHeaderFieldEncoding SpecifiedEncoding { get; }

        /// <summary>
        /// The encoding of the field's base type, as deduced from field and size.
        /// This will be Value8, Value16, Value32, Value64, ZStringChar8 for a
        /// nul-terminated string, or StringLength16Char8 for a binary blob.
        /// The VArrayFlag flag or the CArrayFlag flag may be set for Value8,
        /// Value16, Value32, and Value64.
        /// </summary>
        public EventHeaderFieldEncoding DeducedEncoding { get; }

        /// <summary>
        /// The format of the field's base type, as specified by the field and signed properties.
        /// This will be UnsignedInt, SignedInt, HexInt, String8, or HexBytes.
        /// (Parsed from Field, e.g. if Field = "char my_field[8]" then base type is
        /// "char" so Format = "String8".)
        /// </summary>
        public EventHeaderFieldFormat SpecifiedFormat { get; }

        /// <summary>
        /// The format of the field's base type, as deduced from field, size, and signed.
        /// </summary>
        public EventHeaderFieldFormat DeducedFormat { get; }

        /// <summary>
        /// The kind of array this field is, as specified in the field property.
        /// (Parsed from Field and Size, e.g. if Field = "char my_field[8]" then Array = Fixed.)
        /// </summary>
        public PerfFieldArray Array { get; }

        /// <summary>
        /// For string or blob, this is byte.MaxValue.
        /// For other types, ElementSizeShift is the log2 of the size of each element
        /// in the field. If the field is a single N-bit integer or an array of N-bit
        /// integers, ElementSizeShift is: 0 for 8-bit integers, 1 for 16-bit integers,
        /// 2 for 32-bit integers, and 3 for 64-bit integers.
        /// </summary>
        public byte ElementSizeShift { get; }

        /// <summary>
        /// <para>
        /// Parses a line of the "format:" section of an event's "format" file. The
        /// formatLine string will generally look like
        /// "[whitespace?]field:[declaration]; offset:[number]; size:[number]; ...".
        ///</para><para>
        /// If "field:" is non-empty, "offset:" is a valid unsigned integer, and
        /// "size:" is a valid unsigned integer, returns
        /// PerfFieldFormat(field, offset, size, signed). Otherwise,  returns null.
        /// </para><para>
        /// Note that You'll usually use PerfEventFormat.Parse to parse the entire format
        /// file rather than calling this method directly.
        ///</para>
        /// </summary>
        public static PerfFieldFormat? Parse(
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

            PerfFieldFormat? result;
            if (field.Length == 0 || !foundOffset || !foundSize)
            {
                result = null;
            }
            else
            {
                result = new PerfFieldFormat(longSize64, field.ToString(), offset, size, isSigned);
            }

            return result;
        }

        /// <summary>
        /// <para>
        /// Given the event's raw data (e.g. PerfSampleEventInfo::RawData), return
        /// this field's raw data. Returns null for out of bounds.
        ///</para><para>
        /// Does not do any byte-swapping. This method uses byteReader to resolve
        /// data_loc and rel_loc references, not to fix up the field data.
        ///</para><para>
        /// Note that in some cases, the size returned by GetFieldBytes may be
        /// different from the value returned by Size():
        /// <list type="bullet"><item>
        /// If eventRawDataSize &lt; Offset() + Size(), returns {}.
        /// </item><item>
        /// If Size() == 0, returns all data from offset to the end of the event,
        /// i.e. it returns eventRawDataSize - Offset() bytes.
        /// </item><item>
        /// If Array() is Dynamic or RelDyn, the returned size depends on the
        /// event contents.
        /// </item></list>
        /// </para>
        /// </summary>
        public ReadOnlySpan<byte> GetFieldBytes(
            ReadOnlySpan<byte> eventRawData,
            PerfByteReader byteReader)
        {
            if (this.Offset + this.Size <= eventRawData.Length)
            {
                int dynOffset, dynSize;
                switch (this.Array)
                {
                    case PerfFieldArray.None:
                    case PerfFieldArray.Fixed:

                        return eventRawData.Slice(this.Offset, this.Size);

                    case PerfFieldArray.RestOfEvent:

                        return eventRawData.Slice(this.Offset);

                    case PerfFieldArray.DataLoc2:
                    case PerfFieldArray.RelLoc2:

                        // 2-byte value is an offset leading to the real data, size is strlen.
                        dynOffset = byteReader.ReadU16(eventRawData.Slice(this.Offset));
                        if (this.Array == PerfFieldArray.RelLoc2)
                        {
                            // offset is relative to end of field.
                            dynOffset += this.Offset + this.Size;
                        }

                        if (dynOffset < eventRawData.Length)
                        {
                            dynSize = eventRawData.Slice(dynOffset).IndexOf((byte)0);
                            if (dynSize >= 0)
                            {
                                return eventRawData.Slice(dynOffset, dynSize);
                            }
                        }

                        break;

                    case PerfFieldArray.DataLoc4:
                    case PerfFieldArray.RelLoc4:

                        // 4-byte value is an offset/length pair leading to the real data.
                        var dyn32 = byteReader.ReadU32(eventRawData.Slice(this.Offset));
                        dynSize = (int)(dyn32 >> 16);
                        dynOffset = (int)(dyn32 & 0xFFFF);
                        if (this.Array == PerfFieldArray.RelLoc4)
                        {
                            // offset is relative to end of field.
                            dynOffset += this.Offset + this.Size;
                        }

                        if (dynOffset + dynSize <= eventRawData.Length)
                        {
                            return eventRawData.Slice(dynOffset, dynSize);
                        }

                        break;
                }
            }

            return default;
        }

        /// <summary>
        /// Gets the value of this field from the event's raw data.
        /// </summary>
        /// <param name="sampleEventInfo">
        /// Event information, used for sampleEventInfo.RawDataSpan and sampleEventInfo.ByteReader.
        /// </param>
        /// <returns>
        /// A PerfValue with the field value, or an empty PerfValue (result.Encoding == Invalid)
        /// if the event's expected offset exceeds eventRawData.Length.
        /// </returns>
        public PerfValue GetFieldValue(in PerfSampleEventInfo sampleEventInfo)
        {
            return this.GetFieldValue(sampleEventInfo.RawDataSpan, sampleEventInfo.ByteReader);
        }

        /// <summary>
        /// Gets the value of this field from the event's raw data.
        /// </summary>
        /// <param name="eventRawData">Event's "raw" section, e.g. sampleEventInfo.RawDataSpan.</param>
        /// <param name="byteReader">Event's byte order, e.g. sampleEventInfo.ByteReader.</param>
        /// <returns>
        /// A PerfValue with the field value, or an empty PerfValue (result.Encoding == Invalid)
        /// if the event's expected offset exceeds eventRawData.Length.
        /// </returns>
        public PerfValue GetFieldValue(
            ReadOnlySpan<byte> eventRawData,
            PerfByteReader byteReader)
        {
            bool checkStrLen = this.DeducedEncoding == EventHeaderFieldEncoding.ZStringChar8;
            ReadOnlySpan<byte> bytes;
            ushort arrayCount;

            if (this.Offset + this.Size <= eventRawData.Length)
            {
                int dynOffset, dynSize;
                switch (this.Array)
                {
                    case PerfFieldArray.None:
                    case PerfFieldArray.Fixed:

                        bytes = eventRawData.Slice(this.Offset, this.Size);
                        if (checkStrLen)
                        {
                            bytes = UntilFirstNul(bytes);
                        }

                        arrayCount = this.DeducedArrayCount;
                        goto FixedSize;

                    case PerfFieldArray.RestOfEvent:

                        bytes = eventRawData.Slice(this.Offset);
                        goto VariableSize;

                    case PerfFieldArray.DataLoc2:
                    case PerfFieldArray.RelLoc2:

                        // 2-byte value is an offset leading to the real data, size is strlen.
                        dynOffset = byteReader.ReadU16(eventRawData.Slice(this.Offset));
                        if (this.Array == PerfFieldArray.RelLoc2)
                        {
                            // offset is relative to end of field.
                            dynOffset += this.Offset + this.Size;
                        }

                        if (dynOffset < eventRawData.Length)
                        {
                            bytes = eventRawData.Slice(dynOffset);
                            checkStrLen = true;
                            goto VariableSize;
                        }

                        break;

                    case PerfFieldArray.DataLoc4:
                    case PerfFieldArray.RelLoc4:

                        // 4-byte value is an offset/length pair leading to the real data.
                        var dyn32 = byteReader.ReadU32(eventRawData.Slice(this.Offset));
                        dynSize = (int)(dyn32 >> 16);
                        dynOffset = (int)(dyn32 & 0xFFFF);
                        if (this.Array == PerfFieldArray.RelLoc4)
                        {
                            // offset is relative to end of field.
                            dynOffset += this.Offset + this.Size;
                        }

                        if (dynOffset + dynSize <= eventRawData.Length)
                        {
                            bytes = eventRawData.Slice(dynOffset, dynSize);
                            goto VariableSize;
                        }

                        break;
                }
            }

            return default;

        VariableSize:

            if (checkStrLen)
            {
                bytes = UntilFirstNul(bytes);
            }

            var mask = (1 << this.ElementSizeShift) - 1;
            if (this.ElementSizeShift != byte.MaxValue &&
                0 != (bytes.Length & mask))
            {
                bytes = bytes.Slice(0, bytes.Length & ~mask);
            }

            arrayCount = this.DeducedArrayCount;
            if (arrayCount == 0)
            {
                arrayCount = (ushort)(bytes.Length >> this.ElementSizeShift);
            }

        FixedSize:

            return new PerfValue(
                bytes,
                byteReader,
                this.DeducedEncoding,
                this.DeducedFormat,
                unchecked((byte)(1 << this.ElementSizeShift)),
                arrayCount);
        }

        /// <summary>
        /// Returns this.Field.
        /// </summary>
        public override string ToString()
        {
            return this.Field;
        }

        private static ReadOnlySpan<byte> UntilFirstNul(ReadOnlySpan<byte> bytes)
        {
            var i = bytes.IndexOf((byte)0);
            return i >= 0 ? bytes.Slice(0, i) : bytes;
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
