using System.Collections.Concurrent;
using System.Diagnostics;
using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// Owns the simulation thread. Fixed-step accumulator, time scale, double-buffered snapshots,
/// lock-free command and intention queues. Godot and the Mind only ever touch this class.
/// </summary>
public sealed class SimHost : ISimHost, IIntentionSink
{
    private readonly World _world;
    private readonly ConcurrentQueue<WorldCommand> _commands = new();
    private readonly IntentionQueue _intentions;
    private volatile WorldSnapshot _latest;
    private Thread? _thread;
    private volatile bool _running;
    private volatile int _timeScale = (int)TimeScale.X1;

    public SimHost(SimConfig config, int maxIntentionQueue = 4096)
    {
        _world = new World(config);
        _latest = _world.Snapshot();
        _intentions = new IntentionQueue(maxIntentionQueue, () => _latest);
    }

    public IWorldSnapshot Latest => _latest;
    public IIntentionSink Intentions => _intentions;
    public TimeScale Scale => (TimeScale)_timeScale;

    /// <summary>Rejected intentions and unhandled commands, for the debug overlay. Bounded.</summary>
    public ConcurrentQueue<string> Diagnostics { get; } = new();

    public void Enqueue(WorldCommand command) => _commands.Enqueue(command);

    public bool TryPush(Intention intention) => _intentions.TryPush(intention);

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(Run) { Name = "Eden.Sim", IsBackground = true };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    /// <summary>Synchronous stepping for tests and the console runner. Never call while the thread runs.</summary>
    public void StepSync(int ticks)
    {
        if (_running) throw new InvalidOperationException("sim thread is running");
        DrainQueues();
        for (int i = 0; i < ticks; i++) _world.Step();
        _latest = _world.Snapshot();
    }

    public ulong StateHash() => _world.StateHash();

    private void Run()
    {
        var config = _world.Config;
        var sw = Stopwatch.StartNew();
        double last = sw.Elapsed.TotalSeconds;
        double accumulator = 0;
        while (_running)
        {
            double now = sw.Elapsed.TotalSeconds;
            // M6: while someone is possessed the world runs at walking pace (PossessionTicksPerRealSecond at 1×).
            int rate = _world.PossessedId != Npcs.None ? config.PossessionTicksPerRealSecond : config.TicksPerRealSecond;
            accumulator += (now - last) * rate * _timeScale;
            last = now;

            DrainQueues();
            int batch = (int)Math.Min(accumulator, config.TicksPerRealSecond * 50); // cap: 2.5 s of sim at 1× per frame
            for (int i = 0; i < batch; i++) _world.Step();
            accumulator -= batch;
            if (batch > 0 || _world.Tick == 0) _latest = _world.Snapshot();

            Thread.Sleep(batch == 0 ? 2 : 0);
        }
    }

    private void DrainQueues()
    {
        while (_commands.TryDequeue(out var cmd))
        {
            if (cmd is SetTimeScaleCommand ts) _timeScale = (int)ts.Scale;
            else _world.Apply(cmd);
        }
        while (_world.TryDequeueLog(out var line))
            if (Diagnostics.Count < 256) Diagnostics.Enqueue(line);
        while (_intentions.TryDequeue(out var intent))
        {
            var reason = _world.Apply(intent);
            if (reason is not null && Diagnostics.Count < 256)
                Diagnostics.Enqueue($"intention {intent.GetType().Name} for npc {intent.NpcId} rejected: {reason}");
        }
    }

    public void Dispose() => Stop();
}
