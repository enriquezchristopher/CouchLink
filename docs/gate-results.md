# PadTest Gate Results

Date: 2026-10-05
Host GPU / Windows build: <fill in> / Windows 10

| Check | 2K14 | 2K22 |
|---|---|---|
| Windows sees 9 pads (9 "HID-compliant game controller" devices appear while PadTest runs, 0 after it exits) | pass (checked automatically 2026-10-05) | (same) |
| `PadTest check 9`: 9 separate DS4 devices, each pad's input reaches exactly one device, no leaks (game-free) | pass (2026-10-05) | (same) |
| `PadTest check 9` Test 3: all 32 controls (every button, 8 D-pad directions, both ends of all 4 stick axes, L2/R2 half + full) read back exactly on each of the 9 pads, other pads stay neutral (game-free) | pass (2026-10-05) | (same) |
| joy.cpl: pressing a pad's number key moves only that pad's X axis + button | <pass/fail> | (same) |
| Controller select shows 9 pad icons + keyboard | **fail: only 1 pad icon (labelled P4)** | <pass/fail> |
| Each pad key moves exactly one player | <pass/fail> | <pass/fail> |
| Host keyboard controls its own player at the same time | <pass/fail> | <pass/fail> |

Notes:
- 2K14 picked up only 1 of 9 virtual DS4 pads. Reported limit for 2K14 on PC: 4 XInput (Xbox) + 2 DirectInput controllers (https://steamcommunity.com/groups/morethan4localmultiplayer/discussions/0/135514823815392980). DS4 counts as DirectInput.
- 2K22: Steam detected 9 controllers; in-game controller-select count not checked yet.

## Decision (2026-10-05)
Gate accepted by the project owner on the basis that Steam detects all 9
virtual controllers and `PadTest check 9` passes. In-game 2K22 count is not
required. Known limitation: 2K14 uses only 1 of the virtual DS4 pads.

## Input path (two-PC test)

| Check | Result |
|---|---|
| Host: app Host button + UDP input packets for slot 2 -> 1 virtual pad appears, status "Virtual pads: 1", pad unplugged when app closes (automated, one PC) | pass (2026-10-05) |
| Client keys/mouse reach the host pad (joy.cpl shows W = up, K = button, mouse = right stick, re-centers) | <pass/fail> |
| Focus loss releases held keys immediately | <pass/fail> |
| Closing client while holding a key -> pad centers within ~0.5 s | <pass/fail> |
| Two client PCs on different slots control separate players in 2K22 | <pass/fail> |
