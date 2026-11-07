using Godot;

namespace Eden.Game.Art;

/// <summary>
/// Assembles one baked mesh per building kind from Kenney Fantasy Town Kit pieces (plus a few Nature Kit props).
/// Kit pieces live on a 1-unit tile grid: a wall piece stands on the tile's +X edge, roofs cover one tile, walls are
/// one unit tall. Each kind is laid out in tile units with its door/front facing +Z, then scaled so its footprint
/// roughly matches the sim's <c>BuildingKind.Footprint</c> (metres). Settlement banners are accent parts.
/// </summary>
public static class BuildingFactory
{
    public const string Kit = "res://assets/buildings/";
    public const string Nature = "res://assets/nature/";

    /// <summary>
    /// Scale (spec 45): one unit is one metre; the sim's <c>BuildingKind.Footprint</c> is a clearance radius
    /// (<c>Buildings.SpotFree</c> keeps centres at least the sum of two footprints apart), so each mesh's half-extent
    /// stays within its footprint: hut 6 m across with 3 m eaves, storehouse/farm/archive/dock up to 8 m, well 4 m,
    /// kiln/shrine/wall 6 m. Doors are about 2 m tall.
    /// </summary>
    public static float FootprintOf(string kind) => kind switch
    {
        "storehouse" or "farm-plot" or "workshop" or "archive" or "dock" => 4f,
        "well" => 2f,
        _ => 3f,
    };

    /// <summary>Every building kind id from game/data/buildings.json, in that order.</summary>
    public static readonly string[] Kinds = ["hut", "storehouse", "farm-plot", "well", "kiln", "workshop", "shrine", "archive", "wall", "dock"];

    public const string ConstructionSite = "construction-site";

    /// <summary>Baked mesh for a kind id (or <see cref="ConstructionSite"/>), cached. Null for unknown ids.</summary>
    public static ArrayMesh? Mesh(string kind) => kind switch
    {
        "hut" => MeshBaker.Bake("b:hut", Hut()),
        "storehouse" => MeshBaker.Bake("b:storehouse", Storehouse()),
        "farm-plot" => MeshBaker.Bake("b:farm-plot", FarmPlot()),
        "well" => MeshBaker.Bake("b:well", Well()),
        "kiln" => MeshBaker.Bake("b:kiln", Kiln()),
        "workshop" => MeshBaker.Bake("b:workshop", Workshop()),
        "shrine" => MeshBaker.Bake("b:shrine", Shrine()),
        "archive" => MeshBaker.Bake("b:archive", Archive()),
        "wall" => MeshBaker.Bake("b:wall", Wall()),
        "dock" => MeshBaker.Bake("b:dock", Dock()),
        ConstructionSite => MeshBaker.Bake("b:site", Site()),
        _ => null,
    };

    // ---- layout helpers (tile units) --------------------------------------------------------------

    /// <summary>Tile grid scaled to metres: <paramref name="tile"/> metres per tile across, <paramref name="storey"/> metres per unit up.</summary>
    private sealed class Layout(float tile, float storey, Recolor recolor)
    {
        public readonly List<Part> Parts = new();
        public Vector3 Scale = new(tile, storey, tile);
        public Recolor Recolor = recolor;

        /// <summary>A kit piece at tile (x, z), rotated by quarter turns (0: +X edge, 1: -Z, 2: -X, 3: +Z), y up in tiles.</summary>
        public Layout Add(string piece, float x, float z, int quarter = 0, float y = 0, Vector3? stretch = null, Recolor? recolor = null,
            bool accent = false, Color? tint = null, string root = Kit)
        {
            var s = stretch ?? Vector3.One;
            // World grid scale * rotation * stretch, so the stretch is along the piece's own axes.
            var basis = Basis.FromScale(Scale) * new Basis(Vector3.Up, quarter * MathF.PI * 0.5f) * Basis.FromScale(s);
            var xf = new Transform3D(basis, new Vector3(x, y, z) * Scale);
            Parts.Add(new Part(root + piece + ".glb", xf, recolor ?? Recolor, accent, false, tint));
            return this;
        }

