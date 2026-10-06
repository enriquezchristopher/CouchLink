# CouchLink Plan 4: Host Screen Capture and Encoding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The host streams its real screen, captured on the GPU and encoded to H.264 with AMD AMF (NVIDIA NVENC, or x264 as a fallback), at a resolution and frame rate the host picks before hosting, so the Plan 3 stream carries real video instead of a test pattern.

**Architecture:** Pure, unit-tested choices live in `CouchLink.Core/Video`: `StreamSettings` (resolution presets, frame rates, automatic bitrate), `VideoSize`, and `EncoderChoice` (which encoder for which GPU, with the low-latency options). A new Windows-only project, `CouchLink.Video`, holds the GPU glue: `DesktopCapture` (DXGI Desktop Duplication), `Nv12Converter` (BGRA to NV12 and scaling on the GPU's video processor), `H264Encoder` (FFmpeg through FFmpeg.AutoGen, fed D3D11 textures with zero CPU copy), and `ScreenVideoSource`, which turns them into Plan 3's `IEncodedVideoSource`: it paces frames, repeats the last image when the screen is still, marks frames Paused while capture is lost, and falls back to the next encoder when one won't open. `ScreenVideoSource` is unit-tested with fake capture and encoders; the GPU classes are verified on real hardware by a new `CouchLink.VideoTest` console tool, like PadTest. Plan 5 adds decoding and display on the client.

**Tech Stack:** C# / .NET 10, WPF, FFmpeg 9.0 (BtbN GPL shared build: avcodec-63, avutil-61, swscale-10, swresample-7) through FFmpeg.AutoGen 9.0.1.1, Vortice.Direct3D11 / Vortice.DXGI 3.8.3, xUnit, Microsoft.Extensions.TimeProvider.Testing.

**Spec:** `docs/superpowers/specs/2026-10-05-couchlink-design.md` (section 2 Environment, section 5.1 Host video pipeline, section 7 capture-lost row, section 8 Install & Packaging). Task 9 updates section 5.1 for the host stream settings.

**Builds on:** Plan 3 (`main` @ ef41139). Branch: `plan4-host-video`. Issues: #14, #15. New feature (requested 2026-10-06): the host picks the stream resolution and frame rate.

## Spike findings (2026-10-06, this repo's dev PC)

A throwaway spike ran the whole host path before this plan was written. The code below is taken from it.

- The display adapter is an **AMD Radeon RX 6600**, 2560x1080 at 75 Hz. No NVIDIA GPU is visible on this PC, so the NVENC path can't be run here: `h264_nvenc` fails at open with `Function not implemented`. That failure is why encoder selection must fall back on open failure, not only choose by vendor.
- FFmpeg's own `ddagrab` frames are rejected by AMF (`SubmitInput() failed with error 18`), and its `scale_d3d11` filter fails too. CouchLink therefore captures and converts itself.
- On this AMD driver an NV12 **texture array** can't be a render target (`E_INVALIDARG`); a single NV12 texture can. FFmpeg's frame pool must use `initial_pool_size = 0` (one texture per frame) with `BindFlags = D3D11_BIND_RENDER_TARGET`.
- AMF needs `async_depth=1` (its default of 16 queues 16 frames) and `forced_idr=1` (otherwise a forced I-frame is not an IDR keyframe). NVENC's equivalent is `forced-idr=1`.
- `gop_size = 0` makes x264 send every frame as a keyframe; `gop_size = int.MaxValue` gives one keyframe until one is forced, on AMF and x264.
- GPU convert + AMF encode: p50 4.4 ms, p95 5.1 ms at 2560x1080; 3.0 ms at 1706x720. One packet out per frame in, nothing held back. Keyframes ~125 KB.
- x264 fallback (GPU to CPU copy, `sws_scale`): p50 13.7 ms at 2560x1080 and 11 ms at 720p on the i3-8100, with keyframes up to ~430 KB.
- Correct colour needs the video processor set to `RgbFullG22NoneP709` in and `YcbcrStudioG22LeftP709` out, and the stream tagged BT.709 limited range. `sws_scale`'s default matrix is BT.601, so the software path is tagged BT.601.
- Only four FFmpeg DLLs are needed (~120 MB). The output decodes with D3D11VA and shows the real desktop with correct colours.
- When the screen doesn't change, Desktop Duplication returns nothing. Re-encoding the last image gives tiny delta frames (whole stream ~1.3 Mbps at idle), so clients keep getting newer frames. This closes Plan 3's deferred finding that a lost last frame before an idle screen was never noticed.

## Global Constraints

- C#, .NET 10. `CouchLink.Core` stays `net10.0`; GPU and FFmpeg code lives only in `CouchLink.Video` (`net10.0-windows`, `x64`, unsafe allowed). `TreatWarningsAsErrors` everywhere.
- No Claude attribution trailers in commit messages; commits are signed.
- Host stream settings, chosen in the host window **before** hosting:
  - Resolution presets **Native, 1080p (default), 900p, 720p, 540p**: the stream's height is at most the preset, the width follows the host screen's aspect ratio, never scaled up, both sides even.
  - Frame rates **60 (default), 75, 90, 120, 144, 165, 240**, minimum 60, offering only rates up to the host display's refresh rate (60 is always offered).
  - Bitrate automatic: **10 Mbps x (pixels / 1920x1080) x (fps / 60)**, clamped to **2-30 Mbps**.
- Encoders by display adapter vendor: AMD `0x1002` -> `h264_amf`, NVIDIA `0x10DE` -> `h264_nvenc`, anything else -> `libx264`; if the hardware encoder doesn't open, `libx264`. Host warning text, exactly: **"No hardware encoder - may lag with heavy games."**
- Encoder options: AMF `usage=ultralowlatency rc=vbr_latency preanalysis=false async_depth=1 bf=0 forced_idr=1`; NVENC `preset=p1 tune=ull rc=cbr zerolatency=1 forced-idr=1`; x264 `preset=ultrafast tune=zerolatency`. All: no B-frames, `gop_size = int.MaxValue` (keyframes only when forced), low-delay flag.
- FFmpeg 9.0 GPL shared build from BtbN, fetched by `eng/get-ffmpeg.ps1` into `third_party/ffmpeg/` (git-ignored); the app loads it from `<app folder>\ffmpeg\`. It ships in the release zip with FFmpeg's `LICENSE.txt` and a THIRD-PARTY-NOTICES entry (decided 2026-10-06; #28 finishes GPL compliance).
- While capture is lost (UAC prompt, display mode change, exclusive fullscreen), the host keeps sending the last image with the shard header's **Paused** flag set, and sends a keyframe once capture is back.

## Review Focus

1. **Capture is lost mid-stream** (a UAC prompt, the host changes resolution, a game switches to exclusive fullscreen). The stream keeps going with Paused frames, comes back with a keyframe, and reopens the encoder at the new screen size. Pinned in Task 5 (`Lost_capture_sends_paused_frames_and_resumes_with_a_keyframe`, `A_new_screen_size_reopens_the_encoder_with_a_keyframe`) and checked by hand in Task 8.
2. **The screen is still.** The host repeats the last image at the chosen frame rate, so a client that lost the last real frame notices and asks for a keyframe. Pinned in Task 5 (`A_still_screen_repeats_the_last_image_each_frame`).
3. **The hardware encoder won't open** (no NVIDIA GPU, a broken driver, a too-old GPU). The host falls back to x264 and shows the warning; if nothing opens, hosting still runs the pads and says why video is unavailable. Pinned in Task 5 (`Falls_back_to_the_software_encoder`, `No_encoder_at_all_throws_with_every_reason`) and Task 8.
4. **FFmpeg is missing or the wrong version** (dev build without `get-ffmpeg.ps1`, a damaged install). A clear message instead of a crash. Pinned in Task 4 (`Missing_FFmpeg_gives_a_clear_message`).
5. **A frame rate the host can't produce.** Rates above the display's refresh are not offered; the software encoder at high rates shows the warning. Pinned in Task 1 (`Only_rates_up_to_the_display_refresh_are_offered`) and Task 8.

---

## File Structure

```
src/CouchLink.Core/Video/
  VideoSize.cs              stream size from screen size and a height limit
  StreamSettings.cs         resolution presets, frame rates, automatic bitrate
  EncoderChoice.cs          encoder order per GPU vendor, low-latency options, warning text
  IEncodedVideoSource.cs    (modify) EncodedFrame gets Paused
  FramePacketizer.cs        (modify) Paused goes into the shard header
  FrameAssembler.cs         (modify) AssembledFrame gets Paused
  VideoStreamer.cs          (modify) pass Paused on
  VideoClient.cs            (modify) HostPaused in the stats
src/CouchLink.Core/Protocol/
  VideoShardPacket.cs       (modify) Paused flag bit
src/CouchLink.Core/
  DevOptions.cs             --test-pattern, --save-video=<file>
src/CouchLink.Video/        (new project)
  CouchLink.Video.csproj    FFmpeg.AutoGen, Vortice, copies FFmpeg DLLs to ffmpeg\
  FfmpegLibrary.cs          loads FFmpeg 9 from the app folder, error text
  IScreenCapture.cs         CaptureStatus, IScreenCapture, IFrameEncoder
  ScreenVideoSource.cs      pacing, repeats, Paused, keyframes, encoder fallback
  DisplayInfo.cs            display refresh rate
  DesktopCapture.cs         DXGI Desktop Duplication
  Nv12Converter.cs          GPU BGRA -> NV12 + scaling
  H264Encoder.cs            FFmpeg encoder on D3D11 frames, software path
src/CouchLink.VideoTest/    (new) hardware check tool: capture + encode to a file
src/CouchLink.App/
  HostVideo.cs              (new) picks the source, describes it for the window
  HostInputService.cs       (modify) uses HostVideo
  ClientVideoService.cs     (modify) saves the stream, shows Paused
  MainWindow.xaml(.cs)      (modify) resolution + frame rate pickers
tests/CouchLink.Core.Tests/
  StreamSettingsTests.cs, EncoderChoiceTests.cs, DevOptionsTests.cs
  VideoPacketTests.cs, FrameAssemblerTests.cs, FramePacketizerTests.cs (modify)
tests/CouchLink.Video.Tests/ (new project)
  FfmpegLibraryTests.cs, ScreenVideoSourceTests.cs, Fakes.cs
eng/get-ffmpeg.ps1          (new) downloads FFmpeg 9.0 into third_party/ffmpeg
eng/package.ps1, .github/workflows/ci.yml, .github/workflows/release.yml, .gitignore,
THIRD-PARTY-NOTICES.md, README.md, docs/superpowers/specs/2026-10-05-couchlink-design.md (modify)
```

---

### Task 1: Stream settings and stream size

**Files:**
- Create: `src/CouchLink.Core/Video/VideoSize.cs`
- Create: `src/CouchLink.Core/Video/StreamSettings.cs`
- Test: `tests/CouchLink.Core.Tests/StreamSettingsTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public readonly record struct VideoSize(int Width, int Height)` with `static VideoSize ForStream(int screenWidth, int screenHeight, int maxHeight)`.
  - `public enum StreamResolution { Native = 0, P1080 = 1080, P900 = 900, P720 = 720, P540 = 540 }`.
  - `public sealed record StreamSettings` with ctor `(StreamResolution Resolution, int FrameRate)` (throws `ArgumentOutOfRangeException` for a rate not in `CommonFrameRates` or an undefined resolution), `static StreamSettings Default` (1080p, 60), `static IReadOnlyList<int> CommonFrameRates` (60, 75, 90, 120, 144, 165, 240), `static IReadOnlyList<StreamResolution> Resolutions` (Native, 1080p, 900p, 720p, 540p), `static IReadOnlyList<int> FrameRatesFor(int refreshRate)`, `static string Label(StreamResolution resolution)`, `VideoSize SizeFor(int screenWidth, int screenHeight)`, `long BitRateFor(VideoSize size)`, consts `BaseBitRate = 10_000_000`, `MinBitRate = 2_000_000`, `MaxBitRate = 30_000_000`.

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/StreamSettingsTests.cs`:
```csharp
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class StreamSettingsTests
{
    [Theory]
    [InlineData(1920, 1080, 1080, 1920, 1080)]
    [InlineData(2560, 1080, 720, 1706, 720)]   // ultrawide keeps its shape
    [InlineData(3840, 2160, 1080, 1920, 1080)]
    [InlineData(1366, 768, 1080, 1366, 768)]   // never scaled up
    [InlineData(1366, 768, 540, 960, 540)]
    [InlineData(1365, 767, 1080, 1364, 766)]   // H.264 4:2:0 needs even sides
    public void Stream_size_keeps_the_aspect_and_stays_even(int w, int h, int max, int expectedW, int expectedH)
    {
        Assert.Equal(new VideoSize(expectedW, expectedH), VideoSize.ForStream(w, h, max));
    }

    [Fact]
    public void Native_uses_the_screen_size()
    {
        var settings = new StreamSettings(StreamResolution.Native, 60);
        Assert.Equal(new VideoSize(2560, 1080), settings.SizeFor(2560, 1080));
    }

    [Fact]
    public void Default_is_1080p_at_60()
    {
        Assert.Equal(new StreamSettings(StreamResolution.P1080, 60), StreamSettings.Default);
        Assert.Equal("1080p", StreamSettings.Label(StreamResolution.P1080));
        Assert.Equal("Native", StreamSettings.Label(StreamResolution.Native));
        Assert.Equal(
            [StreamResolution.Native, StreamResolution.P1080, StreamResolution.P900, StreamResolution.P720, StreamResolution.P540],
            StreamSettings.Resolutions);
    }

    [Theory]
    [InlineData(60, new[] { 60 })]
    [InlineData(75, new[] { 60, 75 })]
    [InlineData(144, new[] { 60, 75, 90, 120, 144 })]
    [InlineData(240, new[] { 60, 75, 90, 120, 144, 165, 240 })]
    [InlineData(30, new[] { 60 })]   // 60 is always offered
    public void Only_rates_up_to_the_display_refresh_are_offered(int refresh, int[] expected)
    {
        Assert.Equal(expected, StreamSettings.FrameRatesFor(refresh));
    }

    [Theory]
    [InlineData(StreamResolution.P1080, 60, 1920, 1080, 10_000_000)]
    [InlineData(StreamResolution.P720, 60, 1920, 1080, 4_444_444)]
    [InlineData(StreamResolution.P1080, 144, 1920, 1080, 24_000_000)]
    [InlineData(StreamResolution.P1080, 60, 2560, 1080, 13_333_333)]
    [InlineData(StreamResolution.Native, 240, 2560, 1080, 30_000_000)]  // capped
    [InlineData(StreamResolution.P540, 60, 1920, 1080, 2_500_000)]
    public void Bitrate_follows_size_and_frame_rate(StreamResolution resolution, int fps, int w, int h, long expected)
    {
        var settings = new StreamSettings(resolution, fps);
        Assert.Equal(expected, settings.BitRateFor(settings.SizeFor(w, h)));
    }

    [Fact]
    public void Bitrate_never_goes_below_the_minimum()
    {
        var settings = new StreamSettings(StreamResolution.P540, 60);
        Assert.Equal(StreamSettings.MinBitRate, settings.BitRateFor(new VideoSize(320, 240)));
    }

    [Theory]
    [InlineData(30)]
    [InlineData(59)]
    [InlineData(100)]
    public void Uncommon_frame_rates_are_rejected(int fps)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamSettings(StreamResolution.P1080, fps));
    }

    [Fact]
    public void Undefined_resolutions_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamSettings((StreamResolution)1000, 60));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~StreamSettingsTests`
Expected: build FAILS: `VideoSize`, `StreamSettings`, `StreamResolution` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Video/VideoSize.cs`:
```csharp
namespace CouchLink.Core.Video;

/// <summary>A video frame size in pixels.</summary>
public readonly record struct VideoSize(int Width, int Height)
{
    /// <summary>
    /// Scales a screen size down to at most <paramref name="maxHeight"/> lines, keeping the
    /// aspect ratio, never scaling up. Both sides are even, as H.264 4:2:0 needs.
    /// </summary>
    public static VideoSize ForStream(int screenWidth, int screenHeight, int maxHeight)
    {
        if (screenWidth < 2 || screenHeight < 2)
            throw new ArgumentOutOfRangeException(nameof(screenWidth), "Screen must be at least 2x2.");
        if (maxHeight < 2)
            throw new ArgumentOutOfRangeException(nameof(maxHeight), "Height limit must be at least 2.");

        int height = Math.Min(screenHeight, maxHeight);
        int width = (int)Math.Round(screenWidth * (double)height / screenHeight);
        return new VideoSize(width & ~1, height & ~1);
    }
}
```

`src/CouchLink.Core/Video/StreamSettings.cs`:
```csharp
namespace CouchLink.Core.Video;

/// <summary>Stream resolution presets, by the stream's maximum height.</summary>
public enum StreamResolution
{
    Native = 0,
    P1080 = 1080,
    P900 = 900,
    P720 = 720,
    P540 = 540,
}

/// <summary>
/// What the host streams: a resolution preset and a frame rate, chosen before hosting.
/// The bitrate follows from them: 10 Mbps at 1080p60, scaled by pixels and frame rate.
/// </summary>
public sealed record StreamSettings
{
    public const long BaseBitRate = 10_000_000;
    public const long MinBitRate = 2_000_000;
    public const long MaxBitRate = 30_000_000;

    public static IReadOnlyList<int> CommonFrameRates { get; } = [60, 75, 90, 120, 144, 165, 240];

    public static IReadOnlyList<StreamResolution> Resolutions { get; } =
        [StreamResolution.Native, StreamResolution.P1080, StreamResolution.P900, StreamResolution.P720, StreamResolution.P540];

    public static StreamSettings Default { get; } = new(StreamResolution.P1080, 60);

    public StreamSettings(StreamResolution resolution, int frameRate)
    {
        if (!Resolutions.Contains(resolution))
            throw new ArgumentOutOfRangeException(nameof(resolution), $"Unknown resolution {resolution}.");
        if (!CommonFrameRates.Contains(frameRate))
            throw new ArgumentOutOfRangeException(nameof(frameRate), $"Frame rate must be one of {string.Join(", ", CommonFrameRates)}.");
        Resolution = resolution;
        FrameRate = frameRate;
    }

    public StreamResolution Resolution { get; }
    public int FrameRate { get; }

    /// <summary>Rates the host can pick: up to the display's refresh rate, and always 60.</summary>
    public static IReadOnlyList<int> FrameRatesFor(int refreshRate) =>
        CommonFrameRates.Where(rate => rate == 60 || rate <= refreshRate).ToList();

    public static string Label(StreamResolution resolution) =>
        resolution == StreamResolution.Native ? "Native" : $"{(int)resolution}p";

    public VideoSize SizeFor(int screenWidth, int screenHeight) =>
        VideoSize.ForStream(screenWidth, screenHeight,
            Resolution == StreamResolution.Native ? int.MaxValue : (int)Resolution);

    public long BitRateFor(VideoSize size)
    {
        double pixels = (double)size.Width * size.Height / (1920 * 1080);
        long bitRate = (long)Math.Round(BaseBitRate * pixels * FrameRate / 60.0);
        return Math.Clamp(bitRate, MinBitRate, MaxBitRate);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~StreamSettingsTests`
Expected: PASS (24 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Core/Video/VideoSize.cs src/CouchLink.Core/Video/StreamSettings.cs tests/CouchLink.Core.Tests/StreamSettingsTests.cs
git commit -m "feat(core): host stream settings for resolution and frame rate" -m "Refs #15"
```

---

### Task 2: Encoder choice

**Files:**
- Create: `src/CouchLink.Core/Video/EncoderChoice.cs`
- Test: `tests/CouchLink.Core.Tests/EncoderChoiceTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static class EncoderChoice` with consts `AmdVendorId = 0x1002`, `NvidiaVendorId = 0x10DE`, `Amf = "h264_amf"`, `Nvenc = "h264_nvenc"`, `Software = "libx264"`, `SoftwareWarning = "No hardware encoder - may lag with heavy games."`; `IReadOnlyList<string> Candidates(uint vendorId)`; `bool IsHardware(string encoder)`; `IReadOnlyList<(string Name, string Value)> Options(string encoder)`.

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/EncoderChoiceTests.cs`:
```csharp
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class EncoderChoiceTests
{
    [Fact]
    public void Amd_tries_AMF_then_x264()
    {
        Assert.Equal(["h264_amf", "libx264"], EncoderChoice.Candidates(0x1002));
    }

    [Fact]
    public void Nvidia_tries_NVENC_then_x264()
    {
        Assert.Equal(["h264_nvenc", "libx264"], EncoderChoice.Candidates(0x10DE));
    }

    [Theory]
    [InlineData(0x8086u)] // Intel: out of scope (spec section 1)
    [InlineData(0x1414u)] // Microsoft Basic Display Adapter
    public void Other_GPUs_use_x264(uint vendor)
    {
        Assert.Equal(["libx264"], EncoderChoice.Candidates(vendor));
    }

    [Fact]
    public void Only_x264_is_software()
    {
        Assert.True(EncoderChoice.IsHardware("h264_amf"));
        Assert.True(EncoderChoice.IsHardware("h264_nvenc"));
        Assert.False(EncoderChoice.IsHardware("libx264"));
    }

    [Fact]
    public void AMF_uses_ultra_low_latency_with_one_frame_in_flight_and_real_IDR_keyframes()
    {
        Assert.Equal(
            [("usage", "ultralowlatency"), ("rc", "vbr_latency"), ("preanalysis", "false"),
             ("async_depth", "1"), ("bf", "0"), ("forced_idr", "1")],
            EncoderChoice.Options("h264_amf"));
    }

    [Fact]
    public void NVENC_uses_p1_ull_CBR_and_real_IDR_keyframes()
    {
        Assert.Equal(
            [("preset", "p1"), ("tune", "ull"), ("rc", "cbr"), ("zerolatency", "1"), ("forced-idr", "1")],
            EncoderChoice.Options("h264_nvenc"));
    }

    [Fact]
    public void X264_uses_ultrafast_zerolatency()
    {
        Assert.Equal([("preset", "ultrafast"), ("tune", "zerolatency")], EncoderChoice.Options("libx264"));
    }

    [Fact]
    public void Unknown_encoders_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => EncoderChoice.Options("h264_qsv"));
    }

    [Fact]
    public void Warning_text_matches_the_spec()
    {
        Assert.Equal("No hardware encoder - may lag with heavy games.", EncoderChoice.SoftwareWarning);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~EncoderChoiceTests`
Expected: build FAILS: `EncoderChoice` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Video/EncoderChoice.cs`:
```csharp
namespace CouchLink.Core.Video;

/// <summary>
/// Which H.264 encoder the host uses, and how. Picked by the display adapter's vendor; the
/// software encoder is always the last resort, because a hardware encoder can fail to open
/// (no such GPU, an old driver). Options are the spec's low-latency settings plus what the
/// Plan 4 spike found: AMF queues 16 frames unless async_depth=1, and both hardware encoders
/// need forced IDR for a forced I-frame to be a real keyframe.
/// </summary>
public static class EncoderChoice
{
    public const uint AmdVendorId = 0x1002;
    public const uint NvidiaVendorId = 0x10DE;

    public const string Amf = "h264_amf";
    public const string Nvenc = "h264_nvenc";
    public const string Software = "libx264";

    public const string SoftwareWarning = "No hardware encoder - may lag with heavy games.";

    public static IReadOnlyList<string> Candidates(uint vendorId) => vendorId switch
    {
        AmdVendorId => [Amf, Software],
        NvidiaVendorId => [Nvenc, Software],
        _ => [Software],
    };

    public static bool IsHardware(string encoder) => encoder != Software;

    public static IReadOnlyList<(string Name, string Value)> Options(string encoder) => encoder switch
    {
        Amf =>
        [
            ("usage", "ultralowlatency"), ("rc", "vbr_latency"), ("preanalysis", "false"),
            ("async_depth", "1"), ("bf", "0"), ("forced_idr", "1"),
        ],
        Nvenc => [("preset", "p1"), ("tune", "ull"), ("rc", "cbr"), ("zerolatency", "1"), ("forced-idr", "1")],
        Software => [("preset", "ultrafast"), ("tune", "zerolatency")],
        _ => throw new ArgumentException($"Unknown encoder {encoder}.", nameof(encoder)),
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~EncoderChoiceTests`
Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Core/Video/EncoderChoice.cs tests/CouchLink.Core.Tests/EncoderChoiceTests.cs
git commit -m "feat(core): choose the H.264 encoder by GPU vendor with low-latency options" -m "Refs #15"
```

---

### Task 3: Paused flag through the stream

**Files:**
- Modify: `src/CouchLink.Core/Protocol/VideoShardPacket.cs`
- Modify: `src/CouchLink.Core/Video/IEncodedVideoSource.cs`
- Modify: `src/CouchLink.Core/Video/FramePacketizer.cs`
- Modify: `src/CouchLink.Core/Video/FrameAssembler.cs`
- Modify: `src/CouchLink.Core/Video/VideoStreamer.cs`
- Modify: `src/CouchLink.Core/Video/VideoClient.cs`
- Test: `tests/CouchLink.Core.Tests/VideoPacketTests.cs`, `tests/CouchLink.Core.Tests/FramePacketizerTests.cs`, `tests/CouchLink.Core.Tests/FrameAssemblerTests.cs` (add tests)

**Interfaces:**
- Consumes: Plan 3's protocol and video types.
- Produces:
  - `VideoShardHeader` gains a last positional parameter `bool Paused = false` (flag bit `0x02` in byte 4).
  - `EncodedFrame(ReadOnlyMemory<byte> Data, bool Keyframe, bool Paused = false)`.
  - `FramePacketizer.Packetize(uint frameNumber, ReadOnlySpan<byte> frame, bool keyframe, bool paused = false)`.
  - `AssembledFrame(uint Number, bool Keyframe, byte[] Data, bool Paused = false)`; packets of one frame must agree on Paused.
  - `VideoClientStats` gains a last member `bool HostPaused` (Paused flag of the last assembled frame).

- [ ] **Step 1: Write the failing tests**

Add to `tests/CouchLink.Core.Tests/VideoPacketTests.cs`:
```csharp
    [Fact]
    public void Paused_flag_round_trips_separately_from_keyframe()
    {
        foreach (var (keyframe, paused) in new[] { (false, true), (true, true), (true, false) })
        {
            var header = Sample with { Keyframe = keyframe, Paused = paused };
            Assert.True(VideoShardPacket.TryParse(Packet(header), out var parsed));
            Assert.Equal(header, parsed);
        }
    }
```

Add to `tests/CouchLink.Core.Tests/FramePacketizerTests.cs`:
```csharp
    [Fact]
    public void Paused_frames_are_marked_in_every_packet()
    {
        var headers = Headers(new FramePacketizer(1).Packetize(3, RandomFrame(5_000), keyframe: false, paused: true));
        Assert.All(headers, h => Assert.True(h.Paused));
        Assert.All(Headers(new FramePacketizer(1).Packetize(4, RandomFrame(5_000), false)), h => Assert.False(h.Paused));
    }
```

Add to `tests/CouchLink.Core.Tests/FrameAssemblerTests.cs`:
```csharp
    [Fact]
    public void Paused_frames_arrive_marked_and_packets_must_agree()
    {
        var frame = new byte[5_000];
        var packets = new FramePacketizer(1).Packetize(1, frame, keyframe: false, paused: true);
        var a = new FrameAssembler();

        Feed(a, packets.Take(1));
        Assert.Empty(Feed(a, [Rewrite(packets[1], h => h with { Paused = false })])); // disagrees: ignored
        Assert.True(Assert.Single(Feed(a, packets.Skip(1))).Paused);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~VideoPacketTests|FullyQualifiedName~FramePacketizerTests|FullyQualifiedName~FrameAssemblerTests"`
Expected: build FAILS: `VideoShardHeader` has no `Paused`; `Packetize` has no `paused` parameter; `AssembledFrame` has no `Paused`.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Protocol/VideoShardPacket.cs`:
- Add `bool Paused = false` as the **last** parameter of the `VideoShardHeader` record, documented: `Paused: the host's capture is lost (UAC prompt, mode change); this frame repeats the last image.`
- Next to `FlagKeyframe`, add `private const byte FlagPaused = 2;`
- In `WriteHeader`, replace the flags line with:
```csharp
        packet[4] = (byte)((header.Keyframe ? FlagKeyframe : 0) | (header.Paused ? FlagPaused : 0));
```
- In `TryParse`, add to the `new VideoShardHeader(...)` call:
```csharp
            ParityShards: packet[9],
            Paused: (packet[4] & FlagPaused) != 0);
```

`src/CouchLink.Core/Video/IEncodedVideoSource.cs`: replace the record with
```csharp
/// <summary>One encoded frame (an H.264 access unit). Paused: the host's capture is lost and this repeats the last image.</summary>
public readonly record struct EncodedFrame(ReadOnlyMemory<byte> Data, bool Keyframe, bool Paused = false);
```

`src/CouchLink.Core/Video/FramePacketizer.cs`: change the signature to
```csharp
    public List<byte[]> Packetize(uint frameNumber, ReadOnlySpan<byte> frame, bool keyframe, bool paused = false)
```
and the header construction to
```csharp
                VideoShardPacket.WriteHeader(packet, new VideoShardHeader(
                    StreamId, frameNumber, keyframe, (uint)frame.Length,
                    (byte)block, (byte)blockCount, (byte)s, (byte)k, (byte)m, paused));
```

`src/CouchLink.Core/Video/FrameAssembler.cs`:
- Replace the record: `public sealed record AssembledFrame(uint Number, bool Keyframe, byte[] Data, bool Paused = false);`
- In `PendingFrame`, add `public bool Paused { get; } = first.Paused;`
- In `Add`, extend the consistency check to `h.Keyframe != frame.Keyframe || h.Paused != frame.Paused || h.FrameLength != frame.Length || h.BlockCount != frame.Blocks.Length`.
- In `Finish`, return `new AssembledFrame(frame.Number, frame.Keyframe, data, frame.Paused);`

`src/CouchLink.Core/Video/VideoStreamer.cs`: in `StreamOneFrame`, pass the flag on:
```csharp
        var packets = _packetizer.Packetize(_frameNumber++, frame.Data.Span, frame.Keyframe, frame.Paused);
```

`src/CouchLink.Core/Video/VideoClient.cs`:
- Add `bool HostPaused` as the last member of `VideoClientStats`.
- Add a field `private bool _hostPaused;`
- In `OnDatagram`, inside the lock, right after `frame = _assembler.Add(datagram, Now);` add `if (frame is not null) _hostPaused = frame.Paused;` (before the gate check, so a skipped frame still updates it).
- In `Stats`, pass `_hostPaused` as the last argument.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build` then `dotnet test`
Expected: 0 warnings; all tests PASS (3 new).

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Core tests/CouchLink.Core.Tests
git commit -m "feat(core): mark frames sent while the host's capture is lost" -m "Refs #14"
```

---

### Task 4: CouchLink.Video project and FFmpeg

**Files:**
- Create: `eng/get-ffmpeg.ps1`
- Modify: `.gitignore` (add `third_party/`)
- Create: `src/CouchLink.Video/CouchLink.Video.csproj`
- Create: `src/CouchLink.Video/FfmpegLibrary.cs`
- Create: `tests/CouchLink.Video.Tests/CouchLink.Video.Tests.csproj`
- Test: `tests/CouchLink.Video.Tests/FfmpegLibraryTests.cs`
- Modify: `CouchLink.slnx`

**Interfaces:**
- Consumes: `CouchLink.Core`.
- Produces: `public static class FfmpegLibrary` with `const string AvcodecDll = "avcodec-63.dll"`, `const int AvcodecMajor = 63`, `static string DefaultDirectory` (`<app>\ffmpeg`), `static bool TryLoad(out string? error, string? directory = null)`, `static string ErrorText(int code)`, `static void Check(int result, string what)` (throws `FfmpegException`); `public sealed class FfmpegException(string message) : Exception(message)`. FFmpeg's DLLs and `LICENSE.txt` are copied to `ffmpeg\` in the output of every project that references `CouchLink.Video`.

- [ ] **Step 1: Fetch FFmpeg**

`eng/get-ffmpeg.ps1`:
```powershell
<#
.SYNOPSIS
    Downloads the FFmpeg 9.0 GPL shared build that host video uses into third_party/ffmpeg.
.DESCRIPTION
    BtbN's win64 GPL shared build of the FFmpeg 9.0 branch (h264_amf, h264_nvenc, libx264).
    The app ships avcodec, avutil, swscale and swresample from bin/, plus LICENSE.txt;
    ffprobe/ffplay stay here for development checks. Run once after cloning, and in CI.
.EXAMPLE
    ./eng/get-ffmpeg.ps1
#>
param([switch]$Force)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue' # Invoke-WebRequest is very slow with the progress bar
$root = Split-Path $PSScriptRoot -Parent
$dest = Join-Path $root 'third_party/ffmpeg'
$url = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n9.0-latest-win64-gpl-shared-9.0.zip'

if ((Test-Path (Join-Path $dest 'bin/avcodec-63.dll')) -and -not $Force) {
    Write-Host "FFmpeg is already in $dest (use -Force to download again)."
    return
}

$zip = Join-Path ([IO.Path]::GetTempPath()) "couchlink-ffmpeg-$([guid]::NewGuid()).zip"
$unpacked = "$zip-files"
try {
    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $zip
    Expand-Archive $zip $unpacked
    $top = Get-ChildItem $unpacked -Directory | Select-Object -First 1
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $top.FullName 'bin') $dest -Recurse
    Copy-Item (Join-Path $top.FullName 'LICENSE.txt') $dest
} finally {
    Remove-Item $zip, $unpacked -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path (Join-Path $dest 'bin/avcodec-63.dll'))) {
    throw 'The download has no avcodec-63.dll, so it is not an FFmpeg 9.x build.'
}
Write-Host (& (Join-Path $dest 'bin/ffmpeg.exe') -hide_banner -version | Select-Object -First 1)
```

Add a line `third_party/` to `.gitignore`.

Run: `./eng/get-ffmpeg.ps1`
Expected: prints `ffmpeg version n9.0...`; `third_party/ffmpeg/bin/avcodec-63.dll` and `third_party/ffmpeg/LICENSE.txt` exist; `git status` shows only `.gitignore` and `eng/get-ffmpeg.ps1`.

- [ ] **Step 2: Create the projects and write the failing test**

`src/CouchLink.Video/CouchLink.Video.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\CouchLink.Core\CouchLink.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="FFmpeg.AutoGen" Version="9.0.1.1" />
    <PackageReference Include="Vortice.Direct3D11" Version="3.8.3" />
    <PackageReference Include="Vortice.DXGI" Version="3.8.3" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <!-- FFmpeg 9 from eng/get-ffmpeg.ps1, copied to ffmpeg\ next to every app that uses this library. -->
  <PropertyGroup>
    <FfmpegDir>$(MSBuildThisFileDirectory)..\..\third_party\ffmpeg\</FfmpegDir>
  </PropertyGroup>
  <ItemGroup Condition="Exists('$(FfmpegDir)bin\avcodec-63.dll')">
    <FfmpegFile Include="$(FfmpegDir)bin\avcodec-63.dll;$(FfmpegDir)bin\avutil-61.dll;$(FfmpegDir)bin\swscale-10.dll;$(FfmpegDir)bin\swresample-7.dll;$(FfmpegDir)LICENSE.txt" />
    <None Include="@(FfmpegFile)" Link="ffmpeg\%(Filename)%(Extension)"
          CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>

</Project>
```

`tests/CouchLink.Video.Tests/CouchLink.Video.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.4" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CouchLink.Video\CouchLink.Video.csproj" />
  </ItemGroup>

</Project>
```

In `CouchLink.slnx`, add `<Project Path="src/CouchLink.Video/CouchLink.Video.csproj" />` to the `/src/` folder and `<Project Path="tests/CouchLink.Video.Tests/CouchLink.Video.Tests.csproj" />` to the `/tests/` folder.

`tests/CouchLink.Video.Tests/FfmpegLibraryTests.cs`:
```csharp
using CouchLink.Video;
using FFmpeg.AutoGen;

namespace CouchLink.Video.Tests;

public class FfmpegLibraryTests
{
    [Fact]
    public void Missing_FFmpeg_gives_a_clear_message()
    {
        var empty = Directory.CreateTempSubdirectory("couchlink-no-ffmpeg").FullName;
        try
        {
            Assert.False(FfmpegLibrary.TryLoad(out var error, empty));
            Assert.Contains("FFmpeg 9", error);
            Assert.Contains("get-ffmpeg.ps1", error);
        }
        finally
        {
            Directory.Delete(empty);
        }
    }

    [Fact]
    public void FFmpeg_9_loads_from_the_app_folder_and_has_the_encoders()
    {
        Assert.True(FfmpegLibrary.TryLoad(out var error), error);
        Assert.Equal(FfmpegLibrary.AvcodecMajor, (int)(ffmpeg.avcodec_version() >> 16));
        foreach (var name in new[] { "h264_amf", "h264_nvenc", "libx264" })
            Assert.True(FfmpegLibrary.HasEncoder(name), name);
    }

    [Fact]
    public void Errors_are_readable()
    {
        Assert.True(FfmpegLibrary.TryLoad(out var error), error);
        var e = Assert.Throws<FfmpegException>(() => FfmpegLibrary.Check(ffmpeg.AVERROR(40) /* ENOSYS */, "Opening h264_nvenc"));
        Assert.Equal("Opening h264_nvenc failed: Function not implemented", e.Message);
    }
}
```

Interfaces addition: `FfmpegLibrary.HasEncoder(string name)` (true when `avcodec_find_encoder_by_name` finds it).

Run: `dotnet test tests/CouchLink.Video.Tests`
Expected: build FAILS: `FfmpegLibrary`, `FfmpegException` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Video/FfmpegLibrary.cs`:
```csharp
using System.Runtime.InteropServices;
using FFmpeg.AutoGen;

namespace CouchLink.Video;

/// <summary>An FFmpeg call failed; the message says which and why.</summary>
public sealed class FfmpegException(string message) : Exception(message);

/// <summary>
/// Loads FFmpeg 9 (avcodec-63 and friends) from the app's <c>ffmpeg</c> folder, put there
/// from <c>third_party/ffmpeg</c> by the build (see eng/get-ffmpeg.ps1).
/// </summary>
public static unsafe class FfmpegLibrary
{
    public const string AvcodecDll = "avcodec-63.dll";
    public const int AvcodecMajor = 63;

    private static readonly Lock Gate = new();
    private static bool _loaded;

    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "ffmpeg");

    public static bool TryLoad(out string? error, string? directory = null)
    {
        lock (Gate)
        {
            error = null;
            directory ??= DefaultDirectory;
            if (!File.Exists(Path.Combine(directory, AvcodecDll)))
            {
                error = $"FFmpeg 9 was not found in {directory}. Run eng/get-ffmpeg.ps1 and build again.";
                return false;
            }
            if (_loaded)
                return true; // FFmpeg can only be loaded once per process
            try
            {
                ffmpeg.RootPath = directory;
                DynamicallyLoadedBindings.Initialize();
                int major = (int)(ffmpeg.avcodec_version() >> 16);
                if (major != AvcodecMajor)
                {
                    error = $"FFmpeg in {directory} is avcodec {major}; CouchLink needs {AvcodecMajor} (FFmpeg 9).";
                    return false;
                }
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                error = $"FFmpeg in {directory} could not be loaded: {e.Message}";
                return false;
            }
            _loaded = true;
            return true;
        }
    }

    public static bool HasEncoder(string name) => ffmpeg.avcodec_find_encoder_by_name(name) != null;

    public static string ErrorText(int code)
    {
        const int size = 256;
        byte* buffer = stackalloc byte[size];
        ffmpeg.av_strerror(code, buffer, size);
        return Marshal.PtrToStringUTF8((nint)buffer) ?? $"error {code}";
    }

    public static void Check(int result, string what)
    {
        if (result < 0)
            throw new FfmpegException($"{what} failed: {ErrorText(result)}");
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build` then `dotnet test tests/CouchLink.Video.Tests`
Expected: 0 warnings; PASS (3 tests). `src/CouchLink.Video/bin/Debug/net10.0-windows/ffmpeg/` holds the four DLLs and `LICENSE.txt`.

- [ ] **Step 5: Commit**

```powershell
git add .gitignore eng/get-ffmpeg.ps1 src/CouchLink.Video tests/CouchLink.Video.Tests CouchLink.slnx
git commit -m "build: CouchLink.Video project with FFmpeg 9 loading" -m "Refs #15"
```

---

### Task 5: Screen video source (pacing, repeats, pauses, fallback)

**Files:**
- Create: `src/CouchLink.Video/IScreenCapture.cs`
- Create: `src/CouchLink.Video/ScreenVideoSource.cs`
- Create: `tests/CouchLink.Video.Tests/Fakes.cs`
- Test: `tests/CouchLink.Video.Tests/ScreenVideoSourceTests.cs`

**Interfaces:**
- Consumes: `IEncodedVideoSource`, `EncodedFrame` (Plan 3, Task 3 adds Paused); `StreamSettings`, `VideoSize` (Task 1); `EncoderChoice` (Task 2).
- Produces:
  - `public enum CaptureStatus { NewFrame, NoChange, Lost }`.
  - `public interface IScreenCapture : IDisposable` with `int Width`, `int Height`, `CaptureStatus TryCapture(TimeSpan timeout)` (waits up to timeout for a screen change; returns `Lost` at once when capture is unavailable).
  - `public interface IFrameEncoder : IDisposable` with `string Name`, `bool IsHardware`, `VideoSize Size`, `bool Encode(bool forceKeyframe, out EncodedFrame frame)` (encodes the capture's current image; false if no packet came out).
  - `public sealed class ScreenVideoSource : IEncodedVideoSource`, ctor `(IScreenCapture capture, IReadOnlyList<string> encoderNames, Func<string, VideoSize, IFrameEncoder> openEncoder, StreamSettings settings, TimeProvider time, Action<TimeSpan>? sleep = null)` (owns capture and encoder; throws `InvalidOperationException` if no encoder opens), `string EncoderName`, `bool IsHardware`, `VideoSize Size`, `bool Paused`, `IReadOnlyList<string> SkippedEncoders`.

Behavior: a changed screen is encoded at once, but at most `FrameRate` times a second; with no change the last image is re-encoded once per frame interval; `Lost` sends the last image marked Paused at the frame rate; the first frame after a loss and any frame after the screen size changed (encoder reopened at the new size) is a forced keyframe.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Video.Tests/Fakes.cs`:
```csharp
using CouchLink.Core.Video;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Video.Tests;

/// <summary>Plays back scripted capture results; NoChange waits the whole timeout, NewFrame and Lost return at once.</summary>
internal sealed class FakeCapture(FakeTimeProvider time) : IScreenCapture
{
    public Queue<CaptureStatus> Script { get; } = new();
    public List<TimeSpan> Waits { get; } = [];
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public bool Disposed { get; private set; }

    public CaptureStatus TryCapture(TimeSpan timeout)
    {
        Waits.Add(timeout);
        var status = Script.Count > 0 ? Script.Dequeue() : CaptureStatus.NoChange;
        if (status == CaptureStatus.NoChange)
            time.Advance(timeout);
        return status;
    }

    public void Dispose() => Disposed = true;
}

/// <summary>Records what it was asked to do; its first frame is a keyframe, like a real encoder's.</summary>
internal sealed class FakeEncoder(string name, VideoSize size) : IFrameEncoder
{
    private bool _first = true;

    public string Name { get; } = name;
    public bool IsHardware { get; } = EncoderChoice.IsHardware(name);
    public VideoSize Size { get; } = size;
    public List<bool> ForcedKeyframes { get; } = [];
    public bool ProducePackets { get; set; } = true;
    public bool Disposed { get; private set; }

    public bool Encode(bool forceKeyframe, out EncodedFrame frame)
    {
        ForcedKeyframes.Add(forceKeyframe);
        bool keyframe = forceKeyframe || _first;
        _first = false;
        frame = new EncodedFrame(new byte[] { 1, 2, 3 }, keyframe);
        return ProducePackets;
    }

    public void Dispose() => Disposed = true;
}
```

`tests/CouchLink.Video.Tests/ScreenVideoSourceTests.cs`:
```csharp
using CouchLink.Core.Video;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Video.Tests;

public class ScreenVideoSourceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan Interval60 = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private readonly FakeTimeProvider _time = new();
    private readonly FakeCapture _capture;
    private readonly List<FakeEncoder> _opened = [];
    private readonly DateTimeOffset _start;

    public ScreenVideoSourceTests()
    {
        _capture = new FakeCapture(_time);
        _start = _time.GetUtcNow();
    }

    private TimeSpan Elapsed => _time.GetUtcNow() - _start;

    private ScreenVideoSource Source(StreamSettings? settings = null, params string[] failing) =>
        new(_capture, [EncoderChoice.Amf, EncoderChoice.Software],
            (name, size) =>
            {
                if (failing.Contains(name))
                    throw new InvalidOperationException($"{name} is not available");
                var encoder = new FakeEncoder(name, size);
                _opened.Add(encoder);
                return encoder;
            },
            settings ?? StreamSettings.Default, _time, t => _time.Advance(t));

    private EncodedFrame Next(ScreenVideoSource source, bool force = false)
    {
        Assert.True(source.TryGetFrame(force, Timeout, out var frame));
        return frame;
    }

    [Fact]
    public void A_changed_screen_is_sent_at_once()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();

        var frame = Next(source);

        Assert.True(frame.Keyframe);
        Assert.False(frame.Paused);
        Assert.Equal(TimeSpan.Zero, Elapsed);
    }

    [Fact]
    public void Changes_faster_than_the_frame_rate_wait_for_the_next_slot()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();

        Next(source);
        Next(source);

        Assert.Equal(Interval60, Elapsed);
    }

    [Fact]
    public void A_still_screen_repeats_the_last_image_each_frame()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();

        Next(source);
        var repeat = Next(source); // script empty: no change

        Assert.Equal(Interval60, Elapsed);
        Assert.False(repeat.Keyframe);
        Assert.Equal(2, _opened[0].ForcedKeyframes.Count);
    }

    [Fact]
    public void The_chosen_frame_rate_sets_the_frame_interval()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source(new StreamSettings(StreamResolution.P1080, 120));

        Next(source);
        Next(source);

        Assert.Equal(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 120), Elapsed);
    }

    [Fact]
    public void Nothing_is_returned_when_no_frame_is_due_within_the_timeout()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();
        Next(source);

        Assert.False(source.TryGetFrame(false, TimeSpan.FromMilliseconds(5), out _));
        Assert.Equal(TimeSpan.FromMilliseconds(5), Elapsed);
    }

    [Fact]
    public void Lost_capture_sends_paused_frames_and_resumes_with_a_keyframe()
    {
        foreach (var status in new[] { CaptureStatus.NewFrame, CaptureStatus.Lost, CaptureStatus.Lost, CaptureStatus.NewFrame })
            _capture.Script.Enqueue(status);
        using var source = Source();

        var frames = Enumerable.Range(0, 4).Select(_ => Next(source)).ToList();

        Assert.Equal([false, true, true, false], frames.Select(f => f.Paused));
        Assert.True(frames[3].Keyframe, "the first frame after a loss is a keyframe");
        Assert.Equal(3 * Interval60, Elapsed); // paused frames still keep the frame rate
        Assert.False(source.Paused);
    }

    [Fact]
    public void A_new_screen_size_reopens_the_encoder_with_a_keyframe()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source(new StreamSettings(StreamResolution.P720, 60));
        Next(source);

        _capture.Width = 2560;
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        var frame = Next(source);

        Assert.Equal(2, _opened.Count);
        Assert.True(_opened[0].Disposed);
        Assert.Equal(new VideoSize(1706, 720), _opened[1].Size);
        Assert.Equal(new VideoSize(1706, 720), source.Size);
        Assert.True(frame.Keyframe);
    }

    [Fact]
    public void The_stream_size_comes_from_the_settings()
    {
        _capture.Width = 2560;
        using var source = Source(new StreamSettings(StreamResolution.P720, 60));
        Assert.Equal(new VideoSize(1706, 720), source.Size);
    }

    [Fact]
    public void Falls_back_to_the_software_encoder()
    {
        using var source = Source(null, EncoderChoice.Amf);

        Assert.Equal(EncoderChoice.Software, source.EncoderName);
        Assert.False(source.IsHardware);
        Assert.Equal(["h264_amf: h264_amf is not available"], source.SkippedEncoders);
    }

    [Fact]
    public void No_encoder_at_all_throws_with_every_reason()
    {
        var e = Assert.Throws<InvalidOperationException>(() => Source(null, EncoderChoice.Amf, EncoderChoice.Software));
        Assert.Contains("h264_amf is not available", e.Message);
        Assert.Contains("libx264 is not available", e.Message);
    }

    [Fact]
    public void A_requested_keyframe_is_forced()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();
        Next(source, force: true);
        Assert.True(_opened[0].ForcedKeyframes[0]);
    }

    [Fact]
    public void An_encoder_without_a_packet_yet_returns_nothing()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();
        _opened[0].ProducePackets = false;
        Assert.False(source.TryGetFrame(false, Timeout, out _));
    }

    [Fact]
    public void Dispose_releases_the_encoder_and_the_capture()
    {
        var source = Source();
        source.Dispose();
        Assert.True(_opened[0].Disposed);
        Assert.True(_capture.Disposed);
    }
}
```

Run: `dotnet test tests/CouchLink.Video.Tests --filter FullyQualifiedName~ScreenVideoSourceTests`
Expected: build FAILS: `IScreenCapture`, `IFrameEncoder`, `CaptureStatus`, `ScreenVideoSource` not found.

- [ ] **Step 2: Write the implementation**

`src/CouchLink.Video/IScreenCapture.cs`:
```csharp
using CouchLink.Core.Video;

namespace CouchLink.Video;

public enum CaptureStatus
{
    /// <summary>The screen changed; the new image is ready to encode.</summary>
    NewFrame,

    /// <summary>Nothing changed within the timeout; the last image is still current.</summary>
    NoChange,

    /// <summary>Capture is unavailable (UAC prompt, display mode change, exclusive fullscreen).</summary>
    Lost,
}

/// <summary>The host screen. Not thread-safe; used from the video thread only.</summary>
public interface IScreenCapture : IDisposable
{
    int Width { get; }
    int Height { get; }

    /// <summary>Waits up to <paramref name="timeout"/> for a screen change. Returns Lost at once when capture is unavailable, retrying each call.</summary>
    CaptureStatus TryCapture(TimeSpan timeout);
}

/// <summary>Encodes the capture's current image to H.264 at a fixed size.</summary>
public interface IFrameEncoder : IDisposable
{
    string Name { get; }
    bool IsHardware { get; }
    VideoSize Size { get; }

    /// <summary>Encodes the current image. Returns false when the encoder produced no packet yet.</summary>
    bool Encode(bool forceKeyframe, out EncodedFrame frame);
}
```

`src/CouchLink.Video/ScreenVideoSource.cs`:
```csharp
using CouchLink.Core.Video;

namespace CouchLink.Video;

/// <summary>
/// The host screen as an <see cref="IEncodedVideoSource"/>. A changed screen is encoded at
/// once, at most <see cref="StreamSettings.FrameRate"/> times a second. A still screen is
/// re-encoded once per frame interval (tiny delta frames), so clients always get newer frames
/// and notice a lost one. While capture is lost the last image keeps going out marked Paused;
/// the first frame after it, and the first after the screen size changes, is a keyframe.
/// Owns the capture and the encoder. Used from the streamer's one thread.
/// </summary>
public sealed class ScreenVideoSource : IEncodedVideoSource
{
    private readonly IScreenCapture _capture;
    private readonly IReadOnlyList<string> _encoderNames;
    private readonly Func<string, VideoSize, IFrameEncoder> _openEncoder;
    private readonly StreamSettings _settings;
    private readonly TimeProvider _time;
    private readonly Action<TimeSpan> _sleep;
    private readonly long _start;
    private readonly TimeSpan _interval;
    private readonly List<string> _skipped = [];
    private IFrameEncoder _encoder;
    private (int Width, int Height) _encodedFrom;
    private TimeSpan? _lastSent;
    private bool _keyframeOwed;

    public ScreenVideoSource(
        IScreenCapture capture,
        IReadOnlyList<string> encoderNames,
        Func<string, VideoSize, IFrameEncoder> openEncoder,
        StreamSettings settings,
        TimeProvider time,
        Action<TimeSpan>? sleep = null)
    {
        _capture = capture;
        _encoderNames = encoderNames;
        _openEncoder = openEncoder;
        _settings = settings;
        _time = time;
        _sleep = sleep ?? Thread.Sleep;
        _start = time.GetTimestamp();
        _interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / settings.FrameRate);
        _encoder = OpenEncoder();
    }

    public string EncoderName => _encoder.Name;
    public bool IsHardware => _encoder.IsHardware;
    public VideoSize Size => _encoder.Size;
    public bool Paused { get; private set; }

    /// <summary>Encoders that failed to open before the current one, with the reason.</summary>
    public IReadOnlyList<string> SkippedEncoders => _skipped;

    private TimeSpan Now => _time.GetElapsedTime(_start);

    public bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame)
    {
        frame = default;
        var due = _lastSent is { } last ? last + _interval : Now;
        var wait = Clamp(due - Now, timeout);

        var status = _capture.TryCapture(wait);
        if (status == CaptureStatus.Lost)
        {
            Paused = true;
            var remaining = due - Now; // capture fails fast: wait for the frame slot instead of spinning
            if (remaining > timeout)
            {
                _sleep(timeout);
                return false;
            }
            if (remaining > TimeSpan.Zero)
                _sleep(remaining);
        }
        else
        {
            if (Paused)
            {
                Paused = false;
                _keyframeOwed = true;
            }
            if (status == CaptureStatus.NoChange && Now < due)
                return false; // no change, and no repeat due yet
            if (status == CaptureStatus.NewFrame && Now < due)
                _sleep(due - Now); // the screen changes faster than the frame rate
        }

        if ((_capture.Width, _capture.Height) != _encodedFrom)
        {
            _encoder.Dispose();
            _encoder = OpenEncoder();
            _keyframeOwed = true;
        }

        if (!_encoder.Encode(forceKeyframe || _keyframeOwed, out var encoded))
            return false;
        if (encoded.Keyframe)
            _keyframeOwed = false;
        frame = encoded with { Paused = Paused };
        _lastSent = Now;
        return true;
    }

    private IFrameEncoder OpenEncoder()
    {
        _skipped.Clear();
        _encodedFrom = (_capture.Width, _capture.Height);
        var size = _settings.SizeFor(_capture.Width, _capture.Height);
        foreach (var name in _encoderNames)
        {
            try
            {
                return _openEncoder(name, size);
            }
            catch (Exception e)
            {
                _skipped.Add($"{name}: {e.Message}");
            }
        }
        throw new InvalidOperationException($"No video encoder could be opened. {string.Join(" ", _skipped)}");
    }

    private static TimeSpan Clamp(TimeSpan value, TimeSpan max) =>
        value < TimeSpan.Zero ? TimeSpan.Zero : value > max ? max : value;

    public void Dispose()
    {
        _encoder.Dispose();
        _capture.Dispose();
    }
}
```

- [ ] **Step 3: Run tests to verify they pass**

Run: `dotnet test tests/CouchLink.Video.Tests`
Expected: PASS (16 tests: 3 FFmpeg + 13 source).

- [ ] **Step 4: Commit**

```powershell
git add src/CouchLink.Video tests/CouchLink.Video.Tests
git commit -m "feat(video): screen source with pacing, repeats, pauses and encoder fallback" -m "Refs #14"
```

---

### Task 6: Desktop capture and the VideoTest tool

**Files:**
- Create: `src/CouchLink.Video/DesktopCapture.cs`
- Create: `src/CouchLink.Video/DisplayInfo.cs`
- Create: `src/CouchLink.VideoTest/CouchLink.VideoTest.csproj`
- Create: `src/CouchLink.VideoTest/Program.cs` (capture mode here; Task 7 adds encode mode)
- Modify: `CouchLink.slnx`

**Interfaces:**
- Consumes: `IScreenCapture`, `CaptureStatus` (Task 5).
- Produces:
  - `public sealed class DesktopCapture : IScreenCapture` with `static DesktopCapture Open()` (first adapter that has a display output), `ID3D11Device Device`, `ID3D11DeviceContext Context`, `uint VendorId`, `string AdapterName`, `int RefreshRate`, `ID3D11Texture2D LastFrame` (BGRA, replaced when the size changes).
  - `public static partial class DisplayInfo` with `static int PrimaryRefreshRate()` (Hz; 60 if Windows can't tell).

This task is GPU glue; it is verified on real hardware by `VideoTest capture`, the same way PadTest checks the virtual pads.

- [ ] **Step 1: Write the capture code**

`src/CouchLink.Video/DesktopCapture.cs`:
```csharp
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;

namespace CouchLink.Video;

/// <summary>
/// DXGI Desktop Duplication of the host's display. Each new screen image is copied on the GPU
/// into <see cref="LastFrame"/>, so it can be re-encoded while the screen is still. When
/// duplication is lost (UAC prompt, display mode change, exclusive fullscreen) it reports
/// Lost and tries to start again on every call; a new screen size replaces LastFrame.
/// </summary>
public sealed class DesktopCapture : IScreenCapture
{
    private readonly IDXGIOutput1 _output;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D _lastFrame;

    private DesktopCapture(IDXGIAdapter1 adapter, IDXGIOutput1 output)
    {
        _output = output;
        VendorId = adapter.Description1.VendorId;
        AdapterName = adapter.Description1.Description;
        D3D11CreateDevice(adapter, DriverType.Unknown,
            DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport,
            [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0],
            out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        Device = device;
        Context = context;

        var bounds = output.Description.DesktopCoordinates;
        Width = bounds.Right - bounds.Left;
        Height = bounds.Bottom - bounds.Top;
        _lastFrame = CreateFrameTexture();
        TryStartDuplication();
    }

    public ID3D11Device Device { get; }
    public ID3D11DeviceContext Context { get; }
    public uint VendorId { get; }
    public string AdapterName { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int RefreshRate { get; private set; } = 60;

    /// <summary>The latest screen image (BGRA). A new texture after the screen size changes.</summary>
    public ID3D11Texture2D LastFrame => _lastFrame;

    /// <summary>Opens the first display output of the first adapter that has one.</summary>
    public static DesktopCapture Open()
    {
        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        for (uint i = 0; factory.EnumAdapters1(i, out IDXGIAdapter1? adapter).Success; i++)
        {
            using (adapter)
            {
                if (adapter!.EnumOutputs(0, out IDXGIOutput? output).Success)
                {
                    using (output)
                        return new DesktopCapture(adapter, output!.QueryInterface<IDXGIOutput1>());
                }
            }
        }
        throw new InvalidOperationException("No display output was found to capture.");
    }

    public CaptureStatus TryCapture(TimeSpan timeout)
    {
        if (_duplication is null && !TryStartDuplication())
            return CaptureStatus.Lost;

        var result = _duplication!.AcquireNextFrame(
            (uint)Math.Max(0, timeout.TotalMilliseconds), out _, out IDXGIResource? resource);
        if (result == Vortice.DXGI.ResultCode.WaitTimeout)
            return CaptureStatus.NoChange;
        if (result.Failure)
        {
            StopDuplication();
            return CaptureStatus.Lost;
        }

        using (resource)
        using (var texture = resource!.QueryInterface<ID3D11Texture2D>())
            Context.CopyResource(_lastFrame, texture);
        _duplication.ReleaseFrame();
        return CaptureStatus.NewFrame;
    }

    private bool TryStartDuplication()
    {
        try
        {
            _duplication = _output.DuplicateOutput(Device);
        }
        catch (SharpGenException)
        {
            return false; // e.g. the secure desktop (UAC) is showing; try again next call
        }

        var mode = _duplication.Description.ModeDescription;
        if (mode.RefreshRate.Denominator != 0)
            RefreshRate = (int)Math.Round((double)mode.RefreshRate.Numerator / mode.RefreshRate.Denominator);
        if ((int)mode.Width != Width || (int)mode.Height != Height)
        {
            Width = (int)mode.Width;
            Height = (int)mode.Height;
            _lastFrame.Dispose();
            _lastFrame = CreateFrameTexture();
        }
        return true;
    }

    private void StopDuplication()
    {
        _duplication?.Dispose();
        _duplication = null;
    }

    private ID3D11Texture2D CreateFrameTexture() => Device.CreateTexture2D(new Texture2DDescription
    {
        Width = (uint)Width,
        Height = (uint)Height,
        MipLevels = 1,
        ArraySize = 1,
        Format = Format.B8G8R8A8_UNorm,
        SampleDescription = new SampleDescription(1, 0),
        Usage = ResourceUsage.Default,
        BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
    });

    public void Dispose()
    {
        StopDuplication();
        _lastFrame.Dispose();
        _output.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}
```

`src/CouchLink.Video/DisplayInfo.cs`:
```csharp
using System.Runtime.InteropServices;

namespace CouchLink.Video;

/// <summary>Facts about the host's display, readable before capture starts.</summary>
public static partial class DisplayInfo
{
    private const int EnumCurrentSettings = -1;

    /// <summary>The primary display's refresh rate in Hz, or 60 if Windows can't tell.</summary>
    public static unsafe int PrimaryRefreshRate()
    {
        var mode = new DevMode { Size = (ushort)sizeof(DevMode) };
        return EnumDisplaySettings(null, EnumCurrentSettings, ref mode) && mode.DisplayFrequency > 1
            ? (int)mode.DisplayFrequency
            : 60;
    }

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplaySettings(string? deviceName, int modeNumber, ref DevMode mode);

    /// <summary>DEVMODEW (220 bytes), display fields only.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DevMode
    {
        public fixed char DeviceName[32];
        public ushort SpecVersion, DriverVersion, Size, DriverExtra;
        public uint Fields;
        public int PositionX, PositionY;
        public uint DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        public fixed char FormName[32];
        public ushort LogPixels;
        public uint BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public uint IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
}
```

- [ ] **Step 2: Create the VideoTest tool with capture mode**

`src/CouchLink.VideoTest/CouchLink.VideoTest.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\CouchLink.Video\CouchLink.Video.csproj" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

Add `<Project Path="src/CouchLink.VideoTest/CouchLink.VideoTest.csproj" />` to the `/src/` folder of `CouchLink.slnx`.

`src/CouchLink.VideoTest/Program.cs`:
```csharp
using System.Diagnostics;
using CouchLink.Video;

// VideoTest: checks host capture and encoding on this PC's GPU without a client.
//   VideoTest capture [seconds]   counts screen changes and lost captures
if (args.Length == 0 || args[0] is not ("capture" or "encode"))
{
    Console.WriteLine("Usage: VideoTest capture [seconds] | VideoTest encode [seconds] [--resolution=1080p] [--fps=60] [--encoder=h264_amf] [--out=videotest.h264]");
    return 2;
}

int seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 5;
return args[0] == "capture" ? Capture(seconds) : 2;

static int Capture(int seconds)
{
    using var capture = DesktopCapture.Open();
    Console.WriteLine($"Adapter: {capture.AdapterName} (vendor 0x{capture.VendorId:X4})");
    Console.WriteLine($"Screen: {capture.Width}x{capture.Height} at {capture.RefreshRate} Hz (DisplayInfo says {DisplayInfo.PrimaryRefreshRate()} Hz)");

    int changed = 0, unchanged = 0, lost = 0;
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
    {
        switch (capture.TryCapture(TimeSpan.FromMilliseconds(16)))
        {
            case CaptureStatus.NewFrame: changed++; break;
            case CaptureStatus.NoChange: unchanged++; break;
            default: lost++; Thread.Sleep(16); break;
        }
    }
    Console.WriteLine($"{seconds} s: {changed} screen changes, {unchanged} waits with no change, {lost} lost");
    bool ok = changed > 0 && lost == 0;
    Console.WriteLine(ok ? "PASS: capture works" : "FAIL: no screen changes captured, or capture was lost");
    return ok ? 0 : 1;
}
```

- [ ] **Step 3: Build and run the capture check**

Run: `dotnet build` (0 warnings), then `dotnet run --project src/CouchLink.VideoTest -- capture 5` while moving a window or the mouse over something animated.
Expected on the dev PC: `Adapter: AMD Radeon RX 6600 (vendor 0x1002)`, `Screen: 2560x1080 at 75 Hz (DisplayInfo says 75 Hz)`, more than 0 screen changes, `0 lost`, `PASS: capture works`, exit code 0.

Then run it again and press Win+L (lock) for ~2 s and unlock while it runs.
Expected: a non-zero `lost` count and `FAIL` (capture stops on the secure desktop), proving loss is reported rather than thrown.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Video src/CouchLink.VideoTest CouchLink.slnx
git commit -m "feat(video): desktop duplication capture and VideoTest capture check" -m "Refs #14"
```

---

### Task 7: GPU colour conversion and H.264 encoder

**Files:**
- Create: `src/CouchLink.Video/Nv12Converter.cs`
- Create: `src/CouchLink.Video/H264Encoder.cs`
- Modify: `src/CouchLink.VideoTest/Program.cs` (encode mode)

**Interfaces:**
- Consumes: `DesktopCapture` (Task 6); `IFrameEncoder`, `ScreenVideoSource` (Task 5); `FfmpegLibrary` (Task 4); `EncoderChoice`, `StreamSettings`, `VideoSize` (Tasks 1-2); `EncodedFrame`.
- Produces:
  - `internal sealed class Nv12Converter : IDisposable`, ctor `(ID3D11Device device, ID3D11DeviceContext context, ID3D11Texture2D source, int sourceWidth, int sourceHeight, VideoSize output)`, `void Convert(nint nv12Texture, int arraySlice)`.
  - `public sealed unsafe class H264Encoder : IFrameEncoder`, ctor `(DesktopCapture capture, string name, VideoSize size, int frameRate, long bitRate)` (throws `FfmpegException` when the encoder can't open).

- [ ] **Step 1: Write the converter and the encoder**

`src/CouchLink.Video/Nv12Converter.cs`:
```csharp
using CouchLink.Core.Video;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace CouchLink.Video;

/// <summary>
/// Converts the BGRA screen image to NV12 and scales it, on the GPU's video processor,
/// straight into the textures of FFmpeg's frame pool (zero CPU copy). BT.709 limited range.
/// </summary>
internal sealed class Nv12Converter : IDisposable
{
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext _videoContext;
    private readonly ID3D11VideoProcessorEnumerator _enumerator;
    private readonly ID3D11VideoProcessor _processor;
    private readonly ID3D11VideoProcessorInputView _input;
    private readonly Dictionary<(nint Texture, int Slice), ID3D11VideoProcessorOutputView> _outputs = [];

    public Nv12Converter(ID3D11Device device, ID3D11DeviceContext context, ID3D11Texture2D source,
        int sourceWidth, int sourceHeight, VideoSize output)
    {
        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = context.QueryInterface<ID3D11VideoContext>();
        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational(60, 1),
            InputWidth = (uint)sourceWidth,
            InputHeight = (uint)sourceHeight,
            OutputFrameRate = new Rational(60, 1),
            OutputWidth = (uint)output.Width,
            OutputHeight = (uint)output.Height,
            Usage = VideoUsage.PlaybackNormal,
        });
        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);
        using (var context1 = context.QueryInterface<ID3D11VideoContext1>())
        {
            context1.VideoProcessorSetStreamColorSpace1(_processor, 0, ColorSpaceType.RgbFullG22NoneP709);
            context1.VideoProcessorSetOutputColorSpace1(_processor, ColorSpaceType.YcbcrStudioG22LeftP709);
        }
        _input = _videoDevice.CreateVideoProcessorInputView(source, _enumerator, new VideoProcessorInputViewDescription
        {
            FourCC = 0,
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
        });
    }

    /// <summary>Writes the current source image into one NV12 texture of FFmpeg's pool.</summary>
    public void Convert(nint nv12Texture, int arraySlice)
    {
        if (!_outputs.TryGetValue((nv12Texture, arraySlice), out var view))
        {
            var texture = new ID3D11Texture2D(nv12Texture); // owned by FFmpeg's pool; never disposed here
            view = _videoDevice.CreateVideoProcessorOutputView(texture, _enumerator, new VideoProcessorOutputViewDescription
            {
                ViewDimension = VideoProcessorOutputViewDimension.Texture2DArray,
                Texture2DArray = new Texture2DArrayVideoProcessorOutputView
                {
                    MipSlice = 0, FirstArraySlice = (uint)arraySlice, ArraySize = 1,
                },
            });
            _outputs[(nv12Texture, arraySlice)] = view;
        }
        _videoContext.VideoProcessorBlt(_processor, view, 0, 1,
            [new VideoProcessorStream { Enable = true, InputSurface = _input }]).CheckError();
    }

    public void Dispose()
    {
        foreach (var view in _outputs.Values)
            view.Dispose();
        _input.Dispose();
        _processor.Dispose();
        _enumerator.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
    }
}
```

`src/CouchLink.Video/H264Encoder.cs`:
```csharp
using CouchLink.Core.Video;
using FFmpeg.AutoGen;
using Vortice.Direct3D11;
using Vortice.DXGI;
using ID3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace CouchLink.Video;

/// <summary>
/// H.264 through FFmpeg. Hardware encoders (AMF, NVENC) get D3D11 NV12 frames filled on the
/// GPU by <see cref="Nv12Converter"/>; x264 gets a CPU copy converted by swscale. Keyframes
/// only when forced (gop = int.MaxValue), no B-frames, one packet per frame.
/// </summary>
public sealed unsafe class H264Encoder : IFrameEncoder
{
    private readonly DesktopCapture _capture;
    private readonly int _sourceWidth, _sourceHeight;
    private readonly Nv12Converter? _converter;
    private readonly ID3D11Texture2D? _staging;
    private AVBufferRef* _hwDevice;
    private AVBufferRef* _hwFrames;
    private AVCodecContext* _codec;
    private AVPacket* _packet;
    private SwsContext* _sws;
    private long _pts;

    public H264Encoder(DesktopCapture capture, string name, VideoSize size, int frameRate, long bitRate)
    {
        _capture = capture;
        _sourceWidth = capture.Width;
        _sourceHeight = capture.Height;
        Name = name;
        IsHardware = EncoderChoice.IsHardware(name);
        Size = size;
        try
        {
            AVCodec* codec = ffmpeg.avcodec_find_encoder_by_name(name);
            if (codec == null)
                throw new FfmpegException($"FFmpeg has no {name} encoder.");

            _codec = ffmpeg.avcodec_alloc_context3(codec);
            _codec->width = size.Width;
            _codec->height = size.Height;
            _codec->time_base = new AVRational { num = 1, den = frameRate };
            _codec->framerate = new AVRational { num = frameRate, den = 1 };
            _codec->bit_rate = bitRate;
            _codec->max_b_frames = 0;
            _codec->gop_size = int.MaxValue; // keyframes only when forced (0 would make x264 all-intra)
            _codec->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            _codec->color_range = AVColorRange.AVCOL_RANGE_MPEG;

            if (IsHardware)
            {
                _codec->colorspace = AVColorSpace.AVCOL_SPC_BT709;
                _codec->color_primaries = AVColorPrimaries.AVCOL_PRI_BT709;
                _codec->color_trc = AVColorTransferCharacteristic.AVCOL_TRC_BT709;
                OpenGpuFrames(size);
                _codec->pix_fmt = AVPixelFormat.AV_PIX_FMT_D3D11;
                _codec->hw_frames_ctx = ffmpeg.av_buffer_ref(_hwFrames);
                _converter = new Nv12Converter(capture.Device, capture.Context, capture.LastFrame, _sourceWidth, _sourceHeight, size);
            }
            else
            {
                _codec->colorspace = AVColorSpace.AVCOL_SPC_SMPTE170M; // swscale's default matrix is BT.601
                _codec->pix_fmt = AVPixelFormat.AV_PIX_FMT_NV12;
                _staging = capture.Device.CreateTexture2D(new Texture2DDescription
                {
                    Width = (uint)_sourceWidth,
                    Height = (uint)_sourceHeight,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = Format.B8G8R8A8_UNorm,
                    SampleDescription = new SampleDescription(1, 0),
                    Usage = ResourceUsage.Staging,
                    CPUAccessFlags = CpuAccessFlags.Read,
                });
                _sws = ffmpeg.sws_getContext(_sourceWidth, _sourceHeight, AVPixelFormat.AV_PIX_FMT_BGRA,
                    size.Width, size.Height, AVPixelFormat.AV_PIX_FMT_NV12, ffmpeg.SWS_BILINEAR, null, null, null);
                if (_sws == null)
                    throw new FfmpegException("Creating the colour converter failed.");
            }

            AVDictionary* options = null;
            foreach (var (key, value) in EncoderChoice.Options(name))
                ffmpeg.av_dict_set(&options, key, value, 0);
            int result = ffmpeg.avcodec_open2(_codec, codec, &options);
            ffmpeg.av_dict_free(&options);
            FfmpegLibrary.Check(result, $"Opening {name}");
            _packet = ffmpeg.av_packet_alloc();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string Name { get; }
    public bool IsHardware { get; }
    public VideoSize Size { get; }

    private void OpenGpuFrames(VideoSize size)
    {
        _hwDevice = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
        var deviceContext = (AVD3D11VADeviceContext*)((AVHWDeviceContext*)_hwDevice->data)->hwctx;
        _capture.Device.AddRef(); // FFmpeg releases it when the device context is freed
        deviceContext->device = (FFmpeg.AutoGen.ID3D11Device*)_capture.Device.NativePointer;
        FfmpegLibrary.Check(ffmpeg.av_hwdevice_ctx_init(_hwDevice), "Sharing the D3D11 device with FFmpeg");

        _hwFrames = ffmpeg.av_hwframe_ctx_alloc(_hwDevice);
        var frames = (AVHWFramesContext*)_hwFrames->data;
        frames->format = AVPixelFormat.AV_PIX_FMT_D3D11;
        frames->sw_format = AVPixelFormat.AV_PIX_FMT_NV12;
        frames->width = size.Width;
        frames->height = size.Height;
        frames->initial_pool_size = 0; // one texture per frame: AMD can't render to NV12 texture arrays
        ((AVD3D11VAFramesContext*)frames->hwctx)->BindFlags = (uint)BindFlags.RenderTarget;
        FfmpegLibrary.Check(ffmpeg.av_hwframe_ctx_init(_hwFrames), "Creating GPU frames");
    }

    public bool Encode(bool forceKeyframe, out EncodedFrame frame)
    {
        if (_capture.Width != _sourceWidth || _capture.Height != _sourceHeight)
            throw new InvalidOperationException("The screen size changed; open a new encoder.");

        AVFrame* input = ffmpeg.av_frame_alloc();
        try
        {
            if (IsHardware)
            {
                FfmpegLibrary.Check(ffmpeg.av_hwframe_get_buffer(_hwFrames, input, 0), "Getting a GPU frame");
                _converter!.Convert((nint)input->data[0], (int)(nint)input->data[1]);
            }
            else
            {
                CopyToCpu(input);
            }
            input->pts = _pts++;
            if (forceKeyframe)
                input->pict_type = AVPictureType.AV_PICTURE_TYPE_I;
            FfmpegLibrary.Check(ffmpeg.avcodec_send_frame(_codec, input), $"Encoding with {Name}");
        }
        finally
        {
            ffmpeg.av_frame_free(&input);
        }

        using var output = new MemoryStream();
        bool keyframe = false;
        while (true)
        {
            int result = ffmpeg.avcodec_receive_packet(_codec, _packet);
            if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN))
                break;
            FfmpegLibrary.Check(result, $"Reading a packet from {Name}");
            output.Write(new ReadOnlySpan<byte>(_packet->data, _packet->size));
            keyframe |= (_packet->flags & ffmpeg.AV_PKT_FLAG_KEY) != 0;
            ffmpeg.av_packet_unref(_packet);
        }

        frame = output.Length == 0 ? default : new EncodedFrame(output.ToArray(), keyframe);
        return output.Length > 0;
    }

    private void CopyToCpu(AVFrame* input)
    {
        _capture.Context.CopyResource(_staging!, _capture.LastFrame);
        var map = _capture.Context.Map(_staging!, 0, MapMode.Read);
        try
        {
            input->format = (int)AVPixelFormat.AV_PIX_FMT_NV12;
            input->width = Size.Width;
            input->height = Size.Height;
            FfmpegLibrary.Check(ffmpeg.av_frame_get_buffer(input, 0), "Allocating a frame");
            byte*[] source = [(byte*)map.DataPointer];
            int[] stride = [(int)map.RowPitch];
            ffmpeg.sws_scale(_sws, source, stride, 0, _sourceHeight, input->data.ToArray(), input->linesize.ToArray());
        }
        finally
        {
            _capture.Context.Unmap(_staging!, 0);
        }
    }

    public void Dispose()
    {
        _converter?.Dispose(); // its views point into FFmpeg's textures: release them first
        _staging?.Dispose();
        if (_packet != null) { var packet = _packet; ffmpeg.av_packet_free(&packet); _packet = null; }
        if (_codec != null) { var codec = _codec; ffmpeg.avcodec_free_context(&codec); _codec = null; }
        if (_hwFrames != null) { var frames = _hwFrames; ffmpeg.av_buffer_unref(&frames); _hwFrames = null; }
        if (_hwDevice != null) { var device = _hwDevice; ffmpeg.av_buffer_unref(&device); _hwDevice = null; }
        if (_sws != null) { ffmpeg.sws_freeContext(_sws); _sws = null; }
    }
}
```

- [ ] **Step 2: Add encode mode to VideoTest**

In `src/CouchLink.VideoTest/Program.cs`, add `using CouchLink.Core.Video;` at the top, replace `return args[0] == "capture" ? Capture(seconds) : 2;` with `return args[0] == "capture" ? Capture(seconds) : Encode(seconds, args);`, and add:
```csharp
static int Encode(int seconds, string[] args)
{
    string Option(string name, string fallback) =>
        args.FirstOrDefault(a => a.StartsWith($"--{name}=", StringComparison.Ordinal))?[(name.Length + 3)..] ?? fallback;

    var resolution = StreamSettings.Resolutions.First(r =>
        string.Equals(StreamSettings.Label(r), Option("resolution", "1080p"), StringComparison.OrdinalIgnoreCase));
    var settings = new StreamSettings(resolution, int.Parse(Option("fps", "60")));
    string outPath = Option("out", "videotest.h264");

    if (!FfmpegLibrary.TryLoad(out var error))
    {
        Console.WriteLine($"FAIL: {error}");
        return 1;
    }

    var capture = DesktopCapture.Open();
    IReadOnlyList<string> encoders = args.Any(a => a.StartsWith("--encoder=", StringComparison.Ordinal))
        ? [Option("encoder", EncoderChoice.Software)]
        : EncoderChoice.Candidates(capture.VendorId);
    var encodeTimes = new List<double>();
    using var source = new ScreenVideoSource(capture, encoders,
        (name, size) => new TimedEncoder(new H264Encoder(capture, name, size, settings.FrameRate, settings.BitRateFor(size)), encodeTimes),
        settings, TimeProvider.System);

    Console.WriteLine($"Adapter: {capture.AdapterName} (vendor 0x{capture.VendorId:X4}), screen {capture.Width}x{capture.Height} at {capture.RefreshRate} Hz");
    foreach (var skipped in source.SkippedEncoders)
        Console.WriteLine($"Skipped {skipped}");
    Console.WriteLine($"Encoder: {source.EncoderName} ({(source.IsHardware ? "hardware" : "software")}), " +
        $"{source.Size.Width}x{source.Size.Height} at {settings.FrameRate} fps, {settings.BitRateFor(source.Size) / 1e6:0.0} Mbps target");

    using var file = File.Create(outPath);
    int frames = 0, keyframes = 0, paused = 0;
    long bytes = 0;
    var clock = Stopwatch.StartNew();
    bool forcedMiddle = false;
    while (clock.Elapsed < TimeSpan.FromSeconds(seconds))
    {
        bool force = !forcedMiddle && clock.Elapsed > TimeSpan.FromSeconds(seconds / 2.0);
        if (!source.TryGetFrame(force, TimeSpan.FromMilliseconds(50), out var frame))
            continue;
        forcedMiddle |= force;
        file.Write(frame.Data.Span);
        frames++;
        bytes += frame.Data.Length;
        keyframes += frame.Keyframe ? 1 : 0;
        paused += frame.Paused ? 1 : 0;
    }

    encodeTimes.Sort();
    double P(double q) => encodeTimes.Count == 0 ? double.NaN : encodeTimes[(int)Math.Min(encodeTimes.Count - 1, q * encodeTimes.Count)];
    double budgetMs = 1000.0 / settings.FrameRate;
    Console.WriteLine($"{frames} frames in {seconds} s ({frames / (double)seconds:0.0} fps), {keyframes} keyframes, {paused} paused, " +
        $"{bytes * 8.0 / seconds / 1e6:0.0} Mbps; encode ms p50 {P(0.5):0.00} p95 {P(0.95):0.00} (budget {budgetMs:0.0})");
    Console.WriteLine($"Wrote {Path.GetFullPath(outPath)}");

    bool ok = frames >= 0.9 * seconds * settings.FrameRate && keyframes >= 2 && P(0.95) < budgetMs;
    Console.WriteLine(ok ? "PASS" : "FAIL: too few frames, a missing keyframe, or encoding slower than the frame rate");
    return ok ? 0 : 1;
}

sealed class TimedEncoder(IFrameEncoder inner, List<double> times) : IFrameEncoder
{
    public string Name => inner.Name;
    public bool IsHardware => inner.IsHardware;
    public VideoSize Size => inner.Size;

    public bool Encode(bool forceKeyframe, out EncodedFrame frame)
    {
        long start = Stopwatch.GetTimestamp();
        bool produced = inner.Encode(forceKeyframe, out frame);
        times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        return produced;
    }

    public void Dispose() => inner.Dispose();
}
```

- [ ] **Step 3: Run the encode checks on the GPU**

Run each and compare with the spike numbers:
1. `dotnet run --project src/CouchLink.VideoTest -- encode 5`
   Expected on the dev PC: `Encoder: h264_amf (hardware), 2560x1080 at 60 fps, 13.3 Mbps target`, about 300 frames (~60 fps), `2 keyframes` (first + the forced middle one), `0 paused`, encode p95 well under 16.7 ms (spike: ~5 ms), `PASS`.
2. `dotnet run --project src/CouchLink.VideoTest -- encode 5 --resolution=720p --fps=75`
   Expected: `1706x720 at 75 fps`, about 375 frames, p95 under 13.3 ms, `PASS`.
3. `dotnet run --project src/CouchLink.VideoTest -- encode 5 --encoder=libx264 --resolution=720p`
   Expected: `libx264 (software)`, about 300 frames, p95 under 16.7 ms (spike: ~12 ms), `PASS`.
4. `dotnet run --project src/CouchLink.VideoTest -- encode 2 --encoder=h264_nvenc`
   Expected on this AMD PC: the encoder fails to open, so the run ends with an `InvalidOperationException` naming `h264_nvenc: Opening h264_nvenc failed: Function not implemented` (only NVENC was offered). On a PC with an NVIDIA GPU, expect `PASS`.

Then check the files with the FFmpeg tools from `get-ffmpeg.ps1`:
```powershell
$ff = 'third_party\ffmpeg\bin'
& "$ff\ffprobe.exe" -v error -count_frames -show_entries stream=width,height,color_space,color_range,nb_read_frames -of default=nw=1 videotest.h264
& "$ff\ffmpeg.exe" -v error -hwaccel d3d11va -i videotest.h264 -f null -
& "$ff\ffmpeg.exe" -v error -y -i videotest.h264 -vf "select=eq(n\,150)" -frames:v 1 videotest.png
```
Expected: the size VideoTest printed, `color_space=bt709`, `color_range=tv`, `nb_read_frames` equal to the frame count; the D3D11VA decode prints no errors; `videotest.png` shows the host desktop with correct colours (open it and look).

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Video src/CouchLink.VideoTest
git commit -m "feat(video): GPU NV12 conversion and H.264 encoding through FFmpeg" -m "Refs #15"
```

---

### Task 8: Host streams its screen with the chosen settings

**Files:**
- Create: `src/CouchLink.Core/DevOptions.cs`
- Test: `tests/CouchLink.Core.Tests/DevOptionsTests.cs`
- Create: `src/CouchLink.App/HostVideo.cs`
- Modify: `src/CouchLink.App/CouchLink.App.csproj` (reference `CouchLink.Video`)
- Modify: `src/CouchLink.App/HostInputService.cs`
- Modify: `src/CouchLink.App/ClientVideoService.cs`
- Modify: `src/CouchLink.App/AppServices.cs`
- Modify: `src/CouchLink.App/App.xaml.cs`
- Modify: `src/CouchLink.App/MainWindow.xaml`, `src/CouchLink.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: everything above, plus Plan 3's `VideoStreamer`, `VideoSender`, `TestPatternSource`, `VideoClient`.
- Produces: `public sealed record DevOptions(bool TestPattern, string? SaveVideoPath)` with `static DevOptions Parse(IEnumerable<string> args)` (`--test-pattern`, `--save-video=<file>`); `AppServices.Options`; `HostInputService.TryStart(StreamSettings settings, out HostInputService? service, out string? error)` and `string DescribeVideo()`; `ClientVideoService.TryStart(InputSender sender, string? savePath, out ClientVideoService? service, out string? error)`.

- [ ] **Step 1: Write the failing test for the dev options**

`tests/CouchLink.Core.Tests/DevOptionsTests.cs`:
```csharp
namespace CouchLink.Core.Tests;

public class DevOptionsTests
{
    [Fact]
    public void Nothing_set_by_default()
    {
        Assert.Equal(new DevOptions(false, null), DevOptions.Parse([]));
    }

    [Fact]
    public void Test_pattern_and_save_video_are_read()
    {
        var options = DevOptions.Parse(["--crash-test=ui", "--test-pattern", @"--save-video=C:\temp\in.h264"]);
        Assert.True(options.TestPattern);
        Assert.Equal(@"C:\temp\in.h264", options.SaveVideoPath);
    }

    [Fact]
    public void An_empty_save_path_is_ignored()
    {
        Assert.Null(DevOptions.Parse(["--save-video="]).SaveVideoPath);
    }
}
```

Run: `dotnet test --filter FullyQualifiedName~DevOptionsTests`
Expected: build FAILS: `DevOptions` not found.

`src/CouchLink.Core/DevOptions.cs`:
```csharp
namespace CouchLink.Core;

/// <summary>
/// Developer switches on the command line: <c>--test-pattern</c> streams Plan 3's test pattern
/// instead of the screen; <c>--save-video=&lt;file&gt;</c> makes a client save the H.264 it receives.
/// </summary>
public sealed record DevOptions(bool TestPattern, string? SaveVideoPath)
{
    private const string SaveVideo = "--save-video=";

    public static DevOptions Parse(IEnumerable<string> args)
    {
        bool testPattern = false;
        string? savePath = null;
        foreach (var arg in args)
        {
            if (arg == "--test-pattern")
                testPattern = true;
            else if (arg.StartsWith(SaveVideo, StringComparison.Ordinal) && arg.Length > SaveVideo.Length)
                savePath = arg[SaveVideo.Length..];
        }
        return new DevOptions(testPattern, savePath);
    }
}
```

Run: `dotnet test --filter FullyQualifiedName~DevOptionsTests`
Expected: PASS (3 tests).

- [ ] **Step 2: Host video in the app**

In `src/CouchLink.App/CouchLink.App.csproj`, add `<ProjectReference Include="..\CouchLink.Video\CouchLink.Video.csproj" />` to the existing project-reference group.

In `src/CouchLink.App/AppServices.cs`, add:
```csharp
    /// <summary>Command-line developer switches, read at startup.</summary>
    public static DevOptions Options { get; set; } = new(false, null);
```
(and `using CouchLink.Core;`). In `App.xaml.cs` `OnStartup`, right after the "CouchLink started" log line, add `AppServices.Options = DevOptions.Parse(e.Args);`.

`src/CouchLink.App/HostVideo.cs`:
```csharp
using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Video;
using CouchLink.Video;

namespace CouchLink.App;

/// <summary>
/// The host's video: the screen through <see cref="ScreenVideoSource"/> with the chosen
/// settings, or the test pattern with --test-pattern. If video can't start (no FFmpeg, no
/// encoder, no display) hosting still runs the pads, and <see cref="Describe"/> says why.
/// </summary>
internal sealed class HostVideo : IDisposable
{
    private readonly VideoStreamer? _streamer;
    private readonly ScreenVideoSource? _screen;
    private readonly string _summary;

    private HostVideo(VideoStreamer? streamer, ScreenVideoSource? screen, string summary)
    {
        _streamer = streamer;
        _screen = screen;
        _summary = summary;
    }

    public static HostVideo Start(StreamSettings settings, Action<Exception> onError)
    {
        if (AppServices.Options.TestPattern)
        {
            var pattern = new TestPatternSource(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / settings.FrameRate));
            return new HostVideo(Stream(pattern, onError), null, $"test pattern at {settings.FrameRate} fps");
        }

        if (!FfmpegLibrary.TryLoad(out var error))
            return Unavailable(error!);

        DesktopCapture? capture = null;
        try
        {
            capture = DesktopCapture.Open();
            var screenCapture = capture;
            var screen = new ScreenVideoSource(screenCapture, EncoderChoice.Candidates(capture.VendorId),
                (name, size) => new H264Encoder(screenCapture, name, size, settings.FrameRate, settings.BitRateFor(size)),
                settings, TimeProvider.System);
            foreach (var skipped in screen.SkippedEncoders)
                AppServices.Log.Write($"Video encoder skipped: {skipped}");
            var summary = $"{screen.EncoderName} {screen.Size.Width}x{screen.Size.Height} at {settings.FrameRate} fps, " +
                $"{settings.BitRateFor(screen.Size) / 1e6:0.0} Mbps ({(screen.IsHardware ? "hardware" : "software")}) on {capture.AdapterName}";
            AppServices.Log.Write($"Video: {summary}");
            return new HostVideo(Stream(screen, onError), screen, summary);
        }
        catch (Exception e)
        {
            capture?.Dispose();
            return Unavailable(e.Message);
        }
    }

    private static VideoStreamer Stream(IEncodedVideoSource source, Action<Exception> onError) =>
        new(source, new VideoSender(), Ports.Video, TimeProvider.System, onError);

    private static HostVideo Unavailable(string reason)
    {
        AppServices.Log.Write($"Video unavailable: {reason}");
        return new HostVideo(null, null, $"unavailable: {reason}");
    }

    public void ClientSeen(byte slot, IPAddress from) => _streamer?.ClientSeen(slot, from);

    public void RequestKeyframe() => _streamer?.RequestKeyframe();

    public string Describe()
    {
        var text = $"Video: {_summary}";
        if (_streamer is { } streamer)
        {
            var s = streamer.Stats;
            text += $"\n  {s.Clients} client(s), {s.FramesSent} frames, {s.KeyframesSent} keyframes, {s.BytesSent / 1_000_000.0:0.0} MB";
        }
        if (_screen is { IsHardware: false })
            text += $"\n  {EncoderChoice.SoftwareWarning}";
        if (_screen is { Paused: true })
            text += "\n  Host screen paused (capture lost)";
        return text;
    }

    public void Dispose() => _streamer?.Dispose(); // the streamer owns the source, which owns the capture
}
```

`src/CouchLink.App/HostInputService.cs`:
- Replace the field `private readonly VideoStreamer _video;` with `private readonly HostVideo _video;`.
- The constructor becomes `private HostInputService(InputReceiver receiver, ViGEmPadFactory factory, StreamSettings settings)` and creates `_video = HostVideo.Start(settings, OnVideoError);` in place of the `new VideoStreamer(...)` call.
- Replace `public VideoSendStats VideoStats => _video.Stats;` with `public string DescribeVideo() => _video.Describe();`.
- `TryStart` becomes `public static bool TryStart(StreamSettings settings, out HostInputService? service, out string? error)` and passes `settings` to the constructor.
- Update the class comment: `Until Plan 4 the video is a test pattern.` becomes `The video is the host screen (Plan 4) or, with --test-pattern, a test pattern.`
- Keep `using CouchLink.Core.Video;` (StreamSettings lives there) and drop `using CouchLink.Core.Net;` only if the compiler reports it unused (it is still needed for `Ports` and `InputReceiver`).

`src/CouchLink.App/ClientVideoService.cs`, full file:
```csharp
using System.IO;
using CouchLink.Core.Net;
using CouchLink.Core.Video;

namespace CouchLink.App;

/// <summary>
/// Client side: receives the host's video on UDP 47802. Until Plan 5 adds a decoder it can
/// save the H.264 it receives (--save-video=&lt;file&gt;) so the stream can be checked with
/// ffprobe/ffplay. Dispose before the <see cref="InputSender"/> it sends keyframe requests through.
/// </summary>
internal sealed class ClientVideoService : IDisposable
{
    private readonly VideoClient _client;
    private readonly FileStream? _save;

    private ClientVideoService(VideoReceiver receiver, InputSender sender, string? savePath)
    {
        _save = savePath is null ? null : File.Create(savePath);
        _client = new VideoClient(
            receiver, sender.SendKeyframeRequest, OnFrame, TimeProvider.System,
            e => AppServices.Log.Write($"Video error: {e}"));
    }

    public static bool TryStart(InputSender sender, string? savePath, out ClientVideoService? service, out string? error)
    {
        service = null;
        if (!VideoReceiver.TryCreate(Ports.Video, out var receiver, out error))
            return false;
        service = new ClientVideoService(receiver!, sender, savePath);
        return true;
    }

    private void OnFrame(AssembledFrame frame) => _save?.Write(frame.Data); // receive thread only

    public string Describe()
    {
        var s = _client.Stats;
        return $"Video: {s.FramesDelivered} frames, {s.Receive.ShardsRecovered} repaired, " +
               $"{s.Receive.FramesLost} lost, loss {s.Receive.LossPercent:0.0}%" +
               (s.WaitingForKeyframe ? ", waiting for keyframe" : "") +
               (s.HostPaused ? ", host screen paused" : "") +
               (_save is null ? "" : $"\nSaving to {_save.Name}");
    }

    public void Dispose()
    {
        _client.Dispose(); // stops the receive thread before the file closes
        _save?.Dispose();
    }
}
```

- [ ] **Step 3: Stream settings in the dev window**

`src/CouchLink.App/MainWindow.xaml`: insert a settings row above the Host button, and make room:
- Change `Height="460"` to `Height="540"`.
- Replace `<Button x:Name="HostButton" Content="Host (create pads)" Height="40" Click="OnHost"/>` with:
```xml
        <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
            <TextBlock Text="Stream" VerticalAlignment="Center"/>
            <ComboBox x:Name="ResolutionBox" Width="90" Margin="8,0"/>
            <ComboBox x:Name="FrameRateBox" Width="80" Margin="0,0,8,0"/>
            <TextBlock Text="(set before hosting)" VerticalAlignment="Center" Foreground="Gray"/>
        </StackPanel>
        <Button x:Name="HostButton" Content="Host (create pads, share screen)" Height="40" Click="OnHost"/>
```

`src/CouchLink.App/MainWindow.xaml.cs`:
- Add `using CouchLink.Core.Video;` and `using CouchLink.Video;`.
- In the constructor after the slot setup:
```csharp
        foreach (var resolution in StreamSettings.Resolutions)
            ResolutionBox.Items.Add(new ComboBoxItem { Content = StreamSettings.Label(resolution), Tag = resolution });
        ResolutionBox.SelectedIndex = StreamSettings.Resolutions.ToList().IndexOf(StreamSettings.Default.Resolution);
        foreach (int rate in StreamSettings.FrameRatesFor(DisplayInfo.PrimaryRefreshRate()))
            FrameRateBox.Items.Add(new ComboBoxItem { Content = $"{rate} fps", Tag = rate });
        FrameRateBox.SelectedIndex = 0; // 60
```
  (add `using System.Windows.Controls;` if `ComboBoxItem` isn't already resolved).
- In `OnHost`, build the settings and pass them:
```csharp
        var settings = new StreamSettings(
            (StreamResolution)((ComboBoxItem)ResolutionBox.SelectedItem).Tag,
            (int)((ComboBoxItem)FrameRateBox.SelectedItem).Tag);
        if (!HostInputService.TryStart(settings, out _host, out var error))
```
  and after starting, disable the pickers with the buttons: `HostButton.IsEnabled = JoinButton.IsEnabled = ResolutionBox.IsEnabled = FrameRateBox.IsEnabled = false;`. Also disable both pickers in `OnJoin` with the other controls (a client doesn't stream).
- In `OnJoin`, pass the save path: `ClientVideoService.TryStart(inputSender, AppServices.Options.SaveVideoPath, out _video, out var videoError)`.
- In `UpdateStatus`, the host branch becomes:
```csharp
            StatusText.Text = $"Hosting. Virtual pads: {_host.PadCount}\n{_host.DescribeVideo()}" +
                (_host.LastError is { } err ? $"\nLast error: {err}" : "");
```

Run: `dotnet build`
Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Real screen from host to client on one PC (UI Automation)**

Save as `$env:TEMP\screen-check.ps1`, set `$exe` to this checkout's build output, and run `powershell -ExecutionPolicy Bypass -File $env:TEMP\screen-check.ps1`:
```powershell
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$exe = 'C:\dev\CouchLink\src\CouchLink.App\bin\Debug\net10.0-windows\CouchLink.App.exe'
$saved = Join-Path $env:TEMP 'couchlink-received.h264'
$A = [Windows.Automation.AutomationElement]
function MainWindow($proc) {
    $cond = New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $proc.Id)
    for ($i = 0; $i -lt 40; $i++) {
        $w = $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $cond)
        if ($w -and $w.Current.Name -eq 'CouchLink (dev)') { return $w }
        Start-Sleep -Milliseconds 250
    }
    throw "no main window for process $($proc.Id)"
}
function El($w, $id) {
    $w.FindFirst([Windows.Automation.TreeScope]::Descendants,
        (New-Object Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)))
}
function Click($w, $id) { (El $w $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }

$hostProc = Start-Process $exe -PassThru
$clientProc = Start-Process $exe -ArgumentList "--save-video=$saved" -PassThru
try {
    $hw = MainWindow $hostProc
    $cw = MainWindow $clientProc
    Click $hw 'HostButton'   # default settings: 1080p, 60 fps
    Click $cw 'JoinButton'   # Host IP box defaults to 127.0.0.1
    Start-Sleep -Seconds 6
    "HOST:`n$((El $hw 'StatusText').Current.Name)`n"
    "CLIENT:`n$((El $cw 'StatusText').Current.Name)"
} finally {
    Stop-Process -Id $hostProc.Id, $clientProc.Id -ErrorAction SilentlyContinue
}
& 'third_party\ffmpeg\bin\ffprobe.exe' -v error -count_frames -show_entries stream=width,height,nb_read_frames -of default=nw=1 $saved
& 'third_party\ffmpeg\bin\ffmpeg.exe' -v error -y -i $saved -vf "select=eq(n\,100)" -frames:v 1 (Join-Path $env:TEMP 'couchlink-received.png')
```
Run it from the repository root (it calls the FFmpeg tools by relative path).
Expected:
- HOST: `Video: h264_amf 2560x1080 at 60 fps, 13.3 Mbps (hardware) on AMD Radeon RX 6600`, then `1 client(s)`, frames climbing (~300+), no warning line.
- CLIENT: `Video: N frames, 0 repaired, 0 lost, loss 0.0%` with N close to the host's count, no `waiting for keyframe`, and `Saving to ...couchlink-received.h264`.
- ffprobe: `width=2560`, `height=1080`, `nb_read_frames` close to N.
- `%TEMP%\couchlink-received.png` shows this desktop with correct colours (open it and look).

- [ ] **Step 6: Settings, pause and fallback by hand**

1. Start the host with 720p and 75 fps (pick them in the window before clicking Host) and join a client as in Step 5. Expected: the host shows `1706x720 at 75 fps`; the client's saved file probes as 1706x720.
2. While a client watches, press Win+L on the host for ~3 s, then unlock. Expected: during the lock the client status shows `host screen paused` and the host shows `Host screen paused (capture lost)`; after unlocking both clear, and the client keeps receiving frames without `lost` growing.
3. Start the app with FFmpeg removed from its `ffmpeg\` folder (rename the folder), click Host. Expected: pads still host; status shows `Video: unavailable: FFmpeg 9 was not found in ...`. Rename the folder back.
4. With `--test-pattern`, click Host. Expected: `Video: test pattern at 60 fps`.

- [ ] **Step 7: Commit**

```powershell
git add src/CouchLink.Core/DevOptions.cs tests/CouchLink.Core.Tests/DevOptionsTests.cs src/CouchLink.App
git commit -m "feat(app): host shares its screen at the chosen resolution and frame rate" -m "Closes #14"
```

---

### Task 9: Ship FFmpeg, CI, notices and spec

**Files:**
- Modify: `eng/package.ps1`
- Modify: `.github/workflows/ci.yml`, `.github/workflows/release.yml`
- Modify: `THIRD-PARTY-NOTICES.md`
- Modify: `README.md`
- Modify: `docs/superpowers/specs/2026-10-05-couchlink-design.md`

**Interfaces:**
- Consumes: `eng/get-ffmpeg.ps1` and the `ffmpeg\` output folder (Task 4).
- Produces: release zips that include `ffmpeg\` (four DLLs + `LICENSE.txt`) and `VideoTest\`.

- [ ] **Step 1: Package FFmpeg and VideoTest**

In `eng/package.ps1`, right after `$ErrorActionPreference = 'Stop'` and the variable block, before publishing, add:
```powershell
if (-not (Test-Path (Join-Path $root 'third_party/ffmpeg/bin/avcodec-63.dll'))) {
    throw 'FFmpeg is missing: run ./eng/get-ffmpeg.ps1 first (host video needs it).'
}
```
and after `Publish 'src/CouchLink.PadTest' (Join-Path $stage 'PadTest')` add:
```powershell
Publish 'src/CouchLink.VideoTest' (Join-Path $stage 'VideoTest')
```
Update the script's `.DESCRIPTION` to: `Publishes the app, PadTest and VideoTest as self-contained win-x64 builds (no .NET install needed on the target PC), with FFmpeg 9 in ffmpeg\, and zips them with the license files.`

Run: `./eng/package.ps1 -OutDir artifacts`, then
```powershell
Expand-Archive artifacts\CouchLink-v*-win-x64.zip $env:TEMP\couchlink-pkg -Force
Get-ChildItem $env:TEMP\couchlink-pkg\ffmpeg, $env:TEMP\couchlink-pkg\VideoTest\ffmpeg | Select-Object Name
```
Expected: both `ffmpeg` folders list `avcodec-63.dll`, `avutil-61.dll`, `swresample-7.dll`, `swscale-10.dll`, `LICENSE.txt`. Then run `$env:TEMP\couchlink-pkg\VideoTest\CouchLink.VideoTest.exe encode 3` and expect `PASS`.

- [ ] **Step 2: CI and release get FFmpeg first**

In `.github/workflows/ci.yml`, add before the `Restore` step:
```yaml
      - name: Get FFmpeg
        if: steps.code.outputs.present == 'true'
        shell: pwsh
        run: ./eng/get-ffmpeg.ps1
```
In `.github/workflows/release.yml`, add before the `Test` step of the `publish` job:
```yaml
      - name: Get FFmpeg
        shell: pwsh
        run: ./eng/get-ffmpeg.ps1
```

- [ ] **Step 3: Notices, README and spec**

`THIRD-PARTY-NOTICES.md`:
- Add rows to "Included in releases":
```markdown
| FFmpeg 9.0 (libavcodec, libavutil, libswscale, libswresample; BtbN GPL build, includes x264) | CouchLink.App, VideoTest (host video) | GPL-3.0-or-later; license text in `ffmpeg/LICENSE.txt` | https://ffmpeg.org, build: https://github.com/BtbN/FFmpeg-Builds |
| FFmpeg.AutoGen | CouchLink.App, VideoTest | LGPL-3.0 | https://github.com/Ruslan-B/FFmpeg.AutoGen |
| Vortice.Windows (Direct3D11, DXGI) | CouchLink.App, VideoTest | MIT | https://github.com/amerkoleci/Vortice.Windows |
```
- Replace the closing paragraph ("When screen streaming is added, FFmpeg ... will be listed here and shipped under the GPL.") with: `FFmpeg's source code is available from https://ffmpeg.org/download.html; the exact build scripts are at https://github.com/BtbN/FFmpeg-Builds.`

`README.md`, "Building from source": change the code block to
```powershell
./eng/get-ffmpeg.ps1       # once: FFmpeg 9 for host video, into third_party/ffmpeg
dotnet build
dotnet test
./eng/package.ps1          # builds artifacts/CouchLink-v<version>-win-x64.zip
```

`docs/superpowers/specs/2026-10-05-couchlink-design.md`, section 5.1:
- After the encoder table paragraph ("No hardware encoder ... - may lag with heavy games."), add: `If the vendor's hardware encoder fails to open (e.g. a driver problem), the host falls back to x264 with the same warning.`
- Replace the **Format** bullet with:
```markdown
- **Format:** H.264, no B-frames. The host picks, before hosting:
  - **Resolution:** Native, **1080p** (default), 900p, 720p or 540p. The
    stream's height is at most the preset; the width follows the host
    screen's aspect ratio; never scaled up.
  - **Frame rate:** **60** (default), 75, 90, 120, 144, 165 or 240 fps,
    offering only rates up to the host display's refresh rate.
  - **Bitrate** follows automatically: ~10 Mbps at 1080p60, scaled by
    pixels and frame rate, 2-30 Mbps.
- **Still screen:** the last image is re-encoded at the frame rate (tiny
  delta frames), so clients always receive newer frames and notice a loss.
- **Capture lost** (UAC prompt, mode change, exclusive fullscreen): the
  last image keeps going out marked *paused*; a keyframe follows when
  capture is back.
```
- In section 8, change "FFmpeg DLLs bundled." to "FFmpeg 9 DLLs (avcodec, avutil, swscale, swresample) bundled in `ffmpeg\` with FFmpeg's license."

- [ ] **Step 4: Run the whole suite**

Run: `dotnet build` then `dotnet test`
Expected: 0 warnings; all tests PASS.

- [ ] **Step 5: Commit**

```powershell
git add eng/package.ps1 .github/workflows THIRD-PARTY-NOTICES.md README.md docs/superpowers/specs/2026-10-05-couchlink-design.md
git commit -m "build: ship FFmpeg 9 and VideoTest; spec for host stream settings" -m "Closes #15"
```
