# Café-Ready Install Implementation Plan (Plan 9)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One setup file installs CouchLink, the ViGEmBus driver and the firewall rules on a café PC (silently if wanted), every release ships CouchLink's own minimal FFmpeg with its complete GPL source attached, and a guide takes a café owner from download to first session.

**Architecture:** A Linux CI job cross-compiles a minimal FFmpeg from pinned sources and publishes the DLLs and the source zip to a fixed prerelease tag; `get-ffmpeg.ps1` downloads from there. `package.ps1` stages the app once, zips it and compiles an Inno Setup script against the same staged folder; the setup's Pascal code installs ViGEmBus when missing and adds four `netsh` firewall rules, and the uninstaller removes them. CI and the release workflow run a silent install/repair/uninstall round trip on the Windows runner.

**Tech Stack:** Bash + mingw-w64 (FFmpeg build), GitHub Actions, PowerShell 7, Inno Setup 6.7.3, C#/.NET 10 + xUnit (FFmpeg checks), Markdown.

**Spec:** `docs/superpowers/specs/2026-10-08-couchlink-cafe-install-design.md`

## Global Constraints

- Ports and rule names, exactly: `CouchLink (UDP 47800, discovery)`, `CouchLink (TCP 47801, sessions)`, `CouchLink (UDP 47802, video and audio)`, `CouchLink (UDP 47803, input)`; inbound allow, program `{app}\CouchLink.App.exe`, all profiles, remote address `LocalSubnet`.
- Install folder `C:\Program Files\CouchLink` (`{autopf}\CouchLink`), per-machine, admin; x64 Windows 10 or later; message otherwise: "CouchLink needs 64-bit Windows 10 or later."
- Setup file name `CouchLink-Setup-v<version>.exe`; version from `eng/version.props`.
- Silent: `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`. Exit codes: 0 success; Inno's own 1-8; 10 ViGEmBus didn't install; 11 a firewall rule wasn't added; 12 a newer version is installed.
- ViGEmBus: bundled `ViGEmBus_1.22.0_x64_x86_arm64.exe`, run with `/exenoui /qn /norestart` only when the `ViGEmBus` service is missing; never uninstalled by CouchLink.
- Uninstall keeps `%LOCALAPPDATA%\CouchLink` and ViGEmBus.
- FFmpeg: 9.0.2, DLLs `avcodec-63.dll`, `avutil-61.dll`, `swscale-10.dll`, `swresample-7.dll`; GPL; built with only the `h264` decoder and parser, `h264_d3d11va`/`h264_d3d11va2` hwaccels, `d3d11va`, and the `libx264`, `h264_nvenc`, `h264_amf` encoders.
- FFmpeg deps release tag `deps-ffmpeg-9.0.2-1`, assets `couchlink-ffmpeg-9.0.2-1-win64.zip` and `couchlink-ffmpeg-9.0.2-1-source.zip`; prerelease; release-please never touches it.
- Every CouchLink release attaches the setup, the portable zip and the FFmpeg source zip.
- The guide is `docs/cafe-setup-guide.md`, English, quoting the app's exact labels.
- Every download is pinned by SHA-256 (or by git commit for git sources).
- Commits: Conventional Commits, signed (automatic). No Claude co-author lines.

## Review Focus

1. The FFmpeg DLLs import a mingw runtime DLL (`libwinpthread-1.dll`, `libgcc_s_seh-1.dll`) that a clean café PC doesn't have: video fails only on real PCs. Pinned by `build.sh`'s import check (Task 1, Step 3).
2. Running the setup a second time (repair or upgrade) doubles the firewall rules, or leaves rules pointing at an old path. Pinned by `test-installer.ps1` installing twice and requiring exactly four rules with the current path (Task 4, Step 1).
3. A dependency silently turns off an encoder or the D3D11 decoder at configure time (for example a missing header), so the build "succeeds" without `h264_amf`. Pinned by `build.sh`'s `config_components.h` check (Task 1, Step 3) and the hw-config test (Task 2, Step 1).
4. The finish page starts CouchLink elevated (as the installing admin), so its keyboard hook and window run at a different integrity level from the game. Pinned by `runasoriginaluser` on both postinstall entries and a `verify-package.ps1` check of the script (Task 3, Step 1).
5. Uninstalling while CouchLink is running leaves `CouchLink.App.exe` behind (file in use). Pinned by the uninstaller stopping the app first; `test-installer.ps1` checks the folder is gone (Task 4, Step 1). The running-app case itself is a manual row.

---

## File map

| File | Task | Responsibility |
|---|---|---|
| `eng/ffmpeg/versions.env` | 1 | Pinned FFmpeg, x264, nv-codec-headers, AMF versions and the build id |
| `eng/ffmpeg/build.sh` | 1 | Fetch pinned sources, cross-compile, check, zip DLLs and source |
| `.github/workflows/ffmpeg.yml` | 1 | Build on changes; publish on `deps-ffmpeg-*` tags |
| `eng/get-ffmpeg.ps1` | 2 | Download our build and its source zip, pinned by SHA-256 |
| `tests/CouchLink.Video.Tests/FfmpegLibraryTests.cs` | 2 | Our build is loaded; decoder, parser and D3D11VA are in it |
| `THIRD-PARTY-NOTICES.md` | 2 | FFmpeg, x264, nv-codec-headers, AMF headers; source attached |
| `eng/get-inno-setup.ps1` | 3 | Pinned Inno Setup into `third_party/innosetup` |
| `eng/get-vigembus.ps1` | 3 | Pinned ViGEmBus installer into `third_party/vigembus` |
| `installer/CouchLink.iss` | 3, 4 | The setup |
| `installer/SOURCE.txt` | 3 | Where the source is |
| `eng/package.ps1` | 3 | Stage, zip, compile the setup, copy the FFmpeg source zip, verify |
| `eng/verify-package.ps1` | 3 | Checks the package contents |
| `eng/test-installer.ps1` | 4 | Silent install, repair, uninstall round trip with checks |
| `.github/workflows/ci.yml`, `release.yml` | 4 | Fetch tools, package, round trip, upload |
| `docs/cafe-setup-guide.md` | 5 | The guide |
| `README.md`, `ROADMAP.md`, main design §8, `docs/gate-results.md` | 5 | Docs |

---

### Task 1: Minimal FFmpeg build script and workflow

**Files:**
- Create: `eng/ffmpeg/versions.env`
- Create: `eng/ffmpeg/build.sh`
- Create: `.github/workflows/ffmpeg.yml`

**Interfaces:**
- Produces: `bash eng/ffmpeg/build.sh <work-dir>` writes `<work-dir>/out/couchlink-ffmpeg-$BUILD_ID-win64.zip` (`bin/avcodec-63.dll`, `bin/avutil-61.dll`, `bin/swscale-10.dll`, `bin/swresample-7.dll`, `LICENSE.txt`) and `<work-dir>/out/couchlink-ffmpeg-$BUILD_ID-source.zip`. `versions.env` defines `BUILD_ID=9.0.2-1`. Workflow artifact name `couchlink-ffmpeg`.

This machine has no Linux (no WSL, no Docker), so the build runs only in CI. Iterate by pushing the branch; each push touching `eng/ffmpeg/**` runs the workflow.

- [ ] **Step 1: Write `eng/ffmpeg/versions.env`**

```bash
# Pinned sources for CouchLink's FFmpeg build (eng/ffmpeg/build.sh).
# Bump BUILD_ID's last number on any change, then push the tag deps-ffmpeg-$BUILD_ID.
BUILD_ID=9.0.2-1
FFMPEG_VERSION=9.0.2
FFMPEG_SHA256=8c3850283eb25fa026482078a04051e0be17347b09ef81a0849bec15a96e002e
X264_URL=https://code.videolan.org/videolan/x264.git
X264_COMMIT=b35605ace3ddf7c1a5d67a2eb553f034aef41d55
NVCODEC_URL=https://github.com/FFmpeg/nv-codec-headers.git
NVCODEC_TAG=n12.1.14.1
NVCODEC_COMMIT=e1ff958747e9e08247cea31b6e629d61ece824c0
AMF_URL=https://github.com/GPUOpen-LibrariesAndSDKs/AMF.git
AMF_TAG=v1.5.3
AMF_COMMIT=8c648005e07d4309033282bfd9947df2c7e76104
```

`n12.1.14.1` (not the newest 13.x) on purpose: NVENC headers set the minimum NVIDIA driver, and 12.1 runs on drivers from 531.61 on, which older café PCs are more likely to have than 13.x's 570+.

