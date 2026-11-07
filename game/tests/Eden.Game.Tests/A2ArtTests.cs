namespace Eden.Game.Tests;

using Eden.Game.Art;
using Eden.Game.Dev;
using Eden.Sim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;

/// <summary>
/// A2 acceptance (docs/specs/45-art-pass.md "A2 as built"): animals, less chibi characters, stelae / altars / bridges /
/// draped roads and their placement rules, decor rebuilt per chunk, clouds following the weather, the atlas cache key.
/// </summary>
[TestSuite]
public class A2ArtTests
{
    private static readonly WorldInfo Info = new(7, 64, 64, 2f, 0f, 32);

    /// <summary>A synthetic island: a 30° slope in X, plus an optional channel of sea (x in 60..72 m) cutting it.</summary>
    private static float[] Slope(bool channel = false)
    {
        var h = new float[Info.Width * Info.Height];
        float tan30 = MathF.Tan(MathF.PI / 6f);
        for (int z = 0; z < Info.Height; z++)
        for (int x = 0; x < Info.Width; x++)
        {
            float wx = x * Info.CellSize;
            float v = 2f + wx * tan30 * 0.25f; // gentle 7.5° base so buildings and roads fit
            if (channel && wx >= 60f && wx <= 72f) v = -1.5f;
            h[z * Info.Width + x] = v;
        }
        return h;
    }

