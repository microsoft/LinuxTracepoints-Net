// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
// Adapted from linux/uapi/linux/perf_event.h.

namespace Microsoft.LinuxTracepoints.Decode
{
    using System;
    using System.Runtime.InteropServices;
    using BinaryPrimitives = System.Buffers.Binary.BinaryPrimitives;
    using CultureInfo = System.Globalization.CultureInfo;

    /// <summary>
    /// perf_type_id: uint32 value for PerfEventAttr.Type.
    /// </summary>
    public enum PerfEventAttrType : UInt32
    {
        /// <summary>
        /// PERF_TYPE_HARDWARE
        /// </summary>
        Hardware = 0,

        /// <summary>
        /// PERF_TYPE_SOFTWARE
        /// </summary>
        Software = 1,

        /// <summary>
        /// PERF_TYPE_TRACEPOINT
        /// </summary>
        Tracepoint = 2,

        /// <summary>
        /// PERF_TYPE_HW_CACHE
        /// </summary>
        HwCache = 3,

        /// <summary>
        /// PERF_TYPE_RAW
        /// </summary>
        Raw = 4,

        /// <summary>
        /// PERF_TYPE_BREAKPOINT
        /// </summary>
        Breakpoint = 5,

        /// <summary>
        /// PERF_TYPE_MAX (non-ABI)
        /// </summary>
        Max,
    }

    /// <summary>
    /// Extension methods for PerfEventAttrType.
    /// </summary>
    public static class PerfEventAttrTypeExtensions
    {
        /// <summary>
        /// Returns a string representation of the PerfEventAttrType value.
        /// If value is not known, returns null.
        /// </summary>
        public static string? AsStringIfKnown(this PerfEventAttrType self)
        {
            switch (self)
            {
                case PerfEventAttrType.Hardware: return "Hardware";
                case PerfEventAttrType.Software: return "Software";
                case PerfEventAttrType.Tracepoint: return "Tracepoint";
                case PerfEventAttrType.HwCache: return "HwCache";
                case PerfEventAttrType.Raw: return "Raw";
                case PerfEventAttrType.Breakpoint: return "Breakpoint";
                default: return null;
            }
        }

