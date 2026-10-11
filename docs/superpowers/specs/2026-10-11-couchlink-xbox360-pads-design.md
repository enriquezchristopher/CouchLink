# CouchLink Host Controller Type & Live Controller View (#49)

Status: Draft, awaiting review
Date: 2026-10-11
Author: Christopher Enriquez (@enriquezchristopher) (with Claude Code)

Builds on the [lobby & sessions plan](../plans/2026-10-07-couchlink-plan7-lobby-sessions.md)
and the [controller profiles design](2026-10-08-couchlink-controller-profiles-design.md).
Mockups: [host setup](https://claude.ai/artifact/NpUcdDLmhULz9vAVrdDG8Y),
[live controller](https://claude.ai/artifact/FGDPuot1byFw9U7z78YHAC) (layout A).

## 1. Goal

Every joining player gets a virtual DualShock 4 on the host. NBA 2K14 ignores
its left stick, and the likely reason is that it only handles Xbox 360
controllers. The host picks which virtual controller players get: DualShock 4
(today's behaviour) or Xbox 360. To make room for that choice, clicking Host
no longer starts hosting straight away. It opens a setup screen first.

With two pad types, the Controls editor also shows the session's controller
at the top, drawn accurately, and it reacts live as the player presses their
keys. Clicking a button on the picture changes its key.

### Agreed requirements
Settled in brainstorming (2026-10-11):
- **The host decides, for the whole session.** Clients have no choice and
  keep playing with keyboard and mouse. Physical controllers on the client
  are out of scope.
- **Set before hosting starts.** It can't change while hosting. To change it,
  the host stops hosting and sets up again.
- **Host is a two-step flow.** Start → **Host a game** (setup) → **Start
  hosting** → lobby. Nothing is broadcast and no pads exist until Start
  hosting.
- **Xbox 360 takes at most 4 players**, fixed. A 5th joiner is refused with a
  clear message.
- **Players see Xbox names** (A, B, X, Y, LB...) in the F1 panel and the
  controls editor during an Xbox 360 session.
- **Stream settings stay in the lobby only**, live as today. The setup
  screen holds only the controller.
- **The last controller choice is remembered** per PC. DualShock 4 is the
  first-run default.
- **No throwaway 2K14 check first.** The guess is tested by the final manual
  test.
- **Live controller in the Controls editor, layout A** (picked against the
  ui-ux-pro-max guidelines over callouts and a separate tab): the picture
  sits on top, and the list stays below it for Find, labels, keyboard and
  screen-reader users.
- **Accurate drawings** of both pads, drawn from reference photos, with no
  logos (PS and Guide are plain buttons).

### Success criteria
- Clicking Host opens the setup screen. Back and Esc return to Start without
  hosting.
- Start hosting with DualShock 4 behaves exactly as hosting does today.
- With Xbox 360, every player's pad is a virtual Xbox 360 pad, WASD moves the
  player in NBA 2K14, and every button works.
- With Xbox 360, a 5th joiner is refused with "Host is full: Xbox 360
  sessions take up to 4 players."
- Players in an Xbox 360 session see Xbox names in the F1 panel and the
  controls editor.
- The Controls editor shows the session's controller. Pressing a bound key
  lights that part and its key cap within 100 ms, and clicking a part starts
  changing its key.
- The result is recorded in #30, `docs/gate-results.md` and the README's game
  notes.

### Out of scope
- Reading a real Xbox or DualShock pad on the client.
- Changing the controller type while hosting.
- Counting a real Xbox pad plugged into the host against the 4-player limit
  (the README notes it instead, see 8).
- Reading mouse movement or wheel in the controller preview.

## 2. Core

### 2.1 `PadKind` (`CouchLink.Core/Pads/PadKind.cs`)
```csharp
public enum PadKind : byte { DualShock4 = 1, Xbox360 = 2 }
```
It's sent on the wire, so members keep explicit values. `PadKinds` (a static
class beside it) holds what both ends need:
- `Label(kind)`: "DualShock 4" / "Xbox 360"
- `Capacity(kind)`: 9 / 4 (clients only, P2–P10 / P2–P5)

### 2.2 Xbox 360 report (`CouchLink.Core/Input/Xbox360Report.cs`)
A pure translation from the client's `PadState` (DS4-shaped) to what an
Xbox 360 pad takes, so it is unit-tested without ViGEmBus:

```csharp
public readonly record struct Xbox360Report(
    Xbox360Buttons Buttons, byte LeftTrigger, byte RightTrigger,
    short LeftX, short LeftY, short RightX, short RightY)
{
    public static Xbox360Report From(PadState s);
}
```
`Xbox360Buttons` is a Core `[Flags]` enum with XInput's bit values (Up 0x0001
... Y 0x8000, Guide 0x0400), so Core doesn't depend on the ViGEm package.

| `PadButtons` | Xbox 360 |
|---|---|
| Cross, Circle, Square, Triangle | A, B, X, Y |
| L1, R1 | LeftShoulder, RightShoulder |
| L3, R3 | LeftThumb, RightThumb |
| Options, Share | Start, Back |
| PS | Guide |
| D-pad | Up, Down, Left, Right, after `DpadMath.FromButtons` so opposite directions cancel exactly as on the DS4 |
| Touchpad | nothing |

- **Triggers:** `L2`/`R2` bytes pass through unchanged (0–255 on both).
- **Sticks:** DS4 axes are bytes with 128 at center and Y growing downward;
  XInput axes are shorts with 0 at center and Y growing upward. X:
  `v < 128 ? (v - 128) * 256 : (v - 128) * 32767 / 127`, so 0 → -32768,
  128 → 0, 255 → 32767. Y uses the mirror image: 0 (up) → 32767,
  128 → 0, 255 (down) → -32768. Center stays exactly 0 and full tilt
  reaches the full range both ways.

### 2.3 `IVirtualPadFactory`
Unchanged. The factory already decides what `Create()` returns. `PadManager`
and the input path don't change.

### 2.4 `HostSession` capacity
`HostSession.Capacity` (const 9) becomes an instance property set from the
session's `PadKind` at construction, along with `PadKind` itself.
`FreeSlot()` looks only at slots `FirstSlot .. FirstSlot + Capacity - 1`, so
Xbox 360 players get P2–P5. A rejoin or takeover keeps its seat as today.
When no slot is free, the refusal reason depends on the kind: `Full` for
DualShock 4 and `XboxFull` for Xbox 360. `SessionServer` passes the kind
through to the session.

### 2.5 Protocol (`CouchLink.Core/Protocol`)
- `Wire.Version` goes from 1 to 2. Hosts and clients on different versions
  already ignore each other, and the join list shows an older host as
  incompatible.
- **Accepted:** the body becomes slot (1) + pad kind (1). Decode rejects any
  other length, a slot outside 2–10, or an unknown kind.
  `SessionMessage.Accepted(slot, kind)` and `SessionMessage.PadKind`.
- **Denied:** a new `DenyReason.XboxFull = 5`. Decode's upper bound moves to
  it.
- `SessionFraming`'s summary comment is updated to match.
- `HostAnnounce` is unchanged. It already carries the capacity, so the join
  list shows an Xbox 360 host as "n / 5 players" without further work.

### 2.6 `ClientSession`
- On Accepted it keeps `PadKind` and passes it on:
  `_effects.StartPlaying(slot, kind)`.
- On a reconnect, an Accepted with the same slot but a different kind can
  only come from a new hosting session, so it ends with `LostHost`, like a
  different slot does.
- New text: `XboxFull = "Host is full: Xbox 360 sessions take up to 4 players."`

### 2.7 Control names (`KeyNames`)
- `KeyNames.Of(PadControl, PadKind)`: Xbox 360 names are A, B, X, Y, LB, RB,
  LT, RT, LS, RS, Start, Back. Sticks and the D-pad keep their names. The
  current `Of(PadControl)` stays and means DualShock 4.
- `KeyNames.GroupsFor(PadKind)`: the same groups. For Xbox 360 the Menu group
  is `[Options, Share]` (no Touchpad), and the group names stay the same.
- `KeyNames.Groups` stays the full DualShock 4 list, because
  `ProfileFile.Save` uses it for the control order. Profiles and key layouts
  don't change. A Touchpad binding is still saved and loaded, it just isn't
  shown or sent as anything in an Xbox 360 session.
- `Labelled(control, label, kind)` gives "Pass (A)" for a profile label.

## 3. Pads (`CouchLink.Pads`)
- `ViGEmPad` is renamed `ViGEmDs4Pad`, unchanged.
- **New `ViGEmXbox360Pad`:** `client.CreateXbox360Controller()`,
  `AutoSubmitReport = false`, Connect, neutral. `Apply` sets the buttons, axes
  and sliders from `Xbox360Report.From(state)` and submits once. `Dispose`
  disconnects and roots the controller in the same never-free list, for the
  same native-thread reason (the comment moves to a shared spot).
- `ViGEmPadFactory.TryCreate(PadKind kind, ...)` keeps the kind, and
  `Create()` builds the matching pad.

## 4. App: host

### 4.1 `HostSetupView` (new)
Matches the mockup:
- A back arrow and "Host a game" heading, like the Join list. The arrow and
  Esc go back to Start.
- "Pick the controller the game will see. Friends can find this PC once you
  start hosting."
- A **Controller** card with a two-option segmented control (DualShock 4 /
  Xbox 360), the same `Segmented` style as Quality, with a `SegmentIndicator`.
  Helper text below changes with the choice:
  - DualShock 4: "Up to 9 players. Works with most games, like NBA 2K22."
  - Xbox 360: "Up to 4 players. For games that only work with Xbox
    controllers, like NBA 2K14."
- A footer with one primary button, **Start hosting**, which is the default
  button for Enter.
- If hosting fails to start, an error `Banner` appears above the card with the
  same text the alert shows today, and the view stays as it was.
- Automation IDs `HostSetupBack`, `PadKindDualShock4`, `PadKindXbox360` and
  `StartHosting`, for UI tests and screenshots.

### 4.2 Flow (`MainWindow`)
- `ShowStart` → Host → `ShowHostSetup()`.
- Start hosting → `new HostLobbyView(kind)` and `lobby.TryStart(...)`. On
  success the lobby is shown. On failure the setup view shows the banner.
- Stop hosting in the lobby goes back to Start, as today.

### 4.3 `HostSettings` (new, `CouchLink.App/Presentation`)
Saves `"hostController": "xbox360" | "dualshock4"` in
`%LOCALAPPDATA%\CouchLink\settings.json`, following `MotionSettings`: other
keys are kept, a missing or bad value reads as DualShock 4 and is logged, and
a failed save is logged and keeps the choice for this run. The setup view
preselects it and saves it on Start hosting (not on every click).

### 4.4 `HostService` and `HostLobbyView`
- `HostService.TryStart(PadKind kind, StreamSettings settings, ...)` creates
  the factory and session with the kind. `HostService.PadKind` and
  `Capacity` are exposed for the lobby. The announce uses the instance
  capacity.
- The lobby's players card gets a read-only line, "Controller: Xbox 360 · up
  to 4 players", as text (not a disabled control).
- `PLAYERS n / m` uses `Capacity + 1`.
- `DescribeMode` and the log line say the kind: `Host (Xbox 360, virtual
  pads: 2)`, `Hosting started (Xbox 360)`.

## 5. App: client
- `ClientPlay.TryStart` takes the `PadKind` from `StartPlaying`.
- The F1 overlay (`OverlayText`) and the controls editor (`ControlsWindow`,
  `ControlFilter`) use `GroupsFor(kind)` and `Of(control, kind)`. So in an
  Xbox 360 session the editor lists A/B/X/Y, has no Touchpad row, and
  searching "LB" finds the left bumper.
- Opened from the title bar outside a session, the controls editor uses
  DualShock 4 names, as today.
- `SessionTextTests`/the session screen show `XboxFull`.

## 6. App: live controller in the Controls editor
Layout A of the mockup: the controller on top, today's list below.

### 6.1 Which parts are pressed (Core)
`PadParts.Pressed(PadState state, PadKind kind)` in `CouchLink.Core/Input`
returns the set of `PadControl`s to light, plus the two stick offsets as
-1..1 from `LX/LY/RX/RY`. It is pure and unit-tested. L2/R2 count as pressed
when their value is above 32 (of 255) or their button bit is set, so a light
trigger still shows. For Xbox 360,
Touchpad is never in the set.

### 6.2 `ControllerView` (new WPF control, `CouchLink.App/Ui`)
- Draws the session's pad as vector shapes. The geometry is ported from the
  mockup's SVG, which was measured from reference photos: a top-view
  DualShock 4 photo and an Xbox 360 front diagram. The DualShock 4 has the
  touchpad, Share/Options, speaker grille, plain PS button, four separate
  D-pad arms and side-by-side sticks. The Xbox 360 has offset sticks, a round
  D-pad, Back/Guide/Start (Guide is a plain ring) and coloured A/B/X/Y caps.
  No logos. Colours come from the theme tokens, so High Contrast works.
- Each button, D-pad arm, trigger, bumper and stick is its own element with
  an `AutomationProperties.Name` such as "Cross, change key". Hit areas are at
  least 24×24 px at the drawn size.
- `PadKind Kind` and `PadState State` dependency properties. Setting `State`
  updates the parts:
  - A pressed part fills with `PrimaryBrush`, gets a soft glow and scales to
    92%.
  - Triggers fill.
  - Stick thumbs move toward the direction held.
  - With Reduce motion on (`MotionSettings`), there's no scale or easing, but
    the pressed state still shows.
- `event Action<PadControl> PartClicked`. A stick raises its first direction,
  so the editor can jump to that group.
- Fixed height (about 190 px at the 460 px window width), centred, inside a
  card above Find. The caption under it reads "Press your keys to test them ·
  Click a button to change its key", or "Changing Cross: press a key · Esc
  cancels" while listening.

### 6.3 Live input in the editor (`ControlsWindow`)
- While the editor window is active, its `PreviewKeyDown`/`PreviewKeyUp`
  feed a local `InputMapper` over `AppServices.Controls`. A 60 Hz
  `DispatcherTimer` ticks it and sets `ControllerView.State`. So W tilts the
  stick and K lights Cross, through the player's current bindings, the same
  as the host would see. Deactivating the window releases every key.
- Mouse buttons and mouse movement aren't fed in: in the editor, a click
  picks a part, and moving toward a button shouldn't jitter the right stick.
  Mouse-bound controls still show their key caps in the list.
- While a row is listening for a new key, the preview pauses so the key being
  bound doesn't also light its old control.
- Each row's key caps light while their key is held (`KeyCap` gets an
  `IsLit` state), so the picture and the list react together.

