# CouchLink Plan 8: Playing Screen, Controls & Single Instance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A client plays without fighting Windows (Windows key, Alt+Tab, Alt+Esc, Ctrl+Esc blocked; pointer kept in the game), F1 shows the keys, keys can be rebound mid-game, and a second launch brings the running copy forward.

**Architecture:** Rules are plain code in `CouchLink.Core`: an editable, thread-safe `KeyLayout` inside one in-memory `ControlSettings`, `KeyNames`, `ShortcutFilter`, `InstanceDecision`, and the overlay text. The player window (its own thread, which pumps messages) owns the low-level keyboard hook (`KeyboardBlocker`) and `ClipCursor`, switched on when it becomes active while fullscreen and off when it isn't. The app shares `AppServices.Controls` between the input mapper, the F1 panel and a code-built `ControlsWindow`, and claims a named mutex at startup.

**Tech Stack:** C# / .NET 10, WPF, Win32 (`SetWindowsHookExW`, `ClipCursor`, `AllowSetForegroundWindow`) via `LibraryImport`, Direct2D/DirectWrite (existing overlay), xUnit 2.9.3, Microsoft.Extensions.TimeProvider.Testing.

**Spec:** `docs/superpowers/specs/2026-10-08-couchlink-playing-screen-controls-design.md` (all of it). Main design: sections 6.1-6.4, 6.7, 7.

