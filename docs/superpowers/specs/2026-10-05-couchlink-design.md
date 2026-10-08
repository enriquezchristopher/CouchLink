# CouchLink: LAN Couch Co-op Streaming with Per-Client Virtual Gamepads

Status: Draft for review
Date: 2026-10-05
Author: Christopher Enriquez (@enriquezchristopher) (with Claude Code)

## 1. Overview & Goals

CouchLink lets customers on different PCs in the cafe play **couch co-op
games** together. One PC runs the game and hosts; other PCs join, see and
hear the host's screen, and each joining PC gets **its own virtual
DualShock 4 controller** on the host, driven by that client's keyboard and
mouse. The game sees separate controllers, so each client is a separate
player.

Target games: **NBA 2K14 and NBA 2K22** (up to ~10 players, 5v5).

Goals:
- Up to **10 players** in one game: the host player plus up to 9 clients
  (9 virtual pads, slots P2-P10; see section 4.5).
- Runs **entirely on the LAN**: no internet, no accounts, no cloud.
- **Low latency**: target ~20-35 ms host screen -> client screen on wired
  gigabit.
- **Simple UI** that works: few screens, big buttons.
- Supports **AMD and NVIDIA** host GPUs.

### Agreed requirements (from brainstorming)
- Reason for streaming: couch co-op games (several players on one PC),
  not weak client hardware.
- Clients have **keyboard + mouse only**; no physical gamepads.
- The **host player uses the game's own keyboard controls** (not a virtual
  pad).
- 5+ physical PlayStation controllers have been seen working in 2K on PC,
  so the virtual pad type is **DS4** (not limited to 4 like XInput).
- Clients **hear game audio**.
- **Customers start hosting themselves**; friends pick the host from a LAN
  list; the **host approves** each join.
- Key layout: **default + editable** per player; edits are **not
  persisted**; they last until CouchLink closes (that is fine).
- Mouse movement drives the **right stick**; mouse buttons are bindable.
- Hardware: wired **gigabit** switch, **Windows 10** on all PCs; every PC
  that may host or join has CouchLink and the ViGEmBus driver installed.
- Language: **C#**.
- **Crash reports:** on a crash the app writes a report file and tells
  the user exactly where it is, so they can attach it to a GitHub issue
  by hand (section 7.1).

### Explicitly out of scope
- Different players seeing different screens (each player gets the same
  host screen; no per-instance or split-screen hacks such as running
  multiple game copies).
- Internet / WAN play, Wi-Fi tuning, encryption of the stream.
- Physical gamepads on clients, rumble/lightbar feedback.
- Persisting custom key layouts (server share, profiles).
- Per-game key presets; staff/admin control panel; session billing.
- UDP multicast (possible later optimization, see section 4.6).
- Intel host GPUs (QuickSync); clients may have any GPU.

## 2. Environment

- **Host:** any cafe PC; current example has an **AMD RX 550**. NVIDIA
  cards must also work.
- **Clients:** cafe PCs, Windows 10, keyboard + mouse, any GPU.
- **Network:** wired gigabit switch.
- **Deployment:** CouchLink (with its FFmpeg DLLs), its firewall rules,
  and the ViGEmBus driver are installed on each PC.
- **Game setting:** 2K runs in **borderless windowed** mode (reliable
  capture and the approval popup can show over it).

## 3. Architecture

