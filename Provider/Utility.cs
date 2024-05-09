// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Provider;

using Interlocked = System.Threading.Interlocked;

internal static class Utility
{
    /// <summary>
    /// Atomically: old = location; if (old != null) { return old; } else { location = value; return value; }
    /// </summary>
    public static T InterlockedInitSingleton<T>(ref T? location, T value)
        where T : class
    {
        return Interlocked.CompareExchange(ref location, value, null) ?? value;
    }
}
