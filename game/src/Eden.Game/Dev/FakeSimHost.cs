using System.Collections.Concurrent;
using System.Diagnostics;
using Eden.Sim.Contracts;

namespace Eden.Game.Dev;

/// <summary>
/// Development stand-in for the real <c>Eden.Sim.SimHost</c> so the render shell (M2) does not depend on M1.
/// Generates an island heightmap, spawns N synthetic NPCs that wander between random land targets and
/// publishes immutable snapshots from its own background thread, like the real host. Honours
/// <see cref="SetTimeScaleCommand"/>. M7 UI: six fake settlements (centre = where their first member started) with a
/// god standing that bless / curse / lightning / gifts nudge, a Chronicle entry per god command, and a few made-up
/// Chronicle entries of every kind each in-game day so the timeline has something to show. Other commands are ignored.
/// C1: settlement food and territory radius, settlement details (in <see cref="FakeWorldSnapshot"/>) and a bounded
/// Chronicle history for <see cref="IWorldSnapshot.ChronicleSince"/>, all made up but stable.
/// </summary>
public sealed class FakeSimHost : ISimHost, IIntentionSink
{
    public const int TicksPerRealSecond = 20;
    private const int TicksPerHour = 10, HoursPerDay = 24, DaysPerYear = 10;
    private const float WalkPerTick = 0.09f; // metres; about 1.8 m/s at 1x

    private readonly WorldInfo _info;
    private readonly float[] _heights;
    private readonly byte[] _biomes;
    private readonly ConcurrentQueue<WorldCommand> _commands = new();
    private readonly NpcState[] _npcs;
    private readonly int[] _landCells;
    private readonly ResourceView[] _resources;
    private uint _rng;
    private long _tick;
    private volatile IWorldSnapshot _latest;
    private volatile int _timeScale = (int)TimeScale.X1;
    private volatile bool _running;
    private Thread? _thread;
    private List<ChronicleEntry> _pendingChronicle = new();

    // C1: Chronicle history (oldest first, bounded like the real sim's) and the copy shared by snapshots.
    public const int HistorySize = 2048;
    private readonly List<ChronicleEntry> _history = new();
    private ChronicleEntry[] _publishedHistory = [];

    // M7 UI: fake settlements and their god standing (sim thread only, after construction).
    public const int SettlementCount = 6;
    private static readonly string[] SettlementNames = ["Stoneby", "Elmdale", "Fenholm", "Marstead", "Ashford", "Brightwater"];
    private static readonly string?[] SettlementFaiths = ["the Way of Palom", "the Way of Palom", "the Old Sky", "the Old Sky", "the Way of Palom", null];
    private static readonly string[] FakeKinds = ["birth", "death", "trade", "raid", "discovery", "religion", "schism", "shrine", "prayer", "knowledge-lost"];
    private readonly float[] _setX = new float[SettlementCount], _setZ = new float[SettlementCount];
    private readonly float[] _favour = new float[SettlementCount], _awe = new float[SettlementCount];

    private struct NpcState
    {
        public float X, Z, TargetX, TargetZ, Heading;
        public int Rest; // ticks left idling/working/sleeping
        public NpcPose Pose;
        public Sex Sex;
        public LifeStage Stage;
        public int Settlement;
        public int ThinkTicks;
    }

    public FakeSimHost(int npcCount, int seed = 1, int width = 256, int height = 256, float cellSize = 2f, float seaLevel = 0f, int chunkSize = 32)
    {
        _info = new WorldInfo(seed, width, height, cellSize, seaLevel, chunkSize);
        (_heights, _biomes) = IslandGenerator.Generate(seed, width, height, cellSize, seaLevel);
        _rng = (uint)seed * 2654435761u + 1;
        if (_rng == 0) _rng = 1;

        var land = new List<int>();
        for (int i = 0; i < _heights.Length; i++)
            if (IsWalkable(i)) land.Add(i);
        if (land.Count == 0) land.Add(_heights.Length / 2 + width / 2);
        _landCells = land.ToArray();

        _resources = GenerateResources(seed);
        _npcs = new NpcState[Math.Max(0, npcCount)];
        for (int i = 0; i < _npcs.Length; i++)
        {
            var (x, z) = RandomLandPoint();
            var (tx, tz) = RandomTargetNear(x, z);
            _npcs[i] = new NpcState
            {
                X = x, Z = z, TargetX = tx, TargetZ = tz,
                Pose = NpcPose.Walking,
                Sex = (Next() & 1) == 0 ? Sex.Female : Sex.Male,
                Stage = (Next() % 10) switch { 0 => LifeStage.Child, 1 => LifeStage.Elder, _ => LifeStage.Adult },
                Settlement = (int)(Next() % 6),
            };
        }
        for (int k = 0; k < SettlementCount; k++)
        {
            int first = Array.FindIndex(_npcs, n => n.Settlement == k);
            (_setX[k], _setZ[k]) = first >= 0 ? (_npcs[first].X, _npcs[first].Z) : (width * cellSize * 0.5f, height * cellSize * 0.5f);
            _favour[k] = (k % 3 - 1) * 0.2f; // one favourable, one neutral, one hostile per three
            _awe[k] = k % 2 == 0 ? 0.2f : 0.45f;
        }
        _pendingChronicle.Add(new ChronicleEntry(0, "genesis", $"A fake island rose from the sea with {npcCount} souls (seed {seed}).", 10, Array.Empty<int>()));
        _latest = Publish();
    }

