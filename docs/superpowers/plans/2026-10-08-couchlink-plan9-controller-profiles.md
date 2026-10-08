# CouchLink Plan 9: Controller Profiles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A player on a joining PC loads a controller profile (a JSON file with a game's key layout, mouse settings and action labels) from the controls editor, and an owner saves the current layout as one.

**Architecture:** Everything except the window is plain, tested code in `CouchLink.Core/Input`: `ProfileKeys` (fixed key IDs), `ControlProfile` + `ProfileFile` (JSON read/write, strings only), `ProfileStore` (the folder and files on disk), and new members on `ControlSettings` (`Apply`, labels, profile name). `KeyLayout` keeps its bindings as one immutable snapshot that `InputMapper` reads once per tick, so a profile swap never mixes two layouts. `ControlsWindow` gets a profile row, label boxes and a small `SaveProfileDialog`.

**Tech Stack:** C# / .NET 10, `System.Text.Json` (`JsonDocument`, `Utf8JsonWriter`), WPF (`ComboBox`, `Microsoft.Win32.OpenFileDialog`/`SaveFileDialog`), xUnit 2.9.3.

**Spec:** `docs/superpowers/specs/2026-10-08-couchlink-controller-profiles-design.md` (all of it). Builds on section 2 of `docs/superpowers/specs/2026-10-08-couchlink-playing-screen-controls-design.md`.

**Branch:** `feat/controller-profiles`, created from `docs/controller-profiles-spec` (holds the spec and this plan). Issue: #56.

**One deviation from the spec:** spec 5.2 puts `ProfileStore` in `CouchLink.App`. It moves to `CouchLink.Core/Input` because it only uses `System.IO` (as `FileLog` already does in Core), and that lets the folder listing, size cap and safe write be unit-tested. The App only constructs it.

## Global Constraints

- C#, .NET 10. `CouchLink.Core` stays `net10.0` with no Windows-only APIs. `TreatWarningsAsErrors` everywhere (`Directory.Build.props`).
- Commits are signed. Conventional Commits. **No Claude attribution trailers** in commit messages or PR text. Commit bodies end with `Refs #56` (the last one with `Closes #56`).
- Profiles are client only: no protocol, host or session change.
- Nothing is written automatically: settings and the loaded profile reset when CouchLink closes. Only Save as… writes a file.
- File format `1`. Limits: `name` 1-60 characters, `game` up to 60, `label` up to 24 on one line, file at most 64 KB (65,536 bytes), `sensitivity` 1-10 (default 5), `invertY` default false.
- Reserved keys are never accepted from a file: Esc 0x1B, F1 0x70, F2 0x71, LWin 0x5B, RWin 0x5C.
- A control a profile leaves out has **no key** (it does not keep the built-in key).
- Profiles folder: `Path.Combine(AppContext.BaseDirectory, "profiles")`, top-level `*.json` only.
- Error texts are exactly those in spec 2.3 (copied into Task 3's tests).
- Reset to default restores the built-in layout and clears the profile name, labels and the changed flag.

## Review Focus

1. **A profile is loaded mid-game while the player holds a key** that means something else in the new layout. They expect nothing to stay pressed and nothing to fire by itself. Pinned in Task 4 (`A_profile_loaded_while_a_key_is_held_presses_nothing`).
2. **A file saved by Notepad** (UTF-8 with a byte-order mark) with a non-ASCII name ("Café"). The owner expects it to load and show the name correctly. Pinned in Task 6 (`A_file_with_a_byte_order_mark_and_accents_loads`).
3. **A layout with a key that has no ID** (an OEM key on a non-US keyboard, bound in the editor). The owner expects Save as… to work and the file to load on another PC. Pinned in Task 3 (`A_key_without_an_id_round_trips_as_hex`).
4. **Saving over an existing profile**, including the one currently loaded. The owner expects the file replaced whole, with no leftover temp files in the folder. Pinned in Task 6 (`Write_replaces_an_existing_file_and_leaves_no_temp_file`).
5. **A label typed and the editor closed without pressing Enter or tabbing away.** The owner expects the label kept. Pinned in Task 7 (labels are committed on `Closing`) and the manual gate row "Type a label and close the editor at once".

---

## File Structure

| File | Responsibility |
|---|---|
| `src/CouchLink.Core/Input/ProfileKeys.cs` (new) | Fixed key IDs ↔ virtual-key codes, hex fallback |
| `src/CouchLink.Core/Input/KeyLayout.cs` (modify) | Immutable `Current` snapshot, `Replace` |
| `src/CouchLink.Core/Input/InputMapper.cs` (modify) | Reads the layout once per tick |
| `src/CouchLink.Core/Input/ControlProfile.cs` (new) | The profile record |
| `src/CouchLink.Core/Input/ProfileFile.cs` (new) | `Load` / `Save` JSON, validation, `ProfileResult` |
| `src/CouchLink.Core/Input/ControlSettings.cs` (modify) | Profile name/game, changed flag, labels, `Apply`, `SetLabel`, `ToProfile` |
| `src/CouchLink.Core/Input/KeyNames.cs` (modify) | `Labelled(control, label)` |
| `src/CouchLink.Core/Video/OverlayText.cs` (modify) | F1 panel: profile name, labels |
| `src/CouchLink.Core/Input/ProfileStore.cs` (new) | Folder listing, read with size cap, safe write, file names |
| `src/CouchLink.App/SaveProfileDialog.cs` (new) | Name and Game boxes |
| `src/CouchLink.App/ControlsWindow.cs` (modify) | Profile row, Browse…, Save as…, Show labels, label boxes |
| `tests/CouchLink.Core.Tests/ProfileKeysTests.cs`, `ProfileFileTests.cs`, `ProfileStoreTests.cs`, `ProfileAssert.cs` (new) | Tests and a profile comparison helper |
| `tests/CouchLink.Core.Tests/KeyLayoutTests.cs`, `InputMapperTests.cs`, `ControlSettingsTests.cs`, `OverlayTextTests.cs`, `KeyNamesTests.cs` (modify) | Tests |
| Docs (modify) | Setup guide 7, main design 6.7 and non-goals, gate checklist, README, spec status |

Run tests with: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~<ClassName>"`.

---

### Task 0: Branch

- [ ] **Step 1: Create the branch**

```bash
git switch docs/controller-profiles-spec
git switch -c feat/controller-profiles
```

---

### Task 1: Profile key IDs

**Files:**
- Create: `src/CouchLink.Core/Input/ProfileKeys.cs`
- Test: `tests/CouchLink.Core.Tests/ProfileKeysTests.cs`

**Interfaces:**
- Consumes: `VirtualKeys` constants, `KeyLayout.CreateDefault()`, `KeyLayout.KeysFor`, `KeyLayout.IsReserved` (all existing).
- Produces: `static class ProfileKeys` with `string IdOf(ushort vk)` and `bool TryParse(string id, out ushort vk)`. `TryParse` also accepts the reserved IDs (`Esc`, `F1`, `F2`, `LWin`, `RWin`) and returns their codes; callers check `KeyLayout.IsReserved`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/ProfileKeysTests.cs`:

```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class ProfileKeysTests
{
    [Fact]
    public void Every_code_round_trips_through_its_id()
    {
        for (int vk = 1; vk <= 0xFF; vk++)
        {
            Assert.True(ProfileKeys.TryParse(ProfileKeys.IdOf((ushort)vk), out ushort back), $"0x{vk:X2}");
            Assert.Equal((ushort)vk, back);
        }
    }

    [Fact]
    public void Every_default_key_has_a_named_id()
    {
        var layout = KeyLayout.CreateDefault();
        foreach (var control in Enum.GetValues<PadControl>())
            foreach (var key in layout.KeysFor(control))
                Assert.False(ProfileKeys.IdOf(key).StartsWith("0x", StringComparison.Ordinal), $"0x{key:X2}");
    }

    [Theory]
    [InlineData("J", 0x4A)]
    [InlineData("7", 0x37)]
    [InlineData("LeftClick", 0x01)]
    [InlineData("Mouse4", 0x05)]
    [InlineData("LShift", 0xA0)]
    [InlineData("RCtrl", 0xA3)]
    [InlineData("LAlt", 0xA4)]
    [InlineData("Space", 0x20)]
    [InlineData("Up", 0x26)]
    [InlineData("F3", 0x72)]
    [InlineData("F24", 0x87)]
    [InlineData("Num0", 0x60)]
    [InlineData("NumDivide", 0x6F)]
    [InlineData("Semicolon", 0xBA)]
    [InlineData("Quote", 0xDE)]
    [InlineData("0xE2", 0xE2)]
    [InlineData("0x4a", 0x4A)]
    public void Ids_map_to_their_codes(string id, int vk)
    {
        Assert.True(ProfileKeys.TryParse(id, out ushort parsed));
        Assert.Equal((ushort)vk, parsed);
    }

    [Theory]
    [InlineData("lshift", 0xA0)]
    [InlineData("LEFTCLICK", 0x01)]
    [InlineData("j", 0x4A)]
    public void Matching_ignores_case(string id, int vk)
    {
        Assert.True(ProfileKeys.TryParse(id, out ushort parsed));
        Assert.Equal((ushort)vk, parsed);
    }

    [Theory]
    [InlineData("Spcae")]
    [InlineData("")]
    [InlineData("0x00")]
    [InlineData("0x100")]
    [InlineData("0xZZ")]
    [InlineData("0x")]
    [InlineData("Left click")]
    public void Unknown_ids_are_refused(string id) => Assert.False(ProfileKeys.TryParse(id, out _));

    [Theory]
    [InlineData("Esc")]
    [InlineData("F1")]
    [InlineData("F2")]
    [InlineData("LWin")]
    [InlineData("RWin")]
    public void Reserved_ids_parse_to_reserved_codes(string id)
    {
        Assert.True(ProfileKeys.TryParse(id, out ushort vk));
        Assert.True(KeyLayout.IsReserved(vk));
    }

    [Fact]
    public void Codes_without_an_id_are_written_as_hex() => Assert.Equal("0xE2", ProfileKeys.IdOf(0xE2));
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~ProfileKeysTests"`
Expected: build FAILS with `The name 'ProfileKeys' does not exist in the current context`.

- [ ] **Step 3: Implement**

`src/CouchLink.Core/Input/ProfileKeys.cs`:

```csharp
using System.Globalization;

namespace CouchLink.Core.Input;

/// <summary>
/// The key IDs profile files use ("LShift", "LeftClick"). Fixed for good, unlike the display names in
/// <see cref="KeyNames"/>. A code with no ID is written as "0xNN". Matching ignores case. The reserved
/// keys have IDs too, so a file naming one gets "F1 is reserved" rather than "unknown key".
/// </summary>
public static class ProfileKeys
{
    private static readonly Dictionary<ushort, string> Ids = BuildIds();
    private static readonly Dictionary<string, ushort> Codes =
        Ids.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    public static string IdOf(ushort vk) => Ids.TryGetValue(vk, out var id) ? id : $"0x{vk:X2}";

    public static bool TryParse(string id, out ushort vk)
    {
        if (Codes.TryGetValue(id, out vk))
            return true;
        if (id.Length is 3 or 4 && id.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ushort.TryParse(id.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out vk)
            && vk is >= 1 and <= 0xFF)
            return true;
        vk = 0;
        return false;
    }

    private static Dictionary<ushort, string> BuildIds()
    {
        var ids = new Dictionary<ushort, string>
        {
            [VirtualKeys.LButton] = "LeftClick", [VirtualKeys.RButton] = "RightClick", [VirtualKeys.MButton] = "MiddleClick",
            [VirtualKeys.XButton1] = "Mouse4", [VirtualKeys.XButton2] = "Mouse5",
            [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "CapsLock",
            [0x1B] = "Esc", [0x20] = "Space",
            [0x21] = "PageUp", [0x22] = "PageDown", [0x23] = "End", [0x24] = "Home",
            [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down",
            [0x2D] = "Insert", [0x2E] = "Delete", [0x5B] = "LWin", [0x5C] = "RWin", [0x5D] = "Menu",
            [0x6A] = "NumMultiply", [0x6B] = "NumAdd", [0x6D] = "NumSubtract", [0x6E] = "NumDecimal", [0x6F] = "NumDivide",
            [0x90] = "NumLock", [0x91] = "ScrollLock",
            [0xA0] = "LShift", [0xA1] = "RShift", [0xA2] = "LCtrl", [0xA3] = "RCtrl", [0xA4] = "LAlt", [0xA5] = "RAlt",
            [0xBA] = "Semicolon", [0xBB] = "Equals", [0xBC] = "Comma", [0xBD] = "Minus", [0xBE] = "Period",
            [0xBF] = "Slash", [0xC0] = "Backquote",
            [0xDB] = "LeftBracket", [0xDC] = "Backslash", [0xDD] = "RightBracket", [0xDE] = "Quote",
        };
        for (char c = 'A'; c <= 'Z'; c++)
            ids[c] = c.ToString();
        for (char c = '0'; c <= '9'; c++)
            ids[c] = c.ToString();
        for (int n = 1; n <= 24; n++)
            ids[(ushort)(0x6F + n)] = $"F{n}";
        for (int n = 0; n <= 9; n++)
            ids[(ushort)(0x60 + n)] = $"Num{n}";
        return ids;
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~ProfileKeysTests"`
Expected: PASS (all).

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Input/ProfileKeys.cs tests/CouchLink.Core.Tests/ProfileKeysTests.cs
git commit -m "feat(input): fixed key IDs for controller profile files

Refs #56"
```

---

### Task 2: One layout snapshot per input tick

**Files:**
- Modify: `src/CouchLink.Core/Input/KeyLayout.cs` (whole class body)
- Modify: `src/CouchLink.Core/Input/InputMapper.cs:100-128` (`Tick`, `IsDown`)
- Test: `tests/CouchLink.Core.Tests/KeyLayoutTests.cs`, `tests/CouchLink.Core.Tests/InputMapperTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> KeyLayout.Current { get; }` (immutable; never changes after it is returned) and `void KeyLayout.Replace(IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> keys)`. `KeysFor`, `Bind`, `ResetToDefault`, `IsReserved`, `CreateDefault` keep their signatures.

- [ ] **Step 1: Write the failing tests**

Append to `KeyLayoutTests`:

```csharp
    [Fact]
    public void Replace_swaps_every_binding()
    {
        var layout = KeyLayout.CreateDefault();
        layout.Replace(new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Cross] = [VirtualKeys.Space] });
        Assert.Equal([VirtualKeys.Space], layout.KeysFor(PadControl.Cross));
        Assert.Empty(layout.KeysFor(PadControl.Square)); // left out of the profile: no key
    }

    [Fact]
    public void A_snapshot_taken_before_a_change_keeps_the_old_bindings()
    {
        var layout = KeyLayout.CreateDefault();
        var before = layout.Current;
        layout.Replace(new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Circle] = [K] });
        layout.Bind(PadControl.Triangle, VirtualKeys.Space);
        Assert.Equal([K], before[PadControl.Cross]);
        Assert.False(before.ContainsKey(PadControl.Triangle) && before[PadControl.Triangle].Contains(VirtualKeys.Space));
    }

    [Fact]
    public void Replace_copies_the_keys_it_is_given()
    {
        var layout = KeyLayout.CreateDefault();
        ushort[] keys = [VirtualKeys.Space];
        layout.Replace(new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Cross] = keys });
        keys[0] = K;
        Assert.Equal([VirtualKeys.Space], layout.KeysFor(PadControl.Cross));
    }
