using System.Net;
using System.Net.Sockets;
using CouchLink.Core.Input;
using CouchLink.Core.Protocol;

namespace CouchLink.Core.Net;

/// <summary>Client side: sends controller state to the host. Call Send from one thread.</summary>
public sealed class InputSender : IDisposable
{
    private readonly UdpClient _udp = new();
    private readonly byte[] _buffer = new byte[InputPacket.Size];
    private readonly byte _slot;
    private uint _sequence;

    public InputSender(IPEndPoint host, byte slot)
    {
        _slot = slot;
        Epoch = unchecked((uint)Random.Shared.NextInt64());
        _udp.Connect(host);
    }

    /// <summary>Random per run, so the host can tell a restarted client from stale packets.</summary>
    public uint Epoch { get; }

    public void Send(PadState state)
    {
        new InputPacket(_slot, Epoch, ++_sequence, state).WriteTo(_buffer);
        try
        {
            _udp.Send(_buffer, _buffer.Length);
        }
        catch (SocketException)
        {
            // Host not reachable right now; the next send (<= 8 ms) carries the full state anyway.
        }
    }

    public void Dispose() => _udp.Dispose();
}
