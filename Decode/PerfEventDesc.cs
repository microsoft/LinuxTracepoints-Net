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
        private readonly string name;
        private PerfEventMetadata? metadata;
        private readonly ReadOnlyCollection<ulong> ids;

        /// <summary>
        /// Initializes a new instance of the PerfEventDesc class with the
        /// specified information.
        /// </summary>
        /// <param name="attr">
        /// Event's perf_event_attr, or an attr with size = 0 if event's attr is not available.
        /// </param>
        /// <param name="name">Event's name, or "" if not available.</param>
        /// <param name="metadata">Event's metadata, or null if not available.</param>
        /// <param name="ids">The sample_ids that share this descriptor. May be null.</param>
        public PerfEventDesc(in PerfEventAttr attr, string name, PerfEventMetadata? metadata, ReadOnlyCollection<ulong>? ids)
        {
            this.attr = attr;
            this.name = name;
            this.metadata = metadata;
            this.ids = ids ?? EmptyIds;
        }

        public static PerfEventDesc Empty
        {
            get
            {
                var value = empty;
                if (value == null)
                {
                    value = new PerfEventDesc(default, "", null, EmptyIds);
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
        /// Event's name, or "" if not available.
        /// </summary>
        public string Name => this.name;

        /// <summary>
        /// Event's metadata, or null if not available.
        /// </summary>
        public PerfEventMetadata? Metadata => this.metadata;

        /// <summary>
        /// The sample_ids that share this descriptor, or empty list if none.
        /// </summary>
        public ReadOnlyCollection<ulong> Ids => this.ids;

        internal void SetMetadata(PerfEventMetadata metadata)
        {
            this.metadata = metadata;
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
    }
}
