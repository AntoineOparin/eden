using Eden.Game.Art;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game.Dev;

/// <summary>
/// <c>--lineup</c>: the spec 45 scale check. Every building kind (and the construction site) stands in a row on a flat
/// plane with an adult (1.75 m) and a child (1.35 m) in front of its door, plus one tree per biome, all at true scale.
/// A 1 m grid on the ground makes sizes readable in the screenshot (docs/art/shots/scale-lineup-30m.jpg).
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
            if (BuildingFactory.Mesh(kind) is { } mesh)
            {
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = 1 };
                mm.SetInstanceTransform(0, new Transform3D(Basis.Identity, Origin + at));
                mm.SetInstanceCustomData(0, Palette.SettlementColor(k).SrgbToLinear());
                AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = props });
            }
            float half = kind == BuildingFactory.ConstructionSite ? 3f : BuildingFactory.FootprintOf(kind);
            AddPerson(npc++, Sex.Male, LifeStage.Adult, Origin + at + new Vector3(-0.6f, 0, half + 0.8f), k);
            AddPerson(npc++, Sex.Female, LifeStage.Child, Origin + at + new Vector3(0.6f, 0, half + 0.8f), k);
            for (int m = -4; m <= 4; m++)
                AddChild(new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.01f, m % 5 == 0 ? 0.8f : 0.4f) },
                    Position = Origin + at + new Vector3(m, 0, half + 2.2f),
                    MaterialOverride = grid,
                });
        }
        // One tree per biome, each with an adult beside it.
        float x = -27;
        foreach (byte biome in NatureFactory.TreeBiomes)
        {
            var set = NatureFactory.TreeSet(biome);
            if (NatureFactory.Tree(set[0]) is { } tree)
            {
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = tree, InstanceCount = 1 };
                mm.SetInstanceTransform(0, new Transform3D(Basis.Identity, Origin + new Vector3(x, 0, 30)));
                mm.SetInstanceCustomData(0, Colors.White);
                AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = props });
            }
            AddPerson(npc++, Sex.Female, LifeStage.Adult, Origin + new Vector3(x + 2.2f, 0, 31), 1);
            x += 13;
        }
        foreach (var e in GltfLoader.Errors) GD.PrintErr($"[lineup] {e}");
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
