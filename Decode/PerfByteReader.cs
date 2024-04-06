// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using BinaryPrimitives = System.Buffers.Binary.BinaryPrimitives;

    /// <summary>
    /// Helper for working with data that may be in big or little endian format.
    /// </summary>
    public readonly struct PerfByteReader
    {
        /// <summary>
        /// Initializes a new instance of the ByteReader class for converting data from
        /// the specified endianness to the host endianness.
        /// </summary>
        /// <param name="fromBigEndian">
        /// true if input is big-endian, false if input is little-endian.
        /// </param>
        public PerfByteReader(bool fromBigEndian)
        {
            this.FromBigEndian = fromBigEndian;
        }

        /// <summary>
        /// Returns an instance of ByteReader that does not perform any byte swapping,
        /// i.e. where ByteSwapNeeded == false and FromBigEndian == Host.IsBigEndian.
        /// </summary>
        public static PerfByteReader HostEndian => new PerfByteReader(!BitConverter.IsLittleEndian);

        /// <summary>
        /// Returns an instance of ByteReader that performs any byte swapping,
        /// i.e. where ByteSwapNeeded == true and FromBigEndian == Host.IsLittleEndian.
        /// </summary>
        public static PerfByteReader SwapEndian => new PerfByteReader(BitConverter.IsLittleEndian);

        /// <summary>
        /// Returns true if this ByteReader is converting from big-endian to host-endian.
        /// Returns false if this ByteReader is converting from little-endian to host-endian.
        /// </summary>
        public bool FromBigEndian { get; }

        /// <summary>
        /// Returns true if this ByteReader needs to swap bytes to convert input data to
        /// host-endian. Returns false if input data is already in host-endian order.
        /// </summary>
        public bool ByteSwapNeeded => this.FromBigEndian == BitConverter.IsLittleEndian;

        /// <summary>
        /// Reads an Int16 from the specified byte array. Requires bytes.Length >= 2.
        /// </summary>
        public Int16 ReadI16(ReadOnlySpan<byte> bytes)
        {
            return this.FromBigEndian ? BinaryPrimitives.ReadInt16BigEndian(bytes) : BinaryPrimitives.ReadInt16LittleEndian(bytes);
        }

        /// <summary>
        /// Reads a UInt16 from the specified byte array. Requires bytes.Length >= 2.
        /// </summary>
        public UInt16 ReadU16(ReadOnlySpan<byte> bytes)
        {
            return this.FromBigEndian ? BinaryPrimitives.ReadUInt16BigEndian(bytes) : BinaryPrimitives.ReadUInt16LittleEndian(bytes);
        }

        /// <summary>
        /// Reads an Int32 from the specified byte array. Requires bytes.Length >= 4.
        /// </summary>
        public Int32 ReadI32(ReadOnlySpan<byte> bytes)
        {
            return this.FromBigEndian ? BinaryPrimitives.ReadInt32BigEndian(bytes) : BinaryPrimitives.ReadInt32LittleEndian(bytes);
        }

        /// <summary>
        /// Reads a UInt32 from the specified byte array. Requires bytes.Length >= 4.
        /// </summary>
        public UInt32 ReadU32(ReadOnlySpan<byte> bytes)
        {
            return this.FromBigEndian ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        }

        /// <summary>
        /// Reads a Single from the specified byte array. Requires bytes.Length >= 4.
        /// </summary>
        public Single ReadF32(ReadOnlySpan<byte> bytes)
        {
            var val = this.FromBigEndian ? BinaryPrimitives.ReadInt32BigEndian(bytes) : BinaryPrimitives.ReadInt32LittleEndian(bytes);
            return BitConverter.Int32BitsToSingle(val);
        }

        /// <summary>
        /// Reads an Int64 from the specified byte array. Requires bytes.Length >= 8.
        /// </summary>
        public Int64 ReadI64(ReadOnlySpan<byte> bytes)
        {
            return this.FromBigEndian ? BinaryPrimitives.ReadInt64BigEndian(bytes) : BinaryPrimitives.ReadInt64LittleEndian(bytes);
        }

        /// <summary>
        /// Reads a UInt64 from the specified byte array. Requires bytes.Length >= 8.
        /// </summary>
        public UInt64 ReadU64(ReadOnlySpan<byte> bytes)
        {
            return this.FromBigEndian ? BinaryPrimitives.ReadUInt64BigEndian(bytes) : BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        }

        /// <summary>
        /// Reads a Double from the specified byte array. Requires bytes.Length >= 8.
        /// </summary>
        public Double ReadF64(ReadOnlySpan<byte> bytes)
        {
            var val = this.FromBigEndian ? BinaryPrimitives.ReadInt64BigEndian(bytes) : BinaryPrimitives.ReadInt64LittleEndian(bytes);
            return BitConverter.Int64BitsToDouble(val);
        }

        /// <summary>
        /// If ByteSwapNeeded, returns ReverseEndianness(value). Otherwise, returns value.
        /// </summary>
        public UInt16 FixU16(UInt16 value)
        {
            return this.FromBigEndian == BitConverter.IsLittleEndian
                ? BinaryPrimitives.ReverseEndianness(value)
                : value;
        }

        /// <summary>
        /// If ByteSwapNeeded, returns ReverseEndianness(value). Otherwise, returns value.
        /// </summary>
        public UInt32 FixU32(UInt32 value)
        {
            return this.FromBigEndian == BitConverter.IsLittleEndian
                ? BinaryPrimitives.ReverseEndianness(value)
                : value;
        }

        /// <summary>
        /// If ByteSwapNeeded, returns ReverseEndianness(value). Otherwise, returns value.
        /// </summary>
        public UInt64 FixU64(UInt64 value)
        {
            return this.FromBigEndian == BitConverter.IsLittleEndian
                ? BinaryPrimitives.ReverseEndianness(value)
                : value;
        }

        /// <summary>
        /// Returns a string like "FromBigEndian" or "FromLittleEndian".
        /// </summary>
        public override string ToString()
        {
            return this.FromBigEndian ? "FromBigEndian" : "FromLittleEndian";
        }
    }
}
