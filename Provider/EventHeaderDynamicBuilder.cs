﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Provider;

using System;
using System.Buffers;
using Debug = System.Diagnostics.Debug;
using Encoding = System.Text.Encoding;
using EventOpcode = System.Diagnostics.Tracing.EventOpcode;
using MemoryMarshal = System.Runtime.InteropServices.MemoryMarshal;

/// <summary>
/// Builder for events to be written through an <see cref="EventHeaderDynamicTracepoint"/>.
/// Create a <see cref="EventHeaderDynamicProvider"/> provider, use the provider to get a
/// <see cref="EventHeaderDynamicTracepoint"/> tracepoint. Create a
/// <see cref="EventHeaderDynamicBuilder"/> builder (or reuse an existing one to minimize
/// overhead), add data to it, and then call tracepoint.Write(builder) to emit the event.
/// </summary>
/// <remarks>
/// <para>
/// Builder objects are reusable. If generating several events in sequence, you can minimize
/// overhead by using the same builder for multiple events.
/// </para><para>
/// Builder objects are disposable. The builder uses an ArrayPool to minimize overhead.
/// When you are done with a builder, call Dispose() to return the allocations to the pool
/// so that they can be used by the next builder.
/// </para>
/// </remarks>
public class EventHeaderDynamicBuilder : IDisposable
{
    private const EventHeaderFieldEncoding VArrayFlag = EventHeaderFieldEncoding.VArrayFlag;

    private Vector meta;
    private Vector data;

    /// <summary>
    /// Initializes a new instance of the EventBuilder.
    /// </summary>
    /// <param name="initialMetadataBufferSize">
    /// The initial capacity of the metadata buffer. This must be a power of 2 in the
    /// range 4 through 65536. Default is 256 bytes.
    /// </param>
    /// <param name="initialDataBufferSize">
    /// The initial capacity of the data buffer. This must be a power of 2 in the
    /// range 4 through 65536. Default is 256 bytes.
    /// </param>
    public EventHeaderDynamicBuilder(int initialMetadataBufferSize = 256, int initialDataBufferSize = 256)
        : this(Encoding.UTF8, initialMetadataBufferSize, initialDataBufferSize)
    {
        return;
    }

    /// <summary>
    /// Advanced scenarios: Initializes a new instance of the EventBuilder class that
    /// uses a customized UTF-8 encoding for event and field names.
    /// </summary>
    /// <param name="utf8NameEncoding">
    /// The customized UTF-8 encoding to use for event and field names.
    /// </param>
    /// <param name="initialMetadataBufferSize">
    /// The initial capacity of the metadata buffer. This must be a power of 2 in the
    /// range 4 through 65536.
    /// </param>
    /// <param name="initialDataBufferSize">
    /// The initial capacity of the data buffer. This must be a power of 2 in the
    /// range 4 through 65536.
    /// </param>
    public EventHeaderDynamicBuilder(
        Encoding utf8NameEncoding,
        int initialMetadataBufferSize = 256,
        int initialDataBufferSize = 256)
    {
        if (initialMetadataBufferSize < 4 || initialMetadataBufferSize > 65536 ||
            (initialMetadataBufferSize & (initialMetadataBufferSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialMetadataBufferSize));
        }

        if (initialDataBufferSize < 4 || initialDataBufferSize > 65536 ||
            (initialDataBufferSize & (initialDataBufferSize - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDataBufferSize));
        }

        this.Utf8NameEncoding = utf8NameEncoding;
        this.meta = new Vector(initialMetadataBufferSize);
        this.data = new Vector(initialDataBufferSize);

        // Initial state is the same as Reset("").
        this.meta.AddByte(0); // nul-termination for empty event name.
        Debug.Assert(this.meta.Used == 1);
    }

    /// <summary>
    /// Advanced scenarios: Gets or sets the UTF-8 encoding that will be used for
    /// event and field names.
    /// </summary>
    public Encoding Utf8NameEncoding { get; set; }

    /// <summary>
    /// Provider-defined event tag, or 0 if none.
    /// Reset sets this to 0. Can also be set by the SetTag method.
    /// </summary>
    public ushort Tag { get; set; }

    /// <summary>
    /// Stable id for this event, or 0 if none.
    /// Reset sets this to 0. Can also be set by the SetIdVersion.
    /// </summary>
    public ushort Id { get; set; }

    /// <summary>
    /// Increment Version whenever event layout changes.
    /// Reset sets this to 0. Can also be set by the SetIdVersion.
    /// </summary>
    public byte Version { get; set; }

    /// <summary>
    /// EventOpcode raw value. (Stores the value of the Opcode property.)
    /// Reset sets this to 0 (Info). Can also be set by SetOpcodeByte.
    /// </summary>
    public byte OpcodeByte { get; set; }

