using System.Collections.Concurrent;
using Eden.Sim.Contracts;

namespace Eden.Sim;

public enum DeathCause : byte { OldAge, Starvation, Thirst, Accident, Childbirth, Lightning, Wounds }

/// <summary>
/// The whole simulation state and its tick function. Single-threaded by design: <see cref="SimHost"/>
/// owns the one thread that may call <see cref="Step"/>. Split across partial files:
/// World.Life.cs (needs, ageing, birth, death), World.Behaviour.cs (utility AI, actions, movement),
/// World.Commands.cs (god commands, intentions, events, memory), World.Persistence.cs (save/load, hash).
/// </summary>
public sealed partial class World
{
    public SimConfig Config { get; private set; }
    public long Tick { get; private set; }
    public Rng Rng;

    public Terrain Terrain { get; private set; }
    public ResourceNodes Resources { get; private set; }
    public Npcs Npcs { get; private set; }
    public MemoryStore Memories { get; private set; }
    public Relationships Relationships { get; } = new();
    public EventBus Events { get; } = new();
    public IntentionValidator Validator { get; }

    /// <summary>Totals since genesis, for the console runner and tests. Part of the hashed state.</summary>
    public int TotalBirths { get; private set; }
    public int TotalDeaths { get; private set; }

    /// <summary>Deepest generation number ever born (the starting NPCs are generation 0).</summary>
    public int MaxLineage { get; private set; }

    private SpatialGrid _grid;
    private readonly List<int> _pendingBirths = new(64);
    private readonly List<ChronicleEntry> _pendingChronicle = new();
    /// <summary>C1: bounded Chronicle history behind <see cref="ChronicleTail"/> and <see cref="IWorldSnapshot.ChronicleSince"/>; saved (World.ChronicleSave.cs).</summary>
    public const int ChronicleHistorySize = 2048;
    private readonly ChronicleEntry[] _history = new ChronicleEntry[ChronicleHistorySize];
    private int _historyCount, _historyNext;
    private ChronicleEntry[]? _publishedHistory; // oldest first, shared by snapshots until the next entry
    private readonly List<string> _log = new();
    private readonly ConcurrentQueue<int> _detailRequests = new();
    private readonly List<int> _watched = new();
    private const int MaxWatched = 32;

    // Snapshots share one immutable copy of the terrain until terraforming invalidates it.
    private float[]? _publishedHeights;
    private byte[]? _publishedBiomes;

    public World(SimConfig config)
    {
        Config = config;
        Rng = new Rng((ulong)config.Seed);
        (Terrain, Resources) = WorldGen.Generate(config, ref Rng);
        Npcs = new Npcs(64);
        Memories = new MemoryStore(config.MemoryCap, config.TicksPerDay);
        _knowledge = new Dictionary<string, float>?[64];
        InitKnowledge();
        _grid = new SpatialGrid(Terrain.MaxX + Terrain.CellSize, Terrain.MaxZ + Terrain.CellSize, 32f);
        Validator = new IntentionValidator(this);
        Events.Subscribe(RecordEvent);
        InitSociety();
        InitIntegration();

        var (sx, sz) = WorldGen.PickStart(Terrain, Resources, ref Rng);
        var names = new List<string>();
        for (int i = 0; i < config.StartingNpcs; i++)
        {
            var sex = i switch { 0 => Sex.Female, 1 => Sex.Male, _ => Rng.Chance(0.5f) ? Sex.Female : Sex.Male };
            float age = 17 + Rng.NextFloat() * 5;
            int slot = SpawnAdult(sx + Rng.NextFloat() * 4 - 2, sz + Rng.NextFloat() * 4 - 2, sex, age);
            names.Add(Npcs.Name[slot]);
        }
        _grid.Rebuild(Npcs);
        string who = names.Count == 0 ? "No one" : string.Join(" and ", names);
        Chronicle(0, "genesis", $"The world was seeded with {config.Seed}. {who} awoke on the island.", 10, Array.Empty<int>());
    }

    public SimClock Clock => new(Tick, Config);
    public int Population => Npcs.Count;

