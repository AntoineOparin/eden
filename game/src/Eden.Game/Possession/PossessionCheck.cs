using System.Globalization;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Scripted M6 manual check (<c>--m6-check</c>), driving the same code paths as a player: select an adult in the
/// largest village, possess it through <see cref="PossessionDirector.PossessSelected"/> (the P key / HUD button), pick
/// berries if a bush is near (E: work), walk to the nearest other village by turning the camera and holding W+Shift
/// (<see cref="PossessedController.ScriptedInput"/>), talk to someone there (E + the dialogue panel as the possessed
/// speaker), give them food (G), release (Esc path) and print both memory lists. Logs fps while possessed.
/// Args: <c>--m6-message=TEXT</c>, <c>--m6-shots=DIR</c> (JPEG screenshots), <c>--m6-timeout=SECONDS</c>.
/// </summary>
public partial class PossessionCheck : Node
{
    private enum Phase { Wait, Select, Confirm, ToBush, Gather, Walk, Approach, Talk, Reply, Give, Release, Dump, Done }

    private PossessionDirector _dir = null!;
    private Main _main = null!;
    private Phase _phase = Phase.Wait;
    private double _t, _total, _timeout = 600, _fpsTimer, _progressTimer;
    private string _message = "Good day. I walked a long way to see your village. How is the harvest here?";
    private string? _shots;
    private int _npc = -1, _target = -1, _homeSettlement = -1, _goalSettlement = -1;
    private Vector2 _goal, _bush;
    private float _lastDistance = float.MaxValue;
    private int _detour;
    private readonly List<double> _fps = new();
    private bool _shotThird, _shotFirst;
    private float _startDistance;
    private int _thinkers = 3;
    private const float MinTrip = 100f;
    private bool _debug;
    private int _witness = -1;
    private int _stalls, _retargets;
    private double _replanTimer, _replyAt = -1;
    private double _startNeurons;

