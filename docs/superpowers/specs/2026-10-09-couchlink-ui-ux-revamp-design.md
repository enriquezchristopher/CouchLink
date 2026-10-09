# CouchLink UI/UX Revamp

Status: Approved in brainstorming; awaiting spec review
Date: 2026-10-09
Author: Christopher Enriquez (@enriquezchristopher) (with Claude Code)

Restyles every window in CouchLink.App and reworks the weak spots in the
host, join, session and controls flows. It changes what the
[lobby & sessions design](2026-10-07-couchlink-lobby-sessions-design.md),
the [playing screen & controls design](2026-10-08-couchlink-playing-screen-controls-design.md)
and the [controller profiles design](2026-10-08-couchlink-controller-profiles-design.md)
say about how screens look. It does not change what any of them do.

## 1. Goal

CouchLink today uses stock WPF controls with no styling: grey buttons on a
white window. It works, but it looks unfinished next to the games it runs
alongside, and a few flows hide things a first-time player needs (the
Controls button, why a host can't be joined, what happens next after
clicking Join).

After this work a player at a café PC sees a dark, game-launcher style app
where the next step is always obvious, every state (searching, waiting,
reconnecting, nothing found) says what is going on, and the controls editor
shows clearly what changed when a key is bound.

### Agreed requirements
Settled in brainstorming (2026-10-09):
- **Scope: visuals and UX flows.** Every window in CouchLink.App gets the new
  theme, and the host lobby, join list, session screen, controls editor,
  approval popup and dialogs get the flow changes in section 5.
- **Visual direction A, "game launcher":** deep navy surfaces, a purple
  primary, rose for danger, and a color per player. Chosen over a Windows 11
  Fluent look and a neon arcade look.
- **Structure: guided steps plus a header.** The Start → Host or Join →
  Session flow stays. A slim header on every main-window screen holds a
  Controls button and a Help menu. Chosen over a side navigation rail.
- **Our own theme in XAML resource dictionaries.** No third-party UI library
  and no WPF Fluent `ThemeMode`. No new NuGet packages.
- **Behavior stays the same,** with one addition: Stop hosting asks first
  when players are connected.

### Non-goals
- The in-game F1 and F2 overlays drawn by `PlayerWindow` in CouchLink.Video.
  They use different rendering and stay as they are.
- A light theme or a theme switch. The app is dark only, plus Windows High
  Contrast support (section 3.6).
- MVVM, dependency injection or any other architecture change. Views keep
  code-behind with events, as today.
- Protocol, session, streaming or input changes.
- Localization.

## 2. Users and context

Players at internet cafés and LAN rooms, on shared PCs, often opening
CouchLink for the first time with a game already waiting. They want to get
in fast. The host is usually the café owner or the most experienced player.
Windows 10 and 11, x64, often 1080p monitors at 100% scaling, mouse and
keyboard.

## 3. Design tokens

All tokens live in `Theme/Tokens.xaml` as `Color` resources plus a
`SolidColorBrush` for each (brushes frozen). Components refer to brushes by
key, never to raw hex values.

### 3.1 Colors

| Token | Hex | Use |
|---|---|---|
| `Chrome` | `#0B0B1A` | Title bar caption color (Windows 11) |
| `Background` | `#0F0F23` | Window background |
| `Input` | `#16152B` | Text box, combo box fill |
| `Card` | `#1E1C35` | Cards, secondary buttons, header buttons |
| `Raised` | `#27273B` | Key caps, toggles off, neutral pills, icon tiles |
| `Border` | `#2E2B4F` | Card, input and divider borders |
| `BorderStrong` | `#3B3858` | Key cap and toggle borders |
| `Text` | `#E2E8F0` | Body text |
| `TextMuted` | `#94A3B8` | Secondary text, captions |
| `TextHint` | `#64748B` | Placeholder text only (not for information) |
| `Primary` | `#7C3AED` | Primary buttons, selected segment, toggles on, progress |
| `PrimaryHover` | `#6D28D9` | Primary button hover and pressed |
| `PrimaryText` | `#A78BFA` | Ghost buttons, links, focus ring, icons on dark |
| `ListeningFill` | `#2A1D4F` | Controls editor row while listening |
| `Danger` | `#FB7185` | Danger text and outline |
| `DangerFill` | `#F43F5E` | Filled danger button (white text) |
| `Warning` | `#FBBF24` | Warning text, Reconnecting pill text |
| `Success` | `#4ADE80` | Live pill text |

