using System.Globalization;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game.Dev;

/// <summary>
/// Scripted M7 manual check (<c>--m7-check</c>), through the same paths as a player: pick the largest settlement and
/// its nearest neighbour, put the camera there, bless a member of the chosen settlement once per in-game day for a
/// year (<see cref="Main.BlessNpc"/>, the toolbar's Bless), then let the Mind run for a few in-game days and ask two
/// people of the blessed settlement and one of the neighbour the same question through <see cref="DialoguePanel"/>.
/// Logs the settlements' standing, the Mind's latest reflections/thoughts for the three, the replies and the Neurons
/// spent. Use with <c>--year=N</c> to start from a grown world. Args: <c>--m7-days=N</c> (blessings, default 10),
/// <c>--m7-message=TEXT</c>, <c>--m7-shot=PATH</c>, <c>--m7-timeout=SECONDS</c>, <c>--m7-after-days=N</c> (default 3).
/// </summary>
public partial class M7Check : Node
{
    private enum Phase { Pick, Bless, Settle, Ask, Waiting, Done }

    private Main _main = null!;
    private Phase _phase = Phase.Pick;
    private double _total, _t;
    private int _blessed = -1, _neighbour = -1;
    private int _days = 10, _afterDays = 3, _blessings;
    private long _lastBlessDay = -1, _settleUntil;
    private double _timeout = 900, _startNeurons;
    private string _message = "What do you make of the god who watches over this island, and of your neighbours?";
    private string? _shot;
    private readonly List<int> _askees = new();
    private readonly List<int> _blessTargets = new();
    private int _asking = -1;
    private readonly List<string> _report = new();

