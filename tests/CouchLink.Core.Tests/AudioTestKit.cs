using CouchLink.Core.Protocol;

namespace CouchLink.Core.Tests;

internal static class AudioTestKit
{
    /// <summary>
    /// A packet whose Opus "frame" is one byte, the sequence's low byte, so tests can tell frames
    /// apart. With <paramref name="withPrevious"/> it carries the previous frame the same way.
    /// </summary>
    public static AudioPacket Packet(uint sequence, bool withPrevious = true, ushort stream = 1)
    {
        ReadOnlyMemory<byte> previous = withPrevious ? new[] { (byte)(sequence - 1) } : ReadOnlyMemory<byte>.Empty;
        return new AudioPacket(stream, sequence, new[] { (byte)sequence }, previous);
    }

    public static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException();
            await Task.Delay(10);
        }
    }
}
