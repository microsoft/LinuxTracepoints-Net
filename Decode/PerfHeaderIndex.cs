// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.Decode
{
    /// <summary>
    /// From: perf.data-file-format.txt, perf/util/header.h.
    /// </summary>
    public enum PerfHeaderIndex : byte
    {
        /// <summary>
        /// PERF_HEADER_RESERVED
        /// always cleared
        /// </summary>
        Reserved = 0,

        /// <summary>
        /// PERF_HEADER_TRACING_DATA, PERF_HEADER_FIRST_FEATURE
        /// </summary>
        TracingData = 1,

        /// <summary>
        /// PERF_HEADER_BUILD_ID
        /// </summary>
        BuildId,

        /// <summary>
        /// PERF_HEADER_HOSTNAME
        /// </summary>
        Hostname,

        /// <summary>
        /// PERF_HEADER_OSRELEASE
        /// </summary>
        OSRelease,

        /// <summary>
        /// PERF_HEADER_VERSION
        /// </summary>
        Version,

        /// <summary>
        /// PERF_HEADER_ARCH
        /// </summary>
        Arch,

        /// <summary>
        /// PERF_HEADER_NRCPUS
        /// </summary>
        NrCpus,

        /// <summary>
        /// PERF_HEADER_CPUDESC
        /// </summary>
        CpuDesc,

        /// <summary>
        /// PERF_HEADER_CPUID
        /// </summary>
        CpuId,

        /// <summary>
        /// PERF_HEADER_TOTAL_MEM
        /// </summary>
        TotalMem,

        /// <summary>
        /// PERF_HEADER_CMDLINE
        /// </summary>
        Cmdline,

        /// <summary>
        /// PERF_HEADER_EVENT_DESC
        /// </summary>
        EventDesc,

        /// <summary>
        /// PERF_HEADER_CPU_TOPOLOGY
        /// </summary>
        CpuTopology,

        /// <summary>
        /// PERF_HEADER_NUMA_TOPOLOGY
        /// </summary>
        NumaTopology,

        /// <summary>
        /// PERF_HEADER_BRANCH_STACK
        /// </summary>
        BranchStack,

        /// <summary>
        /// PERF_HEADER_PMU_MAPPINGS
        /// </summary>
        PmuMappings,

        /// <summary>
        /// PERF_HEADER_GROUP_DESC
        /// </summary>
        GroupDesc,

        /// <summary>
        /// PERF_HEADER_AUXTRACE
        /// </summary>
        AuxTrace,

        /// <summary>
        /// PERF_HEADER_STAT
        /// </summary>
        Stat,

        /// <summary>
        /// PERF_HEADER_CACHE
        /// </summary>
        Cache,

        /// <summary>
        /// PERF_HEADER_SAMPLE_TIME
        /// </summary>
        SampleTime,

        /// <summary>
        /// PERF_HEADER_MEM_TOPOLOGY
        /// </summary>
        MemTopology,

        /// <summary>
        /// PERF_HEADER_CLOCKID
        /// </summary>
        ClockId,

        /// <summary>
        /// PERF_HEADER_DIR_FORMAT
        /// </summary>
        DirFormat,

        /// <summary>
        /// PERF_HEADER_BPF_PROG_INFO
        /// </summary>
        BpfProgInfo,

        /// <summary>
        /// PERF_HEADER_BPF_BTF
        /// </summary>
        BpfBtf,

        /// <summary>
        /// PERF_HEADER_COMPRESSED
        /// </summary>
        Compressed,

        /// <summary>
        /// PERF_HEADER_CPU_PMU_CAPS
        /// </summary>
        CpuPmuCaps,

        /// <summary>
        /// PERF_HEADER_CLOCK_DATA
        /// </summary>
        ClockData,

        /// <summary>
        /// PERF_HEADER_HYBRID_TOPOLOGY
        /// </summary>
        HybridTopology,

        /// <summary>
        /// PERF_HEADER_PMU_CAPS
        /// </summary>
        PmuCaps,

        /// <summary>
        /// PERF_HEADER_LAST_FEATURE
        /// </summary>
        LastFeature,
    }
}