        /// <summary>
        /// Returns a string representation of the PerfEventAttrType value.
        /// If value is not known, returns the numeric value as a string.
        /// </summary>
        public static string AsString(this PerfEventAttrType self)
        {
            return AsStringIfKnown(self) ?? unchecked((UInt32)self).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Values for PerfEventAttr.Size.
    /// </summary>
    public enum PerfEventAttrSize : UInt32
    {
        /// <summary>
        /// Invalid value for size.
        /// </summary>
        Zero = 0,

        /// <summary>
        /// PERF_ATTR_SIZE_VER0 - first published struct
        /// </summary>
        Ver0 = 64,

        /// <summary>
        /// PERF_ATTR_SIZE_VER1 - add: Config2
        /// </summary>
        Ver1 = 72,

        /// <summary>
        /// PERF_ATTR_SIZE_VER2 - add: BranchSampleType
        /// </summary>
        Ver2 = 80,

        /// <summary>
        /// PERF_ATTR_SIZE_VER3 - add: SampleRegsUser, SampleStackUser
        /// </summary>
        Ver3 = 96,

        /// <summary>
        /// PERF_ATTR_SIZE_VER4 - add: SampleRegsIntr
        /// </summary>
        Ver4 = 104,

        /// <summary>
        /// PERF_ATTR_SIZE_VER5 - add: AuxWatermark
        /// </summary>
        Ver5 = 112,

        /// <summary>
        /// PERF_ATTR_SIZE_VER6 - add: AuxSampleSize
        /// </summary>
        Ver6 = 120,

        /// <summary>
        /// PERF_ATTR_SIZE_VER7 - add: SigData
        /// </summary>
        Ver7 = 128,
    }

    /// <summary>
    /// perf_event_sample_format: bits that can be set in PerfEventAttr.SampleType.
    /// </summary>
    [Flags]
    public enum PerfEventAttrSampleType : UInt64
    {
        /// <summary>
        /// PERF_SAMPLE_IP
        /// </summary>
        IP = 1U << 0,

        /// <summary>
        /// PERF_SAMPLE_TID
        /// </summary>
        Tid = 1U << 1,

        /// <summary>
        /// PERF_SAMPLE_TIME
        /// </summary>
        Time = 1U << 2,

        /// <summary>
        /// PERF_SAMPLE_ADDR
        /// </summary>
        Addr = 1U << 3,

        /// <summary>
        /// PERF_SAMPLE_READ
        /// </summary>
        Read = 1U << 4,

        /// <summary>
        /// PERF_SAMPLE_CALLCHAIN
        /// </summary>
        Callchain = 1U << 5,

        /// <summary>
        /// PERF_SAMPLE_ID
        /// </summary>
        Id = 1U << 6,

        /// <summary>
        /// PERF_SAMPLE_CPU
        /// </summary>
        Cpu = 1U << 7,

        /// <summary>
        /// PERF_SAMPLE_PERIOD
        /// </summary>
        Period = 1U << 8,

        /// <summary>
        /// PERF_SAMPLE_STREAM_ID
        /// </summary>
        StreamId = 1U << 9,

        /// <summary>
        /// PERF_SAMPLE_RAW
        /// </summary>
        Raw = 1U << 10,

        /// <summary>
        /// PERF_SAMPLE_BRANCH_STACK
        /// </summary>
        BranchStack = 1U << 11,

        /// <summary>
        /// PERF_SAMPLE_REGS_USER
        /// </summary>
        RegsUser = 1U << 12,

        /// <summary>
        /// PERF_SAMPLE_STACK_USER
        /// </summary>
        StackUser = 1U << 13,

        /// <summary>
        /// PERF_SAMPLE_WEIGHT
        /// </summary>
        Weight = 1U << 14,

        /// <summary>
        /// PERF_SAMPLE_DATA_SRC
        /// </summary>
        DataSrc = 1U << 15,

        /// <summary>
        /// PERF_SAMPLE_IDENTIFIER
        /// </summary>
        Identifier = 1U << 16,

        /// <summary>
        /// PERF_SAMPLE_TRANSACTION
        /// </summary>
        Transaction = 1U << 17,

        /// <summary>
        /// PERF_SAMPLE_REGS_INTR
        /// </summary>
        RegsIntr = 1U << 18,

        /// <summary>
        /// PERF_SAMPLE_PHYS_ADDR
        /// </summary>
        PhysAddr = 1U << 19,

        /// <summary>
        /// PERF_SAMPLE_AUX
        /// </summary>
        Aux = 1U << 20,

        /// <summary>
        /// PERF_SAMPLE_CGROUP
        /// </summary>
        Cgroup = 1U << 21,

        /// <summary>
        /// PERF_SAMPLE_DATA_PAGE_SIZE
        /// </summary>
        DataPageSize = 1U << 22,

        /// <summary>
        /// PERF_SAMPLE_CODE_PAGE_SIZE
        /// </summary>
        CodePageSize = 1U << 23,

        /// <summary>
        /// PERF_SAMPLE_WEIGHT_STRUCT
        /// </summary>
        WeightStruct = 1U << 24,

        /// <summary>
        /// PERF_SAMPLE_MAX (non-ABI)
        /// </summary>
        Max = 1U << 25,

        /// <summary>
        /// PERF_SAMPLE_WEIGHT_TYPE = PERF_SAMPLE_WEIGHT | PERF_SAMPLE_WEIGHT_STRUCT
        /// </summary>
        WeightType = Weight | WeightStruct,
    }

    /// <summary>
    /// perf_event_read_format: bits that can be set in PerfEventAttr.ReadFormat.
    /// </summary>
    public enum PerfEventAttrReadFormat : UInt64
    {
        /// <summary>
        /// PERF_FORMAT_TOTAL_TIME_ENABLED
        /// </summary>
        TotalTimeEnabled = 1U << 0,

        /// <summary>
        /// PERF_FORMAT_TOTAL_TIME_RUNNING
        /// </summary>
        TotalTimeRunning = 1U << 1,

        /// <summary>
        /// PERF_FORMAT_ID
        /// </summary>
        Id = 1U << 2,

        /// <summary>
        /// PERF_FORMAT_GROUP
        /// </summary>
        Group = 1U << 3,

        /// <summary>
        /// PERF_FORMAT_LOST
        /// </summary>
        Lost = 1U << 4,

        /// <summary>
        /// PERF_FORMAT_MAX (non-ABI)
        /// </summary>
        Max = 1U << 5,
    }

    /// <summary>
    /// Bits for PerfEventAttr.Options.
    /// </summary>
    [Flags]
    public enum PerfEventAttrOptions : UInt64
    {
        /// <summary>
        /// No flags set.
        /// </summary>
        None = 0,

        /// <summary>
        /// disabled: off by default
        ///</summary>
        Disabled = 1UL << 0,

        /// <summary>
        /// inherit: children inherit it
        ///</summary>
        Inherit = 1UL << 1,

        /// <summary>
        /// pinned: must always be on PMU
        ///</summary>
        Pinned = 1UL << 2,

        /// <summary>
        /// exclusive: only group on PMU
        ///</summary>
        Exclusive = 1UL << 3,

        /// <summary>
        /// exclude_user: don't count user
        ///</summary>
        ExcludeUser = 1UL << 4,

        /// <summary>
        /// exclude_kernel: don't count kernel
        ///</summary>
        ExcludeKernel = 1UL << 5,

        /// <summary>
        /// exclude_hv: don't count hypervisor
        ///</summary>
        ExcludeHypervisor = 1UL << 6,

        /// <summary>
        /// exclude_idle: don't count when idle
        ///</summary>
        ExcludeIdle = 1UL << 7,

        /// <summary>
        /// mmap: include mmap data
        ///</summary>
        Mmap = 1UL << 8,

        /// <summary>
        /// comm: include comm data
        ///</summary>
        Comm = 1UL << 9,

        /// <summary>
        /// freq: use freq, not period
        ///</summary>
        Freq = 1UL << 10,

        /// <summary>
        /// inherit_stat: per task counts
        ///</summary>
        InheritStat = 1UL << 11,

        /// <summary>
        /// enable_on_exec: next exec enables
        ///</summary>
        EnableOnExec = 1UL << 12,

        /// <summary>
        /// task: trace fork/exit
        ///</summary>
        Task = 1UL << 13,

        /// <summary>
        /// watermark: Use WakeupWatermark instead of WakeupEvents
        ///</summary>
        Watermark = 1UL << 14,

        /// <summary>
        /// precise_ip first bit:
        /// If unset, SAMPLE_IP can have arbitrary skid.
        /// If set, SAMPLE_IP must have constant skid.
        /// See also PERF_RECORD_MISC_EXACT_IP.
        ///</summary>
        PreciseIPSkidConstant = 1UL << 15,

        /// <summary>
        /// precise_ip second bit:
        /// SAMPLE_IP requested to have 0 skid.
        /// If precise_ip_skid_constant is also set, SAMPLE_IP must have 0 skid.
        /// See also PERF_RECORD_MISC_EXACT_IP.
        ///</summary>
        PreciseIPSkidZero = 1UL << 16,

        /// <summary>
        /// mmap_data: non-exec mmap data
        ///</summary>
        MmapData = 1UL << 17,

        /// <summary>
        /// sample_id_all: SampleType all events
        ///</summary>
        SampleIdAll = 1UL << 18,

        /// <summary>
        /// exclude_host: don't count in host
        ///</summary>
        ExcludeHost = 1UL << 19,

        /// <summary>
        /// exclude_guest: don't count in guest
        ///</summary>
        ExcludeGuest = 1UL << 20,

        /// <summary>
        /// exclude_callchain_kernel: exclude kernel callchains
        ///</summary>
        ExcludeCallchainKernel = 1UL << 21,

        /// <summary>
        /// exclude_callchain_user: exclude user callchains
        ///</summary>
        ExcludeCallchainUser = 1UL << 22,

        /// <summary>
        /// mmap2: include mmap with inode data
        ///</summary>
        Mmap2 = 1UL << 23,

        /// <summary>
        /// comm_exec: flag comm events that are due to an exec
        ///</summary>
        CommExec = 1UL << 24,

        /// <summary>
        /// use_clockid: use @clockid for time fields
        ///</summary>
        UseClockId = 1UL << 25,

        /// <summary>
        /// context_switch: context switch data
        ///</summary>
        ContextSwitch = 1UL << 26,

        /// <summary>
        /// write_backward: Write ring buffer from end to beginning
        ///</summary>
        WriteBackward = 1UL << 27,

        /// <summary>
        /// namespaces: include namespaces data
        ///</summary>
        Namespaces = 1UL << 28,

        /// <summary>
        /// ksymbol: include ksymbol events
        ///</summary>
        Ksymbol = 1UL << 29,

        /// <summary>
        /// bpf_event: include bpf events
        ///</summary>
        BpfEvent = 1UL << 30,

        /// <summary>
        /// aux_output: generate AUX records instead of events
        ///</summary>
        AuxOutput = 1UL << 31,

        /// <summary>
        /// cgroup: include cgroup events
        ///</summary>
        Cgroup = 1UL << 32,

        /// <summary>
        /// text_poke: include text poke events
        ///</summary>
        TextPoke = 1UL << 33,

        /// <summary>
        /// build_id: use build id in mmap2 events
        ///</summary>
        BuildId = 1UL << 34,

        /// <summary>
        /// inherit_thread: children only inherit if cloned with CLONE_THREAD
        ///</summary>
        InheritThread = 1UL << 35,

        /// <summary>
        /// remove_on_exec: event is removed from task on exec
        ///</summary>
        RemoveOnExec = 1UL << 36,

        /// <summary>
        /// sigtrap: send synchronous SIGTRAP on event
        ///</summary>
        Sigtrap = 1UL << 37,
    }

    /// <summary>
    /// perf_event_attr: Event's collection parameters.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    public struct PerfEventAttr
    {
        /// <summary>
        /// sizeof(PerfEventAttr)
        /// </summary>
        public const int SizeOfStruct = 128;

        /// <summary>
        /// offsetof(PerfEventAttr, Type)
        /// </summary>
        public const int OffsetOfType = 0;

        /// <summary>
        /// offsetof(PerfEventAttr, Size)
        /// </summary>
        public const int OffsetOfSize = 4;

        /// <summary>
        /// offsetof(PerfEventAttr, Config)
        /// </summary>
        public const int OffsetOfConfig = 8;

        /// <summary>
        /// type:
        /// Major type: hardware/software/tracepoint/etc.
        /// </summary>
        [FieldOffset(0)]
        public PerfEventAttrType Type;

        /// <summary>
        /// size:
        /// Size of the attr structure, for fwd/bwd compat.
        /// </summary>
        [FieldOffset(4)]
        public PerfEventAttrSize Size;

        /// <summary>
        /// config:
        /// Type-specific configuration information.
        /// </summary>
        [FieldOffset(8)]
        public UInt64 Config;

        /// <summary>
        /// sample_period:
        /// Note: union'ed with SampleFreq.
        /// </summary>
        [FieldOffset(16)]
        public UInt64 SamplePeriod;

        /// <summary>
        /// sample_freq:
        /// Note: union'ed with SamplePeriod.
        /// </summary>
        [FieldOffset(16)]
        public UInt64 SampleFreq;

        /// <summary>
        /// sample_type
        /// </summary>
        [FieldOffset(24)]
        public PerfEventAttrSampleType SampleType;

        /// <summary>
        /// read_format
        /// </summary>
        [FieldOffset(32)]
        public PerfEventAttrReadFormat ReadFormat;

        /// <summary>
        /// In C, this is a bit-field of various options:
        /// disabled, inherit, pinned, exclusive, exclude_user, exclude_kernel, exclude_hv,
        /// exclude_idle, mmap, comm, freq, inherit_stat, enable_on_exec, task, watermark,
        /// precise_ip (2 bits), mmap_data, sample_id_all, exclude_host, exclude_guest,
        /// exclude_callchain_kernel, exclude_callchain_user, mmap2, comm_exec, use_clockid,
        /// context_switch, write_backward, namespaces, ksymbol, bpf_event, aux_output,
        /// cgroup, text_poke, build_id, inherit_thread, remove_on_exec, sigtrap.
        /// </summary>
        [FieldOffset(40)]
        public PerfEventAttrOptions Options;

        /// <summary>
        /// wakeup_events:
        /// wakeup every n events.
        /// Note: union'ed with WakeupWatermark.
        /// </summary>
        [FieldOffset(48)]
        public UInt32 WakeupEvents;

        /// <summary>
        /// wakeup_watermark:
        /// bytes before wakeup.
        /// Note: union'ed with WakeupEvents.
        /// </summary>
        [FieldOffset(48)]
        public UInt32 WakeupWatermark;

        /// <summary>
        /// bp_type
        /// </summary>
        [FieldOffset(52)]
        public UInt32 BpType;

        /// <summary>
        /// bp_addr:
        /// Note: union'ed with BpAddr, KprobeFunc, UprobePath, Config1.
        /// </summary>
        [FieldOffset(56)]
        public UInt64 BpAddr;

        /// <summary>
        /// kprobe_func:
        /// for perf_kprobe.
        /// Note: union'ed with BpAddr, KprobeFunc, UprobePath, Config1.
        /// </summary>
        [FieldOffset(56)]
        public UInt64 KprobeFunc;

        /// <summary>
        /// uprobe_path:
        /// for perf_uprobe.
        /// Note: union'ed with BpAddr, KprobeFunc, UprobePath, Config1.
        /// </summary>
        [FieldOffset(56)]
        public UInt64 UprobePath;

        /// <summary>
        /// config1:
        /// extension of config.
        /// Note: union'ed with BpAddr, KprobeFunc, UprobePath, Config1.
        /// </summary>
        [FieldOffset(56)]
        public UInt64 Config1;

        /// <summary>
        /// bp_len:
        /// Note: union'ed with BpLen, KprobeAddr, ProbeOffset, Config2.
        /// </summary>
        [FieldOffset(64)]
        public UInt64 BpLen;

        /// <summary>
        /// kprobe_addr:
        /// when KprobeFunc == NULL.
        /// Note: union'ed with BpLen, KprobeAddr, ProbeOffset, Config2.
        /// </summary>
        [FieldOffset(64)]
        public UInt64 KprobeAddr;

        /// <summary>
        /// probe_offset:
        /// for perf_[k,u]probe.
        /// Note: union'ed with BpLen, KprobeAddr, ProbeOffset, Config2.
        /// </summary>
        [FieldOffset(64)]
        public UInt64 ProbeOffset;

        /// <summary>
        /// config2:
        /// extension of Config1.
        /// Note: union'ed with BpLen, KprobeAddr, ProbeOffset, Config2.
        /// </summary>
        [FieldOffset(64)]
        public UInt64 Config2;

        /// <summary>
        /// branch_sample_type:
        /// enum perf_branch_sample_type
        /// </summary>
        [FieldOffset(72)]
        public UInt64 BranchSampleType;

        /// <summary>
        /// sample_regs_user:
        /// Defines set of user regs to dump on samples.
        /// See asm/perf_regs.h for details.  
        /// </summary>
        [FieldOffset(80)]
        public UInt64 SampleRegsUser;

        /// <summary>
        /// sample_stack_user:
        /// Defines size of the user stack to dump on samples.  
        /// </summary>
        [FieldOffset(88)]
        public UInt32 SampleStackUser;

        /// <summary>
        /// clockid
        /// </summary>
        [FieldOffset(92)]
        public UInt32 ClockId;

        /// <summary>
        /// sample_regs_intr:
        /// Defines set of regs to dump for each sample state captured on:
        /// <list type="bullet"><item>
        /// precise = 0: PMU interrupt
        /// </item><item>
        /// precise > 0: sampled instruction
        /// </item></list>
        /// See asm/perf_regs.h for details.
        /// </summary>
        [FieldOffset(96)]
        public UInt64 SampleRegsIntr;

        /// <summary>
        /// aux_watermark:
        /// Wakeup watermark for AUX area 
        /// </summary>
        [FieldOffset(104)]
        public UInt32 AuxWatermark;

        /// <summary>
        /// sample_max_stack
        /// </summary>
        [FieldOffset(108)]
        public UInt16 SampleMaxStack;

        /// <summary>
        /// reserved2
        /// </summary>
        [FieldOffset(110)]
        public UInt16 Reserved2;

        /// <summary>
        /// aux_sample_size
        /// </summary>
        [FieldOffset(112)]
        public UInt32 AuxSampleSize;

        /// <summary>
        /// reserved3
        /// </summary>
        [FieldOffset(116)]
        public UInt32 Reserved3;

        /// <summary>
        /// sig_data:
        /// User provided data if sigtrap=1, passed back to user via
        /// siginfo_t::si_perf_data, e.g. to permit user to identify the event.
        /// Note, siginfo_t::si_perf_data is long-sized, and SigData will be
        /// truncated accordingly on 32 bit architectures.
        /// </summary>
        [FieldOffset(120)]
        public UInt64 SigData;

        /// <summary>
        /// Reverse the endian order of all fields in this struct.
        /// </summary>
        public void ByteSwap()
        {
            this.Type = (PerfEventAttrType)BinaryPrimitives.ReverseEndianness((UInt32)this.Type);
            this.Size = (PerfEventAttrSize)BinaryPrimitives.ReverseEndianness((UInt32)this.Size);
            this.Config = BinaryPrimitives.ReverseEndianness(this.Config);
            this.SamplePeriod = BinaryPrimitives.ReverseEndianness(this.SamplePeriod);
            this.SampleType = (PerfEventAttrSampleType)BinaryPrimitives.ReverseEndianness((UInt64)this.SampleType);
            this.ReadFormat = (PerfEventAttrReadFormat)BinaryPrimitives.ReverseEndianness((UInt64)this.ReadFormat);

            // Bitfield: Reverse bits within each byte, don't reorder bytes.
            var options = (UInt64)this.Options;
            options = (options & 0x5555555555555555UL) << 1 | (options & 0xAAAAAAAAAAAAAAAAUL) >> 1;
            options = (options & 0x3333333333333333UL) << 2 | (options & 0xCCCCCCCCCCCCCCCCUL) >> 2;
            options = (options & 0x0F0F0F0F0F0F0F0FUL) << 4 | (options & 0xF0F0F0F0F0F0F0F0UL) >> 4;
            this.Options = (PerfEventAttrOptions)options;

            this.WakeupEvents = BinaryPrimitives.ReverseEndianness(this.WakeupEvents);
            this.BpType = BinaryPrimitives.ReverseEndianness(this.BpType);
            this.BpAddr = BinaryPrimitives.ReverseEndianness(this.BpAddr);
            this.BpLen = BinaryPrimitives.ReverseEndianness(this.BpLen);
            this.BranchSampleType = BinaryPrimitives.ReverseEndianness(this.BranchSampleType);
            this.SampleRegsUser = BinaryPrimitives.ReverseEndianness(this.SampleRegsUser);
            this.SampleStackUser = BinaryPrimitives.ReverseEndianness(this.SampleStackUser);
            this.AuxWatermark = BinaryPrimitives.ReverseEndianness(this.AuxWatermark);
            this.SampleMaxStack = BinaryPrimitives.ReverseEndianness(this.SampleMaxStack);
            this.AuxSampleSize = BinaryPrimitives.ReverseEndianness(this.AuxSampleSize);
        }
    }

    /// <summary>
    /// perf_event_type: uint32 value for PerfEventHeader.Type.
    /// <br/>
    /// If perf_event_attr.sample_id_all is set then all event types will
    /// have the SampleType selected fields related to where/when
    /// (identity) an event took place (TID, TIME, ID, STREAM_ID, CPU,
    /// IDENTIFIER) described in PERF_RECORD_SAMPLE below, it will be stashed
    /// just after the perf_event_header and the fields already present for
    /// the existing fields, i.e. at the end of the payload. That way a newer
    /// perf.data file will be supported by older perf tools, with these new
    /// optional fields being ignored.
    /// <code><![CDATA[
    /// struct sample_id {
    ///     { u32            pid, tid; } && PERF_SAMPLE_TID
    ///     { u64            time;     } && PERF_SAMPLE_TIME
    ///     { u64            id;       } && PERF_SAMPLE_ID
    ///     { u64            stream_id;} && PERF_SAMPLE_STREAM_ID
    ///     { u32            cpu, res; } && PERF_SAMPLE_CPU
    ///    { u64            id;      } && PERF_SAMPLE_IDENTIFIER
    /// } && perf_event_attr::sample_id_all
    /// ]]></code>
    /// Note that PERF_SAMPLE_IDENTIFIER duplicates PERF_SAMPLE_ID.  The
    /// advantage of PERF_SAMPLE_IDENTIFIER is that its position is fixed
    /// relative to header.size.
    /// </summary>
    public enum PerfEventHeaderType : UInt32
    {
        /// <summary>
        /// Invalid event type.
        /// </summary>
        None,

        /// <summary>
        /// PERF_RECORD_MMAP:
        /// <br/>
        /// The MMAP events record the PROT_EXEC mappings so that we can
        /// correlate userspace IPs to code. They have the following structure:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        /// 
        ///    u32                pid, tid;
        ///    u64                addr;
        ///    u64                len;
        ///    u64                pgoff;
        ///    char                filename[];
        ///     struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Mmap = 1,

        /// <summary>
        /// PERF_RECORD_LOST:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u64                id;
        ///    u64                lost;
        ///     struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Lost = 2,

        /// <summary>
        /// PERF_RECORD_COMM:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        /// 
        ///    u32                pid, tid;
        ///    char                comm[];
        ///     struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Comm = 3,

        /// <summary>
        /// PERF_RECORD_EXIT:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u32                pid, ppid;
        ///    u32                tid, ptid;
        ///    u64                time;
        ///     struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Exit = 4,

        /// <summary>
        /// PERF_RECORD_THROTTLE:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u64                time;
        ///    u64                id;
        ///    u64                stream_id;
        ///     struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Throttle = 5,

        /// <summary>
        /// PERF_RECORD_UNTHROTTLE:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u64                time;
        ///    u64                id;
        ///    u64                stream_id;
        ///     struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Unthrottle = 6,

        /// <summary>
        /// PERF_RECORD_FORK:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u32                pid, ppid;
        ///    u32                tid, ptid;
        ///    u64                time;
        ///     struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Fork = 7,

        /// <summary>
        /// PERF_RECORD_READ:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u32                pid, tid;
        /// 
        ///    struct read_format        values;
        ///     struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Read = 8,

        /// <summary>
        /// PERF_RECORD_SAMPLE:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        /// 
        ///    #
        ///    # Note that PERF_SAMPLE_IDENTIFIER duplicates PERF_SAMPLE_ID.
        ///    # The advantage of PERF_SAMPLE_IDENTIFIER is that its position
        ///    # is fixed relative to header.
        ///    #
        /// 
        ///    { u64            id;      } && PERF_SAMPLE_IDENTIFIER
        ///    { u64            ip;      } && PERF_SAMPLE_IP
        ///    { u32            pid, tid; } && PERF_SAMPLE_TID
        ///    { u64            time;     } && PERF_SAMPLE_TIME
        ///    { u64            addr;     } && PERF_SAMPLE_ADDR
        ///    { u64            id;      } && PERF_SAMPLE_ID
        ///    { u64            stream_id;} && PERF_SAMPLE_STREAM_ID
        ///    { u32            cpu, res; } && PERF_SAMPLE_CPU
        ///    { u64            period;   } && PERF_SAMPLE_PERIOD
        /// 
        ///    { struct read_format    values;      } && PERF_SAMPLE_READ
        /// 
        ///    { u64            nr,
        ///      u64            ips[nr];  } && PERF_SAMPLE_CALLCHAIN
        /// 
        ///    #
        ///    # The RAW record below is opaque data wrt the ABI
        ///    #
        ///    # That is, the ABI doesn't make any promises wrt to
        ///    # the stability of its content, it may vary depending
        ///    # on event, hardware, kernel version and phase of
        ///    # the moon.
        ///    #
        ///    # In other words, PERF_SAMPLE_RAW contents are not an ABI.
        ///    #
        /// 
        ///    { u32            size;
        ///      char                  data[size];}&& PERF_SAMPLE_RAW
        /// 
        ///    { u64                   nr;
        ///      { u64    hw_idx; } && PERF_SAMPLE_BRANCH_HW_INDEX
        ///        { u64 from, to, flags } lbr[nr];
        ///      } && PERF_SAMPLE_BRANCH_STACK
        /// 
        ///     { u64            abi; # enum perf_sample_regs_abi
        ///       u64            regs[weight(mask)]; } && PERF_SAMPLE_REGS_USER
        /// 
        ///     { u64            size;
        ///       char            data[size];
        ///       u64            dyn_size; } && PERF_SAMPLE_STACK_USER
        /// 
        ///    { union perf_sample_weight
        ///     {
        ///        u64        full; && PERF_SAMPLE_WEIGHT
        ///    #if defined(__LITTLE_ENDIAN_BITFIELD)
        ///        struct {
        ///            u32    var1_dw;
        ///            u16    var2_w;
        ///            u16    var3_w;
        ///        } && PERF_SAMPLE_WEIGHT_STRUCT
        ///    #elif defined(__BIG_ENDIAN_BITFIELD)
        ///        struct {
        ///            u16    var3_w;
        ///            u16    var2_w;
        ///            u32    var1_dw;
        ///        } && PERF_SAMPLE_WEIGHT_STRUCT
        ///    #endif
        ///     }
        ///    }
        ///    { u64            data_src; } && PERF_SAMPLE_DATA_SRC
        ///    { u64            transaction; } && PERF_SAMPLE_TRANSACTION
        ///    { u64            abi; # enum perf_sample_regs_abi
        ///      u64            regs[weight(mask)]; } && PERF_SAMPLE_REGS_INTR
        ///    { u64            phys_addr;} && PERF_SAMPLE_PHYS_ADDR
        ///    { u64            size;
        ///      char            data[size]; } && PERF_SAMPLE_AUX
        ///    { u64            data_page_size;} && PERF_SAMPLE_DATA_PAGE_SIZE
        ///    { u64            code_page_size;} && PERF_SAMPLE_CODE_PAGE_SIZE
        /// };
        /// ]]></code>
        /// </summary>
        Sample = 9,

        /// <summary>
        /// PERF_RECORD_MMAP2:
        /// The MMAP2 records are an augmented version of MMAP, they add
        /// maj, min, ino numbers to be used to uniquely identify each mapping
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        /// 
        ///    u32                pid, tid;
        ///    u64                addr;
        ///    u64                len;
        ///    u64                pgoff;
        ///    union {
        ///        struct {
        ///            u32        maj;
        ///            u32        min;
        ///            u64        ino;
        ///            u64        ino_generation;
        ///        };
        ///        struct {
        ///            u8        build_id_size;
        ///            u8        __reserved_1;
        ///            u16        __reserved_2;
        ///            u8        build_id[20];
        ///        };
        ///    };
        ///    u32                prot, flags;
        ///    char                filename[];
        ///     struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Mmap2 = 10,

        /// <summary>
        /// PERF_RECORD_AUX:
        /// 
        /// Records that new data landed in the AUX buffer part.
        /// <code><![CDATA[
        /// struct {
        ///     struct perf_event_header    header;
        /// 
        ///     u64                aux_offset;
        ///     u64                aux_size;
        ///    u64                flags;
        ///     struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Aux = 11,

        /// <summary>
        /// PERF_RECORD_ITRACE_START:
        /// 
        /// Indicates that instruction trace has started
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u32                pid;
        ///    u32                tid;
        ///    struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        ItraceStart = 12,

        /// <summary>
        /// PERF_RECORD_LOST_SAMPLES:
        /// 
        /// Records the dropped/lost sample number.
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        /// 
        ///    u64                lost;
        ///    struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        LostSamples = 13,

        /// <summary>
        /// PERF_RECORD_SWITCH:
        /// 
        /// Records a context switch in or out (flagged by
        /// PERF_RECORD_MISC_SWITCH_OUT). See also
        /// PERF_RECORD_SWITCH_CPU_WIDE.
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Switch = 14,

        /// <summary>
        /// PERF_RECORD_SWITCH_CPU_WIDE:
        /// 
        /// CPU-wide version of PERF_RECORD_SWITCH with next_prev_pid and
        /// next_prev_tid that are the next (switching out) or previous
        /// (switching in) pid/tid.
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u32                next_prev_pid;
        ///    u32                next_prev_tid;
        ///    struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        SwitchCpuWide = 15,

        /// <summary>
        /// PERF_RECORD_NAMESPACES:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u32                pid;
        ///    u32                tid;
        ///    u64                nr_namespaces;
        ///    { u64                dev, inode; } [nr_namespaces];
        ///    struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Namespaces = 16,

        /// <summary>
        /// PERF_RECORD_KSYMBOL:
        /// 
        /// Record ksymbol register/unregister events:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u64                addr;
        ///    u32                len;
        ///    u16                ksym_type;
        ///    u16                flags;
        ///    char                name[];
        ///    struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Ksymbol = 17,

        /// <summary>
        /// PERF_RECORD_BPF_EVENT:
        /// 
        /// Record bpf events:
        /// <code><![CDATA[
        ///  enum perf_bpf_event_type {
        ///    PERF_BPF_EVENT_UNKNOWN        = 0,
        ///    PERF_BPF_EVENT_PROG_LOAD    = 1,
        ///    PERF_BPF_EVENT_PROG_UNLOAD    = 2,
        ///  };
        /// 
        /// struct {
        ///    struct perf_event_header    header;
        ///    u16                type;
        ///    u16                flags;
        ///    u32                id;
        ///    u8                tag[BPF_TAG_SIZE];
        ///    struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        BpfEvent = 18,

        /// <summary>
        /// PERF_RECORD_CGROUP:
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u64                id;
        ///    char                path[];
        ///    struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        Cgroup = 19,

        /// <summary>
        /// PERF_RECORD_TEXT_POKE:
        /// 
        /// Records changes to kernel text i.e. self-modified code. 'old_len' is
        /// the number of old bytes, 'new_len' is the number of new bytes. Either
        /// 'old_len' or 'new_len' may be zero to indicate, for example, the
        /// addition or removal of a trampoline. 'bytes' contains the old bytes
        /// followed immediately by the new bytes.
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u64                addr;
        ///    u16                old_len;
        ///    u16                new_len;
        ///    u8                bytes[];
        ///    struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        TextPoke = 20,

        /// <summary>
        /// PERF_RECORD_AUX_OUTPUT_HW_ID:
        /// 
        /// Data written to the AUX area by hardware due to aux_output, may need
        /// to be matched to the event by an architecture-specific hardware ID.
        /// This records the hardware ID, but requires sample_id to provide the
        /// event ID. e.g. Intel PT uses this record to disambiguate PEBS-via-PT
        /// records from multiple events.
        /// <code><![CDATA[
        /// struct {
        ///    struct perf_event_header    header;
        ///    u64                hw_id;
        ///    struct sample_id        sample_id;
        /// };
        /// ]]></code>
        /// </summary>
        AuxOutputHwId = 21,

        /// <summary>
        /// PERF_RECORD_MAX: non-ABI
        /// </summary>
        Max,

        /// <summary>
        /// PERF_RECORD_HEADER_ATTR:
        /// <code><![CDATA[
        /// struct attr_event {
        ///     struct perf_event_header header;
        ///     struct perf_event_attr attr;
        ///     UInt64 id[];
        /// };
        /// ]]></code>
        /// </summary>
        HeaderAttr = 64,

        /// <summary>
        /// PERF_RECORD_USER_TYPE_START: non-ABI
        /// </summary>
        UserTypeStart = HeaderAttr,

        /// <summary>
        /// PERF_RECORD_HEADER_EVENT_TYPE: deprecated
        /// <code><![CDATA[
        /// #define MAX_EVENT_NAME 64
        /// 
        /// struct perf_trace_event_type {
        ///     UInt64    event_id;
        ///     char    name[MAX_EVENT_NAME];
        /// };
        /// 
        /// struct event_type_event {
        ///     struct perf_event_header header;
        ///     struct perf_trace_event_type event_type;
        /// };
        /// ]]></code>
        /// </summary>
        HeaderEventType = 65,

        /// <summary>
        /// PERF_RECORD_HEADER_TRACING_DATA:
        /// <code><![CDATA[
        /// struct tracing_data_event {
        ///     struct perf_event_header header;
        ///     UInt32 size;
        /// };
        /// ]]></code>
        /// </summary>
        HeaderTracingData = 66,

        /// <summary>
        /// PERF_RECORD_HEADER_BUILD_ID:
        /// 
        /// Define a ELF build ID for a referenced executable.
        /// </summary>
        HeaderBuildId = 67,

        /// <summary>
        /// PERF_RECORD_FINISHED_ROUND:
        /// 
        /// No event reordering over this header. No payload.
        /// </summary>
        FinishedRound = 68,

        /// <summary>
        /// PERF_RECORD_ID_INDEX:
        /// 
        /// Map event ids to CPUs and TIDs.
        /// <code><![CDATA[
        /// struct id_index_entry {
        ///     UInt64 id;
        ///     UInt64 idx;
        ///     UInt64 cpu;
        ///     UInt64 tid;
        /// };
        /// 
        /// struct id_index_event {
        ///     struct perf_event_header header;
        ///     UInt64 nr;
        ///     struct id_index_entry entries[nr];
        /// };
        /// ]]></code>
        /// </summary>
        IdIndex = 69,

        /// <summary>
        /// PERF_RECORD_AUXTRACE_INFO:
        /// 
        /// Auxtrace type specific information. Describe me
        /// <code><![CDATA[
        /// struct auxtrace_info_event {
        ///     struct perf_event_header header;
        ///     UInt32 type;
        ///     UInt32 reserved__; // For alignment
        ///     UInt64 priv[];
        /// };
        /// ]]></code>
        /// </summary>
        AuxtraceInfo = 70,

        /// <summary>
        /// PERF_RECORD_AUXTRACE:
        /// 
        /// Defines auxtrace data. Followed by the actual data. The contents of
        /// the auxtrace data is dependent on the event and the CPU. For example
        /// for Intel Processor Trace it contains Processor Trace data generated
        /// by the CPU.
        /// <code><![CDATA[
        /// struct auxtrace_event {
        ///      struct perf_event_header header;
        ///      UInt64 size;
        ///      UInt64 offset;
        ///      UInt64 reference;
        ///      UInt32 idx;
        ///      UInt32 tid;
        ///      UInt32 cpu;
        ///      UInt32 reserved__; // For alignment
        /// };
        /// 
        /// struct aux_event {
        ///      struct perf_event_header header;
        ///      UInt64    aux_offset;
        ///      UInt64    aux_size;
        ///      UInt64    flags;
        /// };
        /// ]]></code>
        /// </summary>
        Auxtrace = 71,

        /// <summary>
        /// PERF_RECORD_AUXTRACE_ERROR:
        /// 
        /// Describes an error in hardware tracing
        /// <code><![CDATA[
        /// enum auxtrace_error_type {
        ///     PERF_AUXTRACE_ERROR_ITRACE  = 1,
        ///     PERF_AUXTRACE_ERROR_MAX
        /// };
        /// 
        /// #define MAX_AUXTRACE_ERROR_MSG 64
        /// 
        /// struct auxtrace_error_event {
        ///     struct perf_event_header header;
        ///     UInt32 type;
        ///     UInt32 code;
        ///     UInt32 cpu;
        ///     UInt32 pid;
        ///     UInt32 tid;
        ///     UInt32 reserved__; // For alignment
        ///     UInt64 ip;
        ///     char msg[MAX_AUXTRACE_ERROR_MSG];
        /// };
        /// ]]></code>
        /// </summary>
        AuxtraceError = 72,

        /// <summary>
        /// PERF_RECORD_THREAD_MAP
        /// </summary>
        ThreadMap = 73,

        /// <summary>
        /// PERF_RECORD_CPU_MAP
        /// </summary>
        CpuMap = 74,

        /// <summary>
        /// PERF_RECORD_STAT_CONFIG
        /// </summary>
        StatConfig = 75,

        /// <summary>
        /// PERF_RECORD_STAT
        /// </summary>
        Stat = 76,

        /// <summary>
        /// PERF_RECORD_STAT_ROUND
        /// </summary>
        StatRound = 77,

        /// <summary>
        /// PERF_RECORD_EVENT_UPDATE
        /// </summary>
        EventUpdate = 78,

        /// <summary>
        /// PERF_RECORD_TIME_CONV
        /// </summary>
        TimeConv = 79,

        /// <summary>
        /// PERF_RECORD_HEADER_FEATURE:
        /// 
        /// Describes a header feature. These are records used in pipe-mode that
        /// contain information that otherwise would be in perf.data file's header.
        /// </summary>
        HeaderFeature = 80,

        /// <summary>
        /// PERF_RECORD_COMPRESSED:
        /// <code><![CDATA[
        /// struct compressed_event {
        ///     struct perf_event_header    header;
        ///     char                data[];
        /// };
        /// ]]></code>
        /// The header is followed by compressed data frame that can be decompressed
        /// into array of perf trace records. The size of the entire compressed event
        /// record including the header is limited by the max value of header.size.
        /// </summary>
        Compressed = 81,

        /// <summary>
        /// PERF_RECORD_FINISHED_INIT:
        /// 
        /// Marks the end of records for the system, pre-existing threads in system wide
        /// sessions, etc. Those are the ones prefixed PERF_RECORD_USER_*.
        ///
        /// This is used, for instance, to 'perf inject' events after init and before
        /// regular events, those emitted by the kernel, to support combining guest and
        /// host records.
        /// </summary>
        FinishedInit = 82,
    }

    /// <summary>
    /// Extension methods for PerfEventHeaderType.
    /// </summary>
    public static class PerfEventHeaderTypeExtensions
    {
        /// <summary>
        /// Returns a string representation of the PerfEventHeaderType value.
        /// If value is not known, returns null.
        /// </summary>
        public static string? AsStringIfKnown(this PerfEventHeaderType self)
        {
            switch (self)
            {
                case PerfEventHeaderType.None: return "None";
                case PerfEventHeaderType.Mmap: return "Mmap";
                case PerfEventHeaderType.Lost: return "Lost";
                case PerfEventHeaderType.Comm: return "Comm";
                case PerfEventHeaderType.Exit: return "Exit";
                case PerfEventHeaderType.Throttle: return "Throttle";
                case PerfEventHeaderType.Unthrottle: return "Unthrottle";
                case PerfEventHeaderType.Fork: return "Fork";
                case PerfEventHeaderType.Read: return "Read";
                case PerfEventHeaderType.Sample: return "Sample";
                case PerfEventHeaderType.Mmap2: return "Mmap2";
                case PerfEventHeaderType.Aux: return "Aux";
                case PerfEventHeaderType.ItraceStart: return "ItraceStart";
                case PerfEventHeaderType.LostSamples: return "LostSamples";
                case PerfEventHeaderType.Switch: return "Switch";
                case PerfEventHeaderType.SwitchCpuWide: return "SwitchCpuWide";
                case PerfEventHeaderType.Namespaces: return "Namespaces";
                case PerfEventHeaderType.Ksymbol: return "Ksymbol";
                case PerfEventHeaderType.BpfEvent: return "BpfEvent";
                case PerfEventHeaderType.Cgroup: return "Cgroup";
                case PerfEventHeaderType.TextPoke: return "TextPoke";
                case PerfEventHeaderType.AuxOutputHwId: return "AuxOutputHwId";
                case PerfEventHeaderType.Max: return "Max";
                case PerfEventHeaderType.HeaderAttr: return "HeaderAttr";
                case PerfEventHeaderType.HeaderEventType: return "HeaderEventType";
                case PerfEventHeaderType.HeaderTracingData: return "HeaderTracingData";
                case PerfEventHeaderType.HeaderBuildId: return "HeaderBuildId";
                case PerfEventHeaderType.FinishedRound: return "FinishedRound";
                case PerfEventHeaderType.IdIndex: return "IdIndex";
                case PerfEventHeaderType.AuxtraceInfo: return "AuxtraceInfo";
                case PerfEventHeaderType.Auxtrace: return "Auxtrace";
                case PerfEventHeaderType.AuxtraceError: return "AuxtraceError";
                case PerfEventHeaderType.ThreadMap: return "ThreadMap";
                case PerfEventHeaderType.CpuMap: return "CpuMap";
                case PerfEventHeaderType.StatConfig: return "StatConfig";
                case PerfEventHeaderType.Stat: return "Stat";
                case PerfEventHeaderType.StatRound: return "StatRound";
                case PerfEventHeaderType.EventUpdate: return "EventUpdate";
                case PerfEventHeaderType.TimeConv: return "TimeConv";
                case PerfEventHeaderType.HeaderFeature: return "HeaderFeature";
                case PerfEventHeaderType.Compressed: return "Compressed";
                case PerfEventHeaderType.FinishedInit: return "FinishedInit";
                default: return null;
            }
        }

        /// <summary>
        /// Returns a string representation of the PerfEventHeaderType value.
        /// If value is not known, returns the numeric value as a string.
        /// </summary>
        public static string AsString(this PerfEventHeaderType self)
        {
            return AsStringIfKnown(self) ?? unchecked((UInt32)self).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// Values for PerfEventHeaderMisc.CpuMode.
    /// </summary>
    public enum PerfEventHeaderMiscCpuMode : Byte
    {
        /// <summary>
        /// PERF_RECORD_MISC_CPUMODE_UNKNOWN
        /// </summary>
        Unknown,

        /// <summary>
        /// PERF_RECORD_MISC_KERNEL
        /// </summary>
        Kernel,

        /// <summary>
        /// PERF_RECORD_MISC_USER
        /// </summary>
        User,

        /// <summary>
        /// PERF_RECORD_MISC_HYPERVISOR
        /// </summary>
        Hypervisor,

        /// <summary>
        /// PERF_RECORD_MISC_GUEST_KERNEL
        /// </summary>
        GuestKernel,

        /// <summary>
        /// PERF_RECORD_MISC_GUEST_USER
        /// </summary>
        GuestUser,
    }

    /// <summary>
    /// Value for PerfEventHeader.Misc.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PerfEventHeaderMisc
    {
        /// <summary>
        /// sizeof(PerfRecordMisc) == 2
        /// </summary>
        public const int SizeOfStruct = 2;

        /// <summary>
        /// Raw value of the misc field.
        /// </summary>
        public UInt16 Value;

        /// <summary>
        /// PERF_RECORD_MISC_CPUMODE
        /// </summary>
        public PerfEventHeaderMiscCpuMode CpuMode => (PerfEventHeaderMiscCpuMode)(this.Value & 0x7);

        /// <summary>
        /// PERF_RECORD_MISC_PROC_MAP_PARSE_TIMEOUT:
        /// Indicates that /proc/PID/maps parsing are truncated by time out.
        /// </summary>
        public bool ProcMapParseTimeout => (this.Value & 0x1000) != 0;

        /// <summary>
        /// PERF_RECORD_MISC_MMAP_DATA (PERF_RECORD_MMAP* events only)
        /// </summary>
        public bool MmapData => (this.Value & 0x2000) != 0;

        /// <summary>
        /// PERF_RECORD_MISC_COMM_EXEC (PERF_RECORD_COMM events only)
        /// </summary>
        public bool CommExec => (this.Value & 0x2000) != 0;

        /// <summary>
        /// PERF_RECORD_MISC_FORK_EXEC (PERF_RECORD_FORK events only)
        /// </summary>
        public bool ForkExec => (this.Value & 0x2000) != 0;

        /// <summary>
        /// PERF_RECORD_MISC_SWITCH_OUT (PERF_RECORD_SWITCH* events only)
        /// </summary>
        public bool SwitchOut => (this.Value & 0x2000) != 0;

        /// <summary>
        /// PERF_RECORD_MISC_EXACT_IP (PERF_RECORD_SAMPLE precise events only)
        /// </summary>
        public bool ExactIP => (this.Value & 0x4000) != 0;

        /// <summary>
        /// PERF_RECORD_MISC_SWITCH_OUT_PREEMPT (PERF_RECORD_SWITCH* events only)
        /// </summary>
        public bool SwitchOutPreempt => (this.Value & 0x4000) != 0;

        /// <summary>
        /// PERF_RECORD_MISC_MMAP_BUILD_ID (PERF_RECORD_MMAP2 events only)
        /// </summary>
        public bool MmapBuildId => (this.Value & 0x4000) != 0;

        /// <summary>
        /// PERF_RECORD_MISC_EXT_RESERVED
        /// </summary>
        public bool ExtReserved => (this.Value & 0x8000) != 0;
    }

    /// <summary>
    /// perf_event_header: Information at the start of each event.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PerfEventHeader
    {
        /// <summary>
        /// sizeof(PerfEventHeader) == 8
        /// </summary>
        public const int SizeOfStruct = 8;

        /// <summary>
        /// perf_event_header::type: Type of event.
        /// </summary>
        public PerfEventHeaderType Type;

        /// <summary>
        /// perf_event_header::misc:
        /// 
        /// The misc field contains additional information about the sample.
        /// </summary>
        public PerfEventHeaderMisc Misc;

        /// <summary>
        /// perf_event_header::size:
        /// 
        /// This indicates the size of the record.
        /// </summary>
        public UInt16 Size;

        /// <summary>
        /// Reverse the endian order of all fields in this struct.
        /// </summary>
        public void ByteSwap()
        {
            this.Type = (PerfEventHeaderType)BinaryPrimitives.ReverseEndianness((UInt32)this.Type);
            this.Misc.Value = BinaryPrimitives.ReverseEndianness(this.Misc.Value);
            this.Size = BinaryPrimitives.ReverseEndianness(this.Size);
        }
    }
}
