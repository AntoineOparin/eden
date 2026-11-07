using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// M7 UI hook for <see cref="Main"/> (kept in its own file so Main.cs only gains the one <see cref="SetupM7"/> call):
/// the remaining god powers as commands plus their local effects (<see cref="PowerFx"/>), the territory overlay
/// (<see cref="TerritoryOverlay"/>), and the wiring of the HUD's <see cref="GodUi"/> (settlement panel, Chronicle,
/// overlay controls) and the inspector's settlement link. Clicks go through <see cref="OnPickM7"/>, which handles the
/// M7 powers and passes Select / Lightning / Whisper on to <see cref="OnPickRequested"/> unchanged.
/// </summary>
public partial class Main
{
    /// <summary>Terraform height change per click, metres at the brush centre.</summary>
    [Export] public float TerraformStep { get; set; } = 2.5f;
    /// <summary>Rain and drought last this many in-game days.</summary>
    [Export] public int WeatherDays { get; set; } = 3;
    /// <summary>A blessing or curse lasts this many in-game days.</summary>
    [Export] public int BlessDays { get; set; } = 2;

    public PowerFx? Fx { get; private set; }
    public TerritoryOverlay? Territory { get; private set; }
    public GodUi? GodUi => Hud?.God;

    private static readonly int TicksPerDay = new Eden.Sim.SimConfig().TicksPerDay;

