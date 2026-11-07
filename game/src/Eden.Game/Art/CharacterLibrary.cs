using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game.Art;

/// <summary>Poses baked into static meshes for the mid LOD (and rendered into the far-LOD impostor atlas).</summary>
public enum BakedPose { Idle, WalkA, WalkB, Work, Fight, Lying }

/// <summary>One Kenney Mini Character: its file, palette cells and lazily built materials and baked poses.</summary>
public sealed class CharacterModel(string file, Sex sex, Vector2 skinCell, Vector2 mainCell)
{
    public string Path { get; } = CharacterLibrary.Root + file;
    public Sex Sex { get; } = sex;
    /// <summary>Colormap cell (col, row) of the face skin.</summary>
    public Vector2 SkinCell { get; } = skinCell;
    /// <summary>Colormap cell (col, row) of the largest garment, repainted in the settlement colour.</summary>
    public Vector2 MainCell { get; } = mainCell;

    internal ShaderMaterial? Hi, Mid;
    /// <summary>Uniform scale that makes this model's idle pose <see cref="CharacterLibrary.AdultHeight"/> tall (0 until known).</summary>
    internal float Scale;
    internal readonly Dictionary<BakedPose, ArrayMesh> Poses = new();
}

/// <summary>
/// The NPC art: twelve rigged Kenney Mini Characters (six per sex) loaded at runtime, their palette-recolour materials
/// (shaders/npc_hi.gdshader, shaders/npc_mid.gdshader), the animation names the hi-LOD AnimationTree uses, and
/// static pose meshes for the mid LOD baked by CPU skinning (no scene tree needed, so tests can use them).
/// Model and colour choices are a deterministic hash of the NPC id and settlement; the sim is not involved.
/// </summary>
public static class CharacterLibrary
{
    public const string Root = "res://assets/characters/";
    public const string ColormapPath = Root + "Textures/colormap.png";

    /// <summary>Adult height in metres at hi LOD (spec 45, "Scale"). Each model is scaled so its idle pose is this tall.</summary>
    public const float AdultHeight = 1.75f;

    /// <summary>Kenney mini characters are about 0.67 units tall; used only if a model's idle pose cannot be measured.</summary>
    public const float FallbackScale = 2.6f;

    public const string AnimIdle = "idle", AnimWalk = "walk", AnimRun = "sprint", AnimWork = "interact-right",
        AnimFight = "attack-melee-right", AnimDie = "die";

    /// <summary>Animations every character must have (asserted by tests).</summary>
    public static readonly string[] RequiredAnimations = [AnimIdle, AnimWalk, AnimRun, AnimWork, AnimFight, AnimDie];

    private static readonly string[] LoopAnimations = [AnimIdle, AnimWalk, AnimRun, AnimWork, AnimFight];

    // Cells measured from the GLBs' UVs (face cell with 220 vertices; largest garment cell of the body mesh).
    public static readonly CharacterModel[] Models =
    [
        new("character-female-a.glb", Sex.Female, new(6, 3), new(5, 2)),
        new("character-female-b.glb", Sex.Female, new(7, 3), new(7, 2)),
        new("character-female-c.glb", Sex.Female, new(5, 3), new(5, 2)),
        new("character-female-d.glb", Sex.Female, new(7, 3), new(1, 3)),
        new("character-female-e.glb", Sex.Female, new(7, 3), new(5, 2)),
        new("character-female-f.glb", Sex.Female, new(7, 3), new(5, 2)),
        new("character-male-a.glb", Sex.Male, new(6, 3), new(1, 2)),
        new("character-male-b.glb", Sex.Male, new(7, 3), new(4, 2)),
        new("character-male-c.glb", Sex.Male, new(7, 3), new(5, 2)),
        new("character-male-d.glb", Sex.Male, new(7, 3), new(0, 3)),
        new("character-male-e.glb", Sex.Male, new(7, 3), new(2, 2)),
        new("character-male-f.glb", Sex.Male, new(5, 3), new(1, 2)),
    ];

    /// <summary>The two models used for the mid LOD and the impostors (one per sex) to keep MultiMesh count low.</summary>
    public static CharacterModel MidModel(Sex sex) => sex == Sex.Female ? Models[1] : Models[6]; // female-a carries a prop that widens her silhouette

    /// <summary>Settlement colour as a colormap cell (col, row): the character palette's saturated cells.</summary>
    private static readonly Vector2[] SettlementCells =
    [
        new(4, 2), new(5, 2), new(2, 2), new(7, 2), new(3, 2), new(6, 2), new(0, 1), new(1, 2),
    ];

    private static ImageTexture? _colormap;

    public static CharacterModel ModelFor(int npcId, Sex sex)
    {
        uint h = Mix((uint)npcId);
        int k = (int)(h % 6u);
        return Models[(sex == Sex.Female ? 0 : 6) + k];
    }

    /// <summary>Palette params for npc_palette.gdshaderinc: (settlement cell col, row, skin choice, state).</summary>
    public static Color PaletteParams(int npcId, int settlementId, float state)
    {
        var cell = settlementId < 0 ? new Vector2(4, 3) : SettlementCells[(int)((uint)settlementId % SettlementCells.Length)];
        float skin = Mix((uint)npcId * 7919u + 13u) % 4u;
        return new Color(cell.X, cell.Y, skin, state);
    }

    public static ImageTexture? Colormap => _colormap ??= GltfLoader.LoadTexture(ColormapPath, mipmaps: false);

