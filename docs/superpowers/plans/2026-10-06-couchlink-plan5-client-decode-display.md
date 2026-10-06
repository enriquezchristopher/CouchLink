# CouchLink Plan 5: Client Decode, Display and F2 Stats Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A client that joins a host sees the host's screen in a borderless fullscreen window: H.264 decoded on the GPU (D3D11VA, software fallback), presented through a flip-model swap chain with no vsync wait, with an F2 overlay showing fps, bitrate, packet loss, FEC repairs and estimated latency.

**Architecture:** Pure, unit-tested logic lives in `CouchLink.Core`: two small timing packets (a client ping and the host's reply carrying its capture-to-send delay), the round-trip and delay bookkeeping in `VideoClient`, a `DecodeGate.DecodeFailed()` hook so a decoder error resyncs on the next keyframe, and the overlay maths (`Letterbox`, `StatsWindow`, `OverlayText`). `CouchLink.Video` gets the GPU side: `H264Decoder` (FFmpeg, D3D11VA on the presenter's D3D11 device, software fallback), `PlayerCore` (decode every frame in order, present the newest, fall back to software, drop a backlog and resync, redraw status text while no frames come), `FramePresenter` (D3D11 video processor NV12 to BGRA with letterboxing and the right BT.601/709 colours, Direct2D text, `Present(0, AllowTearing)`), and `PlayerWindow`, a plain Win32 popup window on the player's own thread. `PlayerCore` is tested with a fake decoder and presenter; the GPU classes are checked on hardware with a new `VideoTest play` command. The app's Join opens the player; Ctrl+Alt+Q leaves.

**Tech Stack:** C# / .NET 10, WPF, FFmpeg 9.0 (BtbN GPL shared, pinned) through FFmpeg.AutoGen 9.0.1.1, Vortice.Direct3D11 / Vortice.DXGI / Vortice.Direct2D1 3.8.3, Win32 through `LibraryImport`, xUnit 2.9.3, Microsoft.Extensions.TimeProvider.Testing.

**Spec:** `docs/superpowers/specs/2026-10-05-couchlink-design.md` (section 4.1 Playing screen keys, section 5.4 Client playback, section 7 decode-fails row and Diagnostics). Task 9 updates sections 3, 5.4 and 7 for the timing packets and the overlay.

**Builds on:** Plan 4 (`main` @ 41bbf0b, release 1.2.0). Branch: `plan5-client-video`. Issues: #17, #18. This finishes v1.2.

## Spike findings (2026-10-06, this repo's dev PC)

A throwaway spike (`Spike5`, WinForms window, files recorded with `VideoTest encode`) ran the whole client path before this plan was written. The GPU code below is taken from it.

- **D3D11VA works with our own device.** Hand FFmpeg the presenter's `ID3D11Device` through `AV_HWDEVICE_TYPE_D3D11VA` (same as the encoder) and set `hw_device_ctx`; FFmpeg's default `get_format` then picks `AV_PIX_FMT_D3D11`. Frames come back as `data[0]` = `ID3D11Texture2D*` (an NV12 texture array) and `data[1]` = the array slice. Both the AMF stream (2560x1080, BT.709 tagged) and an x264 stream decode.
- **Decoded textures go straight into the video processor.** An input view per (texture, slice), cached, works on the AMD driver. NV12 to BGRA, scaling and letterboxing happen in one `VideoProcessorBlt` into the swap chain's back buffer. Colours are right with stream colour space `YcbcrStudioG22LeftP709` and output `RgbFullG22NoneP709` (checked by screenshot against FFmpeg's `testsrc2`).
- **Timings, 1080p60 high-motion stream, D3D11VA, borderless 2560x1080:** packet in to GPU work finished (event query) p50 3.1 ms, p95 3.6 ms. CPU side (decode call + blt + Present) p50 0.5 ms. Decode is asynchronous on the GPU, so the CPU-side number alone is misleading. Unpaced it runs ~280-540 fps.
- **Software decode** (`thread_type = FF_THREAD_SLICE`, `thread_count = 0`): p50 1.0 ms decode for the x264 1080p stream on the i3-8100; with upload, blt and present p50 2.9 ms to GPU done.
- **Software upload needs a staging texture.** The video processor rejects a `Dynamic` NV12 texture as input (`E_INVALIDARG`), and `Dynamic` with no bind flag can't be created. Write the planes into a `Staging` NV12 texture (`Map(Write)`: luma rows, then interleaved UV rows at `pitch * height`), `CopyResource` into a `Default` NV12 texture with `BindFlags = ShaderResource | RenderTarget`, and make the input view on that.
- **Flip model with tearing:** `IDXGIFactory5.PresentAllowTearing` is true here; swap chain `FlipDiscard`, 2 buffers, `SwapChainFlags.AllowTearing`, `Present(0, PresentFlags.AllowTearing)`. In the borderless fullscreen window frames appear as soon as they are presented.
- **Sleep granularity:** the spike's 60 fps pacing ran at 32 fps until `timeBeginPeriod(1)`, the same cause Plan 4 hit. The player doesn't sleep between frames: it waits on an event that the receive thread signals.
- Colour tags: the AMF stream says `tv, bt709`; the x264 stream (`sws_scale`, BT.601 matrix) says `smpte170m`. A stream with no tag is treated as BT.601 limited range.

## Global Constraints

- C#, .NET 10. `CouchLink.Core` stays `net10.0` with no Windows APIs; GPU, FFmpeg and Win32 code lives in `CouchLink.Video` (`net10.0-windows`, `x64`, unsafe allowed). `TreatWarningsAsErrors` everywhere.
- Commits are signed. No Claude attribution trailers in commit messages or PR text.
- Client playback (spec 5.4): FFmpeg **D3D11VA** hardware decode with **software decode fallback**; the decoded frame stays on the GPU and is presented directly (**D3D11 flip model**, Vortice.Windows), **no vsync wait**. Video plays as soon as it arrives; no A/V sync.
- **Latency target ~20-35 ms** host screen to client screen on wired gigabit.
- "Client hardware decode fails" -> **switch to software decode** (spec 7).
- Playing screen (spec 4.1): **fullscreen stream; Ctrl+Alt+Q leaves; F2 shows stats.** F1 help, Win-key/Alt+Tab blocking and mouse lock are #24 (v1.4), not this plan.
- F2 overlay (spec 7, #18): **fps, bitrate, packet loss, FEC repairs, estimated latency**; it must not add latency noticeably (drawn into the same back buffer before the same Present).
- Status messages, exactly: **"Waiting for the host's picture..."** before the first frame, **"Host screen paused"** while frames carry the Paused flag.
- New UDP packet types use the existing 4-byte `Wire` header: type **4 = timing ping** (client -> host input port 47803), type **5 = timing reply** (host -> client video port 47802). Wire version stays 1; old peers ignore unknown types.

## Review Focus

1. **Hardware decode is unavailable or fails mid-stream** (old GPU, basic display driver, a driver reset). The client switches to software decode, asks for a keyframe, and keeps playing. Pinned in Task 5 (`Falls_back_to_software_without_a_device`) and Task 6 (`A_hardware_decode_error_switches_to_software_and_resyncs`).
2. **The client can't keep up** (software decode of 1440p at 120 fps on a slow PC). The backlog is dropped and the picture resyncs on the next keyframe instead of drifting seconds behind. Pinned in Task 6 (`A_backlog_is_dropped_and_resyncs_on_a_keyframe`).
3. **No picture to show** (before the first frame, host paused, host gone quiet). The window says why instead of staying black or frozen without a word. Pinned in Task 6 (`Shows_waiting_text_before_the_first_frame`, `Shows_paused_text_while_the_host_is_paused`, `Redraws_while_no_frames_arrive`).
4. **The stream size changes mid-session** (the host's encoder reopens at a new size after a host resolution change). The client keeps showing a correctly letterboxed picture. Pinned in Task 4 (`Letterbox` tests) and checked on hardware in Task 7 (`VideoTest play` with two files of different sizes).
5. **The player window loses focus** (Alt+Tab, a notification, a click on another monitor). Keys are released and no input is sent while another app is in front; input resumes when the player is back. Checked by hand in Task 8 (Step 7).

---

## File Structure

```
src/CouchLink.Core/Protocol/
  Wire.cs                   (modify) types 4 and 5
  TimingPing.cs             client -> host: slot + client timestamp
  TimingReply.cs            host -> client: echoed timestamp + host delay
src/CouchLink.Core/Net/
  InputReceiver.cs          (modify) hands timing pings to a callback
  InputSender.cs            (modify) SendTimingPing
src/CouchLink.Core/Video/
  IEncodedVideoSource.cs    (modify) EncodedFrame gets CaptureToEncoded
  VideoStreamer.cs          (modify) HostDelay, ReplyToTimingPing
  FrameAssembler.cs         (modify) AssembledFrame gets AssemblyTime
  DecodeGate.cs             (modify) DecodeFailed
  VideoClient.cs            (modify) pings, round trip, host delay, DecodeFailed
  Letterbox.cs              fit a picture into a window
  StatsWindow.cs            per-second rates from cumulative counters
  OverlayText.cs            F2 overlay lines and status messages
src/CouchLink.Video/
  ScreenVideoSource.cs      (modify) sets CaptureToEncoded
  StreamColor.cs            FFmpeg colour tags -> DXGI colour space
  H264Decoder.cs            FFmpeg H.264 decoder, D3D11VA or software
  H264Frames.cs             splits a saved H.264 file into frames (VideoTest play)
  IPlayerParts.cs           IFrameDecoder, IFramePresenter, DecodedPicture, PlayerFrame
  PlayerCore.cs             decode/present loop logic (thread-free, testable)
  FramePresenter.cs         swap chain, video processor, Direct2D overlay
  PlayerWindow.cs           Win32 borderless popup window
  VideoPlayer.cs            player thread: window + presenter + decoder + PlayerCore
src/CouchLink.VideoTest/Program.cs   (modify) play command
src/CouchLink.App/
  ClientVideoService.cs     (modify) feeds the player
  Input/RawInputSource.cs   (modify) input sink + "is a CouchLink window in front" check
  MainWindow.xaml.cs        (modify) open player on Join, leave on Ctrl+Alt+Q
src/CouchLink.Core/DevOptions.cs     (modify) --windowed-player
tests/CouchLink.Core.Tests/
  TimingPacketTests.cs, LetterboxTests.cs, StatsWindowTests.cs, OverlayTextTests.cs
  (modify) DecodeGateTests.cs, FrameAssemblerTests.cs, VideoClientTimingTests.cs (new), UdpInputTests.cs,
           VideoStreamerTimingTests.cs (new), DevOptionsTests.cs
tests/CouchLink.Video.Tests/
  TestStreams.cs            encodes small x264 test streams in memory
  H264DecoderTests.cs, StreamColorTests.cs, PlayerCoreTests.cs, H264FramesTests.cs
  (modify) ScreenVideoSourceTests.cs, Fakes.cs
```

---
### Task 1: Timing packets

The client measures the network round trip with a ping that the host echoes, and the host's reply carries its own capture-to-send delay. Together with the client's own receive-to-present time, that gives the overlay's estimated latency without syncing the two PCs' clocks.

**Files:**
- Modify: `src/CouchLink.Core/Protocol/Wire.cs`
- Create: `src/CouchLink.Core/Protocol/TimingPing.cs`, `src/CouchLink.Core/Protocol/TimingReply.cs`
- Test: `tests/CouchLink.Core.Tests/TimingPacketTests.cs`

**Interfaces:**
- Produces: `Wire.TypeTimingPing = 4`, `Wire.TypeTimingReply = 5`;
  `readonly record struct TimingPing(byte Slot, long ClientTicks)` with `const int Size = 16`, `void WriteTo(Span<byte>)`, `static bool TryParse(ReadOnlySpan<byte>, out TimingPing)`;
  `readonly record struct TimingReply(long ClientTicks, TimeSpan HostDelay)` with `const int Size = 16` and the same `WriteTo`/`TryParse`. `HostDelay` travels as whole microseconds (uint32, clamped to 0..uint.MaxValue).

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/TimingPacketTests.cs`:

```csharp
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class TimingPacketTests
{
    [Fact]
    public void A_ping_round_trips()
    {
        var bytes = new byte[TimingPing.Size];
        new TimingPing(Slot: 7, ClientTicks: 123_456_789_012).WriteTo(bytes);

        Assert.True(TimingPing.TryParse(bytes, out var ping));
        Assert.Equal(new TimingPing(7, 123_456_789_012), ping);
    }

    [Fact]
    public void A_reply_round_trips_with_the_host_delay_in_microseconds()
    {
        var bytes = new byte[TimingReply.Size];
        new TimingReply(ClientTicks: -5, HostDelay: TimeSpan.FromMicroseconds(8_250)).WriteTo(bytes);

        Assert.True(TimingReply.TryParse(bytes, out var reply));
        Assert.Equal(-5, reply.ClientTicks);
        Assert.Equal(TimeSpan.FromMicroseconds(8_250), reply.HostDelay);
    }

    [Fact]
    public void A_negative_host_delay_is_sent_as_zero()
    {
        var bytes = new byte[TimingReply.Size];
        new TimingReply(1, TimeSpan.FromMilliseconds(-3)).WriteTo(bytes);
        Assert.True(TimingReply.TryParse(bytes, out var reply));
        Assert.Equal(TimeSpan.Zero, reply.HostDelay);
    }

    [Fact]
    public void Other_packets_are_not_timing_packets()
    {
        var ping = new byte[TimingPing.Size];
        new TimingPing(2, 9).WriteTo(ping);
        var keyframe = new byte[KeyframeRequest.Size];
        new KeyframeRequest(2).WriteTo(keyframe);

        Assert.False(TimingReply.TryParse(ping, out _));     // right size, wrong type
        Assert.False(TimingPing.TryParse(keyframe, out _));  // wrong size
        Assert.False(TimingPing.TryParse(ping.AsSpan(0, 15), out _));
        Assert.False(KeyframeRequest.TryParse(ping, out _)); // older code ignores it
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter TimingPacketTests`
Expected: build FAILS: `TimingPing` and `TimingReply` don't exist.

- [ ] **Step 3: Implement**

In `Wire.cs`, after `TypeKeyframeRequest`:

```csharp
    public const byte TypeTimingPing = 4;
    public const byte TypeTimingReply = 5;
```

`src/CouchLink.Core/Protocol/TimingPing.cs`:

```csharp
using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>
/// Client -> host input port: "echo this back" (16 bytes: header, slot, 3 reserved zero bytes,
/// the client's clock in ticks). The reply's arrival time gives the network round trip.
/// </summary>
public readonly record struct TimingPing(byte Slot, long ClientTicks)
{
    public const int Size = 16;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"Need {Size} bytes.", nameof(destination));

        Wire.WriteHeader(destination, Wire.TypeTimingPing);
        destination[4] = Slot;
        destination[5..8].Clear();
        BinaryPrimitives.WriteInt64LittleEndian(destination[8..], ClientTicks);
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out TimingPing ping)
    {
        ping = default;
        if (source.Length != Size || !Wire.HasHeader(source, Wire.TypeTimingPing))
            return false;
        ping = new TimingPing(source[4], BinaryPrimitives.ReadInt64LittleEndian(source[8..]));
        return true;
    }
}
```

`src/CouchLink.Core/Protocol/TimingReply.cs`:

```csharp
using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>
/// Host -> client video port: the ping's client ticks echoed back, plus the host's recent
/// capture-to-send delay (16 bytes: header, delay in microseconds, the echoed ticks).
/// </summary>
public readonly record struct TimingReply(long ClientTicks, TimeSpan HostDelay)
{
    public const int Size = 16;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"Need {Size} bytes.", nameof(destination));

        Wire.WriteHeader(destination, Wire.TypeTimingReply);
        double micros = Math.Clamp(HostDelay.TotalMicroseconds, 0, uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], (uint)micros);
        BinaryPrimitives.WriteInt64LittleEndian(destination[8..], ClientTicks);
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out TimingReply reply)
    {
        reply = default;
        if (source.Length != Size || !Wire.HasHeader(source, Wire.TypeTimingReply))
            return false;
        reply = new TimingReply(
            BinaryPrimitives.ReadInt64LittleEndian(source[8..]),
            TimeSpan.FromMicroseconds(BinaryPrimitives.ReadUInt32LittleEndian(source[4..])));
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter TimingPacketTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Protocol tests/CouchLink.Core.Tests/TimingPacketTests.cs
git commit -S -m "feat(core): timing ping and reply packets" -m "Refs #18"
```

---

### Task 2: Host side of the timing

The host stamps each encoded frame with how long ago its screen image was captured, keeps a smoothed capture-to-send delay, and answers timing pings with it.

**Files:**
- Modify: `src/CouchLink.Core/Video/IEncodedVideoSource.cs` (EncodedFrame)
- Modify: `src/CouchLink.Video/ScreenVideoSource.cs`
- Modify: `src/CouchLink.Core/Video/VideoStreamer.cs`
- Modify: `src/CouchLink.Core/Net/InputReceiver.cs`, `src/CouchLink.Core/Net/InputSender.cs`
- Modify: `src/CouchLink.App/HostVideo.cs`, `src/CouchLink.App/HostInputService.cs`
- Test: `tests/CouchLink.Video.Tests/ScreenVideoSourceTests.cs`, `tests/CouchLink.Core.Tests/VideoStreamerTimingTests.cs`, `tests/CouchLink.Core.Tests/UdpVideoTests.cs`

**Interfaces:**
- Consumes: `TimingPing`, `TimingReply` (Task 1).
- Produces:
  - `EncodedFrame(ReadOnlyMemory<byte> Data, bool Keyframe, bool Paused = false, TimeSpan CaptureToEncoded = default)`.
  - `VideoStreamer.HostDelay` (`TimeSpan`, smoothed with weight 1/8 per frame) and `VideoStreamer.ReplyToTimingPing(TimingPing ping, IPAddress from)`, which sends one `TimingReply` to `from` on the streamer's video port.
  - `InputReceiver.RunAsync(onPacket, ct, onError = null, onKeyframeRequest = null, Action<TimingPing, IPAddress>? onTimingPing = null)`.
  - `InputSender.SendTimingPing(long clientTicks)`.
  - `HostVideo.ReplyToTimingPing(TimingPing, IPAddress)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/CouchLink.Video.Tests/ScreenVideoSourceTests.cs`:

```csharp
    [Fact]
    public void A_frame_says_how_long_ago_its_image_was_captured()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();
        Next(source); // frame 1 at t=0
        _opened[0].OnEncode = () => _time.Advance(TimeSpan.FromMilliseconds(4));

        _time.Advance(TimeSpan.FromMilliseconds(6));
        _capture.Script.Enqueue(CaptureStatus.NewFrame); // new image at t=6, held until the slot
        var frame = Next(source);

        // captured at 6 ms; slot at 16.67 ms; encoding ends 4 ms later
        Assert.Equal(Interval60 + TimeSpan.FromMilliseconds(4 - 6), frame.CaptureToEncoded);
    }

    [Fact]
    public void A_repeated_image_counts_from_its_slot()
    {
        _capture.Script.Enqueue(CaptureStatus.NewFrame);
        using var source = Source();
        Next(source);
        _opened[0].OnEncode = () => _time.Advance(TimeSpan.FromMilliseconds(3));

        var repeat = Next(source); // still screen: the slot re-sends the last image

        Assert.Equal(TimeSpan.FromMilliseconds(3), repeat.CaptureToEncoded);
    }