**Builds on:** `plan7-lobby` @ 4196103 (PR #45, not yet merged). Branch: `plan8-playing`. Issues: #24, #25, #26.

## Global Constraints

- C#, .NET 10. `CouchLink.Core` stays `net10.0` with no Windows-only APIs. `TreatWarningsAsErrors` everywhere.
- Commits are signed. Conventional Commits. **No Claude attribution trailers** in commit messages or PR text.
- One key, one control: binding a key makes it the control's only key and takes it off any other control.
- Bindable: any keyboard key and the five mouse buttons (Left 0x01, Right 0x02, Middle 0x04, X1 0x05, X2 0x06). **Reserved:** Esc 0x1B, F1 0x70, F2 0x71, LWin 0x5B, RWin 0x5C.
- Sensitivity steps **1-10**, default **5** = `MouseStick.DefaultSensitivity` (0.02); each step multiplies by **1.3**.
- Edits are kept in memory only; nothing is written to disk.
- Blocked while the fullscreen player is active: LWin, RWin (down and up), Tab with Alt, Esc with Alt, Esc with Ctrl. Alt+F4 and Ctrl+Alt+Q pass. Nothing is blocked or clipped with `--windowed-player`.
- Start hint text (exact): `F1: controls · Ctrl+Alt+Q: leave`, shown **5 s** from the first frame of a join.
- Single instance: mutex `Local\CouchLink.SingleInstance`, event `Local\CouchLink.Activate`; skipped with `--windowed-player`.
- Exceptions in the hook callback, timer and wait callbacks are caught; they never end the process.

## Review Focus

1. **The player window is activated while it is being created**, before it is registered for messages, so the first `WM_ACTIVATE` is missed. A person expects blocking and the mouse lock from the first second of play. Pinned in Task 5 (the constructor locks if the window is already in front) and the manual gate row "Win key blocked from the first second".
2. **A player rebinds a key while holding it**, or while the game holds the old key. They expect nothing to stay pressed. Pinned in Task 2 (`An_edit_releases_held_keys`).
3. **Alt: Raw Input reports both Alt keys as 0x12, WPF reports 0xA4/0xA5.** A key bound to Alt in the editor must work in the game. Pinned in Task 1 (`Raw_Alt_becomes_left_or_right_alt`).
4. **Reaching the editor mid-game while Alt+Tab and the Windows key are blocked and the pointer is clipped.** A person expects a way in without leaving the session. Ruled in planning: `Ctrl+Alt+C` in the player opens the editor on top (Task 5 and Task 6), named on the F1 panel; manual gate row.
5. **A control left with no key** after its only key moved to another control. The editor and F1 must show it plainly, and the mapper must not throw. Pinned in Task 1 (`Describe_shows_none_for_a_control_without_keys`) and Task 2 (`A_control_without_keys_is_never_pressed`).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/CouchLink.Core/Input/VirtualKeys.cs` (modify) | New codes (X buttons, Esc, Space, F1, F2, Win, L/R Alt); Raw Alt to left/right |
| `src/CouchLink.Core/Input/KeyLayout.cs` (modify) | Editable, thread-safe layout: `Bind`, `IsReserved`, `ResetToDefault`; `BindResult` |
| `src/CouchLink.Core/Input/KeyNames.cs` (new) | Names for keys and controls; the editor/F1 groups |
| `src/CouchLink.Core/Input/ControlSettings.cs` (new) | The one in-memory settings object with `Changed` |
| `src/CouchLink.Core/Input/MouseStick.cs` (modify) | `InvertY` |
| `src/CouchLink.Core/Input/InputMapper.cs` (modify) | Constructor from `ControlSettings`; releases held keys on edits; `IDisposable` |
| `src/CouchLink.Core/Input/ShortcutFilter.cs` (new) | Which keystrokes the hook swallows |
| `src/CouchLink.Core/Startup/InstanceDecision.cs` (new) | Run / HandOff / Bypass |
| `src/CouchLink.Core/Video/OverlayText.cs` (modify) | `Controls(...)`, `StartHint` |
| `src/CouchLink.Video/IPlayerParts.cs`, `FramePresenter.cs` (modify) | Four overlay slots: status centre, controls top-left, stats top-right, hint bottom-left |
| `src/CouchLink.Video/PlayerCore.cs` (modify) | Controls panel and start hint |
| `src/CouchLink.Video/KeyboardBlocker.cs` (new) | `WH_KEYBOARD_LL` hook using `ShortcutFilter` |
| `src/CouchLink.Video/PlayerWindow.cs`, `VideoPlayer.cs` (modify) | F1, Ctrl+Alt+C, input lock on activate, `PlayerOptions.LockInput` |
| `src/CouchLink.App/ControlsWindow.cs` (new) | The editor |
| `src/CouchLink.App/SingleInstance.cs` (new) | Mutex + activate event |
| App glue (modify) | `AppServices`, `ClientPlay`, `ClientStreams`, `ClientVideoService`, `RawInputSource`, `NativeMethods`, Start/Session views, `MainWindow`, `App`, `CrashHandler` |

---

### Task 1: Key codes, the editable layout and key names

**Files:**
- Modify: `src/CouchLink.Core/Input/VirtualKeys.cs`, `src/CouchLink.Core/Input/KeyLayout.cs`
- Create: `src/CouchLink.Core/Input/KeyNames.cs`
- Test: `tests/CouchLink.Core.Tests/VirtualKeysTests.cs`, `tests/CouchLink.Core.Tests/KeyLayoutTests.cs`, `tests/CouchLink.Core.Tests/KeyNamesTests.cs`

**Interfaces:**
- Produces: `VirtualKeys.XButton1/XButton2/Escape/Space/F1/F2/LWin/RWin/LMenu/RMenu`; `readonly record struct BindResult(bool Bound, PadControl? MovedFrom)` with `static BindResult Reserved`; `KeyLayout.Bind(PadControl, ushort) -> BindResult`, `static bool IsReserved(ushort)`, `void ResetToDefault()`, `KeysFor` unchanged; `KeyNames.Of(ushort) -> string`, `KeyNames.Of(PadControl) -> string`, `KeyNames.Describe(KeyLayout, PadControl) -> string`, `KeyNames.Groups : IReadOnlyList<(string Name, PadControl[] Controls)>`.

- [ ] **Step 1: Write the failing tests**

In `tests/CouchLink.Core.Tests/VirtualKeysTests.cs` add:

```csharp
    [Theory]
    [InlineData(0x12, 0x38, KeyDown, VirtualKeys.LMenu)]
    [InlineData(0x12, 0x38, Extended, VirtualKeys.RMenu)]
    [InlineData(0x12, 0x38, Extended | KeyUp, VirtualKeys.RMenu)]
    public void Raw_Alt_becomes_left_or_right_alt(ushort vkey, ushort makeCode, ushort flags, ushort expected)
    {
        // WPF (the controls editor) reports 0xA4/0xA5; Raw Input (the game) reports 0x12.
        Assert.Equal(expected, VirtualKeys.FromRawKeyboard(vkey, makeCode, flags));
    }
```

Create `tests/CouchLink.Core.Tests/KeyLayoutTests.cs`:

```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class KeyLayoutTests
{
    private static readonly ushort K = VirtualKeys.Letter('K');
    private static readonly ushort J = VirtualKeys.Letter('J');

    [Fact]
    public void Bind_makes_the_new_key_the_controls_only_key()
    {
        var layout = KeyLayout.CreateDefault();
        Assert.Equal(new BindResult(true, null), layout.Bind(PadControl.Square, VirtualKeys.Space));
        Assert.Equal([VirtualKeys.Space], layout.KeysFor(PadControl.Square));
    }

    [Fact]
    public void Bind_takes_the_key_from_another_control()
    {
        var layout = KeyLayout.CreateDefault();
        Assert.Equal(new BindResult(true, PadControl.Cross), layout.Bind(PadControl.Circle, K));
        Assert.Equal([K], layout.KeysFor(PadControl.Circle));
        Assert.Empty(layout.KeysFor(PadControl.Cross));
    }

    [Fact]
    public void Taking_one_of_two_default_keys_leaves_the_other()
    {
        var layout = KeyLayout.CreateDefault();
        Assert.Equal(new BindResult(true, PadControl.Square), layout.Bind(PadControl.Cross, VirtualKeys.LButton));
        Assert.Equal([J], layout.KeysFor(PadControl.Square));
    }

    [Fact]
    public void Rebinding_a_two_key_default_keeps_only_the_new_key()
    {
        var layout = KeyLayout.CreateDefault();
        layout.Bind(PadControl.L2, VirtualKeys.LControl);
        Assert.Equal([VirtualKeys.LControl], layout.KeysFor(PadControl.L2));
    }

    [Fact]
    public void Binding_the_key_the_control_already_has_changes_nothing_else()
    {
        var layout = KeyLayout.CreateDefault();
        Assert.Equal(new BindResult(true, null), layout.Bind(PadControl.Cross, K));
        Assert.Equal([K], layout.KeysFor(PadControl.Cross));
    }

    [Theory]
    [InlineData(VirtualKeys.Escape)]
    [InlineData(VirtualKeys.F1)]
    [InlineData(VirtualKeys.F2)]
    [InlineData(VirtualKeys.LWin)]
    [InlineData(VirtualKeys.RWin)]
    public void Reserved_keys_are_refused(ushort key)
    {
        var layout = KeyLayout.CreateDefault();
        Assert.True(KeyLayout.IsReserved(key));
        Assert.Equal(BindResult.Reserved, layout.Bind(PadControl.Cross, key));
        Assert.Equal([K], layout.KeysFor(PadControl.Cross));
    }

    [Fact]
    public void Reset_restores_the_defaults()
    {
        var layout = KeyLayout.CreateDefault();
        layout.Bind(PadControl.Circle, K);
        layout.Bind(PadControl.L2, VirtualKeys.Space);
        layout.ResetToDefault();
        var fresh = KeyLayout.CreateDefault();
        foreach (var control in Enum.GetValues<PadControl>())
            Assert.Equal(fresh.KeysFor(control), layout.KeysFor(control));
    }
}
```

Create `tests/CouchLink.Core.Tests/KeyNamesTests.cs`:

```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class KeyNamesTests
{
    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x37, "7")]
    [InlineData(0x01, "Left click")]
    [InlineData(0x05, "Mouse 4")]
    [InlineData(0x06, "Mouse 5")]
    [InlineData(0xA2, "Left Ctrl")]
    [InlineData(0xA5, "Right Alt")]
    [InlineData(0x26, "↑")]
    [InlineData(0x20, "Space")]
    [InlineData(0x74, "F5")]
    [InlineData(0x63, "Num 3")]
    [InlineData(0xBC, ",")]
    public void Keys_have_readable_names(ushort vk, string name)
    {
        Assert.Equal(name, KeyNames.Of(vk));
    }

    [Fact]
    public void An_unknown_key_falls_back_to_its_code()
    {
        Assert.Equal("Key 0xFF", KeyNames.Of((ushort)0xFF));
    }

    [Fact]
    public void Every_default_key_has_a_real_name()
    {
        var layout = KeyLayout.CreateDefault();
        foreach (var control in Enum.GetValues<PadControl>())
            foreach (var key in layout.KeysFor(control))
                Assert.DoesNotContain("Key 0x", KeyNames.Of(key));
    }

    [Fact]
    public void Every_control_has_a_name_and_one_group()
    {
        foreach (var control in Enum.GetValues<PadControl>())
        {
            Assert.False(string.IsNullOrWhiteSpace(KeyNames.Of(control)));
            Assert.Single(KeyNames.Groups, g => g.Controls.Contains(control));
        }
    }

    [Fact]
    public void Describe_joins_keys()
    {
        Assert.Equal("J / Left click", KeyNames.Describe(KeyLayout.CreateDefault(), PadControl.Square));
    }

    [Fact]
    public void Describe_shows_none_for_a_control_without_keys()
    {
        var layout = KeyLayout.CreateDefault();
        layout.Bind(PadControl.Circle, VirtualKeys.Letter('K')); // Cross loses its only key
        Assert.Equal("(none)", KeyNames.Describe(layout, PadControl.Cross));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~VirtualKeysTests|FullyQualifiedName~KeyLayoutTests|FullyQualifiedName~KeyNamesTests"`
Expected: build FAILS: `'VirtualKeys' does not contain a definition for 'LMenu'` (and `Space`, `BindResult`, `KeyNames`).

- [ ] **Step 3: Implement**

Replace `src/CouchLink.Core/Input/VirtualKeys.cs`:

```csharp
namespace CouchLink.Core.Input;

/// <summary>Win32 virtual-key codes. Mouse buttons use their VK codes too.</summary>
public static class VirtualKeys
{
    public const ushort LButton = 0x01;
    public const ushort RButton = 0x02;
    public const ushort MButton = 0x04;
    public const ushort XButton1 = 0x05;
    public const ushort XButton2 = 0x06;
    public const ushort Back = 0x08;
    public const ushort Tab = 0x09;
    public const ushort Return = 0x0D;
    public const ushort Escape = 0x1B;
    public const ushort Space = 0x20;
    public const ushort Left = 0x25;
    public const ushort Up = 0x26;
    public const ushort Right = 0x27;
    public const ushort Down = 0x28;
    public const ushort LWin = 0x5B;
    public const ushort RWin = 0x5C;
    public const ushort F1 = 0x70;
    public const ushort F2 = 0x71;
    public const ushort LShift = 0xA0;
    public const ushort RShift = 0xA1;
    public const ushort LControl = 0xA2;
    public const ushort RControl = 0xA3;
    public const ushort LMenu = 0xA4;
    public const ushort RMenu = 0xA5;

    private const ushort Shift = 0x10;
    private const ushort Control = 0x11;
    private const ushort Menu = 0x12;
    private const ushort RightShiftMakeCode = 0x36;
    private const ushort ExtendedKeyFlag = 0x02; // RI_KEY_E0

    /// <summary>VK code of a letter or digit key ('A'..'Z', '0'..'9').</summary>
    public static ushort Letter(char c) => char.ToUpperInvariant(c);

    /// <summary>
    /// Raw Input reports both Shift keys as 0x10, both Ctrl keys as 0x11 and both Alt keys as 0x12.
    /// Turns them into the left/right codes (by make code for Shift, E0 flag for Ctrl and Alt) so
    /// holding both and releasing one doesn't release the other, and so they match the codes WPF
    /// gives the controls editor. Other keys pass through.
    /// </summary>
    public static ushort FromRawKeyboard(ushort vkey, ushort makeCode, ushort flags) => vkey switch
    {
        Shift => makeCode == RightShiftMakeCode ? RShift : LShift,
        Control => (flags & ExtendedKeyFlag) != 0 ? RControl : LControl,
        Menu => (flags & ExtendedKeyFlag) != 0 ? RMenu : LMenu,
        _ => vkey,
    };
}
```

Replace `src/CouchLink.Core/Input/KeyLayout.cs`:

```csharp
namespace CouchLink.Core.Input;

/// <summary>What <see cref="KeyLayout.Bind"/> did: refused a reserved key, or bound it, maybe taking it from another control.</summary>
public readonly record struct BindResult(bool Bound, PadControl? MovedFrom)
{
    public static readonly BindResult Reserved = new(false, null);
}

/// <summary>
/// Which keys drive which DS4 control. The defaults give a few controls two keys; a rebind gives the
/// control exactly one key and takes that key off any other control. Thread-safe: the editor binds
/// on the UI thread while the input loop reads every tick.
/// </summary>
public sealed class KeyLayout
{
    private static readonly ushort[] ReservedKeys =
        [VirtualKeys.Escape, VirtualKeys.F1, VirtualKeys.F2, VirtualKeys.LWin, VirtualKeys.RWin];

    private readonly Dictionary<PadControl, ushort[]> _bindings;
    private readonly Lock _gate = new();

    private KeyLayout(Dictionary<PadControl, ushort[]> bindings) => _bindings = bindings;

    public IReadOnlyList<ushort> KeysFor(PadControl control)
    {
        lock (_gate)
            return _bindings.TryGetValue(control, out var keys) ? keys : [];
    }

    /// <summary>Esc cancels a rebind, F1 and F2 toggle the player's panels, the Windows keys are blocked while playing.</summary>
    public static bool IsReserved(ushort key) => ReservedKeys.Contains(key);

    public BindResult Bind(PadControl control, ushort key)
    {
        if (IsReserved(key))
            return BindResult.Reserved;
        lock (_gate)
        {
            PadControl? movedFrom = null;
            foreach (var (other, keys) in _bindings.ToList())
            {
                if (other == control || !keys.Contains(key))
                    continue;
                _bindings[other] = keys.Where(k => k != key).ToArray();
                movedFrom = other;
            }
            _bindings[control] = [key];
            return new BindResult(true, movedFrom);
        }
    }

    public void ResetToDefault()
    {
        lock (_gate)
        {
            _bindings.Clear();
            foreach (var (control, keys) in Defaults())
                _bindings[control] = keys;
        }
    }

    /// <summary>Spec section 6.1 default layout.</summary>
    public static KeyLayout CreateDefault() => new(Defaults());

    private static Dictionary<PadControl, ushort[]> Defaults() => new()
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
        [PadControl.L2] = [VirtualKeys.LControl, VirtualKeys.RControl],
        [PadControl.R2] = [VirtualKeys.LShift, VirtualKeys.RShift],
        [PadControl.L3] = [VirtualKeys.Letter('F')],
        [PadControl.R3] = [VirtualKeys.MButton],
        [PadControl.Options] = [VirtualKeys.Return],
        [PadControl.Share] = [VirtualKeys.Back],
        [PadControl.Touchpad] = [VirtualKeys.Tab],
    };
}
```

Create `src/CouchLink.Core/Input/KeyNames.cs`:

```csharp
namespace CouchLink.Core.Input;

/// <summary>What the controls editor and the F1 panel call keys and controls. A table, no Windows API.</summary>
public static class KeyNames
{
    private static readonly Dictionary<ushort, string> Named = new()
    {
        [0x01] = "Left click", [0x02] = "Right click", [0x04] = "Middle click", [0x05] = "Mouse 4", [0x06] = "Mouse 5",
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x10] = "Shift", [0x11] = "Ctrl", [0x12] = "Alt",
        [0x13] = "Pause", [0x14] = "Caps Lock", [0x1B] = "Esc", [0x20] = "Space",
        [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
        [0x25] = "←", [0x26] = "↑", [0x27] = "→", [0x28] = "↓",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x5B] = "Left Windows", [0x5C] = "Right Windows", [0x5D] = "Menu",
        [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
        [0x90] = "Num Lock", [0x91] = "Scroll Lock",
        [0xA0] = "Left Shift", [0xA1] = "Right Shift", [0xA2] = "Left Ctrl", [0xA3] = "Right Ctrl",
        [0xA4] = "Left Alt", [0xA5] = "Right Alt",
        [0xBA] = ";", [0xBB] = "=", [0xBC] = ",", [0xBD] = "-", [0xBE] = ".", [0xBF] = "/", [0xC0] = "`",
        [0xDB] = "[", [0xDC] = "\\", [0xDD] = "]", [0xDE] = "'",
    };

    /// <summary>The controls in the order the editor and the F1 panel list them (spec 6.1).</summary>
    public static IReadOnlyList<(string Name, PadControl[] Controls)> Groups { get; } =
    [
        ("Left stick", [PadControl.LeftUp, PadControl.LeftDown, PadControl.LeftLeft, PadControl.LeftRight]),
        ("D-pad", [PadControl.DpadUp, PadControl.DpadDown, PadControl.DpadLeft, PadControl.DpadRight]),
        ("Buttons", [PadControl.Cross, PadControl.Circle, PadControl.Square, PadControl.Triangle]),
        ("Shoulders", [PadControl.L1, PadControl.R1, PadControl.L2, PadControl.R2]),
        ("Stick clicks", [PadControl.L3, PadControl.R3]),
        ("Menu", [PadControl.Options, PadControl.Share, PadControl.Touchpad]),
    ];

    public static string Of(ushort vk)
    {
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87)
            return $"F{vk - 0x6F}";
        if (vk is >= 0x60 and <= 0x69)
            return $"Num {vk - 0x60}";
        return Named.TryGetValue(vk, out var name) ? name : $"Key 0x{vk:X2}";
    }

    public static string Of(PadControl control) => control switch
    {
        PadControl.LeftUp => "Left stick up",
        PadControl.LeftDown => "Left stick down",
        PadControl.LeftLeft => "Left stick left",
        PadControl.LeftRight => "Left stick right",
        PadControl.DpadUp => "D-pad up",
        PadControl.DpadDown => "D-pad down",
        PadControl.DpadLeft => "D-pad left",
        PadControl.DpadRight => "D-pad right",
        _ => control.ToString(), // Cross, Circle, L1, Options, Touchpad...
    };

    /// <summary>The control's keys, e.g. "J / Left click", or "(none)".</summary>
    public static string Describe(KeyLayout layout, PadControl control)
    {
        var keys = layout.KeysFor(control);
        return keys.Count == 0 ? "(none)" : string.Join(" / ", keys.Select(Of));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~VirtualKeysTests|FullyQualifiedName~KeyLayoutTests|FullyQualifiedName~KeyNamesTests|FullyQualifiedName~InputMapperTests"`
Expected: PASS (the mapper tests still pass on the unchanged default layout).

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Input tests/CouchLink.Core.Tests/VirtualKeysTests.cs tests/CouchLink.Core.Tests/KeyLayoutTests.cs tests/CouchLink.Core.Tests/KeyNamesTests.cs
git commit -m "feat(core): editable key layout with one key per control, reserved keys and key names"
```

---

### Task 2: Control settings, Invert Y and the mapper following edits

**Files:**
- Create: `src/CouchLink.Core/Input/ControlSettings.cs`
- Modify: `src/CouchLink.Core/Input/MouseStick.cs`, `src/CouchLink.Core/Input/InputMapper.cs`
- Test: `tests/CouchLink.Core.Tests/ControlSettingsTests.cs`, `tests/CouchLink.Core.Tests/MouseStickTests.cs`, `tests/CouchLink.Core.Tests/InputMapperTests.cs`

**Interfaces:**
- Consumes: `KeyLayout`, `BindResult` (Task 1).
- Produces: `ControlSettings` with `MinStep = 1`, `MaxStep = 10`, `DefaultStep = 5`, `KeyLayout Layout`, `int SensitivityStep`, `double Sensitivity`, `bool InvertY`, `event Action? Changed`, `static double SensitivityFor(int step)`, `BindResult Bind(PadControl, ushort)`, `void SetSensitivityStep(int)`, `void SetInvertY(bool)`, `void ResetToDefault()`. `MouseStick.InvertY { get; set; }`. `InputMapper(ControlSettings settings)` and `InputMapper : IDisposable`.

- [ ] **Step 1: Write the failing tests**

Create `tests/CouchLink.Core.Tests/ControlSettingsTests.cs`:

```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class ControlSettingsTests
{
    [Fact]
    public void Step_5_is_the_original_sensitivity_and_each_step_is_1_3_times()
    {
        Assert.Equal(MouseStick.DefaultSensitivity, ControlSettings.SensitivityFor(5), 6);
        Assert.Equal(0.026, ControlSettings.SensitivityFor(6), 6);
        Assert.Equal(0.0070, ControlSettings.SensitivityFor(1), 4);
        Assert.Equal(0.0743, ControlSettings.SensitivityFor(10), 4);
    }

    [Fact]
    public void Steps_outside_1_to_10_are_clamped()
    {
        var settings = new ControlSettings();
        settings.SetSensitivityStep(0);
        Assert.Equal(1, settings.SensitivityStep);
        settings.SetSensitivityStep(11);
        Assert.Equal(10, settings.SensitivityStep);
    }

    [Fact]
    public void Every_edit_raises_Changed_once()
    {
        var settings = new ControlSettings();
        int changed = 0;
        settings.Changed += () => changed++;

        settings.Bind(PadControl.Cross, VirtualKeys.Space);
        settings.SetSensitivityStep(7);
        settings.SetInvertY(true);
        settings.ResetToDefault();

        Assert.Equal(4, changed);
    }

    [Fact]
    public void Refused_or_unchanged_edits_raise_nothing()
    {
        var settings = new ControlSettings();
        int changed = 0;
        settings.Changed += () => changed++;

        settings.Bind(PadControl.Cross, VirtualKeys.F1);
        settings.SetSensitivityStep(ControlSettings.DefaultStep);
        settings.SetInvertY(false);

        Assert.Equal(0, changed);
    }

    [Fact]
    public void Reset_restores_layout_sensitivity_and_invert()
    {
        var settings = new ControlSettings();
        settings.Bind(PadControl.Cross, VirtualKeys.Space);
        settings.SetSensitivityStep(9);
        settings.SetInvertY(true);

        settings.ResetToDefault();

        Assert.Equal([VirtualKeys.Letter('K')], settings.Layout.KeysFor(PadControl.Cross));
        Assert.Equal(ControlSettings.DefaultStep, settings.SensitivityStep);
        Assert.False(settings.InvertY);
    }
}
```

In `tests/CouchLink.Core.Tests/MouseStickTests.cs` add:

```csharp
    [Fact]
    public void InvertY_flips_vertical_movement()
    {
        var m = new MouseStick { InvertY = true };
        m.AddDelta(0, 25); // mouse toward you: stick up instead of down
        Assert.Equal(((byte)128, (byte)65), m.Update(0.001));
    }
```

In `tests/CouchLink.Core.Tests/InputMapperTests.cs` add:

```csharp
    [Fact]
    public void An_edit_counts_from_the_next_tick()
    {
        var settings = new ControlSettings();
        using var m = new InputMapper(settings);
        settings.Bind(PadControl.Cross, VirtualKeys.Space);
        m.KeyDown(VirtualKeys.Space);
        Assert.Equal(PadButtons.Cross, m.Tick(0.001).Buttons);
    }

    [Fact]
    public void An_edit_releases_held_keys()
    {
        var settings = new ControlSettings();
        using var m = new InputMapper(settings);
        m.KeyDown(K);
        m.MouseMove(40, 0);
        settings.SetInvertY(true);
        Assert.Equal(PadState.Neutral, m.Tick(0.001));
    }

    [Fact]
    public void A_control_without_keys_is_never_pressed()
    {
        var settings = new ControlSettings();
        using var m = new InputMapper(settings);
        settings.Bind(PadControl.Circle, K); // Cross has no key now
        m.KeyDown(K);
        Assert.Equal(PadButtons.Circle, m.Tick(0.001).Buttons);
    }

    [Fact]
    public void Sensitivity_and_invert_come_from_the_settings()
    {
        var settings = new ControlSettings();
        settings.SetSensitivityStep(10);
        settings.SetInvertY(true);
        using var m = new InputMapper(settings);
        m.MouseMove(0, 10); // 10 counts * 0.0743 = 0.743 of full deflection, upward
        Assert.Equal((byte)34, m.Tick(0.001).RY);
    }

    [Fact]
    public void After_Dispose_edits_no_longer_reach_the_mapper()
    {
        var settings = new ControlSettings();
        var m = new InputMapper(settings);
        m.Dispose();
        m.KeyDown(K);
        settings.SetInvertY(true); // would release K if still subscribed
        Assert.Equal(PadButtons.Cross, m.Tick(0.001).Buttons);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~ControlSettingsTests|FullyQualifiedName~MouseStickTests|FullyQualifiedName~InputMapperTests"`
Expected: build FAILS: `The type or namespace name 'ControlSettings' could not be found`.

- [ ] **Step 3: Implement**

Create `src/CouchLink.Core/Input/ControlSettings.cs`:

```csharp
namespace CouchLink.Core.Input;

/// <summary>
/// The player's controls for this run of the app: key layout, mouse sensitivity step and Invert Y.
/// In memory only (spec 6.7). Edit through this class so <see cref="Changed"/> is raised. Thread-safe:
/// the editor writes on the UI thread, the input loop and the F1 panel read on their own threads.
/// </summary>
public sealed class ControlSettings
{
    public const int MinStep = 1, MaxStep = 10, DefaultStep = 5;
    private const double StepFactor = 1.3;

    private int _step = DefaultStep;
    private volatile bool _invertY;

    public KeyLayout Layout { get; } = KeyLayout.CreateDefault();

    public int SensitivityStep => Volatile.Read(ref _step);

    /// <summary>Stick deflection per mouse count for the current step.</summary>
    public double Sensitivity => SensitivityFor(SensitivityStep);

    public bool InvertY => _invertY;

    /// <summary>Raised after every edit, on the thread that made it.</summary>
    public event Action? Changed;

    public static double SensitivityFor(int step) =>
        MouseStick.DefaultSensitivity * Math.Pow(StepFactor, Math.Clamp(step, MinStep, MaxStep) - DefaultStep);

    public BindResult Bind(PadControl control, ushort key)
    {
        var result = Layout.Bind(control, key);
        if (result.Bound)
            Changed?.Invoke();
        return result;
    }

    public void SetSensitivityStep(int step)
    {
        step = Math.Clamp(step, MinStep, MaxStep);
        if (Interlocked.Exchange(ref _step, step) != step)
            Changed?.Invoke();
    }

    public void SetInvertY(bool invert)
    {
        if (_invertY == invert)
            return;
        _invertY = invert;
        Changed?.Invoke();
    }

    public void ResetToDefault()
    {
        Layout.ResetToDefault();
        Volatile.Write(ref _step, DefaultStep);
        _invertY = false;
        Changed?.Invoke();
    }
}
```

In `src/CouchLink.Core/Input/MouseStick.cs`, after the `Sensitivity` property add:

```csharp
    /// <summary>Mouse toward you pushes the stick up instead of down.</summary>
    public bool InvertY { get; set; }
```

and in `AddDelta` change `_y += dy * Sensitivity;` to `_y += (InvertY ? -dy : dy) * Sensitivity;`.

In `src/CouchLink.Core/Input/InputMapper.cs`:
1. Change the class declaration to `public sealed class InputMapper : IDisposable` and its summary's last line to `/// Thread-safe: input events arrive on the UI thread, Tick runs on the send loop, edits come from the editor.`
2. Add a field `private readonly ControlSettings? _settings;` after `_gate`.
3. After the existing constructor add:

```csharp
    /// <summary>
    /// Follows the shared controls: an edit counts from the next tick, releases held keys (nothing
    /// stays pressed across a rebind) and applies the mouse settings. Dispose to stop following.
    /// </summary>
    public InputMapper(ControlSettings settings) : this(settings.Layout, new MouseStick())
    {
        _settings = settings;
        ApplyMouseSettings();
        settings.Changed += OnSettingsChanged;
    }

    private void OnSettingsChanged()
    {
        lock (_gate)
        {
            _held.Clear();
            _mouse.Reset();
            ApplyMouseSettings();
        }
    }

    private void ApplyMouseSettings()
    {
        _mouse.Sensitivity = _settings!.Sensitivity;
        _mouse.InvertY = _settings.InvertY;
    }

    public void Dispose()
    {
        if (_settings is not null)
            _settings.Changed -= OnSettingsChanged;
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~ControlSettingsTests|FullyQualifiedName~MouseStickTests|FullyQualifiedName~InputMapperTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Input tests/CouchLink.Core.Tests/ControlSettingsTests.cs tests/CouchLink.Core.Tests/MouseStickTests.cs tests/CouchLink.Core.Tests/InputMapperTests.cs
git commit -m "feat(core): in-memory control settings with sensitivity steps and Invert Y; the mapper follows edits"
```

---

### Task 3: The shortcut filter and the single-instance decision

**Files:**
- Create: `src/CouchLink.Core/Input/ShortcutFilter.cs`, `src/CouchLink.Core/Startup/InstanceDecision.cs`
- Test: `tests/CouchLink.Core.Tests/ShortcutFilterTests.cs`, `tests/CouchLink.Core.Tests/InstanceDecisionTests.cs`

**Interfaces:**
- Consumes: `VirtualKeys` (Task 1), `DevOptions` (existing).
- Produces: `ShortcutFilter.ShouldBlock(ushort vk, bool altDown, bool ctrlDown) -> bool`. `enum InstanceRole { Run, HandOff, Bypass }`; `InstanceDecision.ShouldCheck(DevOptions) -> bool`, `InstanceDecision.Decide(DevOptions, bool ownsMutex) -> InstanceRole` (namespace `CouchLink.Core.Startup`).

- [ ] **Step 1: Write the failing tests**

Create `tests/CouchLink.Core.Tests/ShortcutFilterTests.cs`:

```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class ShortcutFilterTests
{
    [Theory]
    [InlineData(VirtualKeys.LWin, false, false)]
    [InlineData(VirtualKeys.RWin, false, false)]
    [InlineData(VirtualKeys.LWin, true, true)]
    [InlineData(VirtualKeys.Tab, true, false)]     // Alt+Tab
    [InlineData(VirtualKeys.Escape, true, false)]  // Alt+Esc
    [InlineData(VirtualKeys.Escape, false, true)]  // Ctrl+Esc
    public void Windows_shortcuts_are_blocked(ushort vk, bool alt, bool ctrl)
    {
        Assert.True(ShortcutFilter.ShouldBlock(vk, alt, ctrl));
    }

    [Theory]
    [InlineData(VirtualKeys.Tab, false, false)]        // Touchpad by default
    [InlineData(VirtualKeys.Escape, false, false)]
    [InlineData(VirtualKeys.LMenu, true, false)]       // Alt alone
    [InlineData((ushort)0x73, true, false)]            // Alt+F4 still leaves
    [InlineData((ushort)0x51, true, true)]             // Ctrl+Alt+Q
    [InlineData((ushort)0x43, true, true)]             // Ctrl+Alt+C
    [InlineData((ushort)0x4B, false, false)]           // K
    public void Everything_else_passes(ushort vk, bool alt, bool ctrl)
    {
        Assert.False(ShortcutFilter.ShouldBlock(vk, alt, ctrl));
    }
}
```

Create `tests/CouchLink.Core.Tests/InstanceDecisionTests.cs`:

```csharp
using CouchLink.Core.Startup;

namespace CouchLink.Core.Tests;

public class InstanceDecisionTests
{
    private static readonly DevOptions Normal = DevOptions.Parse([]);
    private static readonly DevOptions Windowed = DevOptions.Parse(["--windowed-player"]);

    [Fact]
    public void The_first_copy_runs()
    {
        Assert.True(InstanceDecision.ShouldCheck(Normal));
        Assert.Equal(InstanceRole.Run, InstanceDecision.Decide(Normal, ownsMutex: true));
    }

    [Fact]
    public void A_second_copy_hands_off()
    {
        Assert.Equal(InstanceRole.HandOff, InstanceDecision.Decide(Normal, ownsMutex: false));
    }

    [Fact]
    public void A_windowed_player_copy_never_checks()
    {
        Assert.False(InstanceDecision.ShouldCheck(Windowed));
        Assert.Equal(InstanceRole.Bypass, InstanceDecision.Decide(Windowed, ownsMutex: false));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~ShortcutFilterTests|FullyQualifiedName~InstanceDecisionTests"`
Expected: build FAILS: `The name 'ShortcutFilter' does not exist in the current context`.

- [ ] **Step 3: Implement**

Create `src/CouchLink.Core/Input/ShortcutFilter.cs`:

```csharp
namespace CouchLink.Core.Input;

/// <summary>
/// The Windows shortcuts the player's keyboard hook swallows while the game is in front (spec 6.4):
/// both Windows keys (so every Win+ combination), Alt+Tab, Alt+Esc and Ctrl+Esc. Alt+F4 and
/// Ctrl+Alt+Q pass so there is always a way out.
/// </summary>
public static class ShortcutFilter
{
    public static bool ShouldBlock(ushort vk, bool altDown, bool ctrlDown) => vk switch
    {
        VirtualKeys.LWin or VirtualKeys.RWin => true,
        VirtualKeys.Tab => altDown,
        VirtualKeys.Escape => altDown || ctrlDown,
        _ => false,
    };
}
```

Create `src/CouchLink.Core/Startup/InstanceDecision.cs`:

```csharp
namespace CouchLink.Core.Startup;

public enum InstanceRole
{
    /// <summary>The only copy: start normally and listen for later launches.</summary>
    Run,

    /// <summary>Another copy runs: tell it to come forward, then exit.</summary>
    HandOff,

    /// <summary>--windowed-player: one-PC testing runs two copies, so don't check.</summary>
    Bypass,
}

/// <summary>One CouchLink per Windows sign-in, except with --windowed-player.</summary>
public static class InstanceDecision
{
    public static bool ShouldCheck(DevOptions options) => !options.WindowedPlayer;

    public static InstanceRole Decide(DevOptions options, bool ownsMutex) =>
        !ShouldCheck(options) ? InstanceRole.Bypass
        : ownsMutex ? InstanceRole.Run
        : InstanceRole.HandOff;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~ShortcutFilterTests|FullyQualifiedName~InstanceDecisionTests"`
Expected: PASS (16 tests).

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Input/ShortcutFilter.cs src/CouchLink.Core/Startup tests/CouchLink.Core.Tests/ShortcutFilterTests.cs tests/CouchLink.Core.Tests/InstanceDecisionTests.cs
git commit -m "feat(core): which Windows shortcuts to block while playing, and the single-instance decision"
```

---

### Task 4: The F1 panel text, the start hint and four overlay slots

**Files:**
- Modify: `src/CouchLink.Core/Video/OverlayText.cs`, `src/CouchLink.Video/IPlayerParts.cs`, `src/CouchLink.Video/FramePresenter.cs`, `src/CouchLink.Video/PlayerCore.cs`
- Test: `tests/CouchLink.Core.Tests/OverlayTextTests.cs`, `tests/CouchLink.Video.Tests/Fakes.cs`, `tests/CouchLink.Video.Tests/PlayerCoreTests.cs`

**Interfaces:**
- Consumes: `ControlSettings`, `KeyNames` (Tasks 1-2).
- Produces: `OverlayText.StartHint` (const), `OverlayText.Controls(ControlSettings) -> string`. `IFramePresenter.Present(DecodedPicture? picture, string? status, string? stats, string? controls, string? hint)`. `PlayerCore` last constructor parameter `Func<string?>? controlsText = null`, `bool ShowControls { get; set; }`, `static readonly TimeSpan StartHintFor` (5 s).

- [ ] **Step 1: Write the failing tests**

In `tests/CouchLink.Core.Tests/OverlayTextTests.cs` add (and `using CouchLink.Core.Input;` at the top):

```csharp
    [Fact]
    public void The_controls_panel_lists_every_control_with_its_current_keys()
    {
        var settings = new ControlSettings();
        settings.Bind(PadControl.Cross, VirtualKeys.Space);
        settings.SetSensitivityStep(7);

        var text = OverlayText.Controls(settings);

        foreach (var control in Enum.GetValues<PadControl>())
            Assert.Contains(KeyNames.Of(control), text);
        Assert.Contains($"{"Cross",-18}Space", text);
        Assert.Contains($"{"Square",-18}J / Left click", text);
        Assert.Contains("Mouse (sensitivity 7)", text);
        Assert.Contains("Ctrl+Alt+C", text);
    }
```

In `tests/CouchLink.Video.Tests/Fakes.cs` replace `FakePresenter` with:

```csharp
internal sealed class FakePresenter : IFramePresenter
{
    public List<(DecodedPicture? Picture, string? Status, string? Stats, string? Controls, string? Hint)> Shown { get; } = [];

    public void Present(DecodedPicture? picture, string? status, string? stats, string? controls, string? hint) =>
        Shown.Add((picture, status, stats, controls, hint));

    public void Dispose() { }
}
```

In `tests/CouchLink.Video.Tests/PlayerCoreTests.cs`:
1. Add a field `private string? _controls;`.
2. Change the end of `Core()` from `sessionStatus: () => _session);` to `sessionStatus: () => _session, controlsText: () => _controls);`.
3. Change the `ShownFrame` parameter type to `(DecodedPicture? Picture, string? Status, string? Stats, string? Controls, string? Hint) s`.
4. Add:

```csharp
    [Fact]
    public void The_start_hint_shows_for_5s_from_the_first_frame()
    {
        using var core = Core();
        core.Run();
        Assert.Null(_presenter.Shown[^1].Hint); // nothing shown yet: no hint

        core.Enqueue(F(1, keyframe: true));
        core.Run();
        Assert.Equal(OverlayText.StartHint, _presenter.Shown[^1].Hint);

        _time.Advance(PlayerCore.StartHintFor - TimeSpan.FromMilliseconds(1));
        core.Run();
        Assert.Equal(OverlayText.StartHint, _presenter.Shown[^1].Hint);

        _time.Advance(TimeSpan.FromMilliseconds(1));
        core.Run();
        Assert.Null(_presenter.Shown[^1].Hint);
    }

    [Fact]
    public void F1_hides_the_start_hint_for_good()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();

        core.ShowControls = true;
        core.Run();
        Assert.Null(_presenter.Shown[^1].Hint);

        core.ShowControls = false;
        core.Run();
        Assert.Null(_presenter.Shown[^1].Hint);
    }

    [Fact]
    public void The_controls_panel_shows_the_current_text_while_on()
    {
        using var core = Core();
        core.Enqueue(F(1, keyframe: true));
        core.Run();
        Assert.Null(_presenter.Shown[^1].Controls);

        _controls = "Cross  K";
        core.ShowControls = true;
        core.Run();
        Assert.Equal("Cross  K", _presenter.Shown[^1].Controls);

        _controls = "Cross  Space"; // an edit in the controls editor
        core.Run();                 // text changed: redraw at once
        Assert.Equal("Cross  Space", _presenter.Shown[^1].Controls);

        core.ShowControls = false;
        core.Run();
        Assert.Null(_presenter.Shown[^1].Controls);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Video.Tests --filter "FullyQualifiedName~PlayerCoreTests"`
Expected: build FAILS: `'FakePresenter' does not implement interface member 'IFramePresenter.Present(DecodedPicture?, string?, string?)'` (and `controlsText`, `StartHint`).

- [ ] **Step 3: Implement**

In `src/CouchLink.Core/Video/OverlayText.cs`:
1. Add `using CouchLink.Core.Input;` at the top.
2. After `Reconnecting` add:

```csharp
    public const string StartHint = "F1: controls · Ctrl+Alt+Q: leave";
```

3. Before `Ms` add:

```csharp
    /// <summary>The F1 panel: every control and its keys, grouped as in the editor, read when drawn.</summary>
    public static string Controls(ControlSettings settings)
    {
        var lines = new List<string> { "Controls (F1 hides, Ctrl+Alt+C changes keys)" };
        foreach (var (_, controls) in KeyNames.Groups)
        {
            lines.Add("");
            foreach (var control in controls)
                lines.Add($"{KeyNames.Of(control),-18}{KeyNames.Describe(settings.Layout, control)}");
        }
        lines.Add("");
        lines.Add($"{"Right stick",-18}Mouse (sensitivity {settings.SensitivityStep}{(settings.InvertY ? ", inverted" : "")})");
        return string.Join('\n', lines);
    }
```

In `src/CouchLink.Video/IPlayerParts.cs` replace the `IFramePresenter` interface with:

```csharp
/// <summary>
/// Shows a picture (or black, when null) with optional text: status centred, controls top-left,
/// stats top-right, hint bottom-left.
/// </summary>
public interface IFramePresenter : IDisposable
{
    void Present(DecodedPicture? picture, string? status, string? stats, string? controls, string? hint);
}
```

In `src/CouchLink.Video/FramePresenter.cs`:
1. Change `public void Present(DecodedPicture? picture, string? status, string? stats)` to `public void Present(DecodedPicture? picture, string? status, string? stats, string? controls, string? hint)`.
2. Change its text condition and call to:

```csharp
            if (picture is null || status is not null || stats is not null || controls is not null || hint is not null)
                DrawText(back, clear: picture is null, status, stats, controls, hint);
```

3. Replace `DrawText` and `Panel` with:

```csharp
    private void DrawText(D3D11Texture2D back, bool clear, string? status, string? stats, string? controls, string? hint)
    {
        using var surface = back.QueryInterface<IDXGISurface>();
        using var target = _d2d.CreateBitmapFromDxgiSurface(surface, new BitmapProperties1(
            new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied), 96, 96,
            BitmapOptions.Target | BitmapOptions.CannotDraw));
        _d2d.Target = target;
        _d2d.BeginDraw();
        if (clear)
            _d2d.Clear(new Color4(0f, 0f, 0f, 1f));
        if (controls is not null)
            Panel(controls, _statsFont, 24, 24, alignX: 0, alignY: 0);
        if (stats is not null)
            Panel(stats, _statsFont, _window.Width - 24, 24, alignX: 1, alignY: 0);
        if (hint is not null)
            Panel(hint, _statsFont, 24, _window.Height - 24, alignX: 0, alignY: 1);
        if (status is not null)
            Panel(status, _statusFont, _window.Width / 2f, _window.Height / 2f, alignX: 0.5f, alignY: 0.5f);
        _d2d.EndDraw();
        _d2d.Target = null;
    }

    /// <summary>Text on a dark panel; (x, y) is the point that <paramref name="alignX"/>/<paramref name="alignY"/> (0 = left/top, 1 = right/bottom) of the text sits on.</summary>
    private void Panel(string text, IDWriteTextFormat font, float x, float y, float alignX, float alignY)
    {
        using var layout = _dwrite.CreateTextLayout(text, font, _window.Width, _window.Height);
        var size = layout.Metrics;
        x -= size.Width * alignX;
        y -= size.Height * alignY;
        _d2d.FillRectangle(new Rect(x - 12, y - 8, size.Width + 24, size.Height + 16), _panel);
        _d2d.DrawTextLayout(new Vector2(x, y), layout, _text);
    }
