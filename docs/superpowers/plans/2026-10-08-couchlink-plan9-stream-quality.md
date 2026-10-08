# CouchLink Plan 9: Stream Quality Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The host picks a stream Quality (Low, Balanced, High, Max) in the lobby, which sets the bitrate and the encoder's effort, and the lobby warns when the stream would overload the host's network link.

**Architecture:** Pure, unit-tested logic in `CouchLink.Core`: `StreamQuality` and its helpers, the new bitrate formula in `StreamSettings`, slower encoder candidates in `EncoderChoice`, and `LinkBudget` (which card serves which client, and whether the stream fits). `CouchLink.Video` gets a one-line host converter fix. `CouchLink.App` wires Quality into the lobby, `HostVideo` and `HostService`, and reads the network cards once at start in `HostLinks`. No protocol or client changes.

**Tech Stack:** C# / .NET 10, WPF, FFmpeg 9.0 through FFmpeg.AutoGen, Vortice.Direct3D11, xUnit, Microsoft.Extensions.TimeProvider.Testing.

**Spec:** `docs/superpowers/specs/2026-10-08-couchlink-stream-quality-design.md`

## Global Constraints

- Quality presets and bitrate factors: Low 0.6, Balanced 1, High 2.5, Max 5. Balanced is the default.
- Bitrate = `10 Mbps x (pixels / 1920x1080) x (fps / 60) x factor`, clamped to 2-100 Mbps (`MaxBitRate` 100_000_000).
- High and Max try the slower candidate first: `h264_nvenc (p3)` (`preset=p3` instead of `p1`) and `h264_amf (balanced)` (adds `quality=balanced`). The fast candidates keep today's options exactly. x264 stays `ultrafast` at every Quality.
- Link budget: needed = bitrate x clients x 1.2 (`VideoShardPacket.MaxParityPercent` = 20); warn when needed is more than 70% of the link. No warning with no clients or an unknown speed (`Speed <= 0`).
- Quality applies live through `HostService.ChangeSettings`; it isn't saved between sessions.
- No protocol or client changes.
- Commits: conventional style (`feat(core): ...`), no co-author or "Generated with" lines.
- Never cite Sunshine as a source in code, docs or commits.

## Review Focus

- A host with two network cards (a Wi-Fi or uplink card plus the LAN card): the warning must use the card on the clients' subnet, not the first or fastest card. Test in Task 3.
- Client addresses arriving as IPv4-mapped IPv6 (`::ffff:192.168.1.20`) must still match their card. Test in Task 3.
- Virtual adapters (VPN, Hyper-V) that report `Speed` 0 or -1 must not cause a warning or a crash. Test in Task 3.
- An old AMD card that refuses `quality=balanced` must fall back to `h264_amf` on the GPU, not to x264. Test in Task 2 (`ScreenVideoSourceTests`).
- Changing Quality with players connected must keep them in and send a keyframe, as Resolution does today. Manual check in Task 6's gate rows.

---

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/CouchLink.Core/Video/StreamQuality.cs` | Create | The `StreamQuality` enum and `StreamQualities` helpers (list, label, factor, encoder effort) |
| `src/CouchLink.Core/Video/StreamSettings.cs` | Modify | `Quality` property, bitrate factor, 100 Mbps cap |
| `src/CouchLink.Core/Video/EncoderChoice.cs` | Modify | Slower candidates, `Candidates(vendorId, quality)`, `CodecOf`, `IsHardware` |
| `src/CouchLink.Core/Net/LinkBudget.cs` | Create | `NicAddress`, matching clients to cards, the budget warning |
| `src/CouchLink.Video/Nv12Converter.cs` | Modify | Auto-processing off |
| `src/CouchLink.App/HostLinks.cs` | Create | Reads the PC's IPv4 card addresses and link speeds |
| `src/CouchLink.App/HostVideo.cs` | Modify | Candidates by Quality, summary with Quality, live `BitRate` |
| `src/CouchLink.App/HostService.cs` | Modify | Keeps settings and cards, `LinkWarning()`, log line |
| `src/CouchLink.App/Views/HostLobbyView.xaml(.cs)` | Modify | `QualityBox`, `BudgetWarning` |
| `src/CouchLink.VideoTest/Program.cs` | Modify | `--quality` option |
| `tests/CouchLink.Core.Tests/StreamSettingsTests.cs` | Modify | Quality bitrate tests |
| `tests/CouchLink.Core.Tests/EncoderChoiceTests.cs` | Modify | Candidate and option tests |
| `tests/CouchLink.Core.Tests/LinkBudgetTests.cs` | Create | Budget and card matching tests |
| `tests/CouchLink.Video.Tests/ScreenVideoSourceTests.cs` | Modify | Slower AMF falls back to fast AMF |
| `docs/cafe-setup-guide.md`, `ROADMAP.md`, `docs/gate-results.md` | Modify | Quality docs, 4:4:4 roadmap entry, manual gate |

Run all tests with: `dotnet test CouchLink.slnx`. The Video tests need FFmpeg in `third_party/ffmpeg` (`./eng/get-ffmpeg.ps1`).

---

### Task 1: Quality presets and the bitrate formula

**Files:**
- Create: `src/CouchLink.Core/Video/StreamQuality.cs`
- Modify: `src/CouchLink.Core/Video/StreamSettings.cs`
- Test: `tests/CouchLink.Core.Tests/StreamSettingsTests.cs`

**Interfaces:**
- Produces:
  - `public enum StreamQuality { Low = -1, Balanced = 0, High = 1, Max = 2 }` (Balanced is `default`)
  - `public static class StreamQualities` with `IReadOnlyList<StreamQuality> All`, `string Label(StreamQuality)`, `double Factor(StreamQuality)`, `bool UsesSlowerEncoder(StreamQuality)`
  - `StreamSettings(StreamResolution resolution, int frameRate, StreamQuality quality = StreamQuality.Balanced)` and `StreamQuality Quality { get; }`
  - `StreamSettings.MaxBitRate == 100_000_000`

- [ ] **Step 1: Write the failing tests**

In `tests/CouchLink.Core.Tests/StreamSettingsTests.cs`, change the capped row of `Bitrate_follows_size_and_frame_rate` (the old 30 Mbps cap no longer cuts it):

```csharp
    [InlineData(StreamResolution.Native, 240, 2560, 1080, 53_333_333)]  // was capped at 30 Mbps