        /// <summary>Four walls around a w × d block of tiles centred on the origin. <paramref name="pick"/> chooses the piece per edge slot.</summary>
        public Layout Walls(int w, int d, Func<int, int, string> pick, float y = 0)
        {
            float x0 = -(w - 1) * 0.5f, z0 = -(d - 1) * 0.5f;
            int slot = 0;
            for (int i = 0; i < w; i++)
            {
                Add(pick(3, slot++), x0 + i, z0 + d - 1, 3, y); // front (+Z)
                Add(pick(1, slot++), x0 + i, z0, 1, y); // back (-Z)
            }
            for (int j = 0; j < d; j++)
            {
                Add(pick(0, slot++), x0 + w - 1, z0 + j, 0, y); // east
                Add(pick(2, slot++), x0, z0 + j, 2, y); // west
            }
            return this;
        }
    }

    // ---- kinds --------------------------------------------------------------------------------------

    /// <summary>Hut: one-room timber-frame cottage, plaster walls, door front, steep pyramid roof, chimney.</summary>
    private static List<Part> Hut()
    {
        var l = new Layout(5.1f, 3.0f, Recolor.TownPlaster);
        l.Walls(1, 1, (side, _) => side switch { 3 => "wall-door", 0 => "wall-window-small", 2 => "wall-detail-cross", _ => "wall-detail-diagonal" });
        l.Add("roof-high-point", 0, 0, 0, 1f, new Vector3(1.05f, 0.9f, 1.05f));
        l.Add("chimney", 0, -0.1f, 0, 0.9f, new Vector3(0.8f, 0.9f, 0.8f), Recolor.TownBrick);
        l.Add("banner-red", 0, 0.02f, 3, 0.05f, new Vector3(0.7f, 0.7f, 0.7f), accent: true);
        return l.Parts;
    }

    /// <summary>Storehouse: long timber barn, wood walls, gable roof along X, banner.</summary>
    private static List<Part> Storehouse()
    {
        var l = new Layout(3.5f, 3.2f, Recolor.TownPlaster);
        l.Walls(2, 1, (side, slot) => side switch { 3 => slot == 0 ? "wall-wood-door" : "wall-wood-detail-cross", 1 => "wall-wood-detail-horizontal", _ => "wall-wood" });
        l.Add("roof-gable", -0.5f, 0, 0, 1f, new Vector3(1.0f, 1.1f, 1.08f));
        l.Add("roof-gable", 0.5f, 0, 0, 1f, new Vector3(1.0f, 1.1f, 1.08f));
        l.Add("banner-green", 0.5f, 0.02f, 3, 0.15f, new Vector3(0.8f, 0.8f, 0.8f), accent: true);
        return l.Parts;
    }

    /// <summary>Farm plot: tilled rows inside a low fence (the crop itself is drawn from the "grain" resource view).</summary>
    private static List<Part> FarmPlot()
    {
        var l = new Layout(1.95f, 2.45f, Recolor.Nature);
        for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
            l.Add("crops_dirtDoubleRow", i, j, 0, 0.01f, new Vector3(1f, 1f, 1f), root: Nature);
        for (int i = -1; i <= 1; i++)
        {
            l.Add("fence_simple", i, -1.5f, 0, 0, null, root: Nature);
            l.Add("fence_simple", i, 1.5f, 0, 0, null, root: Nature);
            l.Add("fence_simple", -1.5f, i, 1, 0, null, root: Nature);
            l.Add("fence_simple", 1.5f, i, 1, 0, null, root: Nature);
        }
        return l.Parts;
    }