```

In `src/CouchLink.Video/PlayerCore.cs`:
1. After `QuietAfter` add:

```csharp
    /// <summary>How long "F1: controls · Ctrl+Alt+Q: leave" shows after the first picture of a join.</summary>
    public static readonly TimeSpan StartHintFor = TimeSpan.FromSeconds(5);
```

2. Add fields after `_sessionStatus`: `private readonly Func<string?>? _controlsText;`, and after `_clientDelayTicks`: `private TimeSpan? _firstFrameAt;`, `private bool _showControls, _hintDismissed;`, `private string? _lastControls, _lastHint;`.
3. Add a last constructor parameter `Func<string?>? controlsText = null` and assign `_controlsText = controlsText;`.
4. Replace `public bool ShowStats { get; set; }` with:

```csharp
    public bool ShowStats { get; set; }

    /// <summary>The F1 panel. Turning it on also dismisses the start hint for good.</summary>
    public bool ShowControls
    {
        get => _showControls;
        set
        {
            _showControls = value;
            if (value)
                _hintDismissed = true;
        }
    }
```

5. In `Run`, replace from `if (newest is { } n)` through the `Present` call with:

```csharp
        if (newest is { } n)
        {
            _last = n.Picture;
            FramesShown++;
            _firstFrameAt ??= Now;
        }
        UpdateStats();
        string? statsText = ShowStats ? OverlayText.Stats(_sample, DecoderName, _audioLine?.Invoke()) : null;
        string? controls = ShowControls ? _controlsText?.Invoke() : null;
        string? hint = !_hintDismissed && _firstFrameAt is { } first && Now - first < StartHintFor ? OverlayText.StartHint : null;

        bool due = newest is not null
            || _lastPresent is null
            || Now - _lastPresent >= RedrawInterval
            || status != _lastStatus
            || statsText != _lastStatsText
            || controls != _lastControls
            || hint != _lastHint;
        if (!due)
            return;

        _presenter.Present(_last, status, statsText, controls, hint);
        _lastPresent = Now;
        _lastStatus = status;
        _lastStatsText = statsText;
        _lastControls = controls;
        _lastHint = hint;
