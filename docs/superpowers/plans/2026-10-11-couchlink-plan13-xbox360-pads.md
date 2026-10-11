# Host Controller Type & Live Controller View Implementation Plan (#49)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The host picks DualShock 4 or Xbox 360 virtual pads on a new setup screen before hosting (Xbox 360 takes up to 4 players), and the Controls editor shows the session's controller, drawn accurately, reacting live to the player's keys.

**Architecture:** A `PadKind` enum in Core travels from the setup screen through `HostService` into the pad factory (which builds `ViGEmDs4Pad` or the new `ViGEmXbox360Pad`) and into `HostSession` (capacity 9 or 4). The host tells each client the kind in the session's Accepted message (wire version 2); the client uses it for Xbox names in the F1 panel and the editor. The editor gets a `ControllerView` (WPF vector drawing) fed by a local `InputMapper` while the editor is active.

**Tech Stack:** C# / .NET 10, WPF, xUnit, Nefarius.ViGEm.Client 1.21.256, System.Text.Json.

**Spec:** [docs/superpowers/specs/2026-10-11-couchlink-xbox360-pads-design.md](../specs/2026-10-11-couchlink-xbox360-pads-design.md)

## Global Constraints

- `PadKind` values are on the wire: `DualShock4 = 1`, `Xbox360 = 2`. DualShock 4 is the default everywhere.
- Capacity (clients, not counting the host): DualShock 4 **9** (P2–P10), Xbox 360 **4** (P2–P5).
- `Wire.Version` becomes **2**. Accepted body: slot (1) + pad kind (1). `DenyReason.XboxFull = 5`.
- Exact copy: refusal `Host is full: Xbox 360 sessions take up to 4 players.`; setup helper texts `Up to 9 players. Works with most games, like NBA 2K22.` and `Up to 4 players. For games that only work with Xbox controllers, like NBA 2K14.`; setup lead `Pick the controller the game will see. Friends can find this PC once you start hosting.`; lobby line `Controller: Xbox 360 · up to 4 players` (format `Controller: {label} · up to {n} players`); editor caption `Press your keys to test them · Click a button to change its key`, while listening `Changing {name}: press a key · Esc cancels`.
- Xbox 360 names: Cross A, Circle B, Square X, Triangle Y, L1 LB, R1 RB, L2 LT, R2 RT, L3 LS, R3 RS, Options Start, Share Back. No Touchpad in Xbox 360 mode. Profiles and key layouts never change.
- Settings key: `"hostController": "dualshock4" | "xbox360"` in `%LOCALAPPDATA%\CouchLink\settings.json`, other keys kept.
- No logos in the drawings: the PS button and Guide are plain.
- `dotnet build` treats warnings as errors. Run tests with `dotnet test tests/CouchLink.Core.Tests` and `dotnet test tests/CouchLink.App.Tests` from the repo root.
- Commits use Conventional Commit types and carry **no** Claude co-author line. Never mention Sunshine anywhere.

## Review Focus

1. **Xbox 360 host with a reserved seat.** Four players seated, one drops (reserved): a new fifth joiner is refused with `XboxFull`, and the dropped player rejoining gets their seat back → Task 3 test.
2. **Client reconnecting to a host that restarted in the other mode.** Accepted with the same slot but a different kind must end with "Lost the host." rather than keep playing on the wrong names → Task 4 test.
3. **Typing in Find or a label box while the preview runs.** Typing "wasd" into Find must not tilt the stick on the picture → Task 12 test (`PreviewIgnores`).
4. **A profile loaded or Reset pressed while a key is held.** Nothing may stay lit on the picture or the key caps → Task 12 test.
5. **A hand-edited settings file** with `"hostController": "ps5"` or a number reads as DualShock 4, is logged once, and saving keeps `reduceMotion` → Task 7 test.

---

### Task 1: `PadKind` and the Xbox 360 report

**Files:**
- Create: `src/CouchLink.Core/Pads/PadKind.cs`
- Create: `src/CouchLink.Core/Input/Xbox360Report.cs`
- Test: `tests/CouchLink.Core.Tests/PadKindTests.cs`, `tests/CouchLink.Core.Tests/Xbox360ReportTests.cs`

**Interfaces:**
- Produces: `enum PadKind : byte { DualShock4 = 1, Xbox360 = 2 }` (namespace `CouchLink.Core.Pads`); `static class PadKinds { string Label(PadKind); int Capacity(PadKind); }`; `[Flags] enum Xbox360Buttons : ushort` (XInput bits); `readonly record struct Xbox360Report(Xbox360Buttons Buttons, byte LeftTrigger, byte RightTrigger, short LeftX, short LeftY, short RightX, short RightY)` with `static Xbox360Report From(PadState s)` (namespace `CouchLink.Core.Input`).

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/PadKindTests.cs`:

```csharp
using CouchLink.Core.Pads;

namespace CouchLink.Core.Tests;

public class PadKindTests
{
    [Fact]
    public void Kinds_keep_their_wire_values()
    {
        Assert.Equal(1, (byte)PadKind.DualShock4);
        Assert.Equal(2, (byte)PadKind.Xbox360);
    }