- [ ] **Step 2: Write `eng/ffmpeg/build.sh`**

```bash
#!/usr/bin/env bash
# Builds CouchLink's minimal FFmpeg for Windows x64 from the pinned sources in versions.env, and
# zips the DLLs and their complete source (the GPL "corresponding source").
# Needs Ubuntu 24.04 with: mingw-w64 nasm pkg-config make git curl xz-utils zip
# Usage: bash eng/ffmpeg/build.sh <work-dir>
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=versions.env
source "$here/versions.env"
work="$(realpath -m "${1:?usage: build.sh <work-dir>}")"
src="$work/src" build="$work/build" prefix="$work/prefix" dist="$work/dist" out="$work/out"
host=x86_64-w64-mingw32
jobs="$(nproc)"

rm -rf "$work"
mkdir -p "$src" "$build" "$prefix" "$dist/bin" "$out"

# 1. Pinned sources, kept as downloaded for the source zip.
curl -fsSL -o "$src/ffmpeg-$FFMPEG_VERSION.tar.xz" "https://ffmpeg.org/releases/ffmpeg-$FFMPEG_VERSION.tar.xz"
echo "$FFMPEG_SHA256  $src/ffmpeg-$FFMPEG_VERSION.tar.xz" | sha256sum -c -

# git_snapshot <name> <url> <commit> [paths...]: a .tar.gz of exactly that commit (optionally only some paths).
git_snapshot() {
    local name=$1 url=$2 commit=$3 repo="$work/git-$1"
    shift 3
    git init -q "$repo"
    git -C "$repo" fetch -q --depth 1 --filter=blob:none "$url" "$commit"
    if [ "$(git -C "$repo" rev-parse FETCH_HEAD)" != "$commit" ]; then
        echo "$name: fetched $(git -C "$repo" rev-parse FETCH_HEAD), expected $commit" >&2
        exit 1
    fi
    git -C "$repo" archive --format=tar.gz --prefix="$name/" -o "$src/$name.tar.gz" FETCH_HEAD "$@"
}
git_snapshot "x264-$X264_COMMIT" "$X264_URL" "$X264_COMMIT"
git_snapshot "nv-codec-headers-$NVCODEC_TAG" "$NVCODEC_URL" "$NVCODEC_COMMIT"
git_snapshot "amf-headers-$AMF_TAG" "$AMF_URL" "$AMF_COMMIT" amf/public/include LICENSE.txt

for archive in "$src"/*; do tar -xf "$archive" -C "$build"; done

# 2. NVIDIA and AMD encoder headers (MIT, header-only; the encoders load from the driver).
make -C "$build/nv-codec-headers-$NVCODEC_TAG" PREFIX="$prefix" install
mkdir -p "$prefix/include/AMF"
cp -r "$build/amf-headers-$AMF_TAG/amf/public/include/." "$prefix/include/AMF/"

# 3. x264, static, linked into avcodec.
(
    cd "$build/x264-$X264_COMMIT"
    ./configure --host="$host" --cross-prefix="$host-" --prefix="$prefix" \
        --enable-static --enable-pic --disable-cli --disable-opencl --enable-win32thread
    make -j"$jobs"
    make install
)

# 4. FFmpeg with only what CouchLink calls (see FfmpegLibrary, H264Decoder, H264Encoder, H264Frames).
configure_args=(
    --prefix="$prefix" --target-os=mingw32 --arch=x86_64 --cross-prefix="$host-"
    --pkg-config=pkg-config --pkg-config-flags=--static
    --extra-cflags="-I$prefix/include" --extra-ldflags="-L$prefix/lib -static-libgcc"
    --enable-gpl --enable-shared --disable-static
    --disable-autodetect --disable-everything --disable-programs --disable-doc --disable-network --disable-debug
    --disable-avformat --disable-avdevice --disable-avfilter
    --enable-w32threads --enable-d3d11va --enable-ffnvcodec --enable-nvenc --enable-amf --enable-libx264
    --enable-decoder=h264 --enable-parser=h264 --enable-hwaccel=h264_d3d11va,h264_d3d11va2
    --enable-encoder=libx264,h264_nvenc,h264_amf
)
(
    cd "$build/ffmpeg-$FFMPEG_VERSION"
    PKG_CONFIG_PATH="$prefix/lib/pkgconfig" ./configure "${configure_args[@]}"
    make -j"$jobs"
    make install
)

# 5. Checks: every component configure may have dropped silently is really in, and no DLL needs
#    a mingw runtime DLL that a clean Windows PC doesn't have.
components="$build/ffmpeg-$FFMPEG_VERSION/config_components.h"
for name in H264_DECODER H264_PARSER H264_D3D11VA_HWACCEL H264_D3D11VA2_HWACCEL \
            LIBX264_ENCODER H264_NVENC_ENCODER H264_AMF_ENCODER; do
    grep -q "^#define CONFIG_$name 1" "$components" || { echo "FFmpeg was configured without $name" >&2; exit 1; }
done
dlls=(avcodec-63.dll avutil-61.dll swscale-10.dll swresample-7.dll)
for dll in "${dlls[@]}"; do
    cp "$prefix/bin/$dll" "$dist/bin/"
    "$host-strip" "$dist/bin/$dll"
    bad="$("$host-objdump" -p "$dist/bin/$dll" | awk '/DLL Name:/ {print $3}' | grep -i '^lib' || true)"
    if [ -n "$bad" ]; then echo "$dll needs $bad, which Windows doesn't have" >&2; exit 1; fi
done

# 6. License text and the two zips.
ff="$build/ffmpeg-$FFMPEG_VERSION"
{
    echo "CouchLink's FFmpeg $FFMPEG_VERSION build ($BUILD_ID), with x264 $X264_COMMIT."
    echo "This build is licensed under the GNU GPL version 2 or later. Its complete source is"
    echo "couchlink-ffmpeg-$BUILD_ID-source.zip, attached to every CouchLink release."
    echo
    cat "$ff/LICENSE.md"
    echo
    cat "$ff/COPYING.GPLv2"
} > "$dist/LICENSE.txt"
{
    echo "Toolchain: $("$host-gcc" --version | head -n 1)"
    echo "NASM: $(nasm -v)"
    echo "FFmpeg configure: ${configure_args[*]}"
    echo "x264 configure: --host=$host --enable-static --enable-pic --disable-cli --disable-opencl --enable-win32thread"
} > "$src/CONFIGURE.txt"
cp "$here/build.sh" "$here/versions.env" "$src/"

(cd "$dist" && zip -qr "$out/couchlink-ffmpeg-$BUILD_ID-win64.zip" bin LICENSE.txt)
(cd "$src" && zip -qr "$out/couchlink-ffmpeg-$BUILD_ID-source.zip" .)
ls -l "$out"
```

- [ ] **Step 3: Write `.github/workflows/ffmpeg.yml`**

```yaml
name: FFmpeg

# Builds CouchLink's minimal FFmpeg (eng/ffmpeg/build.sh). Branch pushes that touch the build only
# build it; pushing the tag deps-ffmpeg-<BUILD_ID> also publishes it as a prerelease that
# eng/get-ffmpeg.ps1 downloads from.
on:
  push:
    branches: ['**']
    tags: ['deps-ffmpeg-*']
    paths: ['eng/ffmpeg/**', '.github/workflows/ffmpeg.yml']
  pull_request:
    paths: ['eng/ffmpeg/**', '.github/workflows/ffmpeg.yml']

permissions:
  contents: write

jobs:
  build:
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@v7

      - name: Tools
        run: sudo apt-get update && sudo apt-get install -y mingw-w64 nasm pkg-config make git curl xz-utils zip

      - name: Build
        run: bash eng/ffmpeg/build.sh "$RUNNER_TEMP/ffmpeg-build"

      - uses: actions/upload-artifact@v7
        with:
          name: couchlink-ffmpeg
          path: ${{ runner.temp }}/ffmpeg-build/out/*.zip
          retention-days: 7

      - name: Publish
        if: startsWith(github.ref, 'refs/tags/deps-ffmpeg-')
        env:
          GH_TOKEN: ${{ github.token }}
        run: |
          source eng/ffmpeg/versions.env
          if [ "$GITHUB_REF_NAME" != "deps-ffmpeg-$BUILD_ID" ]; then
            echo "Tag $GITHUB_REF_NAME doesn't match BUILD_ID $BUILD_ID in versions.env" >&2
            exit 1
          fi
          gh release create "$GITHUB_REF_NAME" --prerelease --title "FFmpeg $BUILD_ID for CouchLink" \
            --notes "CouchLink's minimal FFmpeg $FFMPEG_VERSION build for Windows x64. The -source.zip is its complete source and build script." \
            "$RUNNER_TEMP"/ffmpeg-build/out/*.zip
```