```

(keep the `RecordClientDelay` lines that follow).

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Video.Tests` and `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~OverlayTextTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Video/OverlayText.cs src/CouchLink.Video/IPlayerParts.cs src/CouchLink.Video/FramePresenter.cs src/CouchLink.Video/PlayerCore.cs tests/CouchLink.Core.Tests/OverlayTextTests.cs tests/CouchLink.Video.Tests/Fakes.cs tests/CouchLink.Video.Tests/PlayerCoreTests.cs
git commit -m "feat(video): F1 controls panel and a 5 s start hint; stats move to the top-right"
```

---

### Task 5: Key blocking, mouse lock, F1 and Ctrl+Alt+C in the player window

Win32 window code has no unit tests in this repo; this task ends with a build and a manual check.

**Files:**
- Create: `src/CouchLink.Video/KeyboardBlocker.cs`
- Modify: `src/CouchLink.Video/PlayerWindow.cs`, `src/CouchLink.Video/VideoPlayer.cs`

**Interfaces:**
- Consumes: `ShortcutFilter` (Task 3), `PlayerCore.ShowControls` and `controlsText` (Task 4).
- Produces: `PlayerOptions(nint NearWindow = 0, bool Windowed = false, bool PreferHardware = true, bool LockInput = false)`. `PlayerWindow(nint nearWindow, bool windowed, bool lockInput = false, Action<string>? log = null)` with events `ControlsToggled`, `ControlsRequested` and `string? InputLockError`. `VideoPlayer` constructor gains last parameters `Func<string?>? controlsText = null, Action? controlsRequested = null` and a property `string? InputLockError`.

