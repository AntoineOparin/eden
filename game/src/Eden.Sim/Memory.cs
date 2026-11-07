namespace Eden.Sim;

public enum MemoryKind : byte
{
    Birth,
    Death,
    Gift,
    Fight,
    God,
    Discovery,
    Conversation,
    Speech,
    Belief,
    Courtship,
    Gossip,
}

/// <summary>One remembered event. Subjects are NPC ids (the god is <see cref="MemoryStore.GodId"/>).</summary>
public sealed class Memory(long tick, MemoryKind kind, int[] subjects, string text, int importance)
{
    public long Tick { get; } = tick;
    public MemoryKind Kind { get; } = kind;
    public int[] Subjects { get; } = subjects;
    public string Text { get; } = text;
    public int Importance { get; } = importance;

    /// <summary>Set lazily by Eden.Mind for thinking NPCs; the sim only stores it.</summary>
    public float[]? Embedding { get; set; }

    public static string KindName(MemoryKind k) => k switch
    {
        MemoryKind.Birth => "birth",
        MemoryKind.Death => "death",
        MemoryKind.Gift => "gift",
        MemoryKind.Fight => "fight",
        MemoryKind.God => "god",
        MemoryKind.Discovery => "discovery",
        MemoryKind.Conversation => "conversation",
        MemoryKind.Speech => "speech",
        MemoryKind.Belief => "belief",
        MemoryKind.Courtship => "courtship",
        _ => "gossip",
    };
}

/// <summary>
/// Per-NPC memory streams, indexed by NPC slot. Capped per NPC; eviction drops the memory with the lowest
/// importance × recency and folds it into per-kind counters that render as the <c>NpcMemorySummary</c> line.
/// </summary>
public sealed class MemoryStore
{
    public const int GodId = -2;
    private const int KindCount = 11;

    private List<Memory>?[] _streams = new List<Memory>?[64];
    private int[] _evicted = new int[64 * KindCount];

    /// <summary>Per slot: 1 + tick of the newest memory with importance ≥ <see cref="DramaImportance"/>, 0 = none.
    /// Derived from the streams (rebuilt on load), so neither hashed nor saved. Read by Eden.Mind's spotlight.</summary>
    private long[] _drama = new long[64];
    public const int DramaImportance = 7;
    private readonly int _cap;
    private readonly int _ticksPerDay;

    public MemoryStore(int cap, int ticksPerDay)
    {
        _cap = Math.Max(1, cap);
        _ticksPerDay = Math.Max(1, ticksPerDay);
    }

    public IReadOnlyList<Memory> Of(int slot) => slot < _streams.Length && _streams[slot] is { } l ? l : Array.Empty<Memory>();

    public void Reset(int slot)
    {
        Ensure(slot);
        _streams[slot]?.Clear();
        Array.Clear(_evicted, slot * KindCount, KindCount);
        _drama[slot] = 0;
    }

    public void Add(int slot, Memory m, long now)
    {
        Ensure(slot);
        var list = _streams[slot] ??= new List<Memory>(16);
        list.Add(m);
        if (m.Importance >= DramaImportance && m.Tick + 1 > _drama[slot]) _drama[slot] = m.Tick + 1;
        if (list.Count > _cap) Evict(slot, list, now);
    }

    /// <summary>Tick of the newest memory with importance ≥ 7 for this slot, or -1.</summary>
    public long LastDramaTick(int slot) => slot < _drama.Length ? _drama[slot] - 1 : -1;

    public bool Contains(int slot, Memory m)
    {
        if (slot >= _streams.Length || _streams[slot] is not { } list) return false;
        foreach (var x in list)
            if (x.Tick == m.Tick && x.Kind == m.Kind && x.Text == m.Text) return true;
        return false;
    }

    /// <summary>The one-line summary of everything evicted so far, or null if nothing was.</summary>
    public string? Summary(int slot)
    {
        if (slot >= _streams.Length) return null;
        var parts = new List<string>();
        for (int k = 0; k < KindCount; k++)
        {
            int n = _evicted[slot * KindCount + k];
            if (n > 0) parts.Add($"{n} {Memory.KindName((MemoryKind)k)}");
        }
        return parts.Count == 0 ? null : "Faded memories: " + string.Join(", ", parts) + ".";
    }

