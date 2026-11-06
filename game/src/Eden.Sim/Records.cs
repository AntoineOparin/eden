namespace Eden.Sim;

/// <summary>
/// One place where knowledge is written down: an archive building (M3 passes its id) or, before buildings
/// exist, a record site a writer founded where they stood (<see cref="BuildingId"/> = -1). Entries map
/// concept id → written proficiency. Records outlive their writers; that is the point of writing.
/// </summary>
public sealed class WrittenRecord
{
    public int Id { get; init; }
    public float X { get; init; }
    public float Z { get; init; }
    public int BuildingId { get; init; } = -1;
    public Dictionary<string, float> Entries { get; } = new(StringComparer.Ordinal);
}

/// <summary>All written records in the world. Few in number, so lookups are linear scans.</summary>
public sealed class RecordStore
{
    private readonly List<WrittenRecord> _records = new();
    private int _nextId = 1;

    public IReadOnlyList<WrittenRecord> All => _records;
    public int Count => _records.Count;

    public WrittenRecord Create(float x, float z, int buildingId = -1)
    {
        var r = new WrittenRecord { Id = _nextId++, X = x, Z = z, BuildingId = buildingId };
        _records.Add(r);
        return r;
    }

    public bool Remove(int recordId)
    {
        int i = _records.FindIndex(r => r.Id == recordId);
        if (i < 0) return false;
        _records.RemoveAt(i);
        return true;
    }

    public void Clear()
    {
        _records.Clear();
        _nextId = 1;
    }

    /// <summary>Nearest record within <paramref name="radius"/>, or null. Allocation-free.</summary>
    public WrittenRecord? Nearest(float x, float z, float radius)
    {
        WrittenRecord? best = null;
        float bestD2 = radius * radius;
        for (int i = 0; i < _records.Count; i++)
        {
            var r = _records[i];
            float dx = r.X - x, dz = r.Z - z, d2 = dx * dx + dz * dz;
            if (d2 <= bestD2) { bestD2 = d2; best = r; }
        }
        return best;
    }

    /// <summary>How many records hold <paramref name="conceptId"/>.</summary>
    public int Holding(string conceptId)
    {
        int n = 0;
        for (int i = 0; i < _records.Count; i++) if (_records[i].Entries.ContainsKey(conceptId)) n++;
        return n;
    }

    public ulong Hash(ulong h)
    {
        h = Eden.Sim.Hash.Mix(h, (ulong)_nextId);
        h = Eden.Sim.Hash.Mix(h, (ulong)_records.Count);
        foreach (var r in _records)
        {
            h = Eden.Sim.Hash.Mix(h, (ulong)r.Id);
            h = Eden.Sim.Hash.Mix(h, r.X);
            h = Eden.Sim.Hash.Mix(h, r.Z);
            h = Eden.Sim.Hash.Mix(h, (ulong)r.BuildingId);
            ulong sum = 0;
            foreach (var (id, p) in r.Entries) sum += Eden.Sim.Hash.Combine(StableHash.Of(id), BitConverter.SingleToUInt32Bits(p));
            h = Eden.Sim.Hash.Mix(h, sum);
        }
        return h;
    }

    public void Write(BinaryWriter w)
    {
        w.Write(_nextId);
        w.Write(_records.Count);
        foreach (var r in _records)
        {
            w.Write(r.Id); w.Write(r.X); w.Write(r.Z); w.Write(r.BuildingId);
            var keys = new List<string>(r.Entries.Keys);
            keys.Sort(StringComparer.Ordinal);
            w.Write(keys.Count);
            foreach (var k in keys) { w.Write(k); w.Write(r.Entries[k]); }
        }
    }

    public void Read(BinaryReader rd)
    {
        _records.Clear();
        _nextId = rd.ReadInt32();
        int n = rd.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var r = new WrittenRecord { Id = rd.ReadInt32(), X = rd.ReadSingle(), Z = rd.ReadSingle(), BuildingId = rd.ReadInt32() };
            int m = rd.ReadInt32();
            for (int j = 0; j < m; j++) r.Entries[rd.ReadString()] = rd.ReadSingle();
            _records.Add(r);
        }
    }
}