    /// <summary>Called once from <see cref="_Ready"/> after the host, camera and HUD are wired.</summary>
    private void SetupM7(IWorldSnapshot s)
    {
        Fx = new PowerFx { Name = "PowerFx", Tick = UpdateBrush };
        AddChild(Fx);
        Territory = new TerritoryOverlay { Name = "Territory", Visible = false };
        if (Terrain is not null) Territory.Ground = Terrain.SampleRender;
        AddChild(Territory);
        if (CameraRig is not null)
        {
            CameraRig.PickRequested -= OnPickRequested;
            CameraRig.PickRequested += OnPickM7;
        }
        if (Hud?.God is { } ui)
        {
            ui.Frame = () => _frame;
            ui.Overlay = Territory;
            Territory.Knowledge = ui.Knowledge;
            ui.Knowledge.Enqueue = c => Host?.Enqueue(c); // C1: WatchSettlementCommand
            ui.FocusRequested += p => CameraRig?.Focus(new Vector2(p.X, p.Z));
            ui.NpcRequested += Select;
        }
        if (Hud?.Inspector is { } inspector) inspector.SettlementRequested += id => Hud?.God?.OpenSettlement(id);
        if (Hud?.Powers is { } powers) powers.PowerChanged += _ => Fx?.ShowBrush(null, 0, true);
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--ui=")) ApplyUiArg(arg["--ui=".Length..]);
            else if (arg == "--m7-check") AddChild(new Dev.M7Check { Name = "M7Check" });
            else if (arg == "--log-frame-time") AddChild(new Dev.FrameTimeProbe { Name = "FrameTimeProbe" });
        }
    }

    /// <summary>
    /// Dev flag <c>--ui=a,b,...</c> for screenshots: <c>chronicle</c>, <c>settlement[:id]</c>,
    /// <c>settlement:town</c> (the one with most buildings), <c>overlay:stance|religion|knowledge[:concept]</c>, <c>power:&lt;GodPower&gt;</c>, <c>select:&lt;npc id&gt;</c>.
    /// </summary>
    private void ApplyUiArg(string spec)
    {
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var bits = part.Split(':');
            switch (bits[0])
            {
                case "chronicle": Hud?.God?.ToggleChronicle(true); break;
                case "settlement":
                    int id = bits.Length > 1 && int.TryParse(bits[1], out int k) ? k : _frame?.Settlements.Length > 0 ? _frame.Settlements.Span[0].Id : -1;
                    if (bits.Length > 1 && bits[1] == "town" && _frame is not null && FocusPoint(_frame, "town") is { } town) id = SettlementAt(town.X, town.Y) ?? id;
                    Hud?.God?.OpenSettlement(id);
                    break;
                case "overlay" when bits.Length > 1:
                    var mode = bits[1] switch { "religion" => OverlayMode.Religion, "knowledge" => OverlayMode.Knowledge, _ => OverlayMode.Stance };
                    Hud?.God?.SetOverlay(mode, bits.Length > 2 ? bits[2] : null);
                    break;
                case "power" when bits.Length > 1 && Enum.TryParse<GodPower>(bits[1], true, out var p): Hud?.Powers?.Choose(p); break;
                case "select" when bits.Length > 1 && int.TryParse(bits[1], out int npc): Select(npc); break;
            }
        }
    }

    /// <summary>Left click on the world with the current power.</summary>
    private void OnPickM7(Vector2 screen)
    {
        var power = Hud?.Powers?.Current ?? GodPower.Select;
        if (PowerTargets.IsPerson(power))
        {
            int id = PersonAt(screen);
            if (id >= 0) BlessNpc(id, power == GodPower.Curse);
            return;
        }
        if (power is GodPower.Select or GodPower.Whisper or GodPower.Lightning)
        {
            OnPickRequested(screen);
            if (power == GodPower.Select && Npcs?.SelectedId < 0 && GroundAt(screen) is { } g && SettlementAt(g.X, g.Z) is int set && set >= 0)
                Hud?.God?.OpenSettlement(set);
            return;
        }
        if (GroundAt(screen) is { } ground) ApplyPower(power, ground.X, ground.Z);
    }

    /// <summary>Sends the command for a ground-targeted power and plays its effect. Returns false for other powers.</summary>
    public bool ApplyPower(GodPower power, float x, float z)
    {
        var host = Host;
        if (host is null) return false;
        float radius = Hud?.Powers?.BrushRadius ?? 12f;
        int tpd = TicksPerDay;
        switch (power)
        {
            case GodPower.Lightning: StrikeAt(x, z); return true;
            case GodPower.SpawnHuman: host.Enqueue(new SpawnCommand("human", x, z)); break;
            case GodPower.SpawnAnimal: host.Enqueue(new SpawnCommand("animal:deer", x, z, 3)); break;
            case GodPower.SpawnPlant: host.Enqueue(new SpawnCommand("plant:berry", x, z, 3)); break;
            case GodPower.Rain: host.Enqueue(new WeatherCommand("rain", WeatherDays * tpd)); break;
            case GodPower.Drought: host.Enqueue(new WeatherCommand("drought", WeatherDays * tpd)); break;
            case GodPower.RaiseTerrain: host.Enqueue(new TerraformCommand(x, z, radius, TerraformStep)); break;
            case GodPower.LowerTerrain: host.Enqueue(new TerraformCommand(x, z, radius, -TerraformStep)); break;
            case GodPower.DropFood: host.Enqueue(new DropItemCommand("food", x, z, 3)); break;
            default: return false;
        }
        Fx?.Play(power, new Vector3(x, GroundY(x, z), z), radius);
        return true;
    }

    /// <summary>Bless or curse one person (the god's standing in their settlement moves in the sim).</summary>
    public void BlessNpc(int npcId, bool curse)
    {
        if (Host is null || npcId < 0) return;
        Host.Enqueue(new BlessCommand(npcId, curse, BlessDays * TicksPerDay));
        Mind?.NotePlayerInteraction(npcId);
        if (_frame is null) return;
        foreach (var n in _frame.Npcs.Span)
            if (n.Id == npcId)
            {
                Fx?.Play(curse ? GodPower.Curse : GodPower.Bless, new Vector3(n.X, n.Y, n.Z));
                break;
            }
    }

    /// <summary>The person under the cursor, or else the nearest living person within 6 m of the ground point.</summary>
    private int PersonAt(Vector2 screen)
    {
        if (Npcs is not null && CameraRig?.Camera is { } cam)
        {
            int id = Npcs.Pick(cam, screen);
            if (id >= 0) return id;
        }
        if (_frame is null || GroundAt(screen) is not { } g) return -1;
        int best = -1;
        float bestD = 36f;
        foreach (var n in _frame.Npcs.Span)
        {
            if (n.Pose == NpcPose.Dead) continue;
            float d = (n.X - g.X) * (n.X - g.X) + (n.Z - g.Z) * (n.Z - g.Z);
            if (d < bestD) { bestD = d; best = n.Id; }
        }
        return best;
    }

    /// <summary>
    /// The settlement at a ground point: the owner of a building within 8 m, else the settlement whose centre is
    /// within its territory radius (nearest first). Null when nothing is there.
    /// </summary>
    public int? SettlementAt(float x, float z)
    {
        if (_frame is null) return null;
        int best = -1;
        float bestD = 64f; // 8 m
        foreach (var b in _frame.Buildings.Span)
        {
            float d = (b.X - x) * (b.X - x) + (b.Z - z) * (b.Z - z);
            if (d < bestD && b.SettlementId >= 0) { bestD = d; best = b.SettlementId; }
        }
        if (best >= 0) return best;
        float bestRatio = 1f; // nearest centre relative to its own territory radius (C1: the sim's radius per settlement)
        foreach (var v in _frame.Settlements.Span)
        {
            float r = Territory?.RadiusOf(v) ?? TerritoryOverlay.RadiusFor(v, TerritoryOverlay.DefaultRadius);
            float ratio = ((v.X - x) * (v.X - x) + (v.Z - z) * (v.Z - z)) / (r * r);
            if (ratio < bestRatio) { bestRatio = ratio; best = v.Id; }
        }
        return best >= 0 ? best : null;
    }

    private float GroundY(float x, float z)
        => _frame is null ? 0 : MathF.Max(_frame.World.SeaLevel, Terrain?.SampleRender(x, z) ?? HeightField.Sample(_frame.Heights.Span, _frame.World, x, z));

    /// <summary>Per frame (PowerFx.Tick): the terraform brush preview under the mouse.</summary>
    internal void UpdateBrush()
    {
        if (Fx is null) return;
        var power = Hud?.Powers?.Current ?? GodPower.Select;
        if (power is not (GodPower.RaiseTerrain or GodPower.LowerTerrain) || GetViewport() is not { } vp || vp.GuiGetHoveredControl() is not null)
        {
            if (Fx.BrushVisible) Fx.ShowBrush(null, 0, true);
            return;
        }
        Fx.ShowBrush(GroundAt(vp.GetMousePosition()), Hud?.Powers?.BrushRadius ?? 12f, power == GodPower.RaiseTerrain);
    }
}
