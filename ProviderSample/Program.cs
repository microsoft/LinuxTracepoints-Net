namespace ProviderSample;

using Microsoft.LinuxTracepoints;
using Microsoft.LinuxTracepoints.Provider;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Runtime.InteropServices;
using Debug = System.Diagnostics.Debug;

internal static class Program
{
    /// <summary>
    /// Linux error EBADF = 9.
    /// </summary>
    private const int EBADF = 9;

    public static void Main()
    {
        try
        {
            DemonstratePerfTracepoints();
            DemonstrateEventHeaderTracepoints();
            DemonstrateEventHeaderAllTypes();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unexpcted error: {ex}");
        }
    }

    private static void DemonstratePerfTracepoints()
    {
        int errno;

        /*
        The PerfTracepoint class provides direct access to the Linux user_events Tracepoint system.

        - Constructing a PerfTracepoint will attempt to register a tracepoint on the system.

          - Note that the PerfTracepoint class does not parse or validate the command string (the
            string with the tracepoint name, field names, and field types). It is passed directly
            to the kernel.

        - If registration succeeds, you can use the object to check enable state (determine whether
          anyone is collecting your tracepoint) and to write an event (will be recorded by anyone that
          is collecting your tracepoint).

          - Note that the PerfTracepoint class does not pack or validate the event data. It is up to
            the caller to pack the data according to the field declarations in the command string.

        - If registration fails, enable state will always be false and writing an event will always be
          a no-op.

        - Disposing/finalizing a PerfTracepoint unregisters it. After the tracepoint has been Disposed,
          the IsEnabled property will return false and the Write method will be a no-op.

        These tracepoints can be collected using any tracepoint tool, such as the Linux perf tool (e.g.
        perf record -e "user_events:MyTracepointWithNoFields,user_events:MyTracepointWithTwoFields") or
        the tracepoint-collect tool from the github.com/microsoft/LinuxTracepoints repo (e.g.
        tracepoint-collect :MyTracepointWithNoFields :MyTracepointWithTwoFields).
        */

        // Example of a simple tracepoint (no fields):

        // Construct a PerfTracepoint to register it.
        // You'll normally construct and register all of your tracepoints at component initialization.
        // The properties and methods of PerfTracepoint are thread-safe, so tracepoints can be shared
        // with all threads in your component.
        var simpleTracepoint = new PerfTracepoint("MyTracepointWithNoFields");

        // If registration fails, you can get the errno for debugging/diagnostic purposes.
        // However, in your final code, you'll normally ignore registration errors because
        // most programs should continue working normally even if tracing isn't set up.
        // If registration fails, tracepoint methods and properties will be safe no-ops.
        Console.WriteLine($"simpleTracepoint.RegisterResult = {simpleTracepoint.RegisterResult}");

        // For simple events, you can just call Write. If nobody is listening to the event,
        // this will return immediately.
        errno = simpleTracepoint.Write(); // MyTracepointWithNoFields has no fields.

        // If nobody is collecting the tracepoint, Write immediately returns EBADF.
        // You can use the errno returned from Write for debugging/diagnostic purposes.
        // However, in your final code, you'll normally ignore Write errors because
        // most programs should continue working normally even if tracing isn't set up.
        Console.WriteLine($"simpleTracepoint.Write = {errno}");

        // In more complex scenarios, you might want to know whether somebody is listening to
        // the event. You can check the IsEnabled property for that.
        if (simpleTracepoint.IsEnabled)
        {
            Console.WriteLine("simpleTracepoint is enabled.");
        }

        // Dispose all of your tracepoints at component shutdown. This will unregister the tracepoint.
        // After tracepoint has been disposed, IsEnable returns false and Write is a no-op.
        simpleTracepoint.Dispose();
        Debug.Assert(!simpleTracepoint.IsEnabled);
        Debug.Assert(EBADF == simpleTracepoint.Write());

        // Example of a more complex tracepoint:

        // First field is a 32-bit int.
        // Second field is a variable-length string of 8-bit chars, which is encoded as a rel_loc field.
        // Full syntax of the tracepoint registration string is defined in
        // https://docs.kernel.org/trace/user_events.html#command-format
        var complexTracepoint = new PerfTracepoint("MyTracepointWithTwoFields int Field1; __rel_loc char[] Field2");
        Console.WriteLine($"complexTracepoint.RegisterResult = {complexTracepoint.RegisterResult}");

        // You can safely call Write(...) even when nobody is collecting the tracepoint, but
        // usually it's better to check IsEnabled first. That way you don't waste time collecting
        // data and passing parameters to Write when it is just going to immediately return.
        if (complexTracepoint.IsEnabled)
        {
            // Prepare the parameters that you will need to include in your event.
            int field1 = 25;
            ReadOnlySpan<byte> field2 = "SomeChars\0"u8; // Note that the char[] field type expects nul-terminated 8-bit string.

            // Variable-length fields must be packed as a rel_loc.
            // The field slot has a 32-bit integer that is the RelLoc value.
            // High 16 bits of RelLoc are "size of data".
            // Low 16 bits of RelLoc are "offset from end of slot to start of data".
            // The actual data goes after all field slots.
            // In this case, since this is the last field slot, the offset is 0.
            UInt32 field2RelLoc = 0u | ((UInt32)field2.Length << 16);
            errno = complexTracepoint.Write(
                (ReadOnlySpan<int>)MemoryMarshal.CreateSpan(ref field1, 1), // In .NET 7 or later, simplify as: new ReadOnlySpan<int>(ref field1).
                (ReadOnlySpan<UInt32>)MemoryMarshal.CreateSpan(ref field2RelLoc, 1),
                field2);
            Console.WriteLine($"complexTracepoint.Write = {errno}");
        }

        // Unregister tracepoints during component cleanup.
        complexTracepoint.Dispose();
    }

