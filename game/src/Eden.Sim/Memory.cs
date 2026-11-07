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

/// <summary>How a retold (gossip) memory was changed on its last hop (M7 myth).</summary>
public enum MemoryDistortion : byte { None, Subject, Importance, Attribution }

/// <summary>One remembered event. Subjects are NPC ids (the god is <see cref="MemoryStore.GodId"/>).</summary>
public sealed class Memory(long tick, MemoryKind kind, int[] subjects, string text, int importance)
{
    public long Tick { get; } = tick;
    public MemoryKind Kind { get; } = kind;
    public int[] Subjects { get; } = subjects;
    public string Text { get; } = text;
    public int Importance { get; } = importance;

    /// <summary>M7: how many retellings this memory is from the event (0 = witnessed or felt first-hand).</summary>
    public byte Generation { get; internal set; }

    /// <summary>M7: what the last retelling changed (None for first-hand memories and faithful retellings).</summary>
    public MemoryDistortion Distortion { get; internal set; }

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

    /// <summary>M7, per slot: 1 + tick of the newest belief mentioning the god, and of the newest memory about the god
    /// (a god act, or anything naming the god as a subject). Derived from the streams like <see cref="_drama"/> but kept
    /// exact on eviction, because religion emergence reads them. Not saved; rebuilt on load.</summary>
    private long[] _faith = new long[64], _godSeen = new long[64];
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
        _faith[slot] = _godSeen[slot] = 0;
    }

    public void Add(int slot, Memory m, long now)
    {
        Ensure(slot);
        // Full capacity up front (once per slot; Reset keeps the list): growing 16 → 1,024 cost an array copy per doubling.
        var list = _streams[slot] ??= new List<Memory>(_cap + 1);
        list.Add(m);
        if (m.Importance >= DramaImportance && m.Tick + 1 > _drama[slot]) _drama[slot] = m.Tick + 1;
        NoteGod(slot, m);
        if (list.Count > _cap) Evict(slot, list, now);
    }

    /// <summary>Tick of the slot's newest belief that mentions the god (M7 "faithful"), or -1.</summary>
    public long LastGodBeliefTick(int slot) => slot < _faith.Length ? _faith[slot] - 1 : -1;

    /// <summary>Tick of the slot's newest memory about the god (seen, felt or heard), or -1.</summary>
    public long LastGodMemoryTick(int slot) => slot < _godSeen.Length ? _godSeen[slot] - 1 : -1;

    public static bool IsGodBelief(Memory m) => m.Kind == MemoryKind.Belief && GodStanding.MentionsGod(m.Text);

    public static bool IsAboutGod(Memory m) => m.Kind == MemoryKind.God || Array.IndexOf(m.Subjects, GodId) >= 0;

    private void NoteGod(int slot, Memory m)
    {
        if (IsGodBelief(m) && m.Tick + 1 > _faith[slot]) _faith[slot] = m.Tick + 1;
        if (IsAboutGod(m) && m.Tick + 1 > _godSeen[slot]) _godSeen[slot] = m.Tick + 1;
    }

    /// <summary>True if the slot already remembers a story ending in <paramref name="core"/> (any teller, any retelling).</summary>
    public bool ContainsStory(int slot, ReadOnlySpan<char> core)
    {
        if (slot >= _streams.Length || _streams[slot] is not { } list) return false;
        foreach (var x in list)
            if (x.Text.AsSpan().EndsWith(core, StringComparison.Ordinal)) return true;
        return false;
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
        var gone = list[worst];
        list.RemoveAt(worst);
        if (IsGodBelief(gone) || IsAboutGod(gone))
        {
            _faith[slot] = _godSeen[slot] = 0;
            foreach (var m in list) NoteGod(slot, m);
        }
    }

    private void Ensure(int slot)
    {
        if (slot < _streams.Length) return;
        int n = _streams.Length;
        while (n <= slot) n *= 2;
        Array.Resize(ref _streams, n);
        Array.Resize(ref _evicted, n * KindCount);
        Array.Resize(ref _drama, n);
        Array.Resize(ref _faith, n);
        Array.Resize(ref _godSeen, n);
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

    /// <summary>
    /// MEMS section version 2 (M8): a table of distinct memories, then per living NPC its stream as table indices.
    /// One event is remembered by every witness as the same <see cref="Memory"/> object, and version 1 wrote its text
    /// once per holder (16 of the 21 MB of a year-120 save). Memories are merged when they are the same object or equal
    /// in every saved field (a version-1 load had split them), and a load shares one object per table entry again.
    /// Integers are 7-bit varints; subject ids are zigzag varints (the god is -2).
    /// </summary>
    public const short SectionVersion = 2;

    public void Write(BinaryWriter w, Npcs npcs)
    {
        var table = new List<Memory>();
        var index = new Dictionary<Memory, int>(SavedFieldsComparer.Instance);
        var streams = new List<int>();
        for (int s = 0; s < npcs.HighWater; s++)
        {
            if (!npcs.Alive[s]) continue;
            var list = Of(s);
            streams.Add(list.Count);
            foreach (var m in list)
            {
                if (!index.TryGetValue(m, out int i)) { i = table.Count; index.Add(m, i); table.Add(m); }
                streams.Add(i);
            }
        }
        w.Write7BitEncodedInt(table.Count);
        foreach (var m in table)
        {
            w.Write7BitEncodedInt64(m.Tick);
            w.Write((byte)m.Kind);
            w.Write((byte)m.Importance);
            w.Write(m.Text);
            w.Write7BitEncodedInt(m.Subjects.Length);
            foreach (int id in m.Subjects) w.Write7BitEncodedInt((id << 1) ^ (id >> 31));
            w.Write7BitEncodedInt((m.Embedding?.Length ?? -1) + 1);
            if (m.Embedding is { } e) foreach (float f in e) w.Write(f);
        }
        int p = 0;
        for (int s = 0; s < npcs.HighWater; s++)
        {
            if (!npcs.Alive[s]) continue;
            int count = streams[p++];
            w.Write7BitEncodedInt(count);
            for (int i = 0; i < count; i++) w.Write7BitEncodedInt(streams[p++]);
            for (int k = 0; k < KindCount; k++) w.Write7BitEncodedInt(s < _streams.Length ? _evicted[s * KindCount + k] : 0);
        }
    }

    public void Read(BinaryReader r, Npcs npcs, short version = SectionVersion)
    {
        if (version > SectionVersion) throw new InvalidDataException($"MEMS section version {version} is newer than this build");
        _streams = new List<Memory>?[Math.Max(64, npcs.Capacity)];
        _evicted = new int[_streams.Length * KindCount];
        _drama = new long[_streams.Length];
        _faith = new long[_streams.Length];
        _godSeen = new long[_streams.Length];
        if (version < 2) { ReadVersion1(r, npcs); return; }
        long remaining = r.BaseStream.Length - r.BaseStream.Position;
        int unique = r.Read7BitEncodedInt();
        if (unique < 0 || unique > remaining) throw new InvalidDataException("MEMS: bad memory count");
        var table = new Memory[unique];
        for (int i = 0; i < unique; i++)
        {
            long tick = r.Read7BitEncodedInt64();
            var kind = (MemoryKind)r.ReadByte();
            int importance = r.ReadByte();
            string text = r.ReadString();
            int ns = r.Read7BitEncodedInt();
            if (ns < 0 || ns > 1 << 16) throw new InvalidDataException("MEMS: bad subject count");
            var subjects = ns == 0 ? Array.Empty<int>() : new int[ns];
            for (int j = 0; j < ns; j++) { uint z = (uint)r.Read7BitEncodedInt(); subjects[j] = (int)(z >> 1) ^ -(int)(z & 1); }
            var m = new Memory(tick, kind, subjects, text, importance);
            int el = r.Read7BitEncodedInt() - 1;
            if (el > 1 << 16) throw new InvalidDataException("MEMS: bad embedding length");
            if (el >= 0)
            {
                var e = new float[el];
                for (int j = 0; j < el; j++) e[j] = r.ReadSingle();
                m.Embedding = e;
            }
            table[i] = m;
        }
        for (int s = 0; s < npcs.HighWater; s++)
        {
            if (!npcs.Alive[s]) continue;
            int n = r.Read7BitEncodedInt();
            if (n < 0 || n > Math.Max(_cap, 1) * 4) throw new InvalidDataException("MEMS: bad stream length");
            var list = _streams[s] = new List<Memory>(Math.Max(_cap + 1, n));
            for (int i = 0; i < n; i++)
            {
                int at = r.Read7BitEncodedInt();
                if ((uint)at >= (uint)table.Length) throw new InvalidDataException("MEMS: bad memory index");
                AddLoaded(s, list, table[at]);
            }
            for (int k = 0; k < KindCount; k++) _evicted[s * KindCount + k] = r.Read7BitEncodedInt();
        }
    }

    private void ReadVersion1(BinaryReader r, Npcs npcs)
    {
        for (int s = 0; s < npcs.HighWater; s++)
        {
            if (!npcs.Alive[s]) continue;
            int n = r.ReadInt32();
            if (n < 0 || n > Math.Max(_cap, 1) * 4) throw new InvalidDataException("MEMS: bad stream length");
            var list = _streams[s] = new List<Memory>(Math.Max(_cap + 1, n));
            for (int i = 0; i < n; i++)
            {
                long tick = r.ReadInt64();
                var kind = (MemoryKind)r.ReadByte();
                int importance = r.ReadByte();
                string text = r.ReadString();
                int ns = r.ReadInt32();
                if (ns < 0 || ns > 1 << 16) throw new InvalidDataException("MEMS: bad subject count");
                var subjects = new int[ns];
                for (int j = 0; j < subjects.Length; j++) subjects[j] = r.ReadInt32();
                var m = new Memory(tick, kind, subjects, text, importance);
                int el = r.ReadInt32();
                if (el > 1 << 16) throw new InvalidDataException("MEMS: bad embedding length");
                if (el >= 0)
                {
                    var e = new float[el];
                    for (int j = 0; j < el; j++) e[j] = r.ReadSingle();
                    m.Embedding = e;
                }
                AddLoaded(s, list, m);
            }
            for (int k = 0; k < KindCount; k++) _evicted[s * KindCount + k] = r.ReadInt32();
        }
    }

    private void AddLoaded(int s, List<Memory> list, Memory m)
    {
        list.Add(m);
        if (m.Importance >= DramaImportance && m.Tick + 1 > _drama[s]) _drama[s] = m.Tick + 1;
        NoteGod(s, m);
    }

    /// <summary>Equality over every field the save keeps (and the MYTH section's generation and distortion).</summary>
    private sealed class SavedFieldsComparer : IEqualityComparer<Memory>
    {
        public static readonly SavedFieldsComparer Instance = new();

        public bool Equals(Memory? a, Memory? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return false;
            return a.Tick == b.Tick && a.Kind == b.Kind && a.Importance == b.Importance && a.Generation == b.Generation
                && a.Distortion == b.Distortion && a.Text == b.Text && a.Subjects.AsSpan().SequenceEqual(b.Subjects)
                && (ReferenceEquals(a.Embedding, b.Embedding)
                    || (a.Embedding is { } ea && b.Embedding is { } eb && ea.AsSpan().SequenceEqual(eb)));
        }

        public int GetHashCode(Memory m) => HashCode.Combine(m.Tick, m.Text, m.Kind, m.Subjects.Length);
    }
}