    [Theory]
    [InlineData(PadKind.DualShock4, "DualShock 4", 9)]
    [InlineData(PadKind.Xbox360, "Xbox 360", 4)]
    public void Each_kind_has_a_label_and_a_capacity(PadKind kind, string label, int capacity)
    {
        Assert.Equal(label, PadKinds.Label(kind));
        Assert.Equal(capacity, PadKinds.Capacity(kind));
    }
}
```

`tests/CouchLink.Core.Tests/Xbox360ReportTests.cs`:

```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class Xbox360ReportTests
{
    private static Xbox360Report With(PadButtons buttons) => Xbox360Report.From(PadState.Neutral with { Buttons = buttons });

    [Theory]
    [InlineData(PadButtons.Cross, Xbox360Buttons.A)]
    [InlineData(PadButtons.Circle, Xbox360Buttons.B)]
    [InlineData(PadButtons.Square, Xbox360Buttons.X)]
    [InlineData(PadButtons.Triangle, Xbox360Buttons.Y)]
    [InlineData(PadButtons.L1, Xbox360Buttons.LeftShoulder)]
    [InlineData(PadButtons.R1, Xbox360Buttons.RightShoulder)]
    [InlineData(PadButtons.L3, Xbox360Buttons.LeftThumb)]
    [InlineData(PadButtons.R3, Xbox360Buttons.RightThumb)]
    [InlineData(PadButtons.Options, Xbox360Buttons.Start)]
    [InlineData(PadButtons.Share, Xbox360Buttons.Back)]
    [InlineData(PadButtons.PS, Xbox360Buttons.Guide)]
    [InlineData(PadButtons.DpadUp, Xbox360Buttons.Up)]
    [InlineData(PadButtons.DpadDown, Xbox360Buttons.Down)]
    [InlineData(PadButtons.DpadLeft, Xbox360Buttons.Left)]
    [InlineData(PadButtons.DpadRight, Xbox360Buttons.Right)]
    public void Each_button_maps_to_its_Xbox_button(PadButtons ds4, Xbox360Buttons xbox)
    {
        Assert.Equal(xbox, With(ds4).Buttons);
    }

    [Fact]
    public void The_touchpad_and_the_trigger_bits_press_nothing()
    {
        Assert.Equal(Xbox360Buttons.None, With(PadButtons.Touchpad | PadButtons.L2 | PadButtons.R2).Buttons);
    }

    [Fact]
    public void Left_and_right_together_press_neither()
    {
        Assert.Equal(Xbox360Buttons.None, With(PadButtons.DpadLeft | PadButtons.DpadRight).Buttons);
        Assert.Equal(Xbox360Buttons.Up | Xbox360Buttons.Right, With(PadButtons.DpadUp | PadButtons.DpadRight).Buttons);
        Assert.Equal(Xbox360Buttons.Up, With(PadButtons.DpadUp | PadButtons.DpadLeft | PadButtons.DpadRight).Buttons);
    }

    [Fact]
    public void Triggers_pass_through()
    {
        var r = Xbox360Report.From(PadState.Neutral with { L2 = 200, R2 = 255 });
        Assert.Equal((200, 255), (r.LeftTrigger, r.RightTrigger));
    }

    [Theory]
    [InlineData(128, 0)]
    [InlineData(0, -32768)]
    [InlineData(255, 32767)]
    public void Stick_x_spans_the_full_range_with_center_at_zero(byte ds4, short xbox)
    {
        var r = Xbox360Report.From(PadState.Neutral with { LX = ds4, RX = ds4 });
        Assert.Equal((xbox, xbox), (r.LeftX, r.RightX));
    }

    [Theory]
    [InlineData(128, 0)]
    [InlineData(0, 32767)]      // DS4 up is 0; XInput up is positive
    [InlineData(255, -32768)]
    public void Stick_y_is_flipped(byte ds4, short xbox)
    {
        var r = Xbox360Report.From(PadState.Neutral with { LY = ds4, RY = ds4 });
        Assert.Equal((xbox, xbox), (r.LeftY, r.RightY));
    }

    [Fact]
    public void Neutral_is_all_zero()
    {
        Assert.Equal(new Xbox360Report(Xbox360Buttons.None, 0, 0, 0, 0, 0, 0), Xbox360Report.From(PadState.Neutral));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~PadKindTests|FullyQualifiedName~Xbox360ReportTests"`
Expected: build FAILS: `PadKind`, `PadKinds`, `Xbox360Report`, `Xbox360Buttons` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Pads/PadKind.cs`:

```csharp
namespace CouchLink.Core.Pads;

/// <summary>Which virtual controller the host gives every player. Sent on the wire: keep the values.</summary>
public enum PadKind : byte
{
    DualShock4 = 1,
    Xbox360 = 2,
}

public static class PadKinds
{
    public static string Label(PadKind kind) => kind == PadKind.Xbox360 ? "Xbox 360" : "DualShock 4";

    /// <summary>Clients the host takes (the host player is extra). Windows shows games at most 4 Xbox controllers.</summary>
    public static int Capacity(PadKind kind) => kind == PadKind.Xbox360 ? 4 : PadManager.LastSlot - PadManager.FirstSlot + 1;
}
```

`src/CouchLink.Core/Input/Xbox360Report.cs`:

```csharp
namespace CouchLink.Core.Input;

/// <summary>XInput's button bits, as ViGEmBus's Xbox 360 target takes them.</summary>
[Flags]
public enum Xbox360Buttons : ushort
{
    None = 0,
    Up = 0x0001, Down = 0x0002, Left = 0x0004, Right = 0x0008,
    Start = 0x0010, Back = 0x0020, LeftThumb = 0x0040, RightThumb = 0x0080,
    LeftShoulder = 0x0100, RightShoulder = 0x0200, Guide = 0x0400,
    A = 0x1000, B = 0x2000, X = 0x4000, Y = 0x8000,
}

/// <summary>
/// One Xbox 360 report from the client's DS4-shaped state. Sticks: DS4 bytes (128 center, Y down) become XInput
/// shorts (0 center, Y up), center exactly 0 and full tilt the full range. The touchpad has no Xbox button.
/// </summary>
public readonly record struct Xbox360Report(
    Xbox360Buttons Buttons, byte LeftTrigger, byte RightTrigger, short LeftX, short LeftY, short RightX, short RightY)
{
    private static readonly (PadButtons From, Xbox360Buttons To)[] Map =
    [
        (PadButtons.Cross, Xbox360Buttons.A), (PadButtons.Circle, Xbox360Buttons.B),
        (PadButtons.Square, Xbox360Buttons.X), (PadButtons.Triangle, Xbox360Buttons.Y),
        (PadButtons.L1, Xbox360Buttons.LeftShoulder), (PadButtons.R1, Xbox360Buttons.RightShoulder),
        (PadButtons.L3, Xbox360Buttons.LeftThumb), (PadButtons.R3, Xbox360Buttons.RightThumb),
        (PadButtons.Options, Xbox360Buttons.Start), (PadButtons.Share, Xbox360Buttons.Back),
        (PadButtons.PS, Xbox360Buttons.Guide),
    ];

    public static Xbox360Report From(PadState s)
    {
        var buttons = Xbox360Buttons.None;
        foreach (var (from, to) in Map)
            if (s.Buttons.HasFlag(from))
                buttons |= to;
        buttons |= DpadMath.FromButtons(s.Buttons) switch
        {
            Dpad8.North => Xbox360Buttons.Up,
            Dpad8.NorthEast => Xbox360Buttons.Up | Xbox360Buttons.Right,
            Dpad8.East => Xbox360Buttons.Right,
            Dpad8.SouthEast => Xbox360Buttons.Down | Xbox360Buttons.Right,
            Dpad8.South => Xbox360Buttons.Down,
            Dpad8.SouthWest => Xbox360Buttons.Down | Xbox360Buttons.Left,
            Dpad8.West => Xbox360Buttons.Left,
            Dpad8.NorthWest => Xbox360Buttons.Up | Xbox360Buttons.Left,
            _ => Xbox360Buttons.None,
        };
        return new Xbox360Report(buttons, s.L2, s.R2, AxisX(s.LX), AxisY(s.LY), AxisX(s.RX), AxisY(s.RY));
    }

    private static short AxisX(byte v) => v < PadState.Center
        ? (short)((v - PadState.Center) * 256)
        : (short)((v - PadState.Center) * 32767 / 127);

    private static short AxisY(byte v) => v <= PadState.Center
        ? (short)((PadState.Center - v) * 32767 / PadState.Center)
        : (short)(-(v - PadState.Center) * 32768 / 127);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~PadKindTests|FullyQualifiedName~Xbox360ReportTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Pads/PadKind.cs src/CouchLink.Core/Input/Xbox360Report.cs tests/CouchLink.Core.Tests/PadKindTests.cs tests/CouchLink.Core.Tests/Xbox360ReportTests.cs
git commit -m "feat(input): add PadKind and the DS4-to-Xbox 360 report"
```

---

### Task 2: Protocol carries the pad kind (wire version 2)

**Files:**
- Modify: `src/CouchLink.Core/Protocol/Wire.cs:9`
- Modify: `src/CouchLink.Core/Protocol/SessionMessage.cs`
- Modify: `src/CouchLink.Core/Protocol/SessionFraming.cs`
- Test: `tests/CouchLink.Core.Tests/SessionFramingTests.cs`

**Interfaces:**
- Consumes: `PadKind` (Task 1).
- Produces: `SessionMessage.PadKind` (property, `PadKind`); `SessionMessage.Accepted(byte slot, PadKind kind = PadKind.DualShock4)`; `DenyReason.XboxFull = 5`. `ToString()` of an Accepted stays `Accepted P{slot}` for DualShock 4 and becomes `Accepted P{slot} Xbox360` for Xbox 360 (existing test expectations depend on the first form).

- [ ] **Step 1: Write the failing tests**

In `SessionFramingTests.cs`, add to `AllMessages`:

```csharp
        SessionMessage.Accepted(2, PadKind.Xbox360),
        SessionMessage.Accepted(5, PadKind.DualShock4),
        SessionMessage.Denied(DenyReason.XboxFull),
```

Replace the three Accepted lines at the top of `Bodies_of_the_wrong_shape_are_rejected` with:

```csharp
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted, 1, 1)));      // slot 1
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted, 11, 1)));     // slot 11
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted)));            // no slot
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted, 2)));         // version 1 body: no kind
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted, 2, 0)));      // kind 0
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted, 2, 3)));      // unknown kind
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted, 2, 1, 0)));   // extra byte
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeDenied, 6)));           // unknown reason
```

(remove the old `Frame(Wire.TypeDenied, 9)` line; the new 6 covers it.) Add `using CouchLink.Core.Pads;` at the top, and these facts:

```csharp
    [Fact]
    public void The_wire_version_is_2()
    {
        Assert.Equal(2, Wire.Version);
    }

    [Fact]
    public void An_accepted_message_carries_slot_then_kind()
    {
        var bytes = SessionFraming.Encode(SessionMessage.Accepted(3, PadKind.Xbox360));
        Assert.Equal(new byte[] { 3, 2 }, bytes[6..]);
    }

    [Fact]
    public void Accepted_names_the_kind_only_for_Xbox()
    {
        Assert.Equal("Accepted P4", SessionMessage.Accepted(4).ToString());
        Assert.Equal("Accepted P4 Xbox360", SessionMessage.Accepted(4, PadKind.Xbox360).ToString());
        Assert.Equal(PadKind.DualShock4, SessionMessage.Accepted(4).PadKind);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter FullyQualifiedName~SessionFramingTests`
Expected: build FAILS (`Accepted` has no kind parameter, `XboxFull` missing).

- [ ] **Step 3: Write the implementation**

`Wire.cs`: `public const byte Version = 2;` and above it add the comment `// 2: Accepted carries the pad kind (#49).`

`SessionMessage.cs`: add `using CouchLink.Core.Pads;`, add the reason and the kind:

```csharp
public enum DenyReason : byte
{
    Denied = 1,
    Full = 2,
    TimedOut = 3,
    /// <summary>The host's virtual pad could not be created.</summary>
    PadFailed = 4,
    /// <summary>The host gives Xbox 360 pads and already has 4 players.</summary>
    XboxFull = 5,
}

/// <summary>
/// One message on the TCP session channel. <see cref="Name"/> is set only on a join request,
/// <see cref="Slot"/> and <see cref="PadKind"/> only on Accepted, <see cref="Reason"/> only on Denied.
/// </summary>
public readonly record struct SessionMessage(SessionMessageType Type, string Name = "", byte Slot = 0, DenyReason Reason = 0,
    PadKind PadKind = PadKind.DualShock4)
{
    // ... Heartbeat/Leave/Kicked/HostEnded/JoinRequest unchanged ...

    public static SessionMessage Accepted(byte slot, PadKind kind = PadKind.DualShock4) =>
        new(SessionMessageType.Accepted, Slot: slot, PadKind: kind);

    public override string ToString() => Type switch
    {
        SessionMessageType.JoinRequest => $"JoinRequest {Name}",
        SessionMessageType.Accepted => PadKind == PadKind.Xbox360 ? $"Accepted P{Slot} Xbox360" : $"Accepted P{Slot}",
        SessionMessageType.Denied => $"Denied {Reason}",
        _ => Type.ToString(),
    };
}
```

`SessionFraming.cs`: add `using CouchLink.Core.Pads;`; summary line becomes `JoinRequest: name length (1) + name. Accepted: slot (1, 2-10) + pad kind (1, 1-2). Denied: reason (1, 1-5).`; encode `SessionMessageType.Accepted => [message.Slot, (byte)message.PadKind],`; decode:

```csharp
            case Wire.TypeAccepted:
                if (body.Length != 2 || body[0] is < 2 or > 10 || body[1] is not ((byte)PadKind.DualShock4 or (byte)PadKind.Xbox360))
                    throw new InvalidDataException("Bad accepted message.");
                return SessionMessage.Accepted(body[0], (PadKind)body[1]);
            case Wire.TypeDenied:
                if (body.Length != 1 || body[0] is < (byte)DenyReason.Denied or > (byte)DenyReason.XboxFull)
                    throw new InvalidDataException("Bad denied message.");
                return SessionMessage.Denied((DenyReason)body[0]);
```

- [ ] **Step 4: Run the whole Core suite**

Run: `dotnet test tests/CouchLink.Core.Tests`
Expected: PASS. A literal version byte `1` anywhere in other tests would fail here; fix such a test to use `Wire.Version`.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Protocol tests/CouchLink.Core.Tests/SessionFramingTests.cs
git commit -m "feat(protocol)!: send the pad kind with Accepted and add XboxFull" -m "BREAKING CHANGE: wire version 2; hosts and clients must both be on this version."
```

---

### Task 3: Session capacity follows the pad kind

**Files:**
- Modify: `src/CouchLink.Core/Session/HostSession.cs:49-86, 254-307, 351-357`
- Modify: `src/CouchLink.Core/Net/SessionServer.cs:56-80`
- Test: `tests/CouchLink.Core.Tests/HostSessionTests.cs`, `tests/CouchLink.Core.Tests/SessionLoopbackTests.cs`

**Interfaces:**
- Consumes: `PadKind`, `PadKinds.Capacity` (Task 1); `SessionMessage.Accepted(slot, kind)`, `DenyReason.XboxFull` (Task 2).
- Produces: `HostSession(IHostSessionEffects effects, TimeProvider time, Action<string>? log = null, PadKind padKind = PadKind.DualShock4)`; instance `HostSession.PadKind` and `HostSession.Capacity` (the old `const Capacity` is gone); `SessionServer.Start(IHostEffects effects, TimeProvider time, Action<string>? log = null, Action<Exception>? onError = null, PadKind padKind = PadKind.DualShock4)`; `SessionServer.Capacity` (int).

- [ ] **Step 1: Write the failing tests**

Append to `HostSessionTests` (add `using CouchLink.Core.Pads;`):

```csharp
    private HostSession XboxSession() => new(_effects, _time, padKind: PadKind.Xbox360) { AllowEveryone = true };

    [Fact]
    public void An_Xbox_session_takes_four_players_on_P2_to_P5()
    {
        var session = XboxSession();
        for (int i = 1; i <= 4; i++)
        {
            session.Connected(i, IPAddress.Parse($"10.0.0.{i}"));
            session.Received(i, SessionMessage.JoinRequest($"PC-{i}"));
        }
        Assert.Equal(new byte[] { 2, 3, 4, 5 }, session.Players.Select(p => p.Slot));
        Assert.Contains("send #1 Accepted P2 Xbox360", Calls);
        Assert.Equal(4, session.Capacity);
    }

    [Fact]
    public void A_fifth_Xbox_player_is_told_why()
    {
        var session = XboxSession();
        for (int i = 1; i <= 5; i++)
        {
            session.Connected(i, IPAddress.Parse($"10.0.0.{i}"));
            session.Received(i, SessionMessage.JoinRequest($"PC-{i}"));
        }
        Assert.Equal(["send #5 Denied XboxFull", "close #5"], Calls.TakeLast(2));
        Assert.Equal(4, session.PlayerCount);
    }

    [Fact]
    public void A_reserved_Xbox_seat_counts_toward_four_and_comes_back_to_its_player()
    {
        var session = XboxSession();
        for (int i = 1; i <= 4; i++)
        {
            session.Connected(i, IPAddress.Parse($"10.0.0.{i}"));
            session.Received(i, SessionMessage.JoinRequest($"PC-{i}"));
        }
        session.Disconnected(4);                       // PC-4 drops; P5 is reserved
        session.Connected(5, IPAddress.Parse("10.0.0.5"));
        session.Received(5, SessionMessage.JoinRequest("PC-5"));
        Assert.Contains("send #5 Denied XboxFull", Calls);

        session.Connected(6, IPAddress.Parse("10.0.0.4"));
        session.Received(6, SessionMessage.JoinRequest("PC-4"));
        Assert.Contains("send #6 Accepted P5 Xbox360", Calls);
    }

    [Fact]
    public void A_freed_Xbox_seat_lets_the_next_player_in()
    {
        var session = XboxSession();
        for (int i = 1; i <= 4; i++)
        {
            session.Connected(i, IPAddress.Parse($"10.0.0.{i}"));
            session.Received(i, SessionMessage.JoinRequest($"PC-{i}"));
        }
        session.Received(2, SessionMessage.Leave);     // P3 leaves for good
        session.Connected(5, IPAddress.Parse("10.0.0.5"));
        session.Received(5, SessionMessage.JoinRequest("PC-5"));
        Assert.Contains("send #5 Accepted P3 Xbox360", Calls);
    }

    [Fact]
    public void A_DualShock_session_still_takes_nine()
    {
        Assert.Equal(PadKind.DualShock4, _session.PadKind);
        Assert.Equal(9, _session.Capacity);
    }
```

Check that the existing `A_tenth_client_is_told_the_host_is_full` still expects `Denied Full` (it does; do not change it).

In `SessionLoopbackTests`, give `StartServer` a kind and add a test:

```csharp
    private static (SessionServer Server, AppEffects Effects) StartServer(bool allowEveryone, Action<Exception>? onError = null,
        PadKind padKind = PadKind.DualShock4)
    {
        Assert.True(SessionServer.TryCreate(0, out var server, out var error), error);
        var effects = new AppEffects();
        server!.Start(effects, TimeProvider.System, onError: onError, padKind: padKind);
        server.AllowEveryone = allowEveryone;
        return (server, effects);
    }

    [Fact]
    public async Task An_Xbox_host_tells_the_client_its_pad_kind()
    {
        var (server, _) = StartServer(allowEveryone: true, padKind: PadKind.Xbox360);
        using (server)
        {
            Assert.Equal(4, server.Capacity);
            var (client, events) = Join(server);
            using (client)
            {
                Assert.Equal("connected", await events.Next());
                Assert.Equal("Accepted P2 Xbox360", await events.Next());
            }
        }
    }
```

(add `using CouchLink.Core.Pads;`)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~HostSessionTests|FullyQualifiedName~SessionLoopbackTests"`
Expected: build FAILS (no `padKind` parameter).

- [ ] **Step 3: Write the implementation**

`HostSession.cs` (add `using CouchLink.Core.Pads;` is already there via `CouchLink.Core.Pads`):

```csharp
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(5);
    // (delete: public const int Capacity = ...)

    public HostSession(IHostSessionEffects effects, TimeProvider time, Action<string>? log = null, PadKind padKind = PadKind.DualShock4)
    {
        _effects = effects;
        _time = time;
        _log = log;
        PadKind = padKind;
        Capacity = PadKinds.Capacity(padKind);
    }

    /// <summary>The virtual controller every player gets; sent with Accepted.</summary>
    public PadKind PadKind { get; }

    /// <summary>Clients this session takes: P2 up to P(1 + Capacity).</summary>
    public int Capacity { get; }

    private DenyReason FullReason => PadKind == PadKind.Xbox360 ? DenyReason.XboxFull : DenyReason.Full;
```

In `Join` and `Accept`, replace `Refuse(c, DenyReason.Full)` with `Refuse(c, FullReason)`. In `Accept` and `TakeSeat`, send `SessionMessage.Accepted(slot, PadKind)` / `SessionMessage.Accepted(seat.Slot, PadKind)`. `FreeSlot`:

```csharp
    private byte? FreeSlot()
    {
        int last = PadManager.FirstSlot + Capacity - 1;
        for (byte slot = PadManager.FirstSlot; slot <= last; slot++)
            if (!_seats.ContainsKey(slot))
                return slot;
        return null;
    }
```

`SessionServer.cs` (add `using CouchLink.Core.Pads;`):

```csharp
    /// <summary>Starts accepting clients. <paramref name="effects"/> run with the server's lock held.</summary>
    public void Start(IHostEffects effects, TimeProvider time, Action<string>? log = null, Action<Exception>? onError = null,
        PadKind padKind = PadKind.DualShock4)
    {
        _onError = onError;
        lock (_gate)
            _session = new HostSession(new Effects(this, effects), time, log, padKind);
        _acceptLoop = AcceptLoopAsync(_cts.Token);
        _timer = new Timer(_ => OnTimer(), null, TickInterval, TickInterval);
    }

    /// <summary>Clients the session takes, for the announce.</summary>
    public int Capacity
    {
        get { lock (_gate) return _session?.Capacity ?? PadKinds.Capacity(PadKind.DualShock4); }
    }
```

- [ ] **Step 4: Fix the remaining callers of the old const and build**

`HostSession.Capacity` was a const used in `src/CouchLink.App/HostService.cs:59` and `src/CouchLink.App/Views/HostLobbyView.xaml.cs:122`, plus `tests/CouchLink.App.Tests/HostLobbyTests.cs:40,57`. For now replace them with `PadKinds.Capacity(PadKind.DualShock4)` (Task 8 makes them follow the kind). Add `using CouchLink.Core.Pads;` where needed.

Run: `dotnet build` then `dotnet test tests/CouchLink.Core.Tests`
Expected: build succeeds, tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core src/CouchLink.App tests
git commit -m "feat(session): cap Xbox 360 sessions at 4 players and refuse with XboxFull"
```

---

### Task 4: The client learns the pad kind

**Files:**
- Modify: `src/CouchLink.Core/Session/ClientSession.cs`
- Modify: `src/CouchLink.App/ClientSessionService.cs:11-16, 54-57, 92-96`
- Modify: `src/CouchLink.App/MainWindow.xaml.cs:112-125`
- Test: `tests/CouchLink.Core.Tests/ClientSessionTests.cs`

**Interfaces:**
- Consumes: `SessionMessage.PadKind`, `DenyReason.XboxFull` (Task 2).
- Produces: `IClientSessionEffects.StartPlaying(byte slot, PadKind kind)`; `ClientSession.PadKind` (DualShock 4 until accepted); `ClientSession.XboxFull` const; `IClientUi.StartPlaying(byte slot, PadKind kind)`; `ClientSessionService.PadKind`.

- [ ] **Step 1: Write the failing tests**

In `ClientSessionTests`, change the recorder to `public void StartPlaying(byte slot, PadKind kind) => Calls.Add(kind == PadKind.Xbox360 ? $"play P{slot} Xbox360" : $"play P{slot}");` (add `using CouchLink.Core.Pads;`), add `[InlineData(DenyReason.XboxFull, "Host is full: Xbox 360 sessions take up to 4 players.")]` to `Each_denial_has_its_message`, and add:

```csharp
    [Fact]
    public void Accepted_passes_the_pad_kind_on()
    {
        _session.Start();
        _session.Connected();
        Assert.Equal(PadKind.DualShock4, _session.PadKind);
        _session.Received(SessionMessage.Accepted(3, PadKind.Xbox360));
        Assert.Equal("play P3 Xbox360", Calls[^1]);
        Assert.Equal(PadKind.Xbox360, _session.PadKind);
    }

    [Fact]
    public void A_rejoin_with_the_other_pad_kind_is_a_different_host_session()
    {
        Playing(4);                                   // DualShock 4
        _session.Disconnected();                      // reconnecting (see A_dropped_connection_while_playing_reconnects)
        _session.Connected();
        Calls.Clear();
        _session.Received(SessionMessage.Accepted(4, PadKind.Xbox360));
        Assert.Contains("ended Lost the host.", Calls);
        Assert.Equal(ClientState.Ended, _session.State);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter FullyQualifiedName~ClientSessionTests`
Expected: build FAILS (`StartPlaying` signature, `PadKind` missing).

- [ ] **Step 3: Write the implementation**

`ClientSession.cs` (add `using CouchLink.Core.Pads;`):

```csharp
    void StartPlaying(byte slot, PadKind kind);   // in IClientSessionEffects

    public const string XboxFull = "Host is full: Xbox 360 sessions take up to 4 players.";

    /// <summary>The host's virtual controller, from Accepted. DualShock 4 until then.</summary>
    public PadKind PadKind { get; private set; } = PadKind.DualShock4;
```

In `Received`:

```csharp
            case SessionMessageType.Accepted when State == ClientState.Waiting:
                Slot = message.Slot;
                PadKind = message.PadKind;
                SetState(ClientState.Playing);
                _effects.StartPlaying(message.Slot, message.PadKind);
                break;
            case SessionMessageType.Accepted when State == ClientState.Reconnecting:
                if (message.Slot == Slot && message.PadKind == PadKind)
                    SetState(ClientState.Playing);
                else
                    Quit(LostHost); // the host forgot us, or restarted with other pads: a different player
                break;
```

In `DenyMessage`, add `DenyReason.XboxFull => XboxFull,`.

`ClientSessionService.cs`: `void StartPlaying(byte slot, PadKind kind);` in `IClientUi`; a property

```csharp
    public PadKind PadKind
    {
        get { lock (_gate) return _session.PadKind; }
    }
```

and

```csharp
    void IClientSessionEffects.StartPlaying(byte slot, PadKind kind)
    {
        AppServices.Log.Write($"Accepted by {HostName} as P{slot} ({PadKinds.Label(kind)})");
        _dispatcher.InvokeAsync(() => _ui.StartPlaying(slot, kind));
    }
```

`MainWindow.xaml.cs`: `void IClientUi.StartPlaying(byte slot, PadKind kind)`; body unchanged for now except `AppServices.DescribeMode = () => $"Client (slot P{slot}, {PadKinds.Label(kind)})";` (Task 9 passes `kind` on). Add `using CouchLink.Core.Pads;` to both files.

- [ ] **Step 4: Build and run tests**

Run: `dotnet build` then `dotnet test tests/CouchLink.Core.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src tests/CouchLink.Core.Tests/ClientSessionTests.cs
git commit -m "feat(session): client keeps the host's pad kind and explains XboxFull"
```

---

### Task 5: Control names by pad kind

**Files:**
- Modify: `src/CouchLink.Core/Input/KeyNames.cs`
- Modify: `src/CouchLink.Core/Video/OverlayText.cs:58-85`
- Modify: `src/CouchLink.App/Presentation/ControlFilter.cs`
- Test: `tests/CouchLink.Core.Tests/KeyNamesTests.cs`, `tests/CouchLink.Core.Tests/OverlayTextTests.cs`, `tests/CouchLink.App.Tests/ControlFilterTests.cs`

**Interfaces:**
- Consumes: `PadKind` (Task 1).
- Produces: `KeyNames.Of(PadControl control, PadKind kind)`; `KeyNames.GroupsFor(PadKind kind)` (same group names; Xbox 360 Menu is `[Options, Share]`; the Right stick entry is the same `KeyNames.RightStick` instance); `KeyNames.Labelled(PadControl, string?, PadKind kind = PadKind.DualShock4)`; `OverlayText.Controls(ControlSettings settings, PadKind kind = PadKind.DualShock4)`; `ControlFilter.RowName(PadControl, PadKind kind = PadKind.DualShock4)`; `ControlFilter.Matches(PadControl, string group, string? label, string? query, PadKind kind = PadKind.DualShock4)`. `KeyNames.Groups` and `KeyNames.Of(PadControl)` are unchanged (DualShock 4; profiles depend on `Groups`).

- [ ] **Step 1: Write the failing tests**

Append to `KeyNamesTests` (add `using CouchLink.Core.Pads;`):

```csharp
    [Theory]
    [InlineData(PadControl.Cross, "A")]
    [InlineData(PadControl.Circle, "B")]
    [InlineData(PadControl.Square, "X")]
    [InlineData(PadControl.Triangle, "Y")]
    [InlineData(PadControl.L1, "LB")]
    [InlineData(PadControl.R1, "RB")]
    [InlineData(PadControl.L2, "LT")]
    [InlineData(PadControl.R2, "RT")]
    [InlineData(PadControl.L3, "LS")]
    [InlineData(PadControl.R3, "RS")]
    [InlineData(PadControl.Options, "Start")]
    [InlineData(PadControl.Share, "Back")]
    [InlineData(PadControl.LeftUp, "Left stick up")]
    [InlineData(PadControl.DpadLeft, "D-pad left")]
    public void Xbox_controls_have_Xbox_names(PadControl control, string name)
    {
        Assert.Equal(name, KeyNames.Of(control, PadKind.Xbox360));
    }

    [Fact]
    public void DualShock_names_are_unchanged()
    {
        foreach (var control in Enum.GetValues<PadControl>())
            Assert.Equal(KeyNames.Of(control), KeyNames.Of(control, PadKind.DualShock4));
    }

    [Fact]
    public void Xbox_groups_have_no_touchpad_and_keep_the_right_stick_instance()
    {
        var xbox = KeyNames.GroupsFor(PadKind.Xbox360);
        Assert.Equal(KeyNames.Groups.Select(g => g.Name), xbox.Select(g => g.Name));
        Assert.Equal([PadControl.Options, PadControl.Share], xbox.Single(g => g.Name == "Menu").Controls);
        Assert.Same(KeyNames.RightStick, xbox.Single(g => g.Name == "Right stick").Controls);
        Assert.Same(KeyNames.Groups, KeyNames.GroupsFor(PadKind.DualShock4));
    }

    [Fact]
    public void Labels_use_the_kind()
    {
        Assert.Equal("Pass (A)", KeyNames.Labelled(PadControl.Cross, "Pass", PadKind.Xbox360));
        Assert.Equal("Pass (Cross)", KeyNames.Labelled(PadControl.Cross, "Pass"));
    }
```

Append to `OverlayTextTests` (add `using CouchLink.Core.Pads;`):

```csharp
    [Fact]
    public void The_Xbox_panel_uses_Xbox_names_and_has_no_touchpad()
    {
        var text = OverlayText.Controls(new ControlSettings(), PadKind.Xbox360);
        Assert.Contains("\nA ", text);
        Assert.Contains("\nStart ", text);
        Assert.DoesNotContain("Touchpad", text);
        Assert.DoesNotContain("Cross", text);
    }

    [Fact]
    public void The_default_panel_is_unchanged()
    {
        Assert.Equal(OverlayText.Controls(new ControlSettings()), OverlayText.Controls(new ControlSettings(), PadKind.DualShock4));
    }
```

Append to `ControlFilterTests` (add `using CouchLink.Core.Pads;`):

```csharp
    [Fact]
    public void Xbox_rows_are_named_and_found_by_Xbox_names()
    {
        Assert.Equal("LB", ControlFilter.RowName(PadControl.L1, PadKind.Xbox360));
        Assert.True(ControlFilter.Matches(PadControl.L1, "Shoulders", null, "lb", PadKind.Xbox360));
        Assert.False(ControlFilter.Matches(PadControl.L1, "Shoulders", null, "l1", PadKind.Xbox360));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~KeyNamesTests|FullyQualifiedName~OverlayTextTests"`
Expected: build FAILS.

- [ ] **Step 3: Write the implementation**

`KeyNames.cs` (add `using CouchLink.Core.Pads;`):

```csharp
    private static readonly IReadOnlyList<(string Name, PadControl[] Controls)> XboxGroups =
        Groups.Select(g => g.Name == "Menu" ? (g.Name, new[] { PadControl.Options, PadControl.Share }) : g).ToList();

    /// <summary>The editor's and F1 panel's groups for the session's pad. Xbox 360 has no touchpad.</summary>
    public static IReadOnlyList<(string Name, PadControl[] Controls)> GroupsFor(PadKind kind) =>
        kind == PadKind.Xbox360 ? XboxGroups : Groups;

    public static string Of(PadControl control, PadKind kind) => kind != PadKind.Xbox360 ? Of(control) : control switch
    {
        PadControl.Cross => "A",
        PadControl.Circle => "B",
        PadControl.Square => "X",
        PadControl.Triangle => "Y",
        PadControl.L1 => "LB",
        PadControl.R1 => "RB",
        PadControl.L2 => "LT",
        PadControl.R2 => "RT",
        PadControl.L3 => "LS",
        PadControl.R3 => "RS",
        PadControl.Options => "Start",
        PadControl.Share => "Back",
        _ => Of(control),
    };

    public static string Labelled(PadControl control, string? label, PadKind kind = PadKind.DualShock4) =>
        label is null ? Of(control, kind) : $"{label} ({Of(control, kind)})";
```

Remove the old two-parameter `Labelled`. `XboxGroups` must be declared **after** `Groups` in the file (static initialisers run in order).

`OverlayText.Controls`: signature `public static string Controls(ControlSettings settings, PadKind kind = PadKind.DualShock4)`; use `KeyNames.GroupsFor(kind)` instead of `KeyNames.Groups` and `KeyNames.Labelled(c, settings.LabelFor(c), kind)`. Add `using CouchLink.Core.Pads;`.

`ControlFilter.cs`:

```csharp
    public static string RowName(PadControl control, PadKind kind = PadKind.DualShock4) => control switch
    {
        PadControl.LeftUp or PadControl.RightUp or PadControl.DpadUp => "Up",
        PadControl.LeftDown or PadControl.RightDown or PadControl.DpadDown => "Down",
        PadControl.LeftLeft or PadControl.RightLeft or PadControl.DpadLeft => "Left",
        PadControl.LeftRight or PadControl.RightRight or PadControl.DpadRight => "Right",
        _ => KeyNames.Of(control, kind),
    };

    public static bool Matches(PadControl control, string group, string? label, string? query, PadKind kind = PadKind.DualShock4)
    {
        string q = (query ?? "").Trim();
        if (q.Length == 0)
            return true;
        return Has(KeyNames.Of(control, kind)) || Has(group) || (label is not null && Has(label));

        bool Has(string text) => text.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
```

- [ ] **Step 4: Run tests**

Run: `dotnet build` then `dotnet test tests/CouchLink.Core.Tests` and `dotnet test tests/CouchLink.App.Tests --filter FullyQualifiedName~ControlFilterTests`
Expected: PASS (the existing F1 panel tests still pass unchanged).

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Input/KeyNames.cs src/CouchLink.Core/Video/OverlayText.cs src/CouchLink.App/Presentation/ControlFilter.cs tests
git commit -m "feat(input): Xbox 360 control names for the F1 panel and editor"
```

---

### Task 6: Virtual Xbox 360 pads

**Files:**
- Rename: `src/CouchLink.Pads/ViGEmPad.cs` → `src/CouchLink.Pads/ViGEmDs4Pad.cs` (class `ViGEmDs4Pad`)
- Create: `src/CouchLink.Pads/ViGEmXbox360Pad.cs`
- Create: `src/CouchLink.Pads/RemovedTargets.cs`
- Modify: `src/CouchLink.Pads/ViGEmPadFactory.cs`

**Interfaces:**
- Consumes: `PadKind` (Task 1), `Xbox360Report.From` (Task 1).
- Produces: `ViGEmPadFactory.TryCreate(PadKind kind, out ViGEmPadFactory? factory, out string? error)`; the old `TryCreate(out, out)` stays and means DualShock 4 (PadTest uses it); `ViGEmPadFactory.Kind`.

ViGEmBus can't run in CI, so this task is verified by build plus the manual hardware check in Task 13.

- [ ] **Step 1: Move the never-free list to its own file**

`src/CouchLink.Pads/RemovedTargets.cs`:

```csharp
namespace CouchLink.Pads;

/// <summary>
/// Never free a ViGEm target. Connect starts a native notification thread (vigemclient.dll) that nothing can stop;
/// it stays blocked after unplug and reads the target struct if it ever wakes. Freeing the target (Dispose, or the
/// finalizer once unreferenced) is a use-after-free that crashes the host. Rooting it costs about one idle thread
/// and 2 handles per pad ever created.
/// </summary>
internal static class RemovedTargets
{
    private static readonly List<object> Kept = [];

    public static void Keep(object target)
    {
        lock (Kept)
            Kept.Add(target);
    }
}
```

- [ ] **Step 2: Rename the DS4 pad**

`git mv src/CouchLink.Pads/ViGEmPad.cs src/CouchLink.Pads/ViGEmDs4Pad.cs`. In it: class and constructor renamed `ViGEmDs4Pad`, summary unchanged, delete the `Removed` list, and `Dispose` becomes:

```csharp
    public void Dispose()
    {
        _pad.Disconnect();
        RemovedTargets.Keep(_pad);
    }
```

- [ ] **Step 3: Write the Xbox 360 pad**

`src/CouchLink.Pads/ViGEmXbox360Pad.cs`:

```csharp
using CouchLink.Core.Input;
using CouchLink.Core.Pads;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace CouchLink.Pads;

/// <summary>A virtual Xbox 360 pad on ViGEmBus. Each Apply translates the DS4-shaped state and sends one report.</summary>
public sealed class ViGEmXbox360Pad : IVirtualPad
{
    private readonly IXbox360Controller _pad;

    internal ViGEmXbox360Pad(ViGEmClient client)
    {
        _pad = client.CreateXbox360Controller();
        _pad.AutoSubmitReport = false;
        _pad.Connect();
        Apply(PadState.Neutral);
    }

    public void Apply(PadState s)
    {
        var r = Xbox360Report.From(s);
        _pad.SetButtonsFull((ushort)r.Buttons);
        _pad.SetSliderValue(Xbox360Slider.LeftTrigger, r.LeftTrigger);
        _pad.SetSliderValue(Xbox360Slider.RightTrigger, r.RightTrigger);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbX, r.LeftX);
        _pad.SetAxisValue(Xbox360Axis.LeftThumbY, r.LeftY);
        _pad.SetAxisValue(Xbox360Axis.RightThumbX, r.RightX);
        _pad.SetAxisValue(Xbox360Axis.RightThumbY, r.RightY);
        _pad.SubmitReport();
    }

    public void Dispose()
    {
        _pad.Disconnect();
        RemovedTargets.Keep(_pad); // see RemovedTargets: never free a connected target
    }
}
```

(`SetButtonsFull`, `SetSliderValue`, `SetAxisValue`, `SubmitReport` and `AutoSubmitReport` exist on `IXbox360Controller` in 1.21.256; checked by reflection while writing this plan.)

- [ ] **Step 4: The factory builds the session's kind**

`ViGEmPadFactory.cs`:

```csharp
using CouchLink.Core.Pads;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace CouchLink.Pads;

public sealed class ViGEmPadFactory : IVirtualPadFactory, IDisposable
{
    public const string DriverMissingMessage = "ViGEmBus driver not installed";

    private readonly ViGEmClient _client;

    private ViGEmPadFactory(ViGEmClient client, PadKind kind)
    {
        _client = client;
        Kind = kind;
    }

    /// <summary>The pad every Create plugs in.</summary>
    public PadKind Kind { get; }

    public static bool TryCreate(out ViGEmPadFactory? factory, out string? error) =>
        TryCreate(PadKind.DualShock4, out factory, out error);

    public static bool TryCreate(PadKind kind, out ViGEmPadFactory? factory, out string? error)
    {
        try
        {
            factory = new ViGEmPadFactory(new ViGEmClient(), kind);
            error = null;
            return true;
        }
        catch (VigemBusNotFoundException)
        {
            factory = null;
            error = DriverMissingMessage;
            return false;
        }
    }

    public IVirtualPad Create() => Kind == PadKind.Xbox360 ? new ViGEmXbox360Pad(_client) : new ViGEmDs4Pad(_client);

    public void Dispose() => _client.Dispose();
}
```

- [ ] **Step 5: Build, run all tests, commit**

Run: `dotnet build` then `dotnet test`
Expected: build succeeds; all tests PASS. Search for any remaining `ViGEmPad` (without `Ds4`/`Xbox360`/`Factory`) in `src`, `tests` and docs code comments, and fix it.

```bash
git add -A src/CouchLink.Pads
git commit -m "feat(pads): plug in virtual Xbox 360 pads when the host picks them"
```

---

### Task 7: Remember the host's controller choice

**Files:**
- Create: `src/CouchLink.App/Presentation/HostSettings.cs`
- Modify: `src/CouchLink.App/AppServices.cs`
- Test: `tests/CouchLink.App.Tests/HostSettingsTests.cs`

**Interfaces:**
- Consumes: `PadKind` (Task 1).
- Produces: `internal sealed class HostSettings(string path, Action<string> log)` with `PadKind Controller { get; set; }` (saves on set when it changes) and `static string DefaultPath`; `AppServices.HostSettings` (settable, for tests).

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/HostSettingsTests.cs`:

```csharp
using System.IO;
using CouchLink.App.Presentation;
using CouchLink.Core.Pads;

namespace CouchLink.App.Tests;

public sealed class HostSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "CouchLinkTests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    private string SettingsFile => Path.Combine(_folder, "settings.json");

    private HostSettings Open() => new(SettingsFile, _log.Add);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void DualShock_4_without_a_settings_file()
    {
        Assert.Equal(PadKind.DualShock4, Open().Controller);
        Assert.Empty(_log);
    }

    [Fact]
    public void The_choice_is_saved_and_read_back()
    {
        Open().Controller = PadKind.Xbox360;
        Assert.Equal(PadKind.Xbox360, Open().Controller);
        Assert.Contains("\"hostController\": \"xbox360\"", File.ReadAllText(SettingsFile));

        Open().Controller = PadKind.DualShock4;
        Assert.Contains("\"hostController\": \"dualshock4\"", File.ReadAllText(SettingsFile));
    }

    [Theory]
    [InlineData("{ \"hostController\": \"ps5\" }")]
    [InlineData("{ \"hostController\": 2 }")]
    [InlineData("{ not json")]
    public void A_bad_value_reads_as_DualShock_4_and_is_logged_once(string text)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsFile, text);
        Assert.Equal(PadKind.DualShock4, Open().Controller);
        Assert.Single(_log);
    }

    [Fact]
    public void Other_settings_are_kept()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsFile, "{ \"reduceMotion\": true }");
        Open().Controller = PadKind.Xbox360;
        Assert.Contains("\"reduceMotion\": true", File.ReadAllText(SettingsFile));
        Assert.True(new MotionSettings(SettingsFile, _log.Add).ReduceMotion);
    }

    [Fact]
    public void A_file_that_cannot_be_written_keeps_the_choice_for_this_run_and_logs()
    {
        Directory.CreateDirectory(SettingsFile); // a folder where the file should be
        var settings = Open();
        settings.Controller = PadKind.Xbox360;
        Assert.Equal(PadKind.Xbox360, settings.Controller);
        Assert.NotEmpty(_log);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter FullyQualifiedName~HostSettingsTests`
Expected: build FAILS (`HostSettings` missing).

- [ ] **Step 3: Write the implementation**

`src/CouchLink.App/Presentation/HostSettings.cs`:

```csharp
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CouchLink.Core.Pads;

namespace CouchLink.App.Presentation;

/// <summary>
/// The controller the host picked last, saved per PC in %LOCALAPPDATA%\CouchLink\settings.json as
/// <c>{ "hostController": "xbox360" }</c>. DualShock 4 by default. A missing file reads as DualShock 4; an unreadable
/// one or an unknown value reads as DualShock 4 and is logged, never thrown; a file that can't be written keeps the
/// choice for this run. Other keys in the file are kept when it is saved.
/// </summary>
internal sealed class HostSettings
{
    private const string Key = "hostController";
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Action<string> _log;
    private PadKind _controller;

    public HostSettings(string path, Action<string> log)
    {
        _path = path;
        _log = log;
        _controller = Read();
    }

    public static string DefaultPath => MotionSettings.DefaultPath;

    public PadKind Controller
    {
        get => _controller;
        set
        {
            if (value == _controller)
                return;
            _controller = value;
            Save();
        }
    }

    private static string Name(PadKind kind) => kind == PadKind.Xbox360 ? "xbox360" : "dualshock4";

    private PadKind Read()
    {
        if (!File.Exists(_path))
            return PadKind.DualShock4;
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(_path))?.AsObject()[Key];
            if (node is null)
                return PadKind.DualShock4;
            return node.GetValue<string>() switch
            {
                "xbox360" => PadKind.Xbox360,
                "dualshock4" => PadKind.DualShock4,
                var other => throw new FormatException($"unknown controller \"{other}\""),
            };
        }
        catch (Exception ex)
        {
            _log($"Settings: could not read the host controller from {_path}, so it is DualShock 4: {ex.Message}");
            return PadKind.DualShock4;
        }
    }

    private void Save()
    {
        try
        {
            var root = ReadObject() ?? [];
            root[Key] = Name(_controller);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, root.ToJsonString(Indented));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            _log($"Settings: could not save {_path}; the host controller is {PadKinds.Label(_controller)} until CouchLink closes: {ex.Message}");
        }
    }

    private JsonObject? ReadObject()
    {
        try
        {
            return File.Exists(_path) ? JsonNode.Parse(File.ReadAllText(_path)) as JsonObject : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
```

`AppServices.cs`, after `MotionSettings`:

```csharp
    /// <summary>The controller the host picked last, saved for this PC; tests swap it.</summary>
    public static HostSettings HostSettings { get; set; } = new(HostSettings.DefaultPath, Log.Write);
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/CouchLink.App.Tests --filter "FullyQualifiedName~HostSettingsTests|FullyQualifiedName~MotionSettingsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.App/Presentation/HostSettings.cs src/CouchLink.App/AppServices.cs tests/CouchLink.App.Tests/HostSettingsTests.cs
git commit -m "feat(app): remember the host's controller choice per PC"
```

---

### Task 8: The Host a game setup screen

**Files:**
- Create: `src/CouchLink.App/Views/HostSetupView.xaml`, `src/CouchLink.App/Views/HostSetupView.xaml.cs`
- Modify: `src/CouchLink.App/MainWindow.xaml.cs:80-90`
- Modify: `src/CouchLink.App/HostService.cs:43-81`
- Modify: `src/CouchLink.App/Views/HostLobbyView.xaml` and `.xaml.cs`
- Test: `tests/CouchLink.App.Tests/HostSetupTests.cs`, `tests/CouchLink.App.Tests/HostLobbyTests.cs`

**Interfaces:**
- Consumes: `PadKind`, `PadKinds` (Task 1); `SessionServer.Start(..., padKind)` and `SessionServer.Capacity` (Task 3); `ViGEmPadFactory.TryCreate(kind, ...)` (Task 6); `AppServices.HostSettings` (Task 7).
- Produces: `HostSetupView(PadKind initial)` with `PadKind Selected`, `event Action? BackClicked`, `event Action<PadKind>? StartRequested`, `void ShowError(string message)`, `static string HelpFor(PadKind)`; `HostLobbyView(PadKind kind = PadKind.DualShock4)`; `HostService.TryStart(PadKind kind, StreamSettings settings, IHostUi ui, out HostService? service, out string? error)`, `HostService.PadKind`, `HostService.Capacity`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/HostSetupTests.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CouchLink.App.Theme;
using CouchLink.App.Views;
using CouchLink.Core.Pads;

namespace CouchLink.App.Tests;

public class HostSetupTests
{
    private static HostSetupView Setup(PadKind initial = PadKind.DualShock4)
    {
        ThemeManager.Install(Application.Current);
        return new HostSetupView(initial);
    }

    private static RadioButton Choice(HostSetupView view, PadKind kind) =>
        view.PadKindGroup.Children.OfType<RadioButton>().Single(r => (PadKind)r.Tag == kind);

    [Fact]
    public void It_starts_on_the_remembered_choice_with_its_help()
    {
        Wpf.Run(() =>
        {
            var view = Setup(PadKind.Xbox360);
            Assert.Equal(PadKind.Xbox360, view.Selected);
            Assert.Equal("Up to 4 players. For games that only work with Xbox controllers, like NBA 2K14.", view.HelpText.Text);
        });
    }

    [Fact]
    public void Switching_the_controller_changes_the_help()
    {
        Wpf.Run(() =>
        {
            var view = Setup();
            Assert.Equal("Up to 9 players. Works with most games, like NBA 2K22.", view.HelpText.Text);
            Choice(view, PadKind.Xbox360).IsChecked = true;
            Assert.Equal(PadKind.Xbox360, view.Selected);
            Assert.StartsWith("Up to 4 players.", view.HelpText.Text);
        });
    }

    [Fact]
    public void Start_hosting_asks_for_the_selected_kind_and_Back_goes_back()
    {
        Wpf.Run(() =>
        {
            var view = Setup();
            PadKind? started = null;
            bool back = false;
            view.StartRequested += k => started = k;
            view.BackClicked += () => back = true;
            Choice(view, PadKind.Xbox360).IsChecked = true;
            view.StartButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            view.BackButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(PadKind.Xbox360, started);
            Assert.True(back);
            Assert.True(view.StartButton.IsDefault);
        });
    }

    [Fact]
    public void A_failed_start_shows_the_error_and_keeps_the_choice()
    {
        Wpf.Run(() =>
        {
            var view = Setup(PadKind.Xbox360);
            Assert.Equal(Visibility.Collapsed, view.ErrorBanner.Visibility);
            view.ShowError("ViGEmBus driver not installed");
            Assert.Equal(Visibility.Visible, view.ErrorBanner.Visibility);
            Assert.Equal("ViGEmBus driver not installed", view.ErrorBanner.Text);
            Assert.Equal(PadKind.Xbox360, view.Selected);
        });
    }
}
```

In `HostLobbyTests.cs`, make the two heading assertions use `PadKinds.Capacity(PadKind.DualShock4) + 1` (Task 3 already did), and add:

```csharp
    [Fact]
    public void An_Xbox_lobby_shows_its_controller_and_counts_to_five()
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            var lobby = new HostLobbyView(PadKind.Xbox360);
            lobby.ShowPlayers([]);
            Assert.Equal("PLAYERS 1 / 5", lobby.PlayersHeading.Text);
            Assert.Equal("Controller: Xbox 360 · up to 4 players", lobby.ControllerText.Text);
        });
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter "FullyQualifiedName~HostSetupTests|FullyQualifiedName~HostLobbyTests"`
Expected: build FAILS.

- [ ] **Step 3: Write the setup view**

`src/CouchLink.App/Views/HostSetupView.xaml` (header copies the Join list's back-arrow pattern; the segmented control copies the lobby's Quality picker):

```xml
<UserControl x:Class="CouchLink.App.Views.HostSetupView" x:ClassModifier="internal"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="clr-namespace:CouchLink.App.Ui">
    <DockPanel>
        <Border DockPanel.Dock="Bottom" BorderBrush="{DynamicResource BorderBrush}" BorderThickness="0,1,0,0" Padding="16,12">
            <Button x:Name="StartButton" Style="{DynamicResource PrimaryButton}" Content="Start hosting" MinHeight="40" IsDefault="True"
                    AutomationProperties.AutomationId="StartHosting"/>
        </Border>
        <ScrollViewer VerticalScrollBarVisibility="Auto" Focusable="False">
            <StackPanel Margin="16">
                <StackPanel Orientation="Horizontal">
                    <Button x:Name="BackButton" Style="{DynamicResource GhostButton}" Padding="6,0"
                            AutomationProperties.Name="Back" AutomationProperties.AutomationId="HostSetupBack">
                        <ui:Glyph Data="{StaticResource IconArrowLeft}" Brush="{DynamicResource PrimaryTextBrush}"/>
                    </Button>
                    <TextBlock Text="Host a game" Style="{StaticResource TitleText}" Margin="6,0,0,0" VerticalAlignment="Center"/>
                </StackPanel>
                <TextBlock Style="{StaticResource BodyMutedText}" Margin="0,4,0,0" TextWrapping="Wrap"
                           Text="Pick the controller the game will see. Friends can find this PC once you start hosting."/>
                <ui:Banner x:Name="ErrorBanner" Kind="Error" Margin="0,12,0,0" Visibility="Collapsed"
                           AutomationProperties.AutomationId="HostSetupError"/>
                <Border Style="{StaticResource Card}" Margin="0,16,0,0">
                    <StackPanel>
                        <TextBlock Text="CONTROLLER" Style="{StaticResource OverlineText}"/>
                        <Border Style="{StaticResource Segmented}" Margin="0,8,0,0">
                            <Grid>
                                <ui:SegmentIndicator x:Name="PadKindIndicator"/>
                                <UniformGrid x:Name="PadKindGroup" Rows="1" AutomationProperties.Name="Controller"/>
                            </Grid>
                        </Border>
                        <TextBlock x:Name="HelpText" Style="{StaticResource CaptionText}" Margin="0,8,0,0" TextWrapping="Wrap"
                                   AutomationProperties.AutomationId="PadKindHelp"/>
                    </StackPanel>
                </Border>
            </StackPanel>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