    private static FakeWorldSnapshot Snapshot(float[] heights, BuildingView[] buildings, ResourceView[]? resources = null)
    {
        var biomes = new byte[heights.Length];
        Array.Fill(biomes, Biome.Grassland);
        return new FakeWorldSnapshot(0, 0, 0, 12, Info, heights, biomes, [], [], resources, buildings);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Animals_load_at_true_size_with_feet_on_the_ground()
    {
        GltfLoader.Errors.Clear();
        foreach (var s in AnimalLibrary.All)
        {
            var mesh = AnimalLibrary.Mesh(s);
            AssertThat(mesh).OverrideFailureMessage($"{s} did not bake").IsNotNull();
            var aabb = mesh!.GetAabb();
            AssertThat(aabb.Size.Y).IsEqualApprox(AnimalLibrary.HeightOf(s), 0.02f);
            AssertThat(aabb.Position.Y).IsEqualApprox(0f, 0.01f);
            AssertThat(aabb.Size.Y).IsGreater(AnimalLibrary.ShoulderOf(s));
        }
        AssertThat(string.Join("; ", GltfLoader.Errors)).IsEqual("");
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Herds_place_two_to_five_animals_on_the_ground_inside_the_herd()
    {
        var heights = Slope();
        var herds = new[]
        {
            new ResourceView(1, "game", 40, 0, 40, 1f, 3),
            new ResourceView(2, "game", 80, 0, 60, 0.3f, 4),
            new ResourceView(3, "game", 90, 0, 90, 0f, 5), // hunted out: no animals
        };
        var s = Snapshot(heights, [], herds);
        var renderer = new ResourceRenderer();
        renderer.Ground = (x, z) => MathF.Max(Info.SeaLevel, HeightField.Sample(heights, Info, x, z));
        renderer.Render(s, 1.0);
        var placed = renderer.AnimalPositions(37.0, s).ToList();
        AssertThat(placed.Count).IsEqual(5 + 3);
        AssertThat(renderer.AnimalInstances).IsEqual(8);
        foreach (var (species, p) in placed)
        {
            var nearest = herds.MinBy(h => new Vector2(h.X - p.X, h.Z - p.Z).Length());
            float d = new Vector2(nearest.X - p.X, nearest.Z - p.Z).Length();
            AssertThat(d).IsLessEqual(renderer.HerdRadius * 1.15f + renderer.AnimalDrift + 0.01f);
            float ground = HeightField.Sample(heights, Info, p.X, p.Z);
            AssertThat(p.Y).IsBetween(ground - 0.08f, ground); // hooves bedded a few cm, never floating
        }
        renderer.Free();

        // Cost: 200 full herds (1,000 animals) re-placed per frame stay under the brief's 1 ms (2 ms allowed for CI noise).
        var many = Enumerable.Range(0, 200).Select(i => new ResourceView(100 + i, "game", 10 + i % 14 * 8, 0, 10 + i / 14 * 8, 1f, (byte)i)).ToArray();
        var big = Snapshot(heights, [], many);
        var r2 = new ResourceRenderer { Ground = (x, z) => MathF.Max(Info.SeaLevel, HeightField.Sample(heights, Info, x, z)), DecorDensity = 0 };
        r2.Render(big, 1.0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        const int frames = 20;
        for (int f = 0; f < frames; f++) r2.Render(big, 1.0 + f / 60.0);
        double ms = sw.Elapsed.TotalMilliseconds / frames;
        GD.Print($"[a2] 200 herds, {r2.AnimalInstances} animals: {ms:0.000} ms per frame");
        AssertThat(r2.AnimalInstances).IsEqual(1000);
        AssertThat(ms).IsLess(2.0);
        r2.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Characters_are_less_chibi_and_still_adult_height()
    {
        foreach (var model in CharacterLibrary.Models)
        {
            var template = CharacterLibrary.Template(model)!;
            float ratio = CharacterProportions.HeadRatio(template);
            AssertThat(ratio).OverrideFailureMessage($"{model.Path} head ratio {ratio}").IsBetween(0.15f, 0.26f);
            var pose = CharacterLibrary.Pose(model, BakedPose.Idle)!;
            AssertThat(pose.GetAabb().Size.Y).IsBetween(1.7f, 1.8f);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Stela_altar_and_bridge_pieces_have_their_sizes()
    {
        var stela = ProceduralMeshes.Stela().GetAabb();
        AssertThat(stela.End.Y).IsEqualApprox(1.6f, 0.02f);
        AssertThat(stela.Position.Y).IsLessEqual(-0.3f); // footing below ground
        var altar = ProceduralMeshes.Altar().GetAabb();
        AssertThat(altar.Size.X).IsBetween(1.4f, 1.8f);
        AssertThat(altar.End.Y).IsBetween(0.9f, 1.1f);
        AssertThat(ProceduralMeshes.Named("altar")).IsNotNull();
        AssertThat(ProceduralMeshes.BridgeSegment().GetAabb().Size.X).IsEqualApprox(ProceduralMeshes.BridgeWidth, 0.01f);
        for (int v = 0; v < ProceduralMeshes.CloudVariants; v++)
            AssertThat(ProceduralMeshes.Cloud(v).GetAabb().Position.Y).IsGreaterEqual(-0.001f); // flat base
        // The shrine now carries the altar and still fits its footprint (ArtPassTests checks every kind).
        var shrine = BuildingFactory.Mesh("shrine")!.GetAabb();
        AssertThat(MathF.Max(MathF.Abs(shrine.Position.X), MathF.Abs(shrine.End.X))).IsLessEqual(BuildingFactory.FootprintOf("shrine"));
    }

    /// <summary>Spec 45 placement rules for the A2 pieces: stelae on a 30° slope keep their footing under the ground.</summary>
    [TestCase]
    [RequireGodotRuntime]
    public void Stelae_on_a_30_degree_slope_never_float()
    {
        float tan30 = MathF.Tan(MathF.PI / 6f);
        float Ground(float x, float z) => x * tan30;
        var mesh = ProceduralMeshes.Stela();
        var verts = mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var n = new Vector3(-tan30, 1f, 0).Normalized();
        float angle = MathF.Min(MathF.Acos(n.Y), Mathf.DegToRad(ResourceRenderer.MaxTiltDegrees));
        foreach (float yaw in new[] { 0f, 1f, 2.5f })
        {
            var p = new Vector3(20, Ground(20, 0) - 0.05f, 0);
            var xf = new Transform3D(new Basis(Vector3.Up.Cross(n).Normalized(), angle) * new Basis(Vector3.Up, yaw), p);
            // The lowest point of the footing under each corner column is below the ground there.
            float minAbove = float.MaxValue;
            foreach (var v in verts)
            {
                if (v.Y > -0.3f) continue; // footing bottom ring
                var w = xf * v;
                minAbove = MathF.Min(minAbove, w.Y - Ground(w.X, w.Z));
                AssertThat(w.Y).IsLessEqual(Ground(w.X, w.Z) + 0.001f);
            }
            AssertThat(minAbove).IsLess(0f);
        }
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Roads_drape_on_the_terrain_and_softer_yards_blend_out()
    {
        var heights = Slope();
        var buildings = new[]
        {
            new BuildingView(1, "hut", 30, 0, 30, 0, 0, 1f), new BuildingView(2, "hut", 52, 0, 34, 0.4f, 0, 1f),
            new BuildingView(3, "storehouse", 40, 0, 58, 0, 0, 1f), new BuildingView(4, "archive", 70, 0, 80, 0, 0, 1f),
        };
        for (int i = 0; i < buildings.Length; i++)
            buildings[i] = buildings[i] with { Y = HeightField.Sample(heights, Info, buildings[i].X, buildings[i].Z) };
        var s = Snapshot(heights, buildings);
        var terrain = new TerrainBuilder();
        terrain.Build(s);
        var foundations = buildings.Select(b => Foundation.Compute(heights, Info, b, BuildingFactory.Mesh(b.Kind)!.GetAabb())).ToList();
        terrain.ApplyFoundations(foundations, buildings, heights);
        AssertThat(terrain.Roads.Count).IsGreaterEqual(3);

        // Softer terraces: just outside a footprint the ground is between the natural slope and the foundation top.
        var f = foundations[0];
        float outside = f.Centre.X + f.HalfExtents.X + 1.6f;
        float natural = HeightField.Sample(heights, Info, outside, f.Centre.Z), drawn = terrain.SampleRender(outside, f.Centre.Z);
        AssertThat(drawn).IsBetween(MathF.Min(natural, f.Top) - 0.01f, MathF.Max(natural, f.Top) + 0.01f);

        var roads = new RoadRenderer { Terrain = terrain };
        roads.Rebuild(s, terrain.Roads);
        var verts = roads.RoadVerticesOf(0);
        AssertThat(verts.Length).IsGreater(40);
        foreach (var v in verts)
        {
            float g = terrain.SampleRender(v.X, v.Z);
            AssertThat(v.Y).OverrideFailureMessage($"road vertex {v} vs ground {g}").IsBetween(g + 0.01f, g + 0.15f);
        }
        AssertThat(roads.Stelae).IsBetween(1, 3); // in front of the archive
        foreach (var xf in roads.StelaTransforms)
            AssertThat(xf.Origin.Y).IsLessEqual(terrain.SampleRender(xf.Origin.X, xf.Origin.Z));
        // Rebuilding with nothing changed touches nothing.
        roads.Rebuild(s, terrain.Roads);
        AssertThat(roads.LastRebuilt).IsEqual(0);
        roads.Free();
        terrain.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Roads_cross_water_on_bridges_that_rest_on_both_banks()
    {
        var heights = Slope(channel: true);
        var buildings = new[]
        {
            new BuildingView(1, "hut", 44, 0, 60, 0, 0, 1f), new BuildingView(2, "hut", 90, 0, 60, 0, 0, 1f),
        };
        for (int i = 0; i < buildings.Length; i++)
            buildings[i] = buildings[i] with { Y = HeightField.Sample(heights, Info, buildings[i].X, buildings[i].Z) };
        var s = Snapshot(heights, buildings);
        var terrain = new TerrainBuilder();
        terrain.Build(s);
        var foundations = buildings.Select(b => Foundation.Compute(heights, Info, b, BuildingFactory.Mesh(b.Kind)!.GetAabb())).ToList();
        terrain.ApplyFoundations(foundations, buildings, heights);
        AssertThat(terrain.Roads.Any(r => r.Bridges.Length > 0)).IsTrue();

        var roads = new RoadRenderer { Terrain = terrain };
        roads.Rebuild(s, terrain.Roads);
        var bridges = roads.Bridges.ToList();
        AssertThat(bridges.Count).IsGreaterEqual(1);
        foreach (var (start, end, piles) in bridges)
        {
            foreach (var deckEnd in new[] { start, end })
            {
                float bank = terrain.SampleRenderRaw(deckEnd.X, deckEnd.Z);
                AssertThat(bank).IsGreater(Info.SeaLevel); // on land
                AssertThat(deckEnd.Y - 0.12f - bank).IsBetween(-0.1f, 0.02f); // plank bottom on the bank, not floating
            }
            AssertThat(piles.Count).IsGreaterEqual(4);
            foreach (var (_, foot) in piles)
                AssertThat(foot.Y).IsLessEqual(terrain.SampleRenderRaw(foot.X, foot.Z));
        }
        // The road mesh stops at the water: no strip vertex over the sea.
        foreach (var v in roads.RoadVerticesOf(0))
            AssertThat(terrain.SampleRenderRaw(v.X, v.Z)).IsGreater(Info.SeaLevel - 0.01f);
        roads.Free();
        terrain.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Bridge_finder_spans_water_and_notches_only()
    {
        var flat = Enumerable.Repeat(3f, 30).ToArray();
        AssertThat(RoadNetwork.FindBridges(flat, 0f).Length).IsEqual(0);
        var wet = (float[])flat.Clone();
        for (int k = 10; k < 16; k++) wet[k] = -1f;
        var spans = RoadNetwork.FindBridges(wet, 0f);
        AssertThat(spans.Length).IsEqual(1);
        AssertThat(spans[0].From).IsEqual(9f);
        AssertThat(spans[0].To).IsEqual(16f);
        var notch = (float[])flat.Clone();
        notch[14] = 0.8f; notch[15] = 0.6f; notch[16] = 0.9f;
        AssertThat(RoadNetwork.FindBridges(notch, 0f).Length).IsEqual(1);
        var slope = Enumerable.Range(0, 30).Select(i => i * 0.3f).ToArray(); // a steady climb is not a notch
        AssertThat(RoadNetwork.FindBridges(slope, -5f).Length).IsEqual(0);
    }

    [TestCase]
    [RequireGodotRuntime]
    public void A_new_building_rebuilds_only_the_decor_chunks_it_touches()
    {
        using var host = new FakeSimHost(0);
        var s0 = host.Latest;
        var terrain = new TerrainBuilder();
        terrain.Build(s0);
        var resources = new ResourceRenderer { Terrain = terrain, Ground = terrain.SampleRender };
        resources.Render(s0, 0);
        int full = resources.LastDecorCellsScanned;
        AssertThat(full).IsGreater(50_000);

        var heights = s0.Heights.Span;
        int cell = -1;
        for (int i = 0; i < heights.Length && cell < 0; i++)
            if (heights[i] > s0.World.SeaLevel + 4f && heights[i] < s0.World.SeaLevel + 12f) cell = i;
        float x = cell % s0.World.Width * s0.World.CellSize, z = cell / s0.World.Width * s0.World.CellSize;
        var b = new BuildingView(1, "hut", x, heights[cell], z, 0.3f, 0, 1f);
        terrain.ApplyFoundations([Foundation.Compute(heights, s0.World, b, BuildingFactory.Mesh("hut")!.GetAabb())], [b], heights);
        resources.GroundVersion = terrain.RenderVersion;
        resources.Render(s0, 1); // notices the change, schedules the dirty chunks
        resources.Render(s0, 2); // the frame after: rebuilds them
        AssertThat(resources.LastDecorCellsScanned).IsBetween(1, full / 6);
        resources.Free();
        terrain.Free();
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Clouds_thicken_in_rain_and_thin_in_drought()
    {
        using var host = new FakeSimHost(0);
        var s = host.Latest;
        var clouds = new CloudLayer();
        clouds.Configure(s.World, s.Heights.Span);
        AssertThat(clouds.CloudCount).IsEqual(clouds.Count);
        clouds.Settle();
        int clear = clouds.VisibleClouds;
        clouds.Weather.Begin(SkyWeather.Rain, 100);
        clouds.Settle();
        int rain = clouds.VisibleClouds;
        AssertThat(clouds.Tint.R).IsLess(0.7f);
        clouds.Weather.Begin(SkyWeather.Drought, 100);
        clouds.Settle();
        int drought = clouds.VisibleClouds;
        AssertThat(rain).IsGreater(clear);
        AssertThat(clear).IsGreater(drought);
        clouds.Free();

        AssertThat(WeatherState.FromText("The god sent rain.")).IsEqual(SkyWeather.Rain);
        AssertThat(WeatherState.FromText("The god withheld the rain; a drought began.")).IsEqual(SkyWeather.Drought);
        AssertThat(WeatherState.FromText("The skies cleared.")).IsEqual(SkyWeather.Clear);
        AssertThat(WeatherState.FromText("Ada was born.")).IsNull();
        var w = new WeatherState();
        w.Begin(SkyWeather.Rain, 50);
        w.Observe(new FakeWorldSnapshot(60, 0, 0, 12, s.World, s.Heights.ToArray(), s.Biomes.ToArray(), [], []));
        AssertThat(w.Kind).IsEqual(SkyWeather.Clear); // expired by tick
    }

    [TestCase]
    [RequireGodotRuntime]
    public void Impostor_atlas_cache_key_tracks_its_inputs()
    {
        ulong a = ImpostorAtlas.CacheKey(96, 57f, 2.4f);
        AssertThat(ImpostorAtlas.CacheKey(96, 57f, 2.4f)).IsEqual(a);
        AssertThat(ImpostorAtlas.CacheKey(128, 57f, 2.4f)).IsNotEqual(a);
        AssertThat(ImpostorAtlas.CacheKey(96, 50f, 2.4f)).IsNotEqual(a);
        var atlas = new ImpostorAtlas();
        AssertThat(atlas.CachePath).StartsWith(ImpostorAtlas.CacheDir);
        atlas.Free();
    }
}