GitHub skips `paths` for tag pushes, so the tag always builds and publishes.

- [ ] **Step 4: Commit, push, and watch the build**

```bash
git add eng/ffmpeg .github/workflows/ffmpeg.yml
git commit -m "build(ffmpeg): build CouchLink's own minimal FFmpeg from pinned sources in CI"
git push -u origin plan9-install
gh run watch "$(gh run list --workflow ffmpeg.yml --branch plan9-install --limit 1 --json databaseId -q '.[0].databaseId')" --exit-status
```

Expected: the `FFmpeg` run passes; its log ends with `ls -l` showing both zips. If configure or make fails, read the log (`gh run view --log-failed`), fix `build.sh` (a missing mingw header, a renamed component, an x264 flag), ledger a ruling naming the change, commit `fix(ffmpeg): ...`, push, repeat. The import and component checks failing is the script working: fix the cause, never the check.

- [ ] **Step 5: Try the DLLs locally against the existing tests**

```bash
run=$(gh run list --workflow ffmpeg.yml --branch plan9-install --limit 1 --json databaseId -q '.[0].databaseId')
rm -rf "$TEMP/cl-ffmpeg" && gh run download "$run" -n couchlink-ffmpeg -D "$TEMP/cl-ffmpeg"
mv third_party/ffmpeg third_party/ffmpeg-btbn
mkdir -p third_party/ffmpeg && unzip -q "$TEMP/cl-ffmpeg/couchlink-ffmpeg-9.0.2-1-win64.zip" -d third_party/ffmpeg
dotnet test tests/CouchLink.Video.Tests > "$TEMP/video-ourffmpeg.txt" 2>&1; tail -5 "$TEMP/video-ourffmpeg.txt"
```

Expected: `Passed! - Failed: 0, Passed: 56`. These tests decode with the h264 decoder and parser, encode with libx264 through swscale, and check that `h264_amf` and `h264_nvenc` exist. This PC has an NVIDIA GPU: also run `dotnet run --project src/CouchLink.VideoTest -- --help` and, if it offers an encode test, run it with `h264_nvenc` to see NVENC open. Keep `third_party/ffmpeg` as our build (Task 2 rewrites `get-ffmpeg.ps1` to produce the same layout); delete `third_party/ffmpeg-btbn` after Task 2 passes.

---

### Task 2: Publish the build, switch `get-ffmpeg.ps1`, notices

**Files:**
- Modify: `eng/get-ffmpeg.ps1` (whole file)
- Modify: `tests/CouchLink.Video.Tests/FfmpegLibraryTests.cs`
- Modify: `THIRD-PARTY-NOTICES.md`

**Interfaces:**
- Consumes: Task 1's tag scheme and asset names.
- Produces: `third_party/ffmpeg/bin/*.dll`, `third_party/ffmpeg/LICENSE.txt`, `third_party/ffmpeg/BUILD.txt` containing `9.0.2-1`, and `third_party/ffmpeg/couchlink-ffmpeg-9.0.2-1-source.zip`. Task 3's `package.ps1` reads `BUILD.txt` and copies the source zip.

- [ ] **Step 1: Write the failing tests**

Add to `FfmpegLibraryTests` (the class stays non-`unsafe`; mark the second method `unsafe`):

```csharp
    [Fact]
    public void FFmpeg_is_CouchLinks_own_minimal_build()
    {
        // eng/ffmpeg/build.sh configures with --disable-everything; a general-purpose build doesn't.
        Assert.True(FfmpegLibrary.TryLoad(out var error), error);
        Assert.Contains("--disable-everything", ffmpeg.avcodec_configuration());
    }

    [Fact]
    public unsafe void The_build_has_the_H264_parser_and_D3D11_hardware_decoding()
    {
        Assert.True(FfmpegLibrary.TryLoad(out var error), error);
        var decoder = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
        Assert.True(decoder != null, "no h264 decoder");

        bool d3d11 = false;
        for (int i = 0; ffmpeg.avcodec_get_hw_config(decoder, i) is var config && config != null; i++)
            d3d11 |= config->device_type == AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA;
        Assert.True(d3d11, "the h264 decoder has no D3D11VA hardware config");

        var parser = ffmpeg.av_parser_init(AVCodecID.AV_CODEC_ID_H264);
        Assert.True(parser != null, "no h264 parser");
        ffmpeg.av_parser_close(parser);
    }
```

If `av_parser_init` takes `int` in this AutoGen version, pass `(int)AVCodecID.AV_CODEC_ID_H264`, matching `H264Frames.cs:11`.

- [ ] **Step 2: Run them against BtbN's build to see the first fail**

```bash
rm -rf third_party/ffmpeg && mv third_party/ffmpeg-btbn third_party/ffmpeg
dotnet test tests/CouchLink.Video.Tests --filter "FullyQualifiedName~FfmpegLibraryTests" 2>&1 | tail -15
```

