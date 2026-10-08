using System.Buffers.Binary;

namespace CouchLink.Core.Protocol;

/// <summary>
/// TCP session frames: a u16 length (4-4096, the bytes that follow), the 4-byte <see cref="Wire"/>
/// header, then the body. JoinRequest: name length (1) + name. Accepted: slot (1, 2-10).
/// Denied: reason (1, 1-4). Every other message has no body.
/// </summary>
public static class SessionFraming
{
    public const int MaxFrame = 4096;
    private const int MinFrame = 4;

    public static byte[] Encode(SessionMessage message)
    {
        byte[] body = message.Type switch
        {
            SessionMessageType.JoinRequest => NameBody(message.Name),
            SessionMessageType.Accepted => [message.Slot],
            SessionMessageType.Denied => [(byte)message.Reason],
            _ => [],
        };
        var bytes = new byte[2 + 4 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(4 + body.Length));
        Wire.WriteHeader(bytes.AsSpan(2), (byte)message.Type);
        body.CopyTo(bytes, 6);
        return bytes;
    }

    private static byte[] NameBody(string name)
    {
        var bytes = PcName.Encode(name);
        return [(byte)bytes.Length, .. bytes];
    }

    /// <summary>Decodes one frame (without its length prefix). Throws <see cref="InvalidDataException"/> for anything malformed.</summary>
    public static SessionMessage Decode(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < MinFrame || BinaryPrimitives.ReadUInt16LittleEndian(frame) != Wire.Magic)
            throw new InvalidDataException("Not a CouchLink session frame.");
        if (frame[2] != Wire.Version)
            throw new InvalidDataException($"Session frame version {frame[2]}, expected {Wire.Version}.");

        var body = frame[4..];
        switch (frame[3])
        {
            case Wire.TypeJoinRequest:
                if (body.Length < 1 || body.Length != 1 + body[0] || !PcName.TryDecode(body[1..], out var name))
                    throw new InvalidDataException("Bad join request.");
                return SessionMessage.JoinRequest(name);
            case Wire.TypeAccepted:
                if (body.Length != 1 || body[0] is < 2 or > 10)
                    throw new InvalidDataException("Bad accepted message.");
                return SessionMessage.Accepted(body[0]);
            case Wire.TypeDenied:
                if (body.Length != 1 || body[0] is < (byte)DenyReason.Denied or > (byte)DenyReason.PadFailed)
                    throw new InvalidDataException("Bad denied message.");
                return SessionMessage.Denied((DenyReason)body[0]);
            case Wire.TypeHeartbeat or Wire.TypeLeave or Wire.TypeKicked or Wire.TypeHostEnded:
                if (!body.IsEmpty)
                    throw new InvalidDataException("Unexpected body.");
                return new SessionMessage((SessionMessageType)frame[3]);
            default:
                throw new InvalidDataException($"Unknown session message type {frame[3]}.");
        }
    }

    /// <summary>Reads the next message; null if the stream ended cleanly between frames.</summary>
    public static async Task<SessionMessage?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var prefix = new byte[2];
        int got = await stream.ReadAtLeastAsync(prefix, 2, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        if (got == 0)
            return null;
        if (got < 2)
            throw new EndOfStreamException();

        int length = BinaryPrimitives.ReadUInt16LittleEndian(prefix);
        if (length is < MinFrame or > MaxFrame)
            throw new InvalidDataException($"Bad session frame length {length}.");
        var frame = new byte[length];
        await stream.ReadExactlyAsync(frame, ct).ConfigureAwait(false);
        return Decode(frame);
    }
}