```

Append to `InputMapperTests`:

```csharp
    [Fact]
    public void A_tick_reads_one_layout_for_every_control()
    {
        var layout = KeyLayout.CreateDefault();
        var onCross = new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Cross] = [K] };
        var onCircle = new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Circle] = [K] };
        layout.Replace(onCross);
        var mapper = new InputMapper(layout, new MouseStick());
        mapper.KeyDown(K);

        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var swapper = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                layout.Replace(onCircle);
                layout.Replace(onCross);
            }
        });
        int ticks = 0;
        while (!stop.IsCancellationRequested)
        {
            var buttons = mapper.Tick(0.001).Buttons & (PadButtons.Cross | PadButtons.Circle);
            Assert.True(buttons is PadButtons.Cross or PadButtons.Circle, $"tick {ticks}: {buttons}");
            ticks++;
        }
        swapper.Wait();
    }
```

(`K` is already defined in both test classes. Check `PadState` exposes `Buttons`; it is the first positional member of `PadState` in `src/CouchLink.Core/Input/PadState.cs`.)

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~KeyLayoutTests|FullyQualifiedName~InputMapperTests"`
Expected: build FAILS with `'KeyLayout' does not contain a definition for 'Replace'`.

- [ ] **Step 3: Implement `KeyLayout`**

Replace the body of `KeyLayout` (keep `BindResult` above it unchanged) with:

```csharp
/// <summary>
/// Which keys drive which DS4 control. The defaults give a few controls two keys; a rebind gives the
/// control exactly one key and takes that key off any other control. The bindings are one immutable
/// snapshot (<see cref="Current"/>) replaced whole on every edit: the input loop reads it once per tick,
/// so an edit or a profile load can never land halfway through a tick.
/// </summary>
public sealed class KeyLayout
{
    private static readonly ushort[] ReservedKeys =
        [VirtualKeys.Escape, VirtualKeys.F1, VirtualKeys.F2, VirtualKeys.LWin, VirtualKeys.RWin];

    private readonly Lock _gate = new(); // one writer at a time; readers never wait
    private IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> _current;

    private KeyLayout(IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> bindings) => _current = bindings;

    /// <summary>Every binding, as a snapshot that never changes once returned.</summary>
    public IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> Current => Volatile.Read(ref _current);

    public IReadOnlyList<ushort> KeysFor(PadControl control) =>
        Current.TryGetValue(control, out var keys) ? keys : [];

    /// <summary>Esc cancels a rebind, F1 and F2 toggle the player's panels, the Windows keys are blocked while playing.</summary>
    public static bool IsReserved(ushort key) => ReservedKeys.Contains(key);

    public BindResult Bind(PadControl control, ushort key)
    {
        if (IsReserved(key))
            return BindResult.Reserved;
        lock (_gate)
        {
            var current = _current;
            var next = new Dictionary<PadControl, IReadOnlyList<ushort>>(current);
            PadControl? movedFrom = null;
            foreach (var (other, keys) in current)
            {
                if (other == control || !keys.Contains(key))
                    continue;
                next[other] = keys.Where(k => k != key).ToArray();
                movedFrom = other;
            }
            next[control] = [key];
            Volatile.Write(ref _current, next);
            return new BindResult(true, movedFrom);
        }
    }

    /// <summary>Swaps in a whole layout (a loaded profile). The caller has checked reserved keys and duplicates.</summary>
    public void Replace(IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> keys)
    {
        IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> next =
            keys.ToDictionary(p => p.Key, p => (IReadOnlyList<ushort>)p.Value.ToArray());
        lock (_gate)
            Volatile.Write(ref _current, next);
    }

    public void ResetToDefault()
    {
        lock (_gate)
            Volatile.Write(ref _current, Defaults());
    }

    /// <summary>Spec section 6.1 default layout.</summary>
    public static KeyLayout CreateDefault() => new(Defaults());

    private static IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> Defaults() =>
        new Dictionary<PadControl, IReadOnlyList<ushort>>
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

- [ ] **Step 4: Implement the mapper change**

In `InputMapper`, replace `Tick` and `IsDown` with:

```csharp
    public PadState Tick(double dtSeconds)
    {
        lock (_gate)
        {
            var layout = _layout.Current; // one snapshot for the whole tick
            var buttons = PadButtons.None;
            foreach (var (control, button) in ButtonMap)
                if (IsDown(layout, control))
                    buttons |= button;

            var (lx, ly) = StickMath.FromDirections(
                IsDown(layout, PadControl.LeftUp), IsDown(layout, PadControl.LeftDown),
                IsDown(layout, PadControl.LeftLeft), IsDown(layout, PadControl.LeftRight));
            var (rx, ry) = _mouse.Update(dtSeconds);
            byte l2 = buttons.HasFlag(PadButtons.L2) ? (byte)255 : (byte)0;
            byte r2 = buttons.HasFlag(PadButtons.R2) ? (byte)255 : (byte)0;
            return new PadState(buttons, lx, ly, rx, ry, l2, r2);
        }
    }

    private bool IsDown(IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> layout, PadControl control)
    {
        if (!layout.TryGetValue(control, out var keys))
            return false;
        foreach (var key in keys)
            if (_held.Contains(key))
                return true;
        return false;
    }
```

- [ ] **Step 5: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~KeyLayoutTests|FullyQualifiedName~InputMapperTests|FullyQualifiedName~KeyNamesTests|FullyQualifiedName~ControlSettingsTests"`
Expected: PASS (all, including the existing ones).

- [ ] **Step 6: Commit**

```bash
git add src/CouchLink.Core/Input/KeyLayout.cs src/CouchLink.Core/Input/InputMapper.cs tests/CouchLink.Core.Tests/KeyLayoutTests.cs tests/CouchLink.Core.Tests/InputMapperTests.cs
git commit -m "refactor(input): the key layout is one snapshot, read once per input tick

A whole layout can now be swapped in without a tick seeing half of it.

Refs #56"
```

---

### Task 3: The profile and its file format

**Files:**
- Create: `src/CouchLink.Core/Input/ControlProfile.cs`, `src/CouchLink.Core/Input/ProfileFile.cs`
- Test: `tests/CouchLink.Core.Tests/ProfileFileTests.cs`, `tests/CouchLink.Core.Tests/ProfileAssert.cs`

