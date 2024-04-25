namespace Microsoft.LinuxTracepoints.DecodeWpa
{
    using Microsoft.Performance.SDK.Extensibility.SourceParsing;
    using Microsoft.Performance.SDK.Processing;

    public sealed class PerfDataProcessor : CustomDataProcessorWithSourceParser<PerfEventInfo, PerfFileInfo, uint>
    {
        internal PerfDataProcessor(
            ISourceParser<PerfEventInfo, PerfFileInfo, uint> sourceParser,
            ProcessorOptions options,
            IApplicationEnvironment applicationEnvironment,
            IProcessorEnvironment processorEnvironment)
            : base(sourceParser, options, applicationEnvironment, processorEnvironment)
        {
            return;
        }
    }
}