    /// <summary>Well: blue-grey stone ring, two wooden posts and a small tiled roof (reference: lowpoly_village).</summary>
    private static List<Part> Well()
    {
        var l = new Layout(3.6f, 2.0f, Recolor.TownStone);
        l.Add("fountain-round", 0, 0, 0, 0, new Vector3(0.55f, 2.2f, 0.55f));
        l.Add("pillar-wood", -0.44f, 0, 0, 0, new Vector3(1f, 1.35f, 1f));
        l.Add("pillar-wood", 0.44f, 0, 0, 0, new Vector3(1f, 1.35f, 1f));
        l.Add("roof-gable", 0, 0, 1, 1.3f, new Vector3(0.8f, 0.6f, 0.95f));
        return l.Parts;
    }

    /// <summary>Kiln: squat brick oven with a tall chimney.</summary>
    private static List<Part> Kiln()
    {
        var l = new Layout(4.0f, 2.6f, Recolor.TownBrick);
        l.Add("wall-block", 0, 0, 0, 0, new Vector3(1.1f, 0.8f, 1.1f));
        l.Add("roof-flat", 0, 0, 0, 0.8f, new Vector3(1.2f, 1f, 1.2f));
        l.Add("chimney-base", 0, 0, 0, 0.85f, new Vector3(1f, 0.8f, 1f));
        l.Add("chimney", 0, 0, 0, 1.25f, new Vector3(1f, 1.2f, 1f));
        l.Add("chimney-top", 0, 0, 0, 2.3f, null);
        l.Add("log_stack", 0.42f, 0.4f, 1, 0, new Vector3(0.55f, 0.7f, 0.55f), Recolor.Nature, root: Nature);
        return l.Parts;
    }

    /// <summary>Workshop: open-sided timber shed over workbenches, with a cart.</summary>
    private static List<Part> Workshop()
    {
        var l = new Layout(3.5f, 3.0f, Recolor.TownPlaster);
        float h = 1.1f;
        foreach (var (x, z) in new[] { (-0.95f, -0.45f), (0.95f, -0.45f), (-0.95f, 0.45f), (0.95f, 0.45f) })
            l.Add("pillar-wood", x, z, 0, 0, new Vector3(1.2f, h, 1.2f));
        l.Add("wall-wood-detail-horizontal", -0.5f, 0, 1, 0, new Vector3(1f, 0.9f, 1f));
        l.Add("wall-wood-detail-horizontal", 0.5f, 0, 1, 0, new Vector3(1f, 0.9f, 1f));
        l.Add("roof-gable", -0.5f, 0, 0, h, new Vector3(1.05f, 0.85f, 1.15f));
        l.Add("roof-gable", 0.5f, 0, 0, h, new Vector3(1.05f, 0.85f, 1.15f));
        l.Add("stall-bench", -0.4f, -0.1f, 0, 0, new Vector3(1f, 1f, 1f));
        l.Add("stall-bench", 0.4f, -0.1f, 0, 0, new Vector3(1f, 1f, 1f));
        l.Add("cart", 0.6f, 0.62f, 1, 0, new Vector3(0.75f, 0.9f, 0.75f));
        l.Add("banner-red", 0.45f, 0.55f, 3, 0.1f, new Vector3(0.7f, 0.7f, 0.7f), accent: true);
        return l.Parts;
    }

    /// <summary>Shrine: stone platform, four stone pillars, a pointed roof, an altar with offerings and an obelisk behind it.</summary>
    private static List<Part> Shrine()
    {
        var l = new Layout(3.0f, 2.8f, Recolor.TownStone);
        l.Add("roof-flat", 0, 0, 0, 0, new Vector3(1.6f, 1.2f, 1.6f));
        foreach (var (x, z) in new[] { (-0.65f, -0.65f), (0.65f, -0.65f), (-0.65f, 0.65f), (0.65f, 0.65f) })
            l.Add("pillar-stone", x, z, 0, 0.15f, new Vector3(1.2f, 1.2f, 1.2f));
        l.Add("roof-high-point", 0, 0, 0, 1.35f, new Vector3(1.6f, 0.9f, 1.6f));
        // A2: an altar with offerings under the roof (procedural), the obelisk moved behind it.
        l.Add("statue_obelisk", 0, -0.42f, 0, 0.15f, new Vector3(1.0f, 1.0f, 1.0f), Recolor.Nature, root: Nature);
        l.Parts.Add(new Part(MeshBaker.ProceduralPrefix + "altar", new Transform3D(Basis.Identity, new Vector3(0, 0.15f * 2.8f + 0.04f, 0.25f))));
        l.Add("banner-red", 0.3f, 0.68f, 3, 0.2f, new Vector3(0.6f, 0.6f, 0.6f), accent: true);
        return l.Parts;
    }

