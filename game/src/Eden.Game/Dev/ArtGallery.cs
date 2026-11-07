using Eden.Game.Art;
using Godot;

namespace Eden.Game.Dev;

/// <summary>
/// <c>--gallery</c>: lays every baked building kind, the construction site, the tree sets, rocks, bushes and crop
/// stages out on a flat green plane next to the camera focus, so the art can be judged without a grown world.
/// Uses the main scene's lights and environment. Dev-only; not part of the game flow.
/// </summary>
public partial class ArtGallery : Node3D
{
    public Vector3 Origin { get; set; }

    public override void _Ready()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/props.gdshader") };
        material.SetShaderParameter("accent_replace", true);
        var ground = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(120, 60) },
            Position = Origin + new Vector3(0, -0.02f, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Palette.Grass, Roughness = 1f },
        };
        AddChild(ground);

        float x = -50;
        int settlement = 0;
        foreach (var kind in BuildingFactory.Kinds.Append(BuildingFactory.ConstructionSite))
        {
            Place(BuildingFactory.Mesh(kind), new Vector3(x, 0, -12), material, Palette.SettlementColor(settlement++).SrgbToLinear());
            x += 10;
        }
        x = -50;
        foreach (byte biome in NatureFactory.TreeBiomes)
        {
            foreach (var model in NatureFactory.TreeSet(biome))
            {
                Place(NatureFactory.Tree(model), new Vector3(x, 0, 4), material, Colors.White);
                x += 5;
            }
        }
        x = -50;
        for (int v = 0; v < NatureFactory.StoneVariants; v++, x += 5) Place(NatureFactory.Stone(v), new Vector3(x, 0, 14), material, Colors.White);
        Place(NatureFactory.Ore(), new Vector3(x, 0, 14), material, Colors.White);
        x += 5;
        Place(NatureFactory.Berry(), new Vector3(x, 0, 14), material, Colors.White);
        x += 5;
        Place(NatureFactory.BerryBare(), new Vector3(x, 0, 14), material, Colors.White);
        x += 5;
        Place(NatureFactory.Stump(), new Vector3(x, 0, 14), material, Colors.White);
        x += 7;
        for (int stage = 0; stage < 3; stage++, x += 6) Place(NatureFactory.Crop(stage), new Vector3(x, 0, 14), material, Colors.White);
        foreach (byte biome in NatureFactory.TreeBiomes)
            foreach (var d in NatureFactory.Decor(biome))
            {
                Place(d, new Vector3(x, 0, 14), material, Colors.White);
                x += 3;
            }
        // Characters: mid-LOD baked poses of both mid models, then every hi model in its rest pose.
        x = -15;
        foreach (var sex in new[] { Eden.Sim.Contracts.Sex.Female, Eden.Sim.Contracts.Sex.Male })
        {
            var model = CharacterLibrary.MidModel(sex);
            foreach (var pose in Enum.GetValues<BakedPose>())
            {
                if (CharacterLibrary.Pose(model, pose) is { } m)
                {
                    var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = m, InstanceCount = 1 };
                    mm.SetInstanceTransform(0, new Transform3D(Basis.Identity, Origin + new Vector3(x, 0, -3)));
                    mm.SetInstanceCustomData(0, CharacterLibrary.PaletteParams(3, (int)pose, 0));
                    AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = CharacterLibrary.MidMaterial(model) });
                }
                x += 2.5f;
            }
        }
        x = -15;
        foreach (var model in CharacterLibrary.Models)
        {
            if (CharacterLibrary.Instantiate(model) is { } node)
            {
                node.Position = Origin + new Vector3(x, 0, 1);
                AddChild(node);
            }
            x += 2.5f;
        }
        foreach (var kind in BuildingFactory.Kinds)
            if (BuildingFactory.Mesh(kind) is { } bm) GD.Print($"[gallery] {kind} footprint {BuildingFactory.FootprintOf(kind)} aabb {bm.GetAabb()}");
        foreach (var e in GltfLoader.Errors) GD.PrintErr($"[gallery] {e}");
    }

    private void Place(Mesh? mesh, Vector3 at, Material material, Color custom)
    {
        if (mesh is null) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = 1 };
        mm.SetInstanceTransform(0, new Transform3D(Basis.Identity, Origin + at));
        mm.SetInstanceCustomData(0, custom);
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material });
    }
}
