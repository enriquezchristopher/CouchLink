# CouchLink Plan 6: Audio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Clients hear everything the host PC plays: WASAPI loopback on the host, Opus at 48 kHz stereo in 5 ms frames over UDP 47802, and a ~15 ms jitter buffer on the client with redundancy, concealment and drift control, about 30 ms speaker to headphones.

**Architecture:** All decisions that can be tested without sound hardware live in `CouchLink.Core`: the audio packet (wire type 6), the frame slicer, the test tone, the jitter buffer, drift control, the host's `AudioStreamer` and the client's `AudioClient`, plus a `StreamDispatcher` that splits the client's one socket on 47802 between video and audio, and `LocalAddress` for the same-PC mute. A new `CouchLink.Audio` project wraps the hardware and codec: `OpusAudioEncoder`/`OpusAudioDecoder` (Concentus), `LoopbackSource` (NAudio process loopback, device loopback fallback) and `DefaultDevicePlayer` (NAudio low-latency output that follows the default device). The app starts audio beside video on both sides, each able to fail alone, and adds an "Audio" line to the dev window and the F2 overlay.

**Tech Stack:** C# / .NET 10, WPF, NAudio.Wasapi 3.1.0 (with NAudio.Core 3.1.0), Concentus 2.2.2, xUnit 2.9.3, Microsoft.Extensions.TimeProvider.Testing.

**Spec:** `docs/superpowers/specs/2026-10-07-couchlink-audio-design.md` (all of it; read its "Revisions" section). Main design: `docs/superpowers/specs/2026-10-05-couchlink-design.md` section 5.3.

**Builds on:** `main` @ 56a4e97 (release 1.3.0). Branch: `plan6-audio`. Issues: #19, #20.

## Spike findings (2026-10-07, this repo's dev PC, Windows 10 22H2, Realtek "High Definition Audio Device" headphones)

A throwaway console spike (outside the repo, deleted) checked every API this plan uses.

- **Concentus 2.2.2:** `OpusCodecFactory.CreateEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY)`, `Bitrate = 128000`, `Encode(ReadOnlySpan<short>, 240, Span<byte>, maxBytes)`: frames average **81 bytes** (69-108), **0.3 ms** to encode, **0.08 ms** to decode. `Decode(ReadOnlySpan<byte>.Empty, pcm, 240, false)` is PLC and returns 240 samples that carry on the sound. A garbage frame (`FF FF 01`) throws `OpusException`. `OpusCodecFactory.AttemptToUseNativeLibrary` is a static settable property.
- **Process loopback** (`WasapiRecorderBuilder().WithProcessLoopback(pid, ProcessLoopbackMode.ExcludeTargetProcessTree).WithFormat(48 kHz 16-bit stereo).WithEventSync()`): our own tone is left out (peak 1), another process's sound is captured (peak 3120). Its process **tree** is excluded, so a child process is too. Buffers are **1920 bytes (10 ms) every 10 ms, continuously, even when nothing plays** (zeros). One 30 ms late callback appeared when another stream started (data was delayed, not lost). `devicePosition` is always 0. The call needs Windows 10.0.19041, and the CA1416 analyzer fails the build unless it is guarded by `OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)`.
- **Device loopback** (`.WithDevice(defaultRender).WithLoopbackCapture()`): accepts the 48 kHz 16-bit format. It cannot be combined with `WithDefaultDeviceStreamRouting()` (throws). While the PC is silent it delivers nothing or empty buffers (length 0, position 0); the first buffer after silence has `AudioClientBufferFlags.DataDiscontinuity` (and `Silent`) and its position jumps.
- **Output:** `WasapiPlayerBuilder().WithDevice(d).WithSharedMode().WithEventSync().WithLowLatency(true)` gave **2-3 ms** buffers (reads of 512 bytes every 2.8 ms). `WithDefaultDeviceStreamRouting()` cannot be combined with low latency (throws) and gives 10 ms reads plus a 22 ms first fill. Both builders must use `BuildAsync()`. Building from an STA thread works when wrapped in `Task.Run(...)`. `IWaveProvider.Read(Span<byte>)` is NAudio 3's only read method. NAudio already has a class named `WasapiPlayer`.
- **Device notifications:** `MMDeviceEnumerator.CreateNotificationClient(bool useSynchronizationContext = true)` returns an `MMDeviceNotificationClient` with a `DefaultDeviceChanged` event (`Flow`, `Role`). Pass `false`.
- **Build:** both packages restore and build with `TreatWarningsAsErrors` on `net10.0-windows`, x64.

## Global Constraints

