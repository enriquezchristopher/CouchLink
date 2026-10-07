# CouchLink Audio (v1.3 milestone: #19, #20)

Status: Draft for review
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
- **CouchLink's own process is left out of the capture**, so running host
  and client on one PC does not echo.
- **Client output** is the Windows default device, following it when it
  changes.
- **No volume or mute controls** in CouchLink; the Windows volume mixer
  covers that.

### Success criteria
- A client hears the host with about 35-40 ms from host speaker to client
  headphones.
- One lost packet in a row is repaired exactly; short bursts are concealed
  without a long gap.
- An hour-long session keeps the buffer near 15 ms (clock drift handled).

## 2. Components

### 2.1 `CouchLink.Core` (pure logic, tested without sound hardware)

| Type | Job |
|---|---|
| `Protocol/AudioPacket` | Wire type 6. Stream ID, sequence number, the current Opus frame and, optionally, the previous frame. |
| `Audio/JitterBuffer` | Orders frames by sequence, waits for 3 frames (15 ms) before playing, picks each frame to play (real, redundant copy, conceal, or silence), drops late packets, counts everything. |
| `Audio/DriftControl` | Averages buffer depth over about 1 s and says how many samples to drop or repeat in the next frame. |
| `Audio/AudioStreamer` | Host: reads 5 ms frames from an `IAudioSource`, encodes through an `IAudioEncoder`, packetizes with the previous frame attached, sends to current clients. Own thread, own `StreamTargets`, learns clients through `ClientSeen`. |
| `Audio/AudioClient` | Client: takes audio datagrams, feeds the jitter buffer, and supplies PCM when the `IAudioOutput` asks for it, decoding through an `IAudioDecoder`. |
| `Net/StreamDispatcher` | Owns the one UDP socket on 47802 and routes each datagram by its packet type (byte 3) to the video or audio handler. Unknown types are ignored. |

`VideoClient` changes to receive datagrams from the dispatcher instead of
owning `VideoReceiver`'s loop. Its behaviour does not change.

### 2.2 `CouchLink.Audio` (new project: NAudio, Concentus)

| Type | Job |
|---|---|
| `LoopbackSource` | WASAPI process loopback in **exclude** mode (CouchLink's process tree), asking Windows for 48 kHz stereo 16-bit so Windows resamples and downmixes. Slices the incoming buffers into exact 240-sample frames. Falls back to ordinary device loopback where process loopback is unavailable. |
| `OpusAudioEncoder` / `OpusAudioDecoder` | Thin Concentus wrappers: 48 kHz stereo, restricted low-delay (CELT) mode, 128 kbps, 5 ms frames; the decoder also does PLC and decodes redundant copies. |
| `WasapiPlayer` | Shared-mode, event-driven output with the smallest buffer the device allows. Pulls PCM from `AudioClient`. Follows the default output device. |

### 2.3 `CouchLink.App`

`HostAudio` and `ClientAudioService` mirror `HostVideo` and
`ClientVideoService`. Each starts on its own; a failure is logged and shown
in the status text, and the other pipeline keeps running. The host feeds
`ClientSeen` to both video and audio streamers from the same input packets.

## 3. Wire format

Type 6, little-endian, after the 4-byte `Wire` header:

| Field | Size | Notes |
|---|---|---|
| Stream ID | 2 | Random per host start, never 0; a change resets the client. |
| Sequence | 4 | Frame number, +1 per 5 ms frame. |
| Frame length | 2 | Bytes of Opus data for this frame (1-400). |
| Frame | n | Opus data for frame `Sequence`. |
| Previous length | 2 | 0 if there is no previous frame (first packet after a gap). |
| Previous frame | m | Opus data for frame `Sequence - 1`. |

At 128 kbps a frame is about 80 bytes, so a packet is about 180 bytes:
200 packets/s, about 290 kbps per client including UDP/IP headers.

## 4. Data flow and timing

### 4.1 Host
1. WASAPI delivers audio about every 10 ms; `LoopbackSource` emits 5 ms
   frames of 240 stereo samples.
2. `AudioStreamer` encodes each frame, attaches the previous frame's bytes,
   and sends one packet to every current client.
3. **Silence:** when nothing plays, WASAPI may deliver nothing; the host
   sends nothing. The previous frame is not attached to the first packet
   after such a gap (sequence numbers still advance by one per frame sent).

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
roughly 4 ms an hour). `DriftControl` keeps a 1 s average of buffer depth.
Outside 15 ms ± 5 ms, it drops (too full) or repeats (too empty) one sample
per frame until the average is back at 15 ms: a 0.4% speed change, not
audible. Above 60 ms (for example after the client stalled), the buffer
discards frames down to 15 ms at once.

### 4.4 Latency budget
About 10 ms capture period + under 1 ms encode + under 1 ms LAN + 15 ms
jitter buffer + about 10 ms device buffer: **about 35-40 ms**, in line with
video, so no sync logic is needed.

## 5. Error handling

| Case | Behaviour |
|---|---|
| Process loopback unavailable | Fall back to device loopback; log it; status says "device loopback (testing on one PC will echo)". |
| Host has no audio device / WASAPI won't open | Status "Audio unavailable: <reason>"; video and pads carry on. |
| Host default device changes or is unplugged | `LoopbackSource` reopens on the new default, retrying every 2 s; clients see a gap and prime again. |
| Client has no output device | Status "Audio unavailable"; the client still receives and counts packets. |
| Client default device changes | `WasapiPlayer` reopens on the new device; the jitter buffer is kept. |
| Corrupt packet / Opus decode error | Treated as a lost frame (redundant copy, then PLC) and counted; no exception escapes. |
| New stream ID | Jitter buffer and decoder reset. |
| Exception on the audio thread | Sent to the error callback and the log; the thread waits 100 ms and carries on (as `VideoStreamer` does). |

### Stats
- Host: clients, packets sent, capture mode (process / device loopback /
  test tone).
- Client: packets received, frames repaired from redundancy, frames
  concealed, late packets, buffer depth (ms), drift corrections.
- Shown as one "Audio:" line in the F2 overlay and in the dev window status.

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
- `JitterBuffer`: priming; reordering; one gap repaired from redundancy; two
  gaps concealed, then silence and re-priming after 4; late packets dropped
  and counted; reset on new stream ID; cut to target above 60 ms.
- `DriftControl`: slowly rising or falling depth gives drops or repeats;
  nothing inside the dead band.
- `StreamDispatcher`: routing by type; unknown types and junk ignored.
- `AudioStreamer` and `AudioClient` with fake source, encoder, decoder,
  sender and output on `FakeTimeProvider`.
- UDP loopback on 127.0.0.1: video and audio on one port both arrive.

**Audio.Tests** (new project; real codecs, no devices):
- Concentus round trip of a 1 kHz sine: level and frequency survive.
- PLC output has the right length and no NaNs.
- The frame slicer turns uneven buffer sizes into exact 240-sample frames
  with no samples lost or repeated.

**Manual gate** (results in `docs/gate-results.md`):
1. One PC, host and client (`--windowed-player`), YouTube playing on the
   host: the client hears it, no echo.
2. `--test-tone` with `--audio-loss=5`: no audible gaps; F2 repair count
   rises.
3. Two PCs, 30 minutes of a game: buffer depth stays within 15 ± 5 ms, no
   drift dropouts.
4. Unplugging or switching the client's headphones: audio back within
   about 1 s.

## 8. Out of scope
- Volume, mute or device choice inside CouchLink.
- A/V sync.
- Muting the host's own speakers.
- Surround sound (Windows downmixes to stereo).
- Microphone / voice chat.