```

In `Default_is_1080p_at_60`, add after the first assert:

```csharp
        Assert.Equal(StreamQuality.Balanced, StreamSettings.Default.Quality);
```

Add these tests to the class:

```csharp
    [Theory]
    [InlineData(StreamQuality.Low, StreamResolution.P1080, 60, 1920, 1080, 6_000_000)]
    [InlineData(StreamQuality.Balanced, StreamResolution.P1080, 60, 1920, 1080, 10_000_000)]
    [InlineData(StreamQuality.High, StreamResolution.P1080, 60, 1920, 1080, 25_000_000)]
    [InlineData(StreamQuality.Max, StreamResolution.P1080, 60, 1920, 1080, 50_000_000)]
    [InlineData(StreamQuality.Balanced, StreamResolution.Native, 60, 2560, 1440, 17_777_778)]
    [InlineData(StreamQuality.Max, StreamResolution.Native, 60, 2560, 1440, 88_888_889)]
    [InlineData(StreamQuality.High, StreamResolution.P1080, 144, 1920, 1080, 60_000_000)]
    [InlineData(StreamQuality.Max, StreamResolution.P1080, 144, 1920, 1080, 100_000_000)] // capped
    [InlineData(StreamQuality.Low, StreamResolution.P540, 60, 1920, 1080, 2_000_000)]     // floor
    public void Quality_scales_the_bitrate(StreamQuality quality, StreamResolution resolution, int fps, int w, int h, long expected)
    {
        var settings = new StreamSettings(resolution, fps, quality);
        Assert.Equal(expected, settings.BitRateFor(settings.SizeFor(w, h)));
    }

    [Fact]
    public void Qualities_are_listed_low_to_max_with_labels()
    {
        Assert.Equal([StreamQuality.Low, StreamQuality.Balanced, StreamQuality.High, StreamQuality.Max], StreamQualities.All);
        Assert.Equal(["Low", "Balanced", "High", "Max"], StreamQualities.All.Select(StreamQualities.Label));
    }

    [Theory]
    [InlineData(StreamQuality.Low, false)]
    [InlineData(StreamQuality.Balanced, false)]
    [InlineData(StreamQuality.High, true)]
    [InlineData(StreamQuality.Max, true)]
    public void Only_high_and_max_use_the_slower_encoder(StreamQuality quality, bool slower)
    {
        Assert.Equal(slower, StreamQualities.UsesSlowerEncoder(quality));
    }

    [Fact]
    public void Undefined_qualities_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StreamSettings(StreamResolution.P1080, 60, (StreamQuality)7));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~StreamSettingsTests"`
Expected: build FAILS with `The type or namespace name 'StreamQuality' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `src/CouchLink.Core/Video/StreamQuality.cs`:

```csharp
namespace CouchLink.Core.Video;

/// <summary>
/// How much the host spends on picture quality: a bitrate factor on top of resolution and frame
/// rate, and, at High and Max, a slower encoder setting. Balanced is today's stream and the default.
/// </summary>
public enum StreamQuality
{
    Low = -1,
    Balanced = 0,
    High = 1,
    Max = 2,
}

public static class StreamQualities
{
    public static IReadOnlyList<StreamQuality> All { get; } =
        [StreamQuality.Low, StreamQuality.Balanced, StreamQuality.High, StreamQuality.Max];

    public static string Label(StreamQuality quality) => quality switch
    {
        StreamQuality.Low => "Low",
        StreamQuality.Balanced => "Balanced",
        StreamQuality.High => "High",
        StreamQuality.Max => "Max",
        _ => throw new ArgumentOutOfRangeException(nameof(quality), $"Unknown quality {quality}."),
    };

    /// <summary>Multiplies the 10 Mbps at 1080p60 base bitrate.</summary>
    public static double Factor(StreamQuality quality) => quality switch
    {
        StreamQuality.Low => 0.6,
        StreamQuality.Balanced => 1,
        StreamQuality.High => 2.5,
        StreamQuality.Max => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(quality), $"Unknown quality {quality}."),
    };

    /// <summary>High and Max try the hardware encoder's slower, more detailed setting first.</summary>
    public static bool UsesSlowerEncoder(StreamQuality quality) =>
        quality is StreamQuality.High or StreamQuality.Max;
}
```

In `src/CouchLink.Core/Video/StreamSettings.cs`:

Replace the record's summary and the bitrate constants:

```csharp
/// <summary>
/// What the host streams: a resolution preset, a frame rate and a quality, chosen in the host lobby.
/// The bitrate follows from them: 10 Mbps at 1080p60, scaled by pixels, frame rate and quality.
/// </summary>
public sealed record StreamSettings
{
    public const long BaseBitRate = 10_000_000;
    public const long MinBitRate = 2_000_000;
    public const long MaxBitRate = 100_000_000;
```

Replace the constructor and add the property:

```csharp
    public StreamSettings(StreamResolution resolution, int frameRate, StreamQuality quality = StreamQuality.Balanced)
    {
        if (!Resolutions.Contains(resolution))
            throw new ArgumentOutOfRangeException(nameof(resolution), $"Unknown resolution {resolution}.");
        if (!CommonFrameRates.Contains(frameRate))
            throw new ArgumentOutOfRangeException(nameof(frameRate), $"Frame rate must be one of {string.Join(", ", CommonFrameRates)}.");
        if (!StreamQualities.All.Contains(quality))
            throw new ArgumentOutOfRangeException(nameof(quality), $"Unknown quality {quality}.");
        Resolution = resolution;
        FrameRate = frameRate;
        Quality = quality;
    }

