namespace CouchLink.Core.Protocol;

/// <summary>
/// Newest-wins filter per slot. A different epoch means the client restarted,
/// so its packets are accepted even though the sequence starts over.
/// Not thread-safe.
/// </summary>
public sealed class SequenceFilter
{
    private readonly Dictionary<byte, (uint Epoch, uint Sequence)> _last = [];

    public bool Accept(byte slot, uint epoch, uint sequence)
    {
        if (_last.TryGetValue(slot, out var last)
            && last.Epoch == epoch
            && unchecked((int)(sequence - last.Sequence)) <= 0)
            return false;

        _last[slot] = (epoch, sequence);
        return true;
    }
}