Expected: `FFmpeg_is_CouchLinks_own_minimal_build` FAILS (`Assert.Contains() Failure`); the hw-config test passes (BtbN's build has D3D11VA too; it guards against a future build dropping it).

- [ ] **Step 3: Publish the build**

```bash
git tag deps-ffmpeg-9.0.2-1
git push origin deps-ffmpeg-9.0.2-1
gh run watch "$(gh run list --workflow ffmpeg.yml --limit 1 --json databaseId -q '.[0].databaseId')" --exit-status
gh release view deps-ffmpeg-9.0.2-1 --json assets,isPrerelease -q '.isPrerelease, (.assets[].name)'
```

Expected: `true`, `couchlink-ffmpeg-9.0.2-1-source.zip`, `couchlink-ffmpeg-9.0.2-1-win64.zip`. Then the pins:

```bash
rm -rf "$TEMP/cl-pins" && gh release download deps-ffmpeg-9.0.2-1 -D "$TEMP/cl-pins" && sha256sum "$TEMP"/cl-pins/*
```

- [ ] **Step 4: Rewrite `eng/get-ffmpeg.ps1`**

Replace the whole file (paste the two hashes from Step 3):

```powershell
<#
.SYNOPSIS
    Downloads CouchLink's own FFmpeg 9.0.2 build into third_party/ffmpeg.
.DESCRIPTION
    eng/ffmpeg/build.sh builds it in CI from pinned sources with only what CouchLink uses
    (H.264 decode with D3D11VA; h264_amf, h264_nvenc and libx264 encoders) and publishes it to
    the prerelease deps-ffmpeg-<build>. This script fetches the DLLs and the source zip from
    there, checked against pinned SHA-256s, so every release ships the same FFmpeg and attaches
    its complete source. When the build changes: push the new deps-ffmpeg tag, then update
    $build and both hashes here, and THIRD-PARTY-NOTICES.md. Run once after cloning, and in CI.
.EXAMPLE
    ./eng/get-ffmpeg.ps1
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is very slow with the progress bar
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'third_party/ffmpeg'
$build = '9.0.2-1'
$release = "https://github.com/enriquezchristopher/CouchLink/releases/download/deps-ffmpeg-$build"
$assets = @{
    "couchlink-ffmpeg-$build-win64.zip"  = '<sha256 of the win64 zip from Step 3>'
    "couchlink-ffmpeg-$build-source.zip" = '<sha256 of the source zip from Step 3>'
}
$marker = Join-Path $dest 'BUILD.txt'

if ((Test-Path $marker) -and ((Get-Content $marker -Raw).Trim() -eq $build) -and -not $Force) {
    Write-Host "FFmpeg $build is already in $dest (use -Force to download again)."
    return
}

$temp = Join-Path ([IO.Path]::GetTempPath()) "couchlink-ffmpeg-$([guid]::NewGuid())"
New-Item -ItemType Directory $temp | Out-Null
try {
    foreach ($name in $assets.Keys) {
        Write-Host "Downloading $release/$name"
        Invoke-WebRequest "$release/$name" -OutFile (Join-Path $temp $name)
        $actual = (Get-FileHash (Join-Path $temp $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $assets[$name]) {
            throw "$name's SHA-256 is $actual, not the pinned $($assets[$name])."
        }
    }
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    Expand-Archive (Join-Path $temp "couchlink-ffmpeg-$build-win64.zip") $dest
    Copy-Item (Join-Path $temp "couchlink-ffmpeg-$build-source.zip") $dest
    Set-Content $marker $build
} finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path (Join-Path $dest 'bin/avcodec-63.dll'))) {
    throw 'The download has no bin/avcodec-63.dll, so it is not CouchLink''s FFmpeg 9 build.'
}
Write-Host "FFmpeg $build is in $dest."
```

The `<sha256 ...>` markers are the only values this plan can't know in advance; they must be real hashes before the commit.

- [ ] **Step 5: Run the script and the tests**

```bash
pwsh -NoProfile -File eng/get-ffmpeg.ps1 -Force
dotnet build -c Release > "$TEMP/b.txt" 2>&1; grep -E "Warn|Error" "$TEMP/b.txt" | tail -3
dotnet test > "$TEMP/t.txt" 2>&1; grep -E "Passed!|Failed!" "$TEMP/t.txt"
```

Expected: the build has 0 warnings and 0 errors; Core 608, Video 58, Audio 5 passed.

- [ ] **Step 6: Update `THIRD-PARTY-NOTICES.md`**

Replace the FFmpeg row of "Included in releases" with these four rows:

```markdown
| FFmpeg 9.0.2 (libavcodec, libavutil, libswscale, libswresample; CouchLink's own minimal build) | CouchLink.App, VideoTest (video) | GPL-2.0-or-later; license text in `ffmpeg/LICENSE.txt` | https://ffmpeg.org; complete source: `couchlink-ffmpeg-9.0.2-1-source.zip` on every release |
| x264 (commit b35605ac, linked into libavcodec) | CouchLink.App, VideoTest (CPU encoding fallback) | GPL-2.0-or-later | https://code.videolan.org/videolan/x264; in the same source zip |
| nv-codec-headers n12.1.14.1 (compiled into libavcodec) | NVIDIA encoding | MIT | https://github.com/FFmpeg/nv-codec-headers; in the same source zip |
| AMD AMF headers v1.5.3 (compiled into libavcodec) | AMD encoding | MIT | https://github.com/GPUOpen-LibrariesAndSDKs/AMF; in the same source zip |
```

Replace the closing paragraph about BtbN's build with:

```markdown
Releases include CouchLink's own FFmpeg build `9.0.2-1`, built by
[`eng/ffmpeg/build.sh`](eng/ffmpeg/build.sh) from the pinned sources in
[`eng/ffmpeg/versions.env`](eng/ffmpeg/versions.env). Every CouchLink release
attaches `couchlink-ffmpeg-9.0.2-1-source.zip`: the exact FFmpeg, x264,
nv-codec-headers and AMF sources it was built from, the build script and the
configure line. The build is also published at
https://github.com/enriquezchristopher/CouchLink/releases/tag/deps-ffmpeg-9.0.2-1.
```

- [ ] **Step 7: Commit**

```bash
rm -rf third_party/ffmpeg-btbn
git add eng/get-ffmpeg.ps1 tests/CouchLink.Video.Tests/FfmpegLibraryTests.cs THIRD-PARTY-NOTICES.md
git commit -m "build(ffmpeg): releases use CouchLink's own FFmpeg build, with its complete source"
```

---

### Task 3: The setup (files, shortcuts, upgrades) and the package

**Files:**
- Create: `eng/get-inno-setup.ps1`, `eng/get-vigembus.ps1`, `eng/verify-package.ps1`
- Create: `installer/CouchLink.iss`, `installer/SOURCE.txt`
- Modify: `eng/package.ps1` (whole file)

**Interfaces:**
- Consumes: Task 2's `third_party/ffmpeg/BUILD.txt` and source zip.
- Produces: `./eng/package.ps1 -OutDir artifacts` writes `artifacts/CouchLink-v<version>-win-x64.zip`, `artifacts/CouchLink-Setup-v<version>.exe`, `artifacts/couchlink-ffmpeg-<build>-source.zip`. `installer/CouchLink.iss` takes `/DAppVersion=`, `/DStageDir=`, `/DViGEmBusSetup=`, `/DOutputDir=`. Task 4 adds `[Code]` for the driver, firewall and uninstall to the same script.

- [ ] **Step 1: Write `eng/verify-package.ps1` (the failing check)**

```powershell
<#
.SYNOPSIS
    Checks a package built by eng/package.ps1: the staged folder, the zip, the setup and the
    FFmpeg source zip. Throws on the first problem.
#>
param(
    [Parameter(Mandatory)][string]$Stage,
    [Parameter(Mandatory)][string]$OutDir,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$FfmpegBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Require([bool]$ok, [string]$what) {
    if (-not $ok) { throw "Package check failed: $what" }
}

foreach ($file in 'CouchLink.App.exe', 'PadTest/CouchLink.PadTest.exe', 'VideoTest/CouchLink.VideoTest.exe',
                  'ffmpeg/avcodec-63.dll', 'ffmpeg/avutil-61.dll', 'ffmpeg/swscale-10.dll', 'ffmpeg/swresample-7.dll',
                  'ffmpeg/LICENSE.txt', 'LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md', 'SOURCE.txt') {
    Require (Test-Path (Join-Path $Stage $file)) "$file is missing from $Stage"
}

$sourceZip = "couchlink-ffmpeg-$FfmpegBuild-source.zip"
Require (Test-Path (Join-Path $OutDir "CouchLink-v$Version-win-x64.zip")) "the zip is missing"
Require (Test-Path (Join-Path $OutDir $sourceZip)) "$sourceZip is missing"
$setup = Join-Path $OutDir "CouchLink-Setup-v$Version.exe"
Require (Test-Path $setup) "CouchLink-Setup-v$Version.exe is missing"
Require ((Get-Item $setup).VersionInfo.ProductVersion -eq $Version) "the setup's version isn't $Version"

$notices = Get-Content (Join-Path $Stage 'THIRD-PARTY-NOTICES.md') -Raw
Require ($notices.Contains($sourceZip)) "THIRD-PARTY-NOTICES.md doesn't name $sourceZip"

# The finish page must start CouchLink as the signed-in user, not as the admin who ran setup.
$script = Get-Content (Join-Path $root 'installer/CouchLink.iss') -Raw
foreach ($line in $script -split "`n" | Where-Object { $_ -match 'Flags:.*postinstall' }) {
    Require ($line -match 'runasoriginaluser') "a postinstall entry runs elevated: $($line.Trim())"
}
Write-Host "Package checks passed: $OutDir"
```

- [ ] **Step 2: Run it against today's package to see it fail**

```bash
pwsh -NoProfile -File eng/package.ps1 -OutDir artifacts > "$TEMP/pkg.txt" 2>&1; tail -2 "$TEMP/pkg.txt"
pwsh -NoProfile -File eng/verify-package.ps1 -Stage artifacts/CouchLink-v1.6.0-win-x64 -OutDir artifacts -Version 1.6.0 -FfmpegBuild 9.0.2-1
```

Expected: FAIL with `Package check failed: SOURCE.txt is missing from artifacts/CouchLink-v1.6.0-win-x64`. (Use the version in `eng/version.props` if it isn't 1.6.0.)

- [ ] **Step 3: Write `eng/get-inno-setup.ps1` and `eng/get-vigembus.ps1`**

`eng/get-inno-setup.ps1`:

```powershell
<#
.SYNOPSIS
    Installs the pinned Inno Setup 6.7.3 compiler into third_party/innosetup (for the current
    user; no admin). GitHub's Windows runners no longer include it. Run once after cloning, and in CI.
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'third_party/innosetup'
$version = '6.7.3'
$url = "https://github.com/jrsoftware/issrc/releases/download/is-$($version.Replace('.', '_'))/innosetup-$version.exe"
$sha256 = '9c73c3bae7ed48d44112a0f48e66742c00090bdb5bef71d9d3c056c66e97b732'
$iscc = Join-Path $dest 'ISCC.exe'

if ((Test-Path $iscc) -and (Get-Item $iscc).VersionInfo.ProductVersion.StartsWith($version) -and -not $Force) {
    Write-Host "Inno Setup $version is already in $dest."
    return
}