### 6.4 Changing a key from the picture
`PartClicked` does what clicking that row does today: it scrolls the row into
view and starts listening. The picture outlines the part being changed until
a key is set or Esc cancels. Clicking a stick scrolls to its group without
listening. The Test tab and callouts from the mockup are not built.

### 6.5 Pad type in the editor
`ControlsWindow.Open(..., PadKind kind)`. In a session, the client passes the
session's kind. From the title bar outside a session, it's DualShock 4. The
list's names follow the same kind (section 5).

## 7. Testing
TDD, in the existing test files:
- **`Xbox360ReportTests` (new):** every button mapping, Touchpad maps to
  nothing, D-pad opposite directions cancel, triggers pass through, stick
  center → 0, full tilt each way → ±full range, Y is flipped.
- **`HostSessionTests`:** Xbox 360 accepts 4 players on P2–P5 and refuses the
  5th with `XboxFull`; a freed seat lets the next one in; a reserved seat
  counts toward the 4; DualShock 4 still takes 9 and refuses with `Full`.
- **`SessionFramingTests`:** Accepted round-trips slot and kind, rejects a
  1-byte body and an unknown kind; `XboxFull` round-trips.
- **`ClientSessionTests`:** the kind reaches `StartPlaying`; a reconnect with a
  different kind ends the session; `XboxFull` gives the new text.
