// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Runtime.InteropServices;
    using Debug = System.Diagnostics.Debug;
    using StringBuilder = System.Text.StringBuilder;
    using Text = System.Text;

    /// <summary>
    /// Helper for decoding EventHeader-encoded tracepoint data.
    /// </summary>
    public class EventHeaderEnumerator
    {
        private const EventHeaderFieldEncoding ReadFieldError = EventHeaderFieldEncoding.Invalid;

        /// <summary>
        /// Substate allows us to flatten
        /// "switch (state)    { case X: if (condition) ... }" to
        /// "switch (substate) { case X_condition: ... }"
        /// which potentially improves performance.
        /// </summary>
        private enum SubState : byte
        {
            None,
            Error,
            AfterLastItem,
            BeforeFirstItem,
            Value_Metadata,
            Value_Scalar,
            Value_SimpleArrayElement,
            Value_ComplexArrayElement,
            ArrayBegin,
            ArrayEnd,
            StructBegin,
            StructEnd,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StackEntry
        {
            public const int SizeOfStruct = 16;

            public int NextOffset; // m_eventData[NextOffset] starts next field's name.
            public int NameOffset; // m_eventData[NameOffset] starts current field's name.
            public ushort NameSize; // m_eventData[NameOffset + NameSize + 1] starts current field's type.
            public ushort ArrayIndex;
            public ushort ArrayCount;
            public byte RemainingFieldCount; // Number of NextProperty() calls before popping stack.
            public byte Padding;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Stack
        {
            public const int SizeOfStruct = StackEntry.SizeOfStruct * StructNestLimit;

            public StackEntry E0;
            public StackEntry E1;
            public StackEntry E2;
            public StackEntry E3;
            public StackEntry E4;
            public StackEntry E5;
            public StackEntry E6;
            public StackEntry E7;
        }

        private struct FieldType
        {
            public EventHeaderFieldEncoding Encoding;
            public LinuxTracepoints.EventHeaderFieldFormat Format;
            public ushort Tag;
        }

        // Set by StartEvent:
        private EventHeader m_header;
        private ulong m_keyword;
        private int m_metaBegin; // Relative to m_eventData
        private int m_metaEnd;
        private int m_activityIdBegin; // Relative to m_eventData
        private byte m_activityIdSize;
        private PerfByteReader m_byteReader;
        private ushort m_eventNameSize; // Name starts at m_eventData[m_metaBegin]
        private int m_dataBegin; // Relative to m_eventData
        private ReadOnlyMemory<byte> m_eventData;
        private string m_tracepointName = "";

        // Values change during enumeration:
        private int m_dataPosRaw;
        private int m_moveNextRemaining;
        private StackEntry m_stackTop;
        private byte m_stackIndex; // Number of items currently on stack.
        private EventHeaderEnumeratorState m_state = EventHeaderEnumeratorState.None;
        private SubState m_subState = SubState.None;
        private EventHeaderEnumeratorError m_lastError = EventHeaderEnumeratorError.Success;

        private byte m_elementSize; // 0 if item is variable-size or complex.
        private FieldType m_fieldType; // Note: fieldType.Encoding is cooked.
        private int m_dataPosCooked;
        private int m_itemSizeRaw;
        private int m_itemSizeCooked;

        private Stack m_stack;

        /// <summary>
        /// Default number of MoveNext calls to allow when processing an event.
        /// </summary>
        public const int MoveNextLimitDefault = 4096;

        /// <summary>
        /// Structure nesting limit.
        /// </summary>
        public const int StructNestLimit = 8;

        /// <summary>
        /// Initializes a new instance of EventHeaderEnumerator. Sets State to None.
        /// </summary>
        public EventHeaderEnumerator()
        {
            Debug.Assert(Stack.SizeOfStruct == Marshal.SizeOf<Stack>());
            Debug.Assert(StackEntry.SizeOfStruct == Marshal.SizeOf<StackEntry>());
            Debug.Assert(StackEntry.SizeOfStruct * StructNestLimit == Stack.SizeOfStruct);
        }

        /// <summary>
        /// Returns the current state.
        /// </summary>
        public EventHeaderEnumeratorState State
        {
            get
            {
                return m_state;
            }
        }

        /// <summary>
        /// Gets status for the most recent call to StartEvent, MoveNext, or MoveNextSibling.
        /// </summary>
        public EventHeaderEnumeratorError LastError
        {
            get
            {
                return m_lastError;
            }
        }

        /// <summary>
        /// Sets State to None.
        /// </summary>
        public void Clear()
        {
            SetNoneState(EventHeaderEnumeratorError.Success);
        }

        /// <summary>
        /// Starts decoding the specified EventHeader event: decodes the header and
        /// positions the enumerator before the first item.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On success, changes the state to BeforeFirstItem and returns true.
        /// On failure, changes the state to None (not Error) and returns false.
        /// </para><para>
        /// Note that the enumerator stores a reference to the eventData array but does
        /// not copy it, so the referenced data must remain valid and unchanged while
        /// you are processing the data with this enumerator (i.e. do not overwrite the
        /// buffer until you are done with this event).
        /// </para>
        /// </remarks>
        /// <param name="sampleEventInfo">
        /// Info from which to get the tracepoint name (sampleEventInfo.Format.Name) and
        /// tracepoint user data (sampleEventInfo.UserData).
        /// Requires sampleEventInfo.Format != null.
        /// </param>
        /// <param name="moveNextLimit">
        /// Set to the maximum number of MoveNext calls to allow when processing this event (to
        /// guard against DoS attacks from a maliciously-crafted event).
        /// </param>
        /// <returns>Returns false for failure. Check LastError for details.</returns>
        public bool StartEvent(
            in PerfSampleEventInfo sampleEventInfo,
            int moveNextLimit = MoveNextLimitDefault)
        {
            Debug.Assert(sampleEventInfo.Format != null); // Precondition: not null.
            return StartEvent(sampleEventInfo.Format!.Name, sampleEventInfo.UserData, moveNextLimit);
        }

        /// <summary>
        /// Starts decoding the specified EventHeader event: decodes the header and
        /// positions the enumerator before the first item.
        /// </summary>
        /// <remarks>
        /// <para>
        /// On success, changes the state to BeforeFirstItem and returns true.
        /// On failure, changes the state to None (not Error) and returns false.
        /// </para><para>
        /// Note that the enumerator stores a reference to the eventData array but does
        /// not copy it, so the referenced data must remain valid and unchanged while
        /// you are processing the data with this enumerator (i.e. do not overwrite the
        /// buffer until you are done with this event).
        /// </para>
        /// </remarks>
        /// <param name="tracepointName">
        /// Set the tracepoint name without the system name, e.g. "MyProvider_L4K1" (not
        /// "user_events:MyProvider_L4K1"). Typically this will be <c>perfEventFormat.Name</c>.
        /// </param>
        /// <param name="eventData">
        /// Set to the event's user data, starting at the EventHeaderFlags field. Typically
        /// this will be <c>perfSampleEventInfo.UserData</c>.
        /// </param>
        /// <param name="moveNextLimit">
        /// Set to the maximum number of MoveNext calls to allow when processing this event (to
        /// guard against DoS attacks from a maliciously-crafted event).
        /// </param>
        /// <returns>Returns false for failure. Check LastError for details.</returns>
        public bool StartEvent(
            string tracepointName,
            ReadOnlyMemory<byte> eventData,
            int moveNextLimit = MoveNextLimitDefault)
        {
            const int EventHeaderTracepointNameMax = 256;
            const int SizeofEventHeader = 8;
            const int SizeofEventHeaderExtension = 4;

            const EventHeaderFlags KnownFlags = (
                EventHeaderFlags.Pointer64 | EventHeaderFlags.LittleEndian | EventHeaderFlags.Extension);

            var eventBuf = eventData.Span;
            var eventPos = 0;

            if (eventBuf.Length < SizeofEventHeader ||
                tracepointName.Length >= EventHeaderTracepointNameMax)
            {
                // Event has no header or TracepointName too long.
                return SetNoneState(EventHeaderEnumeratorError.InvalidParameter);
            }

            // Get event header and validate it.

            m_header.Flags = (EventHeaderFlags)eventBuf[eventPos];
            m_byteReader = new PerfByteReader((m_header.Flags & EventHeaderFlags.LittleEndian) == 0);
            eventPos += 1;
            m_header.Version = eventBuf[eventPos];
            eventPos += 1;
            m_header.Id = m_byteReader.ReadU16(eventBuf.Slice(eventPos));
            eventPos += 2;
            m_header.Tag = m_byteReader.ReadU16(eventBuf.Slice(eventPos));
            eventPos += 2;
            m_header.OpcodeByte = eventBuf[eventPos];
            eventPos += 1;
            m_header.LevelByte = eventBuf[eventPos];
            eventPos += 1;

            if (m_header.Flags != (m_header.Flags & KnownFlags))
            {
                // Not a supported event: unsupported flags.
                return SetNoneState(EventHeaderEnumeratorError.NotSupported);
            }

            // Validate Tracepoint name (e.g. "ProviderName_L1K2..."), extract keyword.

            int attribPos = tracepointName.LastIndexOf('_');
            if (attribPos < 0)
            {
                // Not a supported event: no underscore in name.
                return SetNoneState(EventHeaderEnumeratorError.NotSupported);
            }

            attribPos += 1; // Skip underscore.

            if (attribPos >= tracepointName.Length ||
                'L' != tracepointName[attribPos])
            {
                // Not a supported event: no Level in name.
                return SetNoneState(EventHeaderEnumeratorError.NotSupported);
            }

            ulong attribLevel;
            attribPos = LowercaseHexToInt(tracepointName, attribPos + 1, out attribLevel);
            if (attribLevel != (byte)m_header.Level)
            {
                // Not a supported event: name's level != header's level.
                return SetNoneState(EventHeaderEnumeratorError.NotSupported);
            }

            if (attribPos >= tracepointName.Length ||
                'K' != tracepointName[attribPos])
            {
                // Not a supported event: no Keyword in name.
                return SetNoneState(EventHeaderEnumeratorError.NotSupported);
            }

            attribPos = LowercaseHexToInt(tracepointName, attribPos + 1, out m_keyword);

            // Validate but ignore any other attributes.

            while (attribPos < tracepointName.Length)
            {
                char ch;
                ch = tracepointName[attribPos];
                if (ch < 'A' || 'Z' < ch)
                {
                    // Invalid attribute start character.
                    return SetNoneState(EventHeaderEnumeratorError.NotSupported);
                }

                // Skip attribute value chars.
                for (attribPos += 1; attribPos < tracepointName.Length; attribPos += 1)
                {
                    ch = tracepointName[attribPos];
                    if ((ch < '0' || '9' < ch) && (ch < 'a' || 'z' < ch))
                    {
                        break;
                    }
                }
            }

            // Parse header extensions.

            m_metaBegin = 0;
            m_metaEnd = 0;
            m_activityIdBegin = 0;
            m_activityIdSize = 0;

            if (0 != (m_header.Flags & EventHeaderFlags.Extension))
            {
                EventHeaderExtension ext;
                do
                {
                    if (eventBuf.Length - eventPos < SizeofEventHeaderExtension)
                    {
                        return SetNoneState(EventHeaderEnumeratorError.InvalidData);
                    }

                    ext.Size = m_byteReader.ReadU16(eventBuf.Slice(eventPos));
                    eventPos += 2;
                    ext.Kind = (EventHeaderExtensionKind)m_byteReader.ReadU16(eventBuf.Slice(eventPos));
                    eventPos += 2;

                    if (eventBuf.Length - eventPos < ext.Size)
                    {
                        return SetNoneState(EventHeaderEnumeratorError.InvalidData);
                    }

                    switch (ext.Kind & EventHeaderExtensionKind.ValueMask)
                    {
                        case EventHeaderExtensionKind.Invalid: // Invalid extension type.
                            return SetNoneState(EventHeaderEnumeratorError.InvalidData);

                        case EventHeaderExtensionKind.Metadata:
                            if (m_metaBegin != 0)
                            {
                                // Multiple Format extensions.
                                return SetNoneState(EventHeaderEnumeratorError.InvalidData);
                            }

                            m_metaBegin = eventPos;
                            m_metaEnd = m_metaBegin + ext.Size;
                            break;

                        case EventHeaderExtensionKind.ActivityId:
                            if (m_activityIdBegin != 0 ||
                                (ext.Size != 16 && ext.Size != 32))
                            {
                                // Multiple ActivityId extensions, or bad activity id size.
                                return SetNoneState(EventHeaderEnumeratorError.InvalidData);
                            }

                            m_activityIdBegin = eventPos;
                            m_activityIdSize = (byte)ext.Size;
                            break;

                        default:
                            break; // Ignore other extension types.
                    }

                    eventPos += ext.Size;
                }
                while (0 != (ext.Kind & EventHeaderExtensionKind.ChainFlag));
            }

            if (m_metaBegin == 0)
            {
                // Not a supported event - no metadata extension.
                return SetNoneState(EventHeaderEnumeratorError.NotSupported);
            }

            int eventNameSize = eventBuf.Slice(m_metaBegin, m_metaEnd - m_metaBegin).IndexOf((byte)0);
            if (eventNameSize < 0)
            {
                // Event name not nul-terminated.
                return SetNoneState(EventHeaderEnumeratorError.InvalidData);
            }

            m_eventNameSize = (ushort)eventNameSize;
            m_dataBegin = eventPos;
            m_eventData = eventData;
            m_tracepointName = tracepointName;

            ResetImpl(moveNextLimit);
            return true;
        }

        /// <summary>
        /// <para>
        /// Positions the enumerator before the first item.
        /// </para><para>
        /// PRECONDITION: Can be called when State != None, i.e. at any time after a
        /// successful call to StartEvent, until a call to Clear.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">Called in invalid State.</exception>
        public void Reset(int moveNextLimit = MoveNextLimitDefault)
        {
            if (m_state == EventHeaderEnumeratorState.None)
            {
                throw new InvalidOperationException(); // PRECONDITION
            }
            else
            {
                ResetImpl(moveNextLimit);
            }
        }

        /// <summary>
        /// <para>
        /// Moves the enumerator to the next item in the current event, or to the end
        /// of the event if no more items. Returns true if moved to a valid item,
        /// false if no more items or decoding error.
        /// </para><para>
        /// PRECONDITION: Can be called when State >= BeforeFirstItem, i.e. after a
        /// successful call to StartEvent, until MoveNext returns false.
        /// </para></summary>
        /// <remarks><para>
        /// Typically called in a loop until it returns false, e.g.:
        /// </para><code>
        /// if (!e.StartEvent(...)) return e.LastError;
        /// while (e.MoveNext())
        /// {
        ///     EventHeaderItemInfo item = e.GetItemInfo();
        ///     switch (e.State)
        ///     {
        ///     case EventHeaderEnumeratorState.Value:
        ///         DoValue(item);
        ///         break;
        ///     case EventHeaderEnumeratorState.StructBegin:
        ///         DoStructBegin(item);
        ///         break;
        ///     case EventHeaderEnumeratorState.StructEnd:
        ///         DoStructEnd(item);
        ///         break;
        ///     case EventHeaderEnumeratorState.ArrayBegin:
        ///         DoArrayBegin(item);
        ///         break;
        ///     case EventHeaderEnumeratorState.ArrayEnd:
        ///         DoArrayEnd(item);
        ///         break;
        ///     }
        /// }
        /// return e.LastError;
        /// </code>
        /// </remarks>
        /// <returns>
        /// Returns true if moved to a valid item.
        /// Returns false and sets state to AfterLastItem if no more items.
        /// Returns false and sets state to Error for decoding error.
        /// Check LastError for details.
        /// </returns>
        /// <exception cref="InvalidOperationException">Called in invalid State.</exception>
        public bool MoveNext()
        {
            return this.MoveNext(m_eventData.Span);
        }

        /// <summary>
        /// <para>
        /// Advanced scenarios: This is the same as MoveNext() except that it uses the
        /// provided eventDataSpan instead of accessing the Span property of a ReadOnlyMemory
        /// field.
        /// </para><para>
        /// PRECONDITION: Can be called when State >= BeforeFirstItem, i.e. after a
        /// successful call to StartEvent, until MoveNext returns false.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is useful as a performance optimization when you have the eventData span
        /// available and want to avoid the overhead of repeatedly converting from
        /// ReadOnlyMemory to ReadOnlySpan.
        /// </para><para>
        /// The provided eventDataSpan must be equal to the Span property of the eventData
        /// parameter that was passed to StartEvent().
        /// </para>
        /// </remarks>
        public bool MoveNext(ReadOnlySpan<byte> eventDataSpan)
        {
            Debug.Assert(m_eventData.Length == eventDataSpan.Length);

            if (m_state < EventHeaderEnumeratorState.BeforeFirstItem)
            {
                throw new InvalidOperationException(); // PRECONDITION
            }

            if (m_moveNextRemaining == 0)
            {
                return SetErrorState(EventHeaderEnumeratorError.ImplementationLimit);
            }

            m_moveNextRemaining -= 1;

            bool movedToItem;
            switch (m_subState)
            {
                default:

                    Debug.Fail("Unexpected substate.");
                    throw new InvalidOperationException();

                case SubState.BeforeFirstItem:

                    Debug.Assert(m_state == EventHeaderEnumeratorState.BeforeFirstItem);
                    movedToItem = NextProperty(eventDataSpan);
                    break;

                case SubState.Value_Metadata:
                    throw new InvalidOperationException();

                case SubState.Value_Scalar:

                    Debug.Assert(m_state == EventHeaderEnumeratorState.Value);
                    Debug.Assert(m_fieldType.Encoding.BaseEncoding() != EventHeaderFieldEncoding.Struct);
                    Debug.Assert(!m_fieldType.Encoding.IsArray());
                    Debug.Assert(eventDataSpan.Length - m_dataPosRaw >= m_itemSizeRaw);

                    m_dataPosRaw += m_itemSizeRaw;
                    movedToItem = NextProperty(eventDataSpan);
                    break;

                case SubState.Value_SimpleArrayElement:

                    Debug.Assert(m_state == EventHeaderEnumeratorState.Value);
                    Debug.Assert(m_fieldType.Encoding.BaseEncoding() != EventHeaderFieldEncoding.Struct);
                    Debug.Assert(m_fieldType.Encoding.IsArray());
                    Debug.Assert(m_stackTop.ArrayIndex < m_stackTop.ArrayCount);
                    Debug.Assert(m_elementSize != 0); // Eligible for fast path.
                    Debug.Assert(eventDataSpan.Length - m_dataPosRaw >= m_itemSizeRaw);

                    m_dataPosRaw += m_itemSizeRaw;
                    m_stackTop.ArrayIndex += 1;

                    if (m_stackTop.ArrayCount == m_stackTop.ArrayIndex)
                    {
                        // End of array.
                        SetEndState(EventHeaderEnumeratorState.ArrayEnd, SubState.ArrayEnd);
                    }
                    else
                    {
                        // Middle of array - get next element.
                        StartValueSimple(); // Fast path for simple array elements.
                    }

                    movedToItem = true;
                    break;

                case SubState.Value_ComplexArrayElement:

                    Debug.Assert(m_state == EventHeaderEnumeratorState.Value);
                    Debug.Assert(m_fieldType.Encoding.BaseEncoding() != EventHeaderFieldEncoding.Struct);
                    Debug.Assert(m_fieldType.Encoding.IsArray());
                    Debug.Assert(m_stackTop.ArrayIndex < m_stackTop.ArrayCount);
                    Debug.Assert(m_elementSize == 0); // Not eligible for fast path.
                    Debug.Assert(eventDataSpan.Length - m_dataPosRaw >= m_itemSizeRaw);

                    m_dataPosRaw += m_itemSizeRaw;
                    m_stackTop.ArrayIndex += 1;

                    if (m_stackTop.ArrayCount == m_stackTop.ArrayIndex)
                    {
                        // End of array.
                        SetEndState(EventHeaderEnumeratorState.ArrayEnd, SubState.ArrayEnd);
                        movedToItem = true;
                    }
                    else
                    {
                        // Middle of array - get next element.
                        movedToItem = StartValue(eventDataSpan); // Normal path for complex array elements.
                    }

                    break;

                case SubState.ArrayBegin:

                    Debug.Assert(m_state == EventHeaderEnumeratorState.ArrayBegin);
                    Debug.Assert(m_fieldType.Encoding.IsArray());
                    Debug.Assert(m_stackTop.ArrayIndex == 0);

                    if (m_stackTop.ArrayCount == 0)
                    {
                        // 0-length array.
                        SetEndState(EventHeaderEnumeratorState.ArrayEnd, SubState.ArrayEnd);
                        movedToItem = true;
                    }
                    else if (m_elementSize != 0)
                    {
                        // First element of simple array.
                        Debug.Assert(m_fieldType.Encoding.BaseEncoding() != EventHeaderFieldEncoding.Struct);
                        m_itemSizeCooked = m_elementSize;
                        m_itemSizeRaw = m_elementSize;
                        SetState(EventHeaderEnumeratorState.Value, SubState.Value_SimpleArrayElement);
                        StartValueSimple();
                        movedToItem = true;
                    }
                    else if (m_fieldType.Encoding.BaseEncoding() != EventHeaderFieldEncoding.Struct)
                    {
                        // First element of complex array.
                        SetState(EventHeaderEnumeratorState.Value, SubState.Value_ComplexArrayElement);
                        movedToItem = StartValue(eventDataSpan);
                    }
                    else
                    {
                        // First element of array of struct.
                        StartStruct();
                        movedToItem = true;
                    }

                    break;

                case SubState.ArrayEnd:

                    Debug.Assert(m_state == EventHeaderEnumeratorState.ArrayEnd);
                    Debug.Assert(m_fieldType.Encoding.IsArray());
                    Debug.Assert(m_stackTop.ArrayCount == m_stackTop.ArrayIndex);

                    // 0-length array of struct means we won't naturally traverse
                    // the child struct's metadata. Since m_stackTop.NextOffset
                    // won't get updated naturally, we need to update it manually.
                    if (m_fieldType.Encoding.BaseEncoding() == EventHeaderFieldEncoding.Struct &&
                        m_stackTop.ArrayCount == 0 &&
                        !SkipStructMetadata(eventDataSpan))
                    {
                        movedToItem = false;
                    }
                    else
                    {
                        movedToItem = NextProperty(eventDataSpan);
                    }

                    break;

                case SubState.StructBegin:

                    Debug.Assert(m_state == EventHeaderEnumeratorState.StructBegin);
                    if (m_stackIndex >= StructNestLimit)
                    {
                        movedToItem = SetErrorState(EventHeaderEnumeratorError.StackOverflow);
                    }
                    else
                    {
                        var stack = MemoryMarshal.CreateSpan(ref m_stack.E0, StructNestLimit);
                        stack[m_stackIndex] = m_stackTop;
                        m_stackIndex += 1;

                        m_stackTop.RemainingFieldCount = (byte)m_fieldType.Format;
                        // Parent's NextOffset is the correct starting point for the struct.
                        movedToItem = NextProperty(eventDataSpan);
                    }

                    break;

                case SubState.StructEnd:

                    Debug.Assert(m_state == EventHeaderEnumeratorState.StructEnd);
                    Debug.Assert(m_fieldType.Encoding.BaseEncoding() == EventHeaderFieldEncoding.Struct);
                    Debug.Assert(m_itemSizeRaw == 0);

                    m_stackTop.ArrayIndex += 1;

                    if (m_stackTop.ArrayCount != m_stackTop.ArrayIndex)
                    {
                        Debug.Assert(m_fieldType.Encoding.IsArray());
                        Debug.Assert(m_stackTop.ArrayIndex < m_stackTop.ArrayCount);

                        // Middle of array - get next element.
                        StartStruct();
                        movedToItem = true;
                    }
                    else if (m_fieldType.Encoding.IsArray())
                    {
                        // End of array.
                        SetEndState(EventHeaderEnumeratorState.ArrayEnd, SubState.ArrayEnd);
                        movedToItem = true;
                    }
                    else
                    {
                        // End of property - move to next property.
                        movedToItem = NextProperty(eventDataSpan);
                    }

                    break;
            }

            return movedToItem;
        }

        /// <summary>
        /// <para>
        /// Moves the enumerator to the next sibling of the current item, or to the end
        /// of the event if no more items. Returns true if moved to a valid item, false
        /// if no more items or decoding error.
        /// </para>
        /// <list type="bullet"><item>
        /// If the current item is ArrayBegin or StructBegin, this efficiently moves
        /// enumeration to AFTER the corresponding ArrayEnd or StructEnd.
        /// </item><item>
        /// Otherwise, this is the same as MoveNext.
        /// </item></list>
        /// <para>
        /// PRECONDITION: Can be called when State >= BeforeFirstItem, i.e. after a
        /// successful call to StartEvent, until MoveNext returns false.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// Typical use for this method is to efficiently skip past an array of fixed-size
        /// items (i.e. an array where TypeSize is nonzero) when you process all of the
        /// array items within the ArrayBegin state.
        /// </para><code>
        /// if (!e.StartEvent(...)) return e.LastError;
        /// if (!e.MoveNext()) return e.LastError;  // AfterLastItem or Error.
        /// while (true)
        /// {
        ///     EventHeaderItemInfo item = e.GetItemInfo();
        ///     switch (e.State)
        ///     {
        ///     case EventHeaderEnumeratorState.Value:
        ///         DoValue(item);
        ///         break;
        ///     case EventHeaderEnumeratorState.StructBegin:
        ///         DoStructBegin(item);
        ///         break;
        ///     case EventHeaderEnumeratorState.StructEnd:
        ///         DoStructEnd(item);
        ///         break;
        ///     case EventHeaderEnumeratorState.ArrayBegin:
        ///         if (item.TypeSize == 0)
        ///         {
        ///             DoComplexArrayBegin(item);
        ///         }
        ///         else
        ///         {
        ///             // Process the entire array directly without using the enumerator.
        ///             DoSimpleArrayBegin(item);
        ///             for (int i = 0; i != item.ElementCount; i++)
        ///             {
        ///                 DoSimpleArrayElement(item, i);
        ///             }
        ///             DoSimpleArrayEnd(item);
        /// 
        ///             // Skip the entire array at once.
        ///             if (!e.MoveNextSibling()) // Instead of MoveNext().
        ///             {
        ///                 return e.LastError;
        ///             }
        ///             continue; // Skip the MoveNext().
        ///         }
        ///         break;
        ///     case EventHeaderEnumeratorState.ArrayEnd:
        ///         DoComplexArrayEnd(item);
        ///         break;
        ///     }
        /// 
        ///     if (!e.MoveNext())
        ///     {
        ///         return e.LastError;
        ///     }
        /// }
        /// </code>
        /// </remarks>
        /// <returns>
        /// Returns true if moved to a valid item.
        /// Returns false and sets state to AfterLastItem if no more items.
        /// Returns false and sets state to Error for decoding error.
        /// Check LastError for details.
        /// </returns>
        /// <exception cref="InvalidOperationException">Called in invalid State.</exception>
        public bool MoveNextSibling()
        {
            return this.MoveNextSibling(m_eventData.Span);
        }

        /// <summary>
        /// <para>
        /// Advanced scenarios: This is the same as MoveNextSibling() except that it uses the
        /// provided eventDataSpan instead of accessing the Span property of a ReadOnlyMemory
        /// field.
        /// </para><para>
        /// PRECONDITION: Can be called when State >= BeforeFirstItem, i.e. after a
        /// successful call to StartEvent, until MoveNext returns false.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is useful as a performance optimization when you have the eventData span
        /// available and want to avoid the overhead of repeatedly converting from
        /// ReadOnlyMemory to ReadOnlySpan.
        /// </para><para>
        /// The provided eventDataSpan must be equal to the Span property of the eventData
        /// parameter that was passed to StartEvent().
        /// </para>
        /// </remarks>
        public bool MoveNextSibling(ReadOnlySpan<byte> eventDataSpan)
        {
            Debug.Assert(m_eventData.Length == eventDataSpan.Length);

            bool movedToItem;
            int depth = 0; // May reach -1 if we start on ArrayEnd/StructEnd.
            do
            {
                switch (m_state)
                {
                    default:
                        // Same as MoveNext.
                        break;

                    case EventHeaderEnumeratorState.ArrayEnd:
                    case EventHeaderEnumeratorState.StructEnd:
                        depth -= 1;
                        break;

                    case EventHeaderEnumeratorState.StructBegin:
                        depth += 1;
                        break;

                    case EventHeaderEnumeratorState.ArrayBegin:
                        if (m_elementSize == 0 || m_moveNextRemaining == 0)
                        {
                            // Use MoveNext for full processing.
                            depth += 1;
                            break;
                        }
                        else
                        {
                            // Array of simple elements - jump directly to next sibling.
                            Debug.Assert(m_subState == SubState.ArrayBegin);
                            Debug.Assert(m_fieldType.Encoding.BaseEncoding() != EventHeaderFieldEncoding.Struct);
                            Debug.Assert(m_fieldType.Encoding.IsArray());
                            Debug.Assert(m_stackTop.ArrayIndex == 0);
                            m_dataPosRaw += m_stackTop.ArrayCount * m_elementSize;
                            m_moveNextRemaining -= 1;
                            movedToItem = NextProperty(eventDataSpan);
                            continue; // Skip MoveNext().
                        }
                }

                movedToItem = MoveNext(eventDataSpan);
            } while (movedToItem && depth > 0);

            return movedToItem;
        }

        /// <summary>
        /// <para>
        /// Advanced scenarios. This method is for extracting type information from an
        /// event without looking at value information. Moves the enumerator to the next
        /// field declaration (not the next field value). Returns true if moved to a valid
        /// item, false if no more items or decoding error.
        /// </para><para>
        /// PRECONDITION: Can be called after a successful call to StartEvent, until
        /// MoveNextMetadata returns false.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// Note that metadata enumeration gives a flat view of arrays and structures.
        /// There are only Value and ArrayBegin items, no ArrayEnd, StructBegin, StructEnd.
        /// A struct shows up as a value with Encoding = Struct (Format holds field count).
        /// An array shows up as an ArrayBegin with ArrayFlags != 0, and ElementCount is either zero
        /// (indicating a runtime-variable array length) or nonzero (indicating a compile-time
        /// constant array length). An array of struct is a ArrayBegin with Encoding = Struct and
        /// ArrayFlags != 0. ValueBytes will always be empty. ArrayIndex and TypeSize
        /// will always be zero.
        /// </para><para>
        /// Note that when enumerating metadata for a structure, the enumeration may end before
        /// the expected number of fields are seen. This is a supported scenario and is not an
        /// error in the event. A large field count just means "this structure contains all the
        /// remaining fields in the event".
        /// </para><para>
        /// Typically called in a loop until it returns false.
        /// </para><code>
        /// if (!e.StartEvent(...)) return e.LastError;
        /// while (e.MoveNextMetadata())
        /// {
        ///     DoFieldDeclaration(e.GetItemInfo());
        /// }
        /// return e.LastError;
        /// </code>
        /// </remarks>
        /// <returns>
        /// Returns true if moved to a valid item.
        /// Returns false and sets state to AfterLastItem if no more items.
        /// Returns false and sets state to Error for decoding error.
        /// Check LastError for details.
        /// </returns>
        public bool MoveNextMetadata()
        {
            return this.MoveNextMetadata(m_eventData.Span);
        }

        /// <summary>
        /// <para>
        /// Advanced scenarios: This is the same as MoveNextMetadata() except that it uses the
        /// provided eventDataSpan instead of accessing the Span property of a ReadOnlyMemory
        /// field.
        /// </para><para>
        /// PRECONDITION: Can be called after a successful call to StartEvent, until
        /// MoveNextMetadata returns false.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is useful as a performance optimization when you have the eventData span
        /// available and want to avoid the overhead of repeatedly converting from
        /// ReadOnlyMemory to ReadOnlySpan.
        /// </para><para>
        /// The provided eventDataSpan must be equal to the Span property of the eventData
        /// parameter that was passed to StartEvent().
        /// </para>
        /// </remarks>
        public bool MoveNextMetadata(ReadOnlySpan<byte> eventDataSpan)
        {
            Debug.Assert(m_eventData.Length == eventDataSpan.Length);

            if (m_subState != SubState.Value_Metadata)
            {
                if (m_state != EventHeaderEnumeratorState.BeforeFirstItem)
                {
                    throw new InvalidOperationException(); // PRECONDITION
                }

                Debug.Assert(m_subState == SubState.BeforeFirstItem);
                m_stackTop.ArrayIndex = 0;
                m_dataPosCooked = eventDataSpan.Length;
                m_itemSizeCooked = 0;
                m_elementSize = 0;
                SetState(EventHeaderEnumeratorState.Value, SubState.Value_Metadata);
            }

            Debug.Assert(
                m_state == EventHeaderEnumeratorState.Value ||
                m_state == EventHeaderEnumeratorState.ArrayBegin);

            bool movedToItem;
            if (m_stackTop.NextOffset != m_metaEnd)
            {
                m_stackTop.NameOffset = m_stackTop.NextOffset;

                m_fieldType = ReadFieldNameAndType(eventDataSpan);
                if (m_fieldType.Encoding == ReadFieldError)
                {
                    movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                }
                else if (
                    EventHeaderFieldEncoding.Struct == m_fieldType.Encoding.BaseEncoding() &&
                    m_fieldType.Format == 0)
                {
                    // Struct must have at least 1 field (potential for DoS).
                    movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                }
                else if (!m_fieldType.Encoding.IsArray())
                {
                    // Non-array.

                    m_stackTop.ArrayCount = 1;
                    movedToItem = true;
                    SetState(EventHeaderEnumeratorState.Value, SubState.Value_Metadata);
                }
                else if (m_fieldType.Encoding.IsVArray())
                {
                    // Runtime-variable array length.

                    m_stackTop.ArrayCount = 0;
                    movedToItem = true;
                    SetState(EventHeaderEnumeratorState.ArrayBegin, SubState.Value_Metadata);
                }
                else if (m_fieldType.Encoding.IsCArray())
                {
                    // Compile-time-constant array length.

                    if (m_metaEnd - m_stackTop.NextOffset < 2)
                    {
                        movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                    }
                    else
                    {
                        m_stackTop.ArrayCount = m_byteReader.ReadU16(eventDataSpan.Slice(m_stackTop.NextOffset));
                        m_stackTop.NextOffset += 2;

                        if (m_stackTop.ArrayCount == 0)
                        {
                            // Constant-length array cannot have length of 0 (potential for DoS).
                            movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                        }
                        else
                        {
                            movedToItem = true;
                            SetState(EventHeaderEnumeratorState.ArrayBegin, SubState.Value_Metadata);
                        }
                    }
                }
                else
                {
                    movedToItem = SetErrorState(EventHeaderEnumeratorError.NotSupported);
                }
            }
            else
            {
                // End of event.

                SetEndState(EventHeaderEnumeratorState.AfterLastItem, SubState.AfterLastItem);
                movedToItem = false; // No more items.
            }

            return movedToItem;
        }

        /// <summary>
        /// <para>
        /// Gets information that applies to the current event, e.g. the event name,
        /// provider name, options, level, keyword, etc.
        /// </para><para>
        /// PRECONDITION: Can be called when State != None, i.e. at any time after a
        /// successful call to StartEvent, until a call to Clear.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">Called in invalid State.</exception>
        public EventHeaderEventInfo GetEventInfo()
        {
            return this.GetEventInfo(m_eventData.Span);
        }

        /// <summary>
        /// <para>
        /// Advanced scenarios: This is the same as GetEventInfo() except that it uses the
        /// provided eventDataSpan instead of accessing the Span property of a ReadOnlyMemory
        /// field.
        /// </para><para>
        /// PRECONDITION: Can be called when State != None, i.e. at any time after a
        /// successful call to StartEvent, until a call to Clear.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is useful as a performance optimization when you have the eventData span
        /// available and want to avoid the overhead of repeatedly converting from
        /// ReadOnlyMemory to ReadOnlySpan.
        /// </para><para>
        /// The provided eventDataSpan must be equal to the Span property of the eventData
        /// parameter that was passed to StartEvent().
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">Called in invalid State.</exception>
        public EventHeaderEventInfo GetEventInfo(ReadOnlySpan<byte> eventDataSpan)
        {
            Debug.Assert(m_eventData.Length == eventDataSpan.Length);

            if (m_state == EventHeaderEnumeratorState.None)
            {
                throw new InvalidOperationException(); // PRECONDITION
            }

            return new EventHeaderEventInfo(
                eventDataSpan,
                m_metaBegin,
                m_eventNameSize,
                m_activityIdBegin,
                m_activityIdSize,
                m_tracepointName,
                m_header,
                m_keyword);
        }

        /// <summary>
        /// <para>
        /// Gets information that applies to the current item, e.g. the item's name,
        /// the item's type (integer, string, float, etc.), data pointer, data size.
        /// The current item changes each time MoveNext() is called.
        /// </para><para>
        /// PRECONDITION: Can be called when State > BeforeFirstItem, i.e. after MoveNext
        /// returns true.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">Called in invalid State.</exception>
        public EventHeaderItemInfo GetItemInfo()
        {
            return this.GetItemInfo(m_eventData.Span);
        }

        /// <summary>
        /// <para>
        /// Advanced scenarios: This is the same as GetItemInfo() except that it uses the
        /// provided eventDataSpan instead of accessing the Span property of a ReadOnlyMemory
        /// field.
        /// </para><para>
        /// PRECONDITION: Can be called when State > BeforeFirstItem, i.e. after MoveNext
        /// returns true.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is useful as a performance optimization when you have the eventData span
        /// available and want to avoid the overhead of repeatedly converting from
        /// ReadOnlyMemory to ReadOnlySpan.
        /// </para><para>
        /// The provided eventDataSpan must be equal to the Span property of the eventData
        /// parameter that was passed to StartEvent().
        /// </para>
        /// </remarks>
        /// <exception cref="InvalidOperationException">Called in invalid State.</exception>
        public EventHeaderItemInfo GetItemInfo(ReadOnlySpan<byte> eventDataSpan)
        {
            Debug.Assert(m_eventData.Length == eventDataSpan.Length);

            if (m_state <= EventHeaderEnumeratorState.BeforeFirstItem)
            {
                throw new InvalidOperationException(); // PRECONDITION
            }

            return new EventHeaderItemInfo(
                eventDataSpan.Slice(m_stackTop.NameOffset, m_stackTop.NameSize),
                new PerfValue(
                    eventDataSpan.Slice(m_dataPosCooked, m_itemSizeCooked),
                    m_byteReader,
                    m_fieldType.Encoding,
                    m_fieldType.Format,
                    m_elementSize,
                    m_state == EventHeaderEnumeratorState.Value ? (ushort)1 : m_stackTop.ArrayCount,
                    m_fieldType.Tag));
        }

        /// <summary>
        /// <para>
        /// Gets the remaining event payload, i.e. the event data that has not yet
        /// been decoded. The data position can change each time MoveNext is called.
        /// </para><para>
        /// PRECONDITION: Can be called when State != None, i.e. at any time after a
        /// successful call to StartEvent, until a call to Clear.
        /// </para><para>
        /// This can be useful after enumeration has completed to to determine
        /// whether the event contains any trailing data (data not described by the
        /// decoding information). Up to 7 bytes of trailing data is normal (padding
        /// between events), but 8 or more bytes of trailing data might indicate some
        /// decodingStyle of encoding problem or data corruption.
        /// </para>
        /// </summary>
        /// <exception cref="InvalidOperationException">Called in invalid State.</exception>
        public ReadOnlyMemory<byte> GetRawDataPosition()
        {
            if (m_state == EventHeaderEnumeratorState.None)
            {
                throw new InvalidOperationException(); // PRECONDITION
            }

            return m_eventData.Slice(m_dataPosRaw);
        }

        /// <summary>
        /// <para>
        /// Appends the current event identity to the provided StringBuilder as a JSON string,
        /// e.g. <c>"MyProvider:MyEvent"</c> (including the quotation marks).
        /// </para><para>
        /// PRECONDITION: Can be called when State != None, i.e. at any time after a
        /// successful call to StartEvent, until a call to Clear.
        /// </para><para>
        /// The event identity includes the provider name and event name, e.g. "MyProvider:MyEvent".
        /// This is commonly used as the value of the "n" property in the JSON rendering of the event.
        /// </para>
        /// </summary>
        public void AppendJsonEventIdentityTo(StringBuilder sb)
        {
            AppendJsonEventIdentityTo(m_eventData.Span, sb);
        }

        /// <summary>
        /// <para>
        /// Advanced scenarios: This is the same as AppendJsonEventIdentityTo(sb) except that
        /// it uses the provided eventDataSpan instead of accessing the Span property of a
        /// ReadOnlyMemory field.
        /// </para><para>
        /// PRECONDITION: Can be called when State != None, i.e. at any time after a
        /// successful call to StartEvent, until a call to Clear.
        /// </para><para>
        /// This is useful as a performance optimization when you have the eventData span
        /// available and want to avoid the overhead of repeatedly converting from
        /// ReadOnlyMemory to ReadOnlySpan.
        /// </para><para>
        /// The provided eventDataSpan must be equal to the Span property of the eventData
        /// parameter that was passed to StartEvent().
        /// </para>
        /// </summary>
        public void AppendJsonEventIdentityTo(ReadOnlySpan<byte> eventDataSpan, StringBuilder sb)
        {
            Debug.Assert(m_eventData.Length == eventDataSpan.Length);

            sb.Append('"');
            PerfConvert.AppendEscapedJson(
                sb,
                m_tracepointName.AsSpan().Slice(0, m_tracepointName.LastIndexOf('_')));
            sb.Append(':');
            PerfConvert.AppendEscapedJson(
                sb,
                eventDataSpan.Slice(m_metaBegin, m_eventNameSize),
                Text.Encoding.UTF8);
            sb.Append('"');
        }

        /// <summary>
        /// <para>
        /// Appends event metadata to the provided StringBuilder as a comma-separated list
        /// of 0 or more JSON name-value pairs, e.g. <c>"level": 5, "keyword": 3</c>
        /// (including the quotation marks).
        /// </para><para>
        /// PRECONDITION: Can be called when State != None, i.e. at any time after a
        /// successful call to StartEvent, until a call to Clear.
        /// </para><para>
        /// One name-value pair is appended for each metadata item that is both requested
        /// by metaOptions and has a meaningful value available in the event. For example,
        /// the "id" metadata item is only appended if the event has a non-zero Id value,
        /// even if the metaOptions parameter includes EventHeaderMetaOptions.Id.
        /// </para><para>
        /// The following metadata items are supported:
        /// <list type="bullet"><item>
        /// "provider": "MyProviderName"
        /// </item><item>
        /// "event": "MyEventName"
        /// </item><item>
        /// "id": 123 (omitted if zero)
        /// </item><item>
        /// "version": 1 (omitted if zero)
        /// </item><item>
        /// "level": 5 (omitted if zero)
        /// </item><item>
        /// "keyword": "0x1" (omitted if zero)
        /// </item><item>
        /// "opcode": 1 (omitted if zero)
        /// </item><item>
        /// "tag": "0x123" (omitted if zero)
        /// </item><item>
        /// "activity": "12345678-1234-1234-1234-1234567890AB" (omitted if not present)
        /// </item><item>
        /// "relatedActivity": "12345678-1234-1234-1234-1234567890AB" (omitted if not present)
        /// </item><item>
        /// "options": "Gmygroup" (omitted if not present)
        /// </item><item>
        /// "flags": "0x7" (omitted if zero)
        /// </item></list>
        /// </para>
        /// </summary>
        /// <returns>
        /// Returns true if a comma would be needed before subsequent JSON output, i.e. if
        /// addCommaBeforeNextItem was true OR if any metadata items were appended.
        /// </returns>
        public bool AppendJsonEventMetaTo(
            StringBuilder sb,
            bool addCommaBeforeNextItem = false,
            EventHeaderMetaOptions metaOptions = EventHeaderMetaOptions.Default,
            PerfJsonOptions jsonOptions = PerfJsonOptions.Default)
        {
            return AppendJsonEventMetaTo(m_eventData.Span, sb, addCommaBeforeNextItem, metaOptions, jsonOptions);
        }

        /// <summary>
        /// <para>
        /// Advanced scenarios: This is the same as AppendJsonEventMetaTo(sb, ...) except that
        /// it uses the provided eventDataSpan instead of accessing the Span property of a
        /// ReadOnlyMemory field.
        /// </para><para>
        /// PRECONDITION: Can be called when State != None, i.e. at any time after a
        /// successful call to StartEvent, until a call to Clear.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is useful as a performance optimization when you have the eventData span
        /// available and want to avoid the overhead of repeatedly converting from
        /// ReadOnlyMemory to ReadOnlySpan.
        /// </para><para>
        /// The provided eventDataSpan must be equal to the Span property of the eventData
        /// parameter that was passed to StartEvent().
        /// </para>
        /// </remarks>
        public bool AppendJsonEventMetaTo(
            ReadOnlySpan<byte> eventDataSpan,
            StringBuilder sb,
            bool addCommaBeforeNextItem,
            EventHeaderMetaOptions metaOptions = EventHeaderMetaOptions.Default,
            PerfJsonOptions jsonOptions = PerfJsonOptions.Default)
        {
            Debug.Assert(m_eventData.Length == eventDataSpan.Length);

            var w = new JsonWriter(sb, jsonOptions, addCommaBeforeNextItem);

            int providerNameEnd =
                0 != (metaOptions & (EventHeaderMetaOptions.Provider | EventHeaderMetaOptions.Options))
                ? m_tracepointName.LastIndexOf('_')
                : 0;

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Provider))
            {
                PerfConvert.StringAppendJson(
                    w.WriteValueNoEscapeName("provider"),
                    m_tracepointName.AsSpan().Slice(0, providerNameEnd));
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Event))
            {
                PerfConvert.StringAppendJson(
                    w.WriteValueNoEscapeName("event"),
                    eventDataSpan.Slice(m_metaBegin, m_eventNameSize),
                    Text.Encoding.UTF8);
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Id) && m_header.Id != 0)
            {
                PerfConvert.UInt32DecimalAppend(
                    w.WriteValueNoEscapeName("id"),
                    m_header.Id);
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Version) && m_header.Version != 0)
            {
                PerfConvert.UInt32DecimalAppend(
                    w.WriteValueNoEscapeName("version"),
                    m_header.Version);
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Level) && m_header.Level != 0)
            {
                PerfConvert.UInt32DecimalAppend(
                    w.WriteValueNoEscapeName("level"),
                    (byte)m_header.Level);
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Keyword) && m_keyword != 0)
            {
                PerfConvert.UInt64HexAppendJson(
                    w.WriteValueNoEscapeName("keyword"),
                    m_keyword,
                    jsonOptions);
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Opcode) && m_header.Opcode != 0)
            {
                PerfConvert.UInt32DecimalAppend(
                    w.WriteValueNoEscapeName("opcode"),
                    (byte)m_header.Opcode);
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Tag) && m_header.Tag != 0)
            {
                PerfConvert.UInt32HexAppendJson(
                    w.WriteValueNoEscapeName("tag"),
                    m_header.Tag,
                    jsonOptions);
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Activity) && m_activityIdSize >= 16)
            {
                w.WriteValueNoEscapeName("activity");
                sb.Append('"');
                PerfConvert.GuidAppend(
                    sb,
                    Utility.ReadGuidBigEndian(eventDataSpan.Slice(m_activityIdBegin)));
                sb.Append('"');
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.RelatedActivity) && m_activityIdSize >= 32)
            {
                w.WriteValueNoEscapeName("relatedActivity");
                sb.Append('"');
                PerfConvert.GuidAppend(
                    sb,
                    Utility.ReadGuidBigEndian(eventDataSpan.Slice(m_activityIdBegin + 16)));
                sb.Append('"');
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Options))
            {
                var n = m_tracepointName;
                for (int i = providerNameEnd + 1; i < n.Length; i += 1)
                {
                    var ch = n[i];
                    if ('A' <= ch && ch <= 'Z' && ch != 'L' && ch != 'K')
                    {
                        PerfConvert.StringAppendJson(
                            w.WriteValueNoEscapeName("options"),
                            n.AsSpan(i));
                        break;
                    }
                }
            }

            if (metaOptions.HasFlag(EventHeaderMetaOptions.Flags))
            {
                PerfConvert.UInt32HexAppendJson(
                    w.WriteValueNoEscapeName("flags"),
                    (byte)m_header.Flags,
                    jsonOptions);
            }

            return w.Comma;
        }

        /// <summary>
        /// <para>
        /// Appends a JSON representation of the current item to the provided StringBuilder,
        /// e.g. for a string Value <c>"MyField": "My Value"</c> (includes the quotation marks),
        /// or for an ArrayBegin <c>"MyField": [ 1, 2, 3 ]</c>. Consumes the current item and its
        /// descendents as if by a call to MoveNextSibling.
        /// </para><para>
        /// PRECONDITION: Can be called when State >= BeforeFirstItem, i.e. after a
        /// successful call to StartEvent, until MoveNext returns false.
        /// </para><para>
        /// After calling this method, check <c>enumerator.State</c> to determine whether the
        /// enumeration has reached the end of the event or has encountered an error, i.e.
        /// enumeration should stop if <c>enumerator.State &lt; BeforeFirstItem</c>
        /// </para>
        /// </summary>
        /// <remarks>
        /// The output and the amount consumed depends on the initial state of the enumerator.
        /// <list type="bullet"><item>
        /// Value: Appends the current item as a JSON value like <c>123</c> (if jsonOptions omits
        /// <c>RootName</c> or the item is an element of an array) or a JSON name-value pair like
        /// <c>"MyField": 123</c> (if jsonOptions includes <c>RootName</c> and item is not an array
        /// element).. Moves enumeration to the next item.
        /// </item><item>
        /// StructBegin: Appends the current item as a JSON object like
        /// <c>{ "StructField1": 123, "StructField2": "Hello" }</c> (if jsonOptions omits
        /// <c>RootName</c> or the item is an element of an array) or a JSON name-object pairlike
        /// <c>"MyStruct": { "StructField1": 123, "StructField2": "Hello" }</c> (if jsonOptions
        /// includes <c>RootName</c> and item is not an array element). Moves enumeration past the
        /// end of the item and its descendents, i.e. after the matching StructEnd.
        /// </item><item>
        /// ArrayBegin: Appends the current item as a JSON array like
        /// <c>[ 1, 2, 3 ]</c> (if jsonOptions omits <c>RootName</c>) or a JSON name-array pair like
        /// <c>"MyArray": [ 1, 2, 3 ]</c> (if jsonOptions includes <c>RootName</c>). Moves enumeration
        /// past the end of the item and its descendents, i.e. after the matching ArrayEnd.
        /// </item><item>
        /// BeforeFirstItem: Appends all items in the current event as a comma-separated list of
        /// name-value pairs, e.g. <c>"MyField": 123, "MyArray": [ 1, 2, 3 ]</c>. Moves enumeration
        /// to AfterLastItem.
        /// </item><item>
        /// ArrayEnd, StructEnd: Unspecified behavior.
        /// </item></list>
        /// </remarks>
        /// <returns>
        /// Returns true if a comma would be needed before subsequent JSON output, i.e. if
        /// addCommaBeforeNextItem was true OR if any metadata items were appended.
        /// </returns>
        public bool AppendJsonItemToAndMoveNextSibling(
            StringBuilder sb,
            bool addCommaBeforeNextItem = false,
            PerfJsonOptions jsonOptions = PerfJsonOptions.Default)
        {
            return AppendJsonItemToAndMoveNextSibling(m_eventData.Span, sb, addCommaBeforeNextItem, jsonOptions);
        }

        /// <summary>
        /// <para>
        /// Advanced scenarios: This is the same as AppendJsonItemToAndMoveNextSibling(sb)
        /// except that it uses the provided eventDataSpan instead of accessing the Span
        /// property of a ReadOnlyMemory field.
        /// </para><para>
        /// PRECONDITION: Can be called when State != None, i.e. at any time after a
        /// successful call to StartEvent, until a call to Clear.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is useful as a performance optimization when you have the eventData span
        /// available and want to avoid the overhead of repeatedly converting from
        /// ReadOnlyMemory to ReadOnlySpan.
        /// </para><para>
        /// The provided eventDataSpan must be equal to the Span property of the eventData
        /// parameter that was passed to StartEvent().
        /// </para>
        /// </remarks>
        public bool AppendJsonItemToAndMoveNextSibling(
            ReadOnlySpan<byte> eventDataSpan,
            StringBuilder sb,
            bool addCommaBeforeNextItem,
            PerfJsonOptions jsonOptions = PerfJsonOptions.Default)
        {
            Debug.Assert(m_eventData.Length == eventDataSpan.Length);
            bool wantName = jsonOptions.HasFlag(PerfJsonOptions.RootName);
            var w = new JsonWriter(sb, jsonOptions, addCommaBeforeNextItem);

            bool ok;
            int depth = 0;
            EventHeaderItemInfo itemInfo;

            do
            {
                switch (m_state)
                {
                    default:
                        Debug.Fail("Enumerator in invalid state.");
                        throw new InvalidOperationException("Enumerator in invalid state.");

                    case EventHeaderEnumeratorState.BeforeFirstItem:
                        depth += 1;
                        break;

                    case EventHeaderEnumeratorState.Value:

                        itemInfo = GetItemInfo(eventDataSpan);
                        if (wantName && !itemInfo.Value.IsArrayOrElement)
                        {
                            w.WritePropertyName(itemInfo.NameBytes, itemInfo.Value.FieldTag);
                        }

                        itemInfo.Value.AppendJsonScalarTo(w.WriteValue());
                        break;

                    case EventHeaderEnumeratorState.ArrayBegin:

                        itemInfo = GetItemInfo(eventDataSpan);
                        if (wantName)
                        {
                            w.WritePropertyName(itemInfo.NameBytes, itemInfo.Value.FieldTag);
                        }

                        if (itemInfo.Value.TypeSize != 0)
                        {
                            itemInfo.Value.AppendJsonSimpleArrayTo(w.WriteValue(), jsonOptions);
                            ok = MoveNextSibling(eventDataSpan);
                            continue; // Skip the MoveNext().
                        }

                        w.WriteStartArray();
                        depth += 1;
                        break;

                    case EventHeaderEnumeratorState.ArrayEnd:

                        w.WriteEndArray();
                        depth -= 1;
                        break;

                    case EventHeaderEnumeratorState.StructBegin:

                        itemInfo = GetItemInfo(eventDataSpan);

                        if (wantName && !itemInfo.Value.IsArrayOrElement)
                        {
                            w.WritePropertyName(itemInfo.NameBytes, itemInfo.Value.FieldTag);
                        }

                        w.WriteStartObject();
                        depth += 1;
                        break;

                    case EventHeaderEnumeratorState.StructEnd:

                        w.WriteEndObject();
                        depth -= 1;
                        break;
                }

                wantName = true;
                ok = MoveNext(eventDataSpan);
            } while (ok && depth > 0);

            return w.Comma;
        }

        private void ResetImpl(int moveNextLimit)
        {
            m_dataPosRaw = m_dataBegin;
            m_moveNextRemaining = moveNextLimit;
            m_stackTop.NextOffset = m_metaBegin + m_eventNameSize + 1;
            m_stackTop.RemainingFieldCount = 255; // Go until we reach end of metadata.
            m_stackIndex = 0;
            SetState(EventHeaderEnumeratorState.BeforeFirstItem, SubState.BeforeFirstItem);
            m_lastError = EventHeaderEnumeratorError.Success;
        }

        private bool SkipStructMetadata(ReadOnlySpan<byte> eventDataSpan)
        {
            Debug.Assert(m_fieldType.Encoding.BaseEncoding() == EventHeaderFieldEncoding.Struct);

            bool ok;
            for (uint remainingFieldCount = (byte)m_fieldType.Format; ;
                remainingFieldCount -= 1)
            {
                // It's a bit unusual but completely legal and fully supported to reach
                // end-of-metadata before remainingFieldCount == 0.
                if (remainingFieldCount == 0 || m_stackTop.NextOffset == m_metaEnd)
                {
                    ok = true;
                    break;
                }

                m_stackTop.NameOffset = m_stackTop.NextOffset;

                // Minimal validation, then skip the field:

                var type = ReadFieldNameAndType(eventDataSpan);
                if (type.Encoding == ReadFieldError)
                {
                    ok = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                    break;
                }

                if (EventHeaderFieldEncoding.Struct == type.Encoding.BaseEncoding())
                {
                    remainingFieldCount += (byte)type.Format;
                }

                if (!type.Encoding.IsCArray())
                {
                    // Scalar or runtime length. We're done with the field.
                }
                else if (!type.Encoding.IsVArray())
                {
                    // CArrayFlag is set, VArrayFlag is unset.
                    // Compile-time-constant array length.
                    // Skip the array length in metadata.

                    if (m_metaEnd - m_stackTop.NextOffset < 2)
                    {
                        ok = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                        break;
                    }

                    m_stackTop.NextOffset += 2;
                }
                else
                {
                    // Both CArrayFlag and VArrayFlag are set (reserved encoding).
                    ok = SetErrorState(EventHeaderEnumeratorError.NotSupported);
                    break;
                }
            }

            return ok;
        }

        private bool NextProperty(ReadOnlySpan<byte> eventDataSpan)
        {
            bool movedToItem;
            if (m_stackTop.RemainingFieldCount != 0 &&
                m_stackTop.NextOffset != m_metaEnd)
            {
                m_stackTop.RemainingFieldCount -= 1;
                m_stackTop.ArrayIndex = 0;
                m_stackTop.NameOffset = m_stackTop.NextOffset;

                // Decode a field:

                m_fieldType = ReadFieldNameAndType(eventDataSpan);
                if (m_fieldType.Encoding == ReadFieldError)
                {
                    movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                }
                else if (!m_fieldType.Encoding.IsArray())
                {
                    // Non-array.

                    m_stackTop.ArrayCount = 1;
                    if (EventHeaderFieldEncoding.Struct != m_fieldType.Encoding)
                    {
                        SetState(EventHeaderEnumeratorState.Value, SubState.Value_Scalar);
                        movedToItem = StartValue(eventDataSpan);
                    }
                    else if (m_fieldType.Format == 0)
                    {
                        // Struct must have at least 1 field (potential for DoS).
                        movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                    }
                    else
                    {
                        StartStruct();
                        movedToItem = true;
                    }
                }
                else if (m_fieldType.Encoding.IsVArray())
                {
                    // Runtime-variable array length.

                    if (eventDataSpan.Length - m_dataPosRaw < 2)
                    {
                        movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                    }
                    else
                    {
                        m_stackTop.ArrayCount = m_byteReader.ReadU16(eventDataSpan.Slice(m_dataPosRaw));
                        m_dataPosRaw += 2;

                        movedToItem = StartArray(); // StartArray will set Flags.
                    }
                }
                else if (m_fieldType.Encoding.IsCArray())
                {
                    // Compile-time-constant array length.

                    if (m_metaEnd - m_stackTop.NextOffset < 2)
                    {
                        movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                    }
                    else
                    {
                        m_stackTop.ArrayCount = m_byteReader.ReadU16(eventDataSpan.Slice(m_stackTop.NextOffset));
                        m_stackTop.NextOffset += 2;

                        if (m_stackTop.ArrayCount == 0)
                        {
                            // Constant-length array cannot have length of 0 (potential for DoS).
                            movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                        }
                        else
                        {
                            movedToItem = StartArray(); // StartArray will set Flags.
                        }
                    }
                }
                else
                {
                    movedToItem = SetErrorState(EventHeaderEnumeratorError.NotSupported);
                }
            }
            else if (m_stackIndex != 0)
            {
                // End of struct.
                // It's a bit unusual but completely legal and fully supported to reach
                // end-of-metadata before RemainingFieldCount == 0.

                // Pop child from stack.
                m_stackIndex -= 1;
                var childMetadataOffset = m_stackTop.NextOffset;
                var stack = MemoryMarshal.CreateSpan(ref m_stack.E0, StructNestLimit);
                m_stackTop = stack[m_stackIndex];

                m_fieldType = ReadFieldType(eventDataSpan, m_stackTop.NameOffset + m_stackTop.NameSize + 1);
                Debug.Assert(EventHeaderFieldEncoding.Struct == m_fieldType.Encoding.BaseEncoding());
                m_elementSize = 0;

                // Unless parent is in the middle of an array, we need to set the
                // "next field" position to the child's metadata position.
                Debug.Assert(m_stackTop.ArrayIndex < m_stackTop.ArrayCount);
                if (m_stackTop.ArrayIndex + 1 == m_stackTop.ArrayCount)
                {
                    m_stackTop.NextOffset = childMetadataOffset;
                }

                SetEndState(EventHeaderEnumeratorState.StructEnd, SubState.StructEnd);
                movedToItem = true;
            }
            else
            {
                // End of event.

                if (m_stackTop.NextOffset != m_metaEnd)
                {
                    // Event has metadata for more than MaxTopLevelProperties.
                    movedToItem = SetErrorState(EventHeaderEnumeratorError.NotSupported);
                }
                else
                {
                    SetEndState(EventHeaderEnumeratorState.AfterLastItem, SubState.AfterLastItem);
                    movedToItem = false; // No more items.
                }
            }

            return movedToItem;
        }

        /// <summary>
        /// Requires m_metaEnd >= m_stackTop.NameOffset.
        /// Reads name, encoding, format, tag starting at m_stackTop.NameOffset.
        /// Updates m_stackTop.NameSize, m_stackTop.NextOffset.
        /// On failure, returns Encoding = ReadFieldError.
        /// </summary>
        private FieldType ReadFieldNameAndType(ReadOnlySpan<byte> eventDataSpan)
        {
            var pos = m_stackTop.NameOffset;
            Debug.Assert(m_metaEnd >= pos);

            var nameSize = eventDataSpan.Slice(pos, m_metaEnd - pos).IndexOf((byte)0);
            var nameEnd = pos + nameSize;
            if (nameSize < 0 || m_metaEnd - nameEnd < 2)
            {
                // Missing nul termination or missing encoding.
                return new FieldType { Encoding = ReadFieldError };
            }
            else
            {
                m_stackTop.NameSize = (ushort)nameSize;
                return ReadFieldType(eventDataSpan, nameEnd + 1);
            }
        }

        /// <summary>
        /// Requires m_metaEnd > typeOffset.
        /// Reads encoding, format, tag starting at m_stackTop.NameOffset.
        /// Updates m_stackTop.NextOffset.
        /// On failure, returns Encoding = ReadFieldError.
        /// </summary>
        private FieldType ReadFieldType(ReadOnlySpan<byte> eventDataSpan, int typeOffset)
        {
            int pos = typeOffset;
            Debug.Assert(m_metaEnd > pos);

            var encoding = (EventHeaderFieldEncoding)eventDataSpan[pos];
            pos += 1;

            var format = LinuxTracepoints.EventHeaderFieldFormat.Default;
            ushort tag = 0;
            if (encoding.HasChainFlag())
            {
                if (m_metaEnd == pos)
                {
                    // Missing format.
                    encoding = ReadFieldError;
                }
                else
                {
                    format = (LinuxTracepoints.EventHeaderFieldFormat)eventDataSpan[pos];
                    pos += 1;
                    if (0 != (format & LinuxTracepoints.EventHeaderFieldFormat.ChainFlag))
                    {
                        if (m_metaEnd - pos < 2)
                        {
                            // Missing tag.
                            encoding = ReadFieldError;
                        }
                        else
                        {
                            tag = m_byteReader.ReadU16(eventDataSpan.Slice(pos));
                            pos += 2;
                        }
                    }
                }
            }

            m_stackTop.NextOffset = pos;
            return new FieldType
            {
                Encoding = encoding & ~EventHeaderFieldEncoding.ChainFlag,
                Format = format & ~LinuxTracepoints.EventHeaderFieldFormat.ChainFlag,
                Tag = tag
            };
        }

        private bool StartArray()
        {
            m_elementSize = 0;
            m_itemSizeRaw = 0;
            m_dataPosCooked = m_dataPosRaw;
            m_itemSizeCooked = 0;
            SetState(EventHeaderEnumeratorState.ArrayBegin, SubState.ArrayBegin);

            // Determine the m_elementSize value.
            bool movedToItem;
            switch (m_fieldType.Encoding.BaseEncoding())
            {
                case EventHeaderFieldEncoding.Struct:
                    movedToItem = true;
                    goto Done;

                case EventHeaderFieldEncoding.Value8:
                    m_elementSize = 1;
                    break;

                case EventHeaderFieldEncoding.Value16:
                    m_elementSize = 2;
                    break;

                case EventHeaderFieldEncoding.Value32:
                    m_elementSize = 4;
                    break;

                case EventHeaderFieldEncoding.Value64:
                    m_elementSize = 8;
                    break;

                case EventHeaderFieldEncoding.Value128:
                    m_elementSize = 16;
                    break;

                case EventHeaderFieldEncoding.ZStringChar8:
                case EventHeaderFieldEncoding.ZStringChar16:
                case EventHeaderFieldEncoding.ZStringChar32:
                case EventHeaderFieldEncoding.StringLength16Char8:
                case EventHeaderFieldEncoding.StringLength16Char16:
                case EventHeaderFieldEncoding.StringLength16Char32:
                    movedToItem = true;
                    goto Done;

                case EventHeaderFieldEncoding.Invalid:
                    movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                    goto Done;

                default:
                    movedToItem = SetErrorState(EventHeaderEnumeratorError.NotSupported);
                    goto Done;
            }

            // For simple array element types, validate that Count * m_elementSize <= RemainingSize.
            // That way we can skip per-element validation and we can safely expose the array data
            // during ArrayBegin.
            var cbRemaining = m_eventData.Length - m_dataPosRaw;
            var cbArray = m_stackTop.ArrayCount * m_elementSize;
            if (cbRemaining < cbArray)
            {
                movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
            }
            else
            {
                m_itemSizeRaw = m_itemSizeCooked = cbArray;
                movedToItem = true;
            }

        Done:

            return movedToItem;
        }

        private void StartStruct()
        {
            Debug.Assert(m_fieldType.Encoding.BaseEncoding() == EventHeaderFieldEncoding.Struct);
            m_elementSize = 0;
            m_itemSizeRaw = 0;
            m_dataPosCooked = m_dataPosRaw;
            m_itemSizeCooked = 0;
            SetState(EventHeaderEnumeratorState.StructBegin, SubState.StructBegin);
        }

        private bool StartValue(ReadOnlySpan<byte> eventDataSpan)
        {
            var cbRemaining = eventDataSpan.Length - m_dataPosRaw;

            Debug.Assert(m_state == EventHeaderEnumeratorState.Value);
            Debug.Assert(m_fieldType.Encoding ==
                (~EventHeaderFieldEncoding.ChainFlag & (EventHeaderFieldEncoding)eventDataSpan[m_stackTop.NameOffset + m_stackTop.NameSize + 1]));
            m_dataPosCooked = m_dataPosRaw;
            m_elementSize = 0;

            bool movedToItem;
            switch (m_fieldType.Encoding.BaseEncoding())
            {
                case EventHeaderFieldEncoding.Value8:
                    m_itemSizeRaw = m_itemSizeCooked = m_elementSize = 1;
                    if (m_itemSizeRaw <= cbRemaining)
                    {
                        movedToItem = true;
                        goto Done;
                    }
                    break;

                case EventHeaderFieldEncoding.Value16:
                    m_itemSizeRaw = m_itemSizeCooked = m_elementSize = 2;
                    if (m_itemSizeRaw <= cbRemaining)
                    {
                        movedToItem = true;
                        goto Done;
                    }
                    break;

                case EventHeaderFieldEncoding.Value32:
                    m_itemSizeRaw = m_itemSizeCooked = m_elementSize = 4;
                    if (m_itemSizeRaw <= cbRemaining)
                    {
                        movedToItem = true;
                        goto Done;
                    }
                    break;

                case EventHeaderFieldEncoding.Value64:
                    m_itemSizeRaw = m_itemSizeCooked = m_elementSize = 8;
                    if (m_itemSizeRaw <= cbRemaining)
                    {
                        movedToItem = true;
                        goto Done;
                    }
                    break;

                case EventHeaderFieldEncoding.Value128:
                    m_itemSizeRaw = m_itemSizeCooked = m_elementSize = 16;
                    if (m_itemSizeRaw <= cbRemaining)
                    {
                        movedToItem = true;
                        goto Done;
                    }
                    break;

                case EventHeaderFieldEncoding.ZStringChar8:
                    StartValueStringNul8(eventDataSpan);
                    break;

                case EventHeaderFieldEncoding.ZStringChar16:
                    StartValueStringNul16(eventDataSpan);
                    break;

                case EventHeaderFieldEncoding.ZStringChar32:
                    StartValueStringNul32(eventDataSpan);
                    break;

                case EventHeaderFieldEncoding.StringLength16Char8:
                    StartValueStringLength16(eventDataSpan, 0);
                    break;

                case EventHeaderFieldEncoding.StringLength16Char16:
                    StartValueStringLength16(eventDataSpan, 1);
                    break;

                case EventHeaderFieldEncoding.StringLength16Char32:
                    StartValueStringLength16(eventDataSpan, 2);
                    break;

                case EventHeaderFieldEncoding.Invalid:
                case EventHeaderFieldEncoding.Struct: // Should never happen.
                default:
                    Debug.Assert(m_fieldType.Encoding.BaseEncoding() != EventHeaderFieldEncoding.Struct);
                    m_itemSizeRaw = m_itemSizeCooked = 0;
                    movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
                    goto Done;
            }

            if (cbRemaining < m_itemSizeRaw)
            {
                m_itemSizeRaw = m_itemSizeCooked = 0;
                movedToItem = SetErrorState(EventHeaderEnumeratorError.InvalidData);
            }
            else
            {
                movedToItem = true;
            }

        Done:

            return movedToItem;
        }

        private void StartValueSimple()
        {
            Debug.Assert(m_stackTop.ArrayIndex < m_stackTop.ArrayCount);
            Debug.Assert(m_fieldType.Encoding.IsArray());
            Debug.Assert(m_fieldType.Encoding.BaseEncoding() != EventHeaderFieldEncoding.Struct);
            Debug.Assert(m_elementSize != 0);
            Debug.Assert(m_itemSizeCooked == m_elementSize);
            Debug.Assert(m_itemSizeRaw == m_elementSize);
            Debug.Assert(m_eventData.Length >= m_dataPosRaw + m_itemSizeRaw);
            Debug.Assert(m_state == EventHeaderEnumeratorState.Value);
            m_dataPosCooked = m_dataPosRaw;
        }

        private void StartValueStringNul8(ReadOnlySpan<byte> eventDataSpan)
        {
            // cch = strnlen(value, cchRemaining)
            var len = eventDataSpan.Slice(m_dataPosRaw).IndexOf((byte)0);
            m_itemSizeCooked = (len < 0 ? eventDataSpan.Length - m_dataPosRaw : len) * sizeof(byte);
            m_itemSizeRaw = m_itemSizeCooked + sizeof(byte);
        }

        private void StartValueStringNul16(ReadOnlySpan<byte> eventDataSpan)
        {
            var endPos = eventDataSpan.Length - sizeof(UInt16);
            for (var pos = m_dataPosRaw; pos <= endPos; pos += sizeof(UInt16))
            {
                if (0 == BitConverter.ToUInt16(eventDataSpan.Slice(pos))) // Byte order not significant.
                {
                    m_itemSizeCooked = pos - m_dataPosRaw;
                    m_itemSizeRaw = m_itemSizeCooked + sizeof(UInt16);
                    return;
                }
            }

            m_itemSizeCooked = eventDataSpan.Length;
            m_itemSizeRaw = eventDataSpan.Length;
        }

        private void StartValueStringNul32(ReadOnlySpan<byte> eventDataSpan)
        {
            var endPos = eventDataSpan.Length - sizeof(UInt32);
            for (var pos = m_dataPosRaw; pos <= endPos; pos += sizeof(UInt32))
            {
                if (0 == BitConverter.ToUInt32(eventDataSpan.Slice(pos))) // Byte order not significant.
                {
                    m_itemSizeCooked = pos - m_dataPosRaw;
                    m_itemSizeRaw = m_itemSizeCooked + sizeof(UInt32);
                    return;
                }
            }

            m_itemSizeCooked = eventDataSpan.Length;
            m_itemSizeRaw = eventDataSpan.Length;
        }

        private void StartValueStringLength16(ReadOnlySpan<byte> eventDataSpan, byte charSizeShift)
        {
            var cbRemaining = eventDataSpan.Length - m_dataPosRaw;
            if (cbRemaining < sizeof(ushort))
            {
                m_itemSizeRaw = sizeof(ushort);
            }
            else
            {
                m_dataPosCooked = m_dataPosRaw + sizeof(ushort);

                var cch = m_byteReader.ReadU16(eventDataSpan.Slice(m_dataPosRaw));
                m_itemSizeCooked = cch << charSizeShift;
                m_itemSizeRaw = m_itemSizeCooked + sizeof(ushort);
            }
        }

        private void SetState(EventHeaderEnumeratorState newState, SubState newSubState)
        {
            m_state = newState;
            m_subState = newSubState;
        }

        private void SetEndState(EventHeaderEnumeratorState newState, SubState newSubState)
        {
            m_dataPosCooked = m_dataPosRaw;
            m_itemSizeRaw = 0;
            m_itemSizeCooked = 0;
            m_state = newState;
            m_subState = newSubState;
        }

        private bool SetNoneState(EventHeaderEnumeratorError error)
        {
            m_eventData = default;
            m_tracepointName = "";
            m_state = EventHeaderEnumeratorState.None;
            m_subState = SubState.None;
            m_lastError = error;
            return false;
        }

        private bool SetErrorState(EventHeaderEnumeratorError error)
        {
            m_state = EventHeaderEnumeratorState.Error;
            m_subState = SubState.Error;
            m_lastError = error;
            return false;
        }

        private static int LowercaseHexToInt(string str, int pos, out ulong val)
        {
            val = 0;
            for (; pos < str.Length; pos += 1)
            {
                uint nibble;
                char ch = str[pos];
                if ('0' <= ch && ch <= '9')
                {
                    nibble = (uint)(ch - '0');
                }
                else if ('a' <= ch && ch <= 'f')
                {
                    nibble = (uint)(ch - 'a' + 10);
                }
                else
                {
                    break;
                }

                val = (val << 4) + nibble;
            }

            return pos;
        }

    }
}