`src/CouchLink.App/Views/HostSetupView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using CouchLink.Core.Pads;

namespace CouchLink.App.Views;

/// <summary>
/// "Host a game": the controller every player gets (DualShock 4 or Xbox 360), then Start hosting. Nothing is
/// broadcast and no pads exist until then. Back and Esc return to the start screen.
/// </summary>
internal sealed partial class HostSetupView : UserControl
{
    public HostSetupView(PadKind initial)
    {
        InitializeComponent();
        foreach (var kind in new[] { PadKind.DualShock4, PadKind.Xbox360 })
        {
            var item = new RadioButton
            {
                Content = PadKinds.Label(kind),
                Tag = kind,
                GroupName = "PadKind",
                IsChecked = kind == initial,
            };
            item.SetResourceReference(StyleProperty, "SegmentedItem"); // follows Reduce motion live
            AutomationProperties.SetAutomationId(item, $"PadKind{kind}");
            item.Checked += (_, _) => HelpText.Text = HelpFor(Selected);
            PadKindGroup.Children.Add(item);
        }
        PadKindIndicator.Track(PadKindGroup);
        HelpText.Text = HelpFor(initial);
        BackButton.Click += (_, _) => BackClicked?.Invoke();
        StartButton.Click += (_, _) => StartRequested?.Invoke(Selected);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape)
                return;
            e.Handled = true;
            BackClicked?.Invoke();
        };
    }

    public event Action? BackClicked;
    public event Action<PadKind>? StartRequested;

    public PadKind Selected => (PadKind)PadKindGroup.Children.OfType<RadioButton>().First(r => r.IsChecked == true).Tag;

    public static string HelpFor(PadKind kind) => kind == PadKind.Xbox360
        ? "Up to 4 players. For games that only work with Xbox controllers, like NBA 2K14."
        : "Up to 9 players. Works with most games, like NBA 2K22.";

    /// <summary>Hosting could not start: say why here and keep the choice.</summary>
    public void ShowError(string message)
    {
        ErrorBanner.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }
}
```

