# Third-Party Notices

CouchLink is licensed under the GNU General Public License v3.0. It includes
or depends on the following third-party components, each under its own
license.

## Included in releases

| Component | Used by | License | Source |
|---|---|---|---|
| .NET runtime and WPF (self-contained) | CouchLink.App, PadTest, VideoTest | MIT | https://github.com/dotnet/runtime, https://github.com/dotnet/wpf |
| Nefarius.ViGEm.Client | CouchLink.App, PadTest | MIT | https://github.com/nefarius/ViGEm.NET |
| HidSharp | PadTest | Apache-2.0 | https://www.zer7.com/software/hidsharp |
| FFmpeg 9.0.2 (libavcodec, libavutil, libswscale, libswresample; BtbN GPL build, includes x264) | CouchLink.App, VideoTest (host video) | GPL-3.0-or-later; license text in `ffmpeg/LICENSE.txt` | https://ffmpeg.org, build: https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-09-30-13-08 |
| FFmpeg.AutoGen | CouchLink.App, VideoTest | LGPL-3.0 | https://github.com/Ruslan-B/FFmpeg.AutoGen |
| Vortice.Windows (Direct3D11, DXGI, Direct2D1) | CouchLink.App, VideoTest | MIT | https://github.com/amerkoleci/Vortice.Windows |

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

Releases include FFmpeg build `n9.0.2-17-g2a571b6068` (BtbN `autobuild-2026-09-30-13-08`,
`ffmpeg-n9.0.2-17-g2a571b6068-win64-gpl-shared-9.0.zip`). Its source is FFmpeg commit
`2a571b6068` on the `release/9.0` branch of https://git.ffmpeg.org/ffmpeg.git (also at
https://github.com/FFmpeg/FFmpeg/commit/2a571b6068). The exact build scripts are at
https://github.com/BtbN/FFmpeg-Builds.
