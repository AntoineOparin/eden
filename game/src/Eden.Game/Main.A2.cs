using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// A2 art hook for <see cref="Main"/> (its own file so Main.cs only gains the one <see cref="SetupA2"/> call): the cloud
/// layer and the weather it shows, the road / bridge / stela renderer, the terrain link that lets decor and props
/// refresh only the chunks a new building touched, and the <c>--fx-check=DIR</c> dev flag (<see cref="Dev.FxCheck"/>).
/// Every node here reads the frame Main already read (<c>_frame</c>); nothing touches the sim.
/// </summary>
public partial class Main
{
    public CloudLayer? Clouds { get; private set; }
    public RoadRenderer? Roads { get; private set; }

    /// <summary>Called once from <see cref="_Ready"/> after <see cref="SetupM7"/>.</summary>
    private void SetupA2(IWorldSnapshot s)
    {
        if (ResourceProps is not null) ResourceProps.Terrain = Terrain;

        Roads = new RoadRenderer { Name = "Roads", Terrain = Terrain, Frame = () => _frame };
        AddChild(Roads);

        Clouds = new CloudLayer { Name = "Clouds", Frame = () => _frame, Daylight = () => Sky?.Daylight ?? 1f };
        AddChild(Clouds);
        Clouds.Configure(s.World, s.Heights.Span);
        if (Fx is not null)
            Fx.WeatherPlayed += kind => Clouds?.Weather.Begin(kind, (_frame?.Tick ?? 0) + (long)WeatherDays * TicksPerDay);

        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            // Scripted runs (screenshots, perf, fx check) must not take keystrokes meant for other windows: a stray
            // hotkey once opened a panel and switched the power in a "paused" screenshot run.
            if ((arg.StartsWith("--quit-after=") || arg.StartsWith("--fx-check=")) && DisplayServer.GetName() != "headless")
            {
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
                // and stay on top: macOS stops drawing a fully covered window, which froze screenshots mid-run.
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.AlwaysOnTop, true);
                if (CameraRig is not null) CameraRig.EdgePan = false; // nor pan when the pointer happens to rest on an edge
            }
            if (arg.StartsWith("--fx-check=")) AddChild(new Dev.FxCheck { Name = "FxCheck", OutputDir = arg["--fx-check=".Length..] });
            else if (arg == "--cloud-shadows")
                foreach (var child in Clouds.GetChildren())
                    if (child is GeometryInstance3D g) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        }
    }
}
