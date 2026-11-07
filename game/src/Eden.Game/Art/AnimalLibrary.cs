using Godot;

namespace Eden.Game.Art;

/// <summary>Species drawn for game herds (A2). Picked per herd from the biome and the node's variant byte.</summary>
public enum Species { Deer, Boar, Sheep }

/// <summary>
/// Animal meshes for the game herds (<c>ResourceView</c> kind <c>game</c>): three Quaternius CC0 models (deer, pig
/// recoloured as a wild boar, sheep; game/assets/animals/SOURCE.md) posed once in their idle animation by CPU skinning
/// (<see cref="Skinning"/>), baked into one vertex-coloured surface in the palette (props shader, no skeleton), feet at
/// y = 0, facing +Z, scaled to a true size (1 u = 1 m): deer 1.2 m at the shoulder (1.7 m to the ears with the head
/// up), boar 0.85 m at the shoulder (0.95 m), sheep 0.75 m at the shoulder (1.05 m to the head).
/// </summary>
public static class AnimalLibrary
{
    public const string Root = "res://assets/animals/";

    public static readonly Species[] All = [Species.Deer, Species.Boar, Species.Sheep];

    private static readonly Dictionary<Species, ArrayMesh?> Meshes = new();

    public static string FileOf(Species s) => s switch
    {
        Species.Deer => Root + "deer.glb",
        Species.Boar => Root + "pig.glb",
        _ => Root + "sheep.glb",
    };

    /// <summary>Overall height (feet to the top of the head, idle pose) each species is scaled to, metres.</summary>
    public static float HeightOf(Species s) => s switch
    {
        Species.Deer => 1.7f,
        Species.Boar => 0.95f,
        _ => 1.05f,
    };

    /// <summary>Shoulder height used in the docs and the lineup (metres).</summary>
    public static float ShoulderOf(Species s) => s switch
    {
        Species.Deer => 1.2f,
        Species.Boar => 0.85f,
        _ => 0.75f,
    };

    /// <summary>Which species a herd shows: forests have deer and boar, grassland deer and sheep, highlands sheep.</summary>
    public static Species SpeciesFor(byte biome, byte variant) => biome switch
    {
        Biome.Forest => variant % 3 == 0 ? Species.Boar : Species.Deer,
        Biome.Mountain or Biome.Snow => Species.Sheep,
        _ => variant % 2 == 0 ? Species.Deer : Species.Sheep,
    };

    /// <summary>The baked mesh of a species, cached. Null when the model failed to load (see <see cref="GltfLoader.Errors"/>).</summary>
    public static ArrayMesh? Mesh(Species s)
    {
        if (Meshes.TryGetValue(s, out var cached)) return cached;
        var mesh = Bake(s);
        Meshes[s] = mesh;
        return mesh;
    }

    public static void ClearCache() => Meshes.Clear();

    private static ArrayMesh? Bake(Species s)
    {
        var root = GltfLoader.Template(FileOf(s));
        if (root is null) return null;
        Animation? idle = null;
        if (CharacterLibrary.Player(root) is { } player)
            foreach (var name in player.GetAnimationList())
                if (name.EndsWith("Idle", StringComparison.Ordinal)) { idle = player.GetAnimation(name); break; }
        var surfaces = Skinning.Pose(root, idle, 0f);
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var cols = new List<Color>();
        var idx = new List<int>();
        foreach (var surface in surfaces)
        {
            var mat = surface.Instance.GetSurfaceOverrideMaterial(surface.Index) ?? surface.Instance.Mesh!.SurfaceGetMaterial(surface.Index);
            var albedo = mat is BaseMaterial3D bm ? bm.AlbedoColor : Colors.White;
            string matName = mat?.ResourceName ?? "";
            var colour = Recolour(s, matName, albedo).SrgbToLinear();
            colour.A = 1f;
            var ii = surface.Arrays[(int)Godot.Mesh.ArrayType.Index].AsInt32Array();
            int baseIndex = verts.Count;
            for (int k = 0; k < surface.Positions.Length; k++)
            {
                verts.Add(surface.Positions[k]);
                norms.Add(surface.Normals[k]);
                cols.Add(colour);
            }
            if (ii.Length > 0) foreach (int i in ii) idx.Add(baseIndex + i);
            else for (int k = 0; k < surface.Positions.Length; k++) idx.Add(baseIndex + k);
        }
        if (verts.Count == 0) return null;
        // Feet at y = 0, centred over the origin, scaled to the species' true height.
        var min = verts[0];
        var max = verts[0];
        foreach (var v in verts) { min = min.Min(v); max = max.Max(v); }
        float scale = HeightOf(s) / MathF.Max(0.01f, max.Y - min.Y);
        var centre = new Vector3((min.X + max.X) * 0.5f, min.Y, (min.Z + max.Z) * 0.5f);
        for (int k = 0; k < verts.Count; k++) verts[k] = (verts[k] - centre) * scale;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Normal] = norms.ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Color] = cols.ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
        mesh.ResourceName = "a:" + s.ToString().ToLowerInvariant();
        return mesh;
    }

    /// <summary>Palette per species and material (the packs' own colours are dark PBR tones; sRGB here).</summary>
    public static Color Recolour(Species s, string material, Color original)
    {
        switch (s)
        {
            case Species.Deer:
                return material switch
                {
                    "Main" => Palette.Hex(0xA8683A),
                    "Main_Light" => Palette.Hex(0xE8D3AE),
                    "Main_Dark" => Palette.Hex(0x6E4326),
                    "Hooves" => Palette.Hex(0x3A2A20),
                    "Eye_Black" => Palette.Hex(0x1E1A18),
                    "Eye_White" => Palette.Hex(0xF2EFE8),
                    "Eye_Lighter" => Palette.Hex(0x8A5432),
                    _ => original.LinearToSrgb(),
                };
            case Species.Boar:
                // The pig's pink body becomes bristly grey-brown, its dark parts (snout, hooves) near black.
                return original.Luminance > 0.2f ? Palette.Hex(0x6E5A48) : Palette.Hex(0x3A2E26);
            default:
                return original.Luminance > 0.2f ? Palette.Hex(0xEFE8D8) : Palette.Hex(0x3D3734);
        }
    }
}
