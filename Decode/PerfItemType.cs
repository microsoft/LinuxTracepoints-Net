// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System.Diagnostics;

    /// <summary>
    /// Provides access to the type of a perf event item. An item is a field of the event
    /// or an element of an array field of the event. The item may represent one of the
    /// following, determined by the context that produced this PerfItemType:
    /// <list type="bullet">
    /// <item>
    /// Scalar (non-array field, or one element of an array field):
    /// ElementCount is 1.
    /// TypeSize is the size of the item's type (itemValue.Bytes.Length == TypeSize) if
    /// the type has a constant size (e.g. a UInt32), or 0 if the type is variable-size
    /// (e.g. a string).
    /// Format is significant.
    /// StructFieldCount should be ignored.
    /// </item>
    /// <item>
    /// The beginning or end of a structure (non-array field, or one element of an array field):
    /// ElementCount is 1.
    /// TypeSize is 0.
    /// Format should be ignored.
    /// StructFieldCount is significant.
    /// </item>
    /// <item>
    /// The beginning of an array of simple type (non-struct, element's type is fixed-size):
    /// ElementCount is the number of elements in the array.
    /// TypeSize is the size of the element's type.
    /// Format is significant.
    /// StructFieldCount should be ignored.
    /// </item>
    /// <item>
    /// The end of an array of simple type:
    /// ElementCount is the number of elements in the array.
    /// TypeSize is the size of the element's type.
    /// Format is significant.
    /// StructFieldCount should be ignored.
    /// </item>
    /// <item>
    /// The beginning or end of an array of complex elements:
    /// ElementCount is the number of elements in the array.
    /// TypeSize is 0.
    /// Either Format or StructFieldCount is significant, depending on whether the Encoding is Struct.
    /// </item>
    /// </list>
    /// </summary>
    public readonly ref struct PerfItemType
    {
        /// <summary>
        /// Initializes a new instance of the EventHeaderItemType struct. These are normally created
        /// by EventHeaderEnumerator.GetItemType().
        /// </summary>
        public PerfItemType(
            PerfByteReader byteReader,
            EventHeaderFieldEncoding encodingAndArrayFlags,
            EventHeaderFieldFormat format,
            byte typeSize,
            ushort elementCount,
            ushort fieldTag = 0)
        {
            this.ElementCount = elementCount;
            this.FieldTag = fieldTag;
            this.TypeSize = typeSize;
            this.EncodingAndArrayFlags = encodingAndArrayFlags;
            this.Format = format;
            this.ByteReader = byteReader;

#if DEBUG
            // Chain flags must be masked-out by caller.
            Debug.Assert(!encodingAndArrayFlags.HasChainFlag());
            Debug.Assert(!format.HasChainFlag());

            // If not an array, elementCount must be 1.
            if ((encodingAndArrayFlags & EventHeaderFieldEncoding.FlagMask) == 0)
            {
                Debug.Assert(elementCount == 1);
            }

            if (encodingAndArrayFlags.BaseEncoding() == EventHeaderFieldEncoding.Struct)
            {
                Debug.Assert(typeSize == 0);
                Debug.Assert(format != 0); // No zero-length structs.
            }
#endif
        }

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
        /// Item's underlying encoding. The encoding indicates how to determine the item's
        /// size. The Encoding also implies a default formatting that should be used if
        /// the specified convertOptions is Default (0), unrecognized, or unsupported. The value
        /// returned by this property does not include any flags.
        /// </summary>
        public EventHeaderFieldEncoding Encoding =>
            this.EncodingAndArrayFlags & EventHeaderFieldEncoding.ValueMask;

        /// <summary>
        /// Contains CArrayFlag or VArrayFlag if the item represents an array begin,
        /// array end, or an element within an array. 0 for a non-array item.
        /// </summary>
        public EventHeaderFieldEncoding ArrayFlags =>
            this.EncodingAndArrayFlags & ~EventHeaderFieldEncoding.ValueMask;

        /// <summary>
        /// Returns Encoding | ArrayFlags.
        /// </summary>
        public EventHeaderFieldEncoding EncodingAndArrayFlags { get; }

        /// <summary>
        /// true if this item represents an array begin, array end, or an element within
        /// an array. false for a non-array item.
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
        /// A ByteReader that can be used to fix the byte order of this item's data.
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
