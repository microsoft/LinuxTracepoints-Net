// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Buffers;
    using Debug = System.Diagnostics.Debug;

    /// <summary>
    /// Manages a buffer (rented from ArrayPool) and its size (exposed as a Memory).
    /// </summary>
    internal struct ArrayMemory : IDisposable
    {
        public Memory<byte> Memory { readonly get; private set; }

        private byte[]? Array { readonly get; set; }

        public void Dispose()
        {
            var array = this.Array;
            if (array != null)
            {
                this = default;
                ArrayPool<byte>.Shared.Return(array);
            }
        }

        /// <summary>
        /// Sets size to 0.
        /// If the allocated memory is larger than the specified capacity, free it.
        /// </summary>
        public void SetSizeTo0AndTrim(int resetIfCapacityGreaterThan)
        {
            this.Memory = default;

            var array = this.Array;
            if (array != null && array.Length > resetIfCapacityGreaterThan)
            {
                this = default;
                ArrayPool<byte>.Shared.Return(array);
            }
        }

        /// <summary>
        /// Sets the size of the memory to the specified size, allocating a new buffer if necessary.
        /// Only the first existingContentSize bytes of the existing buffer are preserved.
        /// Requires existing Memory.Length >= existingContentSize.
        /// Throws if newMemorySize is greater than 1 GB.
        /// Returns the newly-sized Memory.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">newMemorySize is greater than 1 GB</exception>
        public Memory<byte> SetSize(int newMemorySize, int existingContentSize)
        {
            Debug.Assert(existingContentSize <= this.Memory.Length);

            if ((uint)newMemorySize > 0x40000000)
            {
                throw new ArgumentOutOfRangeException(nameof(newMemorySize));
            }

            var array = this.Array;
            if (newMemorySize > (array == null ? 0 : array.Length))
            {
                array = this.Grow(newMemorySize, existingContentSize);
            }

            this.Memory = new Memory<byte>(array, 0, newMemorySize);

#if DEBUG
            // Fill everything with 0xCD except what they asked to preserve.
            this.Memory.Span.Slice(existingContentSize, newMemorySize - existingContentSize).Fill(0xCD);
#endif
            return this.Memory;
        }

        private byte[] Grow(int newMemorySize, int existingContentSize)
        {
            var newArray = ArrayPool<byte>.Shared.Rent(newMemorySize);
            if (existingContentSize > 0)
            {
                this.Memory.Slice(0, existingContentSize).CopyTo(newArray);
            }

            var oldArray = this.Array;
            if (oldArray != null)
            {
                ArrayPool<byte>.Shared.Return(oldArray);
            }

            this.Array = newArray;
            return newArray;
        }
    }
}
