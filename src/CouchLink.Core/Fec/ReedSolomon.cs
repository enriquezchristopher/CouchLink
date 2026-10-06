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
