using Eden.Sim.Contracts;

namespace Eden.Game;

/// <summary>
/// What each settlement knows and owns (the settlement panel and the knowledge-spread overlay), from the sim's
/// <see cref="SettlementDetail"/> (C1; this replaced the M7 UI pass's sample of up to 8 members' NPC details). The sim
/// builds details only for a bounded watch list (8), so this asks for at most <see cref="MaxOutstanding"/> settlements
/// at a time (<see cref="WatchSettlementCommand"/> through <see cref="Enqueue"/> when set, plus the request a missed
/// <see cref="IWorldSnapshot.TryGetSettlementDetail"/> makes by itself), polls until they answer or
/// <see cref="RequestSeconds"/> pass, and caches each answer per in-game day with a real-time floor
/// (<see cref="MinRefreshSeconds"/>) so 500× does not re-ask forever. Driven by <see cref="Update"/> on a timer.
/// </summary>
public sealed class SettlementKnowledge
{
    public sealed class Entry
    {
        public required SettlementDetail Detail;
        public long Day;
        public double RealTime;

        /// <summary>Concept id → the detail's row (holders, best proficiency, record marker).</summary>
        public readonly Dictionary<string, SettlementConceptView> Concepts = new(StringComparer.Ordinal);

        public int SettlementId => Detail.Id;
        public int Members => Detail.Members;

        /// <summary>The first <paramref name="n"/> concepts, most holders first (the sim's order).</summary>
        public IReadOnlyList<SettlementConceptView> Top(int n)
        {
            var all = Detail.Concepts;
            if (all.Count <= n) return all;
            var list = new List<SettlementConceptView>(n);
            for (int i = 0; i < n; i++) list.Add(all[i]);
            return list;
        }

        /// <summary>Concepts with a written record on the settlement's land.</summary>
        public int Recorded
        {
            get
            {
                int r = 0;
                foreach (var c in Detail.Concepts) if (c.HasRecord) r++;
                return r;
            }
        }
    }

    /// <summary>Requests in flight at once; below the sim's watch list (8) so the panel's settlement is never evicted.</summary>
    public int MaxOutstanding { get; set; } = 4;
    public double RequestSeconds { get; set; } = 3;
    public double MinRefreshSeconds { get; set; } = 6;
    public int TicksPerDay { get; set; } = 240;

    /// <summary>Sends <see cref="WatchSettlementCommand"/> to the host when a request starts (Main sets it).</summary>
    public Action<WorldCommand>? Enqueue { get; set; }

    private readonly Dictionary<int, Entry> _cache = new();
    private readonly List<int> _queue = new();
    private readonly List<(int Id, double Started)> _pending = new();
    private double _clock;

    /// <summary>Bumped when an entry arrives; consumers redraw only when it changes.</summary>
    public int Version { get; private set; }

    public Entry? Get(int settlementId) => _cache.TryGetValue(settlementId, out var e) ? e : null;

    /// <summary>True while a request for the settlement is in flight.</summary>
    public bool IsRequesting(int settlementId)
    {
        foreach (var p in _pending) if (p.Id == settlementId) return true;
        return false;
    }

    /// <summary>Ask for a settlement; <paramref name="first"/> puts it at the front of the queue (the open panel).</summary>
    public void Want(int settlementId, bool first = false)
    {
        if (settlementId < 0 || IsRequesting(settlementId)) return;
        int at = _queue.IndexOf(settlementId);
        if (at >= 0)
        {
            if (!first || at == 0) return;
            _queue.RemoveAt(at);
        }
        if (first) _queue.Insert(0, settlementId);
        else _queue.Add(settlementId);
    }

    /// <summary>True when the cached entry is missing or older than the current in-game day (and the real-time floor).</summary>
    public bool IsStale(int settlementId, long tick)
    {
        if (!_cache.TryGetValue(settlementId, out var e)) return true;
        return e.Day != tick / Math.Max(1, TicksPerDay) && _clock - e.RealTime >= MinRefreshSeconds;
    }

    /// <summary>Starts queued requests and collects answers. Call a few times per second with the frame's snapshot.</summary>
    public void Update(IWorldSnapshot s, double realSeconds)
    {
        _clock += realSeconds;
        while (_pending.Count < Math.Max(1, MaxOutstanding) && _queue.Count > 0)
        {
            int id = _queue[0];
            _queue.RemoveAt(0);
            if (!IsStale(id, s.Tick)) continue;
            Enqueue?.Invoke(new WatchSettlementCommand(id));
            _pending.Add((id, _clock));
        }
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var (id, started) = _pending[i];
            if (s.TryGetSettlementDetail(id, out var d)) // a miss asks the sim to watch it
            {
                var e = new Entry { Detail = d, Day = s.Tick / Math.Max(1, TicksPerDay), RealTime = _clock };
                foreach (var c in d.Concepts) e.Concepts[c.ConceptId] = c;
                _cache[id] = e;
                _pending.RemoveAt(i);
                Version++;
            }
            else if (_clock - started >= RequestSeconds) _pending.RemoveAt(i); // abandoned, or the sim is paused: asked again later
        }
    }
}