- [ ] **Step 4: Host service and lobby take the kind**

`HostService.cs`: the constructor gains `PadKind kind` (store as `PadKind` property, `Capacity => _server.Capacity`); `_server.Start(this, TimeProvider.System, message => AppServices.Log.Write(message), OnError, kind);`; the announce becomes `() => HostAnnounce.For(_server.PlayerCount, _server.Capacity, PcName.ThisPc)`; `TryStart(PadKind kind, StreamSettings settings, IHostUi ui, out HostService? service, out string? error)` calls `ViGEmPadFactory.TryCreate(kind, out var factory, out error)` and `new HostService(receiver!, server!, factory!, kind, settings, ui)`.

```csharp
    /// <summary>The virtual controller every player gets.</summary>
    public PadKind PadKind { get; }

    /// <summary>Clients this host takes.</summary>
    public int Capacity => _server.Capacity;
```

`HostLobbyView.xaml`: inside the players card, between `PlayersHeading` and `PlayerList`:

```xml
                        <TextBlock x:Name="ControllerText" Style="{StaticResource CaptionText}" Margin="0,6,0,0"
                                   AutomationProperties.AutomationId="LobbyController"/>
```

`HostLobbyView.xaml.cs`: add `private readonly PadKind _kind;`, constructor `public HostLobbyView(PadKind kind = PadKind.DualShock4)` sets `_kind = kind;` and `ControllerText.Text = $"Controller: {PadKinds.Label(kind)} · up to {PadKinds.Capacity(kind)} players";` after `InitializeComponent()`; `TryStart` calls `HostService.TryStart(_kind, CurrentSettings(), this, out _host, out error)`; `AppServices.DescribeMode = () => $"Host ({PadKinds.Label(_kind)}, virtual pads: {_host?.PadCount ?? 0})";`; `AppServices.Log.Write($"Hosting started ({PadKinds.Label(_kind)})");`; `ShowPlayers` heading `$"PLAYERS {players.Count + 1} / {PadKinds.Capacity(_kind) + 1}"`. Update the class summary: "the controller (read-only)".

