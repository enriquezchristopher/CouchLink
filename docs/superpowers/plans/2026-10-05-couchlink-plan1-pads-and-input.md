# CouchLink Plan 1: PadTest Gate + Input Path Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove that 2K accepts 9 virtual DS4 pads (the gate), then make a client PC's keyboard + mouse drive its own virtual DS4 on the host over the LAN (no video yet).

**Architecture:** A pure, unit-tested `CouchLink.Core` library holds the pad model, key/mouse -> DS4 mapping, the 24-byte input packet, sequence filtering, pad lifecycle (`PadManager`) and UDP send/receive. `CouchLink.Pads` wraps ViGEmBus behind `IVirtualPad`. `CouchLink.PadTest` is the gate console tool. `CouchLink.App` is the WPF app; in this plan it only has a temporary dev window (Host / Join by IP + slot) that later plans replace with the real lobby.

**Tech Stack:** C# / .NET 10, WPF, xUnit, Microsoft.Extensions.TimeProvider.Testing, Nefarius.ViGEm.Client (ViGEmBus), Win32 Raw Input via P/Invoke.

**Spec:** `docs/superpowers/specs/2026-10-05-couchlink-design.md`

**Later plans (not this one):** Plan 2 video, Plan 3 audio, Plan 4 lobby/discovery/session/approval/controls editor/capture rules (Win-key block, pointer clip, Ctrl+Alt+Q, F1/F2).

## Global Constraints

- Language/runtime: **C#, .NET 10**; Windows projects target `net10.0-windows`, `x64`.
- OS: **Windows 10** x64 on host and clients.
- Virtual pad type: **DualShock 4** via **ViGEmBus** (Nefarius.ViGEm.Client).
- Pad slots: **P2-P10** (slot bytes 2..10), **at most 9 virtual pads**; host player is P1 on the game's own keyboard controls.
- Input transport: **unreliable UDP, port 47803**; client sends the **full DS4 state on every change and at least every 8 ms**; host ignores packets older than the latest applied.
- DS4 axis convention: byte, **128 = center**, X: 0 = left / 255 = right, Y: **0 = up / 255 = down**; triggers 0 = released / 255 = fully pressed.
- Default layout (spec 6.1): WASD left stick; mouse -> right stick; arrows D-pad; K Cross; J + Left click Square; L Circle; I Triangle; Q/E L1/R1; Ctrl/Shift L2/R2; F / Middle click L3/R3; Enter/Backspace Options/Share; Tab Touchpad.
- Mouse stick returns to center within **~50 ms** after the mouse stops.
- ViGEmBus missing -> host refuses to start with exactly: **"ViGEmBus driver not installed"**.
- No internet, no accounts, no encryption.
- Key layout edits are not persisted (later plan); this plan uses the default layout only.

## Review Focus

1. **Client window loses focus while keys are held** (Alt+Tab, popup) -> expected: every button/stick on that player's pad releases immediately, nothing stays "stuck". Pinned in Task 5 (`ReleaseAll` test) and wired in Task 9.
2. **Client PC crashes, is unplugged, or its app is killed mid-play** -> expected: the host pad goes to neutral within 500 ms instead of holding the last buttons forever. Pinned in Task 7 (stale-release tests).
3. **Client app restarts** (sequence numbers start again from 1) -> expected: the host accepts the new run immediately, not ignore it as "old". Pinned in Task 6 (epoch test) and Task 7.
4. **Garbage or foreign UDP traffic on port 47803, or a slot outside 2..10** -> expected: ignored, no crash, no pad created. Pinned in Task 6 (parse tests), Task 7 (slot tests), Task 8 (receiver test).
5. **Extreme input**: huge mouse flicks, all four WASD keys at once, opposite keys -> expected: stick clamps to full deflection with no overflow/wrap, opposite keys cancel to center. Pinned in Task 4.

---

## File Structure

```
CouchLink/
  .gitignore
  Directory.Build.props                 shared compiler settings
  CouchLink.slnx
  docs/gate-results.md                  filled in during Task 3
  src/
    CouchLink.Core/                     net10.0, no Windows deps
      Input/PadButtons.cs               [Flags] DS4 buttons
      Input/PadState.cs                 full DS4 state record + Neutral
      Input/DpadMath.cs                 Dpad8 enum + buttons -> direction
      Input/StickMath.cs                double -> axis byte, keys -> stick
      Input/MouseStick.cs               mouse deltas -> decaying right stick
      Input/PadControl.cs               bindable controls enum
      Input/VirtualKeys.cs              Win32 VK codes used by layouts
      Input/KeyLayout.cs                control -> keys, default layout
      Input/InputMapper.cs              held keys + mouse -> PadState
      Pads/IVirtualPad.cs               IVirtualPad + IVirtualPadFactory
      Pads/PadManager.cs                slot -> pad lifecycle, stale release
      Protocol/InputPacket.cs           24-byte wire format
      Protocol/SequenceFilter.cs        newest-wins per slot with epoch
      Net/Ports.cs                      port constants
      Net/SendPolicy.cs                 "on change or every 8 ms"
      Net/InputSender.cs                client UDP sender
      Net/InputReceiver.cs              host UDP receive loop
    CouchLink.Pads/                     net10.0-windows, ViGEm adapter
      ViGEmPad.cs
      ViGEmPadFactory.cs
    CouchLink.PadTest/                  console gate tool
      Program.cs
    CouchLink.App/                      WPF app (dev window for now)
      App.xaml, App.xaml.cs
      MainWindow.xaml, MainWindow.xaml.cs
      Input/RawInputSource.cs           Win32 Raw Input -> key/mouse events
      Input/NativeMethods.cs            P/Invoke declarations
      Input/ClientInputLoop.cs          tick mapper, send via SendPolicy
      HostInputService.cs               receiver + PadManager + ViGEm
  tests/
    CouchLink.Core.Tests/
      Fakes.cs
      DpadMathTests.cs
      StickMathTests.cs
      MouseStickTests.cs
      InputMapperTests.cs
      InputPacketTests.cs
      SequenceFilterTests.cs
      PadManagerTests.cs
      SendPolicyTests.cs
      UdpInputTests.cs
```

---

### Task 1: Scaffold the solution

**Files:**
- Create: `.gitignore`, `Directory.Build.props`, `CouchLink.slnx`, `src/CouchLink.Core/CouchLink.Core.csproj`, `tests/CouchLink.Core.Tests/CouchLink.Core.Tests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: buildable solution; projects `CouchLink.Core` (namespace root `CouchLink.Core`) and `CouchLink.Core.Tests`.

- [ ] **Step 1: Install the .NET 10 SDK (if `dotnet --list-sdks` shows no 10.x)**

Run (PowerShell):
```powershell
winget install --id Microsoft.DotNet.SDK.10 -e --accept-source-agreements --accept-package-agreements
```
Then open a new shell and run: `dotnet --list-sdks`
Expected: a line starting with `10.`

- [ ] **Step 2: Create solution and projects**

Run from `C:\dev\CouchLink`:
```powershell
dotnet new sln -n CouchLink
dotnet new classlib -n CouchLink.Core -o src/CouchLink.Core -f net10.0
dotnet new xunit -n CouchLink.Core.Tests -o tests/CouchLink.Core.Tests -f net10.0
dotnet sln add src/CouchLink.Core tests/CouchLink.Core.Tests
dotnet add tests/CouchLink.Core.Tests reference src/CouchLink.Core
dotnet add tests/CouchLink.Core.Tests package Microsoft.Extensions.TimeProvider.Testing
Remove-Item src/CouchLink.Core/Class1.cs, tests/CouchLink.Core.Tests/UnitTest1.cs
```

- [ ] **Step 3: Add shared build settings**

Create `Directory.Build.props`:
```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

Create `.gitignore`:
```
bin/
obj/
.vs/
*.user
TestResults/
```

- [ ] **Step 4: Build and test**

