# CouchLink Plan 3: Video Stream Protocol Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry encoded video frames from the host to every client over UDP, with Reed-Solomon FEC repairing lost packets and keyframe-on-demand recovering from losses FEC can't fix, so that Plan 4 only has to plug in capture/encode on the host and decode/display on the client.

**Architecture:** Pure, unit-tested pieces in `CouchLink.Core`: a GF(256) Reed-Solomon codec (`Fec/`), the wire format (`Protocol/`), and the frame pipeline (`Video/`): `FramePacketizer` (host: frame -> shard packets), `FrameAssembler` (client: packets -> repaired frames), `DecodeGate` (client: hold frames after a loss until a keyframe, ask for one), `KeyframePolicy` and `StreamTargets` (host: when to force keyframes, who to send to). `VideoStreamer` (host) and `VideoClient` (client) run them over real UDP sockets. Until Plan 4 the host streams a verifiable test pattern (`TestPatternSource`) and the dev window shows live stream counters.

**Tech Stack:** C# / .NET 10, xUnit, Microsoft.Extensions.TimeProvider.Testing, `System.Net.Sockets.UdpClient`, Windows UI Automation (verification only). No new NuGet packages.

**Spec:** `docs/superpowers/specs/2026-10-05-couchlink-design.md` (section 3 Ports, section 5.1 keyframe and send-to-all rules, section 5.2 Packetization & loss recovery, section 9 Testing)

**Builds on:** v1.1.1 (`main` @ c88975a). Branch: `plan3-video`. Issue: #16. Followed by Plan 4 (capture, encode, decode, display, F2 stats: #14, #15, #17, #18), which implements `IEncodedVideoSource` and consumes `VideoClient`'s frames and stats.

**Deviation from spec (decided 2026-10-06):** section 5.2 sends keyframe requests over TCP and section 3 puts session control on TCP 47801. That channel arrives with the lobby (v1.4). Until then the host streams to every client it receives input packets from, and clients send keyframe requests as small UDP datagrams to the host's input port (47803), repeating every 300 ms until a keyframe arrives. v1.4 replaces both.

## Global Constraints

- C#, .NET 10. `CouchLink.Core` stays `net10.0` (no Windows APIs); `TreatWarningsAsErrors` is on for every project.
- No Claude attribution trailers in commit messages. Commits must be signed (`main` requires it; git is already configured to sign).
- Ports: **UDP 47802** video (host -> client), **UDP 47803** input and, until v1.4, keyframe requests (client -> host).
- Every datagram starts with magic **0x4C43 ("CL")**, version **1**, and a packet type byte; all integers little-endian. Types: 1 input, 2 video shard, 3 keyframe request.
- Video shard datagram: **20-byte header + 1200-byte payload = 1220 bytes**, every shard the same size (the last data shard of a frame is zero-padded).
- FEC: Reed-Solomon over GF(256), **at most 200 data shards per FEC block**, parity **20% by default, configurable 10-20%**, rounded up (at least 1 parity shard per block).
- Keyframes **only on demand**: when a new client appears and when a client asks after unrecoverable loss. Host keyframes at least **250 ms** apart; a waiting client repeats its request every **300 ms**.
- A client drops out of the host's send list after **5 s** without input packets.
- A frame unfinished for **250 ms** is given up as lost; the client keeps at most **8** unfinished frames.
- Encode once, send to all: the same packets go to every client, one unicast copy each.
- No encryption (own LAN).

## Review Focus

1. **A keyframe arrives as ~150 back-to-back datagrams.** Windows' default 64 KB UDP receive buffer would drop most of them; the client socket must use an 8 MB buffer so a burst arrives whole. Pinned in Task 7 (`A_keyframe_burst_arrives_complete`).
2. **Garbage or inconsistent datagrams on UDP 47802** (wrong size, impossible indices, packets of one frame that disagree on length or shard counts, a length larger than the shards can hold) are ignored or the frame is dropped as lost, never an exception, and memory stays bounded. Pinned in Task 2 (`Impossible_headers_are_rejected`) and Task 4 (`Packets_that_disagree_with_their_frame_are_ignored`, `Frame_whose_length_does_not_fit_its_shards_is_dropped`, `At_most_MaxPendingFrames_unfinished_frames_are_kept`).
3. **The last frame before the host screen goes still is lost.** No newer frame comes to reveal the loss, so the client must give the frame up after 250 ms and ask for a keyframe instead of waiting forever. Pinned in Task 4 (`Unfinished_frame_is_given_up_after_the_timeout`), Task 5 (`A_lost_frame_waits_for_the_next_keyframe`) and Task 9 (`VideoClient` ticks `AbandonStale`).
4. **Nine clients ask for keyframes at once, or keyframes keep getting lost.** The host sends at most one keyframe per 250 ms and never drops a pending request. Pinned in Task 6 (`Many_requests_cost_one_keyframe`, `Keyframes_are_at_least_MinInterval_apart`, `A_request_forces_keyframes_until_one_is_sent`).
5. **The host stops and starts streaming while a client keeps running.** Frame numbers start over at 0, below what the client already finished; the client must accept the new stream at once instead of discarding it as late packets. Pinned in Task 4 (`A_new_stream_id_starts_over_even_with_lower_frame_numbers`).

---

## File Structure

```
src/CouchLink.Core/Fec/
  GaloisField.cs           GF(256) arithmetic (tables, multiply, inverse, multiply-add)
  ReedSolomon.cs           systematic Cauchy Reed-Solomon erasure code
src/CouchLink.Core/Protocol/
  Wire.cs                  shared magic/version/type header bytes
  InputPacket.cs           (modify) use Wire
  VideoShardPacket.cs      VideoShardHeader + 1220-byte shard datagram
  KeyframeRequest.cs       8-byte client -> host keyframe request
src/CouchLink.Core/Video/
  FramePacketizer.cs       host: frame -> FEC-protected shard packets
  FrameAssembler.cs        client: shard packets -> repaired frames, VideoReceiveStats
  DecodeGate.cs            client: hold frames until a keyframe after loss; request pacing
  KeyframePolicy.cs        host: when to force a keyframe
  StreamTargets.cs         host: clients to stream to, learned from input packets
  IEncodedVideoSource.cs   EncodedFrame + the source interface Plan 4 implements
  TestPattern.cs           verifiable fake frames + TestPatternSource
  VideoStreamer.cs         host: source -> packetize -> send loop, VideoSendStats
  VideoClient.cs           client: receive -> assemble -> gate -> onFrame, VideoClientStats
src/CouchLink.Core/Net/
  Ports.cs                 (modify) add Video = 47802
  UdpReceiveLoop.cs        shared receive loop (from InputReceiver)
  InputReceiver.cs         (modify) pass sender address; keyframe requests
  InputSender.cs           (modify) SendKeyframeRequest
  VideoSender.cs           IVideoPacketSender + unicast sender
  VideoReceiver.cs         client socket with an 8 MB receive buffer
src/CouchLink.App/
  HostInputService.cs      (modify) stream the test pattern to clients
  ClientVideoService.cs    client video pipeline + test-pattern check for the UI
  MainWindow.xaml(.cs)     (modify) start video on Join, show video counters
tests/CouchLink.Core.Tests/
  ReedSolomonTests.cs
  VideoPacketTests.cs
  FramePacketizerTests.cs
  FrameAssemblerTests.cs
  DecodeGateTests.cs
  StreamLossTests.cs
  KeyframePolicyTests.cs
  StreamTargetsTests.cs
  UdpVideoTests.cs
  UdpInputTests.cs         (modify) new RunAsync callback shape
  TestPatternTests.cs
  VideoLoopbackTests.cs
```

---

### Task 1: Reed-Solomon erasure code over GF(256)

**Files:**
- Create: `src/CouchLink.Core/Fec/GaloisField.cs`
- Create: `src/CouchLink.Core/Fec/ReedSolomon.cs`
- Test: `tests/CouchLink.Core.Tests/ReedSolomonTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `public static class GaloisField` with `byte Multiply(byte a, byte b)`, `byte Inverse(byte a)` (throws `DivideByZeroException` for 0), `void MultiplyAdd(byte factor, ReadOnlySpan<byte> source, Span<byte> destination)` (destination ^= factor * source).
  - `public sealed class ReedSolomon` with `const int MaxTotalShards = 256`, ctor `(int dataShards, int parityShards)`, `static ReedSolomon For(int dataShards, int parityShards)` (cached), `int DataShards`, `int ParityShards`, `int TotalShards`, `void Encode(Memory<byte>[] shards)` (fills shards[k..] from shards[..k]), `bool Reconstruct(Memory<byte>[] shards, bool[] present, out int rebuilt)` (rebuilds missing **data** shards in place; false if fewer than k present).

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/ReedSolomonTests.cs`:
```csharp
using CouchLink.Core.Fec;

namespace CouchLink.Core.Tests;

public class ReedSolomonTests
{
    private static Memory<byte>[] EncodedShards(ReedSolomon rs, int size, int seed)
    {
        var rng = new Random(seed);
        var shards = new Memory<byte>[rs.TotalShards];
        for (int i = 0; i < shards.Length; i++)
        {
            var bytes = new byte[size];
            if (i < rs.DataShards)
                rng.NextBytes(bytes);
            shards[i] = bytes;
        }
        rs.Encode(shards);
        return shards;
    }

    private static Memory<byte>[] Copy(Memory<byte>[] shards) =>
        shards.Select(s => (Memory<byte>)s.ToArray()).ToArray();

    [Fact]
    public void Every_nonzero_element_times_its_inverse_is_one()
    {
        for (int a = 1; a < 256; a++)
            Assert.Equal(1, GaloisField.Multiply((byte)a, GaloisField.Inverse((byte)a)));
    }

    [Fact]
    public void Multiplication_distributes_over_xor()
    {
        for (int a = 0; a < 256; a++)
        {
            byte left = GaloisField.Multiply((byte)a, 0x53 ^ 0xCA);
            byte right = (byte)(GaloisField.Multiply((byte)a, 0x53) ^ GaloisField.Multiply((byte)a, 0xCA));
            Assert.Equal(left, right);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(10, 2)]
    [InlineData(17, 4)]
    [InlineData(200, 40)]
    public void Any_k_of_the_shards_rebuild_the_data(int k, int m)
    {
        var rs = ReedSolomon.For(k, m);
        var original = EncodedShards(rs, size: 64, seed: k * 31 + m);
        var rng = new Random(k);
        for (int trial = 0; trial < 50; trial++)
        {
            var shards = Copy(original);
            var present = Enumerable.Repeat(true, rs.TotalShards).ToArray();
            foreach (int lost in Enumerable.Range(0, rs.TotalShards).OrderBy(_ => rng.Next()).Take(m))
            {
                present[lost] = false;
                shards[lost].Span.Fill(0xEE); // garbage where the lost shard was
            }

            Assert.True(rs.Reconstruct(shards, present, out int rebuilt));
            Assert.Equal(present.Take(k).Count(p => !p), rebuilt);
            for (int i = 0; i < k; i++)
                Assert.True(original[i].Span.SequenceEqual(shards[i].Span), $"data shard {i}, trial {trial}");
        }
    }

    [Fact]
    public void More_losses_than_parity_cannot_be_rebuilt()
    {
        var rs = ReedSolomon.For(4, 2);
        var shards = EncodedShards(rs, 16, seed: 1);
        bool[] present = [false, false, false, true, true, true];
        Assert.False(rs.Reconstruct(shards, present, out _));
    }

    [Fact]
    public void Nothing_missing_rebuilds_nothing()
    {
        var rs = ReedSolomon.For(4, 2);
        var original = EncodedShards(rs, 16, seed: 2);
        var shards = Copy(original);
        Assert.True(rs.Reconstruct(shards, [true, true, true, true, false, false], out int rebuilt));
        Assert.Equal(0, rebuilt);
        for (int i = 0; i < 4; i++)
            Assert.True(original[i].Span.SequenceEqual(shards[i].Span));
    }

    [Fact]
    public void For_returns_one_cached_codec_and_encoding_is_deterministic()
    {
        Assert.Same(ReedSolomon.For(10, 2), ReedSolomon.For(10, 2));
        var a = EncodedShards(ReedSolomon.For(10, 2), 32, seed: 3);
        var b = EncodedShards(new ReedSolomon(10, 2), 32, seed: 3);
        for (int i = 0; i < 12; i++)
            Assert.True(a[i].Span.SequenceEqual(b[i].Span));
    }

    [Fact]
    public void Bad_arguments_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReedSolomon(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReedSolomon(1, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReedSolomon(200, 57));
        Assert.Throws<DivideByZeroException>(() => GaloisField.Inverse(0));

        var rs = ReedSolomon.For(2, 1);
        Assert.Throws<ArgumentException>(() => rs.Encode(new Memory<byte>[2]));
        Assert.Throws<ArgumentException>(() => rs.Encode([new byte[4], new byte[4], new byte[5]]));
        Assert.Throws<ArgumentException>(() => rs.Reconstruct([new byte[4], new byte[4], new byte[4]], [true, true], out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~ReedSolomonTests`
Expected: build FAILS: `CouchLink.Core.Fec` / `ReedSolomon` / `GaloisField` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Fec/GaloisField.cs`:
```csharp
namespace CouchLink.Core.Fec;

/// <summary>
/// Arithmetic in GF(2^8) with the polynomial x^8 + x^4 + x^3 + x^2 + 1 (0x11D), generator 2.
/// Addition and subtraction are XOR.
/// </summary>
public static class GaloisField
{
    private static readonly byte[] Exp = new byte[510];
    private static readonly byte[] Log = new byte[256];
    private static readonly byte[] Products = new byte[256 * 256];

    static GaloisField()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Exp[i + 255] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0)
                x ^= 0x11D;
        }

        for (int a = 1; a < 256; a++)
            for (int b = 1; b < 256; b++)
                Products[(a << 8) | b] = Exp[Log[a] + Log[b]];
    }

    public static byte Multiply(byte a, byte b) => Products[(a << 8) | b];

    public static byte Inverse(byte a)
    {
        if (a == 0)
            throw new DivideByZeroException("0 has no inverse in GF(256).");
        return Exp[255 - Log[a]];
    }

    /// <summary>destination[i] ^= factor * source[i] for every i.</summary>
    public static void MultiplyAdd(byte factor, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (factor == 0)
            return;
        var row = Products.AsSpan(factor << 8, 256);
        for (int i = 0; i < source.Length; i++)
            destination[i] ^= row[source[i]];
    }
}
```

`src/CouchLink.Core/Fec/ReedSolomon.cs`:
```csharp
using System.Collections.Concurrent;

namespace CouchLink.Core.Fec;

/// <summary>
/// Systematic Reed-Solomon erasure code over GF(256) built from a Cauchy matrix: k data shards
/// plus m parity shards, and any k of the k + m shards rebuild the data. Parity row i, column j
/// is 1 / ((k + i) XOR j); every square submatrix of a Cauchy matrix is invertible, which is
/// what guarantees "any k shards". Instances are immutable and thread-safe.
/// </summary>
public sealed class ReedSolomon
{
    public const int MaxTotalShards = 256;

    private static readonly ConcurrentDictionary<(int Data, int Parity), ReedSolomon> Cache = new();

    private readonly byte[,] _parityRows; // [parity shard, data shard]