$installer = Join-Path ([IO.Path]::GetTempPath()) "innosetup-$version-$([guid]::NewGuid()).exe"
try {
    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $installer
    $actual = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $sha256) { throw "The download's SHA-256 is $actual, not the pinned $sha256." }
    $p = Start-Process $installer -Wait -PassThru -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART',
        '/SP-', '/CURRENTUSER', '/NOICONS', "/DIR=`"$dest`""
    if ($p.ExitCode -ne 0) { throw "The Inno Setup installer exited with code $($p.ExitCode)." }
} finally {
    Remove-Item $installer -Force -ErrorAction SilentlyContinue
}
if (-not (Test-Path $iscc)) { throw "Inno Setup installed, but $iscc is missing." }
Write-Host "Inno Setup $version is in $dest."
```

`eng/get-vigembus.ps1`:

```powershell
<#
.SYNOPSIS
    Downloads the pinned official ViGEmBus 1.22.0 installer into third_party/vigembus; the
    CouchLink setup bundles it. Run once after cloning, and in CI.
#>
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'third_party/vigembus'
$name = 'ViGEmBus_1.22.0_x64_x86_arm64.exe'
$url = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/$name"
$sha256 = '89220a7865076b342892f98865f3499fb7c4cfd673159e89d352c360fd014c6a'
$file = Join-Path $dest $name

if ((Test-Path $file) -and (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -eq $sha256) {
    Write-Host "$name is already in $dest."
    return
}
New-Item -ItemType Directory -Force $dest | Out-Null
Write-Host "Downloading $url"
Invoke-WebRequest $url -OutFile $file
$actual = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actual -ne $sha256) {
    Remove-Item $file -Force
    throw "The download's SHA-256 is $actual, not the pinned $sha256."
}
Write-Host "$name is in $dest."
```

- [ ] **Step 4: Write `installer/SOURCE.txt`**

```text
CouchLink is free software under the GNU GPL v3. Its source, and the complete source of the
FFmpeg build it ships, are attached to every release at
https://github.com/enriquezchristopher/CouchLink/releases
```

- [ ] **Step 5: Write `installer/CouchLink.iss` (files, shortcuts, upgrades)**

```iss
; CouchLink setup. Built by eng/package.ps1:
;   ISCC.exe /DAppVersion=1.6.0 /DStageDir=<staged app> /DViGEmBusSetup=<ViGEmBus exe> /DOutputDir=<dir> installer\CouchLink.iss
; Spec: docs/superpowers/specs/2026-10-08-couchlink-cafe-install-design.md

#ifndef AppVersion
  #error Pass /DAppVersion=<version>
#endif
#ifndef StageDir
  #error Pass /DStageDir=<the staged app folder>
#endif
#ifndef ViGEmBusSetup
  #error Pass /DViGEmBusSetup=<path to ViGEmBus_1.22.0_x64_x86_arm64.exe>
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif
#define GuideUrl "https://github.com/enriquezchristopher/CouchLink/blob/main/docs/cafe-setup-guide.md"

[Setup]
; Never change AppId: it is how a new setup finds the installed CouchLink.
AppId={{31D75DA0-B0D5-4551-A9AE-F461A7851EE1}
AppName=CouchLink
AppVersion={#AppVersion}
AppVerName=CouchLink {#AppVersion}
AppPublisher=CouchLink contributors
AppPublisherURL=https://github.com/enriquezchristopher/CouchLink
AppSupportURL=https://github.com/enriquezchristopher/CouchLink/issues
VersionInfoVersion={#AppVersion}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={autopf}\CouchLink
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile={#StageDir}\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=CouchLink-Setup-v{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UninstallDisplayName=CouchLink
UninstallDisplayIcon={app}\CouchLink.App.exe

[Messages]
WindowsVersionNotSupported=CouchLink needs 64-bit Windows 10 or later.
OnlyOnTheseArchitectures=CouchLink needs 64-bit Windows 10 or later.

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"

[Files]
Source: "{#StageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ViGEmBusSetup}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\CouchLink"; Filename: "{app}\CouchLink.App.exe"
Name: "{autodesktop}\CouchLink"; Filename: "{app}\CouchLink.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\CouchLink.App.exe"; Description: "Start CouchLink"; Flags: postinstall nowait skipifsilent runasoriginaluser
Filename: "{#GuideUrl}"; Description: "Open the setup guide"; Flags: postinstall nowait skipifsilent shellexec runasoriginaluser

[Code]
const
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{31D75DA0-B0D5-4551-A9AE-F461A7851EE1}_is1';
  ExitNewerInstalled = 12;

procedure ExitProcess(ExitCode: Cardinal); external 'ExitProcess@kernel32.dll stdcall';

{ -1, 0 or 1 as dotted version A is older than, the same as or newer than B. }
function CompareVersions(A, B: String): Integer;
var
  DotA, DotB, PartA, PartB: Integer;
begin
  Result := 0;
  while (Result = 0) and ((A <> '') or (B <> '')) do
  begin
    DotA := Pos('.', A);
    if DotA = 0 then DotA := Length(A) + 1;
    DotB := Pos('.', B);
    if DotB = 0 then DotB := Length(B) + 1;
    PartA := StrToIntDef(Copy(A, 1, DotA - 1), 0);
    PartB := StrToIntDef(Copy(B, 1, DotB - 1), 0);
    if PartA < PartB then Result := -1
    else if PartA > PartB then Result := 1;
    Delete(A, 1, DotA);
    Delete(B, 1, DotB);
  end;
end;

{ Installing over a newer CouchLink is refused; the same version reinstalls (a repair). }
function InitializeSetup(): Boolean;
var
  Installed: String;
begin
  Result := True;
  if RegQueryStringValue(HKLM, UninstallKey, 'DisplayVersion', Installed)
     and (CompareVersions(Installed, '{#AppVersion}') > 0) then
  begin
    Log('CouchLink ' + Installed + ' is installed, newer than this setup ({#AppVersion}).');
    if WizardSilent() then
      ExitProcess(ExitNewerInstalled);
    MsgBox('A newer CouchLink (v' + Installed + ') is already installed.', mbError, MB_OK);
    Result := False;
  end;
end;
```

Inno can't return a custom exit code from `InitializeSetup`, so silent mode ends the process with 12 there; interactive mode shows the message and Inno exits with its own code.

- [ ] **Step 6: Rewrite `eng/package.ps1`**

```powershell
<#
.SYNOPSIS
    Builds the release files: CouchLink-v<version>-win-x64.zip, CouchLink-Setup-v<version>.exe
    and the FFmpeg source zip.
.DESCRIPTION
    Publishes the app, PadTest and VideoTest as self-contained win-x64 builds (no .NET install
    needed on the target PC), with FFmpeg 9 in ffmpeg\, stages them once with the license files,
    zips the staged folder (the portable download) and compiles installer/CouchLink.iss against
    the same folder, then checks the result with eng/verify-package.ps1.
    Needs eng/get-ffmpeg.ps1, eng/get-inno-setup.ps1 and eng/get-vigembus.ps1 run first.
    The version comes from eng/version.props.
.EXAMPLE
    ./eng/package.ps1 -OutDir artifacts
#>
param([string]$OutDir = 'artifacts')

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$props = Get-Content (Join-Path $root 'eng/version.props')
$version = ([string]$props.Project.PropertyGroup.Version).Trim()
$name = "CouchLink-v$version-win-x64"
$out = Join-Path $root $OutDir
$stage = Join-Path $out $name
$ffmpeg = Join-Path $root 'third_party/ffmpeg'
$iscc = Join-Path $root 'third_party/innosetup/ISCC.exe'
$vigembus = Join-Path $root 'third_party/vigembus/ViGEmBus_1.22.0_x64_x86_arm64.exe'

if (-not (Test-Path (Join-Path $ffmpeg 'BUILD.txt'))) {
    throw 'FFmpeg is missing: run ./eng/get-ffmpeg.ps1 first (host video needs it).'
}
if (-not (Test-Path $iscc)) { throw 'Inno Setup is missing: run ./eng/get-inno-setup.ps1 first.' }
if (-not (Test-Path $vigembus)) { throw 'The ViGEmBus installer is missing: run ./eng/get-vigembus.ps1 first.' }
$ffmpegBuild = (Get-Content (Join-Path $ffmpeg 'BUILD.txt') -Raw).Trim()
$sourceZip = Join-Path $ffmpeg "couchlink-ffmpeg-$ffmpegBuild-source.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

function Publish([string]$project, [string]$destination) {
    dotnet publish (Join-Path $root $project) -c Release -r win-x64 --self-contained true `
        -p:DebugType=embedded -o $destination
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $project" }
}

Publish 'src/CouchLink.App' $stage
Publish 'src/CouchLink.PadTest' (Join-Path $stage 'PadTest')
Publish 'src/CouchLink.VideoTest' (Join-Path $stage 'VideoTest')

