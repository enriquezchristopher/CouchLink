# CouchLink Café-Ready Install (v1.5 milestone: #27, #28, #29)

Status: Draft, for review
Date: 2026-10-08
Author: Christopher Enriquez (@enriquezchristopher) (with Claude Code)

Expands section 8 ("Install & Packaging") of the
[main design](2026-10-05-couchlink-design.md).

## 1. Goal

A café owner installs CouchLink on each PC with one setup file, by hand or
silently from a script, and every PC is then ready to host or join: the app
is in Program Files, the virtual controller driver is there, and Windows
Firewall lets the LAN traffic in. Upgrading is running the new setup over
the old one. Every release meets the GPL for the FFmpeg it ships, with the
complete source attached to the release itself. A plain-English guide takes
the owner from download to first session.

### Agreed requirements
From the issues:
- One installer puts CouchLink on a PC, installs ViGEmBus if missing, and
  adds firewall rules for UDP 47800, TCP 47801, UDP 47802 and UDP 47803
  (#27).
- A silent install option for deploying to many PCs (#27).
- A clean uninstall (#27).
- The FFmpeg build includes `h264_amf`, `h264_nvenc` and `libx264` (#28).
- THIRD-PARTY-NOTICES lists FFmpeg and x264 with license and source links
  (#28).
- The release package meets GPL source-availability requirements (#28).
- A step-by-step guide: install on every PC, network requirements (wired
  gigabit), game settings (borderless windowed), first session (#29).
- A troubleshooting section: firewall, ViGEmBus, crash reports (#29).

Decided while brainstorming:
- Cafés install one PC at a time, by hand or with a remote tool. Shared
  master images (diskless boot, cloned disks) are not a target.
- Upgrades run the new setup over the old one. No auto-update: CouchLink
  never needs the internet.
- The installer is built with Inno Setup.
- CouchLink ships its own minimal FFmpeg build, and every release attaches
  that build's complete source.
- The guide is English only, matching the app.

## 2. The installer

`CouchLink-Setup-v<version>.exe`, built from `installer/CouchLink.iss`.

### 2.1 What it installs
- The self-contained app in `C:\Program Files\CouchLink`, with PadTest and
  VideoTest in `PadTest\` and `VideoTest\` and FFmpeg in `ffmpeg\`, laid
  out exactly like the zip. `LICENSE`, `README.md`,
  `THIRD-PARTY-NOTICES.md` and `SOURCE.txt` go in the top folder.
- A Start menu shortcut "CouchLink", and an optional desktop shortcut
  (a checkbox, on by default; silent installs create it).
- x64 Windows 10 or later only. On anything else the installer stops
  with "CouchLink needs 64-bit Windows 10 or later."
- Per-machine, so it asks for administrator rights. The app itself still
  runs as a normal user.
- Pages: welcome, the GPL license, install folder, desktop shortcut,
  progress, finish. The finish page has two checkboxes, "Start CouchLink"
  and "Open the setup guide" (the guide's GitHub URL), both on.

### 2.2 ViGEmBus
- The official installer `ViGEmBus_1.22.0_x64_x86_arm64.exe` is bundled,
  fetched at build time by `eng/get-vigembus.ps1` and checked against a
  pinned SHA-256.
- During install, setup checks whether the `ViGEmBus` service exists. If
  it does, nothing happens. If it doesn't, setup runs the bundled
  installer with `/exenoui /qn /norestart` and waits.
- If that fails, CouchLink is still installed. Interactive installs show
  "CouchLink is installed, but the ViGEmBus driver didn't install (code
  N). Virtual controllers won't work on this PC until it is installed.
  See the setup guide." Silent installs exit with code 10 (section 2.5).
  This PC can still join; it can't host with controllers.
- The driver needs no restart in practice. If the ViGEmBus installer
  returns 3010 (restart needed), setup reports it like any installer does
  and the guide says to restart before hosting.
- Uninstalling CouchLink leaves ViGEmBus installed: other software
  (DS4Windows, Steam Input and others) uses it too.

### 2.3 Firewall rules
- Four inbound allow rules, one per port, each limited to
  `{app}\CouchLink.App.exe`, all profiles (Domain, Private, Public),
  remote address `LocalSubnet`:
  - `CouchLink (UDP 47800, discovery)`
  - `CouchLink (TCP 47801, sessions)`
  - `CouchLink (UDP 47802, video and audio)`
  - `CouchLink (UDP 47803, input)`
  The descriptions match the ports table in section 3 of the main design.
  Every PC gets all four, because any PC can host or join.
- All profiles, because Windows often marks a café LAN "Public", which
  would otherwise block discovery. `LocalSubnet` keeps the ports shut to
  anything outside the LAN.
- Setup adds them with `netsh advfirewall firewall add rule`, after
  deleting any existing rule with the same name, so an upgrade or a repair
  never doubles them.
- If adding a rule fails, setup finishes and reports it (interactive:
  a message naming the rule; silent: exit code 11).

### 2.4 Upgrades
- A fixed `AppId` makes a newer setup install over the older one, into the
  same folder, keeping the desktop-shortcut choice.
- Before copying files, setup checks for the running app through the
  `Local\CouchLink.SingleInstance` mutex (Plan 8). If it is running,
  interactive setup asks to close it ("CouchLink is running. Close it and
  continue?"); silent setup closes it. Closing means Restart Manager
  (`CloseApplications=yes`), which asks the window to close, then ends the
  process.
- Installing the same or an older version over a newer one is refused with
  "A newer CouchLink (vX) is already installed." Silent exit code 12.

### 2.5 Silent mode
- `CouchLink-Setup-v<version>.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART`
  installs or upgrades. `/LOG="path"` writes a setup log. `/NOICONS` and
  `/DIR=` work as in any Inno Setup installer.
- Exit codes: `0` success; Inno Setup's own codes for its own failures
  (1 to 8); `10` ViGEmBus didn't install; `11` a firewall rule wasn't
  added; `12` a newer version is installed. The guide lists them.
- Silent uninstall: `"C:\Program Files\CouchLink\unins000.exe" /VERYSILENT
  /SUPPRESSMSGBOXES`.

### 2.6 Uninstall
- In Apps & features as "CouchLink", with the version and publisher.
- Closes a running CouchLink the same way as an upgrade.
- Removes the program folder, the shortcuts and the four firewall rules.
- Keeps `%LOCALAPPDATA%\CouchLink` (logs and crash reports) and
  ViGEmBus. Logs and crash reports belong to the user and may be needed
  for a bug report.

### 2.7 Building it
- `eng/get-inno-setup.ps1` downloads a pinned Inno Setup 6 installer,
  checks its SHA-256 and installs it silently into
  `third_party/innosetup` (GitHub's Windows runners no longer include it).
- `eng/get-vigembus.ps1` downloads the pinned ViGEmBus installer into
  `third_party/vigembus`.
- `eng/package.ps1` keeps building the zip and then compiles
  `installer/CouchLink.iss` against the same staged folder, so the zip and
  the setup hold identical files. The version comes from
  `eng/version.props` as now.

## 3. Our FFmpeg build

### 3.1 Contents
- FFmpeg 9.0.2, from the official release tarball, pinned by SHA-256. The
  DLL major versions stay the same (`avcodec-63.dll`, `avutil-61.dll`,
  `swscale-10.dll`, `swresample-7.dll`), so `FfmpegLibrary.RequiredDlls`
  and the app's code don't change.
- x264, pinned to one commit on its `stable` branch.
- `nv-codec-headers` and AMF's `amf/public/include`, each pinned to a tag.
  Both are MIT and header-only. The NVIDIA and AMD encoders themselves are
  loaded from the graphics driver at run time, as now.
- Configured `--enable-gpl --enable-libx264 --enable-shared
  --disable-static --disable-programs --disable-doc --disable-network
  --disable-everything`, then enabling only what CouchLink uses: the
  `h264` decoder and parser, the `h264_d3d11va` and `h264_d3d11va2`
  hwaccels, `d3d11va`, the `libx264`, `h264_nvenc` and `h264_amf`
  encoders, `swscale` and `swresample`. The plan confirms this list
  against every FFmpeg call in `CouchLink.Video` before freezing it.

### 3.2 Where it is built
- `eng/ffmpeg/build.sh` does the whole build from the pinned archives. It
  runs on Ubuntu and cross-compiles for Windows x64 with mingw-w64.
- A separate workflow, `.github/workflows/ffmpeg.yml`, run by hand when a
  pinned version changes, runs `build.sh` and publishes two assets to a
  fixed release tag `deps-ffmpeg-9.0.2-1` (the last number counts our own
  rebuilds):
  - `couchlink-ffmpeg-9.0.2-1-win64.zip`: `bin\` with the four DLLs, and
    `LICENSE.txt` (FFmpeg's GPL text plus x264's).
  - `couchlink-ffmpeg-9.0.2-1-source.zip`: the four pinned source archives
    as downloaded, `build.sh`, and `CONFIGURE.txt` with the exact configure
    line and toolchain versions. This is the GPL corresponding source.
- The deps release is a prerelease and is never touched by release-please.
- `eng/get-ffmpeg.ps1` downloads both assets from that tag, pinned by
  SHA-256, instead of BtbN's build. `ffmpeg.exe`/`ffprobe.exe` are no
  longer available for development checks; nothing in the build or tests
  uses them, and the plan confirms that.

### 3.3 In every CouchLink release
- The release workflow attaches `couchlink-ffmpeg-9.0.2-1-source.zip`
  next to the setup and the zip, so the source sits on the same page as
  the binaries for as long as the release exists.
- `THIRD-PARTY-NOTICES.md` lists FFmpeg 9.0.2, x264 (with its commit),
  nv-codec-headers and the AMF headers, each with its license, and says
  the complete source is attached to every release. BtbN's entry is
  removed.
- `SOURCE.txt` in the install folder and the zip: "CouchLink is free
  software under the GPL v3. Its source, and the source of the FFmpeg
  build it ships, are attached to every release at
  https://github.com/enriquezchristopher/CouchLink/releases."
- The main design's section 8 drops "revisit before any redistribution":
  this plan is that revisit.

## 4. The setup guide

`docs/cafe-setup-guide.md`, English, linked from the README and from the
installer's finish page. It quotes the app's exact button labels and
messages instead of using screenshots, so it stays accurate when layouts
shift.

1. Before you start: Windows 10 64-bit on every PC; a wired gigabit LAN
   with every PC on the same switch or subnet; the game installed on the
   PCs that will host.
2. Install on every PC: download the setup, run it; or the silent command
   from a shared folder or remote tool, with a copy-paste example, the log
   option and the exit codes. The SmartScreen "Windows protected your PC"
   warning and how to get past it ("More info", "Run anyway"), because the
   setup isn't code-signed.
3. Game settings: borderless windowed so the host's screen can be
   captured; choosing the controller in NBA 2K22; NBA 2K14 takes only one
   virtual controller.
4. First session: Host on one PC, Join on another, the host clicks Allow,
   play; F1 shows the controls; Ctrl+Alt+Q leaves; Ctrl+Alt+C changes keys.
5. Upgrading: run the new setup over the old one, on every PC.
6. Troubleshooting:
   - The host isn't in the Join list: check the firewall rules exist, both
     PCs on the same subnet; use "Join by address...".
   - No controller in the game: is ViGEmBus installed (Device Manager,
     "Nefarius Virtual Gamepad Emulation Bus"); install it from the
     ViGEmBus releases page.
   - Black screen, stutter or lag: F2 stats and what "lost" and "repaired"
     mean; wired, not Wi-Fi.
   - CouchLink crashed: where the reports are and how to file an issue.

## 5. Releases and docs

- Each release has three files: the setup, the portable zip and the FFmpeg
  source zip. The README's Download section points to the setup first and
  describes the zip as "portable, for testing: it doesn't install the
  driver or the firewall rules".
- The README status line, `ROADMAP.md` (v1.3 and v1.4 done, v1.5 this
  plan) and the main design's section 8 are updated.
- New Plan 9 rows in `docs/gate-results.md` (section 6).

## 6. Testing

Automated:
- Package tests (Pester, run by `package.ps1 -Verify` in CI): the setup
  exists, and the staged folder has the app, `ffmpeg\` with the four DLLs
  and `LICENSE.txt`, `LICENSE`, `THIRD-PARTY-NOTICES.md` and `SOURCE.txt`.
- A Video test that our FFmpeg build reports the `h264_nvenc`, `h264_amf`
  and `libx264` encoders and the `h264` decoder. CI has no GPU to run the
  hardware encoders, but a configure mistake fails here.
- All existing tests run against our FFmpeg build in CI, which exercises
  decoding, swscale and the x264 fallback.
- An install round trip in the release workflow's Windows job, before the
  release files are uploaded: silent install of the built setup, exit code
  0, `CouchLink.App.exe` in Program Files, the four firewall rules present
  with the right ports and scope; then silent uninstall, the rules and the
  folder gone. The runner has no ViGEmBus, so this also runs the driver
  install. If the runner can't install drivers, the job accepts exit code
  10 and logs it, and the driver install stays a manual check.

Manual (owner's checklist, `docs/gate-results.md`):
- A clean PC without ViGEmBus: interactive install, then host with a
  working pad.
- Upgrade from the previous release while CouchLink is running.
- Uninstall: the rules gone, the program folder gone, ViGEmBus and the
  logs still there.
- Hosting with our FFmpeg on an NVIDIA PC, an AMD PC and a PC with neither
  (x264 fallback): picture on the client, F2 shows the encoder.
- A network Windows marks "Public": discovery and joining still work.
- A silent install from a script on a second PC.

## 7. Out of scope

- Code signing. SmartScreen warns on first run; the guide covers it. A
  certificate costs money and is a later decision.
- Shared master images and diskless boot systems.
- Auto-update.
- Saving the controls between runs: they reset on purpose (Plan 8 spec,
  spec 6.7).
- Aligning release numbers with milestone names.
