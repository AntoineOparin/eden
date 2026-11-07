using Godot;

namespace Eden.Game.Art;

/// <summary>
/// Baked meshes for resource nodes and ground decor, from the Kenney Nature Kit, recoloured to the palette.
/// Trees come in sets per biome; the renderer picks one by <c>ResourceView.Variant</c>. Every set is non-empty for
/// every land biome (tested). Kenney nature models are about one unit tall, hence the scales here.
/// </summary>
public static class NatureFactory
{
    public const string Root = "res://assets/nature/";

    private static readonly string[] GrassTrees = ["tree_oak", "tree_default", "tree_detailed", "tree_fat"];
    private static readonly string[] ForestTrees = ["tree_pineDefaultA", "tree_pineRoundC", "tree_pineTallA", "tree_pineDefaultB", "tree_default_dark"];
    private static readonly string[] BeachTrees = ["tree_palmTall", "tree_palmDetailedShort", "tree_palmShort"];
    private static readonly string[] MountainTrees = ["tree_pineSmallA", "tree_pineSmallB", "tree_pineRoundA"];

    /// <summary>Tree model names for a biome byte (<see cref="Biome"/>). Ocean falls back to beach palms.</summary>
    public static string[] TreeSet(byte biome) => biome switch
    {
        Biome.Beach or Biome.Ocean => BeachTrees,
        Biome.Forest => ForestTrees,
        Biome.Mountain or Biome.Snow => MountainTrees,
        _ => GrassTrees,
    };

    /// <summary>All distinct tree set ids used by <see cref="TreeSet"/>, for tests and preloading.</summary>
    public static IEnumerable<byte> TreeBiomes => [Biome.Beach, Biome.Grassland, Biome.Forest, Biome.Mountain, Biome.Snow];

    public static ArrayMesh? Tree(string model) => MeshBaker.Bake("n:" + model, [P(model, 4.6f)]);

    public static ArrayMesh? Stump() => MeshBaker.Bake("n:stump", [P("stump_roundDetailed", 4.5f), P("log", 3.2f, new Vector3(0.7f, 0, 0.4f), 0.6f)]);

    private static readonly string[] Stones = ["stone_largeA", "stone_largeB", "stone_largeC", "stone_tallA"];

    public static int StoneVariants => Stones.Length;

    public static ArrayMesh? Stone(int variant)
    {
        string m = Stones[(int)((uint)variant % Stones.Length)];
        return MeshBaker.Bake("n:" + m, [P(m, 2.6f), P("stone_smallB", 2f, new Vector3(1.1f, 0, 0.6f), 1.3f)]);
    }

    /// <summary>Ore outcrop: grey rock with rusty lumps.</summary>
    public static ArrayMesh? Ore() => MeshBaker.Bake("n:ore",
    [
        P("stone_tallB", 2.4f),
        new Part(Root + "stone_smallA.glb", Xf(2.2f, new Vector3(0.9f, 0, 0.5f), 0.4f), Recolor.Nature, Tint: Palette.Ore),
        new Part(Root + "stone_smallC.glb", Xf(2.0f, new Vector3(-0.7f, 0, 0.8f), 2.1f), Recolor.Nature, Tint: Palette.Ore),
    ]);

    /// <summary>Berry bush: a round green bush dotted with red berries.</summary>
    public static ArrayMesh? Berry() => MeshBaker.Bake("n:berry",
    [
        new Part(Root + "plant_bushLarge.glb", Xf(5.5f, Vector3.Zero, 0), Recolor.Nature, Leaf: true),
        new Part(Root + "flower_redA.glb", Xf(2.6f, new Vector3(0.35f, 0.55f, 0.2f), 0.3f), Recolor.Nature, Tint: Palette.Berry),
        new Part(Root + "flower_redB.glb", Xf(2.6f, new Vector3(-0.4f, 0.5f, -0.1f), 1.7f), Recolor.Nature, Tint: Palette.Berry),
    ]);

