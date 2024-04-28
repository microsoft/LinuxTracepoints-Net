﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK.Extensibility.SourceParsing;
    using Microsoft.Performance.SDK.Processing;
    using System.Collections.Generic;

    [ProcessingSource(
        "{ad303744-aeaa-5ffa-2315-66206f995c54}", // tlgguid(PerfProcessingSource)
        "Linux perf.data",
        "Loads data from Linux perf.data files")]
    [FileDataSource(".data", "Linux perf.data files")]
    public sealed class PerfProcessingSource : ProcessingSource
    {
        public override ProcessingSourceInfo GetAboutInfo()
        {
            return new ProcessingSourceInfo
            {
                CopyrightNotice = "Copyright (c) Microsoft Corporation. All rights reserved.",
                LicenseInfo = new LicenseInfo { Name = "MIT" },
                ProjectInfo = new ProjectInfo { Uri = "https://github.com/microsoft/LinuxTracepoints-Net" },
            };
        }

        protected override bool IsDataSourceSupportedCore(IDataSource dataSource)
        {
            return dataSource.IsFile() && PerfDataFileReader.FileStartsWithMagic(dataSource.Uri.LocalPath);
        }

        protected override ICustomDataProcessor CreateProcessorCore(
            IEnumerable<IDataSource> dataSources,
            IProcessorEnvironment processorEnvironment,
            ProcessorOptions options)
        {
            var filenames = new List<string>();
            foreach (var dataSource in dataSources)
            {
                if (dataSource.IsFile())
                {
                    filenames.Add(dataSource.Uri.LocalPath);
                }
            }

            var parser = new PerfSourceParser(filenames.ToArray());
            return new DataProcessor(parser, options, this.ApplicationEnvironment, processorEnvironment);
        }

        private sealed class DataProcessor
            : CustomDataProcessorWithSourceParser<PerfEventData, PerfFileInfo, PerfEventHeaderType>
        {
            internal DataProcessor(
                ISourceParser<PerfEventData, PerfFileInfo, PerfEventHeaderType> sourceParser,
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
}
