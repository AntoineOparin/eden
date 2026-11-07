using Eden.Sim.Contracts;

namespace Eden.Game;

/// <summary>Filter groups of the Chronicle timeline (M7 UI). Bit i of a filter mask is category i.</summary>
public enum ChronicleCategory
{
    Life, Trade, Raid, Discovery, Invention, Religion, Schism, Shrine, Prayer, God, KnowledgeLost, Society,
}

/// <summary>
/// The client's copy of the Chronicle: a bounded ring buffer (<see cref="Capacity"/>, default 5,000) fed from every
/// frame's snapshot. C1: each new snapshot is read through <see cref="IWorldSnapshot.ChronicleSince"/> from the last
/// tick seen − 1 (god commands can add entries at the tick of an already published snapshot), so entries published
/// in several snapshots between two frames are all kept, and duplicates are dropped by tick and text. The first
/// snapshot (and the first one after a load, recognised by its "load" entry) brings the sim's whole history, up to its
/// 2,048 entries, including what a loaded save carried. <see cref="IWorldSnapshot.NewChronicle"/> is still offered
/// after it, for snapshots that keep no history.
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
        var since = snapshot.ChronicleSince(_lastTick < 0 ? long.MinValue : _lastTick - 1);
        if (HasNewLoad(since))
        {
            // The sim replaced its world (and its history, from the save): start again from what it holds now.
            Clear();
            _lastSnapshot = snapshot;
            since = snapshot.ChronicleSince(long.MinValue);
        }
        for (int i = 0; i < since.Count; i++) Offer(since[i]);
        var fresh = snapshot.NewChronicle;
        for (int i = 0; i < fresh.Count; i++) Offer(fresh[i]);
    }

    private bool HasNewLoad(IReadOnlyList<ChronicleEntry> since)
    {
        if (_count == 0) return false;
        for (int i = 0; i < since.Count; i++)
        {
            var e = since[i];
            if (e.Kind == "load" && (e.Tick > _lastTick || (e.Tick == _lastTick && !_textsAtLastTick.Contains(e.Text)))) return true;
        }
        return false;
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