foreach ($file in 'LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md', 'installer/SOURCE.txt') {
    Copy-Item (Join-Path $root $file) $stage
}

$zip = Join-Path $out "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Write-Host "Package: $zip"

& $iscc /Qp "/DAppVersion=$version" "/DStageDir=$stage" "/DViGEmBusSetup=$vigembus" "/DOutputDir=$out" `
    (Join-Path $root 'installer/CouchLink.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with code $LASTEXITCODE" }
Write-Host "Setup: $(Join-Path $out "CouchLink-Setup-v$version.exe")"

Copy-Item $sourceZip $out
& (Join-Path $PSScriptRoot 'verify-package.ps1') -Stage $stage -OutDir $out -Version $version -FfmpegBuild $ffmpegBuild
```

- [ ] **Step 7: Build the package and see the check pass**

```bash
pwsh -NoProfile -File eng/get-inno-setup.ps1 && pwsh -NoProfile -File eng/get-vigembus.ps1
pwsh -NoProfile -File eng/package.ps1 -OutDir artifacts > "$TEMP/pkg.txt" 2>&1; tail -4 "$TEMP/pkg.txt"
```

Expected: `Setup: ...CouchLink-Setup-v1.6.0.exe` and `Package checks passed: ...artifacts`. Inno compile errors print the line; fix the script, not the check.

- [ ] **Step 8: Commit**

```bash
git add eng/get-inno-setup.ps1 eng/get-vigembus.ps1 eng/verify-package.ps1 eng/package.ps1 installer
git commit -m "feat(install): CouchLink setup with shortcuts, upgrades over older versions, and package checks"
```

---

### Task 4: Driver, firewall and uninstall, with an install round trip in CI

**Files:**
- Create: `eng/test-installer.ps1`
- Modify: `installer/CouchLink.iss` (append to `[Code]`)
- Modify: `.github/workflows/ci.yml`, `.github/workflows/release.yml`

**Interfaces:**
- Consumes: Task 3's setup and `package.ps1`.
- Produces: `./eng/test-installer.ps1 -Setup <path>` (needs admin; changes the machine: installs then uninstalls CouchLink, may install ViGEmBus).

The round trip changes the PC it runs on, so it runs in CI. Run it on this PC only if the owner has said yes; otherwise push and read the CI result.

- [ ] **Step 1: Write `eng/test-installer.ps1` (the failing check)**

```powershell
<#
.SYNOPSIS
    Silent install, repair and uninstall of a CouchLink setup, checking files and firewall rules
    after each. Needs admin, and changes this PC: run it in CI or on a test PC.
.EXAMPLE
    ./eng/test-installer.ps1 -Setup artifacts/CouchLink-Setup-v1.6.0.exe
#>
param([Parameter(Mandatory)][string]$Setup)

$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:ProgramFiles 'CouchLink'
$app = Join-Path $dir 'CouchLink.App.exe'
$rules = [ordered]@{
    'CouchLink (UDP 47800, discovery)'      = 'UDP', '47800'
    'CouchLink (TCP 47801, sessions)'       = 'TCP', '47801'
    'CouchLink (UDP 47802, video and audio)' = 'UDP', '47802'
    'CouchLink (UDP 47803, input)'          = 'UDP', '47803'
}

function Fail([string]$message) { throw "Installer check failed: $message" }

function Install([string]$what) {
    $log = Join-Path ([IO.Path]::GetTempPath()) "couchlink-setup-$what.log"
    $p = Start-Process (Resolve-Path $Setup) -Wait -PassThru `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$log`""
    Write-Host "$what exited with code $($p.ExitCode) (log: $log)"
    if ($p.ExitCode -eq 10) {
        Write-Warning "ViGEmBus didn't install on this machine (code 10); the driver install stays a manual check."
    } elseif ($p.ExitCode -ne 0) {
        Get-Content $log -Tail 30 | Write-Host
        Fail "$what exited with code $($p.ExitCode)"
    }
}

function Check-Installed {
    if (-not (Test-Path $app)) { Fail "$app is missing" }
    $found = @(Get-NetFirewallRule -DisplayName 'CouchLink (*' -ErrorAction SilentlyContinue)
    if ($found.Count -ne $rules.Count) {
        Fail "expected $($rules.Count) CouchLink firewall rules, found $($found.Count): $($found.DisplayName -join '; ')"
    }
    foreach ($name in $rules.Keys) {
        $rule = $found | Where-Object DisplayName -eq $name
        if (-not $rule) { Fail "no firewall rule '$name'" }
        $port = $rule | Get-NetFirewallPortFilter
        $address = $rule | Get-NetFirewallAddressFilter
        $program = ($rule | Get-NetFirewallApplicationFilter).Program
        if ($rule.Direction -ne 'Inbound' -or $rule.Action -ne 'Allow' -or $rule.Enabled -ne 'True') { Fail "'$name' isn't an enabled inbound allow rule" }
        if ($rule.Profile -ne 'Any') { Fail "'$name' applies to $($rule.Profile), not every profile" }
        if ($port.Protocol -ne $rules[$name][0] -or $port.LocalPort -ne $rules[$name][1]) { Fail "'$name' is $($port.Protocol) $($port.LocalPort)" }
        if ($address.RemoteAddress -ne 'LocalSubnet') { Fail "'$name' allows $($address.RemoteAddress), not LocalSubnet" }
        if ($program -ne $app) { Fail "'$name' is for $program, not $app" }
    }
}

Install 'install'
Check-Installed
Install 'repair'   # a second run (repair or upgrade) must not double the rules
Check-Installed

$uninstaller = Join-Path $dir 'unins000.exe'
Start-Process $uninstaller -Wait -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
# The uninstaller re-launches itself from %TEMP% and returns at once; wait for it to finish.
$deadline = (Get-Date).AddMinutes(2)
while ((Test-Path $uninstaller) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 500 }
if (Test-Path $app) { Fail "$app is still there after uninstalling" }
$left = @(Get-NetFirewallRule -DisplayName 'CouchLink (*' -ErrorAction SilentlyContinue)
if ($left.Count -gt 0) { Fail "uninstall left firewall rules: $($left.DisplayName -join '; ')" }
Write-Host 'Installer round trip passed.'
```

- [ ] **Step 2: Run it against Task 3's setup to see it fail**

With the owner's yes, on this PC from an elevated shell:

```bash
pwsh -NoProfile -File eng/test-installer.ps1 -Setup artifacts/CouchLink-Setup-v1.6.0.exe
```

Otherwise add the workflow steps from Step 5 first, push, and read the CI log. Expected: FAIL with `Installer check failed: expected 4 CouchLink firewall rules, found 0`. If it ran here, uninstall the leftover from Settings > Apps, or run `"C:\Program Files\CouchLink\unins000.exe" /VERYSILENT`.

- [ ] **Step 3: Add the driver, firewall and uninstall code**

In `installer/CouchLink.iss`, add to the `const` block:

```iss
  ViGEmBusFile = 'ViGEmBus_1.22.0_x64_x86_arm64.exe';
  ExitViGEmBusFailed = 10;
  ExitFirewallFailed = 11;
```

and append after `InitializeSetup`:

