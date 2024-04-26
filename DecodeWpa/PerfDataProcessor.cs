// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK.Extensibility.SourceParsing;
    using Microsoft.Performance.SDK.Processing;

    internal sealed class PerfDataProcessor
        : CustomDataProcessorWithSourceParser<PerfEventInfo, PerfFileInfo, PerfEventHeaderType>
    {
        internal PerfDataProcessor(
            ISourceParser<PerfEventInfo, PerfFileInfo, PerfEventHeaderType> sourceParser,
            ProcessorOptions options,
            IApplicationEnvironment applicationEnvironment,
            IProcessorEnvironment processorEnvironment)
            : base(sourceParser, options, applicationEnvironment, processorEnvironment)
        {
            return;
        }

        protected override void BuildTableCore(TableDescriptor tableDescriptor, ITableBuilder tableBuilder)
        {
            if (this.SourceParser is PerfSourceParser perfSourceParser)
            {
                if (tableDescriptor.Guid == PerfFileMetadataTable.TableDescriptor.Guid)
                {
                    PerfFileMetadataTable.BuildTable(tableBuilder, perfSourceParser.FileInfos);
                }
            }
        }
    }
}