    public ReedSolomon(int dataShards, int parityShards)
    {
        if (dataShards < 1)
            throw new ArgumentOutOfRangeException(nameof(dataShards), "Need at least one data shard.");
        if (parityShards < 0)
            throw new ArgumentOutOfRangeException(nameof(parityShards), "Parity shards can't be negative.");
        if (dataShards + parityShards > MaxTotalShards)
            throw new ArgumentOutOfRangeException(nameof(parityShards), $"At most {MaxTotalShards} shards in total.");

        DataShards = dataShards;
        ParityShards = parityShards;
        _parityRows = new byte[parityShards, dataShards];
        for (int i = 0; i < parityShards; i++)
            for (int j = 0; j < dataShards; j++)
                _parityRows[i, j] = GaloisField.Inverse((byte)((dataShards + i) ^ j));
    }

    /// <summary>A shared codec for this shape; building one costs k * m inverses.</summary>
    public static ReedSolomon For(int dataShards, int parityShards) =>
        Cache.GetOrAdd((dataShards, parityShards), key => new ReedSolomon(key.Data, key.Parity));

    public int DataShards { get; }
    public int ParityShards { get; }
    public int TotalShards => DataShards + ParityShards;

    /// <summary>Computes shards[k..k+m) from shards[0..k). All shards must have the same length.</summary>
    public void Encode(Memory<byte>[] shards)
    {
        CheckShards(shards);
        for (int i = 0; i < ParityShards; i++)
        {
            var parity = shards[DataShards + i].Span;
            parity.Clear();
            for (int j = 0; j < DataShards; j++)
                GaloisField.MultiplyAdd(_parityRows[i, j], shards[j].Span, parity);
        }
    }

    /// <summary>
    /// Rebuilds the missing data shards in place. <paramref name="present"/>[i] says whether
    /// shards[i] holds received bytes; buffers of missing shards must still be allocated.
    /// Parity shards are not rebuilt. Returns false when fewer than k shards are present.
    /// </summary>
    public bool Reconstruct(Memory<byte>[] shards, bool[] present, out int rebuilt)
    {
        CheckShards(shards);
        if (present.Length != TotalShards)
            throw new ArgumentException($"Expected {TotalShards} presence flags.", nameof(present));

        rebuilt = 0;
        var missing = new List<int>();
        for (int i = 0; i < DataShards; i++)
            if (!present[i])
                missing.Add(i);
        if (missing.Count == 0)
            return true;

        // Rows of the encoding matrix for the first k shards we have.
        var used = new int[DataShards];
        int count = 0;
        for (int i = 0; i < TotalShards && count < DataShards; i++)
            if (present[i])
                used[count++] = i;
        if (count < DataShards)
            return false;

        var matrix = new byte[DataShards, DataShards];
        for (int r = 0; r < DataShards; r++)
            for (int c = 0; c < DataShards; c++)
                matrix[r, c] = used[r] < DataShards
                    ? (byte)(used[r] == c ? 1 : 0)
                    : _parityRows[used[r] - DataShards, c];
        var inverse = Invert(matrix);

        // data[d] = sum over c of inverse[d, c] * (shard used[c])
        foreach (int d in missing)
        {
            var target = shards[d].Span;
            target.Clear();
            for (int c = 0; c < DataShards; c++)
                GaloisField.MultiplyAdd(inverse[d, c], shards[used[c]].Span, target);
        }
        rebuilt = missing.Count;
        return true;
    }

    private void CheckShards(Memory<byte>[] shards)
    {
        if (shards.Length != TotalShards)
            throw new ArgumentException($"Expected {TotalShards} shards.", nameof(shards));
        int size = shards[0].Length;
        foreach (var shard in shards)
            if (shard.Length != size)
                throw new ArgumentException("All shards must be the same size.", nameof(shards));
    }

