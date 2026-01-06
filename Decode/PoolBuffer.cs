// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma warning disable CA1512 // "Use 'ArgumentOutOfRangeException.ThrowIfGreaterThan'" - requires .net 8+

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Buffers;
    using Debug = System.Diagnostics.Debug;

    /// <summary>
    /// Manages a buffer rented from ArrayPool.
    /// </summary>
    internal sealed class PoolBuffer : IDisposable
    {
        private byte[] array = Array.Empty<byte>();

        /// <summary>
        /// Value is unchecked and does NOT affect capacity.
        /// ValidLength is used by the ValidSpan and ValidMemory properties for the returned size.
        /// </summary>
        public int ValidLength;

        /// <summary>
        /// Gets the current capacity of the buffer.
        /// </summary>
        public int Capacity => this.array.Length;

        /// <summary>
        /// Returns Span(0..ValidLength). Throws unless 0 &lt;= ValidLength &lt;= Capacity.
        /// </summary>
        public Span<byte> ValidSpan => new Span<byte>(this.array, 0, this.ValidLength);

        /// <summary>
        /// Returns Memory(0..ValidLength). Throws unless 0 &lt;= ValidLength &lt;= Capacity.
        /// </summary>
        public Memory<byte> ValidMemory => new Memory<byte>(this.array, 0, this.ValidLength);

        /// <summary>
        /// Returns Span(0..Capacity).
        /// </summary>
        public Span<byte> CapacitySpan => new Span<byte>(this.array);

        /// <summary>
        /// Returns Memory(0..Capacity).
        /// </summary>
        public Memory<byte> CapacityMemory => new Memory<byte>(this.array);

        /// <summary>
        /// Sets ValidLength = 0, Capacity = 0.
        /// </summary>
        public void Dispose()
        {
            this.InvalidateAndTrim(0);
        }

        /// <summary>
        /// Sets ValidLength = 0.
        /// If Capacity > freeIfCapacityGreaterThan, sets Capacity to 0 (frees buffer).
        /// </summary>
        public void InvalidateAndTrim(int freeIfCapacityGreaterThan)
        {
            Debug.Assert(freeIfCapacityGreaterThan >= 0);
            this.ValidLength = 0;
            var oldArray = this.array;
            if (oldArray.Length > freeIfCapacityGreaterThan)
            {
                this.array = Array.Empty<byte>();
                ArrayPool<byte>.Shared.Return(oldArray);
            }
        }

        /// <summary>
        /// Keeps buffer content (0..keepData), ensures Capacity >= minimumCapacity.
        /// Requires 0 &lt;= keepData &lt;= Capacity and keepData &lt;= minimumCapacity.
        /// Throws if minimumCapacity > 1 GB.
        /// </summary>
        public void EnsureCapacity(int minimumCapacity, int keepSize)
        {
            Debug.Assert(0 <= keepSize);
            Debug.Assert(keepSize <= this.Capacity);
            Debug.Assert(keepSize <= minimumCapacity);

            if (this.array.Length < minimumCapacity)
            {
                this.Grow(minimumCapacity, keepSize);
            }

#if DEBUG
            Array.Fill(this.array, (byte)0xCD, keepSize, minimumCapacity - keepSize);
#endif
        }

        private void Grow(int minimumCapacity, int keepSize)
        {
            if (minimumCapacity > 0x40000000)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumCapacity));
            }

            var newArray = ArrayPool<byte>.Shared.Rent(minimumCapacity);
            var oldArray = this.array;
            Array.Copy(oldArray, newArray, keepSize);
            this.array = newArray;

            if (oldArray.Length > 0)
            {
                ArrayPool<byte>.Shared.Return(oldArray);
            }
        }
    }
}
