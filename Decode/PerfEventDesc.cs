// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using Array = System.Array;

    /// <summary>
    /// Information about the event (shared by all events with the same Id).
    /// </summary>
    public class PerfEventDesc
    {
        private static PerfEventDesc? empty;

        private readonly PerfEventAttr attr;

        /// <summary>
        /// Initializes a new instance of the PerfEventDesc class with the
        /// specified information.
        /// </summary>
        /// <param name="attr">
        /// Event's perf_event_attr, or an attr with size = 0 if event's attr is not available.
        /// </param>
        /// <param name="name">Event's name. Must not be null (may be "" if name not available).</param>
        /// <param name="format">Event's format. Must not be null (may be empty).</param>
        /// <param name="ids">The sample_ids that share this descriptor. Must not be null.</param>
        public PerfEventDesc(in PerfEventAttr attr, string name, PerfEventFormat format, ReadOnlyCollection<ulong> ids)
        {
            Debug.Assert(name != null);
            Debug.Assert(format != null);
            Debug.Assert(ids != null);

            this.attr = attr;
            this.Name = name;
            this.Format = format;
            this.Ids = ids;
        }

        /// <summary>
        /// Gets the empty event descriptor.
        /// </summary>
        public static PerfEventDesc Empty => empty ?? Utility.InterlockedInitSingleton(
            ref empty,
            new PerfEventDesc(default, "", PerfEventFormat.Empty, new ReadOnlyCollection<ulong>(Array.Empty<ulong>())));

        /// <summary>
        /// Event's perf_event_attr, or an attr with size = 0 if not available.
        /// </summary>
        public ref readonly PerfEventAttr Attr => ref this.attr;

        /// <summary>
        /// Event's full name (including the system name), e.g. "sched:sched_switch",
        /// or "" if PERF_HEADER_EVENT_DESC was not available.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Event's format, or empty if not available.
        /// </summary>
        public PerfEventFormat Format { get; private set; }

        /// <summary>
        /// The sample_ids that share this descriptor, or empty list if none.
        /// </summary>
        public ReadOnlyCollection<ulong> Ids { get; }

        internal void SetFormat(PerfEventFormat format)
        {
            this.Format = format;
        }

        /// <summary>
        /// Returns the full name of the event e.g. "sched:sched_switch", or "" if not
        /// available.
        /// <br/>
        /// Unlike the Name property, this function will fall back to creating a new
        /// string from Format (Format.SystemName + ':' + Format.Name) if the name from
        /// PERF_HEADER_EVENT_DESC is empty and Format is non-empty. It may still return
        /// "" in cases where both PERF_HEADER_EVENT_DESC and Format are missing.
        /// </summary>
        public string GetName()
        {
            var name = this.Name;
            return name.Length > 0 || this.Format.IsEmpty
                ? name
                : this.Format.SystemName + ':' + this.Format.Name;
        }

        /// <summary>
        /// Returns the full name of the event e.g. "sched:sched_switch".
        /// </summary>
        public override string ToString()
        {
            return this.GetName();
        }
    }
}