    /// <summary>
    /// EventOpcode: info, start activity, stop activity, etc.
    /// Reset sets this to 0 (Info). Can also be set by SetOpcode.
    /// Throws OverflowException if set to a value greater than 255.
    /// </summary>
    /// <exception cref="OverflowException">value > 255</exception>
    /// <remarks><para>
    /// Most events set Opcode = Info (0). Other Opcode values add special semantics to
    /// an event that help the event analysis tool with grouping related events. The
    /// most frequently-used special semantics are ActivityStart and ActivityStop.
    /// </para><para>
    /// To record an activity:
    /// </para><list type="bullet"><item>
    /// Generate a new activity id. An activity id is a 128-bit value that must be
    /// unique within the trace. This can be a UUID or it can be generated by any
    /// other id-generation system that is unlikely to create the same value for any
    /// other activity id in the same trace.
    /// </item><item>
    /// Write an event with opcode = ActivityStart and with an ActivityId header
    /// extension. The ActivityId extension should have the newly-generated activity
    /// id, followed by the id of a parent activity (if any). If there is a parent
    /// activity, the extension length will be 32; otherwise it will be 16.
    /// </item><item>
    /// As appropriate, write any number of normal events (events with opcode set to
    /// something other than ActivityStart or ActivityStop, e.g. opcode = Info). To
    /// indicate that the events are part of the activity, each of these events
    /// should have an ActivityId header extension with the new activity id
    /// (extension length will be 16).
    /// </item><item>
    /// When the activity ends, write an event with opcode = ActivityStop and with
    /// an ActivityId header extension containing the activity id of the activity
    /// that is ending (extension length will be 16).
    /// </item></list>
    /// </remarks>
    public EventOpcode Opcode
    {
        get => (EventOpcode)this.OpcodeByte;
        set => this.OpcodeByte = checked((byte)value);
    }

    /// <summary>
    /// Clears the previous event (if any) from the builder and starts building a new event.
    /// Sets Tag, Id, Version, Opcode to 0.
    /// </summary>
    /// <param name="name">
    /// The event name for the new event. Must not contain any '\0' chars.
    /// </param>
    public EventHeaderDynamicBuilder Reset(ReadOnlySpan<char> name)
    {
        Debug.Assert(name.IndexOf('\0') < 0, "Event name must not have embedded NUL characters.");
        this.meta.Reset();
        this.data.Reset();
        this.Tag = 0;
        this.Id = 0;
        this.Version = 0;
        this.OpcodeByte = 0;

        this.meta.ReserveSpaceFor((uint)this.Utf8NameEncoding.GetMaxByteCount(name.Length) + 1);
        var metaBytes = this.meta.UsedSpan;
        var nameUsed = this.Utf8NameEncoding.GetBytes(name, metaBytes);
        metaBytes[nameUsed++] = 0;
        this.meta.SetUsed(nameUsed);

        return this;
    }

    /// <summary>
    /// Sets the provider-defined event tag. Most events have tag 0 (default).
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder SetTag(ushort tag)
    {
        this.Tag = tag;
        return this;
    }

    /// <summary>
    /// Sets the event's stable id and the event's version.
    /// Since events are frequently identified by name, many events use 0,
    /// indicating that they do not have any assigned stable id.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder SetIdVersion(ushort id, byte version)
    {
        this.Id = id;
        this.Version = version;
        return this;
    }

    /// <summary>
    /// EventOpcode: info, start activity, stop activity, etc.
    /// </summary>
    /// <returns>this</returns>
    /// <remarks><para>
    /// Most events set Opcode = Info (0). Other Opcode values add special semantics to
    /// an event that help the event analysis tool with grouping related events. The
    /// most frequently-used special semantics are ActivityStart and ActivityStop.
    /// </para><para>
    /// To record an activity:
    /// </para><list type="bullet"><item>
    /// Generate a new activity id. An activity id is a 128-bit value that must be
    /// unique within the trace. This can be a UUID or it can be generated by any
    /// other id-generation system that is unlikely to create the same value for any
    /// other activity id in the same trace.
    /// </item><item>
    /// Write an event with opcode = ActivityStart and with an ActivityId header
    /// extension. The ActivityId extension should have the newly-generated activity
    /// id, followed by the id of a parent activity (if any). If there is a parent
    /// activity, the extension length will be 32; otherwise it will be 16.
    /// </item><item>
    /// As appropriate, write any number of normal events (events with opcode set to
    /// something other than ActivityStart or ActivityStop, e.g. opcode = Info). To
    /// indicate that the events are part of the activity, each of these events
    /// should have an ActivityId header extension with the new activity id
    /// (extension length will be 16).
    /// </item><item>
    /// When the activity ends, write an event with opcode = ActivityStop and with
    /// an ActivityId header extension containing the activity id of the activity
    /// that is ending (extension length will be 16).
    /// </item></list>
    /// </remarks>
    public EventHeaderDynamicBuilder SetOpcodeByte(byte opcode)
    {
        this.OpcodeByte = opcode;
        return this;
    }

