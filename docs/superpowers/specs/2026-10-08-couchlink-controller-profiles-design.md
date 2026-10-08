# CouchLink Controller Profiles (v1.6 milestone: #56)

Status: Approved; ProfileStore lives in CouchLink.Core (plan 9) so it is unit-tested
Date: 2026-10-08
Author: Christopher Enriquez (@enriquezchristopher) (with Claude Code)

Builds on section 2 (Controls) of the
[playing screen & controls design](2026-10-08-couchlink-playing-screen-controls-design.md)
and changes section 6.7 and the non-goals of the
[main design](2026-10-05-couchlink-design.md).

## 1. Goal

A café owner sets up a key layout for a game once, saves it as a file, and
puts it in a `profiles\` folder next to the exe on every PC. A customer on a
joining PC opens ⚙ Controls, picks "NBA 2K22" from a list, and plays with
keys that suit the game. If the profile names the buttons ("Shoot",
"Pass", "Sprint"), the F1 panel shows those names, so a new player learns
the game's controls without knowing what Square or R2 does.

### Agreed requirements
Settled in brainstorming (2026-10-08):
- **Client only.** Each player picks a profile on their own joining PC. The
  host knows nothing about profiles and the session protocol is unchanged.
- **Two ways to load:** a dropdown in the controls editor listing the files
  in `profiles\` next to the exe, and **Browse…** for a file anywhere else
  (a USB stick, Downloads).
- **Action labels:** a profile can give each control a game-specific name.
  The F1 panel and the editor show it.
- **Save as profile:** the editor writes the current layout to a profile
  file, so nobody has to hand-edit JSON.
- **Label boxes in the editor,** behind a "Show labels" checkbox that is off
  by default, so labels can be made without editing JSON either.
- **Reset to default** always restores CouchLink's built-in layout and drops
  the loaded profile and its labels.
- **Nothing is remembered between runs.** Settings still reset to the
  built-in layout when CouchLink closes (spec 6.7), so each customer starts
  fresh. A profile is a preset the player picks, not saved state.
- **Approach:** a `ControlProfile` record and a `ProfileFile` reader/writer
  in `CouchLink.Core`, using the built-in `System.Text.Json`, applied with
  one call on `ControlSettings`. The file format is defined here and does
  not mirror class internals.

### Success criteria
- A profile dropped into `profiles\` appears in the editor's dropdown the
  next time the editor opens, and choosing it changes every binding, the
  sensitivity and Invert Y at once, mid-game included.
- A player who picks a profile with labels sees "Shoot" next to its keys on
  the F1 panel.
- An owner who rebinds keys and types labels in the editor, then clicks
  Save as…, gets a file that loads back to the same layout and labels on
  another PC.
- A file with a typo loads nothing and the editor says what is wrong and
  where, for example `"Square": unknown key "Spcae"`. CouchLink never
  crashes on a bad file.

## 2. File format

One JSON file per profile, UTF-8, extension `.json`.

```json
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
```

| Field | Required | Rules |
|---|---|---|
| `format` | yes | Integer. This design is format `1`. A higher number is refused ("made for a newer CouchLink"). |
| `name` | yes | 1-60 characters after trimming. Shown in the dropdown. |
| `game` | no | Up to 60 characters. Shown in the Save as… box; for owners, not players. |
| `sensitivity` | no | Integer 1-10. Missing means 5. |
| `invertY` | no | Boolean. Missing means false. |
| `controls` | yes | Object keyed by control name (2.1). May be empty. |
| `controls.X.keys` | no | Array of key IDs (2.2). Missing or empty means the control has no key. |
| `controls.X.label` | no | Up to 24 characters after trimming, no line breaks. Empty means no label. |

- **A profile is a whole layout.** A control the file leaves out has no key
  and no label. It does not keep the built-in key, so a profile looks the
  same on every PC whatever was bound before.
- **Names are matched without regard to case** (`square`, `Square`), so
  hand-written files are forgiving. Save writes them in the case shown here.
- **Comments and trailing commas are allowed** when reading
  (`JsonCommentHandling.Skip`, `AllowTrailingCommas`), so owners can annotate
  a file. Save writes neither.
- **Unknown top-level fields are ignored**, so a later format can add fields
  that this build skips. Unknown control names and unknown fields inside a
  control are errors, because there they are almost always typos.

### 2.1 Control names
The `PadControl` names: `LeftUp`, `LeftDown`, `LeftLeft`, `LeftRight`,
`DpadUp`, `DpadDown`, `DpadLeft`, `DpadRight`, `Cross`, `Circle`, `Square`,
`Triangle`, `L1`, `R1`, `L2`, `R2`, `L3`, `R3`, `Options`, `Share`,
`Touchpad`. The right stick is the mouse and can't appear in `controls`.

Renaming a `PadControl` member would break every profile, so a unit test
pins the list above.

### 2.2 Key IDs
Fixed IDs, separate from `KeyNames`, which is what the screen shows. The
display names use characters that are awkward to type ("←") and may change;
these never do.

| Keys | IDs |
|---|---|
| Letters, digits | `A`-`Z`, `0`-`9` |
| Function keys | `F3`-`F24` |
| Arrows | `Up`, `Down`, `Left`, `Right` |
| Modifiers | `LShift`, `RShift`, `LCtrl`, `RCtrl`, `LAlt`, `RAlt` |
| Editing | `Space`, `Enter`, `Tab`, `Backspace`, `Insert`, `Delete`, `Home`, `End`, `PageUp`, `PageDown` |
| Locks and others | `CapsLock`, `NumLock`, `ScrollLock`, `Pause`, `Menu` |
| Number pad | `Num0`-`Num9`, `NumMultiply`, `NumAdd`, `NumSubtract`, `NumDecimal`, `NumDivide` |
| Punctuation (US layout) | `Semicolon`, `Equals`, `Comma`, `Minus`, `Period`, `Slash`, `Backquote`, `LeftBracket`, `Backslash`, `RightBracket`, `Quote` |
| Mouse | `LeftClick`, `RightClick`, `MiddleClick`, `Mouse4`, `Mouse5` |
| Anything else | `0x` and two hex digits, the virtual-key code (e.g. `0xE2`) |

- The hex form exists so Save never fails: the editor can bind a key that
  has no ID (an OEM key on a non-US keyboard), and Save writes it as hex.
- `Esc`, `F1`, `F2`, `LWin` and `RWin` are recognised so the error can say
  "reserved" rather than "unknown". They are never accepted.
- Punctuation IDs name the virtual-key code, which on a non-US keyboard may
  be a different printed key. The editor always shows the real key the
  player presses, so this only matters to someone hand-editing a file.

### 2.3 Validation
`ProfileFile.Load` either returns a whole profile or an error. It never
returns a partial profile, and loading an invalid file changes nothing.

| Problem | Message (shown in the editor) |
|---|---|
| Not valid JSON | `Not a valid profile file (line 12): <parser message>` |
| `format` missing or not an integer | `Missing "format"` |
| `format` above 1 | `This profile was made for a newer CouchLink` |
| `name` missing, empty or too long | `"name" must be 1-60 characters` |
| Unknown control | `Unknown control "Sqaure"` |
| Unknown key ID | `"Square": unknown key "Spcae"` |
| Reserved key | `"Square": F1 is reserved` |
| One key on two controls | `"K" is on both Cross and Circle` |
| `sensitivity` out of range | `"sensitivity" must be 1-10` |
| Label too long or has a line break | `"Square": label must be up to 24 characters on one line` |
| File over 64 KB (checked by the App before reading) | `File is too large for a profile` |

The same key listed twice on one control is not an error; it is kept once.
The first problem found is reported; the editor shows one message at a time.

## 3. Core (`CouchLink.Core/Input`)

### 3.1 `ControlProfile`
```csharp
public sealed record ControlProfile(
    string Name,
    string? Game,
    int SensitivityStep,
    bool InvertY,
    IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> Keys,
    IReadOnlyDictionary<PadControl, string> Labels);
