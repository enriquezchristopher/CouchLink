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