    /// <summary>Advance one fixed step. Allocation-free except when something chronicle-worthy happens.</summary>
    public void Step()
    {
        Tick++;
        var cfg = Config;
        if (Tick % cfg.TicksPerDay == 0) DailyUpdate();
        _grid.Rebuild(Npcs);

        float hour = (float)(Tick % cfg.TicksPerDay) / cfg.TicksPerHour;
        bool night = hour >= 21f || hour < 5f;
        int hw = Npcs.HighWater;
        for (int s = 0; s < hw; s++)
        {
            if (!Npcs.Alive[s]) continue;
            UpdateNpc(s, night);
        }
        for (int i = 0; i < _pendingBirths.Count; i++) GiveBirth(_pendingBirths[i]);
        _pendingBirths.Clear();
    }

    private void DailyUpdate()
    {
        float food = Weather switch { WeatherKind.Rain => 1.5f, WeatherKind.Drought => 0.3f, _ => 1f };
        Resources.RegenDay(food);
        if (Weather != WeatherKind.Clear && Tick >= WeatherUntil)
        {
            Weather = WeatherKind.Clear;
            Chronicle(Tick, "weather", "The skies cleared.", 2, Array.Empty<int>());
        }
        SocietyDaily();
    }

    public LifeStage StageOf(int slot)
    {
        float years = AgeYears(slot);
        if (years < 3) return LifeStage.Infant;
        if (years < Config.AdultAgeYears) return LifeStage.Child;
        return years >= Math.Max(50f, Npcs.LifespanYears[slot] - 12f) ? LifeStage.Elder : LifeStage.Adult;
    }

    public float AgeYears(int slot) => (Tick - Npcs.BirthTick[slot]) / (float)Config.TicksPerYear;

    // ---- Chronicle --------------------------------------------------------------------------

    public void Chronicle(long tick, string kind, string text, int importance, IReadOnlyList<int> subjects)
    {
        var e = new ChronicleEntry(tick, kind, text, importance, subjects);
        _pendingChronicle.Add(e);
        _history[_historyNext] = e;
        _historyNext = (_historyNext + 1) % _history.Length;
        if (_historyCount < _history.Length) _historyCount++;
        _publishedHistory = null;
    }

    /// <summary>The last <paramref name="max"/> Chronicle entries, oldest first (bounded history, not drained by snapshots).</summary>
    public IReadOnlyList<ChronicleEntry> ChronicleTail(int max)
    {
        int n = Math.Min(max, _historyCount);
        var result = new ChronicleEntry[n];
        for (int i = 0; i < n; i++)
            result[i] = _history[(_historyNext - n + i + _history.Length) % _history.Length];
        return result;
    }

    // ---- Snapshot ---------------------------------------------------------------------------

    /// <summary>Immutable copy for the renderer; drains pending Chronicle entries. Not on the tick path.</summary>
    public WorldSnapshot Snapshot()
    {
        var chronicle = _pendingChronicle.ToArray();
        _pendingChronicle.Clear();
        var clock = Clock;

        var views = new NpcView[Npcs.Count];
        var versions = new int[Npcs.HighWater];
        int k = 0;
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            versions[s] = Npcs.Alive[s] ? Npcs.Version[s] : -Npcs.Version[s];
            if (!Npcs.Alive[s]) continue;
            float x = Npcs.X[s], z = Npcs.Z[s];
            int id = Npcs.IdOf(s);
            views[k++] = new NpcView(id, x, MathF.Max(Terrain.HeightAt(x, z), Config.SeaLevel - 0.8f), z,
                Npcs.Heading[s], Npcs.Sex[s], StageOf(s), PoseOf(s),
                (byte)Math.Clamp((int)(Npcs.Health[s] * 255f), 0, 255), SettlementOfSlot(s), _thinking.Contains(id), id == _possessed);
        }

        while (_detailRequests.TryDequeue(out int req))
        {
            _watched.Remove(req);
            _watched.Add(req);
            if (_watched.Count > MaxWatched) _watched.RemoveAt(0);
        }
        var details = new Dictionary<int, NpcDetail>();
        for (int i = _watched.Count - 1; i >= 0; i--)
        {
            if (TryBuildDetail(_watched[i], out var d)) details[_watched[i]] = d;
            else _watched.RemoveAt(i);
        }
        _publishedHistory ??= (ChronicleEntry[])ChronicleTail(_historyCount);

