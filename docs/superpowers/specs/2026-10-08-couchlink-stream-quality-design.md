# CouchLink Stream Quality

Status: Draft, for review
Date: 2026-10-08
Author: Christopher Enriquez (@enriquezchristopher) (with Claude Code)

Adds a Quality setting to the host's stream settings, next to Resolution and
Frame rate from the [main design](2026-10-05-couchlink-design.md) and Plan 4
(`docs/superpowers/plans/2026-10-06-couchlink-plan4-host-capture-encode.md`).

## 1. Goal

The picture on a client is softer than the host's screen, most visibly on
colored edges and text. The host should be able to trade image quality
against load, to suit the LAN, the host PC and its GPU.

### Why the client looks softer
Nothing in the pipeline is mislabeled: hardware streams are tagged BT.709,
x264 streams BT.601, and the client honors the tags (`StreamColor`). The
softness comes from what the stream throws away:

1. **4:2:0 chroma.** The host converts to NV12 before encoding, so color is
   kept once per 2x2 block. Colored edges and text look soft. This design
   does not change it; see section 8.
2. **Bitrate and encoder presets.** 10 Mbps at 1080p60 on the fastest
   low-latency presets (NVENC `p1`, AMF `ultralowlatency`, x264 `ultrafast`)
   spends few bits on detail, and H.264 compresses chroma harder than luma.
3. **Scaling twice.** A 1080p stream from a larger screen is scaled down on
   the host and up again on the client.
4. **Host video processor defaults.** `Nv12Converter` leaves the driver's
   automatic processing on; the client's `FramePresenter` turns it off.

This design addresses 2 and 4, and keeps 3 in the host's hands through the
existing Resolution setting.

### Agreed requirements
- One Quality dropdown in the host lobby: Low, Balanced, High, Max.
  Balanced is the default and streams what the host streams today, except
  where today's 30 Mbps cap used to cut the bitrate (section 2.2).
- Quality sets the bitrate and the encoder's effort together.
- It applies live, like Resolution and Frame rate: video restarts and
  clients get a keyframe.
- The 30 Mbps cap goes. The host is warned, not limited, when the stream
  would overload its network link.
- No protocol or client changes.

### Success criteria
- At High and Max, colored text and fine detail on the client are visibly
  sharper than at Balanced, on a gigabit LAN.
- The slower encoder settings at High and Max add under 1 ms of encode time
  per frame on the test GPUs; any that cost more keep the fast setting.
- An AMD card that can't open the slower AMF setting still encodes on the
  GPU, never dropping to x264 because of Quality.
- On a 100 Mbps link, the lobby warns before the stream outgrows the link.

## 2. Quality presets

### 2.1 `StreamQuality` (Core)
A new enum in `CouchLink.Core/Video`, with a bitrate factor and a label:

| Quality | Factor | 1080p60 | 1440p60 | 1080p144 | Encoder effort |
|---|---|---|---|---|---|
| Low | 0.6 | 6 Mbps | 10.7 | 14.4 | fast |
| Balanced | 1 | 10 Mbps | 17.8 | 24 | fast |
| High | 2.5 | 25 Mbps | 44 | 60 | slower |
| Max | 5 | 50 Mbps | 89 | 100 (cap) | slower |

`StreamQuality.Factor(quality)` and `StreamQuality.Label(quality)` are
static helpers, the way `StreamSettings.Label` works for resolutions.
`StreamQuality.UsesSlowerEncoder(quality)` is true for High and Max.

### 2.2 `StreamSettings`
- A third constructor argument, `StreamQuality quality`, defaulting to
  `Balanced`. `StreamSettings.Default` stays 1080p, 60 fps, Balanced.
- `BitRateFor(size)` becomes
  `10 Mbps x (pixels / 1920x1080) x (fps / 60) x factor`, clamped to
  2-100 Mbps.
- `MaxBitRate` changes from 30 to 100 Mbps. At Balanced the only bitrates
  that change are the ones the old 30 Mbps cap cut: settings whose formula
  gives more than 30 Mbps, such as Native 4K at 60 fps (40 Mbps), 1080p at
  240 fps (40) or 1440p at 144 fps (42.7). Everything else is unchanged.

### 2.3 Encoder effort (`EncoderChoice`)
Slower candidates are new entries in the candidate list, named like the
existing `"h264_amf (lowlatency)"`:

| Candidate | Opens | Options compared with its fast version |
|---|---|---|
| `h264_nvenc (p3)` | `h264_nvenc` | `preset=p3` instead of `p1` |
| `h264_amf (balanced)` | `h264_amf` | adds `quality=balanced` |

The fast candidates keep today's options exactly. FFmpeg's AMF `quality`
defaults to -1, which leaves the choice to the driver, so the manual gate
(7.2) checks that `quality=balanced` really encodes slower and better than
the driver default. If it doesn't, the slower AMF candidate uses
`quality=quality` instead.

`Candidates(vendorId, quality)` puts the slower candidate first when
`UsesSlowerEncoder(quality)`:
- AMD, High/Max: `h264_amf (balanced)`, `h264_amf`, `h264_amf (lowlatency)`, `libx264`
- NVIDIA, High/Max: `h264_nvenc (p3)`, `h264_nvenc`, `libx264`
- Low/Balanced, and any other vendor: unchanged.

A card that refuses the slower options falls back to the fast hardware
candidate through the existing skip-and-retry in `ScreenVideoSource`, and
the skip is logged as today.

