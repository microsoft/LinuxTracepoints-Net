// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Provider;

using System.Runtime.InteropServices;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct DataSegment
{
    public void* PinnedBase;
    public nuint Length;

    public DataSegment(void* pinnedBase, nuint length)
    {
        this.PinnedBase = pinnedBase;
        this.Length = length;
    }
}
