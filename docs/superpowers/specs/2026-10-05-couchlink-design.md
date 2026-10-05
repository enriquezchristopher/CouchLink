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
  persisted** across reboots (diskless clients reset; that is fine).
- Mouse movement drives the **right stick**; mouse buttons are bindable.
- Hardware: wired **gigabit** switch, **Windows 10** on all PCs, all PCs
  boot the **diskless image** (CouchLink + driver installed once).
- Language: **C#**.

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
- **Deployment:** all PCs boot the diskless image; CouchLink, its FFmpeg
  DLLs, firewall rules, and the ViGEmBus driver live in that image.
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
| UDP 47800 | Discovery broadcast |
| TCP 47801 | Session control |
| UDP 47802 | Video + audio |
| UDP 47803 | Input |

## 4. Session & UI

### 4.1 Screens
1. **Start:** two big buttons **Host** and **Join**, small ⚙ Controls.
2. **Host lobby:** "Hosting on PC-03", player list (P2..P10) with **Kick**
   per player, **Stop hosting**. Host minimizes it and plays.
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
   auto-deny after **30 s** with no answer.
3. **Allow:** host creates a virtual DS4, assigns the next free slot,
   forces a keyframe, starts sending media.
4. **Deny / full:** client sees "Request denied" or "Host is full" and
   returns to the list.

### 4.4 During the session
- Client heartbeat every 1 s. Host treats **5 s** silence as gone and
  unplugs that pad.
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
  | Settings | usage `ultralowlatency`, latency-oriented VBR, no pre-analysis | preset `p1`, tune `ull`, CBR, no B-frames |

  No hardware encoder (e.g. GT 710 / GT 1030) -> **software fallback**
  (x264 `ultrafast` + `zerolatency`) and a host lobby warning: "No
  hardware encoder - may lag with heavy games."
- **Format:** H.264, default **1080p, up to 60 fps**, ~10 Mbps; 720p
  option. No B-frames.
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

### 5.4 Client playback
- FFmpeg **D3D11VA** hardware decode; software decode fallback.
- Decoded frame stays on the GPU and is presented directly (D3D11 flip
  model, Vortice.Windows), **no vsync wait**.
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
Mouse delta deflects the stick, scaled by a **sensitivity** slider.
After the mouse stops, the stick returns to center within **~50 ms**
(supports 2K shot-stick flicks). Pointer is hidden and clipped to the
window while playing.

### 6.4 Capture rules
Raw Input, only while the CouchLink window has focus. **Windows key and
Alt+Tab are blocked** while playing (low-level keyboard hook);
**Ctrl+Alt+Q** always exits.

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
⚙ Controls lists every DS4 control with its key: click a control, press a
key to rebind. **Reset to default** and the mouse sensitivity slider.
Edits are kept locally and lost on reboot (diskless), by design.

## 7. Error Handling & Diagnostics

| Situation | Behavior |
|---|---|
| No hardware encoder | Software encode + host lobby warning |
| ViGEmBus missing | Host mode refuses to start with a clear message |
| Capture lost (UAC prompt, resolution change, exclusive fullscreen) | Recreate capture, force keyframe; clients show "Host screen paused" |
| Client hardware decode fails | Switch to software decode |
| Ports in use (CouchLink already running) | Focus the running instance |
| Client silent 5 s | Host unplugs its pad (section 4.4) |
| Host lost | Client "Reconnecting..." 10 s, then back to list |

**Diagnostics:** F2 overlay on the client: fps, bitrate, packet loss,
FEC repairs, estimated latency. Log file in `%TEMP%` (wiped on reboot;
acceptable).

## 8. Install & Packaging

- Self-contained .NET 10 publish folder (no runtime install), FFmpeg DLLs
  bundled.
- Installed once into the diskless image with: ViGEmBus driver, Windows
  Firewall rules for the ports in section 3.
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