    /// <summary>Gauss-Jordan elimination over GF(256).</summary>
    private static byte[,] Invert(byte[,] matrix)
    {
        int n = matrix.GetLength(0);
        var a = (byte[,])matrix.Clone();
        var inv = new byte[n, n];
        for (int i = 0; i < n; i++)
            inv[i, i] = 1;

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            while (pivot < n && a[pivot, col] == 0)
                pivot++;
            if (pivot == n)
                throw new InvalidOperationException("Matrix is singular.");
            if (pivot != col)
            {
                SwapRows(a, pivot, col);
                SwapRows(inv, pivot, col);
            }

            byte scale = GaloisField.Inverse(a[col, col]);
            for (int c = 0; c < n; c++)
            {
                a[col, c] = GaloisField.Multiply(a[col, c], scale);
                inv[col, c] = GaloisField.Multiply(inv[col, c], scale);
            }

            for (int r = 0; r < n; r++)
            {
                byte factor = a[r, col];
                if (r == col || factor == 0)
                    continue;
                for (int c = 0; c < n; c++)
                {
                    a[r, c] ^= GaloisField.Multiply(factor, a[col, c]);
                    inv[r, c] ^= GaloisField.Multiply(factor, inv[col, c]);
                }
            }
        }
        return inv;
    }

    private static void SwapRows(byte[,] m, int x, int y)
    {
        for (int c = 0; c < m.GetLength(1); c++)
            (m[x, c], m[y, c]) = (m[y, c], m[x, c]);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~ReedSolomonTests`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Core/Fec tests/CouchLink.Core.Tests/ReedSolomonTests.cs
git commit -m "feat(core): Reed-Solomon erasure code over GF(256)"
```

---

### Task 2: Wire format for video shards and keyframe requests

**Files:**
- Create: `src/CouchLink.Core/Protocol/Wire.cs`
- Create: `src/CouchLink.Core/Protocol/VideoShardPacket.cs`
- Create: `src/CouchLink.Core/Protocol/KeyframeRequest.cs`
- Modify: `src/CouchLink.Core/Protocol/InputPacket.cs` (use `Wire`)
- Test: `tests/CouchLink.Core.Tests/VideoPacketTests.cs`

**Interfaces:**
- Consumes: `ReedSolomon.MaxTotalShards` (Task 1).
- Produces:
  - `public static class Wire` with `const ushort Magic = 0x4C43`, `const byte Version = 1`, `TypeInput = 1`, `TypeVideoShard = 2`, `TypeKeyframeRequest = 3`, `void WriteHeader(Span<byte> destination, byte type)`, `bool HasHeader(ReadOnlySpan<byte> source, byte type)`.
  - `public readonly record struct VideoShardHeader(ushort StreamId, uint Frame, bool Keyframe, uint FrameLength, byte Block, byte BlockCount, byte Shard, byte DataShards, byte ParityShards)` with `int TotalShards`.
  - `public static class VideoShardPacket` with `HeaderSize = 20`, `PayloadSize = 1200`, `Size = 1220`, `void WriteHeader(Span<byte> packet, in VideoShardHeader header)`, `bool TryParse(ReadOnlySpan<byte> packet, out VideoShardHeader header)`, `ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> packet)`.
  - `public readonly record struct KeyframeRequest(byte Slot)` with `const int Size = 8`, `void WriteTo(Span<byte> destination)`, `static bool TryParse(ReadOnlySpan<byte> source, out KeyframeRequest request)`.

Shard header layout (little-endian):

| Offset | Size | Field |
|---|---|---|
| 0 | 2 | magic 0x4C43 |
| 2 | 1 | version 1 |
| 3 | 1 | type 2 |
| 4 | 1 | flags (bit 0 = keyframe) |
| 5 | 1 | FEC block index |
| 6 | 1 | FEC block count |
| 7 | 1 | shard index within the block (data first, then parity) |
| 8 | 1 | data shards in this block (k) |
| 9 | 1 | parity shards in this block (m) |
| 10 | 2 | stream id (random per host stream) |
| 12 | 4 | frame number |
| 16 | 4 | frame length in bytes |
| 20 | 1200 | payload |

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/VideoPacketTests.cs`:
```csharp
using CouchLink.Core.Input;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class VideoPacketTests
{
    private static readonly VideoShardHeader Sample = new(
        StreamId: 0xBEEF, Frame: 123_456, Keyframe: true, FrameLength: 150_000,
        Block: 1, BlockCount: 2, Shard: 7, DataShards: 125, ParityShards: 25);

    private static byte[] Packet(VideoShardHeader header)
    {
        var packet = new byte[VideoShardPacket.Size];
        VideoShardPacket.WriteHeader(packet, header);
        return packet;
    }

    [Fact]
    public void Header_round_trips()
    {
        Assert.True(VideoShardPacket.TryParse(Packet(Sample), out var parsed));
        Assert.Equal(Sample, parsed);
        Assert.True(VideoShardPacket.TryParse(Packet(Sample with { Keyframe = false }), out parsed));
        Assert.False(parsed.Keyframe);
    }

    [Fact]
    public void Payload_follows_the_header()
    {
        var packet = Packet(Sample);
        packet[VideoShardPacket.HeaderSize] = 0xAB;
        packet[^1] = 0xCD;
        var payload = VideoShardPacket.Payload(packet);
        Assert.Equal(VideoShardPacket.PayloadSize, payload.Length);
        Assert.Equal(0xAB, payload[0]);
        Assert.Equal(0xCD, payload[^1]);
    }

    [Theory]
    [InlineData(VideoShardPacket.Size - 1)]
    [InlineData(VideoShardPacket.Size + 1)]
    [InlineData(InputPacket.Size)]
    public void Wrong_length_is_rejected(int length)
    {
        var packet = new byte[length];
        Packet(Sample).AsSpan(0, Math.Min(length, VideoShardPacket.Size)).CopyTo(packet);
        Assert.False(VideoShardPacket.TryParse(packet, out _));
    }

    [Fact]
    public void Wrong_magic_version_or_type_is_rejected()
    {
        foreach (int offset in new[] { 0, 2, 3 })
        {
            var packet = Packet(Sample);
            packet[offset] ^= 0xFF;
            Assert.False(VideoShardPacket.TryParse(packet, out _), $"byte {offset} changed");
        }
    }

    [Theory]
    [InlineData("no blocks")]
    [InlineData("block past the end")]
    [InlineData("no data shards")]
    [InlineData("shard past the end")]
    [InlineData("empty frame")]
    [InlineData("too many shards for Reed-Solomon")]
    public void Impossible_headers_are_rejected(string problem)
    {
        var header = problem switch
        {
            "no blocks" => Sample with { Block = 0, BlockCount = 0 },
            "block past the end" => Sample with { Block = 2 },
            "no data shards" => Sample with { DataShards = 0 },
            "shard past the end" => Sample with { Shard = 150 }, // 125 + 25 shards: 0..149
            "empty frame" => Sample with { FrameLength = 0 },
            "too many shards for Reed-Solomon" => Sample with { DataShards = 250, ParityShards = 10 },
            _ => throw new ArgumentException(problem),
        };
        Assert.False(VideoShardPacket.TryParse(Packet(header), out _));
    }

    [Fact]
    public void Keyframe_request_round_trips_and_is_no_other_packet()
    {
        var bytes = new byte[KeyframeRequest.Size];
        new KeyframeRequest(Slot: 5).WriteTo(bytes);
        Assert.True(KeyframeRequest.TryParse(bytes, out var request));
        Assert.Equal((byte)5, request.Slot);
        Assert.False(InputPacket.TryParse(bytes, out _));
        Assert.False(VideoShardPacket.TryParse(bytes, out _));
    }

    [Fact]
    public void Input_packet_is_not_a_keyframe_request()
    {
        var bytes = new byte[InputPacket.Size];
        new InputPacket(2, 1, 1, PadState.Neutral).WriteTo(bytes);
        Assert.False(KeyframeRequest.TryParse(bytes, out _));
        Assert.False(KeyframeRequest.TryParse(bytes.AsSpan(0, KeyframeRequest.Size), out _)); // right size, wrong type
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~VideoPacketTests`
Expected: build FAILS: `VideoShardHeader`, `VideoShardPacket`, `KeyframeRequest` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Protocol/Wire.cs`:
```csharp
using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>The first four bytes of every CouchLink datagram: magic "CL", version, packet type.</summary>
public static class Wire
{
    public const ushort Magic = 0x4C43; // "CL"
    public const byte Version = 1;

    public const byte TypeInput = 1;
    public const byte TypeVideoShard = 2;
    public const byte TypeKeyframeRequest = 3;

    public static void WriteHeader(Span<byte> destination, byte type)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, Magic);
        destination[2] = Version;
        destination[3] = type;
    }

    public static bool HasHeader(ReadOnlySpan<byte> source, byte type) =>
        source.Length >= 4
        && BinaryPrimitives.ReadUInt16LittleEndian(source) == Magic
        && source[2] == Version
        && source[3] == type;
}
```

`src/CouchLink.Core/Protocol/InputPacket.cs`: delete the three private constants `Magic`, `Version`, `TypeInput`, and replace the header code in `WriteTo` and `TryParse`:
```csharp
    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"Need {Size} bytes.", nameof(destination));

        Wire.WriteHeader(destination, Wire.TypeInput);
        destination[4] = Slot;
        destination[5] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[6..], Epoch);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[10..], Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[14..], (uint)State.Buttons);
        destination[18] = State.LX;
        destination[19] = State.LY;
        destination[20] = State.RX;
        destination[21] = State.RY;
        destination[22] = State.L2;
        destination[23] = State.R2;
    }

    /// <summary>Parses a datagram; returns false for anything that is not a v1 input packet.</summary>
    public static bool TryParse(ReadOnlySpan<byte> source, out InputPacket packet)
    {
        packet = default;
        if (source.Length != Size || !Wire.HasHeader(source, Wire.TypeInput))
            return false;

        var buttons = (PadButtons)BinaryPrimitives.ReadUInt32LittleEndian(source[14..]) & PadButtons.Known;
        var state = new PadState(buttons, source[18], source[19], source[20], source[21], source[22], source[23]);
        packet = new InputPacket(
            source[4],
            BinaryPrimitives.ReadUInt32LittleEndian(source[6..]),
            BinaryPrimitives.ReadUInt32LittleEndian(source[10..]),
            state);
        return true;
    }
```

`src/CouchLink.Core/Protocol/VideoShardPacket.cs`:
```csharp
using System.Buffers.Binary;
using CouchLink.Core.Fec;

namespace CouchLink.Core.Protocol;

/// <summary>
/// Header of one video shard datagram. A frame is cut into 1200-byte data shards, grouped into
/// FEC blocks; each block carries <see cref="DataShards"/> data and <see cref="ParityShards"/>
/// parity shards. <see cref="StreamId"/> is random per host stream, so a client can tell a
/// restarted stream (frame numbers back at 0) from late packets.
/// </summary>
public readonly record struct VideoShardHeader(
    ushort StreamId,
    uint Frame,
    bool Keyframe,
    uint FrameLength,
    byte Block,
    byte BlockCount,
    byte Shard,
    byte DataShards,
    byte ParityShards)
{
    public int TotalShards => DataShards + ParityShards;
}

/// <summary>Host -> client video shard datagram: 20-byte header + 1200-byte payload.</summary>
public static class VideoShardPacket
{
    public const int HeaderSize = 20;
    public const int PayloadSize = 1200;
    public const int Size = HeaderSize + PayloadSize;

    private const byte FlagKeyframe = 1;

    public static void WriteHeader(Span<byte> packet, in VideoShardHeader header)
    {
        if (packet.Length < HeaderSize)
            throw new ArgumentException($"Need {HeaderSize} bytes.", nameof(packet));

        Wire.WriteHeader(packet, Wire.TypeVideoShard);
        packet[4] = header.Keyframe ? FlagKeyframe : (byte)0;
        packet[5] = header.Block;
        packet[6] = header.BlockCount;
        packet[7] = header.Shard;
        packet[8] = header.DataShards;
        packet[9] = header.ParityShards;
        BinaryPrimitives.WriteUInt16LittleEndian(packet[10..], header.StreamId);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[12..], header.Frame);
        BinaryPrimitives.WriteUInt32LittleEndian(packet[16..], header.FrameLength);
    }

    /// <summary>Parses and sanity-checks a shard datagram; false for anything else or anything impossible.</summary>
    public static bool TryParse(ReadOnlySpan<byte> packet, out VideoShardHeader header)
    {
        header = default;
        if (packet.Length != Size || !Wire.HasHeader(packet, Wire.TypeVideoShard))
            return false;

        var parsed = new VideoShardHeader(
            StreamId: BinaryPrimitives.ReadUInt16LittleEndian(packet[10..]),
            Frame: BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]),
            Keyframe: (packet[4] & FlagKeyframe) != 0,
            FrameLength: BinaryPrimitives.ReadUInt32LittleEndian(packet[16..]),
            Block: packet[5],
            BlockCount: packet[6],
            Shard: packet[7],
            DataShards: packet[8],
            ParityShards: packet[9]);

        if (parsed.BlockCount == 0
            || parsed.Block >= parsed.BlockCount
            || parsed.DataShards == 0
            || parsed.TotalShards > ReedSolomon.MaxTotalShards
            || parsed.Shard >= parsed.TotalShards
            || parsed.FrameLength == 0)
            return false;

        header = parsed;
        return true;
    }

    public static ReadOnlySpan<byte> Payload(ReadOnlySpan<byte> packet) => packet.Slice(HeaderSize, PayloadSize);
}
```

`src/CouchLink.Core/Protocol/KeyframeRequest.cs`:
```csharp
namespace CouchLink.Core.Protocol;

/// <summary>
/// Client -> host: "send a keyframe" (8 bytes: header, slot, 3 reserved zero bytes). Sent to the
/// host's input port until the v1.4 session channel exists.
/// </summary>
public readonly record struct KeyframeRequest(byte Slot)
{
    public const int Size = 8;

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Size)
            throw new ArgumentException($"Need {Size} bytes.", nameof(destination));

        Wire.WriteHeader(destination, Wire.TypeKeyframeRequest);
        destination[4] = Slot;
        destination[5..Size].Clear();
    }

    public static bool TryParse(ReadOnlySpan<byte> source, out KeyframeRequest request)
    {
        request = default;
        if (source.Length != Size || !Wire.HasHeader(source, Wire.TypeKeyframeRequest))
            return false;
        request = new KeyframeRequest(source[4]);
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~VideoPacketTests|FullyQualifiedName~InputPacketTests"`
Expected: PASS (14 new tests, and every existing `InputPacketTests` test still passes).

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Core/Protocol tests/CouchLink.Core.Tests/VideoPacketTests.cs
git commit -m "feat(core): video shard and keyframe request packets"
```

---

### Task 3: Frame packetizer

**Files:**
- Create: `src/CouchLink.Core/Video/FramePacketizer.cs`
- Test: `tests/CouchLink.Core.Tests/FramePacketizerTests.cs`

**Interfaces:**
- Consumes: `ReedSolomon.For`, `ReedSolomon.Encode` (Task 1); `VideoShardHeader`, `VideoShardPacket` (Task 2).
- Produces: `public sealed class FramePacketizer` with consts `MaxDataShardsPerBlock = 200`, `MinParityPercent = 10`, `MaxParityPercent = 20`, `DefaultParityPercent = 20`; ctor `(ushort streamId, int parityPercent = DefaultParityPercent)`; `ushort StreamId`; `int ParityPercent`; `static int ParityShardsFor(int dataShards, int parityPercent)`; `List<byte[]> Packetize(uint frameNumber, ReadOnlySpan<byte> frame, bool keyframe)` (every packet is a new `byte[VideoShardPacket.Size]`; block by block, data before parity).

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/FramePacketizerTests.cs`:
```csharp
using CouchLink.Core.Fec;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class FramePacketizerTests
{
    private const int Payload = VideoShardPacket.PayloadSize;

    private static byte[] RandomFrame(int length, int seed = 1)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private static List<VideoShardHeader> Headers(List<byte[]> packets) =>
        packets.Select(p =>
        {
            Assert.True(VideoShardPacket.TryParse(p, out var h));
            return h;
        }).ToList();

    [Fact]
    public void Small_frame_is_one_data_shard_plus_one_parity()
    {
        var frame = RandomFrame(100);
        var packets = new FramePacketizer(streamId: 7).Packetize(42, frame, keyframe: true);
        var headers = Headers(packets);

        Assert.Equal(2, packets.Count);
        Assert.All(headers, h =>
        {
            Assert.Equal((ushort)7, h.StreamId);
            Assert.Equal(42u, h.Frame);
            Assert.True(h.Keyframe);
            Assert.Equal(100u, h.FrameLength);
            Assert.Equal(1, h.BlockCount);
            Assert.Equal(1, h.DataShards);
            Assert.Equal(1, h.ParityShards);
        });
        Assert.Equal(new byte[] { 0, 1 }, headers.Select(h => h.Shard).ToArray());

        var payload = VideoShardPacket.Payload(packets[0]);
        Assert.True(payload[..100].SequenceEqual(frame));
        Assert.True(payload[100..].IndexOfAnyExcept((byte)0) < 0, "padding must be zero");
    }

    [Theory]
    [InlineData(1200, 1, 1)]
    [InlineData(1201, 2, 1)]
    [InlineData(12_000, 10, 2)]
    [InlineData(24_000, 20, 4)]
    [InlineData(240_000, 200, 40)]
    public void Shard_counts_at_20_percent(int length, int data, int parity)
    {
        var headers = Headers(new FramePacketizer(1).Packetize(0, RandomFrame(length), false));
        Assert.Equal(data + parity, headers.Count);
        Assert.All(headers, h =>
        {
            Assert.Equal(data, h.DataShards);
            Assert.Equal(parity, h.ParityShards);
            Assert.Equal(1, h.BlockCount);
        });
    }

    [Fact]
    public void Parity_count_rounds_up()
    {
        Assert.Equal(1, FramePacketizer.ParityShardsFor(1, 10));
        Assert.Equal(1, FramePacketizer.ParityShardsFor(10, 10));
        Assert.Equal(2, FramePacketizer.ParityShardsFor(11, 10));
        Assert.Equal(21, FramePacketizer.ParityShardsFor(101, 20));
        Assert.Equal(40, FramePacketizer.ParityShardsFor(200, 20));
    }

    [Fact]
    public void Large_frame_is_split_into_even_blocks_in_order()
    {
        var headers = Headers(new FramePacketizer(1).Packetize(9, RandomFrame(201 * Payload), true));
        var blocks = headers.GroupBy(h => h.Block).OrderBy(g => g.Key).ToList();

        Assert.Equal(2, blocks.Count);
        Assert.All(headers, h => Assert.Equal(2, h.BlockCount));
        Assert.Equal(101, blocks[0].First().DataShards);
        Assert.Equal(21, blocks[0].First().ParityShards);
        Assert.Equal(100, blocks[1].First().DataShards);
        Assert.Equal(20, blocks[1].First().ParityShards);
        var expectedShards = Enumerable.Range(0, 122).Concat(Enumerable.Range(0, 120)).Select(i => (byte)i);
        Assert.Equal(expectedShards, headers.Select(h => h.Shard)); // block by block, data before parity
    }

    [Fact]
    public void Data_shards_in_order_give_back_the_frame()
    {
        var frame = RandomFrame(250_000, seed: 3);
        var packets = new FramePacketizer(1).Packetize(1, frame, true);
        Assert.All(packets, p => Assert.Equal(VideoShardPacket.Size, p.Length));

        var data = packets
            .Where(p => VideoShardPacket.TryParse(p, out var h) && h.Shard < h.DataShards)
            .SelectMany(p => VideoShardPacket.Payload(p).ToArray())
            .Take(frame.Length)
            .ToArray();
        Assert.Equal(frame, data);
    }

    [Fact]
    public void Parity_rebuilds_a_lost_data_shard()
    {
        var packets = new FramePacketizer(1).Packetize(1, RandomFrame(12_000), false); // 10 data + 2 parity
        var shards = packets.Select(p => (Memory<byte>)VideoShardPacket.Payload(p).ToArray()).ToArray();
        var original = shards[3].ToArray();
        shards[3].Span.Clear();
        var present = Enumerable.Range(0, 12).Select(i => i != 3).ToArray();

        Assert.True(ReedSolomon.For(10, 2).Reconstruct(shards, present, out int rebuilt));
        Assert.Equal(1, rebuilt);
        Assert.Equal(original, shards[3].ToArray());
    }

    [Theory]
    [InlineData(9)]
    [InlineData(21)]
    public void Parity_outside_10_to_20_percent_is_rejected(int percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FramePacketizer(1, percent));
    }

    [Fact]
    public void Empty_frame_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new FramePacketizer(1).Packetize(0, Array.Empty<byte>(), false));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~FramePacketizerTests`
Expected: build FAILS: `CouchLink.Core.Video` / `FramePacketizer` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Video/FramePacketizer.cs`:
```csharp
using CouchLink.Core.Fec;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Video;

/// <summary>
/// Host side: splits one encoded frame into shard datagrams. The frame is cut into 1200-byte
/// data shards, grouped into as few FEC blocks of at most 200 data shards as possible (sizes
/// differ by at most one), and each block gets <see cref="ParityPercent"/> (rounded up)
/// Reed-Solomon parity shards. Packets come out block by block, data before parity.
/// </summary>
public sealed class FramePacketizer
{
    public const int MaxDataShardsPerBlock = 200;
    public const int MinParityPercent = 10;
    public const int MaxParityPercent = 20;
    public const int DefaultParityPercent = 20;

    public FramePacketizer(ushort streamId, int parityPercent = DefaultParityPercent)
    {
        if (parityPercent is < MinParityPercent or > MaxParityPercent)
            throw new ArgumentOutOfRangeException(
                nameof(parityPercent), $"Parity must be {MinParityPercent}-{MaxParityPercent}%.");
        StreamId = streamId;
        ParityPercent = parityPercent;
    }

    public ushort StreamId { get; }
    public int ParityPercent { get; }

    public static int ParityShardsFor(int dataShards, int parityPercent) =>
        (dataShards * parityPercent + 99) / 100;

    public List<byte[]> Packetize(uint frameNumber, ReadOnlySpan<byte> frame, bool keyframe)
    {
        if (frame.IsEmpty)
            throw new ArgumentException("Frame is empty.", nameof(frame));

        int totalData = (frame.Length + VideoShardPacket.PayloadSize - 1) / VideoShardPacket.PayloadSize;
        int blockCount = (totalData + MaxDataShardsPerBlock - 1) / MaxDataShardsPerBlock;
        if (blockCount > byte.MaxValue)
            throw new ArgumentException($"A {frame.Length}-byte frame is too large to send.", nameof(frame));

        var packets = new List<byte[]>();
        int firstShard = 0; // index of this block's first data shard within the frame
        for (int block = 0; block < blockCount; block++)
        {
            int k = totalData / blockCount + (block < totalData % blockCount ? 1 : 0);
            int m = ParityShardsFor(k, ParityPercent);
            var shards = new Memory<byte>[k + m];
            for (int s = 0; s < k + m; s++)
            {
                var packet = new byte[VideoShardPacket.Size];
                VideoShardPacket.WriteHeader(packet, new VideoShardHeader(
                    StreamId, frameNumber, keyframe, (uint)frame.Length,
                    (byte)block, (byte)blockCount, (byte)s, (byte)k, (byte)m));
                shards[s] = packet.AsMemory(VideoShardPacket.HeaderSize, VideoShardPacket.PayloadSize);
                if (s < k)
                {
                    int start = (firstShard + s) * VideoShardPacket.PayloadSize;
                    int end = Math.Min(start + VideoShardPacket.PayloadSize, frame.Length);
                    frame[start..end].CopyTo(shards[s].Span); // the rest of the last shard stays zero
                }
                packets.Add(packet);
            }
            ReedSolomon.For(k, m).Encode(shards);
            firstShard += k;
        }
        return packets;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~FramePacketizerTests`
Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Core/Video/FramePacketizer.cs tests/CouchLink.Core.Tests/FramePacketizerTests.cs
git commit -m "feat(core): split encoded frames into FEC-protected shard packets"
```

---

### Task 4: Frame assembler

**Files:**
- Create: `src/CouchLink.Core/Video/FrameAssembler.cs`
- Test: `tests/CouchLink.Core.Tests/FrameAssemblerTests.cs`

**Interfaces:**
- Consumes: `ReedSolomon.For`, `ReedSolomon.Reconstruct` (Task 1); `VideoShardHeader`, `VideoShardPacket` (Task 2); `FramePacketizer` (Task 3, tests only).
- Produces:
  - `public sealed record AssembledFrame(uint Number, bool Keyframe, byte[] Data)`.
  - `public readonly record struct VideoReceiveStats(long PacketsReceived, long FramesCompleted, long FramesLost, long DataShardsExpected, long DataShardsMissing, long ShardsRecovered)` with `double LossPercent`.
  - `public sealed class FrameAssembler` with `const int MaxPendingFrames = 8`, `static readonly TimeSpan FrameTimeout` (250 ms), ctor `(Action<uint>? onLost = null)`, `AssembledFrame? Add(ReadOnlySpan<byte> packet, TimeSpan now)`, `void AbandonStale(TimeSpan now)`, `VideoReceiveStats Stats`, `int PendingFrames`. Not thread-safe.

Rules the implementation follows:
- A frame is delivered as soon as every block has k shards (FEC rebuilds missing data shards).
- When frame N is delivered, every older unfinished frame is given up (`onLost`), and packets for N or older are ignored from then on (late).
- A frame with no new packet for 250 ms is given up by `AbandonStale`; more than 8 unfinished frames gives up the oldest.
- A new stream id clears everything (the host restarted its stream), with no loss reported.
- Loss statistics count **data** shards: missing data shards of finished frames over data shards expected. Parity shards that arrive after a frame is delivered are not tracked, so counting them would overstate loss.

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/FrameAssemblerTests.cs`:
```csharp
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class FrameAssemblerTests
{
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(1);

    /// <summary>A random frame and its packets (24 000 bytes = 20 data + 4 parity shards).</summary>
    private static (byte[] Frame, List<byte[]> Packets) Make(uint number, int length, bool keyframe = false, ushort stream = 1)
    {
        var frame = new byte[length];
        new Random((int)number + length).NextBytes(frame);
        return (frame, new FramePacketizer(stream).Packetize(number, frame, keyframe));
    }

    private static List<AssembledFrame> Feed(FrameAssembler assembler, IEnumerable<byte[]> packets, TimeSpan? now = null)
    {
        var done = new List<AssembledFrame>();
        foreach (var packet in packets)
            if (assembler.Add(packet, now ?? T0) is { } frame)
                done.Add(frame);
        return done;
    }

    private static byte[] Rewrite(byte[] packet, Func<VideoShardHeader, VideoShardHeader> change)
    {
        var copy = (byte[])packet.Clone();
        Assert.True(VideoShardPacket.TryParse(copy, out var header));
        VideoShardPacket.WriteHeader(copy, change(header));
        return copy;
    }

    [Fact]
    public void Complete_frame_is_delivered_once_with_the_right_bytes()
    {
        var (frame, packets) = Make(1, 24_000, keyframe: true);
        var a = new FrameAssembler();

        var done = Assert.Single(Feed(a, packets));

        Assert.Equal(1u, done.Number);
        Assert.True(done.Keyframe);
        Assert.Equal(frame, done.Data);
        Assert.Equal(new VideoReceiveStats(
            PacketsReceived: 24, FramesCompleted: 1, FramesLost: 0,
            DataShardsExpected: 20, DataShardsMissing: 0, ShardsRecovered: 0), a.Stats);
        Assert.Equal(0, a.PendingFrames);
    }

    [Fact]
    public void Packets_in_any_order_give_the_same_frame()
    {
        var (frame, packets) = Make(1, 24_000);
        packets.Reverse();
        Assert.Equal(frame, Assert.Single(Feed(new FrameAssembler(), packets)).Data);
    }

    [Fact]
    public void Lost_data_shards_are_repaired_up_to_the_parity_count()
    {
        var (frame, packets) = Make(1, 24_000); // 20 data + 4 parity
        var a = new FrameAssembler();
        var kept = packets.Where((_, i) => i is not (0 or 5 or 10 or 19)).ToList();

        Assert.Equal(frame, Assert.Single(Feed(a, kept)).Data);
        Assert.Equal(4, a.Stats.ShardsRecovered);
        Assert.Equal(4, a.Stats.DataShardsMissing);
        Assert.Equal(20.0, a.Stats.LossPercent);
    }

    [Fact]
    public void Multi_block_keyframe_is_repaired_in_every_block()
    {
        var (frame, packets) = Make(1, 300_000, keyframe: true); // 250 data shards: 2 blocks of 125 + 25
        var a = new FrameAssembler();
        var kept = packets.Where((_, i) => i % 7 != 3).ToList(); // 21 and 22 packets lost per block

        Assert.Equal(frame, Assert.Single(Feed(a, kept)).Data);
        Assert.True(a.Stats.ShardsRecovered > 0);
    }

    [Fact]
    public void Frame_with_too_many_losses_is_given_up_when_a_newer_frame_completes()
    {
        var (_, first) = Make(1, 24_000);
        var (second, next) = Make(2, 24_000);
        var lost = new List<uint>();
        var a = new FrameAssembler(lost.Add);

        Assert.Empty(Feed(a, first.Skip(5))); // 5 data shards missing, only 4 parity
        Assert.Empty(lost);
        Assert.Equal(second, Assert.Single(Feed(a, next)).Data);

        Assert.Equal(new[] { 1u }, lost);
        Assert.Equal(1, a.Stats.FramesLost);
        Assert.Equal(0, a.PendingFrames);
    }

    [Fact]
    public void Duplicates_and_packets_of_finished_frames_are_ignored()
    {
        var (_, first) = Make(1, 24_000);
        var (_, second) = Make(2, 24_000);
        var a = new FrameAssembler();

        Assert.Single(Feed(a, second.Concat(second)));
        Assert.Empty(Feed(a, first)); // older than the delivered frame 2
        Assert.Equal(0, a.PendingFrames);
    }

    [Fact]
    public void Unfinished_frame_is_given_up_after_the_timeout()
    {
        var (_, packets) = Make(1, 24_000);
        var lost = new List<uint>();
        var a = new FrameAssembler(lost.Add);

        Feed(a, packets.Take(10), T0);
        a.AbandonStale(T0 + FrameAssembler.FrameTimeout - TimeSpan.FromMilliseconds(1));
        Assert.Empty(lost);
        a.AbandonStale(T0 + FrameAssembler.FrameTimeout);
        Assert.Equal(new[] { 1u }, lost);

        Assert.Empty(Feed(a, packets.Skip(10), T0 + FrameAssembler.FrameTimeout)); // too late now
    }

    [Fact]
    public void At_most_MaxPendingFrames_unfinished_frames_are_kept()
    {
        var lost = new List<uint>();
        var a = new FrameAssembler(lost.Add);

        for (uint n = 1; n <= FrameAssembler.MaxPendingFrames + 1; n++)
            Feed(a, Make(n, 24_000).Packets.Take(1));

        Assert.Equal(FrameAssembler.MaxPendingFrames, a.PendingFrames);
        Assert.Equal(new[] { 1u }, lost);
    }

    [Fact]
    public void Packets_that_disagree_with_their_frame_are_ignored()
    {
        var (frame, packets) = Make(1, 24_000);
        var a = new FrameAssembler();

        Feed(a, packets.Take(1));
        Assert.Empty(Feed(a,
        [
            Rewrite(packets[1], h => h with { FrameLength = 99 }),
            Rewrite(packets[2], h => h with { DataShards = 19 }),
            Rewrite(packets[3], h => h with { Keyframe = true }),
        ]));

        Assert.Equal(frame, Assert.Single(Feed(a, packets.Skip(1))).Data);
    }

    [Fact]
    public void Frame_whose_length_does_not_fit_its_shards_is_dropped()
    {
        var (_, packets) = Make(1, 100); // 1 data shard can't hold 5000 bytes
        var lost = new List<uint>();
        var a = new FrameAssembler(lost.Add);

        Assert.Empty(Feed(a, packets.Select(p => Rewrite(p, h => h with { FrameLength = 5_000 }))));
        Assert.Equal(new[] { 1u }, lost);
    }

    [Fact]
    public void Frame_numbers_wrap_around()
    {
        var a = new FrameAssembler();
        Assert.Single(Feed(a, Make(uint.MaxValue, 5_000).Packets));
        Assert.Single(Feed(a, Make(0, 5_000).Packets));
        Assert.Equal(0, a.Stats.FramesLost);
    }

    [Fact]
    public void A_new_stream_id_starts_over_even_with_lower_frame_numbers()
    {
        var lost = new List<uint>();
        var a = new FrameAssembler(lost.Add);
        Assert.Single(Feed(a, Make(50_000, 5_000, stream: 1).Packets));
        Feed(a, Make(50_001, 5_000, stream: 1).Packets.Take(1)); // unfinished frame of the old stream

        var first = Assert.Single(Feed(a, Make(0, 5_000, keyframe: true, stream: 2).Packets));

        Assert.Equal(0u, first.Number);
        Assert.Empty(lost);
        Assert.Equal(0, a.PendingFrames);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~FrameAssemblerTests`
Expected: build FAILS: `FrameAssembler`, `AssembledFrame`, `VideoReceiveStats` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Video/FrameAssembler.cs`:
```csharp
using CouchLink.Core.Fec;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Video;

/// <summary>One complete encoded frame, ready for the decoder.</summary>
public sealed record AssembledFrame(uint Number, bool Keyframe, byte[] Data);

/// <summary>
/// Receive counters for the F2 overlay. Loss is measured on data shards: parity shards that
/// arrive after a frame is already delivered are not tracked, so counting them would overstate loss.
/// </summary>
public readonly record struct VideoReceiveStats(
    long PacketsReceived,
    long FramesCompleted,
    long FramesLost,
    long DataShardsExpected,
    long DataShardsMissing,
    long ShardsRecovered)
{
    public double LossPercent => DataShardsExpected == 0 ? 0 : 100.0 * DataShardsMissing / DataShardsExpected;
}

/// <summary>
/// Client side: collects shard datagrams into frames and repairs missing data shards with FEC.
/// A frame is delivered as soon as every block has enough shards. Delivering frame N gives up
/// every older unfinished frame; packets for N or older are ignored from then on. A frame with
/// no new packet for <see cref="FrameTimeout"/> is given up too, so a loss is noticed even when
/// no newer frame follows. A new stream id (host restarted streaming) clears everything.
/// Not thread-safe.
/// </summary>
public sealed class FrameAssembler
{
    public const int MaxPendingFrames = 8;
    public static readonly TimeSpan FrameTimeout = TimeSpan.FromMilliseconds(250);

    private sealed class Block(int dataShards, int parityShards)
    {
        public int DataShards { get; } = dataShards;
        public int ParityShards { get; } = parityShards;
        public byte[]?[] Shards { get; } = new byte[]?[dataShards + parityShards];
        public int Received { get; set; }
        public bool Done { get; set; }
        public int Rebuilt { get; set; }

        public int DataPresent()
        {
            int n = 0;
            for (int i = 0; i < DataShards; i++)
                if (Shards[i] is not null)
                    n++;
            return n;
        }
    }

    private sealed class PendingFrame(VideoShardHeader first, TimeSpan now)
    {
        public uint Number { get; } = first.Frame;
        public bool Keyframe { get; } = first.Keyframe;
        public uint Length { get; } = first.FrameLength;
        public Block?[] Blocks { get; } = new Block?[first.BlockCount];
        public int BlocksDone { get; set; }
        public TimeSpan LastPacket { get; set; } = now;
    }

    private readonly Action<uint>? _onLost;
    private readonly Dictionary<uint, PendingFrame> _pending = [];
    private ushort? _streamId;
    private uint? _lastFinished;
    private long _packets, _completed, _lost, _dataExpected, _dataMissing, _recovered;

    public FrameAssembler(Action<uint>? onLost = null) => _onLost = onLost;

    public VideoReceiveStats Stats => new(_packets, _completed, _lost, _dataExpected, _dataMissing, _recovered);

    public int PendingFrames => _pending.Count;

    /// <summary>Adds one datagram. Returns the frame it completed, or null.</summary>
    public AssembledFrame? Add(ReadOnlySpan<byte> packet, TimeSpan now)
    {
        if (!VideoShardPacket.TryParse(packet, out var h))
            return null;
        _packets++;

        if (_streamId != h.StreamId)
        {
            _streamId = h.StreamId;
            _pending.Clear();
            _lastFinished = null;
        }
        if (IsFinished(h.Frame))
            return null; // late: already delivered or given up

        if (!_pending.TryGetValue(h.Frame, out var frame))
        {
            if (_pending.Count >= MaxPendingFrames)
            {
                GiveUp(OldestFirst(_pending.Values)[0]);
                if (IsFinished(h.Frame))
                    return null; // this packet's frame was even older
            }
            frame = new PendingFrame(h, now);
            _pending[h.Frame] = frame;
        }
        else if (h.Keyframe != frame.Keyframe || h.FrameLength != frame.Length || h.BlockCount != frame.Blocks.Length)
        {
            return null; // disagrees with the frame's first packet
        }

        var block = frame.Blocks[h.Block] ??= new Block(h.DataShards, h.ParityShards);
        if (block.DataShards != h.DataShards || block.ParityShards != h.ParityShards)
            return null;
        if (block.Done || block.Shards[h.Shard] is not null)
            return null; // block already complete, or a duplicate

        block.Shards[h.Shard] = VideoShardPacket.Payload(packet).ToArray();
        block.Received++;
        frame.LastPacket = now;

        if (block.Received < block.DataShards)
            return null;
        if (!CompleteBlock(block))
        {
            GiveUp(frame);
            return null;
        }
        if (++frame.BlocksDone < frame.Blocks.Length)
            return null;
        return Finish(frame);
    }

    /// <summary>Gives up frames that have had no new packet for <see cref="FrameTimeout"/>.</summary>
    public void AbandonStale(TimeSpan now)
    {
        foreach (var frame in OldestFirst(_pending.Values.Where(f => now - f.LastPacket >= FrameTimeout)))
            GiveUp(frame);
    }

    private bool IsFinished(uint frame) =>
        _lastFinished is { } last && unchecked((int)(frame - last)) <= 0;

    private bool CompleteBlock(Block block)
    {
        if (block.DataPresent() < block.DataShards)
        {
            var shards = new Memory<byte>[block.Shards.Length];
            var present = new bool[block.Shards.Length];
            for (int i = 0; i < shards.Length; i++)
            {
                present[i] = block.Shards[i] is not null;
                shards[i] = block.Shards[i] ??= new byte[VideoShardPacket.PayloadSize];
            }
            if (!ReedSolomon.For(block.DataShards, block.ParityShards).Reconstruct(shards, present, out int rebuilt))
                return false;
            block.Rebuilt = rebuilt;
            _recovered += rebuilt;
        }
        block.Done = true;
        return true;
    }

    private AssembledFrame? Finish(PendingFrame frame)
    {
        long dataShards = frame.Blocks.Sum(b => b!.DataShards);
        long capacity = dataShards * VideoShardPacket.PayloadSize;
        if (frame.Length > capacity || frame.Length <= capacity - VideoShardPacket.PayloadSize)
        {
            GiveUp(frame); // the header's length doesn't match the shards
            return null;
        }

        var data = new byte[frame.Length];
        int offset = 0;
        foreach (var block in frame.Blocks)
            for (int i = 0; i < block!.DataShards && offset < data.Length; i++)
            {
                int n = Math.Min(VideoShardPacket.PayloadSize, data.Length - offset);
                block.Shards[i].AsSpan(0, n).CopyTo(data.AsSpan(offset));
                offset += n;
            }

        foreach (var older in OldestFirst(_pending.Values.Where(p => unchecked((int)(p.Number - frame.Number)) < 0)))
            GiveUp(older);
        _pending.Remove(frame.Number);
        Account(frame);
        _completed++;
        _lastFinished = frame.Number;
        return new AssembledFrame(frame.Number, frame.Keyframe, data);
    }

    private void GiveUp(PendingFrame frame)
    {
        _pending.Remove(frame.Number);
        Account(frame);
        _lost++;
        if (!IsFinished(frame.Number))
            _lastFinished = frame.Number;
        _onLost?.Invoke(frame.Number);
    }

    private void Account(PendingFrame frame)
    {
        int knownDataShards = frame.Blocks.FirstOrDefault(b => b is not null)?.DataShards ?? 0;
        foreach (var block in frame.Blocks)
        {
            if (block is null)
            {
                // Not one packet of this block arrived; assume it was the size of the others.
                _dataExpected += knownDataShards;
                _dataMissing += knownDataShards;
                continue;
            }
            _dataExpected += block.DataShards;
            _dataMissing += block.Done ? block.Rebuilt : block.DataShards - block.DataPresent();
        }
    }

    /// <summary>Sorts frames oldest first, treating frame numbers as wrapping around.</summary>
    private static List<PendingFrame> OldestFirst(IEnumerable<PendingFrame> frames)
    {
        var list = frames.ToList();
        if (list.Count > 1)
        {
            uint reference = list[0].Number;
            list.Sort((x, y) => unchecked((int)(x.Number - reference)).CompareTo(unchecked((int)(y.Number - reference))));
        }
        return list;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~FrameAssemblerTests`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Core/Video/FrameAssembler.cs tests/CouchLink.Core.Tests/FrameAssemblerTests.cs
git commit -m "feat(core): reassemble and repair frames from shard packets"
```

---

### Task 5: Decode gate and simulated packet loss

**Files:**
- Create: `src/CouchLink.Core/Video/DecodeGate.cs`
- Test: `tests/CouchLink.Core.Tests/DecodeGateTests.cs`
- Test: `tests/CouchLink.Core.Tests/StreamLossTests.cs`

**Interfaces:**
- Consumes: `AssembledFrame`, `FrameAssembler`, `VideoReceiveStats` (Task 4); `FramePacketizer` (Task 3, tests only).
- Produces: `public sealed class DecodeGate` with `static readonly TimeSpan RequestInterval` (300 ms), `bool WaitingForKeyframe` (starts true), `long FramesSkipped`, `long KeyframeRequests`, `bool Accept(AssembledFrame frame)`, `void FrameLost(uint number)` (matches `FrameAssembler`'s `onLost`), `bool ShouldRequestKeyframe(TimeSpan now)`. Not thread-safe.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/DecodeGateTests.cs`:
```csharp
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class DecodeGateTests
{
    private static AssembledFrame F(uint number, bool keyframe = false) => new(number, keyframe, []);
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Starts_waiting_and_skips_delta_frames_until_a_keyframe()
    {
        var gate = new DecodeGate();
        Assert.True(gate.WaitingForKeyframe);
        Assert.False(gate.Accept(F(5)));
        Assert.True(gate.Accept(F(6, keyframe: true)));
        Assert.True(gate.Accept(F(7)));
        Assert.False(gate.WaitingForKeyframe);
        Assert.Equal(1, gate.FramesSkipped);
    }

    [Fact]
    public void A_gap_in_frame_numbers_waits_for_the_next_keyframe()
    {
        var gate = new DecodeGate();
        Assert.True(gate.Accept(F(1, keyframe: true)));
        Assert.True(gate.Accept(F(2)));
        Assert.False(gate.Accept(F(4))); // frame 3 never arrived at all
        Assert.False(gate.Accept(F(5)));
        Assert.True(gate.Accept(F(6, keyframe: true)));
        Assert.True(gate.Accept(F(7)));
    }

    [Fact]
    public void A_lost_frame_waits_for_the_next_keyframe()
    {
        var gate = new DecodeGate();
        gate.Accept(F(1, keyframe: true));
        gate.Accept(F(2));
        gate.FrameLost(3);
        Assert.True(gate.WaitingForKeyframe);
        Assert.False(gate.Accept(F(4)));
        Assert.True(gate.Accept(F(5, keyframe: true)));
    }

    [Fact]
    public void A_lost_frame_older_than_the_last_delivered_is_ignored()
    {
        var gate = new DecodeGate();
        gate.Accept(F(1, keyframe: true));
        gate.Accept(F(2));
        gate.Accept(F(3));
        gate.FrameLost(2);
        Assert.True(gate.Accept(F(4)));
    }

    [Fact]
    public void Keyframe_requests_repeat_every_interval_while_waiting()
    {
        var gate = new DecodeGate();
        Assert.True(gate.ShouldRequestKeyframe(Ms(0)));
        Assert.False(gate.ShouldRequestKeyframe(Ms(299)));
        Assert.True(gate.ShouldRequestKeyframe(Ms(300)));
        gate.Accept(F(1, keyframe: true));
        Assert.False(gate.ShouldRequestKeyframe(Ms(1000)));
        Assert.Equal(2, gate.KeyframeRequests);

        gate.FrameLost(2);
        Assert.True(gate.ShouldRequestKeyframe(Ms(1001))); // a new loss asks right away
    }

    [Fact]
    public void Frame_numbers_wrap_around()
    {
        var gate = new DecodeGate();
        Assert.True(gate.Accept(F(uint.MaxValue, keyframe: true)));
        Assert.True(gate.Accept(F(0)));
    }
}
```

`tests/CouchLink.Core.Tests/StreamLossTests.cs`:
```csharp
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

/// <summary>
/// Spec section 9: FEC repair under simulated 1-20% loss. Runs 10 s of 60 fps video through
/// packetizer -> random packet loss -> assembler -> gate, with the host answering keyframe
/// requests on the next frame. Seeds are fixed, so results are repeatable.
/// </summary>
public class StreamLossTests
{
    private const int Frames = 600;
    private static readonly TimeSpan FrameTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private sealed record Result(int Decoded, int Corrupt, VideoReceiveStats Stats);

    private static Result Run(double loss, int seed)
    {
        var rng = new Random(seed);
        var packetizer = new FramePacketizer(streamId: 1);
        var gate = new DecodeGate();
        var assembler = new FrameAssembler(gate.FrameLost);
        var sent = new Dictionary<uint, byte[]>();
        bool keyframeWanted = true;
        int decoded = 0, corrupt = 0;

        for (uint n = 0; n < Frames; n++)
        {
            var now = FrameTime * n;
            bool keyframe = keyframeWanted;
            keyframeWanted = false;
            var frame = new byte[keyframe ? 150_000 : 15_000 + rng.Next(15_000)];
            rng.NextBytes(frame);
            sent[n] = frame;

            foreach (var packet in packetizer.Packetize(n, frame, keyframe))
            {
                if (rng.NextDouble() < loss)
                    continue;
                if (assembler.Add(packet, now) is { } done && gate.Accept(done))
                {
                    decoded++;
                    if (!done.Data.AsSpan().SequenceEqual(sent[done.Number]))
                        corrupt++;
                }
            }
            assembler.AbandonStale(now + FrameTime);
            if (gate.ShouldRequestKeyframe(now + FrameTime))
                keyframeWanted = true;
        }
        return new Result(decoded, corrupt, assembler.Stats);
    }

    [Fact]
    public void No_loss_decodes_every_frame()
    {
        var result = Run(0, seed: 1);
        Assert.Equal(Frames, result.Decoded);
        Assert.Equal(0, result.Stats.FramesLost);
    }

    [Theory]
    [InlineData(0.01, 0.95)]
    [InlineData(0.05, 0.85)]
    [InlineData(0.10, 0.40)]
    public void Most_frames_survive_random_loss(double loss, double minDecoded)
    {
        var result = Run(loss, seed: 1234);
        Assert.Equal(0, result.Corrupt);
        Assert.True(result.Decoded >= minDecoded * Frames, $"decoded {result.Decoded} of {Frames} at {loss:P0} loss");
        Assert.True(result.Stats.ShardsRecovered > 0);
    }

    [Fact]
    public void Twenty_percent_loss_never_shows_a_corrupt_frame()
    {
        var result = Run(0.20, seed: 1234);
        Assert.Equal(0, result.Corrupt);
        Assert.True(result.Stats.ShardsRecovered > 0);
    }

    [Fact]
    public void Measured_loss_matches_the_simulated_loss()
    {
        Assert.InRange(Run(0.05, seed: 99).Stats.LossPercent, 3.5, 6.5);
    }
}
```

The thresholds come from the binomial odds of losing more than 20% of a frame's ~24 packets: about 1 frame in 200 fails at 5% loss, and about 1 in 12 at 10%. Each failure costs that frame plus the frames until the next keyframe arrives. At 20% loss most frames fail, so that test only demands that nothing corrupt is ever shown. If a threshold fails, find out why before changing it.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~DecodeGateTests|FullyQualifiedName~StreamLossTests"`
Expected: build FAILS: `DecodeGate` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Video/DecodeGate.cs`:
```csharp
namespace CouchLink.Core.Video;

/// <summary>
/// Client side: decides which assembled frames may go to the decoder. An H.264 delta frame
/// needs the frame before it, so after any loss (a frame given up, or a gap in frame numbers)
/// the gate drops frames until a keyframe arrives. It asks the host for one at once and then
/// every <see cref="RequestInterval"/> until it comes. It starts out waiting, because a client
/// joining mid-stream needs a keyframe first. Not thread-safe.
/// </summary>
public sealed class DecodeGate
{
    public static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(300);

    private uint? _lastDelivered;
    private TimeSpan? _lastRequest;

    public bool WaitingForKeyframe { get; private set; } = true;
    public long FramesSkipped { get; private set; }
    public long KeyframeRequests { get; private set; }

    /// <summary>Returns true if the frame should be decoded.</summary>
    public bool Accept(AssembledFrame frame)
    {
        if (frame.Keyframe)
        {
            WaitingForKeyframe = false;
            _lastRequest = null;
        }
        else if (_lastDelivered is { } last && frame.Number != unchecked(last + 1))
        {
            WaitingForKeyframe = true; // a frame we never heard of is missing
        }

        if (WaitingForKeyframe)
        {
            FramesSkipped++;
            return false;
        }
        _lastDelivered = frame.Number;
        return true;
    }

    /// <summary>The assembler gave up on a frame; everything after it is undecodable until a keyframe.</summary>
    public void FrameLost(uint number)
    {
        if (_lastDelivered is { } last && unchecked((int)(number - last)) <= 0)
            return; // older than what we already showed
        WaitingForKeyframe = true;
    }

    /// <summary>True when a keyframe request should be sent now.</summary>
    public bool ShouldRequestKeyframe(TimeSpan now)
    {
        if (!WaitingForKeyframe)
            return false;
        if (_lastRequest is { } sent && now - sent < RequestInterval)
            return false;
        _lastRequest = now;
        KeyframeRequests++;
        return true;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~DecodeGateTests|FullyQualifiedName~StreamLossTests"`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Core/Video/DecodeGate.cs tests/CouchLink.Core.Tests/DecodeGateTests.cs tests/CouchLink.Core.Tests/StreamLossTests.cs
git commit -m "feat(core): drop undecodable frames and request keyframes after loss"
```

---

### Task 6: Host keyframe policy and stream targets

**Files:**
- Create: `src/CouchLink.Core/Video/KeyframePolicy.cs`
- Create: `src/CouchLink.Core/Video/StreamTargets.cs`
- Test: `tests/CouchLink.Core.Tests/KeyframePolicyTests.cs`
- Test: `tests/CouchLink.Core.Tests/StreamTargetsTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `public sealed class KeyframePolicy` with `static readonly TimeSpan MinInterval` (250 ms), `void Request()`, `bool ShouldForce(TimeSpan now)`, `void KeyframeSent(TimeSpan now)`. Not thread-safe.
  - `public sealed class StreamTargets(int videoPort)` with `static readonly TimeSpan Timeout` (5 s), `bool Seen(byte slot, IPAddress address, TimeSpan now)` (true = new, moved, or back after a timeout: needs a keyframe), `IReadOnlyList<IPEndPoint> Current(TimeSpan now)`. Not thread-safe.

- [ ] **Step 1: Write the failing tests**

`tests/CouchLink.Core.Tests/KeyframePolicyTests.cs`:
```csharp
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class KeyframePolicyTests
{
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Nothing_is_forced_without_a_request()
    {
        Assert.False(new KeyframePolicy().ShouldForce(Ms(0)));
    }

    [Fact]
    public void A_request_forces_keyframes_until_one_is_sent()
    {
        var policy = new KeyframePolicy();
        policy.Request();
        Assert.True(policy.ShouldForce(Ms(0)));
        Assert.True(policy.ShouldForce(Ms(10))); // the encoder had no frame yet; still pending
        policy.KeyframeSent(Ms(10));
        Assert.False(policy.ShouldForce(Ms(20)));
    }

    [Fact]
    public void Keyframes_are_at_least_MinInterval_apart()
    {
        var policy = new KeyframePolicy();
        policy.Request();
        policy.KeyframeSent(Ms(100));
        policy.Request();
        Assert.False(policy.ShouldForce(Ms(349)));
        Assert.True(policy.ShouldForce(Ms(350)));
    }

    [Fact]
    public void Many_requests_cost_one_keyframe()
    {
        var policy = new KeyframePolicy();
        for (int client = 0; client < 9; client++)
            policy.Request();
        Assert.True(policy.ShouldForce(Ms(0)));
        policy.KeyframeSent(Ms(0));
        Assert.False(policy.ShouldForce(Ms(1)));
    }

    [Fact]
    public void An_unrequested_keyframe_also_counts()
    {
        var policy = new KeyframePolicy();
        policy.KeyframeSent(Ms(0)); // e.g. the encoder's first frame
        policy.Request();
        Assert.False(policy.ShouldForce(Ms(100)));
        Assert.True(policy.ShouldForce(Ms(250)));
    }
}
```

`tests/CouchLink.Core.Tests/StreamTargetsTests.cs`:
```csharp
using System.Net;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class StreamTargetsTests
{
    private const int VideoPort = 47802;
    private static readonly IPAddress A = IPAddress.Parse("192.168.1.20");
    private static readonly IPAddress B = IPAddress.Parse("192.168.1.21");
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void New_client_needs_a_keyframe_and_known_client_does_not()
    {
        var targets = new StreamTargets(VideoPort);
        Assert.True(targets.Seen(2, A, Ms(0)));
        Assert.False(targets.Seen(2, A, Ms(8)));
    }

    [Fact]
    public void Client_gets_video_on_its_address_and_the_video_port()
    {
        var targets = new StreamTargets(VideoPort);
        targets.Seen(2, A, Ms(0));
        Assert.Equal(new IPEndPoint(A, VideoPort), Assert.Single(targets.Current(Ms(0))));
    }

    [Fact]
    public void Client_that_moves_to_another_address_needs_a_keyframe()
    {
        var targets = new StreamTargets(VideoPort);
        targets.Seen(2, A, Ms(0));
        Assert.True(targets.Seen(2, B, Ms(8)));
        Assert.Equal(new IPEndPoint(B, VideoPort), Assert.Single(targets.Current(Ms(8))));
    }

    [Fact]
    public void Silent_client_is_dropped_after_the_timeout_and_is_new_when_it_returns()
    {
        var targets = new StreamTargets(VideoPort);
        targets.Seen(2, A, Ms(0));
        Assert.Single(targets.Current(StreamTargets.Timeout - Ms(1)));
        Assert.Empty(targets.Current(StreamTargets.Timeout));
        Assert.True(targets.Seen(2, A, StreamTargets.Timeout + Ms(1)));
    }

    [Fact]
    public void Silent_client_counts_as_new_even_before_Current_runs()
    {
        var targets = new StreamTargets(VideoPort);
        targets.Seen(2, A, Ms(0));
        Assert.True(targets.Seen(2, A, StreamTargets.Timeout));
    }

    [Fact]
    public void Two_slots_on_one_PC_get_one_copy()
    {
        var targets = new StreamTargets(VideoPort);
        targets.Seen(2, A, Ms(0));
        targets.Seen(3, A, Ms(0));
        Assert.Single(targets.Current(Ms(0)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~KeyframePolicyTests|FullyQualifiedName~StreamTargetsTests"`
Expected: build FAILS: `KeyframePolicy`, `StreamTargets` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Video/KeyframePolicy.cs`:
```csharp
namespace CouchLink.Core.Video;

/// <summary>
/// Host side: when to force a keyframe. A request (a client joined, or lost a frame) stays
/// pending until the encoder actually produces a keyframe, but keyframes are at least
/// <see cref="MinInterval"/> apart, so nine clients asking at once cost one keyframe.
/// Not thread-safe.
/// </summary>
public sealed class KeyframePolicy
{
    public static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(250);

    private bool _pending;
    private TimeSpan? _lastKeyframe;

    public void Request() => _pending = true;

    public bool ShouldForce(TimeSpan now) =>
        _pending && (_lastKeyframe is not { } last || now - last >= MinInterval);

    /// <summary>Call for every keyframe sent, requested or not.</summary>
    public void KeyframeSent(TimeSpan now)
    {
        _pending = false;
        _lastKeyframe = now;
    }
}
```

`src/CouchLink.Core/Video/StreamTargets.cs`:
```csharp
using System.Net;

namespace CouchLink.Core.Video;

/// <summary>
/// Host side: the clients to stream to, learned from their input packets (slot -> address).
/// A client silent for <see cref="Timeout"/> is dropped. Until the v1.4 session channel this is
/// how the host knows who joined. Not thread-safe.
/// </summary>
public sealed class StreamTargets(int videoPort)
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly Dictionary<byte, (IPEndPoint EndPoint, TimeSpan LastSeen)> _targets = [];

    /// <summary>Records that a client was heard from. Returns true if it is new, moved, or back after going silent, so it needs a keyframe.</summary>
    public bool Seen(byte slot, IPAddress address, TimeSpan now)
    {
        bool isNew = !_targets.TryGetValue(slot, out var known)
            || !known.EndPoint.Address.Equals(address)
            || now - known.LastSeen >= Timeout;
        _targets[slot] = (isNew ? new IPEndPoint(address, videoPort) : known.EndPoint, now);
        return isNew;
    }

    /// <summary>Clients heard from within <see cref="Timeout"/>, one entry per endpoint.</summary>
    public IReadOnlyList<IPEndPoint> Current(TimeSpan now)
    {
        foreach (var slot in _targets.Where(t => now - t.Value.LastSeen >= Timeout).Select(t => t.Key).ToList())
            _targets.Remove(slot);
        return _targets.Values.Select(t => t.EndPoint).Distinct().ToList();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~KeyframePolicyTests|FullyQualifiedName~StreamTargetsTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Core/Video/KeyframePolicy.cs src/CouchLink.Core/Video/StreamTargets.cs tests/CouchLink.Core.Tests/KeyframePolicyTests.cs tests/CouchLink.Core.Tests/StreamTargetsTests.cs
git commit -m "feat(core): host keyframe pacing and stream targets"
```

---

### Task 7: UDP transport for video and keyframe requests

**Files:**
- Modify: `src/CouchLink.Core/Net/Ports.cs`
- Create: `src/CouchLink.Core/Net/UdpReceiveLoop.cs`
- Modify: `src/CouchLink.Core/Net/InputReceiver.cs`
- Modify: `src/CouchLink.Core/Net/InputSender.cs`
- Create: `src/CouchLink.Core/Net/VideoSender.cs`
- Create: `src/CouchLink.Core/Net/VideoReceiver.cs`
- Modify: `src/CouchLink.App/HostInputService.cs` (new `RunAsync` callback shape only)
- Modify: `tests/CouchLink.Core.Tests/UdpInputTests.cs`
- Test: `tests/CouchLink.Core.Tests/UdpVideoTests.cs`

**Interfaces:**
- Consumes: `KeyframeRequest` (Task 2); `FramePacketizer` (Task 3) and `FrameAssembler` (Task 4) in tests.
- Produces:
  - `Ports.Video = 47802`.
  - `InputReceiver.RunAsync(Action<InputPacket, IPAddress> onPacket, CancellationToken ct, Action<Exception>? onError = null, Action<KeyframeRequest, IPAddress>? onKeyframeRequest = null)`.
  - `InputSender.SendKeyframeRequest()`, safe to call from any thread.
  - `public interface IVideoPacketSender : IDisposable` with `void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets)`; `public sealed class VideoSender : IVideoPacketSender` (ctor `()`).
  - `public sealed class VideoReceiver : IDisposable` with `const int ReceiveBufferBytes = 8 MB`, ctor `(int port)`, `static bool TryCreate(int port, out VideoReceiver? receiver, out string? error)`, `int LocalPort`, `int ReceiveBufferSize`, `Task RunAsync(Action<byte[]> onDatagram, CancellationToken ct, Action<Exception>? onError = null)`.

- [ ] **Step 1: Write the failing test and update the input tests**

`tests/CouchLink.Core.Tests/UdpVideoTests.cs`:
```csharp
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using CouchLink.Core.Input;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class UdpVideoTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_keyframe_burst_arrives_complete()
    {
        using var receiver = new VideoReceiver(port: 0);
        Assert.Equal(VideoReceiver.ReceiveBufferBytes, receiver.ReceiveBufferSize);

        var frame = new byte[300_000];
        new Random(1).NextBytes(frame);
        var packets = new FramePacketizer(1).Packetize(1, frame, keyframe: true); // 300 datagrams
        var assembler = new FrameAssembler();
        var done = new TaskCompletionSource<AssembledFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        int received = 0;
        using var cts = new CancellationTokenSource();
        var loop = receiver.RunAsync(datagram =>
        {
            Interlocked.Increment(ref received);
            if (assembler.Add(datagram, TimeSpan.Zero) is { } f)
                done.TrySetResult(f);
        }, cts.Token);

        using (var sender = new VideoSender())
            sender.Send(packets, [new IPEndPoint(IPAddress.Loopback, receiver.LocalPort)]);

        Assert.Equal(frame, (await done.Task.WaitAsync(Timeout)).Data);
        var deadline = DateTime.UtcNow + Timeout;
        while (Volatile.Read(ref received) < packets.Count && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Equal(packets.Count, Volatile.Read(ref received)); // nothing dropped by the socket buffer

        cts.Cancel();
        await loop.WaitAsync(Timeout);
    }

    [Fact]
    public void TryCreate_reports_a_port_that_is_already_in_use()
    {
        using var taken = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)taken.Client.LocalEndPoint!).Port;

        Assert.False(VideoReceiver.TryCreate(port, out var receiver, out var error));
        Assert.Null(receiver);
        Assert.Equal($"UDP port {port} is already in use. Is CouchLink already joined to a host on this PC?", error);
    }

    [Fact]
    public async Task Keyframe_requests_and_input_both_reach_the_host_with_the_sender_address()
    {
        using var receiver = new InputReceiver(port: 0);
        var inputs = Channel.CreateUnbounded<(InputPacket Packet, IPAddress From)>();
        var requests = Channel.CreateUnbounded<(KeyframeRequest Request, IPAddress From)>();
        using var cts = new CancellationTokenSource();
        var loop = receiver.RunAsync(
            (p, from) => inputs.Writer.TryWrite((p, from)),
            cts.Token,
            onKeyframeRequest: (r, from) => requests.Writer.TryWrite((r, from)));

        using var sender = new InputSender(new IPEndPoint(IPAddress.Loopback, receiver.LocalPort), slot: 6);
        sender.SendKeyframeRequest();
        sender.Send(PadState.Neutral);

        using var wait = new CancellationTokenSource(Timeout);
        var request = await requests.Reader.ReadAsync(wait.Token);
        var input = await inputs.Reader.ReadAsync(wait.Token);
        Assert.Equal((byte)6, request.Request.Slot);
        Assert.Equal(IPAddress.Loopback, request.From);
        Assert.Equal((byte)6, input.Packet.Slot);
        Assert.Equal(IPAddress.Loopback, input.From);

        cts.Cancel();
        await loop.WaitAsync(Timeout);
    }
}
```

In `tests/CouchLink.Core.Tests/UdpInputTests.cs`, the `onPacket` callback now also gets the sender's address. Change the two `RunAsync` calls:
```csharp
        var loop = receiver.RunAsync((p, _) => received.Writer.TryWrite(p), cts.Token);
```
and
```csharp
        var loop = receiver.RunAsync(
            (p, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    throw new InvalidOperationException("pad plug-in failed");
                received.Writer.TryWrite(p);
            },
            cts.Token,
            e => errors.Writer.TryWrite(e));
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~UdpVideoTests|FullyQualifiedName~UdpInputTests"`
Expected: build FAILS: `VideoReceiver`, `VideoSender`, `SendKeyframeRequest` not found, and `RunAsync` has no two-argument callback.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Net/Ports.cs`:
```csharp
namespace CouchLink.Core.Net;

public static class Ports
{
    /// <summary>Host -> client video (and audio, from v1.3).</summary>
    public const int Video = 47802;

    /// <summary>Client -> host controller state, and keyframe requests until v1.4.</summary>
    public const int Input = 47803;
}
```

`src/CouchLink.Core/Net/UdpReceiveLoop.cs`:
```csharp
using System.Net.Sockets;

namespace CouchLink.Core.Net;

/// <summary>The receive loop shared by every CouchLink UDP socket.</summary>
internal static class UdpReceiveLoop
{
    /// <summary>
    /// Receives until cancelled. An exception from <paramref name="onDatagram"/> is reported to
    /// <paramref name="onError"/> and the loop keeps going, so one bad datagram never stops the stream.
    /// </summary>
    public static async Task RunAsync(
        UdpClient udp, Action<UdpReceiveResult> onDatagram, CancellationToken ct, Action<Exception>? onError)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await udp.ReceiveAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue; // e.g. ICMP port-unreachable reset on Windows; keep listening
            }

            try
            {
                onDatagram(result);
            }
            catch (Exception e)
            {
                onError?.Invoke(e);
            }
        }
    }
}
```

`src/CouchLink.Core/Net/InputReceiver.cs`: replace `RunAsync` (and its doc comment) with:
```csharp
    /// <summary>
    /// Receives until cancelled, passing each valid input packet and keyframe request on with
    /// the sender's address. An exception from a callback (e.g. a virtual pad failing to plug
    /// in) is reported to <paramref name="onError"/> and the loop keeps going, so one bad
    /// packet never stops input for every player.
    /// </summary>
    public Task RunAsync(
        Action<InputPacket, IPAddress> onPacket,
        CancellationToken ct,
        Action<Exception>? onError = null,
        Action<KeyframeRequest, IPAddress>? onKeyframeRequest = null) =>
        UdpReceiveLoop.RunAsync(_udp, result =>
        {
            var from = result.RemoteEndPoint.Address;
            if (InputPacket.TryParse(result.Buffer, out var packet))
                onPacket(packet, from);
            else if (onKeyframeRequest is not null && KeyframeRequest.TryParse(result.Buffer, out var request))
                onKeyframeRequest(request, from);
        }, ct, onError);
```

`src/CouchLink.Core/Net/InputSender.cs`: add a prebuilt request and the send method:
```csharp
    private readonly byte[] _keyframeRequest = new byte[KeyframeRequest.Size];
```
In the constructor, after `_slot = slot;`:
```csharp
        new KeyframeRequest(slot).WriteTo(_keyframeRequest);
```
And the method, after `Send`:
```csharp
    /// <summary>Asks the host for a keyframe. Safe to call from any thread (the bytes never change).</summary>
    public void SendKeyframeRequest()
    {
        try
        {
            _udp.Send(_keyframeRequest, _keyframeRequest.Length);
        }
        catch (SocketException)
        {
            // Host not reachable right now; the client repeats the request until a keyframe arrives.
        }
    }
```
Leave the rest of the class as it is.

`src/CouchLink.Core/Net/VideoSender.cs`:
```csharp
using System.Net;
using System.Net.Sockets;

namespace CouchLink.Core.Net;

/// <summary>Sends one frame's shard packets to every client.</summary>
public interface IVideoPacketSender : IDisposable
{
    void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets);
}

/// <summary>
/// Host side: one unicast copy of every packet per client. Packets go out packet by packet
/// across clients, so every client gets the frame at about the same time.
/// </summary>
public sealed class VideoSender : IVideoPacketSender
{
    public const int SendBufferBytes = 4 * 1024 * 1024;

    private readonly UdpClient _udp = new(AddressFamily.InterNetwork);

    public VideoSender() => _udp.Client.SendBufferSize = SendBufferBytes;

    public void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets)
    {
        foreach (var packet in packets)
            foreach (var target in targets)
            {
                try
                {
                    _udp.Send(packet, packet.Length, target);
                }
                catch (SocketException)
                {
                    // That client is unreachable right now; it drops out of the targets once its input stops.
                }
            }
    }

    public void Dispose() => _udp.Dispose();
}
```

`src/CouchLink.Core/Net/VideoReceiver.cs`:
```csharp
using System.Net;
using System.Net.Sockets;

namespace CouchLink.Core.Net;

/// <summary>Client side: receives video datagrams (UDP 47802) and hands each one to a callback.</summary>
public sealed class VideoReceiver : IDisposable
{
    /// <summary>
    /// A 150 KB keyframe arrives as ~125-150 back-to-back datagrams; Windows' default 64 KB
    /// socket buffer would drop most of them.
    /// </summary>
    public const int ReceiveBufferBytes = 8 * 1024 * 1024;

    private readonly UdpClient _udp;

    public VideoReceiver(int port)
    {
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        _udp.Client.ReceiveBufferSize = ReceiveBufferBytes;
    }

    /// <summary>Opens the port, or returns a message for the user if it can't be opened.</summary>
    public static bool TryCreate(int port, out VideoReceiver? receiver, out string? error)
    {
        try
        {
            receiver = new VideoReceiver(port);
            error = null;
            return true;
        }
        catch (SocketException e)
        {
            receiver = null;
            error = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? $"UDP port {port} is already in use. Is CouchLink already joined to a host on this PC?"
                : $"Could not open UDP port {port}: {e.Message}";
            return false;
        }
    }

    public int LocalPort => ((IPEndPoint)_udp.Client.LocalEndPoint!).Port;

    public int ReceiveBufferSize => _udp.Client.ReceiveBufferSize;

    public Task RunAsync(Action<byte[]> onDatagram, CancellationToken ct, Action<Exception>? onError = null) =>
        UdpReceiveLoop.RunAsync(_udp, result => onDatagram(result.Buffer), ct, onError);

    public void Dispose() => _udp.Dispose();
}
```

`src/CouchLink.App/HostInputService.cs`: the callback now gets the address; keep behaviour the same for now:
```csharp
        _receiveLoop = _receiver.RunAsync((p, _) => _pads.Handle(p), _cts.Token, OnError);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet build` then `dotnet test --filter "FullyQualifiedName~UdpVideoTests|FullyQualifiedName~UdpInputTests"`
Expected: build 0 errors, 0 warnings; PASS (3 new tests, 5 input tests).

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Core/Net src/CouchLink.App/HostInputService.cs tests/CouchLink.Core.Tests/UdpVideoTests.cs tests/CouchLink.Core.Tests/UdpInputTests.cs
git commit -m "feat(core): UDP transport for video and keyframe requests"
```

---

### Task 8: Encoded-frame source interface and test pattern

**Files:**
- Create: `src/CouchLink.Core/Video/IEncodedVideoSource.cs`
- Create: `src/CouchLink.Core/Video/TestPattern.cs`
- Test: `tests/CouchLink.Core.Tests/TestPatternTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `public readonly record struct EncodedFrame(ReadOnlyMemory<byte> Data, bool Keyframe)`.
  - `public interface IEncodedVideoSource : IDisposable` with `bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame)`. Plan 4 implements it with capture + encoder.
  - `public static class TestPattern` with `KeyframeBytes = 150_000`, `DeltaFrameBytes = 20_000`, `byte[] Create(ulong sequence, bool keyframe)`, `bool Verify(ReadOnlySpan<byte> frame, bool keyframe)`.
  - `public sealed class TestPatternSource(TimeSpan frameInterval) : IEncodedVideoSource` with `static readonly TimeSpan SixtyFps`.

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/TestPatternTests.cs`:
```csharp
using System.Diagnostics;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

public class TestPatternTests
{
    [Fact]
    public void Created_frames_verify()
    {
        Assert.True(TestPattern.Verify(TestPattern.Create(7, keyframe: false), keyframe: false));
        var keyframe = TestPattern.Create(8, keyframe: true);
        Assert.Equal(TestPattern.KeyframeBytes, keyframe.Length);
        Assert.True(TestPattern.Verify(keyframe, keyframe: true));
    }

    [Fact]
    public void Any_changed_byte_fails()
    {
        foreach (int index in new[] { 0, 8, 13, TestPattern.DeltaFrameBytes - 1 })
        {
            var frame = TestPattern.Create(3, keyframe: false);
            frame[index] ^= 0x01;
            Assert.False(TestPattern.Verify(frame, keyframe: false), $"byte {index} changed");
        }
    }

    [Fact]
    public void Wrong_keyframe_flag_or_truncation_fails()
    {
        var frame = TestPattern.Create(5, keyframe: false);
        Assert.False(TestPattern.Verify(frame, keyframe: true));
        Assert.False(TestPattern.Verify(frame.AsSpan(0, frame.Length - 1), keyframe: false));
        Assert.False(TestPattern.Verify(frame.AsSpan(0, 4), keyframe: false));
    }

    [Fact]
    public void Source_starts_with_a_keyframe_and_forces_one_on_request()
    {
        using var source = new TestPatternSource(TimeSpan.Zero);
        var timeout = TimeSpan.FromSeconds(1);

        Assert.True(source.TryGetFrame(false, timeout, out var first));
        Assert.True(source.TryGetFrame(false, timeout, out var second));
        Assert.True(source.TryGetFrame(true, timeout, out var forced));

        Assert.True(first.Keyframe);
        Assert.False(second.Keyframe);
        Assert.True(forced.Keyframe);
        Assert.True(TestPattern.Verify(second.Data.Span, keyframe: false));
        Assert.True(TestPattern.Verify(forced.Data.Span, keyframe: true));
    }

    [Fact]
    public void Source_paces_frames()
    {
        using var source = new TestPatternSource(TimeSpan.FromMilliseconds(20));
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < 6; i++)
            Assert.True(source.TryGetFrame(false, TimeSpan.FromSeconds(1), out _));
        Assert.True(clock.ElapsedMilliseconds >= 90, $"6 frames took {clock.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Source_returns_false_when_no_frame_is_due_within_the_timeout()
    {
        using var source = new TestPatternSource(TimeSpan.FromSeconds(1));
        Assert.True(source.TryGetFrame(false, TimeSpan.FromMilliseconds(10), out _)); // first frame is due now
        Assert.False(source.TryGetFrame(false, TimeSpan.FromMilliseconds(10), out _));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~TestPatternTests`
Expected: build FAILS: `TestPattern`, `TestPatternSource` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Video/IEncodedVideoSource.cs`:
```csharp
namespace CouchLink.Core.Video;

/// <summary>One encoded frame (an H.264 access unit from Plan 4 on).</summary>
public readonly record struct EncodedFrame(ReadOnlyMemory<byte> Data, bool Keyframe);

/// <summary>Host side: where encoded frames come from. Called from the streamer's one thread.</summary>
public interface IEncodedVideoSource : IDisposable
{
    /// <summary>
    /// Waits up to <paramref name="timeout"/> for the next frame. With
    /// <paramref name="forceKeyframe"/> the frame must be a keyframe. Returns false if no frame
    /// was ready in time (e.g. the screen didn't change).
    /// </summary>
    bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame);
}
```

`src/CouchLink.Core/Video/TestPattern.cs`:
```csharp
using System.Buffers.Binary;
using System.Diagnostics;