One app, **`CouchLink.exe`** (C#, .NET 10, WPF), with **Host** and **Join**
modes. Any PC can be host or client.

| Component | Runs on | Responsibility |
|---|---|---|
| Lobby UI | both | Start screen, host lobby, join list, approval popup, controls editor |
| Discovery | both | Host broadcasts presence; clients build the host list |
| Session control | both | TCP: join request, approve/deny, slot assignment, heartbeat, leave/kick, keyframe requests |
| Video sender | host | Capture -> GPU color convert -> hardware H.264 encode -> packetize + FEC -> UDP to every client |
| Audio sender | host | WASAPI loopback -> Opus -> UDP to every client |
| Pad manager | host | One virtual DS4 per approved client via ViGEmBus; applies input state |
| Player view | client | Receive, FEC-repair, hardware decode, present fullscreen; play audio |
| Input mapper | client | Raw Input keyboard/mouse -> layout -> DS4 state -> UDP to host |

Each component sits behind a small interface (e.g. `IVideoEncoder`,
`IPadManager`, `IInputMapper`) so it can be tested alone.

**Data flow:** host -> clients: video + audio (UDP). Client -> host:
controller state (UDP). Session control: TCP both ways.

**Ports** (firewall rules added by installer):

| Port | Use |
|---|---|
| UDP 47800 | Discovery broadcast (`HostAnnounce`, wire type 7); only clients bind it |
| TCP 47801 | Session control: join, approval, heartbeat, leave, kick (wire types 8-14) |
| UDP 47802 | Video + audio; host -> client timing replies |
| UDP 47803 | Input; client -> host keyframe requests and timing pings |

## 4. Session & UI

Details: [lobby & sessions design](2026-10-07-couchlink-lobby-sessions-design.md).

### 4.1 Screens
1. **Start:** two big buttons **Host** and **Join**, small ⚙ Controls.
2. **Host lobby:** "Hosting on PC-03", stream resolution and frame rate,
   **Allow everyone**, player list (P2..P10) with **Kick** per player,
   **Stop hosting**. Host minimizes it and plays.
3. **Join list:** hosts found on the LAN, e.g. "PC-03 · 4/10 players".
   Click to join.
4. **Playing:** fullscreen stream. **Ctrl+Alt+Q** leaves, **F1** shows the
   key layout, **F2** shows stats.

### 4.2 Discovery
The host broadcasts once per second on UDP 47800: PC name, player count,
capacity, protocol version. Clients drop a host from the list after 3 s
with no broadcast.

### 4.3 Join flow
1. Client clicks a host -> join request over TCP with its PC name.
2. Host shows topmost popup **"PC-07 wants to join. Allow / Deny"**;
   auto-deny after **30 s** with no answer. With **Allow everyone** ticked
   in the host lobby, joins are accepted with no popup.
3. **Allow:** host creates a virtual DS4, assigns the next free slot,
   forces a keyframe, starts sending media.
4. **Deny / full:** client sees "Request denied" or "Host is full" and
   returns to the list.

### 4.4 During the session
- Heartbeat every 1 s both ways. Host treats **5 s** silence as gone: the
  pad stays plugged in at neutral for the 60 s rejoin window below, then
  is unplugged (so the game keeps the player across a short drop).
- Client that loses the host shows "Reconnecting..." for **10 s**, then
  returns to the list.
- Leave or kick -> pad unplugged, slot freed.
- Stop hosting / host exit -> clients see "Host ended the session."
- **Rejoin:** same PC returning within **60 s** gets its old slot back
  with no approval popup (keeps the same 2K player).

### 4.5 Slot limit
At most **9 virtual pads** per host (slots P2-P10; the host player is P1 on
the keyboard). A join beyond that is answered "Host is
full".

### 4.6 Multicast (deferred)
Unicast copies to each client (~10 Mbps x up to 9 = ~90 Mbps on the
host's gigabit link). Multicast was rejected for now: unmanaged switches
flood it to every port, including PCs not in the game.

## 5. Video & Audio

Design informed by how Sunshine/Moonlight work, tuned for a wired LAN with
one shared stream.

### 5.1 Host video pipeline
- **Capture:** DXGI Desktop Duplication; frames stay in GPU memory.
- **Color conversion:** BGRA -> NV12 on the GPU.
- **Encode in-process** via FFmpeg libavcodec (FFmpeg.AutoGen), feeding
  D3D11 frames directly to the hardware encoder: **zero CPU copy**.
- **Encoder selection** at host start, by the display adapter's vendor:

  | | AMD | NVIDIA |
  |---|---|---|
  | Encoder | `h264_amf` | `h264_nvenc` |
  | Settings | usage `ultralowlatency` (`lowlatency` on older AMD encoders that refuse it, e.g. the RX 550), latency-oriented VBR, no pre-analysis | preset `p1`, tune `ull`, CBR, no B-frames |

  No hardware encoder (e.g. GT 710 / GT 1030) -> **software fallback**
  (x264 `ultrafast` + `zerolatency`) and a host lobby warning: "No
  hardware encoder - may lag with heavy games." If the vendor's hardware
  encoder fails to open (e.g. a driver problem), the host falls back to
  x264 with the same warning.
- **Format:** H.264, no B-frames. The host picks, before hosting:
  - **Resolution:** Native, **1080p** (default), 900p, 720p or 540p. The
    stream's height is at most the preset; the width follows the host
    screen's aspect ratio; never scaled up.
  - **Frame rate:** **60** (default), 75, 90, 120, 144, 165 or 240 fps,
    offering only rates up to the host display's refresh rate.
  - **Bitrate** follows automatically: ~10 Mbps at 1080p60, scaled by
    pixels and frame rate, 2-30 Mbps.
- **Still screen:** the last image is re-encoded at the frame rate (tiny
  delta frames), so clients always receive newer frames and notice a loss.
- **Capture lost** (UAC prompt, mode change, exclusive fullscreen): the
  last image keeps going out marked *paused*; a keyframe follows when
  capture is back.
- **Keyframes only on demand:** on a client join and on unrecoverable
  loss. No periodic keyframes (they cause latency spikes).
- **Encode once, send to all:** the same packets go to every client.

### 5.2 Packetization & loss recovery
- Each frame is split into ~1200-byte UDP payloads. Header: frame number,
  packet index, packet count, keyframe flag, FEC block info.
- **Reed-Solomon FEC per frame**, default **20%** parity (configurable
  10-20%): lost packets are rebuilt on the client with no round trip.
- If FEC can't repair a frame, the client drops it and requests a
  keyframe over TCP (backup path).

### 5.3 Audio
- WASAPI **loopback** capture on the host (NAudio): everything the host
  plays is streamed (acceptable side effect).
- **Opus, 48 kHz stereo, 5 ms frames** (Concentus, pure C#).
- Client jitter buffer **~15 ms**.
- Details (loss recovery, clock drift, errors): [audio design](2026-10-07-couchlink-audio-design.md).

### 5.4 Client playback
- FFmpeg **D3D11VA** hardware decode; software decode fallback.
- Decoded frame stays on the GPU and is presented directly (D3D11 flip
  model, Vortice.Windows), **no vsync wait**.
- The picture keeps its shape: it is scaled to fit the client's screen and centred, with black bars.
  Streams tagged BT.709 are shown as BT.709; anything else as BT.601 (the host's x264 path).
- A decode error (or hardware decode not starting) switches to software decoding and waits for the
  next keyframe; if the client falls more than 6 frames behind it skips to the newest keyframe.
- Before the first picture the window says "Waiting for the host's picture..."; while the host's
  capture is lost it shows the last picture with "Host screen paused"; after 2 s with no frames at
  all it says "No picture from the host". The waiting and no-picture messages add "Ctrl+Alt+Q to
  leave", since the fullscreen window has no visible controls.
- The client needs a GPU with D3D11 video support (any GPU from the last decade; not the Microsoft
  Basic Display Adapter). If the GPU is reset mid-session (driver update or crash), the player
  closes and the player rejoins; recovering in place is not planned.
- Video and audio each play as soon as they arrive; no A/V sync logic.
- **No encryption** (own LAN).

**Latency target:** ~20-35 ms host screen -> client screen.

## 6. Input Mapping

### 6.1 Default layout (editable per player)

| DS4 control | Default key |
|---|---|
| Left stick | W A S D |
| Right stick | Mouse movement |
| D-pad | Arrow keys |
| Cross | K |
| Square | J, Left click |
| Circle | L |
| Triangle | I |
| L1 / R1 | Q / E |
| L2 / R2 | Ctrl / Shift |
| L3 / R3 | F / Middle click |
| Options / Share | Enter / Backspace |
| Touchpad click | Tab |

### 6.2 Keys -> stick
Pressed directions combine; diagonals are normalized to unit length;
opposite keys cancel.

### 6.3 Mouse -> right stick
Mouse delta deflects the stick, scaled by a **sensitivity** slider. An
**Invert Y** checkbox flips the vertical. After the mouse stops, the stick returns to center within **~50 ms**
(supports 2K shot-stick flicks). Pointer is hidden and clipped to the
window while playing.

### 6.4 Capture rules
Raw Input, only while the CouchLink window has focus. While the fullscreen
player is in front, a low-level keyboard hook blocks **both Windows keys,
Alt+Tab, Alt+Esc and Ctrl+Esc**, and the pointer is kept inside the
player. **Ctrl+Alt+Q** always exits and Alt+F4 still leaves;
**Ctrl+Alt+C** opens the controls editor over the game. Nothing is
blocked with `--windowed-player`.

### 6.5 Transport
The client sends the **full DS4 state** (buttons bitmask, 4 stick axes,
2 triggers) on every change and at least every **8 ms**, as unreliable
UDP to port 47803. Each packet carries slot id and sequence number; the
host ignores packets older than the latest it has applied.

### 6.6 Host pads
One virtual DS4 per slot through **ViGEmBus** (Nefarius.ViGEm.Client).
If the driver is missing, Host mode refuses to start with "ViGEmBus
driver not installed".

### 6.7 Controls editor
⚙ Controls (Start screen, session screen, or Ctrl+Alt+C in the game) lists
every DS4 control with its keys: click a control, press a key or mouse
button to bind it. One key drives one control; binding a key takes it off
any other. Esc, F1, F2 and the Windows keys are reserved. **Reset to
default**, the mouse sensitivity slider (steps 1-10) and Invert Y. Edits
apply at once and are kept in memory only, reset when CouchLink closes, by
design. Details: [playing screen & controls
design](2026-10-08-couchlink-playing-screen-controls-design.md).

## 7. Error Handling & Diagnostics

| Situation | Behavior |
|---|---|
| No hardware encoder | Software encode + host lobby warning |
| ViGEmBus missing | Host mode refuses to start with a clear message |
| Capture lost (UAC prompt, resolution change, exclusive fullscreen) | Recreate capture, force keyframe; clients show "Host screen paused" |
| Client hardware decode fails | Switch to software decode |
| CouchLink launched again | The running copy comes forward; the new one exits (not with `--windowed-player`) |
| Client silent 5 s | Host unplugs its pad (section 4.4) |
| Host lost | Client "Reconnecting..." 10 s, then back to list |

**Diagnostics:** F2 overlay on the client: fps, bitrate, packet loss, FEC repairs, estimated latency.
The estimate is the host's capture-to-send time (sent in its timing replies) + half the measured
round trip (a timing ping every second) + the client's receive-to-present time. It leaves out the
GPU work still queued at Present and the display's own scan-out, so the real glass-to-glass time
is a few ms more. Log file in
`%LOCALAPPDATA%\CouchLink\Logs\couchlink.log` (rolling, last 5 files).

### 7.1 Crash reports

When CouchLink crashes, it writes a **crash report file** that the user
uploads manually to the project's GitHub issue tracker. The report is
the starting point for diagnosing the problem.

**When a report is written:** any unhandled exception on any thread
(UI dispatcher, background threads, unobserved tasks). The report is
written first, then the user is told, then the app exits.

**Where it is stored:** `%LOCALAPPDATA%\CouchLink\CrashReports\`, one
file per crash, named `couchlink-crash-YYYYMMDD-HHMMSS.txt` (local time).
Plain text, so it opens in Notepad and attaches to a GitHub issue as-is.
The folder keeps the newest 20 reports; older ones are deleted.

**Contents:**
- CouchLink version and build, date/time, uptime.
- Windows version, .NET version, CPU, RAM, GPU name and driver version.
- Mode at the time (Host / Client / Lobby), player slot, number of
  virtual pads, chosen encoder (AMF / NVENC / software), resolution and fps.
- ViGEmBus installed and version.
- The exception: type, message, full stack trace, all inner exceptions.
- The last 200 lines of the log.
- **No personal data:** no PC names, IP addresses, or Windows user
  names. These are replaced with placeholders such as `<host>` and `<ip>`,
  because the file is posted publicly.

**Telling the user:** after the report is written, a dialog shows:
- "CouchLink crashed. A report was saved to:" followed by the **full
  file path**.
- Buttons: **Open folder** (opens Explorer with the file selected),
  **Copy path**, **Report on GitHub** (opens
  `https://github.com/enriquezchristopher/CouchLink/issues/new` in the
  browser), **Close**.
- A short line: "Please attach this file to a new issue so we can fix it."

**If the dialog can't be shown** (e.g. the crash broke the UI thread):
the file is still written. On the next start, CouchLink shows the same
dialog for any report it hasn't shown yet ("CouchLink crashed last
time...").

**Manual access:** a **Crash reports** link in ⚙ Settings opens the
folder at any time, so users can find older reports.

**Never crash while reporting:** if writing the report fails (disk full,
permissions), the app falls back to `%TEMP%\CouchLink\CrashReports\`,
and if that fails too it still shows the dialog with the error text.

## 8. Install & Packaging

- Self-contained .NET 10 publish folder (no runtime install), FFmpeg 9
  DLLs (avcodec, avutil, swscale, swresample) bundled in `ffmpeg\` with
  FFmpeg's license.
- Installed on each PC together with: ViGEmBus driver, Windows Firewall
  rules for the ports in section 3.
- FFmpeg build: must include `h264_amf`, `h264_nvenc`, and `libx264`
  (GPL build; fine for in-house use, revisit before any redistribution).

## 9. Testing

1. **Gate - PadTest tool:** create 9 virtual DS4 pads on one PC; confirm
   **2K14 and 2K22 detect all 9, each controls a different player, and the
   host keyboard still controls its own player (10 total)**.
   If this fails, stop and revisit the design before building anything
   else.
2. **Unit tests:** key -> stick math and mouse spring-back; packetizer
   split/reassemble; FEC repair under simulated 1-20% loss; session state
   machine (join, approve, deny, timeout, rejoin, kick).
3. **Loopback test:** host and client on one PC, full pipeline.
4. **Shop test:** 2, 5, then 10 real clients on 2K22; record F2 stats;
   pass if latency stays <= ~35 ms and the host keeps frame rate.

## 10. Build Order

1. PadTest gate (section 9.1)
2. Input path, no video (client mapper -> UDP -> host pads)
3. Video pipeline (capture, encode, FEC, decode, present)
4. Audio
5. Lobby, discovery, approval popup, controls editor polish
