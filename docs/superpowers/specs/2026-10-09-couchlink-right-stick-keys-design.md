# CouchLink Right-Stick Keys (#59)

Status: Draft, awaiting review
Date: 2026-10-09
Author: Christopher Enriquez (@enriquezchristopher) (with Claude Code)

Builds on the [controller profiles design](2026-10-08-couchlink-controller-profiles-design.md)
and section 2 (Controls) of the
[playing screen & controls design](2026-10-08-couchlink-playing-screen-controls-design.md).

## 1. Goal

Today the right stick only comes from the mouse. Players can also bind keys
to it, the same way WASD drives the left stick. Games like NBA 2K22 put the
pro stick on the keyboard (Num 8/4/2/6), and the shipped NBA 2K22 profile
can then match the game's own keys.

### Agreed requirements
Settled in brainstorming (2026-10-09):
- **Four new bindable controls:** right stick up, down, left and right. A
  held key gives full tilt, two keys give a diagonal, opposite keys cancel.
  Same behaviour as the left stick.
- **Keys win over the mouse.** While any right-stick key is held, the keys
  set the stick and the mouse is ignored. With none held, the mouse drives
  the stick as today. A key move always does the same thing, even if the
  mouse gets bumped.
- **Defaults unchanged.** The default layout gives the four new controls no
  keys, and the default F1 panel looks the same as before.
- **Profiles** name them `RightUp`, `RightDown`, `RightLeft`, `RightRight`.
- **NBA 2K22 profile** puts the pro stick on Num 8/4/2/6.

### Success criteria
- Keys can be bound to the four right-stick directions in the editor and in
  profile files.
- Held right-stick keys set the stick; the mouse takes over again when they
  are released.
- The default layout and the default F1 panel are unchanged.
- The NBA 2K22 profile has the pro stick on Num 8/4/2/6.
- Tests cover keys only, mouse only, keys held while the mouse moves, the
  new file names and the 2K22 bindings.

## 2. Core (`CouchLink.Core/Input`)

### 2.1 `PadControl`
Four members after `LeftRight`: `RightUp, RightDown, RightLeft, RightRight`.
Nothing stores or sends `PadControl` as a number (the client sends a DS4
state, profiles use names), so inserting them is safe. The summary comment
changes to say the right stick comes from the mouse or from keys.

The unit test that pins the control names gains the four new names.

### 2.2 `KeyNames.Groups`
A `("Right stick", [RightUp, RightDown, RightLeft, RightRight])` group after
"Left stick". The editor rows, the F1 panel and `ProfileFile.Save`'s
control order all come from this list. `KeyNames.Of` names them "Right
stick up", "Right stick down", "Right stick left" and "Right stick right",
like the left stick's rows. With a label, F1 shows e.g.
"Pro stick up (Right stick up)".

### 2.3 `KeyLayout`
The defaults have no entry for the four new controls, so `KeysFor` returns
none. Binding, taking a key from another control, and `Replace` work as for
any other control: a key can't be on both a right-stick direction and a
button.

### 2.4 `InputMapper.Tick`
```
right keys held = any of RightUp/Down/Left/Right is down
if right keys held:
    (rx, ry) = StickMath.FromDirections(up, down, left, right)
    _mouse.Reset()            // drop mouse movement made while keys were held
else:
    (rx, ry) = _mouse.Update(dt)
```
The reset means that releasing the keys returns the stick to centre, not to
a leftover mouse flick. Mouse movement arriving while keys are held is
added to `MouseStick` and cleared on the next tick, so it never reaches the
pad. No change to `MouseStick`.

## 3. App and overlay

### 3.1 Controls editor
- The greyed-out "Right stick · Mouse" button and its heading are removed.
  The "Right stick" group from `KeyNames.Groups` gives four bindable rows,
  each with a label box like every other row.
- "Mouse sensitivity (right stick)" and Invert Y stay as they are. They
  apply to the mouse only.

### 3.2 F1 panel (`OverlayText.Controls`)
- The four right-stick rows are listed only when at least one of them has
  a key, so the default panel doesn't fill up with "(none)". If one has a
  key, all four are listed, so an unbound direction shows "(none)".
- The last line, `Right stick  Mouse (sensitivity N[, inverted])`, always
  stays. The mouse still drives the stick whenever no key is held.

## 4. Profiles

### 4.1 File format
- The control names `RightUp`, `RightDown`, `RightLeft` and `RightRight`
  are accepted in `controls`, with `keys` and `label` like any other.
- `format` stays **1**. Files without these names load as before and give
  the right stick no keys.
- An older CouchLink opening a file that uses them refuses it with its
  existing error, `Unknown control "RightUp"`. A café runs one version on
  every PC, so a format bump isn't worth it.
- Save writes them (in `KeyNames.Groups` order, after the left stick) only
  when they have keys or a label, as for every control today.

### 4.2 NBA 2K22 profile (`src/CouchLink.App/profiles/nba-2k22.json`)
| Control | Key | Label |
|---|---|---|
| `RightUp` | `Num8` | Pro stick up |
| `RightDown` | `Num2` | Pro stick down |
| `RightLeft` | `Num4` | Pro stick left |
| `RightRight` | `Num6` | Pro stick right |

The header comment drops "the pro stick stays on the mouse" and says the
pro stick is on Num 8/4/2/6, with the mouse still working when none is
held. None of these keys is used by another control in the profile.

## 5. Testing (CouchLink.Core.Tests, test first)

- **InputMapper:** keys only (each direction, diagonal, opposite keys
  cancel); mouse only (unchanged); a key held while the mouse moves gives
  the key's tilt; after release the stick is centred and the next mouse
  movement drives it again; a default layout leaves the right stick on the
  mouse.
- **PadControl names:** the pinned list includes the four new names.
- **ProfileFile:** loads the new names with keys and labels; saves and
  reloads them; a file without them gives no right-stick keys.
- **KeyLayout:** binding a key to `RightUp` takes it off a button that had it.
- **OverlayText.Controls:** the default panel is unchanged; with one
  right-stick key bound, all four rows are listed, with labels.
- **NBA 2K22 profile:** the existing pin test gains Num 8/4/2/6 and the
  "Pro stick" labels.

Manual gate (added to `docs/gate-results.md`): load the NBA 2K22 profile,
hold Num 8 in a gamepad tester or the game, move the mouse while holding,
release and check the mouse drives the stick again.

## 6. Docs
- README controls guide: the right stick is the mouse, or keys if bound.
- Controller profiles spec: remove "the right stick is the mouse and can't
  appear in `controls`" and point to this spec.
- ROADMAP: list #59.

## 7. Out of scope
- Adding keys and mouse together (rejected: a mouse bump would bend a key
  move).
- Partial tilt from keys (walk/run modifiers).
- Keys for the triggers' analogue range.