        return new WorldSnapshot(
            Tick, clock.Year, clock.DayOfYear, clock.HourOfDay,
            new WorldInfo(Config.Seed, Config.WorldWidth, Config.WorldHeight, Config.CellSize, Config.SeaLevel, Config.ChunkSize),
            _publishedHeights ??= (float[])Terrain.Heights.Clone(), _publishedBiomes ??= (byte[])Terrain.Biomes.Clone(),
            views, BuildingViews(), chronicle, details, versions, _detailRequests, MindExtras(), ResourceViews(), SettlementViews())
        {
            History = _publishedHistory,
            SettlementDetails = SettlementDetails(),
            SettlementRequests = _settlementRequests,
        };
    }

    /// <summary>
    /// Living settlements with the god's standing for the M7 UI (contract <see cref="SettlementView"/>). Read-only:
    /// the same numbers as <see cref="GodStandingViews"/>, with ids instead of names. Not on the tick path.
    /// </summary>
    private SettlementView[] SettlementViews()
    {
        var st = Settlements;
        var g = Standing;
        g.Ensure(st.Count);
        int alive = 0;
        for (int k = 0; k < st.Count; k++) if (st.Alive[k]) alive++;
        var views = new SettlementView[alive];
        var food = SettlementFood();
        float territory = TerritoryRadius;
        int i = 0;
        for (int k = 0; k < st.Count; k++)
        {
            if (!st.Alive[k]) continue;
            int r = ReligionOfSettlement(k);
            int envied = st.IsAlive(g.EnvyOf[k]) && g.Envy[k] > 0.05f ? g.EnvyOf[k] : -1;
            int feared = st.IsAlive(g.FearOf[k]) && g.Fear[k] > 0.05f ? g.FearOf[k] : -1;
            views[i++] = new SettlementView(k, st.Name[k], st.CenterX[k], st.CenterZ[k], st.Members[k],
                Factions.IsAlive(st.Faction[k]) ? st.Faction[k] : -1, Npcs.IsAlive(st.Leader[k]) ? st.Leader[k] : -1,
                g.Favour[k], g.Awe[k], g.Envy[k], g.Fear[k], envied, feared,
                (Contracts.GodStance)(byte)g.StanceOf(k, Config), r >= 0 ? Religions.Name[r] : null, food[k], territory);
        }
        return views;
    }

    /// <summary>
    /// Resource nodes then finished farm plots (kind "grain", amount = crop growth) for the renderer. Read-only:
    /// nothing here touches sim state or the RNG. Node ids are node indices (nodes are never removed); crop ids are
    /// <see cref="CropViewIdBase"/> + building id.
    /// </summary>
    private ResourceView[] ResourceViews()
    {
        var r = Resources;
        var b = Buildings;
        int crops = 0;
        for (int i = 0; i < b.HighWater; i++)
            if (b.Alive[i] && b.Kind[i] == BuildingKinds.FarmPlot && b.Progress[i] >= 1f) crops++;
        var views = new ResourceView[r.Count + crops];
        for (int i = 0; i < r.Count; i++)
        {
            float x = r.X[i], z = r.Z[i];
            float amount = r.Max[i] > 0 ? Math.Clamp(r.Amount[i] / r.Max[i], 0f, 1f) : 0f;
            uint h = (uint)i * 2654435761u ^ (uint)Config.Seed * 40503u;
            views[i] = new ResourceView(i, ResourceKindIds[(int)r.Kind[i]], x, Terrain.HeightAt(x, z), z, amount, (byte)(h >> 24));
        }
        int k = r.Count;
        for (int i = 0; i < b.HighWater && k < views.Length; i++)
        {
            if (!b.Alive[i] || b.Kind[i] != BuildingKinds.FarmPlot || b.Progress[i] < 1f) continue;
            float growth = b.Crop[i] switch { CropStage.Ripe => 1f, CropStage.Growing => Math.Clamp(b.Growth[i], 0.05f, 0.99f), _ => 0f };
            views[k++] = new ResourceView(CropViewIdBase + i, "grain", b.X[i], Terrain.HeightAt(b.X[i], b.Z[i]), b.Z[i], growth, (byte)(i * 37));
        }
        return views;
    }

    /// <summary>Id offset of farm-plot crops in <see cref="IWorldSnapshot.Resources"/>.</summary>
    public const int CropViewIdBase = 1 << 24;

    private static readonly string[] ResourceKindIds = ["tree", "stone", "ore", "berry", "game"];

    /// <summary>Read-only extras for Eden.Mind (spotlight, prompts); see <see cref="IMindSnapshot"/>.</summary>
    private MindExtras MindExtras()
    {
        var st = Settlements;
        var names = new string?[st.Count];
        var leaders = new List<int>();
        for (int k = 0; k < st.Count; k++)
        {
            if (!st.Alive[k]) continue;
            names[k] = st.Name[k];
            if (Npcs.IsAlive(st.Leader[k])) leaders.Add(st.Leader[k]);
        }
        var drama = new long[Npcs.HighWater];
        for (int s = 0; s < drama.Length; s++) drama[s] = Npcs.Alive[s] ? Memories.LastDramaTick(s) : -1;
        return new MindExtras(Config.TicksPerHour, Config.HoursPerDay, leaders.ToArray(), names, drama, ChronicleTail(32))
            { GodStandings = GodStandingViews() };
    }

    private NpcPose PoseOf(int s) => Npcs.Phase[s] switch
    {
        ActionPhase.Moving => NpcPose.Walking,
        ActionPhase.Acting => Npcs.Action[s] switch
        {
            ActionKind.Rest => NpcPose.Sleeping,
            ActionKind.Gather or ActionKind.Hunt or ActionKind.Build or ActionKind.Farm or ActionKind.Craft => NpcPose.Working,
            ActionKind.Raid => NpcPose.Fighting,
            _ => NpcPose.Idle,
        },
        _ => NpcPose.Idle,
    };

    /// <summary>Inspector data built straight from sim state. Sim thread (or a stopped sim) only.</summary>
    public bool TryBuildDetail(int id, out NpcDetail detail)
    {
        detail = null!;
        if (!Npcs.TryGetSlot(id, out int s)) return false;
        var needs = new Dictionary<string, float>();
        for (int n = 0; n < Needs.Count; n++) needs[Needs.Names[n]] = Npcs.Needs[s * Needs.Count + n];
        // Traits are the genes blended with upbringing (M3, World.Society.cs).
        var traits = new Dictionary<string, float>();
        for (int g = 0; g < Genetics.Count; g++) traits[Genetics.Names[g]] = Trait(s, (Gene)g);
        var knowledge = new Dictionary<string, float>();
        if (s < _knowledge.Length && _knowledge[s] is { } kn)
            foreach (var (c, p) in kn) knowledge[c] = p;
        var rels = new List<RelationshipView>();
        foreach (var (other, r) in Relationships.Of(id))
            rels.Add(new RelationshipView(other, NameOf(other), r.Affinity, r.Trust, r.Debt));
        rels.Sort((a, b) => b.Affinity.CompareTo(a.Affinity));
        var mems = new List<MemoryView>();
        if (Memories.Summary(s) is { } summary) mems.Add(new MemoryView(0, "summary", summary, 1));
        foreach (var m in Memories.Of(s)) mems.Add(new MemoryView(m.Tick, Memory.KindName(m.Kind), m.Text, m.Importance, m.Generation));
        string goal = Npcs.Action[s].ToString().ToLowerInvariant();
        if (Npcs.TargetNpc[s] != Npcs.None && Npcs.IsAlive(Npcs.TargetNpc[s])) goal += " " + NameOf(Npcs.TargetNpc[s]);
        detail = new NpcDetail(id, Npcs.Name[s], (int)AgeYears(s), needs, traits, knowledge, rels, mems, goal, null);
        return true;
    }

    public string NameOf(int id) => id == MemoryStore.GodId ? "the god" : Npcs.TryGetSlot(id, out int s) ? Npcs.Name[s] : "someone";
}

public static class WeatherKind
{
    public const string Clear = "clear";
    public const string Rain = "rain";
    public const string Drought = "drought";
}