    /// <summary>Picked-clean berry bush (amount near zero).</summary>
    public static ArrayMesh? BerryBare() => MeshBaker.Bake("n:berry-bare", [new Part(Root + "plant_bush.glb", Xf(5.0f, Vector3.Zero, 0), Recolor.Nature, Leaf: true)]);

    /// <summary>Crop on a farm plot, stage 0 (sprouts), 1 (growing), 2 (ripe, golden). 3 × 3 clumps over the 4 m plot.</summary>
    public static ArrayMesh? Crop(int stage)
    {
        stage = Math.Clamp(stage, 0, 2);
        string model = stage == 0 ? "crops_wheatStageA" : "crops_wheatStageB";
        var parts = new List<Part>();
        for (int i = -1; i <= 1; i++)
        for (int j = -1; j <= 1; j++)
        {
            float sc = stage == 0 ? 3.8f : 4.6f;
            var xf = new Transform3D(new Basis(Vector3.Up, (i + j) * 0.2f).Scaled(new Vector3(sc, sc, sc)), new Vector3(i * 1.95f, 0.02f, j * 1.95f));
            parts.Add(new Part(Root + model + ".glb", xf, Recolor.Nature, Leaf: true, Tint: stage switch
            {
                0 => Palette.LeafLight,
                1 => Palette.Hex(0x9CC74A),
                _ => Palette.Wheat,
            }));
        }
        return MeshBaker.Bake("n:crop" + stage, parts);
    }

    /// <summary>Ground decor per biome: grass tufts, flower clumps, mushrooms, pebbles. Index by a hash.</summary>
    public static ArrayMesh?[] Decor(byte biome) => biome switch
    {
        Biome.Grassland =>
        [
            MeshBaker.Bake("d:grass", [new Part(Root + "grass_large.glb", Xf(3.2f, Vector3.Zero, 0), Recolor.Nature, Leaf: true)]),
            MeshBaker.Bake("d:grass2", [new Part(Root + "grass.glb", Xf(3.5f, Vector3.Zero, 0), Recolor.Nature, Leaf: true)]),
            MeshBaker.Bake("d:flowers-red", [P("flower_redA", 3.2f), P("grass", 3f, new Vector3(0.25f, 0, 0.1f), 1f)]),
            MeshBaker.Bake("d:flowers-yellow", [P("flower_yellowA", 3.2f), P("grass", 3f, new Vector3(-0.2f, 0, 0.1f), 2f)]),
            MeshBaker.Bake("d:flowers-white", [new Part(Root + "flower_purpleA.glb", Xf(3.2f, Vector3.Zero, 0), Recolor.Nature, Tint: Palette.Hex(0xF4F1E6))]),
        ],
        Biome.Forest =>
        [
            MeshBaker.Bake("d:fern", [new Part(Root + "plant_flatTall.glb", Xf(3.5f, Vector3.Zero, 0), Recolor.Nature, Leaf: true, Tint: Palette.LeafDark)]),
            MeshBaker.Bake("d:mushroom", [P("mushroom_redGroup", 3f)]),
            MeshBaker.Bake("d:bush", [new Part(Root + "plant_bushSmall.glb", Xf(4f, Vector3.Zero, 0), Recolor.Nature, Leaf: true, Tint: Palette.LeafDark)]),
        ],
        Biome.Beach => [MeshBaker.Bake("d:beach-grass", [new Part(Root + "grass.glb", Xf(2.6f, Vector3.Zero, 0), Recolor.Nature, Leaf: true, Tint: Palette.Hex(0xB9C46A))])],
        Biome.Mountain => [MeshBaker.Bake("d:rock", [P("stone_smallC", 2.2f)])],
        _ => [],
    };

    private static Part P(string model, float scale) => new(Root + model + ".glb", Xf(scale, Vector3.Zero, 0), Recolor.Nature);

    private static Part P(string model, float scale, Vector3 offset, float yaw) => new(Root + model + ".glb", Xf(scale, offset, yaw), Recolor.Nature);

    private static Transform3D Xf(float scale, Vector3 offset, float yaw)
        => new(new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale, scale, scale)), offset * scale);
}
