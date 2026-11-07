namespace Eden.Game.Tests;

using Eden.Game.Art;
using Eden.Game.Dev;
using Eden.Sim;
using Eden.Sim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

/// <summary>A1 acceptance (docs/specs/45-art-pass.md): assets, scale, LOD counts, placement on slopes, flattening.</summary>
[TestSuite]
public class ArtPassTests
{
    [TestCase]
    [RequireGodotRuntime]
    public void Every_building_kind_has_a_mesh_and_the_assets_load_cleanly()
    {
        GltfLoader.Errors.Clear();
        var dataIds = Godot.FileAccess.GetFileAsString("res://data/buildings.json");
        foreach (var kind in BuildingKinds.All)
        {
            AssertThat(dataIds.Contains($"\"{kind.Id}\"")).IsTrue();
            AssertThat(BuildingFactory.Kinds.Contains(kind.Id)).IsTrue();
            var mesh = BuildingFactory.Mesh(kind.Id);
            AssertThat(mesh).IsNotNull();
            AssertThat(mesh!.GetSurfaceCount()).IsEqual(1);
        }
        AssertThat(BuildingFactory.Mesh(BuildingFactory.ConstructionSite)).IsNotNull();
        AssertThat(GltfLoader.Errors.Count).IsEqual(0);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Every_building_mesh_fits_inside_its_footprint()
    {
        // BuildingKind.Footprint is the clearance radius the sim keeps around a building; the mesh's half-extent
        // must stay within it (one unit = one metre).
        foreach (var kind in BuildingKinds.All)
        {
            AssertThat(BuildingFactory.FootprintOf(kind.Id)).IsEqual(kind.Footprint);
            var box = BuildingFactory.Mesh(kind.Id)!.GetAabb();
            float halfX = MathF.Max(MathF.Abs(box.Position.X), MathF.Abs(box.End.X));
            float halfZ = MathF.Max(MathF.Abs(box.Position.Z), MathF.Abs(box.End.Z));
            AssertThat(halfX).OverrideFailureMessage($"{kind.Id} half-width {halfX} > {kind.Footprint}").IsLessEqual(kind.Footprint);
            AssertThat(halfZ).OverrideFailureMessage($"{kind.Id} half-depth {halfZ} > {kind.Footprint}").IsLessEqual(kind.Footprint);
            AssertThat(halfX + halfZ).IsGreater(kind.Footprint * 0.8f); // not a toy either
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Every_land_biome_has_at_least_one_tree_variant()
    {
        foreach (byte biome in new[] { Biome.Beach, Biome.Grassland, Biome.Forest, Biome.Mountain, Biome.Snow })
        {
            var set = NatureFactory.TreeSet(biome);
            AssertThat(set.Length).IsGreater(0);
            foreach (var model in set) AssertThat(NatureFactory.Tree(model)).IsNotNull();
        }
        for (int stage = 0; stage < 3; stage++) AssertThat(NatureFactory.Crop(stage)).IsNotNull();
        AssertThat(NatureFactory.Berry()).IsNotNull();
        AssertThat(NatureFactory.Ore()).IsNotNull();
        AssertThat(NatureFactory.Stone(0)).IsNotNull();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Every_character_has_the_required_animations()
    {
        foreach (var model in CharacterLibrary.Models)
        {
            var names = CharacterLibrary.AnimationNames(model);
            foreach (var required in CharacterLibrary.RequiredAnimations)
                AssertThat(names.Contains(required)).OverrideFailureMessage($"{model.Path} lacks {required}").IsTrue();
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Npc_meshes_are_adult_height_and_children_smaller()
    {
        foreach (var model in CharacterLibrary.Models)
        {
            float h = CharacterLibrary.Pose(model, BakedPose.Idle)!.GetAabb().Size.Y;
            AssertThat(h).OverrideFailureMessage($"{model.Path} idle height {h}").IsBetween(1.7f, 1.8f);
        }
        float child = 1.75f * NpcRenderer.StageScale(LifeStage.Child);
        AssertThat(child).IsBetween(1.0f, 1.4f);
        AssertThat(1.75f * NpcRenderer.StageScale(LifeStage.Elder)).IsBetween(1.6f, 1.7f);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Hi_lod_npc_builds_an_animation_tree_and_follows_the_pose()
    {
        var hi = GD.Load<PackedScene>("res://scenes/npc/npc_hi.tscn").Instantiate<NpcHi>();
        hi._Ready();
        var view = new NpcView(3, 10, 0, 10, 0, Sex.Female, LifeStage.Adult, NpcPose.Working, 255, 1, false, false);
        hi.Apply(view, new Vector3(10, 0, 10), 0, CharacterLibrary.PaletteParams(3, 1, 0), 1f, 0.0);
        AssertThat(hi.AnimationTree).IsNotNull();
        AssertThat(hi.CurrentState).IsEqual("work");
        hi.Apply(view with { Pose = NpcPose.Sleeping }, new Vector3(10, 0, 10), 0, CharacterLibrary.PaletteParams(3, 1, 0), 1f, 0.1);
        AssertThat(hi.CurrentState).IsEqual("down");
        hi.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Lod_counts_at_2000_fake_npcs_are_50_hi_700_mid_rest_far()
    {
        using var host = new FakeSimHost(2000);
        var renderer = new NpcRenderer { HiScene = GD.Load<PackedScene>("res://scenes/npc/npc_hi.tscn") };
        renderer.Render(host.Latest, new Vector3(256, 140, 300), 0);
        AssertThat(renderer.HiLodCount).IsEqual(50);
        AssertThat(renderer.MidLodCount).IsEqual(renderer.MidLodSlots);
        AssertThat(renderer.FarLodCount).IsEqual(2000 - 50 - renderer.MidLodSlots);
        AssertThat(renderer.LowLodCount).IsEqual(1950);
        renderer.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Mid_and_far_npcs_grow_only_with_camera_height()
    {
        using var host = new FakeSimHost(100);
        var renderer = new NpcRenderer { HiScene = GD.Load<PackedScene>("res://scenes/npc/npc_hi.tscn") };
        renderer.Render(host.Latest, new Vector3(256, 40, 256), 0);
        AssertThat(renderer.ZoomScale).IsEqual(1f);
        renderer.Render(host.Latest, new Vector3(256, 400, 256), 0.1);
        AssertThat(renderer.ZoomScale).IsBetween(1.55f, 1.6f);
        renderer.Free();
    }

    /// <summary>Spec 45 placement rule 4: buildings on a 30° slope never dip below the terrain, plinths reach the ground.</summary>
    [TestCase]
    [RequireGodotRuntime]
    public void Buildings_on_a_30_degree_slope_stay_above_ground_and_plinths_reach_it()
    {
        var info = new WorldInfo(1, 64, 64, 2f, -50f, 32);
        var heights = new float[64 * 64];
        float tan30 = MathF.Tan(MathF.PI / 6f);
        for (int z = 0; z < 64; z++)
        for (int x = 0; x < 64; x++)
            heights[z * 64 + x] = x * info.CellSize * tan30; // rises 30° along +X
        float Ground(float x, float z) => HeightField.Sample(heights, info, x, z);

        int id = 0;
        foreach (var kind in BuildingFactory.Kinds)
        {
            if (kind == "dock") continue; // docks stand in water by design
            var mesh = BuildingFactory.Mesh(kind)!;
            foreach (float rotation in new[] { 0f, 0.7f, MathF.PI * 0.5f })
            {
                float cx = 60f, cz = 60f;
                var b = new BuildingView(id++, kind, cx, Ground(cx, cz), cz, rotation, 0, 1f);
                var f = Foundation.Compute(heights, info, b, mesh.GetAabb());
                var xf = new Transform3D(new Basis(Vector3.Up, rotation), f.Centre);
                var vertices = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                foreach (var v in vertices)
                {
                    var w = xf * v;
                    AssertThat(w.Y).OverrideFailureMessage($"{kind} vertex {w} below ground {Ground(w.X, w.Z)}").IsGreaterEqual(Ground(w.X, w.Z) - 0.02f);
                }
                // The plinth is the footprint scaled 1.05; its bottom must be at or below the ground at every corner.
                foreach (var corner in Foundation.Samples(f.Centre, rotation, f.HalfExtents * 1.05f))
                    AssertThat(f.Bottom).IsLessEqual(Ground(corner.X, corner.Y));
                AssertThat(f.Height).IsGreater(Foundation.PlinthThreshold + Foundation.Margin);
            }
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Terrain_flattens_only_its_render_copy_under_footprints()
    {
        using var host = new FakeSimHost(0);
        var s = host.Latest;
        var terrain = new TerrainBuilder();
        terrain.Build(s);
        // Find a land cell well above the sea and drop a hut there.
        var heights = s.Heights.Span;
        int cell = -1;
        for (int i = 0; i < heights.Length && cell < 0; i++)
            if (heights[i] > s.World.SeaLevel + 4f && heights[i] < s.World.SeaLevel + 12f) cell = i;
        AssertThat(cell).IsGreaterEqual(0);
        float x = cell % s.World.Width * s.World.CellSize, z = cell / s.World.Width * s.World.CellSize;
        var b = new BuildingView(1, "hut", x, heights[cell], z, 0.3f, 0, 1f);
        var f = Foundation.Compute(heights, s.World, b, BuildingFactory.Mesh("hut")!.GetAabb());
        var before = s.Heights.ToArray();

        terrain.ApplyFoundations([f], [b], s.Heights.Span);

        AssertThat(terrain.LastRebuiltChunks).IsBetween(1, 4);
        AssertThat(terrain.SampleRender(x, z)).IsEqualApprox(f.Top, 0.01f);
        AssertThat(s.Heights.ToArray().SequenceEqual(before)).IsTrue(); // the sim's heights are untouched
        terrain.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Fake_and_real_hosts_emit_resource_views()
    {
        using var fake = new FakeSimHost(10);
        AssertThat(fake.Latest.Resources.Length).IsGreater(100);
        var kinds = fake.Latest.Resources.ToArray().Select(r => r.Kind).Distinct().ToList();
        AssertThat(kinds.Contains("tree")).IsTrue();
        AssertThat(kinds.Contains("stone")).IsTrue();

        using var real = new SimHost(new SimConfig { Seed = 3 });
        var views = real.Latest.Resources.ToArray();
        AssertThat(views.Length).IsGreater(100);
        foreach (var r in views)
        {
            AssertThat(r.Amount).IsBetween(0f, 1f);
            AssertThat(r.Kind is "tree" or "stone" or "ore" or "berry" or "game" or "grain").IsTrue();
        }

        var renderer = new ResourceRenderer();
        renderer.Rebatch(real.Latest);
        AssertThat(renderer.ResourceInstances).IsGreater(50);
        renderer.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Building_renderer_draws_sites_plinths_and_reports_foundations()
    {
        using var host = new FakeSimHost(0);
        var s0 = host.Latest;
        var heights = s0.Heights.Span;
        int cell = -1;
        for (int i = 0; i < heights.Length && cell < 0; i++)
            if (heights[i] > s0.World.SeaLevel + 3f) cell = i;
        float x = cell % s0.World.Width * s0.World.CellSize, z = cell / s0.World.Width * s0.World.CellSize;
        var buildings = new[]
        {
            new BuildingView(1, "hut", x, heights[cell], z, 0, 0, 1f),
            new BuildingView(2, "storehouse", x + 12, heights[cell], z, 0, 0, 0.4f),
        };
        var s = new FakeWorldSnapshot(0, 0, 0, 12, s0.World, s0.Heights.ToArray(), s0.Biomes.ToArray(), [], [], null, buildings);
        var renderer = new BuildingRenderer();
        renderer.Rebatch(s);
        AssertThat(renderer.Foundations.Count).IsEqual(2);
        AssertThat(renderer.InstanceCount).IsGreaterEqual(3); // hut, site scaffold, rising storehouse (+ plinths)
        AssertThat(renderer.FoundationsVersion).IsEqual(1);
        renderer.Free();
    }

    /// <summary>The fps budget (spec 45). Needs a window and a GPU; skipped under --headless (check.sh).</summary>
    [TestCase]
    [RequireGodotRuntime]
    public async Task Fps_budget_2000_fake_npcs()
    {
        if (DisplayServer.GetName() == "headless") return; // no display: measured manually, see docs/STATUS.md
        var main = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate<Main>();
        main.UseFakeSim = true;
        main.FakeNpcCount = 2000;
        var runner = ISceneRunner.Load(main, true);
        await runner.SimulateFrames(120);
        ulong start = Time.GetTicksUsec();
        await runner.SimulateFrames(300);
        double fps = 300.0 / ((Time.GetTicksUsec() - start) / 1_000_000.0);
        AssertThat(fps).IsGreaterEqual(60.0);
    }
}