```iss
var
  CustomExitCode: Integer;
  RestartNeeded: Boolean;

{ The four firewall rules, by index 0-3. Names and ports: section 3 of the main design. }
procedure FirewallRule(Index: Integer; var Name, Protocol, Port: String);
begin
  case Index of
    0: begin Name := 'CouchLink (UDP 47800, discovery)'; Protocol := 'UDP'; Port := '47800'; end;
    1: begin Name := 'CouchLink (TCP 47801, sessions)'; Protocol := 'TCP'; Port := '47801'; end;
    2: begin Name := 'CouchLink (UDP 47802, video and audio)'; Protocol := 'UDP'; Port := '47802'; end;
    3: begin Name := 'CouchLink (UDP 47803, input)'; Protocol := 'UDP'; Port := '47803'; end;
  end;
end;

function Netsh(Params: String): Integer;
begin
  if not Exec(ExpandConstant('{sys}\netsh.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, Result) then
    Log('netsh did not start: ' + SysErrorMessage(Result));
  Log(Format('netsh %s -> %d', [Params, Result]));
end;

procedure Fail(ExitCode: Integer; Message: String);
begin
  if CustomExitCode = 0 then
    CustomExitCode := ExitCode;
  Log(Message);
  SuppressibleMsgBox(Message, mbError, MB_OK, IDOK);
end;

{ Deletes then adds each rule, so a repair or an upgrade never doubles them. }
procedure AddFirewallRules();
var
  I: Integer;
  Name, Protocol, Port: String;
begin
  for I := 0 to 3 do
  begin
    FirewallRule(I, Name, Protocol, Port);
    Netsh('advfirewall firewall delete rule name="' + Name + '"');
    if Netsh('advfirewall firewall add rule name="' + Name + '" dir=in action=allow enable=yes'
             + ' profile=any remoteip=localsubnet protocol=' + Protocol + ' localport=' + Port
             + ' program="' + ExpandConstant('{app}\CouchLink.App.exe') + '"') <> 0 then
      Fail(ExitFirewallFailed, 'CouchLink is installed, but Windows Firewall didn''t accept the rule "'
           + Name + '". Other PCs may not find or reach this one. See the setup guide.');
  end;
end;

procedure RemoveFirewallRules();
var
  I: Integer;
  Name, Protocol, Port: String;
begin
  for I := 0 to 3 do
  begin
    FirewallRule(I, Name, Protocol, Port);
    Netsh('advfirewall firewall delete rule name="' + Name + '"');
  end;
end;

function ViGEmBusInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus');
end;

procedure InstallViGEmBus();
var
  Code: Integer;
begin
  if ViGEmBusInstalled() then
  begin
    Log('ViGEmBus is already installed.');
    exit;
  end;
  WizardForm.StatusLabel.Caption := 'Installing the ViGEmBus driver...';
  ExtractTemporaryFile(ViGEmBusFile);
  if not Exec(ExpandConstant('{tmp}\') + ViGEmBusFile, '/exenoui /qn /norestart', '', SW_HIDE,
              ewWaitUntilTerminated, Code) then
    Log('The ViGEmBus installer did not start: ' + SysErrorMessage(Code));
  Log(Format('The ViGEmBus installer exited with code %d.', [Code]));
  if Code = 3010 then
    RestartNeeded := True
  else if (Code <> 0) or not ViGEmBusInstalled() then
    Fail(ExitViGEmBusFailed, Format('CouchLink is installed, but the ViGEmBus driver didn''t install (code %d). '
         + 'Virtual controllers won''t work on this PC until it is installed. See the setup guide.', [Code]));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    InstallViGEmBus();
    AddFirewallRules();
  end;
end;

function NeedRestart(): Boolean;
begin
  Result := RestartNeeded;
end;

function GetCustomSetupExitCode(): Integer;
begin
  Result := CustomExitCode;
end;

{ Uninstall: ask CouchLink to close, end it if it hasn't after 3 s, and remove the rules.
  ViGEmBus and %LOCALAPPDATA%\CouchLink stay. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Code: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM CouchLink.App.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(3000);
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM CouchLink.App.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
    RemoveFirewallRules();
  end;
end;
```

Pascal Script needs a `var` block before the code that uses it; if ISCC complains that `CustomExitCode` is used before its declaration, move this `var` block above `InitializeSetup`.

- [ ] **Step 4: Rebuild and see the round trip pass**

```bash
pwsh -NoProfile -File eng/package.ps1 -OutDir artifacts > "$TEMP/pkg.txt" 2>&1; tail -2 "$TEMP/pkg.txt"
```

