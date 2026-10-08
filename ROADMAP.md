# CouchLink Roadmap

**Goal:** let customers on different PCs in a café or LAN room play couch
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

## ✅ v1.3 — Audio (released in 1.4.0)

Clients hear the game.

- [#19](https://github.com/enriquezchristopher/CouchLink/issues/19) Capture host audio and encode with Opus
- [#20](https://github.com/enriquezchristopher/CouchLink/issues/20) Client playback with a ~15 ms jitter buffer

## ✅ v1.4 — Lobby & sessions (released in 1.5.0 and 1.6.0)

The real app replaces the temporary dev window.

- [#21](https://github.com/enriquezchristopher/CouchLink/issues/21) LAN discovery and the join list
- [#22](https://github.com/enriquezchristopher/CouchLink/issues/22) Join request, host approval popup and slot assignment
- [#23](https://github.com/enriquezchristopher/CouchLink/issues/23) Heartbeat, timeouts, rejoin and kick
- [#24](https://github.com/enriquezchristopher/CouchLink/issues/24) Playing screen: fullscreen, Ctrl+Alt+Q, F1 help, Win-key/Alt+Tab block, mouse lock
- [#25](https://github.com/enriquezchristopher/CouchLink/issues/25) Controls editor
- [#26](https://github.com/enriquezchristopher/CouchLink/issues/26) Single instance

## ✅ Performance on older GPUs (released in 1.6.1; #53 ships in 1.6.2)

Found while testing on an AMD RX 550 host running NBA 2K22.

- [#51](https://github.com/enriquezchristopher/CouchLink/pull/51) Older AMD GPUs encode on the GPU (AMF low-latency mode) instead of falling back to the CPU
- [#53](https://github.com/enriquezchristopher/CouchLink/pull/53) The host's capture and encoding get GPU priority over the game, so a busy game can't starve the stream

## v1.5 — Café-ready install (next)

Easy to deploy to every PC in a café.

- [#27](https://github.com/enriquezchristopher/CouchLink/issues/27) Installer: app, ViGEmBus driver and firewall rules
- [#28](https://github.com/enriquezchristopher/CouchLink/issues/28) Ship FFmpeg with correct GPL notices
- [#29](https://github.com/enriquezchristopher/CouchLink/issues/29) Setup guide for café owners (the [guide](docs/cafe-setup-guide.md) exists for the zip; the installer will update it)

## v1.6 — Controller profiles

Players load a key layout made for a game from a file, with action labels
such as "Shoot" on the F1 panel. Design:
[controller profiles](docs/superpowers/specs/2026-10-08-couchlink-controller-profiles-design.md).

- [#56](https://github.com/enriquezchristopher/CouchLink/issues/56) Controller profiles: load a game's key layout from a file

## Game compatibility

- [#30](https://github.com/enriquezchristopher/CouchLink/issues/30) Check whether NBA 2K14 works
- [#49](https://github.com/enriquezchristopher/CouchLink/issues/49) Host option: virtual controller type (DualShock 4 or Xbox 360), for games like NBA 2K14 that don't read a DualShock 4's sticks

## Later

- Full color (4:4:4) at the top Quality step, for sharp colored text and edges. Needs NVENC on the host and a decode check on each joining PC.
- [#50](https://github.com/enriquezchristopher/CouchLink/issues/50) Self-serve diagnostics: everything we ask a reporter for, from inside the app

## Not planned

These are out of scope by design, so please don't open requests for them:

- Internet / WAN play, Wi-Fi tuning, stream encryption
- Physical gamepads on client PCs; rumble or lightbar feedback
- Saving custom key layouts between sessions
- Different screens per player (split-screen hacks, multiple game copies)
- UDP multicast streaming
- Intel (QuickSync) host GPUs
