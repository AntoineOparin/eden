namespace Eden.Game.Tests;

using Eden.Game.Dev;
using Eden.Sim.Contracts;
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
        AssertThat(main.UseFakeSim).IsFalse(); // the real SimHost stays the default
        main._Ready();
        AssertThat(main.Host).IsNotNull();
        AssertThat(main.Host is Eden.Sim.SimHost).IsTrue();
        AssertThat(main.Host!.Latest.World.Width).IsEqual(256);
        main._ExitTree();
        main.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public async Task Main_scene_with_fake_sim_renders_1000_npcs_and_wires_ui()
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        main.UseFakeSim = true;
        main.FakeNpcCount = 1000;
        var runner = ISceneRunner.Load(main, true);
        await runner.SimulateFrames(5);

        AssertThat(main.Host is FakeSimHost).IsTrue();
        AssertThat(main.Terrain!.ChunkCount).IsEqual(64);
        AssertThat(main.Npcs!.RenderedCount).IsEqual(1000);
        AssertThat(main.Npcs.HiLodCount).IsEqual(50);
        AssertThat(main.Npcs.LowLodCount).IsEqual(950);

        // Time controls -> SetTimeScaleCommand -> fake host on its own thread.
        var host = (FakeSimHost)main.Host!;
        main.Hud!.TimeControls!.Request(TimeScale.X100);
        for (int i = 0; i < 50 && host.Scale != TimeScale.X100; i++) await runner.AwaitMillis(10);
        AssertThat(host.Scale).IsEqual(TimeScale.X100);
        main.Hud.TimeControls.Request(TimeScale.Paused);
        for (int i = 0; i < 50 && host.Scale != TimeScale.Paused; i++) await runner.AwaitMillis(10);
        AssertThat(host.Scale).IsEqual(TimeScale.Paused);

        // Inspector reads TryGetNpcDetail for the selection.
        main.Select(7);
        AssertThat(main.Hud.Inspector!.Visible).IsTrue();
        AssertThat(main.Hud.Inspector.Detail).IsNotNull();
        AssertThat(main.Hud.Inspector.Detail!.Id).IsEqual(7);
        AssertThat(main.Npcs.SelectedId).IsEqual(7);
        main.Select(-1);
        AssertThat(main.Hud.Inspector.Visible).IsFalse();
    }
}