    public static ShaderMaterial HiMaterial(CharacterModel m) => m.Hi ??= MakeMaterial("res://shaders/npc_hi.gdshader", m);

    public static ShaderMaterial MidMaterial(CharacterModel m) => m.Mid ??= MakeMaterial("res://shaders/npc_mid.gdshader", m);

    private static ShaderMaterial MakeMaterial(string shader, CharacterModel m)
    {
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>(shader) };
        mat.SetShaderParameter("colormap", Colormap!);
        mat.SetShaderParameter("skin_cell", m.SkinCell);
        mat.SetShaderParameter("main_cell", m.MainCell);
        return mat;
    }

    /// <summary>Scale that makes the model <see cref="AdultHeight"/> tall in its idle pose (measured once by CPU skinning).</summary>
    public static float ScaleOf(CharacterModel m)
    {
        if (m.Scale > 0) return m.Scale;
        var root = Template(m);
        if (root is not null && Player(root) is { } player && player.HasAnimation(AnimIdle) && Skin(root, player.GetAnimation(AnimIdle), 0.3f, 1f) is { } unit)
            m.Scale = AdultHeight / MathF.Max(0.1f, unit.GetAabb().Size.Y);
        else m.Scale = FallbackScale;
        return m.Scale;
    }

    /// <summary>A new skinned instance of the model (root scaled to <see cref="AdultHeight"/>, palette material applied).</summary>
    public static Node3D? Instantiate(CharacterModel m)
    {
        var template = Template(m);
        if (template is null) return null;
        PrepareAnimations(template);
        if (template.Duplicate() is not Node3D node) return null;
        node.Scale = Vector3.One * ScaleOf(m);
        var mat = HiMaterial(m);
        foreach (var mi in MeshInstances(node)) mi.MaterialOverride = mat;
        return node;
    }

    /// <summary>The model's AnimationPlayer (glTF import puts one under the root).</summary>
    public static AnimationPlayer? Player(Node root)
    {
        foreach (var child in root.GetChildren())
            if (child is AnimationPlayer p) return p;
        return null;
    }

    /// <summary>Names of the animations in a model, for tests.</summary>
    public static IReadOnlyList<string> AnimationNames(CharacterModel m)
    {
        var t = Template(m);
        if (t is null || Player(t) is not { } p) return [];
        return p.GetAnimationList();
    }

    private static void PrepareAnimations(Node template)
    {
        if (Player(template) is not { } p) return;
        foreach (var name in LoopAnimations)
            if (p.HasAnimation(name)) p.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
    }

    public static IEnumerable<MeshInstance3D> MeshInstances(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is MeshInstance3D mi) yield return mi;
            foreach (var deeper in MeshInstances(child)) yield return deeper;
        }
    }

    /// <summary>
    /// A static mesh of the model in a pose (body and head merged into one surface, UVs kept for the palette shader),
    /// scaled by <see cref="ScaleOf"/> (adult 1.75 m standing), feet at the origin. CPU-skinned from the animation's bone tracks.
    /// </summary>
    public static ArrayMesh? Pose(CharacterModel m, BakedPose pose)
    {
        if (m.Poses.TryGetValue(pose, out var cached)) return cached;
        var root = Template(m);
        if (root is null || Player(root) is not { } player) return null;
        var (anim, time) = pose switch
        {
            BakedPose.WalkA => (AnimWalk, 0.12f),
            BakedPose.WalkB => (AnimWalk, 0.45f),
            BakedPose.Work => (AnimWork, 0.3f),
            BakedPose.Fight => (AnimFight, 0.2f),
            BakedPose.Lying => (AnimDie, 10f),
            _ => (AnimIdle, 0.3f),
        };
        if (!player.HasAnimation(anim)) return null;
        var mesh = Skin(root, player.GetAnimation(anim), time, ScaleOf(m));
        if (mesh is not null) m.Poses[pose] = mesh;
        return mesh;
    }

    /// <summary>
    /// The model's glTF template, re-proportioned once by <see cref="CharacterProportions"/> (A2, "less chibi") before
    /// anything instances, measures or bakes it.
    /// </summary>
    public static Node3D? Template(CharacterModel m)
    {
        var root = GltfLoader.Template(m.Path);
        if (root is not null && !CharacterProportions.Apply(root)) GD.PrintErr($"[art] could not re-proportion {m.Path}");
        return root;
    }

    private static ArrayMesh? Skin(Node3D root, Animation anim, float time, float scale)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var idx = new List<int>();
        foreach (var surface in Skinning.Pose(root, anim, time))
        {
            if (surface.Instance.Skin is null) continue;
            var uv = surface.Arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
            var ii = surface.Arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            int baseIndex = verts.Count;
            for (int k = 0; k < surface.Positions.Length; k++)
            {
                verts.Add(surface.Positions[k] * scale);
                norms.Add(surface.Normals[k]);
                uvs.Add(uv.Length > k ? uv[k] : Vector2.Zero);
            }
            foreach (int i in ii) idx.Add(baseIndex + i);
        }
        if (verts.Count == 0) return null;
        // Feet on the ground: shift so the lowest vertex is at y = 0 (the die pose ends lying down).
        float minY = float.MaxValue;
        foreach (var p in verts) minY = Math.Min(minY, p.Y);
        for (int k = 0; k < verts.Count; k++) verts[k] -= new Vector3(0, minY, 0);
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static uint Mix(uint h)
    {
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }
}
