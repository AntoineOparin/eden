using System.Collections.Concurrent;
using System.Diagnostics;
using Eden.Sim.Contracts;

namespace Eden.Game.Dev;

/// <summary>
/// Development stand-in for the real <c>Eden.Sim.SimHost</c> so the render shell (M2) does not depend on M1.
/// Generates an island heightmap, spawns N synthetic NPCs that wander between random land targets and
/// publishes immutable snapshots from its own background thread, like the real host. Honours
/// <see cref="SetTimeScaleCommand"/>; every other command is ignored.
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
    private uint _rng;
    private long _tick;
    private volatile IWorldSnapshot _latest;
    private volatile int _timeScale = (int)TimeScale.X1;
    private volatile bool _running;
    private Thread? _thread;
    private List<ChronicleEntry> _pendingChronicle = new();

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
            if (cmd is SetTimeScaleCommand ts) _timeScale = (int)ts.Scale;
    }

    private void Step()
    {
        _tick++;
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
        long ticksPerDay = TicksPerHour * HoursPerDay, ticksPerYear = ticksPerDay * DaysPerYear;
        return new FakeWorldSnapshot(_tick, (int)(_tick / ticksPerYear), (int)(_tick % ticksPerYear / ticksPerDay),
            (float)(_tick % ticksPerDay) / TicksPerHour, _info, _heights, _biomes, views, chronicle);
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