    public StreamResolution Resolution { get; }
    public int FrameRate { get; }
    public StreamQuality Quality { get; }
```

Replace `BitRateFor`:

```csharp
    public long BitRateFor(VideoSize size)
    {
        double pixels = (double)size.Width * size.Height / (1920 * 1080);
        long bitRate = (long)Math.Round(BaseBitRate * pixels * FrameRate / 60.0 * StreamQualities.Factor(Quality));
        return Math.Clamp(bitRate, MinBitRate, MaxBitRate);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~StreamSettingsTests"`
Expected: PASS, all tests.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Video/StreamQuality.cs src/CouchLink.Core/Video/StreamSettings.cs tests/CouchLink.Core.Tests/StreamSettingsTests.cs
git commit -m "feat(core): stream quality presets scale the bitrate up to 100 Mbps"
```

---

### Task 2: Slower encoder candidates for High and Max

**Files:**
- Modify: `src/CouchLink.Core/Video/EncoderChoice.cs`
- Test: `tests/CouchLink.Core.Tests/EncoderChoiceTests.cs`
- Test: `tests/CouchLink.Video.Tests/ScreenVideoSourceTests.cs`

**Interfaces:**
- Consumes: `StreamQuality`, `StreamQualities.UsesSlowerEncoder` (Task 1).
- Produces:
  - `EncoderChoice.AmfBalanced = "h264_amf (balanced)"`, `EncoderChoice.NvencP3 = "h264_nvenc (p3)"`
  - `IReadOnlyList<string> EncoderChoice.Candidates(uint vendorId, StreamQuality quality = StreamQuality.Balanced)`
  - `EncoderChoice.Options(name)` accepts the two new names; `CodecOf` maps them; `IsHardware` is true for them.

- [ ] **Step 1: Write the failing tests**

Add to `tests/CouchLink.Core.Tests/EncoderChoiceTests.cs`:

```csharp
    [Theory]
    [InlineData(StreamQuality.High)]
    [InlineData(StreamQuality.Max)]
    public void Amd_at_high_quality_tries_AMF_balanced_first_then_the_usual_order(StreamQuality quality)
    {
        // An AMF that refuses quality=balanced still lands on the GPU, not on x264.
        Assert.Equal(["h264_amf (balanced)", "h264_amf", "h264_amf (lowlatency)", "libx264"],
            EncoderChoice.Candidates(0x1002, quality));
    }

    [Theory]
    [InlineData(StreamQuality.High)]
    [InlineData(StreamQuality.Max)]
    public void Nvidia_at_high_quality_tries_NVENC_p3_first(StreamQuality quality)
    {
        Assert.Equal(["h264_nvenc (p3)", "h264_nvenc", "libx264"], EncoderChoice.Candidates(0x10DE, quality));
    }

    [Theory]
    [InlineData(StreamQuality.Low)]
    [InlineData(StreamQuality.Balanced)]
    public void Low_and_balanced_keep_todays_candidates(StreamQuality quality)
    {
        Assert.Equal(["h264_amf", "h264_amf (lowlatency)", "libx264"], EncoderChoice.Candidates(0x1002, quality));
        Assert.Equal(["h264_nvenc", "libx264"], EncoderChoice.Candidates(0x10DE, quality));
    }

    [Fact]
    public void Other_GPUs_use_x264_at_every_quality()
    {
        Assert.Equal(["libx264"], EncoderChoice.Candidates(0x8086, StreamQuality.Max));
    }

    [Fact]
    public void AMF_balanced_is_AMF_ultra_low_latency_plus_quality_balanced()
    {
        Assert.Equal(
            [("usage", "ultralowlatency"), ("rc", "vbr_latency"), ("preanalysis", "false"),
             ("async_depth", "1"), ("bf", "0"), ("forced_idr", "1"), ("quality", "balanced")],
            EncoderChoice.Options("h264_amf (balanced)"));
    }

    [Fact]
    public void NVENC_p3_is_NVENC_with_preset_p3()
    {
        Assert.Equal(
            [("preset", "p3"), ("tune", "ull"), ("rc", "cbr"), ("zerolatency", "1"), ("forced-idr", "1")],
            EncoderChoice.Options("h264_nvenc (p3)"));
    }
```

In the same file, add rows to `Each_candidate_names_the_FFmpeg_encoder_it_opens`:

```csharp
    [InlineData("h264_amf (balanced)", "h264_amf")]
    [InlineData("h264_nvenc (p3)", "h264_nvenc")]
```

and add to `Only_x264_is_software`:

```csharp
        Assert.True(EncoderChoice.IsHardware("h264_amf (balanced)"));
        Assert.True(EncoderChoice.IsHardware("h264_nvenc (p3)"));
```

In `tests/CouchLink.Video.Tests/ScreenVideoSourceTests.cs`, make the `Source` helper use the settings' quality:

```csharp
    private ScreenVideoSource Source(StreamSettings? settings = null, params string[] failing) =>
        new(_capture, EncoderChoice.Candidates(EncoderChoice.AmdVendorId, (settings ?? StreamSettings.Default).Quality),
```

(the rest of the helper is unchanged) and add:

```csharp
    [Fact]
    public void An_AMF_that_refuses_the_slower_setting_falls_back_to_fast_AMF()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source(new StreamSettings(StreamResolution.P1080, 60, StreamQuality.High), EncoderChoice.AmfBalanced);

        Next(source);

        Assert.Equal(EncoderChoice.Amf, source.EncoderName);
        Assert.True(source.IsHardware);
        Assert.Contains(source.SkippedEncoders, s => s.StartsWith(EncoderChoice.AmfBalanced, StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~EncoderChoiceTests"`
Expected: build FAILS with `No overload for method 'Candidates' takes 2 arguments`.

- [ ] **Step 3: Write the implementation**

In `src/CouchLink.Core/Video/EncoderChoice.cs`, extend the class summary's last sentence:

```csharp
/// need forced IDR for a forced I-frame to be a real keyframe. High and Max quality try a slower,
/// more detailed setting of the hardware encoder first, then fall back to the fast one.
/// </summary>
```

Add the constants after `AmfLowLatency`:

```csharp
    /// <summary>AMF with quality=balanced, for High and Max stream quality.</summary>
    public const string AmfBalanced = "h264_amf (balanced)";
```

and after `Nvenc`:

```csharp
    /// <summary>NVENC with preset p3 instead of p1, for High and Max stream quality.</summary>
    public const string NvencP3 = "h264_nvenc (p3)";
```

Replace `Candidates`, `IsHardware`, `CodecOf` and `Options`:

```csharp
    public static IReadOnlyList<string> Candidates(uint vendorId, StreamQuality quality = StreamQuality.Balanced)
    {
        bool slower = StreamQualities.UsesSlowerEncoder(quality);
        return vendorId switch
        {
            AmdVendorId => slower ? [AmfBalanced, Amf, AmfLowLatency, Software] : [Amf, AmfLowLatency, Software],
            NvidiaVendorId => slower ? [NvencP3, Nvenc, Software] : [Nvenc, Software],
            _ => [Software],
        };
    }

    public static bool IsHardware(string encoder) => CodecOf(encoder) != Software;

    /// <summary>The FFmpeg encoder a candidate opens; a candidate is an encoder plus a set of options.</summary>
    public static string CodecOf(string encoder) => encoder switch
    {
        AmfLowLatency or AmfBalanced => Amf,
        NvencP3 => Nvenc,
        _ => encoder,
    };

    public static IReadOnlyList<(string Name, string Value)> Options(string encoder) => encoder switch
    {
        Amf => AmfOptions("ultralowlatency"),
        AmfLowLatency => AmfOptions("lowlatency"),
        AmfBalanced => [.. AmfOptions("ultralowlatency"), ("quality", "balanced")],
        Nvenc => NvencOptions("p1"),
        NvencP3 => NvencOptions("p3"),
        Software => [("preset", "ultrafast"), ("tune", "zerolatency")],
        _ => throw new ArgumentException($"Unknown encoder {encoder}.", nameof(encoder)),
    };

    private static (string Name, string Value)[] NvencOptions(string preset) =>
        [("preset", preset), ("tune", "ull"), ("rc", "cbr"), ("zerolatency", "1"), ("forced-idr", "1")];
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~EncoderChoiceTests"` then `dotnet test tests/CouchLink.Video.Tests --filter "FullyQualifiedName~ScreenVideoSourceTests"`
Expected: PASS, both.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Video/EncoderChoice.cs tests/CouchLink.Core.Tests/EncoderChoiceTests.cs tests/CouchLink.Video.Tests/ScreenVideoSourceTests.cs
git commit -m "feat(core): slower NVENC and AMF settings for high stream quality"
```

---

### Task 3: Link budget

**Files:**
- Create: `src/CouchLink.Core/Net/LinkBudget.cs`
- Test: `tests/CouchLink.Core.Tests/LinkBudgetTests.cs`

**Interfaces:**
- Consumes: `VideoShardPacket.MaxParityPercent` (existing, 20).
- Produces:
  - `public readonly record struct NicAddress(string Nic, IPAddress Address, int PrefixLength, long BitsPerSecond)`
  - `NicAddress? LinkBudget.CardFor(IPAddress client, IReadOnlyList<NicAddress> nics)`
  - `string? LinkBudget.Check(string quality, long bitRate, IEnumerable<IPAddress> clients, IReadOnlyList<NicAddress> nics)`

- [ ] **Step 1: Write the failing tests**

Create `tests/CouchLink.Core.Tests/LinkBudgetTests.cs`:

```csharp
using System.Net;
using CouchLink.Core.Net;

namespace CouchLink.Core.Tests;

public class LinkBudgetTests
{
    private static readonly NicAddress Lan100 = new("lan", IPAddress.Parse("192.168.1.10"), 24, 100_000_000);

    private static IPAddress[] Clients(int count) =>
        Enumerable.Range(20, count).Select(i => IPAddress.Parse($"192.168.1.{i}")).ToArray();

    [Fact]
    public void Warns_when_the_stream_needs_more_than_70_percent_of_the_link()
    {
        // 25 Mbps x 6 clients x 1.2 FEC = 180 Mbps on a 100 Mbps card.
        Assert.Equal(
            "High to 6 clients needs about 180 Mbps; this PC's network link is 100 Mbps. Lower Quality or Resolution.",
            LinkBudget.Check("High", 25_000_000, Clients(6), [Lan100]));
    }

    [Fact]
    public void Exactly_70_percent_is_fine_and_one_bit_more_warns()
    {
        var lan120 = Lan100 with { BitsPerSecond = 120_000_000 }; // 70 Mbps x 1.2 = 84 Mbps = 70%
        Assert.Null(LinkBudget.Check("Max", 70_000_000, Clients(1), [lan120]));
        Assert.Equal(
            "Max to 1 client needs about 84 Mbps; this PC's network link is 120 Mbps. Lower Quality or Resolution.",
            LinkBudget.Check("Max", 70_000_001, Clients(1), [lan120]));
    }

    [Fact]
    public void Gigabit_has_room_for_max_at_1080p60_with_a_full_room()
    {
        var gigabit = Lan100 with { BitsPerSecond = 1_000_000_000 }; // 50 x 9 x 1.2 = 540 Mbps
        Assert.Null(LinkBudget.Check("Max", 50_000_000, Clients(9), [gigabit]));
    }

    [Fact]
    public void No_clients_no_warning()
    {
        Assert.Null(LinkBudget.Check("Max", 100_000_000, [], [Lan100]));
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void A_card_with_an_unknown_speed_is_left_out(long speed)
    {
        Assert.Null(LinkBudget.Check("Max", 100_000_000, Clients(9), [Lan100 with { BitsPerSecond = speed }]));
    }

    [Fact]
    public void A_client_on_no_cards_subnet_is_left_out()
    {
        Assert.Null(LinkBudget.Check("Max", 100_000_000, [IPAddress.Parse("10.9.9.9")], [Lan100]));
    }

    [Fact]
    public void An_IPv4_mapped_client_address_matches_its_card()
    {
        Assert.Equal(Lan100, LinkBudget.CardFor(IPAddress.Parse("::ffff:192.168.1.20"), [Lan100]));
    }

    [Fact]
    public void Clients_are_counted_on_the_card_of_their_subnet_not_the_first_or_fastest()
    {
        var wifi = new NicAddress("wifi", IPAddress.Parse("10.0.0.5"), 24, 1_000_000_000);
        Assert.Equal(Lan100, LinkBudget.CardFor(IPAddress.Parse("192.168.1.20"), [wifi, Lan100]));
        Assert.Equal(
            "High to 6 clients needs about 180 Mbps; this PC's network link is 100 Mbps. Lower Quality or Resolution.",
            LinkBudget.Check("High", 25_000_000, Clients(6), [wifi, Lan100]));
    }

    [Fact]
    public void With_two_loaded_cards_the_worst_one_is_reported()
    {
        var second = new NicAddress("lan2", IPAddress.Parse("192.168.2.10"), 24, 100_000_000);
        IPAddress[] clients = [.. Clients(3), IPAddress.Parse("192.168.2.20")];
        // lan: 25 x 3 x 1.2 = 90 Mbps (90%); lan2: 30 Mbps (30%, fine).
        Assert.Equal(
            "High to 3 clients needs about 90 Mbps; this PC's network link is 100 Mbps. Lower Quality or Resolution.",
            LinkBudget.Check("High", 25_000_000, clients, [Lan100, second]));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~LinkBudgetTests"`
Expected: build FAILS with `The type or namespace name 'NicAddress' could not be found`.

- [ ] **Step 3: Write the implementation**

Create `src/CouchLink.Core/Net/LinkBudget.cs`:

```csharp
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>One IPv4 address of a network card, with the card's link speed in bits per second (0 or less when unknown).</summary>
public readonly record struct NicAddress(string Nic, IPAddress Address, int PrefixLength, long BitsPerSecond);

/// <summary>
/// Whether the video stream fits the host's network links. Every client gets its own copy plus up to
/// 20% FEC parity, so a card carries bitrate x its clients x 1.2. More than 70% of the link warns;
/// the host decides what to do. Clients on no known card, and cards of unknown speed, are left out.
/// </summary>
public static class LinkBudget
{
    public const int MaxSharePercent = 70;

    public static string? Check(string quality, long bitRate, IEnumerable<IPAddress> clients, IReadOnlyList<NicAddress> nics)
    {
        string? warning = null;
        double worst = 0;
        var cards = clients.Select(c => CardFor(c, nics)).Where(n => n is not null).Select(n => n!.Value);
        foreach (var group in cards.GroupBy(n => n.Nic))
        {
            long link = group.First().BitsPerSecond;
            int count = group.Count();
            long needed = bitRate * count * (100 + VideoShardPacket.MaxParityPercent) / 100;
            if (needed * 100 <= link * MaxSharePercent)
                continue;
            double share = (double)needed / link;
            if (share <= worst)
                continue;
            worst = share;
            string who = count == 1 ? "1 client" : $"{count} clients";
            warning = $"{quality} to {who} needs about {Mbps(needed)} Mbps; this PC's network link is {Mbps(link)} Mbps. " +
                "Lower Quality or Resolution.";
        }
        return warning;
    }

    /// <summary>The card whose IPv4 subnet holds the client, skipping cards of unknown speed.</summary>
    public static NicAddress? CardFor(IPAddress client, IReadOnlyList<NicAddress> nics)
    {
        if (client.IsIPv4MappedToIPv6)
            client = client.MapToIPv4();
        if (client.AddressFamily != AddressFamily.InterNetwork)
            return null;
        foreach (var nic in nics)
        {
            if (nic.BitsPerSecond > 0 && nic.Address.AddressFamily == AddressFamily.InterNetwork
                && SameSubnet(client, nic.Address, nic.PrefixLength))
                return nic;
        }
        return null;
    }

    private static bool SameSubnet(IPAddress a, IPAddress b, int prefixLength)
    {
        uint mask = prefixLength <= 0 ? 0 : prefixLength >= 32 ? uint.MaxValue : uint.MaxValue << (32 - prefixLength);
        return (ToUInt32(a) & mask) == (ToUInt32(b) & mask);
    }

    private static uint ToUInt32(IPAddress address) => BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());

    private static long Mbps(long bitsPerSecond) => (long)Math.Round(bitsPerSecond / 1e6);
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~LinkBudgetTests"`
Expected: PASS, all tests.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Net/LinkBudget.cs tests/CouchLink.Core.Tests/LinkBudgetTests.cs
git commit -m "feat(core): warn when the stream would outgrow the host's network link"
```

---

### Task 4: Host converter without driver auto-processing

**Files:**
- Modify: `src/CouchLink.Video/Nv12Converter.cs:36-41`

**Interfaces:** none (internal behavior).

This runs on the GPU's video processor, which has no unit test seam; the Video tests and `CouchLink.VideoTest` cover it.

- [ ] **Step 1: Turn auto-processing off**

In `Nv12Converter`'s constructor, inside the `using (var context1 = ...)` block, after the two color-space calls:

```csharp
            context1.VideoProcessorSetStreamColorSpace1(_processor, 0, ColorSpaceType.RgbFullG22NoneP709);
            context1.VideoProcessorSetOutputColorSpace1(_processor, ColorSpaceType.YcbcrStudioG22LeftP709);
            // Like the client's presenter: no driver noise reduction or edge enhancement before encoding.
            context1.VideoProcessorSetStreamAutoProcessingMode(_processor, 0, false);
```

Add "No driver auto-processing." to the class summary's last sentence:

```csharp
/// straight into the textures of FFmpeg's frame pool (zero CPU copy). BT.709 limited range, with no
/// driver auto-processing.
```

- [ ] **Step 2: Build and run the Video tests**

Run: `dotnet test tests/CouchLink.Video.Tests`
Expected: PASS.

- [ ] **Step 3: Encode on the real GPU**

Run: `dotnet run --project src/CouchLink.VideoTest -c Release -- encode 5`
Expected: prints `Encoder: h264_amf (hardware)` or `h264_nvenc (hardware)` and ends without `FAIL`.

- [ ] **Step 4: Commit**

```bash
git add src/CouchLink.Video/Nv12Converter.cs
git commit -m "fix(video): turn off driver auto-processing in the host converter"
```

---

### Task 5: Quality in the host lobby, with the link warning

**Files:**
- Create: `src/CouchLink.App/HostLinks.cs`
- Modify: `src/CouchLink.App/HostVideo.cs`
- Modify: `src/CouchLink.App/HostService.cs`
- Modify: `src/CouchLink.App/Views/HostLobbyView.xaml`
- Modify: `src/CouchLink.App/Views/HostLobbyView.xaml.cs`
- Modify: `src/CouchLink.VideoTest/Program.cs`

**Interfaces:**
- Consumes: `StreamQuality`, `StreamQualities` (Task 1); `EncoderChoice.Candidates(vendorId, quality)` (Task 2); `NicAddress`, `LinkBudget.Check` (Task 3).
- Produces: `HostLinks.Read()`, `HostVideo.BitRate`, `HostService.LinkWarning()`.

The App and VideoTest have no unit test project; this task is verified by building and by the manual run in Step 8.

- [ ] **Step 1: Read the network cards**

Create `src/CouchLink.App/HostLinks.cs`:

```csharp
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CouchLink.Core.Net;

namespace CouchLink.App;

/// <summary>This PC's IPv4 addresses with their card's link speed, for <see cref="LinkBudget"/>. Read once when hosting starts.</summary>
internal static class HostLinks
{
    public static IReadOnlyList<NicAddress> Read()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses
                    .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(u => new NicAddress(n.Id, u.Address, u.PrefixLength, n.Speed)))
                .ToList();
        }
        catch (NetworkInformationException e)
        {
            AppServices.Log.Write($"Network cards unreadable, no link warning: {e.Message}");
            return [];
        }
    }
}
```

- [ ] **Step 2: HostVideo uses the quality**

In `src/CouchLink.App/HostVideo.cs`, add a settings field and pass it through the private constructor:

```csharp
    private readonly VideoStreamer? _streamer;
    private readonly ScreenVideoSource? _screen;
    private readonly StreamSettings _settings;
    private readonly string _summary;

    private HostVideo(VideoStreamer? streamer, ScreenVideoSource? screen, StreamSettings settings, string summary)
    {
        _streamer = streamer;
        _screen = screen;
        _settings = settings;
        _summary = summary;
    }
```

Update the three `new HostVideo(...)` calls in `Start` and `Unavailable`:

```csharp
            return new HostVideo(Stream(pattern, onError), null, settings, $"test pattern at {settings.FrameRate} fps");
```

```csharp
            return new HostVideo(Stream(screen, onError), screen, settings, $"{summary}\n  {priority}");
```

```csharp
        if (!FfmpegLibrary.TryLoad(out var error))
            return Unavailable(settings, error!);
```

```csharp
            return Unavailable(settings, e.Message);
```

```csharp
    private static HostVideo Unavailable(StreamSettings settings, string reason)
    {
        AppServices.Log.Write($"Video unavailable: {reason}");
        return new HostVideo(null, null, settings, $"unavailable: {reason}");
    }
```

Pick candidates by quality and name the quality in the summary:

```csharp
            var screen = new ScreenVideoSource(screenCapture, EncoderChoice.Candidates(capture.VendorId, settings.Quality),
```

```csharp
            var summary = $"{screen.EncoderName} {screen.Size.Width}x{screen.Size.Height} at {settings.FrameRate} fps, " +
                $"{settings.BitRateFor(screen.Size) / 1e6:0.0} Mbps, {StreamQualities.Label(settings.Quality)} quality " +
                $"({(screen.IsHardware ? "hardware" : "software")}) on {capture.AdapterName}";
```

Add the live bitrate after `ReplyToTimingPing`:

```csharp
    /// <summary>The screen stream's bitrate per client; null for the test pattern or when video is unavailable.</summary>
    public long? BitRate => _screen is { } screen ? _settings.BitRateFor(screen.Size) : null;
```

- [ ] **Step 3: HostService keeps the settings and computes the warning**

In `src/CouchLink.App/HostService.cs`, add fields:

```csharp
    private readonly IReadOnlyList<NicAddress> _links = HostLinks.Read();
    private StreamSettings _settings;
```

In the private constructor, before `_video = HostVideo.Start(settings, OnVideoError);`:

```csharp
        _settings = settings;
```

Replace `ChangeSettings`:

```csharp
    /// <summary>Restarts video with new settings; clients stay in and get a keyframe. UI thread.</summary>
    public void ChangeSettings(StreamSettings settings)
    {
        lock (_media)
        {
            _settings = settings;
            _video.Dispose();
            _video = HostVideo.Start(settings, OnVideoError);
            foreach (var (slot, address) in _targets)
                _video.AddTarget(slot, address); // forces a keyframe
        }
        AppServices.Log.Write($"Stream settings changed: {StreamSettings.Label(settings.Resolution)} at {settings.FrameRate} fps, " +
            $"{StreamQualities.Label(settings.Quality)} quality");
    }

    /// <summary>A warning when the stream needs more than the host's network link can carry; null when it fits.</summary>
    public string? LinkWarning()
    {
        long? bitRate;
        List<IPAddress> clients;
        StreamQuality quality;
        lock (_media)
        {
            bitRate = _video.BitRate;
            clients = [.. _targets.Values];
            quality = _settings.Quality;
        }
        return bitRate is { } rate ? LinkBudget.Check(StreamQualities.Label(quality), rate, clients, _links) : null;
    }
```

- [ ] **Step 4: The lobby's Quality box and warning line**

In `src/CouchLink.App/Views/HostLobbyView.xaml`, replace the Stream row:

```xml
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,12,0,0">
            <TextBlock Text="Stream" VerticalAlignment="Center"/>
            <ComboBox x:Name="ResolutionBox" Width="90" Margin="8,0"/>
            <ComboBox x:Name="FrameRateBox" Width="80"/>
            <ComboBox x:Name="QualityBox" Width="100" Margin="8,0,0,0"/>
        </StackPanel>
        <TextBlock x:Name="BudgetWarning" DockPanel.Dock="Top" Margin="0,6,0,0" TextWrapping="Wrap"
                   Foreground="DarkOrange" Visibility="Collapsed"/>
```

In `src/CouchLink.App/Views/HostLobbyView.xaml.cs`, update the class summary's first line to include Quality:

```csharp
/// "Hosting on PC-03": stream settings (resolution, frame rate, quality) with a warning when the
/// network link is too slow, Allow everyone, the players with Kick, Stop hosting, and a
```

and keep the rest of that summary. In the constructor, after the frame rate loop:

```csharp
        foreach (var quality in StreamQualities.All)
            QualityBox.Items.Add(new ComboBoxItem { Content = StreamQualities.Label(quality), Tag = quality });
        QualityBox.SelectedIndex = StreamQualities.All.ToList().IndexOf(StreamSettings.Default.Quality);
```

In `TryStart`, after the `FrameRateBox.SelectionChanged` line:

```csharp
        QualityBox.SelectionChanged += (_, _) => _host?.ChangeSettings(CurrentSettings());
```

Replace `CurrentSettings`:

```csharp
    private StreamSettings CurrentSettings() => new(
        (StreamResolution)((ComboBoxItem)ResolutionBox.SelectedItem).Tag,
        (int)((ComboBoxItem)FrameRateBox.SelectedItem).Tag,
        (StreamQuality)((ComboBoxItem)QualityBox.SelectedItem).Tag);
```

At the end of `UpdateDetails` (it runs every 500 ms, so the warning follows players joining and leaving and settings changes):

```csharp
        var warning = _host.LinkWarning();
        BudgetWarning.Text = warning ?? "";
        BudgetWarning.Visibility = warning is null ? Visibility.Collapsed : Visibility.Visible;
```

- [ ] **Step 5: VideoTest `--quality`**

In `src/CouchLink.VideoTest/Program.cs`, update the usage line:

```csharp
    Console.WriteLine("Usage: VideoTest capture [seconds] | VideoTest encode [seconds] [--resolution=1080p] [--fps=60] [--quality=balanced] [--encoder=h264_amf] [--out=videotest.h264] [--normal-priority]");
```

In `Encode`, replace the settings line:

```csharp
    var quality = StreamQualities.All.First(q =>
        string.Equals(StreamQualities.Label(q), Option("quality", "Balanced"), StringComparison.OrdinalIgnoreCase));
    var settings = new StreamSettings(resolution, int.Parse(Option("fps", "60")), quality);
```

the candidates line:

```csharp
        : EncoderChoice.Candidates(capture.VendorId, settings.Quality);
```

and the encoder line:

```csharp
    Console.WriteLine($"Encoder: {source.EncoderName} ({(source.IsHardware ? "hardware" : "software")}), " +
        $"{source.Size.Width}x{source.Size.Height} at {settings.FrameRate} fps, {settings.BitRateFor(source.Size) / 1e6:0.0} Mbps target, " +
        $"{StreamQualities.Label(settings.Quality)} quality");
```

- [ ] **Step 6: Build and run all tests**

Run: `dotnet build CouchLink.slnx` then `dotnet test CouchLink.slnx`
Expected: build succeeds with no new warnings; all tests PASS.

- [ ] **Step 7: Measure encode time per quality**

Run each and note the encode-time percentiles the tool prints:
`dotnet run --project src/CouchLink.VideoTest -c Release -- encode 10 --quality=balanced`
`dotnet run --project src/CouchLink.VideoTest -c Release -- encode 10 --quality=high`
Expected: the High run prints `Encoder: h264_amf (balanced)` or `h264_nvenc (p3)` (or logs a `Skipped` line and uses the fast one), and its median encode time is under 1 ms above Balanced's. If it costs more, stop and report the numbers before going on.

- [ ] **Step 8: Run the app as host**

Run: `dotnet run --project src/CouchLink.App`, start hosting.
Expected: the Stream row shows `[1080p] [60 fps] [Balanced]`; Details shows `..., 10.0 Mbps, Balanced quality (hardware) ...`; switching to Max changes Details to `50.0 Mbps, Max quality` and the log has `Stream settings changed: 1080p at 60 fps, Max quality`; no warning line with no one joined.

- [ ] **Step 9: Commit**

```bash
git add src/CouchLink.App/HostLinks.cs src/CouchLink.App/HostVideo.cs src/CouchLink.App/HostService.cs src/CouchLink.App/Views/HostLobbyView.xaml src/CouchLink.App/Views/HostLobbyView.xaml.cs src/CouchLink.VideoTest/Program.cs
git commit -m "feat(app): stream quality setting in the host lobby, with a network link warning"
```

---

### Task 6: Docs and the manual gate

**Files:**
- Modify: `docs/cafe-setup-guide.md` (section 8)
- Modify: `ROADMAP.md` (Later)
- Modify: `docs/gate-results.md` (new section at the end)

**Interfaces:** none.

- [ ] **Step 1: Setup guide**

In `docs/cafe-setup-guide.md`, section 8, replace the first paragraph and the first bullet:

```markdown
The host lobby's **Stream** setting picks the resolution (Native, 1080p,
900p, 720p, 540p), the frame rate (60 fps, or higher up to the host
monitor's refresh rate) and the quality (Low, Balanced, High, Max). Higher
settings look sharper and need more from the host's GPU and the network.

- Start with **1080p** at **60 fps** and **Balanced**. On older or smaller
  GPUs, or if joining players see stutter, try **900p** or **720p**, or
  **Low**.
- On a gigabit network, **High** or **Max** makes text and fine detail
  sharper. Max at 1080p60 sends about 50 Mbps to each joining PC.
- If the stream needs more than the host PC's network link can carry, the
  lobby shows an orange warning under the Stream row with the numbers.
  Lower the quality or the resolution until it goes away. A 100 Mbps link
  fits Balanced at 1080p60 for up to 5 joining PCs.
```

(5 clients x 10 Mbps x 1.2 = 60 Mbps, under 70 Mbps; 6 would be 72 Mbps.)

- [ ] **Step 2: Roadmap**

In `ROADMAP.md`, under `## Later`, add:

```markdown
- Full color (4:4:4) at the top Quality step, for sharp colored text and edges. Needs NVENC on the host and a decode check on each joining PC.
```

- [ ] **Step 3: Gate**

Append to `docs/gate-results.md`:

```markdown
## Stream quality (Plan 9)

Date: <fill in>
Host / client PCs: <fill in>

| Check | Result |
|---|---|
| `VideoTest encode 10 --quality=high` on AMD: `h264_amf (balanced)` opens (or is skipped and `h264_amf` opens); median encode time within 1 ms of Balanced | <pass/fail, ms> |
| Same on NVIDIA with `h264_nvenc (p3)` | <pass/fail, ms> |
| AMF: `quality=balanced` encodes visibly better than Balanced at the same bitrate (else switch to `quality=quality`, spec 2.3) | <pass/fail> |
| Live session at Max on the gigabit switch: colored text sharper than at Balanced; F2 packet loss on the client no higher than at Balanced, keyframes included | <pass/fail> |
| Changing Quality while two clients play: both stay in, the picture comes back within a second | <pass/fail> |
| Host card forced to 100 Mbps: the orange warning appears as clients join at High, and goes away after switching to Balanced | <pass/fail> |
```

- [ ] **Step 4: Commit**

```bash
git add docs/cafe-setup-guide.md ROADMAP.md docs/gate-results.md
git commit -m "docs: stream quality in the setup guide, gate and roadmap"
```