    private static void DemonstrateEventHeaderTracepoints()
    {
        int errno;

        /*
        The EventHeaderDynamicProvider class manages EventHeader-encoded Linux Tracepoints.

        EventHeaderDynamicProvider is a collection of tracepoints that all share the same ProviderName
        and will all be unregistered at the same time (i.e. when provider is disposed at component
        cleanup). All provider operations are thread-safe (reader lock for Find operations, writer lock
        for Register operations).

        Calling provider.Register(level, keyword) registers and returns a tracepoint based on the
        ProviderName + Level + Keyword (throws if the level+keyword combination is already
        registered).

        You can later call provider.Find(level, keyword) to get a previously-registered tracepoint.
        As an alternative, you can keep track of the tracepoints returned by Register in your own
        data structure to avoid the overhead of acquiring the reader lock and looking up the
        tracepoint each time it is needed.

        You can call provider.FindOrRegister(level, keyword) to register a tracepoint the first time
        it is used. Avoid this if possible because this makes it harder for tracepoint consumers to
        find your tracepoints. It's usually better to Register all needed level+keyword combinations
        during component initialization.

        Calling provider.Dispose() unregisters all tracepoints. Subsequent calls to Register or Find
        will throw ObjectDisposedException.

        The EventHeaderDynamicTracepoint class manages a single tracepoint's registration. EventHeader
        tracepoints are named based on the provider name + level + keyword, e.g. "MyProvider_L1K1" for
        a tracepoint for the "MyProvider" provider, Level 1 (error), and keyword 0x1 (provider-defined).
        You get a tracepoint from provider.Register(level, keyword). You can determine whether any
        active perf collection sessions are collecting the tracepoint's event by checking the
        tracepoint.IsEnabled property. (You cannot set the IsEnabled property -- it becomes true when
        you start a trace collection session that collects it.) You can write an event to a tracepoint
        using an event builder.

        The EventHeaderDynamicBuilder class manages the attributes and fields for an event. To write
        an event, you create a builder, call builder.Reset(eventName), call builder methods to set
        event attributes and add fields, then call builder.Write(tracepoint) to write the event.
        */

        // Construct an EventHeaderDynamicProvider for your provider name.
        // ProviderName should be short and should uniquely identify the provider for purposes of
        // tracepoint filtering. Typically namespaced, e.g. ProviderName = "MyCompany_MyOrg_MyComponent".
        // The provider should generally be created during component initialization and disposed during
        // component cleanup.
        var provider = new EventHeaderDynamicProvider("MyProviderName");

        // Each tracepoint has a provider name, a severity (level) and a 64-bit category mask (keyword).
        // Provider name should be namespaced, level should use EventLevel values, and keyword bits are
        // defined by you (i.e. you might define bit 0x2 as category "I/O" and bit 0x4 as "UI").
        // This allows consumers to enable/disable all events with a particular provider, level, and
        // keyword by enabling/disabling a single tracepoint in their tracepoint collection session.
        // The tracepoint name is a combination of the provider name followed by a suffix that encodes
        // the level and keyword, e.g. MyProviderName_L5K1f for "MyProviderName" + Verbose + keyword 0x1f.
        // Events should not use Level = 0 or Keyword = 0.

        // The provider manages the set of registered tracepoints. Provider operations are thread-safe.
        //
        // Supported operations:
        // - Register: throws if the specified level+keyword combination is already registered, otherwise
        //   returns the newly-registered tracepoint; for use in component initialization.
        // - FindOrRegister: returns an existing tracepoint if already registered, otherwise registers a
        //   new tracepoint; for use when tracepoints are registered on first use.
        // - Find: returns an existing tracepoint if already registered, otherwise returns null; for use
        //   when tracepoints are expected to already be registered but are not being cached (i.e. when
        //   the tracepoints were registered during component initialization, but they were not cached).

        // It is best to register all the provider + level + keyword combinations that your component will
        // need during component initialization. That way, tracepoint consumers can see a complete list of
        // all tracepoints that they might need to collect from your component rather than only being able
        // to see the tracepoint once your component has exercised a specific code path.
        var tpVerbose1 = provider.Register(EventLevel.Verbose, 1); // Register at component startup.

        // If registration fails, you can get the errno for debugging/diagnostic purposes.
        // However, in your final code, you'll normally ignore registration errors because
        // most programs should continue working normally even if tracing isn't set up.
        // If registration fails, tracepoint methods and properties will be safe no-ops.
        Console.WriteLine($"tpVerbose1.RegisterResult = {tpVerbose1.RegisterResult}");

        // If you can't register all level + keyword combinations during component initialization, it is
        // possible to register them at first use.
        var tpWarning1 = provider.FindOrRegister(EventLevel.Warning, 1); // Register on-demand.
        Console.WriteLine($"tpWarning1.RegisterResult = {tpWarning1.RegisterResult}");

        // For optimal performance, keep the tracepoint reference returned from Register to avoid lookup
        // overhead. However, if you don't want to manage a bunch of tracepoint references, you can look
        // them up later with Find.
        Debug.Assert(tpVerbose1 == provider.Find(EventLevel.Verbose, 1));
        Debug.Assert(tpWarning1 == provider.Find(EventLevel.Warning, 1));

        // When you want to write an event: use an EventHeaderDynamicBuilder to set event attributes and
        // fields, then write the builder's event to a tracepoint with the desired provider + level + keyword.
        using (var builder = new EventHeaderDynamicBuilder())
        {
            // Call builder.Reset to assign the event's name.
            builder.Reset("WarningEventName"); // Event name = "WarningEventName"

            // Call other builder methods to configure event attributes or add event fields.
            builder.AddInt32("Field1", 24);

            // Writes a "WarningEventName { Field1 = 24 }" event to the "MyProviderName_L3K1" tracepoint.
            errno = builder.Write(tpWarning1);

            // You can use the errno returned from Write for debugging/diagnostic purposes.
            // However, in your final code, you'll normally ignore Write errors because
            // most programs should continue working normally even if tracing isn't set up.
            Console.WriteLine($"tpWarning1.Write = {errno}");
        }

        // The builder.Write method is a no-op (immediately returns EBADF) if nobody is collecting the
        // tracepoint. While it returns immediately, it's faster to not prepare the event data or call
        // Write at all. To do this, check the tracepoint.IsEnabled property before preparing or writing
        // events.
        if (tpVerbose1.IsEnabled)
        {
            // - EventBuilder is reusable, so if we are writing a bunch of events in sequence, we can
            //   save a bit of garbage collector overhead by using one builder for all of the events.
            // - EventBuilder is disposable - it uses ArrayPool for its temporary buffers, and returns
            //   the buffers to the pool when it is disposed.
            using (var builder = new EventHeaderDynamicBuilder())
            {
                // Most builder methods return 'this' so you can chain the method calls if you like.
                errno = builder.Reset("MyEventName")
                    .AddInt32("Field1", 25)
                    .AddString16("Field2", "SomeStringValue")
                    .Write(tpVerbose1);
                Console.WriteLine($"tpVerbose1.Write = {errno}");

                // This next event will declare the start of an "activity".

                // An activity is a group of events that can be grouped during analysis. An activity
                // consists of an event with opcode = Start, then any number of info events, then an
                // event with opcode = Stop. All of the events in the activity should be tagged with
                // the same activity ID. The Start event can optionally specify the ID of a related
                // (parent) activity to created a nested activity (not shown here).
                var activityID = Guid.NewGuid();

                // We'll reuse the existing builder to minimize overhead.
                builder.Reset("MyActivityStart")
                    .SetOpcode(EventOpcode.Start) // To start an activity: write an event with opcode = start.
                    .AddString8("TaskName", "James"u8) // Field with a UTF-8 string value.
                    .Write(tpVerbose1, activityID); // Specify the activity ID (if any) and related activity ID (if any) in the Write method.

                // The next event illustrates some options you might use in your events.

                // Event name can be provided as either a ReadOnlySpan<char> (i.e. a string or string
                // segment) or a ReadOnlySpan<byte> (UTF-8 encoded bytes). Using UTF-8 saves a bit of
                // string transcoding overhead.
                builder.Reset("MyActivityProgress"u8); // To avoid UTF-16 to UTF-8 conversion overhead, you can use u8 for event name.

                // Field name can be provided as either a ReadOnlySpan<char> (i.e. a string or string
                // segment) or a ReadOnlySpan<byte> (UTF-8 encoded bytes). Using UTF-8 saves a bit of
                // string transcoding overhead.
                builder.AddInt32("NameUTF8"u8, 1234); // To avoid UTF-16 to UTF-8 conversion overhead, you can use u8 for field name.

                // Formatting: Applying a "Time" format to an Int64 field turns it into a Time64 field.
                // Different formats apply to different types, e.g. Int32 can format as HexInt, Errno, Pid, Time,
                // StringUtf, or IPv4, and String8 can fornat as Latin1, UTF-8, UTF-with-BOM, or HexBytes.
                builder.AddInt64("Time", (Int64)(DateTime.Now - DateTime.UnixEpoch).TotalSeconds, EventHeaderFieldFormat.Time);

                // Grouping: You can declare a "struct", which is a named grouping of fields.
                builder.AddStruct("Group1", 2); // The next two fields will be part of Group1.

                // Tag: You can add a "tag" to a field, which is a user-defined 16-bit value. This might be
                // a good place to put field category bits, such as "PII" or "Sum".
                builder.AddFloat32("Group1Field1", (float)Math.PI, tag: 0x52);

                // Groups can be nested. The nested group and all its fields count as a single field
                // for grouping purposes.
                builder.AddStruct("Group1Nested"u8, 3);

                // Strings are logged as length-prefixed by default, so you can log a string with embedded nul chars.
                builder.AddString16("Group1Nested1", "Embeds\0Nul\0No\0Problem");

                // Rarely needed, but it is possible to log a nul-terminated string.
                builder.AddZString16("Group1Nested2", "Truncated Here->\0This won't be in the trace.");

                // You can log arrays of things: arrays of integers, arrays of strings, arrays of blobs.
                builder.AddInt32Array("Group1Nested3", [ 1, 2, 3 ]);
                builder.AddString16Array("Strings", [ "Str1", "Str2", "Str3" ]);

                // Note that the bytes in the trace depend only on the method, but the way the bytes are
                // interpreted depend only on the format. So this field will have the bytes of a float64,
                // but those bytes will be decoded as a hexadecimal integer.
                builder.AddFloat64("PiAsHex", Math.PI, EventHeaderFieldFormat.HexInt);

                // This event is part of the activity, so include the activity ID.
                builder.Write(tpVerbose1, activityID);

                // End the activity with a Stop event.
                builder.Reset("MyActivityStop")
                    .SetOpcode(EventOpcode.Stop)
                    .Write(tpVerbose1, activityID); // Stop event must include the activity ID.

                // Advanced scenarios:

                // If you have a common set of fields that need to be added to multiple events, you can
                // avoid the overhead of repeatedly encoding them by encoding them once and saving the
                // resulting data. Do this by using a builder to encode the fields, then dumping the
                // builder's state with GetRawFields(), then adding the saved data to subsequent builders
                // as needed.
                var commonFieldsData = builder.Reset(""u8)
                    .AddInt32("CommonField1", 25)
                    .AddString8("CommonField2", "FieldValue"u8)
                    .GetRawFields();
                builder.Reset("EventWithCommonFields1")
                    .AddInt32("BeforeCommonFields", 10)
                    .AddRawFields(commonFieldsData)
                    .AddInt32("AfterCommonFields", 20)
                    .Write(tpVerbose1);

                // If you don't know how many fields will be in a struct (typically because the struct
                // is being generated based on an IEnumerable<Field>), you can initially create a
                // struct with a placeholder field count then update the field count once the actual
                // field count is known. Note that field count is still limited to 127.
                IEnumerable<KeyValuePair<string, int>> unknownNumberOfFields = [
                    new("Field1", 1),
                    new("Field2", 2),
                    ];
                int metadataPos;
                builder.Reset("EventWithPlaceholderFieldCount")
                    .AddStructWithMetadataPosition("VariableLengthStruct", out  metadataPos);
                byte fieldCount = 0;
                foreach (var field in unknownNumberOfFields)
                {
                    builder.AddInt32(field.Key, field.Value);
                    fieldCount += 1;
                    if (fieldCount == 127)
                    {
                        // At the limit - skip the remaining fields.
                        break;
                    }
                }
                builder.SetStructFieldCount(metadataPos, fieldCount)
                    .Write(tpVerbose1);
            }
        }

        // Unregister all of the tracepoints in the provider at component cleanup.
        provider.Dispose();

        // After calling provider.Dispose(), provider operations will throw ObjectDisposedException.
        // After calling provider.Dispose(), tracepoint.IsEnabled will return false and tracepoint.Write()
        // will be a safe no-op.
        Debug.Assert(!tpVerbose1.IsEnabled);
        using (var builder = new EventHeaderDynamicBuilder())
        {
            Debug.Assert(EBADF == builder.Write(tpVerbose1));
        }
    }