- [ ] **Step 1: The keyboard blocker**

Create `src/CouchLink.Video/KeyboardBlocker.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CouchLink.Core.Input;

namespace CouchLink.Video;

/// <summary>
/// A low-level keyboard hook that swallows what <see cref="ShortcutFilter"/> says (the Windows keys,
/// Alt+Tab, Alt+Esc, Ctrl+Esc). Install and uninstall on the player thread: it pumps messages, so the
/// callback runs there and never waits on the WPF UI thread. The callback only checks a few keys, well
/// inside Windows' limit for slow hooks, and never throws.
/// </summary>
public sealed unsafe partial class KeyboardBlocker : IDisposable
{
    private const int WH_KEYBOARD_LL = 13, HC_ACTION = 0, VK_CONTROL = 0x11;
    private const uint LLKHF_ALTDOWN = 0x20;

    private nint _hook;

    public bool IsInstalled => _hook != 0;

    public bool Install(out string? error)
    {
        error = null;
        if (_hook != 0)
            return true;
        _hook = SetWindowsHookExW(WH_KEYBOARD_LL,
            (nint)(delegate* unmanaged[Stdcall]<int, nint, nint, nint>)&HookProc, GetModuleHandleW(null), 0);
        if (_hook != 0)
            return true;
        error = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
        return false;
    }

    public void Uninstall()
    {
        if (_hook == 0)
            return;
        UnhookWindowsHookEx(_hook);
        _hook = 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint HookProc(int code, nint wParam, nint lParam)
    {
        try
        {
            if (code == HC_ACTION)
            {
                var info = (KbdLlHookStruct*)lParam;
                bool alt = (info->Flags & LLKHF_ALTDOWN) != 0;
                bool ctrl = GetAsyncKeyState(VK_CONTROL) < 0;
                if (ShortcutFilter.ShouldBlock((ushort)info->VkCode, alt, ctrl))
                    return 1;
            }
        }
        catch (Exception)
        {
            // A hook must never throw into Windows; let the key through.
        }
        return CallNextHookEx(0, code, wParam, lParam);
    }

    public void Dispose() => Uninstall();

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode, ScanCode, Flags, Time;
        public nuint ExtraInfo;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetWindowsHookExW(int idHook, nint proc, nint module, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int key);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetModuleHandleW(string? name);
}
```