- [ ] **Step 5: Main window flow**

`MainWindow.xaml.cs` (add `using CouchLink.Core.Pads;`):

```csharp
    private void ShowHost()
    {
        var setup = new HostSetupView(AppServices.HostSettings.Controller);
        setup.BackClicked += ShowStart;
        setup.StartRequested += kind => StartHosting(setup, kind);
        Show(setup);
    }

    /// <summary>Start hosting on the setup screen: the lobby on success, otherwise the reason on the setup screen.</summary>
    private void StartHosting(HostSetupView setup, PadKind kind)
    {
        var lobby = new HostLobbyView(kind);
        if (!lobby.TryStart(out var error))
        {
            setup.ShowError(error ?? "Hosting could not start.");
            return;
        }
        AppServices.HostSettings.Controller = kind;
        lobby.Stopped += ShowStart;
        Show(lobby);
    }
```

- [ ] **Step 6: Run tests**

Run: `dotnet build` then `dotnet test tests/CouchLink.App.Tests`
Expected: PASS. If a start-screen test asserted that Host opens the lobby, change it to expect `HostSetupView`.

- [ ] **Step 7: Commit**

```bash
git add src/CouchLink.App tests/CouchLink.App.Tests
git commit -m "feat(app): pick the controller on a Host a game screen before hosting starts"
```

---

### Task 9: Clients see Xbox names in the F1 panel and the editor

**Files:**
- Modify: `src/CouchLink.App/ClientPlay.cs:39-56`
- Modify: `src/CouchLink.App/MainWindow.xaml.cs:32, 105, 112-125, 173-178`
- Modify: `src/CouchLink.App/ControlsWindow.xaml.cs:53-84, 161-212, 233-299`
- Test: `tests/CouchLink.App.Tests/ControlsEditorTests.cs`

**Interfaces:**
- Consumes: `KeyNames.GroupsFor/Of(control, kind)`, `OverlayText.Controls(settings, kind)`, `ControlFilter.RowName/Matches(..., kind)` (Task 5); `ClientSessionService.PadKind`, `IClientUi.StartPlaying(slot, kind)` (Task 4).
- Produces: `ControlsWindow.Open(Window? owner, bool overGame = false, Action? closed = null, PadKind kind = PadKind.DualShock4)`; `internal ControlsWindow(PadKind kind = PadKind.DualShock4)`; `ControlsWindow.Kind`; `ClientPlay.TryStart(Window, IPAddress, byte slot, PadKind kind, Action leave, Func<string?> sessionStatus, Action openControls, out ClientPlay?, out string?)`.

- [ ] **Step 1: Write the failing tests**

In `ControlsEditorTests`, give `WithEditor` a kind parameter and add tests:

```csharp
    private static void WithEditor(Action<ControlsWindow> test, PadKind kind = PadKind.DualShock4)
    {
        Wpf.Run(() =>
        {
            ThemeManager.Install(Application.Current);
            AppServices.Controls.ResetToDefault();
            var editor = new ControlsWindow(kind);
            try
            {
                test(editor);
            }
            finally
            {
                editor.Close();
                AppServices.Controls.ResetToDefault();
            }
        });
    }

    [Fact]
    public void An_Xbox_editor_has_Xbox_rows_and_no_touchpad()
    {
        WithEditor(editor =>
        {
            Assert.False(editor.HasRow(PadControl.Touchpad));
            Assert.Equal("A", editor.RowAutomationName(PadControl.Cross));
            editor.FindBox.Text = "lb";
            Assert.True(editor.IsRowVisible(PadControl.L1));
            Assert.False(editor.IsRowVisible(PadControl.R1));
        }, PadKind.Xbox360);
    }

    [Fact]
    public void A_DualShock_editor_keeps_its_touchpad_row()
    {
        WithEditor(editor =>
        {
            Assert.True(editor.HasRow(PadControl.Touchpad));
            Assert.Equal("Cross", editor.RowAutomationName(PadControl.Cross));
        });
    }
```

(add `using CouchLink.Core.Pads;` and `using System.Windows.Automation;`)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter FullyQualifiedName~ControlsEditorTests`
Expected: build FAILS.

- [ ] **Step 3: Editor uses the kind**

`ControlsWindow.xaml.cs` (add `using CouchLink.Core.Pads;`):

```csharp
    public static void Open(Window? owner, bool overGame = false, Action? closed = null, PadKind kind = PadKind.DualShock4)
    {
        // ... unchanged, except:
        var window = new ControlsWindow(kind) { Topmost = overGame };
        // ...
    }

    /// <summary>Internal for tests; use <see cref="Open"/>.</summary>
    internal ControlsWindow(PadKind kind = PadKind.DualShock4)
    {
        Kind = kind;
        InitializeComponent();
        // ... rest unchanged
    }

    /// <summary>The session's pad: names, rows and (Task 11) the picture follow it.</summary>
    internal PadKind Kind { get; }

    internal bool HasRow(PadControl control) => _rows.ContainsKey(control);

    internal string RowAutomationName(PadControl control) => AutomationProperties.GetName(_rows[control]);
```

In `BuildRows`: `foreach (var (group, controls) in KeyNames.GroupsFor(Kind))`, and both `KeyNames.Of(control)` → `KeyNames.Of(control, Kind)`. In `RowContent`: `ControlFilter.RowName(control, Kind)`. In `ApplyFilter`: `ControlFilter.Matches(control, name, _settings.LabelFor(control), query, Kind)`. `Refresh` and `Bind` already iterate `_rows`, so a missing Touchpad row is fine; `Bind`'s `_rows[moved].BringIntoView()` must become `if (_rows.TryGetValue(moved, out var movedRow)) movedRow.BringIntoView();` (a key can move from Touchpad, which has no row in Xbox mode). Likewise `_labelBoxes` iteration is fine.

- [ ] **Step 4: Client passes the kind on**

`ClientPlay.TryStart` gains `PadKind kind` after `slot` and uses `() => OverlayText.Controls(AppServices.Controls, kind)`. `MainWindow`:

```csharp
        Header.ControlsClicked += () => ControlsWindow.Open(this, kind: _session?.PadKind ?? PadKind.DualShock4);
        // ...
        _sessionView.ControlsClicked += () => ControlsWindow.Open(this, kind: _session?.PadKind ?? PadKind.DualShock4);
        // ...
    void IClientUi.StartPlaying(byte slot, PadKind kind)
    {
        if (_session is not { } session || _closed)
            return;
        if (!ClientPlay.TryStart(this, session.Host, slot, kind, () => Dispatcher.InvokeAsync(LeaveSession),
                () => session.PlayerStatus, () => Dispatcher.InvokeAsync(OpenControlsOverGame), out _play, out var error))
        // ... rest unchanged
    }
        // ...
        ControlsWindow.Open(this, overGame: true, closed: ReturnToGame, kind: _session?.PadKind ?? PadKind.DualShock4);
```

- [ ] **Step 5: Run tests, commit**

Run: `dotnet build` then `dotnet test tests/CouchLink.App.Tests`
Expected: PASS.

```bash
git add src/CouchLink.App tests/CouchLink.App.Tests/ControlsEditorTests.cs
git commit -m "feat(app): Xbox names in the F1 panel and controls editor during Xbox sessions"
```

---

### Task 10: Which parts of the picture are pressed

**Files:**
- Create: `src/CouchLink.Core/Input/PadPicture.cs`
- Test: `tests/CouchLink.Core.Tests/PadPictureTests.cs`

**Interfaces:**
- Consumes: `PadState`, `PadButtons`, `DpadMath` (existing); `PadKind` (Task 1).
- Produces: `readonly record struct PadPicture(IReadOnlySet<PadControl> Lit, double LeftX, double LeftY, double RightX, double RightY)` with `static PadPicture Neutral` and `static PadPicture From(PadState state, PadKind kind)`; `const byte TriggerThreshold = 32`. Stick offsets are -1..1, Y positive down (screen coordinates).

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/PadPictureTests.cs`:

```csharp
using CouchLink.Core.Input;
using CouchLink.Core.Pads;

namespace CouchLink.Core.Tests;

public class PadPictureTests
{
    private static PadPicture Of(PadState s, PadKind kind = PadKind.DualShock4) => PadPicture.From(s, kind);

    [Theory]
    [InlineData(PadButtons.Cross, PadControl.Cross)]
    [InlineData(PadButtons.Circle, PadControl.Circle)]
    [InlineData(PadButtons.Square, PadControl.Square)]
    [InlineData(PadButtons.Triangle, PadControl.Triangle)]
    [InlineData(PadButtons.L1, PadControl.L1)]
    [InlineData(PadButtons.R1, PadControl.R1)]
    [InlineData(PadButtons.L3, PadControl.L3)]
    [InlineData(PadButtons.R3, PadControl.R3)]
    [InlineData(PadButtons.Options, PadControl.Options)]
    [InlineData(PadButtons.Share, PadControl.Share)]
    [InlineData(PadButtons.Touchpad, PadControl.Touchpad)]
    [InlineData(PadButtons.DpadUp, PadControl.DpadUp)]
    [InlineData(PadButtons.DpadLeft, PadControl.DpadLeft)]
    public void Each_button_lights_its_part(PadButtons button, PadControl part)
    {
        Assert.Equal([part], Of(PadState.Neutral with { Buttons = button }).Lit);
    }

    [Fact]
    public void Neutral_lights_nothing_and_centres_the_sticks()
    {
        var p = Of(PadState.Neutral);
        Assert.Empty(p.Lit);
        Assert.Equal((0.0, 0.0, 0.0, 0.0), (p.LeftX, p.LeftY, p.RightX, p.RightY));
        Assert.Equal(PadPicture.Neutral.Lit, p.Lit);
    }

    [Fact]
    public void Opposite_dpad_directions_light_neither()
    {
        Assert.Empty(Of(PadState.Neutral with { Buttons = PadButtons.DpadLeft | PadButtons.DpadRight }).Lit);
        Assert.Equal([PadControl.DpadUp, PadControl.DpadRight],
            Of(PadState.Neutral with { Buttons = PadButtons.DpadUp | PadButtons.DpadRight }).Lit.Order());
    }

    [Theory]
    [InlineData(32, false)]
    [InlineData(33, true)]
    [InlineData(255, true)]
    public void A_trigger_lights_above_the_threshold(byte value, bool lit)
    {
        Assert.Equal(lit, Of(PadState.Neutral with { L2 = value }).Lit.Contains(PadControl.L2));
        Assert.Equal(lit, Of(PadState.Neutral with { R2 = value }).Lit.Contains(PadControl.R2));
    }

    [Fact]
    public void A_trigger_bit_lights_it_even_at_zero()
    {
        Assert.Contains(PadControl.L2, Of(PadState.Neutral with { Buttons = PadButtons.L2 }).Lit);
    }

    [Fact]
    public void Full_tilt_reaches_one_each_way()
    {
        var p = Of(PadState.Neutral with { LX = 0, LY = 255, RX = 255, RY = 0 });
        Assert.Equal((-1.0, 1.0, 1.0, -1.0), (p.LeftX, p.LeftY, p.RightX, p.RightY));
    }

    [Fact]
    public void Xbox_never_lights_the_touchpad()
    {
        Assert.Empty(Of(PadState.Neutral with { Buttons = PadButtons.Touchpad }, PadKind.Xbox360).Lit);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter FullyQualifiedName~PadPictureTests`
