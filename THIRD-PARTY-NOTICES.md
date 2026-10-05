# Third-Party Notices

CouchLink is licensed under the GNU General Public License v3.0. It includes
or depends on the following third-party components, each under its own
license.

## Included in releases

| Component | Used by | License | Source |
|---|---|---|---|
| .NET runtime and WPF (self-contained) | CouchLink.App, PadTest | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/wpf |
| Nefarius.ViGEm.Client | CouchLink.App, PadTest | MIT | https://github.com/nefarius/ViGEm.NET |
| HidSharp | PadTest | Apache-2.0 | https://www.zer7.com/software/hidsharp |

## Required separately (not included)

| Component | Purpose | License | Source |
|---|---|---|---|
| ViGEmBus driver | Creates the virtual controllers on the host | BSD-3-Clause | https://github.com/nefarius/ViGEmBus |

## Development only (not included in releases)

| Component | License |
|---|---|
| xUnit | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
| coverlet.collector | MIT |
| Microsoft.Extensions.TimeProvider.Testing | MIT |

When screen streaming is added, FFmpeg (built with x264, GPL) will be listed
here and shipped under the GPL.