    private void Evict(int slot, List<Memory> list, long now)
    {
        int worst = 0;
        float worstScore = float.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            float ageDays = (now - list[i].Tick) / (float)_ticksPerDay;
            float score = list[i].Importance / (1f + ageDays / 10f);
            if (score < worstScore) { worstScore = score; worst = i; }
        }
        _evicted[slot * KindCount + (int)list[worst].Kind]++;
        list.RemoveAt(worst);
    }

    private void Ensure(int slot)
    {
        if (slot < _streams.Length) return;
        int n = _streams.Length;
        while (n <= slot) n *= 2;
        Array.Resize(ref _streams, n);
        Array.Resize(ref _evicted, n * KindCount);
        Array.Resize(ref _drama, n);
    }

    public ulong Hash(ulong h, Npcs npcs)
    {
        for (int s = 0; s < npcs.HighWater; s++)
        {
            if (!npcs.Alive[s]) continue;
            var list = Of(s);
            h = Eden.Sim.Hash.Mix(h, (ulong)list.Count);
            foreach (var m in list)
            {
                h = Eden.Sim.Hash.Mix(h, (ulong)m.Tick);
                h = Eden.Sim.Hash.Mix(h, (ulong)m.Kind);
                h = Eden.Sim.Hash.Mix(h, (ulong)m.Importance);
                h = Eden.Sim.Hash.Mix(h, StableHash.Of(m.Text));
                foreach (int id in m.Subjects) h = Eden.Sim.Hash.Mix(h, (ulong)id);
            }
            if (s < _streams.Length)
                for (int k = 0; k < KindCount; k++) h = Eden.Sim.Hash.Mix(h, (ulong)_evicted[s * KindCount + k]);
        }
        return h;
    }

    public void Write(BinaryWriter w, Npcs npcs)
    {
        for (int s = 0; s < npcs.HighWater; s++)
        {
            if (!npcs.Alive[s]) continue;
            var list = Of(s);
            w.Write(list.Count);
            foreach (var m in list)
            {
                w.Write(m.Tick);
                w.Write((byte)m.Kind);
                w.Write((byte)m.Importance);
                w.Write(m.Text);
                w.Write(m.Subjects.Length);
                foreach (int id in m.Subjects) w.Write(id);
                w.Write(m.Embedding?.Length ?? -1);
                if (m.Embedding is { } e) foreach (float f in e) w.Write(f);
            }
            for (int k = 0; k < KindCount; k++) w.Write(s < _streams.Length ? _evicted[s * KindCount + k] : 0);
        }
    }

    public void Read(BinaryReader r, Npcs npcs)
    {
        _streams = new List<Memory>?[Math.Max(64, npcs.Capacity)];
        _evicted = new int[_streams.Length * KindCount];
        _drama = new long[_streams.Length];
        for (int s = 0; s < npcs.HighWater; s++)
        {
            if (!npcs.Alive[s]) continue;
            int n = r.ReadInt32();
            var list = _streams[s] = new List<Memory>(Math.Max(16, n));
            for (int i = 0; i < n; i++)
            {
                long tick = r.ReadInt64();
                var kind = (MemoryKind)r.ReadByte();
                int importance = r.ReadByte();
                string text = r.ReadString();
                var subjects = new int[r.ReadInt32()];
                for (int j = 0; j < subjects.Length; j++) subjects[j] = r.ReadInt32();
                var m = new Memory(tick, kind, subjects, text, importance);
                int el = r.ReadInt32();
                if (el >= 0)
                {
                    var e = new float[el];
                    for (int j = 0; j < el; j++) e[j] = r.ReadSingle();
                    m.Embedding = e;
                }
                list.Add(m);
                if (m.Importance >= DramaImportance && m.Tick + 1 > _drama[s]) _drama[s] = m.Tick + 1;
            }
            for (int k = 0; k < KindCount; k++) _evicted[s * KindCount + k] = r.ReadInt32();
        }
    }
}
