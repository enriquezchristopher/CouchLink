using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class AudioPacketTests
{
    [Fact]
    public void Round_trips_with_the_previous_frame()
    {
        var bytes = new AudioPacket(0xBEEF, 0x01020304, new byte[] { 1, 2, 3 }, new byte[] { 9, 8 }).ToArray();

        Assert.Equal(AudioPacket.HeaderSize + 5, bytes.Length);
        Assert.True(AudioPacket.TryParse(bytes, out var p));
        Assert.Equal((ushort)0xBEEF, p.StreamId);
        Assert.Equal(0x01020304u, p.Sequence);
        Assert.Equal(new byte[] { 1, 2, 3 }, p.Frame.ToArray());
        Assert.Equal(new byte[] { 9, 8 }, p.Previous.ToArray());
    }

    [Fact]
    public void Round_trips_without_a_previous_frame()
    {
        var bytes = new AudioPacket(7, 42, new byte[] { 5 }, ReadOnlyMemory<byte>.Empty).ToArray();

        Assert.True(AudioPacket.TryParse(bytes, out var p));
        Assert.Equal(42u, p.Sequence);
        Assert.Equal(new byte[] { 5 }, p.Frame.ToArray());
        Assert.True(p.Previous.IsEmpty);
    }

    [Fact]
    public void Every_truncation_and_an_extra_byte_are_rejected()
    {
        var bytes = new AudioPacket(7, 42, new byte[] { 1, 2, 3 }, new byte[] { 4 }).ToArray();

        for (int length = 0; length < bytes.Length; length++)
            Assert.False(AudioPacket.TryParse(bytes[..length], out _));
        Assert.False(AudioPacket.TryParse([.. bytes, 0], out _));
    }

    [Fact]
    public void Other_packet_types_are_rejected()
    {
        var bytes = new AudioPacket(7, 42, new byte[] { 1 }, ReadOnlyMemory<byte>.Empty).ToArray();
        bytes[3] = Wire.TypeVideoShard;

        Assert.False(AudioPacket.TryParse(bytes, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(AudioPacket.MaxFrameBytes + 1)]
    public void Frames_must_be_1_to_400_bytes(int length)
    {
        Assert.Throws<ArgumentException>(
            () => new AudioPacket(1, 1, new byte[length], ReadOnlyMemory<byte>.Empty).ToArray());
    }

    [Fact]
    public void Random_bytes_with_an_audio_header_never_throw()
    {
        var random = new Random(6);
        for (int i = 0; i < 10_000; i++)
        {
            var bytes = new byte[random.Next(0, 64)];
            random.NextBytes(bytes);
            if (bytes.Length >= 4)
                Wire.WriteHeader(bytes, Wire.TypeAudio);
            AudioPacket.TryParse(bytes, out _); // returns true or false; never throws
        }
    }

    [Fact]
    public void Wire_reads_the_type_of_any_couchlink_datagram()
    {
        var bytes = new byte[8];
        Wire.WriteHeader(bytes, Wire.TypeAudio);

        Assert.True(Wire.TryGetType(bytes, out var type));
        Assert.Equal(Wire.TypeAudio, type);
        Assert.False(Wire.TryGetType(bytes.AsSpan(0, 3), out _));
        Assert.False(Wire.TryGetType(new byte[8], out _)); // no "CL" magic
    }
}
