// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

namespace Microsoft.Performance.Toolkit.Plugins.PerfDataExtension
{
    using Microsoft.LinuxTracepoints.Decode;
    using Microsoft.Performance.SDK.Extensibility.SourceParsing;
    using Microsoft.Performance.SDK.Processing;
    using Microsoft.Performance.SDK.Processing.DataSourceGrouping;
    using System;
    using System.Collections.Generic;

    [ProcessingSource(
        "{ad303744-aeaa-5ffa-2315-66206f995c54}", // tlgguid(PerfProcessingSource)
        "Linux perf.data",
        "Loads data from Linux perf.data files")]
    [FileDataSource(".data", "Linux perf.data files")]
    public sealed class PerfDataProcessingSource
        : ProcessingSource
        , IDataSourceGrouper
    {
        public override ProcessingSourceInfo GetAboutInfo()
        {
            return new ProcessingSourceInfo
            {
                CopyrightNotice = "Copyright (c) Microsoft Corporation. All rights reserved.",
                LicenseInfo = new LicenseInfo { Name = "MIT" },
                Owners = Array.Empty<ContactInfo>(),
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

            var parser = new PerfDataSourceParser(filenames.ToArray());
            return new DataProcessor(parser, options, this.ApplicationEnvironment, processorEnvironment);
        }

        protected override ICustomDataProcessor CreateProcessorCore(
            IDataSourceGroup dataSourceGroup,
            IProcessorEnvironment processorEnvironment,
            ProcessorOptions options)
        {
            return CreateProcessorCore(dataSourceGroup.DataSources, processorEnvironment, options);
        }

        public IReadOnlyCollection<IDataSourceGroup> CreateValidGroups(IEnumerable<IDataSource> dataSources, ProcessorOptions options)
        {
            // PerfDataSourceParser can handle multi-source groups, but there might be complexities,
            // e.g. cookers get confused by that. For now, force each group to contain only one source.

            var groups = new List<DataSourceGroup>();
            var mode = new DefaultProcessingMode();
            foreach (var dataSource in dataSources)
            {
                groups.Add(new DataSourceGroup(new[] { dataSource }, mode));
            }

            return groups;
        }

        private sealed class DataProcessor
            : CustomDataProcessorWithSourceParser<PerfDataEvent, PerfDataFileInfo, PerfEventHeaderType>
        {
            internal DataProcessor(
                ISourceParser<PerfDataEvent, PerfDataFileInfo, PerfEventHeaderType> sourceParser,
                ProcessorOptions options,
                IApplicationEnvironment applicationEnvironment,
                IProcessorEnvironment processorEnvironment)
                : base(sourceParser, options, applicationEnvironment, processorEnvironment)
            {
                return;
            }

            protected override void BuildTableCore(TableDescriptor tableDescriptor, ITableBuilder tableBuilder)
            {
                if (this.SourceParser is PerfDataSourceParser perfSourceParser)
                {
                    if (tableDescriptor.Guid == PerfDataFilesTable.TableDescriptor.Guid)
                    {
                        PerfDataFilesTable.BuildTable(tableBuilder, perfSourceParser.FileInfos);
                    }
                }
            }
        }
    }
}
