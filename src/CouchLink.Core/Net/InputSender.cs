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
    private readonly byte[] _keyframeRequest = new byte[KeyframeRequest.Size];
    private readonly byte[] _timingPing = new byte[TimingPing.Size];
    private readonly byte _slot;
    private uint _sequence;

    public InputSender(IPEndPoint host, byte slot)
    {
        _slot = slot;
        new KeyframeRequest(slot).WriteTo(_keyframeRequest);
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

    /// <summary>Sends a timing ping stamped with the caller's clock. Call from one thread at a time.</summary>
    public void SendTimingPing(long clientTicks)
    {
        new TimingPing(_slot, clientTicks).WriteTo(_timingPing);
        try
        {
            _udp.Send(_timingPing, _timingPing.Length);
        }
        catch (SocketException)
        {
            // Host not reachable right now; the next ping, a second later, tries again.
        }
    }

    public void Dispose() => _udp.Dispose();
}