- [ ] **Step 2: The player window**

In `src/CouchLink.Video/PlayerWindow.cs`:
1. Summary: replace `F2 toggles stats; Ctrl+Alt+Q, Alt+F4 and the close` / `button ask to leave.` with `F1 toggles the controls panel, F2 the stats, Ctrl+Alt+C asks for the controls editor; Ctrl+Alt+Q, Alt+F4 and the close button ask to leave. With lockInput, while it is active it blocks the Windows shortcuts (<see cref="KeyboardBlocker"/>) and keeps the pointer inside it.`
2. Constants: add `WM_ACTIVATE = 0x0006` to the `uint WM_*` list; change the `int VK_*` line to `private const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_F1 = 0x70, VK_F2 = 0x71, VK_Q = 0x51, VK_C = 0x43, WA_INACTIVE = 0;`.
3. Fields after `_error`: 

```csharp
    private readonly bool _lockInput;
    private readonly Action<string>? _log;
    private readonly KeyboardBlocker _blocker = new();
    private bool _inputLocked, _clipFailedLogged;
```

4. Constructor signature: `public PlayerWindow(nint nearWindow, bool windowed, bool lockInput = false, Action<string>? log = null)`; first lines `_lockInput = lockInput && !windowed;` and `_log = log;`. At the end of the constructor, after `SetForegroundWindow(Handle);` add:

```csharp
        // Creation activated the window before it was registered above, so that WM_ACTIVATE was missed.
        if (_lockInput && GetForegroundWindow() == Handle)
            LockInput();
```

5. Add after `Resized`:

```csharp
    public event Action? ControlsToggled;
    public event Action? ControlsRequested;

    /// <summary>Why the Windows shortcuts can't be blocked, or null. Set on the player thread, read anywhere.</summary>
    public string? InputLockError { get; private set; }
```

6. In `OnMessage`, in the key case, before the F2 check add:

```csharp
                if (wParam == VK_F1 && !repeat)
                {
                    ControlsToggled?.Invoke();
                    return 0;
                }
```

and after the Ctrl+Alt+Q block add:

```csharp
                if (wParam == VK_C && GetKeyState(VK_CONTROL) < 0 && GetKeyState(VK_MENU) < 0)
                {
                    ControlsRequested?.Invoke();
                    return 0;
                }
```

7. Add a case before `WM_SETCURSOR`:

```csharp
            case WM_ACTIVATE when _lockInput:
                if ((wParam & 0xFFFF) != WA_INACTIVE)
                    LockInput();
                else
                    UnlockInput();
                return null; // DefWindowProc still sets the focus
```

8. In the `WM_SIZE` case, after `Height = ...;` add `if (_inputLocked) ClipToWindow();`.
9. Add methods before `WndProc`:

```csharp
    private void LockInput()
    {
        _inputLocked = true;
        ClipToWindow();
        if (!_blocker.Install(out var error) && InputLockError is null)
        {
            InputLockError = $"Key blocking unavailable ({error})";
            _log?.Invoke($"Could not block the Windows shortcuts: {error}");
        }
    }

    private void UnlockInput()
    {
        _inputLocked = false;
        _blocker.Uninstall();
        ClipCursor(null);
    }

    private void ClipToWindow()
    {
        GetWindowRect(Handle, out var r);
        if (!ClipCursor(&r) && !_clipFailedLogged)
        {
            _clipFailedLogged = true;
            _log?.Invoke($"Could not keep the pointer in the player (error {Marshal.GetLastPInvokeError()})");
        }
    }
```

10. `Dispose`: first line `UnlockInput();`.
11. Add imports next to the others:

```csharp
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClipCursor(Rect* rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();
```

- [ ] **Step 3: The video player**

In `src/CouchLink.Video/VideoPlayer.cs`:
1. `PlayerOptions`: `public readonly record struct PlayerOptions(nint NearWindow = 0, bool Windowed = false, bool PreferHardware = true, bool LockInput = false);`
2. Summary: add `<c>controlsText</c> is the F1 panel's text; <c>controlsRequested</c> runs on the player thread on Ctrl+Alt+C (post it to the UI thread). With <see cref="PlayerOptions.LockInput"/> the player blocks the Windows shortcuts and keeps the pointer while it is in front.`
3. Constructor: add last parameters `Func<string?>? controlsText = null, Action? controlsRequested = null`, and pass them to `Run(options, stats, decodeFailed, closeRequested, log, audioLine, sessionStatus, controlsText, controlsRequested)`. Give `Run` the same two last parameters.
4. Add a field `private PlayerWindow? _window;` and a property `public string? InputLockError => _window?.InputLockError;`.
5. In `Run`: `window = new PlayerWindow(options.NearWindow, options.Windowed, options.LockInput, log);`; pass `controlsText` as the last `PlayerCore` argument (`..., audioLine, sessionStatus, controlsText);`); after `window.StatsToggled += ...` add:

```csharp
            window.ControlsToggled += () => c.ShowControls = !c.ShowControls;
            if (controlsRequested is not null)
                window.ControlsRequested += controlsRequested;
```

and next to `_core = core;` add `_window = window;`.

- [ ] **Step 4: Build and test**

Run: `dotnet build -c Release`
Expected: `0 Warning(s) 0 Error(s)`.
Run: `dotnet test tests/CouchLink.Video.Tests -c Release --no-build`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Video/KeyboardBlocker.cs src/CouchLink.Video/PlayerWindow.cs src/CouchLink.Video/VideoPlayer.cs
git commit -m "feat(video): the fullscreen player blocks Windows shortcuts and keeps the pointer; F1 and Ctrl+Alt+C"
```

---

### Task 6: The controls editor and the client wiring

**Files:**
- Create: `src/CouchLink.App/ControlsWindow.cs`
- Modify: `src/CouchLink.App/AppServices.cs`, `src/CouchLink.App/ClientPlay.cs`, `src/CouchLink.App/ClientStreams.cs`, `src/CouchLink.App/ClientVideoService.cs`, `src/CouchLink.App/Input/RawInputSource.cs`, `src/CouchLink.App/Input/NativeMethods.cs`, `src/CouchLink.App/Views/StartView.xaml`, `StartView.xaml.cs`, `SessionView.xaml`, `SessionView.xaml.cs`, `src/CouchLink.App/MainWindow.xaml.cs`, `src/CouchLink.App/Diagnostics/CrashHandler.cs`

**Interfaces:**
- Consumes: `ControlSettings`, `KeyNames` (Tasks 1-2), `OverlayText.Controls` (Task 4), `PlayerOptions.LockInput`, `VideoPlayer(..., controlsText, controlsRequested)`, `InputLockError` (Task 5).
- Produces: `AppServices.Controls`; `ControlsWindow.Open(Window? owner, bool overGame = false, Action? closed = null)`; `ClientPlay.TryStart(Window window, IPAddress host, byte slot, Action leave, Func<string?> sessionStatus, Action openControls, out ClientPlay? play, out string? error)` and `nint ClientPlay.PlayerWindow`; `StartView.ControlsClicked`, `SessionView.ControlsClicked`; `NativeMethods.ClipCursor(nint)`, `SetForegroundWindow(nint)`, `AllowSetForegroundWindow(int)`.

- [ ] **Step 1: Native methods, mouse side buttons, shared settings**

In `src/CouchLink.App/Input/NativeMethods.cs` add after the `RI_MOUSE_MIDDLE` line:

```csharp
    public const ushort RI_MOUSE_BUTTON_4_DOWN = 0x0040, RI_MOUSE_BUTTON_4_UP = 0x0080;
    public const ushort RI_MOUSE_BUTTON_5_DOWN = 0x0100, RI_MOUSE_BUTTON_5_UP = 0x0200;
    public const int ASFW_ANY = -1;
