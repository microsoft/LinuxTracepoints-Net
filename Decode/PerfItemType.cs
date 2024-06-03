// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System.Diagnostics;

    /// <summary>
    /// Provides access to the metadata
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
    /// </item>
    ///
    /// </list>
    /// </summary>
    public readonly ref struct PerfItemMetadata
    {
        /// <summary>
        /// Initializes a new instance of the PerfItemMetadata struct.
        /// <br/>
        /// These are not normally created directly. You'll normally get instances of this struct from
        /// <see cref="EventHeaderEnumerator"/><c>.GetItemMetadata()</c> or
        /// <see cref="PerfFieldFormat"/><c>.GetFieldValue()</c>.
        /// </summary>
        /// <param name="byteReader">
        /// Reader that is configured for the event data's byte order.
        /// </param>
        /// <param name="encodingAndArrayFlag">
        /// The field encoding, including the appropriate array flag if the field is an array element,
        /// array-begin, or array-end. The chain flag must be unset.
        /// </param>
        /// <param name="format">
        /// The field format. The chain flag must be unset.
        /// </param>
        /// <param name="isScalar">
        /// True if this represents a non-array value or a single element of an array.
        /// False if this represents an array-begin or an array-end.
        /// </param>
        /// <param name="typeSize">
        /// For simple encodings (e.g. Value8, Value16, Value32, Value64, Value128),
        /// this is the size of one element in bytes (1, 2, 4, 8, 16). For complex types
        /// (e.g. Struct or string), this is 0.
        /// </param>
        /// <param name="elementCount">
        /// For array-begin or array-end, this is number of elements in the array.
        /// For non-array or for array element, this is 1.
        /// This may be 0 in the case of a variable-length array of length 0.
        /// </param>
        /// <param name="fieldTag">
        /// Field tag, or 0 if none.
        /// </param>
        public PerfItemMetadata(
            PerfByteReader byteReader,
            EventHeaderFieldEncoding encodingAndArrayFlag,
            EventHeaderFieldFormat format,
            bool isScalar,
            byte typeSize,
            ushort elementCount,
            ushort fieldTag = 0)
        {
            this.ElementCount = elementCount;
            this.FieldTag = fieldTag;
            this.TypeSize = typeSize;
            this.EncodingAndArrayFlagAndIsScalar = encodingAndArrayFlag | (isScalar ? EventHeaderFieldEncoding.ChainFlag : 0);
            this.Format = format;
            this.ByteReader = byteReader;

#if DEBUG
            // chain flag must be masked-out by caller.
            Debug.Assert(!encodingAndArrayFlag.HasChainFlag());
            Debug.Assert(!format.HasChainFlag());

            // Cannot set both VArrayFlag and CArrayFlag.
            Debug.Assert(encodingAndArrayFlag.ArrayFlag() != EventHeaderFieldEncoding.ArrayFlagMask);

            if (isScalar)
            {
                // If scalar, elementCount must be 1.
                Debug.Assert(elementCount == 1);
            }
            else
            {
                // If non-scalar, must be an array.
                Debug.Assert(encodingAndArrayFlag.IsArray());
            }

            if (encodingAndArrayFlag.BaseEncoding() == EventHeaderFieldEncoding.Struct)
            {
                Debug.Assert(typeSize == 0); // Structs are not simple types.
                Debug.Assert(format != 0); // No zero-length structs.
            }
#endif
        }

        /// <summary>
        /// For array-begin or array-end item, this is number of elements in the array.
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
        /// (e.g. Struct or String), this is 0.
        /// </summary>
        public byte TypeSize { get; }

        /// <summary>
        /// Returns Encoding | ArrayFlag | (IsScalar ? ChainFlag : 0).
        /// </summary>
        private EventHeaderFieldEncoding EncodingAndArrayFlagAndIsScalar { get; }

        /// <summary>
        /// Item's underlying encoding. The encoding indicates how to determine the item's
        /// size. The Encoding also implies a default formatting that should be used if
        /// the specified Format is Default (0), unrecognized, or unsupported. The value
        /// returned by this property does not include any flags.
        /// </summary>
        public EventHeaderFieldEncoding Encoding =>
            this.EncodingAndArrayFlagAndIsScalar & EventHeaderFieldEncoding.ValueMask;

        /// <summary>
        /// Returns the field's CArrayFlag or VArrayFlag if the item represents an array-begin
        /// field, an array-end field, or an element within an array field.
        /// Returns 0 for a non-array item.
        /// </summary>
        public EventHeaderFieldEncoding ArrayFlag =>
            this.EncodingAndArrayFlagAndIsScalar & EventHeaderFieldEncoding.ArrayFlagMask;

        /// <summary>
        /// Returns true if this item is a scalar (a non-array field or a single element of an array field).
        /// Returns false if this item is an array (an array-begin or an array-end item).
        /// </summary>
        public bool IsScalar =>
            0 != (this.EncodingAndArrayFlagAndIsScalar & EventHeaderFieldEncoding.ChainFlag);

        /// <summary>
        /// Returns true if this item represents an element within an array.
        /// Returns false if this item is a non-array field, an array-begin, or an array-end.
        /// </summary>
        public bool IsElement
        {
            get
            {
                var enc = this.EncodingAndArrayFlagAndIsScalar;
                return 0 != (enc & EventHeaderFieldEncoding.ChainFlag) && // ItemIsScalar
                    0 != (enc & EventHeaderFieldEncoding.ArrayFlagMask);  // FieldIsArray
            }
        }

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
        /// A PerfByteReader that can be used to fix the byte order of this item's data.
        /// This is the same as PerfByteReader(this.FromBigEndian).
        /// </summary>
        public PerfByteReader ByteReader { get; }

        /// <summary>
        /// True if this item's data uses big-endian byte order.
        /// This is the same as this.ByteReader.FromBigEndian.
        /// </summary>
        public bool FromBigEndian => this.ByteReader.FromBigEndian;
    }
}
