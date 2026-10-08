# CouchLink Lobby & Sessions (v1.4 milestone, part 1: #21, #22, #23)

Status: Approved; small corrections from planning (deny reason 4, the host never binds 47800, by-address version mismatch)
Date: 2026-10-07
Author: Christopher Enriquez (@enriquezchristopher) (with Claude Code)

Expands sections 4.1-4.5 of the [main design](2026-10-05-couchlink-design.md).
The rest of v1.4 (#24 playing screen, #25 controls editor, #26 single
instance) is Plan 8 and has its own spec.

## 1. Goal

Friends find the host in a list, ask to join, and play, with no IP addresses
or slot numbers typed by hand. The host decides who gets in, can remove
players, and a player who drops for a moment comes back as the same 2K
player. The real app replaces the dev window.

### Agreed requirements
From the main spec:
- Discovery broadcast once per second on UDP 47800 (PC name, players,
  capacity, protocol version); clients drop a host after 3 s of silence.
- Join request over TCP 47801 with the client's PC name; topmost host popup
  "PC-07 wants to join. Allow / Deny", auto-deny after 30 s.
- Allow assigns the next free slot (P2-P10, 9 pads at most); deny or full
  sends the client back to the list with a clear message.
- Heartbeat every 1 s; 5 s of silence means the client is gone.
- A client that loses the host shows "Reconnecting..." for 10 s, then
  returns to the list.
- Kick and leave free the slot; Stop hosting tells every client.
- The same PC rejoining within 60 s gets its old slot with no popup.

Settled in brainstorming (2026-10-07):
- **Split:** this plan covers #21-#23 and the new Start / Host lobby / Join
  list screens; #24-#26 are Plan 8.
- **Approval:** the popup by default, plus an **"Allow everyone"** checkbox
  on the host lobby that accepts joins without a popup.
- **Stream settings** (resolution, frame rate) are pickers on the host
  lobby. Clicking Host starts with the defaults; changing a picker restarts
  the video stream and clients recover with a fresh keyframe, without
  rejoining.
- **Approach A:** the session is the single source of truth for who is in.
  It plugs and unplugs pads, sets the video and audio targets, and binds
  each slot to its client's IP for input.
- **Reserved pads stay plugged in:** a client that goes silent keeps its pad
  plugged in (at neutral) for the 60 s rejoin window, so 2K keeps the
  player. This changes main spec 4.4, which unplugged at 5 s.

### Success criteria
- A client finds the host in the list and plays without typing anything.
- Deny, the 30 s timeout, auto-allow, kick and Stop hosting each end with a
  clear message on the client.
- Pulling a client's cable for 20 s, or killing and restarting the client,
  brings it back to the same slot and the same 2K player with no popup.
- Changing resolution mid-session: clients show the new picture within
  about 1 s.

## 2. Wire format

All additions are new packet types; `Wire.Version` stays 1. Little-endian,
after the 4-byte `Wire` header. Names are `Environment.MachineName`, UTF-8,
at most 63 bytes (longer names are cut at a character boundary).

### 2.1 Discovery (UDP 47800)

Type 7, `HostAnnounce`:

| Field | Size | Notes |
|---|---|---|
| Players | 1 | Active and reserved slots (0-9). |
| Capacity | 1 | 9. |
| Name length | 1 | 1-63. |
| Name | n | Host PC name. |

