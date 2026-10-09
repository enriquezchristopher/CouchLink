# Right-Stick Keys Implementation Plan (#59)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players bind keys to the four right-stick directions; held keys override the mouse, and the mouse drives the stick whenever no right-stick key is held.

**Architecture:** Four new `PadControl` members and a "Right stick" group in `KeyNames.Groups`, which already drives the editor rows, the F1 panel and profile save order. `InputMapper.Tick` picks keys or mouse for the right stick. `OverlayText.Controls` hides the right-stick rows until one has a key. Profile files pick up the names from the enum with no reader changes.

**Tech Stack:** C# / .NET 10, xUnit, WPF (editor only), System.Text.Json.

**Spec:** [docs/superpowers/specs/2026-10-09-couchlink-right-stick-keys-design.md](../specs/2026-10-09-couchlink-right-stick-keys-design.md)

## Global Constraints

- Control names in profile files: `RightUp`, `RightDown`, `RightLeft`, `RightRight`. Profile `format` stays **1**.
- Display names: "Right stick up", "Right stick down", "Right stick left", "Right stick right".
- The default layout gives the four new controls **no keys**; the default F1 panel must be byte-for-byte unchanged.
- Keys win: while any right-stick key is held, the stick comes from the keys and the mouse's stored movement is reset.
- Commits use Conventional Commit types (see CONTRIBUTING.md) and carry **no** Claude co-author line.
- Never mention Sunshine anywhere.
- Run tests with `dotnet test tests/CouchLink.Core.Tests` from the repo root.

## Review Focus

1. **Default F1 column width.** "Right stick right" is 17 characters, so computing the column width over every control widens the default panel from 18 to 19 even when the rows are hidden. Expect the default panel unchanged → width computed over shown rows only (test in Task 3).
2. **Opposite keys held with the mouse moving.** Num 8 + Num 2 held cancel to centre; the mouse must still be ignored (keys are held), not leak through → Task 2 test.
3. **Mouse moved before a key is pressed.** A flick in progress when Num 8 goes down must not come back when Num 8 is released → stick centred after release (Task 2 test).
4. **A key already on a button.** Binding Num 8 to `RightUp` when it's on Square takes it off Square, and the editor says so → Task 1 test.
5. **Hand-written profile with a label but no keys on a right-stick control.** It loads; F1 keeps the rows hidden (no keys) → Task 3 test.

---

### Task 1: Right-stick controls, names and profile files

**Files:**
- Modify: `src/CouchLink.Core/Input/PadControl.cs`
- Modify: `src/CouchLink.Core/Input/KeyNames.cs:23-31` (Groups) and `:44-54` (`Of`)
- Test: `tests/CouchLink.Core.Tests/KeyNamesTests.cs`, `tests/CouchLink.Core.Tests/ProfileFileTests.cs`, `tests/CouchLink.Core.Tests/KeyLayoutTests.cs`

**Interfaces:**
- Produces: `PadControl.RightUp/RightDown/RightLeft/RightRight`; `KeyNames.RightStick` (`PadControl[]`, the four in that order, the same array instance used in `Groups`); `KeyNames.Of(PadControl.RightUp) == "Right stick up"` etc.

- [ ] **Step 1: Write the failing tests**

Append to `KeyNamesTests.cs` (inside the class):

```csharp
    [Theory]
    [InlineData(PadControl.RightUp, "Right stick up")]
    [InlineData(PadControl.RightDown, "Right stick down")]
    [InlineData(PadControl.RightLeft, "Right stick left")]
    [InlineData(PadControl.RightRight, "Right stick right")]
    public void Right_stick_directions_have_names(PadControl control, string name)
    {
        Assert.Equal(name, KeyNames.Of(control));
    }

    [Fact]
    public void The_right_stick_group_follows_the_left_stick()
    {
        Assert.Equal(["Left stick", "Right stick", "D-pad", "Buttons", "Shoulders", "Stick clicks", "Menu"],
            KeyNames.Groups.Select(g => g.Name));
        Assert.Same(KeyNames.RightStick, KeyNames.Groups[1].Controls);
        Assert.Equal([PadControl.RightUp, PadControl.RightDown, PadControl.RightLeft, PadControl.RightRight], KeyNames.RightStick);
    }

    [Fact]
    public void The_default_layout_has_no_right_stick_keys()
    {
        var layout = KeyLayout.CreateDefault();
        foreach (var control in KeyNames.RightStick)
            Assert.Empty(layout.KeysFor(control));
    }
```

