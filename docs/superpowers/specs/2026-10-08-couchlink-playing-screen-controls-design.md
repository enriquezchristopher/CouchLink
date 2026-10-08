# CouchLink Playing Screen, Controls & Single Instance (v1.4 milestone, part 2: #24, #25, #26)

Status: Approved; additions from planning (Ctrl+Alt+C opens the editor over the game; one control per line on the F1 panel; Raw Alt reported as Left/Right Alt)
Date: 2026-10-08
Author: Christopher Enriquez (@enriquezchristopher) (with Claude Code)

Expands sections 6.1-6.4, 6.7 and 7 ("Ports in use") of the
[main design](2026-10-05-couchlink-design.md). Part 1 of v1.4 is the
[lobby & sessions design](2026-10-07-couchlink-lobby-sessions-design.md);
this part builds on its session screen.

## 1. Goal

A player at a café PC plays without fighting Windows: the Windows key and
Alt+Tab do nothing mid-game, the pointer can't wander onto another monitor,
F1 shows which key does what, and a key they don't like can be changed in
the middle of a match. Double-clicking the CouchLink shortcut again brings
the running copy forward instead of starting a second one.

### Agreed requirements
From the main spec and the issues:
- F1 shows the current key layout (#24).
- The Windows key and Alt+Tab are blocked while playing, with a low-level
  keyboard hook; Ctrl+Alt+Q always leaves (#24, spec 6.4).
- The pointer is hidden and clipped to the window while playing (#24,
  spec 6.3). Fullscreen and Ctrl+Alt+Q already shipped in 1.3.0.
- ⚙ Controls lists every DS4 control with its key; click a control, press a
  key to rebind; Reset to default; a mouse sensitivity slider. Edits are
  kept in memory only and reset when CouchLink closes, by design (#25,
  spec 6.7).
- Starting CouchLink while it is running brings the running window to the
  front (#26, spec 7).

Settled in brainstorming (2026-10-08):
- **One key, one control.** Binding a key makes it the control's only key
  and takes it off any other control.
- **Bindable inputs:** any keyboard key and all five mouse buttons (left,
  right, middle, X1, X2). **Reserved:** Esc (cancels a rebind), F1, F2 and
  both Windows keys. Ctrl+Alt+Q keeps working whatever is bound.
- **Edit anywhere:** ⚙ Controls is on the Start screen and the session
  screen; changes apply at once, mid-game included.
- **F1 toggles** a controls panel over the stream, like F2. For 5 s after
  the first picture of a join, a corner hint reads
  "F1: controls · Ctrl+Alt+Q: leave".
- **Blocked while playing:** both Windows keys (so every Win+ combination),
  Alt+Tab, Alt+Esc, Ctrl+Esc. Alt+F4 still leaves. Nothing is blocked with
  `--windowed-player`.
- **Invert Y** checkbox for the mouse, next to the sensitivity slider.
- **Single instance**, except that a copy started with `--windowed-player`
  may run alongside, so one-PC testing still works.
- **Approach A:** rules live in `CouchLink.Core` as plain tested code; the
  hook runs on the player window's own thread, never the WPF UI thread.

### Success criteria
- Mid-game, the Windows key, Alt+Tab, Alt+Esc and Ctrl+Esc do nothing;
  after Ctrl+Alt+Q they work again.
- With two monitors, clicking and moving the mouse never leaves the game.
- F1 shows every control and its key, including an edit made a moment ago.
- A player rebinds Cross to Space mid-game and Space presses Cross on the
  next input tick, with no rejoin.
- Double-clicking the shortcut while hosting or playing brings CouchLink
  forward and starts nothing new.

## 2. Controls

### 2.1 `ControlSettings` (Core)
One per app run (`AppServices.Controls`), never saved. Holds:
- `KeyLayout Layout`, editable (2.2).
- `int SensitivityStep`, 1-10, default 5. Sensitivity per mouse count is
  `MouseStick.DefaultSensitivity * 1.3^(step - 5)` (about 0.007 to 0.074;
  step 5 is today's 0.02).
- `bool InvertY`, default false.
- `event Action Changed`, raised after any edit.
- `ResetToDefault()`: the spec 6.1 layout, step 5, InvertY off.

Thread-safe: the editor writes on the UI thread, the input loop reads every
8 ms on its own thread.

### 2.2 `KeyLayout` becomes editable
- `KeysFor(control)` as today.
- `Bind(PadControl control, ushort key) -> BindResult`: the control's keys
  become exactly `[key]`; the key is removed from every other control.
  Returns `Bound(movedFrom: PadControl?)` or `Reserved`. Binding a key the
  control already has alone is a no-op `Bound(null)`.
- `IsReserved(key)`: Esc (0x1B), F1 (0x70), F2 (0x71), LWin (0x5B),
  RWin (0x5C).
- The right stick is not in `PadControl` (it is the mouse) and cannot be
  bound.
- The default layout keeps its two-key controls (Square: J and Left click;
  L2: either Ctrl; R2: either Shift) until one of them is rebound, which
  applies the one-key rule. Reset restores them.

### 2.3 `KeyNames` (Core)
`KeyNames.Of(ushort vk) -> string`: "A".."Z", "0".."9", "F3".."F12",
"Left click", "Right click", "Middle click", "Mouse 4", "Mouse 5",
"Left Ctrl", "Right Ctrl", "Left Shift", "Right Shift", "Alt" (Raw Input reports both Alt keys as 0x12), "Left Alt",
"Right Alt", "Space", "Enter", "Backspace", "Tab", "Caps Lock",
"↑ ↓ ← →", the numpad, punctuation keys by their US label, and
"Key 0x5D" for anything else. A table, no Windows API, so Core stays
`net10.0`. `KeyNames.Of(control)` gives "Cross", "L2", "Left stick up" and
so on. `KeyNames.Describe(layout, control)` joins keys with " / ".

### 2.4 Input path
- `InputMapper` takes the `ControlSettings` instead of a `KeyLayout`, and
  reads `Layout` on every `Tick`. On `Changed` it releases held keys (no
  key stays pressed across a rebind).
- `MouseStick` reads the sensitivity and InvertY from the settings on every
  `AddDelta`.
- Raw Input reports X1 and X2 as `VirtualKeys.XButton1` (0x05) and
  `XButton2` (0x06), added to `RawInputSource` next to the three buttons it
  reports today.

### 2.5 Controls editor (App)
A small window, `ControlsWindow`, opened from ⚙ Controls on the Start screen
and the session screen; one at a time (a second click focuses it).
- Rows grouped as spec 6.1: Left stick (up, down, left, right), D-pad,
  Cross / Circle / Square / Triangle, L1 / R1 / L2 / R2, L3 / R3,
  Options / Share / Touchpad. Each row: control name and its keys. A last,
  disabled row "Right stick · Mouse".
- Click a row: it reads "Press a key or mouse button... (Esc cancels)".
  The next key or mouse button (WPF `PreviewKeyDown` / `PreviewMouseDown`,
  including `SystemKey` for Alt and F10) binds it. A reserved key shows
  "F1 is reserved" and keeps listening. A key taken from another control
  shows "K moved from Cross" under the list for 3 s.
- Sensitivity slider 1-10 with ticks, Invert Y checkbox, Reset to default.
- While the editor has focus, the game gets no input, because input counts
  only while the main window or the player is in front (unchanged).
- Also opened over the game with **Ctrl+Alt+C** in the player (topmost, no
  owner), because Alt+Tab and the Windows key are blocked and the pointer
  is held in the player; closing it returns to the game.

## 3. Playing screen

All of this lives in the player window and runs on the player thread.

### 3.1 F1 controls panel
- F1 toggles it (key repeat ignored), like F2. Drawn by the same text
  overlay; the controls panel sits top-left, the stats panel top-right, so
  both can be on.
- Text from `OverlayText.Controls(ControlSettings)`: one line per group,
  e.g. `Left stick  W / A / S / D`, `Cross  K`, `Square  J / Left click`,
  `Right stick  Mouse (sensitivity 5)`. Built when the panel is drawn, so
  an edit shows on the next frame.

### 3.2 Startup hint
`PlayerCore` shows "F1: controls · Ctrl+Alt+Q: leave" in a corner for 5 s
from the first frame shown. Once per `VideoPlayer`, which lives for one
join; a session reconnect keeps the same player, so it doesn't show again.
Hidden early if F1 is pressed.

### 3.3 Key blocking
- `ShortcutFilter.ShouldBlock(ushort vk, bool altDown, bool ctrlDown)`
  (Core, pure):
  - LWin, RWin: always (down and up), which also stops every Win+ combo.
  - Tab with Alt down (Alt+Tab).
  - Esc with Alt down (Alt+Esc) or Ctrl down (Ctrl+Esc).
  - Everything else: no. Tab alone, Alt alone, Alt+F4, Ctrl+Alt+Q and Ctrl+Alt+C pass.
- `KeyboardBlocker` (CouchLink.Video): `SetWindowsHookEx(WH_KEYBOARD_LL)` on
  the player thread, which already pumps messages. The callback reads Alt
  from `LLKHF_ALTDOWN` and Ctrl from `GetAsyncKeyState`, asks the filter,
  and returns 1 to swallow or calls `CallNextHookEx`. It catches every
  exception and passes the key on if anything goes wrong.
- Installed when the player window becomes active while fullscreen;
  removed on deactivate, close and dispose. Never with `--windowed-player`
  (`PlayerOptions.LockInput` false).
- A swallowed key never reaches Raw Input. That matches the reserved keys:
  Esc and the Windows keys can't be bound, and Alt+Tab doesn't press the
  touchpad.

### 3.4 Mouse lock
- `ClipCursor` to the player window's client rectangle when it becomes
  active while fullscreen and on every resize; `ClipCursor(null)` on
  deactivate, close and dispose. The cursor stays hidden as today.
- Raw Input mouse deltas keep flowing while clipped, so the right stick is
  unaffected.
- The crash handler calls `ClipCursor(null)` as well, so a crash never
  leaves the pointer trapped.

## 4. Single instance

`SingleInstance` (App), checked in `App.OnStartup` before any window:
- Mutex `Local\CouchLink.SingleInstance`, event
  `Local\CouchLink.Activate` (per Windows sign-in).
- First copy: owns the mutex, waits on the event on a background thread;
  each signal posts to the UI thread, which brings forward the player window
  if a session is playing, otherwise restores (if minimized) and activates
  the main window.
- Second copy without `--windowed-player`: `AllowSetForegroundWindow(ASFW_ANY)`
  (a freshly launched app may hand over foreground rights), sets the event,
  exits with code 0, shows nothing.
- With `--windowed-player`: skips the mutex and the event entirely.
- A crashed copy's mutex is released by Windows, so it never blocks the
  next start.
- If the mutex is taken but the event can't be opened (very rare), the
  copy starts normally; its port errors then explain the clash as today.
- The decision (`Run`, `HandOff`, `Bypass`) is a pure function of
  "mutex owned" and the switches, tested in Core; the mutex and event are
  thin wrappers in App.

## 5. Components

### 5.1 `CouchLink.Core`
- `Input/ControlSettings.cs`, `Input/KeyNames.cs`, `Input/ShortcutFilter.cs`
  (new).
- `Input/KeyLayout.cs` (Bind, IsReserved, ResetToDefault),
  `Input/InputMapper.cs`, `Input/MouseStick.cs`, `Input/VirtualKeys.cs`
  (X buttons, Esc, F1, F2, Win keys) (changed).
- `Video/OverlayText.cs`: `Controls(...)`, `StartHint` (changed).
- `Startup/InstanceDecision.cs`: the single-instance decision (new).

### 5.2 `CouchLink.Video`
- `KeyboardBlocker.cs` (new).
- `PlayerWindow.cs`: F1, activate/deactivate hooks for blocking and
  clipping, `ClipCursor` (changed).
- `PlayerCore.cs`, `VideoPlayer.cs`: controls panel text, start hint,
  `PlayerOptions.LockInput` (changed).

### 5.3 `CouchLink.App`
- `ControlsWindow.cs` (new), `SingleInstance.cs` (new).
- `AppServices.Controls`; `ClientPlay` builds the mapper from it;
  `ClientVideoService`/`ClientStreams` pass the controls text; `RawInputSource`
  reports X1/X2; Start view and session view get ⚙ Controls; `App.OnStartup`
  runs the single-instance check; the crash handler releases the clip
  (changed).

## 6. Error handling

| Situation | Behavior |
|---|---|
| Hook can't be installed | Play continues without blocking; logged; session Details shows "Key blocking unavailable" |
| Exception in the hook callback | Caught; the key is passed on |
| `ClipCursor` fails | Ignored (pointer is hidden anyway); logged once |
| Activate event can't be opened | Second copy starts normally |
| Crash while playing | Crash handler releases the clip; Windows removes the hook with the thread |

## 7. Testing

### 7.1 Unit (CouchLink.Core.Tests)
- `KeyLayout`: Bind replaces the control's keys; moves a key from another
  control and reports it; rebinding a two-key default keeps only the new
  key; reserved keys refused; Reset restores the defaults.
- `ControlSettings`: step-to-sensitivity values; Changed raised; Reset.
- `InputMapper`: an edit takes effect on the next Tick; held keys released
  on Changed; Invert Y flips the right stick's Y.
- `KeyNames`: every default key and every `PadControl` has a name; unknown
  keys fall back to "Key 0x..".
- `ShortcutFilter`: each blocked combination; Tab alone, Alt alone,
  Alt+F4, Ctrl+Alt+Q, Esc alone pass.
- `OverlayText.Controls`: lists every control with current keys.
- `PlayerCore` (Video.Tests): start hint shown for 5 s from the first frame,
  not before a frame, hidden by F1; controls panel text redrawn after an
  edit.
- Single-instance decision: Run / HandOff / Bypass.

### 7.2 Manual gate (added to `docs/gate-results.md`)
- Win key, Alt+Tab, Alt+Esc, Ctrl+Esc do nothing while playing; all work
  after Ctrl+Alt+Q; Alt+F4 leaves.
- Two monitors: the pointer can't leave the game.
- F1 shows the layout; rebind Cross to Space mid-game, F1 shows it and
  Space presses Cross in 2K.
- Mouse sensitivity 1 and 10 feel clearly different; Invert Y flips the
  shot stick.
- Double-click the shortcut while on the Start screen, while hosting and
  while playing: CouchLink comes forward, no second copy (Task Manager).
- A `--windowed-player` second copy still starts and joins.

## 8. Out of scope
- Saving layouts or settings between runs (by design, spec 6.7).
- Per-player or per-game profiles.
- Rebinding the right stick or binding keys to analog stick values.
- Physical gamepads on the client.
- Blocking Ctrl+Alt+Del, Win+L or other secure-desktop keys (Windows
  doesn't allow it).

## 9. Changes to the main design
- 6.3: add "Invert Y" next to the sensitivity slider.
- 6.4: list the blocked shortcuts (Windows keys, Alt+Tab, Alt+Esc,
  Ctrl+Esc), note Alt+F4 still leaves and nothing is blocked with
  `--windowed-player`.
- 6.7: one key per control; reserved keys; editable mid-session; X1/X2
  mouse buttons bindable.
- 7: "Ports in use" row becomes "Second launch: brings the running copy
  forward (not with `--windowed-player`)".