**Interfaces:**
- Consumes: `ProfileKeys.TryParse`, `ProfileKeys.IdOf` (Task 1); `KeyLayout.IsReserved`, `KeyLayout.CreateDefault().Current` (Task 2); `KeyNames.Of(ushort)`, `KeyNames.Groups`, `ControlSettings.MinStep/MaxStep/DefaultStep` (existing).
- Produces:
  - `sealed record ControlProfile(string Name, string? Game, int SensitivityStep, bool InvertY, IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> Keys, IReadOnlyDictionary<PadControl, string> Labels)`. `Keys` holds only controls with at least one key; `Labels` only non-empty labels.
  - `sealed record ProfileResult(ControlProfile? Profile, string? Error)` with `static Success(ControlProfile)` and `static Failure(string)`.
  - `static class ProfileFile` with constants `Format = 1`, `MaxNameLength = 60`, `MaxGameLength = 60`, `MaxLabelLength = 24`, `MaxBytes = 65536`, and `ProfileResult Load(string json)`, `string Save(ControlProfile profile)`.
  - Test helper `ProfileAssert.Same(ControlProfile expected, ControlProfile actual)`.

- [ ] **Step 1: Write the test helper**

`tests/CouchLink.Core.Tests/ProfileAssert.cs`:

```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

/// <summary>Compares two profiles field by field (records compare dictionaries by reference).</summary>
internal static class ProfileAssert
{
    public static void Same(ControlProfile expected, ControlProfile actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Game, actual.Game);
        Assert.Equal(expected.SensitivityStep, actual.SensitivityStep);
        Assert.Equal(expected.InvertY, actual.InvertY);
        foreach (var control in Enum.GetValues<PadControl>())
        {
            Assert.Equal(KeysOf(expected, control), KeysOf(actual, control));
            Assert.Equal(expected.Labels.GetValueOrDefault(control), actual.Labels.GetValueOrDefault(control));
        }
    }

    private static ushort[] KeysOf(ControlProfile profile, PadControl control) =>
        profile.Keys.TryGetValue(control, out var keys) ? keys.ToArray() : [];
}
```

- [ ] **Step 2: Write the failing tests**

`tests/CouchLink.Core.Tests/ProfileFileTests.cs`:

```csharp
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public class ProfileFileTests
{
    /// <summary>Writes JSON with ' for " so test data stays readable.</summary>
    private static string J(string text) => text.Replace('\'', '"');

    private static string WithControls(string controls) => J($"{{'format':1,'name':'T','controls':{{{controls}}}}}");

    private const string SpecExample = """
        {
          "format": 1,
          "name": "NBA 2K22 Café layout",
          "game": "NBA 2K22",
          "sensitivity": 6,
          "invertY": false,
          "controls": {
            "Square":  { "keys": ["J", "LeftClick"], "label": "Shoot" },
            "Cross":   { "keys": ["K"], "label": "Pass" },
            "R2":      { "keys": ["LShift"], "label": "Sprint" },
            "LeftUp":  { "keys": ["W"] }
          }
        }
        """;

    private static ControlProfile Loaded(string json)
    {
        var result = ProfileFile.Load(json);
        Assert.Null(result.Error);
        return result.Profile!;
    }

    [Fact]
    public void The_spec_example_loads()
    {
        var p = Loaded(SpecExample);
        Assert.Equal("NBA 2K22 Café layout", p.Name);
        Assert.Equal("NBA 2K22", p.Game);
        Assert.Equal(6, p.SensitivityStep);
        Assert.False(p.InvertY);
        Assert.Equal([VirtualKeys.Letter('J'), VirtualKeys.LButton], p.Keys[PadControl.Square]);
        Assert.Equal([VirtualKeys.LShift], p.Keys[PadControl.R2]);
        Assert.Equal("Shoot", p.Labels[PadControl.Square]);
        Assert.False(p.Labels.ContainsKey(PadControl.LeftUp));
    }

    [Fact]
    public void Controls_left_out_have_no_key()
    {
        var p = Loaded(SpecExample);
        Assert.False(p.Keys.ContainsKey(PadControl.LeftDown));
        Assert.False(p.Keys.ContainsKey(PadControl.Circle));
    }

    [Fact]
    public void Missing_optional_fields_get_their_defaults()
    {
        var p = Loaded(WithControls(""));
        Assert.Null(p.Game);
        Assert.Equal(ControlSettings.DefaultStep, p.SensitivityStep);
        Assert.False(p.InvertY);
        Assert.Empty(p.Keys);
        Assert.Empty(p.Labels);
    }

    [Fact]
    public void Comments_trailing_commas_any_case_and_unknown_top_level_fields_are_fine()
    {
        var p = Loaded(J("""
            {
              // made for the back room
              'FORMAT': 1,
              'Name': '  Back room  ',
              'theme': 'dark',
              'controls': { 'square': { 'KEYS': ['j', 'J'], 'Label': ' Shoot ' }, },
            }
            """));
        Assert.Equal("Back room", p.Name);
        Assert.Equal([VirtualKeys.Letter('J')], p.Keys[PadControl.Square]); // listed twice, kept once
        Assert.Equal("Shoot", p.Labels[PadControl.Square]);
    }

    [Theory]
    [InlineData("{'name':'T','controls':{}}", "Missing \"format\"")]
    [InlineData("{'format':'1','name':'T','controls':{}}", "Missing \"format\"")]
    [InlineData("{'format':2,'name':'T','controls':{}}", "This profile was made for a newer CouchLink")]
    [InlineData("{'format':1,'controls':{}}", "\"name\" must be 1-60 characters")]
    [InlineData("{'format':1,'name':'   ','controls':{}}", "\"name\" must be 1-60 characters")]
    [InlineData("{'format':1,'name':'T'}", "Missing \"controls\"")]
    [InlineData("{'format':1,'name':'T','sensitivity':0,'controls':{}}", "\"sensitivity\" must be 1-10")]
    [InlineData("{'format':1,'name':'T','sensitivity':11,'controls':{}}", "\"sensitivity\" must be 1-10")]
    [InlineData("{'format':1,'name':'T','sensitivity':'5','controls':{}}", "\"sensitivity\" must be 1-10")]
    [InlineData("{'format':1,'name':'T','invertY':'yes','controls':{}}", "\"invertY\" must be true or false")]
    [InlineData("[]", "Not a valid profile file: it must be a JSON object")]
    public void Bad_top_level_fields_say_what_is_wrong(string json, string message) =>
        Assert.Equal(message, ProfileFile.Load(J(json)).Error);

    [Theory]
    [InlineData("'Sqaure':{'keys':['J']}", "Unknown control \"Sqaure\"")]
    [InlineData("'3':{'keys':['J']}", "Unknown control \"3\"")]
    [InlineData("'Square':{'keys':['Spcae']}", "\"Square\": unknown key \"Spcae\"")]
    [InlineData("'Square':{'keys':['F1']}", "\"Square\": F1 is reserved")]
    [InlineData("'Square':{'keys':['Esc']}", "\"Square\": Esc is reserved")]
    [InlineData("'Square':{'keys':['0x5B']}", "\"Square\": Left Windows is reserved")]
    [InlineData("'Cross':{'keys':['K']},'Circle':{'keys':['k']}", "\"K\" is on both Cross and Circle")]
    [InlineData("'Square':{'keys':'J'}", "\"Square\": \"keys\" must be a list of key names")]
    [InlineData("'Square':{'keys':[74]}", "\"Square\": \"keys\" must be a list of key names")]
    [InlineData("'Square':{'lable':'Shoot'}", "\"Square\": unknown field \"lable\"")]
    [InlineData("'Square':['J']", "\"Square\": must be an object with \"keys\" and \"label\"")]
    [InlineData("'Square':{'label':'1234567890123456789012345'}", "\"Square\": label must be up to 24 characters on one line")]
    [InlineData("'Square':{'label':'Shoot\\nPass'}", "\"Square\": label must be up to 24 characters on one line")]
    [InlineData("'Square':{'keys':['J']},'square':{'keys':['K']}", "Control \"Square\" is listed twice")]
    public void Bad_controls_say_what_is_wrong(string controls, string message) =>
        Assert.Equal(message, ProfileFile.Load(WithControls(controls)).Error);

    [Fact]
    public void A_name_over_60_characters_is_refused() =>
        Assert.Equal("\"name\" must be 1-60 characters",
            ProfileFile.Load(J($"{{'format':1,'name':'{new string('x', 61)}','controls':{{}}}}")).Error);

    [Fact]
    public void Broken_json_names_the_line()
    {
        var error = ProfileFile.Load("{\n  \"format\": 1\n  \"name\": \"T\"\n}").Error;
        Assert.NotNull(error);
        Assert.StartsWith("Not a valid profile file (line 3): ", error);
        Assert.DoesNotContain("LineNumber", error);
    }

    [Fact]
    public void Save_writes_fields_and_controls_in_a_fixed_order()
    {
        var profile = new ControlProfile("Test", null, 5, false,
            new Dictionary<PadControl, IReadOnlyList<ushort>>
            {
                [PadControl.Square] = [VirtualKeys.Letter('J'), VirtualKeys.LButton],
                [PadControl.Cross] = [VirtualKeys.Letter('K')],
            },
            new Dictionary<PadControl, string> { [PadControl.Square] = "Shoot", [PadControl.Touchpad] = "Map" });

        const string expected = """
            {
              "format": 1,
              "name": "Test",
              "sensitivity": 5,
              "invertY": false,
              "controls": {
                "Cross": {
                  "keys": [
                    "K"
                  ]
                },
                "Square": {
                  "keys": [
                    "J",
                    "LeftClick"
                  ],
                  "label": "Shoot"
                },
                "Touchpad": {
                  "label": "Map"
                }
              }
            }
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n") + "\n", ProfileFile.Save(profile));
    }

    [Fact]
    public void Save_keeps_accents_readable() =>
        Assert.Contains("\"name\": \"Café\"", ProfileFile.Save(new ControlProfile("Café", null, 5, false,
            new Dictionary<PadControl, IReadOnlyList<ushort>>(), new Dictionary<PadControl, string>())));

    [Fact]
    public void The_built_in_layout_round_trips()
    {
        var profile = new ControlProfile("Default", "Any", 7, true, KeyLayout.CreateDefault().Current,
            new Dictionary<PadControl, string>());
        ProfileAssert.Same(profile, Loaded(ProfileFile.Save(profile)));
    }

    [Fact]
    public void A_profile_with_labels_round_trips()
    {
        var profile = Loaded(SpecExample);
        ProfileAssert.Same(profile, Loaded(ProfileFile.Save(profile)));
    }

    [Fact]
    public void A_key_without_an_id_round_trips_as_hex()
    {
        var profile = new ControlProfile("OEM", null, 5, false,
            new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Cross] = [0xE2] },
            new Dictionary<PadControl, string>());
        var json = ProfileFile.Save(profile);
        Assert.Contains("\"0xE2\"", json);
        ProfileAssert.Same(profile, Loaded(json));
    }
}
```

