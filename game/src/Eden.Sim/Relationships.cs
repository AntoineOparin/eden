namespace Eden.Sim;

public struct Relationship
{
    public float Affinity;
    public float Trust;
    public float Debt;
}

/// <summary>
/// Sparse symmetric relationship table keyed by the unordered pair of NPC ids. Never iterated by sim
/// logic (only by the inspector, hashing and saves), so dictionary order can never leak into the sim.
/// </summary>
public sealed class Relationships
{
    private readonly Dictionary<long, Relationship> _pairs = new();

    public int Count => _pairs.Count;

    public static long Key(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    public Relationship Get(int a, int b) => _pairs.TryGetValue(Key(a, b), out var r) ? r : default;

    public void Adjust(int a, int b, float affinity, float trust = 0, float debt = 0)
    {
        long k = Key(a, b);
        _pairs.TryGetValue(k, out var r);
        r.Affinity = Math.Clamp(r.Affinity + affinity, -1f, 1f);
        r.Trust = Math.Clamp(r.Trust + trust, -1f, 1f);
        r.Debt = Math.Clamp(r.Debt + debt, -10f, 10f);
        _pairs[k] = r;
    }

    /// <summary>Drop every pair involving <paramref name="id"/> (on death). Allocation-free: Dictionary.Remove is
    /// allowed while enumerating (.NET Core 3+), and the table is never iterated by sim logic, so slot reuse order
    /// cannot leak into the sim.</summary>
    public void Forget(int id)
    {
        foreach (var k in _pairs.Keys)
            if ((int)(k >> 32) == id || (int)(uint)k == id) _pairs.Remove(k);
    }

    /// <summary>All pairs involving <paramref name="id"/>, for the inspector.</summary>
    public IEnumerable<(int other, Relationship rel)> Of(int id)
    {
        foreach (var (k, r) in _pairs)
        {
            int a = (int)(k >> 32), b = (int)(uint)k;
            if (a == id) yield return (b, r);
            else if (b == id) yield return (a, r);
        }
    }

    /// <summary>Order-independent hash so a save/load that rebuilds the dictionary keeps the same value.</summary>
    public ulong Hash(ulong h)
    {
        ulong sum = 0;
        foreach (var (k, r) in _pairs)
            sum += Eden.Sim.Hash.Combine((ulong)k, BitConverter.SingleToUInt32Bits(r.Affinity),
                BitConverter.SingleToUInt32Bits(r.Trust), BitConverter.SingleToUInt32Bits(r.Debt));
        h = Eden.Sim.Hash.Mix(h, (ulong)_pairs.Count);
        return Eden.Sim.Hash.Mix(h, sum);
    }

    public void Write(BinaryWriter w)
    {
        // Sorted so the file is canonical.
        var keys = new List<long>(_pairs.Keys);
        keys.Sort();
        w.Write(keys.Count);
        foreach (var k in keys)
        {
            var r = _pairs[k];
            w.Write(k);
            w.Write(r.Affinity);
            w.Write(r.Trust);
            w.Write(r.Debt);
        }
    }

    public void Read(BinaryReader r)
    {
        _pairs.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            long k = r.ReadInt64();
            _pairs[k] = new Relationship { Affinity = r.ReadSingle(), Trust = r.ReadSingle(), Debt = r.ReadSingle() };
        }
    }
}