Run: `dotnet build` then `dotnet test`
Expected: build succeeds; test run reports 0 tests (no failures).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "chore: scaffold CouchLink solution with Core and tests"
```

---

### Task 2: Pad model (buttons, state, D-pad, pad interfaces)

**Files:**
- Create: `src/CouchLink.Core/Input/PadButtons.cs`, `src/CouchLink.Core/Input/PadState.cs`, `src/CouchLink.Core/Input/DpadMath.cs`, `src/CouchLink.Core/Pads/IVirtualPad.cs`
- Test: `tests/CouchLink.Core.Tests/DpadMathTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `[Flags] enum PadButtons : uint` with `None, Cross, Circle, Square, Triangle, L1, R1, L2, R2, L3, R3, Options, Share, Touchpad, PS, DpadUp, DpadDown, DpadLeft, DpadRight` and `const PadButtons Known`.
  - `readonly record struct PadState(PadButtons Buttons, byte LX, byte LY, byte RX, byte RY, byte L2, byte R2)` with `const byte Center = 128` and `static PadState Neutral`.
  - `enum Dpad8 { None, North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest }`, `static Dpad8 DpadMath.FromButtons(PadButtons)`.
  - `interface IVirtualPad : IDisposable { void Apply(PadState state); }`, `interface IVirtualPadFactory { IVirtualPad Create(); }`.

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/DpadMathTests.cs`:
```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class DpadMathTests
{
    [Theory]
    [InlineData(PadButtons.None, Dpad8.None)]
    [InlineData(PadButtons.DpadUp, Dpad8.North)]
    [InlineData(PadButtons.DpadUp | PadButtons.DpadRight, Dpad8.NorthEast)]
    [InlineData(PadButtons.DpadRight, Dpad8.East)]
    [InlineData(PadButtons.DpadDown | PadButtons.DpadRight, Dpad8.SouthEast)]
    [InlineData(PadButtons.DpadDown, Dpad8.South)]
    [InlineData(PadButtons.DpadDown | PadButtons.DpadLeft, Dpad8.SouthWest)]
    [InlineData(PadButtons.DpadLeft, Dpad8.West)]
    [InlineData(PadButtons.DpadUp | PadButtons.DpadLeft, Dpad8.NorthWest)]
    [InlineData(PadButtons.DpadUp | PadButtons.DpadDown, Dpad8.None)]
    [InlineData(PadButtons.DpadLeft | PadButtons.DpadRight | PadButtons.DpadUp, Dpad8.North)]
    [InlineData(PadButtons.Cross, Dpad8.None)]
    public void FromButtons_maps_dpad_flags_to_direction(PadButtons buttons, Dpad8 expected)
    {
        Assert.Equal(expected, DpadMath.FromButtons(buttons));
    }

    [Fact]
    public void Neutral_is_centered_with_nothing_pressed()
    {
        var n = PadState.Neutral;
        Assert.Equal(PadButtons.None, n.Buttons);
        Assert.Equal((128, 128, 128, 128, 0, 0), (n.LX, n.LY, n.RX, n.RY, n.L2, n.R2));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~DpadMathTests`
Expected: build FAILS — `PadButtons`, `Dpad8`, `DpadMath`, `PadState` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Input/PadButtons.cs`:
```csharp
namespace CouchLink.Core.Input;

[Flags]
public enum PadButtons : uint
{
    None = 0,
    Cross = 1u << 0,
    Circle = 1u << 1,
    Square = 1u << 2,
    Triangle = 1u << 3,
    L1 = 1u << 4,
    R1 = 1u << 5,
    L2 = 1u << 6,
    R2 = 1u << 7,
    L3 = 1u << 8,
    R3 = 1u << 9,
    Options = 1u << 10,
    Share = 1u << 11,
    Touchpad = 1u << 12,
    PS = 1u << 13,
    DpadUp = 1u << 14,
    DpadDown = 1u << 15,
    DpadLeft = 1u << 16,
    DpadRight = 1u << 17,

    /// <summary>Every defined button bit; anything else is unknown.</summary>
    Known = (1u << 18) - 1,
}
```

`src/CouchLink.Core/Input/PadState.cs`:
```csharp
namespace CouchLink.Core.Input;

/// <summary>
/// Full DualShock 4 state. Axes: 128 = center; X 0 = left, 255 = right;
/// Y 0 = up, 255 = down. Triggers: 0 = released, 255 = fully pressed.
/// Never use default(PadState): its axes are 0 (stick pushed up-left).
/// </summary>
public readonly record struct PadState(
    PadButtons Buttons, byte LX, byte LY, byte RX, byte RY, byte L2, byte R2)
{
    public const byte Center = 128;

    public static PadState Neutral { get; } =
        new(PadButtons.None, Center, Center, Center, Center, 0, 0);
}
```

`src/CouchLink.Core/Input/DpadMath.cs`:
```csharp
namespace CouchLink.Core.Input;

public enum Dpad8 { None, North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest }

public static class DpadMath
{
    /// <summary>Combines D-pad flags into one of 8 directions; opposite flags cancel.</summary>
    public static Dpad8 FromButtons(PadButtons buttons)
    {
        int x = (buttons.HasFlag(PadButtons.DpadRight) ? 1 : 0) - (buttons.HasFlag(PadButtons.DpadLeft) ? 1 : 0);
        int y = (buttons.HasFlag(PadButtons.DpadDown) ? 1 : 0) - (buttons.HasFlag(PadButtons.DpadUp) ? 1 : 0);
        return (x, y) switch
        {
            (0, -1) => Dpad8.North,
            (1, -1) => Dpad8.NorthEast,
            (1, 0) => Dpad8.East,
            (1, 1) => Dpad8.SouthEast,
            (0, 1) => Dpad8.South,
            (-1, 1) => Dpad8.SouthWest,
            (-1, 0) => Dpad8.West,
            (-1, -1) => Dpad8.NorthWest,
            _ => Dpad8.None,
        };
    }
}
```

`src/CouchLink.Core/Pads/IVirtualPad.cs`:
```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Pads;

/// <summary>One virtual controller plugged into the host. Dispose unplugs it.</summary>
public interface IVirtualPad : IDisposable
{
    void Apply(PadState state);
}

public interface IVirtualPadFactory
{
    /// <summary>Plugs in a new virtual pad, already in the Neutral state.</summary>
    IVirtualPad Create();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~DpadMathTests`
Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(core): DS4 pad model, D-pad math and virtual pad interfaces"
```

---

### Task 3: ViGEm adapter, PadTest tool, and the GATE

**Files:**
- Create: `src/CouchLink.Pads/CouchLink.Pads.csproj`, `src/CouchLink.Pads/ViGEmPad.cs`, `src/CouchLink.Pads/ViGEmPadFactory.cs`, `src/CouchLink.PadTest/CouchLink.PadTest.csproj`, `src/CouchLink.PadTest/Program.cs`, `docs/gate-results.md`

**Interfaces:**
- Consumes: `PadState`, `PadButtons`, `DpadMath`, `Dpad8`, `IVirtualPad`, `IVirtualPadFactory` (Task 2).
- Produces: `sealed class ViGEmPadFactory : IVirtualPadFactory, IDisposable` with `static bool TryCreate(out ViGEmPadFactory? factory, out string? error)`; error text is exactly `"ViGEmBus driver not installed"`.

No unit tests here: this code only talks to the ViGEmBus driver. It is verified by the manual gate in Steps 5-7.

- [ ] **Step 1: Create the projects**

```powershell
dotnet new classlib -n CouchLink.Pads -o src/CouchLink.Pads -f net10.0
dotnet new console -n CouchLink.PadTest -o src/CouchLink.PadTest -f net10.0
dotnet sln add src/CouchLink.Pads src/CouchLink.PadTest
dotnet add src/CouchLink.Pads reference src/CouchLink.Core
dotnet add src/CouchLink.Pads package Nefarius.ViGEm.Client
dotnet add src/CouchLink.PadTest reference src/CouchLink.Pads src/CouchLink.Core
Remove-Item src/CouchLink.Pads/Class1.cs
```

In BOTH `src/CouchLink.Pads/CouchLink.Pads.csproj` and `src/CouchLink.PadTest/CouchLink.PadTest.csproj`, change `<TargetFramework>net10.0</TargetFramework>` to:
```xml
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
```

- [ ] **Step 2: Write the ViGEm pad**

`src/CouchLink.Pads/ViGEmPad.cs`:
```csharp
using CouchLink.Core.Input;
using CouchLink.Core.Pads;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace CouchLink.Pads;

/// <summary>A virtual DualShock 4 on ViGEmBus. Each Apply sends one full report.</summary>
public sealed class ViGEmPad : IVirtualPad
{
    private static readonly (PadButtons Flag, DualShock4Button Button)[] Buttons =
    [
        (PadButtons.Cross, DualShock4Button.Cross),
        (PadButtons.Circle, DualShock4Button.Circle),
        (PadButtons.Square, DualShock4Button.Square),
        (PadButtons.Triangle, DualShock4Button.Triangle),
        (PadButtons.L1, DualShock4Button.ShoulderLeft),
        (PadButtons.R1, DualShock4Button.ShoulderRight),
        (PadButtons.L2, DualShock4Button.TriggerLeft),
        (PadButtons.R2, DualShock4Button.TriggerRight),
        (PadButtons.L3, DualShock4Button.ThumbLeft),
        (PadButtons.R3, DualShock4Button.ThumbRight),
        (PadButtons.Options, DualShock4Button.Options),
        (PadButtons.Share, DualShock4Button.Share),
    ];

    private readonly IDualShock4Controller _pad;

    internal ViGEmPad(ViGEmClient client)
    {
        _pad = client.CreateDualShock4Controller();
        _pad.AutoSubmitReport = false;
        _pad.Connect();
        Apply(PadState.Neutral);
    }

    public void Apply(PadState s)
    {
        foreach (var (flag, button) in Buttons)
            _pad.SetButtonState(button, s.Buttons.HasFlag(flag));
        _pad.SetButtonState(DualShock4SpecialButton.Touchpad, s.Buttons.HasFlag(PadButtons.Touchpad));
        _pad.SetButtonState(DualShock4SpecialButton.Ps, s.Buttons.HasFlag(PadButtons.PS));
        _pad.SetDPadDirection(ToViGEm(DpadMath.FromButtons(s.Buttons)));
        _pad.SetAxisValue(DualShock4Axis.LeftThumbX, s.LX);
        _pad.SetAxisValue(DualShock4Axis.LeftThumbY, s.LY);
        _pad.SetAxisValue(DualShock4Axis.RightThumbX, s.RX);
        _pad.SetAxisValue(DualShock4Axis.RightThumbY, s.RY);
        _pad.SetSliderValue(DualShock4Slider.LeftTrigger, s.L2);
        _pad.SetSliderValue(DualShock4Slider.RightTrigger, s.R2);
        _pad.SubmitReport();
    }

    public void Dispose() => _pad.Disconnect();

    private static DualShock4DPadDirection ToViGEm(Dpad8 d) => d switch
    {
        Dpad8.North => DualShock4DPadDirection.North,
        Dpad8.NorthEast => DualShock4DPadDirection.Northeast,
        Dpad8.East => DualShock4DPadDirection.East,
        Dpad8.SouthEast => DualShock4DPadDirection.Southeast,
        Dpad8.South => DualShock4DPadDirection.South,
        Dpad8.SouthWest => DualShock4DPadDirection.Southwest,
        Dpad8.West => DualShock4DPadDirection.West,
        Dpad8.NorthWest => DualShock4DPadDirection.Northwest,
        _ => DualShock4DPadDirection.None,
    };
}
```

`src/CouchLink.Pads/ViGEmPadFactory.cs`:
```csharp
using CouchLink.Core.Pads;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Exceptions;

namespace CouchLink.Pads;

public sealed class ViGEmPadFactory : IVirtualPadFactory, IDisposable
{
    public const string DriverMissingMessage = "ViGEmBus driver not installed";

    private readonly ViGEmClient _client;

    private ViGEmPadFactory(ViGEmClient client) => _client = client;

    public static bool TryCreate(out ViGEmPadFactory? factory, out string? error)
    {
        try
        {
            factory = new ViGEmPadFactory(new ViGEmClient());
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

    public IVirtualPad Create() => new ViGEmPad(_client);

    public void Dispose() => _client.Dispose();
}
```

- [ ] **Step 3: Write the PadTest console tool**

`src/CouchLink.PadTest/Program.cs`:
```csharp
using CouchLink.Core.Input;
using CouchLink.Core.Pads;
using CouchLink.Pads;

int count = args.Length > 0 && int.TryParse(args[0], out var n) ? Math.Clamp(n, 1, 9) : 9;

if (!ViGEmPadFactory.TryCreate(out var factory, out var error))
{
    Console.Error.WriteLine(error);
    return 1;
}

using (factory)
{
    var pads = new List<IVirtualPad>();
    for (int i = 0; i < count; i++)
        pads.Add(factory!.Create());

    Console.WriteLine($"Created {count} virtual DS4 pads (P2..P{count + 1}).");
    Console.WriteLine("Keys: 2-9 = pulse P2..P9, 0 = pulse P10, A = pulse all in order, Q = quit");
    Console.WriteLine("A pulse holds Cross + left stick RIGHT for 1 second on that pad only.");

    while (true)
    {
        char key = char.ToUpperInvariant(Console.ReadKey(intercept: true).KeyChar);
        if (key == 'Q')
            break;
        if (key == 'A')
        {
            for (int i = 0; i < pads.Count; i++)
                Pulse(pads, i);
            continue;
        }
        int slot = key == '0' ? 10 : key - '0';
        int index = slot - 2;
        if (index >= 0 && index < pads.Count)
            Pulse(pads, index);
    }

    foreach (var pad in pads)
        pad.Dispose();
}
return 0;

static void Pulse(List<IVirtualPad> pads, int index)
{
    Console.WriteLine($"P{index + 2}: Cross + stick right for 1 s");
    pads[index].Apply(PadState.Neutral with { Buttons = PadButtons.Cross, LX = 255 });
    Thread.Sleep(1000);
    pads[index].Apply(PadState.Neutral);
}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: build succeeds. If a ViGEm member name fails to compile (e.g. `Northeast` vs `NorthEast`, `Ps` vs `PS`), open the package's types with Visual Studio's "Go To Definition" or `dotnet build` error text and use the exact member name the package defines; the mapping itself must stay the same.

- [ ] **Step 5: GATE part 1 - Windows sees 9 pads**

Run: `dotnet run --project src/CouchLink.PadTest -- 9`
Expected console: `Created 9 virtual DS4 pads (P2..P10).`
Then press Win+R, run `joy.cpl`.
Expected: **9** "Wireless Controller" entries. Select one, Properties, and press its number key in the PadTest window: only that pad's X axis and button 2 react.

- [ ] **Step 6: GATE part 2 - 2K14 and 2K22 see 9 separate players**

With PadTest still running, start NBA 2K22 (borderless windowed), start a Play Now game, and go to the controller-select screen.
1. Press `A` in PadTest: each controller icon moves to the right **one at a time** (9 icons, plus the keyboard/P1 icon).
2. Put pads on both teams, start the game, press `2`, `3`, ... `0`: **only one player** moves for each key.
3. Use the host keyboard at the same time: the keyboard controls its own player (10 total).
Repeat steps 1-3 in NBA 2K14.

- [ ] **Step 7: Record the result**

Create `docs/gate-results.md`:
```markdown
# PadTest Gate Results

Date: <YYYY-MM-DD>
Host GPU / Windows build: <e.g. RX 550 / Windows 10 22H2>

| Check | 2K14 | 2K22 |
|---|---|---|
| joy.cpl shows 9 pads | <pass/fail> | (same) |
| Controller select shows 9 pad icons + keyboard | <pass/fail> | <pass/fail> |
| Each pad key moves exactly one player | <pass/fail> | <pass/fail> |
| Host keyboard controls its own player at the same time | <pass/fail> | <pass/fail> |

Notes: <anything odd, e.g. pads only recognized after restart of the game>
```
Fill in every cell with the observed result.

**STOP CONDITION:** if any 2K cell is `fail`, stop here. Do not run Tasks 4-9. Report the failing check to the human partner; the design (spec section 6.6 pad type, or the 9-pad target) must be revisited first.

- [ ] **Step 8: Commit**

```powershell
git add .
git commit -m "feat(pads): ViGEm DS4 adapter, PadTest gate tool and gate results"
```

---

### Task 4: Stick math and mouse stick

**Files:**
- Create: `src/CouchLink.Core/Input/StickMath.cs`, `src/CouchLink.Core/Input/MouseStick.cs`
- Test: `tests/CouchLink.Core.Tests/StickMathTests.cs`, `tests/CouchLink.Core.Tests/MouseStickTests.cs`

**Interfaces:**
- Consumes: `PadState.Center` (Task 2).
- Produces:
  - `static byte StickMath.ToAxis(double value)` (-1..1 -> 1..255, 0 -> 128, clamped).
  - `static (byte X, byte Y) StickMath.FromDirections(bool up, bool down, bool left, bool right)`.
  - `sealed class MouseStick` with `double Sensitivity { get; set; }` (default `0.02`), `void AddDelta(int dx, int dy)`, `(byte X, byte Y) Update(double dtSeconds)`, `void Reset()`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/StickMathTests.cs`:
```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class StickMathTests
{
    [Theory]
    [InlineData(0.0, 128)]
    [InlineData(1.0, 255)]
    [InlineData(-1.0, 1)]
    [InlineData(0.5, 192)]
    [InlineData(5.0, 255)]
    [InlineData(-5.0, 1)]
    public void ToAxis_maps_and_clamps(double value, int expected)
    {
        Assert.Equal((byte)expected, StickMath.ToAxis(value));
    }

    [Theory]
    [InlineData(false, false, false, false, 128, 128)]
    [InlineData(true, false, false, false, 128, 1)]     // W = up = Y 0-ish
    [InlineData(false, true, false, false, 128, 255)]   // S = down
    [InlineData(false, false, true, false, 1, 128)]     // A = left
    [InlineData(false, false, false, true, 255, 128)]   // D = right
    [InlineData(true, false, false, true, 218, 38)]     // up + right, normalized
    [InlineData(true, true, false, false, 128, 128)]    // opposite cancel
    [InlineData(true, true, true, true, 128, 128)]      // all four held
    [InlineData(true, true, false, true, 255, 128)]     // up+down cancel, right stays full
    public void FromDirections_combines_keys(bool up, bool down, bool left, bool right, int x, int y)
    {
        Assert.Equal(((byte)x, (byte)y), StickMath.FromDirections(up, down, left, right));
    }
}
```

`tests/CouchLink.Core.Tests/MouseStickTests.cs`:
```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class MouseStickTests
{
    [Fact]
    public void Idle_stick_is_centered()
    {
        Assert.Equal(((byte)128, (byte)128), new MouseStick().Update(0.001));
    }

    [Fact]
    public void Delta_deflects_by_sensitivity()
    {
        var m = new MouseStick();
        m.AddDelta(25, 0); // 25 * 0.02 = 0.5
        Assert.Equal(((byte)192, (byte)128), m.Update(0.001));
    }

    [Fact]
    public void Moving_mouse_down_pushes_stick_down()
    {
        var m = new MouseStick();
        m.AddDelta(0, 25);
        Assert.Equal(((byte)128, (byte)192), m.Update(0.001));
    }

    [Fact]
    public void Returns_to_center_within_50ms_after_mouse_stops()
    {
        var m = new MouseStick();
        m.AddDelta(50, -50);
        (byte X, byte Y) last = default;
        for (int i = 0; i < 50; i++)
            last = m.Update(0.001);
        Assert.Equal(((byte)128, (byte)128), last);
    }

    [Fact]
    public void Huge_flick_clamps_to_full_deflection_without_overflow()
    {
        var m = new MouseStick();
        m.AddDelta(int.MaxValue, 0);
        m.AddDelta(int.MaxValue, 0);
        Assert.Equal(((byte)255, (byte)128), m.Update(0.001));
    }

    [Fact]
    public void Diagonal_flick_clamps_magnitude_to_one()
    {
        var m = new MouseStick();
        m.AddDelta(1000, 1000);
        Assert.Equal(((byte)218, (byte)218), m.Update(0.001));
    }

    [Fact]
    public void Reset_centers_immediately()
    {
        var m = new MouseStick();
        m.AddDelta(40, 40);
        m.Reset();
        Assert.Equal(((byte)128, (byte)128), m.Update(0.001));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~StickMathTests|FullyQualifiedName~MouseStickTests"`
Expected: build FAILS — `StickMath`, `MouseStick` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Input/StickMath.cs`:
```csharp
namespace CouchLink.Core.Input;

public static class StickMath
{
    /// <summary>Maps -1..1 to an axis byte (1..255, 0 -> 128). Values outside are clamped.</summary>
    public static byte ToAxis(double value)
    {
        double clamped = Math.Clamp(value, -1.0, 1.0);
        return (byte)Math.Round(PadState.Center + clamped * 127.0, MidpointRounding.AwayFromZero);
    }

    /// <summary>Digital directions to a stick. Diagonals are scaled to unit length; opposites cancel.</summary>
    public static (byte X, byte Y) FromDirections(bool up, bool down, bool left, bool right)
    {
        double x = (right ? 1 : 0) - (left ? 1 : 0);
        double y = (down ? 1 : 0) - (up ? 1 : 0);
        if (x != 0 && y != 0)
        {
            x *= Math.Sqrt(0.5);
            y *= Math.Sqrt(0.5);
        }
        return (ToAxis(x), ToAxis(y));
    }
}
```

`src/CouchLink.Core/Input/MouseStick.cs`:
```csharp
namespace CouchLink.Core.Input;

/// <summary>
/// Turns mouse movement into a right-stick deflection that springs back to
/// center shortly after the mouse stops (supports 2K shot-stick flicks).
/// </summary>
public sealed class MouseStick
{
    public const double DefaultSensitivity = 0.02;

    // exp(-t / 10 ms) drops a full deflection below SnapToZero in ~40 ms.
    private const double DecayTimeConstantSeconds = 0.010;
    private const double SnapToZero = 0.02;

    private double _x;
    private double _y;

    /// <summary>Stick deflection per mouse count (1.0 = full).</summary>
    public double Sensitivity { get; set; } = DefaultSensitivity;

    public void AddDelta(int dx, int dy)
    {
        _x += dx * Sensitivity;
        _y += dy * Sensitivity;
        double magnitude = Math.Sqrt(_x * _x + _y * _y);
        if (magnitude > 1.0)
        {
            _x /= magnitude;
            _y /= magnitude;
        }
    }

    /// <summary>Returns the current stick, then decays it by <paramref name="dtSeconds"/>.</summary>
    public (byte X, byte Y) Update(double dtSeconds)
    {
        var output = (StickMath.ToAxis(_x), StickMath.ToAxis(_y));
        double k = Math.Exp(-Math.Max(dtSeconds, 0) / DecayTimeConstantSeconds);
        _x *= k;
        _y *= k;
        if (Math.Abs(_x) < SnapToZero) _x = 0;
        if (Math.Abs(_y) < SnapToZero) _y = 0;
        return output;
    }

    public void Reset()
    {
        _x = 0;
        _y = 0;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~StickMathTests|FullyQualifiedName~MouseStickTests"`
Expected: PASS (15 + 7 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(core): keyboard stick math and decaying mouse right stick"
```

---

### Task 5: Key layout and input mapper

**Files:**
- Create: `src/CouchLink.Core/Input/PadControl.cs`, `src/CouchLink.Core/Input/VirtualKeys.cs`, `src/CouchLink.Core/Input/KeyLayout.cs`, `src/CouchLink.Core/Input/InputMapper.cs`
- Test: `tests/CouchLink.Core.Tests/InputMapperTests.cs`

**Interfaces:**
- Consumes: `PadState`, `PadButtons` (Task 2); `StickMath`, `MouseStick` (Task 4).
- Produces:
  - `enum PadControl { LeftUp, LeftDown, LeftLeft, LeftRight, DpadUp, DpadDown, DpadLeft, DpadRight, Cross, Circle, Square, Triangle, L1, R1, L2, R2, L3, R3, Options, Share, Touchpad }`.
  - `static class VirtualKeys` with `const ushort LButton = 0x01, RButton = 0x02, MButton = 0x04, Back = 0x08, Tab = 0x09, Return = 0x0D, Shift = 0x10, Control = 0x11, Left = 0x25, Up = 0x26, Right = 0x27, Down = 0x28` and `static ushort Letter(char c)`.
  - `sealed class KeyLayout` with `static KeyLayout CreateDefault()` and `IReadOnlyList<ushort> KeysFor(PadControl control)`.
  - `sealed class InputMapper(KeyLayout layout, MouseStick mouse)` with thread-safe `void KeyDown(ushort vk)`, `void KeyUp(ushort vk)`, `void MouseMove(int dx, int dy)`, `void ReleaseAll()`, `PadState Tick(double dtSeconds)`. Mouse buttons are passed as `KeyDown/KeyUp(VirtualKeys.LButton | RButton | MButton)`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/InputMapperTests.cs`:
```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class InputMapperTests
{
    private static InputMapper NewMapper() => new(KeyLayout.CreateDefault(), new MouseStick());

    private static readonly ushort W = VirtualKeys.Letter('W');
    private static readonly ushort D = VirtualKeys.Letter('D');
    private static readonly ushort S = VirtualKeys.Letter('S');
    private static readonly ushort J = VirtualKeys.Letter('J');
    private static readonly ushort K = VirtualKeys.Letter('K');

    [Fact]
    public void Nothing_held_is_neutral()
    {
        Assert.Equal(PadState.Neutral, NewMapper().Tick(0.001));
    }

    [Fact]
    public void W_pushes_left_stick_up()
    {
        var m = NewMapper();
        m.KeyDown(W);
        var s = m.Tick(0.001);
        Assert.Equal(((byte)128, (byte)1), (s.LX, s.LY));
    }

    [Fact]
    public void W_and_D_give_normalized_diagonal()
    {
        var m = NewMapper();
        m.KeyDown(W);
        m.KeyDown(D);
        var s = m.Tick(0.001);
        Assert.Equal(((byte)218, (byte)38), (s.LX, s.LY));
    }

    [Fact]
    public void W_and_S_cancel()
    {
        var m = NewMapper();
        m.KeyDown(W);
        m.KeyDown(S);
        Assert.Equal(PadState.Neutral, m.Tick(0.001));
    }

    [Theory]
    [InlineData((ushort)'K', PadButtons.Cross)]
    [InlineData((ushort)'J', PadButtons.Square)]
    [InlineData(VirtualKeys.LButton, PadButtons.Square)]
    [InlineData((ushort)'L', PadButtons.Circle)]
    [InlineData((ushort)'I', PadButtons.Triangle)]
    [InlineData((ushort)'Q', PadButtons.L1)]
    [InlineData((ushort)'E', PadButtons.R1)]
    [InlineData((ushort)'F', PadButtons.L3)]
    [InlineData(VirtualKeys.MButton, PadButtons.R3)]
    [InlineData(VirtualKeys.Return, PadButtons.Options)]
    [InlineData(VirtualKeys.Back, PadButtons.Share)]
    [InlineData(VirtualKeys.Tab, PadButtons.Touchpad)]
    [InlineData(VirtualKeys.Up, PadButtons.DpadUp)]
    [InlineData(VirtualKeys.Down, PadButtons.DpadDown)]
    [InlineData(VirtualKeys.Left, PadButtons.DpadLeft)]
    [InlineData(VirtualKeys.Right, PadButtons.DpadRight)]
    public void Default_layout_maps_key_to_button(ushort vk, PadButtons expected)
    {
        var m = NewMapper();
        m.KeyDown(vk);
        Assert.Equal(expected, m.Tick(0.001).Buttons);
    }

    [Fact]
    public void Ctrl_and_Shift_are_full_L2_and_R2()
    {
        var m = NewMapper();
        m.KeyDown(VirtualKeys.Control);
        m.KeyDown(VirtualKeys.Shift);
        var s = m.Tick(0.001);
        Assert.Equal(PadButtons.L2 | PadButtons.R2, s.Buttons);
        Assert.Equal(((byte)255, (byte)255), (s.L2, s.R2));
    }

    [Fact]
    public void Control_stays_down_while_any_bound_key_is_held()
    {
        var m = NewMapper();
        m.KeyDown(J);
        m.KeyDown(VirtualKeys.LButton);
        m.KeyUp(J);
        Assert.Equal(PadButtons.Square, m.Tick(0.001).Buttons);
        m.KeyUp(VirtualKeys.LButton);
        Assert.Equal(PadButtons.None, m.Tick(0.001).Buttons);
    }

    [Fact]
    public void Mouse_moves_right_stick()
    {
        var m = NewMapper();
        m.MouseMove(25, 0);
        var s = m.Tick(0.001);
        Assert.Equal(((byte)192, (byte)128), (s.RX, s.RY));
    }

    [Fact]
    public void Unbound_key_does_nothing()
    {
        var m = NewMapper();
        m.KeyDown(VirtualKeys.Letter('Z'));
        Assert.Equal(PadState.Neutral, m.Tick(0.001));
    }

    [Fact]
    public void ReleaseAll_clears_held_keys_and_mouse_stick()
    {
        var m = NewMapper();
        m.KeyDown(W);
        m.KeyDown(K);
        m.KeyDown(VirtualKeys.Shift);
        m.MouseMove(40, 40);
        m.ReleaseAll();
        Assert.Equal(PadState.Neutral, m.Tick(0.001));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~InputMapperTests`
Expected: build FAILS — `InputMapper`, `KeyLayout`, `VirtualKeys` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Input/PadControl.cs`:
```csharp
namespace CouchLink.Core.Input;

/// <summary>Every DS4 control a key can be bound to. The right stick comes from the mouse.</summary>
public enum PadControl
{
    LeftUp, LeftDown, LeftLeft, LeftRight,
    DpadUp, DpadDown, DpadLeft, DpadRight,
    Cross, Circle, Square, Triangle,
    L1, R1, L2, R2, L3, R3,
    Options, Share, Touchpad,
}
```

`src/CouchLink.Core/Input/VirtualKeys.cs`:
```csharp
namespace CouchLink.Core.Input;

/// <summary>Win32 virtual-key codes. Mouse buttons use their VK codes too.</summary>
public static class VirtualKeys
{
    public const ushort LButton = 0x01;
    public const ushort RButton = 0x02;
    public const ushort MButton = 0x04;
    public const ushort Back = 0x08;
    public const ushort Tab = 0x09;
    public const ushort Return = 0x0D;
    public const ushort Shift = 0x10;
    public const ushort Control = 0x11;
    public const ushort Left = 0x25;
    public const ushort Up = 0x26;
    public const ushort Right = 0x27;
    public const ushort Down = 0x28;

    /// <summary>VK code of a letter or digit key ('A'..'Z', '0'..'9').</summary>
    public static ushort Letter(char c) => char.ToUpperInvariant(c);
}
```

`src/CouchLink.Core/Input/KeyLayout.cs`:
```csharp
namespace CouchLink.Core.Input;

/// <summary>Which keys drive which DS4 control. Several keys may drive one control.</summary>
public sealed class KeyLayout
{
    private readonly Dictionary<PadControl, ushort[]> _bindings;

    private KeyLayout(Dictionary<PadControl, ushort[]> bindings) => _bindings = bindings;

    public IReadOnlyList<ushort> KeysFor(PadControl control) =>
        _bindings.TryGetValue(control, out var keys) ? keys : [];

    /// <summary>Spec section 6.1 default layout.</summary>
    public static KeyLayout CreateDefault() => new(new Dictionary<PadControl, ushort[]>
    {
        [PadControl.LeftUp] = [VirtualKeys.Letter('W')],
        [PadControl.LeftLeft] = [VirtualKeys.Letter('A')],
        [PadControl.LeftDown] = [VirtualKeys.Letter('S')],
        [PadControl.LeftRight] = [VirtualKeys.Letter('D')],
        [PadControl.DpadUp] = [VirtualKeys.Up],
        [PadControl.DpadDown] = [VirtualKeys.Down],
        [PadControl.DpadLeft] = [VirtualKeys.Left],
        [PadControl.DpadRight] = [VirtualKeys.Right],
        [PadControl.Cross] = [VirtualKeys.Letter('K')],
        [PadControl.Square] = [VirtualKeys.Letter('J'), VirtualKeys.LButton],
        [PadControl.Circle] = [VirtualKeys.Letter('L')],
        [PadControl.Triangle] = [VirtualKeys.Letter('I')],
        [PadControl.L1] = [VirtualKeys.Letter('Q')],
        [PadControl.R1] = [VirtualKeys.Letter('E')],
        [PadControl.L2] = [VirtualKeys.Control],
        [PadControl.R2] = [VirtualKeys.Shift],
        [PadControl.L3] = [VirtualKeys.Letter('F')],
        [PadControl.R3] = [VirtualKeys.MButton],
        [PadControl.Options] = [VirtualKeys.Return],
        [PadControl.Share] = [VirtualKeys.Back],
        [PadControl.Touchpad] = [VirtualKeys.Tab],
    });
}
```

`src/CouchLink.Core/Input/InputMapper.cs`:
```csharp
namespace CouchLink.Core.Input;

/// <summary>
/// Tracks held keys and mouse movement and turns them into a DS4 state.
/// Thread-safe: input events arrive on the UI thread, Tick runs on the send loop.
/// </summary>
public sealed class InputMapper
{
    private static readonly (PadControl Control, PadButtons Button)[] ButtonMap =
    [
        (PadControl.Cross, PadButtons.Cross),
        (PadControl.Circle, PadButtons.Circle),
        (PadControl.Square, PadButtons.Square),
        (PadControl.Triangle, PadButtons.Triangle),
        (PadControl.L1, PadButtons.L1),
        (PadControl.R1, PadButtons.R1),
        (PadControl.L2, PadButtons.L2),
        (PadControl.R2, PadButtons.R2),
        (PadControl.L3, PadButtons.L3),
        (PadControl.R3, PadButtons.R3),
        (PadControl.Options, PadButtons.Options),
        (PadControl.Share, PadButtons.Share),
        (PadControl.Touchpad, PadButtons.Touchpad),
        (PadControl.DpadUp, PadButtons.DpadUp),
        (PadControl.DpadDown, PadButtons.DpadDown),
        (PadControl.DpadLeft, PadButtons.DpadLeft),
        (PadControl.DpadRight, PadButtons.DpadRight),
    ];

    private readonly KeyLayout _layout;
    private readonly MouseStick _mouse;
    private readonly HashSet<ushort> _held = [];
    private readonly Lock _gate = new();

    public InputMapper(KeyLayout layout, MouseStick mouse)
    {
        _layout = layout;
        _mouse = mouse;
    }

    public void KeyDown(ushort vk) { lock (_gate) _held.Add(vk); }

    public void KeyUp(ushort vk) { lock (_gate) _held.Remove(vk); }

    public void MouseMove(int dx, int dy) { lock (_gate) _mouse.AddDelta(dx, dy); }

    /// <summary>Releases everything, e.g. when the window loses focus.</summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            _held.Clear();
            _mouse.Reset();
        }
    }

    public PadState Tick(double dtSeconds)
    {
        lock (_gate)
        {
            var buttons = PadButtons.None;
            foreach (var (control, button) in ButtonMap)
                if (IsDown(control))
                    buttons |= button;

            var (lx, ly) = StickMath.FromDirections(
                IsDown(PadControl.LeftUp), IsDown(PadControl.LeftDown),
                IsDown(PadControl.LeftLeft), IsDown(PadControl.LeftRight));
            var (rx, ry) = _mouse.Update(dtSeconds);
            byte l2 = buttons.HasFlag(PadButtons.L2) ? (byte)255 : (byte)0;
            byte r2 = buttons.HasFlag(PadButtons.R2) ? (byte)255 : (byte)0;
            return new PadState(buttons, lx, ly, rx, ry, l2, r2);
        }
    }

    private bool IsDown(PadControl control)
    {
        foreach (var key in _layout.KeysFor(control))
            if (_held.Contains(key))
                return true;
        return false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~InputMapperTests`
Expected: PASS (25 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(core): default key layout and keyboard/mouse to DS4 mapper"
```

---

### Task 6: Input packet and sequence filter

**Files:**
- Create: `src/CouchLink.Core/Protocol/InputPacket.cs`, `src/CouchLink.Core/Protocol/SequenceFilter.cs`
- Test: `tests/CouchLink.Core.Tests/InputPacketTests.cs`, `tests/CouchLink.Core.Tests/SequenceFilterTests.cs`

**Interfaces:**
- Consumes: `PadState`, `PadButtons` (Task 2).
- Produces:
  - `readonly record struct InputPacket(byte Slot, uint Epoch, uint Sequence, PadState State)` with `const int Size = 24`, `void WriteTo(Span<byte> destination)`, `static bool TryParse(ReadOnlySpan<byte> source, out InputPacket packet)`.
  - `sealed class SequenceFilter` with `bool Accept(byte slot, uint epoch, uint sequence)` (not thread-safe; caller locks).

Wire format (little-endian, 24 bytes):

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | magic `0x4C43` (bytes `43 4C`, "CL") |
| 2 | 1 | version = 1 |
| 3 | 1 | type = 1 (input) |
| 4 | 1 | slot (2..10) |
| 5 | 1 | reserved = 0 |
| 6 | 4 | epoch (random per client run) |
| 10 | 4 | sequence |
| 14 | 4 | buttons (`PadButtons`) |
| 18 | 6 | LX, LY, RX, RY, L2, R2 |

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/InputPacketTests.cs`:
```csharp
using CouchLink.Core.Input;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class InputPacketTests
{
    private static readonly InputPacket Sample = new(
        Slot: 3, Epoch: 0xA1B2C3D4, Sequence: 42,
        State: new PadState(PadButtons.Cross | PadButtons.DpadLeft, 1, 2, 3, 4, 5, 6));

    private static byte[] Bytes(InputPacket p)
    {
        var b = new byte[InputPacket.Size];
        p.WriteTo(b);
        return b;
    }

    [Fact]
    public void Round_trips()
    {
        Assert.True(InputPacket.TryParse(Bytes(Sample), out var parsed));
        Assert.Equal(Sample, parsed);
    }

    [Fact]
    public void Header_layout_is_fixed()
    {
        var b = Bytes(Sample);
        Assert.Equal(new byte[] { 0x43, 0x4C, 1, 1, 3, 0 }, b[..6]);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6 }, b[18..]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(25)]
    public void Wrong_length_is_rejected(int length)
    {
        Assert.False(InputPacket.TryParse(new byte[length], out _));
    }

    [Theory]
    [InlineData(0)] // magic low byte
    [InlineData(1)] // magic high byte
    [InlineData(2)] // version
    [InlineData(3)] // type
    public void Wrong_header_is_rejected(int index)
    {
        var b = Bytes(Sample);
        b[index] ^= 0xFF;
        Assert.False(InputPacket.TryParse(b, out _));
    }

    [Fact]
    public void Random_garbage_is_rejected()
    {
        var b = new byte[InputPacket.Size];
        new Random(1234).NextBytes(b);
        Assert.False(InputPacket.TryParse(b, out _));
    }

    [Fact]
    public void Unknown_button_bits_are_stripped()
    {
        var b = Bytes(Sample);
        b[17] = 0xFF; // top byte of buttons: bits 24..31 are undefined
        Assert.True(InputPacket.TryParse(b, out var parsed));
        Assert.Equal(PadButtons.Cross | PadButtons.DpadLeft, parsed.State.Buttons);
    }

    [Fact]
    public void WriteTo_rejects_short_buffer()
    {
        Assert.Throws<ArgumentException>(() => Sample.WriteTo(new byte[10]));
    }
}
```

`tests/CouchLink.Core.Tests/SequenceFilterTests.cs`:
```csharp
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class SequenceFilterTests
{
    [Fact]
    public void First_packet_for_a_slot_is_accepted()
    {
        Assert.True(new SequenceFilter().Accept(2, epoch: 7, sequence: 100));
    }

    [Fact]
    public void Newer_accepted_duplicate_and_older_rejected()
    {
        var f = new SequenceFilter();
        Assert.True(f.Accept(2, 7, 10));
        Assert.True(f.Accept(2, 7, 11));
        Assert.False(f.Accept(2, 7, 11));
        Assert.False(f.Accept(2, 7, 9));
    }

    [Fact]
    public void Sequence_wraparound_is_treated_as_newer()
    {
        var f = new SequenceFilter();
        Assert.True(f.Accept(2, 7, uint.MaxValue));
        Assert.True(f.Accept(2, 7, 0));
        Assert.True(f.Accept(2, 7, 1));
    }

    [Fact]
    public void Restarted_client_with_new_epoch_is_accepted_even_with_low_sequence()
    {
        var f = new SequenceFilter();
        Assert.True(f.Accept(2, 7, 5000));
        Assert.True(f.Accept(2, 8, 1));
        Assert.False(f.Accept(2, 8, 1));
    }

    [Fact]
    public void Slots_are_independent()
    {
        var f = new SequenceFilter();
        Assert.True(f.Accept(2, 7, 10));
        Assert.True(f.Accept(3, 9, 1));
        Assert.True(f.Accept(2, 7, 11));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~InputPacketTests|FullyQualifiedName~SequenceFilterTests"`
Expected: build FAILS — `InputPacket`, `SequenceFilter` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Protocol/InputPacket.cs`:
```csharp
using System.Buffers.Binary;
using CouchLink.Core.Input;

namespace CouchLink.Core.Protocol;

/// <summary>One client -> host controller-state datagram (24 bytes, little-endian).</summary>
public readonly record struct InputPacket(byte Slot, uint Epoch, uint Sequence, PadState State)
{
    public const int Size = 24;
    private const ushort Magic = 0x4C43; // "CL"
    private const byte Version = 1;
    private const byte TypeInput = 1;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"Need {Size} bytes.", nameof(destination));

        BinaryPrimitives.WriteUInt16LittleEndian(destination, Magic);
        destination[2] = Version;
        destination[3] = TypeInput;
        destination[4] = Slot;
        destination[5] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[6..], Epoch);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[10..], Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[14..], (uint)State.Buttons);
        destination[18] = State.LX;
        destination[19] = State.LY;
        destination[20] = State.RX;
        destination[21] = State.RY;
        destination[22] = State.L2;
        destination[23] = State.R2;
    }

    /// <summary>Parses a datagram; returns false for anything that is not a v1 input packet.</summary>
    public static bool TryParse(ReadOnlySpan<byte> source, out InputPacket packet)
    {
        packet = default;
        if (source.Length != Size
            || BinaryPrimitives.ReadUInt16LittleEndian(source) != Magic
            || source[2] != Version
            || source[3] != TypeInput)
            return false;

        var buttons = (PadButtons)BinaryPrimitives.ReadUInt32LittleEndian(source[14..]) & PadButtons.Known;
        var state = new PadState(buttons, source[18], source[19], source[20], source[21], source[22], source[23]);
        packet = new InputPacket(
            source[4],
            BinaryPrimitives.ReadUInt32LittleEndian(source[6..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[10..]),
            state);
        return true;
    }
}
```

`src/CouchLink.Core/Protocol/SequenceFilter.cs`:
```csharp
namespace CouchLink.Core.Protocol;

/// <summary>
/// Newest-wins filter per slot. A different epoch means the client restarted,
/// so its packets are accepted even though the sequence starts over.
/// Not thread-safe.
/// </summary>
public sealed class SequenceFilter
{
    private readonly Dictionary<byte, (uint Epoch, uint Sequence)> _last = [];

    public bool Accept(byte slot, uint epoch, uint sequence)
    {
        if (_last.TryGetValue(slot, out var last)
            && last.Epoch == epoch
            && unchecked((int)(sequence - last.Sequence)) <= 0)
            return false;

        _last[slot] = (epoch, sequence);
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~InputPacketTests|FullyQualifiedName~SequenceFilterTests"`
Expected: PASS (12 + 5 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(core): 24-byte input packet and newest-wins sequence filter"
```

---

### Task 7: Pad manager (slot lifecycle and stale release)

**Files:**
- Create: `src/CouchLink.Core/Pads/PadManager.cs`
- Test: `tests/CouchLink.Core.Tests/Fakes.cs`, `tests/CouchLink.Core.Tests/PadManagerTests.cs`

**Interfaces:**
- Consumes: `IVirtualPad`, `IVirtualPadFactory`, `PadState` (Task 2); `InputPacket`, `SequenceFilter` (Task 6).
- Produces: `sealed class PadManager(IVirtualPadFactory factory, TimeProvider time) : IDisposable` with `const byte FirstSlot = 2`, `const byte LastSlot = 10`, `static readonly TimeSpan StaleAfter` (500 ms), `bool Handle(InputPacket packet)`, `void ReleaseStale()`, `int Count`. Thread-safe.

- [ ] **Step 1: Write the test fakes and failing tests**

`tests/CouchLink.Core.Tests/Fakes.cs`:
```csharp
using CouchLink.Core.Input;
using CouchLink.Core.Pads;

namespace CouchLink.Core.Tests;

internal sealed class FakePad : IVirtualPad
{
    public List<PadState> Applied { get; } = [];
    public bool Disposed { get; private set; }
    public void Apply(PadState state) => Applied.Add(state);
    public void Dispose() => Disposed = true;
}

internal sealed class FakePadFactory : IVirtualPadFactory
{
    public List<FakePad> Created { get; } = [];

    public IVirtualPad Create()
    {
        var pad = new FakePad();
        Created.Add(pad);
        return pad;
    }
}
```

`tests/CouchLink.Core.Tests/PadManagerTests.cs`:
```csharp
using CouchLink.Core.Input;
using CouchLink.Core.Pads;
using CouchLink.Core.Protocol;
using Microsoft.Extensions.Time.Testing;

namespace CouchLink.Core.Tests;

public class PadManagerTests
{
    private readonly FakePadFactory _factory = new();
    private readonly FakeTimeProvider _time = new();
    private readonly PadManager _manager;

    private static readonly PadState Pressed = PadState.Neutral with { Buttons = PadButtons.Cross, LX = 255 };

    public PadManagerTests() => _manager = new PadManager(_factory, _time);

    private static InputPacket Packet(byte slot, uint seq, PadState? state = null, uint epoch = 1) =>
        new(slot, epoch, seq, state ?? Pressed);

    [Fact]
    public void First_packet_creates_pad_and_applies_state()
    {
        Assert.True(_manager.Handle(Packet(3, 1)));
        var pad = Assert.Single(_factory.Created);
        Assert.Equal(Pressed, pad.Applied[^1]);
        Assert.Equal(1, _manager.Count);
    }

    [Fact]
    public void Same_slot_reuses_its_pad()
    {
        _manager.Handle(Packet(3, 1));
        _manager.Handle(Packet(3, 2, PadState.Neutral));
        var pad = Assert.Single(_factory.Created);
        Assert.Equal(PadState.Neutral, pad.Applied[^1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(11)]
    [InlineData(255)]
    public void Slot_outside_2_to_10_is_ignored(byte slot)
    {
        Assert.False(_manager.Handle(Packet(slot, 1)));
        Assert.Empty(_factory.Created);
    }

    [Fact]
    public void All_nine_slots_get_separate_pads()
    {
        for (byte slot = 2; slot <= 10; slot++)
            Assert.True(_manager.Handle(Packet(slot, 1)));
        Assert.Equal(9, _factory.Created.Count);
        Assert.Equal(9, _manager.Count);
    }

    [Fact]
    public void Old_or_duplicate_packets_are_not_applied()
    {
        _manager.Handle(Packet(2, 5));
        Assert.False(_manager.Handle(Packet(2, 5, PadState.Neutral)));
        Assert.False(_manager.Handle(Packet(2, 4, PadState.Neutral)));
        Assert.Equal(Pressed, _factory.Created[0].Applied[^1]);
    }

    [Fact]
    public void Restarted_client_is_accepted_on_same_pad()
    {
        _manager.Handle(Packet(2, 900, epoch: 1));
        Assert.True(_manager.Handle(Packet(2, 1, PadState.Neutral, epoch: 2)));
        var pad = Assert.Single(_factory.Created);
        Assert.Equal(PadState.Neutral, pad.Applied[^1]);
    }

    [Fact]
    public void Silent_pad_is_released_after_500ms_exactly_once()
    {
        _manager.Handle(Packet(2, 1));
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
    public void Active_pad_is_not_released()
    {
        _manager.Handle(Packet(2, 1));
        for (uint seq = 2; seq < 100; seq++)
        {
            _time.Advance(TimeSpan.FromMilliseconds(8));
            _manager.Handle(Packet(2, seq));
            _manager.ReleaseStale();
        }
        Assert.DoesNotContain(PadState.Neutral, _factory.Created[0].Applied.Skip(1));
    }

    [Fact]
    public void Dispose_unplugs_all_pads()
    {
        _manager.Handle(Packet(2, 1));
        _manager.Handle(Packet(3, 1));
        _manager.Dispose();
        Assert.All(_factory.Created, p => Assert.True(p.Disposed));
        Assert.Equal(0, _manager.Count);
    }
}
```

Note: `Active_pad_is_not_released` skips index 0 only as a guard; `FakePad` does not apply Neutral on creation (the real `ViGEmPad` does), so `Applied` holds only packet states.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~PadManagerTests`
Expected: build FAILS — `PadManager` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Pads/PadManager.cs`:
```csharp
using CouchLink.Core.Input;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Pads;

/// <summary>
/// Owns the host's virtual pads: one per slot (P2..P10), created on the first
/// packet for that slot. A pad that stops receiving packets is released to
/// Neutral so a crashed client can't leave a player running forever.
/// </summary>
public sealed class PadManager : IDisposable
{
    public const byte FirstSlot = 2;
    public const byte LastSlot = 10;
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMilliseconds(500);

    private sealed class Entry(IVirtualPad pad)
    {
        public IVirtualPad Pad { get; } = pad;
        public long LastSeen { get; set; }
        public bool IsNeutral { get; set; }
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

    /// <summary>Applies a packet. Returns false if it was ignored.</summary>
    public bool Handle(InputPacket packet)
    {
        if (packet.Slot is < FirstSlot or > LastSlot)
            return false;

        lock (_gate)
        {
            if (!_filter.Accept(packet.Slot, packet.Epoch, packet.Sequence))
                return false;

            if (!_pads.TryGetValue(packet.Slot, out var entry))
            {
                entry = new Entry(_factory.Create());
                _pads[packet.Slot] = entry;
            }

            entry.Pad.Apply(packet.State);
            entry.LastSeen = _time.GetTimestamp();
            entry.IsNeutral = packet.State == PadState.Neutral;
            return true;
        }
    }

    /// <summary>Call periodically (e.g. every 100 ms).</summary>
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

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~PadManagerTests`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```powershell
git add .
git commit -m "feat(core): pad manager with slot range, newest-wins and stale release"
```

---

### Task 8: Send policy and UDP input transport

**Files:**
- Create: `src/CouchLink.Core/Net/Ports.cs`, `src/CouchLink.Core/Net/SendPolicy.cs`, `src/CouchLink.Core/Net/InputSender.cs`, `src/CouchLink.Core/Net/InputReceiver.cs`
- Test: `tests/CouchLink.Core.Tests/SendPolicyTests.cs`, `tests/CouchLink.Core.Tests/UdpInputTests.cs`

**Interfaces:**
- Consumes: `PadState` (Task 2); `InputPacket` (Task 6).
- Produces:
  - `static class Ports { const int Input = 47803; }`.
  - `sealed class SendPolicy` with `static readonly TimeSpan MaxInterval` (8 ms) and `bool ShouldSend(PadState state, TimeSpan now)`.
  - `sealed class InputSender(IPEndPoint host, byte slot) : IDisposable` with `void Send(PadState state)`; random epoch per instance, sequence starts at 1.
  - `sealed class InputReceiver(int port) : IDisposable` with `int LocalPort` and `Task RunAsync(Action<InputPacket> onPacket, CancellationToken ct)`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/SendPolicyTests.cs`:
```csharp
using CouchLink.Core.Input;
using CouchLink.Core.Net;

namespace CouchLink.Core.Tests;

public class SendPolicyTests
{
    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);
    private static readonly PadState Pressed = PadState.Neutral with { Buttons = PadButtons.Cross };

    [Fact]
    public void First_state_is_always_sent()
    {
        Assert.True(new SendPolicy().ShouldSend(PadState.Neutral, Ms(0)));
    }

    [Fact]
    public void Unchanged_state_waits_for_8ms()
    {
        var p = new SendPolicy();
        p.ShouldSend(PadState.Neutral, Ms(0));
        Assert.False(p.ShouldSend(PadState.Neutral, Ms(7.9)));
        Assert.True(p.ShouldSend(PadState.Neutral, Ms(8)));
        Assert.False(p.ShouldSend(PadState.Neutral, Ms(9)));
    }

    [Fact]
    public void Changed_state_is_sent_immediately()
    {
        var p = new SendPolicy();
        p.ShouldSend(PadState.Neutral, Ms(0));
        Assert.True(p.ShouldSend(Pressed, Ms(1)));
        Assert.True(p.ShouldSend(PadState.Neutral, Ms(2)));
    }
}
```

`tests/CouchLink.Core.Tests/UdpInputTests.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CouchLink.Core.Input;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class UdpInputTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Sender_packets_reach_receiver_in_order_and_garbage_is_ignored()
    {
        using var receiver = new InputReceiver(port: 0);
        var received = Channel.CreateUnbounded<InputPacket>();
        using var cts = new CancellationTokenSource();
        var loop = receiver.RunAsync(p => received.Writer.TryWrite(p), cts.Token);

        var host = new IPEndPoint(IPAddress.Loopback, receiver.LocalPort);
        using (var raw = new UdpClient())
        {
            raw.Send([1, 2, 3], 3, host);                              // too short
            raw.Send(new byte[InputPacket.Size], InputPacket.Size, host); // wrong magic
        }

        var pressed = PadState.Neutral with { Buttons = PadButtons.Square, RX = 200 };
        using var sender = new InputSender(host, slot: 4);
        sender.Send(pressed);
        sender.Send(PadState.Neutral);

        using var wait = new CancellationTokenSource(Timeout);
        var first = await received.Reader.ReadAsync(wait.Token);
        var second = await received.Reader.ReadAsync(wait.Token);

        Assert.Equal((byte)4, first.Slot);
        Assert.Equal(pressed, first.State);
        Assert.Equal(PadState.Neutral, second.State);
        Assert.Equal(first.Epoch, second.Epoch);
        Assert.Equal(first.Sequence + 1, second.Sequence);
        Assert.False(received.Reader.TryRead(out _));

        cts.Cancel();
        await loop.WaitAsync(Timeout);
    }

    [Fact]
    public void Each_sender_gets_its_own_epoch()
    {
        var host = new IPEndPoint(IPAddress.Loopback, 9);
        using var a = new InputSender(host, 2);
        using var b = new InputSender(host, 2);
        Assert.NotEqual(a.Epoch, b.Epoch);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SendPolicyTests|FullyQualifiedName~UdpInputTests"`
Expected: build FAILS — `SendPolicy`, `InputSender`, `InputReceiver` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Net/Ports.cs`:
```csharp
namespace CouchLink.Core.Net;

public static class Ports
{
    public const int Input = 47803;
}
```

`src/CouchLink.Core/Net/SendPolicy.cs`:
```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Net;

/// <summary>Send the full state on every change, and at least every 8 ms.</summary>
public sealed class SendPolicy
{
    public static readonly TimeSpan MaxInterval = TimeSpan.FromMilliseconds(8);

    private PadState _last;
    private TimeSpan? _lastSent;

    public bool ShouldSend(PadState state, TimeSpan now)
    {
        if (_lastSent is { } sent && state == _last && now - sent < MaxInterval)
            return false;

        _last = state;
        _lastSent = now;
        return true;
    }
}
```

`src/CouchLink.Core/Net/InputSender.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Input;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>Client side: sends controller state to the host. Call Send from one thread.</summary>
public sealed class InputSender : IDisposable
{
    private readonly UdpClient _udp = new();
    private readonly byte[] _buffer = new byte[InputPacket.Size];
    private readonly byte _slot;
    private uint _sequence;

    public InputSender(IPEndPoint host, byte slot)
    {
        _slot = slot;
        Epoch = unchecked((uint)Random.Shared.NextInt64());
        _udp.Connect(host);
    }

    /// <summary>Random per run, so the host can tell a restarted client from stale packets.</summary>
    public uint Epoch { get; }

    public void Send(PadState state)
    {
        new InputPacket(_slot, Epoch, ++_sequence, state).WriteTo(_buffer);
        try
        {
            _udp.Send(_buffer, _buffer.Length);
        }
        catch (SocketException)
        {
            // Host not reachable right now; the next send (<= 8 ms) carries the full state anyway.
        }
    }

    public void Dispose() => _udp.Dispose();
}
```

`src/CouchLink.Core/Net/InputReceiver.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>Host side: receives input datagrams and hands valid packets to a callback.</summary>
public sealed class InputReceiver : IDisposable
{
    private readonly UdpClient _udp;

    public InputReceiver(int port) => _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));

    public int LocalPort => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

    public async Task RunAsync(Action<InputPacket> onPacket, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue; // e.g. ICMP port-unreachable reset on Windows; keep listening
            }

            if (InputPacket.TryParse(result.Buffer, out var packet))
                onPacket(packet);
        }
    }

    public void Dispose() => _udp.Dispose();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SendPolicyTests|FullyQualifiedName~UdpInputTests"`
Expected: PASS (3 + 2 tests). If Windows Firewall prompts for `testhost.exe`, allow private networks.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 6: Commit**

```powershell
git add .
git commit -m "feat(core): send policy and UDP input sender/receiver"
```

---

### Task 9: Dev app - Raw Input client and ViGEm host, two-PC test

**Files:**
- Create: `src/CouchLink.App/CouchLink.App.csproj` (via template), `src/CouchLink.App/Input/NativeMethods.cs`, `src/CouchLink.App/Input/RawInputSource.cs`, `src/CouchLink.App/Input/ClientInputLoop.cs`, `src/CouchLink.App/HostInputService.cs`
- Modify: `src/CouchLink.App/MainWindow.xaml`, `src/CouchLink.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `InputMapper`, `KeyLayout`, `MouseStick`, `PadState` (Tasks 2-5); `PadManager` (Task 7); `Ports`, `SendPolicy`, `InputSender`, `InputReceiver` (Task 8); `ViGEmPadFactory` (Task 3).
- Produces: the runnable dev app. Nothing later depends on its classes except `RawInputSource` and `ClientInputLoop`, which Plan 4 reuses in the real "Playing" screen.

This task is Windows UI + driver glue with no unit tests; it is verified by the manual two-PC test in Steps 7-8. The window is temporary and is replaced by the real lobby in Plan 4.

- [ ] **Step 1: Create the WPF project**

```powershell
dotnet new wpf -n CouchLink.App -o src/CouchLink.App -f net10.0
dotnet sln add src/CouchLink.App
dotnet add src/CouchLink.App reference src/CouchLink.Core src/CouchLink.Pads
```
In `src/CouchLink.App/CouchLink.App.csproj` make sure the property group contains:
```xml
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <UseWPF>true</UseWPF>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
```

- [ ] **Step 2: P/Invoke declarations**

`src/CouchLink.App/Input/NativeMethods.cs`:
```csharp
using System.Runtime.InteropServices;

namespace CouchLink.App.Input;

internal static partial class NativeMethods
{
    public const int WM_INPUT = 0x00FF;
    public const uint RID_INPUT = 0x10000003;
    public const uint RIM_TYPEMOUSE = 0;
    public const uint RIM_TYPEKEYBOARD = 1;
    public const ushort RI_KEY_BREAK = 0x01;
    public const ushort MOUSE_MOVE_ABSOLUTE = 0x01;
    public const ushort RI_MOUSE_LEFT_DOWN = 0x0001, RI_MOUSE_LEFT_UP = 0x0002;
    public const ushort RI_MOUSE_RIGHT_DOWN = 0x0004, RI_MOUSE_RIGHT_UP = 0x0008;
    public const ushort RI_MOUSE_MIDDLE_DOWN = 0x0010, RI_MOUSE_MIDDLE_UP = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTHEADER
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct RAWMOUSE
    {
        [FieldOffset(0)] public ushort Flags;
        [FieldOffset(4)] public ushort ButtonFlags;
        [FieldOffset(6)] public ushort ButtonData;
        [FieldOffset(8)] public uint RawButtons;
        [FieldOffset(12)] public int LastX;
        [FieldOffset(16)] public int LastY;
        [FieldOffset(20)] public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RAWKEYBOARD
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterRawInputDevices(
        [In] RAWINPUTDEVICE[] devices, uint count, uint size);

    [LibraryImport("user32.dll")]
    public static unsafe partial uint GetRawInputData(
        IntPtr rawInput, uint command, void* data, ref uint size, uint headerSize);

    [LibraryImport("winmm.dll")]
    public static partial uint timeBeginPeriod(uint milliseconds);

    [LibraryImport("winmm.dll")]
    public static partial uint timeEndPeriod(uint milliseconds);
}
```

- [ ] **Step 3: Raw Input source**

`src/CouchLink.App/Input/RawInputSource.cs`:
```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;
using CouchLink.Core.Input;
using static CouchLink.App.Input.NativeMethods;

namespace CouchLink.App.Input;

/// <summary>
/// Reads keyboard and relative mouse input for one window via Win32 Raw Input.
/// Only delivers input while that window is in the foreground.
/// </summary>
internal sealed class RawInputSource : IDisposable
{
    private readonly HwndSource _source;

    public event Action<ushort>? KeyDown;
    public event Action<ushort>? KeyUp;
    public event Action<int, int>? MouseMove;

    public RawInputSource(HwndSource source)
    {
        _source = source;
        RAWINPUTDEVICE[] devices =
        [
            new() { UsagePage = 0x01, Usage = 0x06, Flags = 0, Target = source.Handle }, // keyboard
            new() { UsagePage = 0x01, Usage = 0x02, Flags = 0, Target = source.Handle }, // mouse
        ];
        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
            throw new InvalidOperationException($"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");
        _source.AddHook(WndProc);
    }

    private unsafe IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT)
            return IntPtr.Zero;

        uint headerSize = (uint)sizeof(RAWINPUTHEADER);
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, null, ref size, headerSize);
        if (size == 0 || size > 1024)
            return IntPtr.Zero;

        byte* buffer = stackalloc byte[(int)size];
        if (GetRawInputData(lParam, RID_INPUT, buffer, ref size, headerSize) != size)
            return IntPtr.Zero;

        var header = *(RAWINPUTHEADER*)buffer;
        byte* data = buffer + headerSize;
        if (header.Type == RIM_TYPEKEYBOARD)
            OnKeyboard(*(RAWKEYBOARD*)data);
        else if (header.Type == RIM_TYPEMOUSE)
            OnMouse(*(RAWMOUSE*)data);
        return IntPtr.Zero;
    }

    private void OnKeyboard(RAWKEYBOARD kb)
    {
        if (kb.VKey is 0 or 0xFF)
            return; // fake/overrun keys
        if ((kb.Flags & RI_KEY_BREAK) != 0)
            KeyUp?.Invoke(kb.VKey);
        else
            KeyDown?.Invoke(kb.VKey);
    }

    private void OnMouse(RAWMOUSE m)
    {
        if ((m.Flags & MOUSE_MOVE_ABSOLUTE) == 0 && (m.LastX != 0 || m.LastY != 0))
            MouseMove?.Invoke(m.LastX, m.LastY);

        Button(m.ButtonFlags, RI_MOUSE_LEFT_DOWN, RI_MOUSE_LEFT_UP, VirtualKeys.LButton);
        Button(m.ButtonFlags, RI_MOUSE_RIGHT_DOWN, RI_MOUSE_RIGHT_UP, VirtualKeys.RButton);
        Button(m.ButtonFlags, RI_MOUSE_MIDDLE_DOWN, RI_MOUSE_MIDDLE_UP, VirtualKeys.MButton);
    }

    private void Button(ushort flags, ushort down, ushort up, ushort vk)
    {
        if ((flags & down) != 0) KeyDown?.Invoke(vk);
        if ((flags & up) != 0) KeyUp?.Invoke(vk);
    }

    public void Dispose() => _source.RemoveHook(WndProc);
}
```

- [ ] **Step 4: Client send loop**

`src/CouchLink.App/Input/ClientInputLoop.cs`:
```csharp
using System.Diagnostics;
using CouchLink.Core.Input;
using CouchLink.Core.Net;
using static CouchLink.App.Input.NativeMethods;

namespace CouchLink.App.Input;

/// <summary>Ticks the mapper every ~1 ms and sends state on change or every 8 ms.</summary>
internal sealed class ClientInputLoop : IDisposable
{
    private readonly InputMapper _mapper;
    private readonly InputSender _sender;
    private readonly Thread _thread;
    private volatile bool _running = true;

    public ClientInputLoop(InputMapper mapper, InputSender sender)
    {
        _mapper = mapper;
        _sender = sender;
        _thread = new Thread(Run) { IsBackground = true, Name = "CouchLink input", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Last state sent; read by the UI for display.</summary>
    public PadState LastSent { get; private set; } = PadState.Neutral;

    private void Run()
    {
        timeBeginPeriod(1); // default Windows timer is ~15.6 ms; we need ~1 ms sleeps
        try
        {
            var policy = new SendPolicy();
            var clock = Stopwatch.StartNew();
            var lastTick = TimeSpan.Zero;
            while (_running)
            {
                var now = clock.Elapsed;
                var state = _mapper.Tick((now - lastTick).TotalSeconds);
                lastTick = now;
                if (policy.ShouldSend(state, now))
                {
                    _sender.Send(state);
                    LastSent = state;
                }
                Thread.Sleep(1);
            }
            _sender.Send(PadState.Neutral);
        }
        finally
        {
            timeEndPeriod(1);
        }
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join();
        _sender.Dispose();
    }
}
```

- [ ] **Step 5: Host service**

`src/CouchLink.App/HostInputService.cs`:
```csharp
using CouchLink.Core.Net;
using CouchLink.Core.Pads;
using CouchLink.Pads;

namespace CouchLink.App;

/// <summary>Host side: receives input on UDP 47803 and drives one virtual DS4 per slot.</summary>
internal sealed class HostInputService : IDisposable
{
    private readonly ViGEmPadFactory _factory;
    private readonly PadManager _pads;
    private readonly InputReceiver _receiver;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoop;
    private readonly Timer _staleTimer;

    private HostInputService(ViGEmPadFactory factory)
    {
        _factory = factory;
        _pads = new PadManager(factory, TimeProvider.System);
        _receiver = new InputReceiver(Ports.Input);
        _receiveLoop = _receiver.RunAsync(p => _pads.Handle(p), _cts.Token);
        _staleTimer = new Timer(_ => _pads.ReleaseStale(), null, 100, 100);
    }

    public int PadCount => _pads.Count;

    public static bool TryStart(out HostInputService? service, out string? error)
    {
        service = null;
        if (!ViGEmPadFactory.TryCreate(out var factory, out error))
            return false;
        service = new HostInputService(factory!);
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _staleTimer.Dispose();
        _receiveLoop.Wait(TimeSpan.FromSeconds(2));
        _receiver.Dispose();
        _pads.Dispose();
        _factory.Dispose();
    }
}
```

- [ ] **Step 6: Temporary dev window**

`src/CouchLink.App/MainWindow.xaml` (replace contents):
```xml
<Window x:Class="CouchLink.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="CouchLink (dev)" Width="460" Height="320">
    <StackPanel Margin="16">
        <Button x:Name="HostButton" Content="Host (create pads)" Height="40" Click="OnHost"/>
        <StackPanel Orientation="Horizontal" Margin="0,16,0,0">
            <TextBlock Text="Host IP" VerticalAlignment="Center"/>
            <TextBox x:Name="HostIpBox" Width="140" Margin="8,0" Text="127.0.0.1"/>
            <TextBlock Text="Slot" VerticalAlignment="Center"/>
            <ComboBox x:Name="SlotBox" Width="60" Margin="8,0"/>
        </StackPanel>
        <Button x:Name="JoinButton" Content="Join (send input)" Height="40" Margin="0,8,0,0" Click="OnJoin"/>
        <TextBlock x:Name="StatusText" Margin="0,16,0,0" TextWrapping="Wrap" FontFamily="Consolas"/>
    </StackPanel>
</Window>
```

`src/CouchLink.App/MainWindow.xaml.cs` (replace contents):
```csharp
using System.Net;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CouchLink.App.Input;
using CouchLink.Core.Input;
using CouchLink.Core.Net;

namespace CouchLink.App;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private HostInputService? _host;
    private RawInputSource? _rawInput;
    private InputMapper? _mapper;
    private ClientInputLoop? _client;

    public MainWindow()
    {
        InitializeComponent();
        for (int slot = 2; slot <= 10; slot++)
            SlotBox.Items.Add(slot);
        SlotBox.SelectedIndex = 0;
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        Deactivated += (_, _) => _mapper?.ReleaseAll(); // never leave keys stuck
    }

    private void OnHost(object sender, RoutedEventArgs e)
    {
        if (!HostInputService.TryStart(out _host, out var error))
        {
            MessageBox.Show(this, error, "CouchLink");
            return;
        }
        HostButton.IsEnabled = JoinButton.IsEnabled = false;
    }

    private void OnJoin(object sender, RoutedEventArgs e)
    {
        if (!IPAddress.TryParse(HostIpBox.Text.Trim(), out var ip))
        {
            MessageBox.Show(this, "Enter a valid host IP address.", "CouchLink");
            return;
        }

        _mapper = new InputMapper(KeyLayout.CreateDefault(), new MouseStick());
        _rawInput = new RawInputSource((HwndSource)PresentationSource.FromVisual(this));
        _rawInput.KeyDown += _mapper.KeyDown;
        _rawInput.KeyUp += _mapper.KeyUp;
        _rawInput.MouseMove += _mapper.MouseMove;

        var sender = new InputSender(new IPEndPoint(ip, Ports.Input), (byte)(int)SlotBox.SelectedItem);
        _client = new ClientInputLoop(_mapper, sender);
        HostButton.IsEnabled = JoinButton.IsEnabled = false;
    }

    private void UpdateStatus()
    {
        if (_host is not null)
            StatusText.Text = $"Hosting. Virtual pads: {_host.PadCount}";
        else if (_client is not null)
        {
            var s = _client.LastSent;
            StatusText.Text =
                $"Sending to {HostIpBox.Text} as P{SlotBox.SelectedItem}\n" +
                $"Buttons: {s.Buttons}\nL: {s.LX},{s.LY}  R: {s.RX},{s.RY}  L2/R2: {s.L2}/{s.R2}";
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _statusTimer.Stop();
        _client?.Dispose();
        _rawInput?.Dispose();
        _host?.Dispose();
        base.OnClosed(e);
    }
}
```

Run: `dotnet build`
Expected: build succeeds.

- [ ] **Step 7: One-PC smoke test**

1. Run two copies: `dotnet run --project src/CouchLink.App` twice.
2. Window A: click **Host**. Allow the firewall prompt for private networks. Status: `Hosting. Virtual pads: 0`.
3. Window B: IP `127.0.0.1`, slot 2, click **Join**. Keep window B focused.
4. Expected: window A shows `Virtual pads: 1`. Press W in window B: B's status shows `L: 128,1`. Open `joy.cpl`, Properties of the new Wireless Controller: holding W moves the crosshair up, K lights button 2, moving the mouse moves the Z/rotation (right stick) display and it re-centers when you stop.
5. Hold W and click another window (focus loss): the joy.cpl crosshair returns to center immediately.
6. Close window B while holding W: within ~0.5 s the pad goes to center.

- [ ] **Step 8: Two-PC and 2K test**

1. Host PC: run the app, click **Host**. Note its IP (`ipconfig`).
2. Two client PCs: enter the host IP, choose **different** slots (2 and 3), click **Join**.
3. On the host, start 2K22 (borderless windowed). In controller select, each client's keys move its own icon; in a game each client controls its own player; the host keyboard controls P1.
4. Record any problems in `docs/gate-results.md` under a new heading `## Input path (two-PC test)` with pass/fail per check: pads appear, separate players, stuck-key release on focus loss, release on client close.

- [ ] **Step 9: Commit**

```powershell
git add .
git commit -m "feat(app): dev window with Raw Input client and ViGEm host over UDP"
```
