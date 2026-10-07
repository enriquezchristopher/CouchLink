# CouchLink Roadmap

**Goal:** let customers on different PCs in a cafe or LAN room play couch
co-op games together. One PC hosts the game; every other PC streams its
screen and sound and gets **its own virtual controller** on the host, so the
game sees separate players. Up to 10 players, entirely on the local network,
with no accounts or internet.

Full design: [docs/superpowers/specs/2026-10-05-couchlink-design.md](docs/superpowers/specs/2026-10-05-couchlink-design.md)

Each release below is a GitHub
[milestone](https://github.com/enriquezchristopher/CouchLink/milestones);
each item is an issue. Order is the planned build order; versions follow
the commit types in [CONTRIBUTING.md](CONTRIBUTING.md), so a milestone may
ship under a different number.

## ✅ v1.0 — Virtual pads & input (released)

Each client's keyboard and mouse drives its own virtual DualShock 4 on the
host over the LAN.

- One virtual DS4 per client (P2-P10) via ViGEmBus
- Keyboard + mouse -> DS4 mapping (mouse drives the right stick)
- Low-latency UDP input; stuck-key and crashed-client protection
- `PadTest check`: game-free test of 9 pads and all 32 DS4 actions

## ✅ v1.1 — Crash reports & reliability (released in 1.1.0 and 1.1.1)

When something breaks, users can tell us exactly what happened.

- [#8](https://github.com/enriquezchristopher/CouchLink/issues/8) Crash reports: save a report and tell the user where it is
- [#9](https://github.com/enriquezchristopher/CouchLink/issues/9) Crashed client's pad can take up to ~0.6 s to release
- [#10](https://github.com/enriquezchristopher/CouchLink/issues/10) Releasing one of two held Shift/Ctrl keys releases R2/L2
- [#11](https://github.com/enriquezchristopher/CouchLink/issues/11) App crashes if UDP port 47803 is already in use
- [#12](https://github.com/enriquezchristopher/CouchLink/issues/12) Virtual pad's native handle is not disposed
- [#13](https://github.com/enriquezchristopher/CouchLink/issues/13) Dev window: slot and IP boxes stay editable while playing

## ✅ v1.2 — Video streaming (released in 1.2.0 and 1.3.0)

Clients see the host's screen with ~20-35 ms latency.

- [#14](https://github.com/enriquezchristopher/CouchLink/issues/14) Capture the host screen on the GPU
- [#15](https://github.com/enriquezchristopher/CouchLink/issues/15) Hardware H.264 encoding (AMD AMF / NVIDIA NVENC, software fallback)
- [#16](https://github.com/enriquezchristopher/CouchLink/issues/16) Packetize with Reed-Solomon FEC and keyframe-on-demand
- [#17](https://github.com/enriquezchristopher/CouchLink/issues/17) Client hardware decode and low-latency display
- [#18](https://github.com/enriquezchristopher/CouchLink/issues/18) F2 stats overlay

## v1.3 — Audio (next)

Clients hear the game. Likely to ship as 1.4.0.

- [#19](https://github.com/enriquezchristopher/CouchLink/issues/19) Capture host audio and encode with Opus
- [#20](https://github.com/enriquezchristopher/CouchLink/issues/20) Client playback with a ~15 ms jitter buffer

## v1.4 — Lobby & sessions

The real app replaces the temporary dev window.

- [#21](https://github.com/enriquezchristopher/CouchLink/issues/21) LAN discovery and the join list
- [#22](https://github.com/enriquezchristopher/CouchLink/issues/22) Join request, host approval popup and slot assignment
- [#23](https://github.com/enriquezchristopher/CouchLink/issues/23) Heartbeat, timeouts, rejoin and kick
- [#24](https://github.com/enriquezchristopher/CouchLink/issues/24) Playing screen: fullscreen, Ctrl+Alt+Q, F1 help, Win-key/Alt+Tab block, mouse lock (fullscreen and Ctrl+Alt+Q shipped in 1.3.0)
- [#25](https://github.com/enriquezchristopher/CouchLink/issues/25) Controls editor
- [#26](https://github.com/enriquezchristopher/CouchLink/issues/26) Single instance

## v1.5 — Café-ready install

Easy to deploy to every PC in a cafe.

- [#27](https://github.com/enriquezchristopher/CouchLink/issues/27) Installer: app, ViGEmBus driver and firewall rules
- [#28](https://github.com/enriquezchristopher/CouchLink/issues/28) Ship FFmpeg with correct GPL notices
- [#29](https://github.com/enriquezchristopher/CouchLink/issues/29) Setup guide for cafe owners

## Game compatibility

- [#30](https://github.com/enriquezchristopher/CouchLink/issues/30) Check whether NBA 2K14 works

## Not planned

These are out of scope by design, so please don't open requests for them:

- Internet / WAN play, Wi-Fi tuning, stream encryption
- Physical gamepads on client PCs; rumble or lightbar feedback
- Saving custom key layouts between sessions
- Different screens per player (split-screen hacks, multiple game copies)
- UDP multicast streaming
- Intel (QuickSync) host GPUs
