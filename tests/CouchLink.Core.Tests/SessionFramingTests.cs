using System.Buffers.Binary;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

public class SessionFramingTests
{
    public static TheoryData<SessionMessage> AllMessages => new()
    {
        SessionMessage.JoinRequest("PC-07"),
        SessionMessage.Accepted(2),
        SessionMessage.Accepted(10),
        SessionMessage.Denied(DenyReason.Denied),
        SessionMessage.Denied(DenyReason.Full),
        SessionMessage.Denied(DenyReason.TimedOut),
        SessionMessage.Denied(DenyReason.PadFailed),
        SessionMessage.Heartbeat,
        SessionMessage.Leave,
        SessionMessage.Kicked,
        SessionMessage.HostEnded,
    };

    [Theory]
    [MemberData(nameof(AllMessages))]
    public async Task Every_message_round_trips_through_a_stream(SessionMessage message)
    {
        using var stream = new MemoryStream(SessionFraming.Encode(message));

        Assert.Equal(message, await SessionFraming.ReadAsync(stream, CancellationToken.None));
        Assert.Null(await SessionFraming.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Frames_back_to_back_are_read_one_at_a_time()
    {
        var bytes = SessionFraming.Encode(SessionMessage.JoinRequest("PC-07"))
            .Concat(SessionFraming.Encode(SessionMessage.Heartbeat)).ToArray();
        using var stream = new MemoryStream(bytes);

        Assert.Equal("JoinRequest PC-07", (await SessionFraming.ReadAsync(stream, default))!.Value.ToString());
        Assert.Equal(SessionMessage.Heartbeat, await SessionFraming.ReadAsync(stream, default));
    }

    [Fact]
    public async Task An_end_of_stream_mid_frame_is_an_error()
    {
        var bytes = SessionFraming.Encode(SessionMessage.JoinRequest("PC-07"));
        using var stream = new MemoryStream(bytes[..^2]);

        await Assert.ThrowsAsync<EndOfStreamException>(() => SessionFraming.ReadAsync(stream, default));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4097)]
    public async Task A_length_outside_4_to_4096_is_rejected(int length)
    {
        var bytes = new byte[2 + 8];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)length);
        using var stream = new MemoryStream(bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() => SessionFraming.ReadAsync(stream, default));
    }

    [Fact]
    public void Wrong_magic_version_or_type_is_rejected()
    {
        var frame = SessionFraming.Encode(SessionMessage.Heartbeat)[2..];

        var magic = (byte[])frame.Clone();
        magic[0] ^= 0xFF;
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(magic));

        var version = (byte[])frame.Clone();
        version[2] = Wire.Version + 1;
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(version));

        var type = (byte[])frame.Clone();
        type[3] = Wire.TypeInput;
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(type));
    }

    [Fact]
    public void Bodies_of_the_wrong_shape_are_rejected()
    {
        byte[] Frame(byte type, params byte[] body)
        {
            var frame = new byte[4 + body.Length];
            Wire.WriteHeader(frame, type);
            body.CopyTo(frame, 4);
            return frame;
        }

        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted, 1)));      // slot 1
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted, 11)));     // slot 11
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeAccepted)));         // no slot
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeDenied, 9)));        // unknown reason
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeHeartbeat, 0)));     // body on an empty message
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeJoinRequest, 3, 65))); // name shorter than its length
        Assert.Throws<InvalidDataException>(() => SessionFraming.Decode(Frame(Wire.TypeJoinRequest, 0)));   // empty name
    }

    [Fact]
    public void Decode_throws_only_InvalidDataException_for_random_frames()
    {
        var random = new Random(11);
        for (int i = 0; i < 20_000; i++)
        {
            var frame = new byte[random.Next(0, 70)];
            random.NextBytes(frame);
            if (frame.Length >= 4)
            {
                Wire.WriteHeader(frame, (byte)random.Next(7, 16));
            }
            try
            {
                SessionFraming.Decode(frame);
            }
            catch (InvalidDataException)
            {
            }
        }
    }

    [Fact]
    public void Messages_describe_themselves_for_logs_and_tests()
    {
        Assert.Equal("JoinRequest PC-07", SessionMessage.JoinRequest("PC-07").ToString());
        Assert.Equal("Accepted P4", SessionMessage.Accepted(4).ToString());
        Assert.Equal("Denied Full", SessionMessage.Denied(DenyReason.Full).ToString());
        Assert.Equal("Kicked", SessionMessage.Kicked.ToString());
    }
}