- [ ] **Step 3: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~ProfileFileTests"`
Expected: build FAILS with `The type or namespace name 'ControlProfile' could not be found`.

- [ ] **Step 4: Implement `ControlProfile`**

`src/CouchLink.Core/Input/ControlProfile.cs`:

```csharp
namespace CouchLink.Core.Input;

/// <summary>
/// A controller profile: a whole key layout for a game, its mouse settings and optional action labels
/// ("Shoot" on Square). Already validated. <see cref="Keys"/> holds only controls that have a key;
/// <see cref="Labels"/> only non-empty labels. Made by <see cref="ProfileFile.Load"/> and
/// <see cref="ControlSettings.ToProfile"/>.
/// </summary>
public sealed record ControlProfile(
    string Name,
    string? Game,
    int SensitivityStep,
    bool InvertY,
    IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> Keys,
    IReadOnlyDictionary<PadControl, string> Labels);
```

- [ ] **Step 5: Implement `ProfileFile`**

`src/CouchLink.Core/Input/ProfileFile.cs`:

```csharp
using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace CouchLink.Core.Input;

/// <summary>What <see cref="ProfileFile.Load"/> gave back: a whole profile, or why the file was refused.</summary>
public sealed record ProfileResult(ControlProfile? Profile, string? Error)
{
    public static ProfileResult Success(ControlProfile profile) => new(profile, null);
    public static ProfileResult Failure(string error) => new(null, error);
}

/// <summary>
/// Reads and writes controller profile files (controller profiles design, section 2). Works on strings;
/// <see cref="ProfileStore"/> does the disk. Load returns a whole profile or the first problem found,
/// never part of a profile.
/// </summary>
public static class ProfileFile
{
    public const int Format = 1;
    public const int MaxNameLength = 60, MaxGameLength = 60, MaxLabelLength = 24;
    public const int MaxBytes = 64 * 1024;

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonWriterOptions WriteOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // "Café", not "Café"
    };

    private static readonly Dictionary<string, PadControl> ControlNames =
        Enum.GetValues<PadControl>().ToDictionary(c => c.ToString(), StringComparer.OrdinalIgnoreCase);

    public static ProfileResult Load(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, ReadOptions);
            return ProfileResult.Success(Read(document.RootElement));
        }
        catch (JsonException e)
        {
            return ProfileResult.Failure($"Not a valid profile file (line {(e.LineNumber ?? 0) + 1}): {ParserMessage(e)}");
        }
        catch (ProfileException e)
        {
            return ProfileResult.Failure(e.Message);
        }
    }

    public static string Save(ControlProfile profile)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, WriteOptions))
        {
            w.WriteStartObject();
            w.WriteNumber("format", Format);
            w.WriteString("name", profile.Name);
            if (profile.Game is { } game)
                w.WriteString("game", game);
            w.WriteNumber("sensitivity", profile.SensitivityStep);
            w.WriteBoolean("invertY", profile.InvertY);
            w.WriteStartObject("controls");
            foreach (var (_, controls) in KeyNames.Groups)
            {
                foreach (var control in controls)
                {
                    IReadOnlyList<ushort> keys = profile.Keys.TryGetValue(control, out var k) ? k : [];
                    var label = profile.Labels.GetValueOrDefault(control);
                    if (keys.Count == 0 && label is null)
                        continue;
                    w.WriteStartObject(control.ToString());
                    if (keys.Count > 0)
                    {
                        w.WriteStartArray("keys");
                        foreach (var key in keys)
                            w.WriteStringValue(ProfileKeys.IdOf(key));
                        w.WriteEndArray();
                    }
                    if (label is not null)
                        w.WriteString("label", label);
                    w.WriteEndObject();
                }
            }
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan) + "\n";
    }

    private static ControlProfile Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new ProfileException("Not a valid profile file: it must be a JSON object");
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in root.EnumerateObject())
            fields[field.Name] = field.Value; // unknown fields are kept here and ignored

        if (!fields.TryGetValue("format", out var format) || format.ValueKind != JsonValueKind.Number
            || !format.TryGetInt32(out int version) || version < 1)
            throw new ProfileException("Missing \"format\"");
        if (version > Format)
            throw new ProfileException("This profile was made for a newer CouchLink");

        string name = fields.TryGetValue("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
        if (name.Length is 0 or > MaxNameLength)
            throw new ProfileException($"\"name\" must be 1-{MaxNameLength} characters");

        string? game = null;
        if (fields.TryGetValue("game", out var g) && g.ValueKind != JsonValueKind.Null)
        {
            game = g.ValueKind == JsonValueKind.String ? g.GetString()!.Trim() : null;
            if (game is null || game.Length > MaxGameLength)
                throw new ProfileException($"\"game\" must be up to {MaxGameLength} characters");
            if (game.Length == 0)
                game = null;
        }

        int step = ControlSettings.DefaultStep;
        if (fields.TryGetValue("sensitivity", out var s)
            && (s.ValueKind != JsonValueKind.Number || !s.TryGetInt32(out step)
                || step is < ControlSettings.MinStep or > ControlSettings.MaxStep))
            throw new ProfileException($"\"sensitivity\" must be {ControlSettings.MinStep}-{ControlSettings.MaxStep}");

        bool invertY = false;
        if (fields.TryGetValue("invertY", out var invert))
        {
            if (invert.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ProfileException("\"invertY\" must be true or false");
            invertY = invert.GetBoolean();
        }

        if (!fields.TryGetValue("controls", out var controls) || controls.ValueKind != JsonValueKind.Object)
            throw new ProfileException("Missing \"controls\"");

        var keys = new Dictionary<PadControl, IReadOnlyList<ushort>>();
        var labels = new Dictionary<PadControl, string>();
        var owners = new Dictionary<ushort, PadControl>();
        var seen = new HashSet<PadControl>();
        foreach (var entry in controls.EnumerateObject())
        {
            if (!ControlNames.TryGetValue(entry.Name, out var control))
                throw new ProfileException($"Unknown control \"{entry.Name}\"");
            if (!seen.Add(control))
                throw new ProfileException($"Control \"{control}\" is listed twice");
            if (entry.Value.ValueKind != JsonValueKind.Object)
                throw new ProfileException($"\"{control}\": must be an object with \"keys\" and \"label\"");
            foreach (var field in entry.Value.EnumerateObject())
            {
                if (field.Name.Equals("keys", StringComparison.OrdinalIgnoreCase))
                    ReadKeys(control, field.Value, keys, owners);
                else if (field.Name.Equals("label", StringComparison.OrdinalIgnoreCase))
                    ReadLabel(control, field.Value, labels);
                else
                    throw new ProfileException($"\"{control}\": unknown field \"{field.Name}\"");
            }
        }
        return new ControlProfile(name, game, step, invertY, keys, labels);
    }

    private static void ReadKeys(PadControl control, JsonElement value,
        Dictionary<PadControl, IReadOnlyList<ushort>> keys, Dictionary<ushort, PadControl> owners)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return;
        string notAList = $"\"{control}\": \"keys\" must be a list of key names";
        if (value.ValueKind != JsonValueKind.Array)
            throw new ProfileException(notAList);
        var list = new List<ushort>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new ProfileException(notAList);
            string id = item.GetString()!;
            if (!ProfileKeys.TryParse(id, out ushort vk))
                throw new ProfileException($"\"{control}\": unknown key \"{id}\"");
            if (KeyLayout.IsReserved(vk))
                throw new ProfileException($"\"{control}\": {KeyNames.Of(vk)} is reserved");
            if (list.Contains(vk))
                continue;
            if (owners.TryGetValue(vk, out var other))
                throw new ProfileException($"\"{ProfileKeys.IdOf(vk)}\" is on both {other} and {control}");
            owners[vk] = control;
            list.Add(vk);
        }
        if (list.Count > 0)
            keys[control] = list.ToArray();
    }

    private static void ReadLabel(PadControl control, JsonElement value, Dictionary<PadControl, string> labels)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return;
        string? raw = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        if (raw is null || raw.Contains('\n') || raw.Contains('\r') || raw.Trim().Length > MaxLabelLength)
            throw new ProfileException($"\"{control}\": label must be up to {MaxLabelLength} characters on one line");
        string label = raw.Trim();
        if (label.Length > 0)
            labels[control] = label;
    }

    /// <summary>The parser's message without its "Path: ... | LineNumber: ..." tail (the line is shown separately).</summary>
    private static string ParserMessage(JsonException e)
    {
        string message = e.Message;
        int cut = message.IndexOf(" Path:", StringComparison.Ordinal);
        if (cut < 0)
            cut = message.IndexOf(" LineNumber:", StringComparison.Ordinal);
        return (cut < 0 ? message : message[..cut]).TrimEnd(' ', '|', '.');
    }

    private sealed class ProfileException(string message) : Exception(message);
}
```

- [ ] **Step 6: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~ProfileFileTests"`
Expected: PASS. If `Save_writes_fields_and_controls_in_a_fixed_order` fails only on whitespace, the writer's indent differs from the expected text: fix the test text to match `Utf8JsonWriter`'s output (two-space indent, one array item per line), not the writer.

- [ ] **Step 7: Commit**

```bash
git add src/CouchLink.Core/Input/ControlProfile.cs src/CouchLink.Core/Input/ProfileFile.cs tests/CouchLink.Core.Tests/ProfileFileTests.cs tests/CouchLink.Core.Tests/ProfileAssert.cs
git commit -m "feat(input): read and write controller profile files

Refs #56"
```

---

### Task 4: Applying a profile to the controls

**Files:**
- Modify: `src/CouchLink.Core/Input/ControlSettings.cs` (whole file)
- Test: `tests/CouchLink.Core.Tests/ControlSettingsTests.cs`, `tests/CouchLink.Core.Tests/InputMapperTests.cs`

**Interfaces:**
- Consumes: `ControlProfile`, `ProfileFile.MaxLabelLength`, `ProfileFile.Load` (Task 3); `KeyLayout.Replace`, `KeyLayout.Current` (Task 2); `ProfileAssert.Same` (Task 3).
- Produces on `ControlSettings`: `string? ProfileName`, `string? ProfileGame`, `bool ProfileChanged`, `string? LabelFor(PadControl)`, `void Apply(ControlProfile)`, `void SetLabel(PadControl, string?)`, `ControlProfile ToProfile(string name, string? game)`. `ResetToDefault()` also clears the profile and labels.

- [ ] **Step 1: Write the failing tests**

Append to `ControlSettingsTests`:

```csharp
    private static ControlProfile Profile(string name = "2K22") => new(name, "NBA 2K22", 8, true,
        new Dictionary<PadControl, IReadOnlyList<ushort>>
        {
            [PadControl.Circle] = [VirtualKeys.Letter('K')],
            [PadControl.Square] = [VirtualKeys.Space],
        },
        new Dictionary<PadControl, string> { [PadControl.Square] = "Shoot" });

    [Fact]
    public void Apply_sets_layout_sensitivity_invert_labels_and_name()
    {
        var settings = new ControlSettings();
        settings.Apply(Profile());

        Assert.Equal([VirtualKeys.Letter('K')], settings.Layout.KeysFor(PadControl.Circle));
        Assert.Empty(settings.Layout.KeysFor(PadControl.Cross)); // left out: no key, not the built-in K
        Assert.Equal(8, settings.SensitivityStep);
        Assert.True(settings.InvertY);
        Assert.Equal("Shoot", settings.LabelFor(PadControl.Square));
        Assert.Null(settings.LabelFor(PadControl.Cross));
        Assert.Equal("2K22", settings.ProfileName);
        Assert.Equal("NBA 2K22", settings.ProfileGame);
        Assert.False(settings.ProfileChanged);
    }

    [Fact]
    public void Apply_raises_Changed_once()
    {
        var settings = new ControlSettings();
        int changed = 0;
        settings.Changed += () => changed++;
        settings.Apply(Profile());
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Every_kind_of_edit_after_Apply_marks_the_profile_changed()
    {
        Action<ControlSettings>[] edits =
        [
            s => s.Bind(PadControl.Cross, VirtualKeys.Letter('X')),
            s => s.SetSensitivityStep(2),
            s => s.SetInvertY(false),
            s => s.SetLabel(PadControl.Cross, "Pass"),
        ];
        foreach (var edit in edits)
        {
            var settings = new ControlSettings();
            settings.Apply(Profile());
            edit(settings);
            Assert.True(settings.ProfileChanged);
        }
    }

    [Fact]
    public void Edits_on_the_built_in_layout_mark_nothing()
    {
        var settings = new ControlSettings();
        settings.Bind(PadControl.Cross, VirtualKeys.Space);
        settings.SetLabel(PadControl.Cross, "Pass");
        Assert.Null(settings.ProfileName);
        Assert.False(settings.ProfileChanged);
    }

    [Fact]
    public void Applying_again_clears_the_changed_mark()
    {
        var settings = new ControlSettings();
        settings.Apply(Profile());
        settings.SetSensitivityStep(2);
        settings.Apply(Profile("Other"));
        Assert.False(settings.ProfileChanged);
        Assert.Equal("Other", settings.ProfileName);
    }

    [Fact]
    public void Reset_clears_the_profile_and_its_labels()
    {
        var settings = new ControlSettings();
        settings.Apply(Profile());
        settings.SetSensitivityStep(2);

        settings.ResetToDefault();

        Assert.Null(settings.ProfileName);
        Assert.Null(settings.ProfileGame);
        Assert.False(settings.ProfileChanged);
        Assert.Null(settings.LabelFor(PadControl.Square));
        Assert.Equal([VirtualKeys.Letter('K')], settings.Layout.KeysFor(PadControl.Cross));
    }

    [Theory]
    [InlineData("  Shoot  ", "Shoot")]
    [InlineData("Shoot\r\nfar", "Shoot far")]
    [InlineData("1234567890123456789012345678", "123456789012345678901234")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void SetLabel_trims_caps_and_clears(string? typed, string? stored)
    {
        var settings = new ControlSettings();
        settings.SetLabel(PadControl.Square, "Old");
        settings.SetLabel(PadControl.Square, typed);
        Assert.Equal(stored, settings.LabelFor(PadControl.Square));
    }

    [Fact]
    public void Setting_the_same_label_raises_nothing()
    {
        var settings = new ControlSettings();
        settings.SetLabel(PadControl.Square, "Shoot");
        int changed = 0;
        settings.Changed += () => changed++;
        settings.SetLabel(PadControl.Square, " Shoot ");
        Assert.Equal(0, changed);
    }

    [Fact]
    public void ToProfile_after_Apply_gives_the_same_profile()
    {
        var settings = new ControlSettings();
        var profile = Profile();
        settings.Apply(profile);
        ProfileAssert.Same(profile, settings.ToProfile("2K22", "NBA 2K22"));
    }

    [Fact]
    public void ToProfile_leaves_out_controls_without_keys_and_tidies_name_and_game()
    {
        var settings = new ControlSettings();
        settings.Bind(PadControl.Circle, VirtualKeys.Letter('K')); // Cross loses its only key
        var profile = settings.ToProfile("  Mine  ", "   ");
        Assert.False(profile.Keys.ContainsKey(PadControl.Cross));
        Assert.Equal("Mine", profile.Name);
        Assert.Null(profile.Game);
        Assert.Null(ProfileFile.Load(ProfileFile.Save(profile)).Error);
    }
```

Append to `InputMapperTests`:

```csharp
    [Fact]
    public void A_profile_loaded_while_a_key_is_held_presses_nothing()
    {
        var settings = new ControlSettings();
        using var mapper = new InputMapper(settings);
        mapper.KeyDown(K);
        Assert.Equal(PadButtons.Cross, mapper.Tick(0.001).Buttons);

        settings.Apply(new ControlProfile("P", null, 5, false,
            new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Circle] = [K] },
            new Dictionary<PadControl, string>()));

        Assert.Equal(PadState.Neutral, mapper.Tick(0.001)); // released, not Circle until pressed again
        mapper.KeyDown(K);
        Assert.Equal(PadButtons.Circle, mapper.Tick(0.001).Buttons);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~ControlSettingsTests|FullyQualifiedName~InputMapperTests"`
Expected: build FAILS with `'ControlSettings' does not contain a definition for 'Apply'`.

- [ ] **Step 3: Implement**

Replace `src/CouchLink.Core/Input/ControlSettings.cs` with:

```csharp
namespace CouchLink.Core.Input;

/// <summary>
/// The player's controls for this run of the app: key layout, mouse sensitivity step, Invert Y, action
/// labels and the loaded profile's name. In memory only (spec 6.7): a profile is loaded from a file by
/// choice, nothing is saved by itself. Edit through this class so <see cref="Changed"/> is raised.
/// Thread-safe: the editor writes on the UI thread, the input loop and the F1 panel read on their own threads.
/// </summary>
public sealed class ControlSettings
{
    public const int MinStep = 1, MaxStep = 10, DefaultStep = 5;
    private const double StepFactor = 1.3;
    private static readonly IReadOnlyDictionary<PadControl, string> NoLabels = new Dictionary<PadControl, string>();

    private int _step = DefaultStep;
    private volatile bool _invertY;
    private IReadOnlyDictionary<PadControl, string> _labels = NoLabels; // replaced whole, read without a lock
    private volatile string? _profileName;
    private volatile string? _profileGame;
    private volatile bool _profileChanged;

    public KeyLayout Layout { get; } = KeyLayout.CreateDefault();

    public int SensitivityStep => Volatile.Read(ref _step);

    /// <summary>Stick deflection per mouse count for the current step.</summary>
    public double Sensitivity => SensitivityFor(SensitivityStep);

    public bool InvertY => _invertY;

    /// <summary>The loaded profile's name, or null on the built-in layout.</summary>
    public string? ProfileName => _profileName;

    /// <summary>The loaded profile's game, if it names one.</summary>
    public string? ProfileGame => _profileGame;

    /// <summary>True after an edit made since the profile was loaded. The editor and F1 show "(changed)".</summary>
    public bool ProfileChanged => _profileChanged;

    /// <summary>Raised after every edit, on the thread that made it.</summary>
    public event Action? Changed;

    public static double SensitivityFor(int step) =>
        MouseStick.DefaultSensitivity * Math.Pow(StepFactor, Math.Clamp(step, MinStep, MaxStep) - DefaultStep);

    /// <summary>What the control does in the game ("Shoot"), or null.</summary>
    public string? LabelFor(PadControl control) => Volatile.Read(ref _labels).GetValueOrDefault(control);

    public BindResult Bind(PadControl control, ushort key)
    {
        var result = Layout.Bind(control, key);
        if (result.Bound)
            Edited();
        return result;
    }

    public void SetSensitivityStep(int step)
    {
        step = Math.Clamp(step, MinStep, MaxStep);
        if (Interlocked.Exchange(ref _step, step) != step)
            Edited();
    }

    public void SetInvertY(bool invert)
    {
        if (_invertY == invert)
            return;
        _invertY = invert;
        Edited();
    }

    /// <summary>Sets what the control does in the game: one line, trimmed, at most 24 characters; empty removes it.</summary>
    public void SetLabel(PadControl control, string? label)
    {
        string text = (label ?? "").ReplaceLineEndings(" ").Trim();
        if (text.Length > ProfileFile.MaxLabelLength)
            text = text[..ProfileFile.MaxLabelLength].TrimEnd();
        if (text == (LabelFor(control) ?? ""))
            return;
        var next = new Dictionary<PadControl, string>(Volatile.Read(ref _labels));
        if (text.Length == 0)
            next.Remove(control);
        else
            next[control] = text;
        Volatile.Write(ref _labels, next);
        Edited();
    }

    /// <summary>Loads a whole profile: keys, sensitivity, Invert Y and labels, then one <see cref="Changed"/>.</summary>
    public void Apply(ControlProfile profile)
    {
        Layout.Replace(profile.Keys);
        Volatile.Write(ref _step, Math.Clamp(profile.SensitivityStep, MinStep, MaxStep));
        _invertY = profile.InvertY;
        Volatile.Write(ref _labels, new Dictionary<PadControl, string>(profile.Labels));
        _profileName = profile.Name;
        _profileGame = profile.Game;
        _profileChanged = false;
        Changed?.Invoke();
    }

    /// <summary>The current controls as a profile, for Save as….</summary>
    public ControlProfile ToProfile(string name, string? game) => new(
        name.Trim(),
        string.IsNullOrWhiteSpace(game) ? null : game.Trim(),
        SensitivityStep,
        InvertY,
        Layout.Current.Where(p => p.Value.Count > 0).ToDictionary(p => p.Key, p => p.Value),
        new Dictionary<PadControl, string>(Volatile.Read(ref _labels)));

    public void ResetToDefault()
    {
        Layout.ResetToDefault();
        Volatile.Write(ref _step, DefaultStep);
        _invertY = false;
        Volatile.Write(ref _labels, NoLabels);
        _profileName = null;
        _profileGame = null;
        _profileChanged = false;
        Changed?.Invoke();
    }

    private void Edited()
    {
        if (_profileName is not null)
            _profileChanged = true;
        Changed?.Invoke();
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~ControlSettingsTests|FullyQualifiedName~InputMapperTests|FullyQualifiedName~OverlayTextTests"`
Expected: PASS (new and existing).

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Input/ControlSettings.cs tests/CouchLink.Core.Tests/ControlSettingsTests.cs tests/CouchLink.Core.Tests/InputMapperTests.cs
git commit -m "feat(input): load a profile into the controls, with action labels