```

and after `GetForegroundWindow`:

```csharp
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(int processId);

    /// <summary>With 0: lets the pointer go anywhere again.</summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClipCursor(IntPtr rect);
```

In `src/CouchLink.App/Input/RawInputSource.cs`, after the middle-button line in `OnMouse` add:

```csharp
        Button(m.ButtonFlags, RI_MOUSE_BUTTON_4_DOWN, RI_MOUSE_BUTTON_4_UP, VirtualKeys.XButton1);
        Button(m.ButtonFlags, RI_MOUSE_BUTTON_5_DOWN, RI_MOUSE_BUTTON_5_UP, VirtualKeys.XButton2);
```

In `src/CouchLink.App/AppServices.cs` add `using CouchLink.Core.Input;` and:

```csharp
    /// <summary>The player's controls for this run: shared by the input mapper, the F1 panel and the editor.</summary>
    public static ControlSettings Controls { get; } = new();
```

In `src/CouchLink.App/Diagnostics/CrashHandler.cs`, at the top of `Handle`'s first `try` block (before `AppServices.Log.Write`), add:

```csharp
            Input.NativeMethods.ClipCursor(IntPtr.Zero); // never leave the pointer trapped in a dead player
```

- [ ] **Step 2: Pass the controls through to the player**

In `src/CouchLink.App/ClientVideoService.cs`:
1. Add parameters `Func<string?> controlsText, Action openControls` after `sessionStatus` to both the private constructor and `TryStart`, and pass them through.
2. In the constructor, change the player construction's last arguments from `..., audioLine, sessionStatus);` to `..., audioLine, sessionStatus, controlsText, openControls);`.
3. In `Describe`, before `(_save is null ? ...)` add `(_player.InputLockError is { } lockError ? $"\n{lockError}" : "") +`.

In `src/CouchLink.App/ClientStreams.cs`, add `Func<string?> controlsText, Action openControls` after `Func<string?> sessionStatus` in `TryStart`, and pass them to `ClientVideoService.TryStart(sender, options, savePath, leave, audio.Describe, sessionStatus, controlsText, openControls, out var video, out error)`.

In `src/CouchLink.App/ClientPlay.cs`:
1. Add `using CouchLink.Core.Video;`.
2. Constructor: `_mapper = new InputMapper(AppServices.Controls);`.
3. `TryStart`: add `Action openControls` after `Func<string?> sessionStatus`; options become `new PlayerOptions(new WindowInteropHelper(window).Handle, AppServices.Options.WindowedPlayer, LockInput: !AppServices.Options.WindowedPlayer)`; the streams call becomes `ClientStreams.TryStart(host, sender, options, AppServices.Options.SaveVideoPath, leave, sessionStatus, () => OverlayText.Controls(AppServices.Controls), openControls, out var streams, out error)`.
4. Add `public nint PlayerWindow => _streams.PlayerWindow;`.
5. `Dispose`: after `_input.Dispose();` add `_mapper.Dispose();   // stops following controls edits`.

- [ ] **Step 3: The editor**

Create `src/CouchLink.App/ControlsWindow.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CouchLink.Core.Input;

namespace CouchLink.App;

/// <summary>
/// ⚙ Controls: every DS4 control with its keys. Click a row, then press a key or mouse button to bind
/// it (Esc cancels). Mouse sensitivity, Invert Y and Reset to default. Edits go straight into
/// <see cref="AppServices.Controls"/>, so they count mid-game. One window at a time.
/// </summary>
internal sealed class ControlsWindow : Window
{
    private const string Listening = "Press a key or mouse button... (Esc cancels)";
    private static ControlsWindow? _open;

    private readonly ControlSettings _settings = AppServices.Controls;
    private readonly Dictionary<PadControl, Button> _rows = [];
    private readonly TextBlock _message = new() { Foreground = Brushes.Gray, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Slider _sensitivity;
    private readonly CheckBox _invertY;
    private PadControl? _listening;

    /// <summary>Shows the editor, or brings the open one forward. <paramref name="overGame"/>: opened with Ctrl+Alt+C over the fullscreen player.</summary>
    public static void Open(Window? owner, bool overGame = false, Action? closed = null)
    {
        if (_open is { } existing)
        {
            existing.Activate();
            return;
        }
        var window = new ControlsWindow { Topmost = overGame };
        if (owner is { IsVisible: true, WindowState: not WindowState.Minimized } && !overGame)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        window.Closed += (_, _) =>
        {
            _open = null;
            closed?.Invoke();
        };
        _open = window;
        window.Show();
        window.Activate();
    }

    private ControlsWindow()
    {
        Title = "Controls";
        Width = 440;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var list = new StackPanel();
        foreach (var (group, controls) in KeyNames.Groups)
        {
            list.Children.Add(new TextBlock { Text = group, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
            foreach (var control in controls)
            {
                var row = new Button
                {
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(8, 4, 8, 4),
                    Margin = new Thickness(0, 1, 0, 1),
                };
                var c = control;
                row.Click += (_, _) => Listen(c);
                _rows[control] = row;
                list.Children.Add(row);
            }
        }
        list.Children.Add(new TextBlock { Text = "Right stick", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        list.Children.Add(new Button
        {
            Content = Row("Right stick", "Mouse"),
            IsEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 4, 8, 4),
        });

        _sensitivity = new Slider
        {
            Minimum = ControlSettings.MinStep,
            Maximum = ControlSettings.MaxStep,
            TickFrequency = 1,
            IsSnapToTickEnabled = true,
            TickPlacement = TickPlacement.BottomRight,
            Value = _settings.SensitivityStep,
        };
        _sensitivity.ValueChanged += (_, e) => _settings.SetSensitivityStep((int)Math.Round(e.NewValue));
        _invertY = new CheckBox { Content = "Invert Y (mouse toward you pushes the stick up)", Margin = new Thickness(0, 8, 0, 0) };
        _invertY.Click += (_, _) => _settings.SetInvertY(_invertY.IsChecked == true);
        var reset = new Button { Content = "Reset to default", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) =>
        {
            _listening = null;
            _settings.ResetToDefault();
            ShowMessage("Back to the default controls.");
        };

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Click a control, then press the key or mouse button for it.", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(new ScrollViewer { Content = list, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        root.Children.Add(_message);
        root.Children.Add(new TextBlock { Text = "Mouse sensitivity (right stick)", Margin = new Thickness(0, 12, 0, 4) });
        root.Children.Add(_sensitivity);
        root.Children.Add(_invertY);
        root.Children.Add(reset);
        Content = root;

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += OnPreviewMouseDown;
        _messageTimer.Tick += (_, _) =>
        {
            _messageTimer.Stop();
            _message.Text = "";
        };
        _settings.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            _settings.Changed -= OnSettingsChanged;
            _messageTimer.Stop();
        };
        Refresh();
    }

    private void OnSettingsChanged() => Dispatcher.InvokeAsync(Refresh);

    private void Refresh()
    {
        foreach (var (control, row) in _rows)
            row.Content = Row(KeyNames.Of(control), _listening == control ? Listening : KeyNames.Describe(_settings.Layout, control));
        _sensitivity.Value = _settings.SensitivityStep;
        _invertY.IsChecked = _settings.InvertY;
    }

    private static DockPanel Row(string name, string keys)
    {
        var panel = new DockPanel();
        var keysText = new TextBlock { Text = keys, Foreground = Brushes.DimGray };
        DockPanel.SetDock(keysText, Dock.Right);
        panel.Children.Add(keysText);
        panel.Children.Add(new TextBlock { Text = name });
        return panel;
    }

    private void Listen(PadControl control)
    {
        _listening = control;
        Refresh();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_listening is not { } control)
            return;
        e.Handled = true; // Space, Enter and Tab must not press a button here
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            _listening = null;
            Refresh();
            return;
        }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk is > 0 and <= 0xFF)
            Bind(control, (ushort)vk);
    }

    private void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_listening is not { } control)
            return;
        e.Handled = true;
        Bind(control, e.ChangedButton switch
        {
            MouseButton.Left => VirtualKeys.LButton,
            MouseButton.Right => VirtualKeys.RButton,
            MouseButton.Middle => VirtualKeys.MButton,
            MouseButton.XButton1 => VirtualKeys.XButton1,
            _ => VirtualKeys.XButton2,
        });
    }

    private void Bind(PadControl control, ushort key)
    {
        var result = _settings.Bind(control, key);
        if (!result.Bound)
        {
            ShowMessage($"{KeyNames.Of(key)} is reserved. Press another key.");
            return; // keep listening
        }
        _listening = null;
        Refresh();
        if (result.MovedFrom is { } from)
            ShowMessage($"{KeyNames.Of(key)} moved from {KeyNames.Of(from)}.");
    }

    private void ShowMessage(string text)
    {
        _message.Text = text;
        _messageTimer.Stop();
        _messageTimer.Start();
    }
}
```

- [ ] **Step 4: The buttons and the main window**

`src/CouchLink.App/Views/StartView.xaml`: replace the `ControlsButton` element with `<Button x:Name="ControlsButton" Content="⚙ Controls" Padding="12,4"/>`.

`src/CouchLink.App/Views/StartView.xaml.cs`: add `ControlsButton.Click += (_, _) => ControlsClicked?.Invoke();` in the constructor and `public event Action? ControlsClicked;`.

`src/CouchLink.App/Views/SessionView.xaml`: after the `LeaveButton` add

```xml
        <Button x:Name="ControlsButton" Content="⚙ Controls" Height="32" Margin="0,8,0,0" Focusable="False"
                HorizontalAlignment="Left" Padding="12,0"/>
```

`src/CouchLink.App/Views/SessionView.xaml.cs`: add `ControlsButton.Click += (_, _) => ControlsClicked?.Invoke();` and `public event Action? ControlsClicked;`; extend the Playing hint to `"Ctrl+Alt+Q leaves. F1 shows the keys, Ctrl+Alt+C changes them. F2 shows stats."`.