Append to `ProfileFileTests.cs`:

```csharp
    /// <summary>Profile files name controls by these; renaming one breaks every profile that uses it.</summary>
    [Fact]
    public void Control_names_are_pinned()
    {
        string[] expected =
        [
            "LeftUp", "LeftDown", "LeftLeft", "LeftRight",
            "RightUp", "RightDown", "RightLeft", "RightRight",
            "DpadUp", "DpadDown", "DpadLeft", "DpadRight",
            "Cross", "Circle", "Square", "Triangle",
            "L1", "R1", "L2", "R2", "L3", "R3",
            "Options", "Share", "Touchpad",
        ];
        Assert.Equal(expected, Enum.GetNames<PadControl>());
    }

    [Fact]
    public void Right_stick_controls_load_with_keys_and_labels()
    {
        var p = Loaded(WithControls("'RightUp':{'keys':['Num8'],'label':'Pro stick up'},'RightRight':{'keys':['Num6']}"));
        Assert.Equal([(ushort)0x68], p.Keys[PadControl.RightUp]);
        Assert.Equal([(ushort)0x66], p.Keys[PadControl.RightRight]);
        Assert.Equal("Pro stick up", p.Labels[PadControl.RightUp]);
    }

    [Fact]
    public void A_file_without_right_stick_controls_gives_the_right_stick_no_keys()
    {
        var settings = new ControlSettings();
        settings.Apply(Loaded(SpecExample));
        foreach (var control in KeyNames.RightStick)
            Assert.Empty(settings.Layout.KeysFor(control));
    }

    [Fact]
    public void Right_stick_controls_save_after_the_left_stick_and_load_back()
    {
        var keys = new Dictionary<PadControl, IReadOnlyList<ushort>>
        {
            [PadControl.LeftUp] = [VirtualKeys.Letter('W')],
            [PadControl.RightUp] = [0x68],
            [PadControl.DpadUp] = [VirtualKeys.Up],
        };
        var profile = new ControlProfile("T", null, 5, false, keys,
            new Dictionary<PadControl, string> { [PadControl.RightUp] = "Pro stick up" });

        string json = ProfileFile.Save(profile);

        Assert.True(json.IndexOf("\"LeftUp\"", StringComparison.Ordinal) < json.IndexOf("\"RightUp\"", StringComparison.Ordinal));
        Assert.True(json.IndexOf("\"RightUp\"", StringComparison.Ordinal) < json.IndexOf("\"DpadUp\"", StringComparison.Ordinal));
        Assert.Contains("\"Num8\"", json);
        Assert.DoesNotContain("\"RightDown\"", json); // no keys, no label: left out
        ProfileAssert.Same(profile, Loaded(json));
    }
```

Append to `KeyLayoutTests.cs`:

```csharp
    [Fact]
    public void Binding_a_right_stick_direction_takes_the_key_off_a_button()
    {
        var layout = KeyLayout.CreateDefault();
        var result = layout.Bind(PadControl.RightUp, VirtualKeys.Letter('K'));
        Assert.Equal(new BindResult(true, PadControl.Cross), result);
        Assert.Equal([VirtualKeys.Letter('K')], layout.KeysFor(PadControl.RightUp));
        Assert.Empty(layout.KeysFor(PadControl.Cross));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests`
Expected: build error, `'PadControl' does not contain a definition for 'RightUp'` and `'KeyNames' does not contain a definition for 'RightStick'`.