Refs #56"
```

---

### Task 5: Labels and the profile name on the F1 panel

**Files:**
- Modify: `src/CouchLink.Core/Input/KeyNames.cs` (add `Labelled`)
- Modify: `src/CouchLink.Core/Video/OverlayText.cs` (`Controls`)
- Test: `tests/CouchLink.Core.Tests/KeyNamesTests.cs`, `tests/CouchLink.Core.Tests/OverlayTextTests.cs`

**Interfaces:**
- Consumes: `ControlSettings.ProfileName`, `ProfileChanged`, `LabelFor`, `Apply`, `SetLabel` (Task 4).
- Produces: `static string KeyNames.Labelled(PadControl control, string? label)`: `"Shoot (Square)"` with a label, `KeyNames.Of(control)` without. Used by the F1 panel and the editor (Task 7).

- [ ] **Step 1: Write the failing tests**

Append to `KeyNamesTests`:

```csharp
    [Fact]
    public void A_label_goes_before_the_control_name()
    {
        Assert.Equal("Shoot (Square)", KeyNames.Labelled(PadControl.Square, "Shoot"));
        Assert.Equal("D-pad up", KeyNames.Labelled(PadControl.DpadUp, null));
    }
```

Append to `OverlayTextTests`:

```csharp
    [Fact]
    public void The_built_in_layout_keeps_the_plain_title()
    {
        var text = OverlayText.Controls(new ControlSettings());
        Assert.StartsWith("Controls (F1 hides, Ctrl+Alt+C changes keys)\n", text);
    }

    [Fact]
    public void A_loaded_profile_is_named_in_the_title_and_shows_changes()
    {
        var settings = new ControlSettings();
        settings.Apply(new ControlProfile("2K22 Café", null, 5, false,
            KeyLayout.CreateDefault().Current, new Dictionary<PadControl, string>()));
        Assert.StartsWith("Controls: 2K22 Café (F1 hides, Ctrl+Alt+C changes keys)\n", OverlayText.Controls(settings));

        settings.SetSensitivityStep(9);
        Assert.StartsWith("Controls: 2K22 Café (changed) (F1 hides", OverlayText.Controls(settings));
    }

    [Fact]
    public void Labels_lead_their_rows_and_the_column_widens_to_fit()
    {
        var settings = new ControlSettings();
        settings.SetLabel(PadControl.Square, "Shoot from range");

        var text = OverlayText.Controls(settings);

        int width = "Shoot from range (Square)".Length + 2;
        Assert.Contains("Shoot from range (Square)".PadRight(width) + "J / Left click", text);
        Assert.Contains("Cross".PadRight(width) + "K", text);
        Assert.Contains("Right stick".PadRight(width) + "Mouse", text);
    }
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~KeyNamesTests|FullyQualifiedName~OverlayTextTests"`
Expected: build FAILS with `'KeyNames' does not contain a definition for 'Labelled'`.

- [ ] **Step 3: Implement**

In `KeyNames`, after `Of(PadControl control)`:

```csharp
    /// <summary>A control as the player sees it: "Shoot (Square)" when a profile names it, else "Square".</summary>
    public static string Labelled(PadControl control, string? label) =>
        label is null ? Of(control) : $"{label} ({Of(control)})";
```

In `OverlayText`, replace `Controls` with:

```csharp
    /// <summary>
    /// The F1 panel: the loaded profile, then every control and its keys, grouped as in the editor and
    /// led by its action label when it has one. Read when drawn.
    /// </summary>
    public static string Controls(ControlSettings settings)
    {
        const string Help = "(F1 hides, Ctrl+Alt+C changes keys)";
        string title = settings.ProfileName is { } profile
            ? $"Controls: {profile}{(settings.ProfileChanged ? " (changed)" : "")} {Help}"
            : $"Controls {Help}";
        var names = KeyNames.Groups
            .SelectMany(g => g.Controls)
            .ToDictionary(c => c, c => KeyNames.Labelled(c, settings.LabelFor(c)));
        int width = Math.Max(18, names.Values.Max(n => n.Length) + 2);

        var lines = new List<string> { title };
        foreach (var (_, controls) in KeyNames.Groups)
        {
            lines.Add("");
            foreach (var control in controls)
                lines.Add(names[control].PadRight(width) + KeyNames.Describe(settings.Layout, control));
        }
        lines.Add("");
        lines.Add("Right stick".PadRight(width) + $"Mouse (sensitivity {settings.SensitivityStep}{(settings.InvertY ? ", inverted" : "")})");
        return string.Join('\n', lines);
    }
```

(With no labels the longest name is "Left stick right", 16 characters, so the width stays 18 and the built-in panel reads exactly as before.)

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~KeyNamesTests|FullyQualifiedName~OverlayTextTests"`
Expected: PASS, including the existing `The_controls_panel_lists_every_control_with_its_current_keys`.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Input/KeyNames.cs src/CouchLink.Core/Video/OverlayText.cs tests/CouchLink.Core.Tests/KeyNamesTests.cs tests/CouchLink.Core.Tests/OverlayTextTests.cs
git commit -m "feat(input): the F1 panel shows the profile name and action labels

Refs #56"
```

---

### Task 6: Profile files on disk

**Files:**
- Create: `src/CouchLink.Core/Input/ProfileStore.cs`
- Test: `tests/CouchLink.Core.Tests/ProfileStoreTests.cs`

**Interfaces:**
- Consumes: `ProfileFile.Load`, `ProfileFile.Save`, `ProfileFile.MaxBytes`, `ProfileResult`, `ControlProfile` (Task 3).
- Produces:
  - `sealed record ProfileEntry(string Path, ControlProfile Profile, string DisplayName)`.
  - `sealed class ProfileStore(string folder, Action<string>? log = null)` with `string Folder`, `static string DefaultFolder`, `IReadOnlyList<ProfileEntry> List()`, `static ProfileResult Read(string path)`, `string? Write(string path, ControlProfile profile)` (null on success, else the message), `bool TryCreateFolder()`, `static string SuggestedFileName(string name)`.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/ProfileStoreTests.cs`:

```csharp
using System.Text;
using CouchLink.Core.Input;

namespace CouchLink.Core.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "couchlink-profiles-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _log = [];

    public ProfileStoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private ProfileStore Store() => new(_folder, _log.Add);

    private static string Json(string name) => $$"""{ "format": 1, "name": "{{name}}", "controls": { "Cross": { "keys": ["K"] } } }""";

    private void File(string name, string content) =>
        System.IO.File.WriteAllText(Path.Combine(_folder, name), content, new UTF8Encoding(false));

    private static ControlProfile Sample(string name) => new(name, "Game", 6, true,
        new Dictionary<PadControl, IReadOnlyList<ushort>> { [PadControl.Cross] = [VirtualKeys.Space] },
        new Dictionary<PadControl, string> { [PadControl.Cross] = "Pass" });

    [Fact]
    public void A_missing_folder_lists_nothing() =>
        Assert.Empty(new ProfileStore(Path.Combine(_folder, "nope"), _log.Add).List());

    [Fact]
    public void Lists_good_files_by_name_and_logs_why_bad_ones_are_skipped()
    {
        File("b.json", Json("Bravo"));
        File("a.json", Json("alpha"));
        File("broken.json", """{ "format": 1, "name": "X", "controls": { "Square": { "keys": ["Spcae"] } } }""");

        var entries = Store().List();

        Assert.Equal(["alpha", "Bravo"], entries.Select(e => e.DisplayName));
        Assert.Equal(Path.Combine(_folder, "a.json"), entries[0].Path);
        Assert.Contains(_log, line => line == "profile skipped: broken.json: \"Square\": unknown key \"Spcae\"");
    }

    [Fact]
    public void Only_top_level_json_files_are_listed()
    {
        File("good.json", Json("Good"));
        File("notes.txt", Json("Text"));
        File("good.json.1234.tmp", Json("Temp"));
        Directory.CreateDirectory(Path.Combine(_folder, "old"));
        System.IO.File.WriteAllText(Path.Combine(_folder, "old", "older.json"), Json("Older"));

        Assert.Equal(["Good"], Store().List().Select(e => e.DisplayName));
    }

    [Fact]
    public void Two_profiles_with_one_name_show_their_file_names()
    {
        File("2k22.json", Json("NBA 2K22"));
        File("2k22-alt.json", Json("nba 2k22"));

        Assert.Equal(["NBA 2K22 (2k22.json)", "nba 2k22 (2k22-alt.json)"],
            Store().List().Select(e => e.DisplayName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_file_over_64_KB_is_refused_before_reading()
    {
        File("big.json", Json("Big") + new string(' ', ProfileFile.MaxBytes));
        Assert.Equal("File is too large for a profile", ProfileStore.Read(Path.Combine(_folder, "big.json")).Error);
    }

    [Fact]
    public void A_missing_file_says_it_could_not_be_read() =>
        Assert.StartsWith("Couldn't read gone.json: ", ProfileStore.Read(Path.Combine(_folder, "gone.json")).Error);

    [Fact]
    public void A_file_with_a_byte_order_mark_and_accents_loads()
    {
        System.IO.File.WriteAllText(Path.Combine(_folder, "cafe.json"), Json("Café"), new UTF8Encoding(true));
        Assert.Equal("Café", ProfileStore.Read(Path.Combine(_folder, "cafe.json")).Profile?.Name);
    }

    [Fact]
    public void Write_then_Read_gives_the_same_profile()
    {
        string path = Path.Combine(_folder, "mine.json");
        Assert.Null(Store().Write(path, Sample("Mine")));
        ProfileAssert.Same(Sample("Mine"), ProfileStore.Read(path).Profile!);
    }

    [Fact]
    public void Write_replaces_an_existing_file_and_leaves_no_temp_file()
    {
        string path = Path.Combine(_folder, "mine.json");
        File("mine.json", Json("Old"));

        Assert.Null(Store().Write(path, Sample("New")));

        Assert.Equal("New", ProfileStore.Read(path).Profile?.Name);
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    [Fact]
    public void A_failed_write_says_why_logs_it_and_leaves_nothing()
    {
        string path = Path.Combine(_folder, "missing-folder", "mine.json");

        string? error = Store().Write(path, Sample("Mine"));

        Assert.StartsWith("Couldn't save mine.json: ", error);
        Assert.Single(_log);
        Assert.Empty(Directory.GetFiles(_folder, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void TryCreateFolder_makes_the_folder()
    {
        var store = new ProfileStore(Path.Combine(_folder, "profiles"), _log.Add);
        Assert.True(store.TryCreateFolder());
        Assert.True(Directory.Exists(store.Folder));
    }

    [Theory]
    [InlineData("NBA 2K22 Café", "NBA 2K22 Café.json")]
    [InlineData("2K22: back/room?", "2K22- back-room-.json")]
    [InlineData("  ...  ", "profile.json")]
    public void Suggested_file_names_are_safe(string name, string file) =>
        Assert.Equal(file, ProfileStore.SuggestedFileName(name));
}
```

- [ ] **Step 2: Run the tests to see them fail**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~ProfileStoreTests"`
Expected: build FAILS with `The type or namespace name 'ProfileStore' could not be found`.

- [ ] **Step 3: Implement**

`src/CouchLink.Core/Input/ProfileStore.cs`:

```csharp
using System.Text;

namespace CouchLink.Core.Input;

/// <summary>A profile file found in the profiles folder. <see cref="DisplayName"/> adds the file name when two profiles share a name.</summary>
public sealed record ProfileEntry(string Path, ControlProfile Profile, string DisplayName);

/// <summary>
/// Profile files on disk: the <c>profiles</c> folder next to the exe, and any file the player browses to.
/// Never throws for I/O: problems come back as messages, and skipped or failed files go to the log.
/// </summary>
public sealed class ProfileStore(string folder, Action<string>? log = null)
{
    private const string UnsafeFileNameChars = "\\/:*?\"<>|";

    /// <summary>The <c>profiles</c> folder next to CouchLink.App.exe.</summary>
    public static string DefaultFolder => Path.Combine(AppContext.BaseDirectory, "profiles");

    public string Folder { get; } = folder;

    /// <summary>Every profile that loads from the folder's top-level *.json files, sorted by name. Read fresh each call.</summary>
    public IReadOnlyList<ProfileEntry> List()
    {
        string[] files;
        try
        {
            if (!Directory.Exists(Folder))
                return [];
            files = Directory.GetFiles(Folder, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"profiles folder can't be read: {e.Message}");
            return [];
        }

        var found = new List<(string Path, ControlProfile Profile)>();
        foreach (var file in files)
        {
            var result = Read(file);
            if (result.Profile is { } profile)
                found.Add((file, profile));
            else
                log?.Invoke($"profile skipped: {Path.GetFileName(file)}: {result.Error}");
        }

        var shared = found
            .GroupBy(f => f.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return found
            .OrderBy(f => f.Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => Path.GetFileName(f.Path), StringComparer.OrdinalIgnoreCase)
            .Select(f => new ProfileEntry(f.Path, f.Profile,
                shared.Contains(f.Profile.Name) ? $"{f.Profile.Name} ({Path.GetFileName(f.Path)})" : f.Profile.Name))
            .ToList();
    }

    /// <summary>Reads one profile file: refused above 64 KB, UTF-8 with or without a byte-order mark.</summary>
    public static ProfileResult Read(string path)
    {
        try
        {
            if (new FileInfo(path).Length > ProfileFile.MaxBytes)
                return ProfileResult.Failure("File is too large for a profile");
            return ProfileFile.Load(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return ProfileResult.Failure($"Couldn't read {Path.GetFileName(path)}: {e.Message}");
        }
    }

    /// <summary>Writes through a temp file in the same folder, so a failed save never leaves half a file. Null, or why it failed.</summary>
    public string? Write(string path, ControlProfile profile)
    {
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, ProfileFile.Save(profile), new UTF8Encoding(false));
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException) { }
            log?.Invoke($"profile not saved: {path}: {e.Message}");
            return $"Couldn't save {Path.GetFileName(path)}: {e.Message}";
        }
    }

    /// <summary>Creates the folder for the first Save as…; false (and logged) when it can't be created.</summary>
    public bool TryCreateFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"profiles folder can't be created: {e.Message}");
            return false;
        }
    }

    /// <summary>A file name for a profile name: characters Windows refuses become '-'.</summary>
    public static string SuggestedFileName(string name)
    {
        var chars = name.Trim().Select(c => c < ' ' || UnsafeFileNameChars.Contains(c) ? '-' : c).ToArray();
        string stem = new string(chars).Trim(' ', '.');
        return (stem.Length == 0 ? "profile" : stem) + ".json";
    }
}
```

- [ ] **Step 4: Run the tests to see them pass**

Run: `dotnet test tests/CouchLink.Core.Tests -c Release --filter "FullyQualifiedName~ProfileStoreTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Input/ProfileStore.cs tests/CouchLink.Core.Tests/ProfileStoreTests.cs
git commit -m "feat(input): list, read and safely write profile files

Refs #56"
```

---

### Task 7: Profiles in the controls editor

**Files:**
- Create: `src/CouchLink.App/SaveProfileDialog.cs`
- Modify: `src/CouchLink.App/ControlsWindow.cs`

**Interfaces:**
- Consumes: `ProfileStore`, `ProfileStore.DefaultFolder`, `ProfileStore.Read`, `ProfileStore.SuggestedFileName`, `ProfileEntry` (Task 6); `ProfileFile.MaxNameLength/MaxGameLength/MaxLabelLength` (Task 3); `ControlSettings.Apply/SetLabel/LabelFor/ToProfile/ProfileName/ProfileGame/ProfileChanged` (Task 4); `KeyNames.Labelled` (Task 5); `AppServices.Log.Write(string)`, `AppServices.Controls` (existing).
- Produces: UI only.

No unit tests here (the App has no test project, as before); the manual checks in Step 6 and the gate rows in Task 8 cover it.

- [ ] **Step 1: Add the Save as… details dialog**

`src/CouchLink.App/SaveProfileDialog.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using CouchLink.Core.Input;

namespace CouchLink.App;

/// <summary>Save as…: the profile's name (shown in every PC's list) and, optionally, the game it is for.</summary>
internal sealed class SaveProfileDialog : Window
{
    private readonly TextBox _name = new() { MaxLength = ProfileFile.MaxNameLength };
    private readonly TextBox _game = new() { MaxLength = ProfileFile.MaxGameLength, Margin = new Thickness(0, 0, 0, 12) };

    public SaveProfileDialog(string? name, string? game)
    {
        Title = "Save profile";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        _name.Text = name ?? "";
        _game.Text = game ?? "";

        var save = new Button { Content = "Save", IsDefault = true, MinWidth = 80, IsEnabled = ProfileName.Length > 0 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80, Margin = new Thickness(8, 0, 0, 0) };
        _name.TextChanged += (_, _) => save.IsEnabled = ProfileName.Length > 0;
        save.Click += (_, _) => DialogResult = true;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock { Text = "Name (shown in the profile list)", Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(_name);
        root.Children.Add(new TextBlock { Text = "Game (optional)", Margin = new Thickness(0, 8, 0, 4) });
        root.Children.Add(_game);
        root.Children.Add(buttons);
        Content = root;
        Loaded += (_, _) =>
        {
            _name.Focus();
            _name.SelectAll();
        };
    }

    public string ProfileName => _name.Text.Trim();

    public string? Game => string.IsNullOrWhiteSpace(_game.Text) ? null : _game.Text.Trim();
}
```

- [ ] **Step 2: Add the profile row, label boxes and their fields to `ControlsWindow`**

At the top of `ControlsWindow.cs`, add the usings:

```csharp
using System.IO;
using Microsoft.Win32;
```

Update the class summary to:

```csharp
/// <summary>
/// ⚙ Controls: every DS4 control with its keys. Click a row, then press a key or mouse button to bind
/// it (Esc cancels). A profile row on top loads a profile from the profiles folder or a file and saves
/// the current controls as one; Show labels adds a box per row for the game's name of each button.
/// Mouse sensitivity, Invert Y and Reset to default. Edits go straight into
/// <see cref="AppServices.Controls"/>, so they count mid-game. One window at a time.
/// </summary>
```

Add these members next to the existing fields:

```csharp
    private const string FileFilter = "CouchLink profile (*.json)|*.json";
    private const double NarrowWidth = 440, WideWidth = 600;

    /// <summary>The profile file loaded last this run. <see cref="ControlSettings.ProfileName"/> says whether it still is.</summary>
    private static ProfileChoice? _loaded;

    private readonly ProfileStore _store = new(ProfileStore.DefaultFolder, AppServices.Log.Write);
    private readonly ComboBox _profile = new() { MinWidth = 180 };
    private readonly CheckBox _showLabels = new() { Content = "Show labels (what each button does in the game)", Margin = new Thickness(0, 8, 0, 0) };
    private readonly Dictionary<PadControl, TextBox> _labelBoxes = [];
    private IReadOnlyList<ProfileEntry> _entries = [];
    private bool _updatingProfiles;

    private sealed record ProfileChoice(string Text, string? Path)
    {
        public static readonly ProfileChoice Default = new("Default layout", null);
        public override string ToString() => Text;
    }
```

In the constructor, change `Width = 440;` to `Width = NarrowWidth;`.

Replace the inner loop that builds each row (the `foreach (var control in controls)` block) with:

```csharp
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

                var label = new TextBox
                {
                    MaxLength = ProfileFile.MaxLabelLength,
                    Width = 140,
                    Margin = new Thickness(6, 1, 0, 1),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed,
                    ToolTip = "What this button does in the game, e.g. Shoot",
                };
                label.LostFocus += (_, _) => CommitLabel(c);
                label.KeyDown += (_, e) =>
                {
                    if (e.Key != Key.Enter)
                        return;
                    CommitLabel(c);
                    e.Handled = true;
                };
                _labelBoxes[control] = label;

                var line = new DockPanel();
                DockPanel.SetDock(label, Dock.Right);
                line.Children.Add(label);
                line.Children.Add(row);
                list.Children.Add(line);
            }
```

Before `var root = new StackPanel { Margin = new Thickness(16) };`, build the profile row:

```csharp
        var browse = new Button { Content = "Browse…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => Browse();
        var saveAs = new Button { Content = "Save as…", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        saveAs.Click += (_, _) => SaveAs();
        _profile.SelectionChanged += (_, _) => OnProfileChosen();
        _showLabels.Click += (_, _) => ShowLabels(_showLabels.IsChecked == true);

        var profileLabel = new TextBlock { Text = "Profile:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var profileRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(profileLabel, Dock.Left);
        DockPanel.SetDock(saveAs, Dock.Right);
        DockPanel.SetDock(browse, Dock.Right);
        profileRow.Children.Add(profileLabel);
        profileRow.Children.Add(saveAs);
        profileRow.Children.Add(browse);
        profileRow.Children.Add(_profile);
```

Change the start of the `root` children to put the profile row first and Show labels under the instruction:

```csharp
        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(profileRow);
        root.Children.Add(new TextBlock { Text = "Click a control, then press the key or mouse button for it.", TextWrapping = TextWrapping.Wrap });
        root.Children.Add(_showLabels);
        root.Children.Add(new ScrollViewer { Content = list, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
```

(the rest of `root` stays as it is).

Replace the existing `Closed += ...` handler block with one that also commits labels, and add a `Closing` handler before it:

```csharp
        Closing += (_, _) =>
        {
            foreach (var control in _labelBoxes.Keys)
                CommitLabel(control); // a label typed just before closing is kept
        };
        Closed += (_, _) =>
        {
            _settings.Changed -= OnSettingsChanged;
            _messageTimer.Stop();
        };
        _entries = _store.List();
        Refresh();
```

(Remove the old final `Refresh();` call so it runs once, after `_entries` is read.)

- [ ] **Step 3: Update `Refresh` and the message line**

Replace `Refresh` with:

```csharp
    private void Refresh()
    {
        foreach (var (control, row) in _rows)
        {
            string name = KeyNames.Labelled(control, _settings.LabelFor(control));
            row.Content = Row(name, _listening == control ? Listening : KeyNames.Describe(_settings.Layout, control));
        }
        foreach (var (control, box) in _labelBoxes)
            if (!box.IsKeyboardFocusWithin) // don't overwrite what is being typed
                box.Text = _settings.LabelFor(control) ?? "";
        _sensitivity.Value = _settings.SensitivityStep;
        _invertY.IsChecked = _settings.InvertY;
        RefreshProfiles();
    }
```

Replace `ShowMessage` with:

```csharp
    private void ShowMessage(string text, double seconds = 3)
    {
        _message.Text = text;
        _messageTimer.Stop();
        _messageTimer.Interval = TimeSpan.FromSeconds(seconds);
        _messageTimer.Start();
    }

    /// <summary>Errors stay long enough to read a file problem such as "Square": unknown key "Spcae".</summary>
    private void ShowError(string text) => ShowMessage(text, 10);
```

- [ ] **Step 4: Add the profile actions**

Add these methods to `ControlsWindow`:

```csharp
    /// <summary>Rebuilds the dropdown: Default layout, the folder's profiles, and the loaded file if it is elsewhere.</summary>
    private void RefreshProfiles()
    {
        var choices = new List<ProfileChoice> { ProfileChoice.Default };
        choices.AddRange(_entries.Select(e => new ProfileChoice(e.DisplayName, e.Path)));
        int selected = 0;
        if (_settings.ProfileName is not null && _loaded is { Path: { } loadedPath } loaded)
        {
            selected = choices.FindIndex(c => c.Path is not null && SamePath(c.Path, loadedPath));
            if (selected < 0)
            {
                choices.Add(loaded);
                selected = choices.Count - 1;
            }
            if (_settings.ProfileChanged)
                choices[selected] = choices[selected] with { Text = choices[selected].Text + " (changed)" };
        }
        _updatingProfiles = true;
        try
        {
            _profile.ItemsSource = choices;
            _profile.SelectedIndex = selected;
        }
        finally
        {
            _updatingProfiles = false;
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private void OnProfileChosen()
    {
        if (_updatingProfiles || _profile.SelectedItem is not ProfileChoice choice)
            return;
        _listening = null;
        if (choice.Path is null)
        {
            _settings.ResetToDefault();
            ShowMessage("Back to the default controls.");
            return;
        }
        Load(choice.Path);
    }

    /// <summary>Reads the file again (it may have changed since the list was made) and applies it, or says why not.</summary>
    private void Load(string path)
    {
        var result = ProfileStore.Read(path);
        if (result.Profile is not { } profile)
        {
            AppServices.Log.Write($"profile not loaded: {path}: {result.Error}");
            ShowError(result.Error!);
            RefreshProfiles(); // the dropdown goes back to what is really loaded
            return;
        }
        _loaded = new ProfileChoice(profile.Name, path);
        _settings.Apply(profile);
        ShowMessage($"Loaded {profile.Name}.");
    }

    private void Browse()
    {
        _listening = null;
        Refresh();
        var dialog = new OpenFileDialog { Title = "Load a controller profile", Filter = FileFilter };
        if (Directory.Exists(_store.Folder))
            dialog.InitialDirectory = _store.Folder;
        if (dialog.ShowDialog(this) == true)
            Load(dialog.FileName);
    }

    private void SaveAs()
    {
        _listening = null;
        Refresh();
        var details = new SaveProfileDialog(_settings.ProfileName, _settings.ProfileGame) { Owner = this, Topmost = Topmost };
        if (details.ShowDialog() != true)
            return;
        var dialog = new SaveFileDialog
        {
            Title = "Save controller profile",
            Filter = FileFilter,
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = ProfileStore.SuggestedFileName(details.ProfileName),
            InitialDirectory = _store.TryCreateFolder()
                ? _store.Folder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var profile = _settings.ToProfile(details.ProfileName, details.Game);
        if (_store.Write(dialog.FileName, profile) is { } error)
        {
            ShowError(error);
            return;
        }
        _entries = _store.List();
        _loaded = new ProfileChoice(profile.Name, dialog.FileName);
        _settings.Apply(profile); // the saved file is now the loaded profile, unchanged
        ShowMessage($"Saved {Path.GetFileName(dialog.FileName)}.");
    }

    private void ShowLabels(bool show)
    {
        foreach (var box in _labelBoxes.Values)
            box.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Width = show ? WideWidth : NarrowWidth;
    }

    private void CommitLabel(PadControl control) => _settings.SetLabel(control, _labelBoxes[control].Text);
```

- [ ] **Step 5: Build**

Run: `dotnet build -c Release`
Expected: `0 Warning(s)` and `0 Error(s)`.

- [ ] **Step 6: Check it by hand**

Run the app windowed on one PC: `src/CouchLink.App/bin/Release/net10.0-windows/CouchLink.App.exe`. Before starting it, create `src/CouchLink.App/bin/Release/net10.0-windows/profiles/` holding the spec example (section 2 of the spec) as `2k22.json` and a copy with `"Spcae"` as one key, named `broken.json`.
1. ⚙ Controls: the dropdown lists "Default layout" and "NBA 2K22 Café layout" only; `%LOCALAPPDATA%\CouchLink\Logs` has `profile skipped: broken.json: "Square": unknown key "Spcae"`.
2. Choose the profile: rows show `Shoot (Square)` with `J / Left click`, Cross shows `K` and Pass, Circle shows `(none)`; sensitivity 6; message "Loaded NBA 2K22 Café layout.".
3. Rebind Cross: the dropdown reads "NBA 2K22 Café layout (changed)".
4. Show labels, type `Rebound` on Circle and close the window at once; reopen: Circle's label is Rebound.
5. Save as… with Name "Test", save as `test.json`: the dropdown lists Test and has it selected; the file matches spec section 2's shape.
6. Browse… to `broken.json`: the message shows `"Square": unknown key "Spcae"` for about 10 s and nothing changes.
7. Reset to default: the dropdown shows "Default layout", no labels, built-in keys.

- [ ] **Step 7: Commit**

```bash
git add src/CouchLink.App/SaveProfileDialog.cs src/CouchLink.App/ControlsWindow.cs
git commit -m "feat(app): load, browse and save controller profiles in the controls editor

Refs #56"
```

---

### Task 8: Docs

**Files:**
- Modify: `docs/cafe-setup-guide.md` (section 7), `docs/superpowers/specs/2026-10-05-couchlink-design.md` (6.7 and the non-goals near line 54), `docs/superpowers/specs/2026-10-08-couchlink-controller-profiles-design.md` (status line), `docs/gate-results.md` (new section at the end), `README.md` (Features)

- [ ] **Step 1: Setup guide**

In `docs/cafe-setup-guide.md`, after the "Changing keys" subsection's last paragraph ("Changes apply at once, ... starts with the default layout."), add:

```markdown
### Profiles

A profile is a key layout for one game, saved as a file. Players pick one
from the **Profile** list at the top of the controls editor instead of
changing keys one by one.

- **Set them up once:** make a `profiles` folder next to
  `CouchLink.App.exe` and put the profile files in it. Copy the folder to
  every PC. The list reads the folder each time the editor opens.
- **Make one:** change the keys in the editor, then click **Save as…**,
  give it a name (this is what players see in the list) and save it in the
  `profiles` folder.
- **Name the buttons:** tick **Show labels** and type what each button
  does in the game, such as "Shoot" or "Pass". The labels are saved with
  the profile, and F1 shows them during a game.
- **Load one from elsewhere:** **Browse…** opens a profile from a USB stick
  or any folder.
- A profile that doesn't load is left out of the list. The editor (for
  **Browse…**) or the log (for the `profiles` folder) says why, for example
  `"Square": unknown key "Spcae"`.

Changes made after loading a profile show "(changed)" and don't change the
file. **Reset to default** goes back to the layout above. As with any key
change, everything resets when CouchLink closes.

Profiles are JSON files that can also be edited in Notepad. The design
document lists every field and key name:
[controller profiles design](superpowers/specs/2026-10-08-couchlink-controller-profiles-design.md#2-file-format).
```

- [ ] **Step 2: Main design**

In `docs/superpowers/specs/2026-10-05-couchlink-design.md` section 6.7, add before the sentence `Details: [playing screen & controls`:

```markdown
**Profiles:** a dropdown lists the profile files in a `profiles\` folder
next to the exe; **Browse…** loads one from anywhere; **Save as…** writes
the current controls as one. A profile can name each button for its game
("Shoot"), shown in the editor and on the F1 panel. Loading a profile is a
choice, not saved state: it resets with everything else when CouchLink
closes. Details: [controller profiles
design](2026-10-08-couchlink-controller-profiles-design.md).
```

In the non-goals list, replace the two lines

```markdown
- Persisting custom key layouts (server share, profiles).
- Per-game key presets; staff/admin control panel; session billing.
```

with

```markdown
- Saving the player's layout automatically between runs; sharing profiles
  over the network (profile files are covered by the controller profiles
  design).
- Staff/admin control panel; session billing.
```

- [ ] **Step 3: Spec status**

In the controller profiles design, change `Status: Draft, awaiting review` to `Status: Approved; ProfileStore lives in CouchLink.Core (plan 9) so it is unit-tested`.

- [ ] **Step 4: Gate checklist**

Append to `docs/gate-results.md`:

```markdown
## Controller profiles (Plan 9)

Date: <fill in>
Client PC: <fill in>

| Check | Result |
|---|---|
| Two profiles and one broken file in `profiles\`: the two are listed, the broken one isn't, and the log says why | <pass/fail> |
| Load a profile mid-game in NBA 2K22 (Ctrl+Alt+C): the new keys work at once; F1 shows the profile name and labels | <pass/fail> |
| Rebind a key after loading: the list and F1 show "(changed)"; the file on disk is unchanged | <pass/fail> |
| Type a label and close the editor at once: the label is kept | <pass/fail> |
| Type labels, Save as…, copy the file to another PC, load it there: same keys, labels, sensitivity and Invert Y | <pass/fail> |
| Browse… to a profile on a USB stick: it loads; a broken one shows its message | <pass/fail> |
| Open the editor with Ctrl+Alt+C and click Save as…: both dialogs open in front of the game | <pass/fail> |
| Reset to default after loading: built-in keys, no labels, no profile name | <pass/fail> |
| Close and restart CouchLink: built-in layout | <pass/fail> |
```

- [ ] **Step 5: README**

In `README.md` Features, after the "Built for shared PCs" bullet, add:

```markdown
- **Controller profiles**: save a key layout for a game as a file, put it
  in the `profiles` folder on every PC, and players pick it from a list.
  Profiles can name each button ("Shoot", "Pass") on the F1 panel.
```

- [ ] **Step 6: Final check**

Run: `dotnet build -c Release` then `dotnet test -c Release --no-build`
Expected: `0 Warning(s) 0 Error(s)`, all tests PASS.

- [ ] **Step 7: Commit**

```bash
git add docs README.md
git commit -m "docs: controller profiles in the setup guide, main design, gate checklist and README

Closes #56"
```