    /// <summary>Archive: two-storey stone-and-plaster hall with a high roof and banners.</summary>
    private static List<Part> Archive()
    {
        var l = new Layout(3.6f, 2.6f, Recolor.TownStone);
        l.Walls(2, 2, (side, slot) => side == 3 && slot == 0 ? "wall-door" : "wall-window-shutters");
        l.Recolor = Recolor.TownPlaster;
        l.Walls(2, 2, (side, _) => side is 3 or 1 ? "wall-detail-cross" : "wall-window-small", 1f);
        l.Recolor = Recolor.TownStone;
        l.Add("roof-high-point", 0, 0, 0, 2f, new Vector3(1.95f, 1.3f, 1.95f));
        l.Add("banner-red", -0.5f, 0.52f, 3, 0.05f, new Vector3(0.8f, 0.8f, 0.8f), accent: true);
        l.Add("banner-red", 0.5f, 0.52f, 3, 0.05f, new Vector3(0.8f, 0.8f, 0.8f), accent: true);
        return l.Parts;
    }

    /// <summary>Wall: a stretch of grey stone rampart with crenels.</summary>
    private static List<Part> Wall()
    {
        var l = new Layout(2.0f, 2.5f, Recolor.TownStone);
        for (int i = -1; i <= 1; i++)
        {
            l.Add("wall-block", i, 0, 0, 0, new Vector3(1f, 1.2f, 0.6f));
            l.Add("wall-block", i - 0.25f, 0, 0, 1.2f, new Vector3(0.3f, 0.3f, 0.6f));
            l.Add("wall-block", i + 0.25f, 0, 0, 1.2f, new Vector3(0.3f, 0.3f, 0.6f));
        }
        return l.Parts;
    }

    /// <summary>Dock: plank deck on posts reaching into the water, with a mooring post.</summary>
    private static List<Part> Dock()
    {
        var l = new Layout(2.6f, 1.6f, Recolor.TownPlaster);
        for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
            l.Add("planks", i * 0.95f, j * 0.95f, 0, 0.35f, new Vector3(1f, 1.5f, 1f));
        foreach (var (x, z) in new[] { (-1.4f, -1.4f), (1.4f, -1.4f), (-1.4f, 1.4f), (1.4f, 1.4f), (0f, 1.4f) })
            l.Add("pillar-wood", x, z, 0, -0.8f, new Vector3(1.3f, 1.4f, 1.3f));
        return l.Parts;
    }

    /// <summary>Construction site: corner posts, a beam frame and a stack of logs. Drawn under the rising building.</summary>
    private static List<Part> Site()
    {
        var l = new Layout(2.0f, 1.6f, Recolor.TownPlaster);
        foreach (var (x, z) in new[] { (-1.4f, -1.4f), (1.4f, -1.4f), (-1.4f, 1.4f), (1.4f, 1.4f) })
            l.Add("pillar-wood", x, z, 0, 0, new Vector3(1.4f, 2.4f, 1.4f));
        l.Add("poles-horizontal", 0, -0.9f, 1, 1.4f, new Vector3(1f, 1f, 2.9f));
        l.Add("poles-horizontal", 0, 0.9f, 3, 1.4f, new Vector3(1f, 1f, 2.9f));
        l.Add("planks", 0.4f, 1.1f, 0, 0, new Vector3(1.2f, 1.6f, 0.8f));
        l.Add("log_stack", -1.1f, 0.5f, 1, 0, new Vector3(1.6f, 1.6f, 1.6f), Recolor.Nature, root: Nature);
        return l.Parts;
    }
}