Expected: `Package checks passed`. Then the round trip as in Step 2 (here with the owner's yes, or in CI after Step 5). Expected: `Installer round trip passed.`

- [ ] **Step 5: Wire up CI and the release**

In `.github/workflows/ci.yml`, after `Get FFmpeg`, add:

```yaml
      - name: Get Inno Setup and ViGEmBus
        if: steps.code.outputs.present == 'true'
        shell: pwsh
        run: |
          ./eng/get-inno-setup.ps1
          ./eng/get-vigembus.ps1
```

Rename `Trial release package` to `Package` (same `run:`), and after it add:

```yaml
      - name: Install, repair and uninstall the setup
        if: steps.code.outputs.present == 'true'
        shell: pwsh
        run: ./eng/test-installer.ps1 -Setup (Get-ChildItem artifacts/CouchLink-Setup-*.exe).FullName
```

and change `Upload package`'s `path:` to:

```yaml
          path: |
            artifacts/*.zip
            artifacts/*.exe
```

In `.github/workflows/release.yml`'s `publish` job, add the same `Get Inno Setup and ViGEmBus` step (without the `if:`) after `Get FFmpeg`, the same round-trip step (without the `if:`) after `Package`, and change the upload step's `run:` to:

```yaml
        run: gh release upload "${{ needs.release-please.outputs.tag_name }}" (Get-ChildItem artifacts/*.zip, artifacts/*.exe).FullName --clobber
```

- [ ] **Step 6: Commit and push; read CI**

```bash
git add installer/CouchLink.iss eng/test-installer.ps1 .github/workflows/ci.yml .github/workflows/release.yml
git commit -m "feat(install): the setup installs ViGEmBus and the firewall rules; uninstall removes them; CI checks a round trip"
git push
gh run watch "$(gh run list --workflow ci.yml --branch plan9-install --limit 1 --json databaseId -q '.[0].databaseId')" --exit-status
```

Expected: CI passes, the round-trip step prints `Installer round trip passed.` Record whether the runner installed ViGEmBus (code 0) or not (code 10 warning) in the ledger; the PR says which.

---

### Task 5: The guide and the docs

**Files:**
- Create: `docs/cafe-setup-guide.md`
- Modify: `README.md` (status and Download), `ROADMAP.md`, `docs/superpowers/specs/2026-10-05-couchlink-design.md` (section 8), `docs/gate-results.md`

**Interfaces:**
- Consumes: the exit codes, rule names and file names from Tasks 2-4; the app's labels: `Host`, `Join`, `Allow`, `Deny`, `Allow everyone (no popup when someone joins)`, `Kick`, `Stop hosting`, `Leave`, `Join by address...`, `⚙ Controls`, `Crash reports`; messages `The host didn't answer.`, `Request denied.`, `Lost the host.`; F2 lines `Packet loss` and `FEC repairs`.

- [ ] **Step 1: Write `docs/cafe-setup-guide.md`**

```markdown
# CouchLink setup guide for cafés and LAN rooms

CouchLink lets players on different PCs play one couch co-op game together.
One PC runs the game and hosts; the other PCs join, see and hear the game,
and each gets its own controller in it. Everything stays on your local
network.

## 1. Before you start

- Every PC runs Windows 10 or 11, 64-bit.
- The PCs are wired to the same gigabit switch (or at least the same
  subnet). Wi-Fi works badly for game streaming.
- The game is installed on each PC you want to host from. Joining PCs
  don't need the game.

## 2. Install CouchLink on every PC

Download `CouchLink-Setup-vX.Y.Z.exe` from
https://github.com/enriquezchristopher/CouchLink/releases (under Assets).
Install it on every PC: any PC can host or join.

By hand: run the setup and click through. It needs administrator rights.
It installs:

- CouchLink, in `C:\Program Files\CouchLink`, with a Start menu and a
  desktop shortcut;
- the ViGEmBus driver, which makes the virtual controllers (skipped if the
  PC already has it);
- four Windows Firewall rules so the PCs can find and reach each other on
  the local network. They are named `CouchLink (UDP 47800, discovery)`,
  `CouchLink (TCP 47801, sessions)`, `CouchLink (UDP 47802, video and
  audio)` and `CouchLink (UDP 47803, input)`.

The setup isn't code-signed yet, so Windows may say "Windows protected
your PC". Click More info, then Run anyway.

Silently, for many PCs (from a shared folder or a remote tool, as
administrator):

    CouchLink-Setup-vX.Y.Z.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="C:\couchlink-setup.log"

It returns one of these exit codes:

| Code | Meaning |
|---|---|
| 0 | Installed. |
| 10 | Installed, but the ViGEmBus driver didn't install. The PC can join, but can't host with controllers. See Troubleshooting. |
| 11 | Installed, but a firewall rule couldn't be added. Other PCs may not find this one. |
| 12 | Not installed: a newer CouchLink is already on this PC. |
| 1 to 8 | The setup itself failed; the log says why. |

To uninstall: Settings > Apps > CouchLink, or silently
`"C:\Program Files\CouchLink\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES`.
This removes CouchLink and its firewall rules. It keeps the ViGEmBus
driver, which other software also uses, and the logs and crash reports in
`%LOCALAPPDATA%\CouchLink`.

## 3. Game settings

- Run the game in borderless windowed mode (sometimes called "windowed
  fullscreen"), so CouchLink can capture the screen.
- NBA 2K22: each joining player shows up as a controller in the
  controller select screen; move it to a team as usual.
- NBA 2K14 accepts only one virtual controller.

## 4. The first session

1. On the PC with the game, start CouchLink and click Host. Start the game.
2. On another PC, start CouchLink, click Join, and pick the host in the
   list.
3. On the host, click Allow in the popup. (Tick "Allow everyone (no popup
   when someone joins)" to skip the popup.)
4. The joining PC goes fullscreen and shows the game. Its keyboard and
   mouse are now its controller.

While playing: F1 shows which key does what, Ctrl+Alt+C changes keys,
F2 shows connection stats, and Ctrl+Alt+Q leaves. On the host, Kick
removes a player and Stop hosting ends the session for everyone.

## 5. Upgrading

Run the new setup on every PC, by hand or with the silent command. It
installs over the old version and closes CouchLink if it's running.
Hosts and joining PCs should run the same version.

## 6. Troubleshooting

The host isn't in the Join list
- Check both PCs are on the same switch or subnet.
- Check the four CouchLink firewall rules exist (Windows Defender
  Firewall > Advanced settings > Inbound Rules). If not, run the setup
  again.
- Use Join by address... and type the host's IP address (run `ipconfig`
  on the host to see it).

The joining player has no controller in the game
- On the host, open Device Manager > System devices and look for "Nefarius
  Virtual Gamepad Emulation Bus". If it's missing, run the CouchLink setup
  again, or install ViGEmBus from
  https://github.com/nefarius/ViGEmBus/releases.
- Restart the game after the driver is installed.

"The host didn't answer." or "Request denied."
- Someone on the host has to click Allow within 30 seconds.

Black screen, stutter or lag
- Press F2 on the joining PC. A high Packet loss or many FEC repairs mean
  the network is the problem: use a cable, not Wi-Fi, and check the
  switch.
- "Lost the host." means the connection dropped for more than 10
  seconds. Pick the host again within a minute to get the same controller
  back.

CouchLink crashed
- It saves a report in `%LOCALAPPDATA%\CouchLink\CrashReports` and shows
  where. Click Crash reports on the Start screen to open the folder.
  Please attach the file to a new issue at
  https://github.com/enriquezchristopher/CouchLink/issues/new/choose.
```

- [ ] **Step 2: Check every quoted label against the app**

```bash
for s in "Join by address..." "Allow everyone (no popup when someone joins)" "Stop hosting" "Crash reports" "The host didn't answer." "Request denied." "Lost the host." "Packet loss" "FEC repairs"; do
  grep -rqF "$s" src --include=*.cs --include=*.xaml && echo "ok   $s" || echo "MISSING $s"
done
```

Expected: every line `ok`. A `MISSING` line means the guide quotes something the app doesn't say: fix the guide to match the app.

- [ ] **Step 3: Update the README**

Replace the status paragraph's last sentence "The installer is next." with "A setup installs it, the controller driver and the firewall rules in one go; see the [café setup guide](docs/cafe-setup-guide.md)." Replace the "## Download" section body with:

```markdown
Get the latest `CouchLink-Setup-vX.Y.Z.exe` from
[Releases](https://github.com/enriquezchristopher/CouchLink/releases) and
run it on every PC. It installs CouchLink, the ViGEmBus driver and the
firewall rules; the [café setup guide](docs/cafe-setup-guide.md) covers
silent installs for many PCs.

`CouchLink-vX.Y.Z-win-x64.zip` is the portable version, for testing: unzip
it anywhere and run `CouchLink.App.exe`. It doesn't install the driver or
the firewall rules.

`PadTest\CouchLink.PadTest.exe check 9` checks, without any game, that this
PC can create 9 separate virtual controllers and that every button, stick and
trigger works on each.
```

In "## Requirements", change the ViGEmBus bullet to "[ViGEmBus driver](https://github.com/nefarius/ViGEmBus/releases) on the host PC (creates the virtual controllers); the setup installs it." In "## Building from source", add after `./eng/get-ffmpeg.ps1`:

```powershell
./eng/get-inno-setup.ps1   # once: the setup compiler, into third_party/innosetup
./eng/get-vigembus.ps1     # once: the ViGEmBus installer the setup bundles
```

and change the package line's comment to `# builds the zip, the setup and the FFmpeg source zip in artifacts/`.

- [ ] **Step 4: Update `ROADMAP.md`**

Change `## v1.3 — Audio (next)` to `## ✅ v1.3 — Audio (released in 1.4.0)` and its "Likely to ship as 1.4.0." line to "Clients hear the game.". Change `## v1.4 — Lobby & sessions` to `## ✅ v1.4 — Lobby & sessions (released in 1.5.0 and 1.6.0)`, and remove "(fullscreen and Ctrl+Alt+Q shipped in 1.3.0)" from #24's line. Change `## v1.5 — Café-ready install` to `## v1.5 — Café-ready install (next)`.

- [ ] **Step 5: Update the main design's section 8**

Replace the body of `## 8. Install & Packaging` with:

```markdown
- `CouchLink-Setup-v<version>.exe` (Inno Setup) installs the self-contained
  .NET 10 app into Program Files, the ViGEmBus driver when missing, and
  Windows Firewall rules for the ports in section 3 (all profiles, local
  subnet only). Silent mode and exit codes for installing on many PCs; the
  uninstaller removes the app and the rules and keeps the driver and the
  user's logs. A portable zip of the same files is kept for testing.
- FFmpeg 9 DLLs (avcodec, avutil, swscale, swresample) in `ffmpeg\` are
  CouchLink's own minimal GPL build (`eng/ffmpeg/build.sh`): H.264 decode
  with D3D11VA and the `h264_amf`, `h264_nvenc` and `libx264` encoders
  only. Every release attaches its complete source.
- Details: [café-ready install design](2026-10-08-couchlink-cafe-install-design.md).
```

- [ ] **Step 6: Append the Plan 9 rows to `docs/gate-results.md`**

```markdown

## Café-ready install (Plan 9)

Date: <fill in>
PCs: <fill in>

| Check | Result |
|---|---|
| CI round trip: silent install, repair, uninstall; four rules with the right ports and scope, then none (checked automatically on every PR) | <pass/fail> |
| A clean PC without ViGEmBus: install by hand, host, a client's pad works in the game | <pass/fail> |
| Upgrade from the previous release while CouchLink is running: it closes, the new version starts, still four rules | <pass/fail> |
| Uninstall: rules and program folder gone; ViGEmBus and `%LOCALAPPDATA%\CouchLink` still there | <pass/fail> |
| Host on an NVIDIA PC: picture on the client, host Details shows h264_nvenc | <pass/fail> |
| Host on an AMD PC: picture on the client, host Details shows h264_amf | <pass/fail> |
| Host on a PC with neither: picture on the client, host Details shows libx264 | <pass/fail> |
| A network Windows marks "Public": the host is listed and joining works | <pass/fail> |
| Silent install from a script on a second PC: exit code 0 | <pass/fail> |
```

- [ ] **Step 7: Commit**

```bash
git add docs/cafe-setup-guide.md README.md ROADMAP.md docs/superpowers/specs/2026-10-05-couchlink-design.md docs/gate-results.md
git commit -m "docs: café setup guide, install in the README and main design, roadmap and the Plan 9 checklist"
```

---

## Self-review notes

- Spec 2.1-2.7 → Tasks 3 and 4; 3.1-3.3 → Tasks 1, 2 (and 3 for `SOURCE.txt`, 4 for attaching the source zip); 4 → Task 5; 5 → Tasks 4 and 5; 6 → Tasks 1-4 checks and Task 5's rows.
- Spec 6 says "Pester"; this plan uses plain PowerShell checks (`verify-package.ps1`) because only Pester 3.4 is on this PC and CI would need another module. Same checks, no new dependency.
- Spec 2.4 says setup detects the running app through the `Local\CouchLink.SingleInstance` mutex; this plan relies on Inno's Restart Manager (`CloseApplications=yes`), which finds CouchLink by its open files, asks before closing it interactively and closes it in silent mode. The uninstaller asks it to close with `taskkill`, then ends it after 3 s.
- Spec 2.4 says "the same or an older version over a newer one is refused"; reinstalling the same version is a repair (spec 2.3 relies on it), so only an older setup over a newer install is refused.
- The two `<sha256 ...>` markers in Task 2 Step 4 are filled from Step 3's output in the same task; they are values, not open design.