namespace CouchLink.Core.Video;

/// <summary>
/// Fake encoded frames for testing the stream without a GPU. The bytes are deterministic
/// (sequence number, keyframe flag, length, then seeded pseudo-random data), so a client can
/// check every byte and any corruption in the pipeline shows up.
/// </summary>
public static class TestPattern
{
    public const int KeyframeBytes = 150_000;
    public const int DeltaFrameBytes = 20_000;
    private const int HeaderBytes = 13; // u64 sequence, u8 keyframe, i32 length

    public static byte[] Create(ulong sequence, bool keyframe)
    {
        int length = keyframe ? KeyframeBytes : DeltaFrameBytes + (int)(sequence % 5_000);
        var data = new byte[length];
        BinaryPrimitives.WriteUInt64LittleEndian(data, sequence);
        data[8] = keyframe ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(9), length);
        new Random(unchecked((int)sequence)).NextBytes(data.AsSpan(HeaderBytes));
        return data;
    }

    public static bool Verify(ReadOnlySpan<byte> frame, bool keyframe)
    {
        if (frame.Length < HeaderBytes)
            return false;
        var expected = Create(BinaryPrimitives.ReadUInt64LittleEndian(frame), keyframe);
        return frame.SequenceEqual(expected);
    }
}

/// <summary>An <see cref="IEncodedVideoSource"/> that produces test-pattern frames at a fixed rate.</summary>
public sealed class TestPatternSource(TimeSpan frameInterval) : IEncodedVideoSource
{
    public static readonly TimeSpan SixtyFps = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 60);

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _next;
    private ulong _sequence;

    public bool TryGetFrame(bool forceKeyframe, TimeSpan timeout, out EncodedFrame frame)
    {
        var now = _clock.Elapsed;
        if (_next > now)
        {
            var wait = _next - now;
            if (wait > timeout)
            {
                Thread.Sleep(timeout);
                frame = default;
                return false;
            }
            Thread.Sleep(wait);
            now = _next;
        }
        _next = now + frameInterval;

        bool keyframe = forceKeyframe || _sequence == 0;
        frame = new EncodedFrame(TestPattern.Create(_sequence++, keyframe), keyframe);
        return true;
    }

    public void Dispose()
    {
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~TestPatternTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/CouchLink.Core/Video/IEncodedVideoSource.cs src/CouchLink.Core/Video/TestPattern.cs tests/CouchLink.Core.Tests/TestPatternTests.cs
git commit -m "feat(core): encoded frame source interface and test pattern"
```

---

### Task 9: Host video streamer and client video pipeline

**Files:**
- Create: `src/CouchLink.Core/Video/VideoStreamer.cs`
- Create: `src/CouchLink.Core/Video/VideoClient.cs`
- Test: `tests/CouchLink.Core.Tests/VideoLoopbackTests.cs`

**Interfaces:**
- Consumes: `FramePacketizer` (Task 3); `FrameAssembler`, `AssembledFrame`, `VideoReceiveStats` (Task 4); `DecodeGate` (Task 5); `KeyframePolicy`, `StreamTargets` (Task 6); `IVideoPacketSender`, `VideoSender`, `VideoReceiver` (Task 7); `IEncodedVideoSource`, `EncodedFrame`, `TestPattern`, `TestPatternSource` (Task 8).
- Produces:
  - `public readonly record struct VideoSendStats(long FramesSent, long KeyframesSent, long BytesSent, int Clients)`.
  - `public sealed class VideoStreamer : IDisposable`, ctor `(IEncodedVideoSource source, IVideoPacketSender sender, int videoPort, TimeProvider time, Action<Exception>? onError = null, int parityPercent = FramePacketizer.DefaultParityPercent)` (owns source and sender), `void ClientSeen(byte slot, IPAddress address)`, `void RequestKeyframe()`, `VideoSendStats Stats`.
  - `public readonly record struct VideoClientStats(VideoReceiveStats Receive, long FramesDelivered, long FramesSkipped, long KeyframeRequests, bool WaitingForKeyframe)`.
  - `public sealed class VideoClient : IDisposable`, ctor `(VideoReceiver receiver, Action requestKeyframe, Action<AssembledFrame> onFrame, TimeProvider time, Action<Exception>? onError = null)` (owns the receiver; `onFrame` runs on the receive thread), `static readonly TimeSpan TickInterval` (50 ms), `VideoClientStats Stats`.

- [ ] **Step 1: Write the failing test**

`tests/CouchLink.Core.Tests/VideoLoopbackTests.cs`:
```csharp
using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;

namespace CouchLink.Core.Tests;

/// <summary>Spec section 9 loopback test: host streamer and client pipeline on one PC over real UDP.</summary>
public class VideoLoopbackTests
{
    private sealed class LossySender(IVideoPacketSender inner, Func<VideoShardHeader, bool> drop) : IVideoPacketSender
    {
        public void Send(IReadOnlyList<byte[]> packets, IReadOnlyList<IPEndPoint> targets) =>
            inner.Send(packets.Where(p => !(VideoShardPacket.TryParse(p, out var h) && drop(h))).ToList(), targets);

        public void Dispose() => inner.Dispose();
    }

    /// <summary>A streamer sending a 100 fps test pattern to one client on localhost.</summary>
    private sealed class Rig : IDisposable
    {
        private readonly Timer _keepAlive;
        private int _delivered, _corrupt, _firstWasKeyframe = -1;

        public Rig(Func<IVideoPacketSender, IVideoPacketSender>? wrap = null)
        {
            var receiver = new VideoReceiver(port: 0);
            IVideoPacketSender sender = new VideoSender();
            if (wrap is not null)
                sender = wrap(sender);
            Streamer = new VideoStreamer(
                new TestPatternSource(TimeSpan.FromMilliseconds(10)), sender, receiver.LocalPort, TimeProvider.System);
            Client = new VideoClient(receiver, Streamer.RequestKeyframe, OnFrame, TimeProvider.System);
            // A real client's input packets keep it in the host's targets; do the same here.
            _keepAlive = new Timer(_ => Streamer.ClientSeen(2, IPAddress.Loopback), null, 0, 100);
        }

        public VideoStreamer Streamer { get; }
        public VideoClient Client { get; }
        public int Delivered => Volatile.Read(ref _delivered);
        public int Corrupt => Volatile.Read(ref _corrupt);
        public bool FirstWasKeyframe => Volatile.Read(ref _firstWasKeyframe) == 1;

        private void OnFrame(AssembledFrame frame)
        {
            if (Interlocked.Increment(ref _delivered) == 1)
                Volatile.Write(ref _firstWasKeyframe, frame.Keyframe ? 1 : 0);
            if (!TestPattern.Verify(frame.Data, frame.Keyframe))
                Interlocked.Increment(ref _corrupt);
        }

        public async Task WaitForFrames(int count)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (Delivered < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Only {Delivered} of {count} frames arrived. Client: {Client.Stats}");
                await Task.Delay(20);
            }
        }

        public void Dispose()
        {
            _keepAlive.Dispose();
            Client.Dispose();
            Streamer.Dispose();
        }
    }

    [Fact]
    public async Task Frames_arrive_intact_starting_with_a_keyframe()
    {
        using var rig = new Rig();
        await rig.WaitForFrames(50);

        Assert.True(rig.FirstWasKeyframe);
        Assert.Equal(0, rig.Corrupt);
        Assert.Equal(0, rig.Client.Stats.Receive.FramesLost);
        Assert.Equal(1, rig.Streamer.Stats.Clients);
    }

    [Fact]
    public async Task A_lost_frame_is_recovered_with_a_requested_keyframe()
    {
        using var rig = new Rig(sender => new LossySender(sender, h => h.Frame == 20));
        await rig.WaitForFrames(60);

        Assert.Equal(0, rig.Corrupt);
        Assert.True(rig.Client.Stats.FramesSkipped >= 1, "frames after the lost one must not be decoded");
        Assert.True(rig.Streamer.Stats.KeyframesSent >= 2, "the client's request must produce a new keyframe");
    }

    [Fact]
    public async Task Random_loss_is_repaired_without_corruption()
    {
        var rng = new Random(5);
        using var rig = new Rig(sender => new LossySender(sender, _ => rng.NextDouble() < 0.05));
        await rig.WaitForFrames(100);

        Assert.Equal(0, rig.Corrupt);
        Assert.True(rig.Client.Stats.Receive.ShardsRecovered > 0);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~VideoLoopbackTests`
Expected: build FAILS: `VideoStreamer`, `VideoClient` not found.

- [ ] **Step 3: Write the implementation**

`src/CouchLink.Core/Video/VideoStreamer.cs`:
```csharp
using System.Net;
using CouchLink.Core.Net;

namespace CouchLink.Core.Video;

public readonly record struct VideoSendStats(long FramesSent, long KeyframesSent, long BytesSent, int Clients);

/// <summary>
/// Host side: on its own thread, pulls encoded frames from the source, packetizes each frame
/// once and sends the packets to every current client. Clients are learned from their input
/// packets (<see cref="ClientSeen"/>); a new client or a keyframe request forces a keyframe.
/// Takes ownership of the source and the sender.
/// </summary>
public sealed class VideoStreamer : IDisposable
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromMilliseconds(50);

    private readonly IEncodedVideoSource _source;
    private readonly IVideoPacketSender _sender;
    private readonly TimeProvider _time;
    private readonly long _start;
    private readonly Action<Exception>? _onError;
    private readonly FramePacketizer _packetizer;
    private readonly StreamTargets _targets;
    private readonly KeyframePolicy _keyframes = new();
    private readonly Lock _gate = new();
    private readonly Thread _thread;
    private volatile bool _running = true;
    private uint _frameNumber;
    private long _framesSent, _keyframesSent, _bytesSent;
    private int _clients;

    public VideoStreamer(
        IEncodedVideoSource source,
        IVideoPacketSender sender,
        int videoPort,
        TimeProvider time,
        Action<Exception>? onError = null,
        int parityPercent = FramePacketizer.DefaultParityPercent)
    {
        _source = source;
        _sender = sender;
        _time = time;
        _start = time.GetTimestamp();
        _onError = onError;
        // Random per stream, so clients can tell a restarted stream from late packets.
        _packetizer = new FramePacketizer((ushort)Random.Shared.Next(1, ushort.MaxValue + 1), parityPercent);
        _targets = new StreamTargets(videoPort);
        _thread = new Thread(Run) { IsBackground = true, Name = "CouchLink video", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public VideoSendStats Stats => new(
        Interlocked.Read(ref _framesSent),
        Interlocked.Read(ref _keyframesSent),
        Interlocked.Read(ref _bytesSent),
        Volatile.Read(ref _clients));

    private TimeSpan Now => _time.GetElapsedTime(_start);

    /// <summary>A client's input packet arrived; it gets video, and a keyframe if it is new.</summary>
    public void ClientSeen(byte slot, IPAddress address)
    {
        lock (_gate)
            if (_targets.Seen(slot, address, Now))
                _keyframes.Request();
    }

    public void RequestKeyframe()
    {
        lock (_gate)
            _keyframes.Request();
    }

    private void Run()
    {
        while (_running)
        {
            try
            {
                StreamOneFrame();
            }
            catch (Exception e)
            {
                _onError?.Invoke(e);
                Thread.Sleep(100); // don't spin if the source keeps failing
            }
        }
    }

    private void StreamOneFrame()
    {
        bool force;
        lock (_gate)
            force = _keyframes.ShouldForce(Now);
        if (!_source.TryGetFrame(force, PollTimeout, out var frame))
            return;

        IReadOnlyList<IPEndPoint> targets;
        lock (_gate)
        {
            if (frame.Keyframe)
                _keyframes.KeyframeSent(Now);
            targets = _targets.Current(Now);
        }
        Volatile.Write(ref _clients, targets.Count);
        if (targets.Count == 0)
            return; // nobody to send to; frame numbers stay consecutive for the next client

        var packets = _packetizer.Packetize(_frameNumber++, frame.Data.Span, frame.Keyframe);
        _sender.Send(packets, targets);
        Interlocked.Increment(ref _framesSent);
        Interlocked.Add(ref _bytesSent, frame.Data.Length);
        if (frame.Keyframe)
            Interlocked.Increment(ref _keyframesSent);
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join();
        _source.Dispose();
        _sender.Dispose();
    }
}
```

`src/CouchLink.Core/Video/VideoClient.cs`:
```csharp
using CouchLink.Core.Net;

namespace CouchLink.Core.Video;

public readonly record struct VideoClientStats(
    VideoReceiveStats Receive,
    long FramesDelivered,
    long FramesSkipped,
    long KeyframeRequests,
    bool WaitingForKeyframe);

/// <summary>
/// Client side: receives shard datagrams, assembles and repairs frames, and passes decodable
/// frames to <c>onFrame</c> on the receive thread. Whenever the decode gate waits for a
/// keyframe it asks the host through <c>requestKeyframe</c>, at once and then every
/// <see cref="DecodeGate.RequestInterval"/>. A timer gives up stalled frames every
/// <see cref="TickInterval"/>. Takes ownership of the receiver.
/// </summary>
public sealed class VideoClient : IDisposable
{
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(50);

    private readonly VideoReceiver _receiver;
    private readonly Action _requestKeyframe;
    private readonly Action<AssembledFrame> _onFrame;
    private readonly Action<Exception>? _onError;
    private readonly TimeProvider _time;
    private readonly long _start;
    private readonly DecodeGate _gate = new();
    private readonly FrameAssembler _assembler;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly ITimer _timer;
    private long _delivered;

    public VideoClient(
        VideoReceiver receiver,
        Action requestKeyframe,
        Action<AssembledFrame> onFrame,
        TimeProvider time,
        Action<Exception>? onError = null)
    {
        _receiver = receiver;
        _requestKeyframe = requestKeyframe;
        _onFrame = onFrame;
        _onError = onError;
        _time = time;
        _start = time.GetTimestamp();
        _assembler = new FrameAssembler(_gate.FrameLost);
        _loop = receiver.RunAsync(OnDatagram, _cts.Token, onError);
        _timer = time.CreateTimer(_ => Tick(), null, TickInterval, TickInterval);
    }

    public VideoClientStats Stats
    {
        get
        {
            lock (_lock)
                return new VideoClientStats(
                    _assembler.Stats,
                    Interlocked.Read(ref _delivered),
                    _gate.FramesSkipped,
                    _gate.KeyframeRequests,
                    _gate.WaitingForKeyframe);
        }
    }

    private TimeSpan Now => _time.GetElapsedTime(_start);

    private void OnDatagram(byte[] datagram)
    {
        AssembledFrame? frame;
        lock (_lock)
        {
            frame = _assembler.Add(datagram, Now);
            if (frame is not null && !_gate.Accept(frame))
                frame = null;
        }
        if (frame is not null)
        {
            Interlocked.Increment(ref _delivered);
            _onFrame(frame);
        }
        RequestKeyframeIfNeeded(); // a loss found by this packet is reported at once
    }

    private void Tick()
    {
        try
        {
            lock (_lock)
                _assembler.AbandonStale(Now);
            RequestKeyframeIfNeeded();
        }
        catch (Exception e)
        {
            _onError?.Invoke(e); // an exception on a timer thread would kill the process
        }
    }

    private void RequestKeyframeIfNeeded()
    {
        bool send;
        lock (_lock)
            send = _gate.ShouldRequestKeyframe(Now);
        if (send)
            _requestKeyframe();
    }

    public void Dispose()
    {
        _timer.Dispose();
        _cts.Cancel();
        _loop.Wait(TimeSpan.FromSeconds(2));
        _receiver.Dispose();
        _cts.Dispose();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~VideoLoopbackTests`
Expected: PASS (3 tests), each within a few seconds.

- [ ] **Step 5: Run the whole suite**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/CouchLink.Core/Video/VideoStreamer.cs src/CouchLink.Core/Video/VideoClient.cs tests/CouchLink.Core.Tests/VideoLoopbackTests.cs
git commit -m "feat(core): host video streamer and client video pipeline"
```

---

### Task 10: Stream the test pattern in the dev window

**Files:**
- Modify: `src/CouchLink.App/HostInputService.cs`
- Create: `src/CouchLink.App/ClientVideoService.cs`
- Modify: `src/CouchLink.App/MainWindow.xaml`
- Modify: `src/CouchLink.App/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `VideoStreamer`, `VideoSendStats`, `VideoClient`, `VideoClientStats` (Task 9); `TestPatternSource`, `TestPattern` (Task 8); `VideoSender`, `VideoReceiver`, `Ports.Video`, `InputReceiver.RunAsync` (new shape), `InputSender.SendKeyframeRequest` (Task 7).
- Produces: `HostInputService.VideoStats` (`VideoSendStats`); `internal sealed class ClientVideoService : IDisposable` with `static bool TryStart(InputSender sender, out ClientVideoService? service, out string? error)` and `string Describe()`.

- [ ] **Step 1: Host streams the test pattern**

`src/CouchLink.App/HostInputService.cs`, full file:
```csharp
using System.Net;
using CouchLink.Core.Net;
using CouchLink.Core.Pads;
using CouchLink.Core.Protocol;
using CouchLink.Core.Video;
using CouchLink.Pads;

namespace CouchLink.App;

/// <summary>
/// Host side: receives input on UDP 47803 and drives one virtual DS4 per slot, and streams
/// video to every client it hears from. Until Plan 4 the video is a test pattern.
/// </summary>
internal sealed class HostInputService : IDisposable
{
    private readonly ViGEmPadFactory _factory;
    private readonly PadManager _pads;
    private readonly InputReceiver _receiver;
    private readonly VideoStreamer _video;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _receiveLoop;
    private readonly Timer _staleTimer;

    private HostInputService(InputReceiver receiver, ViGEmPadFactory factory)
    {
        _factory = factory;
        _pads = new PadManager(factory, TimeProvider.System);
        _receiver = receiver;
        _video = new VideoStreamer(
            new TestPatternSource(TestPatternSource.SixtyFps), new VideoSender(), Ports.Video, TimeProvider.System, OnVideoError);
        _receiveLoop = _receiver.RunAsync(OnInput, _cts.Token, OnError, (_, _) => _video.RequestKeyframe());
        _staleTimer = new Timer(_ => ReleaseStale(), null, PadManager.CheckInterval, PadManager.CheckInterval);
    }

    public int PadCount => _pads.Count;

    public VideoSendStats VideoStats => _video.Stats;

    /// <summary>Most recent pad or video error, shown to the host instead of failing silently.</summary>
    public string? LastError { get; private set; }

    private void OnInput(InputPacket packet, IPAddress from)
    {
        if (_pads.Handle(packet))
            _video.ClientSeen(packet.Slot, from);
    }

    private void ReleaseStale()
    {
        try
        {
            _pads.ReleaseStale();
        }
        catch (Exception e)
        {
            OnError(e); // an unhandled exception here would kill the host process
        }
    }

    private void OnError(Exception e)
    {
        LastError = $"{e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Pad error: {e}");
    }

    private void OnVideoError(Exception e)
    {
        LastError = $"Video: {e.GetType().Name}: {e.Message}";
        AppServices.Log.Write($"Video error: {e}");
    }

    public static bool TryStart(out HostInputService? service, out string? error)
    {
        service = null;
        // Port first: it's the step most likely to fail, and nothing needs cleaning up yet.
        if (!InputReceiver.TryCreate(Ports.Input, out var receiver, out error))
            return false;
        if (!ViGEmPadFactory.TryCreate(out var factory, out error))
        {
            receiver!.Dispose();
            return false;
        }
        service = new HostInputService(receiver!, factory!);
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _staleTimer.Dispose();
        _receiveLoop.Wait(TimeSpan.FromSeconds(2));
        _video.Dispose();
        _receiver.Dispose();
        _pads.Dispose();
        _factory.Dispose();
    }
}
```

- [ ] **Step 2: Client video service**

`src/CouchLink.App/ClientVideoService.cs`:
```csharp
using CouchLink.Core.Net;
using CouchLink.Core.Video;

namespace CouchLink.App;

/// <summary>
/// Client side: receives the host's video on UDP 47802 and, until Plan 4 adds a decoder,
/// checks every frame against the test pattern so corruption shows in the dev window.
/// Dispose before the <see cref="InputSender"/> it sends keyframe requests through.
/// </summary>
internal sealed class ClientVideoService : IDisposable
{
    private readonly VideoClient _client;
    private long _corrupt;

    private ClientVideoService(VideoReceiver receiver, InputSender sender)
    {
        _client = new VideoClient(
            receiver, sender.SendKeyframeRequest, OnFrame, TimeProvider.System,
            e => AppServices.Log.Write($"Video error: {e}"));
    }

    public static bool TryStart(InputSender sender, out ClientVideoService? service, out string? error)
    {
        service = null;
        if (!VideoReceiver.TryCreate(Ports.Video, out var receiver, out error))
            return false;
        service = new ClientVideoService(receiver!, sender);
        return true;
    }

    private void OnFrame(AssembledFrame frame)
    {
        if (!TestPattern.Verify(frame.Data, frame.Keyframe))
            Interlocked.Increment(ref _corrupt);
    }

    public string Describe()
    {
        var s = _client.Stats;
        return $"Video: {s.FramesDelivered} frames, {s.Receive.ShardsRecovered} repaired, " +
               $"{s.Receive.FramesLost} lost, {Interlocked.Read(ref _corrupt)} corrupt, " +
               $"loss {s.Receive.LossPercent:0.0}%" +
               (s.WaitingForKeyframe ? ", waiting for keyframe" : "");
    }

    public void Dispose() => _client.Dispose();
}
```

- [ ] **Step 3: Wire the dev window**

`src/CouchLink.App/MainWindow.xaml`: make room for the extra status lines; change the window's `Height="380"` to `Height="460"`.

`src/CouchLink.App/MainWindow.xaml.cs`:

Add a field after `private ClientInputLoop? _client;`:
```csharp
    private ClientVideoService? _video;
```

In `OnJoin`, replace everything from `_mapper = new InputMapper(...)` to the end of the method with:
```csharp
        var slot = (int)SlotBox.SelectedItem;
        var inputSender = new InputSender(new IPEndPoint(ip, Ports.Input), (byte)slot);
        if (!ClientVideoService.TryStart(inputSender, out _video, out var videoError))
        {
            inputSender.Dispose();
            MessageBox.Show(this, videoError, "CouchLink");
            return;
        }

        _mapper = new InputMapper(KeyLayout.CreateDefault(), new MouseStick());
        _rawInput = new RawInputSource((HwndSource)PresentationSource.FromVisual(this));
        _rawInput.KeyDown += _mapper.KeyDown;
        _rawInput.KeyUp += _mapper.KeyUp;
        _rawInput.MouseMove += _mapper.MouseMove;

        _client = new ClientInputLoop(_mapper, inputSender);
        // Raw Input keys still reach focused controls; lock them so play can't change what's shown.
        HostButton.IsEnabled = JoinButton.IsEnabled = HostIpBox.IsEnabled = SlotBox.IsEnabled = false;
        _clientStatus = $"Sending to {ip} as P{slot}";
        AppServices.DescribeMode = () => $"Client (slot P{slot})";
        AppServices.Log.Write($"Joined as P{slot}");
```

Replace `UpdateStatus` with:
```csharp
    private void UpdateStatus()
    {
        if (_host is not null)
        {
            var v = _host.VideoStats;
            StatusText.Text = $"Hosting. Virtual pads: {_host.PadCount}" +
                $"\nVideo (test pattern): {v.Clients} client(s), {v.FramesSent} frames, " +
                $"{v.KeyframesSent} keyframes, {v.BytesSent / 1_000_000.0:0.0} MB" +
                (_host.LastError is { } err ? $"\nLast error: {err}" : "");
        }
        else if (_client is not null)
        {
            var s = _client.LastSent;
            StatusText.Text =
                $"{_clientStatus}\n" +
                $"Buttons: {s.Buttons}\nL: {s.LX},{s.LY}  R: {s.RX},{s.RY}  L2/R2: {s.L2}/{s.R2}\n" +
                _video?.Describe();
        }
    }
```

In `OnClosed`, dispose the video first, because it sends keyframe requests through the input sender that `_client` disposes:
```csharp
    protected override void OnClosed(EventArgs e)
    {
        _statusTimer.Stop();
        _video?.Dispose();
        _client?.Dispose();
        _rawInput?.Dispose();
        _host?.Dispose();
        base.OnClosed(e);
    }
```

Run: `dotnet build`
Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Run the whole suite**

Run: `dotnet test`
Expected: all tests PASS.

- [ ] **Step 5: Loopback check in the real app (UI Automation)**

Host and client on one PC: the host binds 47803, the client binds 47802, and the host streams to 127.0.0.1 because that's where the input comes from. ViGEmBus must be installed (Host needs it). Save as `$env:TEMP\video-check.ps1`, set `$exe` to this checkout's build output, and run `powershell -ExecutionPolicy Bypass -File $env:TEMP\video-check.ps1`:
```powershell
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$exe = 'C:\dev\CouchLink\src\CouchLink.App\bin\Debug\net10.0-windows\CouchLink.App.exe'
$A = [Windows.Automation.AutomationElement]
function MainWindow($proc) {
    $cond = New-Object Windows.Automation.PropertyCondition($A::ProcessIdProperty, $proc.Id)
    for ($i = 0; $i -lt 40; $i++) {
        $w = $A::RootElement.FindFirst([Windows.Automation.TreeScope]::Children, $cond)
        if ($w -and $w.Current.Name -eq 'CouchLink (dev)') { return $w }
        Start-Sleep -Milliseconds 250
    }
    throw "no main window for process $($proc.Id)"
}
function El($w, $id) {
    $w.FindFirst([Windows.Automation.TreeScope]::Descendants,
        (New-Object Windows.Automation.PropertyCondition($A::AutomationIdProperty, $id)))
}
function Click($w, $id) { (El $w $id).GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }

$hostProc = Start-Process $exe -PassThru
$clientProc = Start-Process $exe -PassThru
try {
    $hw = MainWindow $hostProc
    $cw = MainWindow $clientProc
    Click $hw 'HostButton'
    Click $cw 'JoinButton'   # Host IP box defaults to 127.0.0.1
    Start-Sleep -Seconds 5
    "HOST:`n$((El $hw 'StatusText').Current.Name)`n"
    "CLIENT:`n$((El $cw 'StatusText').Current.Name)"
} finally {
    Stop-Process -Id $hostProc.Id, $clientProc.Id -ErrorAction SilentlyContinue
}
```
Expected:
- HOST: `Video (test pattern): 1 client(s), N frames, K keyframes` with N around 250-300 and K of 1 or 2.
- CLIENT: `Video: N frames, 0 repaired, 0 lost, 0 corrupt, loss 0.0%` with N close to the host's count, and no `waiting for keyframe`.

- [ ] **Step 6: Two-PC check (optional, recommended)**

On the client PC, allow the video port once (elevated PowerShell): `New-NetFirewallRule -DisplayName "CouchLink video (dev)" -Direction Inbound -Protocol UDP -LocalPort 47802 -Action Allow`. Start Host on one PC; on the other, enter the host's IP and click Join. After 10 s the client status should show frames climbing at ~60 per second, `0 corrupt`, and loss near 0% on a wired switch.

- [ ] **Step 7: Commit**

```powershell
git add src/CouchLink.App
git commit -m "feat(app): stream a test pattern from host to clients" -m "Closes #16"
```
