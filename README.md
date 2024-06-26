# .NET libraries for Linux Tracepoints and user_events

This repository contains .NET libraries for parsing `perf.data` files and decoding
[Linux Tracepoint](https://www.kernel.org/doc/html/latest/trace/tracepoints.html)
events, including events that use the [EventHeader](Types/README.md#eventheader) encoding.

## Overview

- [Provider](Provider) - library for generating Linux user_events tracepoint events.

- [Decode](Decode) - library for parsing `perf.data` files and decoding tracepoint
  events.

- [DecodeSample](DecodeSample) - tool that prints the events in a `perf.data`
  file.  Demonstrates basic usage of the `Decode` library.

- [DecodePerfToJson](DecodePerfToJson) - tool that converts `perf.data` files
  into JSON. Demonstrates advanced use of the `Decode` library for custom
  formatting and minimizing allocations.

- [Types](Types) - library with types used by the `Decode` library. This includes the
  definitions for the `EventHeader` encoding.

See also [PerfDataExtension](https://github.com/microsoft/Microsoft-Performance-Tools-Linux-Android/tree/develop/PerfDataExtension)
for a Windows Performance Toolkit plugin to decode `perf.data` files using this library.

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit [https://cla.opensource.microsoft.com](https://cla.opensource.microsoft.com).

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

## Trademarks

This project may contain trademarks or logos for projects, products, or services. Authorized use of Microsoft
trademarks or logos is subject to and must follow
[Microsoft's Trademark & Brand Guidelines](https://www.microsoft.com/legal/intellectualproperty/trademarks/usage/general).
Use of Microsoft trademarks or logos in modified versions of this project must not cause confusion or imply Microsoft sponsorship.
Any use of third-party trademarks or logos are subject to those third-party's policies.