- **`KeyNamesTests`:** Xbox names for every control; the Xbox Menu group has no
  Touchpad; `Groups` (for profiles) is unchanged.
- **`SessionLoopbackTests`:** a full join over loopback with Xbox 360 delivers
  the kind to the client.
- **`HostSettings` tests:** following the `MotionSettings` tests, covering a
  missing file, a bad value, keeping other keys, and a failed save.
- **App UI tests:** Host opens setup, Back returns to Start, Start hosting opens
  the lobby showing the chosen controller.
- **`PadPartsTests` (new):** each button bit lights its part, triggers above
  and below the threshold, stick offsets at center and full tilt, and no
  Touchpad for Xbox 360.
- **`ControllerView` tests (App.Tests):** both kinds build every part with an
  automation name; Xbox 360 has no Touchpad part; clicking a part raises
  `PartClicked` with its control; setting `State` marks the right parts
  pressed.
- **`ControlsWindow` tests:** a held bound key lights the picture and its key
  cap; a click on a part starts listening on that row; Esc cancels; the
  preview pauses while listening; deactivating releases keys.

Manual, on real hardware:
- Host NBA 2K14 in Xbox 360 mode with one client: WASD moves the player and
  every bound button works.
- Join a 5th client and see the refusal.
- Host NBA 2K22 in DualShock 4 mode: it works as before.
- Open the Controls editor in and out of a session in both modes, then press
  every default key and rebind one from the picture.

## 8. Docs
- README: the host flow now has a setup step. Game notes say "NBA 2K14: host
  with Xbox 360, up to 4 players" and that a real Xbox pad on the host uses
  one of the 4 Xbox slots Windows allows.
- `docs/cafe-setup-guide.md`: the setup step and when to pick Xbox 360.
- `docs/gate-results.md` and #30: the 2K14 result.
- Screenshots of the setup screen, the Xbox 360 lobby and the Controls editor
  with the controller (both kinds), refreshed in `docs/images` for the PR.
- README's controls section: the live controller and click-to-change.
- `CHANGELOG.md` is written by release-please from the commit messages and
  isn't edited by hand.
