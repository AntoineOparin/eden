using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// The whole simulation state and its tick function. Single-threaded by design: <see cref="SimHost"/>
/// owns the one thread that may call <see cref="Tick"/>. M1 fills this in (see docs/specs/10-sim-core.md);
/// this skeleton only has the clock, the RNG, the heightmap placeholder and the plumbing.
/// </summary>
public sealed class World
{
    public SimConfig Config { get; }
    public long Tick { get; private set; }
    public Rng Rng;

    private readonly float[] _heights;
    private readonly byte[] _biomes;
    private readonly List<ChronicleEntry> _pendingChronicle = new();
    private readonly List<string> _log = new();

    public World(SimConfig config)
    {
        Config = config;
        Rng = new Rng((ulong)config.Seed);
        _heights = new float[config.WorldWidth * config.WorldHeight];
        _biomes = new byte[config.WorldWidth * config.WorldHeight];
        Chronicle(0, "genesis", $"The world was seeded with {config.Seed}.", 10, Array.Empty<int>());
    }

    public SimClock Clock => new(Tick, Config);

    /// <summary>Advance one fixed step. Must not allocate on the hot path once M1 lands.</summary>
    public void Step()
    {
        Tick++;
        // M1: needs decay, utility AI, movement, life cycle, memory recording go here.
    }

    public void Apply(WorldCommand command)
    {
        switch (command)
        {
            case SetTimeScaleCommand:
                break; // handled by SimHost
            default:
                _log.Add($"unhandled command {command.GetType().Name}"); // M1+: every command gets a handler
                break;
        }
    }

    /// <summary>Validate and apply one intention. Returns the rejection reason or null.</summary>
    public string? Apply(Intention intention)
    {
        // M1: IntentionValidator + execution. Until then nothing is alive, so everything is rejected.
        return "no such npc";
    }

    public void Chronicle(long tick, string kind, string text, int importance, IReadOnlyList<int> subjects)
        => _pendingChronicle.Add(new ChronicleEntry(tick, kind, text, importance, subjects));

    /// <summary>Immutable copy for the renderer; drains pending Chronicle entries.</summary>
    public WorldSnapshot Snapshot()
    {
        var chronicle = _pendingChronicle.ToArray();
        _pendingChronicle.Clear();
        var clock = Clock;
        return new WorldSnapshot(
            Tick, clock.Year, clock.DayOfYear, clock.HourOfDay,
            new WorldInfo(Config.Seed, Config.WorldWidth, Config.WorldHeight, Config.CellSize, Config.SeaLevel, Config.ChunkSize),
            _heights, _biomes,
            Array.Empty<NpcView>(), Array.Empty<BuildingView>(), chronicle);
    }

    /// <summary>Order-sensitive hash of everything that affects future ticks.</summary>
    public ulong StateHash()
    {
        ulong h = Hash.Offset;
        h = Hash.Mix(h, (ulong)Tick);
        h = Hash.Mix(h, Rng.StateHash());
        foreach (var v in _heights) h = Hash.Mix(h, v);
        return h;
    }
}