Expected: build FAILS.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Input/PadPicture.cs`:

```csharp
using CouchLink.Core.Pads;

namespace CouchLink.Core.Input;

/// <summary>
/// What the editor's controller picture shows for one pad state: the lit parts and how far each stick leans
/// (-1..1, Y down as on screen). The D-pad is lit as the game sees it (opposite directions cancel); a trigger
/// lights when pressed past <see cref="TriggerThreshold"/>. Xbox 360 has no touchpad.
/// </summary>
public readonly record struct PadPicture(IReadOnlySet<PadControl> Lit, double LeftX, double LeftY, double RightX, double RightY)
{
    public const byte TriggerThreshold = 32;

    private static readonly (PadButtons Button, PadControl Part)[] Buttons =
    [
        (PadButtons.Cross, PadControl.Cross), (PadButtons.Circle, PadControl.Circle),
        (PadButtons.Square, PadControl.Square), (PadButtons.Triangle, PadControl.Triangle),
        (PadButtons.L1, PadControl.L1), (PadButtons.R1, PadControl.R1),
        (PadButtons.L3, PadControl.L3), (PadButtons.R3, PadControl.R3),
        (PadButtons.Options, PadControl.Options), (PadButtons.Share, PadControl.Share),
        (PadButtons.Touchpad, PadControl.Touchpad),
    ];

    public static PadPicture Neutral { get; } = new(new HashSet<PadControl>(), 0, 0, 0, 0);

    public static PadPicture From(PadState s, PadKind kind)
    {
        var lit = new HashSet<PadControl>();
        foreach (var (button, part) in Buttons)
            if (s.Buttons.HasFlag(button))
                lit.Add(part);
        if (kind == PadKind.Xbox360)
            lit.Remove(PadControl.Touchpad);
        if (s.L2 > TriggerThreshold || s.Buttons.HasFlag(PadButtons.L2))
            lit.Add(PadControl.L2);
        if (s.R2 > TriggerThreshold || s.Buttons.HasFlag(PadButtons.R2))
            lit.Add(PadControl.R2);
        var d = DpadMath.FromButtons(s.Buttons);
        if (d is Dpad8.North or Dpad8.NorthEast or Dpad8.NorthWest) lit.Add(PadControl.DpadUp);
        if (d is Dpad8.South or Dpad8.SouthEast or Dpad8.SouthWest) lit.Add(PadControl.DpadDown);
        if (d is Dpad8.West or Dpad8.NorthWest or Dpad8.SouthWest) lit.Add(PadControl.DpadLeft);
        if (d is Dpad8.East or Dpad8.NorthEast or Dpad8.SouthEast) lit.Add(PadControl.DpadRight);
        return new PadPicture(lit, Lean(s.LX), Lean(s.LY), Lean(s.RX), Lean(s.RY));
    }

    private static double Lean(byte v) => Math.Clamp((v - PadState.Center) / 127.0, -1, 1);
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/CouchLink.Core.Tests --filter FullyQualifiedName~PadPictureTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Input/PadPicture.cs tests/CouchLink.Core.Tests/PadPictureTests.cs
git commit -m "feat(input): work out which controller parts a pad state lights"
```

---

### Task 11: `ControllerView`, the accurate controller drawing

**Files:**
- Create: `src/CouchLink.App/Ui/ControllerView.cs`
- Create: `src/CouchLink.App/Ui/ControllerShapes.cs`
- Test: `tests/CouchLink.App.Tests/ControllerViewTests.cs`

**Interfaces:**
- Consumes: `PadPicture` (Task 10); `PadKind` (Task 1); `Motion.Reduced` (existing, `CouchLink.App.Theme`).
- Produces: `internal sealed class ControllerView : Viewbox` with `PadKind Kind { get; set; }` (rebuilds), `void Show(PadPicture picture)`, `PadControl? Outlined { get; set; }`, `event Action<PadControl>? PartClicked`; for tests: `IReadOnlyCollection<PadControl> Parts`, `bool IsLit(PadControl)`, `string PartName(PadControl)`, `void ClickPart(PadControl)`. `static class ControllerShapes { string Symmetric(...) }`.

Clicking a stick's thumb raises `L3`/`R3` (the stick click); clicking the ring around it raises `LeftUp`/`RightUp`, which the editor treats as "show this stick's keys".

Geometry: coordinates are the reference images' pixels, copied from the approved mockup (https://claude.ai/artifact/FGDPuot1byFw9U7z78YHAC, version 2). DS4: viewBox `180 268 952 744`, from a 1280 px top-view photo (Wikimedia Commons, "Sony DualShock 4 wireless controller for PlayStation 4 (black) - top view.jpg"). Xbox 360: viewBox `178 40 600 360`, proportions from a 960 px front diagram. No artwork is copied; only measured positions.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.App.Tests/ControllerViewTests.cs`:

```csharp
using System.Windows;
using CouchLink.App.Theme;
using CouchLink.App.Ui;
using CouchLink.Core.Input;
using CouchLink.Core.Pads;

namespace CouchLink.App.Tests;

public class ControllerViewTests
{
    private static readonly PadControl[] Ds4Parts =
    [
        PadControl.Cross, PadControl.Circle, PadControl.Square, PadControl.Triangle,
        PadControl.L1, PadControl.R1, PadControl.L2, PadControl.R2, PadControl.L3, PadControl.R3,
        PadControl.Options, PadControl.Share, PadControl.Touchpad,
        PadControl.DpadUp, PadControl.DpadDown, PadControl.DpadLeft, PadControl.DpadRight,
        PadControl.LeftUp, PadControl.RightUp,
    ];

    private static ControllerView View(PadKind kind)
    {
        ThemeManager.Install(Application.Current);
        return new ControllerView { Kind = kind };
    }

    [Fact]
    public void A_DualShock_has_every_part_with_a_name()
    {
        Wpf.Run(() =>
        {
            var view = View(PadKind.DualShock4);
            Assert.Equal(Ds4Parts.Order(), view.Parts.Order());
            Assert.Equal("Cross, change key", view.PartName(PadControl.Cross));
            Assert.Equal("Left stick click, L3, change key", view.PartName(PadControl.L3));
            Assert.Equal("Left stick, show keys", view.PartName(PadControl.LeftUp));
        });
    }

    [Fact]
    public void An_Xbox_pad_has_no_touchpad_and_Xbox_names()
    {
        Wpf.Run(() =>
        {
            var view = View(PadKind.Xbox360);
            Assert.Equal(Ds4Parts.Where(p => p != PadControl.Touchpad).Order(), view.Parts.Order());
            Assert.Equal("A, change key", view.PartName(PadControl.Cross));
            Assert.Equal("Start, change key", view.PartName(PadControl.Options));
        });
    }

    [Fact]
    public void Show_lights_exactly_the_pressed_parts()
    {
        Wpf.Run(() =>
        {
            var view = View(PadKind.DualShock4);
            view.Show(PadPicture.From(PadState.Neutral with { Buttons = PadButtons.Cross | PadButtons.L1 }, PadKind.DualShock4));
            Assert.True(view.IsLit(PadControl.Cross));
            Assert.True(view.IsLit(PadControl.L1));
            Assert.False(view.IsLit(PadControl.Circle));
            view.Show(PadPicture.Neutral);
            Assert.False(view.IsLit(PadControl.Cross));
        });
    }

    [Fact]
    public void Clicking_a_part_raises_PartClicked()
    {
        Wpf.Run(() =>
        {
            var view = View(PadKind.Xbox360);
            PadControl? clicked = null;
            view.PartClicked += c => clicked = c;
            view.ClickPart(PadControl.Square);
            Assert.Equal(PadControl.Square, clicked);
        });
    }

    [Fact]
    public void Switching_kind_rebuilds_the_parts()
    {
        Wpf.Run(() =>
        {
            var view = View(PadKind.DualShock4);
            view.Kind = PadKind.Xbox360;
            Assert.DoesNotContain(PadControl.Touchpad, view.Parts);
        });
    }

    [Fact]
    public void Symmetric_mirrors_the_left_half()
    {
        string d = ControllerShapes.Symmetric((10, 0), [[(0, 0)], [(0, 5), (2, 9), (10, 10)]], cx: 10);
        Assert.Equal("M10 0 L0 0 C0 5 2 9 10 10 C18 9 20 5 20 0 L10 0 Z", d);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter FullyQualifiedName~ControllerViewTests`
Expected: build FAILS.

- [ ] **Step 3: Write the outline helper**

`src/CouchLink.App/Ui/ControllerShapes.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace CouchLink.App.Ui;

/// <summary>Path data for the controller outlines. Only the left half is written; the right half is its mirror.</summary>
internal static class ControllerShapes
{
    /// <summary>
    /// A closed outline symmetric about x = <paramref name="cx"/>. <paramref name="segments"/> run from
    /// <paramref name="start"/> (on the axis) around the left half back to the axis: one point is a line to it,
    /// three points are a cubic curve (control 1, control 2, end).
    /// </summary>
    public static string Symmetric((double X, double Y) start, (double X, double Y)[][] segments, double cx)
    {
        (double X, double Y) M((double X, double Y) p) => (2 * cx - p.X, p.Y);
        static string P((double X, double Y) p) => string.Create(CultureInfo.InvariantCulture, $"{p.X} {p.Y}");

        var d = new StringBuilder("M" + P(start));
        var points = new List<(double X, double Y)> { start };
        foreach (var s in segments)
        {
            d.Append(s.Length == 1 ? " L" + P(s[0]) : $" C{P(s[0])} {P(s[1])} {P(s[2])}");
            points.Add(s[^1]);
        }
        for (int i = segments.Length - 1; i >= 0; i--)
        {
            var s = segments[i];
            var from = points[i];
            d.Append(s.Length == 1 ? " L" + P(M(from)) : $" C{P(M(s[1]))} {P(M(s[0]))} {P(M(from))}");
        }
        return d.Append(" Z").ToString();
    }

    public static readonly string Ds4Body = Symmetric((656, 366),
    [
        [(440, 362)],
        [(395, 348), (330, 340), (282, 358)],
        [(238, 376), (214, 428), (207, 500)],
        [(201, 570), (204, 645), (214, 712)],
        [(227, 820), (250, 925), (286, 973)],
        [(312, 1003), (366, 998), (386, 962)],
        [(402, 925), (411, 845), (425, 792)],
        [(438, 752), (470, 738), (522, 738)],
        [(656, 738)],
    ], 656);

    public static readonly string Xbox360Body = Symmetric((478, 92),
    [
        [(400, 92), (340, 78), (292, 88)],
        [(246, 98), (222, 148), (212, 200)],
        [(202, 252), (199, 302), (209, 346)],
        [(217, 377), (250, 394), (292, 389)],
        [(326, 385), (339, 362), (350, 346)],
        [(362, 338), (400, 340), (430, 341)],
        [(478, 341)],
    ], 478);
}
```

- [ ] **Step 4: Write the view**

`src/CouchLink.App/Ui/ControllerView.cs`:

```csharp
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using CouchLink.App.Theme;
using CouchLink.Core.Input;
using CouchLink.Core.Pads;

namespace CouchLink.App.Ui;

/// <summary>
/// The session's controller, drawn from reference photos, lit as the player presses keys. Every button, D-pad arm,
/// bumper, trigger and stick is a focusable button named for screen readers; clicking one raises
/// <see cref="PartClicked"/>. A pressed part fills with the primary colour, glows and shrinks to 92% (no shrink with
/// Reduce motion); a trigger fills; a stick's thumb leans the way it is held. No logos: PS and Guide are plain.
/// </summary>
internal sealed class ControllerView : Viewbox
{
    private sealed record Part(PadControl Control, Button Hit, Canvas Content, Shape[] Faces, string FillKey,
        bool KeepFill, ScaleTransform Scale, Shape? TriggerFill);

    private static readonly ControlTemplate Bare = new(typeof(Button)) { VisualTree = new FrameworkElementFactory(typeof(ContentPresenter)) };

    private readonly Canvas _surface = new() { ClipToBounds = false };
    private readonly Canvas _drawing = new();
    private readonly Dictionary<PadControl, Part> _parts = [];
    private readonly TranslateTransform _leftThumb = new(), _rightThumb = new();
    private PadKind _kind = PadKind.DualShock4;
    private PadPicture _picture = PadPicture.Neutral;
    private PadControl? _outlined;
    private double _unit = 1, _tilt = 20;
    private Rect _box;

    public ControllerView()
    {
        Stretch = Stretch.Uniform;
        _surface.Children.Add(_drawing);
        Child = _surface;
        Build();
    }

    public event Action<PadControl>? PartClicked;

    public PadKind Kind
    {
        get => _kind;
        set
        {
            if (value == _kind)
                return;
            _kind = value;
            Build();
        }
    }

    /// <summary>The part whose key is being changed: drawn with a dashed outline.</summary>
    public PadControl? Outlined
    {
        get => _outlined;
        set
        {
            _outlined = value;
            Paint();
        }
    }

    public void Show(PadPicture picture)
    {
        _picture = picture;
        Paint();
    }

    internal IReadOnlyCollection<PadControl> Parts => _parts.Keys;

    internal bool IsLit(PadControl control) => _parts.ContainsKey(control) && _picture.Lit.Contains(control);

    internal string PartName(PadControl control) => AutomationProperties.GetName(_parts[control].Hit);

    internal void ClickPart(PadControl control) => _parts[control].Hit.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

    private string Name(PadControl control) => KeyNames.Of(control, _kind);

    private void Build()
    {
        _parts.Clear();
        _drawing.Children.Clear();
        if (_kind == PadKind.Xbox360)
            DrawXbox();
        else
            DrawDs4();
        _surface.Width = _box.Width;
        _surface.Height = _box.Height;
        _drawing.RenderTransform = new TranslateTransform(-_box.X, -_box.Y);
        Paint();
    }

    // ---- shapes ----

    private T Styled<T>(T shape, string fill, string stroke = "BorderStrongBrush") where T : Shape
    {
        shape.SetResourceReference(Shape.FillProperty, fill);
        shape.SetResourceReference(Shape.StrokeProperty, stroke);
        shape.StrokeThickness = 1.2 * _unit;
        return shape;
    }

    private Ellipse Circle(double x, double y, double r, string fill = "RaisedBrush")
    {
        var e = Styled(new Ellipse { Width = 2 * r, Height = 2 * r }, fill);
        Canvas.SetLeft(e, x - r);
        Canvas.SetTop(e, y - r);
        return e;
    }

    private Rectangle Box(double x, double y, double w, double h, double r, string fill = "RaisedBrush")
    {
        var b = Styled(new Rectangle { Width = w, Height = h, RadiusX = r, RadiusY = r }, fill);
        Canvas.SetLeft(b, x);
        Canvas.SetTop(b, y);
        return b;
    }

    private Path Outline(string data, string fill) => Styled(new Path { Data = Geometry.Parse(data) }, fill);

    private static TextBlock Text(double x, double y, double size, string text, string brush = "TextMutedBrush", bool bold = false)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = size, Width = 300, TextAlignment = TextAlignment.Center,
            FontWeight = bold ? FontWeights.Bold : FontWeights.SemiBold, IsHitTestVisible = false,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        Canvas.SetLeft(t, x - 150);
        Canvas.SetTop(t, y - size * 0.68);
        return t;
    }

    private static Path Stroke(string data, Brush brush, double thickness, double x = 0, double y = 0)
    {
        var p = new Path
        {
            Data = Geometry.Parse(data), Stroke = brush, StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(p, x);
        Canvas.SetTop(p, y);
        return p;
    }

    private void Decor(params UIElement[] elements)
    {
        foreach (var e in elements)
        {
            e.IsHitTestVisible = false;
            _drawing.Children.Add(e);
        }
    }

    /// <summary>
    /// One clickable part. The button spans the whole drawing so WPF's pressed-state check (pointer inside the
    /// button) holds; with no background, only its shapes take clicks.
    /// </summary>
    private void AddPart(PadControl control, string name, Point center, Shape[] faces, UIElement[]? decor = null,
        Transform? wrap = null, bool keepFill = false, Shape? triggerFill = null)
    {
        var content = new Canvas();
        foreach (var f in faces)
            content.Children.Add(f);
        if (triggerFill is not null)
            content.Children.Add(triggerFill);
        foreach (var d in decor ?? [])
        {
            d.IsHitTestVisible = false;
            content.Children.Add(d);
        }
        var scale = new ScaleTransform(1, 1, center.X, center.Y);
        content.RenderTransform = wrap is null ? scale : new TransformGroup { Children = { scale, wrap } };
        var hit = new Button
        {
            Template = Bare, Content = content, Cursor = Cursors.Hand, FocusVisualStyle = null,
            Width = _box.Right, Height = _box.Bottom,
        };
        AutomationProperties.SetName(hit, name);
        AutomationProperties.SetAutomationId(hit, $"Pad_{control}");
        hit.Click += (_, _) => PartClicked?.Invoke(control);
        hit.GotKeyboardFocus += (_, _) => Paint();
        hit.LostKeyboardFocus += (_, _) => Paint();
        _drawing.Children.Add(hit);
        string fillKey = faces[0].Tag as string ?? "RaisedBrush";
        _parts[control] = new Part(control, hit, content, faces, fillKey, keepFill, scale, triggerFill);
    }

    private Shape Face(Shape s, string fill = "RaisedBrush")
    {
        s.Tag = fill;
        return s;
    }

    private void Stick(PadControl click, PadControl well, string side, double x, double y, double wellR, double r, double ringR,
        TranslateTransform thumb)
    {
        AddPart(well, $"{side} stick, show keys", new Point(x, y), [Face(Circle(x, y, wellR, "InputBrush"), "InputBrush")]);
        var ring = Circle(x, y, ringR);
        ring.Fill = null;
        AddPart(click, $"{side} stick click, {Name(click)}, change key", new Point(x, y), [Face(Circle(x, y, r))], [ring], wrap: thumb);
    }

    private void DpadArms(Func<Shape> arm, Point armCenter, double cx, double cy)
    {
        foreach (var (control, angle, word) in new[] { (PadControl.DpadUp, 0, "up"), (PadControl.DpadRight, 90, "right"),
                     (PadControl.DpadDown, 180, "down"), (PadControl.DpadLeft, 270, "left") })
            AddPart(control, $"D-pad {word}, change key", armCenter, [Face(arm())], wrap: new RotateTransform(angle, cx, cy));
    }

    // ---- the two controllers ----

    private void DrawDs4()
    {
        _box = new Rect(180, 268, 952, 744);
        _unit = _box.Height / 190;
        _tilt = 20;
        foreach (var (control, x) in new[] { (PadControl.L2, 300.0), (PadControl.R2, 892.0) })
        {
            var fill = Box(x + 6, 287, 108, 34, 12, "PrimaryBrush");
            fill.Stroke = null;
            AddPart(control, $"{Name(control)}, change key", new Point(x + 60, 305), [Face(Box(x, 282, 120, 46, 16))],
                [Text(x + 60, 302, 20, Name(control))], triggerFill: fill);
        }
        foreach (var (control, x) in new[] { (PadControl.L1, 290.0), (PadControl.R1, 882.0) })
            AddPart(control, $"{Name(control)}, change key", new Point(x + 70, 346), [Face(Box(x, 326, 140, 40, 14))],
                [Text(x + 70, 342, 18, Name(control))]);
        Decor(Outline(ControllerShapes.Ds4Body, "InputBrush"));
        AddPart(PadControl.Touchpad, "Touchpad, change key", new Point(656, 448), [Face(Box(490, 368, 332, 160, 12))]);
        Decor(Text(456, 382, 13, "SHARE", "TextHintBrush"), Text(856, 382, 13, "OPTIONS", "TextHintBrush"));
        AddPart(PadControl.Share, "Share, change key", new Point(456, 413), [Face(Box(442, 392, 28, 42, 14))]);
        AddPart(PadControl.Options, "Options, change key", new Point(856, 413), [Face(Box(842, 392, 28, 42, 14))]);
        foreach (var (dx, y) in new[] { (-18, 556), (0, 556), (18, 556), (-27, 570), (-9, 570), (9, 570), (27, 570), (-18, 584), (0, 584), (18, 584) })
        {
            var dot = Circle(656 + dx, y, 4, "ChromeBrush");
            dot.Stroke = null;
            Decor(dot);
        }
        Decor(Circle(656, 642, 21, "InputBrush")); // PS button, plain
        Decor(Circle(337, 497, 106, "InputBrush"));
        DpadArms(() => Styled(new Polygon { Points = PointCollection.Parse("314,415 360,415 360,464 337,484 314,464"), StrokeLineJoin = PenLineJoin.Round }, "RaisedBrush"),
            new Point(337, 449), 337, 497);
        Decor(Circle(975, 497, 112, "InputBrush"));
        var symbols = new Dictionary<PadControl, (string Data, Color Color)>
        {
            [PadControl.Triangle] = ("M0 -14 L13 9 L-13 9 Z", Color.FromRgb(0x3E, 0xB4, 0x89)),
            [PadControl.Circle] = ("M13 0 A13 13 0 1 1 -13 0 A13 13 0 1 1 13 0", Color.FromRgb(0xE5, 0x53, 0x4B)),
            [PadControl.Cross] = ("M-11 -11 L11 11 M11 -11 L-11 11", Color.FromRgb(0x7B, 0x8F, 0xD9)),
            [PadControl.Square] = ("M-11 -11 L11 -11 L11 11 L-11 11 Z", Color.FromRgb(0xD6, 0x7F, 0xC7)),
        };
        foreach (var (control, x, y) in new[] { (PadControl.Triangle, 975.0, 439.0), (PadControl.Circle, 1033.0, 497.0),
                     (PadControl.Cross, 975.0, 555.0), (PadControl.Square, 917.0, 497.0) })
            AddPart(control, $"{Name(control)}, change key", new Point(x, y), [Face(Circle(x, y, 30))],
                [Stroke(symbols[control].Data, new SolidColorBrush(symbols[control].Color), 3.4, x, y)]);
        Stick(PadControl.L3, PadControl.LeftUp, "Left", 484, 606, 90, 52, 38, _leftThumb);
        Stick(PadControl.R3, PadControl.RightUp, "Right", 828, 606, 90, 52, 38, _rightThumb);
    }

    private void DrawXbox()
    {
        _box = new Rect(178, 40, 600, 360);
        _unit = _box.Height / 190;
        _tilt = 12;
        foreach (var (control, x) in new[] { (PadControl.L2, 305.0), (PadControl.R2, 607.0) })
        {
            var fill = Box(x + 4, 54, 36, 40, 7, "PrimaryBrush");
            fill.Stroke = null;
            AddPart(control, $"{Name(control)}, change key", new Point(x + 22, 79), [Face(Box(x, 50, 44, 58, 9))],
                [Text(x + 22, 72, 13, Name(control))], triggerFill: fill);
        }
        Decor(Box(405, 82, 146, 30, 8, "InputBrush"));
        Decor(Outline(ControllerShapes.Xbox360Body, "InputBrush"));
        foreach (var (control, x) in new[] { (PadControl.L1, 256.0), (PadControl.R1, 588.0) })
            AddPart(control, $"{Name(control)}, change key", new Point(x + 56, 126), [Face(Box(x, 110, 112, 32, 14))],
                [Text(x + 56, 126, 13, Name(control))]);
        var slot = Box(442, 124, 72, 10, 5, "ChromeBrush");
        slot.Stroke = null;
        Decor(slot);
        var chevron = (Brush)FindResource("TextMutedBrush");
        AddPart(PadControl.Share, "Back, change key", new Point(418, 228), [Face(Box(403, 219, 30, 18, 9))],
            [Stroke("M422 222 L412 228 L422 234", chevron, 2)]);
        AddPart(PadControl.Options, "Start, change key", new Point(538, 228), [Face(Box(523, 219, 30, 18, 9))],
            [Stroke("M534 222 L544 228 L534 234", chevron, 2)]);
        var guideRing = new Ellipse { Width = 54, Height = 54, Stroke = new SolidColorBrush(Color.FromRgb(0x7C, 0xC2, 0x42)), StrokeThickness = 2.5 * _unit };
        Canvas.SetLeft(guideRing, 451);
        Canvas.SetTop(guideRing, 201);
        Decor(guideRing, Circle(478, 228, 20, "InputBrush")); // Guide, plain
        Stick(PadControl.L3, PadControl.LeftUp, "Left", 313, 243, 56, 36, 26, _leftThumb);
        Decor(Circle(397, 318, 52, "InputBrush"), Circle(397, 318, 44, "RaisedBrush"));
        DpadArms(() => Box(384, 278, 26, 34, 5), new Point(397, 295), 397, 318);
        Stick(PadControl.R3, PadControl.RightUp, "Right", 559, 330, 56, 36, 26, _rightThumb);
        var caps = new Dictionary<PadControl, Color>
        {
            [PadControl.Triangle] = Color.FromRgb(0xE8, 0xB9, 0x0F), [PadControl.Circle] = Color.FromRgb(0xD9, 0x36, 0x3E),
            [PadControl.Cross] = Color.FromRgb(0x4F, 0xAE, 0x3C), [PadControl.Square] = Color.FromRgb(0x2D, 0x73, 0xD2),
        };
        foreach (var (control, x, y) in new[] { (PadControl.Triangle, 650.0, 183.0), (PadControl.Circle, 693.0, 226.0),
                     (PadControl.Cross, 650.0, 269.0), (PadControl.Square, 607.0, 226.0) })
        {
            var cap = Circle(x, y, 21);
            cap.Fill = new SolidColorBrush(caps[control]);
            var letter = Text(x, y + 1, 22, Name(control), bold: true);
            letter.Foreground = new SolidColorBrush(Color.FromArgb(0x9E, 0x0F, 0x0F, 0x23));
            AddPart(control, $"{Name(control)}, change key", new Point(x, y), [cap], [letter], keepFill: true);
        }
    }

    // ---- state ----

    private void Paint()
    {
        var glowColor = TryFindResource("PrimaryTextBrush") is SolidColorBrush b ? b.Color : Colors.MediumPurple;
        foreach (var part in _parts.Values)
        {
            bool lit = _picture.Lit.Contains(part.Control);
            bool marked = _outlined == part.Control || part.Hit.IsKeyboardFocused;
            foreach (var face in part.Faces)
            {
                if (!part.KeepFill)
                    face.SetResourceReference(Shape.FillProperty, lit ? "PrimaryBrush" : part.FillKey);
                face.SetResourceReference(Shape.StrokeProperty, lit ? "OnPrimaryBrush" : marked ? "PrimaryTextBrush" : "BorderStrongBrush");
                face.StrokeThickness = (lit || marked ? 2.5 : 1.2) * _unit;
                face.StrokeDashArray = _outlined == part.Control && !lit ? new DoubleCollection { 3, 2 } : null;
            }
            if (part.TriggerFill is { } fill)
                fill.Opacity = lit ? 1 : 0;
            part.Content.Effect = lit ? new DropShadowEffect { ShadowDepth = 0, BlurRadius = 14, Color = glowColor, Opacity = 0.9 } : null;
            double s = lit && !Motion.Reduced ? 0.92 : 1;
            part.Scale.ScaleX = part.Scale.ScaleY = s;
        }
        _leftThumb.X = _picture.LeftX * _tilt;
        _leftThumb.Y = _picture.LeftY * _tilt;
        _rightThumb.X = _picture.RightX * _tilt;
        _rightThumb.Y = _picture.RightY * _tilt;
    }
}
```

Note: `Face(...)` stores the fill key in `Tag` so `Paint` can restore it; `AddPart` must be called after `_box` is set (both `Draw` methods set it first). If `FindResource("TextMutedBrush")` throws in a test because no theme is installed, the tests call `ThemeManager.Install` first, as the other WPF tests do.

- [ ] **Step 5: Run tests**

Run: `dotnet build` then `dotnet test tests/CouchLink.App.Tests --filter FullyQualifiedName~ControllerViewTests`
Expected: PASS.

- [ ] **Step 6: Look at it**

Add a throwaway window in a scratch test, or run the app and open Controls after Task 12. Compare against the mockup at both kinds. It must look like the mockup (parts in the same places, D-pad arms around the centre, sticks side by side on the DS4 and offset on the Xbox). Fix coordinates only if something visibly differs from the mockup. Do not commit scratch code.

- [ ] **Step 7: Commit**

```bash
git add src/CouchLink.App/Ui/ControllerView.cs src/CouchLink.App/Ui/ControllerShapes.cs tests/CouchLink.App.Tests/ControllerViewTests.cs
git commit -m "feat(app): draw the session's controller accurately, part by part"
```

---

### Task 12: Live controller in the Controls editor

**Files:**
- Modify: `src/CouchLink.App/ControlsWindow.xaml` (picture card above Find)
- Modify: `src/CouchLink.App/ControlsWindow.xaml.cs`
- Modify: `src/CouchLink.App/Ui/KeyCap.cs`
- Test: `tests/CouchLink.App.Tests/ControlsEditorTests.cs`

**Interfaces:**
- Consumes: `ControllerView` (Task 11); `PadPicture.From` (Task 10); `InputMapper(ControlSettings)` (existing: `KeyDown(ushort)`, `KeyUp(ushort)`, `ReleaseAll()`, `Tick(double)`, `Dispose()`); `ControlsWindow.Kind` (Task 9).
- Produces: `KeyCap.VirtualKey` (`ushort?`) and `KeyCap.IsLit` (`bool`); in `ControlsWindow`: `Pad` (the `ControllerView`, from XAML), `PadCaption` (TextBlock), `internal void PreviewDown(ushort vk)`, `internal void PreviewUp(ushort vk)`, `internal void ReleasePreview()`, `internal void UpdatePreview()`, `internal bool IsKeyCapLit(PadControl control, ushort vk)`, `internal static bool PreviewIgnores(object? focused)`.

- [ ] **Step 1: Write the failing tests**

Append to `ControlsEditorTests`:

```csharp
    private const ushort W = 0x57, A = 0x41, S = 0x53, D = 0x44;

    [Fact]
    public void A_held_key_lights_its_part_and_key_cap()
    {
        WithEditor(editor =>
        {
            editor.PreviewDown(K);
            editor.UpdatePreview();
            Assert.True(editor.Pad.IsLit(PadControl.Cross));
            Assert.True(editor.IsKeyCapLit(PadControl.Cross, K));
            editor.PreviewUp(K);
            editor.UpdatePreview();
            Assert.False(editor.Pad.IsLit(PadControl.Cross));
            Assert.False(editor.IsKeyCapLit(PadControl.Cross, K));
        });
    }

    [Fact]
    public void Clicking_a_part_starts_changing_its_key_and_outlines_it()
    {
        WithEditor(editor =>
        {
            editor.Pad.ClickPart(PadControl.Circle);
            Assert.Equal(PadControl.Circle, editor.Listening);
            Assert.Equal(PadControl.Circle, editor.Pad.Outlined);
            Assert.Equal("Changing Circle: press a key · Esc cancels", editor.PadCaption.Text);
        });
    }

    [Fact]
    public void Clicking_a_stick_ring_does_not_listen()
    {
        WithEditor(editor =>
        {
            editor.Pad.ClickPart(PadControl.LeftUp);
            Assert.Null(editor.Listening);
        });
    }

    [Fact]
    public void The_preview_pauses_while_listening()
    {
        WithEditor(editor =>
        {
            editor.PreviewDown(K);
            editor.Listen(PadControl.Circle);
            editor.UpdatePreview();
            Assert.False(editor.Pad.IsLit(PadControl.Cross));
        });
    }

    [Fact]
    public void Releasing_the_preview_clears_everything()
    {
        WithEditor(editor =>
        {
            editor.PreviewDown(K);
            editor.ReleasePreview();          // what Deactivated does
            editor.UpdatePreview();
            Assert.False(editor.Pad.IsLit(PadControl.Cross));
            Assert.False(editor.IsKeyCapLit(PadControl.Cross, K));
        });
    }

    [Fact]
    public void A_reset_while_a_key_is_held_leaves_nothing_lit()
    {
        WithEditor(editor =>
        {
            editor.PreviewDown(K);
            AppServices.Controls.ResetToDefault();      // raises Changed; the editor refreshes on the dispatcher
            Wpf.PumpFor(50);
            editor.UpdatePreview();
            Assert.False(editor.Pad.IsLit(PadControl.Cross));
            Assert.False(editor.IsKeyCapLit(PadControl.Cross, K));
        });
    }

    [Fact]
    public void Typing_in_a_text_box_does_not_drive_the_preview()
    {
        Wpf.Run(() =>
        {
            Assert.True(ControlsWindow.PreviewIgnores(new System.Windows.Controls.TextBox()));
            Assert.False(ControlsWindow.PreviewIgnores(new System.Windows.Controls.Button()));
            Assert.False(ControlsWindow.PreviewIgnores(null));
        });
    }

    [Fact]
    public void The_picture_follows_the_kind_and_the_caption_invites_testing()
    {
        WithEditor(editor =>
        {
            Assert.Equal(PadKind.Xbox360, editor.Pad.Kind);
            Assert.Equal("Press your keys to test them · Click a button to change its key", editor.PadCaption.Text);
        }, PadKind.Xbox360);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CouchLink.App.Tests --filter FullyQualifiedName~ControlsEditorTests`
Expected: build FAILS.

- [ ] **Step 3: Key caps can light**

`KeyCap.cs`, add:

```csharp
    /// <summary>The key this cap shows, so the editor can light it while the key is held.</summary>
    public ushort? VirtualKey { get; init; }

    private bool _lit;

    /// <summary>Held right now: the cap fills with the primary colour.</summary>
    public bool IsLit
    {
        get => _lit;
        set
        {
            if (value == _lit || string.IsNullOrEmpty(_key))
                return;
            _lit = value;
            _cap.SetResourceReference(Border.BackgroundProperty, value ? "PrimaryBrush" : "RaisedBrush");
            _cap.SetResourceReference(Border.BorderBrushProperty, value ? "PrimaryTextBrush" : "BorderStrongBrush");
            _text.SetResourceReference(TextBlock.ForegroundProperty, value ? "OnPrimaryBrush" : "TextBrush");
        }
    }
```

- [ ] **Step 4: The picture card**

`ControlsWindow.xaml`: in the top `StackPanel`, between the profile `DockPanel` and the Find `DockPanel` (change the Find `DockPanel` margin stays `0,10,0,0`):

```xml
                <Border Style="{StaticResource Card}" Margin="0,10,0,0" Padding="8,8,8,6">
                    <StackPanel>
                        <ui:ControllerView x:Name="Pad" Height="190" AutomationProperties.Name="Controller"
                                           AutomationProperties.AutomationId="ControllerPicture"/>
                        <TextBlock x:Name="PadCaption" Style="{StaticResource CaptionText}" HorizontalAlignment="Center"
                                   Margin="0,4,0,0" AutomationProperties.AutomationId="ControllerCaption"
                                   AutomationProperties.LiveSetting="Polite"/>
                    </StackPanel>
                </Border>
```

- [ ] **Step 5: The live preview**

`ControlsWindow.xaml.cs` (add `using System.Windows.Controls.Primitives;` and `using CouchLink.Core.Pads;` if not present):

```csharp
    private const string PadIdleCaption = "Press your keys to test them · Click a button to change its key";
    private readonly InputMapper _preview = new(AppServices.Controls);
    private readonly HashSet<ushort> _held = [];
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _lastPreview = DateTime.UtcNow;
```

In the constructor, after `BuildRows();`:

```csharp
        Pad.Kind = Kind;
        Pad.PartClicked += OnPartClicked;
        PreviewKeyUp += OnPreviewKeyUp;
        Deactivated += (_, _) => ReleasePreview();
        _previewTimer.Tick += (_, _) => UpdatePreview();
        _previewTimer.Start();
```

and in `Closed`: `_previewTimer.Stop(); _preview.Dispose();`.

```csharp
    /// <summary>Keys typed into Find or a label box are text, not pad input.</summary>
    internal static bool PreviewIgnores(object? focused) => focused is TextBoxBase;

    internal void PreviewDown(ushort vk)
    {
        _held.Add(vk);
        _preview.KeyDown(vk);
    }

    internal void PreviewUp(ushort vk)
    {
        _held.Remove(vk);
        _preview.KeyUp(vk);
    }

    internal void ReleasePreview()
    {
        _held.Clear();
        _preview.ReleaseAll();
    }

    /// <summary>One frame: the pad state the host would get, drawn; the held keys' caps lit. Paused while listening.</summary>
    internal void UpdatePreview()
    {
        var now = DateTime.UtcNow;
        var state = _preview.Tick((now - _lastPreview).TotalSeconds);
        _lastPreview = now;
        Pad.Show(Listening is null ? PadPicture.From(state, Kind) : PadPicture.Neutral);
        foreach (var row in _rows.Values)
            if (row.Content is DockPanel panel)
                foreach (var cap in panel.Children.OfType<StackPanel>().SelectMany(s => s.Children.OfType<KeyCap>()))
                    cap.IsLit = Listening is null && cap.VirtualKey is { } vk && _held.Contains(vk);
    }

    internal bool IsKeyCapLit(PadControl control, ushort vk) =>
        _rows[control].Content is DockPanel panel
        && panel.Children.OfType<StackPanel>().SelectMany(s => s.Children.OfType<KeyCap>()).Any(c => c.VirtualKey == vk && c.IsLit);

    private void OnPartClicked(PadControl control)
    {
        if (control is PadControl.LeftUp or PadControl.RightUp)
        {
            _rows[control].BringIntoView(); // the stick's group; the thumb itself changes L3/R3
            return;
        }
        Listen(control);
        _rows[control].BringIntoView();
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        int vk = KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
        if (vk is > 0 and <= 0xFF)
            PreviewUp((ushort)vk);
    }
```

Change `OnPreviewKeyDown` so the not-listening path feeds the preview and never marks the key handled (Space, Enter and Tab keep working):

```csharp
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Listening is not { } control)
        {
            int down = KeyInterop.VirtualKeyFromKey(key);
            if (down is > 0 and <= 0xFF && !PreviewIgnores(Keyboard.FocusedElement))
                PreviewDown((ushort)down);
            return;
        }
        e.Handled = true; // Space, Enter and Tab must not press a button here
        // ... rest unchanged (Escape / Bind)
    }
```

`Listen` releases the preview and the outline/caption follow `Listening`:

```csharp
    internal void Listen(PadControl control)
    {
        ReleasePreview();
        Listening = control;
        Refresh();
    }
```

In `Refresh()`, after the rows loop, add:

```csharp
        Pad.Outlined = Listening;
        PadCaption.Text = Listening is { } listening
            ? $"Changing {KeyNames.Of(listening, Kind)}: press a key · Esc cancels"
            : PadIdleCaption;
```

In `RowContent`, create caps with their key: `keys.Children.Add(new KeyCap { Key = KeyNames.Of(key), VirtualKey = key });`.

`OnSettingsChanged` also releases held keys (the mapper drops them on a settings change; the caps must too):

```csharp
    private void OnSettingsChanged() => Dispatcher.InvokeAsync(() =>
    {
        ReleasePreview();
        Refresh();
    });
```

Update the class summary: "The session's controller on top reacts to the player's keys through the current bindings; clicking a part changes its key."

- [ ] **Step 6: Run tests**

Run: `dotnet build` then `dotnet test tests/CouchLink.App.Tests`
Expected: PASS (existing editor tests included).

- [ ] **Step 7: Commit**

```bash
git add src/CouchLink.App/ControlsWindow.xaml src/CouchLink.App/ControlsWindow.xaml.cs src/CouchLink.App/Ui/KeyCap.cs tests/CouchLink.App.Tests/ControlsEditorTests.cs
git commit -m "feat(app): live controller in the Controls editor with click-to-change"
```

---

### Task 13: Docs, screenshots and the hardware check

**Files:**
- Modify: `README.md` (host flow, game notes, controls section)
- Modify: `docs/cafe-setup-guide.md`
- Modify: `docs/gate-results.md`
- Add: `docs/images/host-setup.png`, `docs/images/host-lobby-xbox.png`, `docs/images/controls-live-ds4.png`, `docs/images/controls-live-xbox.png`; refresh `docs/images/start.png` only if it changed

- [ ] **Step 1: Hardware check (needs the 2K14/2K22 host and a client)**

On a host with ViGEmBus:
1. Host → setup screen → pick **Xbox 360** → Start hosting. In `joy.cpl` an "Controller (XBOX 360 For Windows)" appears when a client joins.
2. Client joins; in NBA 2K14, W/A/S/D move the player; every bound button does its action (A pass, X shoot, LB/RB, LT/RT, Start, Back).
3. Join four clients, then a fifth: the fifth sees "Host is full: Xbox 360 sessions take up to 4 players."
4. Stop hosting, set up again with **DualShock 4**, play NBA 2K22: works as before.
5. Open Controls on a client in each mode and outside a session: the picture matches the session's pad; press every default key; rebind one button by clicking it on the picture.

Write the results down (date, games, what passed). If step 2 fails, stop: the guess in #49 was wrong, so report back before changing anything.

- [ ] **Step 2: Docs**

- README: the host flow now has a **Host a game** step (pick the controller, then Start hosting). Game notes: "NBA 2K14: host with **Xbox 360**, up to 4 players. A real Xbox pad plugged into the host uses one of the 4 Xbox slots Windows allows." Controls section: the editor shows your controller; press keys to test, click a button to change its key.
- `docs/cafe-setup-guide.md`: the setup step and when to pick Xbox 360.
- `docs/gate-results.md`: the 2K14 result from Step 1.

- [ ] **Step 3: Screenshots**

Capture with UI Automation as earlier PRs did (dark theme, 540×660 main window, 460 px editor): the setup screen (Xbox 360 selected), the Xbox 360 lobby with one player, and the editor in both kinds with a key held. Save under `docs/images/`.

- [ ] **Step 4: Commit**

```bash
git add README.md docs/cafe-setup-guide.md docs/gate-results.md docs/images
git commit -m "docs: host controller choice, Xbox 360 notes and the live controller"
```

- [ ] **Step 5: Record the result in #30**

Post the 2K14 result as a comment on issue #30 (written with the doc-coauthoring and humanizer skills, per the repo owner's preference), only after the owner has seen the text.
