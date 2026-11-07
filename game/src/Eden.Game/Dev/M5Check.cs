using System.Globalization;
using Eden.Sim;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game.Dev;

/// <summary>
/// Scripted M5 manual check (<c>--m5-check</c>), driving the same code paths as a player: fast-forward until a
/// village has a leader, select the leader (inspector + dialogue panel), strike lightning next to them through
/// <see cref="Main.StrikeAt"/>, wait for the memory, then speak through <see cref="DialoguePanel.Send"/> with a line
/// that does not mention the strike, and log the reply and the budget meter. Args: <c>--m5-year=N</c> (earliest
/// year, default 3), <c>--m5-message=TEXT</c>, <c>--m5-shot=PATH</c> (screenshot), <c>--m5-timeout=SECONDS</c>.
/// </summary>
public partial class M5Check : Node
{
    private enum Phase { FastForward, Settle, Strike, Talk, Waiting, Done }

    private Main _main = null!;
    private Phase _phase = Phase.FastForward;
    private double _t, _total, _replyAt = -1;
    private int _leader = -1;
    private double _startNeurons;
    private int _minYear = 3;
    private double _timeout = 300;
    private string _message = "Tell me, how do your people fare today?";
    private string? _shot;

    public override void _Ready()
    {
        _main = GetParent<Main>();
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--m5-year=") && int.TryParse(arg["--m5-year=".Length..], out int y)) _minYear = y;
            else if (arg.StartsWith("--m5-message=")) _message = arg["--m5-message=".Length..];
            else if (arg.StartsWith("--m5-shot=")) _shot = arg["--m5-shot=".Length..];
            else if (arg.StartsWith("--m5-timeout=") && double.TryParse(arg["--m5-timeout=".Length..], CultureInfo.InvariantCulture, out double t)) _timeout = t;
        }
        _startNeurons = _main.Mind?.Budget.NeuronsToday ?? 0;
        GD.Print($"[m5] start: provider {_main.Mind?.Provider.Id}, {_startNeurons:0.00} Neurons already used today");
        _main.Host?.Enqueue(new SetTimeScaleCommand(TimeScale.X500));
    }

    public override void _Process(double delta)
    {
        _t += delta;
        _total += delta;
        if (_phase != Phase.Done && _total > _timeout) { Finish($"timed out in phase {_phase}"); return; }
        var host = _main.Host;
        if (host is null) return;
        var snap = host.Latest;
        switch (_phase)
        {
            case Phase.FastForward:
                if (snap.Year < _minYear || snap is not IMindSnapshot extras || extras.LeaderIds.Count == 0) return;
                // The leader of the largest village.
                var views = snap.Npcs.ToArray();
                _leader = extras.LeaderIds.OrderByDescending(id => views.Count(v => v.SettlementId == SettlementOf(views, id))).First();
                var lv = views.First(v => v.Id == _leader);
                host.Enqueue(new SetTimeScaleCommand(TimeScale.X1));
                _main.Hud?.TimeControls?.Request(TimeScale.X1);
                _main.CameraRig?.Focus(new Vector2(lv.X, lv.Z));
                _main.CameraRig?.SetZoom(60, immediate: true);
                GD.Print($"[m5] year {snap.Year + 1}: leader {_leader} of {extras.SettlementName(lv.SettlementId)} " +
                         $"({views.Count(v => v.SettlementId == lv.SettlementId)} people), population {snap.Population}");
                Next(Phase.Settle);
                break;
            case Phase.Settle:
                _main.Select(_leader); // inspector + dialogue panel; asks the sim for details
                if (_t < 1.5) return;
                Next(Phase.Strike);
                break;
            case Phase.Strike:
                var v0 = snap.Npcs.ToArray().FirstOrDefault(v => v.Id == _leader);
                if (v0.Id != _leader) { Finish("leader died before the strike"); return; }
                _main.StrikeAt(v0.X + 7f, v0.Z);
                GD.Print($"[m5] lightning at ({v0.X + 7f:0.0}, {v0.Z:0.0}), 7 m from the leader, radius {_main.LightningRadius}");
                Next(Phase.Talk);
                break;
            case Phase.Talk:
                if (_t < 3) return;
                if (snap.TryGetNpcDetail(_leader, out var d))
                {
                    _main.Select(_leader); // refresh the name on the panel
                    GD.Print($"[m5] {d.Name}'s newest memories:");
                    foreach (var m in d.Memories.OrderByDescending(m => m.Tick).Take(5))
                        GD.Print($"[m5]   [{m.Importance}] {m.Text}");
                }
                var panel = _main.Hud?.Dialogue;
                if (panel is null || !panel.Send(_message)) { Finish("dialogue panel refused to send"); return; }
                GD.Print($"[m5] god: {_message}");
                Next(Phase.Waiting);
                break;
            case Phase.Waiting:
                var p = _main.Hud!.Dialogue!;
                if (p.Pending is not null) return;
                if (p.LastError is not null) { Finish($"dialogue failed: {p.LastError}"); return; }
                if (_replyAt < 0)
                {
                    _replyAt = _total;
                    GD.Print($"[m5] {p.NpcName}: {_main.Mind!.History(_leader).LastOrDefault()?.Text}");
                }
                if (_total - _replyAt < 1) return; // let the panel and meter draw before the screenshot
                _main.Hud.UpdateMind(_main.Mind.Status);
                GD.Print($"[m5] transcript:\n{p.Transcript}");
                Finish("ok");
                break;
        }
    }

    private static int SettlementOf(NpcView[] views, int id)
    {
        foreach (var v in views) if (v.Id == id) return v.SettlementId;
        return -1;
    }

    private void Next(Phase phase)
    {
        _phase = phase;
        _t = 0;
    }

    private void Finish(string result)
    {
        _phase = Phase.Done;
        var mind = _main.Mind;
        if (mind is not null)
        {
            var s = mind.Status;
            GD.Print($"[m5] meter: {Game.BudgetMeter.Format(s)}");
            GD.Print($"[m5] session cost: {s.NeuronsToday - _startNeurons:0.00} Neurons (completed {s.Completed}, failures {s.Failures}, last error {s.LastError ?? "none"})");
        }
        GD.Print($"[m5] result: {result}");
        if (_shot is not null)
        {
            var err = GetViewport().GetTexture().GetImage().SavePng(_shot);
            GD.Print($"[m5] screenshot {_shot} ({err})");
        }
        GetTree().Quit();
    }
}