x264 keeps `ultrafast` at every Quality: it is the last resort, already
CPU-bound, and a slower preset would make it lag more.

`IsHardware(name)` changes from `name != libx264` to
`CodecOf(name) != libx264`, and `CodecOf` maps the two new names.

## 3. Host video processor fix

`Nv12Converter` calls
`VideoProcessorSetStreamAutoProcessingMode(processor, 0, false)` after
creating the processor, matching `FramePresenter`. This applies at every
Quality and needs no setting.

## 4. Link budget warning

### 4.1 `LinkBudget` (Core)
A pure function in `CouchLink.Core/Net`:

```
LinkBudget.Check(long bitRate, int clients, long? linkBitsPerSecond) -> string? warning
```

- Needed = `bitRate x clients x 1.2`. The 1.2 is the worst-case FEC
  overhead (`VideoShardPacket.MaxParityPercent`, 20%).
- Returns a warning when needed is more than 70% of the link; otherwise
  null. Null too when there are no clients or the link speed is unknown.
- Warning text, for example:
  `High at 6 players needs about 180 Mbps; this PC's network link is 100 Mbps. Lower Quality or Resolution.`
  The function takes the Quality label and works out the rest.

### 4.2 Which link
The host streams to each client from the network card that carries the
client's subnet. `HostLinks` (App) finds, for each client address in
`HostService._targets`, the up, non-loopback `NetworkInterface` with an
IPv4 unicast address whose subnet contains it, and reads its `Speed`.
Clients are grouped by card, and the warning shows for the card with the
worst ratio. A client that matches no card, or a card that reports
`Speed <= 0`, is left out of the check.

### 4.3 When it runs
On `IHostUi.PlayersChanged` and after `ChangeSettings`. `HostLobbyView`
shows the result in a new `BudgetWarning` text line under the Stream row,
in amber, collapsed when there is no warning.

## 5. Host lobby UI

The Stream row gets a third dropdown:

```
Stream  [1080p v] [60 fps v] [Balanced v]
        ! High at 6 players needs about 180 Mbps; ...   (only when over)
```

- `QualityBox` lists Low, Balanced, High, Max; Balanced selected.
- Its `SelectionChanged` calls `_host.ChangeSettings(CurrentSettings())`,
  like the other two boxes.
- Like Resolution and Frame rate, Quality isn't saved between sessions.
- The Details text already shows the encoder and bitrate; it adds the
  Quality label, so a fallback from `h264_amf (balanced)` to `h264_amf` is
  visible there and in the log.
- The settings-changed log line includes the Quality label.

## 6. Components

### 6.1 `CouchLink.Core`
- `Video/StreamQuality.cs` (new): enum and helpers.
- `Video/StreamSettings.cs`: `Quality`, new bitrate formula, 100 Mbps cap.
- `Video/EncoderChoice.cs`: new candidates, `Candidates(vendorId, quality)`,
  options, `CodecOf`, `IsHardware`.
- `Net/LinkBudget.cs` (new): the budget check.

### 6.2 `CouchLink.Video`
- `Nv12Converter.cs`: auto-processing off.

### 6.3 `CouchLink.App`
- `HostVideo.cs`: passes `settings.Quality` to `EncoderChoice.Candidates`;
  summary includes the Quality label.
- `HostService.cs`: log line; exposes the client addresses for the budget.
- `HostLinks.cs` (new): maps client addresses to link speeds.
- `Views/HostLobbyView.xaml(.cs)`: `QualityBox`, `BudgetWarning`.

### 6.4 `CouchLink.VideoTest`
- A `--quality` option, so encode time per preset can be measured.

## 7. Testing

### 7.1 Unit (CouchLink.Core.Tests)
- `StreamSettings`: bitrate at each Quality for 1080p60, 1440p60 and
  1080p144; the 100 Mbps cap; the 2 Mbps floor; Balanced unchanged from
  today for every case Plan 4 tested below the old cap.
- `EncoderChoice`: candidate order per vendor and Quality; options of the
  new candidates; `CodecOf` and `IsHardware` for every candidate.
- `LinkBudget`: exactly 70% (no warning), just over (warning), no clients,
  unknown speed, the warning text.

### 7.2 Manual gate (added to `docs/gate-results.md`)
- `CouchLink.VideoTest --quality high` and `--quality max` on an NVIDIA and
  an AMD card: the slower candidate opens (or falls back to the fast one,
  logged), and encode time rises by under 1 ms per frame against Balanced.
- A live session at Max on a gigabit switch: colored text is sharper than
  at Balanced, and the client's packet-loss stats stay where they are at
  Balanced, including across keyframes.
- With the host's card forced to 100 Mbps: the warning appears as players
  join at High, and disappears after switching to Balanced.

## 8. Out of scope

- **4:4:4 color.** This is what fixes cause 1. It needs NVENC (AMF can't
  encode H.264 4:4:4), a client decode capability check because most GPU
  decoders can't decode it, and protocol changes. It gets its own design;
  ROADMAP.md gets an entry.
- Automatic bitrate adaptation to packet loss or link speed. The host
  decides; the warning only informs.
- An expert mode with separate bitrate and encoder controls.
- Saving stream settings between sessions.

## 9. Docs

- README and `docs/cafe-setup-guide.md`: a short paragraph on Quality and
  the link warning.
- ROADMAP.md: the 4:4:4 entry from section 8.