Banner and pill fills:

| Kind | Fill | Border | Text |
|---|---|---|---|
| Warning banner | `#2A1F05` | `#6B4F0A` | `#FDE68A` |
| Error banner | `#2A0F18` | `#7F1D35` | `#FECDD3` |
| Info banner | `#1E1C35` | `#2E2B4F` | `#E2E8F0` |
| Live pill | `#14532D` | none | `Success` |
| Reconnecting pill | `#422006` | none | `Warning` |
| Neutral pill | `Raised` | none | `TextMuted` |

Measured WCAG contrast (all pass AA at 4.5:1): Text on Background 15.3,
TextMuted on Card 6.4 and on Background 7.4, white on Primary 5.7,
PrimaryText on Background 6.9 and on Card 6.1, Danger 7.0, Warning 11.3,
Success 10.8, Success on Live pill 5.2, Warning on Reconnecting pill 8.7.

### 3.2 Player colors

A player's color follows them everywhere they appear: lobby rows, join-list
dots and the session screen. Text on a player color is `Background`
(`#0F0F23`), at 9.5:1 or higher on all ten.

| Slot | Hex | Slot | Hex |
|---|---|---|---|
| P1 | `#C4B5FD` | P6 | `#F9A8D4` |
| P2 | `#FDA4AF` | P7 | `#BEF264` |
| P3 | `#FCD34D` | P8 | `#FDBA74` |
| P4 | `#6EE7B7` | P9 | `#5EEAD4` |
| P5 | `#7DD3FC` | P10 | `#A5B4FC` |

`PlayerColors.For(byte slot)` returns the brush; slots outside 1 to 10 get
`Raised`.

### 3.3 Type

Segoe UI Variable (falling back to Segoe UI) for everything except stats and
key caps, which use Cascadia Mono (falling back to Consolas). Both ship with
Windows, so nothing is bundled.

