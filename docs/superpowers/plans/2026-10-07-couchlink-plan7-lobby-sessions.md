# CouchLink Plan 7: Lobby & Sessions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Friends find the host in a LAN list, ask to join, and play, with no IP addresses or slot numbers typed by hand. The host approves (or ticks "Allow everyone"), can kick, and a client that drops for a moment comes back to the same slot and the same 2K player. The real app replaces the dev window.

**Architecture:** All session decisions live in `CouchLink.Core` as socket-free state machines driven by a `TimeProvider`: `HostSession` (slots, pending asks, 60 s reservations, heartbeat deadlines) and `ClientSession` (connecting, waiting, playing, reconnecting, ended). Thin network edges carry them: `DiscoveryBroadcaster`/`DiscoveryListener` on UDP 47800 (wire type 7) and `SessionServer`/`SessionClient` on TCP 47801 (length-prefixed frames, wire types 8-14). The session becomes the single source of truth for who is in: it plugs, holds and unplugs pads (`PadManager.Plug/Hold/Unplug`, input accepted only from the slot's IP) and sets the video and audio targets (`AddTarget/RemoveTarget`, replacing input sniffing). The WPF app becomes one window swapping Start, Host lobby, Join list and Session views, plus a topmost approval popup.

**Tech Stack:** C# / .NET 10, WPF, `System.Net.Sockets` (`TcpListener`, `TcpClient`, `UdpClient`), `System.Threading.Channels`, xUnit 2.9.3, Microsoft.Extensions.TimeProvider.Testing.

**Spec:** `docs/superpowers/specs/2026-10-07-couchlink-lobby-sessions-design.md` (all of it). Main design: `docs/superpowers/specs/2026-10-05-couchlink-design.md` sections 3, 4.1-4.5.

**Builds on:** `main` @ 874e87c (Plan 6 audio merged). Branch: `plan7-lobby`. Issues: #21, #22, #23.

## Global Constraints

- C#, .NET 10. `CouchLink.Core` stays `net10.0` with no Windows-only APIs (`System.Net.NetworkInformation` is fine). `TreatWarningsAsErrors` everywhere.
- Commits are signed. Conventional Commits. **No Claude attribution trailers** in commit messages or PR text.
- Ports: **UDP 47800** discovery (host only *sends* to it; clients listen), **TCP 47801** session, UDP 47802 video+audio (unchanged), UDP 47803 input, keyframe requests, timing pings (unchanged).
- Wire: `Wire.Version` stays **1**. New types: **7** `HostAnnounce`, **8** `JoinRequest`, **9** `Accepted`, **10** `Denied`, **11** `Heartbeat`, **12** `Leave`, **13** `Kicked`, **14** `HostEnded`. Little-endian.
- Names: `Environment.MachineName`, UTF-8, **at most 63 bytes**, cut at a character boundary.
- Session frames: `u16` length (**4-4096**, bytes that follow) + 4-byte `Wire` header + body.
- Timings: announce every **1 s**, host dropped from the list after **3 s**; heartbeat every **1 s** both ways, **5 s** silence = gone; popup auto-deny after **30 s**; reservation **60 s**; client reconnect window **10 s**, one attempt per second; TCP connect timeout **3 s**.
- Capacity **9** (slots P2-P10). Lowest free slot first. Join list shows `"{name} · {players + 1}/{capacity + 1} players"`.
- A reserved slot keeps its pad **plugged in at neutral**; input to it is ignored until it is active again. Unplug only on leave, kick, expiry or stop.
- A reserved slot is reclaimed by **PC name alone** (any IP). An active slot is taken over only by the same **name and IP**.
- Client messages (exact text): `Couldn't reach {host}.`, `Request denied.`, `Host is full.`, `The host didn't answer.`, `The host couldn't add a controller for you.`, `Lost the host.`, `You were removed by the host.`, `Host ended the session.`
- Exceptions on socket, timer and read-loop threads are caught and logged; they never end the process.
- Dev switches (`--test-pattern`, `--test-tone`, `--windowed-player`, `--save-video=`, `--audio-loss=`) keep working.

## Review Focus

1. **A host with two network adapters, or a one-PC test, announces itself on several addresses.** The join list shows it once, and a loopback announce never replaces a LAN address. Pinned in Task 5 (`A_host_seen_on_two_addresses_is_listed_once`, `Loopback_never_replaces_a_lan_address`).
2. **A client crashes and restarts within 5 s, before the host notices the old connection is dead.** It gets its own slot back with no popup, and the old connection's late disconnect doesn't reserve the slot under it. Pinned in Task 6 (`Same_name_and_ip_takes_over_an_active_slot`, `The_old_connections_late_disconnect_changes_nothing`).
3. **The host clicks Allow after the last slot was filled by auto-allow, or after the client gave up.** No tenth pad, no exception: the client hears "Host is full." or nothing happens. Pinned in Task 6 (`Allow_after_the_host_filled_up_denies_full`, `Allow_for_a_connection_that_is_gone_does_nothing`).
4. **Junk or a different program connects to TCP 47801.** Only that connection is closed; players already in keep playing. Pinned in Task 4 (`Decode_throws_only_InvalidDataException_for_random_frames`) and Task 8 (`Garbage_closes_only_that_connection`).
5. **The client's network drops mid-game and comes back within 10 s.** Video and input keep running, the player shows "Reconnecting...", and the same slot comes back without a second `StartPlaying`. Pinned in Task 7 (`Silence_while_playing_reconnects_and_resumes_the_same_slot`) and Task 9 (`Session_status_overrides_the_picture_status`).

---

## File Structure

```
src/CouchLink.Core/Video/
  StreamTargets.cs           (rewrite) explicit slot -> endpoint set, Add/Remove/Current
  VideoStreamer.cs           (modify) AddTarget (forces keyframe) / RemoveTarget replace ClientSeen
src/CouchLink.Core/Audio/
  AudioStreamer.cs           (modify) AddTarget / RemoveTarget replace ClientSeen
src/CouchLink.Core/Pads/
  PadManager.cs              (modify) Plug/Hold/Unplug/IsPlugged; Handle(packet, from)
src/CouchLink.Core/Protocol/
  Wire.cs                    (modify) types 7-14
  PcName.cs                  63-byte UTF-8 names: Clip, Encode, TryDecode
  HostAnnounce.cs            type 7
  SessionMessage.cs          session message record, DenyReason, SessionMessageType
  SessionFraming.cs          encode, decode, ReadAsync over a Stream
src/CouchLink.Core/Net/
  Ports.cs                   (modify) Discovery 47800, Session 47801
  BroadcastAddresses.cs      directed broadcast per IPv4 interface (+ loopback)
  DiscoveryBroadcaster.cs    host: announce every second
  DiscoveryListener.cs       client: receive announces on 47800
  SessionConnection.cs       one TCP connection: stream + queued writer
  SessionServer.cs           host: TCP 47801, one HostSession under one lock
  SessionClient.cs           client: one connection, heartbeat, generations
src/CouchLink.Core/Session/
  HostList.cs                hosts found, by name, expire after 3 s
  HostSession.cs             host state machine (+ IHostEffects, IHostSessionEffects, PlayerInfo)
  ClientSession.cs           client state machine (+ IClientSessionEffects, ClientState)
src/CouchLink.Core/Video/
  OverlayText.cs             (modify) Reconnecting text; Status takes a session line
src/CouchLink.Video/
  PlayerCore.cs, VideoPlayer.cs  (modify) optional sessionStatus provider
src/CouchLink.App/
  HostService.cs             (replaces HostInputService.cs) pads, session server, broadcaster, video, audio
  HostVideo.cs, HostAudio.cs (modify) AddTarget/RemoveTarget
  ApprovalPopup.cs           topmost "PC-07 wants to join" with countdown and taskbar flash
  ClientPlay.cs              (from MainWindow) streams + raw input + input loop for one slot
  ClientSessionService.cs    ClientSession + SessionClient + tick timer, marshals to the UI
  ClientStreams.cs, ClientVideoService.cs (modify) pass the session status line to the player
  MainWindow.xaml(.cs)       (rewrite) navigation between views
  Views/StartView.xaml(.cs)
  Views/HostLobbyView.xaml(.cs)
  Views/JoinListView.xaml(.cs)
  Views/SessionView.xaml(.cs)
tests/CouchLink.Core.Tests/
  StreamTargetsTests.cs (rewrite), VideoStreamerTargetsTests.cs, PadManagerTests.cs (rewrite),
  PcNameTests.cs, HostAnnounceTests.cs, SessionFramingTests.cs, BroadcastAddressesTests.cs,
  HostListTests.cs, DiscoveryLoopbackTests.cs, HostSessionTests.cs, ClientSessionTests.cs,
  SessionLoopbackTests.cs, OverlayTextTests.cs (modify)
tests/CouchLink.Video.Tests/
  PlayerCoreTests.cs (modify)
docs/
  gate-results.md, README.md, main design spec, lobby spec (small corrections)
```

Test commands: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~<Class>"`. Whole build: `dotnet build -c Release` (expect `0 Warning(s) 0 Error(s)`).

---

### Task 1: Explicit stream targets

The session will decide who gets video and audio. Replace "learned from input packets" with explicit `AddTarget`/`RemoveTarget`. The dev window keeps working through a small shim in `HostInputService` (removed in Task 10).

**Files:**
- Rewrite: `src/CouchLink.Core/Video/StreamTargets.cs`
- Modify: `src/CouchLink.Core/Video/VideoStreamer.cs`, `src/CouchLink.Core/Audio/AudioStreamer.cs`
- Modify: `src/CouchLink.App/HostVideo.cs`, `src/CouchLink.App/HostAudio.cs`, `src/CouchLink.App/HostInputService.cs`
- Rewrite: `tests/CouchLink.Core.Tests/StreamTargetsTests.cs`
- Create: `tests/CouchLink.Core.Tests/VideoStreamerTargetsTests.cs`
- Modify: `tests/CouchLink.Core.Tests/AudioStreamerTests.cs`, `VideoLoopbackTests.cs`, `VideoStreamerTimingTests.cs`

**Interfaces:**
- Produces: `StreamTargets(int port)` with `void Add(byte slot, IPAddress address)`, `bool Remove(byte slot)`, `IReadOnlyList<IPEndPoint> Current()`. `VideoStreamer.AddTarget(byte slot, IPAddress address)` (forces a keyframe), `VideoStreamer.RemoveTarget(byte slot)`, same two on `AudioStreamer` (no keyframe). `HostVideo.AddTarget/RemoveTarget`, `HostAudio.AddTarget/RemoveTarget` with the same signatures. `ClientSeen` is gone everywhere.

- [ ] **Step 1: Write the failing tests**

Replace `tests/CouchLink.Core.Tests/StreamTargetsTests.cs`:

```csharp
using System.Net;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class StreamTargetsTests
{
    private const int Port = 47802;
    private static readonly IPAddress A = IPAddress.Parse("192.168.1.20");
    private static readonly IPAddress B = IPAddress.Parse("192.168.1.21");

    [Fact]
    public void An_added_client_gets_its_address_on_the_port()
    {
        var targets = new StreamTargets(Port);
        targets.Add(2, A);
        Assert.Equal(new IPEndPoint(A, Port), Assert.Single(targets.Current()));
    }

    [Fact]
    public void Adding_a_slot_again_with_a_new_address_replaces_it()
    {
        var targets = new StreamTargets(Port);
        targets.Add(2, A);
        targets.Add(2, B);
        Assert.Equal(new IPEndPoint(B, Port), Assert.Single(targets.Current()));
    }

    [Fact]
    public void A_removed_slot_gets_nothing()
    {
        var targets = new StreamTargets(Port);
        targets.Add(2, A);
        Assert.True(targets.Remove(2));
        Assert.False(targets.Remove(2));
        Assert.Empty(targets.Current());
    }

    [Fact]
    public void Two_slots_on_one_PC_get_one_copy_until_both_are_removed()
    {
        var targets = new StreamTargets(Port);
        targets.Add(2, A);
        targets.Add(3, A);
        Assert.Single(targets.Current());
        targets.Remove(2);
        Assert.Single(targets.Current());
        targets.Remove(3);
        Assert.Empty(targets.Current());
    }
}
```

Create `tests/CouchLink.Core.Tests/VideoStreamerTargetsTests.cs`:

```csharp
using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Video;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class VideoStreamerTargetsTests
{
    private static readonly IPAddress A = IPAddress.Parse("10.0.0.2");
    private static readonly IPAddress B = IPAddress.Parse("10.0.0.3");

    private sealed class RecordingSender : IVideoPacketSender
    {
        private readonly List<IPEndPoint> _targets = [];

        public int Count { get { lock (_targets) return _targets.Count; } }

        public int SentTo(IPAddress address)
        {
            lock (_targets)
                return _targets.Count(t => t.Address.Equals(address));
        }

        public void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets)
        {
            lock (_targets)
                foreach (var _ in packets)
                    _targets.AddRange(targets);
        }

        public void Dispose() { }
    }

    /// <summary>A frame every 5 ms; counts the frames the streamer asked to be keyframes.</summary>
    private sealed class CountingSource : IEncodedVideoSource
    {
        private int _forced;

        public int Forced => Volatile.Read(ref _forced);

        public bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame)
        {
            Thread.Sleep(5);
            if (forceKeyframe)
                Interlocked.Increment(ref _forced);
            frame = new EncodedFrame(new byte[100], forceKeyframe);
            return true;
        }

        public void Dispose() { }
    }

    [Fact]
    public async Task Nothing_is_sent_until_a_target_is_added_and_adding_one_forces_a_keyframe()
    {
        var source = new CountingSource();
        var sender = new RecordingSender();
        using var streamer = new VideoStreamer(source, sender, 47802, TimeProvider.System);
        await Task.Delay(100);
        Assert.Equal(0, sender.Count);
        int forcedBefore = source.Forced;

        streamer.AddTarget(2, A);

        await Until(() => sender.SentTo(A) > 0);
        await Until(() => source.Forced > forcedBefore);
    }

    [Fact]
    public async Task A_removed_target_gets_nothing_more()
    {
        var sender = new RecordingSender();
        using var streamer = new VideoStreamer(new CountingSource(), sender, 47802, TimeProvider.System);
        streamer.AddTarget(2, A);
        streamer.AddTarget(3, B);
        await Until(() => sender.SentTo(B) > 0);

        streamer.RemoveTarget(3);
        await Task.Delay(50); // a frame already being sent may still reach B
        int toB = sender.SentTo(B);
        int toA = sender.SentTo(A);
        await Until(() => sender.SentTo(A) > toA + 5);

        Assert.Equal(toB, sender.SentTo(B));
        Assert.Equal(1, streamer.Stats.Clients);
    }
}
```

In `tests/CouchLink.Core.Tests/AudioStreamerTests.cs` replace every `streamer.ClientSeen(` with `streamer.AddTarget(` and add at the end of the class:

```csharp
    [Fact]
    public async Task A_removed_client_gets_nothing_more()
    {
        using var streamer = Streamer();
        streamer.AddTarget(2, A);
        streamer.AddTarget(3, B);
        _source.Add(1);
        await Until(() => _sender.Count == 2);

        streamer.RemoveTarget(3);
        _source.Add(2);
        await Until(() => _sender.Count == 3);

        Assert.Equal(A, _sender.Parsed()[^1].Target.Address);
    }
```

In `tests/CouchLink.Core.Tests/VideoStreamerTimingTests.cs` replace `streamer.ClientSeen(2, IPAddress.Loopback);` with `streamer.AddTarget(2, IPAddress.Loopback);`.

In `tests/CouchLink.Core.Tests/VideoLoopbackTests.cs` (the `Rig` class): delete the `private readonly Timer _keepAlive;` field and the `_keepAlive.Dispose();` line, and replace

```csharp
            // A real client's input packets keep it in the host's targets; do the same here.
            _keepAlive = new Timer(_ => Streamer.ClientSeen(2, IPAddress.Loopback), null, 0, 100);
```

with

```csharp
            Streamer.AddTarget(2, IPAddress.Loopback); // the session would do this on Accept
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~StreamTargetsTests|FullyQualifiedName~VideoStreamerTargetsTests|FullyQualifiedName~AudioStreamerTests"`
Expected: build FAILS: `'StreamTargets' does not contain a definition for 'Add'`, `'VideoStreamer' does not contain a definition for 'AddTarget'`.

- [ ] **Step 3: Implement**

Replace `src/CouchLink.Core/Video/StreamTargets.cs`:

```csharp
using System.Net;

namespace CouchLink.Core.Video;

/// <summary>
/// Host side: the clients to stream to (slot -> address), set by the session as clients are let in,
/// leave, are kicked or go silent. Two slots on one PC get one copy. Not thread-safe.
/// </summary>
public sealed class StreamTargets(int port)
{
    private readonly Dictionary<byte, IPEndPoint> _targets = [];

    public void Add(byte slot, IPAddress address) => _targets[slot] = new IPEndPoint(address, port);

    public bool Remove(byte slot) => _targets.Remove(slot);

    /// <summary>One entry per endpoint.</summary>
    public IReadOnlyList<IPEndPoint> Current() => _targets.Values.Distinct().ToList();
}
```

In `src/CouchLink.Core/Video/VideoStreamer.cs`:

1. In the class summary replace `Clients are learned from their input packets (<see cref="ClientSeen"/>); a new client or a keyframe request forces a keyframe.` with `Clients are added and removed by the session (<see cref="AddTarget"/>); a new client or a keyframe request forces a keyframe.`
2. Replace the `ClientSeen` method with:

```csharp
    /// <summary>The session let a client in (or back in): it gets video from the next frame, starting with a keyframe.</summary>
    public void AddTarget(byte slot, IPAddress address)
    {
        lock (_gate)
        {
            _targets.Add(slot, address);
            _keyframes.Request();
        }
    }

    /// <summary>The client left, was kicked or went silent: no more video for it.</summary>
    public void RemoveTarget(byte slot)
    {
        lock (_gate)
            _targets.Remove(slot);
    }
```

3. In `StreamOneFrame` replace `targets = _targets.Current(Now);` with `targets = _targets.Current();`.

In `src/CouchLink.Core/Audio/AudioStreamer.cs`:

1. In the class summary replace `Clients are learned from their input packets (<see cref="ClientSeen"/>), as for video.` with `Clients are added and removed by the session (<see cref="AddTarget"/>), as for video.`
2. Replace the `ClientSeen` method with:

```csharp
    /// <summary>The session let a client in: it gets audio from the next frame.</summary>
    public void AddTarget(byte slot, IPAddress address)
    {
        lock (_gate)
            _targets.Add(slot, address);
    }

    /// <summary>The client left, was kicked or went silent: no more audio for it.</summary>
    public void RemoveTarget(byte slot)
    {
        lock (_gate)
            _targets.Remove(slot);
    }
```

3. In `StreamOneFrame` replace `targets = _targets.Current(Now);` with `targets = _targets.Current();`.
4. `AudioStreamer` no longer needs a clock: delete the `Now` property, the `_time` and `_start` fields and their assignments, and the `TimeProvider time` constructor parameter. Callers drop that argument: in `src/CouchLink.App/HostAudio.cs` the call becomes `new AudioStreamer(source, new OpusAudioEncoder(), new VideoSender(), Ports.Video, onError)`, and in `tests/CouchLink.Core.Tests/AudioStreamerTests.cs` the `Streamer` helper becomes `new(_source, new TinyEncoder(), _sender, 47802, onError)`. (A `_time` field assigned but never read would trip CS0414 under warnings-as-errors.)

In `src/CouchLink.App/HostVideo.cs` replace the `ClientSeen` line with:

```csharp
    public void AddTarget(byte slot, IPAddress address) => _streamer?.AddTarget(slot, address);

    public void RemoveTarget(byte slot) => _streamer?.RemoveTarget(slot);
```

In `src/CouchLink.App/HostAudio.cs` replace the `ClientSeen` line with:

```csharp
    public void AddTarget(byte slot, IPAddress address) => _streamer?.AddTarget(slot, address);

    public void RemoveTarget(byte slot) => _streamer?.RemoveTarget(slot);
```

and in its summary replace `sent to every client video is sent to` with `sent to every client the session lets in`.

In `src/CouchLink.App/HostInputService.cs` add a field and replace `OnInput`:

```csharp
    // Interim until the session channel (Plan 7 Task 10): a slot/address heard from once becomes a target.
    private readonly HashSet<(byte Slot, IPAddress Address)> _targets = [];
```

```csharp
    private void OnInput(InputPacket packet, IPAddress from)
    {
        if (!_pads.Handle(packet))
            return;
        lock (_targets)
        {
            if (!_targets.Add((packet.Slot, from)))
                return;
        }
        _video.AddTarget(packet.Slot, from);
        _audio.AddTarget(packet.Slot, from);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests`
Expected: all PASS (including `VideoLoopbackTests` and `AudioStreamerTests`).
Run: `dotnet build -c Release`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add -A src/CouchLink.Core src/CouchLink.App tests/CouchLink.Core.Tests
git commit -m "feat(core): video and audio targets are set explicitly, not learned from input"
```

---

### Task 2: Pads are plugged by the session and bound to the client's IP

**Files:**
- Modify: `src/CouchLink.Core/Pads/PadManager.cs`
- Modify: `src/CouchLink.App/HostInputService.cs`
- Rewrite: `tests/CouchLink.Core.Tests/PadManagerTests.cs`

**Interfaces:**
- Produces on `PadManager`: `bool Plug(byte slot, IPAddress address)` (creates the pad if the slot has none, or rebinds an existing one; false for a slot outside 2-10; a factory exception propagates), `void Hold(byte slot)` (neutral now, input ignored, pad stays plugged), `void Unplug(byte slot)` (disposes the pad), `bool IsPlugged(byte slot)`, `bool Handle(InputPacket packet, IPAddress from)` (false if the slot is not plugged, held, or bound to another address). `Count`, `ReleaseStale`, `Dispose`, `FirstSlot`, `LastSlot`, `StaleAfter`, `CheckInterval` unchanged.

- [ ] **Step 1: Write the failing tests**

Replace `tests/CouchLink.Core.Tests/PadManagerTests.cs`:

```csharp
using System.Net;
using CouchLink.Core.Input;
using CouchLink.Core.Pads;
using CouchLink.Core.Protocol;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public class PadManagerTests
{
    private static readonly IPAddress A = IPAddress.Parse("10.0.0.7");
    private static readonly IPAddress B = IPAddress.Parse("10.0.0.8");

    private readonly FakePadFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly PadManager _manager;

    private static readonly PadState Pressed = PadState.Neutral with { Buttons = PadButtons.Cross, LX = 255 };

    public PadManagerTests() => _manager = new PadManager(_factory, _time);

    private static InputPacket Packet(byte slot, uint seq, PadState? state = null, uint epoch = 1) =>
        new(slot, epoch, seq, state ?? Pressed);

    [Fact]
    public void Plug_creates_a_pad_and_its_input_is_applied()
    {
        Assert.True(_manager.Plug(3, A));
        Assert.True(_manager.IsPlugged(3));
        Assert.True(_manager.Handle(Packet(3, 1), A));
        var pad = Assert.Single(_factory.Created);
        Assert.Equal(Pressed, pad.Applied[^1]);
        Assert.Equal(1, _manager.Count);
    }

    [Fact]
    public void Input_for_a_slot_that_is_not_plugged_is_ignored_and_creates_nothing()
    {
        Assert.False(_manager.Handle(Packet(3, 1), A));
        Assert.Empty(_factory.Created);
        Assert.False(_manager.IsPlugged(3));
    }

    [Fact]
    public void Input_from_another_address_is_ignored()
    {
        _manager.Plug(3, A);
        Assert.False(_manager.Handle(Packet(3, 1), B));
        Assert.True(_manager.Handle(Packet(3, 1), A)); // the ignored packet did not use up sequence 1
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(255)]
    public void Slot_outside_2_to_10_cannot_be_plugged(byte slot)
    {
        Assert.False(_manager.Plug(slot, A));
        Assert.Empty(_factory.Created);
    }

    [Fact]
    public void Plugging_a_slot_again_rebinds_it_without_a_new_pad()
    {
        _manager.Plug(2, A);
        _manager.Plug(2, B);
        Assert.Single(_factory.Created);
        Assert.False(_manager.Handle(Packet(2, 1), A));
        Assert.True(_manager.Handle(Packet(2, 1), B));
    }

    [Fact]
    public void Hold_centers_the_pad_keeps_it_plugged_and_ignores_input()
    {
        _manager.Plug(2, A);
        _manager.Handle(Packet(2, 1), A);
        var pad = _factory.Created[0];

        _manager.Hold(2);

        Assert.Equal(PadState.Neutral, pad.Applied[^1]);
        Assert.False(pad.Disposed);
        Assert.True(_manager.IsPlugged(2));
        Assert.Equal(1, _manager.Count);
        Assert.False(_manager.Handle(Packet(2, 2), A));
    }

    [Fact]
    public void Plug_after_hold_resumes_on_the_same_pad_from_a_new_address()
    {
        _manager.Plug(2, A);
        _manager.Hold(2);
        _manager.Plug(2, B);
        Assert.True(_manager.Handle(Packet(2, 1, epoch: 9), B));
        Assert.Single(_factory.Created);
    }

    [Fact]
    public void Unplug_disposes_the_pad_and_frees_the_slot()
    {
        _manager.Plug(2, A);
        _manager.Unplug(2);
        Assert.True(_factory.Created[0].Disposed);
        Assert.False(_manager.IsPlugged(2));
        Assert.Equal(0, _manager.Count);
        _manager.Unplug(2); // twice is fine
    }

    [Fact]
    public void All_nine_slots_get_separate_pads()
    {
        for (byte slot = 2; slot <= 10; slot++)
            Assert.True(_manager.Plug(slot, A));
        Assert.Equal(9, _factory.Created.Count);
        Assert.Equal(9, _manager.Count);
    }

    [Fact]
    public void Old_or_duplicate_packets_are_not_applied()
    {
        _manager.Plug(2, A);
        _manager.Handle(Packet(2, 5), A);
        Assert.False(_manager.Handle(Packet(2, 5, PadState.Neutral), A));
        Assert.False(_manager.Handle(Packet(2, 4, PadState.Neutral), A));
        Assert.Equal(Pressed, _factory.Created[0].Applied[^1]);
    }

    [Fact]
    public void Restarted_client_is_accepted_on_same_pad()
    {
        _manager.Plug(2, A);
        _manager.Handle(Packet(2, 900, epoch: 1), A);
        Assert.True(_manager.Handle(Packet(2, 1, PadState.Neutral, epoch: 2), A));
        var pad = Assert.Single(_factory.Created);
        Assert.Equal(PadState.Neutral, pad.Applied[^1]);
    }

    [Fact]
    public void Silent_pad_is_released_after_500ms_exactly_once()
    {
        _manager.Plug(2, A);
        _manager.Handle(Packet(2, 1), A);
        var pad = _factory.Created[0];

        _time.Advance(TimeSpan.FromMilliseconds(499));
        _manager.ReleaseStale();
        Assert.Equal(Pressed, pad.Applied[^1]);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        _manager.ReleaseStale();
        Assert.Equal(PadState.Neutral, pad.Applied[^1]);

        int count = pad.Applied.Count;
        _time.Advance(TimeSpan.FromSeconds(5));
        _manager.ReleaseStale();
        Assert.Equal(count, pad.Applied.Count);
    }

    [Fact]
    public void Silent_pad_is_neutral_within_525ms_when_checked_every_CheckInterval()
    {
        var sincePacket = TimeSpan.Zero;
        void Step(TimeSpan by)
        {
            _time.Advance(by);
            sincePacket += by;
        }

        _manager.Plug(2, A);
        Step(TimeSpan.FromMilliseconds(1));
        _manager.Handle(Packet(2, 1), A);
        sincePacket = TimeSpan.Zero;
        var pad = _factory.Created[0];

        Step(PadManager.CheckInterval - TimeSpan.FromMilliseconds(1));
        _manager.ReleaseStale();
        while (pad.Applied[^1] != PadState.Neutral)
        {
            Step(PadManager.CheckInterval);
            _manager.ReleaseStale();
            Assert.True(sincePacket < TimeSpan.FromSeconds(2), "pad was never released");
        }

        Assert.InRange(sincePacket, PadManager.StaleAfter, TimeSpan.FromMilliseconds(525));
    }

    [Fact]
    public void Active_pad_is_not_released()
    {
        _manager.Plug(2, A);
        _manager.Handle(Packet(2, 1), A);
        for (uint seq = 2; seq < 100; seq++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(8));
            _manager.Handle(Packet(2, seq), A);
            _manager.ReleaseStale();
        }
        Assert.DoesNotContain(PadState.Neutral, _factory.Created[0].Applied);
    }

    [Fact]
    public void Dispose_unplugs_all_pads()
    {
        _manager.Plug(2, A);
        _manager.Plug(3, B);
        _manager.Dispose();
        Assert.All(_factory.Created, p => Assert.True(p.Disposed));
        Assert.Equal(0, _manager.Count);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~PadManagerTests"`
Expected: build FAILS: `'PadManager' does not contain a definition for 'Plug'`.

- [ ] **Step 3: Implement**

Replace `src/CouchLink.Core/Pads/PadManager.cs`:

```csharp
using System.Net;
using CouchLink.Core.Input;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Pads;

/// <summary>
/// Owns the host's virtual pads: one per slot (P2..P10), plugged in by the session when a client is
/// let in and bound to that client's address. Input for a slot is applied only from its address. A
/// held slot (client gone, waiting for it to rejoin) stays plugged in at neutral and ignores input,
/// so the game keeps the player. A pad that stops receiving packets is released to Neutral so a
/// crashed client can't leave a player running forever.
/// </summary>
public sealed class PadManager : IDisposable
{
    public const byte FirstSlot = 2;
    public const byte LastSlot = 10;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(500);

    /// <summary>How often <see cref="ReleaseStale"/> should run; a silent pad is released within StaleAfter + CheckInterval.</summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(25);

    private sealed class Entry(IVirtualPad pad)
    {
        public IVirtualPad Pad { get; } = pad;
        public IPAddress? Address { get; set; } // null while held
        public long LastSeen { get; set; }
        public bool IsNeutral { get; set; } = true;
    }

    private readonly IVirtualPadFactory _factory;
    private readonly TimeProvider _time;
    private readonly SequenceFilter _filter = new();
    private readonly Dictionary<byte, Entry> _pads = [];
    private readonly Lock _gate = new();

    public PadManager(IVirtualPadFactory factory, TimeProvider time)
    {
        _factory = factory;
        _time = time;
    }

    public int Count
    {
        get { lock (_gate) return _pads.Count; }
    }

    public bool IsPlugged(byte slot)
    {
        lock (_gate)
            return _pads.ContainsKey(slot);
    }

    /// <summary>Plugs in a pad for the slot (or keeps its pad) and binds it to the client's address. False for a slot outside 2-10.</summary>
    public bool Plug(byte slot, IPAddress address)
    {
        if (slot is < FirstSlot or > LastSlot)
            return false;

        lock (_gate)
        {
            if (!_pads.TryGetValue(slot, out var entry))
            {
                entry = new Entry(_factory.Create());
                _pads[slot] = entry;
            }
            entry.Address = address;
            return true;
        }
    }

    /// <summary>The client is gone for now: center the pad, keep it plugged in, ignore input until <see cref="Plug"/>.</summary>
    public void Hold(byte slot)
    {
        lock (_gate)
        {
            if (!_pads.TryGetValue(slot, out var entry))
                return;
            entry.Address = null;
            entry.Pad.Apply(PadState.Neutral);
            entry.IsNeutral = true;
        }
    }

    public void Unplug(byte slot)
    {
        lock (_gate)
        {
            if (_pads.Remove(slot, out var entry))
                entry.Pad.Dispose();
        }
    }

    /// <summary>Applies a packet from <paramref name="from"/>. Returns false if it was ignored.</summary>
    public bool Handle(InputPacket packet, IPAddress from)
    {
        lock (_gate)
        {
            if (!_pads.TryGetValue(packet.Slot, out var entry) || entry.Address is null || !entry.Address.Equals(from))
                return false;
            if (!_filter.Accept(packet.Slot, packet.Epoch, packet.Sequence))
                return false;

            entry.Pad.Apply(packet.State);
            entry.LastSeen = _time.GetTimestamp();
            entry.IsNeutral = packet.State == PadState.Neutral;
            return true;
        }
    }

    /// <summary>Call every <see cref="CheckInterval"/>.</summary>
    public void ReleaseStale()
    {
        lock (_gate)
        {
            foreach (var entry in _pads.Values)
            {
                if (entry.IsNeutral || _time.GetElapsedTime(entry.LastSeen) < StaleAfter)
                    continue;
                entry.Pad.Apply(PadState.Neutral);
                entry.IsNeutral = true;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _pads.Values)
                entry.Pad.Dispose();
            _pads.Clear();
        }
    }
}
```

In `src/CouchLink.App/HostInputService.cs` replace the first lines of `OnInput`:

```csharp
        if (!_pads.Handle(packet))
            return;
```

with

```csharp
        // Interim until the session channel (Plan 7 Task 10): the first packet for a slot plugs its pad.
        if (!_pads.IsPlugged(packet.Slot) && !_pads.Plug(packet.Slot, from))
            return;
        if (!_pads.Handle(packet, from))
            return;
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~PadManagerTests"`
Expected: PASS (18 tests).
Run: `dotnet build -c Release`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Pads/PadManager.cs src/CouchLink.App/HostInputService.cs tests/CouchLink.Core.Tests/PadManagerTests.cs
git commit -m "feat(core): pads are plugged, held and unplugged explicitly and bound to the client's address"
```

---

### Task 3: Wire types, PC names and the host announce

**Files:**
- Modify: `src/CouchLink.Core/Protocol/Wire.cs`, `src/CouchLink.Core/Net/Ports.cs`
- Create: `src/CouchLink.Core/Protocol/PcName.cs`, `src/CouchLink.Core/Protocol/HostAnnounce.cs`
- Test: `tests/CouchLink.Core.Tests/PcNameTests.cs`, `tests/CouchLink.Core.Tests/HostAnnounceTests.cs`

**Interfaces:**
- Produces: `Wire.TypeHostAnnounce = 7`, `TypeJoinRequest = 8`, `TypeAccepted = 9`, `TypeDenied = 10`, `TypeHeartbeat = 11`, `TypeLeave = 12`, `TypeKicked = 13`, `TypeHostEnded = 14`. `Ports.Discovery = 47800`, `Ports.Session = 47801`. `PcName.MaxBytes = 63`, `string PcName.Clip(string name)`, `string PcName.ThisPc`, `byte[] PcName.Encode(string name)` (clips first), `bool PcName.TryDecode(ReadOnlySpan<byte> bytes, out string name)` (1-63 bytes of valid UTF-8). `readonly record struct HostAnnounce(byte Version, byte Players, byte Capacity, string Name)` with `bool Compatible`, `static HostAnnounce For(int players, int capacity, string name)`, `byte[] ToArray()`, `static bool TryParse(ReadOnlySpan<byte> source, out HostAnnounce announce)` (accepts any version).

- [ ] **Step 1: Write the failing tests**

Create `tests/CouchLink.Core.Tests/PcNameTests.cs`:

```csharp
using System.Text;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class PcNameTests
{
    [Fact]
    public void A_short_name_is_kept()
    {
        Assert.Equal("PC-07", PcName.Clip("PC-07"));
    }

    [Fact]
    public void A_long_name_is_cut_to_63_bytes()
    {
        Assert.Equal(new string('A', 63), PcName.Clip(new string('A', 80)));
    }

    [Fact]
    public void A_cut_never_splits_a_character()
    {
        var clipped = PcName.Clip(new string('é', 40)); // 2 bytes each
        Assert.Equal(31, clipped.Length);
        Assert.Equal(62, Encoding.UTF8.GetByteCount(clipped));

        var emoji = PcName.Clip(new string('A', 61) + "😀"); // 4-byte character does not fit
        Assert.Equal(new string('A', 61), emoji);
    }

    [Fact]
    public void A_blank_name_becomes_a_placeholder()
    {
        Assert.Equal("Unknown PC", PcName.Clip("   "));
    }

    [Fact]
    public void Encode_and_decode_round_trip()
    {
        Assert.True(PcName.TryDecode(PcName.Encode("Café-03"), out var name));
        Assert.Equal("Café-03", name);
    }

    [Fact]
    public void Decode_rejects_empty_too_long_and_invalid_utf8()
    {
        Assert.False(PcName.TryDecode([], out _));
        Assert.False(PcName.TryDecode(new byte[64], out _));
        Assert.False(PcName.TryDecode([0xC3], out _)); // half a character
    }
}
```

Create `tests/CouchLink.Core.Tests/HostAnnounceTests.cs`:

```csharp
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class HostAnnounceTests
{
    [Fact]
    public void Round_trips()
    {
        var announce = HostAnnounce.For(players: 4, capacity: 9, name: "PC-03");

        Assert.True(HostAnnounce.TryParse(announce.ToArray(), out var parsed));

        Assert.Equal(announce, parsed);
        Assert.Equal(Wire.Version, parsed.Version);
        Assert.True(parsed.Compatible);
    }

    [Fact]
    public void An_announce_from_another_version_still_parses_and_is_not_compatible()
    {
        var bytes = new HostAnnounce(Version: 2, Players: 1, Capacity: 9, Name: "PC-03").ToArray();

        Assert.True(HostAnnounce.TryParse(bytes, out var parsed));

        Assert.Equal(2, parsed.Version);
        Assert.False(parsed.Compatible);
    }

    [Fact]
    public void Truncated_wrong_type_or_inconsistent_announces_are_rejected()
    {
        var good = HostAnnounce.For(1, 9, "PC-03").ToArray();

        Assert.False(HostAnnounce.TryParse(good.AsSpan(0, good.Length - 1), out _)); // truncated name
        Assert.False(HostAnnounce.TryParse(good.AsSpan(0, 6), out _));

        var wrongType = (byte[])good.Clone();
        wrongType[3] = Wire.TypeInput;
        Assert.False(HostAnnounce.TryParse(wrongType, out _));

        var tooMany = (byte[])good.Clone();
        tooMany[4] = 10; // players > capacity
        Assert.False(HostAnnounce.TryParse(tooMany, out _));

        var noName = (byte[])good.Clone();
        noName[6] = 0;
        Assert.False(HostAnnounce.TryParse(noName.AsSpan(0, 7), out _));
    }

    [Fact]
    public void Random_bytes_never_throw()
    {
        var random = new Random(7);
        for (int i = 0; i < 10_000; i++)
        {
            var bytes = new byte[random.Next(0, 80)];
            random.NextBytes(bytes);
            if (bytes.Length >= 4)
            {
                bytes[0] = 0x43;
                bytes[1] = 0x4C;
                bytes[3] = Wire.TypeHostAnnounce;
            }
            HostAnnounce.TryParse(bytes, out _);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~PcNameTests|FullyQualifiedName~HostAnnounceTests"`
Expected: build FAILS: `The name 'PcName' does not exist in the current context`.

- [ ] **Step 3: Implement**

In `src/CouchLink.Core/Protocol/Wire.cs` add after `TypeAudio`:

```csharp
    public const byte TypeHostAnnounce = 7;
    public const byte TypeJoinRequest = 8;
    public const byte TypeAccepted = 9;
    public const byte TypeDenied = 10;
    public const byte TypeHeartbeat = 11;
    public const byte TypeLeave = 12;
    public const byte TypeKicked = 13;
    public const byte TypeHostEnded = 14;
```

and change its summary to `/// <summary>The first four bytes of every CouchLink datagram and session frame: magic "CL", version, packet type.</summary>`.

Replace `src/CouchLink.Core/Net/Ports.cs`:

```csharp
namespace CouchLink.Core.Net;

public static class Ports
{
    /// <summary>Host -> LAN broadcast: "I'm hosting" once a second. Only clients listen on it.</summary>
    public const int Discovery = 47800;

    /// <summary>Client -> host TCP session channel: join, approval, heartbeat, leave, kick.</summary>
    public const int Session = 47801;

    /// <summary>Host -> client video and audio; host -> client timing replies.</summary>
    public const int Video = 47802;

    /// <summary>Client -> host controller state, keyframe requests and timing pings.</summary>
    public const int Input = 47803;
}
```

Create `src/CouchLink.Core/Protocol/PcName.cs`:

```csharp
using System.Text;

namespace CouchLink.Core.Protocol;

/// <summary>A PC's name on the wire: UTF-8, 1-63 bytes, never cut in the middle of a character.</summary>
public static class PcName
{
    public const int MaxBytes = 63;
    private const string Placeholder = "Unknown PC";
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string ThisPc => Clip(Environment.MachineName);

    public static string Clip(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            return Placeholder;

        var result = new StringBuilder();
        int bytes = 0;
        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > MaxBytes)
                break;
            result.Append(rune.ToString());
            bytes += rune.Utf8SequenceLength;
        }
        return result.ToString();
    }

    public static byte[] Encode(string name) => Strict.GetBytes(Clip(name));

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out string name)
    {
        name = "";
        if (bytes.Length is 0 or > MaxBytes)
            return false;
        try
        {
            name = Strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
```

Create `src/CouchLink.Core/Protocol/HostAnnounce.cs`:

```csharp
using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>
/// Host -> LAN, once a second on UDP 47800 (type 7). After the 4-byte header: players (1),
/// capacity (1), name length (1), name. <see cref="TryParse"/> accepts any version, so a host on
/// another version is still listed (greyed out) instead of silently missing.
/// </summary>
public readonly record struct HostAnnounce(byte Version, byte Players, byte Capacity, string Name)
{
    private const int FixedSize = 7;

    public bool Compatible => Version == Wire.Version;

    public static HostAnnounce For(int players, int capacity, string name) =>
        new(Wire.Version, (byte)players, (byte)capacity, PcName.Clip(name));

    public byte[] ToArray()
    {
        var name = PcName.Encode(Name);
        var bytes = new byte[FixedSize + name.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, Wire.Magic);
        bytes[2] = Version;
        bytes[3] = Wire.TypeHostAnnounce;
        bytes[4] = Players;
        bytes[5] = Capacity;
        bytes[6] = (byte)name.Length;
        name.CopyTo(bytes, FixedSize);
        return bytes;
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out HostAnnounce announce)
    {
        announce = default;
        if (source.Length < FixedSize
            || BinaryPrimitives.ReadUInt16LittleEndian(source) != Wire.Magic
            || source[3] != Wire.TypeHostAnnounce)
            return false;

        byte players = source[4], capacity = source[5];
        int nameLength = source[6];
        if (capacity == 0 || players > capacity || source.Length != FixedSize + nameLength)
            return false;
        if (!PcName.TryDecode(source.Slice(FixedSize, nameLength), out var name))
            return false;

        announce = new HostAnnounce(source[2], players, capacity, name);
        return true;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~PcNameTests|FullyQualifiedName~HostAnnounceTests"`
Expected: PASS (10 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Protocol src/CouchLink.Core/Net/Ports.cs tests/CouchLink.Core.Tests/PcNameTests.cs tests/CouchLink.Core.Tests/HostAnnounceTests.cs
git commit -m "feat(core): host announce packet (wire type 7), PC names and the discovery and session ports"
```

---

### Task 4: Session messages and framing

**Files:**
- Create: `src/CouchLink.Core/Protocol/SessionMessage.cs`, `src/CouchLink.Core/Protocol/SessionFraming.cs`
- Test: `tests/CouchLink.Core.Tests/SessionFramingTests.cs`

**Interfaces:**
- Consumes: `Wire` types 8-14, `PcName` (Task 3).
- Produces:
  - `enum SessionMessageType : byte { JoinRequest = 8, Accepted = 9, Denied = 10, Heartbeat = 11, Leave = 12, Kicked = 13, HostEnded = 14 }`
  - `enum DenyReason : byte { Denied = 1, Full = 2, TimedOut = 3, PadFailed = 4 }`
  - `readonly record struct SessionMessage(SessionMessageType Type, string Name = "", byte Slot = 0, DenyReason Reason = 0)` with factories `JoinRequest(string name)`, `Accepted(byte slot)`, `Denied(DenyReason reason)` and static readonly `Heartbeat`, `Leave`, `Kicked`, `HostEnded`; `override string ToString()` gives `"JoinRequest PC-07"`, `"Accepted P4"`, `"Denied Full"`, or the type name.
  - `SessionFraming.MaxFrame = 4096`, `byte[] Encode(SessionMessage message)` (length prefix included), `SessionMessage Decode(ReadOnlySpan<byte> frame)` (frame without the length prefix; throws `InvalidDataException`), `Task<SessionMessage?> ReadAsync(Stream stream, CancellationToken ct)` (null on a clean end of stream before a frame; `EndOfStreamException` mid-frame; `InvalidDataException` on a bad frame).

`PadFailed` (4) is a small addition to spec 2.2 so a client isn't left waiting when the host's pad can't be created; the spec (2.2, 3.2) lists it.

- [ ] **Step 1: Write the failing tests**

Create `tests/CouchLink.Core.Tests/SessionFramingTests.cs`:

```csharp
using System.Buffers.Binary;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class SessionFramingTests
{
    public static TheoryData<SessionMessage> AllMessages => new()
    {
        SessionMessage.JoinRequest("PC-07"),
        SessionMessage.Accepted(2),
        SessionMessage.Accepted(10),
        SessionMessage.Denied(DenyReason.Denied),
        SessionMessage.Denied(DenyReason.Full),
        SessionMessage.Denied(DenyReason.TimedOut),
        SessionMessage.Denied(DenyReason.PadFailed),
        SessionMessage.Heartbeat,
        SessionMessage.Leave,
        SessionMessage.Kicked,
        SessionMessage.HostEnded,
    };

    [Theory]
    [MemberData(nameof(AllMessages))]
    public async Task Every_message_round_trips_through_a_stream(SessionMessage message)
    {
        using var stream = new MemoryStream(SessionFraming.Encode(message));

        Assert.Equal(message, await SessionFraming.ReadAsync(stream, CancellationToken.None));
        Assert.Null(await SessionFraming.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Frames_back_to_back_are_read_one_at_a_time()
    {
        var bytes = SessionFraming.Encode(SessionMessage.JoinRequest("PC-07"))
            .Concat(SessionFraming.Encode(SessionMessage.Heartbeat)).ToArray();
        using var stream = new MemoryStream(bytes);

        Assert.Equal("JoinRequest PC-07", (await SessionFraming.ReadAsync(stream, default))!.Value.ToString());
        Assert.Equal(SessionMessage.Heartbeat, await SessionFraming.ReadAsync(stream, default));
    }

    [Fact]
    public async Task An_end_of_stream_mid_frame_is_an_error()
    {
        var bytes = SessionFraming.Encode(SessionMessage.JoinRequest("PC-07"));
        using var stream = new MemoryStream(bytes[..^2]);

        await Assert.ThrowsAsync<EndOfStreamException>(() => SessionFraming.ReadAsync(stream, default));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4097)]
    public async Task A_length_outside_4_to_4096_is_rejected(int length)
    {
        var bytes = new byte[2 + 8];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)length);
        using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() => SessionFraming.ReadAsync(stream, default));
    }

    [Fact]
    public void Wrong_magic_version_or_type_is_rejected()
    {
        var frame = SessionFraming.Encode(SessionMessage.Heartbeat)[2..];

        var magic = (byte[])frame.Clone();
        magic[0] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(magic));

        var version = (byte[])frame.Clone();
        version[2] = Wire.Version + 1;
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(version));

        var type = (byte[])frame.Clone();
        type[3] = Wire.TypeInput;
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(type));
    }

    [Fact]
    public void Bodies_of_the_wrong_shape_are_rejected()
    {
        byte[] Frame(byte type, params byte[] body)
        {
            var frame = new byte[4 + body.Length];
            Wire.WriteHeader(frame, type);
            body.CopyTo(frame, 4);
            return frame;
        }

        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted, 1)));      // slot 1
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted, 11)));     // slot 11
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted)));         // no slot
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeDenied, 9)));        // unknown reason
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeHeartbeat, 0)));     // body on an empty message
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeJoinRequest, 3, 65))); // name shorter than its length
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeJoinRequest, 0)));   // empty name
    }

    [Fact]
    public void Decode_throws_only_InvalidDataException_for_random_frames()
    {
        var random = new Random(11);
        for (int i = 0; i < 20_000; i++)
        {
            var frame = new byte[random.Next(0, 70)];
            random.NextBytes(frame);
            if (frame.Length >= 4)
            {
                Wire.WriteHeader(frame, (byte)random.Next(7, 16));
            }
            try
            {
                SessionFraming.Decode(frame);
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    [Fact]
    public void Messages_describe_themselves_for_logs_and_tests()
    {
        Assert.Equal("JoinRequest PC-07", SessionMessage.JoinRequest("PC-07").ToString());
        Assert.Equal("Accepted P4", SessionMessage.Accepted(4).ToString());
        Assert.Equal("Denied Full", SessionMessage.Denied(DenyReason.Full).ToString());
        Assert.Equal("Kicked", SessionMessage.Kicked.ToString());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~SessionFramingTests"`
Expected: build FAILS: `The type or namespace name 'SessionMessage' could not be found`.

- [ ] **Step 3: Implement**

Create `src/CouchLink.Core/Protocol/SessionMessage.cs`:

```csharp
namespace CouchLink.Core.Protocol;

public enum SessionMessageType : byte
{
    JoinRequest = Wire.TypeJoinRequest,
    Accepted = Wire.TypeAccepted,
    Denied = Wire.TypeDenied,
    Heartbeat = Wire.TypeHeartbeat,
    Leave = Wire.TypeLeave,
    Kicked = Wire.TypeKicked,
    HostEnded = Wire.TypeHostEnded,
}

public enum DenyReason : byte
{
    Denied = 1,
    Full = 2,
    TimedOut = 3,
    /// <summary>The host's virtual pad could not be created.</summary>
    PadFailed = 4,
}

/// <summary>
/// One message on the TCP session channel. <see cref="Name"/> is set only on a join request,
/// <see cref="Slot"/> only on Accepted, <see cref="Reason"/> only on Denied.
/// </summary>
public readonly record struct SessionMessage(SessionMessageType Type, string Name = "", byte Slot = 0, DenyReason Reason = 0)
{
    public static readonly SessionMessage Heartbeat = new(SessionMessageType.Heartbeat);
    public static readonly SessionMessage Leave = new(SessionMessageType.Leave);
    public static readonly SessionMessage Kicked = new(SessionMessageType.Kicked);
    public static readonly SessionMessage HostEnded = new(SessionMessageType.HostEnded);

    public static SessionMessage JoinRequest(string name) => new(SessionMessageType.JoinRequest, Name: PcName.Clip(name));

    public static SessionMessage Accepted(byte slot) => new(SessionMessageType.Accepted, Slot: slot);

    public static SessionMessage Denied(DenyReason reason) => new(SessionMessageType.Denied, Reason: reason);

    public override string ToString() => Type switch
    {
        SessionMessageType.JoinRequest => $"JoinRequest {Name}",
        SessionMessageType.Accepted => $"Accepted P{Slot}",
        SessionMessageType.Denied => $"Denied {Reason}",
        _ => Type.ToString(),
    };
}
```

Create `src/CouchLink.Core/Protocol/SessionFraming.cs`:

```csharp
using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>
/// TCP session frames: a u16 length (4-4096, the bytes that follow), the 4-byte <see cref="Wire"/>
/// header, then the body. JoinRequest: name length (1) + name. Accepted: slot (1, 2-10).
/// Denied: reason (1, 1-4). Every other message has no body.
/// </summary>
public static class SessionFraming
{
    public const int MaxFrame = 4096;
    private const int MinFrame = 4;

    public static byte[] Encode(SessionMessage message)
    {
        byte[] body = message.Type switch
        {
            SessionMessageType.JoinRequest => NameBody(message.Name),
            SessionMessageType.Accepted => [message.Slot],
            SessionMessageType.Denied => [(byte)message.Reason],
            _ => [],
        };
        var bytes = new byte[2 + 4 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(4 + body.Length));
        Wire.WriteHeader(bytes.AsSpan(2), (byte)message.Type);
        body.CopyTo(bytes, 6);
        return bytes;
    }

    private static byte[] NameBody(string name)
    {
        var bytes = PcName.Encode(name);
        return [(byte)bytes.Length, .. bytes];
    }

    /// <summary>Decodes one frame (without its length prefix). Throws <see cref="InvalidDataException"/> for anything malformed.</summary>
    public static SessionMessage Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < MinFrame || BinaryPrimitives.ReadUInt16LittleEndian(frame) != Wire.Magic)
            throw new InvalidDataException("Not a CouchLink session frame.");
        if (frame[2] != Wire.Version)
            throw new InvalidDataException($"Session frame version {frame[2]}, expected {Wire.Version}.");

        var body = frame[4..];
        switch (frame[3])
        {
            case Wire.TypeJoinRequest:
                if (body.Length < 1 || body.Length != 1 + body[0] || !PcName.TryDecode(body[1..], out var name))
                    throw new InvalidDataException("Bad join request.");
                return SessionMessage.JoinRequest(name);
            case Wire.TypeAccepted:
                if (body.Length != 1 || body[0] is < 2 or > 10)
                    throw new InvalidDataException("Bad accepted message.");
                return SessionMessage.Accepted(body[0]);
            case Wire.TypeDenied:
                if (body.Length != 1 || body[0] is < (byte)DenyReason.Denied or > (byte)DenyReason.PadFailed)
                    throw new InvalidDataException("Bad denied message.");
                return SessionMessage.Denied((DenyReason)body[0]);
            case Wire.TypeHeartbeat or Wire.TypeLeave or Wire.TypeKicked or Wire.TypeHostEnded:
                if (!body.IsEmpty)
                    throw new InvalidDataException("Unexpected body.");
                return new SessionMessage((SessionMessageType)frame[3]);
            default:
                throw new InvalidDataException($"Unknown session message type {frame[3]}.");
        }
    }

    /// <summary>Reads the next message; null if the stream ended cleanly between frames.</summary>
    public static async Task<SessionMessage?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[2];
        int got = await stream.ReadAtLeastAsync(prefix, 2, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        if (got == 0)
            return null;
        if (got < 2)
            throw new EndOfStreamException();

        int length = BinaryPrimitives.ReadUInt16LittleEndian(prefix);
        if (length is < MinFrame or > MaxFrame)
            throw new InvalidDataException($"Bad session frame length {length}.");
        var frame = new byte[length];
        await stream.ReadExactlyAsync(frame, ct).ConfigureAwait(false);
        return Decode(frame);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~SessionFramingTests"`
Expected: PASS (19 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Protocol/SessionMessage.cs src/CouchLink.Core/Protocol/SessionFraming.cs tests/CouchLink.Core.Tests/SessionFramingTests.cs
git commit -m "feat(core): session messages (wire types 8-14) and length-prefixed framing"
```

---
### Task 5: Discovery: broadcast addresses, the host list, broadcaster and listener

**Files:**
- Create: `src/CouchLink.Core/Net/BroadcastAddresses.cs`, `src/CouchLink.Core/Net/DiscoveryBroadcaster.cs`, `src/CouchLink.Core/Net/DiscoveryListener.cs`, `src/CouchLink.Core/Session/HostList.cs`
- Test: `tests/CouchLink.Core.Tests/BroadcastAddressesTests.cs`, `tests/CouchLink.Core.Tests/HostListTests.cs`, `tests/CouchLink.Core.Tests/DiscoveryLoopbackTests.cs`

**Interfaces:**
- Consumes: `HostAnnounce` (Task 3), `UdpReceiveLoop.RunAsync` (existing, internal).
- Produces:
  - `BroadcastAddresses.For(IPAddress address, IPAddress mask) -> IPAddress`, `BroadcastAddresses.Current() -> IReadOnlyList<IPAddress>` (loopback first, then one directed broadcast per IPv4 address on every interface that is up).
  - `DiscoveryBroadcaster(int port, Func<HostAnnounce> announce, Func<IReadOnlyList<IPAddress>>? targets = null, Action<Exception>? onError = null) : IDisposable`; sends at once, then every `Interval` (1 s); re-reads targets every 10 s.
  - `DiscoveryListener.TryCreate(int port, out DiscoveryListener? listener, out string? error) -> bool`, `int LocalPort`, `Task RunAsync(Action<HostAnnounce, IPAddress> onAnnounce, CancellationToken ct, Action<Exception>? onError = null)`, `IDisposable`. Error text when the port is taken: `Can't search for hosts (port {port} in use).`
  - `readonly record struct FoundHost(string Name, IPAddress Address, int Players, int Capacity, bool Compatible)`; `HostList(TimeProvider time)` with `ExpireAfter` (3 s), `void Seen(HostAnnounce announce, IPAddress from)`, `IReadOnlyList<FoundHost> Current()` (sorted by name, expired ones removed). Thread-safe. Keyed by name (case-insensitive); a loopback address never replaces a LAN address.

- [ ] **Step 1: Write the failing tests**

Create `tests/CouchLink.Core.Tests/BroadcastAddressesTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Net;

namespace CouchLink.Core.Tests;

public class BroadcastAddressesTests
{
    [Theory]
    [InlineData("192.168.1.23", "255.255.255.0", "192.168.1.255")]
    [InlineData("10.1.2.3", "255.255.0.0", "10.1.255.255")]
    [InlineData("172.16.5.9", "255.255.255.255", "172.16.5.9")]
    public void Directed_broadcast_sets_the_host_bits(string address, string mask, string expected)
    {
        Assert.Equal(IPAddress.Parse(expected), BroadcastAddresses.For(IPAddress.Parse(address), IPAddress.Parse(mask)));
    }

    [Fact]
    public void Current_starts_with_loopback_and_holds_only_distinct_IPv4_addresses()
    {
        var current = BroadcastAddresses.Current();
        Assert.Equal(IPAddress.Loopback, current[0]);
        Assert.All(current, a => Assert.Equal(AddressFamily.InterNetwork, a.AddressFamily));
        Assert.Equal(current.Count, current.Distinct().Count());
    }
}
```

Create `tests/CouchLink.Core.Tests/HostListTests.cs`:

```csharp
using System.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public class HostListTests
{
    private static readonly IPAddress Lan = IPAddress.Parse("192.168.1.3");
    private static readonly IPAddress OtherLan = IPAddress.Parse("10.0.0.3");

    private readonly FakeTimeProvider _time = new();
    private readonly HostList _list;

    public HostListTests() => _list = new HostList(_time);

    [Fact]
    public void A_host_is_listed_with_its_address_and_counts()
    {
        _list.Seen(HostAnnounce.For(4, 9, "PC-03"), Lan);
        Assert.Equal(new FoundHost("PC-03", Lan, 4, 9, true), Assert.Single(_list.Current()));
    }

    [Fact]
    public void A_host_is_dropped_after_3s_without_an_announce()
    {
        _list.Seen(HostAnnounce.For(1, 9, "PC-03"), Lan);
        _time.Advance(HostList.ExpireAfter - TimeSpan.FromMilliseconds(1));
        Assert.Single(_list.Current());
        _time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Empty(_list.Current());
    }

    [Fact]
    public void A_new_announce_keeps_the_host_and_updates_its_counts()
    {
        _list.Seen(HostAnnounce.For(1, 9, "PC-03"), Lan);
        _time.Advance(TimeSpan.FromSeconds(2));
        _list.Seen(HostAnnounce.For(5, 9, "PC-03"), Lan);
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(5, Assert.Single(_list.Current()).Players);
    }

    [Fact]
    public void A_host_seen_on_two_addresses_is_listed_once()
    {
        _list.Seen(HostAnnounce.For(1, 9, "PC-03"), Lan);
        _list.Seen(HostAnnounce.For(1, 9, "pc-03"), OtherLan);
        Assert.Equal(OtherLan, Assert.Single(_list.Current()).Address);
    }

    [Fact]
    public void Loopback_never_replaces_a_lan_address()
    {
        _list.Seen(HostAnnounce.For(1, 9, "PC-03"), Lan);
        _list.Seen(HostAnnounce.For(2, 9, "PC-03"), IPAddress.Loopback);
        var host = Assert.Single(_list.Current());
        Assert.Equal(Lan, host.Address);
        Assert.Equal(2, host.Players);

        var list = new HostList(_time);
        list.Seen(HostAnnounce.For(1, 9, "PC-03"), IPAddress.Loopback);
        list.Seen(HostAnnounce.For(1, 9, "PC-03"), Lan);
        Assert.Equal(Lan, Assert.Single(list.Current()).Address);
    }

    [Fact]
    public void Hosts_are_sorted_by_name_and_other_versions_are_marked()
    {
        _list.Seen(HostAnnounce.For(0, 9, "PC-07"), Lan);
        _list.Seen(new HostAnnounce(2, 0, 9, "PC-01"), OtherLan);
        var hosts = _list.Current();
        Assert.Equal(["PC-01", "PC-07"], hosts.Select(h => h.Name));
        Assert.False(hosts[0].Compatible);
        Assert.True(hosts[1].Compatible);
    }
}
```

Create `tests/CouchLink.Core.Tests/DiscoveryLoopbackTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class DiscoveryLoopbackTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Announces_reach_a_listener_and_garbage_is_ignored()
    {
        Assert.True(DiscoveryListener.TryCreate(0, out var listener, out _));
        using (listener)
        {
            var heard = Channel.CreateUnbounded<(HostAnnounce Announce, IPAddress From)>();
            using var cts = new CancellationTokenSource();
            var loop = listener!.RunAsync((a, from) => heard.Writer.TryWrite((a, from)), cts.Token);

            using (var raw = new UdpClient())
                raw.Send([1, 2, 3, 4, 5], 5, new IPEndPoint(IPAddress.Loopback, listener.LocalPort));

            using (new DiscoveryBroadcaster(listener.LocalPort, () => HostAnnounce.For(3, 9, "PC-03"), () => [IPAddress.Loopback]))
            {
                using var wait = new CancellationTokenSource(Timeout);
                var (announce, from) = await heard.Reader.ReadAsync(wait.Token);
                Assert.Equal("PC-03", announce.Name);
                Assert.Equal(3, announce.Players);
                Assert.Equal(IPAddress.Loopback, from);
            }

            cts.Cancel();
            await loop.WaitAsync(Timeout);
        }
    }

    [Fact]
    public async Task The_broadcaster_repeats_every_second()
    {
        Assert.True(DiscoveryListener.TryCreate(0, out var listener, out _));
        using (listener)
        {
            int count = 0;
            using var cts = new CancellationTokenSource();
            var loop = listener!.RunAsync((_, _) => Interlocked.Increment(ref count), cts.Token);
            using (new DiscoveryBroadcaster(listener.LocalPort, () => HostAnnounce.For(0, 9, "PC-03"), () => [IPAddress.Loopback]))
                await Task.Delay(2500);
            Assert.InRange(Volatile.Read(ref count), 2, 4); // at 0, 1 and 2 s, give or take a slow timer
            cts.Cancel();
            await loop.WaitAsync(Timeout);
        }
    }

    [Fact]
    public void A_taken_port_gives_a_message()
    {
        using var taken = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)taken.Client.LocalEndPoint!).Port;

        Assert.False(DiscoveryListener.TryCreate(port, out var listener, out var error));

        Assert.Null(listener);
        Assert.Equal($"Can't search for hosts (port {port} in use).", error);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~BroadcastAddressesTests|FullyQualifiedName~HostListTests|FullyQualifiedName~DiscoveryLoopbackTests"`
Expected: build FAILS: `The name 'BroadcastAddresses' does not exist in the current context`.

- [ ] **Step 3: Implement**

Create `src/CouchLink.Core/Net/BroadcastAddresses.cs`:

```csharp
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace CouchLink.Core.Net;

/// <summary>
/// Where the host's announce goes: a directed broadcast (e.g. 192.168.1.255) on every IPv4 interface
/// that is up, because Windows sends 255.255.255.255 out of one adapter only. Loopback comes first so
/// a client on the host's own PC finds it even with no network.
/// </summary>
public static class BroadcastAddresses
{
    public static IPAddress For(IPAddress address, IPAddress mask)
    {
        var bytes = address.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] |= (byte)~maskBytes[i];
        return new IPAddress(bytes);
    }

    public static IReadOnlyList<IPAddress> Current()
    {
        var result = new List<IPAddress> { IPAddress.Loopback };
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                if (unicast.Address.AddressFamily == AddressFamily.InterNetwork
                    && unicast.IPv4Mask is { } mask
                    && !mask.Equals(IPAddress.Any))
                    result.Add(For(unicast.Address, mask));
            }
        }
        return result.Distinct().ToList();
    }
}
```

Create `src/CouchLink.Core/Net/DiscoveryBroadcaster.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>
/// Host side: sends <see cref="HostAnnounce"/> at once and then every second to every broadcast
/// address (re-read every 10 s so a cable plugged in later is covered). Only sends; it never binds the
/// discovery port, so a client on the same PC can listen on it.
/// </summary>
public sealed class DiscoveryBroadcaster : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RefreshTargets = TimeSpan.FromSeconds(10);

    private readonly UdpClient _udp = new(AddressFamily.InterNetwork) { EnableBroadcast = true };
    private readonly int _port;
    private readonly Func<HostAnnounce> _announce;
    private readonly Func<IReadOnlyList<IPAddress>> _targets;
    private readonly Action<Exception>? _onError;
    private readonly Lock _gate = new();
    private readonly Timer _timer;
    private IReadOnlyList<IPAddress> _current = [];
    private long? _refreshedAt;
    private bool _disposed;

    public DiscoveryBroadcaster(int port, Func<HostAnnounce> announce,
        Func<IReadOnlyList<IPAddress>>? targets = null, Action<Exception>? onError = null)
    {
        _port = port;
        _announce = announce;
        _targets = targets ?? BroadcastAddresses.Current;
        _onError = onError;
        _timer = new Timer(_ => Send(), null, TimeSpan.Zero, Interval);
    }

    private void Send()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            try
            {
                long now = Environment.TickCount64;
                if (_refreshedAt is not { } at || now - at >= RefreshTargets.TotalMilliseconds)
                {
                    _current = _targets();
                    _refreshedAt = now;
                }
                var bytes = _announce().ToArray();
                foreach (var address in _current)
                {
                    try
                    {
                        _udp.Send(bytes, bytes.Length, new IPEndPoint(address, _port));
                    }
                    catch (SocketException)
                    {
                        // An adapter that just went away; the next refresh drops it.
                    }
                }
            }
            catch (Exception e)
            {
                _onError?.Invoke(e); // never let a timer thread take the host down
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _disposed = true;
        _timer.Dispose();
        _udp.Dispose();
    }
}
```

Create `src/CouchLink.Core/Net/DiscoveryListener.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>Client side: receives host announces on the discovery port.</summary>
public sealed class DiscoveryListener : IDisposable
{
    private readonly UdpClient _udp;

    private DiscoveryListener(UdpClient udp) => _udp = udp;

    /// <summary>Opens the port, or returns a message for the join list if it can't be opened.</summary>
    public static bool TryCreate(int port, out DiscoveryListener? listener, out string? error)
    {
        try
        {
            listener = new DiscoveryListener(new UdpClient(new IPEndPoint(IPAddress.Any, port)));
            error = null;
            return true;
        }
        catch (SocketException e)
        {
            listener = null;
            error = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"Can't search for hosts (port {port} in use)."
                : $"Can't search for hosts: {e.Message}";
            return false;
        }
    }

    public int LocalPort => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

    /// <summary>Receives until cancelled; anything that isn't an announce is ignored.</summary>
    public Task RunAsync(Action<HostAnnounce, IPAddress> onAnnounce, CancellationToken ct, Action<Exception>? onError = null) =>
        UdpReceiveLoop.RunAsync(_udp, result =>
        {
            if (HostAnnounce.TryParse(result.Buffer, out var announce))
                onAnnounce(announce, result.RemoteEndPoint.Address);
        }, ct, onError);

    public void Dispose() => _udp.Dispose();
}
```

Create `src/CouchLink.Core/Session/HostList.cs`:

```csharp
using System.Net;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Session;

/// <summary>A host on the join list. <see cref="Players"/> and <see cref="Capacity"/> count clients only (the host player is extra).</summary>
public readonly record struct FoundHost(string Name, IPAddress Address, int Players, int Capacity, bool Compatible);

/// <summary>
/// Client side: the hosts heard on the LAN, by name, so a host with two adapters (or a host on this
/// same PC, heard on loopback and on its LAN address) is listed once. A loopback address never
/// replaces a LAN one. A host is dropped after <see cref="ExpireAfter"/> without an announce.
/// Thread-safe.
/// </summary>
public sealed class HostList(TimeProvider time)
{
    public static readonly TimeSpan ExpireAfter = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, (FoundHost Host, long SeenAt)> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public void Seen(HostAnnounce announce, IPAddress from)
    {
        lock (_gate)
        {
            var address = from;
            if (IPAddress.IsLoopback(from)
                && _hosts.TryGetValue(announce.Name, out var known)
                && !IPAddress.IsLoopback(known.Host.Address))
                address = known.Host.Address;
            var host = new FoundHost(announce.Name, address, announce.Players, announce.Capacity, announce.Compatible);
            _hosts[announce.Name] = (host, time.GetTimestamp());
        }
    }

    public IReadOnlyList<FoundHost> Current()
    {
        lock (_gate)
        {
            foreach (var name in _hosts.Where(h => time.GetElapsedTime(h.Value.SeenAt) >= ExpireAfter).Select(h => h.Key).ToList())
                _hosts.Remove(name);
            return _hosts.Values.Select(h => h.Host).OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~BroadcastAddressesTests|FullyQualifiedName~HostListTests|FullyQualifiedName~DiscoveryLoopbackTests"`
Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Net src/CouchLink.Core/Session tests/CouchLink.Core.Tests/BroadcastAddressesTests.cs tests/CouchLink.Core.Tests/HostListTests.cs tests/CouchLink.Core.Tests/DiscoveryLoopbackTests.cs
git commit -m "feat(core): LAN discovery: host announces on every adapter, clients keep a host list"
```

---

### Task 6: The host session state machine

**Files:**
- Create: `src/CouchLink.Core/Session/HostSession.cs`
- Test: `tests/CouchLink.Core.Tests/HostSessionTests.cs`

**Interfaces:**
- Consumes: `SessionMessage`, `DenyReason` (Task 4), `PadManager.FirstSlot/LastSlot` (Task 2).
- Produces (namespace `CouchLink.Core.Session`):
  - `enum PlayerState { Active, Reserved }`; `readonly record struct PlayerInfo(byte Slot, string Name, IPAddress Address, PlayerState State)`.
  - `interface IHostEffects { bool PlugPad(byte slot, IPAddress address); void HoldPad(byte slot); void UnplugPad(byte slot); void AddTarget(byte slot, IPAddress address); void RemoveTarget(byte slot); void AskHost(int connection, string name); void CloseAsk(int connection); void PlayersChanged(); }`
  - `interface IHostSessionEffects : IHostEffects { void Send(int connection, SessionMessage message); void Close(int connection); }`
  - `HostSession(IHostSessionEffects effects, TimeProvider time, Action<string>? log = null)` with `Capacity` (9), `SilenceLimit` (5 s), `AskTimeout` (30 s), `ReserveFor` (60 s), `bool AllowEveryone { get; set; }`, `int PlayerCount`, `IReadOnlyList<PlayerInfo> Players` (by slot), and `Connected(int connection, IPAddress address)`, `Received(int connection, SessionMessage message)`, `Disconnected(int connection)`, `Allow(int connection)`, `Deny(int connection)`, `Kick(byte slot)`, `Tick()`, `Stop()`. Not thread-safe: the caller holds one lock around every call.
- Contract with the server: when the session calls `Close(id)` it has already forgotten that connection, so a later `Disconnected(id)` or message from it does nothing.

- [ ] **Step 1: Write the failing tests**

Create `tests/CouchLink.Core.Tests/HostSessionTests.cs`:

```csharp
using System.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

internal sealed class RecordingHostEffects : IHostSessionEffects
{
    public List<string> Calls { get; } = [];
    public bool PlugFails { get; set; }
    public int PlayersChangedCount { get; private set; }

    public bool PlugPad(byte slot, IPAddress address)
    {
        Calls.Add($"plug P{slot} {address}");
        return !PlugFails;
    }

    public void HoldPad(byte slot) => Calls.Add($"hold P{slot}");
    public void UnplugPad(byte slot) => Calls.Add($"unplug P{slot}");
    public void AddTarget(byte slot, IPAddress address) => Calls.Add($"target+ P{slot} {address}");
    public void RemoveTarget(byte slot) => Calls.Add($"target- P{slot}");
    public void AskHost(int connection, string name) => Calls.Add($"ask #{connection} {name}");
    public void CloseAsk(int connection) => Calls.Add($"close-ask #{connection}");
    public void PlayersChanged() => PlayersChangedCount++;
    public void Send(int connection, SessionMessage message) => Calls.Add($"send #{connection} {message}");
    public void Close(int connection) => Calls.Add($"close #{connection}");
}

public class HostSessionTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingHostEffects _effects = new();
    private readonly HostSession _session;

    public HostSessionTests() => _session = new HostSession(_effects, _time);

    private List<string> Calls => _effects.Calls;

    private void Join(int connection, string name = "PC-07", string ip = "10.0.0.7")
    {
        _session.Connected(connection, IPAddress.Parse(ip));
        _session.Received(connection, SessionMessage.JoinRequest(name));
    }

    private void JoinAllowed(int connection, string name = "PC-07", string ip = "10.0.0.7")
    {
        bool allow = _session.AllowEveryone;
        _session.AllowEveryone = true;
        Join(connection, name, ip);
        _session.AllowEveryone = allow;
    }

    /// <summary>Lets time pass in 250 ms steps, the server's tick rate; <paramref name="alive"/> send heartbeats.</summary>
    private void Pass(TimeSpan duration, params int[] alive)
    {
        var step = TimeSpan.FromMilliseconds(250);
        for (var passed = TimeSpan.Zero; passed < duration; passed += step)
        {
            _time.Advance(duration - passed < step ? duration - passed : step);
            foreach (var connection in alive)
                _session.Received(connection, SessionMessage.Heartbeat);
            _session.Tick();
        }
    }

    [Fact]
    public void With_allow_everyone_a_join_gets_the_lowest_slot_at_once()
    {
        _session.AllowEveryone = true;
        Join(1);
        Assert.Equal(["plug P2 10.0.0.7", "target+ P2 10.0.0.7", "send #1 Accepted P2"], Calls);
        Assert.Equal(new PlayerInfo(2, "PC-07", IPAddress.Parse("10.0.0.7"), PlayerState.Active), Assert.Single(_session.Players));
        Assert.Equal(1, _effects.PlayersChangedCount);
    }

    [Fact]
    public void Without_allow_everyone_the_host_is_asked()
    {
        Join(1);
        Assert.Equal(["ask #1 PC-07"], Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void Allow_accepts_the_client()
    {
        Join(1);
        Calls.Clear();
        _session.Allow(1);
        Assert.Equal(["close-ask #1", "plug P2 10.0.0.7", "target+ P2 10.0.0.7", "send #1 Accepted P2"], Calls);
    }

    [Fact]
    public void Deny_tells_the_client_and_closes()
    {
        Join(1);
        Calls.Clear();
        _session.Deny(1);
        Assert.Equal(["close-ask #1", "send #1 Denied Denied", "close #1"], Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void An_unanswered_ask_is_denied_after_30s()
    {
        Join(1);
        Calls.Clear();
        Pass(HostSession.AskTimeout - TimeSpan.FromMilliseconds(250), alive: 1);
        Assert.Empty(Calls);
        Pass(TimeSpan.FromMilliseconds(250), alive: 1);
        Assert.Equal(["close-ask #1", "send #1 Denied TimedOut", "close #1"], Calls);
    }

    [Fact]
    public void A_tenth_client_is_told_the_host_is_full()
    {
        for (int i = 1; i <= 9; i++)
            JoinAllowed(i, $"PC-{i}", $"10.0.0.{i}");
        Calls.Clear();
        Join(10, "PC-10", "10.0.0.10");
        Assert.Equal(["send #10 Denied Full", "close #10"], Calls);
        Assert.Equal(9, _session.PlayerCount);
    }

    [Fact]
    public void Allow_after_the_host_filled_up_denies_full()
    {
        Join(1, "PC-01", "10.0.0.1"); // waits on the popup
        for (int i = 2; i <= 10; i++)
            JoinAllowed(i, $"PC-{i}", $"10.0.0.{i}");
        Calls.Clear();
        _session.Allow(1);
        Assert.Equal(["close-ask #1", "send #1 Denied Full", "close #1"], Calls);
        Assert.Equal(9, _session.PlayerCount);
    }

    [Fact]
    public void Allow_for_a_connection_that_is_gone_does_nothing()
    {
        Join(1);
        _session.Disconnected(1);
        Calls.Clear();
        _session.Allow(1);
        _session.Deny(1);
        Assert.Empty(Calls);
    }

    [Fact]
    public void A_freed_slot_is_the_next_one_given_out()
    {
        JoinAllowed(1, "PC-1", "10.0.0.1");
        JoinAllowed(2, "PC-2", "10.0.0.2");
        JoinAllowed(3, "PC-3", "10.0.0.3");
        _session.Received(2, SessionMessage.Leave);
        Calls.Clear();
        JoinAllowed(4, "PC-4", "10.0.0.4");
        Assert.Equal("plug P3 10.0.0.4", Calls[0]);
    }

    [Fact]
    public void Leave_unplugs_and_frees_the_slot()
    {
        JoinAllowed(1);
        Calls.Clear();
        _session.Received(1, SessionMessage.Leave);
        Assert.Equal(["unplug P2", "target- P2", "close #1"], Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void Kick_tells_the_client_unplugs_and_keeps_no_reservation()
    {
        JoinAllowed(1);
        Calls.Clear();
        _session.Kick(2);
        Assert.Equal(["send #1 Kicked", "close #1", "unplug P2", "target- P2"], Calls);
        Assert.Empty(_session.Players);

        Calls.Clear();
        Join(2); // the same PC comes back: the host is asked again
        Assert.Equal(["ask #2 PC-07"], Calls);
    }

    [Fact]
    public void Kick_on_a_reserved_slot_just_unplugs_it()
    {
        JoinAllowed(1);
        _session.Disconnected(1);
        Calls.Clear();
        _session.Kick(2);
        Assert.Equal(["unplug P2"], Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void Five_seconds_of_silence_reserves_the_slot_and_keeps_the_pad_plugged()
    {
        JoinAllowed(1);
        Calls.Clear();
        Pass(HostSession.SilenceLimit - TimeSpan.FromMilliseconds(250));
        Assert.Empty(Calls);
        Pass(TimeSpan.FromMilliseconds(250));
        Assert.Equal(["hold P2", "target- P2", "close #1"], Calls);
        Assert.Equal(PlayerState.Reserved, Assert.Single(_session.Players).State);
    }

    [Fact]
    public void Heartbeats_keep_a_client_in()
    {
        JoinAllowed(1);
        Calls.Clear();
        Pass(TimeSpan.FromSeconds(30), alive: 1);
        Assert.Empty(Calls);
    }

    [Fact]
    public void A_dropped_connection_reserves_the_slot()
    {
        JoinAllowed(1);
        Calls.Clear();
        _session.Disconnected(1);
        Assert.Equal(["hold P2", "target- P2"], Calls);
        Assert.Equal(PlayerState.Reserved, Assert.Single(_session.Players).State);
    }

    [Fact]
    public void The_same_name_rejoining_within_60s_gets_its_slot_back_without_asking()
    {
        JoinAllowed(1);
        _session.Disconnected(1);
        Pass(HostSession.ReserveFor - TimeSpan.FromSeconds(1));
        Calls.Clear();
        Join(2, "PC-07", "10.0.0.9"); // new DHCP lease: a new address is fine
        Assert.Equal(["plug P2 10.0.0.9", "target+ P2 10.0.0.9", "send #2 Accepted P2"], Calls);
        Assert.Equal(new PlayerInfo(2, "PC-07", IPAddress.Parse("10.0.0.9"), PlayerState.Active), Assert.Single(_session.Players));
    }

    [Fact]
    public void A_reservation_expires_after_60s_and_unplugs_the_pad()
    {
        JoinAllowed(1);
        _session.Disconnected(1);
        Calls.Clear();
        Pass(HostSession.ReserveFor - TimeSpan.FromMilliseconds(250));
        Assert.Empty(Calls);
        Pass(TimeSpan.FromMilliseconds(250));
        Assert.Equal(["unplug P2"], Calls);
        Assert.Empty(_session.Players);

        Calls.Clear();
        Join(2);
        Assert.Equal(["ask #2 PC-07"], Calls);
    }

    [Fact]
    public void Same_name_and_ip_takes_over_an_active_slot()
    {
        JoinAllowed(1);
        Calls.Clear();
        Join(2); // the client restarted before the host noticed the old connection was dead
        Assert.Equal(["close #1", "plug P2 10.0.0.7", "target+ P2 10.0.0.7", "send #2 Accepted P2"], Calls);
    }

    [Fact]
    public void The_old_connections_late_disconnect_changes_nothing()
    {
        JoinAllowed(1);
        Join(2);
        Calls.Clear();
        _session.Disconnected(1);
        _session.Received(1, SessionMessage.Leave);
        Assert.Empty(Calls);
        Assert.Equal(PlayerState.Active, Assert.Single(_session.Players).State);
    }

    [Fact]
    public void Same_name_from_another_ip_while_active_goes_to_the_host()
    {
        JoinAllowed(1);
        Calls.Clear();
        Join(2, "PC-07", "10.0.0.99");
        Assert.Equal(["ask #2 PC-07"], Calls);
    }

    [Fact]
    public void A_pending_client_that_disconnects_closes_its_popup()
    {
        Join(1);
        Calls.Clear();
        _session.Disconnected(1);
        Assert.Equal(["close-ask #1"], Calls);
    }

    [Fact]
    public void A_second_request_from_the_same_pc_replaces_the_first()
    {
        Join(1);
        Calls.Clear();
        Join(2);
        Assert.Equal(["close-ask #1", "close #1", "ask #2 PC-07"], Calls);
    }

    [Fact]
    public void A_join_request_twice_on_one_connection_is_ignored()
    {
        JoinAllowed(1);
        Calls.Clear();
        _session.Received(1, SessionMessage.JoinRequest("PC-07"));
        Assert.Empty(Calls);
    }

    [Fact]
    public void A_client_sending_a_host_message_is_dropped()
    {
        JoinAllowed(1);
        Calls.Clear();
        _session.Received(1, SessionMessage.Accepted(3));
        Assert.Equal(["hold P2", "target- P2", "close #1"], Calls);
    }

    [Fact]
    public void A_pad_that_cannot_be_created_denies_the_join()
    {
        _effects.PlugFails = true;
        JoinAllowed(1);
        Assert.Equal(["plug P2 10.0.0.7", "send #1 Denied PadFailed", "close #1"], Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void Stop_ends_every_connection_and_unplugs_every_pad()
    {
        JoinAllowed(1, "PC-1", "10.0.0.1"); // active on P2
        JoinAllowed(2, "PC-2", "10.0.0.2"); // P3, then reserved
        _session.Disconnected(2);
        Join(3, "PC-3", "10.0.0.3");        // pending
        Calls.Clear();

        _session.Stop();

        Assert.Equal(
            ["send #1 HostEnded", "close #1", "close-ask #3", "send #3 HostEnded", "close #3", "unplug P2", "target- P2", "unplug P3"],
            Calls);
        Assert.Empty(_session.Players);
    }

    [Fact]
    public void Players_lists_active_and_reserved_slots_in_order()
    {
        JoinAllowed(1, "PC-1", "10.0.0.1");
        JoinAllowed(2, "PC-2", "10.0.0.2");
        _session.Disconnected(1);
        Assert.Equal(
            [new PlayerInfo(2, "PC-1", IPAddress.Parse("10.0.0.1"), PlayerState.Reserved),
             new PlayerInfo(3, "PC-2", IPAddress.Parse("10.0.0.2"), PlayerState.Active)],
            _session.Players);
        Assert.Equal(2, _session.PlayerCount);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~HostSessionTests"`
Expected: build FAILS: `The type or namespace name 'IHostSessionEffects' could not be found`.

- [ ] **Step 3: Implement**

Create `src/CouchLink.Core/Session/HostSession.cs`:

```csharp
using System.Net;
using CouchLink.Core.Pads;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Session;

public enum PlayerState
{
    Active,
    /// <summary>The client went silent or dropped; its slot and pad are kept for <see cref="HostSession.ReserveFor"/>.</summary>
    Reserved,
}

public readonly record struct PlayerInfo(byte Slot, string Name, IPAddress Address, PlayerState State);

/// <summary>
/// What the session asks the app to do. Called with the session's lock held: return quickly, and
/// never call back into the session (post to the UI thread instead).
/// </summary>
public interface IHostEffects
{
    /// <summary>Plugs in (or rebinds) the slot's pad for this address. False if the pad could not be created.</summary>
    bool PlugPad(byte slot, IPAddress address);
    void HoldPad(byte slot);
    void UnplugPad(byte slot);
    void AddTarget(byte slot, IPAddress address);
    void RemoveTarget(byte slot);
    void AskHost(int connection, string name);
    void CloseAsk(int connection);
    void PlayersChanged();
}

/// <summary>The app's effects plus the transport's.</summary>
public interface IHostSessionEffects : IHostEffects
{
    void Send(int connection, SessionMessage message);

    /// <summary>Closes the connection after what's queued is sent. The session has already forgotten it.</summary>
    void Close(int connection);
}

/// <summary>
/// The host's sessions: who is waiting on the popup, who has which slot, who is gone but may come
/// back. Every slot is free, active (a live connection) or reserved (held for
/// <see cref="ReserveFor"/> with its pad still plugged in, so the game keeps the player). A reserved
/// slot is reclaimed by PC name alone; an active one is taken over only by the same name and address.
/// Not thread-safe: the caller holds one lock around every call.
/// </summary>
public sealed class HostSession
{
    public const int Capacity = PadManager.LastSlot - PadManager.FirstSlot + 1;
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ReserveFor = TimeSpan.FromSeconds(60);

    private sealed class Connection(int id, IPAddress address, long lastHeard)
    {
        public int Id { get; } = id;
        public IPAddress Address { get; } = address;
        public long LastHeard { get; set; } = lastHeard;
        public string? Name { get; set; }
        public long? AskedAt { get; set; } // set while the host is being asked
        public byte Slot { get; set; }     // 0: none
    }

    private sealed class Seat(byte slot, string name, IPAddress address)
    {
        public byte Slot { get; } = slot;
        public string Name { get; } = name;
        public IPAddress Address { get; set; } = address;
        public int? Connection { get; set; } // null while reserved
        public long ReservedAt { get; set; }
    }

    private readonly IHostSessionEffects _effects;
    private readonly TimeProvider _time;
    private readonly Action<string>? _log;
    private readonly Dictionary<int, Connection> _connections = [];
    private readonly SortedDictionary<byte, Seat> _seats = [];

    public HostSession(IHostSessionEffects effects, TimeProvider time, Action<string>? log = null)
    {
        _effects = effects;
        _time = time;
        _log = log;
    }

    /// <summary>Accept joins without asking the host.</summary>
    public bool AllowEveryone { get; set; }

    /// <summary>Active and reserved slots.</summary>
    public int PlayerCount => _seats.Count;

    public IReadOnlyList<PlayerInfo> Players =>
        _seats.Values.Select(s => new PlayerInfo(s.Slot, s.Name, s.Address,
            s.Connection is null ? PlayerState.Reserved : PlayerState.Active)).ToList();

    private long Now => _time.GetTimestamp();

    private TimeSpan Since(long timestamp) => _time.GetElapsedTime(timestamp);

    public void Connected(int connection, IPAddress address) =>
        _connections[connection] = new Connection(connection, address, Now);

    public void Received(int connection, SessionMessage message)
    {
        if (!_connections.TryGetValue(connection, out var c))
            return;
        c.LastHeard = Now;
        switch (message.Type)
        {
            case SessionMessageType.Heartbeat:
                break;
            case SessionMessageType.JoinRequest:
                if (c.Name is null)
                {
                    c.Name = message.Name;
                    Join(c);
                }
                break;
            case SessionMessageType.Leave:
                Leave(c);
                break;
            default:
                _log?.Invoke($"Session: {Describe(c)} sent {message}, which only a host sends; closing");
                Drop(c);
                _effects.Close(c.Id);
                break;
        }
    }

    public void Disconnected(int connection)
    {
        if (!_connections.TryGetValue(connection, out var c))
            return;
        _log?.Invoke($"Session: {Describe(c)} disconnected");
        Drop(c);
    }

    public void Allow(int connection)
    {
        if (!_connections.TryGetValue(connection, out var c) || c.AskedAt is null)
            return;
        c.AskedAt = null;
        _effects.CloseAsk(c.Id);
        _log?.Invoke($"Session: host allowed {Describe(c)}");
        Accept(c);
    }

    public void Deny(int connection)
    {
        if (!_connections.TryGetValue(connection, out var c) || c.AskedAt is null)
            return;
        _log?.Invoke($"Session: host denied {Describe(c)}");
        Refuse(c, DenyReason.Denied);
    }

    public void Kick(byte slot)
    {
        if (!_seats.Remove(slot, out var seat))
            return;
        _log?.Invoke($"Session: host kicked {seat.Name} ({seat.Address}) from P{slot}");
        bool active = seat.Connection is not null;
        if (seat.Connection is { } id && _connections.Remove(id))
        {
            _effects.Send(id, SessionMessage.Kicked);
            _effects.Close(id);
        }
        _effects.UnplugPad(slot);
        if (active)
            _effects.RemoveTarget(slot);
        _effects.PlayersChanged();
    }

    /// <summary>Call every 250 ms: silence, unanswered asks and expired reservations.</summary>
    public void Tick()
    {
        foreach (var c in _connections.Values.Where(c => Since(c.LastHeard) >= SilenceLimit).ToList())
        {
            _log?.Invoke($"Session: {Describe(c)} silent for {SilenceLimit.TotalSeconds:0} s");
            Drop(c);
            _effects.Close(c.Id);
        }
        foreach (var c in _connections.Values.Where(c => c.AskedAt is { } at && Since(at) >= AskTimeout).ToList())
        {
            _log?.Invoke($"Session: no answer for {Describe(c)}; denied");
            Refuse(c, DenyReason.TimedOut);
        }
        foreach (var seat in _seats.Values.Where(s => s.Connection is null && Since(s.ReservedAt) >= ReserveFor).ToList())
        {
            _log?.Invoke($"Session: P{seat.Slot} ({seat.Name}) did not come back; pad unplugged");
            _seats.Remove(seat.Slot);
            _effects.UnplugPad(seat.Slot);
            _effects.PlayersChanged();
        }
    }

    /// <summary>Stop hosting: tell everyone, close everything, unplug every pad.</summary>
    public void Stop()
    {
        foreach (var c in _connections.Values)
        {
            if (c.AskedAt is not null)
                _effects.CloseAsk(c.Id);
            _effects.Send(c.Id, SessionMessage.HostEnded);
            _effects.Close(c.Id);
        }
        _connections.Clear();
        foreach (var seat in _seats.Values)
        {
            _effects.UnplugPad(seat.Slot);
            if (seat.Connection is not null)
                _effects.RemoveTarget(seat.Slot);
        }
        _seats.Clear();
        _effects.PlayersChanged();
        _log?.Invoke("Session: hosting stopped");
    }

    private void Join(Connection c)
    {
        string name = c.Name!;
        _log?.Invoke($"Session: {Describe(c)} asks to join");

        var reserved = _seats.Values.FirstOrDefault(s => s.Connection is null && SameName(s.Name, name));
        if (reserved is not null)
        {
            _log?.Invoke($"Session: {Describe(c)} rejoins P{reserved.Slot}");
            Seat(c, reserved);
            return;
        }

        var active = _seats.Values.FirstOrDefault(s =>
            s.Connection is not null && SameName(s.Name, name) && s.Address.Equals(c.Address));
        if (active is not null)
        {
            int old = active.Connection!.Value;
            _connections.Remove(old);
            _effects.Close(old);
            _log?.Invoke($"Session: {Describe(c)} takes over P{active.Slot} from its old connection");
            Seat(c, active);
            return;
        }

        foreach (var other in _connections.Values
                     .Where(o => o != c && o.AskedAt is not null && SameName(o.Name!, name) && o.Address.Equals(c.Address))
                     .ToList())
        {
            _connections.Remove(other.Id);
            _effects.CloseAsk(other.Id);
            _effects.Close(other.Id);
        }

        if (FreeSlot() is null)
        {
            _log?.Invoke($"Session: {Describe(c)} denied, host is full");
            Refuse(c, DenyReason.Full);
        }
        else if (AllowEveryone)
            Accept(c);
        else
        {
            c.AskedAt = Now;
            _effects.AskHost(c.Id, name);
        }
    }

    private void Accept(Connection c)
    {
        if (FreeSlot() is not { } slot)
        {
            Refuse(c, DenyReason.Full);
            return;
        }
        if (!_effects.PlugPad(slot, c.Address))
        {
            _log?.Invoke($"Session: could not create a pad for {Describe(c)}");
            Refuse(c, DenyReason.PadFailed);
            return;
        }
        _seats[slot] = new Seat(slot, c.Name!, c.Address) { Connection = c.Id };
        c.Slot = slot;
        _effects.AddTarget(slot, c.Address);
        _effects.Send(c.Id, SessionMessage.Accepted(slot));
        _effects.PlayersChanged();
        _log?.Invoke($"Session: {Describe(c)} is P{slot}");
    }

    /// <summary>Puts a connection into an existing seat (a rejoin or a takeover).</summary>
    private void Seat(Connection c, Seat seat)
    {
        if (!_effects.PlugPad(seat.Slot, c.Address))
        {
            _seats.Remove(seat.Slot);
            _effects.UnplugPad(seat.Slot);
            _effects.RemoveTarget(seat.Slot);
            Refuse(c, DenyReason.PadFailed);
            _effects.PlayersChanged();
            return;
        }
        seat.Address = c.Address;
        seat.Connection = c.Id;
        c.Slot = seat.Slot;
        _effects.AddTarget(seat.Slot, c.Address);
        _effects.Send(c.Id, SessionMessage.Accepted(seat.Slot));
        _effects.PlayersChanged();
    }

    private void Refuse(Connection c, DenyReason reason)
    {
        if (c.AskedAt is not null)
            _effects.CloseAsk(c.Id);
        _connections.Remove(c.Id);
        _effects.Send(c.Id, SessionMessage.Denied(reason));
        _effects.Close(c.Id);
    }

    private void Leave(Connection c)
    {
        _log?.Invoke($"Session: {Describe(c)} left");
        _connections.Remove(c.Id);
        if (c.AskedAt is not null)
            _effects.CloseAsk(c.Id);
        if (c.Slot != 0 && _seats.TryGetValue(c.Slot, out var seat) && seat.Connection == c.Id)
        {
            _seats.Remove(c.Slot);
            _effects.UnplugPad(c.Slot);
            _effects.RemoveTarget(c.Slot);
            _effects.PlayersChanged();
        }
        _effects.Close(c.Id);
    }

    /// <summary>The connection is gone (silent, dropped or misbehaving). An active seat is reserved.</summary>
    private void Drop(Connection c)
    {
        _connections.Remove(c.Id);
        if (c.AskedAt is not null)
            _effects.CloseAsk(c.Id);
        if (c.Slot != 0 && _seats.TryGetValue(c.Slot, out var seat) && seat.Connection == c.Id)
        {
            seat.Connection = null;
            seat.ReservedAt = Now;
            _effects.HoldPad(c.Slot);
            _effects.RemoveTarget(c.Slot);
            _effects.PlayersChanged();
            _log?.Invoke($"Session: P{c.Slot} ({seat.Name}) reserved for {ReserveFor.TotalSeconds:0} s");
        }
    }

    private byte? FreeSlot()
    {
        for (byte slot = PadManager.FirstSlot; slot <= PadManager.LastSlot; slot++)
            if (!_seats.ContainsKey(slot))
                return slot;
        return null;
    }

    private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string Describe(Connection c) => $"{c.Name ?? "?"} ({c.Address})";
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~HostSessionTests"`
Expected: PASS (27 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Session/HostSession.cs tests/CouchLink.Core.Tests/HostSessionTests.cs
git commit -m "feat(core): host session: approval, slots, heartbeat silence, 60 s reservations, kick and stop"
```

---

### Task 7: The client session state machine

**Files:**
- Create: `src/CouchLink.Core/Session/ClientSession.cs`
- Test: `tests/CouchLink.Core.Tests/ClientSessionTests.cs`

**Interfaces:**
- Consumes: `SessionMessage`, `DenyReason` (Task 4).
- Produces (namespace `CouchLink.Core.Session`):
  - `enum ClientState { Connecting, Waiting, Playing, Reconnecting, Ended }`
  - `interface IClientSessionEffects { void Connect(); void Send(SessionMessage message); void Disconnect(); void StartPlaying(byte slot); void Ended(string? message); void StateChanged(); }` (`Connect` starts one attempt and reports back through `Connected`/`ConnectFailed`; `Ended(null)` means the user left.)
  - `ClientSession(IClientSessionEffects effects, TimeProvider time, string hostName, string ownName)` with `SilenceLimit` (5 s), `GiveUpAfter` (10 s), `RetryEvery` (1 s), message constants `RequestDenied`, `HostFull`, `NoAnswer`, `PadFailed`, `LostHost`, `KickedMessage`, `HostEndedMessage`, `static string CouldNotReach(string host)`, properties `HostName`, `State`, `Slot`, and methods `Start()`, `Connected()`, `ConnectFailed()`, `Received(SessionMessage)`, `Disconnected()`, `Leave()`, `Fail(string message)`, `Tick()`. Not thread-safe.

- [ ] **Step 1: Write the failing tests**

Create `tests/CouchLink.Core.Tests/ClientSessionTests.cs`:

```csharp
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public class ClientSessionTests
{
    private sealed class RecordingClientEffects : IClientSessionEffects
    {
        public List<string> Calls { get; } = [];
        public Action? OnConnect { get; set; }

        public void Connect()
        {
            Calls.Add("connect");
            OnConnect?.Invoke();
        }

        public void Send(SessionMessage message) => Calls.Add($"send {message}");
        public void Disconnect() => Calls.Add("disconnect");
        public void StartPlaying(byte slot) => Calls.Add($"play P{slot}");
        public void Ended(string? message) => Calls.Add($"ended {message ?? "(left)"}");
        public void StateChanged() { }
    }

    private readonly FakeTimeProvider _time = new();
    private readonly RecordingClientEffects _effects = new();
    private readonly ClientSession _session;

    public ClientSessionTests() => _session = new ClientSession(_effects, _time, hostName: "PC-03", ownName: "PC-07");

    private List<string> Calls => _effects.Calls;

    private void Pass(TimeSpan duration, bool hostAlive = false)
    {
        var step = TimeSpan.FromMilliseconds(250);
        for (var passed = TimeSpan.Zero; passed < duration; passed += step)
        {
            _time.Advance(duration - passed < step ? duration - passed : step);
            if (hostAlive)
                _session.Received(SessionMessage.Heartbeat);
            _session.Tick();
        }
    }

    private void Playing(byte slot = 4)
    {
        _session.Start();
        _session.Connected();
        _session.Received(SessionMessage.Accepted(slot));
        Calls.Clear();
    }

    [Fact]
    public void Start_connects_then_asks_to_join()
    {
        _session.Start();
        Assert.Equal(["connect"], Calls);
        Assert.Equal(ClientState.Connecting, _session.State);

        _session.Connected();
        Assert.Equal(["connect", "send JoinRequest PC-07"], Calls);
        Assert.Equal(ClientState.Waiting, _session.State);
    }

    [Fact]
    public void A_failed_connection_ends_with_could_not_reach()
    {
        _session.Start();
        _session.ConnectFailed();
        Assert.Equal(["connect", "disconnect", "ended Couldn't reach PC-03."], Calls);
        Assert.Equal(ClientState.Ended, _session.State);
    }

    [Fact]
    public void Accepted_starts_playing_in_that_slot()
    {
        _session.Start();
        _session.Connected();
        _session.Received(SessionMessage.Accepted(4));
        Assert.Equal("play P4", Calls[^1]);
        Assert.Equal(ClientState.Playing, _session.State);
        Assert.Equal(4, _session.Slot);
    }

    [Theory]
    [InlineData(DenyReason.Denied, "Request denied.")]
    [InlineData(DenyReason.Full, "Host is full.")]
    [InlineData(DenyReason.TimedOut, "The host didn't answer.")]
    [InlineData(DenyReason.PadFailed, "The host couldn't add a controller for you.")]
    public void Each_denial_has_its_message(DenyReason reason, string message)
    {
        _session.Start();
        _session.Connected();
        Calls.Clear();
        _session.Received(SessionMessage.Denied(reason));
        Assert.Equal(["disconnect", $"ended {message}"], Calls);
    }

    [Fact]
    public void Cancel_while_waiting_tells_the_host_and_ends_with_no_message()
    {
        _session.Start();
        _session.Connected();
        Calls.Clear();
        _session.Leave();
        Assert.Equal(["send Leave", "disconnect", "ended (left)"], Calls);
    }

    [Fact]
    public void Losing_the_connection_while_waiting_ends_with_lost_the_host()
    {
        _session.Start();
        _session.Connected();
        Calls.Clear();
        _session.Disconnected();
        Assert.Equal(["disconnect", "ended Lost the host."], Calls);
    }

    [Fact]
    public void Five_seconds_of_host_silence_while_waiting_ends_the_wait()
    {
        _session.Start();
        _session.Connected();
        Calls.Clear();
        Pass(ClientSession.SilenceLimit);
        Assert.Equal(["disconnect", "ended Lost the host."], Calls);
    }

    [Fact]
    public void Heartbeats_keep_a_session_playing()
    {
        Playing();
        Pass(TimeSpan.FromSeconds(20), hostAlive: true);
        Assert.Empty(Calls);
        Assert.Equal(ClientState.Playing, _session.State);
    }

    [Fact]
    public void Silence_while_playing_reconnects_and_resumes_the_same_slot()
    {
        Playing(4);
        Pass(ClientSession.SilenceLimit - TimeSpan.FromMilliseconds(250));
        Assert.Empty(Calls);
        Pass(TimeSpan.FromMilliseconds(250));
        Assert.Equal(["disconnect", "connect"], Calls);
        Assert.Equal(ClientState.Reconnecting, _session.State);

        _session.Connected();
        _session.Received(SessionMessage.Accepted(4));

        Assert.Equal(["disconnect", "connect", "send JoinRequest PC-07"], Calls); // no second "play"
        Assert.Equal(ClientState.Playing, _session.State);
    }

    [Fact]
    public void A_dropped_connection_while_playing_reconnects()
    {
        Playing();
        _session.Disconnected();
        Assert.Equal(["disconnect", "connect"], Calls);
        Assert.Equal(ClientState.Reconnecting, _session.State);
    }

    [Fact]
    public void Reconnecting_retries_every_second_and_gives_up_at_10s()
    {
        Playing();
        _effects.OnConnect = _session.ConnectFailed; // the host is unreachable
        _session.Disconnected();

        Pass(ClientSession.GiveUpAfter - TimeSpan.FromMilliseconds(250));
        Assert.Equal(ClientState.Reconnecting, _session.State);
        Assert.Equal(10, Calls.Count(c => c == "connect")); // at 0, 1, ... 9 s

        Pass(TimeSpan.FromMilliseconds(250));
        Assert.Equal("ended Lost the host.", Calls[^1]);
        Assert.Equal(10, Calls.Count(c => c == "connect"));
    }

    [Fact]
    public void Coming_back_to_a_different_slot_ends_the_session()
    {
        Playing(4);
        _session.Disconnected();
        _session.Connected();
        Calls.Clear();
        _session.Received(SessionMessage.Accepted(5));
        Assert.Equal(["send Leave", "disconnect", "ended Lost the host."], Calls);
    }

    [Fact]
    public void Kicked_and_host_ended_have_their_messages()
    {
        Playing();
        _session.Received(SessionMessage.Kicked);
        Assert.Equal("ended You were removed by the host.", Calls[^1]);

        var other = new RecordingClientEffects();
        var session = new ClientSession(other, _time, "PC-03", "PC-07");
        session.Start();
        session.Connected();
        session.Received(SessionMessage.Accepted(2));
        session.Received(SessionMessage.HostEnded);
        Assert.Equal("ended Host ended the session.", other.Calls[^1]);
    }

    [Fact]
    public void Fail_tells_the_host_and_ends_with_the_reason()
    {
        Playing();
        _session.Fail("The video window could not start.");
        Assert.Equal(["send Leave", "disconnect", "ended The video window could not start."], Calls);
    }

    [Fact]
    public void Leaving_while_reconnecting_without_a_connection_sends_nothing()
    {
        Playing();
        _session.Disconnected();
        _session.ConnectFailed();
        Calls.Clear();
        _session.Leave();
        Assert.Equal(["disconnect", "ended (left)"], Calls);
    }

    [Fact]
    public void Ended_is_final()
    {
        Playing();
        _session.Received(SessionMessage.Kicked);
        Calls.Clear();
        _session.Received(SessionMessage.HostEnded);
        _session.Disconnected();
        _session.Leave();
        Pass(TimeSpan.FromSeconds(30));
        Assert.Empty(Calls);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~ClientSessionTests"`
Expected: build FAILS: `The type or namespace name 'IClientSessionEffects' could not be found`.

- [ ] **Step 3: Implement**

Create `src/CouchLink.Core/Session/ClientSession.cs`:

```csharp
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Session;

public enum ClientState { Connecting, Waiting, Playing, Reconnecting, Ended }

/// <summary>What the client session asks the app to do. Called with the session's lock held.</summary>
public interface IClientSessionEffects
{
    /// <summary>Starts one connection attempt; report back through Connected or ConnectFailed.</summary>
    void Connect();
    void Send(SessionMessage message);
    void Disconnect();
    void StartPlaying(byte slot);

    /// <summary>The session is over; <paramref name="message"/> says why, or is null when the user left.</summary>
    void Ended(string? message);
    void StateChanged();
}

/// <summary>
/// The client's side of one join: connect, ask, wait for the host, play. If the host goes quiet for
/// <see cref="SilenceLimit"/> or the connection drops while playing, it reconnects once a second and
/// asks again (the host kept the slot), giving up after <see cref="GiveUpAfter"/>. Video and input
/// keep running meanwhile; only the session channel is redone. Not thread-safe.
/// </summary>
public sealed class ClientSession
{
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan RetryEvery = TimeSpan.FromSeconds(1);

    public const string RequestDenied = "Request denied.";
    public const string HostFull = "Host is full.";
    public const string NoAnswer = "The host didn't answer.";
    public const string PadFailed = "The host couldn't add a controller for you.";
    public const string LostHost = "Lost the host.";
    public const string KickedMessage = "You were removed by the host.";
    public const string HostEndedMessage = "Host ended the session.";

    public static string CouldNotReach(string host) => $"Couldn't reach {host}.";

    private readonly IClientSessionEffects _effects;
    private readonly TimeProvider _time;
    private readonly string _ownName;
    private bool _connected, _connecting;
    private long _lastHeard, _lostAt, _lastAttempt;

    public ClientSession(IClientSessionEffects effects, TimeProvider time, string hostName, string ownName)
    {
        _effects = effects;
        _time = time;
        HostName = hostName;
        _ownName = ownName;
    }

    public string HostName { get; }
    public ClientState State { get; private set; } = ClientState.Connecting;
    public byte Slot { get; private set; }

    private long Now => _time.GetTimestamp();

    private TimeSpan Since(long timestamp) => _time.GetElapsedTime(timestamp);

    public void Start() => Attempt();

    public void Connected()
    {
        if (State == ClientState.Ended)
            return;
        _connecting = false;
        _connected = true;
        _lastHeard = Now;
        _effects.Send(SessionMessage.JoinRequest(_ownName));
        if (State == ClientState.Connecting)
            SetState(ClientState.Waiting);
    }

    public void ConnectFailed()
    {
        if (State == ClientState.Ended)
            return;
        _connecting = false;
        if (State == ClientState.Connecting)
            End(CouldNotReach(HostName));
        // Reconnecting: Tick tries again.
    }

    public void Received(SessionMessage message)
    {
        if (State == ClientState.Ended || !_connected)
            return;
        _lastHeard = Now;
        switch (message.Type)
        {
            case SessionMessageType.Accepted when State == ClientState.Waiting:
                Slot = message.Slot;
                SetState(ClientState.Playing);
                _effects.StartPlaying(message.Slot);
                break;
            case SessionMessageType.Accepted when State == ClientState.Reconnecting:
                if (message.Slot == Slot)
                    SetState(ClientState.Playing);
                else
                    Quit(LostHost); // the host forgot us; another slot would be a different player
                break;
            case SessionMessageType.Denied when State == ClientState.Waiting:
                End(DenyMessage(message.Reason));
                break;
            case SessionMessageType.Denied when State == ClientState.Reconnecting:
                End(LostHost);
                break;
            case SessionMessageType.Kicked:
                End(KickedMessage);
                break;
            case SessionMessageType.HostEnded:
                End(HostEndedMessage);
                break;
        }
    }

    public void Disconnected()
    {
        if (State == ClientState.Ended)
            return;
        _connected = false;
        _connecting = false;
        if (State == ClientState.Waiting)
            End(LostHost);
        else if (State == ClientState.Playing)
            Reconnect();
    }

    /// <summary>The user left (Cancel, Leave, Ctrl+Alt+Q).</summary>
    public void Leave() => Quit(null);

    /// <summary>Playing could not start on this PC (e.g. the video window failed).</summary>
    public void Fail(string message) => Quit(message);

    /// <summary>Call every 250 ms.</summary>
    public void Tick()
    {
        switch (State)
        {
            case ClientState.Waiting or ClientState.Playing when _connected && Since(_lastHeard) >= SilenceLimit:
                if (State == ClientState.Waiting)
                    End(LostHost);
                else
                    Reconnect();
                break;
            case ClientState.Reconnecting when Since(_lostAt) >= GiveUpAfter:
                End(LostHost);
                break;
            case ClientState.Reconnecting when !_connected && !_connecting && Since(_lastAttempt) >= RetryEvery:
                Attempt();
                break;
        }
    }

    private void Attempt()
    {
        _connecting = true;
        _lastAttempt = Now;
        _effects.Connect();
    }

    private void Reconnect()
    {
        _connected = false;
        _lostAt = Now;
        SetState(ClientState.Reconnecting);
        _effects.Disconnect();
        Attempt();
    }

    private void Quit(string? message)
    {
        if (State == ClientState.Ended)
            return;
        if (_connected)
            _effects.Send(SessionMessage.Leave);
        End(message);
    }

    private void End(string? message)
    {
        if (State == ClientState.Ended)
            return;
        _connected = _connecting = false;
        State = ClientState.Ended;
        _effects.Disconnect();
        _effects.Ended(message);
        _effects.StateChanged();
    }

    private void SetState(ClientState state)
    {
        State = state;
        _effects.StateChanged();
    }

    private static string DenyMessage(DenyReason reason) => reason switch
    {
        DenyReason.Full => HostFull,
        DenyReason.TimedOut => NoAnswer,
        DenyReason.PadFailed => PadFailed,
        _ => RequestDenied,
    };
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~ClientSessionTests"`
Expected: PASS (19 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Session/ClientSession.cs tests/CouchLink.Core.Tests/ClientSessionTests.cs
git commit -m "feat(core): client session: join, wait, play, reconnect for 10 s, and every way it ends"
```

---

### Task 8: Session server and client over TCP

**Files:**
- Create: `src/CouchLink.Core/Net/SessionConnection.cs`, `src/CouchLink.Core/Net/SessionServer.cs`, `src/CouchLink.Core/Net/SessionClient.cs`
- Test: `tests/CouchLink.Core.Tests/SessionLoopbackTests.cs`

**Interfaces:**
- Consumes: `SessionFraming`, `SessionMessage` (Task 4), `HostSession`, `IHostEffects`, `IHostSessionEffects`, `PlayerInfo` (Task 6).
- Produces:
  - `SessionServer.TryCreate(int port, out SessionServer? server, out string? error) -> bool` (binds only; error when taken: `TCP port {port} is already in use. Is CouchLink already hosting on this PC?`), `int LocalPort`, `void Start(IHostEffects effects, TimeProvider time, Action<string>? log = null, Action<Exception>? onError = null)`, `bool AllowEveryone { get; set; }`, `IReadOnlyList<PlayerInfo> Players`, `int PlayerCount`, `Allow(int)`, `Deny(int)`, `Kick(byte)`, `Dispose()` (sends `HostEnded` to everyone, unplugs through the session, waits up to 1 s for those to go out). Ticks the session every 250 ms; heartbeats every connection every 1 s.
  - `interface ISessionClientEvents { void Connected(); void ConnectFailed(); void Received(SessionMessage message); void Disconnected(); }` (raised on thread-pool threads, never under the client's lock).
  - `SessionClient(IPEndPoint host, ISessionClientEvents events, Action<Exception>? onError = null)` with `ConnectTimeout` (3 s), `HeartbeatInterval` (1 s), `Connect()` (non-blocking; replaces any current connection), `Send(SessionMessage)`, `Disconnect()` (sends what's queued, then closes; raises nothing), `Dispose()`. Heartbeats every second while connected. Events from a connection that was replaced or disconnected are not raised.

- [ ] **Step 1: Write the failing tests**

Create `tests/CouchLink.Core.Tests/SessionLoopbackTests.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using static CouchLink.Core.Tests.AudioTestKit;

namespace CouchLink.Core.Tests;

public class SessionLoopbackTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class AppEffects : IHostEffects
    {
        public ConcurrentQueue<string> Calls { get; } = new();
        public Channel<int> Asks { get; } = Channel.CreateUnbounded<int>();

        public bool PlugPad(byte slot, IPAddress address)
        {
            Calls.Enqueue($"plug P{slot}");
            return true;
        }

        public void HoldPad(byte slot) => Calls.Enqueue($"hold P{slot}");
        public void UnplugPad(byte slot) => Calls.Enqueue($"unplug P{slot}");
        public void AddTarget(byte slot, IPAddress address) => Calls.Enqueue($"target+ P{slot}");
        public void RemoveTarget(byte slot) => Calls.Enqueue($"target- P{slot}");
        public void AskHost(int connection, string name) => Asks.Writer.TryWrite(connection);
        public void CloseAsk(int connection) { }
        public void PlayersChanged() { }
    }

    private sealed class ClientEvents(string name) : ISessionClientEvents
    {
        private readonly Channel<string> _events = Channel.CreateUnbounded<string>();
        private int _heartbeats;

        public SessionClient? Client { get; set; }
        public int Heartbeats => Volatile.Read(ref _heartbeats);
        public bool HasMore => _events.Reader.TryPeek(out _);

        public void Connected()
        {
            _events.Writer.TryWrite("connected");
            Client!.Send(SessionMessage.JoinRequest(name));
        }

        public void ConnectFailed() => _events.Writer.TryWrite("connect failed");

        public void Received(SessionMessage message)
        {
            if (message.Type == SessionMessageType.Heartbeat)
                Interlocked.Increment(ref _heartbeats);
            else
                _events.Writer.TryWrite(message.ToString());
        }

        public void Disconnected() => _events.Writer.TryWrite("disconnected");

        public async Task<string> Next()
        {
            using var cts = new CancellationTokenSource(Timeout);
            return await _events.Reader.ReadAsync(cts.Token);
        }
    }

    private static (SessionServer Server, AppEffects Effects) StartServer(bool allowEveryone, Action<Exception>? onError = null)
    {
        Assert.True(SessionServer.TryCreate(0, out var server, out var error), error);
        var effects = new AppEffects();
        server!.Start(effects, TimeProvider.System, onError: onError);
        server.AllowEveryone = allowEveryone;
        return (server, effects);
    }

    private static (SessionClient Client, ClientEvents Events) Join(SessionServer server, string name = "PC-07")
    {
        var events = new ClientEvents(name);
        var client = new SessionClient(new IPEndPoint(IPAddress.Loopback, server.LocalPort), events);
        events.Client = client;
        client.Connect();
        return (client, events);
    }

    [Fact]
    public async Task A_client_joins_and_is_kicked()
    {
        var (server, _) = StartServer(allowEveryone: true);
        using (server)
        {
            var (client, events) = Join(server);
            using (client)
            {
                Assert.Equal("connected", await events.Next());
                Assert.Equal("Accepted P2", await events.Next());
                Assert.Equal(PlayerState.Active, Assert.Single(server.Players).State);

                server.Kick(2);

                Assert.Equal("Kicked", await events.Next());
                Assert.Equal("disconnected", await events.Next());
                Assert.Empty(server.Players);
            }
        }
    }

    [Fact]
    public async Task The_host_lets_a_client_in_with_Allow()
    {
        var (server, effects) = StartServer(allowEveryone: false);
        using (server)
        {
            var (client, events) = Join(server);
            using (client)
            {
                Assert.Equal("connected", await events.Next());
                using var cts = new CancellationTokenSource(Timeout);
                int connection = await effects.Asks.Reader.ReadAsync(cts.Token);

                server.Allow(connection);

                Assert.Equal("Accepted P2", await events.Next());
                Assert.Equal(["plug P2", "target+ P2"], effects.Calls);
            }
        }
    }

    [Fact]
    public async Task A_dropped_connection_reserves_the_slot_and_the_same_name_gets_it_back()
    {
        var (server, _) = StartServer(allowEveryone: true);
        using (server)
        {
            var (first, events) = Join(server);
            Assert.Equal("connected", await events.Next());
            Assert.Equal("Accepted P2", await events.Next());

            first.Dispose(); // the client vanishes

            await Until(() => server.Players is [{ State: PlayerState.Reserved }]);
            server.AllowEveryone = false; // a rejoin must not need the popup
            var (second, again) = Join(server);
            using (second)
            {
                Assert.Equal("connected", await again.Next());
                Assert.Equal("Accepted P2", await again.Next());
            }
        }
    }

    [Fact]
    public async Task Heartbeats_flow_both_ways_and_keep_a_quiet_client_in()
    {
        var (server, _) = StartServer(allowEveryone: true);
        using (server)
        {
            var (client, events) = Join(server);
            using (client)
            {
                Assert.Equal("connected", await events.Next());
                Assert.Equal("Accepted P2", await events.Next());

                await Task.Delay(HostSession.SilenceLimit + TimeSpan.FromSeconds(1));

                Assert.Equal(PlayerState.Active, Assert.Single(server.Players).State);
                Assert.InRange(events.Heartbeats, 4, 8);
                Assert.False(events.HasMore);
            }
        }
    }

    [Fact]
    public async Task Garbage_closes_only_that_connection()
    {
        var errors = new ConcurrentQueue<Exception>();
        var (server, _) = StartServer(allowEveryone: true, errors.Enqueue);
        using (server)
        {
            var (client, events) = Join(server);
            using (client)
            {
                Assert.Equal("connected", await events.Next());
                Assert.Equal("Accepted P2", await events.Next());

                using var raw = new TcpClient();
                await raw.ConnectAsync(IPAddress.Loopback, server.LocalPort);
                var stream = raw.GetStream();
                await stream.WriteAsync(new byte[] { 0x01, 0x00, 0xAA }); // frame length 1: invalid

                using var cts = new CancellationTokenSource(Timeout);
                var buffer = new byte[64];
                try
                {
                    while (await stream.ReadAsync(buffer, cts.Token) > 0)
                    {
                    }
                }
                catch (IOException)
                {
                    // a reset counts as closed too
                }

                await Until(() => errors.Any(e => e is InvalidDataException));
                Assert.Equal(PlayerState.Active, Assert.Single(server.Players).State);
                Assert.False(events.HasMore);
            }
        }
    }

    [Fact]
    public async Task Stopping_the_server_tells_the_client()
    {
        var (server, effects) = StartServer(allowEveryone: true);
        var (client, events) = Join(server);
        using (client)
        {
            Assert.Equal("connected", await events.Next());
            Assert.Equal("Accepted P2", await events.Next());

            server.Dispose();

            Assert.Equal("HostEnded", await events.Next());
            Assert.Equal("disconnected", await events.Next());
            Assert.Contains("unplug P2", effects.Calls);
        }
    }

    [Fact]
    public async Task Connecting_to_a_closed_port_fails()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var events = new ClientEvents("PC-07");
        using var client = new SessionClient(new IPEndPoint(IPAddress.Loopback, port), events);
        events.Client = client;
        client.Connect();

        Assert.Equal("connect failed", await events.Next());
    }

    [Fact]
    public void A_taken_port_gives_a_message()
    {
        Assert.True(SessionServer.TryCreate(0, out var first, out _));
        using (first)
        {
            int port = first!.LocalPort;
            Assert.False(SessionServer.TryCreate(port, out var second, out var error));
            Assert.Null(second);
            Assert.Equal($"TCP port {port} is already in use. Is CouchLink already hosting on this PC?", error);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~SessionLoopbackTests"`
Expected: build FAILS: `The type or namespace name 'SessionServer' could not be found`.

- [ ] **Step 3: Implement**

Create `src/CouchLink.Core/Net/SessionConnection.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>
/// One TCP session connection. Messages are queued and written by one writer task, so sending never
/// blocks the session's lock. <see cref="CloseAfterSending"/> flushes the queue and then closes,
/// giving up after <see cref="CloseGrace"/> if the peer has stopped reading.
/// </summary>
internal sealed class SessionConnection : IDisposable
{
    private static readonly TimeSpan CloseGrace = TimeSpan.FromSeconds(2);

    private readonly TcpClient _tcp;
    private readonly Channel<byte[]> _outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    public SessionConnection(TcpClient tcp)
    {
        _tcp = tcp;
        _tcp.NoDelay = true;
        Stream = tcp.GetStream();
        Address = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
        Writer = WriteLoopAsync();
    }

    public NetworkStream Stream { get; }
    public IPAddress Address { get; }

    /// <summary>Completes once everything queued is written (or writing failed) and the socket is closed.</summary>
    public Task Writer { get; }

    public void Send(SessionMessage message) => _outgoing.Writer.TryWrite(SessionFraming.Encode(message));

    public void CloseAfterSending()
    {
        if (_outgoing.Writer.TryComplete())
            _ = Task.Delay(CloseGrace).ContinueWith(_ => _tcp.Dispose(), TaskScheduler.Default);
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var bytes in _outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
                await Stream.WriteAsync(bytes).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
        {
            // The peer is gone; the read side reports it.
        }
        finally
        {
            _tcp.Dispose();
        }
    }

    public void Dispose()
    {
        _outgoing.Writer.TryComplete();
        _tcp.Dispose();
    }
}
```

Create `src/CouchLink.Core/Net/SessionServer.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;

namespace CouchLink.Core.Net;

/// <summary>
/// Host side: the TCP session channel. Every connection's messages, the 250 ms tick and the UI's
/// Allow/Deny/Kick go into one <see cref="HostSession"/> under one lock. An exception anywhere is
/// reported to <c>onError</c> and never ends the process; a malformed frame closes only its
/// connection. Bind with <see cref="TryCreate"/>, then <see cref="Start"/>.
/// </summary>
public sealed class SessionServer : IDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);
    private const int TicksPerHeartbeat = 4; // one heartbeat a second

    private readonly TcpListener _listener;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, SessionConnection> _connections = [];
    private readonly CancellationTokenSource _cts = new();
    private HostSession? _session;
    private Action<Exception>? _onError;
    private Task? _acceptLoop;
    private Timer? _timer;
    private int _nextId, _ticks;
    private bool _stopped;

    private SessionServer(TcpListener listener) => _listener = listener;

    /// <summary>Opens the port, or returns a message for the user if it can't be opened.</summary>
    public static bool TryCreate(int port, out SessionServer? server, out string? error)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        try
        {
            listener.Start();
        }
        catch (SocketException e)
        {
            listener.Stop();
            server = null;
            error = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"TCP port {port} is already in use. Is CouchLink already hosting on this PC?"
                : $"Could not open TCP port {port}: {e.Message}";
            return false;
        }
        server = new SessionServer(listener);
        error = null;
        return true;
    }

    public int LocalPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Starts accepting clients. <paramref name="effects"/> run with the server's lock held.</summary>
    public void Start(IHostEffects effects, TimeProvider time, Action<string>? log = null, Action<Exception>? onError = null)
    {
        _onError = onError;
        lock (_gate)
            _session = new HostSession(new Effects(this, effects), time, log);
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        _timer = new Timer(_ => OnTimer(), null, TickInterval, TickInterval);
    }

    public bool AllowEveryone
    {
        get { lock (_gate) return _session?.AllowEveryone ?? false; }
        set => Guard(s => s.AllowEveryone = value);
    }

    public IReadOnlyList<PlayerInfo> Players
    {
        get { lock (_gate) return _session?.Players ?? []; }
    }

    public int PlayerCount
    {
        get { lock (_gate) return _session?.PlayerCount ?? 0; }
    }

    public void Allow(int connection) => Guard(s => s.Allow(connection));

    public void Deny(int connection) => Guard(s => s.Deny(connection));

    public void Kick(byte slot) => Guard(s => s.Kick(slot));

    private void Guard(Action<HostSession> action)
    {
        lock (_gate)
        {
            if (_stopped || _session is null)
                return;
            try
            {
                action(_session);
            }
            catch (Exception e)
            {
                _onError?.Invoke(e);
            }
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient tcp;
            try
            {
                tcp = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException
                                      || (e is SocketException && ct.IsCancellationRequested))
            {
                break;
            }
            catch (SocketException e)
            {
                _onError?.Invoke(e);
                continue;
            }

            SessionConnection connection;
            try
            {
                connection = new SessionConnection(tcp);
            }
            catch (Exception e)
            {
                tcp.Dispose();
                _onError?.Invoke(e);
                continue;
            }

            int id;
            lock (_gate)
            {
                if (_stopped)
                {
                    connection.Dispose();
                    break;
                }
                id = ++_nextId;
                _connections[id] = connection;
            }
            Guard(s => s.Connected(id, connection.Address));
            _ = ReadLoopAsync(id, connection);
        }
    }

    private async Task ReadLoopAsync(int id, SessionConnection connection)
    {
        try
        {
            while (await SessionFraming.ReadAsync(connection.Stream, CancellationToken.None).ConfigureAwait(false) is { } message)
                Guard(s => s.Received(id, message));
        }
        catch (InvalidDataException e)
        {
            _onError?.Invoke(new InvalidDataException($"Session connection from {connection.Address}: {e.Message}", e));
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
        {
            // Dropped, or closed by us.
        }

        bool known;
        lock (_gate)
            known = _connections.Remove(id);
        if (known)
        {
            connection.Dispose();
            Guard(s => s.Disconnected(id));
        }
    }

    private void OnTimer()
    {
        Guard(s => s.Tick());
        if (Interlocked.Increment(ref _ticks) % TicksPerHeartbeat != 0)
            return;
        lock (_gate)
            foreach (var connection in _connections.Values)
                connection.Send(SessionMessage.Heartbeat);
    }

    private void SendTo(int id, SessionMessage message)
    {
        if (_connections.TryGetValue(id, out var connection))
            connection.Send(message);
    }

    private void CloseConnection(int id)
    {
        if (_connections.Remove(id, out var connection))
            connection.CloseAfterSending();
    }

    public void Dispose()
    {
        List<SessionConnection> closing;
        lock (_gate)
        {
            if (_stopped)
                return;
            closing = _connections.Values.ToList();
            try
            {
                _session?.Stop(); // queues HostEnded and closes every connection
            }
            catch (Exception e)
            {
                _onError?.Invoke(e);
            }
            _stopped = true;
        }
        _timer?.Dispose();
        Task.WhenAll(closing.Select(c => c.Writer)).Wait(TimeSpan.FromSeconds(1));
        _cts.Cancel();
        _listener.Stop();
        foreach (var connection in closing)
            connection.Dispose();
        _acceptLoop?.Wait(TimeSpan.FromSeconds(1));
    }

    /// <summary>Called by the session with <see cref="_gate"/> held.</summary>
    private sealed class Effects(SessionServer server, IHostEffects app) : IHostSessionEffects
    {
        public bool PlugPad(byte slot, IPAddress address) => app.PlugPad(slot, address);
        public void HoldPad(byte slot) => app.HoldPad(slot);
        public void UnplugPad(byte slot) => app.UnplugPad(slot);
        public void AddTarget(byte slot, IPAddress address) => app.AddTarget(slot, address);
        public void RemoveTarget(byte slot) => app.RemoveTarget(slot);
        public void AskHost(int connection, string name) => app.AskHost(connection, name);
        public void CloseAsk(int connection) => app.CloseAsk(connection);
        public void PlayersChanged() => app.PlayersChanged();
        public void Send(int connection, SessionMessage message) => server.SendTo(connection, message);
        public void Close(int connection) => server.CloseConnection(connection);
    }
}
```

Create `src/CouchLink.Core/Net/SessionClient.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>Raised on thread-pool threads, never with the client's lock held.</summary>
public interface ISessionClientEvents
{
    void Connected();
    void ConnectFailed();
    void Received(SessionMessage message);
    void Disconnected();
}

/// <summary>
/// Client side: one TCP connection to the host's session port at a time, with a heartbeat every
/// second while connected. Each <see cref="Connect"/> starts a new generation; events from an older
/// connection, or after <see cref="Disconnect"/>, are not raised.
/// </summary>
public sealed class SessionClient : IDisposable
{
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(1);

    private readonly IPEndPoint _host;
    private readonly ISessionClientEvents _events;
    private readonly Action<Exception>? _onError;
    private readonly Lock _gate = new();
    private readonly Timer _heartbeat;
    private int _generation;
    private SessionConnection? _connection;
    private CancellationTokenSource? _connecting;
    private bool _disposed;

    public SessionClient(IPEndPoint host, ISessionClientEvents events, Action<Exception>? onError = null)
    {
        _host = host;
        _events = events;
        _onError = onError;
        _heartbeat = new Timer(_ => Send(SessionMessage.Heartbeat), null, HeartbeatInterval, HeartbeatInterval);
    }

    /// <summary>Starts a connection attempt; reports Connected or ConnectFailed.</summary>
    public void Connect()
    {
        int generation;
        CancellationToken token;
        lock (_gate)
        {
            if (_disposed)
                return;
            CloseCurrent();
            generation = ++_generation;
            _connecting = new CancellationTokenSource();
            token = _connecting.Token;
        }
        _ = Task.Run(() => RunAsync(generation, token));
    }

    public void Send(SessionMessage message)
    {
        lock (_gate)
            _connection?.Send(message);
    }

    /// <summary>Sends what's queued, then closes. Raises nothing.</summary>
    public void Disconnect()
    {
        lock (_gate)
        {
            _generation++;
            CloseCurrent();
        }
    }

    // Cancels only a connect in progress: an established connection closes by flushing, so a Leave
    // queued just before still goes out.
    private void CloseCurrent()
    {
        _connecting?.Cancel();
        _connecting?.Dispose();
        _connecting = null;
        _connection?.CloseAfterSending();
        _connection = null;
    }

    private bool IsCurrent(int generation)
    {
        lock (_gate)
            return generation == _generation && !_disposed;
    }

    private async Task RunAsync(int generation, CancellationToken token)
    {
        var tcp = new TcpClient(AddressFamily.InterNetwork);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(ConnectTimeout);
            await tcp.ConnectAsync(_host, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException or ObjectDisposedException)
        {
            tcp.Dispose();
            if (IsCurrent(generation))
                Raise(_events.ConnectFailed);
            return;
        }

        SessionConnection connection;
        lock (_gate)
        {
            if (generation != _generation || _disposed)
            {
                tcp.Dispose();
                return;
            }
            _connecting?.Dispose();
            _connecting = null;
            connection = _connection = new SessionConnection(tcp);
        }
        Raise(_events.Connected);

        try
        {
            while (await SessionFraming.ReadAsync(connection.Stream, CancellationToken.None).ConfigureAwait(false) is { } message)
            {
                if (!IsCurrent(generation))
                    return;
                Raise(() => _events.Received(message));
            }
        }
        catch (InvalidDataException e)
        {
            _onError?.Invoke(e);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
        {
            // Dropped, or closed by us.
        }

        bool current;
        lock (_gate)
        {
            current = generation == _generation && ReferenceEquals(_connection, connection);
            if (current)
                _connection = null;
        }
        if (current)
        {
            connection.Dispose();
            Raise(_events.Disconnected);
        }
    }

    private void Raise(Action raise)
    {
        try
        {
            raise();
        }
        catch (Exception e)
        {
            _onError?.Invoke(e);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _generation++;
            CloseCurrent();
        }
        _heartbeat.Dispose();
    }
}
```

`SessionConnection` is `internal`; both users are in `CouchLink.Core`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~SessionLoopbackTests"`
Expected: PASS (8 tests; the heartbeat test takes about 6 s).
Run: `dotnet test tests/CouchLink.Core.Tests`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Net/SessionConnection.cs src/CouchLink.Core/Net/SessionServer.cs src/CouchLink.Core/Net/SessionClient.cs tests/CouchLink.Core.Tests/SessionLoopbackTests.cs
git commit -m "feat(core): session server and client over TCP 47801 with heartbeats"
```

---

### Task 9: "Reconnecting..." on the player

The fullscreen player stays open while the session reconnects; it shows the session's line instead of the picture status.

**Files:**
- Modify: `src/CouchLink.Core/Video/OverlayText.cs`, `src/CouchLink.Video/PlayerCore.cs`, `src/CouchLink.Video/VideoPlayer.cs`
- Test: `tests/CouchLink.Core.Tests/OverlayTextTests.cs`, `tests/CouchLink.Video.Tests/PlayerCoreTests.cs`

**Interfaces:**
- Produces: `OverlayText.Reconnecting = "Reconnecting..."`; `OverlayText.Status(bool anyFrameShown, bool hostPaused, TimeSpan sinceLastFrame, TimeSpan quietAfter, string? session = null)` returns `"{session}\n{LeaveHint}"` whenever `session` is not null. `PlayerCore` and `VideoPlayer` constructors take a last optional parameter `Func<string?>? sessionStatus = null`.

- [ ] **Step 1: Write the failing tests**

In `tests/CouchLink.Core.Tests/OverlayTextTests.cs` add:

```csharp
    [Fact]
    public void A_session_line_overrides_the_picture_status()
    {
        var expected = $"{OverlayText.Reconnecting}\n{OverlayText.LeaveHint}";
        Assert.Equal(expected, OverlayText.Status(true, false, TimeSpan.Zero, TimeSpan.FromSeconds(2), OverlayText.Reconnecting));
        Assert.Equal(expected, OverlayText.Status(false, true, TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(2), OverlayText.Reconnecting));
        Assert.Null(OverlayText.Status(true, false, TimeSpan.Zero, TimeSpan.FromSeconds(2), session: null));
    }
```

In `tests/CouchLink.Video.Tests/PlayerCoreTests.cs` add a field `private string? _session;`, change the end of the `Core()` helper from `_time, audioLine: () => _audio);` to `_time, audioLine: () => _audio, sessionStatus: () => _session);`, and add:

```csharp
    [Fact]
    public void Session_status_overrides_the_picture_status()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();
        Assert.Null(_presenter.Shown[^1].Status);

        _session = OverlayText.Reconnecting;
        core.Run();                          // status changed: redraw at once

        Assert.Equal($"{OverlayText.Reconnecting}\n{OverlayText.LeaveHint}", _presenter.Shown[^1].Status);
        Assert.Equal(1u, ShownFrame(_presenter.Shown[^1])); // over the last picture
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~OverlayTextTests"`
Expected: build FAILS: `'OverlayText' does not contain a definition for 'Reconnecting'`.

- [ ] **Step 3: Implement**

In `src/CouchLink.Core/Video/OverlayText.cs` add `public const string Reconnecting = "Reconnecting...";` after `LeaveHint`, and replace `Status` (with its summary) by:

```csharp
    /// <summary>
    /// A centred message when there is no live picture, or null. A session line (e.g.
    /// "Reconnecting...") wins over everything. When nothing is coming (yet), it also says how to
    /// leave: the fullscreen player has no visible controls.
    /// </summary>
    public static string? Status(bool anyFrameShown, bool hostPaused, TimeSpan sinceLastFrame, TimeSpan quietAfter,
        string? session = null) =>
        session is not null ? $"{session}\n{LeaveHint}"
        : !anyFrameShown ? $"{Waiting}\n{LeaveHint}"
        : sinceLastFrame >= quietAfter ? $"{NoPicture}\n{LeaveHint}"
        : hostPaused ? Paused
        : null;
```

In `src/CouchLink.Video/PlayerCore.cs`:
1. Add a field `private readonly Func<string?>? _sessionStatus;` after `_audioLine`.
2. Add a last constructor parameter `Func<string?>? sessionStatus = null` and assign `_sessionStatus = sessionStatus;`.
3. In `Run`, change the status line to:

```csharp
        string? status = OverlayText.Status(FramesShown > 0 || newest is not null, _stats().HostPaused, sinceLastFrame,
            QuietAfter, _sessionStatus?.Invoke());
```

In `src/CouchLink.Video/VideoPlayer.cs`:
1. Add a last constructor parameter `Func<string?>? sessionStatus = null` and pass it into the thread lambda: `Run(options, stats, decodeFailed, closeRequested, log, audioLine, sessionStatus)`.
2. Add the same last parameter to the private `Run` method and pass it on: `new PlayerCore(..., TimeProvider.System, log, audioLine, sessionStatus)`.
3. Add to the class summary: `<c>sessionStatus</c>, when it returns text, replaces the picture status (e.g. "Reconnecting...").`

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~OverlayTextTests"` and `dotnet test tests/CouchLink.Video.Tests --filter "FullyQualifiedName~PlayerCoreTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Video/OverlayText.cs src/CouchLink.Video/PlayerCore.cs src/CouchLink.Video/VideoPlayer.cs tests/CouchLink.Core.Tests/OverlayTextTests.cs tests/CouchLink.Video.Tests/PlayerCoreTests.cs
git commit -m "feat(video): the player shows the session's Reconnecting line over the last picture"
```

---

### Task 10: The host side of the app: Start screen, host lobby, approval popup

WPF glue has no unit tests in this repo; each app task ends with a Release build and a manual check. After this task the Join button is disabled until Task 11, and the old dev join path is gone.

**Files:**
- Create: `src/CouchLink.App/HostService.cs` (replaces `src/CouchLink.App/HostInputService.cs`, which is deleted)
- Create: `src/CouchLink.App/ApprovalPopup.cs`
- Create: `src/CouchLink.App/Views/StartView.xaml`, `StartView.xaml.cs`, `HostLobbyView.xaml`, `HostLobbyView.xaml.cs`
- Rewrite: `src/CouchLink.App/MainWindow.xaml`, `src/CouchLink.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `SessionServer` (Task 8), `DiscoveryBroadcaster` (Task 5), `HostAnnounce`, `PcName` (Task 3), `HostSession.Capacity`, `IHostEffects`, `PlayerInfo`, `PlayerState` (Task 6), `PadManager.Plug/Hold/Unplug/Handle(packet, from)` (Task 2), `HostVideo/HostAudio.AddTarget/RemoveTarget` (Task 1), `Ports.Session/Discovery/Input` (Task 3).
- Produces:
  - `internal interface IHostUi { void PlayersChanged(); void AskHost(int connection, string name); void CloseAsk(int connection); }` (called from network threads).
  - `HostService.TryStart(StreamSettings settings, IHostUi ui, out HostService? service, out string? error)`, `IReadOnlyList<PlayerInfo> Players`, `bool AllowEveryone { set; }`, `Allow(int)`, `Deny(int)`, `Kick(byte)`, `ChangeSettings(StreamSettings)`, `int PadCount`, `string DescribeStreams()`, `string? LastError`, `Dispose()`.
  - `ApprovalPopup(string name, int stackIndex, Action allow, Action deny)`, `CloseByHost()`.
  - `StartView` events `HostClicked`, `JoinClicked`, `CrashReportsClicked`. `HostLobbyView` with `bool TryStart(out string? error)`, event `Stopped`, `IDisposable`.

- [ ] **Step 1: Host service**

Delete `src/CouchLink.App/HostInputService.cs`. Create `src/CouchLink.App/HostService.cs`:

```csharp
using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Pads;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using CouchLink.Core.Video;
using CouchLink.Pads;

namespace CouchLink.App;

/// <summary>What the host lobby shows. Called from network threads: post to the UI thread.</summary>
internal interface IHostUi
{
    void PlayersChanged();
    void AskHost(int connection, string name);
    void CloseAsk(int connection);
}

/// <summary>
/// Host side. The session server on TCP 47801 decides who is in; this plugs their pads (bound to
/// their address), applies their input from UDP 47803, streams video (the screen, or --test-pattern)
/// and sound (loopback, or --test-tone) to them, and announces the host on UDP 47800. Video can be
/// restarted with new settings while clients stay in.
/// </summary>
internal sealed class HostService : IDisposable, IHostEffects
{
    private readonly ViGEmPadFactory _factory;
    private readonly PadManager _pads;
    private readonly InputReceiver _receiver;
    private readonly SessionServer _server;
    private readonly DiscoveryBroadcaster _broadcaster;
    private readonly HostAudio _audio;
    private readonly IHostUi _ui;
    private readonly Lock _media = new();
    private readonly Dictionary<byte, IPAddress> _targets = [];
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoop;
    private readonly Timer _staleTimer;
    private HostVideo _video;

    private HostService(InputReceiver receiver, SessionServer server, ViGEmPadFactory factory, StreamSettings settings, IHostUi ui)
    {
        _ui = ui;
        _factory = factory;
        _pads = new PadManager(factory, TimeProvider.System);
        _receiver = receiver;
        _server = server;
        _video = HostVideo.Start(settings, OnVideoError);
        _audio = HostAudio.Start(OnAudioError);
        _receiveLoop = _receiver.RunAsync((packet, from) => _pads.Handle(packet, from), _cts.Token, OnError,
            onKeyframeRequest: (_, _) => { lock (_media) _video.RequestKeyframe(); },
            onTimingPing: (ping, from) => { lock (_media) _video.ReplyToTimingPing(ping, from); });
        _staleTimer = new Timer(_ => ReleaseStale(), null, PadManager.CheckInterval, PadManager.CheckInterval);
        _server.Start(this, TimeProvider.System, message => AppServices.Log.Write(message), OnError);
        _broadcaster = new DiscoveryBroadcaster(Ports.Discovery,
            () => HostAnnounce.For(_server.PlayerCount, HostSession.Capacity, PcName.ThisPc), onError: OnError);
    }

    public static bool TryStart(StreamSettings settings, IHostUi ui, out HostService? service, out string? error)
    {
        service = null;
        // Ports first: they are the steps most likely to fail, and nothing needs cleaning up yet.
        if (!InputReceiver.TryCreate(Ports.Input, out var receiver, out error))
            return false;
        if (!SessionServer.TryCreate(Ports.Session, out var server, out error))
        {
            receiver!.Dispose();
            return false;
        }
        if (!ViGEmPadFactory.TryCreate(out var factory, out error))
        {
            server!.Dispose();
            receiver!.Dispose();
            return false;
        }
        service = new HostService(receiver!, server!, factory!, settings, ui);
        return true;
    }

    public IReadOnlyList<PlayerInfo> Players => _server.Players;

    public bool AllowEveryone
    {
        set => _server.AllowEveryone = value;
    }

    public int PadCount => _pads.Count;

    /// <summary>Most recent pad, session, video or audio error, shown to the host instead of failing silently.</summary>
    public string? LastError { get; private set; }

    public void Allow(int connection) => _server.Allow(connection);

    public void Deny(int connection) => _server.Deny(connection);

    public void Kick(byte slot) => _server.Kick(slot);

    /// <summary>Restarts video with new settings; clients stay in and get a keyframe. UI thread.</summary>
    public void ChangeSettings(StreamSettings settings)
    {
        lock (_media)
        {
            _video.Dispose();
            _video = HostVideo.Start(settings, OnVideoError);
            foreach (var (slot, address) in _targets)
                _video.AddTarget(slot, address); // forces a keyframe
        }
        AppServices.Log.Write($"Stream settings changed: {StreamSettings.Label(settings.Resolution)} at {settings.FrameRate} fps");
    }

    public string DescribeStreams()
    {
        lock (_media)
            return $"{_video.Describe()}\n{_audio.Describe()}";
    }

    // IHostEffects: called by the session with its lock held.

    public bool PlugPad(byte slot, IPAddress address)
    {
        try
        {
            return _pads.Plug(slot, address);
        }
        catch (Exception e)
        {
            OnError(e); // e.g. ViGEmBus refused another pad; the client is told
            return false;
        }
    }

    public void HoldPad(byte slot) => _pads.Hold(slot);

    public void UnplugPad(byte slot)
    {
        try
        {
            _pads.Unplug(slot);
        }
        catch (Exception e)
        {
            OnError(e);
        }
    }

    public void AddTarget(byte slot, IPAddress address)
    {
        lock (_media)
        {
            _targets[slot] = address;
            _video.AddTarget(slot, address);
            _audio.AddTarget(slot, address);
        }
    }

    public void RemoveTarget(byte slot)
    {
        lock (_media)
        {
            _targets.Remove(slot);
            _video.RemoveTarget(slot);
            _audio.RemoveTarget(slot);
        }
    }

    public void AskHost(int connection, string name) => _ui.AskHost(connection, name);

    public void CloseAsk(int connection) => _ui.CloseAsk(connection);

    public void PlayersChanged() => _ui.PlayersChanged();

    private void ReleaseStale()
    {
        try
        {
            _pads.ReleaseStale();
        }
        catch (Exception e)
        {
            OnError(e); // an unhandled exception here would kill the host process
        }
    }

    private void OnError(Exception e)
    {
        LastError = $"{e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Host error: {e}");
    }

    private void OnVideoError(Exception e)
    {
        LastError = $"Video: {e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Video error: {e}");
    }

    private void OnAudioError(Exception e)
    {
        LastError = $"Audio: {e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Audio error: {e}");
    }

    public void Dispose()
    {
        _broadcaster.Dispose(); // stop advertising first
        _server.Dispose();      // tells every client, unplugs every pad through the session
        _cts.Cancel();
        _staleTimer.Dispose();
        _receiveLoop.Wait(TimeSpan.FromSeconds(2));
        lock (_media)
            _video.Dispose();
        _audio.Dispose();
        _receiver.Dispose();
        _pads.Dispose();
        _factory.Dispose();
    }
}
```

- [ ] **Step 2: Approval popup**

Create `src/CouchLink.App/ApprovalPopup.cs`:

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CouchLink.Core.Session;

namespace CouchLink.App;

/// <summary>
/// "PC-07 wants to join. Allow / Deny", topmost in the bottom-right corner over the game, without
/// taking the keyboard from it; the taskbar button flashes. Several stack upwards. Closing it with
/// the X denies. The session denies by itself after 30 s and closes it with <see cref="CloseByHost"/>.
/// </summary>
internal sealed partial class ApprovalPopup : Window
{
    private const uint FLASHW_ALL = 3, FLASHW_TIMERNOFG = 12;

    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Stopwatch _shown = Stopwatch.StartNew();
    private readonly TextBlock _remaining;
    private bool _answered;

    public ApprovalPopup(string name, int stackIndex, Action allow, Action deny)
    {
        Title = "CouchLink";
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowActivated = false;

        var panel = new StackPanel { Margin = new Thickness(16), MinWidth = 300 };
        panel.Children.Add(new TextBlock { Text = $"{name} wants to join.", FontSize = 16, FontWeight = FontWeights.SemiBold });
        _remaining = new TextBlock { Margin = new Thickness(0, 4, 0, 0), Foreground = Brushes.Gray };
        panel.Children.Add(_remaining);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        buttons.Children.Add(MakeButton("Allow", () => Answer(allow)));
        buttons.Children.Add(MakeButton("Deny", () => Answer(deny)));
        panel.Children.Add(buttons);
        Content = panel;

        UpdateRemaining();
        _countdown.Tick += (_, _) => UpdateRemaining();
        _countdown.Start();
        Loaded += (_, _) => PlaceAt(stackIndex);
        SourceInitialized += (_, _) => Flash();
        Closed += (_, _) =>
        {
            _countdown.Stop();
            if (!_answered)
                deny();
        };
    }

    /// <summary>The session already answered (timed out, or the client gave up): close without denying again.</summary>
    public void CloseByHost()
    {
        _answered = true;
        Close();
    }

    private void Answer(Action answer)
    {
        _answered = true;
        answer();
        Close();
    }

    private void UpdateRemaining()
    {
        var left = HostSession.AskTimeout - _shown.Elapsed;
        _remaining.Text = $"Denied automatically in {Math.Max(0, (int)Math.Ceiling(left.TotalSeconds))} s";
    }

    private void PlaceAt(int stackIndex)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = Math.Max(area.Top, area.Bottom - (ActualHeight + 8) * (stackIndex + 1) - 8);
    }

    private void Flash()
    {
        var info = new FlashInfo
        {
            Size = (uint)Marshal.SizeOf<FlashInfo>(),
            Hwnd = new WindowInteropHelper(this).Handle,
            Flags = FLASHW_ALL | FLASHW_TIMERNOFG,
        };
        FlashWindowEx(ref info);
    }

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Content = text, MinWidth = 88, Height = 32, Margin = new Thickness(8, 0, 0, 0) };
        button.Click += (_, _) => click();
        return button;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashInfo
    {
        public uint Size;
        public nint Hwnd;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool FlashWindowEx(ref FlashInfo info);
}
```

- [ ] **Step 3: Start view and host lobby view**

Create `src/CouchLink.App/Views/StartView.xaml`:

```xml
<UserControl x:Class="CouchLink.App.Views.StartView" x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Margin="24">
        <StackPanel VerticalAlignment="Center">
            <TextBlock Text="CouchLink" FontSize="32" FontWeight="SemiBold" HorizontalAlignment="Center"/>
            <TextBlock Text="Couch co-op over the LAN" Foreground="Gray" HorizontalAlignment="Center" Margin="0,4,0,0"/>
            <Button x:Name="HostButton" Content="Host" Height="72" FontSize="24" Margin="0,32,0,0"/>
            <Button x:Name="JoinButton" Content="Join" Height="72" FontSize="24" Margin="0,12,0,0" IsEnabled="False"/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,24,0,0">
                <Button x:Name="ControlsButton" Content="⚙ Controls" Padding="12,4" IsEnabled="False"
                        ToolTip="Changing keys arrives in a later version" ToolTipService.ShowOnDisabled="True"/>
                <Button x:Name="CrashReportsButton" Content="Crash reports" Padding="12,4" Margin="8,0,0,0"/>
            </StackPanel>
        </StackPanel>
    </Grid>
</UserControl>
```

Create `src/CouchLink.App/Views/StartView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace CouchLink.App.Views;

/// <summary>The first screen: big Host and Join buttons.</summary>
internal sealed partial class StartView : UserControl
{
    public StartView()
    {
        InitializeComponent();
        HostButton.Click += (_, _) => HostClicked?.Invoke();
        JoinButton.Click += (_, _) => JoinClicked?.Invoke();
        CrashReportsButton.Click += (_, _) => CrashReportsClicked?.Invoke();
    }

    public event Action? HostClicked;
    public event Action? JoinClicked;
    public event Action? CrashReportsClicked;
}
```

Create `src/CouchLink.App/Views/HostLobbyView.xaml`:

```xml
<UserControl x:Class="CouchLink.App.Views.HostLobbyView" x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel Margin="16">
        <TextBlock x:Name="Heading" DockPanel.Dock="Top" FontSize="22" FontWeight="SemiBold" TextWrapping="Wrap"/>
        <TextBlock DockPanel.Dock="Top" Foreground="Gray" Margin="0,4,0,0" TextWrapping="Wrap"
                   Text="Minimize this window and start the game. Friends pick this PC under Join."/>
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="0,12,0,0">
            <TextBlock Text="Stream" VerticalAlignment="Center"/>
            <ComboBox x:Name="ResolutionBox" Width="90" Margin="8,0"/>
            <ComboBox x:Name="FrameRateBox" Width="80"/>
        </StackPanel>
        <CheckBox x:Name="AllowEveryoneBox" DockPanel.Dock="Top" Margin="0,12,0,0"
                  Content="Allow everyone (no popup when someone joins)"/>
        <Button x:Name="StopButton" DockPanel.Dock="Bottom" Content="Stop hosting" Height="40" Margin="0,12,0,0"/>
        <Expander DockPanel.Dock="Bottom" Header="Details" Margin="0,12,0,0">
            <TextBlock x:Name="DetailsText" FontFamily="Consolas" TextWrapping="Wrap" Margin="0,4,0,0"/>
        </Expander>
        <TextBlock x:Name="PlayersHeading" DockPanel.Dock="Top" Margin="0,16,0,4" FontWeight="SemiBold"/>
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <StackPanel x:Name="PlayerList"/>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

Create `src/CouchLink.App/Views/HostLobbyView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using CouchLink.Core.Video;
using CouchLink.Video;

namespace CouchLink.App.Views;

/// <summary>
/// "Hosting on PC-03": stream settings, Allow everyone, the players with Kick, Stop hosting, and a
/// Details section with the dev stats. Owns the <see cref="HostService"/> and the approval popups.
/// </summary>
internal sealed partial class HostLobbyView : UserControl, IHostUi, IDisposable
{
    private readonly DispatcherTimer _details = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Dictionary<int, ApprovalPopup> _asks = [];
    private HostService? _host;

    public HostLobbyView()
    {
        InitializeComponent();
        Heading.Text = $"Hosting on {PcName.ThisPc}";
        foreach (var resolution in StreamSettings.Resolutions)
            ResolutionBox.Items.Add(new ComboBoxItem { Content = StreamSettings.Label(resolution), Tag = resolution });
        ResolutionBox.SelectedIndex = StreamSettings.Resolutions.ToList().IndexOf(StreamSettings.Default.Resolution);
        foreach (int rate in StreamSettings.FrameRatesFor(DisplayInfo.PrimaryRefreshRate()))
            FrameRateBox.Items.Add(new ComboBoxItem { Content = $"{rate} fps", Tag = rate });
        FrameRateBox.SelectedIndex = 0; // 60
        _details.Tick += (_, _) => UpdateDetails();
    }

    /// <summary>The host clicked Stop hosting; the view has already cleaned up.</summary>
    public event Action? Stopped;

    public bool TryStart(out string? error)
    {
        if (!HostService.TryStart(CurrentSettings(), this, out _host, out error))
            return false;
        ResolutionBox.SelectionChanged += (_, _) => _host?.ChangeSettings(CurrentSettings());
        FrameRateBox.SelectionChanged += (_, _) => _host?.ChangeSettings(CurrentSettings());
        AllowEveryoneBox.Click += (_, _) =>
        {
            if (_host is not null)
                _host.AllowEveryone = AllowEveryoneBox.IsChecked == true;
        };
        StopButton.Click += (_, _) =>
        {
            Dispose();
            Stopped?.Invoke();
        };
        AppServices.DescribeMode = () => $"Host (virtual pads: {_host?.PadCount ?? 0})";
        AppServices.Log.Write("Hosting started");
        RefreshPlayers();
        UpdateDetails();
        _details.Start();
        return true;
    }

    private StreamSettings CurrentSettings() => new(
        (StreamResolution)((ComboBoxItem)ResolutionBox.SelectedItem).Tag,
        (int)((ComboBoxItem)FrameRateBox.SelectedItem).Tag);

    void IHostUi.PlayersChanged() => Dispatcher.InvokeAsync(RefreshPlayers);

    void IHostUi.AskHost(int connection, string name) => Dispatcher.InvokeAsync(() => Ask(connection, name));

    void IHostUi.CloseAsk(int connection) => Dispatcher.InvokeAsync(() =>
    {
        if (_asks.Remove(connection, out var popup))
            popup.CloseByHost();
    });

    private void Ask(int connection, string name)
    {
        if (_host is not { } host)
            return;
        var popup = new ApprovalPopup(name, _asks.Count, () => host.Allow(connection), () => host.Deny(connection));
        _asks[connection] = popup;
        popup.Closed += (_, _) => _asks.Remove(connection);
        popup.Show();
    }

    private void RefreshPlayers()
    {
        if (_host is null)
            return;
        var players = _host.Players;
        PlayersHeading.Text = $"Players: {players.Count + 1}/{HostSession.Capacity + 1} (you are P1)";
        PlayerList.Children.Clear();
        if (players.Count == 0)
        {
            PlayerList.Children.Add(new TextBlock
            {
                Text = "No one has joined yet.",
                Foreground = Brushes.Gray,
            });
        }
        foreach (var player in players)
        {
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var kick = new Button { Content = "Kick", Padding = new Thickness(12, 2, 12, 2) };
            byte slot = player.Slot;
            kick.Click += (_, _) => _host?.Kick(slot);
            DockPanel.SetDock(kick, Dock.Right);
            row.Children.Add(kick);
            row.Children.Add(new TextBlock
            {
                Text = $"P{player.Slot}   {player.Name}" + (player.State == PlayerState.Reserved ? "   (reconnecting)" : ""),
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Center,
            });
            PlayerList.Children.Add(row);
        }
    }

    private void UpdateDetails()
    {
        if (_host is null)
            return;
        DetailsText.Text = $"Virtual pads: {_host.PadCount}\n{_host.DescribeStreams()}" +
            (_host.LastError is { } error ? $"\nLast error: {error}" : "");
    }

    public void Dispose()
    {
        if (_host is not { } host)
            return;
        _host = null;
        _details.Stop();
        foreach (var popup in _asks.Values.ToList())
            popup.CloseByHost();
        _asks.Clear();
        host.Dispose(); // tells every client the session ended
        AppServices.DescribeMode = () => "Idle";
        AppServices.Log.Write("Hosting stopped");
    }
}
```

- [ ] **Step 4: Main window becomes the navigator**

Replace `src/CouchLink.App/MainWindow.xaml`:

```xml
<Window x:Class="CouchLink.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="CouchLink" Width="520" Height="620" MinWidth="420" MinHeight="480">
    <ContentControl x:Name="Screen" Focusable="False"/>
</Window>
```

Replace `src/CouchLink.App/MainWindow.xaml.cs`:

```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using CouchLink.App.Views;

namespace CouchLink.App;

/// <summary>The app's one window: shows the Start screen, the host lobby, the join list or the session.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ShowStart();
    }

    /// <summary>Shows a view; the one it replaces is disposed (stopping whatever it owned).</summary>
    private void Show(UserControl view)
    {
        if (Screen.Content is IDisposable old && !ReferenceEquals(old, view))
            old.Dispose();
        Screen.Content = view;
    }

    private void ShowStart()
    {
        var start = new StartView();
        start.HostClicked += ShowHost;
        start.CrashReportsClicked += OpenCrashReports;
        Show(start);
        AppServices.DescribeMode = () => "Idle";
    }

    private void ShowHost()
    {
        var lobby = new HostLobbyView();
        if (!lobby.TryStart(out var error))
        {
            MessageBox.Show(this, error, "CouchLink");
            return;
        }
        lobby.Stopped += ShowStart;
        Show(lobby);
    }

    private void OpenCrashReports()
    {
        try
        {
            var directory = AppServices.CrashReports.ReportsDirectory();
            Process.Start("explorer.exe", $"\"{directory}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the crash reports folder:\n{ex.Message}", "CouchLink");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        (Screen.Content as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build -c Release`
Expected: `0 Warning(s) 0 Error(s)`. (`ClientStreams`, `ClientInputLoop` and `RawInputSource` are unused until Task 11; unused internal types don't warn.)
Run: `dotnet test -c Release --no-build`
Expected: all PASS.

- [ ] **Step 6: Manual check (one PC)**

1. `dotnet run --project src/CouchLink.App -c Release`. The Start screen shows Host and Join (Join greyed out until Task 11).
2. Click **Host**. The lobby reads "Hosting on <this PC>", "Players: 1/10 (you are P1)", "No one has joined yet." Details shows the video and audio lines.
3. Change the resolution. Details shows the new size within a second; the log (`%LOCALAPPDATA%\CouchLink\Logs\couchlink.log`) has `Stream settings changed`.
4. In PowerShell, check the announce is going out: `$u = New-Object System.Net.Sockets.UdpClient 47800; $ep = $null; [Text.Encoding]::UTF8.GetString($u.Receive([ref]$ep)); $u.Close()`. It prints the PC name (after a few binary bytes).
5. Click **Stop hosting**: back to Start; the log has `Session: hosting stopped` and `Hosting stopped`.

- [ ] **Step 7: Commit**

```bash
git add -A src/CouchLink.App
git commit -m "feat(app): Start screen, host lobby with players, kick and stream settings, approval popup"
```

---

### Task 11: The client side of the app: join list, waiting, playing, reconnecting

**Files:**
- Create: `src/CouchLink.App/ClientPlay.cs`, `src/CouchLink.App/ClientSessionService.cs`
- Create: `src/CouchLink.App/Views/JoinListView.xaml`, `JoinListView.xaml.cs`, `SessionView.xaml`, `SessionView.xaml.cs`
- Modify: `src/CouchLink.App/ClientStreams.cs`, `src/CouchLink.App/ClientVideoService.cs`, `src/CouchLink.App/Views/StartView.xaml`, `src/CouchLink.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `SessionClient`, `ISessionClientEvents` (Task 8), `ClientSession`, `IClientSessionEffects`, `ClientState` (Task 7), `DiscoveryListener`, `HostList`, `FoundHost` (Task 5), `OverlayText.Reconnecting`, `VideoPlayer(..., sessionStatus)` (Task 9).
- Produces:
  - `ClientStreams.TryStart(IPAddress host, InputSender sender, PlayerOptions options, string? savePath, Action leave, Func<string?> sessionStatus, out ClientStreams? streams, out string? error)`; `ClientVideoService.TryStart(..., Func<string?> audioLine, Func<string?> sessionStatus, out ..., out ...)`.
  - `ClientPlay.TryStart(Window window, IPAddress host, byte slot, Action leave, Func<string?> sessionStatus, out ClientPlay? play, out string? error)`, `string Describe()`, `Dispose()`.
  - `internal interface IClientUi { void StartPlaying(byte slot); void Ended(string? message); void StateChanged(); }` (always called on the UI thread).
  - `ClientSessionService(IPAddress host, string hostName, Dispatcher dispatcher, IClientUi ui)` with `Host`, `HostName`, `State`, `Slot`, `PlayerStatus` (any thread), `Leave()`, `Fail(string)`, `Dispose()`.
  - `JoinListView` events `BackClicked`, `JoinRequested(IPAddress, string)`, `ShowMessage(string?)`, `IDisposable`. `SessionView` with `Show(ClientState, string host, byte slot)`, `Details` setter, event `LeaveClicked`.

- [ ] **Step 1: Pass the session line through to the player**

In `src/CouchLink.App/ClientVideoService.cs`:
1. Add a `Func<string?> sessionStatus` parameter after `audioLine` to both the private constructor and `TryStart`, and pass it through from `TryStart` to the constructor.
2. In the constructor, change `leave, message => AppServices.Log.Write(message), audioLine);` to `leave, message => AppServices.Log.Write(message), audioLine, sessionStatus);`.
3. Add to the class summary: `<c>sessionStatus</c> replaces the picture status while it returns text ("Reconnecting...").`

In `src/CouchLink.App/ClientStreams.cs`, add `Func<string?> sessionStatus` after `Action leave` in `TryStart`, and change the video start to `ClientVideoService.TryStart(sender, options, savePath, leave, audio.Describe, sessionStatus, out var video, out error)`.

- [ ] **Step 2: Client play and the client session service**

Create `src/CouchLink.App/ClientPlay.cs`:

```csharp
using System.Net;
using System.Windows;
using System.Windows.Interop;
using CouchLink.App.Input;
using CouchLink.Core.Input;
using CouchLink.Core.Net;
using CouchLink.Video;

namespace CouchLink.App;

/// <summary>
/// Client side, once the host let us in: the host's picture and sound, and this PC's keyboard and
/// mouse driving the slot's pad. Input counts only while the app window or the player window is in
/// front; losing focus releases every key. Create and dispose on the UI thread.
/// </summary>
internal sealed class ClientPlay : IDisposable
{
    private readonly Window _window;
    private readonly ClientStreams _streams;
    private readonly InputMapper _mapper;
    private readonly RawInputSource _rawInput;
    private readonly ClientInputLoop _input;

    private ClientPlay(Window window, ClientStreams streams, InputSender sender)
    {
        _window = window;
        _streams = streams;
        _mapper = new InputMapper(KeyLayout.CreateDefault(), new MouseStick());
        _rawInput = new RawInputSource((HwndSource)PresentationSource.FromVisual(window), InputAllowed);
        _rawInput.InputSuspended += _mapper.ReleaseAll;
        _rawInput.KeyDown += _mapper.KeyDown;
        _rawInput.KeyUp += _mapper.KeyUp;
        _rawInput.MouseMove += _mapper.MouseMove;
        _input = new ClientInputLoop(_mapper, sender);
        _window.Deactivated += OnDeactivated;
    }

    public static bool TryStart(Window window, IPAddress host, byte slot, Action leave, Func<string?> sessionStatus,
        out ClientPlay? play, out string? error)
    {
        play = null;
        var sender = new InputSender(new IPEndPoint(host, Ports.Input), slot);
        var options = new PlayerOptions(new WindowInteropHelper(window).Handle, AppServices.Options.WindowedPlayer);
        if (!ClientStreams.TryStart(host, sender, options, AppServices.Options.SaveVideoPath, leave, sessionStatus,
                out var streams, out error))
        {
            sender.Dispose();
            return false;
        }
        play = new ClientPlay(window, streams!, sender);
        return true;
    }

    private bool InputAllowed()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        return foreground == new WindowInteropHelper(_window).Handle || foreground == _streams.PlayerWindow;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!InputAllowed())
            _mapper.ReleaseAll(); // never leave keys stuck
    }

    /// <summary>The pad state being sent and the streams' stats, for the Details section.</summary>
    public string Describe()
    {
        var s = _input.LastSent;
        return $"Buttons: {s.Buttons}\nL: {s.LX},{s.LY}  R: {s.RX},{s.RY}  L2/R2: {s.L2}/{s.R2}\n{_streams.Describe()}";
    }

    public void Dispose()
    {
        _window.Deactivated -= OnDeactivated;
        _streams.Dispose(); // before the input sender that video sends keyframe requests through
        _input.Dispose();   // sends a neutral pad state and closes the input sender
        _rawInput.Dispose();
    }
}
```

Create `src/CouchLink.App/ClientSessionService.cs`:

```csharp
using System.Net;
using System.Windows.Threading;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Session;
using CouchLink.Core.Video;

namespace CouchLink.App;

/// <summary>What the main window does as the session moves on. Always called on the UI thread.</summary>
internal interface IClientUi
{
    void StartPlaying(byte slot);
    void Ended(string? message);
    void StateChanged();
}

/// <summary>
/// Client side: one join to one host. Runs <see cref="ClientSession"/> over a <see cref="SessionClient"/>
/// under one lock, ticks it every 250 ms, and hands what the UI must do to the UI thread.
/// </summary>
internal sealed class ClientSessionService : IDisposable, IClientSessionEffects, ISessionClientEvents
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private readonly Lock _gate = new();
    private readonly Dispatcher _dispatcher;
    private readonly IClientUi _ui;
    private readonly SessionClient _client;
    private readonly ClientSession _session;
    private readonly Timer _tick;

    public ClientSessionService(IPAddress host, string hostName, Dispatcher dispatcher, IClientUi ui)
    {
        Host = host;
        HostName = hostName;
        _dispatcher = dispatcher;
        _ui = ui;
        _client = new SessionClient(new IPEndPoint(host, Ports.Session), this, e => AppServices.Log.Write($"Session error: {e}"));
        _session = new ClientSession(this, TimeProvider.System, hostName, PcName.ThisPc);
        AppServices.Log.Write($"Joining {hostName} ({host})");
        Guard(s => s.Start());
        _tick = new Timer(_ => Guard(s => s.Tick()), null, TickInterval, TickInterval);
    }

    public IPAddress Host { get; }
    public string HostName { get; }

    public ClientState State
    {
        get { lock (_gate) return _session.State; }
    }

    public byte Slot
    {
        get { lock (_gate) return _session.Slot; }
    }

    /// <summary>The player's status line: "Reconnecting..." while the host is lost, otherwise null. Any thread.</summary>
    public string? PlayerStatus => State == ClientState.Reconnecting ? OverlayText.Reconnecting : null;

    public void Leave() => Guard(s => s.Leave());

    public void Fail(string message) => Guard(s => s.Fail(message));

    private void Guard(Action<ClientSession> action)
    {
        lock (_gate)
        {
            try
            {
                action(_session);
            }
            catch (Exception e)
            {
                AppServices.Log.Write($"Session error: {e}"); // never let a timer or socket thread take the app down
            }
        }
    }

    // ISessionClientEvents: thread-pool threads.
    void ISessionClientEvents.Connected() => Guard(s => s.Connected());
    void ISessionClientEvents.ConnectFailed() => Guard(s => s.ConnectFailed());
    void ISessionClientEvents.Received(SessionMessage message) => Guard(s => s.Received(message));
    void ISessionClientEvents.Disconnected() => Guard(s => s.Disconnected());

    // IClientSessionEffects: called with _gate held.
    void IClientSessionEffects.Connect() => _client.Connect();
    void IClientSessionEffects.Send(SessionMessage message) => _client.Send(message);
    void IClientSessionEffects.Disconnect() => _client.Disconnect();

    void IClientSessionEffects.StartPlaying(byte slot)
    {
        AppServices.Log.Write($"Accepted by {HostName} as P{slot}");
        _dispatcher.InvokeAsync(() => _ui.StartPlaying(slot));
    }

    void IClientSessionEffects.Ended(string? message)
    {
        AppServices.Log.Write($"Session with {HostName} ended: {message ?? "left"}");
        _dispatcher.InvokeAsync(() => _ui.Ended(message));
    }

    void IClientSessionEffects.StateChanged() => _dispatcher.InvokeAsync(_ui.StateChanged);

    public void Dispose()
    {
        _tick.Dispose();
        _client.Dispose();
    }
}
```

- [ ] **Step 3: Join list and session views**

In `src/CouchLink.App/Views/StartView.xaml` remove ` IsEnabled="False"` from `JoinButton`.

Create `src/CouchLink.App/Views/JoinListView.xaml`:

```xml
<UserControl x:Class="CouchLink.App.Views.JoinListView" x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel Margin="16">
        <DockPanel DockPanel.Dock="Top">
            <Button x:Name="BackButton" Content="Back" DockPanel.Dock="Left" Padding="12,2"/>
            <TextBlock Text="Join a game" FontSize="22" FontWeight="SemiBold" Margin="12,0,0,0"/>
        </DockPanel>
        <Border x:Name="MessageBar" DockPanel.Dock="Top" Background="#FFF4E5" BorderBrush="#F0B060" BorderThickness="1"
                Padding="8" Margin="0,12,0,0" Visibility="Collapsed">
            <TextBlock x:Name="MessageText" TextWrapping="Wrap"/>
        </Border>
        <StackPanel DockPanel.Dock="Bottom" Margin="0,12,0,0">
            <TextBlock><Hyperlink x:Name="ByAddressLink">Join by address...</Hyperlink></TextBlock>
            <StackPanel x:Name="ByAddressPanel" Orientation="Horizontal" Margin="0,8,0,0" Visibility="Collapsed">
                <TextBox x:Name="AddressBox" Width="160" VerticalContentAlignment="Center"/>
                <Button x:Name="AddressJoinButton" Content="Join" Padding="16,2" Margin="8,0,0,0"/>
            </StackPanel>
        </StackPanel>
        <TextBlock x:Name="EmptyText" DockPanel.Dock="Top" Margin="0,12,0,0" Foreground="Gray" TextWrapping="Wrap"
                   Text="Looking for hosts on this network..."/>
        <ScrollViewer VerticalScrollBarVisibility="Auto" Margin="0,12,0,0">
            <StackPanel x:Name="HostButtons"/>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

Create `src/CouchLink.App/Views/JoinListView.xaml.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CouchLink.Core.Net;
using CouchLink.Core.Session;

namespace CouchLink.App.Views;

/// <summary>
/// The hosts on the LAN, "PC-03 · 4/10 players", heard on UDP 47800 while this view is shown, plus
/// "Join by address..." for when broadcasts don't get through. A message bar says why the last
/// session ended.
/// </summary>
internal sealed partial class JoinListView : UserControl, IDisposable
{
    private readonly HostList _hosts = new(TimeProvider.System);
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DiscoveryListener? _listener;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<FoundHost> _shown = [];

    public JoinListView()
    {
        InitializeComponent();
        BackButton.Click += (_, _) => BackClicked?.Invoke();
        ByAddressLink.Click += (_, _) =>
        {
            ByAddressPanel.Visibility = Visibility.Visible;
            AddressBox.Focus();
        };
        AddressJoinButton.Click += (_, _) => JoinByAddress();
        AddressBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                JoinByAddress();
        };
        _refresh.Tick += (_, _) => Refresh();
        Loaded += (_, _) => StartListening();
        Unloaded += (_, _) => Dispose();
    }

    public event Action? BackClicked;
    public event Action<IPAddress, string>? JoinRequested;

    public void ShowMessage(string? message)
    {
        MessageText.Text = message ?? "";
        MessageBar.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void StartListening()
    {
        if (_listener is not null)
            return;
        if (!DiscoveryListener.TryCreate(Ports.Discovery, out _listener, out var error))
        {
            EmptyText.Text = $"{error} Join by address still works.";
            ByAddressPanel.Visibility = Visibility.Visible;
            return;
        }
        _cts = new CancellationTokenSource();
        _ = _listener!.RunAsync((announce, from) => _hosts.Seen(announce, from), _cts.Token,
            e => AppServices.Log.Write($"Discovery error: {e}"));
        _refresh.Start();
    }

    private void Refresh()
    {
        var hosts = _hosts.Current();
        if (hosts.SequenceEqual(_shown))
            return;
        _shown = hosts;
        EmptyText.Visibility = hosts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HostButtons.Children.Clear();
        foreach (var host in hosts)
        {
            var button = new Button
            {
                Height = 56,
                FontSize = 18,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(16, 0, 16, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
            };
            if (host.Compatible)
            {
                button.Content = $"{host.Name} · {host.Players + 1}/{host.Capacity + 1} players";
                var chosen = host;
                button.Click += (_, _) => JoinRequested?.Invoke(chosen.Address, chosen.Name);
            }
            else
            {
                button.Content = $"{host.Name} · needs the same CouchLink version";
                button.IsEnabled = false;
            }
            HostButtons.Children.Add(button);
        }
    }

    private void JoinByAddress()
    {
        if (IPAddress.TryParse(AddressBox.Text.Trim(), out var address) && address.AddressFamily == AddressFamily.InterNetwork)
            JoinRequested?.Invoke(address, address.ToString());
        else
            ShowMessage("Enter an IP address like 192.168.1.23.");
    }

    public void Dispose()
    {
        _refresh.Stop();
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _listener?.Dispose(); // frees UDP 47800 for the next list
        _listener = null;
    }
}
```

Create `src/CouchLink.App/Views/SessionView.xaml`:

```xml
<UserControl x:Class="CouchLink.App.Views.SessionView" x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <StackPanel Margin="24" VerticalAlignment="Center">
        <TextBlock x:Name="Heading" FontSize="22" FontWeight="SemiBold" TextWrapping="Wrap"/>
        <TextBlock x:Name="Hint" Margin="0,8,0,0" Foreground="Gray" TextWrapping="Wrap"/>
        <!-- Not focusable: Raw Input keys still reach a focused control, so Space or Enter in the game must not press it. -->
        <Button x:Name="LeaveButton" Height="40" Margin="0,16,0,0" Focusable="False"/>
        <Expander Header="Details" Margin="0,16,0,0" Focusable="False">
            <TextBlock x:Name="DetailsText" FontFamily="Consolas" TextWrapping="Wrap" Margin="0,4,0,0"/>
        </Expander>
    </StackPanel>
</UserControl>
```

Create `src/CouchLink.App/Views/SessionView.xaml.cs`:

```csharp
using System.Windows.Controls;
using CouchLink.Core.Session;

namespace CouchLink.App.Views;

/// <summary>The main window while joining or playing: what is happening, and Cancel or Leave.</summary>
internal sealed partial class SessionView : UserControl
{
    public SessionView()
    {
        InitializeComponent();
        LeaveButton.Click += (_, _) => LeaveClicked?.Invoke();
    }

    public event Action? LeaveClicked;

    public string Details
    {
        set => DetailsText.Text = value;
    }

    public void Show(ClientState state, string host, byte slot)
    {
        (string heading, string hint, string button) = state switch
        {
            ClientState.Connecting => ($"Connecting to {host}...", "", "Cancel"),
            ClientState.Waiting => ($"Waiting for {host} to let you in...", "The host sees a popup and can allow or deny.", "Cancel"),
            ClientState.Playing => ($"Playing on {host} as P{slot}", "Ctrl+Alt+Q leaves. F2 shows stats.", "Leave"),
            ClientState.Reconnecting => ($"Reconnecting to {host}...", "Your slot is kept for a minute.", "Leave"),
            _ => (Heading.Text, Hint.Text, LeaveButton.Content as string ?? "Leave"),
        };
        Heading.Text = heading;
        Hint.Text = hint;
        LeaveButton.Content = button;
    }
}
```

- [ ] **Step 4: Main window: join, play, leave**

Replace `src/CouchLink.App/MainWindow.xaml.cs`:

```csharp
using System.Diagnostics;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CouchLink.App.Views;

namespace CouchLink.App;

/// <summary>
/// The app's one window: the Start screen, the host lobby, the join list, or the session view while
/// joining and playing (the fullscreen player sits in front of it).
/// </summary>
public partial class MainWindow : Window, IClientUi
{
    private readonly DispatcherTimer _detailsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private ClientSessionService? _session;
    private ClientPlay? _play;
    private SessionView? _sessionView;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        _detailsTimer.Tick += (_, _) =>
        {
            if (_sessionView is not null)
                _sessionView.Details = _play?.Describe() ?? "";
        };
        ShowStart();
    }

    /// <summary>Shows a view; the one it replaces is disposed (stopping whatever it owned).</summary>
    private void Show(UserControl view)
    {
        if (Screen.Content is IDisposable old && !ReferenceEquals(old, view))
            old.Dispose();
        Screen.Content = view;
    }

    private void ShowStart()
    {
        var start = new StartView();
        start.HostClicked += ShowHost;
        start.JoinClicked += () => ShowJoinList(null);
        start.CrashReportsClicked += OpenCrashReports;
        Show(start);
        AppServices.DescribeMode = () => "Idle";
    }

    private void ShowHost()
    {
        var lobby = new HostLobbyView();
        if (!lobby.TryStart(out var error))
        {
            MessageBox.Show(this, error, "CouchLink");
            return;
        }
        lobby.Stopped += ShowStart;
        Show(lobby);
    }

    private void ShowJoinList(string? message)
    {
        var list = new JoinListView();
        list.BackClicked += ShowStart;
        list.JoinRequested += Join;
        list.ShowMessage(message);
        Show(list);
    }

    private void Join(IPAddress host, string hostName)
    {
        _sessionView = new SessionView();
        _sessionView.LeaveClicked += LeaveSession;
        Show(_sessionView); // closes the join list, freeing UDP 47800
        _session = new ClientSessionService(host, hostName, Dispatcher, this);
        UpdateSessionView();
        _detailsTimer.Start();
    }

    void IClientUi.StartPlaying(byte slot)
    {
        if (_session is not { } session || _closed)
            return;
        if (!ClientPlay.TryStart(this, session.Host, slot, () => Dispatcher.InvokeAsync(LeaveSession),
                () => session.PlayerStatus, out _play, out var error))
        {
            session.Fail(error!);
            return;
        }
        Keyboard.ClearFocus(); // Raw Input keys still reach a focused control
        AppServices.DescribeMode = () => $"Client (slot P{slot})";
        UpdateSessionView();
    }

    void IClientUi.Ended(string? message)
    {
        if (_closed)
            return;
        EndSession();
        ShowJoinList(message);
    }

    void IClientUi.StateChanged() => UpdateSessionView();

    private void UpdateSessionView()
    {
        if (_session is { } session && _sessionView is { } view)
            view.Show(session.State, session.HostName, session.Slot);
    }

    /// <summary>Cancel, Leave or Ctrl+Alt+Q: the session tells the host, then ends and calls Ended(null).</summary>
    private void LeaveSession() => _session?.Leave();

    private void EndSession()
    {
        _detailsTimer.Stop();
        _play?.Dispose();
        _play = null;
        _session?.Dispose();
        _session = null;
        _sessionView = null;
        AppServices.DescribeMode = () => "Idle";
        Activate();
    }

    private void OpenCrashReports()
    {
        try
        {
            var directory = AppServices.CrashReports.ReportsDirectory();
            Process.Start("explorer.exe", $"\"{directory}\"");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the crash reports folder:\n{ex.Message}", "CouchLink");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _session?.Leave(); // tells the host now instead of after 5 s of silence
        EndSession();
        (Screen.Content as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
```

- [ ] **Step 5: Build and test**

Run: `dotnet build -c Release`
Expected: `0 Warning(s) 0 Error(s)`.
Run: `dotnet test -c Release --no-build`
Expected: all PASS.

- [ ] **Step 6: Manual check (one PC, two copies)**

Run the host: `dotnet run --project src/CouchLink.App -c Release`, click **Host**. Run the client from a second terminal: `dotnet run --project src/CouchLink.App -c Release -- --windowed-player`, click **Join**.

1. Within about a second the list shows `<this PC> · 1/10 players`. Click it: "Waiting for <PC> to let you in...". The host's popup appears bottom-right, flashing, counting down from 30. Click **Allow**: the client's 1280x720 player opens; the host lobby shows `P2   <PC>`; the client view reads "Playing on <PC> as P2".
2. Ctrl+Alt+Q in the player: the client is back on the list with no message; the host lobby shows "No one has joined yet."
3. Join again, click **Deny**: "Request denied." Join again and wait 30 s: "The host didn't answer."
4. Tick **Allow everyone** and join: no popup, straight to playing.
5. **Kick** P2: "You were removed by the host."
6. Join (auto-allowed), then end the client with Task Manager. Within 5 s the host shows `P2   <PC>   (reconnecting)`. Start the client again and join: back on P2 with no popup.
7. While a client plays, change the host's resolution: the picture returns within about a second.
8. **Stop hosting** while a client plays: "Host ended the session."

- [ ] **Step 7: Commit**

```bash
git add -A src/CouchLink.App
git commit -m "feat(app): join list, waiting and playing screens; leave, kick and reconnect end in clear messages"
```

---

### Task 12: Docs: main design, gate checklist, README

**Files:**
- Modify: `docs/superpowers/specs/2026-10-05-couchlink-design.md`, `docs/gate-results.md`, `README.md`

- [ ] **Step 1: Main design**

In `docs/superpowers/specs/2026-10-05-couchlink-design.md`:

1. In the ports table (section 3) replace

```markdown
| UDP 47800 | Discovery broadcast |
| TCP 47801 | Session control |
```

with

```markdown
| UDP 47800 | Discovery broadcast (`HostAnnounce`, wire type 7); only clients bind it |
| TCP 47801 | Session control: join, approval, heartbeat, leave, kick (wire types 8-14) |
```

and replace `| UDP 47803 | Input; client -> host keyframe requests and timing pings (until the v1.4 session channel) |` with `| UDP 47803 | Input; client -> host keyframe requests and timing pings |`.

2. In 4.1 replace

```markdown
2. **Host lobby:** "Hosting on PC-03", player list (P2..P10) with **Kick**
   per player, **Stop hosting**. Host minimizes it and plays.
```

with

```markdown
2. **Host lobby:** "Hosting on PC-03", stream resolution and frame rate,
   **Allow everyone**, player list (P2..P10) with **Kick** per player,
   **Stop hosting**. Host minimizes it and plays.
```

3. In 4.3 replace

```markdown
2. Host shows topmost popup **"PC-07 wants to join. Allow / Deny"**;
   auto-deny after **30 s** with no answer.
```

with

```markdown
2. Host shows topmost popup **"PC-07 wants to join. Allow / Deny"**;
   auto-deny after **30 s** with no answer. With **Allow everyone** ticked
   in the host lobby, joins are accepted with no popup.
```

4. In 4.4 replace

```markdown
- Client heartbeat every 1 s. Host treats **5 s** silence as gone and
  unplugs that pad.
```

with

```markdown
- Heartbeat every 1 s both ways. Host treats **5 s** silence as gone: the
  pad stays plugged in at neutral for the 60 s rejoin window below, then
  is unplugged (so the game keeps the player across a short drop).
```

5. Under the heading of section 4 add a line: `Details: [lobby & sessions design](2026-10-07-couchlink-lobby-sessions-design.md).`

- [ ] **Step 2: Gate checklist**

Append to `docs/gate-results.md`:

```markdown

## Lobby & sessions (Plan 7)

Date: <fill in>
Host / client PCs: <fill in>

| Check | Result |
|---|---|
| One PC, host + client (`--windowed-player`): host listed as "<PC> · 1/10 players", popup appears, Allow -> playing as P2 | <pass/fail> |
| Two PCs: Deny -> "Request denied."; no answer for 30 s -> "The host didn't answer." | <pass/fail> |
| Two PCs: Allow everyone -> joins with no popup | <pass/fail> |
| Kick -> "You were removed by the host."; slot freed | <pass/fail> |
| Stop hosting -> "Host ended the session." on every client | <pass/fail> |
| Pull the client's cable for 20 s -> "Reconnecting...", then back on the same slot and the same 2K player | <pass/fail> |
| Kill and restart the client -> same slot, no popup | <pass/fail> |
| Change resolution mid-session -> clients' picture back within about 1 s | <pass/fail> |
| Three or more clients join at once -> distinct slots | <pass/fail> |
```

- [ ] **Step 3: README status**

In `README.md` replace `own virtual DualShock 4. The lobby and the join/approval flow are next.` with `own virtual DualShock 4. Friends find the host in a list and the host approves each join. Playing-screen polish (F1 help, key blocking, mouse lock), the controls editor and the installer are next.`

- [ ] **Step 4: Final check**

Run: `dotnet build -c Release` then `dotnet test -c Release --no-build`
Expected: `0 Warning(s) 0 Error(s)`, all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add docs README.md
git commit -m "docs: lobby and sessions in the main design, the Plan 7 gate checklist and README status"
```
