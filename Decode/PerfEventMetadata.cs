﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Collections.Generic;
    using Debug = System.Diagnostics.Debug;

    /// <summary>
    /// Values for the Kind property of PerfEventMetadata.
    /// </summary>
    public enum PerfEventKind : byte
    {
        /// <summary>
        /// No special handling detected.
        /// </summary>
        Normal,

        /// <summary>
        /// First user field is named "eventheader_flags".
        /// </summary>
        EventHeader,
    }

    /// <summary>
    /// Event information parsed from a tracefs "format" file.
    /// </summary>
    public class PerfEventMetadata
    {
        private static PerfEventMetadata? empty;

        private readonly string systemName;
        private readonly string name;
        private readonly string printFmt;
        private readonly PerfFieldMetadata[] fields;
        private readonly uint id; // From common_type; not the same as the perf_event_attr::id or PerfSampleEventInfo::id.
        private readonly ushort commonFieldCount; // fields[common_field_count] is the first user field.
        private readonly ushort commonFieldsSize; // Offset of the end of the last common field
        private readonly PerfEventKind kind;

        private PerfEventMetadata(
            string systemName,
            string name,
            string printFmt,
            PerfFieldMetadata[] fields,
            uint id,
            ushort commonFieldCount,
            ushort commonFieldsSize,
            PerfEventKind kind)
        {
            this.systemName = systemName;
            this.name = name;
            this.printFmt = printFmt;
            this.fields = fields;
            this.id = id;
            this.commonFieldCount = commonFieldCount;
            this.commonFieldsSize = commonFieldsSize;
            this.kind = kind;
        }

        /// <summary>
        /// Gets the empty PerfEventMetadata object.
        /// </summary>
        public static PerfEventMetadata Empty
        {
            get
            {
                var value = empty;
                if (value == null)
                {
                    value = new PerfEventMetadata("", "", "", Array.Empty<PerfFieldMetadata>(), 0, 0, 0, 0);
                    empty = value;
                }

                return value;
            }
        }

        /// <summary>
        /// Returns the value of the systemName parameter, e.g. "user_events".
        /// </summary>
        public string SystemName => this.systemName;

        /// <summary>
        // Returns the value of the "name:" property, e.g. "my_event".
        /// </summary>
        public string Name => this.name;

        /// <summary>
        /// Returns the value of the "print fmt:" property.
        /// </summary>
        public string PrintFmt => this.printFmt;

        /// <summary>
        // Returns the fields from the "format:" property.
        /// </summary>
        public PerfFieldMetadata[] Fields => this.fields;

        /// <summary>
        /// Returns the value of the "ID:" property. Note that this value gets
        /// matched against the "common_type" field of an event, not the id field
        /// of perf_event_attr or PerfSampleEventInfo.
        /// </summary>
        public uint Id => this.id;

        /// <summary>
        /// Returns the number of "common_*" fields at the start of the event.
        /// User fields start at this index. At present, there are 4 common fields:
        /// common_type, common_flags, common_preempt_count, common_pid.
        /// </summary>
        public ushort CommonFieldCount => this.commonFieldCount;

        /// <summary>
        /// Returns the offset of the end of the last "common_*" field.
        /// This is the start of the first user field.
        /// </summary>
        public ushort CommonFieldsSize => this.commonFieldsSize;

        /// <summary>
        /// Returns the detected event decoding system - Normal or EventHeader.
        /// </summary>
        public PerfEventKind Kind => this.kind;

        /// <summary>
        /// Parses an event's "format" file and sets the fields of this object based
        /// on the results.
        /// 
        /// If "ID:" is a valid unsigned and and "name:" is not empty, returns a new
        /// PerfEventMetadata. Otherwise, returns null.
        /// </summary>
        /// <param name="longSize64">
        /// Indicates the size to use for "long" fields in this event.
        /// true if sizeof(long) == 8, false if sizeof(long) == 4.
        /// </param>
        /// <param name="systemName">
        /// The name of the system. For example, the systemName for "user_events:my_event" would
        /// be "user_events".
        /// </param>
        /// <param name="formatFileContents">
        /// The contents of the "format" file. This is typically obtained from tracefs, e.g. the
        /// formatFileContents for "user_events:my_event" will usually be the contents of
        /// "/sys/kernel/tracing/events/user_events/my_event/format".
        /// </param>
        public static PerfEventMetadata? Parse(
            bool longSize64,
            string systemName,
            ReadOnlySpan<char> formatFileContents)
        {
            var name = "";
            var printFmt = "";
            var fields = new List<PerfFieldMetadata>();
            var id = 0u;
            var commonFieldCount = (ushort)0;

            var foundId = false;
            var str = formatFileContents;

            // Search for lines like "NAME: VALUE..."
            var i = 0;
            while (i < str.Length)
            {
            ContinueNextLine:

                // Skip any newlines.
                while (ParseUtils.IsEolChar(str[i]))
                {
                    i += 1;
                    if (i >= str.Length) goto Done;
                }

                // Skip spaces.
                while (ParseUtils.IsSpaceOrTab(str[i]))
                {
                    Debug.WriteLine("Space before propname in event");
                    i += 1; // Unexpected.
                    if (i >= str.Length) goto Done;
                }

                // "NAME:"
                var iPropName = i;
                while (str[i] != ':')
                {
                    if (ParseUtils.IsEolChar(str[i]))
                    {
                        Debug.WriteLine("EOL before ':' in format");
                        goto ContinueNextLine; // Unexpected.
                    }

                    i += 1;

                    if (i >= str.Length)
                    {
                        Debug.WriteLine("EOF before ':' in format");
                        goto Done; // Unexpected.
                    }
                }

                var propName = str.Slice(iPropName, i - iPropName);
                i += 1; // Skip ':'

                // Skip spaces.
                while (i < str.Length && ParseUtils.IsSpaceOrTab(str[i]))
                {
                    i += 1;
                }

                var iPropValue = i;

                // "VALUE..."
                while (i < str.Length && !ParseUtils.IsEolChar(str[i]))
                {
                    char consumed;

                    consumed = str[i];
                    i += 1;

                    if (consumed == '"')
                    {
                        i = ParseUtils.ConsumeString(i, str, '"');
                    }
                }

                // Did we find something we can use?
                if (propName == "name")
                {
                    name = str.Slice(iPropValue, i - iPropValue).ToString();
                }
                else if (propName == "ID" && i < str.Length)
                {
                    foundId = ParseUtils.ParseUInt(str.Slice(iPropValue, i - iPropValue), out id);
                }
                else if (propName == "print fmt")
                {
                    printFmt = str.Slice(iPropValue, i - iPropValue).ToString();
                }
                else if (propName == "format")
                {
                    bool common = true;
                    fields.Clear();

                    // Search for lines like: " field:TYPE NAME; offset:N; size:N; signed:N;"
                    while (i < str.Length)
                    {
                        Debug.Assert(ParseUtils.IsEolChar(str[i]), "Loop should only repeat at EOL");

                        if (str.Length - i >= 2 && str[i] == '\r' && str[i + 1] == '\n')
                        {
                            i += 2; // Skip CRLF.
                        }
                        else
                        {
                            i += 1; // Skip CR or LF.
                        }

                        var iLine = i;
                        while (i < str.Length && !ParseUtils.IsEolChar(str[i]))
                        {
                            i += 1;
                        }

                        if (iLine == i)
                        {
                            // Blank line.
                            if (common)
                            {
                                // First blank line means we're done with common fields.
                                common = false;
                                continue;
                            }
                            else
                            {
                                // Second blank line means we're done with format.
                                break;
                            }
                        }

                        var field = PerfFieldMetadata.Parse(longSize64, str.Slice(iLine, i - iLine));                        
                        if (field == null)
                        {
                            Debug.WriteLine("Field parse failure");
                        }
                        else
                        {
                            fields.Add(field);
                            if (common)
                            {
                                commonFieldCount += 1;
                            }
                        }
                    }
                }
            }

        Done:

            PerfEventMetadata? result;
            if (name.Length == 0 || !foundId)
            {
                result = null;
            }
            else
            {
                ushort commonFieldsSize;
                if (commonFieldCount == 0)
                {
                    commonFieldsSize = 0;
                }
                else
                {
                    var lastCommonField = fields[commonFieldCount - 1];
                    commonFieldsSize = (ushort)(lastCommonField.Offset + lastCommonField.Size);
                }

                var kind =
                    fields.Count > commonFieldCount && fields[commonFieldCount].Name == "eventheader_flags"
                    ? PerfEventKind.EventHeader
                    : PerfEventKind.Normal;

                result = new PerfEventMetadata(
                    systemName,
                    name,
                    printFmt,
                    fields.Count == 0 ? Array.Empty<PerfFieldMetadata>() : fields.ToArray(),
                    id,
                    commonFieldCount,
                    commonFieldsSize,
                    kind);
            }

            return result;
        }
    }
}