| Style | Size | Weight |
|---|---|---|
| Display | 28 | Bold |
| Title | 20 | SemiBold |
| Subtitle | 15 | SemiBold |
| Body | 14 | Regular (the window default, up from WPF's 12) |
| Caption | 12 | Regular, `TextMuted` |
| Overline | 11 | SemiBold, uppercase, letter-spaced, `TextMuted` (card headings) |
| Mono | 12 | Regular |

### 3.4 Spacing, radius, size

- Spacing steps: 4, 8, 12, 16, 24, 32. Window content padding 16; card
  padding 12; gap between cards 12.
- Radius: 6 inputs and key caps, 8 buttons and banners, 10 cards, 12 hero
  buttons, full pill for pills and toggles.
- Interactive targets are at least 32 px tall (hero buttons 72, primary
  footer buttons 40).

### 3.5 Motion

Hover and pressed color changes take 120 ms. Screen changes in the main
window and toasts fade and slide 8 px in 180 ms. Nothing else animates
except the scanning spinner and the toast countdown bar. When Windows
"Show animations" is off (`SystemParameters.ClientAreaAnimation` is false)
all transitions are instant and the spinner is replaced by a static icon.

### 3.6 High Contrast

When `SystemParameters.HighContrast` is true, `Theme/HighContrast.xaml`
replaces the token brushes with `SystemColors` brushes, and the app follows
changes while running (`SystemParameters.StaticPropertyChanged`). Brushes
are therefore referenced with `DynamicResource`. Sizes, spacing and
styles that are not brushes use `StaticResource`.

### 3.7 Window chrome

Every window keeps the native title bar so snapping, resizing and the system
menu work as usual. `WindowTheme.Apply(Window)` sets
`DWMWA_USE_IMMERSIVE_DARK_MODE` (Windows 10 20H1 and later) and, on
Windows 11, `DWMWA_CAPTION_COLOR` to `Chrome`. If either call fails the
window keeps the light title bar; nothing else depends on it.

## 4. Components

Implicit styles in `Theme/Controls.xaml` restyle the stock controls, so
existing markup picks up the theme without changes:

- **Button** (Secondary by default) and the named variants `PrimaryButton`,
  `DangerButton`, `DangerFilledButton`, `GhostButton`, `HeroButton`
  (icon tile, title and one line of description) and `HeaderButton`.
  Disabled is 40% opacity. Keyboard focus shows a 2 px `PrimaryText` ring
  offset 2 px; mouse clicks do not show it.
- **Segmented control:** RadioButtons with the `SegmentedItem` style inside
  a Border with the `Segmented` style, for stream quality.
- **CheckBox** as a toggle switch (`ToggleSwitch` style) for on/off settings,
  and as a plain check box elsewhere.
- **ComboBox**, **TextBox** (with a placeholder attached property),
  **Slider**, **ScrollBar** (thin, appears on hover), **Expander**
  (chevron, used for stats sections), **ToolTip**, **ContextMenu/MenuItem**
  (the Help menu).

Small reusable controls in `Ui/`:

| Control | What it shows |
|---|---|
| `AppHeader` | Logo mark and "CouchLink", Controls button, Help menu (Crash reports, About CouchLink). Raises `ControlsClicked`, `CrashReportsClicked`, `AboutClicked`. |
| `PlayerChip` | Rounded square in the player's color with "P3". Sizes: small (row), large (session). |
| `KeyCap` | A key name in a key-shaped box; a dashed "No key" variant. |
| `StatusPill` | Live, Reconnecting or neutral text in a pill. |
| `Banner` | Info, warning or error message with an icon; optional close button. |
| `StepTracker` | Connect → Host lets you in → Play, with done, current and upcoming steps. |
| `Card` | Border with Card fill, radius 10 and an optional overline heading. |
| `ThemedDialog` | Modal window with a title, a message and one or two buttons. `ThemedDialog.Alert(owner, title, message)` and `ThemedDialog.Confirm(owner, title, message, confirmText, cancelText, danger)` return a bool. Replaces every `MessageBox.Show` in CouchLink.App. |

Icons are `Geometry` resources in `Theme/Icons.xaml` drawn with `Path`:
monitor (host), arrow (join), gamepad (controls), help, alert, check,
chevron, close, search, refresh, user. No emoji and no icon font. Every
icon-only button has `AutomationProperties.Name`.

## 5. Screens

The main window is 540×660 by default (minimum 460×560). Every main-window
screen shows `AppHeader` at the top; the content below changes.

### 5.1 Start

- Heading "Ready to play?" (Display) and "Couch co-op across the PCs on this
  network." (muted).
- Two hero buttons: **Host a game**, "Run the game here. Each friend gets
  their own controller." (Primary fill); **Join a game**, "Play a game
  running on another PC." (Card fill).
- Footer: "This PC: PORTAL-SERVER" on the left, the version on the right.
- The old Controls and Crash reports buttons move into the header.

### 5.2 Host lobby

- Title "Hosting on PORTAL-SERVER" with a Live pill; under it "Minimize this
  and start the game. Friends pick this PC under Join."
- **Stream card:** resolution and frame rate combo boxes side by side, and
  quality as a segmented control (Low, Balanced, High, Max). Changing any of
  them calls `HostService.ChangeSettings` as today.
- The link-speed warning from `LinkWarning()` shows as a warning banner under
  the Stream card, only while there is one.
- **Players card:** overline "Players 3 / 10". Row 1 is always "You (host)"
  with the P1 chip. Each joined player: chip, PC name, a Reconnecting pill
  when `PlayerState.Reserved`, and a ghost **Kick** button. With no one
  joined: "No one has joined yet. Players appear here when they join."
  At the bottom of the card, the **Let everyone in** toggle with "No popup
  when someone joins" under it (was the "Allow everyone" check box).
- **Stream stats** expander, collapsed, with the current Details text in
  Mono (virtual pads, stream description, last error).
- Footer: **Stop hosting** (DangerButton, full width). With one or more
  players connected or reconnecting it first shows
  `ThemedDialog.Confirm("Stop hosting?", message, "Stop hosting", "Keep
  hosting", danger: true)`. The message names up to three players: "PC-07
  will be disconnected.", "PC-07 and PC-11 will be disconnected.", "PC-07,
  PC-11 and PC-12 will be disconnected."; with four or more it counts them:
  "4 players will be disconnected." With no players it stops at once.
- A host start error shows in `ThemedDialog.Alert` instead of `MessageBox`.

### 5.3 Join list

- Back button (ghost, arrow icon) and title "Join a game".
- The message from the last session (why it ended) shows as an error banner
  with a close button. Validation messages for the address show under the
  address field instead (below).
- A scanning row with a spinner: "Looking for hosts on this network…".
- **Host cards**, one per host, the whole card clickable: monitor icon, host
  name, one small dot per player and "3 / 10 players". Discovery only sends
  a player count, not slots, so the dots take the colors P1, P2, P3… in
  order; they show how full the game is, not who is in it. A host on another version is dimmed, not clickable, with a
  neutral pill "Different CouchLink version".
- If no host has been seen 10 s after the list opens, a help card appears
  under the scanning row: "No hosts yet. Check that the host clicked Host a
  game, that both PCs are on the same network, and that the firewall allows
  CouchLink. You can also join by address below." It hides as soon as a host
  appears.
- **Join by address card**, always visible at the bottom: text box with
  placeholder "192.168.1.23", a Join button, and the validation error
  "Enter an IP address like 192.168.1.23." under the field in Danger. Enter
  in the box joins. If discovery can't start, the scanning row is replaced by
  a warning banner with the error and "Join by address still works."

### 5.4 Session

Shown while connecting, waiting, playing (behind the fullscreen player) and
reconnecting.

- `StepTracker` at the top: Connect → Host lets you in → Play.
- **Connecting:** step 1 current; "Connecting to PORTAL-SERVER…" with a
  spinner.
- **Waiting:** step 2 current; "Waiting for PORTAL-SERVER to let you in" and
  "The host sees a popup and can allow or deny."
- **Playing:** step 3 current; a large player chip, "You're P3" and "Playing
  on PORTAL-SERVER", then a card of shortcuts with key caps: Leave the game
  (Ctrl Alt Q), Show the keys (F1), Change keys (Ctrl Alt C), Stream stats
  (F2).
- **Reconnecting:** step 3 with a Reconnecting pill; "Reconnecting to
  PORTAL-SERVER…" and "Your slot is kept for a minute."
- **Connection details** expander (collapsed) with the existing details text
  in Mono.
- Footer: **Controls** (Secondary) and **Cancel** / **Leave** (Danger;
  Cancel while connecting or waiting, Leave otherwise).
- All buttons and the expander on this screen stay `Focusable="False"`, and
  `AppHeader` buttons too while this screen is shown, because Raw Input keys
  still reach a focused control.

### 5.5 Controls editor

Same window, same features, same `AppServices.Controls` edits applied live.
Width 460, height sized to content up to 85% of the work area, then the
list scrolls.

- **Profile bar:** profile combo box, Browse…, Save as…. "(changed)" stays on
  the selected profile's name after an edit, as today.
- **Find box** with a search icon and placeholder "Find a control or label".
  It filters rows by control name, group name or label, ignoring case;
  groups with no match hide. Esc in the box clears it.
- **Labels toggle** (was "Show labels" check box) shows a label text box at
  the end of each row.
- **Groups as cards** in `KeyNames.Groups` order, the group name as overline.
  Rows inside a stick or D-pad group show only the direction (Up, Down, Left,
  Right). Each row: control name, a `KeyCap` per key (or the dashed "No
  key"), and the label box when Labels is on. The row is a button; clicking
  it starts listening.
- **Listening:** the row fills with `ListeningFill`, gets a `PrimaryText`
  outline, and reads "Press a key or mouse button · Esc cancels".
  A reserved key keeps listening and shows "<key> is reserved. Press another
  key." in the banner as today.
- **Moved key:** when `BindResult.MovedFrom` is set, the row that lost the
  key gets a warning tint for 4 s, the list scrolls it into view if it is
  off screen, and a warning banner says "Num 5 moved here from Circle.
  Circle has no key now."
- **Mouse card:** overline "Mouse (right stick)", the sensitivity slider with
  its value shown at the end, and the Invert Y toggle with "mouse toward you
  pushes the stick up".
- **Footer:** **Reset to default** (ghost) on the left, **Done** (primary,
  closes the window) on the right.
- Opening over the game with Ctrl+Alt+C (`overGame: true`, topmost) works as
  today.

### 5.6 Approval toast

`ApprovalPopup` keeps its behavior: topmost, `ShowActivated = false`,
bottom-right of the work area, stacks upward, flashes the taskbar button,
closing it denies, `CloseByHost` closes without denying.

- Borderless window (`WindowStyle.None`, no taskbar entry), Card fill,
  Border outline, radius 10, a soft shadow.
- User icon tile, "PC-11 wants to join", "Denied automatically in 22 s".
- A thin countdown bar that drains over `HostSession.AskTimeout`.
- **Deny** (Secondary) and **Allow** (Primary). A small close button in the
  corner denies, as the window X does today.
- The host does not know the joining player's slot when the toast appears,
  so it shows no player color.

### 5.7 Dialogs

- **ThemedDialog** (section 4) for every alert and confirm.
- **Save profile:** Name field with "Shown in the profile list" under it,
  Game field marked optional, Cancel and Save (Save disabled while Name is
  empty). Same `ProfileName` and `Game` results.
- **Crash dialog:** same heading, report path box, the four buttons (Open
  folder, Copy path, Report on GitHub, Close) and their automation IDs
  unchanged. It must still show if the theme failed to load, so it uses only
  implicit styles and `DynamicResource`, which fall back to stock WPF
  instead of throwing.
- **About CouchLink** (new, from the Help menu): name, version, "Licensed
  under the GNU GPL v3", and Close. No network access.

## 6. Code layout

All changes are in `src/CouchLink.App` plus one new test project.

```
src/CouchLink.App/
  App.xaml                 merges the Theme dictionaries
  Theme/
    Tokens.xaml            colors, brushes, sizes, type
    HighContrast.xaml      brush overrides from SystemColors
    Controls.xaml          implicit and named styles, templates
    Icons.xaml             Geometry resources
    ThemeManager.cs        loads the dictionaries, follows High Contrast
    WindowTheme.cs         dark title bar through DWM
  Ui/
    AppHeader.xaml(.cs)  PlayerChip  KeyCap  StatusPill  Banner
    StepTracker  ThemedDialog.xaml(.cs)  AboutDialog.xaml(.cs)
    PlayerColors.cs
  Views/                   Start, HostLobby, JoinList, Session (rewritten XAML)
  ControlsWindow.xaml(.cs) was built in C#
  ApprovalPopup.xaml(.cs)  was built in C#
  SaveProfileDialog.xaml(.cs)
  Diagnostics/CrashDialog.xaml(.cs)
```

- Views keep their public events and methods (`StartView.HostClicked`,
  `JoinListView.JoinRequested`, `SessionView.Show`, `HostLobbyView.TryStart`
  and so on), so `MainWindow` changes only to host `AppHeader` and to use
  `ThemedDialog`.
- Logic that decides what to show moves into plain classes the tests can
  reach without a window. They live in CouchLink.App and are `internal`, with
  `InternalsVisibleTo` for the test project:
  - `PlayerColors.For(slot)`
  - `SessionText.For(ClientState, hostName, slot)`: heading, hint, step,
    button text
  - `StopHostingPrompt.For(players)`: null when no prompt is needed, else the
    message
  - `ControlFilter.Matches(control, group, label, query)`
  - `AskCountdown.For(elapsed, timeout)`: fraction and seconds-left text
  - `AddressInput.TryParse(text, out address, out error)`

## 7. Testing

- New `tests/CouchLink.App.Tests` (xunit 2.9.3, net10.0-windows, references
  CouchLink.App) covering the six classes in section 6: every
  `ClientState`, slots 0, 1, 10 and 11, the one/two/three/four-player Stop
  messages, filter matches on name, group and label and a non-match,
  countdown at 0, half and past the timeout, and good and bad addresses
  (including IPv6, which is rejected as today).
- All existing tests keep passing.
- Every interactive control gets a stable `AutomationProperties.AutomationId`
  and a `Name`. A screenshot script (in `eng/`) launches the built app,
  drives each screen through UI Automation and saves PNGs, which replace the
  ones in `docs/images/`.
- Manual check on Windows 10 and 11: dark title bar, High Contrast on and off,
  animations off, keyboard-only navigation through Start, Join and the
  Controls editor, Ctrl+Alt+C over the game, two approval toasts stacked,
  and keys typed in the game never pressing a button on the Session screen.

## 8. Docs

- Replace the screenshots in `docs/images/` and the README.
- Update `docs/cafe-setup-guide.md` and the README where they name buttons
  that changed ("Allow everyone" → "Let everyone in", "Join by address..."
  is now a card, Controls and Crash reports are in the header).
- `docs/gate-results.md` gets a UI revamp section for the manual checks in
  section 7.