    /// <summary>
    /// EventOpcode: info, start activity, stop activity, etc.
    /// Throws OverflowException if value > 255.
    /// </summary>
    /// <returns>this</returns>
    /// <exception cref="OverflowException">value > 255</exception>
    /// <remarks><para>
    /// Most events set Opcode = Info (0). Other Opcode values add special semantics to
    /// an event that help the event analysis tool with grouping related events. The
    /// most frequently-used special semantics are ActivityStart and ActivityStop.
    /// </para><para>
    /// To record an activity:
    /// </para><list type="bullet"><item>
    /// Generate a new activity id. An activity id is a 128-bit value that must be
    /// unique within the trace. This can be a UUID or it can be generated by any
    /// other id-generation system that is unlikely to create the same value for any
    /// other activity id in the same trace.
    /// </item><item>
    /// Write an event with opcode = ActivityStart and with an ActivityId header
    /// extension. The ActivityId extension should have the newly-generated activity
    /// id, followed by the id of a parent activity (if any). If there is a parent
    /// activity, the extension length will be 32; otherwise it will be 16.
    /// </item><item>
    /// As appropriate, write any number of normal events (events with opcode set to
    /// something other than ActivityStart or ActivityStop, e.g. opcode = Info). To
    /// indicate that the events are part of the activity, each of these events
    /// should have an ActivityId header extension with the new activity id
    /// (extension length will be 16).
    /// </item><item>
    /// When the activity ends, write an event with opcode = ActivityStop and with
    /// an ActivityId header extension containing the activity id of the activity
    /// that is ending (extension length will be 16).
    /// </item></list>
    /// </remarks>
    public EventHeaderDynamicBuilder SetOpcode(EventOpcode opcode)
    {
        this.Opcode = opcode;
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value8"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as UnsignedInt).
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Boolean, HexBytes, String8.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddUInt8(
        ReadOnlySpan<char> name,
        Byte value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value8, format, tag);
        this.data.AddByte(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value8"/> array to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as UnsignedInt).
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Boolean, HexBytes, String8.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddUInt8Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<Byte> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value8 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value8"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.SignedInt"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Boolean, HexBytes, String8.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddInt8(
        ReadOnlySpan<char> name,
        SByte value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.SignedInt,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value8, format, tag);
        this.data.AddByte(unchecked((Byte)value));
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value8"/> array to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.SignedInt"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Boolean, HexBytes, String8.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddInt8Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<SByte> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.SignedInt,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value8 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value16"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as UnsignedInt).
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Boolean, HexBytes, StringUtf,
    /// Port.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddUInt16(
        ReadOnlySpan<char> name,
        UInt16 value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value16, format, tag);
        this.AddDataT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value16"/> array to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as UnsignedInt).
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Boolean, HexBytes, StringUtf,
    /// Port.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddUInt16Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<UInt16> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value16 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value16"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.SignedInt"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Boolean, HexBytes, StringUtf,
    /// Port.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddInt16(
        ReadOnlySpan<char> name,
        Int16 value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.SignedInt,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value16, format, tag);
        this.AddDataT(unchecked((UInt16)value));
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value16"/> array to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.SignedInt"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Boolean, HexBytes, StringUtf,
    /// Port.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddInt16Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<Int16> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.SignedInt,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value16 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value16"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.StringUtf"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Boolean, HexBytes, StringUtf,
    /// Port.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddChar16(
        ReadOnlySpan<char> name,
        Char value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.StringUtf,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value16, format, tag);
        this.AddDataT(unchecked((UInt16)value));
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value16"/> array to the event.
    /// Note that this adds an array of char (i.e. ['A', 'B', 'C']), not a String.
    /// Default format is <see cref="EventHeaderFieldFormat.StringUtf"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Boolean, HexBytes, StringUtf,
    /// Port.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddChar16Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<Char> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.StringUtf,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value16 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value32"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as UnsignedInt).
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Errno, Pid, Time, Boolean,
    /// Float, HexBytes, StringUtf, IPv4.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddUInt32(
        ReadOnlySpan<char> name,
        UInt32 value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value32, format, tag);
        this.AddDataT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value32"/> array to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as UnsignedInt).
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Errno, Pid, Time, Boolean,
    /// Float, HexBytes, StringUtf, IPv4.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddUInt32Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<UInt32> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value32 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value32"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.SignedInt"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Errno, Pid, Time, Boolean,
    /// Float, HexBytes, StringUtf, IPv4.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddInt32(
        ReadOnlySpan<char> name,
        Int32 value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.SignedInt,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value32, format, tag);
        this.AddDataT(unchecked((UInt32)value));
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value32"/> array to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.SignedInt"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Errno, Pid, Time, Boolean,
    /// Float, HexBytes, StringUtf, IPv4.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddInt32Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<Int32> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.SignedInt,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value32 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value64"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as UnsignedInt).
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Time, Float, HexBytes.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddUInt64(
        ReadOnlySpan<char> name,
        UInt64 value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value64, format, tag);
        this.AddDataT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value64"/> array to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as UnsignedInt).
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Time, Float, HexBytes.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddUInt64Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<UInt64> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value64 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value64"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.SignedInt"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Time, Float, HexBytes.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddInt64(
        ReadOnlySpan<char> name,
        Int64 value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.SignedInt,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value64, format, tag);
        this.AddDataT(unchecked((UInt64)value));
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value64"/> array to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.SignedInt"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Time, Float, HexBytes.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddInt64Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<Int64> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.SignedInt,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value64 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeader.IntPtrEncoding"/> field to the event (either
    /// <see cref="EventHeaderFieldEncoding.Value32"/> or <see cref="EventHeaderFieldEncoding.Value64"/>.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as UnsignedInt).
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Time, Float, HexBytes.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddUIntPtr(
        ReadOnlySpan<char> name,
        nuint value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeader.IntPtrEncoding, format, tag);
        this.AddDataT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeader.IntPtrEncoding"/> array to the event (either
    /// <see cref="EventHeaderFieldEncoding.Value32"/> or <see cref="EventHeaderFieldEncoding.Value64"/>.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as UnsignedInt).
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Time, Float, HexBytes.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddUIntPtrArray(
        ReadOnlySpan<char> name,
        ReadOnlySpan<nuint> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeader.IntPtrEncoding | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeader.IntPtrEncoding"/> field to the event (either
    /// <see cref="EventHeaderFieldEncoding.Value32"/> or <see cref="EventHeaderFieldEncoding.Value64"/>.
    /// Default format is <see cref="EventHeaderFieldFormat.SignedInt"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Time, Float, HexBytes.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddIntPtr(
        ReadOnlySpan<char> name,
        nint value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.SignedInt,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeader.IntPtrEncoding, format, tag);
        this.AddDataT(unchecked((nuint)value));
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeader.IntPtrEncoding"/> array to the event (either
    /// <see cref="EventHeaderFieldEncoding.Value32"/> or <see cref="EventHeaderFieldEncoding.Value64"/>.
    /// Default format is <see cref="EventHeaderFieldFormat.SignedInt"/>.
    /// Applicable formats include: UnsignedInt, SignedInt, HexInt, Time, Float, HexBytes.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddIntPtrArray(
        ReadOnlySpan<char> name,
        ReadOnlySpan<nint> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.SignedInt,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeader.IntPtrEncoding | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value32"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Float"/>.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddFloat32(
        ReadOnlySpan<char> name,
        Single value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Float,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value32, format, tag);
        this.AddDataT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value32"/> array to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Float"/>.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddFloat32Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<Single> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Float,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value32 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value64"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Float"/>.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddFloat64(
        ReadOnlySpan<char> name,
        Double value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Float,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value64, format, tag);
        this.AddDataT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value64"/> array to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Float"/>.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddFloat64Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<Double> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Float,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value64 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value128"/> field to the event,
    /// fixing byte order as appropriate for GUID--UUID conversion.
    /// Default format is <see cref="EventHeaderFieldFormat.Uuid"/>.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddGuid(
        ReadOnlySpan<char> name,
        in Guid value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Uuid,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value128, format, tag);
        Utility.WriteGuidBigEndian(this.data.ReserveSpanFor(16), value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value128"/> array to the event,
    /// fixing byte order as appropriate for GUID--UUID conversion.
    /// Default format is <see cref="EventHeaderFieldFormat.Uuid"/>.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddGuidArray(
        ReadOnlySpan<char> name,
        ReadOnlySpan<Guid> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Uuid,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value128 | VArrayFlag, format, tag);
        var dest = this.data.ReserveSpanFor((uint)sizeof(UInt16) + (uint)values.Length * 16);
        var count = (UInt16)values.Length;
        MemoryMarshal.Write(dest, ref count);
        var pos = sizeof(UInt16);
        foreach (var v in values)
        {
            Utility.WriteGuidBigEndian(dest.Slice(pos), v);
            pos += 16;
        }
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value128"/> field to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as HexBytes).
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddValue128(
        ReadOnlySpan<char> name,
        EventHeaderValue128 value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value128, format, tag);
        MemoryMarshal.Write(this.data.ReserveSpanFor(16), ref value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.Value128"/> array to the event.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as HexBytes).
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddValue128Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<EventHeaderValue128> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.Value128 | VArrayFlag, format, tag);
        this.AddDataArray(values);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.ZStringChar8"/> field to the event (zero-terminated
    /// sequence of 8-bit values). You should prefer AddString8 over this method in most scenarios.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddZString8(
        ReadOnlySpan<char> name,
        ReadOnlySpan<byte> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.ZStringChar8, format, tag);
        this.AddDataZStringT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.ZStringChar8"/> array to the event (array of
    /// zero-terminated 8-bit strings). You should prefer AddString8Array over this method in most
    /// scenarios.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddZString8Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<ReadOnlyMemory<byte>> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.ZStringChar8 | VArrayFlag, format, tag);
        this.AddDataT((UInt16)values.Length);
        foreach (var v in values)
        {
            this.AddDataZStringT(v.Span);
        }
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.ZStringChar16"/> field to the event (zero-terminated
    /// sequence of 16-bit values). You should prefer AddString16 over this method in most scenarios.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddZString16(
        ReadOnlySpan<char> name,
        ReadOnlySpan<UInt16> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.ZStringChar16, format, tag);
        this.AddDataZStringT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.ZStringChar16"/> array to the event (array of
    /// zero-terminated 16-bit strings). You should prefer AddString16Array over this method in most
    /// scenarios.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddZString16Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<ReadOnlyMemory<UInt16>> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.ZStringChar16 | VArrayFlag, format, tag);
        this.AddDataT((UInt16)values.Length);
        foreach (var v in values)
        {
            this.AddDataZStringT(v.Span);
        }
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.ZStringChar16"/> field to the event (zero-terminated
    /// sequence of 16-bit values). You should prefer AddString16 over this method in most scenarios.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddZString16(
        ReadOnlySpan<char> name,
        ReadOnlySpan<Char> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.ZStringChar16, format, tag);
        this.AddDataZStringT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.ZStringChar16"/> array to the event (array of
    /// zero-terminated 16-bit strings). You should prefer AddString16Array over this method in most
    /// scenarios.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddZString16Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<ReadOnlyMemory<Char>> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.ZStringChar16 | VArrayFlag, format, tag);
        this.AddDataT((UInt16)values.Length);
        foreach (var v in values)
        {
            this.AddDataZStringT(v.Span);
        }
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.ZStringChar16"/> array to the event (array of
    /// zero-terminated 16-bit strings). You should prefer AddString16Array over this method in most
    /// scenarios.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddZString16Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<String> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.ZStringChar16 | VArrayFlag, format, tag);
        this.AddDataT((UInt16)values.Length);
        foreach (var v in values)
        {
            this.AddDataZStringT(v.AsSpan());
        }
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.ZStringChar32"/> field to the event (zero-terminated
    /// sequence of 32-bit values). You should prefer AddString32 over this method in most scenarios.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddZString32(
        ReadOnlySpan<char> name,
        ReadOnlySpan<UInt32> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.ZStringChar32, format, tag);
        this.AddDataZStringT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.ZStringChar32"/> array to the event (array of
    /// zero-terminated 32-bit strings). You should prefer AddString32Array over this method in most
    /// scenarios.
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddZString32Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<ReadOnlyMemory<UInt32>> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.ZStringChar32 | VArrayFlag, format, tag);
        this.AddDataT((UInt16)values.Length);
        foreach (var v in values)
        {
            this.AddDataZStringT(v.Span);
        }
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.StringLength16Char8"/> field to the event
    /// (counted sequence of 8-bit values, e.g. a UTF-8 string or a binary blob).
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddString8(
        ReadOnlySpan<char> name,
        ReadOnlySpan<byte> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.StringLength16Char8, format, tag);
        this.AddDataStringT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.StringLength16Char8"/> array to the event
    /// (e.g. array of binary blobs, array of UTF-8 strings, array of Latin1 strings, etc.).
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddString8Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<ReadOnlyMemory<byte>> values,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.StringLength16Char8 | VArrayFlag, format, tag);
        this.AddDataT((UInt16)values.Length);
        foreach (var v in values)
        {
            this.AddDataStringT(v.Span);
        }
        return this;
    }


    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.StringLength16Char16"/> field to the event
    /// (counted sequence of 16-bit values, e.g. a UTF-16 string).
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddString16(
        ReadOnlySpan<char> name,
        ReadOnlySpan<UInt16> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.StringLength16Char16, format, tag);
        this.AddDataStringT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.StringLength16Char16"/> array to the event
    /// (e.g. array of UTF-16 strings).
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddString16Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<ReadOnlyMemory<UInt16>> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.StringLength16Char16 | VArrayFlag, format, tag);
        this.AddDataT((UInt16)value.Length);
        foreach (var v in value)
        {
            this.AddDataStringT(v.Span);
        }
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.StringLength16Char16"/> field to the event
    /// (counted sequence of 16-bit values, e.g. a UTF-16 string).
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddString16(
        ReadOnlySpan<char> name,
        ReadOnlySpan<Char> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.StringLength16Char16, format, tag);
        this.AddDataStringT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.StringLength16Char16"/> array to the event
    /// (array of strings).
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddString16Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<ReadOnlyMemory<Char>> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.StringLength16Char16 | VArrayFlag, format, tag);
        this.AddDataT((UInt16)value.Length);
        foreach (var v in value)
        {
            this.AddDataStringT(v.Span);
        }
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.StringLength16Char16"/> array to the event
    /// (array of strings).
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddString16Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<String> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.StringLength16Char16 | VArrayFlag, format, tag);
        this.AddDataT((UInt16)value.Length);
        foreach (var v in value)
        {
            this.AddDataStringT(v.AsSpan());
        }
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.StringLength16Char32"/> field to the event
    /// (counted sequence of 32-bit values, e.g. a UTF-32 string).
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddString32(
        ReadOnlySpan<char> name,
        ReadOnlySpan<UInt32> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.StringLength16Char32, format, tag);
        this.AddDataStringT(value);
        return this;
    }

    /// <summary>
    /// Adds a <see cref="EventHeaderFieldEncoding.StringLength16Char32"/> array to the event
    /// (e.g. array of UTF-32 strings).
    /// Default format is <see cref="EventHeaderFieldFormat.Default"/> (formats as StringUtf).
    /// Applicable formats include: StringUtf, HexBytes, StringUtfBom, StringXml, StringJson.
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddString32Array(
        ReadOnlySpan<char> name,
        ReadOnlySpan<ReadOnlyMemory<UInt32>> value,
        EventHeaderFieldFormat format = EventHeaderFieldFormat.Default,
        ushort tag = 0)
    {
        this.AddMeta(name, EventHeaderFieldEncoding.StringLength16Char32 | VArrayFlag, format, tag);
        this.AddDataT((UInt16)value.Length);
        foreach (var v in value)
        {
            this.AddDataStringT(v.Span);
        }
        return this;
    }

    /// <summary>
    /// Adds a new logical field with the specified name and indicates that the next
    /// fieldCount logical fields should be considered as members of this field.
    /// Note that fieldCount must be in the range 1 to 127 (must NOT be 0).
    /// </summary>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddStruct(ReadOnlySpan<char> name, byte fieldCount, ushort tag = 0)
    {
        if (fieldCount < 1 || fieldCount > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldCount));
        }

        this.AddMeta(name, EventHeaderFieldEncoding.Struct, (EventHeaderFieldFormat)fieldCount, tag);
        return this;
    }

    /// <summary>
    /// Advanced: For use when field count is not yet known.
    /// Adds a new logical field with the specified name and indicates that the next
    /// initialFieldCount logical fields should be considered as members of this field.
    /// Note that initialFieldCount must be in the range 1 to 127 (must NOT be 0).
    /// Returns the position of the field count so that it can subsequently updated by
    /// a call to SetStructFieldCount.
    /// </summary>
    /// <param name="name">The name of the field.</param>
    /// <param name="initialFieldCount">The field count for the struct.</param>
    /// <param name="tag">User-defined field tag.</param>
    /// <param name="metadataPosition">
    /// Receives the offset of the field count within the metadata.
    /// You can use this value with SetStructFieldCount.
    /// </param>
    /// <returns>this</returns>
    public EventHeaderDynamicBuilder AddStruct(ReadOnlySpan<char> name, byte initialFieldCount, ushort tag, out int metadataPosition)
    {
        if (initialFieldCount < 1 || initialFieldCount > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(initialFieldCount));
        }

        metadataPosition = this.AddMeta(name, EventHeaderFieldEncoding.Struct, (EventHeaderFieldFormat)initialFieldCount, tag);
        return this;
    }

    /// <summary>
    /// Advanced: Resets the number of logical fields in the specified structure.
    /// </summary>
    /// <param name="metadataPosition">
    /// The position of the metadata field within the structure. This value is
    /// returned by the AddStruct method.
    /// </param>
    /// <param name="fieldCount">
    /// The actual number of fields in the structure. This value must be in the range
    /// 1 to 127.
    /// </param>
    public void SetStructFieldCount(int metadataPosition, byte fieldCount)
    {
        if (fieldCount < 1 || fieldCount > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(fieldCount));
        }

        var bytes = this.meta.Bytes;
        bytes[metadataPosition] = (byte)((bytes[metadataPosition] & 0x80) | (fieldCount & 0x7F));
    }

    /// <summary>
    /// If !tracepoint.IsEnabled, immediately returns EBADF.
    /// Otherwise, writes this builder's event to the specified tracepoint.
    /// </summary>
    /// <param name="tracepoint">
    /// The tracepoint (provider name, level, and keyword) to which the event should
    /// be written.
    /// </param>
    /// <returns>
    /// 0 if event was written, errno otherwise.
    /// Typically returns EBADF if no data collection sessions are listening for the tracepoint.
    /// The return value is for debugging/diagnostic purposes and is usually ignored in normal operation
    /// since most programs should continue to function even when tracing is not configured.
    /// </returns>
    public int Write(EventHeaderDynamicTracepoint tracepoint)
    {
        unsafe
        {
            return tracepoint.WriteRaw(this, null, null);
        }
    }

    /// <summary>
    /// If !tracepoint.IsEnabled, immediately returns EBADF.
    /// Otherwise, writes this builder's event to the specified tracepoint.
    /// </summary>
    /// <param name="tracepoint">
    /// The tracepoint (provider name, level, and keyword) to which the event should
    /// be written.
    /// </param>
    /// <param name="activityId">
    /// ID of the event's activity.
    /// </param>
    /// <returns>
    /// 0 if event was written, errno otherwise.
    /// Typically returns EBADF if no data collection sessions are listening for the tracepoint.
    /// The return value is for debugging/diagnostic purposes and is usually ignored in normal operation
    /// since most programs should continue to function even when tracing is not configured.
    /// </returns>
    public int Write(EventHeaderDynamicTracepoint tracepoint, in Guid activityId)
    {
        unsafe
        {
            fixed (Guid* activityIdPtr = &activityId)
            {
                return tracepoint.WriteRaw(this, activityIdPtr, null);
            }
        }
    }

    /// <summary>
    /// If !tracepoint.IsEnabled, immediately returns EBADF.
    /// Otherwise, writes this builder's event to the specified tracepoint.
    /// </summary>
    /// <param name="tracepoint">
    /// The tracepoint (provider name, level, and keyword) to which the event should
    /// be written.
    /// </param>
    /// <param name="activityId">
    /// ID of the event's activity.
    /// </param>
    /// <param name="relatedActivityId">
    /// ID of the activity's parent. Usually used only when Opcode = Start,
    /// i.e. when starting a new activity.
    /// </param>
    /// <returns>
    /// 0 if event was written, errno otherwise.
    /// Typically returns EBADF if no data collection sessions are listening for the tracepoint.
    /// The return value is for debugging/diagnostic purposes and is usually ignored in normal operation
    /// since most programs should continue to function even when tracing is not configured.
    /// </returns>
    public int Write(EventHeaderDynamicTracepoint tracepoint, in Guid activityId, in Guid relatedActivityId)
    {
        unsafe
        {
            fixed (Guid* activityIdPtr = &activityId, relatedActivityIdPtr = &relatedActivityId)
            {
                return tracepoint.WriteRaw(this, activityIdPtr, relatedActivityIdPtr);
            }
        }
    }

    /// <summary>
    /// Releases resources used by this builder (returns memory to the array pool).
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// If disposing, returns allocations to the array pool.
    /// </summary>
    /// <param name="disposing">true if disposing, false if finalizing.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.meta.Dispose();
            this.data.Dispose();
        }
    }

    internal ReadOnlySpan<byte> GetRawMeta() => this.meta.UsedSpan;
    internal ReadOnlySpan<byte> GetRawData() => this.data.UsedSpan;

    private void AddDataT(UInt16 value)
    {
        MemoryMarshal.Write(this.data.ReserveSpanFor(sizeof(UInt16)), ref value);
    }

    private void AddDataT<T>(T value)
        where T : unmanaged
    {
        uint sizeofT;
        unsafe
        {
            sizeofT = (uint)sizeof(T);
        }

        MemoryMarshal.Write(this.data.ReserveSpanFor(sizeofT), ref value);
    }

    private void AddDataArray<T>(ReadOnlySpan<T> values)
        where T : unmanaged
    {
        var valuesBytes = MemoryMarshal.AsBytes(values);
        var dest = this.data.ReserveSpanFor((uint)sizeof(UInt16) + (uint)valuesBytes.Length);
        var count = (UInt16)values.Length;
        MemoryMarshal.Write(dest, ref count);
        valuesBytes.CopyTo(dest.Slice(sizeof(UInt16)));
    }

    private void AddDataStringT<T>(ReadOnlySpan<T> value)
        where T : unmanaged
    {
        if (value.Length > UInt16.MaxValue)
        {
            value = value.Slice(0, UInt16.MaxValue);
        }

        var valueBytes = MemoryMarshal.AsBytes(value);
        var span = this.data.ReserveSpanFor((uint)sizeof(UInt16) + (uint)valueBytes.Length);
        UInt16 len = (UInt16)value.Length;
        MemoryMarshal.Write(span, ref len);
        valueBytes.CopyTo(span.Slice(sizeof(UInt16)));
    }

    private void AddDataZStringT<T>(ReadOnlySpan<T> value)
        where T : unmanaged, IEquatable<T>
    {
        if (value.Length > UInt16.MaxValue)
        {
            value = value.Slice(0, UInt16.MaxValue);
        }

        var len = 0;
        while (len < value.Length)
        {
            if (value[len].Equals(default))
            {
                value = value.Slice(0, len);
                break;
            }

            len += 1;
        }

        var valueBytes = MemoryMarshal.AsBytes(value);
        var span = this.data.ReserveSpanFor((uint)valueBytes.Length);
        valueBytes.CopyTo(span);
        this.AddDataT(default(T));
    }

    /// <returns>The position of the format byte within the metadata array (for AddStruct).</returns>
    private int AddMeta(
        ReadOnlySpan<char> name,
        EventHeaderFieldEncoding encoding,
        EventHeaderFieldFormat format,
        UInt16 tag)
    {
        int metadataPos;
        Debug.Assert(name.IndexOf('\0') < 0, "Field name must not have embedded NUL characters.");
        Debug.Assert(!encoding.HasChainFlag());
        Debug.Assert(!format.HasChainFlag());

        int pos;

        var nameLength = name.Length;
        var nameMaxByteCount = this.Utf8NameEncoding.GetMaxByteCount(name.Length);
        if (tag != 0)
        {
            pos = this.meta.ReserveSpaceFor((uint)nameMaxByteCount + 5);
            var metaSpan = this.meta.UsedSpan;
            pos += this.Utf8NameEncoding.GetBytes(name, metaSpan.Slice(pos));
            metaSpan[pos++] = 0;
            metaSpan[pos++] = (byte)(encoding | EventHeaderFieldEncoding.ChainFlag);
            metaSpan[pos++] = (byte)(format | EventHeaderFieldFormat.ChainFlag);
            MemoryMarshal.Write(metaSpan.Slice(pos), ref tag);
            pos += sizeof(UInt16);
            this.meta.SetUsed(pos);
            metadataPos = pos - 3; // Returned from AddStruct.
        }
        else if (format != 0)
        {
            pos = this.meta.ReserveSpaceFor((uint)nameMaxByteCount + 3);
            var metaSpan = this.meta.UsedSpan;
            pos += this.Utf8NameEncoding.GetBytes(name, metaSpan.Slice(pos));
            metaSpan[pos++] = 0;
            metaSpan[pos++] = (byte)(encoding | EventHeaderFieldEncoding.ChainFlag);
            metaSpan[pos++] = (byte)format;
            this.meta.SetUsed(pos);
            metadataPos = pos - 1; // Returned from AddStruct.
        }
        else
        {
            pos = this.meta.ReserveSpaceFor((uint)nameMaxByteCount + 2);
            var metaSpan = this.meta.UsedSpan;
            pos += this.Utf8NameEncoding.GetBytes(name, metaSpan.Slice(pos));
            metaSpan[pos++] = 0;
            metaSpan[pos++] = (byte)encoding;
            this.meta.SetUsed(pos);
            metadataPos = -1; // AddStruct doesn't accept format == 0.
        }

        return metadataPos; // For AddStruct: Position of the format byte, or -1 if format == 0.
    }

    private struct Vector : IDisposable
    {
        public Vector(int initialCapacity)
        {
            Debug.Assert(0 < initialCapacity, "initialCapacity <= 0");
            Debug.Assert(initialCapacity <= 65536, "initialCapacity > 65536");
            Debug.Assert((initialCapacity & (initialCapacity - 1)) == 0, "initialCapacity is not a power of 2.");
            this.Bytes = ArrayPool<byte>.Shared.Rent(initialCapacity);
        }

        public byte[] Bytes { readonly get; private set; }

        public int Used { readonly get; private set; }

        public readonly Span<byte> UsedSpan => new Span<byte>(this.Bytes, 0, this.Used);

        public void Dispose()
        {
            var oldBytes = this.Bytes;
            this.Bytes = Array.Empty<byte>();
            this.Used = 0;

            if (oldBytes.Length != 0)
            {
                ArrayPool<byte>.Shared.Return(oldBytes);
            }
        }

        public void Reset()
        {
            this.Used = 0;
        }

        public void AddByte(byte value)
        {
            var oldUsed = this.Used;
            Debug.Assert(this.Bytes.Length >= oldUsed);

            if (this.Bytes.Length == oldUsed)
            {
                this.Grow(1);
            }

            this.Bytes[oldUsed] = value;
            this.Used = oldUsed + 1;
        }

        public int ReserveSpaceFor(uint requiredSize)
        {
            int oldUsed = this.Used;
            Debug.Assert(this.Bytes.Length >= oldUsed);

            // condition will always be true if requiredSize > int.MaxValue.
            if ((uint)(this.Bytes.Length - oldUsed) < requiredSize)
            {
                // Grow will always throw if requiredSize > int.MaxValue.
                this.Grow(requiredSize);
            }

            this.Used = oldUsed + (int)requiredSize;
            return oldUsed;
        }

        public Span<byte> ReserveSpanFor(uint requiredSize)
        {
            int oldUsed = this.Used;
            Debug.Assert(this.Bytes.Length >= oldUsed);

            // condition will always be true if requiredSize > int.MaxValue.
            if ((uint)(this.Bytes.Length - oldUsed) < requiredSize)
            {
                // Grow will always throw if requiredSize > int.MaxValue.
                this.Grow(requiredSize);
            }

            this.Used = oldUsed + (int)requiredSize;
            return new Span<byte>(this.Bytes, oldUsed, (int)requiredSize);
        }

        public void SetUsed(int newUsed)
        {
            Debug.Assert(newUsed <= this.Used);
            this.Used = newUsed;
        }

        private void Grow(uint requiredSize)
        {
            var oldCapacity = this.Bytes.Length;
            if (oldCapacity <= 0)
            {
                throw new ObjectDisposedException(nameof(EventHeaderDynamicBuilder));
            }

            var newCapacity = (uint)oldCapacity + requiredSize;
            if (newCapacity < requiredSize || newCapacity > 65536)
            {
                throw new InvalidOperationException("Event too large");
            }

            var sharedPool = ArrayPool<byte>.Shared;
            var oldArray = this.Bytes;
            var newArray = sharedPool.Rent((int)newCapacity);

            Buffer.BlockCopy(oldArray, 0, newArray, 0, this.Used);
            this.Bytes = newArray;
            sharedPool.Return(oldArray);
        }
    }
}