    private static void DemonstrateEventHeaderAllTypes()
    {
        using var b = new EventHeaderDynamicBuilder();
        using var provider = new EventHeaderDynamicProvider("MyProviderName");
        var tp = provider.Register(EventLevel.Verbose, 1);
        ReadOnlySpan<byte> bytes = [ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

        b.Reset("Int8")
            .AddUInt8("UMax", byte.MaxValue, EventHeaderFieldFormat.HexInt)
            .AddUInt8Array("U123", [1, 2, 3])
            .AddUInt8("UMin", byte.MinValue)
            .AddInt8("IMax", sbyte.MaxValue)
            .AddInt8Array("I123", [1, 2, 3])
            .AddInt8("IMin", sbyte.MinValue)
            .Write(tp);
        b.Reset("Int16")
            .AddUInt16("UMax", UInt16.MaxValue, EventHeaderFieldFormat.HexInt)
            .AddUInt16Array("U123", [1, 2, 3])
            .AddUInt16("UMin", UInt16.MinValue)
            .AddInt16("IMax", Int16.MaxValue)
            .AddInt16Array("I123", [1, 2, 3])
            .AddInt16("IMin", Int16.MinValue)
            .AddChar16("CMax", char.MaxValue)
            .AddChar16Array("C123", ['1', '2', '3'])
            .AddChar16("CMin", char.MinValue)
            .Write(tp);
        b.Reset("Int32")
            .AddUInt32("UMax", UInt32.MaxValue, EventHeaderFieldFormat.HexInt)
            .AddUInt32Array("U123", [1, 2, 3])
            .AddUInt32("UMin", UInt32.MinValue)
            .AddInt32("IMax", Int32.MaxValue)
            .AddInt32Array("I123", [1, 2, 3])
            .AddInt32("IMin", Int32.MinValue)
            .Write(tp);
        b.Reset("Int64")
            .AddUInt64("UMax", UInt64.MaxValue, EventHeaderFieldFormat.HexInt)
            .AddUInt64Array("U123", [1, 2, 3])
            .AddUInt64("UMin", UInt64.MinValue)
            .AddInt64("IMax", Int64.MaxValue)
            .AddInt64Array("I123", [1, 2, 3])
            .AddInt64("IMin", Int64.MinValue)
            .Write(tp);
        b.Reset("IntPtr")
            .AddUIntPtr("UMax", UIntPtr.MaxValue, EventHeaderFieldFormat.HexInt)
            .AddUIntPtrArray("U123", [1, 2, 3])
            .AddUIntPtr("UMin", UIntPtr.MinValue)
            .AddIntPtr("IMax", IntPtr.MaxValue)
            .AddIntPtrArray("I123", [1, 2, 3])
            .AddIntPtr("IMin", IntPtr.MinValue)
            .Write(tp);
        b.Reset("Float")
            .AddFloat32("FMax", float.MaxValue, EventHeaderFieldFormat.HexInt)
            .AddFloat32Array("F123", [1, 2, 3])
            .AddFloat32("FMin", float.MinValue)
            .AddFloat64("DMax", double.MaxValue)
            .AddFloat64Array("D123", [1, 2, 3])
            .AddFloat64("DMin", double.MinValue)
            .Write(tp);
        b.Reset("Guid")
            .AddGuid("Bytes", new Guid(bytes))
            .AddGuidArray("Array", [new Guid(bytes), default])
            .AddGuid("String", new Guid("00010203-0405-0607-0809-0a0b0c0d0e0f"))
            .Write(tp);
        b.Reset("Value128")
            .AddValue128("Bytes", new EventHeaderValue128(bytes))
            .AddValue128Array("Array", [new EventHeaderValue128(bytes), default])
            .AddValue128("IPv6", new EventHeaderValue128(bytes), EventHeaderFieldFormat.IPv6)
            .Write(tp);
        b.Reset("ZString8")
            .AddZString8("Bytes[1]", bytes.Slice(1))
            .AddZString8Array("UArray", [new byte[] { 65, 66, 67, 0, 70 }, default])
            .AddZString8("Bytes[10]", bytes.Slice(10))
            .Write(tp);
        b.Reset("ZString16")
            .AddZString16("U123", [49, 50, 51])
            .AddZString16Array("UArray", [new UInt16[] { 65, 66, 67, 0, 70 }, default])
            .AddZString16("C123", (ReadOnlySpan<char>)['1', '2', '3'])
            .AddZString16Array("CArray", [new Char[] { 'A', 'B', 'C', '\0', 'F' }, default])
            .AddZString16Array("SArray", ["123", ""])
            .Write(tp);
        b.Reset("ZString32")
            .AddZString32("U123", [49, 50, 51])
            .AddZString32Array("UArray", [new UInt32[] { 65, 66, 67, 0, 70 }, default])
            .AddZString32("UABC", [65, 66, 67])
            .Write(tp);
        b.Reset("String8")
            .AddString8("Bytes[1]", bytes.Slice(1))
            .AddString8Array("UArray", [new byte[] { 65, 66, 67, 0, 70 }, default])
            .AddString8("Bytes[10]", bytes.Slice(10))
            .Write(tp);
        b.Reset("String16")
            .AddString16("U123", [49, 50, 51])
            .AddString16Array("UArray", [new UInt16[] { 65, 66, 67, 0, 70 }, default])
            .AddString16("C123", (ReadOnlySpan<char>)['1', '2', '3'])
            .AddString16Array("CArray", [new Char[] { 'A', 'B', 'C', '\0', 'F' }, default])
            .AddString16Array("SArray", ["123", ""])
            .Write(tp);
        b.Reset("String32")
            .AddString32("U123", [49, 50, 51])
            .AddString32Array("UArray", [new UInt32[] { 65, 66, 67, 0, 70 }, default])
            .AddString32("UABC", [65, 66, 67])
            .Write(tp);
    }
}
