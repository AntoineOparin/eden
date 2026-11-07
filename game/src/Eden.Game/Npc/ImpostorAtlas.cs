using Eden.Game.Art;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Renders the far-LOD impostor atlas once at startup: the two mid-LOD models (female row block, male row block) in
/// 8 headings × 2 poses (standing, lying), seen from the god camera's pitch through an orthographic camera in a
/// transparent <see cref="SubViewport"/> with its own world. Cells are <see cref="CellPixels"/> square; layout is
/// 8 columns (heading) × 4 rows (female stand, female lying, male stand, male lying). <see cref="Texture"/> is null
/// until the frame after the render completes.
/// </summary>
public partial class ImpostorAtlas : Node
{
    public const int Columns = 8, Rows = 4;
    [Export] public int CellPixels { get; set; } = 96;
    [Export] public float PitchDegrees { get; set; } = 57f;
    /// <summary>World size of one cell (metres); the baked characters are about 1.75 m tall.</summary>
    [Export] public float CellMetres { get; set; } = 2.4f;

    private SubViewport? _viewport;
    private int _framesWaited;

    public ImageTexture? Texture { get; private set; }
    public bool IsReady => Texture is not null;

    public static int Cell(Sex sex, bool lying, int heading8) => ((sex == Sex.Male ? 2 : 0) + (lying ? 1 : 0)) * Columns + heading8;

    public override void _Ready()
    {
        _viewport = new SubViewport
        {
            Size = new Vector2I(Columns * CellPixels, Rows * CellPixels),
            TransparentBg = true,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };
        AddChild(_viewport);

        float pitch = Mathf.DegToRad(PitchDegrees);
        var camBasis = new Basis(Vector3.Right, -pitch);
        var right = camBasis.X;
        var up = camBasis.Y;
        var forward = -camBasis.Z;
        var center = Vector3.Zero;
        var cam = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = Rows * CellMetres,
            Near = 0.1f,
            Far = 200f,
            Current = true,
        };
        cam.Transform = new Transform3D(camBasis, center - forward * 60f);
        _viewport.AddChild(cam);

        for (int row = 0; row < Rows; row++)
        {
            var sex = row < 2 ? Sex.Female : Sex.Male;
            bool lying = row % 2 == 1;
            var model = CharacterLibrary.MidModel(sex);
            var mesh = CharacterLibrary.Pose(model, lying ? BakedPose.Lying : BakedPose.Idle);
            if (mesh is null) continue;
            var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/npc_atlas.gdshader") };
            material.SetShaderParameter("colormap", CharacterLibrary.Colormap!);
            material.SetShaderParameter("skin_cell", model.SkinCell);
            material.SetShaderParameter("main_cell", model.MainCell);
            for (int col = 0; col < Columns; col++)
            {
                // Cell centre in the camera plane; the model's mid-height sits on it.
                float cx = (col + 0.5f - Columns * 0.5f) * CellMetres;
                float cy = (Rows * 0.5f - row - 0.5f) * CellMetres;
                var cellCentre = center + right * cx + up * cy;
                float midHeight = lying ? 0.25f : 0.85f;
                var basis = new Basis(Vector3.Up, col * MathF.Tau / Columns);
                var mi = new MeshInstance3D
                {
                    Mesh = mesh,
                    MaterialOverride = material,
                    Transform = new Transform3D(basis, cellCentre - new Vector3(0, midHeight, 0)),
                };
                _viewport.AddChild(mi);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (Texture is not null || _viewport is null) return;
        // Give the one-shot render two frames to land before reading it back.
        if (++_framesWaited < 3) return;
        var image = _viewport.GetTexture().GetImage();
        if (image is null || image.IsEmpty()) return;
        image.GenerateMipmaps();
        Texture = ImageTexture.CreateFromImage(image);
        _viewport.QueueFree();
        _viewport = null;
    }
}