- [ ] **Step 3: Implement**

`src/CouchLink.Core/Input/PadControl.cs`:

```csharp
namespace CouchLink.Core.Input;

/// <summary>
/// Every DS4 control a key can be bound to. The right stick also follows the mouse while none of its keys is held.
/// Profile files use these names: renaming one breaks every profile that uses it.
/// </summary>
public enum PadControl
{
    LeftUp, LeftDown, LeftLeft, LeftRight,
    RightUp, RightDown, RightLeft, RightRight,
    DpadUp, DpadDown, DpadLeft, DpadRight,
    Cross, Circle, Square, Triangle,
    L1, R1, L2, R2, L3, R3,
    Options, Share, Touchpad,
}
```

In `KeyNames.cs`, above `Groups` (it must come first: static properties initialise in file order, and `Groups` reads it):

```csharp
    /// <summary>The right stick's four directions. They have no keys by default: the mouse drives the stick.</summary>
    public static PadControl[] RightStick { get; } =
        [PadControl.RightUp, PadControl.RightDown, PadControl.RightLeft, PadControl.RightRight];
```

and in `Groups`, after the "Left stick" line:

```csharp
        ("Right stick", RightStick),
```

In `KeyNames.Of`, after the `LeftRight` arm:

```csharp
        PadControl.RightUp => "Right stick up",
        PadControl.RightDown => "Right stick down",
        PadControl.RightLeft => "Right stick left",
        PadControl.RightRight => "Right stick right",
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests`
Expected: all pass. `OverlayTextTests.The_controls_panel_lists_every_control_with_its_current_keys` still passes here (F1 lists every group); Task 3 changes it.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Input/PadControl.cs src/CouchLink.Core/Input/KeyNames.cs tests/CouchLink.Core.Tests/KeyNamesTests.cs tests/CouchLink.Core.Tests/ProfileFileTests.cs tests/CouchLink.Core.Tests/KeyLayoutTests.cs
git commit -m "feat(input): right-stick directions as bindable controls, in profile files too"
```

---

### Task 2: Held right-stick keys set the stick; the mouse takes over when they're released

**Files:**
- Modify: `src/CouchLink.Core/Input/InputMapper.cs` (`Tick`)
- Test: `tests/CouchLink.Core.Tests/InputMapperTests.cs`

**Interfaces:**
- Consumes: `PadControl.RightUp/RightDown/RightLeft/RightRight` (Task 1), `MouseStick.Reset()`, `StickMath.FromDirections(bool up, bool down, bool left, bool right)`.
- Produces: no new API; `InputMapper.Tick` behaviour only.

- [ ] **Step 1: Write the failing tests**

Append to `InputMapperTests.cs`:

```csharp
    private const ushort Num8 = 0x68, Num2 = 0x62, Num4 = 0x64, Num6 = 0x66;

    /// <summary>The default layout plus the NBA 2K22 pro stick: Num 8/2/4/6 on the right stick.</summary>
    private static InputMapper RightKeysMapper()
    {
        var layout = KeyLayout.CreateDefault();
        layout.Bind(PadControl.RightUp, Num8);
        layout.Bind(PadControl.RightDown, Num2);
        layout.Bind(PadControl.RightLeft, Num4);
        layout.Bind(PadControl.RightRight, Num6);
        return new InputMapper(layout, new MouseStick());
    }

    [Theory]
    [InlineData(Num8, 128, 1)]
    [InlineData(Num2, 128, 255)]
    [InlineData(Num4, 1, 128)]
    [InlineData(Num6, 255, 128)]
    public void A_right_stick_key_gives_full_tilt(ushort key, int rx, int ry)
    {
        var m = RightKeysMapper();
        m.KeyDown(key);
        var s = m.Tick(0.001);
        Assert.Equal(((byte)rx, (byte)ry), (s.RX, s.RY));
        Assert.Equal((PadState.Center, PadState.Center), (s.LX, s.LY));
    }

    [Fact]
    public void Two_right_stick_keys_give_a_diagonal()
    {
        var m = RightKeysMapper();
        m.KeyDown(Num8);
        m.KeyDown(Num6);
        var s = m.Tick(0.001);
        Assert.Equal(((byte)218, (byte)38), (s.RX, s.RY));
    }

    [Fact]
    public void Held_right_stick_keys_win_over_the_mouse()
    {
        var m = RightKeysMapper();
        m.KeyDown(Num8);
        m.MouseMove(40, 0);
        var s = m.Tick(0.001);
        Assert.Equal(((byte)128, (byte)1), (s.RX, s.RY));
    }

    [Fact]
    public void Opposite_right_stick_keys_cancel_and_still_hold_off_the_mouse()
    {
        var m = RightKeysMapper();
        m.KeyDown(Num8);
        m.KeyDown(Num2);
        m.MouseMove(25, 0);
        var s = m.Tick(0.001);
        Assert.Equal((PadState.Center, PadState.Center), (s.RX, s.RY));
    }

    [Fact]
    public void Releasing_the_keys_centres_the_stick_and_the_mouse_takes_over()
    {
        var m = RightKeysMapper();
        m.MouseMove(25, 0); // a flick already in progress
        m.KeyDown(Num8);
        m.MouseMove(40, 0); // bumped while the key is held
        m.Tick(0.001);
        m.KeyUp(Num8);

        var released = m.Tick(0.001);
        Assert.Equal((PadState.Center, PadState.Center), (released.RX, released.RY));

        m.MouseMove(25, 0);
        var mouse = m.Tick(0.001);
        Assert.Equal(((byte)192, (byte)128), (mouse.RX, mouse.RY));
    }

    [Fact]
    public void Without_right_stick_keys_the_mouse_drives_the_stick()
    {
        var m = NewMapper(); // default layout: no right-stick keys
        m.KeyDown(Num8);
        m.MouseMove(25, 0);
        var s = m.Tick(0.001);
        Assert.Equal(((byte)192, (byte)128), (s.RX, s.RY));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~InputMapperTests"`
Expected: FAIL. The key tests get `(128, 128)` (keys ignored); `Held_right_stick_keys_win_over_the_mouse` gets `(230, 128)`. `Without_right_stick_keys_the_mouse_drives_the_stick` already passes.

- [ ] **Step 3: Implement**

In `InputMapper.Tick`, replace `var (rx, ry) = _mouse.Update(dtSeconds);` with:

```csharp
            bool rightUp = IsDown(layout, PadControl.RightUp), rightDown = IsDown(layout, PadControl.RightDown);
            bool rightLeft = IsDown(layout, PadControl.RightLeft), rightRight = IsDown(layout, PadControl.RightRight);
            byte rx, ry;
            if (rightUp || rightDown || rightLeft || rightRight)
            {
                // Keys win: a key move always does the same thing, even if the mouse gets bumped. Dropping the
                // mouse's movement means releasing the keys centres the stick instead of replaying a flick.
                (rx, ry) = StickMath.FromDirections(rightUp, rightDown, rightLeft, rightRight);
                _mouse.Reset();
            }
            else
            {
                (rx, ry) = _mouse.Update(dtSeconds);
            }
```

Update the class summary's first line to: `Tracks held keys and mouse movement and turns them into a DS4 state. Right-stick keys, while held, override the mouse.`

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Input/InputMapper.cs tests/CouchLink.Core.Tests/InputMapperTests.cs
git commit -m "feat(input): held right-stick keys set the stick, the mouse takes over when they're released"
```

---

### Task 3: F1 lists the right-stick rows only when one has a key

**Files:**
- Modify: `src/CouchLink.Core/Video/OverlayText.cs` (`Controls`)
- Test: `tests/CouchLink.Core.Tests/OverlayTextTests.cs`

**Interfaces:**
- Consumes: `KeyNames.RightStick`, `KeyNames.Groups` (Task 1); `ControlSettings.Layout`, `ControlSettings.SetLabel`, `ControlSettings.Bind`.

- [ ] **Step 1: Write the failing tests and update the existing one**

In `The_controls_panel_lists_every_control_with_its_current_keys`, change the loop to skip the hidden rows:

```csharp
        foreach (var control in Enum.GetValues<PadControl>().Except(KeyNames.RightStick))
            Assert.Contains(KeyNames.Of(control), text);
```

Append:

```csharp
    [Fact]
    public void The_default_panel_hides_the_right_stick_keys_and_keeps_its_width()
    {
        var text = OverlayText.Controls(new ControlSettings());

        Assert.DoesNotContain("Right stick up", text);
        Assert.Contains($"{"Cross",-18}K\n", text);
        Assert.EndsWith($"{"Right stick",-18}Mouse (sensitivity {ControlSettings.DefaultStep})", text);
    }

    [Fact]
    public void One_right_stick_key_lists_all_four_directions()
    {
        var settings = new ControlSettings();
        settings.Bind(PadControl.RightUp, 0x68);
        settings.SetLabel(PadControl.RightUp, "Pro stick up");

        var text = OverlayText.Controls(settings);

        int width = "Pro stick up (Right stick up)".Length + 2;
        Assert.Contains("Pro stick up (Right stick up)".PadRight(width) + "Num 8", text);
        Assert.Contains("Right stick down".PadRight(width) + "(none)", text);
        Assert.Contains("Right stick left".PadRight(width) + "(none)", text);
        Assert.Contains("Right stick right".PadRight(width) + "(none)", text);
        Assert.Contains("Right stick".PadRight(width) + "Mouse", text); // the mouse still works when no key is held
    }

    [Fact]
    public void A_right_stick_label_without_a_key_stays_hidden()
    {
        var settings = new ControlSettings();
        settings.SetLabel(PadControl.RightUp, "Pro stick up");

        var text = OverlayText.Controls(settings);

        Assert.DoesNotContain("Pro stick up", text);
        Assert.Contains($"{"Cross",-18}K\n", text);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~OverlayTextTests"`
Expected: FAIL. The default panel contains "Right stick up" and the column is 19 wide ("Right stick right" is 17 + 2), so `"Cross" + 13 spaces + "K"` isn't found.

- [ ] **Step 3: Implement**

In `OverlayText.Controls`, replace the `names`/`width`/group loop with:

```csharp
        // The right stick's keys are listed only once one has a key: by default the mouse is the right stick,
        // and four "(none)" rows would crowd the panel (and widen its first column).
        bool rightStickKeys = KeyNames.RightStick.Any(c => settings.Layout.KeysFor(c).Count > 0);
        var groups = KeyNames.Groups
            .Where(g => rightStickKeys || !ReferenceEquals(g.Controls, KeyNames.RightStick))
            .ToList();
        var names = groups
            .SelectMany(g => g.Controls)
            .ToDictionary(c => c, c => KeyNames.Labelled(c, settings.LabelFor(c)));
        int width = Math.Max(18, names.Values.Max(n => n.Length) + 2);

        var lines = new List<string> { title };
        foreach (var (_, controls) in groups)
        {
            lines.Add("");
            foreach (var control in controls)
                lines.Add(names[control].PadRight(width) + KeyNames.Describe(settings.Layout, control));
        }
```

Leave the final `Right stick … Mouse (sensitivity …)` line as it is. Update the method's summary to mention: "The right stick's direction rows appear only once one has a key."

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests`
Expected: all pass, including `ShippedProfilesTests.F1_shows_the_NBA_2K22_labels_with_their_keys`.

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.Core/Video/OverlayText.cs tests/CouchLink.Core.Tests/OverlayTextTests.cs
git commit -m "feat(input): F1 lists the right-stick keys only once one is bound"
```

---

### Task 4: Controls editor shows four bindable right-stick rows

**Files:**
- Modify: `src/CouchLink.App/ControlsWindow.cs:125-132`

**Interfaces:**
- Consumes: the "Right stick" group in `KeyNames.Groups` (Task 1). The editor's group loop already builds a bindable row and a label box for every control in `Groups`.

No unit tests: `CouchLink.App` (WPF) has no test project. Verified by the build, the manual check in Step 3 and the gate row in Task 6.

- [ ] **Step 1: Remove the greyed-out mouse row**

Delete these lines (the heading and the disabled button after the group loop):

```csharp
        list.Children.Add(new TextBlock { Text = "Right stick", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2) });
        list.Children.Add(new Button
        {
            Content = Row("Right stick", "Mouse"),
            IsEnabled = false,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 4, 8, 4),
        });
```

`Row(...)` is still used at line 214, so keep the helper. The "Mouse sensitivity (right stick)" heading, slider and Invert Y stay.

- [ ] **Step 2: Build**

Run: `dotnet build CouchLink.slnx -c Debug`
Expected: build succeeded, 0 warnings added.

- [ ] **Step 3: Check it by hand**

Run the app (use the `run` skill or `dotnet run --project src/CouchLink.App`), open ⚙ Controls:
- A "Right stick" group sits under "Left stick" with four rows reading "(none)".
- Click "Right stick up", press Num 8: the row reads "Num 8". Press K on it instead: the message says "K moved from Cross."
- Tick "Show labels": the four rows have label boxes.
- Reset to default: the four rows read "(none)" again.

- [ ] **Step 4: Commit**

```bash
git add src/CouchLink.App/ControlsWindow.cs
git commit -m "feat(app): the controls editor binds keys to the right stick"
```

---

### Task 5: NBA 2K22 profile puts the pro stick on Num 8/2/4/6

**Files:**
- Modify: `src/CouchLink.App/profiles/nba-2k22.json`
- Test: `tests/CouchLink.Core.Tests/ShippedProfilesTests.cs`

**Interfaces:**
- Consumes: the `RightUp`…`RightRight` names (Task 1), F1 rows (Task 3).

- [ ] **Step 1: Write the failing tests**

Add four rows to the `The_NBA_2K22_profile_uses_the_games_keyboard_keys` theory, after `LeftRight`:

```csharp
    [InlineData(PadControl.RightUp, "Num8")]       // pro stick
    [InlineData(PadControl.RightDown, "Num2")]
    [InlineData(PadControl.RightLeft, "Num4")]
    [InlineData(PadControl.RightRight, "Num6")]
```

Append:

```csharp
    [Fact]
    public void The_NBA_2K22_profile_labels_the_pro_stick_and_F1_lists_it()
    {
        var profile = Nba2K22();
        Assert.Equal("Pro stick up", profile.Labels[PadControl.RightUp]);
        Assert.Equal("Pro stick down", profile.Labels[PadControl.RightDown]);
        Assert.Equal("Pro stick left", profile.Labels[PadControl.RightLeft]);
        Assert.Equal("Pro stick right", profile.Labels[PadControl.RightRight]);

        var settings = new ControlSettings();
        settings.Apply(profile);
        var lines = OverlayText.Controls(settings).Split('\n');
        Assert.Contains(lines, l => l.StartsWith("Pro stick up (Right stick up)", StringComparison.Ordinal) && l.EndsWith("Num 8", StringComparison.Ordinal));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/CouchLink.Core.Tests --filter "FullyQualifiedName~ShippedProfilesTests"`
Expected: FAIL with `KeyNotFoundException` for `RightUp`.

- [ ] **Step 3: Update the profile**

Replace the header comment's first three lines with:

```jsonc
  // NBA 2K22's own PC keyboard keys, each on the DualShock 4 button that does
  // the same thing in the game. The pro stick is on Num 8/2/4/6 as in the
  // game; the mouse still moves it while none of those keys is held.
```

(keep `// Keep Num Lock on so the number pad keys work.`), and after the `"LeftRight"` line add:

```json
    "RightUp":    { "keys": ["Num8"], "label": "Pro stick up" },
    "RightDown":  { "keys": ["Num2"], "label": "Pro stick down" },
    "RightLeft":  { "keys": ["Num4"], "label": "Pro stick left" },
    "RightRight": { "keys": ["Num6"], "label": "Pro stick right" },
```

Re-align the existing lines' value columns only if the file's alignment would otherwise look ragged; it's cosmetic.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/CouchLink.Core.Tests`
Expected: all pass. The profile loads (no key used twice: Num 8/2/4/6 aren't on any other control).

- [ ] **Step 5: Commit**

```bash
git add src/CouchLink.App/profiles/nba-2k22.json tests/CouchLink.Core.Tests/ShippedProfilesTests.cs
git commit -m "feat(app): the NBA 2K22 profile puts the pro stick on Num 8/2/4/6"
```

---

### Task 6: Docs and manual gate

**Files:**
- Modify: `README.md:95`, `docs/cafe-setup-guide.md:209,222-223,238-239`, `docs/superpowers/specs/2026-10-08-couchlink-controller-profiles-design.md:44-47,118,389`, `ROADMAP.md`, `docs/gate-results.md`

- [ ] **Step 1: Update the docs**

- `README.md` table: `| Right stick | Mouse | …` → `| Right stick | Mouse (or keys you bind) | …`.
- `docs/cafe-setup-guide.md`:
  - default-layout table: `| Right stick | Mouse |` stays (the default has no keys).
  - after "The mouse acts like a stick that springs back…" add: "You can also put keys on the right stick's four directions in the editor. While one of those keys is held, the keys move the stick and the mouse is ignored; let go and the mouse works again."
  - "Mouse sensitivity" and "Invert Y" bullets: unchanged (they apply to the mouse).
  - Profiles section, line 262: replace "pro stick stays on the mouse." with "pro stick is on Num 8/2/4/6, and the mouse still moves it while none of those keys is held."
- Controller profiles spec: line 46 "have no equivalent: CouchLink's right stick is always the mouse." → "are on the right stick since #59 (see the [right-stick keys design](2026-10-09-couchlink-right-stick-keys-design.md))."; line 118 "The right stick is the mouse and can't appear in `controls`." → "`RightUp`, `RightDown`, `RightLeft` and `RightRight` were added by #59."
- `ROADMAP.md` under "v1.6 — Controller profiles", add:
  `- [#59](https://github.com/enriquezchristopher/CouchLink/issues/59) Bind keys to the right stick, alongside the mouse`
- `docs/gate-results.md` controller-profiles table, add rows:
  `| Bind Num 8 to Right stick up in the editor; in a gamepad tester (joy.cpl) holding Num 8 pushes the right stick up; moving the mouse while holding changes nothing; after release the mouse moves the stick again | <pass/fail> |`
  `| Shipped NBA 2K22 profile in 2K22: Num 8/2/4/6 work the pro stick; F1 lists "Pro stick up (Right stick up)  Num 8" | <pass/fail> |`
  and change the existing NBA 2K22 row's "the mouse is the pro stick" to "the mouse also moves the pro stick".

- Retake `docs/images/controls-profile.png` and `docs/images/f1-profile.png` with the NBA 2K22 profile loaded, so they show the right-stick rows (capture via UI Automation, as for the earlier profile screenshots).

Don't edit `CHANGELOG.md`; release-please writes it.

- [ ] **Step 2: Run the whole test suite**

Run: `dotnet test`
Expected: all test projects pass.

- [ ] **Step 3: Commit**

```bash
git add docs/images/controls-profile.png docs/images/f1-profile.png README.md docs/cafe-setup-guide.md docs/superpowers/specs/2026-10-08-couchlink-controller-profiles-design.md ROADMAP.md docs/gate-results.md
git commit -m "docs: keys on the right stick in the guide, roadmap and gate checks"
```