The listener reads the magic and type even when the version byte differs,
so a host on another version is still listed (greyed out, "needs the same
CouchLink version") instead of silently missing.

The host sends the announce as a **directed broadcast on every IPv4
interface that is up** (for example 192.168.1.255), not only to
255.255.255.255, which Windows sends out of one adapter only. Interfaces are
re-read every 10 s so a cable plugged in later is covered.

### 2.2 Session channel (TCP 47801)

Each message is framed as a `u16` length (bytes that follow, 4-4096),
then the 4-byte `Wire` header, then the body.

| Type | Message | Direction | Body |
|---|---|---|---|
| 8 | `JoinRequest` | client -> host | name length (1) + name |
| 9 | `Accepted` | host -> client | slot (1, 2-10) |
| 10 | `Denied` | host -> client | reason (1): 1 denied, 2 full, 3 timed out, 4 the host's pad could not be created |
| 11 | `Heartbeat` | both | empty |
| 12 | `Leave` | client -> host | empty |
| 13 | `Kicked` | host -> client | empty |
| 14 | `HostEnded` | host -> client | empty |

A frame with a bad length, wrong magic or version, or unknown type closes
that connection (logged). A client checks the version from the announce
before joining (the join list greys out other versions). Joining such a
host by address ends with "Lost the host." when the host closes the
connection: the client cannot tell a version mismatch from a drop.

UDP traffic is unchanged: input, keyframe requests and timing pings stay on
47803, video and audio on 47802. Timing pings must stay on UDP so the round
trip is measured on the media path.

## 3. Session behavior

### 3.1 Host (`HostSession`)

Each slot P2-P10 is **free**, **pending** (waiting on the popup),
**active**, or **reserved** (client gone, held for rejoin).

| Event | Result |
|---|---|
| `JoinRequest`, name matches a reserved slot | Slot becomes active again for this connection, no popup: bind the pad to the client's (possibly new) IP, add the targets, force a keyframe, send `Accepted(slot)`. |
| `JoinRequest`, name **and** IP match an active slot | The new connection takes over the slot (client restarted before the old one timed out); the old connection is closed. No popup. |
| `JoinRequest`, no free slot | `Denied(full)`. |
| `JoinRequest`, "Allow everyone" ticked | Accepted at once. |
| `JoinRequest`, otherwise | Pending; host is asked (popup). |
| Host clicks Allow | Accepted, unless full by then (`Denied(full)`). |
| Host clicks Deny | `Denied(denied)`. |
| 30 s with no answer | `Denied(timed out)`. |
| Pending client disconnects | Request dropped; its popup closes. |
| **Accept** | Lowest free slot; plug the pad bound to the client's IP; add it to the video and audio targets; force a keyframe; send `Accepted(slot)`. |
| `Leave` | Unplug pad, remove targets, free the slot. |
| Host clicks Kick | Send `Kicked`, close, unplug, remove targets, free the slot. No reservation. |
| 5 s without a heartbeat, or the connection drops | Slot reserved for 60 s: pad stays plugged at neutral, targets removed. |
| Reservation reaches 60 s | Unplug the pad, free the slot. |
| Stop hosting / app closes | Send `HostEnded` to everyone; unplug all pads. |

Players shown in the lobby and in the announce count active and reserved
slots. A reserved slot shows as "P4 PC-07 (reconnecting)" with its Kick
button, which frees it at once.

A reserved slot keyed by name accepts the rejoin from any IP (cafe PCs may
get a new DHCP lease); the new IP replaces the old binding. An active slot
is only taken over by the same name **and** IP, so a second PC with a
duplicated name cannot steal a live player.

Input packets keep the 0.5 s neutral release from Plan 1. Input for a slot
that is not plugged, or from an IP other than the slot's, is ignored.

### 3.2 Client (`ClientSession`)

States: **Connecting** -> **Waiting** (for approval) -> **Playing** ->
**Reconnecting** -> **Ended(reason)**.

- Connect failure: Ended "Couldn't reach PC-03."
- `Denied`: Ended "Request denied." / "Host is full." / "The host didn't
  answer." / "The host couldn't add a controller for you."
- Cancel while waiting: closes the connection, back to the list.
- `Accepted(slot)`: start video, audio and input for that slot. If video
  fails to start, send `Leave` and end with the error (audio failing never
  ends a join, as in Plan 6).
- 5 s without a host heartbeat, or the connection drops: Reconnecting. Retry
  the connection and `JoinRequest` once per second; the reserved slot brings
  it back. Video and input keep running meanwhile (input to a reserved slot
  is ignored until it is active again). After 10 s: Ended "Lost the host."
- `Kicked`: Ended "You were removed by the host."
- `HostEnded`: Ended "Host ended the session."
- Ctrl+Alt+Q: send `Leave`, back to the list.

## 4. Components

### 4.1 `CouchLink.Core`: pure logic, tested with a fake `TimeProvider`
- `Protocol/HostAnnounce`, `Protocol/SessionMessage` (+ framing reader and
  writer): encode, decode, reject malformed input.
- `Session/HostSession`: the 3.1 state machine. No sockets; inputs are
  method calls (`OnJoinRequest`, `OnHeartbeat`, `Allow`, `Deny`, `Kick`,
  `Tick`, ...), outputs are calls on an `IHostSessionEffects` interface
  (`PlugPad`, `UnplugPad`, `AddTarget`, `RemoveTarget`, `AskHost`,
  `CloseAsk`, `Send`, `Close`).
- `Session/ClientSession`: the 3.2 state machine, same style.
- `Session/HostList`: hosts by address; expires after 3 s.

### 4.2 `CouchLink.Core`: network edges, tested over loopback
- `Net/DiscoveryBroadcaster`, `Net/DiscoveryListener` (UDP 47800).
- `Net/SessionServer`: TCP listener on 47801; one read loop per connection
  feeding `HostSession` under one lock; a 250 ms timer calls `Tick`.
- `Net/SessionClient`: one connection, read loop and heartbeat timer.
- `Ports`: add `Discovery = 47800` and `Session = 47801`; update the
  comments that say "until v1.4".

### 4.3 Changes to existing code
- `PadManager`: no more creating pads on the first packet. New
  `Plug(slot, address)` and `Unplug(slot)`; `Handle(packet, from)` ignores
  slots that are not plugged or come from another address.
- `StreamTargets` is replaced by an explicit target set filled by the
  session. `VideoStreamer` and `AudioStreamer` get `AddTarget(slot, address)`
  and `RemoveTarget(slot)` in place of `ClientSeen`; `AddTarget` forces a
  keyframe on video.

### 4.4 `CouchLink.App`
- `MainWindow` becomes the real app: one window swapping four views.
  - **Start:** big **Host** and **Join** buttons; ⚙ Controls shown
    disabled until Plan 8; Crash reports link.
  - **Host lobby:** "Hosting on PC-03"; resolution and fps pickers;
    "Allow everyone" checkbox; player list P2-P10 with Kick; **Stop
    hosting**; a collapsed "Details" section with the dev window's stream
    and pad stats and last error.
  - **Join list:** hosts as "PC-03 · 4/10 players" (players + 1 for the
    host); click to join; a "Join by address..." link; a message bar for
    the Ended reasons.
  - **Waiting:** "Waiting for PC-03 to let you in..." with **Cancel**.
- `ApprovalPopup`: small topmost window, "PC-07 wants to join.
  Allow / Deny", 30 s countdown, flashes the taskbar. Several requests stack.
- `HostInputService` becomes `HostService`: owns the session server,
  broadcaster, pads, video and audio. A picker change restarts `HostVideo`,
  re-adds the current targets and forces a keyframe.
- `ClientSessionService`: runs `ClientSession`; on Accepted starts
  `ClientStreams` and input with the assigned slot. "Reconnecting..." shows
  in the player's status text, so a fullscreen player stays open.
- Dev switches (`--test-pattern`, `--test-tone`, `--windowed-player`,
  `--save-video`, `--audio-loss`) keep working.

## 5. Error handling

| Situation | Behavior |
|---|---|
| TCP 47801 or UDP 47803 busy when hosting starts | Hosting refuses with a clear message; nothing is left half-started. The host only sends on UDP 47800 and never binds it, so a client on the same PC can still list hosts. |
| UDP 47800 busy on a client | Join list: "Can't search for hosts (port 47800 in use)." Join by address still works. |
| Can't connect to the host | "Couldn't reach PC-03." Back to the list. |
| Video fails to start after Accepted | Client sends `Leave`, shows the error, back to the list. |
| Bad frame from a connection | Close that connection, log it; others unaffected. |
| Exceptions on socket, timer or read-loop threads | Caught and logged, shown as the lobby's last error; never end the host process. |
| Host crashes | Clients: Reconnecting for 10 s, then "Lost the host." |

The log records every join, accept, deny, timeout, leave, kick, reservation
and expiry with PC name, IP and slot.

## 6. Testing

### 6.1 Unit (CouchLink.Core.Tests)
- Round-trip every session message and `HostAnnounce`; reject truncated,
  oversized, wrong-magic and unknown-type input; announce from another
  version parses with its version reported.
- `HostSession`: full; auto-allow; Allow; Deny; 30 s timeout; Allow after
  the host filled up; lowest free slot; leave and kick free the slot; 5 s
  silence reserves (pad still plugged, targets removed); rejoin within 60 s
  gets the same slot without asking; expiry at 60 s unplugs; takeover by
  same name and IP; same name from another IP while active goes to the
  popup; pending client disconnect closes the ask; Stop hosting sends
  `HostEnded` to all.
- `ClientSession`: each Denied reason maps to its message; reconnect within
  10 s returns to Playing; gives up at 10 s; Kicked and HostEnded.
- `HostList`: expiry at 3 s; update on new announce.
- `PadManager`: input for an unplugged slot or wrong address is ignored.

### 6.2 Loopback (real sockets on 127.0.0.1)
- `SessionServer` + `SessionClient`: join, heartbeat, dropped socket goes to
  reserved, rejoin, kick.
- Broadcaster + listener exchange an announce.

### 6.3 Manual gate (added to `docs/gate-results.md`)
- One PC, host + `--windowed-player` client: the host appears in the list,
  the popup appears, play works.
- Two PCs: deny, 30 s timeout, auto-allow, kick, Stop hosting.
- Pull the client's cable for 20 s: Reconnecting..., then same slot and same
  2K player.
- Kill and restart the client: same slot, no popup.
- Change resolution mid-session: clients recover within about 1 s.
- Three or more clients join at once: distinct slots.

## 7. Out of scope
- Plan 8: F1 help, Windows key / Alt+Tab block, mouse lock (#24); controls
  editor (#25); single instance (#26).
- Remembering hosts or "Allow everyone" between runs.
- Passwords or any authentication; the host's approval is the only gate,
  as in the main spec (no encryption, LAN only).
- Spectators, or more than 9 clients.

## 8. Changes to the main design
- 3 Ports: 47800 carries `HostAnnounce`; 47801 carries the session
  channel; 47803 no longer says "until the v1.4 session channel".
- 4.3: add the "Allow everyone" checkbox.
- 4.4: a silent client's pad stays plugged at neutral for the 60 s rejoin
  window instead of being unplugged at 5 s.
- 4.1: the host lobby holds the stream settings pickers.
