using Eden.Game.Art;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game.Dev;

/// <summary>
/// <c>--lineup</c>: the spec 45 scale check. Every building kind (and the construction site) stands in a row on a flat
/// plane with an adult (1.75 m) and a child (1.35 m) in front of its door, plus one tree per biome, all at true scale.
/// A2 adds the three herd animals, a record stela and a short plank bridge to the last row. A 1 m grid on the ground
/// makes sizes readable in the screenshot (docs/art/shots/scale-lineup-30m.jpg). <c>--lineup-close</c> adds a camera
/// 7 m from the first pair of people, for judging proportions (docs/art/shots/a2-characters-close.jpg).
/// </summary>
public partial class ScaleLineup : Node3D
{
    public Vector3 Origin { get; set; }

    public override void _Ready()
    {
        var props = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/props.gdshader") };
        props.SetShaderParameter("accent_replace", true);
        AddChild(new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(120, 90) },
            Position = Origin + new Vector3(0, -0.01f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Palette.Grass, Roughness = 1f },
        });
        // 1 m ticks (5 m longer) along the front edge of each row.
        var grid = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.4f, 0.18f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };

        // Buildings in rows of four, each with an adult and a child in front.
        var kinds = BuildingFactory.Kinds.Append(BuildingFactory.ConstructionSite).ToArray();
        int npc = 0;
        for (int k = 0; k < kinds.Length; k++)
        {
            string kind = kinds[k];
            int row = k / 4, col = k % 4;
            var at = new Vector3(-27f + col * 18f, 0, -16f + row * 15f);
            if (BuildingFactory.Mesh(kind) is { } mesh) Place(mesh, Origin + at, props, Palette.SettlementColor(k).SrgbToLinear());
            float half = kind == BuildingFactory.ConstructionSite ? 3f : BuildingFactory.FootprintOf(kind);
            AddPerson(npc++, Sex.Male, LifeStage.Adult, Origin + at + new Vector3(-0.6f, 0, half + 0.8f), k);
            AddPerson(npc++, Sex.Female, LifeStage.Child, Origin + at + new Vector3(0.6f, 0, half + 0.8f), k);
            Ticks(Origin + at + new Vector3(0, 0, half + 2.2f), grid);
        }
        // A2 extras in the free slots of the last row: herd animals with a person, a record stela, a plank bridge.
        var extras = Origin + new Vector3(-27f + 3 * 18f - 6f, 0, -16f + 2 * 15f);
        float ax = 0;
        foreach (var species in AnimalLibrary.All)
        {
            if (AnimalLibrary.Mesh(species) is { } animal) Place(animal, extras + new Vector3(ax, 0, 0), props, Colors.White, new Basis(Vector3.Up, -0.9f));
            ax += species == Species.Deer ? 2.6f : 2.1f;
        }
        AddPerson(npc++, Sex.Female, LifeStage.Adult, extras + new Vector3(ax, 0, 0.2f), 2);
        Place(ProceduralMeshes.Stela(), extras + new Vector3(ax + 1.6f, 0, 0), props, Colors.White);
        Ticks(extras + new Vector3(3f, 0, 2f), grid);
        var bridgeAt = extras + new Vector3(10f, 0, -4f);
        for (int s = 0; s < 6; s++) Place(ProceduralMeshes.BridgeSegment(), bridgeAt + new Vector3(0, 0.12f, s), props, Colors.White);

        // One tree per biome, each with an adult beside it.
        float x = -27;
        foreach (byte biome in NatureFactory.TreeBiomes)
        {
            var set = NatureFactory.TreeSet(biome);
            if (NatureFactory.Tree(set[0]) is { } tree) Place(tree, Origin + new Vector3(x, 0, 30), props, Colors.White);
            AddPerson(npc++, Sex.Female, LifeStage.Adult, Origin + new Vector3(x + 2.2f, 0, 31), 1);
            x += 13;
        }
        foreach (var e in GltfLoader.Errors) GD.PrintErr($"[lineup] {e}");

        if (OS.GetCmdlineUserArgs().Contains("--lineup-close"))
        {
            // The first pair (hut door) seen from 7 m, slightly above eye height.
            var target = Origin + new Vector3(-27f, 0.9f, -16f + BuildingFactory.FootprintOf("hut") + 0.8f);
            // The two mid-LOD baked poses (what the 700 nearest after the hi slots look like) beside the pair.
            float mx = -2.2f;
            foreach (var sex in new[] { Sex.Male, Sex.Female })
            {
                var mid = CharacterLibrary.MidModel(sex);
                if (CharacterLibrary.Pose(mid, BakedPose.Idle) is { } pose)
                    Place(pose, target + new Vector3(mx, -0.9f, 0), CharacterLibrary.MidMaterial(mid), CharacterLibrary.PaletteParams(7, 3, 0));
                mx -= 1.1f;
            }
            var cam = new Camera3D { Fov = 40f, Current = true };
            AddChild(cam);
            cam.GlobalPosition = target + new Vector3(2.2f, 1.6f, 6.6f);
            cam.LookAt(target, Vector3.Up);
        }
    }

    private void Place(Mesh mesh, Vector3 at, Material material, Color custom, Basis? basis = null)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = 1 };
        mm.SetInstanceTransform(0, new Transform3D(basis ?? Basis.Identity, at));
        mm.SetInstanceCustomData(0, custom);
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material });
    }

    private void Ticks(Vector3 at, Material grid)
    {
        for (int m = -4; m <= 4; m++)
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.01f, m % 5 == 0 ? 0.8f : 0.4f) },
                Position = at + new Vector3(m, 0, 0),
                MaterialOverride = grid,
            });
    }

    private void AddPerson(int id, Sex sex, LifeStage stage, Vector3 at, int settlement)
    {
        var model = CharacterLibrary.ModelFor(id, sex);
        if (CharacterLibrary.Instantiate(model) is not { } node) return;
        node.Position = at;
        node.Scale *= NpcRenderer.StageScale(stage);
        AddChild(node);
        foreach (var mi in CharacterLibrary.MeshInstances(node))
            mi.SetInstanceShaderParameter("npc_params", CharacterLibrary.PaletteParams(id, settlement, 0));
        if (CharacterLibrary.Player(node) is { } player) player.Play(CharacterLibrary.AnimIdle);
    }
}
