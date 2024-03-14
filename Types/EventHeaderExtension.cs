// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using InteropServices = System.Runtime.InteropServices;

namespace Microsoft.LinuxTracepoints
{
    /// <summary>
    /// <para>
    /// Additional information for an EventHeader event.
    /// </para><para>
    /// If EventHeader.Flags has the Extension bit set then the EventHeader is
    /// followed by one or more EventHeaderExtension blocks. Otherwise the EventHeader
    /// is followed by the event payload data.
    /// </para><para>
    /// If EventHeaderExtension.Kind has the Chain flag set then the
    /// EventHeaderExtension block is followed immediately (no alignment/padding) by
    /// another extension block. Otherwise it is followed immediately (no
    /// alignment/padding) by the event payload data.
    /// </para>
    /// </summary>
    [InteropServices.StructLayout(InteropServices.LayoutKind.Sequential, Size = 4)]
    public struct EventHeaderExtension
    {
        public ushort Size;
        public EventHeaderExtensionKind Kind;

        // Followed by Size bytes of data. No padding/alignment.
    }
}