```

Create `tests/CouchLink.Core.Tests/VideoStreamerTimingTests.cs`:

```csharp
using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class VideoStreamerTimingTests
{
    private sealed class RecordingSender : IVideoPacketSender
    {
        public List<(byte[] Packet, IPEndPoint Target)> Sent { get; } = [];

        public void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets)
        {
            lock (Sent)
                foreach (var p in packets)
                    foreach (var t in targets)
                        Sent.Add((p, t));
        }

        public void Dispose() { }
    }

    /// <summary>Hands out a frame every 5 ms whose image is always 8 ms old.</summary>
    private sealed class AgedSource : IEncodedVideoSource
    {
        public bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame)
        {
            Thread.Sleep(5);
            frame = new EncodedFrame(new byte[100], forceKeyframe, CaptureToEncoded: TimeSpan.FromMilliseconds(8));
            return true;
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task Host_delay_follows_the_frames_capture_age()
    {
        using var streamer = new VideoStreamer(new AgedSource(), new RecordingSender(), 47802, TimeProvider.System);
        streamer.ClientSeen(2, IPAddress.Loopback);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (streamer.Stats.FramesSent < 60 && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        // 8 ms of capture age plus a little packetizing time
        Assert.InRange(streamer.HostDelay.TotalMilliseconds, 7.5, 12);
    }

    [Fact]
    public void A_timing_ping_is_answered_on_the_video_port()
    {
        var sender = new RecordingSender();
        using var streamer = new VideoStreamer(new AgedSource(), sender, 47802, TimeProvider.System);
        var from = IPAddress.Parse("192.168.1.23");

        streamer.ReplyToTimingPing(new TimingPing(3, ClientTicks: 4242), from);

        (byte[] Packet, IPEndPoint Target) reply;
        lock (sender.Sent)
            reply = sender.Sent.Single(s => TimingReply.TryParse(s.Packet, out _));
        Assert.Equal(new IPEndPoint(from, 47802), reply.Target);
        Assert.True(TimingReply.TryParse(reply.Packet, out var parsed));
        Assert.Equal(4242, parsed.ClientTicks);
    }
}
```

Add to `tests/CouchLink.Core.Tests/UdpVideoTests.cs`:

```csharp
    [Fact]
    public async Task Timing_pings_reach_the_host_with_the_sender_address()
    {
        using var receiver = new InputReceiver(port: 0);
        var pings = Channel.CreateUnbounded<(TimingPing Ping, IPAddress From)>();
        using var cts = new CancellationTokenSource();
        var loop = receiver.RunAsync((_, _) => { }, cts.Token,
            onTimingPing: (p, from) => pings.Writer.TryWrite((p, from)));

        using var sender = new InputSender(new IPEndPoint(IPAddress.Loopback, receiver.LocalPort), slot: 4);
        sender.SendTimingPing(clientTicks: 77);

        using var wait = new CancellationTokenSource(Timeout);
        var ping = await pings.Reader.ReadAsync(wait.Token);
        Assert.Equal(new TimingPing(4, 77), ping.Ping);
        Assert.Equal(IPAddress.Loopback, ping.From);

        cts.Cancel();
        await loop.WaitAsync(Timeout);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS: `CaptureToEncoded`, `HostDelay`, `ReplyToTimingPing`, `onTimingPing` and `SendTimingPing` don't exist.

- [ ] **Step 3: Implement**

`IEncodedVideoSource.cs`, change the record:

```csharp
/// <summary>
/// One encoded frame. <see cref="CaptureToEncoded"/> is how long before the end of encoding its
/// screen image was captured (zero when unknown, e.g. the test pattern).
/// </summary>
public readonly record struct EncodedFrame(
    ReadOnlyMemory<byte> Data, bool Keyframe, bool Paused = false, TimeSpan CaptureToEncoded = default);
```

`ScreenVideoSource.cs`: add a field `private TimeSpan? _imageAt; // when the oldest unsent screen change was captured`. In `TryGetFrame`'s `else` branch, right after the `if (Paused) { ... }` block:

```csharp
            if (status == CaptureStatus.NewFrame)
                _imageAt ??= Now;
```

and replace `frame = encoded with { Paused = Paused };` with:

```csharp
        var imageAt = _imageAt is { } at && at <= slot ? at : slot; // a repeat is as old as its slot
        frame = encoded with { Paused = Paused, CaptureToEncoded = Now - imageAt };
        _imageAt = null;
```

(`_lastSent = ...` and `return true;` stay after it.)

`VideoStreamer.cs` (add `using CouchLink.Core.Protocol;`):

```csharp
    private readonly int _videoPort;
    private long _hostDelayTicks; // smoothed; read from other threads
```

In the constructor, `_videoPort = videoPort;`. Add:

```csharp
    /// <summary>Smoothed time from a frame's screen capture to its first packet going out.</summary>
    public TimeSpan HostDelay => TimeSpan.FromTicks(Interlocked.Read(ref _hostDelayTicks));

    /// <summary>Answers a client's timing ping on its video port. Any thread.</summary>
    public void ReplyToTimingPing(TimingPing ping, IPAddress from)
    {
        var reply = new byte[TimingReply.Size];
        new TimingReply(ping.ClientTicks, HostDelay).WriteTo(reply);
        _sender.Send([reply], [new IPEndPoint(from, _videoPort)]);
    }
```

In `StreamOneFrame`, add `var got = Now;` right after `TryGetFrame` returns true, and right before `_sender.Send(packets, targets);`:

```csharp
        var delay = frame.CaptureToEncoded + (Now - got); // capture age + packetizing, up to the first send
        long old = Interlocked.Read(ref _hostDelayTicks);
        Interlocked.Exchange(ref _hostDelayTicks, old == 0 ? delay.Ticks : old + (delay.Ticks - old) / 8);
```

`VideoSender.Send` is now called from the streamer thread and from the input receive thread. Sending on one UDP socket from two threads is safe, and each call sends whole datagrams.

`InputReceiver.RunAsync`: add the parameter `Action<TimingPing, IPAddress>? onTimingPing = null` after `onKeyframeRequest`, and one more branch:

```csharp
            else if (onTimingPing is not null && TimingPing.TryParse(result.Buffer, out var ping))
                onTimingPing(ping, from);
```

and say "each valid input packet, keyframe request and timing ping" in its doc comment.

`InputSender.cs`:

```csharp
    private readonly byte[] _timingPing = new byte[TimingPing.Size];

    /// <summary>Sends a timing ping stamped with the caller's clock. Call from one thread at a time.</summary>
    public void SendTimingPing(long clientTicks)
    {
        new TimingPing(_slot, clientTicks).WriteTo(_timingPing);
        try
        {
            _udp.Send(_timingPing, _timingPing.Length);
        }
        catch (SocketException)
        {
            // Host not reachable right now; the next ping, a second later, tries again.
        }
    }
```

`HostVideo.cs` (add `using CouchLink.Core.Protocol;`):

```csharp
    public void ReplyToTimingPing(TimingPing ping, IPAddress from) => _streamer?.ReplyToTimingPing(ping, from);
```

`HostInputService.cs`, the receive loop:

```csharp
        _receiveLoop = _receiver.RunAsync(OnInput, _cts.Token, OnError,
            onKeyframeRequest: (_, _) => _video.RequestKeyframe(),
            onTimingPing: _video.ReplyToTimingPing);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test`
Expected: all pass, with 3 new Core tests and 2 new Video tests.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -S -m "feat(core): host measures capture-to-send delay and answers timing pings" -m "Refs #18"
```

---

### Task 3: Client side of the timing, and resync after a decode error

**Files:**
- Modify: `src/CouchLink.Core/Video/FrameAssembler.cs` (AssembledFrame, PendingFrame, Finish)
- Modify: `src/CouchLink.Core/Video/DecodeGate.cs`
- Modify: `src/CouchLink.Core/Video/VideoClient.cs`
- Modify: `src/CouchLink.App/ClientVideoService.cs`
- Test: `tests/CouchLink.Core.Tests/FrameAssemblerTests.cs`, `tests/CouchLink.Core.Tests/DecodeGateTests.cs`, `tests/CouchLink.Core.Tests/VideoClientTimingTests.cs`

**Interfaces:**
- Consumes: `TimingReply` (Task 1); `InputSender.SendTimingPing(long)` (Task 2).
- Produces:
  - `AssembledFrame(uint Number, bool Keyframe, byte[] Data, bool Paused = false, TimeSpan AssemblyTime = default)`: from the frame's first packet to the packet that completed it.
  - `DecodeGate.DecodeFailed()`: waits for the next keyframe and asks for it, as after a loss.
  - `VideoClient(VideoReceiver receiver, Action requestKeyframe, Action<AssembledFrame> onFrame, TimeProvider time, Action<Exception>? onError = null, Action<long>? sendTimingPing = null)`; with `sendTimingPing` it pings every `VideoClient.PingInterval` (1 s), stamped with its own `Now.Ticks`.
  - `VideoClient.DecodeFailed()` (any thread).
  - `VideoClientStats` gains `TimeSpan? RoundTrip = null` (smoothed, weight 1/4; null before the first reply) and `TimeSpan HostDelay = default` (from the latest reply).

- [ ] **Step 1: Write the failing tests**

Add to `FrameAssemblerTests.cs`:

```csharp
    [Fact]
    public void A_frame_reports_how_long_its_packets_took_to_arrive()
    {
        var packets = new FramePacketizer(streamId: 9).Packetize(0, new byte[5000], keyframe: true);
        var a = new FrameAssembler();
        AssembledFrame? done = null;
        for (int i = 0; i < packets.Count && done is null; i++)
            done = a.Add(packets[i], TimeSpan.FromMilliseconds(10 + i)); // 1 ms apart

        Assert.NotNull(done);
        Assert.Equal(TimeSpan.FromMilliseconds(4), done.AssemblyTime); // 5 data shards: packets 0-4
    }
```

Add to `DecodeGateTests.cs`:

```csharp
    [Fact]
    public void A_decode_failure_waits_for_the_next_keyframe_and_asks_for_it()
    {
        var gate = new DecodeGate();
        Assert.True(gate.Accept(F(1, keyframe: true)));
        Assert.False(gate.ShouldRequestKeyframe(Ms(0)));

        gate.DecodeFailed();

        Assert.True(gate.WaitingForKeyframe);
        Assert.False(gate.Accept(F(2)));
        Assert.True(gate.ShouldRequestKeyframe(Ms(10)));
        Assert.True(gate.Accept(F(3, keyframe: true)));
    }
```

Create `tests/CouchLink.Core.Tests/VideoClientTimingTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public class VideoClientTimingTests
{
    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }

    [Fact]
    public void Pings_go_out_once_a_second_with_the_clients_clock()
    {
        var time = new FakeTimeProvider();
        var pings = new List<long>();
        using var client = new VideoClient(new VideoReceiver(port: 0), () => { }, _ => { }, time,
            sendTimingPing: t => { lock (pings) pings.Add(t); });

        for (int i = 0; i < 25; i++)
            time.Advance(VideoClient.TickInterval); // 1.25 s

        lock (pings)
        {
            Assert.Equal(2, pings.Count); // at the first tick, then 1 s later
            Assert.Equal(VideoClient.PingInterval.Ticks, pings[1] - pings[0]);
        }
    }

    [Fact]
    public async Task A_reply_sets_the_round_trip_and_the_host_delay()
    {
        var time = new FakeTimeProvider();
        var receiver = new VideoReceiver(port: 0);
        int port = receiver.LocalPort;
        long? sentAt = null;
        using var client = new VideoClient(receiver, () => { }, _ => { }, time,
            sendTimingPing: t => sentAt ??= t);
        time.Advance(VideoClient.TickInterval); // first ping
        Assert.NotNull(sentAt);

        time.Advance(TimeSpan.FromMilliseconds(2)); // the reply comes back 2 ms later
        var reply = new byte[TimingReply.Size];
        new TimingReply(sentAt.Value, TimeSpan.FromMilliseconds(9)).WriteTo(reply);
        using var udp = new UdpClient();
        udp.Send(reply, reply.Length, new IPEndPoint(IPAddress.Loopback, port));

        await Until(() => client.Stats.RoundTrip is not null);
        Assert.Equal(TimeSpan.FromMilliseconds(2), client.Stats.RoundTrip);
        Assert.Equal(TimeSpan.FromMilliseconds(9), client.Stats.HostDelay);
    }

    [Fact]
    public async Task DecodeFailed_asks_the_host_for_a_keyframe_at_once()
    {
        var time = new FakeTimeProvider();
        int requests = 0, frames = 0;
        var receiver = new VideoReceiver(port: 0);
        int port = receiver.LocalPort;
        using var client = new VideoClient(receiver, () => Interlocked.Increment(ref requests),
            _ => Interlocked.Increment(ref frames), time);
        using var udp = new UdpClient();
        foreach (var p in new FramePacketizer(streamId: 1).Packetize(0, new byte[100], keyframe: true))
            udp.Send(p, p.Length, new IPEndPoint(IPAddress.Loopback, port));
        await Until(() => Volatile.Read(ref frames) == 1); // decoding normally now
        int before = Volatile.Read(ref requests);

        client.DecodeFailed();

        Assert.Equal(before + 1, Volatile.Read(ref requests)); // sent by DecodeFailed itself, not a later tick
        Assert.True(client.Stats.WaitingForKeyframe);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests`
Expected: build FAILS: `AssemblyTime`, `DecodeFailed`, `sendTimingPing`, `PingInterval`, `RoundTrip` and `HostDelay` don't exist.

- [ ] **Step 3: Implement**

`FrameAssembler.cs`:

```csharp
/// <summary>A complete frame. <see cref="AssemblyTime"/> is from its first packet to the one that completed it.</summary>
public sealed record AssembledFrame(uint Number, bool Keyframe, byte[] Data, bool Paused = false, TimeSpan AssemblyTime = default);
```

In `PendingFrame` add `public TimeSpan FirstPacket { get; } = now;`. In `Finish`, return
`new AssembledFrame(frame.Number, frame.Keyframe, data, frame.Paused, frame.LastPacket - frame.FirstPacket);`.

`DecodeGate.cs`:

```csharp
    /// <summary>The decoder failed on a frame; nothing after it decodes until a keyframe.</summary>
    public void DecodeFailed() => WaitingForKeyframe = true;
```

`VideoClient.cs` (add `using CouchLink.Core.Protocol;`):

```csharp
    public static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(1);

    private readonly Action<long>? _sendTimingPing;
    private TimeSpan? _lastPing;
    private TimeSpan? _roundTrip;
    private TimeSpan _hostDelay;
```

Constructor: add the last parameter `Action<long>? sendTimingPing = null` and `_sendTimingPing = sendTimingPing;`.

`VideoClientStats`:

```csharp
public readonly record struct VideoClientStats(
    VideoReceiveStats Receive,
    long FramesDelivered,
    long FramesSkipped,
    long KeyframeRequests,
    bool WaitingForKeyframe,
    bool HostPaused,
    TimeSpan? RoundTrip = null,
    TimeSpan HostDelay = default);
```

`Stats` passes `_roundTrip, _hostDelay` inside the existing lock.

`OnDatagram` starts with:

```csharp
        if (TimingReply.TryParse(datagram, out var reply))
        {
            var rtt = Now - TimeSpan.FromTicks(reply.ClientTicks);
            lock (_lock)
            {
                _roundTrip = _roundTrip is { } old ? old + (rtt - old) / 4 : rtt;
                _hostDelay = reply.HostDelay;
            }
            return;
        }
```

`Tick`, inside the `try` after `RequestKeyframeIfNeeded();`, add `SendPingIfDue();`, and:

```csharp
    private void SendPingIfDue()
    {
        if (_sendTimingPing is null)
            return;
        var now = Now;
        lock (_lock)
        {
            if (_lastPing is { } last && now - last < PingInterval)
                return;
            _lastPing = now;
        }
        _sendTimingPing(now.Ticks);
    }

    /// <summary>The decoder failed; skip to the next keyframe and ask for it now. Any thread.</summary>
    public void DecodeFailed()
    {
        lock (_lock)
            _gate.DecodeFailed();
        RequestKeyframeIfNeeded();
    }
```

Add to the class doc comment: "With <c>sendTimingPing</c> it pings the host every <see cref="PingInterval"/> and keeps the round trip and the host's delay from the replies."

`ClientVideoService.cs`: pass `sendTimingPing: sender.SendTimingPing` to the `VideoClient` constructor (Task 8 rewrites this class; this keeps the app building and pinging meanwhile).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -S -m "feat(core): client measures round trip and resyncs after a decode error" -m "Refs #17, #18"
```

---

### Task 4: Letterbox, per-second stats and overlay text

**Files:**
- Create: `src/CouchLink.Core/Video/Letterbox.cs`, `src/CouchLink.Core/Video/StatsWindow.cs`, `src/CouchLink.Core/Video/OverlayText.cs`
- Test: `tests/CouchLink.Core.Tests/LetterboxTests.cs`, `tests/CouchLink.Core.Tests/StatsWindowTests.cs`, `tests/CouchLink.Core.Tests/OverlayTextTests.cs`

**Interfaces:**
- Consumes: `VideoClientStats` with `RoundTrip` and `HostDelay` (Task 3); `VideoReceiveStats(long PacketsReceived, long FramesCompleted, long FramesLost, long DataShardsExpected, long DataShardsMissing, long ShardsRecovered)`.
- Produces:
  - `readonly record struct PixelRect(int X, int Y, int Width, int Height)`; `static PixelRect Letterbox.Fit(int pictureWidth, int pictureHeight, int windowWidth, int windowHeight)`: the largest rectangle with the picture's shape, centred, never empty.
  - `sealed class StatsWindow` with `StatsSample? Update(VideoClientStats stats, long framesShown, TimeSpan clientDelay, TimeSpan now)`: a new sample once at least `StatsWindow.Length` (1 s) has passed since the last one; the first call only records.
  - `readonly record struct StatsSample(double Fps, double Mbps, double LossPercent, long FecRepairs, TimeSpan? Latency, TimeSpan HostDelay, TimeSpan? Network, TimeSpan ClientDelay)`.
  - `static class OverlayText`: `const string Waiting = "Waiting for the host's picture..."`, `const string Paused = "Host screen paused"`, `static string Stats(StatsSample? sample, string decoder)`, `static string? Status(bool anyFrameShown, bool hostPaused)`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/LetterboxTests.cs`:

```csharp
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class LetterboxTests
{
    [Theory]
    [InlineData(1920, 1080, 1920, 1080, 0, 0, 1920, 1080)]   // same size
    [InlineData(1920, 1080, 2560, 1080, 320, 0, 1920, 1080)] // pillarbox on an ultrawide
    [InlineData(2560, 1080, 1920, 1080, 0, 135, 1920, 810)]  // letterbox an ultrawide stream
    [InlineData(1280, 720, 1920, 1080, 0, 0, 1920, 1080)]    // scale up
    [InlineData(1706, 720, 1366, 768, 0, 96, 1366, 576)]
    public void Fits_the_picture_centred_keeping_its_shape(int pw, int ph, int ww, int wh, int x, int y, int w, int h)
    {
        Assert.Equal(new PixelRect(x, y, w, h), Letterbox.Fit(pw, ph, ww, wh));
    }

    [Fact]
    public void Never_returns_an_empty_rectangle()
    {
        var r = Letterbox.Fit(4000, 10, 100, 100);
        Assert.True(r.Width >= 1 && r.Height >= 1);
    }
}
```

`tests/CouchLink.Core.Tests/StatsWindowTests.cs`:

```csharp
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class StatsWindowTests
{
    private static VideoClientStats S(long packets, long expected, long missing, long recovered,
        TimeSpan? rtt = null, TimeSpan hostDelay = default) =>
        new(new VideoReceiveStats(packets, 0, 0, expected, missing, recovered), 0, 0, 0, false, false, rtt, hostDelay);

    [Fact]
    public void The_first_update_only_records()
    {
        Assert.Null(new StatsWindow().Update(S(0, 0, 0, 0), 0, TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void Rates_are_per_second_over_the_last_window()
    {
        var w = new StatsWindow();
        w.Update(S(1000, 900, 0, 5), framesShown: 100, TimeSpan.Zero, TimeSpan.Zero);
        Assert.Null(w.Update(S(1100, 1000, 0, 5), 110, TimeSpan.Zero, TimeSpan.FromMilliseconds(500))); // too soon

        var s = w.Update(S(2000, 1900, 19, 12, TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(9)),
            framesShown: 160, clientDelay: TimeSpan.FromMilliseconds(5), now: TimeSpan.FromSeconds(1));

        Assert.NotNull(s);
        Assert.Equal(60, s.Value.Fps, 3);
        Assert.Equal(1000.0 * VideoShardPacket.Size * 8 / 1e6, s.Value.Mbps, 3); // 1000 packets in 1 s
        Assert.Equal(1.9, s.Value.LossPercent, 3);                              // 19 of 1000 data shards
        Assert.Equal(7, s.Value.FecRepairs);
        Assert.Equal(TimeSpan.FromMilliseconds(1), s.Value.Network);            // half the round trip
        Assert.Equal(TimeSpan.FromMilliseconds(9 + 1 + 5), s.Value.Latency);
    }

    [Fact]
    public void Latency_is_unknown_until_a_round_trip_is_measured()
    {
        var w = new StatsWindow();
        w.Update(S(0, 0, 0, 0), 0, TimeSpan.Zero, TimeSpan.Zero);
        var s = w.Update(S(10, 10, 0, 0), 1, TimeSpan.FromMilliseconds(5), TimeSpan.FromSeconds(1));
        Assert.Null(s!.Value.Latency);
        Assert.Null(s.Value.Network);
    }
}
```

`tests/CouchLink.Core.Tests/OverlayTextTests.cs`:

```csharp
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class OverlayTextTests
{
    [Fact]
    public void Stats_show_every_number_the_overlay_promises()
    {
        var sample = new StatsSample(Fps: 59.6, Mbps: 12.34, LossPercent: 0.26, FecRepairs: 3,
            Latency: TimeSpan.FromMilliseconds(23.4), HostDelay: TimeSpan.FromMilliseconds(14.1),
            Network: TimeSpan.FromMilliseconds(0.6), ClientDelay: TimeSpan.FromMilliseconds(8.7));

        Assert.Equal(
            "60 fps  12.3 Mbps  (D3D11VA)\n" +
            "Packet loss 0.3%  FEC repairs 3/s\n" +
            "Latency ~23 ms (host 14 + network 1 + client 9)",
            OverlayText.Stats(sample, "D3D11VA"));
    }

    [Fact]
    public void Unknown_latency_and_no_sample_yet_say_so()
    {
        var sample = new StatsSample(60, 5, 0, 0, null, TimeSpan.Zero, null, TimeSpan.FromMilliseconds(3));
        Assert.EndsWith("Latency measuring...", OverlayText.Stats(sample, "software"));
        Assert.Equal("Collecting stats...", OverlayText.Stats(null, "software"));
    }

    [Theory]
    [InlineData(false, false, OverlayText.Waiting)]
    [InlineData(false, true, OverlayText.Waiting)]
    [InlineData(true, true, OverlayText.Paused)]
    [InlineData(true, false, null)]
    public void Status_says_why_there_is_no_live_picture(bool shown, bool paused, string? expected)
    {
        Assert.Equal(expected, OverlayText.Status(shown, paused));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "LetterboxTests|StatsWindowTests|OverlayTextTests"`
Expected: build FAILS: the types don't exist.

- [ ] **Step 3: Implement**

`src/CouchLink.Core/Video/Letterbox.cs`:

```csharp
namespace CouchLink.Core.Video;

public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>Where a picture goes in a window: as large as fits, same shape, centred, black around it.</summary>
public static class Letterbox
{
    public static PixelRect Fit(int pictureWidth, int pictureHeight, int windowWidth, int windowHeight)
    {
        double scale = Math.Min((double)windowWidth / pictureWidth, (double)windowHeight / pictureHeight);
        int w = Math.Max(1, (int)Math.Round(pictureWidth * scale));
        int h = Math.Max(1, (int)Math.Round(pictureHeight * scale));
        return new PixelRect((windowWidth - w) / 2, (windowHeight - h) / 2, w, h);
    }
}
```

`src/CouchLink.Core/Video/StatsWindow.cs`:

```csharp
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Video;

/// <summary>One second of F2 overlay numbers. Latency = host delay + half the round trip + client delay.</summary>
public readonly record struct StatsSample(
    double Fps, double Mbps, double LossPercent, long FecRepairs,
    TimeSpan? Latency, TimeSpan HostDelay, TimeSpan? Network, TimeSpan ClientDelay);

/// <summary>Turns the client's running totals into per-second numbers, once a second. Not thread-safe.</summary>
public sealed class StatsWindow
{
    public static readonly TimeSpan Length = TimeSpan.FromSeconds(1);

    private (VideoClientStats Stats, long Shown, TimeSpan At)? _last;

    public StatsSample? Update(VideoClientStats stats, long framesShown, TimeSpan clientDelay, TimeSpan now)
    {
        if (_last is not { } last)
        {
            _last = (stats, framesShown, now);
            return null;
        }
        var elapsed = now - last.At;
        if (elapsed < Length)
            return null;
        _last = (stats, framesShown, now);

        double seconds = elapsed.TotalSeconds;
        var r = stats.Receive;
        var p = last.Stats.Receive;
        long expected = r.DataShardsExpected - p.DataShardsExpected;
        long missing = r.DataShardsMissing - p.DataShardsMissing;
        TimeSpan? network = stats.RoundTrip / 2;
        return new StatsSample(
            Fps: (framesShown - last.Shown) / seconds,
            Mbps: (r.PacketsReceived - p.PacketsReceived) * (double)VideoShardPacket.Size * 8 / seconds / 1e6,
            LossPercent: expected <= 0 ? 0 : 100.0 * missing / expected,
            FecRepairs: (long)Math.Round((r.ShardsRecovered - p.ShardsRecovered) / seconds),
            Latency: network is { } n ? stats.HostDelay + n + clientDelay : null,
            HostDelay: stats.HostDelay,
            Network: network,
            ClientDelay: clientDelay);
    }
}
```

`src/CouchLink.Core/Video/OverlayText.cs`:

```csharp
namespace CouchLink.Core.Video;

/// <summary>The player's on-screen text: the F2 stats, and why there is no live picture.</summary>
public static class OverlayText
{
    public const string Waiting = "Waiting for the host's picture...";
    public const string Paused = "Host screen paused";

    public static string Stats(StatsSample? sample, string decoder)
    {
        if (sample is not { } s)
            return "Collecting stats...";
        string latency = s.Latency is { } total && s.Network is { } network
            ? $"Latency ~{Ms(total)} ms (host {Ms(s.HostDelay)} + network {Ms(network)} + client {Ms(s.ClientDelay)})"
            : "Latency measuring...";
        return $"{s.Fps:0} fps  {s.Mbps:0.0} Mbps  ({decoder})\n" +
               $"Packet loss {s.LossPercent:0.0}%  FEC repairs {s.FecRepairs}/s\n" +
               latency;
    }

    /// <summary>A centred message when there is no live picture, or null.</summary>
    public static string? Status(bool anyFrameShown, bool hostPaused) =>
        !anyFrameShown ? Waiting : hostPaused ? Paused : null;

    private static string Ms(TimeSpan t) => Math.Round(t.TotalMilliseconds, MidpointRounding.AwayFromZero).ToString("0");
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Video tests/CouchLink.Core.Tests
git commit -S -m "feat(core): letterbox, per-second stats and overlay text for the player" -m "Refs #18"
```

---

### Task 5: H.264 decoder and colour mapping

**Files:**
- Create: `src/CouchLink.Video/IPlayerParts.cs`, `src/CouchLink.Video/StreamColor.cs`, `src/CouchLink.Video/H264Decoder.cs`
- Create: `tests/CouchLink.Video.Tests/TestStreams.cs`
- Test: `tests/CouchLink.Video.Tests/StreamColorTests.cs`, `tests/CouchLink.Video.Tests/H264DecoderTests.cs`

**Interfaces:**
- Consumes: `FfmpegLibrary.TryLoad`, `FfmpegLibrary.Check`, `FfmpegException` (Plan 4).
- Produces:
  - `readonly record struct DecodedPicture(int Width, int Height, nint Frame, bool OnGpu, ColorSpaceType Color)`: `Frame` is the decoder's `AVFrame*`, valid until that decoder's next `Decode` or `Dispose`. `OnGpu` means `AV_PIX_FMT_D3D11` (`data[0]` texture, `data[1]` slice); otherwise planar YUV 4:2:0 in system memory.
  - `interface IFrameDecoder : IDisposable { string Name { get; } bool IsHardware { get; } bool Decode(byte[] data, out DecodedPicture picture); }` (`Decode` throws `FfmpegException` on bad data, returns false when the decoder wants more data).
  - `interface IFramePresenter : IDisposable { void Present(DecodedPicture? picture, string? status, string? stats); }`.
  - `static ColorSpaceType StreamColor.For(AVColorSpace space, AVColorRange range)`.
  - `sealed class H264Decoder : IFrameDecoder` with `static H264Decoder Open(D3D11Device? device, out string? hardwareError)` (D3D11VA on `device` when given; otherwise, or when that fails, software with the reason in `hardwareError`) and `static H264Decoder OpenSoftware()`. `Name` is `"D3D11VA"` or `"software"`.
  - Test helper `TestStreams.X264(int width, int height, int frames, AVColorSpace colorSpace = AVColorSpace.AVCOL_SPC_UNSPECIFIED)` returning one Annex B packet per frame, the first a keyframe.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Video.Tests/StreamColorTests.cs`:

```csharp
using CouchLink.Video;
using FFmpeg.AutoGen;
using Vortice.DXGI;

namespace CouchLink.Video.Tests;

public class StreamColorTests
{
    [Theory]
    [InlineData(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_MPEG, ColorSpaceType.YcbcrStudioG22LeftP709)]
    [InlineData(AVColorSpace.AVCOL_SPC_BT709, AVColorRange.AVCOL_RANGE_JPEG, ColorSpaceType.YcbcrFullG22LeftP709)]
    [InlineData(AVColorSpace.AVCOL_SPC_SMPTE170M, AVColorRange.AVCOL_RANGE_MPEG, ColorSpaceType.YcbcrStudioG22LeftP601)]
    [InlineData(AVColorSpace.AVCOL_SPC_BT470BG, AVColorRange.AVCOL_RANGE_JPEG, ColorSpaceType.YcbcrFullG22LeftP601)]
    [InlineData(AVColorSpace.AVCOL_SPC_UNSPECIFIED, AVColorRange.AVCOL_RANGE_UNSPECIFIED, ColorSpaceType.YcbcrStudioG22LeftP601)]
    public void Stream_colour_tags_pick_the_matching_colour_space(AVColorSpace space, AVColorRange range, ColorSpaceType expected)
    {
        Assert.Equal(expected, StreamColor.For(space, range));
    }
}
```

`tests/CouchLink.Video.Tests/TestStreams.cs`:

```csharp
using CouchLink.Video;
using FFmpeg.AutoGen;

namespace CouchLink.Video.Tests;

/// <summary>Small H.264 streams made with x264 in memory, for decoder tests.</summary>
internal static unsafe class TestStreams
{
    /// <summary>One Annex B packet per frame, the first a keyframe; a moving diagonal pattern.</summary>
    public static List<byte[]> X264(int width, int height, int frames, AVColorSpace colorSpace = AVColorSpace.AVCOL_SPC_UNSPECIFIED)
    {
        Assert.True(FfmpegLibrary.TryLoad(out var error), error);
        var packets = new List<byte[]>();
        AVCodecContext* ctx = null;
        AVFrame* frame = null;
        AVPacket* packet = null;
        try
        {
            var codec = ffmpeg.avcodec_find_encoder_by_name("libx264");
            ctx = ffmpeg.avcodec_alloc_context3(codec);
            ctx->width = width;
            ctx->height = height;
            ctx->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            ctx->time_base = new AVRational { num = 1, den = 60 };
            ctx->gop_size = int.MaxValue;
            ctx->max_b_frames = 0;
            ctx->colorspace = colorSpace;
            ctx->color_range = AVColorRange.AVCOL_RANGE_MPEG;
            AVDictionary* options = null;
            ffmpeg.av_dict_set(&options, "preset", "ultrafast", 0);
            ffmpeg.av_dict_set(&options, "tune", "zerolatency", 0);
            int opened = ffmpeg.avcodec_open2(ctx, codec, &options);
            ffmpeg.av_dict_free(&options);
            FfmpegLibrary.Check(opened, "Opening libx264");

            frame = ffmpeg.av_frame_alloc();
            frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            frame->width = width;
            frame->height = height;
            FfmpegLibrary.Check(ffmpeg.av_frame_get_buffer(frame, 0), "Allocating a frame");
            packet = ffmpeg.av_packet_alloc();

            for (int i = 0; i < frames; i++)
            {
                FfmpegLibrary.Check(ffmpeg.av_frame_make_writable(frame), "Making the frame writable");
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        frame->data[0][y * frame->linesize[0] + x] = (byte)(x + y + i * 4);
                for (int y = 0; y < height / 2; y++)
                    for (int x = 0; x < width / 2; x++)
                    {
                        frame->data[1][y * frame->linesize[1] + x] = 128;
                        frame->data[2][y * frame->linesize[2] + x] = 128;
                    }
                frame->pts = i;
                FfmpegLibrary.Check(ffmpeg.avcodec_send_frame(ctx, frame), "Encoding");
                Drain(ctx, packet, packets);
            }
            FfmpegLibrary.Check(ffmpeg.avcodec_send_frame(ctx, null), "Flushing");
            Drain(ctx, packet, packets);
        }
        finally
        {
            ffmpeg.av_packet_free(&packet);
            ffmpeg.av_frame_free(&frame);
            ffmpeg.avcodec_free_context(&ctx);
        }
        Assert.Equal(frames, packets.Count); // zerolatency: one packet per frame
        return packets;
    }

    private static void Drain(AVCodecContext* ctx, AVPacket* packet, List<byte[]> packets)
    {
        while (ffmpeg.avcodec_receive_packet(ctx, packet) == 0)
        {
            packets.Add(new ReadOnlySpan<byte>(packet->data, packet->size).ToArray());
            ffmpeg.av_packet_unref(packet);
        }
    }
}
```

`tests/CouchLink.Video.Tests/H264DecoderTests.cs`:

```csharp
using CouchLink.Video;
using FFmpeg.AutoGen;
using Vortice.DXGI;

namespace CouchLink.Video.Tests;

public class H264DecoderTests
{
    [Fact]
    public void Falls_back_to_software_without_a_device()
    {
        Assert.True(FfmpegLibrary.TryLoad(out var error), error);
        using var decoder = H264Decoder.Open(device: null, out var hardwareError);

        Assert.False(decoder.IsHardware);
        Assert.Equal("software", decoder.Name);
        Assert.Contains("no D3D11 device", hardwareError);
    }

    [Fact]
    public void Decodes_every_frame_at_its_size_with_no_delay()
    {
        var packets = TestStreams.X264(320, 240, frames: 10);
        using var decoder = H264Decoder.OpenSoftware();

        foreach (var packet in packets)
        {
            Assert.True(decoder.Decode(packet, out var picture)); // one frame out per packet in
            Assert.Equal((320, 240), (picture.Width, picture.Height));
            Assert.False(picture.OnGpu);
            Assert.NotEqual(0, picture.Frame);
        }
    }

    [Fact]
    public void Pictures_carry_the_streams_colour_space()
    {
        var bt709 = TestStreams.X264(64, 64, 1, AVColorSpace.AVCOL_SPC_BT709);
        var untagged = TestStreams.X264(64, 64, 1);
        using var a = H264Decoder.OpenSoftware();
        using var b = H264Decoder.OpenSoftware();

        Assert.True(a.Decode(bt709[0], out var p709));
        Assert.True(b.Decode(untagged[0], out var p601));

        Assert.Equal(ColorSpaceType.YcbcrStudioG22LeftP709, p709.Color);
        Assert.Equal(ColorSpaceType.YcbcrStudioG22LeftP601, p601.Color);
    }

    [Fact]
    public void Garbage_is_an_FfmpegException_or_nothing_never_a_crash()
    {
        using var decoder = H264Decoder.OpenSoftware();
        var garbage = new byte[2000];
        new Random(1).NextBytes(garbage);
        garbage[0] = 0; garbage[1] = 0; garbage[2] = 1; garbage[3] = 0x65; // looks like a slice

        var e = Record.Exception(() => decoder.Decode(garbage, out _));

        Assert.True(e is null or FfmpegException, e?.ToString());
    }

    [Fact]
    public void Decoding_recovers_at_the_next_keyframe_after_garbage()
    {
        var stream = TestStreams.X264(160, 120, frames: 3);
        using var decoder = H264Decoder.OpenSoftware();
        var garbage = new byte[500];
        new Random(2).NextBytes(garbage);
        try { decoder.Decode(garbage, out _); } catch (FfmpegException) { }

        Assert.True(decoder.Decode(stream[0], out var picture)); // the keyframe
        Assert.Equal(160, picture.Width);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Video.Tests --filter "StreamColorTests|H264DecoderTests"`
Expected: build FAILS: `StreamColor`, `H264Decoder`, `DecodedPicture` don't exist.

- [ ] **Step 3: Implement**

`src/CouchLink.Video/IPlayerParts.cs`:

```csharp
using Vortice.DXGI;

namespace CouchLink.Video;

/// <summary>
/// A decoded picture. <see cref="Frame"/> is the decoder's <c>AVFrame*</c>, valid until that decoder's
/// next Decode or Dispose. <see cref="OnGpu"/>: a D3D11 texture (data[0]) and array slice (data[1]);
/// otherwise YUV 4:2:0 planes in memory.
/// </summary>
public readonly record struct DecodedPicture(int Width, int Height, nint Frame, bool OnGpu, ColorSpaceType Color);

/// <summary>Turns one frame's H.264 into a picture. Throws <see cref="FfmpegException"/> on bad data.</summary>
public interface IFrameDecoder : IDisposable
{
    string Name { get; }
    bool IsHardware { get; }

    /// <summary>False when the decoder needs more data before it has a picture.</summary>
    bool Decode(byte[] data, out DecodedPicture picture);
}

/// <summary>Shows a picture (or black, when null) with optional centred status text and top-left stats.</summary>
public interface IFramePresenter : IDisposable
{
    void Present(DecodedPicture? picture, string? status, string? stats);
}
```

`src/CouchLink.Video/StreamColor.cs`:

```csharp
using FFmpeg.AutoGen;
using Vortice.DXGI;

namespace CouchLink.Video;

/// <summary>
/// The video processor's input colour space for a stream. The host tags hardware streams BT.709 and
/// x264 streams BT.601 (SMPTE 170M); anything not tagged BT.709 is treated as BT.601, limited range
/// unless tagged full.
/// </summary>
public static class StreamColor
{
    public static ColorSpaceType For(AVColorSpace space, AVColorRange range)
    {
        bool full = range == AVColorRange.AVCOL_RANGE_JPEG;
        return space == AVColorSpace.AVCOL_SPC_BT709
            ? full ? ColorSpaceType.YcbcrFullG22LeftP709 : ColorSpaceType.YcbcrStudioG22LeftP709
            : full ? ColorSpaceType.YcbcrFullG22LeftP601 : ColorSpaceType.YcbcrStudioG22LeftP601;
    }
}
```

`src/CouchLink.Video/H264Decoder.cs`:

```csharp
using FFmpeg.AutoGen;
using D3D11Device = Vortice.Direct3D11.ID3D11Device;

namespace CouchLink.Video;

/// <summary>
/// FFmpeg's H.264 decoder. With a D3D11 device it decodes on the GPU (D3D11VA) into textures of that
/// device, which the presenter reads directly; without one, or if that fails, it decodes in software.
/// Low-delay: one picture out per frame in. Used from one thread.
/// </summary>
public sealed unsafe class H264Decoder : IFrameDecoder
{
    private AVCodecContext* _ctx;
    private AVBufferRef* _hwDevice;
    private AVFrame* _frame;
    private AVPacket* _packet;

    private H264Decoder(D3D11Device? device)
    {
        try
        {
            var codec = ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264);
            if (codec == null)
                throw new FfmpegException("FFmpeg has no H.264 decoder.");
            _ctx = ffmpeg.avcodec_alloc_context3(codec);
            _ctx->flags |= ffmpeg.AV_CODEC_FLAG_LOW_DELAY;
            _ctx->flags2 |= ffmpeg.AV_CODEC_FLAG2_FAST;
            if (device is not null)
            {
                _hwDevice = ffmpeg.av_hwdevice_ctx_alloc(AVHWDeviceType.AV_HWDEVICE_TYPE_D3D11VA);
                var deviceContext = (AVD3D11VADeviceContext*)((AVHWDeviceContext*)_hwDevice->data)->hwctx;
                device.AddRef(); // FFmpeg releases it when the device context is freed
                deviceContext->device = (FFmpeg.AutoGen.ID3D11Device*)device.NativePointer;
                FfmpegLibrary.Check(ffmpeg.av_hwdevice_ctx_init(_hwDevice), "Sharing the D3D11 device with FFmpeg");
                _ctx->hw_device_ctx = ffmpeg.av_buffer_ref(_hwDevice);
                _ctx->extra_hw_frames = 4; // the presenter keeps the last picture for redraws
                _ctx->thread_count = 1;
                IsHardware = true;
            }
            else
            {
                _ctx->thread_type = ffmpeg.FF_THREAD_SLICE; // frame threads would add a frame of delay each
                _ctx->thread_count = 0;
            }
            FfmpegLibrary.Check(ffmpeg.avcodec_open2(_ctx, codec, null), "Opening the H.264 decoder");
            _frame = ffmpeg.av_frame_alloc();
            _packet = ffmpeg.av_packet_alloc();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool IsHardware { get; private set; }
    public string Name => IsHardware ? "D3D11VA" : "software";

    /// <summary>D3D11VA on <paramref name="device"/>, or software with the reason in <paramref name="hardwareError"/>.</summary>
    public static H264Decoder Open(D3D11Device? device, out string? hardwareError)
    {
        hardwareError = null;
        if (device is null)
        {
            hardwareError = "no D3D11 device";
            return OpenSoftware();
        }
        try
        {
            return new H264Decoder(device);
        }
        catch (Exception e) when (e is FfmpegException or SharpGen.Runtime.SharpGenException)
        {
            hardwareError = e.Message;
            return OpenSoftware();
        }
    }

    public static H264Decoder OpenSoftware() => new(null);

    public bool Decode(byte[] data, out DecodedPicture picture)
    {
        picture = default;
        ffmpeg.av_frame_unref(_frame); // the previous picture is no longer needed
        fixed (byte* bytes = data)
        {
            _packet->data = bytes;
            _packet->size = data.Length;
            int sent = ffmpeg.avcodec_send_packet(_ctx, _packet); // copies the data: no refcounted buffer
            _packet->data = null;
            _packet->size = 0;
            FfmpegLibrary.Check(sent, "Decoding a frame");
        }
        int result = ffmpeg.avcodec_receive_frame(_ctx, _frame);
        if (result == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            return false;
        FfmpegLibrary.Check(result, "Decoding a frame");

        bool onGpu = _frame->format == (int)AVPixelFormat.AV_PIX_FMT_D3D11;
        if (IsHardware && !onGpu)
            IsHardware = false; // FFmpeg fell back to software by itself (e.g. an unsupported profile)
        picture = new DecodedPicture(_frame->width, _frame->height, (nint)_frame, onGpu,
            StreamColor.For(_frame->colorspace, _frame->color_range));
        return true;
    }

    public void Dispose()
    {
        if (_packet != null) { var p = _packet; ffmpeg.av_packet_free(&p); _packet = null; }
        if (_frame != null) { var f = _frame; ffmpeg.av_frame_free(&f); _frame = null; }
        if (_ctx != null) { var c = _ctx; ffmpeg.avcodec_free_context(&c); _ctx = null; }
        if (_hwDevice != null) { var d = _hwDevice; ffmpeg.av_buffer_unref(&d); _hwDevice = null; }
    }
}
```

Callers load FFmpeg (`FfmpegLibrary.TryLoad`) before opening a decoder, as `HostVideo` does before opening an encoder; `H264Decoder` doesn't load it itself.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Video.Tests`
Expected: all pass. If `Garbage_is_...` shows a different exception type (for example `AccessViolationException` would crash the run), stop and use superpowers:systematic-debugging: the decoder must never crash on network data.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Video tests/CouchLink.Video.Tests
git commit -S -m "feat(video): H.264 decoder with D3D11VA and software fallback" -m "Refs #17"
```

---

### Task 6: Player logic (decode, present, fall back, resync, status)

`PlayerCore` is everything the player does between "a frame arrived" and "show this", with no thread, window or GPU of its own, so it can be tested with fakes. `VideoPlayer` (Task 7) runs it on the player thread.

**Files:**
- Create: `src/CouchLink.Video/PlayerCore.cs`
- Modify: `tests/CouchLink.Video.Tests/Fakes.cs`
- Test: `tests/CouchLink.Video.Tests/PlayerCoreTests.cs`

**Interfaces:**
- Consumes: `IFrameDecoder`, `IFramePresenter`, `DecodedPicture` (Task 5); `AssembledFrame` with `AssemblyTime`, `VideoClientStats` (Task 3); `StatsWindow`, `OverlayText` (Task 4); `FfmpegException`.
- Produces: `sealed class PlayerCore : IDisposable` with
  - `PlayerCore(Func<bool, IFrameDecoder> openDecoder, IFramePresenter presenter, Func<VideoClientStats> stats, Action decodeFailed, TimeProvider time, Action<string>? log = null)`; `openDecoder(true)` is called once at construction (hardware preferred), `openDecoder(false)` after a hardware decode error.
  - `const int MaxBacklog = 6`, `static readonly TimeSpan RedrawInterval = 250 ms`.
  - `void Enqueue(AssembledFrame frame)` (any thread), `WaitHandle FrameReady`.
  - `void Run()` (player thread): one pass.
  - `bool ShowStats { get; set; }`, `long FramesShown`, `string DecoderName`, `TimeSpan ClientDelay` (smoothed weight 1/8: receive to present plus assembly time).

- [ ] **Step 1: Write the failing tests**

Add to `tests/CouchLink.Video.Tests/Fakes.cs`:

```csharp
/// <summary>"Decodes" a frame whose data is its frame number (4 bytes); picture.Frame is that number.</summary>
internal sealed class FakeDecoder(bool hardware) : IFrameDecoder
{
    public string Name => IsHardware ? "D3D11VA" : "software";
    public bool IsHardware { get; } = hardware;
    public List<uint> Decoded { get; } = [];
    public Func<uint, bool> FailOn { get; set; } = _ => false;
    public bool Disposed { get; private set; }

    public bool Decode(byte[] data, out DecodedPicture picture)
    {
        uint number = BitConverter.ToUInt32(data);
        if (FailOn(number))
            throw new FfmpegException($"bad frame {number}");
        Decoded.Add(number);
        picture = new DecodedPicture(1920, 1080, (nint)number, IsHardware, Vortice.DXGI.ColorSpaceType.YcbcrStudioG22LeftP709);
        return true;
    }

    public void Dispose() => Disposed = true;
}

internal sealed class FakePresenter : IFramePresenter
{
    public List<(DecodedPicture? Picture, string? Status, string? Stats)> Shown { get; } = [];
    public void Present(DecodedPicture? picture, string? status, string? stats) => Shown.Add((picture, status, stats));
    public void Dispose() { }
}
```

`tests/CouchLink.Video.Tests/PlayerCoreTests.cs`:

```csharp
using CouchLink.Core.Video;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Video.Tests;

public class PlayerCoreTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakePresenter _presenter = new();
    private readonly List<FakeDecoder> _decoders = [];
    private VideoClientStats _stats = default;
    private int _decodeFailed;
    private Func<uint, bool> _hardwareFailsOn = _ => false;

    private PlayerCore Core() => new(
        hardware =>
        {
            var d = new FakeDecoder(hardware) { FailOn = hardware ? _hardwareFailsOn : _ => false };
            _decoders.Add(d);
            return d;
        },
        _presenter, () => _stats, () => _decodeFailed++, _time);

    private static AssembledFrame F(uint number, bool keyframe = false, TimeSpan assembly = default) =>
        new(number, keyframe, BitConverter.GetBytes(number), AssemblyTime: assembly);

    private static uint? ShownFrame((DecodedPicture? Picture, string? Status, string? Stats) s) =>
        s.Picture is { } p ? (uint)p.Frame : null;

    [Fact]
    public void Decodes_every_frame_but_presents_only_the_newest()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Enqueue(F(2));
        core.Enqueue(F(3));

        core.Run();

        Assert.Equal([1u, 2u, 3u], _decoders[0].Decoded);
        Assert.Equal(3u, ShownFrame(Assert.Single(_presenter.Shown)));
        Assert.Equal(1, core.FramesShown);
    }

    [Fact]
    public void A_hardware_decode_error_switches_to_software_and_resyncs()
    {
        _hardwareFailsOn = n => n == 2;
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();
        core.Enqueue(F(2));
        core.Enqueue(F(3));                 // after the failure: dropped, it depends on frame 2

        core.Run();

        Assert.Equal(2, _decoders.Count);
        Assert.True(_decoders[0].Disposed);
        Assert.False(_decoders[1].IsHardware);
        Assert.Equal("software", core.DecoderName);
        Assert.Equal(1, _decodeFailed);
        Assert.Empty(_decoders[1].Decoded);

        core.Enqueue(F(4, keyframe: true));
        core.Run();
        Assert.Equal([4u], _decoders[1].Decoded);
        Assert.Equal(4u, ShownFrame(_presenter.Shown[^1]));
    }

    [Fact]
    public void A_software_decode_error_only_resyncs()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();                          // hardware works...
        _hardwareFailsOn = _ => false;
        _decoders[0].FailOn = n => n == 2;   // ...then a frame is corrupt
        core.Enqueue(F(2));
        core.Run();                          // switches to software

        _decoders[1].FailOn = n => n == 5;
        core.Enqueue(F(5));
        core.Run();

        Assert.Equal(2, _decoders.Count);    // no third decoder
        Assert.Equal(2, _decodeFailed);
    }

    [Fact]
    public void A_backlog_is_dropped_and_resyncs_on_a_keyframe()
    {
        using var core = Core();
        for (uint n = 1; n <= PlayerCore.MaxBacklog + 4; n++)
            core.Enqueue(F(n));              // the player fell far behind; no keyframe in the backlog

        core.Run();

        Assert.Empty(_decoders[0].Decoded);
        Assert.Equal(1, _decodeFailed);
    }

    [Fact]
    public void A_backlog_with_a_keyframe_starts_from_it()
    {
        using var core = Core();
        for (uint n = 1; n <= PlayerCore.MaxBacklog + 4; n++)
            core.Enqueue(F(n, keyframe: n == 8));

        core.Run();

        Assert.Equal([8u, 9u, 10u], _decoders[0].Decoded);
        Assert.Equal(0, _decodeFailed);
    }

    [Fact]
    public void Shows_waiting_text_before_the_first_frame()
    {
        using var core = Core();
        core.Run();
        var shown = Assert.Single(_presenter.Shown);
        Assert.Null(shown.Picture);
        Assert.Equal(OverlayText.Waiting, shown.Status);
    }

    [Fact]
    public void Shows_paused_text_while_the_host_is_paused()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();
        _stats = _stats with { HostPaused = true };

        core.Run();                          // status changed: redraw at once

        Assert.Equal(OverlayText.Paused, _presenter.Shown[^1].Status);
        Assert.Equal(1u, ShownFrame(_presenter.Shown[^1])); // over the last picture
    }

    [Fact]
    public void Redraws_while_no_frames_arrive()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();
        core.Run();
        Assert.Single(_presenter.Shown);     // nothing changed: no redraw yet

        _time.Advance(PlayerCore.RedrawInterval);
        core.Run();

        Assert.Equal(2, _presenter.Shown.Count);
        Assert.Equal(1u, ShownFrame(_presenter.Shown[^1]));
    }

    [Fact]
    public void Stats_appear_when_switched_on()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();

        core.ShowStats = true;
        core.Run();
        Assert.Equal("Collecting stats...", _presenter.Shown[^1].Stats);

        _time.Advance(StatsWindow.Length);
        core.Run();
        Assert.Contains("fps", _presenter.Shown[^1].Stats);

        core.ShowStats = false;
        core.Run();
        Assert.Null(_presenter.Shown[^1].Stats);
    }

    [Fact]
    public void Client_delay_is_assembly_plus_receive_to_present()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true, assembly: TimeSpan.FromMilliseconds(2)));
        _time.Advance(TimeSpan.FromMilliseconds(3)); // waited 3 ms in the queue

        core.Run();

        Assert.Equal(TimeSpan.FromMilliseconds(5), core.ClientDelay);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Video.Tests --filter PlayerCoreTests`
Expected: build FAILS: `PlayerCore` doesn't exist.

- [ ] **Step 3: Implement**

`src/CouchLink.Video/PlayerCore.cs`:

```csharp
using CouchLink.Core.Video;

namespace CouchLink.Video;

/// <summary>
/// The player's logic, run on the player thread one pass at a time. Every queued frame is decoded in
/// order (H.264 needs them all) but only the newest picture is presented. A hardware decode error
/// switches to software; any decode error drops the rest and resyncs on the next keyframe; a backlog
/// longer than <see cref="MaxBacklog"/> is skipped to its last keyframe, or dropped and resynced.
/// Without new frames it redraws when the status or stats text changes, and every
/// <see cref="RedrawInterval"/>.
/// </summary>
public sealed class PlayerCore : IDisposable
{
    public const int MaxBacklog = 6;
    public static readonly TimeSpan RedrawInterval = TimeSpan.FromMilliseconds(250);

    private readonly Func<bool, IFrameDecoder> _openDecoder;
    private readonly IFramePresenter _presenter;
    private readonly Func<VideoClientStats> _stats;
    private readonly Action _decodeFailed;
    private readonly TimeProvider _time;
    private readonly Action<string>? _log;
    private readonly long _start;
    private readonly Lock _queueLock = new();
    private readonly List<(AssembledFrame Frame, TimeSpan ReceivedAt)> _queue = [];
    private readonly AutoResetEvent _frameReady = new(false);
    private readonly StatsWindow _statsWindow = new();
    private IFrameDecoder _decoder;
    private DecodedPicture? _last;
    private StatsSample? _sample;
    private TimeSpan? _lastPresent;
    private string? _lastStatus, _lastStatsText;
    private long _clientDelayTicks;

    public PlayerCore(Func<bool, IFrameDecoder> openDecoder, IFramePresenter presenter,
        Func<VideoClientStats> stats, Action decodeFailed, TimeProvider time, Action<string>? log = null)
    {
        _openDecoder = openDecoder;
        _presenter = presenter;
        _stats = stats;
        _decodeFailed = decodeFailed;
        _time = time;
        _log = log;
        _start = time.GetTimestamp();
        _decoder = openDecoder(true);
    }

    public WaitHandle FrameReady => _frameReady;
    public bool ShowStats { get; set; }
    public long FramesShown { get; private set; }
    public string DecoderName => _decoder.Name;
    public TimeSpan ClientDelay => TimeSpan.FromTicks(Interlocked.Read(ref _clientDelayTicks));

    private TimeSpan Now => _time.GetElapsedTime(_start);

    /// <summary>Queues a frame for the player thread. Any thread.</summary>
    public void Enqueue(AssembledFrame frame)
    {
        lock (_queueLock)
            _queue.Add((frame, Now));
        _frameReady.Set();
    }

    public void Run()
    {
        List<(AssembledFrame Frame, TimeSpan ReceivedAt)> batch;
        lock (_queueLock)
        {
            batch = [.. _queue];
            _queue.Clear();
        }

        if (batch.Count > MaxBacklog)
        {
            int key = batch.FindLastIndex(f => f.Frame.Keyframe);
            if (key >= 0)
            {
                batch = batch[key..];
            }
            else
            {
                _log?.Invoke($"Video player fell {batch.Count} frames behind; waiting for a keyframe");
                batch.Clear();
                _decodeFailed();
            }
        }

        (DecodedPicture Picture, AssembledFrame Frame, TimeSpan ReceivedAt)? newest = null;
        foreach (var (frame, receivedAt) in batch)
        {
            try
            {
                if (_decoder.Decode(frame.Data, out var picture))
                    newest = (picture, frame, receivedAt);
            }
            catch (FfmpegException e)
            {
                OnDecodeError(e);
                newest = null; // later frames in this batch depend on the broken one
                break;
            }
        }

        string? status = OverlayText.Status(FramesShown > 0 || newest is not null, _stats().HostPaused);
        if (newest is { } n)
        {
            _last = n.Picture;
            FramesShown++;
        }
        UpdateStats();
        string? statsText = ShowStats ? OverlayText.Stats(_sample, DecoderName) : null;

        bool due = newest is not null
            || _lastPresent is null
            || Now - _lastPresent >= RedrawInterval
            || status != _lastStatus
            || statsText != _lastStatsText;
        if (!due)
            return;

        _presenter.Present(_last, status, statsText);
        _lastPresent = Now;
        _lastStatus = status;
        _lastStatsText = statsText;
        if (newest is { } shown)
            RecordClientDelay(shown.Frame.AssemblyTime + (Now - shown.ReceivedAt));
    }

    private void OnDecodeError(FfmpegException e)
    {
        if (_decoder.IsHardware)
        {
            _log?.Invoke($"Hardware video decoding failed ({e.Message}); switching to software decoding");
            _decoder.Dispose();
            _last = null; // its picture belonged to the old decoder
            _decoder = _openDecoder(false);
        }
        else
        {
            _log?.Invoke($"Video decoding failed ({e.Message}); waiting for a keyframe");
        }
        _decodeFailed();
    }

    private void UpdateStats()
    {
        if (_statsWindow.Update(_stats(), FramesShown, ClientDelay, Now) is { } sample)
            _sample = sample;
    }

    private void RecordClientDelay(TimeSpan delay)
    {
        long old = Interlocked.Read(ref _clientDelayTicks);
        Interlocked.Exchange(ref _clientDelayTicks, old == 0 ? delay.Ticks : old + (delay.Ticks - old) / 8);
    }

    public void Dispose()
    {
        _decoder.Dispose();
        _frameReady.Dispose();
    }
}
```

Notes for the implementer:
- `Stats_appear_when_switched_on` needs the stats sample to arrive after `StatsWindow.Length`: the first `UpdateStats` call (first `Run`) records, the call one second later produces a sample.
- `A_software_decode_error_only_resyncs` relies on `FailOn` being settable on an existing fake; that is why `FakeDecoder.FailOn` has a setter.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Video.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Video/PlayerCore.cs tests/CouchLink.Video.Tests
git commit -S -m "feat(video): player logic with software fallback, resync and status text" -m "Refs #17, #18"
```

---

### Task 7: Presenter, player window, player thread and `VideoTest play`

The GPU and window side, taken from the spike. These classes can't run on CI (no GPU, no desktop), so they are checked on hardware with a new `VideoTest play` command, the way Plan 4 checked capture and encoding. The one pure helper, splitting an H.264 file into frames, gets a unit test.

**Files:**
- Modify: `src/CouchLink.Video/CouchLink.Video.csproj` (add `Vortice.Direct2D1` 3.8.3)
- Create: `src/CouchLink.Video/H264Frames.cs`, `src/CouchLink.Video/FramePresenter.cs`, `src/CouchLink.Video/PlayerWindow.cs`, `src/CouchLink.Video/VideoPlayer.cs`
- Modify: `src/CouchLink.VideoTest/Program.cs` (play command)
- Test: `tests/CouchLink.Video.Tests/H264FramesTests.cs`

**Interfaces:**
- Consumes: `IFramePresenter`, `DecodedPicture`, `H264Decoder` (Task 5); `PlayerCore` (Task 6); `Letterbox.Fit` (Task 4); `TimerResolution` (Plan 4, internal to `CouchLink.Video`).
- Produces:
  - `static List<byte[]> H264Frames.Split(byte[] annexB)`: one entry per frame (FFmpeg's H.264 parser).
  - `sealed class FramePresenter : IFramePresenter` with `FramePresenter(ID3D11Device device, ID3D11DeviceContext context, nint window, int width, int height)` and `void Resize(int width, int height)`.
  - `sealed class PlayerWindow : IDisposable` with `PlayerWindow(nint nearWindow, bool windowed)`, `nint Handle`, `int Width`, `int Height`, events `Action? CloseRequested`, `Action? StatsToggled`, `Action<int, int>? Resized`, `bool PumpMessages()`, `void WaitForInput(WaitHandle handle, TimeSpan timeout)`.
  - `readonly record struct PlayerOptions(nint NearWindow = 0, bool Windowed = false, bool PreferHardware = true)`.
  - `sealed class VideoPlayer : IDisposable` with `VideoPlayer(PlayerOptions options, Func<VideoClientStats> stats, Action decodeFailed, Action closeRequested, Action<string>? log = null)` (throws `InvalidOperationException` with a user-readable message if the window or GPU can't start), `void Enqueue(AssembledFrame frame)` (any thread), `nint WindowHandle`, `string DecoderName`, `long FramesShown`, `TimeSpan ClientDelay`, `string? HardwareDecodeError`.

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Video.Tests/H264FramesTests.cs`:

```csharp
using CouchLink.Video;

namespace CouchLink.Video.Tests;

public class H264FramesTests
{
    [Fact]
    public void A_recorded_stream_splits_back_into_its_frames()
    {
        var packets = TestStreams.X264(160, 120, frames: 12);
        var file = packets.SelectMany(p => p).ToArray();

        var frames = H264Frames.Split(file);

        Assert.Equal(packets.Count, frames.Count);
        Assert.Equal(packets[0], frames[0]);
        Assert.Equal(packets[^1], frames[^1]);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/CouchLink.Video.Tests --filter H264FramesTests`
Expected: build FAILS: `H264Frames` doesn't exist.

- [ ] **Step 3: Implement `H264Frames`**

`src/CouchLink.Video/H264Frames.cs`:

```csharp
using FFmpeg.AutoGen;

namespace CouchLink.Video;

/// <summary>Splits an Annex B H.264 file (as saved by VideoTest encode or --save-video) into frames.</summary>
public static unsafe class H264Frames
{
    public static List<byte[]> Split(byte[] annexB)
    {
        var frames = new List<byte[]>();
        var parser = ffmpeg.av_parser_init((int)AVCodecID.AV_CODEC_ID_H264);
        var ctx = ffmpeg.avcodec_alloc_context3(ffmpeg.avcodec_find_decoder(AVCodecID.AV_CODEC_ID_H264));
        try
        {
            fixed (byte* start = annexB)
            {
                int offset = 0;
                while (true)
                {
                    bool flushing = offset >= annexB.Length;
                    byte* output;
                    int size;
                    int used = ffmpeg.av_parser_parse2(parser, ctx, &output, &size,
                        flushing ? null : start + offset, flushing ? 0 : annexB.Length - offset,
                        ffmpeg.AV_NOPTS_VALUE, ffmpeg.AV_NOPTS_VALUE, 0);
                    offset += used;
                    if (size > 0)
                        frames.Add(new ReadOnlySpan<byte>(output, size).ToArray());
                    else if (flushing)
                        break;
                }
            }
        }
        finally
        {
            ffmpeg.av_parser_close(parser);
            ffmpeg.avcodec_free_context(&ctx);
        }
        return frames;
    }
}
```

If `av_parser_init` takes an `AVCodecID` in FFmpeg.AutoGen 9 (the spike's build said so), drop the `(int)` cast.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/CouchLink.Video.Tests --filter H264FramesTests`
Expected: PASS.

- [ ] **Step 5: Implement the presenter, window and player**

Add to `src/CouchLink.Video/CouchLink.Video.csproj`, next to the other Vortice packages:

```xml
    <PackageReference Include="Vortice.Direct2D1" Version="3.8.3" />
```

`src/CouchLink.Video/FramePresenter.cs`:

```csharp
using System.Numerics;
using CouchLink.Core.Video;
using AVFrame = FFmpeg.AutoGen.AVFrame;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Vortice.Mathematics;
using D3D11Device = Vortice.Direct3D11.ID3D11Device;
using D3D11Texture2D = Vortice.Direct3D11.ID3D11Texture2D;

namespace CouchLink.Video;

/// <summary>
/// Shows decoded pictures in a window: the D3D11 video processor converts NV12 to BGRA, scales and
/// letterboxes straight into a flip-model swap chain's back buffer, Direct2D draws any text on top,
/// and Present(0, AllowTearing) shows it without waiting for vsync. Software pictures are uploaded
/// through a staging texture first. Used from the player thread.
/// </summary>
public sealed unsafe class FramePresenter : IFramePresenter
{
    private const int MaxCachedInputs = 32;

    private readonly D3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGISwapChain1 _swapChain;
    private readonly bool _tearing;
    private readonly ID3D11VideoDevice _videoDevice;
    private readonly ID3D11VideoContext1 _videoContext;
    private readonly ID2D1Factory1 _d2dFactory;
    private readonly ID2D1Device _d2dDevice;
    private readonly ID2D1DeviceContext _d2d;
    private readonly IDWriteFactory _dwrite;
    private readonly IDWriteTextFormat _statsFont, _statusFont;
    private readonly ID2D1SolidColorBrush _text, _panel;
    private readonly Dictionary<(nint Texture, uint Slice), ID3D11VideoProcessorInputView> _inputs = [];
    private ID3D11VideoProcessorEnumerator? _enumerator;
    private ID3D11VideoProcessor? _processor;
    private D3D11Texture2D? _staging, _upload;
    private ID3D11VideoProcessorInputView? _uploadView;
    private (int Width, int Height) _picture, _window;

    public FramePresenter(D3D11Device device, ID3D11DeviceContext context, nint window, int width, int height)
    {
        _device = device;
        _context = context;
        _window = (width, height);
        using (var factory = DXGI.CreateDXGIFactory2<IDXGIFactory5>(false))
        {
            _tearing = factory.PresentAllowTearing;
            _swapChain = factory.CreateSwapChainForHwnd(device, window, new SwapChainDescription1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2,
                BufferUsage = Usage.RenderTargetOutput,
                SwapEffect = SwapEffect.FlipDiscard,
                SampleDescription = new SampleDescription(1, 0),
                Scaling = Scaling.Stretch,
                Flags = SwapFlags,
            });
            factory.MakeWindowAssociation(window, WindowAssociationFlags.IgnoreAltEnter);
        }
        _videoDevice = device.QueryInterface<ID3D11VideoDevice>();
        _videoContext = context.QueryInterface<ID3D11VideoContext1>();

        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory1>();
        using (var dxgiDevice = device.QueryInterface<IDXGIDevice>())
            _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _d2d = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);
        _dwrite = DWrite.DWriteCreateFactory<IDWriteFactory>();
        _statsFont = _dwrite.CreateTextFormat("Consolas", FontWeight.Normal, FontStyle.Normal, FontStretch.Normal, 18);
        _statusFont = _dwrite.CreateTextFormat("Segoe UI", FontWeight.SemiBold, FontStyle.Normal, FontStretch.Normal, 32);
        _text = _d2d.CreateSolidColorBrush(new Color4(1f, 1f, 1f, 1f));
        _panel = _d2d.CreateSolidColorBrush(new Color4(0f, 0f, 0f, 0.65f));
    }

    private SwapChainFlags SwapFlags => _tearing ? SwapChainFlags.AllowTearing : SwapChainFlags.None;

    /// <summary>The window's client area changed size.</summary>
    public void Resize(int width, int height)
    {
        if ((width, height) == _window || width <= 0 || height <= 0)
            return;
        _window = (width, height);
        _swapChain.ResizeBuffers(2, (uint)width, (uint)height, Format.Unknown, SwapFlags).CheckError();
        ResetProcessor(); // output size changed
    }

    public void Present(DecodedPicture? picture, string? status, string? stats)
    {
        using (var back = _swapChain.GetBuffer<D3D11Texture2D>(0))
        {
            if (picture is { } p)
                Blt(p, back);
            if (picture is null || status is not null || stats is not null)
                DrawText(back, clear: picture is null, status, stats);
        }
        _swapChain.Present(0, _tearing ? PresentFlags.AllowTearing : PresentFlags.None).CheckError();
    }

    private void Blt(DecodedPicture p, D3D11Texture2D back)
    {
        EnsureProcessor(p.Width, p.Height);
        var input = p.OnGpu ? GpuInput((AVFrame*)p.Frame) : Upload((AVFrame*)p.Frame);
        _videoContext.VideoProcessorSetStreamColorSpace1(_processor!, 0, p.Color);
        using var output = _videoDevice.CreateVideoProcessorOutputView(back, _enumerator!, new VideoProcessorOutputViewDescription
        {
            ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
        });
        _videoContext.VideoProcessorBlt(_processor!, output, 0, 1,
            [new VideoProcessorStream { Enable = true, InputSurface = input }]).CheckError();
    }

    private void EnsureProcessor(int width, int height)
    {
        if (_processor is not null && _picture == (width, height))
            return;
        ResetProcessor();
        _picture = (width, height);
        _enumerator = _videoDevice.CreateVideoProcessorEnumerator(new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputFrameRate = new Rational(60, 1),
            InputWidth = (uint)width,
            InputHeight = (uint)height,
            OutputFrameRate = new Rational(60, 1),
            OutputWidth = (uint)_window.Width,
            OutputHeight = (uint)_window.Height,
            Usage = VideoUsage.PlaybackNormal,
        });
        _processor = _videoDevice.CreateVideoProcessor(_enumerator, 0);
        var fit = Letterbox.Fit(width, height, _window.Width, _window.Height);
        _videoContext.VideoProcessorSetOutputColorSpace1(_processor, ColorSpaceType.RgbFullG22NoneP709);
        _videoContext.VideoProcessorSetStreamFrameFormat(_processor, 0, VideoFrameFormat.Progressive);
        _videoContext.VideoProcessorSetStreamAutoProcessingMode(_processor, 0, false);
        _videoContext.VideoProcessorSetStreamSourceRect(_processor, 0, true, new Vortice.RawRect(0, 0, width, height));
        _videoContext.VideoProcessorSetStreamDestRect(_processor, 0, true,
            new Vortice.RawRect(fit.X, fit.Y, fit.X + fit.Width, fit.Y + fit.Height));
        _videoContext.VideoProcessorSetOutputTargetRect(_processor, true, new Vortice.RawRect(0, 0, _window.Width, _window.Height));
        _videoContext.VideoProcessorSetOutputBackgroundColor(_processor, false,
            new Vortice.Direct3D11.VideoColor { Rgba = new VideoColorRgba { R = 0, G = 0, B = 0, A = 1 } });
    }

    private ID3D11VideoProcessorInputView GpuInput(AVFrame* frame)
    {
        var key = ((nint)frame->data[0], (uint)(nint)frame->data[1]);
        if (_inputs.TryGetValue(key, out var view))
            return view;
        if (_inputs.Count >= MaxCachedInputs)
            ClearInputs(); // a new decoder's textures; the old ones can go
        var texture = new D3D11Texture2D(key.Item1); // owned by FFmpeg's pool; never disposed here
        view = _videoDevice.CreateVideoProcessorInputView(texture, _enumerator!, new VideoProcessorInputViewDescription
        {
            ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = key.Item2 },
        });
        _inputs[key] = view;
        return view;
    }

    /// <summary>Copies a software picture's planes into an NV12 texture the video processor can read.</summary>
    private ID3D11VideoProcessorInputView Upload(AVFrame* frame)
    {
        int w = frame->width, h = frame->height;
        if (_upload is null)
        {
            var description = new Texture2DDescription
            {
                Width = (uint)w,
                Height = (uint)h,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.NV12,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                CPUAccessFlags = CpuAccessFlags.Write,
            };
            _staging = _device.CreateTexture2D(description);
            description.Usage = ResourceUsage.Default;
            description.CPUAccessFlags = CpuAccessFlags.None;
            description.BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget; // AMD rejects ShaderResource alone
            _upload = _device.CreateTexture2D(description);
            _uploadView = _videoDevice.CreateVideoProcessorInputView(_upload, _enumerator!, new VideoProcessorInputViewDescription
            {
                ViewDimension = VideoProcessorInputViewDimension.Texture2D,
            });
        }

        var map = _context.Map(_staging!, 0, MapMode.Write);
        try
        {
            byte* dst = (byte*)map.DataPointer;
            int pitch = (int)map.RowPitch;
            for (int y = 0; y < h; y++)
                Buffer.MemoryCopy(frame->data[0] + y * frame->linesize[0], dst + y * pitch, w, w);
            byte* uv = dst + pitch * h; // NV12: interleaved UV rows follow the luma rows
            for (int y = 0; y < h / 2; y++)
            {
                byte* u = frame->data[1] + y * frame->linesize[1];
                byte* v = frame->data[2] + y * frame->linesize[2];
                byte* row = uv + y * pitch;
                for (int x = 0; x < w / 2; x++)
                {
                    row[2 * x] = u[x];
                    row[2 * x + 1] = v[x];
                }
            }
        }
        finally
        {
            _context.Unmap(_staging!, 0);
        }
        _context.CopyResource(_upload!, _staging!);
        return _uploadView!;
    }

    private void DrawText(D3D11Texture2D back, bool clear, string? status, string? stats)
    {
        using var surface = back.QueryInterface<IDXGISurface>();
        using var target = _d2d.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96,
            BitmapOptions.Target | BitmapOptions.CannotDraw));
        _d2d.Target = target;
        _d2d.BeginDraw();
        if (clear)
            _d2d.Clear(new Color4(0f, 0f, 0f, 1f));
        if (stats is not null)
            Panel(stats, _statsFont, 24, 24, centred: false);
        if (status is not null)
            Panel(status, _statusFont, _window.Width / 2f, _window.Height / 2f, centred: true);
        _d2d.EndDraw();
        _d2d.Target = null;
    }

    private void Panel(string text, IDWriteTextFormat font, float x, float y, bool centred)
    {
        using var layout = _dwrite.CreateTextLayout(text, font, _window.Width, _window.Height);
        var size = layout.Metrics;
        if (centred)
        {
            x -= size.Width / 2;
            y -= size.Height / 2;
        }
        _d2d.FillRectangle(new Rect(x - 12, y - 8, size.Width + 24, size.Height + 16), _panel);
        _d2d.DrawTextLayout(new Vector2(x, y), layout, _text);
    }

    private void ClearInputs()
    {
        foreach (var view in _inputs.Values)
            view.Dispose();
        _inputs.Clear();
    }

    private void ResetProcessor()
    {
        ClearInputs();
        _uploadView?.Dispose(); _uploadView = null;
        _upload?.Dispose(); _upload = null;
        _staging?.Dispose(); _staging = null;
        _processor?.Dispose(); _processor = null;
        _enumerator?.Dispose(); _enumerator = null;
    }

    public void Dispose()
    {
        ResetProcessor();
        _text.Dispose();
        _panel.Dispose();
        _statsFont.Dispose();
        _statusFont.Dispose();
        _dwrite.Dispose();
        _d2d.Dispose();
        _d2dDevice.Dispose();
        _d2dFactory.Dispose();
        _videoContext.Dispose();
        _videoDevice.Dispose();
        _swapChain.Dispose();
    }
}
```

The Vortice names above (`D2D1.D2D1CreateFactory`, `CreateBitmapFromDxgiSurface`, `BitmapProperties1`, `Vortice.DCommon.PixelFormat`, `IDWriteTextLayout.Metrics`, `DrawTextLayout`) were written against Vortice 3.8.3 but not compiled in the spike. If one is named differently in the package, use the package's name and record it as a ruling; the calls and their order are the design.

`src/CouchLink.Video/PlayerWindow.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CouchLink.Video;

/// <summary>
/// The player's Win32 window: borderless and covering the monitor of <c>nearWindow</c> (or 1280x720
/// windowed, for testing on one PC), cursor hidden. F2 toggles stats; Ctrl+Alt+Q, Alt+F4 and the close
/// button ask to leave. Messages are handled on the thread that created it.
/// </summary>
public sealed unsafe partial class PlayerWindow : IDisposable
{
    private const string ClassName = "CouchLinkPlayer";
    private const uint WS_POPUP = 0x80000000, WS_VISIBLE = 0x10000000, WS_OVERLAPPEDWINDOW = 0x00CF0000;
    private const uint WS_EX_APPWINDOW = 0x00040000;
    private const uint WM_DESTROY = 0x0002, WM_SIZE = 0x0005, WM_CLOSE = 0x0010, WM_ERASEBKGND = 0x0014,
        WM_SETCURSOR = 0x0020, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_SYSCOMMAND = 0x0112;
    private const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_F2 = 0x71, VK_Q = 0x51;
    private const int HTCLIENT = 1, SC_KEYMENU = 0xF100;
    private const uint PM_REMOVE = 1, QS_ALLINPUT = 0x04FF, MWMO_INPUTAVAILABLE = 0x0004, MONITOR_DEFAULTTOPRIMARY = 1;

    private static readonly Dictionary<nint, PlayerWindow> Windows = [];
    private static bool _registered;
    private bool _destroyed;

    public PlayerWindow(nint nearWindow, bool windowed)
    {
        RegisterClassOnce();
        var info = new MonitorInfo { Size = (uint)sizeof(MonitorInfo) };
        GetMonitorInfoW(MonitorFromWindow(nearWindow, MONITOR_DEFAULTTOPRIMARY), ref info);
        var m = info.Monitor;
        int x = m.Left, y = m.Top, w = m.Right - m.Left, h = m.Bottom - m.Top;
        uint style = WS_POPUP | WS_VISIBLE;
        if (windowed)
        {
            var frame = new Rect { Right = 1280, Bottom = 720 };
            style = WS_OVERLAPPEDWINDOW | WS_VISIBLE;
            AdjustWindowRectEx(ref frame, style, false, WS_EX_APPWINDOW);
            w = frame.Right - frame.Left;
            h = frame.Bottom - frame.Top;
            x = m.Left + (m.Right - m.Left - w) / 2;
            y = m.Top + (m.Bottom - m.Top - h) / 2;
        }

        Handle = CreateWindowExW(WS_EX_APPWINDOW, ClassName, "CouchLink", style, x, y, w, h, 0, 0, GetModuleHandleW(null), 0);
        if (Handle == 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Creating the video window failed");
        lock (Windows)
            Windows[Handle] = this;
        GetClientRect(Handle, out var client);
        Width = client.Right;
        Height = client.Bottom;
        SetForegroundWindow(Handle);
    }

    public nint Handle { get; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    public event Action? CloseRequested;
    public event Action? StatsToggled;
    public event Action<int, int>? Resized;

    /// <summary>Handles every waiting message. False once the window is gone.</summary>
    public bool PumpMessages()
    {
        while (PeekMessageW(out var message, 0, 0, 0, PM_REMOVE))
        {
            TranslateMessage(in message);
            DispatchMessageW(in message);
        }
        return !_destroyed;
    }

    /// <summary>Sleeps until <paramref name="handle"/> is set, a message arrives, or the timeout passes.</summary>
    public void WaitForInput(WaitHandle handle, TimeSpan timeout)
    {
        nint h = handle.SafeWaitHandle.DangerousGetHandle();
        MsgWaitForMultipleObjectsEx(1, &h, (uint)timeout.TotalMilliseconds, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    }

    private nint? OnMessage(uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_KEYDOWN or WM_SYSKEYDOWN:
                bool repeat = (lParam & (1 << 30)) != 0;
                if (wParam == VK_F2 && !repeat)
                {
                    StatsToggled?.Invoke();
                    return 0;
                }
                if (wParam == VK_Q && GetKeyState(VK_CONTROL) < 0 && GetKeyState(VK_MENU) < 0)
                {
                    CloseRequested?.Invoke();
                    return 0;
                }
                return null; // Alt+F4 goes on to DefWindowProc, which sends WM_CLOSE
            case WM_SYSCOMMAND when ((int)wParam & 0xFFF0) == SC_KEYMENU:
                return 0; // Alt alone must not open a window menu and steal keys
            case WM_SETCURSOR when (lParam & 0xFFFF) == HTCLIENT:
                SetCursor(0);
                return 1;
            case WM_ERASEBKGND:
                return 1;
            case WM_SIZE:
                Width = (int)(lParam & 0xFFFF);
                Height = (int)((lParam >> 16) & 0xFFFF);
                if (Width > 0 && Height > 0)
                    Resized?.Invoke(Width, Height);
                return 0;
            case WM_CLOSE:
                CloseRequested?.Invoke(); // the owner decides; the window stays until disposed
                return 0;
            case WM_DESTROY:
                _destroyed = true;
                return 0;
            default:
                return null;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WndProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        PlayerWindow? window;
        lock (Windows)
            Windows.TryGetValue(hwnd, out window);
        return window?.OnMessage(message, wParam, lParam) ?? DefWindowProcW(hwnd, message, wParam, lParam);
    }

    private static void RegisterClassOnce()
    {
        lock (Windows)
        {
            if (_registered)
                return;
            fixed (char* name = ClassName)
            {
                var wc = new WndClassEx
                {
                    Size = (uint)sizeof(WndClassEx),
                    WndProc = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WndProc,
                    Instance = GetModuleHandleW(null),
                    ClassName = name,
                };
                if (RegisterClassExW(in wc) == 0)
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), "Registering the video window class failed");
            }
            _registered = true;
        }
    }

    public void Dispose()
    {
        if (!_destroyed)
            DestroyWindow(Handle);
        lock (Windows)
            Windows.Remove(Handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public nint Hwnd; public uint Message; public nint WParam, LParam; public uint Time; public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public uint Size, Style;
        public nint WndProc;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public char* MenuName, ClassName;
        public nint IconSmall;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial ushort RegisterClassExW(in WndClassEx wc);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateWindowExW(uint exStyle, string className, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);

    [LibraryImport("user32.dll")]
    private static partial nint DefWindowProcW(nint hwnd, uint message, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PeekMessageW(out Msg message, nint hwnd, uint min, uint max, uint remove);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(in Msg message);

    [LibraryImport("user32.dll")]
    private static partial nint DispatchMessageW(in Msg message);

    [LibraryImport("user32.dll")]
    private static partial uint MsgWaitForMultipleObjectsEx(uint count, nint* handles, uint milliseconds, uint wakeMask, uint flags);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromWindow(nint hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AdjustWindowRectEx(ref Rect rect, uint style, [MarshalAs(UnmanagedType.Bool)] bool menu, uint exStyle);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    private static partial nint SetCursor(nint cursor);

    [LibraryImport("user32.dll")]
    private static partial short GetKeyState(int key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(nint hwnd);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
```

`src/CouchLink.Video/VideoPlayer.cs`:

```csharp
using CouchLink.Core.Video;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using static Vortice.Direct3D11.D3D11;

namespace CouchLink.Video;

public readonly record struct PlayerOptions(nint NearWindow = 0, bool Windowed = false, bool PreferHardware = true);

/// <summary>
/// The client's video window. Its own thread owns the window, the D3D11 device, the decoder and the
/// presenter, and runs <see cref="PlayerCore"/> whenever a frame arrives or a window message does.
/// <see cref="Enqueue"/> is called from the receive thread. <c>closeRequested</c> runs on the player
/// thread: post it to the UI thread, and never Dispose the player from inside it.
/// </summary>
public sealed class VideoPlayer : IDisposable
{
    private static readonly TimeSpan IdleWait = TimeSpan.FromMilliseconds(50);

    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new();
    private volatile bool _stop;
    private Exception? _startError;
    private PlayerCore? _core;

    public VideoPlayer(PlayerOptions options, Func<VideoClientStats> stats, Action decodeFailed,
        Action closeRequested, Action<string>? log = null)
    {
        _thread = new Thread(() => Run(options, stats, decodeFailed, closeRequested, log))
        {
            IsBackground = true,
            Name = "CouchLink player",
            Priority = ThreadPriority.AboveNormal,
        };
        _thread.Start();
        _ready.Wait();
        if (_startError is { } e)
        {
            _thread.Join();
            throw new InvalidOperationException($"The video window could not start: {e.Message}", e);
        }
    }

    public nint WindowHandle { get; private set; }
    public string? HardwareDecodeError { get; private set; }
    public string DecoderName => _core?.DecoderName ?? "";
    public long FramesShown => _core?.FramesShown ?? 0;
    public TimeSpan ClientDelay => _core?.ClientDelay ?? TimeSpan.Zero;

    public void Enqueue(AssembledFrame frame) => _core!.Enqueue(frame);

    private void Run(PlayerOptions options, Func<VideoClientStats> stats, Action decodeFailed,
        Action closeRequested, Action<string>? log)
    {
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        PlayerWindow? window = null;
        FramePresenter? presenter = null;
        PlayerCore? core = null;
        TimerResolution? timer = null;
        try
        {
            if (!FfmpegLibrary.TryLoad(out var error))
                throw new InvalidOperationException(error);
            D3D11CreateDevice(null, DriverType.Hardware,
                DeviceCreationFlags.VideoSupport | DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0], out device, out context).CheckError();
            using (var multithread = device!.QueryInterface<ID3D11Multithread>())
                multithread.SetMultithreadProtected(true); // FFmpeg's D3D11VA locks the context too

            window = new PlayerWindow(options.NearWindow, options.Windowed);
            presenter = new FramePresenter(device, context!, window.Handle, window.Width, window.Height);
            var d = device;
            core = new PlayerCore(
                hardware => hardware && options.PreferHardware ? OpenHardware(d, log) : H264Decoder.OpenSoftware(),
                presenter, stats, decodeFailed, TimeProvider.System, log);
            var c = core;
            var p = presenter;
            window.StatsToggled += () => c.ShowStats = !c.ShowStats;
            window.CloseRequested += closeRequested;
            window.Resized += p.Resize;
            timer = new TimerResolution(); // precise wakeups for the wait below
            WindowHandle = window.Handle;
            _core = core;
        }
        catch (Exception e)
        {
            _startError = e;
            core?.Dispose();
            presenter?.Dispose();
            window?.Dispose();
            context?.Dispose();
            device?.Dispose();
            _ready.Set();
            return;
        }
        _ready.Set();

        try
        {
            while (!_stop && window.PumpMessages())
            {
                core.Run();
                window.WaitForInput(core.FrameReady, IdleWait);
            }
        }
        catch (Exception e)
        {
            log?.Invoke($"Video player error: {e}");
            closeRequested();
        }
        finally
        {
            timer.Dispose();
            core.Dispose();
            presenter.Dispose();
            window.Dispose();
            context!.Dispose();
            device!.Dispose();
        }
    }

    private H264Decoder OpenHardware(ID3D11Device device, Action<string>? log)
    {
        var decoder = H264Decoder.Open(device, out var error);
        if (error is not null)
        {
            HardwareDecodeError = error;
            log?.Invoke($"Hardware video decoding unavailable ({error}); using software decoding");
        }
        return decoder;
    }

    public void Dispose()
    {
        _stop = true;
        _thread.Join(); // the loop wakes within IdleWait
        _ready.Dispose();
    }
}
```

`TimerResolution` is `internal` to `CouchLink.Video`, which `VideoPlayer` is part of. If the compiler's flow analysis complains that `window`, `core`, `presenter` or `timer` may be null after the `try`, add `!` there; they are all set when `_startError` is null.

- [ ] **Step 6: Add `VideoTest play`**

In `src/CouchLink.VideoTest/Program.cs`, extend the usage check and dispatch:

```csharp
if (args.Length == 0 || args[0] is not ("capture" or "encode" or "play"))
{
    Console.WriteLine("Usage: VideoTest capture [seconds] | VideoTest encode [seconds] [--resolution=1080p] [--fps=60] [--encoder=h264_amf] [--out=videotest.h264]");
    Console.WriteLine("       VideoTest play <file.h264> [more files] [--software] [--windowed] [--fps=60]");
    return 2;
}

if (args[0] == "play")
    return Play(args);
```

(before the `int seconds = ...` line), and add the method:

```csharp
static int Play(string[] args)
{
    var files = args.Skip(1).Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
    if (files.Count == 0 || !FfmpegLibrary.TryLoad(out var error) && Fail(error))
        return 2;
    int fps = int.Parse(args.FirstOrDefault(a => a.StartsWith("--fps=", StringComparison.Ordinal))?[6..] ?? "60");
    var options = new PlayerOptions(Windowed: args.Contains("--windowed"), PreferHardware: !args.Contains("--software"));

    int decodeFailures = 0;
    bool closed = false;
    using var player = new VideoPlayer(options, () => default, () => decodeFailures++, () => closed = true, Console.WriteLine);
    Console.WriteLine($"Decoder: {player.DecoderName}" + (player.HardwareDecodeError is { } e ? $" (hardware unavailable: {e})" : ""));

    long total = 0;
    var interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / fps);
    var clock = Stopwatch.StartNew();
    uint number = 0;
    foreach (var file in files)
    {
        var frames = H264Frames.Split(File.ReadAllBytes(file));
        Console.WriteLine($"{file}: {frames.Count} frames");
        for (int i = 0; i < frames.Count && !closed; i++, number++, total++)
        {
            player.Enqueue(new AssembledFrame(number, i == 0, frames[i]));
            var due = interval * (total + 1);
            while (clock.Elapsed < due)
                Thread.Sleep(1);
        }
    }
    Thread.Sleep(500); // let the last frames show
    Console.WriteLine($"Shown {player.FramesShown} of {total} frames with {player.DecoderName}, " +
        $"client delay {player.ClientDelay.TotalMilliseconds:0.0} ms, {decodeFailures} decode failures");

    bool ok = player.FramesShown >= total * 0.9 && decodeFailures == 0;
    Console.WriteLine(ok ? "PASS" : "FAIL: frames were dropped or failed to decode");
    return ok ? 0 : 1;

    static bool Fail(string? message)
    {
        Console.WriteLine($"FAIL: {message}");
        return true;
    }
}
```

(`Thread.Sleep(1)` pacing only holds 60 fps with a 1 ms timer; the player raises it while it runs. Add `using CouchLink.Core.Video;` if it isn't there; it is.)

- [ ] **Step 7: Build and run the unit tests**

Run: `dotnet build` then `dotnet test`
Expected: 0 warnings; all tests pass.

- [ ] **Step 8: Check on hardware**

Record test streams first, if they aren't there from earlier checks:

Run: `dotnet run --project src/CouchLink.VideoTest -c Release -- encode 4 --out=artifacts/play1080.h264` and `dotnet run --project src/CouchLink.VideoTest -c Release -- encode 4 --resolution=720p --out=artifacts/play720.h264`
Expected: both PASS.

Then:

1. `dotnet run --project src/CouchLink.VideoTest -c Release -- play artifacts/play1080.h264`
   Expected: a borderless fullscreen window shows the recorded desktop, correctly coloured; console `Decoder: D3D11VA`, `Shown N of N frames`, client delay a few ms, `PASS`.
2. Same with `--software`. Expected: `Decoder: software`, `PASS`.
3. `play artifacts/play1080.h264 artifacts/play720.h264` (the stream size changes mid-run, Review Focus 4). Expected: both parts show letterboxed correctly; `PASS`.
4. During a run, press F2: the stats panel appears top-left ("Collecting stats..." then numbers; the latency line says "measuring..." because `play` has no host). Press Ctrl+Alt+Q: the run ends early (`closed`), FAIL is expected for that run only.
5. A screenshot of run 1 (the recorded desktop is the user's own screen: view it, then delete it) to check colours against the real desktop.

Record what each run printed in the ledger.

- [ ] **Step 9: Commit**

```bash
git add src/CouchLink.Video src/CouchLink.VideoTest tests/CouchLink.Video.Tests
git commit -S -m "feat(video): fullscreen player window with D3D11 presentation and overlay" -m "Refs #17, #18"
```

---

### Task 8: The client shows the host's screen

**Files:**
- Modify: `src/CouchLink.Core/DevOptions.cs`, `tests/CouchLink.Core.Tests/DevOptionsTests.cs`
- Modify: `src/CouchLink.App/ClientVideoService.cs`
- Modify: `src/CouchLink.App/Input/RawInputSource.cs`, `src/CouchLink.App/Input/NativeMethods.cs`
- Modify: `src/CouchLink.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `VideoPlayer`, `PlayerOptions` (Task 7); `VideoClient` with `sendTimingPing` and `DecodeFailed` (Task 3); `InputSender.SendTimingPing` (Task 2).
- Produces: `DevOptions(bool TestPattern, string? SaveVideoPath, bool WindowedPlayer = false)` parsing `--windowed-player`; `ClientVideoService.TryStart(InputSender sender, PlayerOptions options, string? savePath, Action leave, out ClientVideoService? service, out string? error)` and `nint PlayerWindow`; `RawInputSource(HwndSource source, Func<bool> inputAllowed)` with event `Action? InputSuspended`.

- [ ] **Step 1: Write the failing test**

Add to `DevOptionsTests.cs`:

```csharp
    [Fact]
    public void Windowed_player_is_a_dev_switch()
    {
        Assert.True(DevOptions.Parse(["--windowed-player"]).WindowedPlayer);
        Assert.False(DevOptions.Parse([]).WindowedPlayer);
    }
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/CouchLink.Core.Tests --filter DevOptionsTests`
Expected: build FAILS: `WindowedPlayer` doesn't exist.

- [ ] **Step 3: Implement `--windowed-player`**

`DevOptions.cs`:

```csharp
/// <summary>
/// Developer switches on the command line: <c>--test-pattern</c> streams Plan 3's test pattern
/// instead of the screen (clients can't display it: it isn't H.264); <c>--save-video=&lt;file&gt;</c>
/// makes a client save the H.264 it receives; <c>--windowed-player</c> opens the client's video in a
/// 1280x720 window instead of fullscreen, for testing host and client on one PC.
/// </summary>
public sealed record DevOptions(bool TestPattern, string? SaveVideoPath, bool WindowedPlayer = false)
```

and in `Parse`, `bool windowed = false;`, `else if (arg == "--windowed-player") windowed = true;`, `return new DevOptions(testPattern, savePath, windowed);`.

Run: `dotnet test tests/CouchLink.Core.Tests --filter DevOptionsTests`
Expected: PASS.

- [ ] **Step 4: Feed the player**

`ClientVideoService.cs`, replacing the class:

```csharp
using System.IO;
using CouchLink.Core.Net;
using CouchLink.Core.Video;
using CouchLink.Video;

namespace CouchLink.App;

/// <summary>
/// Client side: receives the host's video on UDP 47802 and shows it in the player window (and,
/// with --save-video=&lt;file&gt;, also saves the H.264). <c>leave</c> runs on the player thread when
/// the player asks to leave (Ctrl+Alt+Q, Alt+F4). Dispose before the <see cref="InputSender"/> it
/// sends keyframe requests and timing pings through.
/// </summary>
internal sealed class ClientVideoService : IDisposable
{
    private readonly VideoClient _client;
    private readonly VideoPlayer _player;
    private readonly FileStream? _save;

    private ClientVideoService(VideoReceiver receiver, InputSender sender, PlayerOptions options, string? savePath, Action leave)
    {
        VideoClient? client = null;
        _player = new VideoPlayer(options, () => client?.Stats ?? default, () => client?.DecodeFailed(),
            leave, message => AppServices.Log.Write(message));
        _save = savePath is null ? null : File.Create(savePath);
        client = new VideoClient(
            receiver, sender.SendKeyframeRequest, OnFrame, TimeProvider.System,
            e => AppServices.Log.Write($"Video error: {e}"), sender.SendTimingPing);
        _client = client;
    }

    public static bool TryStart(InputSender sender, PlayerOptions options, string? savePath, Action leave,
        out ClientVideoService? service, out string? error)
    {
        service = null;
        if (!VideoReceiver.TryCreate(Ports.Video, out var receiver, out error))
            return false;
        try
        {
            service = new ClientVideoService(receiver!, sender, options, savePath, leave);
            return true;
        }
        catch (InvalidOperationException e)
        {
            receiver!.Dispose();
            error = e.Message;
            return false;
        }
    }

    public nint PlayerWindow => _player.WindowHandle;

    private void OnFrame(AssembledFrame frame) // receive thread only
    {
        _save?.Write(frame.Data);
        _player.Enqueue(frame);
    }

    public string Describe()
    {
        var s = _client.Stats;
        return $"Video: {_player.FramesShown} shown ({_player.DecoderName}), {s.Receive.ShardsRecovered} repaired, " +
               $"{s.Receive.FramesLost} lost, loss {s.Receive.LossPercent:0.0}%" +
               (s.WaitingForKeyframe ? ", waiting for keyframe" : "") +
               (s.HostPaused ? ", host screen paused" : "") +
               (s.RoundTrip is { } rtt ? $", round trip {rtt.TotalMilliseconds:0.0} ms" : "") +
               (_save is null ? "" : $"\nSaving to {_save.Name}");
    }

    public void Dispose()
    {
        _client.Dispose(); // stops the receive thread before the player and the file close
        _player.Dispose();
        _save?.Dispose();
    }
}
```

The receiver is created first (a port clash is the likeliest failure, and nothing needs cleaning up yet); if the player can't start, the receiver is closed and the message goes to the user. A frame can't reach `_player` before it exists, because the `VideoClient` that delivers frames is created after it.

- [ ] **Step 5: Let input through while the player is in front**

`NativeMethods.cs`: add `public const uint RIDEV_INPUTSINK = 0x00000100;` and

```csharp
    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();
```

`RawInputSource.cs`:

```csharp
    private readonly Func<bool> _inputAllowed;
    private bool _suspended;

    /// <summary>Raised once when input arrives while no CouchLink window is in front; release every key.</summary>
    public event Action? InputSuspended;

    public RawInputSource(HwndSource source, Func<bool> inputAllowed)
    {
        _source = source;
        _inputAllowed = inputAllowed;
        RAWINPUTDEVICE[] devices =
        [
            // Input sink: the main window gets input while the player window is in front too.
            new() { UsagePage = 0x01, Usage = 0x06, Flags = RIDEV_INPUTSINK, Target = source.Handle }, // keyboard
            new() { UsagePage = 0x01, Usage = 0x02, Flags = RIDEV_INPUTSINK, Target = source.Handle }, // mouse
        ];
        ...
    }
```

and at the start of `WndProc`, right after `if (msg != WM_INPUT) return IntPtr.Zero;` (keep its exact existing form):

```csharp
        if (!_inputAllowed())
        {
            if (!_suspended)
            {
                _suspended = true;
                InputSuspended?.Invoke();
            }
            return IntPtr.Zero; // another app is in front: its keys are not ours
        }
        _suspended = false;
```

- [ ] **Step 6: Open the player on Join, leave on Ctrl+Alt+Q**

`MainWindow.xaml.cs`:

```csharp
    private bool InputAllowed()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        return foreground == new WindowInteropHelper(this).Handle
            || (_video is not null && foreground == _video.PlayerWindow);
    }
```

(`NativeMethods` is in `CouchLink.App.Input`; it's already imported.) Change the `Deactivated` handler in the constructor to
`Deactivated += (_, _) => { if (!InputAllowed()) _mapper?.ReleaseAll(); }; // never leave keys stuck`.

In `OnJoin`, replace the `ClientVideoService.TryStart` call with:

```csharp
        var playerOptions = new PlayerOptions(new WindowInteropHelper(this).Handle, AppServices.Options.WindowedPlayer);
        if (!ClientVideoService.TryStart(inputSender, playerOptions, AppServices.Options.SaveVideoPath,
                () => Dispatcher.InvokeAsync(LeaveSession), out _video, out var videoError))
```

and the raw input line with:

```csharp
        _rawInput = new RawInputSource((HwndSource)PresentationSource.FromVisual(this), InputAllowed);
        _rawInput.InputSuspended += _mapper.ReleaseAll;
```

Add:

```csharp
    /// <summary>Back to the start: the player closed (Ctrl+Alt+Q) or failed.</summary>
    private void LeaveSession()
    {
        if (_client is null)
            return;
        _video?.Dispose();
        _video = null;
        _client.Dispose();
        _client = null;
        _rawInput?.Dispose();
        _rawInput = null;
        _mapper = null;
        HostButton.IsEnabled = JoinButton.IsEnabled = HostIpBox.IsEnabled = SlotBox.IsEnabled = true;
        ResolutionBox.IsEnabled = FrameRateBox.IsEnabled = true;
        AppServices.DescribeMode = () => "Idle";
        StatusText.Text = "Left the session.";
        AppServices.Log.Write("Left the session");
        Activate();
    }
```

`ClientInputLoop.Dispose` sends a neutral pad state and disposes the `InputSender`, so the video service (which sends keyframe requests and pings through that sender) is disposed first. `"Idle"` is `AppServices.DescribeMode`'s starting value.

- [ ] **Step 7: Check it end to end on one PC**

1. `dotnet build -c Release`, then start two copies of `src/CouchLink.App/bin/Release/.../CouchLink.App.exe`: one hosts (default settings), the other is started with `--windowed-player` and joins `127.0.0.1` as P2.
   Expected: a 1280x720 "CouchLink" window shows the host's desktop (including itself, mirrored) within a second, with no "Waiting" text. The client's main window status shows `Video: N shown (D3D11VA)` with frames rising and `round trip` under 1 ms.
2. Click into the player window and press F2. Expected: stats panel with ~60 fps, a bitrate, `Packet loss 0.0%`, and `Latency ~N ms (host H + network 0 + client C)`. Record N, H, C in the ledger. N should be well under 35 ms; if not, record by how much and why (which part dominates) as a finding.
3. With the player in front, hold W: the client's status shows the left stick moving. Alt+Tab to another app while holding W, then release it: the status shows the stick back at the centre (keys released, Review Focus 5). Alt+Tab back: input works again.
4. Press Ctrl+Alt+Q in the player. Expected: the player closes, the client's main window says "Left the session." and Join works again.
5. Fullscreen: start the client without `--windowed-player` and join. Expected: a borderless fullscreen picture with no cursor; Ctrl+Alt+Q leaves. (On one PC this shows the host's own screen; that's fine.)
6. Host stops while a client watches. Expected: the picture freezes on the last frame (the player keeps redrawing); "Reconnecting..." is v1.4 (#23). Record it as expected behaviour.

- [ ] **Step 8: Run all tests and commit**

Run: `dotnet build` (0 warnings) and `dotnet test`
Expected: all pass.

```bash
git add src tests
git commit -S -m "feat(app): joining opens the host's screen fullscreen; Ctrl+Alt+Q leaves" -m "Closes #17"
```

---

### Task 9: Spec, README, notices

**Files:**
- Modify: `docs/superpowers/specs/2026-10-05-couchlink-design.md` (sections 3, 5.4, 7)
- Modify: `README.md`, `THIRD-PARTY-NOTICES.md`

- [ ] **Step 1: Spec**

Section 3 (ports table): make the two rows read

```
| UDP 47802 | Video + audio; host -> client timing replies |
| UDP 47803 | Input; client -> host keyframe requests and timing pings (until the v1.4 session channel) |
```

Section 5.4, after "no vsync wait", add:

```markdown
- The picture keeps its shape: it is scaled to fit the client's screen and centred, with black bars.
  Streams tagged BT.709 are shown as BT.709; anything else as BT.601 (the host's x264 path).
- A decode error (or hardware decode not starting) switches to software decoding and waits for the
  next keyframe; if the client falls more than 6 frames behind it skips to the newest keyframe.
- Before the first picture the window says "Waiting for the host's picture..."; while the host's
  capture is lost it shows the last picture with "Host screen paused".
```

Section 7, replace the Diagnostics sentence's first part with:

```markdown
**Diagnostics:** F2 overlay on the client: fps, bitrate, packet loss, FEC repairs, estimated latency.
The estimate is the host's capture-to-send time (sent in its timing replies) + half the measured
round trip (a timing ping every second) + the client's receive-to-present time. It leaves out the
display's own scan-out, so the real glass-to-glass time is a few ms more.
```

(keep the log file sentence after it).

- [ ] **Step 2: README and notices**

`README.md`, the status note:

```markdown
> **Status: early development.** Joining PCs see the host's screen with low latency (GPU capture,
> hardware H.264, FEC over UDP, GPU decode) and each gets its own virtual DualShock 4. Audio, the
> lobby and the join/approval flow are next. See the [roadmap](ROADMAP.md) and
> [the design spec](docs/superpowers/specs/2026-10-05-couchlink-design.md).
```

`THIRD-PARTY-NOTICES.md`, the Vortice row: `| Vortice.Windows (Direct3D11, DXGI, Direct2D1) | CouchLink.App, VideoTest | MIT | ... |`.

- [ ] **Step 3: Check and commit**

Run: `dotnet build` and `dotnet test`
Expected: all pass. Then `./eng/package.ps1` and check the zip has `Vortice.Direct2D1.dll` next to `CouchLink.App.exe`.

```bash
git add docs README.md THIRD-PARTY-NOTICES.md
git commit -S -m "docs: client playback, timing packets and the F2 overlay in the spec" -m "Closes #18"
```