    public IWorldSnapshot Latest => _latest;
    public IIntentionSink Intentions => this;
    public TimeScale Scale => (TimeScale)_timeScale;
    public bool IsRunning => _running;

    public bool TryPush(Intention intention) => false;

    public void Enqueue(WorldCommand command) => _commands.Enqueue(command);

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Run) { Name = "Eden.FakeSim", IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    public void Dispose() => Stop();

    /// <summary>Synchronous stepping for tests. Never call while the thread runs.</summary>
    public void StepSync(int ticks)
    {
        if (_running) throw new InvalidOperationException("fake sim thread is running");
        DrainCommands();
        for (int i = 0; i < ticks; i++) Step();
        _latest = Publish();
    }

    public ulong StateHash()
    {
        ulong h = 1469598103934665603UL;
        h = (h ^ (ulong)_tick) * 1099511628211UL;
        foreach (var n in _npcs)
        {
            h = (h ^ BitConverter.SingleToUInt32Bits(n.X)) * 1099511628211UL;
            h = (h ^ BitConverter.SingleToUInt32Bits(n.Z)) * 1099511628211UL;
        }
        return h;
    }

    /// <summary>Bilinear height in world metres; cell (x, z) sits at (x*CellSize, z*CellSize).</summary>
    public float SampleHeight(float x, float z) => HeightField.Sample(_heights, _info, x, z);

    private void Run()
    {
        var sw = Stopwatch.StartNew();
        double last = sw.Elapsed.TotalSeconds, accumulator = 0;
        while (_running)
        {
            DrainCommands();
            double now = sw.Elapsed.TotalSeconds;
            accumulator += (now - last) * TicksPerRealSecond * _timeScale;
            last = now;
            int batch = (int)Math.Min(accumulator, TicksPerRealSecond * 25);
            for (int i = 0; i < batch; i++) Step();
            accumulator -= batch;
            if (batch > 0) _latest = Publish();
            Thread.Sleep(batch == 0 ? 2 : 1);
        }
    }

    private void DrainCommands()
    {
        while (_commands.TryDequeue(out var cmd))
        {
            switch (cmd)
            {
                case SetTimeScaleCommand ts: _timeScale = (int)ts.Scale; break;
                case BlessCommand b when b.TargetId >= 0 && b.TargetId < _npcs.Length:
                    Nudge(_npcs[b.TargetId].Settlement, b.Curse ? -0.2f : 0.15f, b.Curse ? 0.12f : 0.05f);
                    God(b.Curse ? "curse" : "blessing", $"The god {(b.Curse ? "cursed" : "blessed")} {FakeWorldSnapshot.NameOf(b.TargetId)}.", [b.TargetId]);
                    break;
                case LightningCommand l:
                    Nudge(NearestSettlement(l.X, l.Z), -0.08f, 0.2f);
                    God("god", "Lightning struck from a clear sky.", []);
                    break;
                case DropItemCommand d:
                    Nudge(NearestSettlement(d.X, d.Z), 0.08f, 0.03f);
                    God("god", $"Gifts of {d.ItemId} fell from the sky.", []);
                    break;
                case SpawnCommand sp: God("god", $"The god made {sp.Kind} appear out of nothing.", []); break;
                case TerraformCommand t: God("god", t.DeltaHeight > 0 ? "The god raised the land." : "The god sank the land.", []); break;
                case WeatherCommand w: God("god", w.Kind == "rain" ? "The god sent rain." : "The god withheld the rain; a drought began.", []); break;
            }
        }
    }

    private void Nudge(int k, float favour, float awe)
    {
        if (k < 0 || k >= SettlementCount) return;
        _favour[k] = Math.Clamp(_favour[k] + favour, -1f, 1f);
        _awe[k] = Math.Clamp(_awe[k] + awe, 0f, 1f);
    }

    private void God(string kind, string text, int[] subjects) => _pendingChronicle.Add(new ChronicleEntry(_tick, kind, text, 8, subjects));

    private int NearestSettlement(float x, float z)
    {
        int best = -1;
        float bd = float.MaxValue;
        for (int k = 0; k < SettlementCount; k++)
        {
            float d = (x - _setX[k]) * (x - _setX[k]) + (z - _setZ[k]) * (z - _setZ[k]);
            if (d < bd) { bd = d; best = k; }
        }
        return best;
    }

    /// <summary>Three made-up Chronicle entries per in-game day, from a hash of the tick (no draw from the NPC stream).</summary>
    private void FakeChronicle()
    {
        const long ticksPerDay = TicksPerHour * HoursPerDay;
        if (_tick % ticksPerDay != 0) return;
        long day = _tick / ticksPerDay;
        for (int i = 0; i < 3; i++)
        {
            float r = IslandGenerator.Hash01((int)day, i, 71);
            string kind = FakeKinds[(int)(r * FakeKinds.Length) % FakeKinds.Length];
            int k = (int)(IslandGenerator.Hash01((int)day, i, 73) * SettlementCount) % SettlementCount;
            int who = _npcs.Length == 0 ? -1 : (int)(IslandGenerator.Hash01((int)day, i, 79) * _npcs.Length) % _npcs.Length;
            int importance = 2 + (int)(IslandGenerator.Hash01((int)day, i, 83) * 8);
            _pendingChronicle.Add(new ChronicleEntry(_tick, kind, $"[{kind}] something happened in {SettlementNames[k]}.", importance,
                who >= 0 ? [who] : Array.Empty<int>()));
        }
        for (int k = 0; k < SettlementCount; k++)
        {
            _favour[k] *= 0.98f;
            _awe[k] *= 0.97f;
        }
    }

    private SettlementView[] SettlementViews()
    {
        var members = new int[SettlementCount];
        int[] leader = [-1, -1, -1, -1, -1, -1];
        for (int i = 0; i < _npcs.Length; i++)
        {
            int k = _npcs[i].Settlement;
            if (k < 0 || k >= SettlementCount) continue;
            members[k]++;
            if (leader[k] < 0 && _npcs[i].Stage == LifeStage.Adult) leader[k] = i;
        }
        var views = new SettlementView[SettlementCount];
        float maxFav = _favour.Max();
        for (int k = 0; k < SettlementCount; k++)
        {
            int favoured = Array.IndexOf(_favour, maxFav);
            float envy = favoured != k ? Math.Clamp(maxFav - _favour[k], 0f, 1f) * 0.6f : 0f;
            float fear = Math.Clamp(_awe[favoured] - _awe[k], 0f, 1f) * 0.5f;
            var stance = _favour[k] > 0.15f ? GodStance.Favourable : _awe[k] >= 0.35f ? GodStance.Fearful : _favour[k] < -0.15f ? GodStance.Hostile : GodStance.Neutral;
            views[k] = new SettlementView(k, SettlementNames[k], _setX[k], _setZ[k], members[k], k / 2, leader[k],
                _favour[k], _awe[k], envy, fear, envy > 0.05f ? favoured : -1, fear > 0.05f && favoured != k ? favoured : -1, stance, SettlementFaiths[k],
                FakeFood(k, members[k]), FakeTerritoryRadius(k));
        }
        return views;
    }

    /// <summary>C1: made-up food stock, stable within an in-game day: 2–6 days of eating.</summary>
    private float FakeFood(int k, int members)
    {
        int day = (int)(_tick / (TicksPerHour * HoursPerDay));
        return MathF.Round(FakeWorldSnapshot.FakeFoodPerDay(members) * (2f + 4f * IslandGenerator.Hash01(k, day, 5)), 1);
    }

    /// <summary>C1: made-up territory radius per settlement (the real sim uses one radius for all).</summary>
    public static float FakeTerritoryRadius(int k) => 52f + 4f * k;

    private void Step()
    {
        _tick++;
        FakeChronicle();
        for (int i = 0; i < _npcs.Length; i++)
        {
            ref var n = ref _npcs[i];
            if (n.ThinkTicks > 0) n.ThinkTicks--;
            else if (Next() % 4000 == 0) n.ThinkTicks = 60;

            if (n.Rest > 0)
            {
                if (--n.Rest == 0)
                {
                    (n.TargetX, n.TargetZ) = RandomTargetNear(n.X, n.Z);
                    n.Pose = NpcPose.Walking;
                }
                continue;
            }
            float dx = n.TargetX - n.X, dz = n.TargetZ - n.Z;
            float d = MathF.Sqrt(dx * dx + dz * dz);
            float speed = n.Stage == LifeStage.Elder ? WalkPerTick * 0.6f : WalkPerTick;
            if (d <= speed)
            {
                n.X = n.TargetX;
                n.Z = n.TargetZ;
                n.Rest = 20 + (int)(Next() % 200);
                uint r = Next() % 10;
                n.Pose = r < 6 ? NpcPose.Idle : r < 9 ? NpcPose.Working : NpcPose.Sleeping;
                continue;
            }
            n.X += dx / d * speed;
            n.Z += dz / d * speed;
            n.Heading = MathF.Atan2(dx, dz);
        }
    }

    private FakeWorldSnapshot Publish()
    {
        var views = new NpcView[_npcs.Length];
        for (int i = 0; i < _npcs.Length; i++)
        {
            ref readonly var n = ref _npcs[i];
            views[i] = new NpcView(i, n.X, SampleHeight(n.X, n.Z), n.Z, n.Heading, n.Sex, n.Stage, n.Pose,
                100, n.Settlement, n.ThinkTicks > 0, false);
        }
        var chronicle = _pendingChronicle;
        _pendingChronicle = new List<ChronicleEntry>();
        if (chronicle.Count > 0)
        {
            _history.AddRange(chronicle);
            if (_history.Count > HistorySize) _history.RemoveRange(0, _history.Count - HistorySize);
            _publishedHistory = _history.ToArray();
        }
        long ticksPerDay = TicksPerHour * HoursPerDay, ticksPerYear = ticksPerDay * DaysPerYear;
        return new FakeWorldSnapshot(_tick, (int)(_tick / ticksPerYear), (int)(_tick % ticksPerYear / ticksPerDay),
            (float)(_tick % ticksPerDay) / TicksPerHour, _info, _heights, _biomes, views, chronicle, _resources,
            settlements: SettlementViews(), history: _publishedHistory);
    }

    /// <summary>
    /// Static resource nodes, placed like the real WorldGen (trees and berries in forest and grassland, stone and ore on
    /// mountains, a few herds), from a hash of the cell so the layout is deterministic per seed. Amounts never change.
    /// </summary>
    private ResourceView[] GenerateResources(int seed)
    {
        var list = new List<ResourceView>();
        for (int i = 0; i < _heights.Length; i++)
        {
            int cx = i % _info.Width, cz = i / _info.Width;
            float roll = IslandGenerator.Hash01(seed + 101, cx, cz);
            string? kind = _biomes[i] switch
            {
                Biome.Forest => roll < 0.06f ? "tree" : roll < 0.075f ? "berry" : roll < 0.078f ? "game" : null,
                Biome.Grassland => roll < 0.008f ? "berry" : roll < 0.014f ? "tree" : roll < 0.0165f ? "game" : null,
                Biome.Mountain => roll < 0.04f ? "stone" : roll < 0.052f ? "ore" : null,
                Biome.Beach => roll < 0.01f ? "tree" : null,
                _ => null,
            };
            if (kind is null) continue;
            float x = cx * _info.CellSize, z = cz * _info.CellSize;
            float amount = 0.4f + 0.6f * IslandGenerator.Hash01(seed + 7, cx, cz);
            list.Add(new ResourceView(list.Count, kind, x, _heights[i], z, amount, (byte)(IslandGenerator.Hash01(seed + 13, cx, cz) * 255)));
        }
        return list.ToArray();
    }

    private bool IsWalkable(int cell) => _heights[cell] > _info.SeaLevel + 1.0f && _biomes[cell] != Biome.Snow;

    private (float X, float Z) RandomLandPoint()
    {
        int cell = _landCells[Next() % (uint)_landCells.Length];
        float jitterX = (Next() % 1000) / 1000f, jitterZ = (Next() % 1000) / 1000f;
        return ((cell % _info.Width + jitterX) * _info.CellSize, (cell / _info.Width + jitterZ) * _info.CellSize);
    }

    private (float X, float Z) RandomTargetNear(float x, float z)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            float angle = (Next() % 6283) / 1000f;
            float dist = 6f + (Next() % 3000) / 100f;
            float tx = x + MathF.Cos(angle) * dist, tz = z + MathF.Sin(angle) * dist;
            int cx = (int)(tx / _info.CellSize), cz = (int)(tz / _info.CellSize);
            if (cx < 0 || cz < 0 || cx >= _info.Width || cz >= _info.Height) continue;
            if (IsWalkable(cz * _info.Width + cx)) return (tx, tz);
        }
        return (x, z);
    }

    private uint Next()
    {
        // xorshift32. Only the constructor and the sim thread touch it, never concurrently.
        uint x = _rng;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        return _rng = x;
    }
}