`src/CouchLink.App/MainWindow.xaml.cs`:
1. Add `using System.Windows.Interop;` and `using CouchLink.App.Input;`.
2. In `ShowStart`, add `start.ControlsClicked += () => ControlsWindow.Open(this);`.
3. In `Join`, after `_sessionView.LeaveClicked += LeaveSession;` add `_sessionView.ControlsClicked += () => ControlsWindow.Open(this);`.
4. In `IClientUi.StartPlaying`, the `ClientPlay.TryStart` call gains `() => Dispatcher.InvokeAsync(OpenControlsOverGame)` after `() => session.PlayerStatus`.
5. Add:

```csharp
    /// <summary>Ctrl+Alt+C in the player: the editor on top of the game, and back to the game when it closes.</summary>
    private void OpenControlsOverGame()
    {
        if (_play is null || _closed)
            return;
        ControlsWindow.Open(this, overGame: true, closed: ReturnToGame);
    }

    private void ReturnToGame()
    {
        if (_play is { PlayerWindow: not 0 } play)
            NativeMethods.SetForegroundWindow(play.PlayerWindow);
    }
```

- [ ] **Step 5: Build and test**

Run: `dotnet build -c Release`
Expected: `0 Warning(s) 0 Error(s)`.
Run: `dotnet test -c Release --no-build`
Expected: all PASS.

- [ ] **Step 6: Manual check (one PC)**

1. `dotnet run --project src/CouchLink.App -c Release`. ⚙ Controls is enabled on Start; the editor lists 21 rows in 6 groups plus "Right stick · Mouse".
2. Click Cross, press Space: Cross shows Space. Click Circle, press Space: "Space moved from Cross."; Cross shows "(none)". Click Triangle, press F1: "F1 is reserved..." and it keeps listening; press Esc: back to "I".
3. Click L1, press the mouse side button: "Mouse 4". Drag the slider, tick Invert Y, Reset to default: everything back.

- [ ] **Step 7: Commit**

```bash
git add -A src/CouchLink.App
git commit -m "feat(app): controls editor on the Start and session screens and over the game; side mouse buttons"
```

---

### Task 7: Single instance

**Files:**
- Create: `src/CouchLink.App/SingleInstance.cs`
- Modify: `src/CouchLink.App/App.xaml.cs`, `src/CouchLink.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `InstanceDecision`, `InstanceRole` (Task 3), `NativeMethods.AllowSetForegroundWindow/SetForegroundWindow`, `ClientPlay.PlayerWindow` (Task 6).
- Produces: `SingleInstance.TryClaim(DevOptions options, out SingleInstance? instance) -> bool` (false: exit now), `void OnActivate(Action activate)`, `Dispose()`; `MainWindow.BringToFront()`.

- [ ] **Step 1: Single instance**

Create `src/CouchLink.App/SingleInstance.cs`:

```csharp
using CouchLink.App.Input;
using CouchLink.Core;
using CouchLink.Core.Startup;

namespace CouchLink.App;

/// <summary>
/// One CouchLink per Windows sign-in. A second launch tells the first to come forward and exits;
/// with --windowed-player it is skipped so one-PC testing can run two copies. A crashed copy's
/// mutex is released by Windows, so it never blocks the next start.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\CouchLink.SingleInstance";
    private const string EventName = @"Local\CouchLink.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activate;
    private RegisteredWaitHandle? _wait;

    private SingleInstance(Mutex mutex, EventWaitHandle activate)
    {
        _mutex = mutex;
        _activate = activate;
    }

    /// <summary>False: another copy was told to come forward, so exit. <paramref name="instance"/> is null when not checked.</summary>
    public static bool TryClaim(DevOptions options, out SingleInstance? instance)
    {
        instance = null;
        if (!InstanceDecision.ShouldCheck(options))
            return true;
        Mutex mutex;
        EventWaitHandle activate;
        try
        {
            mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
            activate = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
            if (InstanceDecision.Decide(options, created) == InstanceRole.Run)
            {
                instance = new SingleInstance(mutex, activate);
                return true;
            }
        }
        catch (Exception e) when (e is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            AppServices.Log.Write($"Single-instance check failed ({e.Message}); starting anyway");
            return true;
        }

        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY); // a fresh launch may hand over focus
        activate.Set();
        activate.Dispose();
        mutex.Dispose();
        return false;
    }

    /// <summary>Runs <paramref name="activate"/> on a pool thread whenever a later launch signals.</summary>
    public void OnActivate(Action activate) =>
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) =>
        {
            try
            {
                activate();
            }
            catch (Exception e)
            {
                AppServices.Log.Write($"Bringing CouchLink forward failed: {e}");
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);

    /// <summary>On the thread that claimed it (the UI thread).</summary>
    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
```

`IOException` needs `using System.IO;` at the top.

- [ ] **Step 2: Startup and bringing the window forward**

In `src/CouchLink.App/App.xaml.cs`:
1. Add a field `private SingleInstance? _instance;`.
2. After `AppServices.Options = DevOptions.Parse(e.Args);` add:

```csharp
        if (!SingleInstance.TryClaim(AppServices.Options, out _instance))
        {
            AppServices.Log.Write("CouchLink is already running; asked it to come forward");
            Shutdown(0);
            return;
        }
```

3. After `window.Show();` add `_instance?.OnActivate(() => window.Dispatcher.InvokeAsync(window.BringToFront));`.
4. In `OnExit`, before `base.OnExit(e);` add `_instance?.Dispose();`.

In `src/CouchLink.App/MainWindow.xaml.cs` add:

```csharp
    /// <summary>A later launch asked for this copy: the game if one is playing, otherwise this window.</summary>
    internal void BringToFront()
    {
        if (_play is { PlayerWindow: not 0 } play)
        {
            NativeMethods.SetForegroundWindow(play.PlayerWindow);
            return;
        }
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Show();
        Activate();
    }
```

- [ ] **Step 3: Build and test**

Run: `dotnet build -c Release`
Expected: `0 Warning(s) 0 Error(s)`.
Run: `dotnet test -c Release --no-build`
Expected: all PASS.

- [ ] **Step 4: Manual check (one PC)**

1. Start `src/CouchLink.App/bin/Release/net10.0-windows/CouchLink.exe`, minimize it, start it again: the first window comes back; Task Manager shows one CouchLink; the log has "already running".
2. With one running, start a second with `--windowed-player`: it opens.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.App/SingleInstance.cs src/CouchLink.App/App.xaml.cs src/CouchLink.App/MainWindow.xaml.cs
git commit -m "feat(app): launching again brings the running copy forward"
```

---

### Task 8: Docs: main design, gate checklist, README, roadmap

**Files:**
- Modify: `docs/superpowers/specs/2026-10-05-couchlink-design.md`, `docs/superpowers/specs/2026-10-08-couchlink-playing-screen-controls-design.md`, `docs/gate-results.md`, `README.md`

- [ ] **Step 1: Main design**

In `docs/superpowers/specs/2026-10-05-couchlink-design.md`:
1. 6.3: after "scaled by a **sensitivity** slider." add " An **Invert Y** checkbox flips the vertical."
2. 6.4: replace "**Windows key and Alt+Tab are blocked** while playing (low-level keyboard hook); **Ctrl+Alt+Q** always exits." with "While the fullscreen player is in front, a low-level keyboard hook blocks **both Windows keys, Alt+Tab, Alt+Esc and Ctrl+Esc**, and the pointer is kept inside the player. **Ctrl+Alt+Q** always exits and Alt+F4 still leaves; **Ctrl+Alt+C** opens the controls editor over the game. Nothing is blocked with `--windowed-player`."
3. 6.7: replace the paragraph with "⚙ Controls (Start screen, session screen, or Ctrl+Alt+C in the game) lists every DS4 control with its keys: click a control, press a key or mouse button to bind it. One key drives one control; binding a key takes it off any other. Esc, F1, F2 and the Windows keys are reserved. **Reset to default**, the mouse sensitivity slider (steps 1-10) and Invert Y. Edits apply at once and are kept in memory only, reset when CouchLink closes, by design. Details: [playing screen & controls design](2026-10-08-couchlink-playing-screen-controls-design.md)."
4. Section 7 table: replace `| Ports in use (CouchLink already running) | Focus the running instance |` with `| CouchLink launched again | The running copy comes forward; the new one exits (not with --windowed-player) |`.

In the Plan 8 spec, set `Status: Approved` and add `Ctrl+Alt+C` to 3.3's pass list and a line in 2.5: "Also opened over the game with Ctrl+Alt+C (Topmost, no owner); closing it returns to the game."

- [ ] **Step 2: Gate checklist**

Append to `docs/gate-results.md`:

```markdown

## Playing screen, controls & single instance (Plan 8)

Date: <fill in>
Host / client PCs: <fill in>

| Check | Result |
|---|---|
| Win key, Alt+Tab, Alt+Esc, Ctrl+Esc do nothing from the first second of play; all work again after Ctrl+Alt+Q | <pass/fail> |
| Alt+F4 in the player leaves the session | <pass/fail> |
| Two monitors: the pointer can't leave the game; it is free again after leaving | <pass/fail> |
| The start hint shows for about 5 s; F1 shows every control and its keys; F1 and F2 can be on together | <pass/fail> |
| Ctrl+Alt+C opens the editor over the game; rebind Cross to Space; closing it returns to the game, F1 shows Space and Space presses Cross in 2K | <pass/fail> |
| Mouse side button bound to L1 works in 2K | <pass/fail> |
| Sensitivity 1 and 10 feel clearly different; Invert Y flips the shot stick | <pass/fail> |
| Launch CouchLink again on the Start screen, while hosting and while playing: the running copy comes forward, no second copy in Task Manager | <pass/fail> |
| A `--windowed-player` second copy still starts and joins | <pass/fail> |
```

- [ ] **Step 3: README status**

In `README.md` replace `Playing-screen polish (F1 help, key blocking, mouse lock), the controls editor and the installer` / `are next.` wording with: `The fullscreen player blocks the Windows key and Alt+Tab, F1 shows the keys and they can be rebound mid-game. The installer is next.` (keep the blockquote `> ` prefixes and the links on the last line).

- [ ] **Step 4: Final check**

Run: `dotnet build -c Release` then `dotnet test -c Release --no-build`
Expected: `0 Warning(s) 0 Error(s)`, all tests PASS.

- [ ] **Step 5: Commit**

```bash
git add docs README.md
git commit -m "docs: playing screen, controls and single instance in the main design, the Plan 8 gate checklist and README status"
```
