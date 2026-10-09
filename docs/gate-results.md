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
| Client keys/mouse reach the host pad (joy.cpl shows W = up, K = button, mouse = right stick, re-centers) | waived by owner (2026-10-05) |
| Focus loss releases held keys immediately | waived by owner (2026-10-05); covered by unit tests |
| Closing client while holding a key -> pad centers within ~0.5 s | waived by owner (2026-10-05); covered by unit tests |
| Two client PCs on different slots control separate players in 2K22 | waived by owner (2026-10-05) |

## Audio (Plan 6)

Date: <fill in>
Host / client PCs and sound devices: <fill in>

| Check | Result |
|---|---|
| One PC, host + client (`--windowed-player`), YouTube on the host: client says "muted: host is this PC", its packet count rises, no echo | pass (checked automatically 2026-10-07: ~200 packets/s, 0 late/concealed) |
| Two PCs, host `--test-tone`, client `--audio-loss=5`: no audible gaps; F2 "repaired" rises | <pass/fail> |
| Two PCs, 30 minutes of a game: F2 audio buffer stays within 5 ms of where it started; no dropouts | <pass/fail> |
| Unplug / switch the client's headphones mid-session: audio back within about 1 s | <pass/fail> |
| Sound feels in time with the picture (no visible lag between a hit and its sound) | <pass/fail> |

## Lobby & sessions (Plan 7)

Date: <fill in>
Host / client PCs: <fill in>

| Check | Result |
|---|---|
| One PC, host + client (`--windowed-player`): host listed as "<PC> · 1/10 players", popup appears, Allow -> playing as P2 | <pass/fail> |
| Two PCs: Deny -> "Request denied."; no answer for 30 s -> "The host didn't answer." | <pass/fail> |
| Two PCs: Allow everyone -> joins with no popup | <pass/fail> |
| Kick -> "You were removed by the host."; slot freed | <pass/fail> |
| Stop hosting -> "Host ended the session." on every client | <pass/fail> |
| Pull the client's cable for 5 s -> "Reconnecting...", then playing again on the same slot and the same 2K player without touching anything | <pass/fail> |
| Pull the client's cable for 20 s -> "Reconnecting...", then "Lost the host." after 10 s; pick the host again within the minute -> same slot and the same 2K player, no popup | <pass/fail> |
| Kill and restart the client -> same slot, no popup | <pass/fail> |
| Change resolution mid-session -> clients' picture back within about 1 s | <pass/fail> |
| Three or more clients join at once -> distinct slots | <pass/fail> |

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
| Shipped NBA 2K22 profile in 2K22 (Num Lock on): Space passes, Num 5 shoots, Num 1 bounce pass, Num 3 lob, Enter sprints, Left Shift posts up, Tab calls a play, Page Down pauses, the mouse also moves the pro stick | <pass/fail> |
| Bind Num 8 to Right stick up in the editor; in a gamepad tester (joy.cpl) holding Num 8 pushes the right stick up; moving the mouse while holding changes nothing; after release the mouse moves the stick again | <pass/fail> |
| Shipped NBA 2K22 profile in 2K22: Num 8/2/4/6 work the pro stick; F1 lists "Pro stick up (Right stick up)  Num 8" | <pass/fail> |
| F1 with the NBA 2K22 profile on a 1366x768 (or 720p) client screen: the whole panel fits, down to the "Right stick  Mouse" line | <pass/fail> |

## Stream quality (Plan 10)

Date: <fill in>
Host / client PCs: <fill in>

| Check | Result |
|---|---|
| `VideoTest encode 10 --quality=high` on AMD: `h264_amf (balanced)` opens (or is skipped and `h264_amf` opens); median encode time within 1 ms of Balanced | pass, RX 6600 at 2560x1080: 4.34 ms Balanced, 4.36 ms High, 4.39 ms Max (2026-10-08) |
| Same on NVIDIA with `h264_nvenc (p3)` | <pass/fail, ms> |
| AMF: `quality=balanced` encodes visibly better than Balanced at the same bitrate (else switch to `quality=quality`, spec 2.3) | pass, small: RX 6600, FFmpeg `testsrc2` 1080p60 at 3 Mbps, `usage=ultralowlatency`. The driver default is byte-identical to `quality=speed` (PSNR 34.53); `balanced` 34.57 (luma +0.11 dB); `quality` 34.56, no better, so `balanced` stays (2026-10-08) |
| Live session at Max on the gigabit switch: colored text sharper than at Balanced; F2 packet loss on the client no higher than at Balanced, keyframes included | <pass/fail> |
| Changing Quality while two clients play: both stay in, the picture comes back within a second | <pass/fail> |
| Host card forced to 100 Mbps: the orange warning appears as clients join at High, and goes away after switching to Balanced | <pass/fail> |

## UI/UX revamp (Plan 12)

Date: <fill in>
PCs / Windows builds: <fill in>

| Check | Result |
|---|---|
| Windows 10 and 11: every window has the dark title bar; Windows 11 shows the navy caption color | <pass/fail> |
| Windows High Contrast on while CouchLink runs: every screen switches to system colors at once and stays readable; off again: back to the dark theme | <pass/fail> |
| "Show animations in Windows" off: screen changes, toasts and button hovers are instant; the spinners stand still | <pass/fail> |
| Keyboard only: Tab reaches Host a game, Join a game, Controls, Help; the purple focus ring shows; Enter presses; the Help menu opens and works with arrows | <pass/fail> |
| Keyboard only in the Controls editor: Find, Labels, each row, the slider (arrows), Invert Y, Reset, Done | <pass/fail> |
| While playing, Space and Enter in the game never press Leave, Controls or a header button on the client's main window | <pass/fail> |
| Ctrl+Alt+C over the game opens the new editor on top; Done returns to the game | <pass/fail> |
| Two players join at once: two toasts stack in the bottom-right, both count down, the host's taskbar button flashes, the game keeps the keyboard | <pass/fail> |
| Stop hosting with two players in: the confirm names both; Enter keeps hosting; Stop hosting ends both sessions | <pass/fail> |
| Join by address with "192.168": the error shows under the box and nothing connects | <pass/fail> |
| `--crash-test=ui` and `--crash-test=startup`: the themed crash dialog opens with its four buttons working | <pass/fail> |
| 125% and 150% display scaling: nothing clipped on Start, the lobby with 9 players, the join list, the editor with Labels on | <pass/fail> |
