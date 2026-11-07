namespace Eden.Game.Tests;

using Eden.Game.Dev;
using Eden.Sim;
using Eden.Sim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

[TestSuite]
public class PossessionTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void Possess_rig_walks_where_the_camera_looks_and_clamps_pitch()
    {
        var rig = GD.Load<PackedScene>("res://scenes/cameras/possess_rig.tscn").Instantiate<PossessRig>();
        rig.Initialise();
        AssertThat(rig.Arm.CollisionMask).IsEqual(TerrainCollision.Layer);
        AssertThat(rig.Arm.SpringLength).IsEqual(rig.ArmLength);

        rig.Yaw = 0;
        AssertThat(rig.Forward.DistanceTo(new Vector3(0, 0, -1))).IsLess(1e-4f);
        AssertThat(rig.Right.DistanceTo(new Vector3(1, 0, 0))).IsLess(1e-4f);
        rig.Yaw = Mathf.Pi / 2;
        AssertThat(rig.Forward.DistanceTo(new Vector3(-1, 0, 0))).IsLess(1e-4f);

        rig.Look(new Vector2(0, -100000));
        AssertThat(rig.Pitch).IsEqualApprox(Mathf.DegToRad(rig.MaxPitchDegrees), 1e-4f);
        rig.Look(new Vector2(0, 100000));
        AssertThat(rig.Pitch).IsEqualApprox(Mathf.DegToRad(rig.MinPitchDegrees), 1e-4f);

        rig.SetFirstPerson(true);
        AssertThat(rig.Arm.SpringLength).IsEqual(0f);
        rig.ToggleFirstPerson();
        AssertThat(rig.FirstPerson).IsFalse();
        rig.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Terrain_collision_covers_only_the_chunks_around_the_body()
    {
        using var host = new FakeSimHost(10, 3);
        var terrain = new TerrainBuilder();
        terrain.Build(host.Latest);
        var collision = new TerrainCollision();
        var info = host.Latest.World;
        float chunk = info.ChunkSize * info.CellSize;

        collision.UpdateAround(new Vector3(chunk * 3.5f, 0, chunk * 4.5f), terrain);
        AssertThat(collision.ShapeCount).IsEqual(9);
        AssertThat(collision.Rebuilds).IsEqual(9);
        AssertThat(collision.Chunks.Contains((3, 4))).IsTrue();
        AssertThat(TerrainCollision.ChunkFaces(terrain, 3, 4).Length).IsEqual(info.ChunkSize * info.ChunkSize * 6);

        collision.UpdateAround(new Vector3(chunk * 3.6f, 0, chunk * 4.4f), terrain); // same chunk: nothing to do
        AssertThat(collision.Rebuilds).IsEqual(9);
        collision.UpdateAround(new Vector3(chunk * 4.5f, 0, chunk * 4.5f), terrain); // one chunk east: three new columns
        AssertThat(collision.ShapeCount).IsEqual(9);
        AssertThat(collision.Rebuilds).IsEqual(12);
        collision.UpdateAround(new Vector3(1, 0, 1), terrain); // map corner: 2x2
        AssertThat(collision.ShapeCount).IsEqual(4);

        // The faces sit on the drawn terrain.
        var faces = TerrainCollision.ChunkFaces(terrain, 3, 4);
        AssertThat(faces[0].Y).IsEqualApprox(terrain.RenderHeightAt(3 * info.ChunkSize, 4 * info.ChunkSize), 1e-4f);
        collision.Clear();
        AssertThat(collision.ShapeCount).IsEqual(0);
        collision.Free();
        terrain.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Renderer_leaves_the_possessed_npc_to_the_controller()
    {
        using var host = new FakeSimHost(1000);
        var renderer = new NpcRenderer { HiScene = GD.Load<PackedScene>("res://scenes/npc/npc_hi.tscn") };
        var viewer = new Vector3(256, 60, 256);
        renderer.Render(host.Latest, viewer, 0);
        int id = renderer.HiLodNpcIds.First();
        renderer.PossessedId = id;
        renderer.Render(host.Latest, viewer, 0.05);
        AssertThat(renderer.HiLodNpcIds.Contains(id)).IsFalse();
        AssertThat(renderer.HiLodCount).IsEqual(50);
        AssertThat(renderer.MidLodCount + renderer.FarLodCount).IsEqual(949); // drawn nowhere
        renderer.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Hud_hides_god_panels_while_possessed_and_restores_them()
    {
        var hud = GD.Load<PackedScene>("res://scenes/ui/hud.tscn").Instantiate<Hud>();
        hud._Ready();
        hud.Inspector!.Visible = false;
        var top = hud.GetNode<Control>("TopBar");
        AssertThat(top.Visible).IsTrue();
        bool? seen = null;
        hud.PossessedChanged += on => seen = on;

        hud.SetPossessed(true);
        AssertThat(hud.IsPossessed).IsTrue();
        AssertThat(seen).IsEqual(true);
        AssertThat(top.Visible).IsFalse();
        AssertThat(hud.GetNode<Control>("PowersBar").Visible).IsFalse();

        hud.SetPossessed(false);
        AssertThat(top.Visible).IsTrue();
        AssertThat(hud.GetNode<Control>("PowersBar").Visible).IsTrue();
        AssertThat(hud.Inspector.Visible).IsFalse(); // it was hidden before, it stays hidden
        hud.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Dialogue_panel_speaks_as_the_possessed_npc_until_reset()
    {
        var panel = GD.Load<PackedScene>("res://scenes/ui/dialogue.tscn").Instantiate<DialoguePanel>();
        panel._Ready();
        panel.ShowPossessed(7, "Ada", 3, "Bram");
        AssertThat(panel.IsPossessedSpeaker).IsTrue();
        AssertThat(panel.SpeakerName).IsEqual("Bram");
        AssertThat(panel.Visible).IsTrue();
        panel.ShowNpc(7, "Ada");
        AssertThat(panel.IsPossessedSpeaker).IsFalse();
        panel.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Interaction_prompt_prefers_people_then_bushes_stores_and_sites()
    {
        var npc = new PossessionDirector.Interaction(PossessionDirector.InteractionKind.Npc, 4, "Ada", Vector3.Zero);
        AssertThat(PossessionDirector.PromptFor(npc)).Contains("E  talk to Ada").Contains("G  give food").Contains("R  work alongside");
        var bush = new PossessionDirector.Interaction(PossessionDirector.InteractionKind.Bush, 1, "berry bush", Vector3.Zero);
        AssertThat(PossessionDirector.PromptFor(bush)).IsEqual("E  pick berries");
        AssertThat(PossessionDirector.PromptFor(default)).IsEqual("");
        var store = new PossessionDirector.Interaction(PossessionDirector.InteractionKind.Store, 2, "storehouse", Vector3.Zero);
        AssertThat(PossessionDirector.PromptFor(npc, store)).EndsWith("T  take food from the store");
        AssertThat(PossessionDirector.PromptFor(default, store)).IsEqual("E  take food from the store");
    }

    [TestCase(Timeout = 60000)]
    [RequireGodotRuntime]
    public async Task Possess_walk_and_release_through_the_main_scene()
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        main.UseBrain = false;
        main.Seed = 7;
        var runner = ISceneRunner.Load(main, true);
        await runner.SimulateFrames(5);
        var dir = main.GetNode<PossessionDirector>("Possession");
        dir.MouseCaptureEnabled = false;
        var host = main.Host!;
        var start = host.Latest.Npcs.ToArray()[0];

        main.Select(start.Id);
        await runner.SimulateFrames(20, 20); // the HUD refreshes five times a second
        AssertThat(dir.PossessionHud.PossessButtonVisible).IsTrue();
        dir.PossessSelected();
        AssertThat(dir.IsPossessed).IsTrue();
        AssertThat(main.Hud!.IsPossessed).IsTrue();
        AssertThat(main.Npcs!.PossessedId).IsEqual(start.Id);
        AssertThat(main.Hud.TimeControls!.Current).IsEqual(TimeScale.X1);
        AssertThat(main.CameraRig!.InputEnabled).IsFalse();
        AssertThat(main.GetViewport().GetCamera3D() == dir.Rig.Camera).IsTrue();
        for (int i = 0; i < 100 && !dir.Confirmed; i++) await runner.AwaitMillis(20);
        AssertThat(dir.Confirmed).IsTrue();
        AssertThat(dir.Collision.ShapeCount).IsGreater(0);

        // Walk "forward" for a second and a half: the sim follows the body (2 ticks/s at 1x while possessed).
        var simStart = new Vector2(start.X, start.Z);
        dir.Controller.ScriptedInput = new Vector2(0, 1);
        for (int i = 0; i < 90; i++) await runner.SimulateFrames(1, 16);
        dir.Controller.ScriptedInput = Vector2.Zero;
        for (int i = 0; i < 80; i++) await runner.SimulateFrames(1, 16);
        var now = host.Latest.Npcs.ToArray().First(v => v.Id == start.Id);
        var body = dir.Controller.GlobalPosition;
        AssertThat(dir.Controller.CommandsSent).IsGreater(0);
        AssertThat(new Vector2(now.X, now.Z).DistanceTo(simStart)).IsGreater(0.5f);
        AssertThat(new Vector2(now.X, now.Z).DistanceTo(new Vector2(body.X, body.Z))).IsLess(1.0f); // the sim caught up
        AssertThat(dir.PossessionHud.CardVisible).IsTrue();

        dir.Release();
        AssertThat(dir.IsPossessed).IsFalse();
        AssertThat(main.Hud.IsPossessed).IsFalse();
        AssertThat(main.Npcs.PossessedId).IsEqual(-1);
        AssertThat(main.CameraRig.InputEnabled).IsTrue();
        AssertThat(main.GetViewport().GetCamera3D() == main.CameraRig.Camera).IsTrue();
        AssertThat(main.Npcs.SelectedId).IsEqual(start.Id); // the inspector shows the body's memories
        for (int i = 0; i < 100 && host.Latest.Npcs.ToArray().Any(v => v.IsPossessed); i++) await runner.AwaitMillis(20);
        AssertThat(host.Latest.Npcs.ToArray().Any(v => v.IsPossessed)).IsFalse();
        NpcDetail? detail = null;
        for (int i = 0; i < 100 && (detail is null || !detail.Memories.Any(m => m.Text.Contains("I lost time"))); i++)
        {
            host.Latest.TryGetNpcDetail(start.Id, out detail);
            await runner.AwaitMillis(20);
        }
        AssertThat(detail!.Memories.Any(m => m.Text.Contains("I lost time"))).IsTrue();
    }

    [TestCase(Timeout = 30000)]
    [RequireGodotRuntime]
    public async Task Fake_sim_never_confirms_so_possession_falls_back_to_god_view()
    {
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        main.UseFakeSim = true;
        main.FakeNpcCount = 50;
        var runner = ISceneRunner.Load(main, true);
        await runner.SimulateFrames(3);
        var dir = main.GetNode<PossessionDirector>("Possession");
        dir.MouseCaptureEnabled = false;
        dir.ConfirmSeconds = 0.2;
        AssertThat(dir.Possess(main.Host!.Latest.Npcs.Span[0].Id)).IsTrue();
        for (int i = 0; i < 100 && dir.IsPossessed; i++) await runner.SimulateFrames(1, 20);
        AssertThat(dir.IsPossessed).IsFalse();
        AssertThat(main.Hud!.IsPossessed).IsFalse();
        AssertThat(dir.PossessionHud.NoticeText).Contains("did not let you in");
    }
}
