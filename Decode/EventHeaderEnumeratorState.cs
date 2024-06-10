// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    /// <summary>
    /// Values for the State property of EventHeaderEnumerator.
    /// </summary>
    public enum EventHeaderEnumeratorState : byte
    {
        /// <summary>
        /// After construction, a call to Clear, or a failed StartEvent.
        /// </summary>
        None,

        /// <summary>
        /// After an error has been returned by MoveNext.
        /// </summary>
        Error,

        /// <summary>
        /// Positioned after the last item in the event.
        /// </summary>
        AfterLastItem,

        // MoveNext() is an invalid operation for all states above this line.
        // MoveNext() is a valid operation for all states below this line.

        /// <summary>
        /// Positioned before the first item in the event.
        /// </summary>
        BeforeFirstItem,

        // GetItemInfo() is an invalid operation for all states above this line.
        // GetItemInfo() is a valid operation for all states below this line.

        /// <summary>
        /// Positioned at an item with data (a field or an array element).
        /// </summary>
        Value,

        /// <summary>
        /// Positioned before the first item in an array.
        /// </summary>
        ArrayBegin,

        /// <summary>
        /// Positioned after the last item in an array.
        /// </summary>
        ArrayEnd,

        /// <summary>
        /// Positioned before the first item in a struct.
        /// </summary>
        StructBegin,

        /// <summary>
        /// Positioned after the last item in a struct.
        /// </summary>
        StructEnd,
    }

    /// <summary>
    /// Extension methods for <see cref="EventHeaderEnumeratorState"/>
    /// </summary>
    public static class EventHeaderEnumeratorStateExtensions
    {
        /// <summary>
        /// Returns true if `state >= EventHeaderEnumeratorState.BeforeFirstItem`,
        /// i.e. if <see cref="EventHeaderEnumerator.MoveNext()"/> is a valid operation.
        /// </summary>
        public static bool CanMoveNext(this EventHeaderEnumeratorState state)
        {
            return state >= EventHeaderEnumeratorState.BeforeFirstItem;
        }

        /// <summary>
        /// Returns true if `state > EventHeaderEnumeratorState.BeforeFirstItem`,
        /// i.e. if <see cref="EventHeaderEnumerator.GetItemInfo()"/> is a valid operation.
        /// </summary>
        public static bool CanGetItemInfo(this EventHeaderEnumeratorState state)
        {
            return state > EventHeaderEnumeratorState.BeforeFirstItem;
        }
    }
}
