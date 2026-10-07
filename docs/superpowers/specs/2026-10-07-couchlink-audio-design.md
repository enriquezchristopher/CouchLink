# CouchLink Audio (v1.3 milestone: #19, #20)

Status: Approved; revised 2026-10-07 after the Plan 6 spike (see "Revisions")
Date: 2026-10-07
Author: Christopher Enriquez (@enriquezchristopher) (with Claude Code)

Expands section 5.3 of the [main design](2026-10-05-couchlink-design.md).

## 1. Goal

Clients hear everything the host PC plays, with about the same delay as the
picture, and no audible gaps from ordinary packet loss on a wired LAN.

### Agreed requirements
From the main spec:
- WASAPI loopback capture on the host (NAudio).
- Opus, 48 kHz stereo, 5 ms frames (Concentus, pure C#).
- Sent over UDP 47802, the video port, to every client.
- Client jitter buffer of about 15 ms; audio plays as soon as it arrives, no
  A/V sync.

Settled in brainstorming (2026-10-07):
- **Loss recovery:** each packet also carries the previous frame
  (redundancy); longer gaps use Opus packet loss concealment (PLC). Opus's
  own in-band FEC is not used: it needs frames of 10 ms or more.
- **Structure:** a separate audio pipeline beside video, sharing only the
  client's receive socket (approach A).
- **Clients** are the ones video already streams to (learned from input
  packets, `StreamTargets`); no new join logic before v1.4.
- **Independent of video:** either can fail without stopping the other.
- **Same-PC client is muted:** a client whose host is this same PC does not
  play audio, because the host's capture would pick up that playback and
  send it round again (an echo loop). It still receives and counts packets,
  and says "muted: host is this PC". Host and client are separate processes,
  so leaving the host's own process out of the capture cannot prevent this.
- **Client output** is the Windows default device, following it when it
  changes.
- **No volume or mute controls** in CouchLink; the Windows volume mixer
  covers that.

### Success criteria
- A client hears the host with about 30 ms from host speaker to client
  headphones.
- One lost packet in a row is repaired exactly; short bursts are concealed
  without a long gap.
- An hour-long session keeps the buffer near its starting depth (clock drift
  handled).

## 2. Components

### 2.1 `CouchLink.Core` (pure logic, tested without sound hardware)

| Type | Job |
|---|---|
| `Protocol/AudioPacket` | Wire type 6. Stream ID, sequence number, the current Opus frame and, optionally, the previous frame. |
| `Audio/AudioFormat` | The constants: 48 kHz, stereo, 240 samples (5 ms) per frame, 128 kbps. |
| `Audio/FrameSlicer` | Cuts captured PCM, in buffers of any size, into exact 240-sample frames; a discontinuity drops the partial frame and marks the next one. |
| `Audio/TestToneSource` | `--test-tone`: a 440 Hz beep, 100 ms on, 900 ms off, in real time. |
| `Audio/JitterBuffer` | Orders frames by sequence, waits for 3 frames (15 ms) before playing, picks each frame to play (real, redundant copy, conceal, or silence), drops late packets, counts everything. |
| `Audio/DriftControl` | Averages buffer depth over 1 s and says whether to drop or repeat one sample in the next frame. |
| `Audio/AudioStreamer` | Host: reads 5 ms frames from an `IAudioSource`, encodes through an `IAudioEncoder`, packetizes with the previous frame attached, sends to current clients. Own thread, own `StreamTargets`, learns clients through `ClientSeen`. |
| `Audio/AudioClient` | Client: takes audio datagrams (`Receive`), feeds the jitter buffer, and fills PCM when the output device asks (`Read`), decoding through an `IAudioDecoder`. |
| `Net/StreamDispatcher` | Owns the one UDP socket on 47802 and routes each datagram by its packet type (byte 3) to the video or audio handler. Unknown types are ignored. |
| `Net/LocalAddress` | Whether an address is this PC (loopback or one of its own addresses), for the same-PC mute. |

`VideoClient` gains a constructor without a receiver and a public `Receive`
that the dispatcher calls. Its behaviour does not change, and the existing
constructor that owns a `VideoReceiver` stays.

### 2.2 `CouchLink.Audio` (new project: NAudio 3.1, Concentus 2.2)

| Type | Job |
|---|---|
| `LoopbackSource` | WASAPI capture as 48 kHz 16-bit stereo (Windows resamples and downmixes). On Windows 10 2004 and later: process loopback, excluding CouchLink's own process tree. Before that: loopback of the default output device. Feeds a `FrameSlicer`; reopens if capture stops. |
| `OpusAudioEncoder` / `OpusAudioDecoder` | Thin Concentus wrappers: 48 kHz stereo, restricted low-delay (CELT) mode, 128 kbps, 5 ms frames; the decoder also does PLC. |
| `DefaultDevicePlayer` | Shared-mode, event-driven output on the default device, in low-latency mode (IAudioClient3) where the driver supports it. Pulls PCM from `AudioClient.Read`. Reopens when Windows' default device changes or the device stops. |

### 2.3 `CouchLink.App`

`HostAudio` and `ClientAudioService` mirror `HostVideo` and
`ClientVideoService`. Each starts on its own; a failure is logged and shown
in the status text, and the other pipeline keeps running. The host feeds
`ClientSeen` to both video and audio streamers from the same input packets.
`ClientStreams` owns the client's socket, the dispatcher, and the video and
audio services.

## 3. Wire format

Type 6, little-endian, after the 4-byte `Wire` header:

| Field | Size | Notes |
|---|---|---|
| Stream ID | 2 | Random per host start, never 0; a change resets the client. |
| Sequence | 4 | Frame number, +1 per 5 ms frame; jumps by 16 after a capture discontinuity. |
| Frame length | 2 | Bytes of Opus data for this frame (1-400). |
| Frame | n | Opus data for frame `Sequence`. |
| Previous length | 2 | 0 if there is no previous frame (first packet after a discontinuity). |
| Previous frame | m | Opus data for frame `Sequence - 1`. |

At 128 kbps a frame is about 81 bytes (measured: 69-108), so a packet is
about 180 bytes: 200 packets/s, about 290 kbps per client including UDP/IP
headers.

## 4. Data flow and timing

### 4.1 Host
1. WASAPI delivers 10 ms buffers; the `FrameSlicer` turns them into 5 ms
   frames of 240 stereo samples.
2. `AudioStreamer` encodes each frame (about 0.3 ms), attaches the previous
   frame's bytes, and sends one packet to every current client.
3. **Silence:** process loopback keeps delivering buffers (of zeros) when
   nothing plays, so the stream is continuous. Device loopback delivers
   nothing while the PC is silent and marks the first buffer after with
   WASAPI's data-discontinuity flag.
4. **Discontinuity:** the first frame after a capture (re)start, a buffer
   flagged as a discontinuity, or (device loopback) a jump in the device
   position. The streamer then sends that frame without a previous frame
   and jumps the sequence by 16, so clients start over cleanly instead of
   treating the new audio as late.
5. While no client is listening, nothing is encoded or sent.

### 4.2 Client
1. `StreamDispatcher` passes each type-6 datagram to `AudioClient`, which
   puts it in the `JitterBuffer`.
2. **Priming:** playback starts once 3 frames are queued.
3. Each time the device needs audio, the client takes the next sequence:
   - present: decode and play it;
   - missing, but the following packet has arrived: decode its redundant
     copy (exact);
   - both missing: Opus PLC, for at most 4 frames (20 ms) in a row;
   - after that: silence, and prime again.
4. A packet for a frame already played or concealed is dropped and counted
   as late.

### 4.3 Clock drift
Host and client sound cards run at slightly different rates (about 0.01%,
roughly 4 ms an hour). `DriftControl` averages the buffer depth over each
second. The first second's average after playback starts is the baseline:
it already includes how the device pulls audio (in 2-10 ms chunks), so it is
the right target rather than a fixed 15 ms. When a later second averages
more than 5 ms above the baseline, one sample per frame is dropped until the
average is back at or below it; more than 5 ms below, one sample is
repeated. That is a 0.4% speed change, not audible. Above 60 ms queued (for
example after the client stalled), the buffer discards frames down to 15 ms
at once.

### 4.4 Latency budget
About 10 ms capture period + 0.3 ms encode + under 1 ms LAN + 15 ms jitter
buffer + 2-3 ms device buffer (10 ms without low-latency support):
**about 30 ms**, in line with video, so no sync logic is needed.

## 5. Error handling

| Case | Behaviour |
|---|---|
| Process loopback unavailable (Windows before 2004, or it fails to open) | Fall back to device loopback; log it; status says "device loopback". |
| Host has no audio device / WASAPI won't open | Status "Audio: unavailable: <reason>"; video and pads carry on. |
| Host capture stops (device unplugged) | `LoopbackSource` reopens, retrying every 2 s; clients see a gap and prime again. Process loopback is not tied to one device; device loopback opens the current default when it reopens. |
| Client has no output device | Status says "no output device"; the client still receives and counts packets, and retries the device every 1 s. |
| Client default device changes, or the device stops | `DefaultDevicePlayer` reopens on the current default; the jitter buffer is kept. |
| Client's host is this PC | Not played; status "muted: host is this PC"; packets still counted. |
| Corrupt packet / Opus decode error | Treated as a lost frame (PLC) and counted; no exception escapes. |
| New stream ID | Jitter buffer, drift control and decoder reset. |
| Exception on the audio thread | Sent to the error callback and the log; the thread waits 100 ms and carries on (as `VideoStreamer` does). |

### Stats
- Host: clients, packets sent, capture mode (process loopback / device
  loopback / test tone).
- Client: packets received, buffer depth (ms), frames repaired from
  redundancy, frames concealed, late packets, drift corrections, and the
  output device (or why there is none).
- Shown as one "Audio" line in the F2 overlay and in the dev window status.

## 6. Dev switches

Added to `DevOptions`:
- `--test-tone`: the host sends a 440 Hz beep (100 ms on, 900 ms off)
  instead of loopback, so gaps and clicks are easy to hear without a game.
- `--audio-loss=<percent>`: the client drops that share of incoming audio
  packets at random, to hear redundancy and PLC at work.

## 7. Testing

**Core.Tests** (test-first, no hardware):
- `AudioPacket` round trip, with and without a previous frame; truncated and
  wrong-type packets rejected.
- `FrameSlicer`: uneven buffer sizes become exact frames with no samples
  lost or repeated; discontinuities and silent buffers.
- `JitterBuffer`: priming; reordering; one gap repaired from redundancy; a
  gap concealed; silence and re-priming after 4 concealed frames; late
  packets dropped and counted; reset on new stream ID; cut to 15 ms above
  60 ms.
- `DriftControl`: rising or falling depth gives drops or repeats; nothing
  inside the dead band.
- `StreamDispatcher`: routing by type; unknown types and junk ignored.
- `AudioStreamer` and `AudioClient` with fake source, encoder, decoder and
  sender.
- UDP loopback on 127.0.0.1: video and audio on one port both arrive.
- `LocalAddress`, the overlay's audio line, and the new dev switches.

**Audio.Tests** (new project; real codec, no devices):
- Concentus round trip of a 1 kHz sine: level and frequency survive.
- Packets fit in `AudioPacket`'s limit; PLC returns a full frame; a corrupt
  frame throws (so `AudioClient` conceals it).

**Manual gate** (results in `docs/gate-results.md`):
1. One PC, host and client (`--windowed-player`), YouTube playing on the
   host: the client says "muted: host is this PC", its packet count rises,
   and nothing echoes.
2. Two PCs, host with `--test-tone`, client with `--audio-loss=5`: no
   audible gaps; F2 repaired count rises.
3. Two PCs, 30 minutes of a game: buffer depth stays within 5 ms of where
   it started, no drift dropouts.
4. Unplugging or switching the client's headphones: audio back within
   about 1 s.

## 8. Out of scope
- Volume, mute or device choice inside CouchLink.
- A/V sync.
- Muting the host's own speakers.
- Surround sound (Windows downmixes to stereo).
- Microphone / voice chat.

## Revisions

2026-10-07, after the Plan 6 spike on the dev PC (Windows 10 22H2):
- Excluding CouchLink's own process does not stop a one-PC echo, because
  host and client are separate processes. A client whose host is this PC
  now mutes itself (decided with the project owner).
- Process loopback delivers continuously, including silence; device
  loopback stops while silent. Discontinuities are now detected from
  WASAPI's flag and device position, and the sequence jumps after one.
- Low-latency output measured at 2 ms on this PC (10 ms without it), so the
  budget is about 30 ms. NAudio cannot combine low-latency mode with
  automatic device routing, so `DefaultDevicePlayer` follows default-device
  changes itself. It is not called `WasapiPlayer`, which NAudio already uses.
- Drift control targets the measured starting depth instead of a fixed
  15 ms, because the device's pull pattern changes the average.
- The frame slicer is pure logic, so it lives and is tested in Core.
