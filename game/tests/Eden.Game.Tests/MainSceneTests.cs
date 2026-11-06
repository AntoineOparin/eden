namespace Eden.Game.Tests;

using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
public class MainSceneTests
{
    [TestCase]
    public void Sim_config_defaults_are_sane()
    {
        var cfg = new Eden.Sim.SimConfig();
        AssertThat(cfg.TicksPerYear).IsEqual(2400);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Main_scene_instantiates_and_boots_sim()
    {
        var scene = GD.Load<PackedScene>("res://scenes/main.tscn");
        AssertThat(scene).IsNotNull();
        var main = scene.Instantiate<Main>();
        AssertThat(main).IsNotNull();
        main._Ready();
        AssertThat(main.Host).IsNotNull();
        AssertThat(main.Host!.Latest.World.Width).IsEqual(256);
        main._ExitTree();
        main.Free();
    }
}
