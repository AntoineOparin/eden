using Godot;

namespace Eden.Game.Dev;

/// <summary>
/// Dev flag <c>--log-frame-time</c> (M7 UI): when the display forces V-Sync (the fps counter then sits at the refresh
/// rate whatever the load), this measures the cost of a frame instead. Two probes bracket every <c>_Process</c>
/// callback (first and last process priority) to time the scripts, and the viewport's measured render times give the
/// render-thread CPU and GPU cost. Logs averages every two seconds and a summary with the frame rate those costs allow
/// (1000 / max(GPU, scripts + render CPU)).
/// A2: also tracks hitches. Every frame longer than <see cref="HitchMs"/> after the 4 s warm-up is logged with its time
/// and the one-off work noted during it (<see cref="Note"/>); the summary gives the worst frame and the hitch count.
/// </summary>
public partial class FrameTimeProbe : Node
{
    /// <summary>A frame longer than this (ms) after the warm-up counts as a hitch (A2 target: none).</summary>
    public const double HitchMs = 33.0;

    private ulong _start;
    private double _scripts, _cpu, _gpu, _timer, _elapsed;
    private int _frames;
    private readonly List<double> _allowed = new();
    private FrameTimeProbeEnd? _end;
    private double _worst, _worstAt;
    private int _hitches;
    private ulong _lastFrameUsec;
    private static FrameTimeProbe? _active;
    private static readonly List<string> Notes = new();

    /// <summary>
    /// Records one-off work (label, milliseconds) so a hitch line can say what ran in that frame; printed at once when
    /// it took 1 ms or more. A no-op unless <c>--log-frame-time</c> is on.
    /// </summary>
    public static void Note(string label, double ms)
    {
        if (_active is null) return;
        Notes.Add($"{label} {ms:0.0} ms");
        if (ms >= 1.0) GD.Print($"[hitch] {label} took {ms:0.0} ms at {_active._elapsed:0.00} s");
    }

    public override void _Ready()
    {
        _active = this;
        ProcessPriority = int.MinValue;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        _end = new FrameTimeProbeEnd { Probe = this, ProcessPriority = int.MaxValue, Name = "FrameTimeProbeEnd" };
        GetParent().CallDeferred(Node.MethodName.AddChild, _end);
    }

    public override void _Process(double delta) => _start = Time.GetTicksUsec();

    internal void Finish(double delta)
    {
        ulong now = Time.GetTicksUsec();
        _scripts += (now - _start) / 1000.0;
        var vp = GetViewport().GetViewportRid();
        _cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + RenderingServer.GetFrameSetupTimeCpu();
        _gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
        _frames++;
        _timer += delta;
        _elapsed += delta;
        // Wall time between the ends of two frames: catches stalls outside _Process too (render, readbacks, compiles).
        double frameMs = _lastFrameUsec == 0 ? 0 : (now - _lastFrameUsec) / 1000.0;
        _lastFrameUsec = now;
        if (_elapsed > 4.0)
        {
            if (frameMs > _worst) { _worst = frameMs; _worstAt = _elapsed; }
            if (frameMs > HitchMs)
            {
                _hitches++;
                GD.Print($"[hitch] frame {frameMs:0.0} ms at {_elapsed:0.00} s{(Notes.Count > 0 ? " during " + string.Join(", ", Notes) : "")}");
            }
        }
        else if (frameMs > HitchMs)
            GD.Print($"[hitch] warm-up frame {frameMs:0.0} ms at {_elapsed:0.00} s{(Notes.Count > 0 ? " during " + string.Join(", ", Notes) : "")}");
        Notes.Clear();
        if (_timer < 2.0) return;
        double s = _scripts / _frames, c = _cpu / _frames, g = _gpu / _frames;
        double allowed = 1000.0 / Math.Max(0.1, Math.Max(g, s + c));
        if (_elapsed > 4.0) _allowed.Add(allowed);
        GD.Print($"[frame] scripts {s:0.00} ms, render cpu {c:0.00} ms, gpu {g:0.00} ms -> ~{allowed:0} fps possible");
        _timer = 0;
        _scripts = _cpu = _gpu = 0;
        _frames = 0;
    }

    public override void _ExitTree()
    {
        if (_active == this) _active = null;
        GD.Print($"[hitch] summary: worst frame {_worst:0.0} ms at {_worstAt:0.00} s, {_hitches} frames over {HitchMs:0} ms after the 4 s warm-up");
        if (_allowed.Count > 0)
            GD.Print($"[frame] summary: ~{_allowed.Average():0} fps possible on average (min {_allowed.Min():0}) over {_allowed.Count} samples");
    }
}

/// <summary>The last-priority half of <see cref="FrameTimeProbe"/>.</summary>
public partial class FrameTimeProbeEnd : Node
{
    public FrameTimeProbe? Probe { get; set; }
    public override void _Process(double delta) => Probe?.Finish(delta);
}
