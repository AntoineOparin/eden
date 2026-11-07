using Godot;

namespace Eden.Game.Dev;

/// <summary>
/// Dev flag <c>--log-frame-time</c> (M7 UI): when the display forces V-Sync (the fps counter then sits at the refresh
/// rate whatever the load), this measures the cost of a frame instead. Two probes bracket every <c>_Process</c>
/// callback (first and last process priority) to time the scripts, and the viewport's measured render times give the
/// render-thread CPU and GPU cost. Logs averages every two seconds and a summary with the frame rate those costs allow
/// (1000 / max(GPU, scripts + render CPU)).
/// </summary>
public partial class FrameTimeProbe : Node
{
    private ulong _start;
    private double _scripts, _cpu, _gpu, _timer, _elapsed;
    private int _frames;
    private readonly List<double> _allowed = new();
    private FrameTimeProbeEnd? _end;

    public override void _Ready()
    {
        ProcessPriority = int.MinValue;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        _end = new FrameTimeProbeEnd { Probe = this, ProcessPriority = int.MaxValue, Name = "FrameTimeProbeEnd" };
        GetParent().CallDeferred(Node.MethodName.AddChild, _end);
    }

    public override void _Process(double delta) => _start = Time.GetTicksUsec();

    internal void Finish(double delta)
    {
        _scripts += (Time.GetTicksUsec() - _start) / 1000.0;
        var vp = GetViewport().GetViewportRid();
        _cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + RenderingServer.GetFrameSetupTimeCpu();
        _gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
        _frames++;
        _timer += delta;
        _elapsed += delta;
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
