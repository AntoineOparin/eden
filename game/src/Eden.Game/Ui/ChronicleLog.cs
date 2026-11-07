using Eden.Sim.Contracts;

namespace Eden.Game;

/// <summary>Filter groups of the Chronicle timeline (M7 UI). Bit i of a filter mask is category i.</summary>
public enum ChronicleCategory
{
    Life, Trade, Raid, Discovery, Invention, Religion, Schism, Shrine, Prayer, God, KnowledgeLost, Society,
}

/// <summary>
/// The client's copy of the Chronicle: a bounded ring buffer (<see cref="Capacity"/>, default 5,000) fed from every
/// frame's snapshot. <see cref="IWorldSnapshot.NewChronicle"/> only holds what was appended since the previous
/// <em>published</em> snapshot, and the sim may publish several snapshots between two frames, so for the real sim the
/// log also merges <see cref="Eden.Sim.IMindSnapshot.RecentChronicle"/> (the last 32 entries) and drops duplicates by
/// tick and text. Bursts of more than 32 entries between two frames can still lose the oldest ones. No per-frame
/// allocation once the scratch lists have grown.
/// </summary>
public sealed class ChronicleLog
{
    public const int DefaultCapacity = 5000;
    public const int CategoryCount = 12;
    public const int AllCategories = (1 << CategoryCount) - 1;

    private readonly ChronicleEntry?[] _ring;
    private int _start, _count;
    private long _lastTick = -1;
    private readonly HashSet<string> _textsAtLastTick = new();
    private readonly List<ChronicleEntry> _merge = new();
    private IWorldSnapshot? _lastSnapshot;

    public ChronicleLog(int capacity = DefaultCapacity) => _ring = new ChronicleEntry?[Math.Max(1, capacity)];

    public int Capacity => _ring.Length;
    public int Count => _count;

    /// <summary>Bumped whenever an entry is added; panels rebuild only when it changes.</summary>
    public int Version { get; private set; }

    /// <summary>Total entries ever accepted (including the ones the ring has since dropped).</summary>
    public long Accepted { get; private set; }

    /// <summary>Entry <paramref name="i"/>, oldest first.</summary>
    public ChronicleEntry this[int i] => _ring[(_start + i) % _ring.Length]!;

    /// <summary>Takes the new entries of a frame's snapshot. Cheap when the snapshot did not change.</summary>
    public void Ingest(IWorldSnapshot snapshot)
    {
        if (ReferenceEquals(snapshot, _lastSnapshot)) return;
        if (_lastSnapshot is not null && snapshot.Tick < _lastSnapshot.Tick) // a load went back in time
        {
            _lastTick = -1;
            _textsAtLastTick.Clear();
        }
        _lastSnapshot = snapshot;
        var fresh = snapshot.NewChronicle;
        var recent = (snapshot as Eden.Sim.IMindSnapshot)?.RecentChronicle;
        if (recent is null || recent.Count == 0)
        {
            for (int i = 0; i < fresh.Count; i++) Offer(fresh[i]);
            return;
        }
        // Both lists are in tick order: merge them, then the tick/text rule drops what is already in the log.
        _merge.Clear();
        int a = 0, b = 0;
        while (a < recent.Count || b < fresh.Count)
        {
            if (b >= fresh.Count || (a < recent.Count && recent[a].Tick <= fresh[b].Tick)) _merge.Add(recent[a++]);
            else _merge.Add(fresh[b++]);
        }
        foreach (var e in _merge) Offer(e);
    }

    /// <summary>Adds one entry unless it is older than the newest seen or a duplicate at the same tick.</summary>
    public bool Offer(ChronicleEntry e)
    {
        if (e.Tick < _lastTick) return false;
        if (e.Tick == _lastTick && _textsAtLastTick.Contains(e.Text)) return false;
        if (e.Tick > _lastTick)
        {
            _lastTick = e.Tick;
            _textsAtLastTick.Clear();
        }
        _textsAtLastTick.Add(e.Text);
        if (_count < _ring.Length) _ring[(_start + _count++) % _ring.Length] = e;
        else
        {
            _ring[_start] = e;
            _start = (_start + 1) % _ring.Length;
        }
        Accepted++;
        Version++;
        return true;
    }

    public void Clear()
    {
        Array.Clear(_ring);
        _start = _count = 0;
        _lastTick = -1;
        _textsAtLastTick.Clear();
        _lastSnapshot = null;
        Version++;
    }

    /// <summary>Indices (oldest first) of entries whose category bit is in <paramref name="mask"/> with importance ≥ the threshold.</summary>
    public void Filter(int mask, int minImportance, List<int> into)
    {
        into.Clear();
        for (int i = 0; i < _count; i++)
        {
            var e = this[i];
            if (e.Importance >= minImportance && (mask & (1 << (int)CategoryOf(e.Kind))) != 0) into.Add(i);
        }
    }

    /// <summary>Which filter group a Chronicle kind belongs to. Unknown kinds count as society.</summary>
    public static ChronicleCategory CategoryOf(string kind) => kind switch
    {
        "birth" or "death" => ChronicleCategory.Life,
        "trade" => ChronicleCategory.Trade,
        "raid" => ChronicleCategory.Raid,
        "discovery" => ChronicleCategory.Discovery,
        "invention" => ChronicleCategory.Invention,
        "religion" => ChronicleCategory.Religion,
        "schism" => ChronicleCategory.Schism,
        "shrine" => ChronicleCategory.Shrine,
        "prayer" => ChronicleCategory.Prayer,
        "god" or "blessing" or "curse" or "weather" => ChronicleCategory.God,
        "knowledge-lost" => ChronicleCategory.KnowledgeLost,
        _ => ChronicleCategory.Society,
    };

    /// <summary>Short label for a filter toggle.</summary>
    public static string Label(ChronicleCategory c) => c switch
    {
        ChronicleCategory.Life => "Births & deaths",
        ChronicleCategory.KnowledgeLost => "Knowledge lost",
        ChronicleCategory.God => "God's acts",
        _ => c.ToString(),
    };
}
