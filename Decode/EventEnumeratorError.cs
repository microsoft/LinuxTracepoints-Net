// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    /// <summary>
    /// Values for the LastError property of EventEnumerator.
    /// </summary>
    public enum EventEnumeratorError : byte
    {
        /// <summary>
        /// No error.
        /// </summary>
        Success,

        /// <summary>
        /// Event is smaller than 8 bytes or larger than 2GB,
        /// or tracepointName is longer than 255 characters.
        /// </summary>
        InvalidParameter,

        /// <summary>
        /// Event does not follow the EventHeader naming/layout rules,
        /// has unrecognized flags, or has unrecognized types.
        /// </summary>
        NotSupported,

        /// <summary>
        /// Resource usage limit (moveNextLimit) reached.
        /// </summary>
        ImplementationLimit,

        /// <summary>
        /// Event has an out-of-range value.
        /// </summary>
        InvalidData,

        /// <summary>
        /// Event has more than 8 levels of nested structs.
        /// </summary>
        StackOverflow,
    }
}