- C#, .NET 10. `CouchLink.Core` stays `net10.0` with no Windows APIs; NAudio and Concentus live only in `CouchLink.Audio` (`net10.0-windows`, `x64`). `TreatWarningsAsErrors` everywhere.
- Commits are signed. Conventional Commits. **No Claude attribution trailers** in commit messages or PR text.
- Audio format: **Opus, 48 kHz stereo, 5 ms frames (240 samples per channel), 128 kbps, restricted low-delay mode**, Concentus (pure C#).
- Wire: type **6 = audio**, host -> client on **UDP 47802** (the video port), existing 4-byte `Wire` header, version stays 1. Layout: stream ID u16 (never 0), sequence u32, frame length u16 (1-400), frame, previous length u16 (0 = none), previous frame. Little-endian.
- Client: **prime at 3 frames (15 ms)**; redundant copy, then **PLC for at most 4 frames (20 ms)**, then silence and prime again; above **12 frames (60 ms)** queued, cut to 3. Drift: one sample per frame dropped/repeated when the 1 s average depth is more than **1 frame (5 ms)** from the first second's average.
- Host: after a capture discontinuity, the next packet has no previous frame and the sequence jumps by **16**. Nothing is encoded or sent while no client is listening.
- **A client whose host is this PC does not play audio**; status says exactly **"muted: host is this PC"**. It still receives and counts packets.
- Audio and video are independent: either can fail to start or fail at runtime without stopping the other or the pads.
- Dev switches: **`--test-tone`** (host: 440 Hz, 100 ms on, 900 ms off) and **`--audio-loss=<percent>`** (client drops that share of audio packets).
- No volume, mute or device choice in CouchLink; no A/V sync.

## Review Focus

1. **A host or network hiccup delivers a burst of late packets** (the spike saw a 30 ms late capture callback). Frames already concealed are dropped as late and playback carries on from the next frame without stopping to prime again. Pinned in Task 3 (`A_late_burst_after_a_hiccup_resumes_at_once`).
2. **The host's capture restarts mid-session** (device unplugged, capture reopened). Clients go quiet briefly and then play the new audio from its first frame; nothing from the new stream is thrown away as late. Pinned in Task 6 (`After_a_host_discontinuity_playback_restarts_cleanly`) and Task 5 (`After_a_discontinuity_the_previous_frame_is_left_out_and_the_sequence_jumps`).
3. **Junk arrives on UDP 47802** (another program, a port scan, a corrupt packet). Nothing throws, and a failing audio handler never stops video. Pinned in Task 1 (`Random_bytes_with_an_audio_header_never_throw`) and Task 7 (`Datagrams_go_to_video_or_audio_by_type_and_the_rest_is_ignored`, `A_failing_audio_handler_does_not_stop_video`).
4. **The output device asks for odd-sized chunks** (low-latency mode pulls 128 frames, other drivers other sizes). Audio is continuous with no sample lost or repeated across frame boundaries. Pinned in Task 6 (`Odd_device_chunks_lose_nothing`).
5. **The tester types the host's LAN address, not 127.0.0.1, on a one-PC test.** The client still recognises its own PC and mutes. Pinned in Task 7 (`One_of_our_own_lan_addresses_is_this_pc`, `Every_address_this_pc_reports_is_this_pc`).

---

## File Structure

```
src/CouchLink.Core/Protocol/
  Wire.cs                    (modify) TypeAudio = 6, TryGetType
  AudioPacket.cs             type-6 packet: build and parse
src/CouchLink.Core/Audio/
  AudioFormat.cs             48 kHz, stereo, 240-sample frames, 128 kbps
  IAudioSource.cs            host: where 5 ms frames come from
  FrameSlicer.cs             any-size capture buffers -> exact frames
  TestToneSource.cs          --test-tone
  JitterBuffer.cs            ordering, priming, repair/conceal/silence, late, trim
  DriftControl.cs            drop/repeat one sample to hold the buffer depth
  AudioStreamer.cs           host thread: read, encode, send (+ IAudioEncoder)
  AudioClient.cs             client: Receive datagrams, Read PCM (+ IAudioDecoder)
src/CouchLink.Core/Net/
  StreamDispatcher.cs        one socket on 47802 -> video or audio
  LocalAddress.cs            is this address this PC?
src/CouchLink.Core/Video/
  VideoClient.cs             (modify) receiver-less constructor, public Receive
  OverlayText.cs             (modify) Audio line; Stats takes an optional audio line
src/CouchLink.Core/
  DevOptions.cs              (modify) --test-tone, --audio-loss=
src/CouchLink.Audio/         (new project: NAudio.Wasapi, Concentus)
  CouchLink.Audio.csproj
  OpusAudioEncoder.cs
  OpusAudioDecoder.cs
  LoopbackSource.cs          process loopback / device loopback, reopen
  DefaultDevicePlayer.cs     low-latency output on the default device (+ PcmReader)
src/CouchLink.Video/
  PlayerCore.cs              (modify) optional audio line in the F2 stats
  VideoPlayer.cs             (modify) passes the audio line through
src/CouchLink.App/
  CouchLink.App.csproj       (modify) references CouchLink.Audio
  HostAudio.cs               host: source + encoder + streamer, Describe
  ClientAudioService.cs      client: AudioClient + player or same-PC mute, --audio-loss
  ClientStreams.cs           client: socket + dispatcher + video + audio
  ClientVideoService.cs      (modify) no socket of its own; Receive; audio line
  HostInputService.cs        (modify) starts HostAudio, feeds ClientSeen
  MainWindow.xaml.cs         (modify) uses ClientStreams, shows audio status
tests/CouchLink.Core.Tests/
  AudioTestKit.cs            Packet(), fakes, Until()
  AudioPacketTests.cs, FrameSlicerTests.cs, TestToneSourceTests.cs,
  JitterBufferTests.cs, DriftControlTests.cs, AudioStreamerTests.cs,
  AudioClientTests.cs, StreamDispatcherTests.cs, LocalAddressTests.cs
  OverlayTextTests.cs, DevOptionsTests.cs   (modify)
tests/CouchLink.Audio.Tests/  (new project)
  CouchLink.Audio.Tests.csproj
  OpusTests.cs
tests/CouchLink.Video.Tests/
  PlayerCoreTests.cs         (modify) audio line in stats
CouchLink.slnx               (modify) two new projects
THIRD-PARTY-NOTICES.md, README.md, docs/gate-results.md  (modify)
```

Commands below run from the repo root in Git Bash or PowerShell. `dotnet test` on a single project builds it first.

---

### Task 1: Audio packet, format constants and the wire type

**Files:**
- Modify: `src/CouchLink.Core/Protocol/Wire.cs`
- Create: `src/CouchLink.Core/Protocol/AudioPacket.cs`
- Create: `src/CouchLink.Core/Audio/AudioFormat.cs`
- Test: `tests/CouchLink.Core.Tests/AudioPacketTests.cs`

**Interfaces:**
- Consumes: `Wire.WriteHeader(Span<byte>, byte)`, `Wire.HasHeader(ReadOnlySpan<byte>, byte)` (existing).
- Produces:
  - `Wire.TypeAudio = 6`; `static bool Wire.TryGetType(ReadOnlySpan<byte> source, out byte type)`.
  - `readonly record struct AudioPacket(ushort StreamId, uint Sequence, ReadOnlyMemory<byte> Frame, ReadOnlyMemory<byte> Previous)` with `const int HeaderSize = 14`, `const int MaxFrameBytes = 400`, `int Size`, `byte[] ToArray()` (throws `ArgumentException` for a frame of 0 or >400 bytes or a previous frame >400), `static bool TryParse(byte[] datagram, out AudioPacket packet)` (`Frame`/`Previous` point into `datagram`).
  - `static class AudioFormat`: `SampleRate = 48_000`, `Channels = 2`, `FrameSamples = 240`, `FrameValues = 480`, `BitRate = 128_000`, `FrameDuration` (5 ms).

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/AudioPacketTests.cs`:

```csharp
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class AudioPacketTests
{
    [Fact]
    public void Round_trips_with_the_previous_frame()
    {
        var bytes = new AudioPacket(0xBEEF, 0x01020304, new byte[] { 1, 2, 3 }, new byte[] { 9, 8 }).ToArray();

        Assert.Equal(AudioPacket.HeaderSize + 5, bytes.Length);
        Assert.True(AudioPacket.TryParse(bytes, out var p));
        Assert.Equal((ushort)0xBEEF, p.StreamId);
        Assert.Equal(0x01020304u, p.Sequence);
        Assert.Equal(new byte[] { 1, 2, 3 }, p.Frame.ToArray());
        Assert.Equal(new byte[] { 9, 8 }, p.Previous.ToArray());
    }

    [Fact]
    public void Round_trips_without_a_previous_frame()
    {
        var bytes = new AudioPacket(7, 42, new byte[] { 5 }, ReadOnlyMemory<byte>.Empty).ToArray();

        Assert.True(AudioPacket.TryParse(bytes, out var p));
        Assert.Equal(42u, p.Sequence);
        Assert.Equal(new byte[] { 5 }, p.Frame.ToArray());
        Assert.True(p.Previous.IsEmpty);
    }

    [Fact]
    public void Every_truncation_and_an_extra_byte_are_rejected()
    {
        var bytes = new AudioPacket(7, 42, new byte[] { 1, 2, 3 }, new byte[] { 4 }).ToArray();

        for (int length = 0; length < bytes.Length; length++)
            Assert.False(AudioPacket.TryParse(bytes[..length], out _));
        Assert.False(AudioPacket.TryParse([.. bytes, 0], out _));
    }

    [Fact]
    public void Other_packet_types_are_rejected()
    {
        var bytes = new AudioPacket(7, 42, new byte[] { 1 }, ReadOnlyMemory<byte>.Empty).ToArray();
        bytes[3] = Wire.TypeVideoShard;

        Assert.False(AudioPacket.TryParse(bytes, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(AudioPacket.MaxFrameBytes + 1)]
    public void Frames_must_be_1_to_400_bytes(int length)
    {
        Assert.Throws<ArgumentException>(
            () => new AudioPacket(1, 1, new byte[length], ReadOnlyMemory<byte>.Empty).ToArray());
    }

    [Fact]
    public void Random_bytes_with_an_audio_header_never_throw()
    {
        var random = new Random(6);
        for (int i = 0; i < 10_000; i++)
        {
            var bytes = new byte[random.Next(0, 64)];
            random.NextBytes(bytes);
            if (bytes.Length >= 4)
                Wire.WriteHeader(bytes, Wire.TypeAudio);
            AudioPacket.TryParse(bytes, out _); // returns true or false; never throws
        }
    }

    [Fact]
    public void Wire_reads_the_type_of_any_couchlink_datagram()
    {
        var bytes = new byte[8];
        Wire.WriteHeader(bytes, Wire.TypeAudio);

        Assert.True(Wire.TryGetType(bytes, out var type));
        Assert.Equal(Wire.TypeAudio, type);
        Assert.False(Wire.TryGetType(bytes.AsSpan(0, 3), out _));
        Assert.False(Wire.TryGetType(new byte[8], out _)); // no "CL" magic
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~AudioPacketTests"`
Expected: build fails with `error CS0246: The type or namespace name 'AudioPacket' could not be found`.

- [ ] **Step 3: Add the wire type and `TryGetType`**

In `src/CouchLink.Core/Protocol/Wire.cs`, after `public const byte TypeTimingReply = 5;` add:

```csharp
    public const byte TypeAudio = 6;
```

and after `HasHeader` add:

```csharp
    /// <summary>The packet type of a CouchLink datagram, or false if it has no valid header.</summary>
    public static bool TryGetType(ReadOnlySpan<byte> source, out byte type)
    {
        type = 0;
        if (source.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(source) != Magic || source[2] != Version)
            return false;
        type = source[3];
        return true;
    }
```

- [ ] **Step 4: Write `AudioFormat`**

`src/CouchLink.Core/Audio/AudioFormat.cs`:

```csharp
namespace CouchLink.Core.Audio;

/// <summary>The one audio format CouchLink streams: Opus, 48 kHz stereo, 5 ms frames, 128 kbps.</summary>
public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int Channels = 2;

    /// <summary>Samples per channel in one 5 ms frame.</summary>
    public const int FrameSamples = 240;

    /// <summary>16-bit values in one frame, interleaved left, right, left, ...</summary>
    public const int FrameValues = FrameSamples * Channels;

    public const int BitRate = 128_000;

    public static readonly TimeSpan FrameDuration = TimeSpan.FromMilliseconds(5);
}
```

- [ ] **Step 5: Write `AudioPacket`**

`src/CouchLink.Core/Protocol/AudioPacket.cs`:

```csharp
using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>
/// Host -> client video port: one 5 ms Opus frame, plus a copy of the frame before it so a client
/// can rebuild a single lost packet exactly. After the 4-byte header: stream ID (u16), sequence
/// (u32), frame length (u16), frame, previous length (u16, 0 = none), previous frame.
/// <see cref="Frame"/> and <see cref="Previous"/> from <see cref="TryParse"/> point into the
/// datagram; they are not copies.
/// </summary>
public readonly record struct AudioPacket(
    ushort StreamId, uint Sequence, ReadOnlyMemory<byte> Frame, ReadOnlyMemory<byte> Previous)
{
    public const int HeaderSize = 14; // header 4, stream 2, sequence 4, two lengths 2 + 2
    public const int MaxFrameBytes = 400;

    public int Size => HeaderSize + Frame.Length + Previous.Length;

    public byte[] ToArray()
    {
        if (Frame.Length is 0 or > MaxFrameBytes)
            throw new ArgumentException($"A frame must be 1-{MaxFrameBytes} bytes.");
        if (Previous.Length > MaxFrameBytes)
            throw new ArgumentException($"The previous frame must be at most {MaxFrameBytes} bytes.");

        var packet = new byte[Size];
        var span = packet.AsSpan();
        Wire.WriteHeader(span, Wire.TypeAudio);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], StreamId);
        BinaryPrimitives.WriteUInt32LittleEndian(span[6..], Sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)Frame.Length);
        Frame.Span.CopyTo(span[12..]);
        int at = 12 + Frame.Length;
        BinaryPrimitives.WriteUInt16LittleEndian(span[at..], (ushort)Previous.Length);
        Previous.Span.CopyTo(span[(at + 2)..]);
        return packet;
    }

    public static bool TryParse(byte[] datagram, out AudioPacket packet)
    {
        packet = default;
        var span = datagram.AsSpan();
        if (span.Length < HeaderSize || !Wire.HasHeader(span, Wire.TypeAudio))
            return false;
        int frameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[10..]);
        if (frameLength is 0 or > MaxFrameBytes || span.Length < HeaderSize + frameLength)
            return false;
        int at = 12 + frameLength;
        int previousLength = BinaryPrimitives.ReadUInt16LittleEndian(span[at..]);
        if (previousLength > MaxFrameBytes || span.Length != HeaderSize + frameLength + previousLength)
            return false;

        packet = new AudioPacket(
            BinaryPrimitives.ReadUInt16LittleEndian(span[4..]),
            BinaryPrimitives.ReadUInt32LittleEndian(span[6..]),
            datagram.AsMemory(12, frameLength),
            datagram.AsMemory(at + 2, previousLength));
        return true;
    }
}
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~AudioPacketTests"`
Expected: PASS, 8 tests.

- [ ] **Step 7: Commit**

```bash
git add src/CouchLink.Core/Protocol src/CouchLink.Core/Audio/AudioFormat.cs tests/CouchLink.Core.Tests/AudioPacketTests.cs
git commit -m "feat(core): audio packet (wire type 6) and audio format constants"
```

---

### Task 2: Audio source interface, frame slicer and test tone

**Files:**
- Create: `src/CouchLink.Core/Audio/IAudioSource.cs`
- Create: `src/CouchLink.Core/Audio/FrameSlicer.cs`
- Create: `src/CouchLink.Core/Audio/TestToneSource.cs`
- Test: `tests/CouchLink.Core.Tests/FrameSlicerTests.cs`, `tests/CouchLink.Core.Tests/TestToneSourceTests.cs`

**Interfaces:**
- Consumes: `AudioFormat` (Task 1).
- Produces:
  - `interface IAudioSource : IDisposable { string Description { get; } bool TryRead(Span<short> frame, TimeSpan timeout, out bool discontinuity); }`. `frame` holds `AudioFormat.FrameValues` values.
  - `sealed class FrameSlicer(Action<short[], bool> onFrame)` with `void Write(ReadOnlySpan<byte> pcm, bool discontinuity = false, bool silent = false)`. `onFrame(frame, discontinuity)` gets a new 480-value array each time.
  - `sealed class TestToneSource : IAudioSource` with `const int Frequency = 440`, `const short Amplitude = 8000`, `static TimeSpan BeepLength` (100 ms), `static TimeSpan Period` (1 s), `static void Fill(Span<short> frame, long index)`, `Description == "test tone"`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/FrameSlicerTests.cs`:

```csharp
using System.Runtime.InteropServices;
using CouchLink.Core.Audio;

namespace CouchLink.Core.Tests;

public class FrameSlicerTests
{
    /// <summary>Little-endian bytes of the 16-bit values first, first + 1, ...</summary>
    private static byte[] Bytes(int first, int count) =>
        MemoryMarshal.AsBytes(Enumerable.Range(first, count).Select(v => (short)v).ToArray().AsSpan()).ToArray();

    [Fact]
    public void Uneven_buffers_become_exact_frames_with_nothing_lost_or_repeated()
    {
        var frames = new List<short[]>();
        var slicer = new FrameSlicer((f, _) => frames.Add(f));

        int next = 0;
        foreach (int values in new[] { 960, 2, 1000, 3, 480, 1395 }) // 3840 values = 8 frames
        {
            slicer.Write(Bytes(next, values));
            next += values;
        }

        Assert.Equal(8, frames.Count);
        Assert.All(frames, f => Assert.Equal(AudioFormat.FrameValues, f.Length));
        Assert.Equal(Enumerable.Range(0, 3840).Select(v => (short)v), frames.SelectMany(f => f));
    }

    [Fact]
    public void A_discontinuity_drops_the_partial_frame_and_marks_the_next()
    {
        var frames = new List<(short[] Frame, bool Discontinuity)>();
        var slicer = new FrameSlicer((f, d) => frames.Add((f, d)));

        slicer.Write(Bytes(0, 960));                       // two frames; the first follows nothing
        slicer.Write(Bytes(5000, 100));                    // part of a frame...
        slicer.Write(Bytes(0, 480), discontinuity: true);  // ...thrown away here
        slicer.Write(Bytes(0, 480));

        Assert.Equal(new[] { true, false, true, false }, frames.Select(f => f.Discontinuity));
        Assert.Equal(Enumerable.Range(0, 480).Select(v => (short)v), frames[2].Frame);
    }

    [Fact]
    public void A_silent_buffer_becomes_zeros()
    {
        var frames = new List<short[]>();
        var slicer = new FrameSlicer((f, _) => frames.Add(f));

        slicer.Write(Bytes(1, 480), silent: true);

        Assert.All(Assert.Single(frames), v => Assert.Equal(0, v));
    }
}
```

`tests/CouchLink.Core.Tests/TestToneSourceTests.cs`:

```csharp
using System.Diagnostics;
using CouchLink.Core.Audio;

namespace CouchLink.Core.Tests;

public class TestToneSourceTests
{
    [Fact]
    public void Beeps_for_100_ms_then_is_silent_until_the_next_second()
    {
        var frame = new short[AudioFormat.FrameValues];

        TestToneSource.Fill(frame, 0);
        Assert.InRange(frame.Max(), (short)7000, TestToneSource.Amplitude);
        TestToneSource.Fill(frame, 19);  // 95-100 ms: still beeping
        Assert.NotEqual(0, frame.Max());
        TestToneSource.Fill(frame, 20);  // 100 ms: quiet
        Assert.All(frame, v => Assert.Equal(0, v));
        TestToneSource.Fill(frame, 200); // 1 s: the next beep
        Assert.NotEqual(0, frame.Max());
    }

    [Fact]
    public void Both_channels_carry_the_same_tone()
    {
        var frame = new short[AudioFormat.FrameValues];
        TestToneSource.Fill(frame, 3);

        for (int i = 0; i < AudioFormat.FrameSamples; i++)
            Assert.Equal(frame[2 * i], frame[2 * i + 1]);
    }

    [Fact]
    public void Frames_come_in_real_time_and_only_the_first_is_a_discontinuity()
    {
        using var tone = new TestToneSource();
        var frame = new short[AudioFormat.FrameValues];
        var clock = Stopwatch.StartNew();

        Assert.True(tone.TryRead(frame, TimeSpan.FromSeconds(1), out bool first));
        Assert.True(first);
        for (int i = 1; i < 40; i++)
        {
            Assert.True(tone.TryRead(frame, TimeSpan.FromSeconds(1), out bool later));
            Assert.False(later);
        }

        Assert.InRange(clock.ElapsedMilliseconds, 180, 400); // frame 39 is due at 195 ms
        Assert.Equal("test tone", tone.Description);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~FrameSlicerTests|FullyQualifiedName~TestToneSourceTests"`
Expected: build fails, `FrameSlicer` and `TestToneSource` not found.

- [ ] **Step 3: Write `IAudioSource`**

`src/CouchLink.Core/Audio/IAudioSource.cs`:

```csharp
namespace CouchLink.Core.Audio;

/// <summary>Where the host's 5 ms frames of 16-bit stereo PCM come from.</summary>
public interface IAudioSource : IDisposable
{
    /// <summary>For the status text: "process loopback", "device loopback" or "test tone".</summary>
    string Description { get; }

    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the next frame and copies it into
    /// <paramref name="frame"/> (<see cref="AudioFormat.FrameValues"/> interleaved values).
    /// <paramref name="discontinuity"/> is true when audio before this frame is missing (capture
    /// started, restarted, or lost data), so the frame does not follow the previous one.
    /// </summary>
    bool TryRead(Span<short> frame, TimeSpan timeout, out bool discontinuity);
}
```

- [ ] **Step 4: Write `FrameSlicer`**

`src/CouchLink.Core/Audio/FrameSlicer.cs`:

```csharp
using System.Runtime.InteropServices;

namespace CouchLink.Core.Audio;

/// <summary>
/// Cuts captured 16-bit stereo PCM, which arrives in buffers of any size, into exact 5 ms frames
/// and hands each to <c>onFrame</c> as a new array. A discontinuity throws away the partial frame
/// and marks the next whole frame, and so is the very first frame (nothing came before it).
/// Not thread-safe.
/// </summary>
public sealed class FrameSlicer(Action<short[], bool> onFrame)
{
    private readonly short[] _partial = new short[AudioFormat.FrameValues];
    private int _filled;
    private bool _discontinuity = true;

    /// <summary>Adds captured bytes. <paramref name="silent"/>: treat the buffer as silence (WASAPI's silent flag).</summary>
    public void Write(ReadOnlySpan<byte> pcm, bool discontinuity = false, bool silent = false)
    {
        if (discontinuity)
        {
            _filled = 0;
            _discontinuity = true;
        }

        var source = MemoryMarshal.Cast<byte, short>(pcm);
        int done = 0;
        while (done < source.Length)
        {
            int take = Math.Min(source.Length - done, _partial.Length - _filled);
            var target = _partial.AsSpan(_filled, take);
            if (silent)
                target.Clear();
            else
                source.Slice(done, take).CopyTo(target);
            _filled += take;
            done += take;

            if (_filled == _partial.Length)
            {
                onFrame(_partial.ToArray(), _discontinuity);
                _filled = 0;
                _discontinuity = false;
            }
        }
    }
}
```

- [ ] **Step 5: Write `TestToneSource`**

`src/CouchLink.Core/Audio/TestToneSource.cs`:

```csharp
using System.Diagnostics;

namespace CouchLink.Core.Audio;

/// <summary>
/// --test-tone: a 440 Hz beep, <see cref="BeepLength"/> on in every <see cref="Period"/>, produced
/// in real time, so gaps and clicks are easy to hear without a game.
/// </summary>
public sealed class TestToneSource : IAudioSource
{
    public const int Frequency = 440;
    public const short Amplitude = 8000;
    public static readonly TimeSpan BeepLength = TimeSpan.FromMilliseconds(100);
    public static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

    private static readonly long BeepSamples = (long)(BeepLength.TotalSeconds * AudioFormat.SampleRate);
    private static readonly long PeriodSamples = (long)(Period.TotalSeconds * AudioFormat.SampleRate);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _frames;

    public string Description => "test tone";

    public bool TryRead(Span<short> frame, TimeSpan timeout, out bool discontinuity)
    {
        discontinuity = false;
        var wait = AudioFormat.FrameDuration * _frames - _clock.Elapsed;
        if (wait > timeout)
        {
            Thread.Sleep(timeout);
            return false;
        }
        if (wait > TimeSpan.Zero)
            Thread.Sleep(wait);

        discontinuity = _frames == 0;
        Fill(frame, _frames++);
        return true;
    }

    /// <summary>Writes frame number <paramref name="index"/> of the tone, the same on both channels.</summary>
    public static void Fill(Span<short> frame, long index)
    {
        for (int i = 0; i < AudioFormat.FrameSamples; i++)
        {
            long n = index * AudioFormat.FrameSamples + i;
            short value = n % PeriodSamples < BeepSamples
                ? (short)(Amplitude * Math.Sin(2 * Math.PI * Frequency * n / AudioFormat.SampleRate))
                : (short)0;
            frame[2 * i] = value;
            frame[2 * i + 1] = value;
        }
    }

    public void Dispose()
    {
    }
}
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~FrameSlicerTests|FullyQualifiedName~TestToneSourceTests"`
Expected: PASS, 6 tests.

- [ ] **Step 7: Commit**

```bash
git add src/CouchLink.Core/Audio tests/CouchLink.Core.Tests/FrameSlicerTests.cs tests/CouchLink.Core.Tests/TestToneSourceTests.cs
git commit -m "feat(core): audio source interface, frame slicer and test tone"
```

---

### Task 3: Jitter buffer

**Files:**
- Create: `src/CouchLink.Core/Audio/JitterBuffer.cs`
- Create: `tests/CouchLink.Core.Tests/AudioTestKit.cs`
- Test: `tests/CouchLink.Core.Tests/JitterBufferTests.cs`

**Interfaces:**
- Consumes: `AudioPacket` (Task 1).
- Produces:
  - `enum PlayoutKind { Frame, Repaired, Conceal, Silence }`
  - `readonly record struct Playout(PlayoutKind Kind, ReadOnlyMemory<byte> Data = default)`
  - `readonly record struct JitterStats(long Received, long Repaired, long Concealed, long Late, long Discarded, long Underruns)`
  - `sealed class JitterBuffer` with `const int PrimeFrames = 3`, `MaxConcealFrames = 4`, `MaxDepthFrames = 12`; `bool Playing`; `int Depth`; `JitterStats Stats`; `bool Add(AudioPacket packet)` (true when the packet starts a new stream); `Playout Next()`.
  - Test kit: `AudioTestKit.Packet(uint sequence, bool withPrevious = true, ushort stream = 1)` (frame = the sequence's low byte; previous = the low byte of sequence - 1) and `AudioTestKit.Until(Func<bool>)`.

- [ ] **Step 1: Write the test kit**

`tests/CouchLink.Core.Tests/AudioTestKit.cs`:

```csharp
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

internal static class AudioTestKit
{
    /// <summary>
    /// A packet whose Opus "frame" is one byte, the sequence's low byte, so tests can tell frames
    /// apart. With <paramref name="withPrevious"/> it carries the previous frame the same way.
    /// </summary>
    public static AudioPacket Packet(uint sequence, bool withPrevious = true, ushort stream = 1)
    {
        ReadOnlyMemory<byte> previous = withPrevious ? new[] { (byte)(sequence - 1) } : ReadOnlyMemory<byte>.Empty;
        return new AudioPacket(stream, sequence, new[] { (byte)sequence }, previous);
    }

    public static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/CouchLink.Core.Tests/JitterBufferTests.cs`:

```csharp
using CouchLink.Core.Audio;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class JitterBufferTests
{
    /// <summary>Plays <paramref name="count"/> frames: "12" a frame, "r12" a repaired copy, "c" concealed, "-" silence.</summary>
    private static string Play(JitterBuffer buffer, int count) =>
        string.Join(" ", Enumerable.Range(0, count).Select(_ => buffer.Next() switch
        {
            { Kind: PlayoutKind.Frame } p => p.Data.Span[0].ToString(),
            { Kind: PlayoutKind.Repaired } p => $"r{p.Data.Span[0]}",
            { Kind: PlayoutKind.Conceal } => "c",
            _ => "-",
        }));

    /// <summary>A buffer that has primed on 10, 11, 12 and plays 10 next.</summary>
    private static JitterBuffer Started()
    {
        var buffer = new JitterBuffer();
        buffer.Add(Packet(10, withPrevious: false));
        buffer.Add(Packet(11));
        buffer.Add(Packet(12));
        return buffer;
    }

    [Fact]
    public void Waits_for_three_frames_before_playing()
    {
        var buffer = new JitterBuffer();
        buffer.Add(Packet(10, withPrevious: false));
        buffer.Add(Packet(11));
        Assert.Equal("- -", Play(buffer, 2));
        Assert.False(buffer.Playing);

        buffer.Add(Packet(12));

        Assert.True(buffer.Playing);
        Assert.Equal("10 11 12 c", Play(buffer, 4));
    }

    [Fact]
    public void Plays_in_sequence_order_whatever_the_arrival_order()
    {
        var buffer = new JitterBuffer();
        buffer.Add(Packet(12));
        buffer.Add(Packet(10, withPrevious: false));
        buffer.Add(Packet(11));

        Assert.Equal("10 11 12", Play(buffer, 3));
    }

    [Fact]
    public void One_lost_packet_is_rebuilt_from_the_copy_in_the_next()
    {
        var buffer = Started();
        buffer.Add(Packet(14)); // 13 was lost; 14 carries it

        Assert.Equal("10 11 12 r13 14", Play(buffer, 5));
        Assert.Equal(1, buffer.Stats.Repaired);
    }

    [Fact]
    public void A_frame_lost_with_its_copy_is_concealed()
    {
        var buffer = Started();
        buffer.Add(Packet(15)); // 13 and 14 lost; 15 carries 14 only

        Assert.Equal("10 11 12 c r14 15", Play(buffer, 6));
        Assert.Equal(1, buffer.Stats.Concealed);
    }

    [Fact]
    public void After_four_concealed_frames_it_plays_silence_and_primes_again()
    {
        var buffer = Started();
        Assert.Equal("10 11 12", Play(buffer, 3));

        Assert.Equal("c c c c -", Play(buffer, 5));
        Assert.False(buffer.Playing);
        Assert.Equal(1, buffer.Stats.Underruns);

        buffer.Add(Packet(20, withPrevious: false));
        buffer.Add(Packet(21));
        Assert.Equal("-", Play(buffer, 1));
        buffer.Add(Packet(22));
        Assert.Equal("20 21 22", Play(buffer, 3));
    }

    [Fact]
    public void Late_packets_are_dropped_and_counted()
    {
        var buffer = Started();
        Assert.Equal("10 11 12 c", Play(buffer, 4)); // 13 concealed

        buffer.Add(Packet(13)); // too late: concealed already
        buffer.Add(Packet(11)); // too late: played already

        Assert.Equal(2, buffer.Stats.Late);
        Assert.Equal("c", Play(buffer, 1)); // 14 is still missing; 13 was not queued
    }

    [Fact]
    public void A_late_burst_after_a_hiccup_resumes_at_once()
    {
        var buffer = Started();
        Assert.Equal("10 11 12 c c", Play(buffer, 5)); // a hiccup: 13 and 14 concealed

        foreach (uint s in new uint[] { 13, 14, 15, 16, 17 })
            buffer.Add(Packet(s)); // the delayed burst

        Assert.True(buffer.Playing);
        Assert.Equal("15 16 17", Play(buffer, 3));
        Assert.Equal(2, buffer.Stats.Late);
    }

    [Fact]
    public void A_new_stream_id_starts_over()
    {
        var buffer = Started();
        Assert.Equal("10", Play(buffer, 1));

        Assert.True(buffer.Add(Packet(100, withPrevious: false, stream: 2)));
        Assert.False(buffer.Playing);
        Assert.Equal("-", Play(buffer, 1));
        Assert.False(buffer.Add(Packet(101, stream: 2)));
        buffer.Add(Packet(102, stream: 2));

        Assert.Equal("100 101 102", Play(buffer, 3));
    }

    [Fact]
    public void More_than_60_ms_queued_is_cut_to_15_ms()
    {
        var buffer = Started();
        Assert.Equal("10", Play(buffer, 1)); // 11 is next

        for (uint s = 13; s <= 23; s++)
            buffer.Add(Packet(s)); // at 23, 13 frames would be queued

        Assert.Equal(3, buffer.Depth);
        Assert.Equal(10, buffer.Stats.Discarded); // 11 to 20
        Assert.Equal("21 22 23", Play(buffer, 3));
    }

    [Fact]
    public void Depth_counts_from_the_next_frame_to_the_newest()
    {
        var buffer = Started();
        Assert.Equal(3, buffer.Depth);

        Play(buffer, 1);
        Assert.Equal(2, buffer.Depth);

        buffer.Add(Packet(14));
        Assert.Equal(4, buffer.Depth); // 11, 12, 13 (from 14's copy), 14
    }

    [Fact]
    public void Sequence_numbers_wrap_around()
    {
        var buffer = new JitterBuffer();
        buffer.Add(Packet(uint.MaxValue - 1, withPrevious: false));
        buffer.Add(Packet(uint.MaxValue));
        buffer.Add(Packet(0));

        Assert.Equal("254 255 0", Play(buffer, 3));
    }

    [Fact]
    public void A_duplicate_packet_plays_once()
    {
        var buffer = Started();
        buffer.Add(Packet(11));

        Assert.Equal("10 11 12 c", Play(buffer, 4));
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~JitterBufferTests"`
Expected: build fails, `JitterBuffer` not found.

- [ ] **Step 4: Write `JitterBuffer`**

`src/CouchLink.Core/Audio/JitterBuffer.cs`:

```csharp
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Audio;

public enum PlayoutKind { Frame, Repaired, Conceal, Silence }

/// <summary>What to play for the next 5 ms. <see cref="Data"/> is the Opus frame for Frame and Repaired.</summary>
public readonly record struct Playout(PlayoutKind Kind, ReadOnlyMemory<byte> Data = default);

public readonly record struct JitterStats(
    long Received, long Repaired, long Concealed, long Late, long Discarded, long Underruns);

/// <summary>
/// Client side: orders audio frames by sequence and decides what plays every 5 ms. Playback starts
/// once <see cref="PrimeFrames"/> frames are queued. A missing frame is replaced by the copy that
/// the next packet carried (Repaired) when that arrived, else concealed, for up to
/// <see cref="MaxConcealFrames"/> in a row; after that it plays silence and primes again. A packet
/// for a frame already played or concealed is late and dropped. More than
/// <see cref="MaxDepthFrames"/> queued (the client stalled) drops the oldest down to
/// <see cref="PrimeFrames"/>. A new stream ID starts over. Not thread-safe.
/// </summary>
public sealed class JitterBuffer
{
    public const int PrimeFrames = 3;      // 15 ms
    public const int MaxConcealFrames = 4; // 20 ms
    public const int MaxDepthFrames = 12;  // 60 ms

    private readonly Dictionary<uint, (ReadOnlyMemory<byte> Data, bool Repaired)> _frames = [];
    private ushort? _streamId;
    private bool _started; // _next means something: earlier frames are late
    private uint _next;
    private uint _newest;
    private int _concealRun;
    private long _received, _repaired, _concealed, _late, _discarded, _underruns;

    public bool Playing { get; private set; }

    /// <summary>Frames from the next one to play to the newest received (0 while not playing).</summary>
    public int Depth => Playing && !Before(_newest, _next) ? (int)(_newest - _next + 1) : 0;

    public JitterStats Stats => new(_received, _repaired, _concealed, _late, _discarded, _underruns);

    /// <summary>Adds a received packet. Returns true if it starts a new stream (reset the decoder).</summary>
    public bool Add(AudioPacket packet)
    {
        bool newStream = _streamId != packet.StreamId;
        if (newStream)
            StartOver(packet.StreamId);
        _received++;

        if (IsLate(packet.Sequence))
        {
            _late++;
            return newStream;
        }
        Store(packet.Sequence, packet.Frame, repaired: false);

        uint previous = packet.Sequence - 1;
        if (!packet.Previous.IsEmpty && !IsLate(previous) && !_frames.ContainsKey(previous))
            Store(previous, packet.Previous, repaired: true);

        TryStart();
        TrimIfTooDeep();
        return newStream;
    }

    public Playout Next()
    {
        TryStart();
        if (!Playing)
            return new Playout(PlayoutKind.Silence);

        if (_frames.Remove(_next, out var frame))
        {
            _next++;
            _concealRun = 0;
            if (frame.Repaired)
                _repaired++;
            return new Playout(frame.Repaired ? PlayoutKind.Repaired : PlayoutKind.Frame, frame.Data);
        }
        if (_concealRun < MaxConcealFrames)
        {
            _next++;
            _concealRun++;
            _concealed++;
            return new Playout(PlayoutKind.Conceal);
        }

        Playing = false;
        _underruns++;
        return new Playout(PlayoutKind.Silence);
    }

    private bool IsLate(uint sequence) => _started && Before(sequence, _next);

    private void Store(uint sequence, ReadOnlyMemory<byte> data, bool repaired)
    {
        if (_frames.Count == 0 || Before(_newest, sequence))
            _newest = sequence;
        _frames[sequence] = (data, repaired);
    }

    private void TryStart()
    {
        if (Playing || _frames.Count == 0)
            return;
        uint oldest = _frames.Keys.Aggregate((a, b) => Before(a, b) ? a : b);
        if ((int)(_newest - oldest + 1) < PrimeFrames)
            return;

        _next = oldest;
        _started = true;
        _concealRun = 0;
        Playing = true;
    }

    private void TrimIfTooDeep()
    {
        if (Depth <= MaxDepthFrames)
            return;
        uint keepFrom = _newest - PrimeFrames + 1;
        foreach (var sequence in _frames.Keys.Where(s => Before(s, keepFrom)).ToList())
            _frames.Remove(sequence);
        _discarded += (int)(keepFrom - _next);
        _next = keepFrom;
    }

    private void StartOver(ushort streamId)
    {
        _streamId = streamId;
        _frames.Clear();
        _started = false;
        _concealRun = 0;
        Playing = false;
    }

    /// <summary>Sequence order that survives the 32-bit wrap.</summary>
    private static bool Before(uint a, uint b) => (int)(a - b) < 0;
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~JitterBufferTests"`
Expected: PASS, 12 tests.

- [ ] **Step 6: Commit**

```bash
git add src/CouchLink.Core/Audio/JitterBuffer.cs tests/CouchLink.Core.Tests/AudioTestKit.cs tests/CouchLink.Core.Tests/JitterBufferTests.cs
git commit -m "feat(core): audio jitter buffer with redundancy, concealment and priming"
```

---

### Task 4: Drift control

**Files:**
- Create: `src/CouchLink.Core/Audio/DriftControl.cs`
- Test: `tests/CouchLink.Core.Tests/DriftControlTests.cs`

**Interfaces:**
- Produces: `sealed class DriftControl` with `const int WindowFrames = 200`, `const double DeadBandFrames = 1`, `double? Baseline`, `long Corrections`, `int Next(int depthFrames)` (returns -1 drop a sample, +1 repeat one, 0 neither), `void Reset()`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/DriftControlTests.cs`:

```csharp
using CouchLink.Core.Audio;

namespace CouchLink.Core.Tests;

public class DriftControlTests
{
    /// <summary>Feeds one second (or <paramref name="frames"/>) of the same depth; returns the last answer.</summary>
    private static int Feed(DriftControl drift, int depth, int frames = DriftControl.WindowFrames)
    {
        int last = 0;
        for (int i = 0; i < frames; i++)
            last = drift.Next(depth);
        return last;
    }

    [Fact]
    public void The_first_second_sets_the_baseline_and_small_changes_do_nothing()
    {
        var drift = new DriftControl();

        Assert.Equal(0, Feed(drift, 3));
        Assert.Equal(3, drift.Baseline);
        Assert.Equal(0, Feed(drift, 4)); // 5 ms above: inside the dead band
        Assert.Equal(0, Feed(drift, 2));
        Assert.Equal(0, drift.Corrections);
    }

    [Fact]
    public void Too_full_drops_a_sample_per_frame_until_back_at_the_baseline()
    {
        var drift = new DriftControl();
        Feed(drift, 3);

        Assert.Equal(-1, Feed(drift, 5)); // 10 ms above: dropping from the end of this second
        Assert.Equal(-1, Feed(drift, 4)); // still above the baseline
        Assert.Equal(0, Feed(drift, 3));  // back at it

        Assert.Equal(1 + 200 + 199, drift.Corrections);
    }

    [Fact]
    public void Too_empty_repeats_a_sample_per_frame_until_back_at_the_baseline()
    {
        var drift = new DriftControl();
        Feed(drift, 3);

        Assert.Equal(1, Feed(drift, 1));
        Assert.Equal(0, Feed(drift, 3));
    }

    [Fact]
    public void Reset_learns_the_baseline_again()
    {
        var drift = new DriftControl();
        Feed(drift, 3);
        Assert.Equal(-1, Feed(drift, 6));

        drift.Reset();

        Assert.Null(drift.Baseline);
        Assert.Equal(0, Feed(drift, 6));
        Assert.Equal(6, drift.Baseline);
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~DriftControlTests"`
Expected: build fails, `DriftControl` not found.

- [ ] **Step 3: Write `DriftControl`**

`src/CouchLink.Core/Audio/DriftControl.cs`:

```csharp
namespace CouchLink.Core.Audio;

/// <summary>
/// Client side: holds the jitter buffer at its starting depth while the host's and the client's
/// sound cards run at slightly different rates. It is given the depth before each played frame.
/// The average over the first <see cref="WindowFrames"/> (1 s) is the baseline; it already
/// includes how the device pulls audio. When a later second averages more than
/// <see cref="DeadBandFrames"/> above it, one sample per frame is dropped until a second averages
/// at or below it; below it, one is repeated. That is a 0.4% speed change, too small to hear, and
/// moves the depth by one frame in about 1.2 s. Not thread-safe.
/// </summary>
public sealed class DriftControl
{
    public const int WindowFrames = 200;
    public const double DeadBandFrames = 1;

    private long _sum;
    private int _count;
    private int _correction; // -1 drop, +1 repeat, 0 none

    public double? Baseline { get; private set; }

    public long Corrections { get; private set; }

    /// <summary>Returns -1 to drop one sample from the next frame, +1 to repeat one, 0 to leave it.</summary>
    public int Next(int depthFrames)
    {
        _sum += depthFrames;
        if (++_count == WindowFrames)
        {
            double average = (double)_sum / WindowFrames;
            _sum = 0;
            _count = 0;
            if (Baseline is not { } baseline)
                Baseline = average;
            else if (average > baseline + DeadBandFrames)
                _correction = -1;
            else if (average < baseline - DeadBandFrames)
                _correction = +1;
            else if ((_correction == -1 && average <= baseline) || (_correction == +1 && average >= baseline))
                _correction = 0;
        }
        if (_correction != 0)
            Corrections++;
        return _correction;
    }

    /// <summary>Playback started again (after silence, or a new stream): learn a new baseline.</summary>
    public void Reset()
    {
        Baseline = null;
        _sum = 0;
        _count = 0;
        _correction = 0;
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~DriftControlTests"`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Audio/DriftControl.cs tests/CouchLink.Core.Tests/DriftControlTests.cs
git commit -m "feat(core): audio drift control"
```

---

### Task 5: Host audio streamer

**Files:**
- Create: `src/CouchLink.Core/Audio/AudioStreamer.cs`
- Modify: `tests/CouchLink.Core.Tests/AudioTestKit.cs` (add fakes)
- Test: `tests/CouchLink.Core.Tests/AudioStreamerTests.cs`

**Interfaces:**
- Consumes: `IAudioSource`, `AudioFormat` (Tasks 1-2), `AudioPacket` (Task 1), existing `IVideoPacketSender` (`void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets)`) and `StreamTargets(int port)` (`bool Seen(byte slot, IPAddress address, TimeSpan now)`, `IReadOnlyList<IPEndPoint> Current(TimeSpan now)`).
- Produces:
  - `interface IAudioEncoder : IDisposable { int Encode(ReadOnlySpan<short> pcm, Span<byte> output); }`
  - `readonly record struct AudioSendStats(long FramesCaptured, long PacketsSent, long BytesSent, int Clients)`
  - `sealed class AudioStreamer : IDisposable` with `AudioStreamer(IAudioSource source, IAudioEncoder encoder, IVideoPacketSender sender, int port, TimeProvider time, Action<Exception>? onError = null)`, `const uint SequenceJump = 16`, `ushort StreamId`, `string SourceDescription`, `AudioSendStats Stats`, `void ClientSeen(byte slot, IPAddress address)`. Takes ownership of source, encoder and sender.

- [ ] **Step 1: Add the fakes to the test kit**

Append to `tests/CouchLink.Core.Tests/AudioTestKit.cs` (add `using System.Collections.Concurrent;`, `using System.Net;`, `using CouchLink.Core.Audio;`, `using CouchLink.Core.Net;` at the top):

```csharp
/// <summary>A source the test feeds: each frame is filled with one value.</summary>
internal sealed class QueueSource : IAudioSource
{
    private readonly BlockingCollection<(short Value, bool Discontinuity)> _frames = new();
    private int _taken;

    public string Description => "queue";

    public int Taken => Volatile.Read(ref _taken);

    public void Add(short value, bool discontinuity = false) => _frames.Add((value, discontinuity));

    public bool TryRead(Span<short> frame, TimeSpan timeout, out bool discontinuity)
    {
        discontinuity = false;
        if (!_frames.TryTake(out var item, timeout))
            return false;
        frame.Fill(item.Value);
        discontinuity = item.Discontinuity;
        Interlocked.Increment(ref _taken);
        return true;
    }

    public void Dispose() => _frames.Dispose();
}

/// <summary>"Encodes" a frame as two bytes: its first sample's low byte, then 0xEE. Throws on a frame of 99s.</summary>
internal sealed class TinyEncoder : IAudioEncoder
{
    public int Encode(ReadOnlySpan<short> pcm, Span<byte> output)
    {
        if (pcm[0] == 99)
            throw new InvalidOperationException("encoder broke");
        output[0] = (byte)pcm[0];
        output[1] = 0xEE;
        return 2;
    }

    public void Dispose() { }
}

internal sealed class AudioRecordingSender : IVideoPacketSender
{
    private readonly List<(byte[] Packet, IPEndPoint Target)> _sent = [];

    public int Count { get { lock (_sent) return _sent.Count; } }

    public List<(AudioPacket Packet, IPEndPoint Target)> Parsed()
    {
        lock (_sent)
            return _sent.Select(s =>
            {
                Assert.True(AudioPacket.TryParse(s.Packet, out var p));
                return (p, s.Target);
            }).ToList();
    }

    public void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets)
    {
        lock (_sent)
            foreach (var p in packets)
                foreach (var t in targets)
                    _sent.Add((p, t));
    }

    public void Dispose() { }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/CouchLink.Core.Tests/AudioStreamerTests.cs`:

```csharp
using System.Net;
using CouchLink.Core.Audio;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class AudioStreamerTests
{
    private static readonly IPAddress A = IPAddress.Parse("10.0.0.2");
    private static readonly IPAddress B = IPAddress.Parse("10.0.0.3");

    private readonly QueueSource _source = new();
    private readonly AudioRecordingSender _sender = new();

    private AudioStreamer Streamer(Action<Exception>? onError = null) =>
        new(_source, new TinyEncoder(), _sender, 47802, TimeProvider.System, onError);

    [Fact]
    public async Task Each_frame_goes_to_every_client_with_the_previous_frame_attached()
    {
        using var streamer = Streamer();
        streamer.ClientSeen(2, A);
        streamer.ClientSeen(3, B);
        _source.Add(1);
        _source.Add(2);
        _source.Add(3);

        await Until(() => _sender.Count == 6);

        var sent = _sender.Parsed();
        Assert.Equal(
            new[] { new IPEndPoint(A, 47802), new IPEndPoint(B, 47802) }.OrderBy(e => e.ToString()),
            sent.Select(s => s.Target).Distinct().OrderBy(e => e.ToString()));
        var toA = sent.Where(s => s.Target.Address.Equals(A)).Select(s => s.Packet).ToList();
        Assert.Equal(new byte[] { 1, 0xEE }, toA[0].Frame.ToArray());
        Assert.True(toA[0].Previous.IsEmpty);
        Assert.Equal(toA[0].Sequence + 1, toA[1].Sequence);
        Assert.Equal(toA[0].Frame.ToArray(), toA[1].Previous.ToArray());
        Assert.Equal(toA[1].Frame.ToArray(), toA[2].Previous.ToArray());
        Assert.All(sent, s => Assert.Equal(streamer.StreamId, s.Packet.StreamId));
        Assert.NotEqual(0, streamer.StreamId);
    }

    [Fact]
    public async Task After_a_discontinuity_the_previous_frame_is_left_out_and_the_sequence_jumps()
    {
        using var streamer = Streamer();
        streamer.ClientSeen(2, A);
        _source.Add(1);
        _source.Add(2, discontinuity: true);
        _source.Add(3);

        await Until(() => _sender.Count == 3);

        var p = _sender.Parsed().Select(s => s.Packet).ToList();
        Assert.Equal(p[0].Sequence + 1 + AudioStreamer.SequenceJump, p[1].Sequence);
        Assert.True(p[1].Previous.IsEmpty);
        Assert.Equal(p[1].Frame.ToArray(), p[2].Previous.ToArray());
    }

    [Fact]
    public async Task Nothing_is_sent_while_no_client_listens_and_the_next_packet_has_no_previous()
    {
        using var streamer = Streamer();
        _source.Add(1);
        _source.Add(2);
        await Until(() => streamer.Stats.FramesCaptured == 2);
        Assert.Equal(0, _sender.Count);

        streamer.ClientSeen(2, A);
        _source.Add(3);
        await Until(() => _sender.Count == 1);

        var p = Assert.Single(_sender.Parsed()).Packet;
        Assert.Equal(new byte[] { 3, 0xEE }, p.Frame.ToArray());
        Assert.True(p.Previous.IsEmpty);
    }

    [Fact]
    public async Task An_encoder_error_is_reported_and_streaming_carries_on()
    {
        var errors = new List<Exception>();
        using var streamer = Streamer(e => { lock (errors) errors.Add(e); });
        streamer.ClientSeen(2, A);
        _source.Add(1);
        _source.Add(99); // the encoder throws on this one
        _source.Add(3);

        await Until(() => _sender.Count == 2);

        lock (errors)
            Assert.IsType<InvalidOperationException>(Assert.Single(errors));
        var p = _sender.Parsed().Select(s => s.Packet).ToList();
        Assert.Equal(new byte[] { 3, 0xEE }, p[1].Frame.ToArray());
        Assert.True(p[1].Previous.IsEmpty); // frame 99 never went out, so 3 doesn't follow 1
    }

    [Fact]
    public async Task Stats_count_frames_packets_bytes_and_clients()
    {
        using var streamer = Streamer();
        streamer.ClientSeen(2, A);
        streamer.ClientSeen(3, B);
        _source.Add(1);

        await Until(() => streamer.Stats.PacketsSent == 1);

        var s = streamer.Stats;
        Assert.Equal(1, s.FramesCaptured);
        Assert.Equal(AudioPacket.HeaderSize + 2, s.BytesSent); // one packet, sent to both
        Assert.Equal(2, s.Clients);
        Assert.Equal("queue", streamer.SourceDescription);
    }
}
```

Add `using CouchLink.Core.Protocol;` at the top for `AudioPacket`.

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~AudioStreamerTests"`
Expected: build fails, `IAudioEncoder` / `AudioStreamer` not found.

- [ ] **Step 4: Write `AudioStreamer`**

`src/CouchLink.Core/Audio/AudioStreamer.cs`:

```csharp
using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Audio;

/// <summary>Encodes one 5 ms frame of interleaved 16-bit stereo PCM.</summary>
public interface IAudioEncoder : IDisposable
{
    /// <summary>Returns the number of bytes written to <paramref name="output"/>.</summary>
    int Encode(ReadOnlySpan<short> pcm, Span<byte> output);
}

/// <summary>A packet goes to every client; <see cref="PacketsSent"/> and <see cref="BytesSent"/> count it once.</summary>
public readonly record struct AudioSendStats(long FramesCaptured, long PacketsSent, long BytesSent, int Clients);

/// <summary>
/// Host side: on its own thread, reads 5 ms frames from the source, encodes each once and sends it,
/// with the previous frame attached, to every current client. Clients are learned from their input
/// packets (<see cref="ClientSeen"/>), as for video. After a discontinuity the packet carries no
/// previous frame and the sequence jumps by <see cref="SequenceJump"/>, so a client starts over
/// instead of treating the new audio as late. While no client listens nothing is encoded or sent.
/// Takes ownership of the source, the encoder and the sender.
/// </summary>
public sealed class AudioStreamer : IDisposable
{
    public const uint SequenceJump = 16;
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMilliseconds(50);

    private readonly IAudioSource _source;
    private readonly IAudioEncoder _encoder;
    private readonly IVideoPacketSender _sender;
    private readonly TimeProvider _time;
    private readonly long _start;
    private readonly Action<Exception>? _onError;
    private readonly StreamTargets _targets;
    private readonly Lock _gate = new();
    private readonly Thread _thread;
    private readonly short[] _pcm = new short[AudioFormat.FrameValues];
    private readonly byte[] _encoded = new byte[AudioPacket.MaxFrameBytes];
    private volatile bool _running = true;
    private byte[]? _previous;
    private uint _sequence;
    private long _framesCaptured, _packetsSent, _bytesSent;
    private int _clients;

    public AudioStreamer(
        IAudioSource source,
        IAudioEncoder encoder,
        IVideoPacketSender sender,
        int port,
        TimeProvider time,
        Action<Exception>? onError = null)
    {
        _source = source;
        _encoder = encoder;
        _sender = sender;
        _time = time;
        _start = time.GetTimestamp();
        _onError = onError;
        _targets = new StreamTargets(port);
        // Random per stream, so clients can tell a restarted host from late packets.
        StreamId = (ushort)Random.Shared.Next(1, ushort.MaxValue + 1);
        // Highest: a late frame is an audible gap, and the work is 0.3 ms per 5 ms.
        _thread = new Thread(Run) { IsBackground = true, Name = "CouchLink audio", Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    public ushort StreamId { get; }

    public string SourceDescription => _source.Description;

    public AudioSendStats Stats => new(
        Interlocked.Read(ref _framesCaptured),
        Interlocked.Read(ref _packetsSent),
        Interlocked.Read(ref _bytesSent),
        Volatile.Read(ref _clients));

    private TimeSpan Now => _time.GetElapsedTime(_start);

    /// <summary>A client's input packet arrived; it gets audio from the next frame.</summary>
    public void ClientSeen(byte slot, IPAddress address)
    {
        lock (_gate)
            _targets.Seen(slot, address, Now);
    }

    private void Run()
    {
        while (_running)
        {
            try
            {
                StreamOneFrame();
            }
            catch (Exception e)
            {
                _previous = null; // the next packet must not claim to follow a frame that never went out
                _onError?.Invoke(e);
                Thread.Sleep(100); // don't spin if the source or encoder keeps failing
            }
        }
    }

    private void StreamOneFrame()
    {
        if (!_source.TryRead(_pcm, PollTimeout, out bool discontinuity))
            return;
        Interlocked.Increment(ref _framesCaptured);
        if (discontinuity)
        {
            _previous = null;
            _sequence += SequenceJump;
        }

        IReadOnlyList<IPEndPoint> targets;
        lock (_gate)
            targets = _targets.Current(Now);
        Volatile.Write(ref _clients, targets.Count);
        if (targets.Count == 0)
        {
            _previous = null; // whoever joins next starts cleanly
            return;
        }

        int length = _encoder.Encode(_pcm, _encoded);
        var frame = _encoded.AsSpan(0, length).ToArray();
        var packet = new AudioPacket(StreamId, _sequence++, frame, _previous).ToArray();
        _previous = frame;
        _sender.Send([packet], targets);
        Interlocked.Increment(ref _packetsSent);
        Interlocked.Add(ref _bytesSent, packet.Length);
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join();
        _source.Dispose();
        _encoder.Dispose();
        _sender.Dispose();
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~AudioStreamerTests"`
Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add src/CouchLink.Core/Audio/AudioStreamer.cs tests/CouchLink.Core.Tests/AudioTestKit.cs tests/CouchLink.Core.Tests/AudioStreamerTests.cs
git commit -m "feat(core): host audio streamer with redundancy and discontinuity handling"
```

---

### Task 6: Client audio (receive, decode, conceal, drift)

**Files:**
- Create: `src/CouchLink.Core/Audio/AudioClient.cs`
- Modify: `tests/CouchLink.Core.Tests/AudioTestKit.cs` (add `FakeAudioDecoder`)
- Test: `tests/CouchLink.Core.Tests/AudioClientTests.cs`

**Interfaces:**
- Consumes: `JitterBuffer`, `Playout`, `PlayoutKind` (Task 3), `DriftControl` (Task 4), `AudioPacket` (Task 1), `AudioFormat`.
- Produces:
  - `interface IAudioDecoder : IDisposable { int Decode(ReadOnlySpan<byte> frame, Span<short> pcm); void Conceal(Span<short> pcm); void Reset(); }`
  - `readonly record struct AudioClientStats(long Packets, long Repaired, long Concealed, long Late, long Discarded, long Underruns, long DecodeErrors, long DriftCorrections, double BufferMs, bool Playing)`
  - `sealed class AudioClient : IDisposable` with `AudioClient(IAudioDecoder decoder)`, `void Receive(byte[] datagram)` (any thread), `int Read(Span<short> output)` (fills all of it; the output device's thread), `AudioClientStats Stats`. Takes ownership of the decoder.

- [ ] **Step 1: Add the fake decoder to the test kit**

Append to `tests/CouchLink.Core.Tests/AudioTestKit.cs`:

```csharp
/// <summary>
/// "Decodes" a frame into samples that all equal its first byte; conceals as -1; throws on a frame
/// longer than one byte (the corrupt-frame stand-in).
/// </summary>
internal sealed class FakeAudioDecoder : IAudioDecoder
{
    public int Decoded;
    public int Resets;

    public int Decode(ReadOnlySpan<byte> frame, Span<short> pcm)
    {
        if (frame.Length > 1)
            throw new InvalidDataException("corrupt frame");
        Decoded++;
        pcm.Fill(frame[0]);
        return AudioFormat.FrameSamples;
    }

    public void Conceal(Span<short> pcm) => pcm.Fill(-1);

    public void Reset() => Resets++;

    public void Dispose() { }
}
```

- [ ] **Step 2: Write the failing tests**

`tests/CouchLink.Core.Tests/AudioClientTests.cs`:

```csharp
using CouchLink.Core.Audio;
using CouchLink.Core.Protocol;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class AudioClientTests
{
    private const int Frame = AudioFormat.FrameValues;

    private readonly FakeAudioDecoder _decoder = new();

    private static void Send(AudioClient client, params AudioPacket[] packets)
    {
        foreach (var p in packets)
            client.Receive(p.ToArray());
    }

    /// <summary>Reads <paramref name="frames"/> frames' worth in device-sized chunks; one value per 5 ms ("mixed" if a block isn't uniform).</summary>
    private static string Hear(AudioClient client, int frames, int chunk = 256)
    {
        var all = new short[frames * Frame];
        for (int at = 0; at < all.Length; at += chunk)
            client.Read(all.AsSpan(at, Math.Min(chunk, all.Length - at)));
        return string.Join(" ", all.Chunk(Frame).Select(f => f.Distinct().Count() == 1 ? f[0].ToString() : "mixed"));
    }

    [Fact]
    public void Silence_until_three_frames_are_queued()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2));
        Assert.Equal("0", Hear(client, 1));

        Send(client, Packet(3));

        Assert.Equal("1 2 3", Hear(client, 3));
    }

    [Fact]
    public void Odd_device_chunks_lose_nothing()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3), Packet(4), Packet(5), Packet(6));

        Assert.Equal("1 2 3 4 5 6", Hear(client, 6, chunk: 2 * 137));
    }

    [Fact]
    public void A_lost_packet_plays_its_copy_from_the_next()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3), Packet(5)); // 4 lost

        Assert.Equal("1 2 3 4 5", Hear(client, 5));
        Assert.Equal(1, client.Stats.Repaired);
    }

    [Fact]
    public void A_frame_lost_everywhere_is_concealed()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3), Packet(5, withPrevious: false), Packet(6));

        Assert.Equal("1 2 3 -1 5 6", Hear(client, 6));
        Assert.Equal(1, client.Stats.Concealed);
    }

    [Fact]
    public void A_corrupt_frame_is_concealed_and_counted()
    {
        using var client = new AudioClient(_decoder);
        Send(client,
            Packet(1, withPrevious: false),
            Packet(2),
            new AudioPacket(1, 3, new byte[] { 0xFF, 0xFF }, new byte[] { 2 }),
            Packet(4));

        Assert.Equal("1 2 -1 4", Hear(client, 4));
        Assert.Equal(1, client.Stats.DecodeErrors);
    }

    [Fact]
    public void A_new_stream_resets_the_decoder_and_starts_over()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3));
        Assert.Equal(1, _decoder.Resets);
        Assert.Equal("1", Hear(client, 1));

        Send(client, Packet(50, withPrevious: false, stream: 2));
        Assert.Equal(2, _decoder.Resets);
        Assert.Equal("0", Hear(client, 1));

        Send(client, Packet(51, stream: 2), Packet(52, stream: 2));
        Assert.Equal("50 51 52", Hear(client, 3));
    }

    [Fact]
    public void After_a_host_discontinuity_playback_restarts_cleanly()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3), Packet(4));
        Assert.Equal("1 2 3 4", Hear(client, 4));
        Assert.Equal("-1 -1 -1 -1 0", Hear(client, 5)); // the host's capture restarts: concealment, then silence

        // The host's next packets: no previous frame, sequence jumped by 16.
        Send(client, Packet(5 + AudioStreamer.SequenceJump, withPrevious: false), Packet(22), Packet(23));

        Assert.Equal("21 22 23", Hear(client, 3));
        Assert.Equal(0, client.Stats.Late);
    }

    [Fact]
    public void A_buffer_that_stays_too_full_is_played_slightly_faster()
    {
        using var client = new AudioClient(_decoder);
        var frame = new short[Frame];
        uint next = 1;
        void SendOne() => client.Receive(Packet(next, withPrevious: next++ > 1).ToArray());

        for (int i = 0; i < 3; i++)
            SendOne(); // primed
        for (int i = 0; i < DriftControl.WindowFrames; i++)
        {
            SendOne();
            client.Read(frame); // 4 queued before each frame: the baseline
        }
        for (int i = 0; i < 3; i++)
            SendOne(); // 15 ms more
        for (int i = 0; i < DriftControl.WindowFrames; i++)
        {
            SendOne();
            client.Read(frame); // 7 queued: too full
        }
        double before = client.Stats.BufferMs;

        for (int i = 0; i < 2 * DriftControl.WindowFrames; i++)
        {
            SendOne();
            client.Read(frame);
        }

        Assert.True(client.Stats.DriftCorrections > 0);
        Assert.True(client.Stats.BufferMs < before, $"{client.Stats.BufferMs} ms should be below {before} ms");
    }

    [Fact]
    public void Stats_report_packets_and_buffer_depth()
    {
        using var client = new AudioClient(_decoder);
        Send(client, Packet(1, withPrevious: false), Packet(2), Packet(3));

        var s = client.Stats;
        Assert.Equal(3, s.Packets);
        Assert.Equal(15, s.BufferMs);
        Assert.True(s.Playing);
    }

    [Fact]
    public void Junk_is_ignored()
    {
        using var client = new AudioClient(_decoder);
        client.Receive(new byte[] { 1, 2, 3 });

        Assert.Equal(0, client.Stats.Packets);
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~AudioClientTests"`
Expected: build fails, `IAudioDecoder` / `AudioClient` not found.

- [ ] **Step 4: Write `AudioClient`**

`src/CouchLink.Core/Audio/AudioClient.cs`:

```csharp
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Audio;

/// <summary>Decodes Opus frames into interleaved 16-bit stereo PCM.</summary>
public interface IAudioDecoder : IDisposable
{
    /// <summary>Decodes one frame; returns samples per channel. Throws on a corrupt frame.</summary>
    int Decode(ReadOnlySpan<byte> frame, Span<short> pcm);

    /// <summary>Makes up one frame to cover a lost one (packet loss concealment).</summary>
    void Conceal(Span<short> pcm);

    void Reset();
}

public readonly record struct AudioClientStats(
    long Packets,
    long Repaired,
    long Concealed,
    long Late,
    long Discarded,
    long Underruns,
    long DecodeErrors,
    long DriftCorrections,
    double BufferMs,
    bool Playing);

/// <summary>
/// Client side: audio datagrams in through <see cref="Receive"/> (the receive thread), PCM out
/// through <see cref="Read"/> (the sound device's thread). Each 5 ms of output comes from the
/// jitter buffer: the frame decoded, its redundant copy decoded, Opus concealment, or silence. A
/// corrupt frame is concealed and counted. Drift control drops or repeats the middle sample of a
/// frame when the buffer runs away from its starting depth. Takes ownership of the decoder.
/// </summary>
public sealed class AudioClient(IAudioDecoder decoder) : IDisposable
{
    private readonly Lock _lock = new();
    private readonly JitterBuffer _buffer = new();
    private readonly DriftControl _drift = new();
    private readonly short[] _frame = new short[AudioFormat.FrameValues + AudioFormat.Channels]; // room to repeat one sample
    private int _frameLength, _frameRead; // device thread only
    private long _decodeErrors;

    public AudioClientStats Stats
    {
        get
        {
            lock (_lock)
            {
                var j = _buffer.Stats;
                return new AudioClientStats(j.Received, j.Repaired, j.Concealed, j.Late, j.Discarded, j.Underruns,
                    _decodeErrors, _drift.Corrections,
                    _buffer.Depth * AudioFormat.FrameDuration.TotalMilliseconds, _buffer.Playing);
            }
        }
    }

    /// <summary>An audio datagram from the host. Anything that isn't a valid audio packet is ignored.</summary>
    public void Receive(byte[] datagram)
    {
        if (!AudioPacket.TryParse(datagram, out var packet))
            return;
        lock (_lock)
        {
            if (_buffer.Add(packet))
            {
                decoder.Reset();
                _drift.Reset();
            }
        }
    }

    /// <summary>Fills all of <paramref name="output"/> (interleaved stereo) and returns its length.</summary>
    public int Read(Span<short> output)
    {
        int written = 0;
        while (written < output.Length)
        {
            if (_frameRead == _frameLength)
                NextFrame();
            int take = Math.Min(output.Length - written, _frameLength - _frameRead);
            _frame.AsSpan(_frameRead, take).CopyTo(output[written..]);
            _frameRead += take;
            written += take;
        }
        return output.Length;
    }

    private void NextFrame()
    {
        _frameRead = 0;
        _frameLength = AudioFormat.FrameValues;
        var pcm = _frame.AsSpan(0, AudioFormat.FrameValues);
        lock (_lock)
        {
            int depth = _buffer.Depth;
            var playout = _buffer.Next();
            switch (playout.Kind)
            {
                case PlayoutKind.Frame or PlayoutKind.Repaired:
                    try
                    {
                        decoder.Decode(playout.Data.Span, pcm);
                    }
                    catch (Exception)
                    {
                        _decodeErrors++; // counted, not logged: a bad stream would flood the log
                        decoder.Conceal(pcm);
                    }
                    break;
                case PlayoutKind.Conceal:
                    decoder.Conceal(pcm);
                    break;
                default:
                    pcm.Clear();
                    _drift.Reset();
                    return;
            }
            AdjustForDrift(_drift.Next(depth));
        }
    }

    /// <summary>Drops (-1) or repeats (+1) the middle sample of the current frame.</summary>
    private void AdjustForDrift(int correction)
    {
        const int middle = AudioFormat.FrameValues / 2; // a sample boundary: left channel
        if (correction < 0)
            _frame.AsSpan(middle + AudioFormat.Channels, AudioFormat.FrameValues - middle - AudioFormat.Channels)
                .CopyTo(_frame.AsSpan(middle));
        else if (correction > 0)
            _frame.AsSpan(middle, AudioFormat.FrameValues - middle).CopyTo(_frame.AsSpan(middle + AudioFormat.Channels));
        _frameLength = AudioFormat.FrameValues + correction * AudioFormat.Channels;
    }

    public void Dispose() => decoder.Dispose();
}
```

(`Span.CopyTo` handles the overlapping copies correctly.)

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~AudioClientTests"`
Expected: PASS, 10 tests.

- [ ] **Step 6: Commit**

```bash
git add src/CouchLink.Core/Audio/AudioClient.cs tests/CouchLink.Core.Tests/AudioTestKit.cs tests/CouchLink.Core.Tests/AudioClientTests.cs
git commit -m "feat(core): client audio with decode, concealment and drift correction"
```

---

### Task 7: One socket for video and audio; same-PC check

**Files:**
- Create: `src/CouchLink.Core/Net/StreamDispatcher.cs`
- Create: `src/CouchLink.Core/Net/LocalAddress.cs`
- Modify: `src/CouchLink.Core/Video/VideoClient.cs`
- Test: `tests/CouchLink.Core.Tests/StreamDispatcherTests.cs`, `tests/CouchLink.Core.Tests/LocalAddressTests.cs`

**Interfaces:**
- Consumes: `Wire.TryGetType`, `Wire.TypeAudio` (Task 1), existing `VideoReceiver` (`Task RunAsync(Action<byte[]>, CancellationToken, Action<Exception>?)`, `int LocalPort`), `FramePacketizer(ushort streamId)`, `AudioTestKit.Packet`/`Until` (Task 3).
- Produces:
  - `sealed class StreamDispatcher : IDisposable` with `StreamDispatcher(VideoReceiver receiver, Action<byte[]> video, Action<byte[]> audio, Action<Exception>? onError = null)` and `static void Route(byte[] datagram, Action<byte[]> video, Action<byte[]> audio)`. Takes ownership of the receiver.
  - `VideoClient(Action requestKeyframe, Action<AssembledFrame> onFrame, TimeProvider time, Action<Exception>? onError = null, Action<long>? sendTimingPing = null)` (no receiver) and `public void Receive(byte[] datagram)`. The existing constructor with a `VideoReceiver` first is unchanged for callers.
  - `static class LocalAddress` with `bool IsThisPc(IPAddress address)`, `bool IsThisPc(IPAddress address, IEnumerable<IPAddress> own)`, `IEnumerable<IPAddress> OwnAddresses()`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/StreamDispatcherTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class StreamDispatcherTests
{
    private static byte[] Typed(byte type)
    {
        var datagram = new byte[20];
        Wire.WriteHeader(datagram, type);
        return datagram;
    }

    [Fact]
    public void Datagrams_go_to_video_or_audio_by_type_and_the_rest_is_ignored()
    {
        var video = new List<byte[]>();
        var audio = new List<byte[]>();
        var datagrams = new[]
        {
            Typed(Wire.TypeVideoShard), Typed(Wire.TypeTimingReply), Typed(Wire.TypeAudio),
            Typed(Wire.TypeInput), Typed(99), new byte[3], new byte[20], Array.Empty<byte>(),
        };

        foreach (var d in datagrams)
            StreamDispatcher.Route(d, video.Add, audio.Add);

        Assert.Equal(new[] { Wire.TypeVideoShard, Wire.TypeTimingReply }, video.Select(d => d[3]));
        Assert.Equal(Wire.TypeAudio, Assert.Single(audio)[3]);
    }

    [Fact]
    public async Task Video_and_audio_on_one_port_both_arrive()
    {
        int frames = 0, audio = 0;
        var receiver = new VideoReceiver(port: 0);
        var to = new IPEndPoint(IPAddress.Loopback, receiver.LocalPort);
        using var video = new VideoClient(() => { }, _ => Interlocked.Increment(ref frames), TimeProvider.System);
        using var dispatcher = new StreamDispatcher(receiver, video.Receive, _ => Interlocked.Increment(ref audio));
        using var udp = new UdpClient();

        foreach (var p in new FramePacketizer(streamId: 1).Packetize(0, new byte[5000], keyframe: true))
            udp.Send(p, p.Length, to);
        for (uint s = 1; s <= 20; s++)
        {
            var a = Packet(s).ToArray();
            udp.Send(a, a.Length, to);
        }

        await Until(() => Volatile.Read(ref frames) == 1 && Volatile.Read(ref audio) == 20);
    }

    [Fact]
    public async Task A_failing_audio_handler_does_not_stop_video()
    {
        int frames = 0, errors = 0;
        var receiver = new VideoReceiver(port: 0);
        var to = new IPEndPoint(IPAddress.Loopback, receiver.LocalPort);
        using var video = new VideoClient(() => { }, _ => Interlocked.Increment(ref frames), TimeProvider.System);
        using var dispatcher = new StreamDispatcher(receiver, video.Receive,
            _ => throw new InvalidOperationException("audio broke"), _ => Interlocked.Increment(ref errors));
        using var udp = new UdpClient();

        var a = Packet(1).ToArray();
        udp.Send(a, a.Length, to);
        foreach (var p in new FramePacketizer(streamId: 1).Packetize(0, new byte[5000], keyframe: true))
            udp.Send(p, p.Length, to);

        await Until(() => Volatile.Read(ref frames) == 1 && Volatile.Read(ref errors) == 1);
    }
}
```

`tests/CouchLink.Core.Tests/LocalAddressTests.cs`:

```csharp
using System.Net;
using CouchLink.Core.Net;

namespace CouchLink.Core.Tests;

public class LocalAddressTests
{
    [Fact]
    public void Loopback_is_this_pc()
    {
        Assert.True(LocalAddress.IsThisPc(IPAddress.Loopback, []));
        Assert.True(LocalAddress.IsThisPc(IPAddress.Parse("127.0.0.5"), []));
    }

    [Fact]
    public void One_of_our_own_lan_addresses_is_this_pc()
    {
        var own = new[] { IPAddress.Parse("192.168.1.10") };

        Assert.True(LocalAddress.IsThisPc(IPAddress.Parse("192.168.1.10"), own));
        Assert.False(LocalAddress.IsThisPc(IPAddress.Parse("192.168.1.11"), own));
    }

    [Fact]
    public void Every_address_this_pc_reports_is_this_pc()
    {
        Assert.True(LocalAddress.IsThisPc(IPAddress.Loopback));
        foreach (var address in LocalAddress.OwnAddresses())
            Assert.True(LocalAddress.IsThisPc(address));
    }
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~StreamDispatcherTests|FullyQualifiedName~LocalAddressTests"`
Expected: build fails: `StreamDispatcher`, `LocalAddress` not found, and no `VideoClient` constructor takes 3 arguments.

- [ ] **Step 3: Give `VideoClient` a receiver-less constructor and a public `Receive`**

In `src/CouchLink.Core/Video/VideoClient.cs`:

1. Change the fields `private readonly VideoReceiver _receiver;` to `private readonly VideoReceiver? _receiver;` and `private readonly Task _loop;` to `private readonly Task? _loop;`.
2. Replace the existing constructor with these two:

```csharp
    /// <summary>Datagrams come in through <see cref="Receive"/>, from a <see cref="StreamDispatcher"/>.</summary>
    public VideoClient(
        Action requestKeyframe,
        Action<AssembledFrame> onFrame,
        TimeProvider time,
        Action<Exception>? onError = null,
        Action<long>? sendTimingPing = null)
    {
        _sendTimingPing = sendTimingPing;
        _requestKeyframe = requestKeyframe;
        _onFrame = onFrame;
        _onError = onError;
        _time = time;
        _start = time.GetTimestamp();
        _assembler = new FrameAssembler(_gate.FrameLost);
        _timer = time.CreateTimer(_ => Tick(), null, TickInterval, TickInterval);
    }

    /// <summary>Runs <paramref name="receiver"/>'s receive loop itself, and owns it.</summary>
    public VideoClient(
        VideoReceiver receiver,
        Action requestKeyframe,
        Action<AssembledFrame> onFrame,
        TimeProvider time,
        Action<Exception>? onError = null,
        Action<long>? sendTimingPing = null)
        : this(requestKeyframe, onFrame, time, onError, sendTimingPing)
    {
        _receiver = receiver;
        _loop = receiver.RunAsync(Receive, _cts.Token, onError);
    }
```

3. Rename `private void OnDatagram(byte[] datagram)` to:

```csharp
    /// <summary>A video datagram: a shard or a timing reply. Call from one receive thread only.</summary>
    public void Receive(byte[] datagram)
```

4. In `Dispose`, change `_loop.Wait(TimeSpan.FromSeconds(2));` to `_loop?.Wait(TimeSpan.FromSeconds(2));` and `_receiver.Dispose();` to `_receiver?.Dispose();`.
5. In the class summary, change "Takes ownership of the receiver." to "Given a receiver, it runs the receive loop and takes ownership of it; otherwise a `StreamDispatcher` calls `Receive`."

- [ ] **Step 4: Write `StreamDispatcher`**

`src/CouchLink.Core/Net/StreamDispatcher.cs`:

```csharp
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>
/// Client side: the one socket on the video port (47802) carries video shards, timing replies and
/// audio. This runs its receive loop and hands each datagram to the video or the audio handler by
/// its packet type; anything else is ignored. An exception in a handler goes to <c>onError</c> and
/// the loop carries on, so a failing audio handler never stops video. Takes ownership of the receiver.
/// </summary>
public sealed class StreamDispatcher : IDisposable
{
    private readonly VideoReceiver _receiver;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;

    public StreamDispatcher(VideoReceiver receiver, Action<byte[]> video, Action<byte[]> audio, Action<Exception>? onError = null)
    {
        _receiver = receiver;
        _loop = receiver.RunAsync(datagram => Route(datagram, video, audio), _cts.Token, onError);
    }

    public static void Route(byte[] datagram, Action<byte[]> video, Action<byte[]> audio)
    {
        if (!Wire.TryGetType(datagram, out var type))
            return;
        if (type == Wire.TypeAudio)
            audio(datagram);
        else if (type is Wire.TypeVideoShard or Wire.TypeTimingReply)
            video(datagram);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _loop.Wait(TimeSpan.FromSeconds(2));
        _receiver.Dispose();
        _cts.Dispose();
    }
}
```

- [ ] **Step 5: Write `LocalAddress`**

`src/CouchLink.Core/Net/LocalAddress.cs`:

```csharp
using System.Net;
using System.Net.NetworkInformation;

namespace CouchLink.Core.Net;

/// <summary>Whether an address is this PC: loopback, or one of its own network addresses.</summary>
public static class LocalAddress
{
    public static bool IsThisPc(IPAddress address) => IsThisPc(address, OwnAddresses());

    public static bool IsThisPc(IPAddress address, IEnumerable<IPAddress> own) =>
        IPAddress.IsLoopback(address) || own.Contains(address);

    public static IEnumerable<IPAddress> OwnAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(u => u.Address);
}
```

- [ ] **Step 6: Run the new tests and every existing video test**

Run: `dotnet test tests/CouchLink.Core.Tests`
Expected: PASS, all tests (the existing `VideoClientTimingTests`, `VideoLoopbackTests` and `UdpVideoTests` still use the receiver constructor and must not change).

- [ ] **Step 7: Commit**

```bash
git add src/CouchLink.Core/Net src/CouchLink.Core/Video/VideoClient.cs tests/CouchLink.Core.Tests/StreamDispatcherTests.cs tests/CouchLink.Core.Tests/LocalAddressTests.cs
git commit -m "feat(core): share the client's video port with audio; same-PC check"
```

---

### Task 8: `CouchLink.Audio` project and the Opus codec

**Files:**
- Create: `src/CouchLink.Audio/CouchLink.Audio.csproj`
- Create: `src/CouchLink.Audio/OpusAudioEncoder.cs`
- Create: `src/CouchLink.Audio/OpusAudioDecoder.cs`
- Create: `tests/CouchLink.Audio.Tests/CouchLink.Audio.Tests.csproj`
- Test: `tests/CouchLink.Audio.Tests/OpusTests.cs`
- Modify: `CouchLink.slnx`

**Interfaces:**
- Consumes: `IAudioEncoder` (Task 5), `IAudioDecoder` (Task 6), `AudioFormat`, `AudioPacket.MaxFrameBytes`.
- Produces: `sealed class OpusAudioEncoder : IAudioEncoder` and `sealed class OpusAudioDecoder : IAudioDecoder`, both with parameterless constructors, in namespace `CouchLink.Audio`.

- [ ] **Step 1: Create the two projects and add them to the solution**

`src/CouchLink.Audio/CouchLink.Audio.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\CouchLink.Core\CouchLink.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Concentus" Version="2.2.2" />
    <PackageReference Include="NAudio.Wasapi" Version="3.1.0" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

`tests/CouchLink.Audio.Tests/CouchLink.Audio.Tests.csproj`:

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
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CouchLink.Audio\CouchLink.Audio.csproj" />
  </ItemGroup>

</Project>
```

In `CouchLink.slnx`, add `<Project Path="src/CouchLink.Audio/CouchLink.Audio.csproj" />` right after the `src/CouchLink.App/...` line, and `<Project Path="tests/CouchLink.Audio.Tests/CouchLink.Audio.Tests.csproj" />` right before the `tests/CouchLink.Core.Tests/...` line (both keep the folders alphabetical).

- [ ] **Step 2: Write the failing tests**

`tests/CouchLink.Audio.Tests/OpusTests.cs`:

```csharp
using CouchLink.Core.Audio;
using CouchLink.Core.Protocol;

namespace CouchLink.Audio.Tests;

public class OpusTests
{
    private static short[] Sine(int frame, int hz = 1000, short amplitude = 8000)
    {
        var pcm = new short[AudioFormat.FrameValues];
        for (int i = 0; i < AudioFormat.FrameSamples; i++)
        {
            long n = (long)frame * AudioFormat.FrameSamples + i;
            var value = (short)(amplitude * Math.Sin(2 * Math.PI * hz * n / AudioFormat.SampleRate));
            pcm[2 * i] = value;
            pcm[2 * i + 1] = value;
        }
        return pcm;
    }

    [Fact]
    public void A_1_kHz_sine_keeps_its_level_and_pitch()
    {
        using var encoder = new OpusAudioEncoder();
        using var decoder = new OpusAudioDecoder();
        var packet = new byte[AudioPacket.MaxFrameBytes];
        var left = new List<short>();

        for (int f = 0; f < 200; f++)
        {
            int length = encoder.Encode(Sine(f), packet);
            var pcm = new short[AudioFormat.FrameValues];
            Assert.Equal(AudioFormat.FrameSamples, decoder.Decode(packet.AsSpan(0, length), pcm));
            if (f >= 10) // after the codec settles
                left.AddRange(pcm.Where((_, i) => i % 2 == 0));
        }

        double rms = Math.Sqrt(left.Average(v => (double)v * v));
        Assert.InRange(rms, 8000 / Math.Sqrt(2) * 0.8, 8000 / Math.Sqrt(2) * 1.2);
        int crossings = left.Zip(left.Skip(1)).Count(p => (p.First < 0) != (p.Second < 0));
        Assert.InRange(crossings, 1840, 1960); // 190 frames = 950 ms of 1 kHz = 1900 crossings
    }

    [Fact]
    public void Frames_are_about_80_bytes_and_fit_a_packet()
    {
        using var encoder = new OpusAudioEncoder();
        var packet = new byte[AudioPacket.MaxFrameBytes];

        var sizes = Enumerable.Range(0, 200).Select(f => encoder.Encode(Sine(f, hz: 440 + f), packet)).ToList();

        Assert.All(sizes, s => Assert.InRange(s, 1, AudioPacket.MaxFrameBytes));
        Assert.InRange(sizes.Average(), 60, 110);
    }

    [Fact]
    public void Concealment_carries_the_sound_on_for_a_full_frame()
    {
        using var encoder = new OpusAudioEncoder();
        using var decoder = new OpusAudioDecoder();
        var packet = new byte[AudioPacket.MaxFrameBytes];
        var pcm = new short[AudioFormat.FrameValues];
        for (int f = 0; f < 20; f++)
            decoder.Decode(packet.AsSpan(0, encoder.Encode(Sine(f), packet)), pcm);

        Array.Clear(pcm);
        decoder.Conceal(pcm);

        Assert.Contains(pcm, v => v != 0);
    }

    [Fact]
    public void A_corrupt_frame_throws_so_the_client_conceals_it()
    {
        using var decoder = new OpusAudioDecoder();

        Assert.ThrowsAny<Exception>(() => decoder.Decode(new byte[] { 0xFF, 0xFF, 0x01 }, new short[AudioFormat.FrameValues]));
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Audio.Tests`
Expected: build fails, `OpusAudioEncoder` / `OpusAudioDecoder` not found.

- [ ] **Step 4: Write the codec wrappers**

`src/CouchLink.Audio/OpusAudioEncoder.cs`:

```csharp
using Concentus;
using Concentus.Enums;
using CouchLink.Core.Audio;

namespace CouchLink.Audio;

/// <summary>Opus through Concentus (pure C#): 48 kHz stereo, low-delay (CELT) mode, 128 kbps, 5 ms frames.</summary>
public sealed class OpusAudioEncoder : IAudioEncoder
{
    private readonly IOpusEncoder _encoder;

    public OpusAudioEncoder()
    {
        OpusCodecFactory.AttemptToUseNativeLibrary = false; // always the managed codec we ship and test
        _encoder = OpusCodecFactory.CreateEncoder(
            AudioFormat.SampleRate, AudioFormat.Channels, OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
        _encoder.Bitrate = AudioFormat.BitRate;
    }

    public int Encode(ReadOnlySpan<short> pcm, Span<byte> output) =>
        _encoder.Encode(pcm, AudioFormat.FrameSamples, output, output.Length);

    public void Dispose()
    {
    }
}
```

`src/CouchLink.Audio/OpusAudioDecoder.cs`:

```csharp
using Concentus;
using CouchLink.Core.Audio;

namespace CouchLink.Audio;

/// <summary>Opus through Concentus (pure C#); <see cref="Conceal"/> is Opus packet loss concealment.</summary>
public sealed class OpusAudioDecoder : IAudioDecoder
{
    private readonly IOpusDecoder _decoder;

    public OpusAudioDecoder()
    {
        OpusCodecFactory.AttemptToUseNativeLibrary = false;
        _decoder = OpusCodecFactory.CreateDecoder(AudioFormat.SampleRate, AudioFormat.Channels);
    }

    /// <summary>Throws Concentus's OpusException on a corrupt frame.</summary>
    public int Decode(ReadOnlySpan<byte> frame, Span<short> pcm) =>
        _decoder.Decode(frame, pcm, AudioFormat.FrameSamples, false);

    public void Conceal(Span<short> pcm) =>
        _decoder.Decode(ReadOnlySpan<byte>.Empty, pcm, AudioFormat.FrameSamples, false);

    public void Reset() => _decoder.ResetState();

    public void Dispose()
    {
    }
}
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Audio.Tests`
Expected: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
git add src/CouchLink.Audio tests/CouchLink.Audio.Tests CouchLink.slnx
git commit -m "feat(audio): CouchLink.Audio project with the Opus encoder and decoder"
```

---

### Task 9: Loopback capture and default-device output

**Files:**
- Create: `src/CouchLink.Audio/LoopbackSource.cs`
- Create: `src/CouchLink.Audio/DefaultDevicePlayer.cs`

**Interfaces:**
- Consumes: `IAudioSource`, `FrameSlicer`, `AudioFormat` (Task 2); NAudio 3.1 (`WasapiRecorderBuilder`, `WasapiRecorder`, `WasapiPlayerBuilder`, `WasapiPlayer`, `MMDeviceEnumerator`, `MMDeviceNotificationClient`, `IWaveProvider`).
- Produces:
  - `sealed class LoopbackSource : IAudioSource` with `static LoopbackSource Open(Action<string>? log = null)` (throws if nothing can be captured), `const string ProcessLoopback = "process loopback"`, `const string DeviceLoopback = "device loopback"`, `static TimeSpan RetryInterval` (2 s).
  - `delegate int PcmReader(Span<short> output)` (`AudioClient.Read` fits it).
  - `sealed class DefaultDevicePlayer : IDisposable` with `DefaultDevicePlayer(PcmReader read, Action<string>? log = null)` (never throws for a missing device; it keeps retrying), `string Status`, `static TimeSpan RetryInterval` (1 s).

These classes need real sound hardware, so they have no unit tests. CI has no audio device. They are checked in the app in Task 11 (Step 8) and in the gate (Task 12).

- [ ] **Step 1: Write `LoopbackSource`**

`src/CouchLink.Audio/LoopbackSource.cs`:

```csharp
using System.Collections.Concurrent;
using CouchLink.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CouchLink.Audio;

/// <summary>
/// The host's sound, captured with WASAPI loopback as 48 kHz 16-bit stereo (Windows resamples and
/// downmixes). On Windows 10 2004 and later it is process loopback, leaving out CouchLink's own
/// process tree; it delivers 10 ms buffers continuously, zeros included. Otherwise it is loopback of
/// the default output device, which delivers nothing while the PC is silent and flags the first
/// buffer after. A frame after missing audio (WASAPI's discontinuity flag, a jump in the device
/// position, a dropped backlog or a restart) is marked as a discontinuity. If capture stops it
/// reopens every <see cref="RetryInterval"/>.
/// </summary>
public sealed class LoopbackSource : IAudioSource
{
    public const string ProcessLoopback = "process loopback";
    public const string DeviceLoopback = "device loopback";
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(2);

    /// <summary>200 ms. If the streamer falls this far behind, the backlog is dropped.</summary>
    private const int MaxQueuedFrames = 40;

    private static readonly WaveFormat Format = new(AudioFormat.SampleRate, 16, AudioFormat.Channels);

    private readonly BlockingCollection<(short[] Frame, bool Discontinuity)> _frames = new();
    private readonly MMDeviceEnumerator _devices = new();
    private readonly FrameSlicer _slicer;
    private readonly Action<string>? _log;
    private readonly Lock _lock = new();
    private WasapiRecorder? _recorder;
    private Timer? _retry;
    private long _expectedPosition = -1;
    private bool _disposed;

    private LoopbackSource(Action<string>? log)
    {
        _log = log;
        _slicer = new FrameSlicer(Enqueue);
    }

    public string Description { get; private set; } = "";

    /// <summary>Starts capturing. Throws if the host's sound can't be captured at all.</summary>
    public static LoopbackSource Open(Action<string>? log = null)
    {
        var source = new LoopbackSource(log);
        try
        {
            source.Start();
        }
        catch
        {
            source.Dispose();
            throw;
        }
        return source;
    }

    public bool TryRead(Span<short> frame, TimeSpan timeout, out bool discontinuity)
    {
        discontinuity = false;
        if (!_frames.TryTake(out var item, timeout))
            return false;
        item.Frame.CopyTo(frame);
        discontinuity = item.Discontinuity;
        return true;
    }

    private void Start()
    {
        var recorder = Build(out var description);
        recorder.DataAvailable += OnData;
        recorder.RecordingStopped += OnStopped;
        lock (_lock)
        {
            if (_disposed)
            {
                recorder.RecordingStopped -= OnStopped;
                recorder.Dispose();
                return;
            }
            _recorder = recorder;
            _expectedPosition = -1;
            _slicer.Write([], discontinuity: true);
            Description = description;
        }
        recorder.StartRecording();
    }

    private WasapiRecorder Build(out string description)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            try
            {
                var builder = new WasapiRecorderBuilder()
                    .WithProcessLoopback((uint)Environment.ProcessId, ProcessLoopbackMode.ExcludeTargetProcessTree)
                    .WithFormat(Format)
                    .WithEventSync();
                description = ProcessLoopback;
                // Off the calling (UI) thread: activation completes asynchronously.
                return Task.Run(() => builder.BuildAsync()).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                _log?.Invoke($"Audio: process loopback unavailable ({e.Message}); using device loopback");
            }
        }

        // Device loopback can't use NAudio's automatic default-device routing, so take the default now.
        var device = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var deviceBuilder = new WasapiRecorderBuilder().WithDevice(device).WithLoopbackCapture().WithFormat(Format).WithEventSync();
        description = DeviceLoopback;
        return Task.Run(() => deviceBuilder.BuildAsync()).GetAwaiter().GetResult();
    }

    private void OnData(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (data.IsEmpty)
            return; // device loopback hands out empty buffers while the PC is silent
        lock (_lock)
        {
            // Process loopback reports position 0 throughout; device loopback's position jumps after silence.
            bool jumped = devicePosition != 0 && _expectedPosition >= 0 && devicePosition != _expectedPosition;
            _expectedPosition = devicePosition + data.Length / Format.BlockAlign;
            _slicer.Write(data,
                discontinuity: jumped || flags.HasFlag(AudioClientBufferFlags.DataDiscontinuity),
                silent: flags.HasFlag(AudioClientBufferFlags.Silent));
        }
    }

    /// <summary>From the slicer, under <c>_lock</c>.</summary>
    private void Enqueue(short[] frame, bool discontinuity)
    {
        if (_frames.Count >= MaxQueuedFrames)
        {
            while (_frames.TryTake(out _))
            {
            }
            discontinuity = true;
        }
        _frames.Add((frame, discontinuity));
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || _retry is not null)
                return;
            _log?.Invoke($"Audio capture stopped{(e.Exception is { } ex ? $" ({ex.Message})" : "")}; reopening");
            _retry = new Timer(_ => Reopen(), null, RetryInterval, Timeout.InfiniteTimeSpan);
        }
    }

    private void Reopen()
    {
        WasapiRecorder? old;
        lock (_lock)
        {
            _retry?.Dispose();
            _retry = null;
            if (_disposed)
                return;
            old = _recorder;
            _recorder = null;
        }
        if (old is not null)
        {
            old.RecordingStopped -= OnStopped;
            old.Dispose();
        }

        try
        {
            Start();
            _log?.Invoke($"Audio capture reopened ({Description})");
        }
        catch (Exception e)
        {
            _log?.Invoke($"Audio capture could not reopen ({e.Message}); retrying");
            lock (_lock)
                if (!_disposed)
                    _retry = new Timer(_ => Reopen(), null, RetryInterval, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        WasapiRecorder? recorder;
        lock (_lock)
        {
            _disposed = true;
            _retry?.Dispose();
            _retry = null;
            recorder = _recorder;
            _recorder = null;
        }
        if (recorder is not null)
        {
            recorder.RecordingStopped -= OnStopped;
            recorder.StopRecording();
            recorder.Dispose();
        }
        _devices.Dispose();
        _frames.Dispose();
    }
}
```

- [ ] **Step 2: Write `DefaultDevicePlayer`**

`src/CouchLink.Audio/DefaultDevicePlayer.cs`:

```csharp
using System.Runtime.InteropServices;
using CouchLink.Core.Audio;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace CouchLink.Audio;

/// <summary>Fills <paramref name="output"/> with interleaved 16-bit stereo PCM. <c>AudioClient.Read</c> fits.</summary>
public delegate int PcmReader(Span<short> output);

/// <summary>
/// Plays the client's audio on the Windows default output device in shared mode, with the shortest
/// buffer the driver allows (IAudioClient3 low latency, 2-3 ms; otherwise 10 ms). Low-latency mode
/// can't use Windows' automatic stream routing, so this listens for default-device changes itself
/// and reopens on the new device. With no device, or after the device stops (unplugged), it retries
/// every <see cref="RetryInterval"/>. PCM is pulled from <c>read</c> on the device's thread.
/// </summary>
public sealed class DefaultDevicePlayer : IDisposable
{
    public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(1);

    private static readonly WaveFormat Format = new(AudioFormat.SampleRate, 16, AudioFormat.Channels);

    private readonly PullProvider _provider;
    private readonly Action<string>? _log;
    private readonly MMDeviceEnumerator _devices = new();
    private readonly MMDeviceNotificationClient _notifications;
    private readonly Lock _lock = new();
    private WasapiPlayer? _player;
    private Timer? _retry;
    private bool _disposed;
    private string _status = "starting";

    public DefaultDevicePlayer(PcmReader read, Action<string>? log = null)
    {
        _provider = new PullProvider(read);
        _log = log;
        _notifications = _devices.CreateNotificationClient(useSynchronizationContext: false);
        _notifications.DefaultDeviceChanged += OnDefaultDeviceChanged;
        Reopen();
    }

    /// <summary>The device and its buffer, e.g. "Headphones, 2 ms (low latency)", or why nothing plays.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (value == _status)
                return;
            _status = value;
            _log?.Invoke($"Audio output: {value}"); // only changes, so a missing device doesn't log every second
        }
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs e)
    {
        if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia)
            ScheduleReopen(TimeSpan.Zero); // never reopen inside Windows' notification callback
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        _log?.Invoke($"Audio output stopped{(e.Exception is { } ex ? $" ({ex.Message})" : "")}; reopening");
        ScheduleReopen(RetryInterval);
    }

    private void ScheduleReopen(TimeSpan delay)
    {
        lock (_lock)
        {
            if (_disposed || _retry is not null)
                return;
            _retry = new Timer(_ => Reopen(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Reopen()
    {
        WasapiPlayer? old;
        lock (_lock)
        {
            _retry?.Dispose();
            _retry = null;
            if (_disposed)
                return;
            old = _player;
            _player = null;
        }
        if (old is not null)
        {
            old.PlaybackStopped -= OnStopped;
            old.Dispose();
        }

        try
        {
            var player = Open();
            lock (_lock)
            {
                if (_disposed)
                {
                    player.PlaybackStopped -= OnStopped;
                    player.Dispose();
                    return;
                }
                _player = player;
            }
            Status = $"{player.DeviceFriendlyName}, {player.LatencyMilliseconds} ms{(player.LowLatencyActive ? " (low latency)" : "")}";
        }
        catch (Exception e)
        {
            Status = _devices.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
                ? $"unavailable: {e.Message}"
                : "no output device";
            ScheduleReopen(RetryInterval);
        }
    }

    private WasapiPlayer Open()
    {
        var device = _devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        WasapiPlayer player;
        try
        {
            player = Build(device, lowLatency: true);
        }
        catch (Exception e)
        {
            _log?.Invoke($"Audio output: low-latency mode failed ({e.Message}); using a 10 ms buffer");
            player = Build(device, lowLatency: false);
        }

        try
        {
            player.Init(_provider);
            player.PlaybackStopped += OnStopped;
            player.Play();
            return player;
        }
        catch
        {
            player.PlaybackStopped -= OnStopped;
            player.Dispose();
            throw;
        }
    }

    private static WasapiPlayer Build(MMDevice device, bool lowLatency)
    {
        var builder = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithEventSync()
            .WithCategory(AudioStreamCategory.GameMedia);
        builder = lowLatency ? builder.WithLowLatency(true) : builder.WithLatency(10);
        return Task.Run(() => builder.BuildAsync()).GetAwaiter().GetResult(); // off the calling (UI) thread
    }

    public void Dispose()
    {
        WasapiPlayer? player;
        lock (_lock)
        {
            _disposed = true;
            _retry?.Dispose();
            _retry = null;
            player = _player;
            _player = null;
        }
        _notifications.DefaultDeviceChanged -= OnDefaultDeviceChanged;
        _notifications.Dispose();
        if (player is not null)
        {
            player.PlaybackStopped -= OnStopped;
            player.Dispose();
        }
        _devices.Dispose();
    }

    /// <summary>NAudio's side of <see cref="PcmReader"/>: bytes asked for, 16-bit stereo given.</summary>
    private sealed class PullProvider(PcmReader read) : IWaveProvider
    {
        public WaveFormat WaveFormat => Format;

        public int Read(Span<byte> buffer)
        {
            int whole = buffer.Length / Format.BlockAlign * Format.BlockAlign;
            read(MemoryMarshal.Cast<byte, short>(buffer[..whole]));
            buffer[whole..].Clear();
            return buffer.Length;
        }
    }
}
```

- [ ] **Step 3: Build with warnings as errors**

Run: `dotnet build src/CouchLink.Audio -c Release`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If CA1416 fires on `WithProcessLoopback`, the call has moved outside the `OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)` block; put it back inside. If any other NAudio member name doesn't compile, check it against the spike findings above (the names there were verified by reflection on 3.1.0) and fix only the call, not the behaviour.

- [ ] **Step 4: Commit**

```bash
git add src/CouchLink.Audio/LoopbackSource.cs src/CouchLink.Audio/DefaultDevicePlayer.cs
git commit -m "feat(audio): WASAPI loopback capture and low-latency default-device output"
```

---

### Task 10: Audio line in the F2 overlay; audio dev switches

**Files:**
- Modify: `src/CouchLink.Core/Video/OverlayText.cs`
- Modify: `src/CouchLink.Core/DevOptions.cs`
- Modify: `src/CouchLink.Video/PlayerCore.cs`
- Modify: `src/CouchLink.Video/VideoPlayer.cs`
- Test: `tests/CouchLink.Core.Tests/OverlayTextTests.cs`, `tests/CouchLink.Core.Tests/DevOptionsTests.cs`, `tests/CouchLink.Video.Tests/PlayerCoreTests.cs`

**Interfaces:**
- Consumes: `AudioClientStats` (Task 6).
- Produces:
  - `OverlayText.Audio(AudioClientStats stats, string output)` -> two lines, or one line when no packet has come yet.
  - `OverlayText.Stats(StatsSample? sample, string decoder, string? audio = null)`: appends `\n{audio}` when given.
  - `DevOptions(bool TestPattern, string? SaveVideoPath, bool WindowedPlayer = false, bool TestTone = false, double AudioLossPercent = 0)`.
  - `PlayerCore(..., Action<string>? log = null, Func<string?>? audioLine = null)` and `VideoPlayer(PlayerOptions options, Func<VideoClientStats> stats, Action decodeFailed, Action closeRequested, Action<string>? log = null, Func<string?>? audioLine = null)`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/CouchLink.Core.Tests/OverlayTextTests.cs` (and `using CouchLink.Core.Audio;` at the top):

```csharp
    [Fact]
    public void The_audio_line_shows_buffer_repairs_and_the_output()
    {
        var s = new AudioClientStats(Packets: 1234, Repaired: 3, Concealed: 1, Late: 0, Discarded: 0, Underruns: 0,
            DecodeErrors: 0, DriftCorrections: 12, BufferMs: 15, Playing: true);

        Assert.Equal(
            "Audio buffer 15 ms  repaired 3  concealed 1  late 0\n" +
            "  1234 packets  drift 12  (Headphones, 2 ms)",
            OverlayText.Audio(s, "Headphones, 2 ms"));
        Assert.Equal("Audio: nothing from the host yet (muted: host is this PC)",
            OverlayText.Audio(default, "muted: host is this PC"));
    }

    [Fact]
    public void An_audio_line_goes_under_the_video_stats()
    {
        var sample = new StatsSample(60, 5, 0, 0, null, TimeSpan.Zero, null, TimeSpan.FromMilliseconds(3));

        Assert.EndsWith("Latency measuring...\nAudio x", OverlayText.Stats(sample, "software", "Audio x"));
        Assert.Equal("Collecting stats...\nAudio x", OverlayText.Stats(null, "software", "Audio x"));
        Assert.Equal("Collecting stats...", OverlayText.Stats(null, "software"));
    }
```

Add to `tests/CouchLink.Core.Tests/DevOptionsTests.cs`:

```csharp
    [Fact]
    public void Test_tone_and_audio_loss_are_dev_switches()
    {
        var options = DevOptions.Parse(["--test-tone", "--audio-loss=5"]);

        Assert.True(options.TestTone);
        Assert.Equal(5, options.AudioLossPercent);
        Assert.Equal(2.5, DevOptions.Parse(["--audio-loss=2.5"]).AudioLossPercent);
        Assert.Equal(100, DevOptions.Parse(["--audio-loss=250"]).AudioLossPercent);
        Assert.Equal(0, DevOptions.Parse(["--audio-loss=lots"]).AudioLossPercent);
        Assert.False(DevOptions.Parse([]).TestTone);
    }
```

In `tests/CouchLink.Video.Tests/PlayerCoreTests.cs`, add a field `private string? _audio;`, change the end of `Core()` from `_presenter, () => _stats, () => _decodeFailed++, _time);` to `_presenter, () => _stats, () => _decodeFailed++, _time, audioLine: () => _audio);`, and add:

```csharp
    [Fact]
    public void Stats_include_the_audio_line()
    {
        _audio = "Audio buffer 15 ms";
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.ShowStats = true;

        core.Run();

        Assert.Equal("Collecting stats...\nAudio buffer 15 ms", _presenter.Shown[^1].Stats);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~OverlayTextTests|FullyQualifiedName~DevOptionsTests"` and `dotnet test tests/CouchLink.Video.Tests --filter "FullyQualifiedName~PlayerCoreTests"`
Expected: build fails (`OverlayText.Audio`, `TestTone`, `audioLine` not found).

- [ ] **Step 3: Add the audio line to `OverlayText`**

In `src/CouchLink.Core/Video/OverlayText.cs`, add `using CouchLink.Core.Audio;` at the top. Replace the `Stats` method with:

```csharp
    public static string Stats(StatsSample? sample, string decoder, string? audio = null)
    {
        string video;
        if (sample is not { } s)
            video = "Collecting stats...";
        else
        {
            string latency = s.Latency is { } total && s.Network is { } network
                ? $"Latency ~{Ms(total)} ms (host {Ms(s.HostDelay)} + network {Ms(network)} + client {Ms(s.ClientDelay)})"
                : "Latency measuring...";
            video = $"{s.Fps:0} fps  {s.Mbps:0.0} Mbps  ({decoder})\n" +
                    $"Packet loss {s.LossPercent:0.0}%  FEC repairs {s.FecRepairs}/s\n" +
                    latency;
        }
        return audio is null ? video : $"{video}\n{audio}";
    }

    /// <summary>The client's audio, for the F2 overlay and the dev window; <paramref name="output"/> is the device or why it is silent.</summary>
    public static string Audio(AudioClientStats s, string output) =>
        s.Packets == 0
            ? $"Audio: nothing from the host yet ({output})"
            : $"Audio buffer {s.BufferMs:0} ms  repaired {s.Repaired}  concealed {s.Concealed}  late {s.Late}\n" +
              $"  {s.Packets} packets  drift {s.DriftCorrections}  ({output})";
```

- [ ] **Step 4: Add the dev switches**

Replace `src/CouchLink.Core/DevOptions.cs` with:

```csharp
using System.Globalization;

namespace CouchLink.Core;

/// <summary>
/// Developer switches on the command line: <c>--test-pattern</c> streams Plan 3's test pattern
/// instead of the screen (clients can't display it: it isn't H.264); <c>--save-video=&lt;file&gt;</c>
/// makes a client save the H.264 it receives; <c>--windowed-player</c> opens the client's video in a
/// 1280x720 window instead of fullscreen, for testing host and client on one PC;
/// <c>--test-tone</c> makes the host send a beep instead of its sound; <c>--audio-loss=&lt;percent&gt;</c>
/// makes a client drop that share of audio packets, to hear the loss recovery.
/// </summary>
public sealed record DevOptions(
    bool TestPattern,
    string? SaveVideoPath,
    bool WindowedPlayer = false,
    bool TestTone = false,
    double AudioLossPercent = 0)
{
    private const string SaveVideo = "--save-video=";
    private const string AudioLoss = "--audio-loss=";

    public static DevOptions Parse(IEnumerable<string> args)
    {
        bool testPattern = false;
        string? savePath = null;
        bool windowed = false;
        bool testTone = false;
        double audioLoss = 0;
        foreach (var arg in args)
        {
            if (arg == "--test-pattern")
                testPattern = true;
            else if (arg.StartsWith(SaveVideo, StringComparison.Ordinal) && arg.Length > SaveVideo.Length)
                savePath = arg[SaveVideo.Length..];
            else if (arg == "--windowed-player")
                windowed = true;
            else if (arg == "--test-tone")
                testTone = true;
            else if (arg.StartsWith(AudioLoss, StringComparison.Ordinal)
                     && double.TryParse(arg[AudioLoss.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var loss))
                audioLoss = Math.Clamp(loss, 0, 100);
        }
        return new DevOptions(testPattern, savePath, windowed, testTone, audioLoss);
    }
}
```

- [ ] **Step 5: Pass the audio line through the player**

In `src/CouchLink.Video/PlayerCore.cs`:
1. Add a field `private readonly Func<string?>? _audioLine;`.
2. Change the constructor signature to end `TimeProvider time, Action<string>? log = null, Func<string?>? audioLine = null)` and add `_audioLine = audioLine;` in its body.
3. Change `string? statsText = ShowStats ? OverlayText.Stats(_sample, DecoderName) : null;` to:

```csharp
        string? statsText = ShowStats ? OverlayText.Stats(_sample, DecoderName, _audioLine?.Invoke()) : null;
```

In `src/CouchLink.Video/VideoPlayer.cs`:
1. Change the constructor to:

```csharp
    public VideoPlayer(PlayerOptions options, Func<VideoClientStats> stats, Action decodeFailed,
        Action closeRequested, Action<string>? log = null, Func<string?>? audioLine = null)
    {
        _thread = new Thread(() => Run(options, stats, decodeFailed, closeRequested, log, audioLine))
```

(the rest of the constructor stays).
2. Change `private void Run(PlayerOptions options, Func<VideoClientStats> stats, Action decodeFailed,` / `Action closeRequested, Action<string>? log)` to end `Action closeRequested, Action<string>? log, Func<string?>? audioLine)`.
3. Change `presenter, stats, decodeFailed, TimeProvider.System, log);` (the `new PlayerCore(` call) to `presenter, stats, decodeFailed, TimeProvider.System, log, audioLine);`.

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests` and `dotnet test tests/CouchLink.Video.Tests`
Expected: PASS, all tests.

- [ ] **Step 7: Commit**

```bash
git add src/CouchLink.Core/Video/OverlayText.cs src/CouchLink.Core/DevOptions.cs src/CouchLink.Video tests/CouchLink.Core.Tests/OverlayTextTests.cs tests/CouchLink.Core.Tests/DevOptionsTests.cs tests/CouchLink.Video.Tests/PlayerCoreTests.cs
git commit -m "feat(video): audio line in the F2 overlay; --test-tone and --audio-loss switches"
```

---

### Task 11: The host streams its sound; the client plays it

**Files:**
- Modify: `src/CouchLink.App/CouchLink.App.csproj`
- Create: `src/CouchLink.App/HostAudio.cs`
- Create: `src/CouchLink.App/ClientAudioService.cs`
- Create: `src/CouchLink.App/ClientStreams.cs`
- Modify: `src/CouchLink.App/ClientVideoService.cs`
- Modify: `src/CouchLink.App/HostInputService.cs`
- Modify: `src/CouchLink.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: everything above. `AppServices.Options` (`DevOptions`), `AppServices.Log.Write(string)`, `VideoSender` (`IVideoPacketSender`), `Ports.Video`, `VideoReceiver.TryCreate(int port, out VideoReceiver? receiver, out string? error)`, `InputSender.SendKeyframeRequest`/`SendTimingPing`.
- Produces (app-internal): `HostAudio.Start(Action<Exception> onError)`, `HostAudio.ClientSeen(byte, IPAddress)`, `HostAudio.Describe()`; `ClientAudioService(IPAddress host)`, `.Receive(byte[])`, `.Describe()`; `ClientStreams.TryStart(IPAddress host, InputSender sender, PlayerOptions options, string? savePath, Action leave, out ClientStreams? streams, out string? error)`, `.PlayerWindow`, `.Describe()`; `HostInputService.DescribeStreams()` (replaces `DescribeVideo()`).

- [ ] **Step 1: Reference the audio project**

In `src/CouchLink.App/CouchLink.App.csproj`, add `<ProjectReference Include="..\CouchLink.Audio\CouchLink.Audio.csproj" />` before the `CouchLink.Core` reference.

- [ ] **Step 2: Write `HostAudio`**

`src/CouchLink.App/HostAudio.cs`:

```csharp
using System.Net;
using CouchLink.Audio;
using CouchLink.Core.Audio;
using CouchLink.Core.Net;

namespace CouchLink.App;

/// <summary>
/// The host's sound: loopback capture (or, with --test-tone, a beep) encoded once and sent to every
/// client video is sent to. If audio can't start, hosting still runs video and the pads, and
/// <see cref="Describe"/> says why.
/// </summary>
internal sealed class HostAudio : IDisposable
{
    private readonly AudioStreamer? _streamer;
    private readonly string _summary;

    private HostAudio(AudioStreamer? streamer, string summary)
    {
        _streamer = streamer;
        _summary = summary;
    }

    public static HostAudio Start(Action<Exception> onError)
    {
        IAudioSource? source = null;
        try
        {
            source = AppServices.Options.TestTone
                ? new TestToneSource()
                : LoopbackSource.Open(message => AppServices.Log.Write(message));
            var streamer = new AudioStreamer(source, new OpusAudioEncoder(), new VideoSender(), Ports.Video,
                TimeProvider.System, onError);
            var summary = $"Opus {AudioFormat.BitRate / 1000} kbps from {source.Description}";
            AppServices.Log.Write($"Audio: {summary}");
            return new HostAudio(streamer, summary);
        }
        catch (Exception e)
        {
            source?.Dispose();
            AppServices.Log.Write($"Audio unavailable: {e}");
            return new HostAudio(null, $"unavailable: {e.Message}");
        }
    }

    public void ClientSeen(byte slot, IPAddress from) => _streamer?.ClientSeen(slot, from);

    public string Describe()
    {
        var text = $"Audio: {_summary}";
        if (_streamer is { } streamer)
        {
            var s = streamer.Stats;
            text += $"\n  {s.Clients} client(s), {s.PacketsSent} packets, {s.BytesSent / 1_000_000.0:0.0} MB";
        }
        return text;
    }

    public void Dispose() => _streamer?.Dispose(); // the streamer owns the source, encoder and sender
}
```

- [ ] **Step 3: Write `ClientAudioService`**

`src/CouchLink.App/ClientAudioService.cs`:

```csharp
using System.Net;
using CouchLink.Audio;
using CouchLink.Core.Audio;
using CouchLink.Core.Net;
using CouchLink.Core.Video;

namespace CouchLink.App;

/// <summary>
/// Client side: plays the host's sound from the datagrams <see cref="Receive"/> is given. It never
/// fails to start. If the host is this same PC it does not play at all, because the host's capture
/// would pick up our playback and send it round again; it still receives and counts. With
/// --audio-loss=&lt;percent&gt; it drops that share of audio packets on purpose.
/// </summary>
internal sealed class ClientAudioService : IDisposable
{
    public const string SamePcMuted = "muted: host is this PC";

    private readonly AudioClient _client = new(new OpusAudioDecoder());
    private readonly DefaultDevicePlayer? _player;
    private readonly double _lossPercent = AppServices.Options.AudioLossPercent;

    public ClientAudioService(IPAddress host)
    {
        if (LocalAddress.IsThisPc(host))
            AppServices.Log.Write($"Audio: {SamePcMuted}");
        else
            _player = new DefaultDevicePlayer(_client.Read, message => AppServices.Log.Write(message));
    }

    /// <summary>An audio datagram, from the receive thread.</summary>
    public void Receive(byte[] datagram)
    {
        if (_lossPercent > 0 && Random.Shared.NextDouble() * 100 < _lossPercent)
            return;
        _client.Receive(datagram);
    }

    /// <summary>The audio line for the F2 overlay and the dev window.</summary>
    public string Describe() => OverlayText.Audio(_client.Stats, _player?.Status ?? SamePcMuted);

    public void Dispose()
    {
        _player?.Dispose(); // stops the device thread before the client goes
        _client.Dispose();
    }
}
```

- [ ] **Step 4: Let `ClientVideoService` use the shared socket**

Replace the constructor, `TryStart` and add `Receive` in `src/CouchLink.App/ClientVideoService.cs`, so the class reads:

```csharp
using System.IO;
using CouchLink.Core.Net;
using CouchLink.Core.Video;
using CouchLink.Video;

namespace CouchLink.App;

/// <summary>
/// Client side: shows the host's video, from the datagrams <see cref="Receive"/> is given, in the
/// player window (and, with --save-video=&lt;file&gt;, also saves the H.264). <c>leave</c> runs on the
/// player thread when the player asks to leave (Ctrl+Alt+Q, Alt+F4). <c>audioLine</c> is the F2
/// overlay's audio line. Dispose before the <see cref="InputSender"/> it sends keyframe requests
/// and timing pings through.
/// </summary>
internal sealed class ClientVideoService : IDisposable
{
    private readonly VideoClient _client;
    private readonly VideoPlayer _player;
    private readonly FileStream? _save;

    private ClientVideoService(InputSender sender, PlayerOptions options, string? savePath, Action leave, Func<string?> audioLine)
    {
        VideoClient? client = null;
        _player = new VideoPlayer(options, () => client?.Stats ?? default, () => client?.DecodeFailed(),
            leave, message => AppServices.Log.Write(message), audioLine);
        _save = savePath is null ? null : File.Create(savePath);
        client = new VideoClient(
            sender.SendKeyframeRequest, OnFrame, TimeProvider.System,
            e => AppServices.Log.Write($"Video error: {e}"), sender.SendTimingPing);
        _client = client;
    }

    public static bool TryStart(InputSender sender, PlayerOptions options, string? savePath, Action leave,
        Func<string?> audioLine, out ClientVideoService? service, out string? error)
    {
        try
        {
            service = new ClientVideoService(sender, options, savePath, leave, audioLine);
            error = null;
            return true;
        }
        catch (InvalidOperationException e)
        {
            service = null;
            error = e.Message;
            return false;
        }
    }

    public nint PlayerWindow => _player.WindowHandle;

    /// <summary>A video datagram, from the receive thread.</summary>
    public void Receive(byte[] datagram) => _client.Receive(datagram);
```

Keep `OnFrame`, `Describe` and `Dispose` as they are. (`_client.Dispose()` no longer has a receive loop to stop; the dispatcher's is stopped first by `ClientStreams`.)

- [ ] **Step 5: Write `ClientStreams`**

`src/CouchLink.App/ClientStreams.cs`:

```csharp
using System.Net;
using CouchLink.Core.Net;
using CouchLink.Video;

namespace CouchLink.App;

/// <summary>
/// Client side: the host's picture and sound, both arriving on UDP 47802 through one
/// <see cref="StreamDispatcher"/>. Video failing to start fails the join; audio never does.
/// Dispose before the <see cref="InputSender"/> that video sends through.
/// </summary>
internal sealed class ClientStreams : IDisposable
{
    private readonly StreamDispatcher _dispatcher;
    private readonly ClientVideoService _video;
    private readonly ClientAudioService _audio;

    private ClientStreams(StreamDispatcher dispatcher, ClientVideoService video, ClientAudioService audio)
    {
        _dispatcher = dispatcher;
        _video = video;
        _audio = audio;
    }

    public static bool TryStart(IPAddress host, InputSender sender, PlayerOptions options, string? savePath,
        Action leave, out ClientStreams? streams, out string? error)
    {
        streams = null;
        if (!VideoReceiver.TryCreate(Ports.Video, out var receiver, out error))
            return false;
        var audio = new ClientAudioService(host);
        if (!ClientVideoService.TryStart(sender, options, savePath, leave, audio.Describe, out var video, out error))
        {
            audio.Dispose();
            receiver!.Dispose();
            return false;
        }
        var dispatcher = new StreamDispatcher(receiver!, video!.Receive, audio.Receive,
            e => AppServices.Log.Write($"Receive error: {e}"));
        streams = new ClientStreams(dispatcher, video, audio);
        return true;
    }

    public nint PlayerWindow => _video.PlayerWindow;

    public string Describe() => $"{_video.Describe()}\n{_audio.Describe()}";

    public void Dispose()
    {
        _dispatcher.Dispose(); // stops the receive thread before video and audio go
        _video.Dispose();
        _audio.Dispose();
    }
}
```

- [ ] **Step 6: Start audio with hosting**

In `src/CouchLink.App/HostInputService.cs`:
1. Class summary: replace "and streams video to every client it hears from: the host screen (Plan 4) or, with --test-pattern, a test pattern." with "and streams video (the host screen, or --test-pattern) and sound (loopback, or --test-tone) to every client it hears from."
2. Add the field `private readonly HostAudio _audio;` after `private readonly HostVideo _video;`.
3. After `_video = HostVideo.Start(settings, OnVideoError);` add `_audio = HostAudio.Start(OnAudioError);`.
4. Replace `public string DescribeVideo() => _video.Describe();` with `public string DescribeStreams() => $"{_video.Describe()}\n{_audio.Describe()}";`.
5. In `OnInput`, after `_video.ClientSeen(packet.Slot, from);` add `_audio.ClientSeen(packet.Slot, from);` (both inside the `if`, so add braces):

```csharp
    private void OnInput(InputPacket packet, IPAddress from)
    {
        if (_pads.Handle(packet))
        {
            _video.ClientSeen(packet.Slot, from);
            _audio.ClientSeen(packet.Slot, from);
        }
    }
```

6. After `OnVideoError`, add:

```csharp
    private void OnAudioError(Exception e)
    {
        LastError = $"Audio: {e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Audio error: {e}");
    }
```

7. In `Dispose`, after `_video.Dispose();` add `_audio.Dispose();`.

- [ ] **Step 7: Use `ClientStreams` in the window**

In `src/CouchLink.App/MainWindow.xaml.cs`:
1. Field `private ClientVideoService? _video;` becomes `private ClientStreams? _streams;`, and every other `_video` in this file becomes `_streams` (in `InputAllowed`, `LeaveSession`, `UpdateStatus`, `OnClosed`).
2. In `OnJoin`, replace the `ClientVideoService.TryStart(...)` block with:

```csharp
        if (!ClientStreams.TryStart(ip, inputSender, playerOptions, AppServices.Options.SaveVideoPath,
                () => Dispatcher.InvokeAsync(LeaveSession), out _streams, out var streamError))
        {
            inputSender.Dispose();
            MessageBox.Show(this, streamError, "CouchLink");
            return;
        }
```

3. In `UpdateStatus`, change `{_host.DescribeVideo()}` to `{_host.DescribeStreams()}`.

Build: `dotnet build -c Release`. Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 8: Check it in the real app on one PC**

1. `dotnet run --project src/CouchLink.App -c Release -- --windowed-player`, click **Host**. The status shows `Audio: Opus 128 kbps from process loopback` and `0 client(s)`.
2. Start a second copy the same way, enter `127.0.0.1`, click **Join**. Press F2 in the player window.
3. Play any sound on the PC (a YouTube video). Expected: the host's audio line shows `1 client(s)` and its packet count rising by about 200 a second; the client's F2 overlay and dev window show `Audio buffer ... ms` with packets rising and `(muted: host is this PC)`; nothing echoes.
4. Close the client with Ctrl+Alt+Q, then the host. Open `%LOCALAPPDATA%\CouchLink\Logs` and check the latest log has `Audio: Opus 128 kbps from process loopback` and no `Audio error`.
5. Repeat step 1 with `--test-tone` added: the host status says `from test tone`.

If something fails here, fix it before committing and note the fix in the commit message.

- [ ] **Step 9: Run every test**

Run: `dotnet test -c Release`
Expected: PASS, all projects.

- [ ] **Step 10: Commit**

```bash
git add src/CouchLink.App
git commit -m "feat(app): host streams its sound and clients play it, muted when the host is the same PC"
```

---

### Task 12: Notices, README and the audio gate

**Files:**
- Modify: `THIRD-PARTY-NOTICES.md`
- Modify: `README.md`
- Modify: `docs/gate-results.md`

**Interfaces:** none (documentation).

- [ ] **Step 1: Add the new packages to the notices**

In `THIRD-PARTY-NOTICES.md`, under "Included in releases", add after the Vortice.Windows row:

```markdown
| NAudio (NAudio.Wasapi, NAudio.Core) 3.1.0 | CouchLink.App (host capture, client playback) | MIT | https://github.com/naudio/NAudio |
| Concentus 2.2.2 (managed Opus) | CouchLink.App (audio codec) | BSD-3-Clause (the Opus license) | https://github.com/lostromb/concentus |
```

- [ ] **Step 2: Update the README status**

In `README.md`, replace the status paragraph's first two sentences with:

```markdown
> **Status: early development.** Joining PCs see and hear the host with low latency (GPU capture,
> hardware H.264, FEC over UDP, GPU decode; WASAPI loopback and Opus for sound) and each gets its
> own virtual DualShock 4. The lobby and the join/approval flow are next.
```

(keep the "See the roadmap..." sentence that follows).

- [ ] **Step 3: Add the audio gate to `docs/gate-results.md`**

Append:

```markdown
## Audio (Plan 6)

Date: <fill in>
Host / client PCs and sound devices: <fill in>

| Check | Result |
|---|---|
| One PC, host + client (`--windowed-player`), YouTube on the host: client says "muted: host is this PC", its packet count rises, no echo | <pass/fail> |
| Two PCs, host `--test-tone`, client `--audio-loss=5`: no audible gaps; F2 "repaired" rises | <pass/fail> |
| Two PCs, 30 minutes of a game: F2 audio buffer stays within 5 ms of where it started; no dropouts | <pass/fail> |
| Unplug / switch the client's headphones mid-session: audio back within about 1 s | <pass/fail> |
| Sound feels in time with the picture (no visible lag between a hit and its sound) | <pass/fail> |
```

- [ ] **Step 4: Full build and test**

Run: `dotnet build -c Release` then `dotnet test -c Release --no-build`
Expected: build `0 Warning(s) 0 Error(s)`; every test project passes.

- [ ] **Step 5: Commit**

```bash
git add THIRD-PARTY-NOTICES.md README.md docs/gate-results.md
git commit -m "docs: audio notices, README status and the audio gate checklist"
```

The two-PC gate rows are for the project owner to run and fill in before release; they are not part of this plan's automated work.