```
Plain data, already validated. Created only by `ProfileFile.Load` and
`ControlSettings.ToProfile`.

### 3.2 `ProfileKeys`
The table in 2.2: `bool TryParse(string id, out ushort vk)` and
`string IdOf(ushort vk)` (hex form for codes with no ID). A static class
with no Windows API, like `KeyNames`.

### 3.3 `ProfileFile`
- `ProfileResult Load(string json)`: a profile, or an error message (2.3).
- `string Save(ControlProfile profile)`: indented JSON in the field order of
  section 2, controls in `KeyNames.Groups` order, controls with no keys and
  no label left out.
- Works on strings only, so tests need no disk. Reading and writing files is
  the App's job (4.4).

`Load(Save(p))` returns a profile equal to `p`; a round-trip test covers it.

### 3.4 `KeyLayout`
`Replace(IReadOnlyDictionary<PadControl, IReadOnlyList<ushort>> keys)`
swaps every binding at once. Replace does not check reserved keys or
duplicates; `ProfileFile` already did.

Today `InputMapper.Tick` calls `KeysFor` once per control, so a swap landing
in the middle of a tick could mix the two layouts for that tick. To prevent
it, the bindings become an immutable dictionary held by reference:
- `KeyLayout.Current` returns the current dictionary. `Bind`, `Replace` and
  `ResetToDefault` build a new one under the existing lock and publish it
  with `Volatile.Write`.
- `InputMapper.Tick` reads `Current` once at the start and uses it for every
  control in that tick.
- `KeysFor` stays for the editor and the F1 panel.

### 3.5 `ControlSettings`
New members:
- `string? ProfileName`: the loaded profile's name, null on the built-in
  layout.
- `bool ProfileChanged`: true after any edit made since the profile was
  loaded. The editor shows it as "(changed)".
- `string? LabelFor(PadControl control)`.
- `void Apply(ControlProfile profile)`: `Layout.Replace`, then the
  sensitivity step, Invert Y, labels and `ProfileName`; clears
  `ProfileChanged`; raises `Changed` once at the end.
- `void SetLabel(PadControl control, string? label)`: trims, caps at 24
  characters, treats empty as none; raises `Changed`.
- `ControlProfile ToProfile(string name, string? game)`: a snapshot of the
  current layout, labels, step and Invert Y.

Changed members:
- `ResetToDefault()` also clears the labels, `ProfileName` and
  `ProfileChanged`.
- `Bind`, `SetSensitivityStep`, `SetInvertY` and `SetLabel` set
  `ProfileChanged` when a profile is loaded.

Labels live in an immutable dictionary swapped with `Volatile.Write`, so the
F1 panel can read them from the player thread without a lock.

`InputMapper` already releases held keys on `Changed`, so a key held while
a profile loads is released and doesn't stay pressed under its new meaning.
The sensitivity and Invert Y are read by `MouseStick` on its next mouse
movement, a moment after the layout swaps; nothing in a game depends on the
two changing in the same tick.

### 3.6 F1 panel (`OverlayText.Controls`)
- Title line: `Controls: NBA 2K22 Café layout (F1 hides, Ctrl+Alt+C changes
  keys)`, or today's title on the built-in layout. "(changed)" follows the
  name when `ProfileChanged` is set.
- A control with a label is listed as `Shoot (Square)`; the first column
  widens to fit the longest entry. Controls without a label stay as today.

## 4. Editor and App (`CouchLink.App`)

### 4.1 Profile row
A new row at the top of `ControlsWindow`:

```
Profile: [ NBA 2K22 Café layout ▾ ]  [Browse…]  [Save as…]
```

- The dropdown lists "Default layout" first, then each profile in
  `profiles\` sorted by name. Two files with the same `name` show the file
  name after it: `NBA 2K22 (2k22-alt.json)`.
- The selected entry is the loaded profile, with " (changed)" added when
  `ProfileChanged` is set. A profile loaded with Browse… that isn't in the
  folder appears as an extra entry until the editor closes.
- Choosing "Default layout" does the same as Reset to default.
- Choosing a profile loads it. The message line under the list says
  "Loaded NBA 2K22 Café layout." for 3 s, like the "moved from" message.

### 4.2 Profiles folder
- `Path.Combine(AppContext.BaseDirectory, "profiles")`, read each time the
  editor opens. Top-level `*.json` files only.
- A file that fails to load is left out of the list and logged with its
  message (`profile skipped: 2k22.json: "Square": unknown key "Spcae"`), so
  an owner who sees a profile missing can find why in the log.
- No folder means a list with "Default layout" only. CouchLink doesn't create
  the folder until the first Save as… (4.3).

### 4.3 Browse… and Save as…
- **Browse…** opens the Windows file picker (`Microsoft.Win32.OpenFileDialog`,
  filter `CouchLink profile (*.json)`), starting in `profiles\` when it
  exists. A file that fails to load shows its message on the message line
  and changes nothing.
- **Save as…** opens a small dialog with Name and Game boxes, filled from
  the loaded profile when there is one, then the Windows save dialog
  (`SaveFileDialog`, overwrite prompt on) in `profiles\`, creating the folder
  if it is missing, with the file name suggested from Name. If the folder
  can't be created, the save dialog starts in Documents. After saving, the
  saved file becomes the loaded profile and appears in the dropdown.
- Both dialogs are modal to the editor. When the editor was opened over the
  game with Ctrl+Alt+C they are topmost too, so they don't open behind the
  player.

### 4.4 Reading and writing files
In the App, around `ProfileFile`:
- Read: check the size (64 KB), read as UTF-8, `ProfileFile.Load`. I/O
  errors become a message ("Couldn't read 2k22.json: access denied") and
  are logged.
- Write: `ProfileFile.Save`, written to a temp file in the same folder and
  moved over the target, so a failed save never leaves half a file. Errors
  become a message and are logged.

### 4.5 Label boxes
- A "Show labels" checkbox under the profile row, off each time the editor
  opens.
- When on, each control row gets a text box (max 24 characters) to its
  right, holding `LabelFor(control)`. The label is set on Enter or when the
  box loses focus.
- Typing in a label box never starts a rebind; clicking the row's key
  button still does.
- The window widens to fit the boxes while the checkbox is on.

## 5. Components

### 5.1 `CouchLink.Core`
- `Input/ControlProfile.cs`, `Input/ProfileKeys.cs`, `Input/ProfileFile.cs`
  (new).
- `Input/KeyLayout.cs` (immutable `Current`, Replace),
  `Input/InputMapper.cs` (one layout read per tick),
  `Input/ControlSettings.cs` (profile name, labels, Apply, ToProfile,
  changed flag), `Video/OverlayText.cs` (labels, profile name) (changed).

### 5.2 `CouchLink.App`
- `ProfileStore.cs`: lists the folder, reads and writes files (4.2, 4.4)
  (new).
- `SaveProfileDialog.cs`: the Name and Game boxes (new).
- `ControlsWindow.cs`: profile row, Show labels, label boxes (changed).

## 6. Error handling

| Situation | Behavior |
|---|---|
| Invalid profile chosen with Browse… | Message line shows why; nothing changes |
| Invalid profile in `profiles\` | Left out of the list; logged with the reason |
| `profiles\` missing | List shows "Default layout" only |
| `profiles\` can't be read (permissions) | List shows "Default layout" only; logged |
| File over 64 KB | Refused before reading; message shown |
| Save fails (read-only folder, disk full) | Message shows why; target file unchanged; logged |
| Profile loaded mid-game while keys are held | Held keys released (existing `Changed` behavior) |

## 7. Testing

### 7.1 Unit (CouchLink.Core.Tests)
- `ProfileKeys`: every ID parses and maps back to itself; every default
  key has an ID; unknown codes write as hex and parse back; reserved IDs
  are recognised; matching ignores case.
- `ProfileFile.Load`: the example in section 2 loads to the expected
  profile; each row of the 2.3 table gives its message; missing optional
  fields get their defaults; left-out controls have no keys; comments and
  trailing commas accepted; unknown top-level fields ignored.
- `ProfileFile.Save`: round trip for the built-in layout, a profile with
  labels, and one with a hex key; output field order and control order are
  fixed (snapshot test).
- `PadControl` names pinned (2.1).
- `KeyLayout.Replace`: replaces all bindings; `Current` read before a
  Replace keeps the old bindings.
- `InputMapper`: a Tick uses one layout for every control (a held key that
  is Cross in the old layout and Circle in the new presses exactly one of
  them in any tick, never both or neither).
- `ControlSettings`: Apply sets everything and raises `Changed` once; edits
  after Apply set `ProfileChanged`; Reset clears profile, labels and flag;
  `ToProfile` after Apply returns an equal profile; `SetLabel` trims, caps
  and clears.
- `OverlayText.Controls`: profile name and "(changed)" in the title; labels
  shown as `Shoot (Square)`; built-in layout unchanged from today.

### 7.2 Manual gate (added to `docs/gate-results.md`)
- Put two profiles and one broken file in `profiles\`: the two are listed,
  the broken one isn't, and the log says why.
- Load a profile mid-game in NBA 2K22: the new keys work at once; F1 shows
  the profile name and labels.
- Rebind a key after loading: the dropdown and F1 show "(changed)"; the
  file on disk is unchanged.
- Type labels, Save as…, close CouchLink, copy the file to another PC,
  load it there: same keys, labels, sensitivity and Invert Y.
- Browse… to a profile on a USB stick: it loads; a broken one shows its
  message.
- Open the editor with Ctrl+Alt+C, click Save as…: the dialogs open in
  front of the game.
- Reset to default after loading: built-in keys, no labels, no profile name.
- Close and restart CouchLink: built-in layout.

## 8. Out of scope
- The host choosing a profile for every joining PC (needs a session
  message).
- Picking a profile automatically from the running game.
- Remembering the last profile between runs.
- Shipping profiles in the release zip.
- Per-game mouse modes (a right stick that doesn't spring back).
- Editing the JSON inside CouchLink, or a profile manager (rename, delete).
  Owners use Explorer for that.

## 9. Changes to the main design
- 6.7: add profiles: a dropdown of files in `profiles\` next to the exe,
  Browse…, Save as…, action labels shown in the editor and on the F1
  panel. Edits and the loaded profile still reset when CouchLink closes.
- Non-goals: "Persisting custom key layouts (server share, profiles)" and
  "Per-game key presets" become "Saving the player's layout automatically
  between runs; sharing profiles over the network". Profile files are
  covered by this design.