    public override void _Ready()
    {
        _main = GetParent<Main>();
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--m7-days=") && int.TryParse(arg["--m7-days=".Length..], out int d)) _days = d;
            else if (arg.StartsWith("--m7-after-days=") && int.TryParse(arg["--m7-after-days=".Length..], out int a)) _afterDays = a;
            else if (arg.StartsWith("--m7-message=")) _message = arg["--m7-message=".Length..];
            else if (arg.StartsWith("--m7-shot=")) _shot = arg["--m7-shot=".Length..];
            else if (arg.StartsWith("--m7-timeout=") && double.TryParse(arg["--m7-timeout=".Length..], CultureInfo.InvariantCulture, out double t)) _timeout = t;
        }
        _startNeurons = _main.Mind?.Budget.NeuronsToday ?? 0;
        Log($"start: provider {_main.Mind?.Provider.Id}, {_startNeurons:0.00} Neurons already used today");
    }

    public override void _Process(double delta)
    {
        _total += delta;
        _t += delta;
        if (_phase != Phase.Done && _total > _timeout) { Finish($"timed out in phase {_phase}"); return; }
        if (_main.Host?.Latest is not { } s) return;
        long day = s.Tick / 240;
        switch (_phase)
        {
            case Phase.Pick:
                if (_t < 1.0) return;
                PickSettlements(s);
                if (_blessed < 0 || _neighbour < 0) { Finish("need two settlements"); return; }
                var b = Find(s, _blessed)!.Value;
                _main.CameraRig?.Focus(new Vector2(b.X, b.Z));
                _main.CameraRig?.SetZoom(90, immediate: true);
                _main.Hud?.God?.SetOverlay(OverlayMode.Stance);
                _main.Hud?.God?.OpenSettlement(_blessed);
                Standing(s, "before");
                _main.Hud?.TimeControls?.Request(TimeScale.X10);
                _main.Host.Enqueue(new SetTimeScaleCommand(TimeScale.X10));
                Next(Phase.Bless);
                break;
            case Phase.Bless:
                if (day == _lastBlessDay) return;
                _lastBlessDay = day;
                if (_blessings >= _days)
                {
                    Standing(s, $"after {_blessings} blessings");
                    _settleUntil = s.Tick + _afterDays * 240L;
                    Next(Phase.Settle);
                    return;
                }
                int target = BlessTarget(s);
                if (target < 0) { Finish("blessed settlement has no adults left"); return; }
                _main.BlessNpc(target, curse: false);
                _blessings++;
                Log($"day {day}: blessed {Name(s, target)} of {Find(s, _blessed)?.Name}");
                break;
            case Phase.Settle:
                if (s.Tick < _settleUntil) return;
                _main.Hud?.TimeControls?.Request(TimeScale.X1);
                _main.Host.Enqueue(new SetTimeScaleCommand(TimeScale.X1));
                Standing(s, "before asking");
                PickAskees(s);
                foreach (int id in _askees)
                {
                    string thought = _main.Mind?.CurrentThought(id) ?? "(no reflection or thought yet)";
                    Log($"latest Mind thought/reflection of {Name(s, id)} ({SettlementOf(s, id)}): {thought}");
                    if (s.TryGetNpcDetail(id, out var det))
                        foreach (var m in det.Memories.Where(m => m.Kind == "god" || m.Text.Contains("god", StringComparison.OrdinalIgnoreCase))
                                     .OrderByDescending(m => m.Tick).Take(3))
                            Log($"    memory [{m.Importance}{(m.Generation > 0 ? $", myth {m.Generation}" : "")}] {m.Text}");
                }
                Next(Phase.Ask);
                break;
            case Phase.Ask:
                if (_t < 0.5) return;
                if (++_asking >= _askees.Count) { Finish("ok"); return; }
                int who = _askees[_asking];
                _main.Select(who);
                var panel = _main.Hud?.Dialogue;
                if (panel is null || !panel.Send(_message)) { Log($"dialogue refused for {who}"); Next(Phase.Ask); return; }
                Log($"god to {Name(s, who)} of {SettlementOf(s, who)}: {_message}");
                Next(Phase.Waiting);
                break;
            case Phase.Waiting:
                var p = _main.Hud!.Dialogue!;
                if (p.Pending is not null) return;
                int npc = _askees[_asking];
                if (p.LastError is not null) Log($"dialogue with {npc} failed: {p.LastError}");
                else Log($"{Name(s, npc)} of {SettlementOf(s, npc)}: {_main.Mind!.History(npc).LastOrDefault()?.Text}");
                Next(Phase.Ask);
                break;
        }
    }

    /// <summary>The largest settlement with a neighbour, and its nearest neighbour.</summary>
    private void PickSettlements(IWorldSnapshot s)
    {
        var all = s.Settlements.ToArray();
        if (all.Length < 2) return;
        var big = all.OrderByDescending(v => v.Members).First();
        var near = all.Where(v => v.Id != big.Id).OrderBy(v => (v.X - big.X) * (v.X - big.X) + (v.Z - big.Z) * (v.Z - big.Z)).First();
        _blessed = big.Id;
        _neighbour = near.Id;
        Log($"year {s.Year + 1}: blessing {big.Name} ({big.Members} members); neighbour {near.Name} ({near.Members}), " +
            $"{MathF.Sqrt((near.X - big.X) * (near.X - big.X) + (near.Z - big.Z) * (near.Z - big.Z)):0} m away");
    }

    /// <summary>Rotates over three adults of the blessed settlement (the leader first).</summary>
    private int BlessTarget(IWorldSnapshot s)
    {
        if (_blessTargets.Count == 0)
        {
            var b = Find(s, _blessed);
            if (b is { LeaderId: >= 0 } bv) _blessTargets.Add(bv.LeaderId);
            foreach (var n in s.Npcs.Span)
                if (n.SettlementId == _blessed && n.Stage == LifeStage.Adult && !_blessTargets.Contains(n.Id) && _blessTargets.Count < 3)
                    _blessTargets.Add(n.Id);
        }
        _blessTargets.RemoveAll(id => !Alive(s, id));
        return _blessTargets.Count == 0 ? -1 : _blessTargets[_blessings % _blessTargets.Count];
    }

    /// <summary>Two people of the blessed settlement (a blessed one and one who only saw or heard of it) and the neighbour's leader or an adult.</summary>
    private void PickAskees(IWorldSnapshot s)
    {
        // The neighbour that ended up envying or fearing the blessed one, if any, else the nearest.
        var b = Find(s, _blessed);
        foreach (var v in s.Settlements.Span)
            if (v.Id != _blessed && (v.EnviedId == _blessed || v.FearedId == _blessed) && Find(s, _neighbour) is { } cur
                && v.Envy + v.Fear > cur.Envy + cur.Fear) _neighbour = v.Id;
        if (_blessTargets.Count > 0) _askees.Add(_blessTargets[0]);
        foreach (var n in s.Npcs.Span)
            if (n.SettlementId == _blessed && n.Stage == LifeStage.Adult && !_blessTargets.Contains(n.Id)) { _askees.Add(n.Id); break; }
        var nb = Find(s, _neighbour);
        if (nb is { LeaderId: >= 0 } nv && Alive(s, nv.LeaderId)) _askees.Add(nv.LeaderId);
        else
            foreach (var n in s.Npcs.Span)
                if (n.SettlementId == _neighbour && n.Stage == LifeStage.Adult) { _askees.Add(n.Id); break; }
        foreach (int id in _askees) _main.Mind?.NotePlayerInteraction(id);
        Log($"asking {string.Join(", ", _askees.Select(id => $"{Name(s, id)} ({SettlementOf(s, id)})"))}; blessed {b?.Name}, neighbour {nb?.Name}");
    }

    private void Standing(IWorldSnapshot s, string when)
    {
        foreach (int id in new[] { _blessed, _neighbour })
            if (Find(s, id) is { } v)
                Log($"{when}: {v.Name} favour {v.Favour:+0.00;-0.00} awe {v.Awe:0.00} envy {v.Envy:0.00} (of {NameOrNone(s, v.EnviedId)}) " +
                    $"fear {v.Fear:0.00} (of {NameOrNone(s, v.FearedId)}) stance {OverlayColors.StanceWord(v.Stance)} faith {v.ReligionName ?? "none"}");
    }

    private static SettlementView? Find(IWorldSnapshot s, int id)
    {
        foreach (var v in s.Settlements.Span) if (v.Id == id) return v;
        return null;
    }

    private static bool Alive(IWorldSnapshot s, int id)
    {
        foreach (var n in s.Npcs.Span) if (n.Id == id) return true;
        return false;
    }

    private static string NameOrNone(IWorldSnapshot s, int id) => id < 0 ? "none" : Find(s, id)?.Name ?? $"#{id}";

    private static string Name(IWorldSnapshot s, int id) => s.TryGetNpcDetail(id, out var d) ? d.Name : $"#{id}";

    private static string SettlementOf(IWorldSnapshot s, int id)
    {
        foreach (var n in s.Npcs.Span)
            if (n.Id == id) return Find(s, n.SettlementId)?.Name ?? "nowhere";
        return "dead";
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _t = 0;
    }

    private void Log(string line)
    {
        _report.Add(line);
        GD.Print($"[m7] {line}");
    }

    private void Finish(string result)
    {
        _phase = Phase.Done;
        if (_main.Mind is { } mind)
        {
            var st = mind.Status;
            Log($"meter: {Game.BudgetMeter.Format(st)}");
            Log($"session cost: {st.NeuronsToday - _startNeurons:0.00} Neurons (completed {st.Completed}, failures {st.Failures}, last error {st.LastError ?? "none"})");
        }
        Log($"result: {result}");
        if (_shot is not null)
        {
            var err = GetViewport().GetTexture().GetImage().SavePng(_shot);
            Log($"screenshot {_shot} ({err})");
        }
        GetTree().Quit();
    }
}