    public override void _Ready()
    {
        _dir = GetParent<PossessionDirector>();
        _main = _dir.Main!;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--m6-message=")) _message = arg["--m6-message=".Length..];
            else if (arg.StartsWith("--m6-shots=")) _shots = arg["--m6-shots=".Length..];
            else if (arg.StartsWith("--m6-timeout=") && double.TryParse(arg["--m6-timeout=".Length..], CultureInfo.InvariantCulture, out double t)) _timeout = t;
            else if (arg == "--m6-debug") _debug = true;
            else if (arg.StartsWith("--m6-thinkers=") && int.TryParse(arg["--m6-thinkers=".Length..], out int n)) _thinkers = n;
        }
        // Background thoughts are most of a session's Neurons; the check keeps a small spotlight (not saved).
        if (_main.Mind is { } mind && _thinkers >= 0) mind.ApplySettings(mind.Settings with { MaxThinkingNpcs = _thinkers });
        _dir.MouseCaptureEnabled = false; // scripted: the real mouse stays free
        _startNeurons = _main.Mind?.Budget.NeuronsToday ?? 0;
        GD.Print($"[m6] check start: provider {_main.Mind?.Provider.Id}, {_startNeurons:0.00} Neurons used today, spotlight {_thinkers}");
    }

    public override void _Process(double delta)
    {
        _t += delta;
        _total += delta;
        if (_phase != Phase.Done && _total > _timeout) { Finish($"timed out in phase {_phase}"); return; }
        if (_main.Host is not { } host) return;
        var snap = host.Latest;
        if (_dir.IsPossessed && _dir.Confirmed)
        {
            _fpsTimer += delta;
            if (_fpsTimer >= 1.0)
            {
                _fpsTimer = 0;
                _fps.Add(Engine.GetFramesPerSecond());
            }
        }
        switch (_phase)
        {
            case Phase.Wait:
                if (_t < 3 || snap.Population < 2) return;
                PickVillages(snap);
                break;
            case Phase.Select:
                _main.Select(_npc);
                if (_t < 1.5) return;
                GD.Print($"[m6] pressing Possess (P) on {NpcName(snap, _npc)} (#{_npc})");
                _dir.PossessSelected();
                Next(Phase.Confirm);
                break;
            case Phase.Confirm:
                if (!_dir.IsPossessed) { Finish("possession was refused"); return; }
                if (!_dir.Confirmed || _t < 2) return;
                GD.Print($"[m6] possessed {_dir.PossessedName}; sim tick {snap.Tick}");
                _witness = NearestNeighbour(snap);
                Next(FindBush(snap) ? Phase.ToBush : Phase.Walk);
                break;
            case Phase.ToBush:
                if (_route is null) { Next(Phase.Walk); return; }
                bool food = _dir.CurrentThing.Kind is PossessionDirector.InteractionKind.Bush or PossessionDirector.InteractionKind.Store;
                if (FollowRoute(2.5f, sprint: (_bush - Body()).Length() > 8f) || (food && _t > 1))
                {
                    _route = null;
                    Stop();
                    var thing = _dir.CurrentThing;
                    if (thing.Kind is PossessionDirector.InteractionKind.Bush or PossessionDirector.InteractionKind.Store && _dir.WorkThing())
                        GD.Print($"[m6] T: {PossessionDirector.PromptFor(thing)[3..]} at ({_bush.X:0}, {_bush.Y:0})");
                    else GD.Print($"[m6] no bush in reach at ({Body().X:0}, {Body().Y:0})");
                    Next(Phase.Gather);
                }
                else if (_t > 120) { _route = null; Next(Phase.Walk); }
                break;
            case Phase.Gather:
                if (_t < 14) return;
                GD.Print($"[m6] gathering done ({(_dir.LastRefusal ?? "no refusal")})");
                Shot("possession-work");
                _startDistance = (Body() - _goal).Length();
                Next(Phase.Walk);
                break;
            case Phase.Walk:
            {
                if (_startDistance <= 0) _startDistance = (Body() - _goal).Length();
                float left = (Body() - _goal).Length();
                LogProgress(delta, left);
                if (!_shotThird && left < _startDistance * 0.55f) { Stop(); Shot("possession-third-person"); _shotThird = true; }
                if (_shotThird && !_shotFirst && left < _startDistance * 0.4f)
                {
                    if (!_dir.Rig.FirstPerson) { _dir.Rig.SetFirstPerson(true); _t = 0; }
                    if (_t < 0.6) { Stop(); return; }
                    Shot("possession-first-person");
                    _dir.Rig.SetFirstPerson(false);
                    _shotFirst = true;
                }
                _route ??= PlanRoute(snap, Body(), _goal);
                if (_route is null) { Finish("no walkable route to the other village"); return; }
                if (FollowRoute(24f, sprint: true))
                {
                    _route = null;
                    _target = NearGoalVillager(snap, 40f);
                    if (_target < 0) { Finish("reached the village but nobody is around"); return; }
                    GD.Print($"[m6] arrived at {SettlementName(snap, _goalSettlement)} after {_total:0} s; walking up to {NpcName(snap, _target)} (#{_target})");
                    Next(Phase.Approach);
                }
                break;
            }
            case Phase.Approach:
            {
                if (!TryFind(snap, _target, out var v)) { Finish("the target vanished"); return; }
                bool inReach = _dir.Current.Kind == PossessionDirector.InteractionKind.Npc && _dir.Current.Id == _target;
                if (!inReach)
                {
                    var at = new Vector2(v.X, v.Z);
                    _replanTimer -= delta;
                    if (_route is null || _replanTimer <= 0)
                    {
                        _route = (at - Body()).Length() < 6f ? [at] : PlanRoute(snap, Body(), at, quiet: true);
                        _waypoint = 0;
                        _replanTimer = 3;
                    }
                    if (_route is null) Steer(at, 1.5f, sprint: false);
                    else FollowRoute(1.5f, sprint: (at - Body()).Length() > 10f);
                    if (_t > 40)
                    {
                        if (++_retargets > 3) { Finish("could not reach anyone in the village"); return; }
                        int next = NearGoalVillager(snap, 60f, exclude: _target);
                        GD.Print($"[m6] cannot reach {NpcName(snap, _target)}; trying {NpcName(snap, next)} (#{next})");
                        if (next < 0) { Finish("could not reach the target"); return; }
                        _target = next;
                        _route = null;
                        _t = 0;
                    }
                    return;
                }
                _route = null;
                Stop();
                Next(Phase.Talk);
                break;
            }
            case Phase.Talk:
                if (_t < 0.3) return;
                if (_dir.Current.Kind != PossessionDirector.InteractionKind.Npc || _dir.Current.Id != _target) { Next(Phase.Approach); return; }
                if (!_dir.InteractPrimary()) { Finish("E found nobody to talk to"); return; }
                var panel = _main.Hud?.Dialogue;
                if (panel is null || !panel.IsPossessedSpeaker) { Finish("dialogue panel did not open as the possessed speaker"); return; }
                if (!panel.Send(_message)) { Finish("dialogue panel refused to send"); return; }
                GD.Print($"[m6] E: talk. {_dir.PossessedName} (possessed) to {panel.NpcName}: {_message}");
                Next(Phase.Reply);
                break;
            case Phase.Reply:
            {
                var p = _main.Hud!.Dialogue!;
                if (p.Pending is not null) { _replyAt = -1; return; }
                if (_replyAt < 0) _replyAt = _total;
                if (_t < 1.0 || _total - _replyAt < 1.0) return; // let the reply draw before the screenshot
                if (p.LastError is not null) GD.Print($"[m6] dialogue error: {p.LastError}");
                else GD.Print($"[m6] {p.NpcName}: {_main.Mind?.History(_target).LastOrDefault()?.Text}");
                GD.Print($"[m6] transcript:\n{p.Transcript}");
                Shot("possession-dialogue");
                _dir.CloseDialogue();
                Next(Phase.Give);
                break;
            }
            case Phase.Give:
                if (_t < 0.5) return;
                if (_dir.Current.Kind != PossessionDirector.InteractionKind.Npc || _dir.Current.Id != _target)
                {
                    if (TryFind(snap, _target, out var tv)) Steer(new Vector2(tv.X, tv.Z), 1.5f, false);
                    if (_t > 20) { Finish("lost the target before giving"); return; }
                    return;
                }
                Stop();
                _dir.GiveFood();
                GD.Print($"[m6] G: give food to {_dir.Current.Name}");
                _dir.CloseDialogue();
                Next(Phase.Release);
                break;
            case Phase.Release:
                if (_t < 2.0) return;
                Report("while possessed");
                GD.Print("[m6] Esc: release");
                _dir.Release();
                Next(Phase.Dump);
                break;
            case Phase.Dump:
                snap.TryGetNpcDetail(_target, out _);
                if (_witness >= 0) snap.TryGetNpcDetail(_witness, out _);
                if (_t < 3.0) return;
                if (!snap.TryGetNpcDetail(_npc, out var a) || !snap.TryGetNpcDetail(_target, out var b)) { if (_t > 10) Finish("details never arrived"); return; }
                Dump(a);
                Dump(b);
                if (_witness >= 0 && snap.TryGetNpcDetail(_witness, out var w))
                {
                    GD.Print($"[m6] {w.Name} (#{w.Id}), a neighbour at home, remembers about {a.Name}:");
                    foreach (var m in w.Memories.Where(m => m.Text.Contains(a.Name, StringComparison.Ordinal)).OrderByDescending(m => m.Tick).Take(4))
                        GD.Print($"[m6]   tick {m.Tick} [{m.Importance}] {m.Kind}: {m.Text}");
                }
                Shot("possession-memories");
                Finish("ok");
                break;
        }
    }

    // ---- planning ---------------------------------------------------------------------------

    private void PickVillages(IWorldSnapshot snap)
    {
        var groups = new Dictionary<int, (float X, float Z, int N)>();
        foreach (var v in snap.Npcs.Span)
        {
            if (v.SettlementId < 0 || v.Pose == NpcPose.Dead) continue;
            var g = groups.GetValueOrDefault(v.SettlementId);
            groups[v.SettlementId] = (g.X + v.X, g.Z + v.Z, g.N + 1);
        }
        var villages = groups.Where(kv => kv.Value.N >= 5).Select(kv => (Id: kv.Key, C: new Vector2(kv.Value.X / kv.Value.N, kv.Value.Z / kv.Value.N), kv.Value.N)).ToList();
        if (villages.Count < 2)
        {
            if (_total > 40) Finish($"need two villages, found {villages.Count}"); // else a --load-world may still be loading
            return;
        }
        var home = villages.OrderByDescending(v => v.N).First();
        // "Another village": the nearest one at least MinTrip away (neighbouring settlements can share a street).
        var others = villages.Where(v => v.Id != home.Id).OrderBy(v => (v.C - home.C).Length()).ToList();
        var goal = others.FirstOrDefault(v => (v.C - home.C).Length() >= MinTrip);
        if (goal.N == 0) goal = others.Last();
        _homeSettlement = home.Id;
        _goalSettlement = goal.Id;
        _goal = goal.C;
        _npc = -1;
        float best = float.MaxValue;
        foreach (var v in snap.Npcs.Span)
        {
            if (v.SettlementId != home.Id || v.Stage != LifeStage.Adult || v.Pose is NpcPose.Dead or NpcPose.Sleeping) continue;
            float d = (new Vector2(v.X, v.Z) - home.C).Length();
            if (d < best) { best = d; _npc = v.Id; }
        }
        if (_npc < 0) { Finish("no adult awake in the home village"); return; }
        GD.Print($"[m6] year {snap.Year + 1}, population {snap.Population}, {villages.Count} villages; from {SettlementName(snap, home.Id)} " +
                 $"({home.N} people) to {SettlementName(snap, goal.Id)} ({goal.N} people), {(goal.C - home.C).Length():0} m apart");
        _main.CameraRig?.Focus(home.C);
        _main.CameraRig?.SetZoom(60, immediate: true);
        Next(Phase.Select);
    }

    /// <summary>The reachable berry bush with the smallest detour on the way to the other village (food to give).</summary>
    private bool FindBush(IWorldSnapshot snap)
    {
        var body = Body();
        var bushes = new List<(Vector2 P, float D)>();
        foreach (var r in snap.Resources.Span)
        {
            if (r.Kind != "berry" || r.Amount < 0.08f) continue;
            var p = new Vector2(r.X, r.Z);
            bushes.Add((p, (p - body).Length() + (_goal - p).Length()));
        }
        int berries = 0;
        float most = 0;
        foreach (var r in snap.Resources.Span) if (r.Kind == "berry") { berries++; most = MathF.Max(most, r.Amount); }
        GD.Print($"[m6] {berries} berry bushes on the island, {bushes.Count} with fruit (fullest {most:0.00})");
        foreach (var (p, d) in bushes.OrderBy(b => b.D).Take(12))
        {
            if (PlanRoute(snap, body, p) is not { } route) continue;
            _bush = p;
            _route = route;
            GD.Print($"[m6] berry bush {(p - body).Length():0} m away ({d - (_goal - body).Length():0} m detour): gather some food first");
            return true;
        }
        // Year 120 lives on farms: take food from the home storehouse instead.
        float best = float.MaxValue;
        foreach (var b in snap.Buildings.Span)
        {
            if (b.Kind != "storehouse" || b.Progress < 1f || b.SettlementId != _homeSettlement) continue;
            var p = new Vector2(b.X, b.Z);
            if ((p - body).Length() < best) { best = (p - body).Length(); _bush = p; }
        }
        if (best == float.MaxValue || PlanRoute(snap, body, _bush) is not { } storeRoute) return false;
        _route = storeRoute;
        GD.Print($"[m6] no reachable berries; the home storehouse is {best:0} m away: take food there first");
        return true;
    }

    /// <summary>The nearest other adult of the home village (a witness for the "acting strangely" memory).</summary>
    private int NearestNeighbour(IWorldSnapshot snap)
    {
        var body = Body();
        int best = -1;
        float bestD = 20f;
        foreach (var v in snap.Npcs.Span)
        {
            if (v.Id == _npc || v.SettlementId != _homeSettlement || v.Stage is LifeStage.Infant or LifeStage.Child || v.Pose == NpcPose.Dead) continue;
            float d = (new Vector2(v.X, v.Z) - body).Length();
            if (d < bestD) { bestD = d; best = v.Id; }
        }
        return best;
    }

    private int NearGoalVillager(IWorldSnapshot snap, float radius, int exclude = -1)
    {
        var body = Body();
        int best = -1;
        float bestD = radius;
        foreach (var v in snap.Npcs.Span)
        {
            if (v.SettlementId != _goalSettlement || v.Stage is LifeStage.Infant || v.Pose == NpcPose.Dead || v.Id == _npc || v.Id == exclude) continue;
            float d = (new Vector2(v.X, v.Z) - body).Length();
            if (d < bestD) { bestD = d; best = v.Id; }
        }
        return best;
    }

    // ---- driving ----------------------------------------------------------------------------

    private Vector2 Body() => new(_dir.Controller.GlobalPosition.X, _dir.Controller.GlobalPosition.Z);

    /// <summary>Turns the camera toward <paramref name="to"/> (with a detour angle when stuck) and holds W. True on arrival.</summary>
    private bool Steer(Vector2 to, float arrive, bool sprint)
    {
        var d = to - Body();
        if (d.Length() <= arrive) { Stop(); return true; }
        float angle = MathF.Atan2(-d.X, -d.Y) + _detour switch { 1 => 0.8f, 2 => -0.8f, 3 => 1.5f, 4 => -1.5f, _ => 0f };
        _dir.Rig.Yaw = Mathf.LerpAngle(_dir.Rig.Yaw, angle, 0.2f);
        _dir.Rig.Pitch = -0.2f;
        _dir.Rig.Look(Vector2.Zero);
        _dir.Controller.ScriptedInput = new Vector2(0, 1);
        _dir.Controller.ScriptedSprint = sprint;
        return false;
    }

    private List<Vector2>? _route;
    private int _waypoint;

    /// <summary>Steers along <see cref="_route"/>; true when within <paramref name="arrive"/> of its end.</summary>
    private bool FollowRoute(float arrive, bool sprint)
    {
        var end = _route![^1];
        if ((end - Body()).Length() <= arrive) { Stop(); return true; }
        while (_waypoint < _route.Count - 1 && (_route[_waypoint] - Body()).Length() < 3f) _waypoint++;
        _detour = 0;
        Steer(_route[_waypoint], 0.5f, sprint);
        return false;
    }

    /// <summary>
    /// A player looks where they are going; the script plans it: 8-neighbour A* over the snapshot heightmap where land
    /// is anything shallower than the controller's wading limit. Waypoints every 3 cells. Null if unreachable.
    /// </summary>
    private List<Vector2>? PlanRoute(IWorldSnapshot snap, Vector2 from, Vector2 to, bool quiet = false)
    {
        _waypoint = 0;
        var w = snap.World;
        var h = snap.Heights.ToArray();
        int W = w.Width, H = w.Height;
        float limit = w.SeaLevel - _dir.Controller.MaxWadeDepth + 0.6f; // keep clear of the wading limit
        bool Land(int i) => h[i] > limit;
        int Cell(Vector2 p) => Math.Clamp((int)MathF.Round(p.Y / w.CellSize), 0, H - 1) * W + Math.Clamp((int)MathF.Round(p.X / w.CellSize), 0, W - 1);
        int start = Cell(from), goal = Cell(to);
        if (!Land(goal))
        {
            // The nearest land cell to the goal.
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < h.Length; i++)
            {
                if (!Land(i)) continue;
                float d = (new Vector2(i % W, i / W) * w.CellSize - to).LengthSquared();
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return null;
            goal = best;
        }
        var cost = new float[h.Length];
        Array.Fill(cost, float.MaxValue);
        var came = new int[h.Length];
        var open = new PriorityQueue<int, float>();
        cost[start] = 0;
        open.Enqueue(start, 0);
        float Heur(int i) => new Vector2(i % W - goal % W, i / W - goal / W).Length();
        while (open.TryDequeue(out int c, out _))
        {
            if (c == goal) break;
            int cx = c % W, cz = c / W;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = cx + dx, nz = cz + dz;
                if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                int n = nz * W + nx;
                if (!Land(n) && n != start) continue;
                float step = dx != 0 && dz != 0 ? 1.414f : 1f;
                step += MathF.Abs(h[n] - h[c]) * 0.5f; // prefer gentle slopes
                if (cost[c] + step >= cost[n]) continue;
                cost[n] = cost[c] + step;
                came[n] = c;
                open.Enqueue(n, cost[n] + Heur(n));
            }
        }
        if (cost[goal] == float.MaxValue) return null;
        var cells = new List<int>();
        for (int c = goal; c != start; c = came[c]) cells.Add(c);
        cells.Reverse();
        var route = new List<Vector2>();
        for (int i = 2; i < cells.Count; i += 3) route.Add(new Vector2(cells[i] % W, cells[i] / W) * w.CellSize);
        route.Add(cells.Count > 0 ? new Vector2(cells[^1] % W, cells[^1] / W) * w.CellSize : to);
        if (!quiet) GD.Print($"[m6] route planned: {route.Count} waypoints, {cost[goal] * w.CellSize:0} m");
        return route;
    }

    private void Stop()
    {
        _dir.Controller.ScriptedInput = Vector2.Zero;
        _dir.Controller.ScriptedSprint = false;
    }

    private void LogProgress(double delta, float left)
    {
        _progressTimer += delta;
        if (_progressTimer < 4) return;
        _progressTimer = 0;
        if (_lastDistance - left < 2f)
        {
            _detour = (_detour + 1) % 5; // stuck on a coast or a cliff: try another angle
            if (++_stalls >= 3 && _route is not null)
            {
                GD.Print("[m6] no progress for 12 s: replanning from here");
                _route = null;
                _stalls = 0;
            }
        }
        else { _stalls = 0; if (_lastDistance - left > 6f) _detour = 0; }
        _lastDistance = left;
        var c = _dir.Controller;
        GD.Print($"[m6] walking: {left:0} m to go, gap to sim {c.Gap:0.00} m, {Engine.GetFramesPerSecond():0} fps, detour {_detour}" +
                 (_debug ? $", body {c.GlobalPosition.X:0.0},{c.GlobalPosition.Y:0.0},{c.GlobalPosition.Z:0.0} v {c.Velocity.Length():0.0} floor {c.IsOnFloor()} input {c.InputVector} water {c.WaterAhead} wp {_waypoint}/{_route?.Count} next {(_route is null ? Vector2.Zero : _route[_waypoint])}" : ""));
        if (_debug) Shot($"debug-{(int)_total:000}");
    }

    // ---- output -----------------------------------------------------------------------------

    private void Dump(NpcDetail d)
    {
        GD.Print($"[m6] memories of {d.Name} (#{d.Id}), newest first:");
        foreach (var m in d.Memories.OrderByDescending(m => m.Tick).Take(14))
            GD.Print($"[m6]   tick {m.Tick} [{m.Importance}] {m.Kind}: {m.Text}");
    }

    private void Report(string when)
    {
        if (_fps.Count == 0) return;
        GD.Print($"[m6] fps {when}: min {_fps.Min():0}, avg {_fps.Average():0}, max {_fps.Max():0} over {_fps.Count} s; " +
                 $"commands sent {_dir.Controller.CommandsSent}, corrections {_dir.Controller.Corrections}, collision rebuilds {_dir.Collision.Rebuilds}");
    }

    private void Shot(string name)
    {
        if (_shots is null) return;
        string dir = _shots.StartsWith("res://") || _shots.StartsWith("user://") ? ProjectSettings.GlobalizePath(_shots) : _shots;
        string path = System.IO.Path.Combine(dir, name + ".jpg");
        var err = GetViewport().GetTexture().GetImage().SaveJpg(path, 0.85f);
        _fpsTimer = -1.0; // the readback stalls a frame; keep it out of the fps samples
        GD.Print($"[m6] screenshot {path} ({err})");
    }

    private static bool TryFind(IWorldSnapshot snap, int id, out NpcView view)
    {
        foreach (var v in snap.Npcs.Span)
            if (v.Id == id) { view = v; return true; }
        view = default;
        return false;
    }

    private static string NpcName(IWorldSnapshot snap, int id) => snap.TryGetNpcDetail(id, out var d) ? d.Name : $"#{id}";

    private static string SettlementName(IWorldSnapshot snap, int id)
        => snap is Eden.Sim.IMindSnapshot m ? m.SettlementName(id) ?? $"settlement {id}" : $"settlement {id}";

    private void Next(Phase phase)
    {
        _phase = phase;
        _t = 0;
    }

    private void Finish(string result)
    {
        _phase = Phase.Done;
        Stop();
        Report("overall");
        if (_main.Mind is { } mind)
        {
            GD.Print($"[m6] meter: {BudgetMeter.Format(mind.Status)}");
            GD.Print($"[m6] session cost: {mind.Status.NeuronsToday - _startNeurons:0.00} Neurons");
        }
        GD.Print($"[m6] result: {result}");
        GetTree().Quit();
    }
}
