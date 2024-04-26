// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System.Collections.ObjectModel;
    using Array = System.Array;

    /// <summary>
    /// Information about the event (shared by all events with the same Id).
    /// </summary>
    public class PerfEventDesc
    {
        private static PerfEventDesc? empty;
        private static ReadOnlyCollection<ulong>? emptyIds;

        private readonly PerfEventAttr attr;

        /// <summary>
        /// Initializes a new instance of the PerfEventDesc class with the
        /// specified information.
        /// </summary>
        /// <param name="attr">
        /// Event's perf_event_attr, or an attr with size = 0 if event's attr is not available.
        /// </param>
        /// <param name="name">Event's name, or "" if not available.</param>
        /// <param name="format">Event's format, or null if not available.</param>
        /// <param name="ids">The sample_ids that share this descriptor. May be null.</param>
        public PerfEventDesc(in PerfEventAttr attr, string name, PerfEventFormat? format, ReadOnlyCollection<ulong>? ids)
        {
            this.attr = attr;
            this.Name = name;
            this.Format = format;
            this.Ids = ids ?? EmptyIds;
        }

        /// <summary>
        /// Gets the empty event descriptor.
        /// </summary>
        public static PerfEventDesc Empty
        {
            get
            {
                var value = empty;
                if (value == null)
                {
                    value = new PerfEventDesc(default, string.Empty, null, null);
                    empty = value;
                }
                return value;
            }
        }

        /// <summary>
        /// Event's perf_event_attr, or an attr with size = 0 if not available.
        /// </summary>
        public ref readonly PerfEventAttr Attr => ref this.attr;

        /// <summary>
        /// Event's full name (including the system name), e.g. "sched:sched_switch",
        /// or "" if not available.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Event's format, or null if not available.
        /// </summary>
        public PerfEventFormat? Format { get; private set; }

        /// <summary>
        /// The sample_ids that share this descriptor, or empty list if none.
        /// </summary>
        public ReadOnlyCollection<ulong> Ids { get; }

        internal void SetFormat(PerfEventFormat format)
        {
            this.Format = format;
        }

        private static ReadOnlyCollection<ulong> EmptyIds
        {
            get
            {
                var value = emptyIds;
                if (value == null)
                {
                    value = new ReadOnlyCollection<ulong>(Array.Empty<ulong>());
                    emptyIds = value;
                }
                return value;
            }
        }

        /// <summary>
        /// Returns this.Name.
        /// </summary>
        public override string ToString()
        {
            return this.Name;
        }
    }
}
