# CouchLink

Play couch co-op games across the PCs on your LAN. One PC runs the game and
hosts; the other PCs join, see and hear the host's screen, and **each joining
PC gets its own virtual controller** on the host, driven by its keyboard and
mouse. The game sees separate controllers, so every PC is a separate player.

Built for internet cafes and LAN rooms: everything runs on your local
network, with **no accounts, no cloud, and no internet** required.

> **Status: early development.** Joining PCs see the host's screen with low latency (GPU capture,
> hardware H.264, FEC over UDP, GPU decode) and each gets its own virtual DualShock 4. Audio, the
> lobby and the join/approval flow are next. See the [roadmap](ROADMAP.md) and
> [the design spec](docs/superpowers/specs/2026-10-05-couchlink-design.md).

## Target setup

- Up to **10 players**: the host player on the game's own keyboard controls,
  plus up to 9 clients, each as a virtual DualShock 4.
- First target games: **NBA 2K22** and **NBA 2K14**.
  (2K14 only accepts one virtual DualShock 4; this is a limit of the game.)
- Wired gigabit LAN, Windows 10 on every PC.

## Requirements

- Windows 10 (x64).
- [ViGEmBus driver](https://github.com/nefarius/ViGEmBus/releases) on the
  host PC (creates the virtual controllers).
- No .NET install needed: releases are self-contained.

## Download

Get the latest `CouchLink-vX.Y.Z-win-x64.zip` from
[Releases](https://github.com/enriquezchristopher/CouchLink/releases),
unzip it anywhere, and run `CouchLink.App.exe`.

`PadTest\CouchLink.PadTest.exe check 9` checks, without any game, that this
PC can create 9 separate virtual controllers and that every button, stick and
trigger works on each.

## Reporting a crash

If CouchLink crashes it saves a crash report and shows you where it is.
Reports are stored in:

```
%LOCALAPPDATA%\CouchLink\CrashReports\couchlink-crash-YYYYMMDD-HHMMSS.txt
```

(paste `%LOCALAPPDATA%\CouchLink\CrashReports` into the Explorer address bar
to open the folder). Please
[open a new issue](https://github.com/enriquezchristopher/CouchLink/issues/new/choose)
and attach the file. Reports contain no PC names, user names, or IP addresses.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
./eng/get-ffmpeg.ps1       # once: FFmpeg 9 for host video, into third_party/ffmpeg
dotnet build
dotnet test
./eng/package.ps1          # builds artifacts/CouchLink-v<version>-win-x64.zip
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Please follow the
[Code of Conduct](CODE_OF_CONDUCT.md). Security issues: see
[SECURITY.md](SECURITY.md).

## License

CouchLink is licensed under the [GNU General Public License v3.0](LICENSE).
Third-party components are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
