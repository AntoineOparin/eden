using Eden.Game.Art;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// The far-LOD impostor atlas: the two mid-LOD models (female row block, male row block) in 8 headings × 2 poses
/// (standing, lying), seen from the god camera's pitch through an orthographic camera in a transparent
/// <see cref="SubViewport"/> with its own world. Cells are <see cref="CellPixels"/> square; layout is 8 columns
/// (heading) × 4 rows (female stand, female lying, male stand, male lying).
/// <para>
/// A2: the GPU readback of that render was a start-up hitch, so the result is cached as a PNG under
/// <see cref="CacheDir"/>, keyed by a hash of everything that shapes it (<see cref="CacheKey"/>: the character files,
/// <see cref="CharacterProportions.Version"/>, the atlas shader and the layout numbers). With a cache hit the atlas is
/// loaded in <c>_Ready</c> (before the first frame) and nothing is rendered; on a miss it renders once, reads back
/// and writes the file on a worker thread for the next run. <see cref="Texture"/> is null until one of the two lands.
/// </para>
/// </summary>
public partial class ImpostorAtlas : Node
{
    public const int Columns = 8, Rows = 4;
    public const string CacheDir = "user://cache/";
    [Export] public int CellPixels { get; set; } = 96;
    [Export] public float PitchDegrees { get; set; } = 57f;
    /// <summary>World size of one cell (metres); the baked characters are about 1.75 m tall.</summary>
    [Export] public float CellMetres { get; set; } = 2.4f;
    /// <summary>Off: always render (tests of the render path).</summary>
    [Export] public bool UseCache { get; set; } = true;

    private SubViewport? _viewport;
    private int _framesWaited;

    public ImageTexture? Texture { get; private set; }
    public bool IsReady => Texture is not null;
    /// <summary>True when the texture came from the disk cache (diagnostics, tests).</summary>
    public bool FromCache { get; private set; }

    public static int Cell(Sex sex, bool lying, int heading8) => ((sex == Sex.Male ? 2 : 0) + (lying ? 1 : 0)) * Columns + heading8;

    /// <summary>Cache file for the current character set and layout.</summary>
    public string CachePath => $"{CacheDir}impostor-atlas-{CacheKey(CellPixels, PitchDegrees, CellMetres):x16}.png";

    /// <summary>
    /// FNV-1a over the inputs of the atlas: the mid models' file names and sizes, the proportion version, the atlas
    /// shader source and the layout. Any change gives a new file name (old files are left; they are ~100 KB).
    /// </summary>
    public static ulong CacheKey(int cellPixels, float pitch, float cellMetres)
    {
        ulong h = 1469598103934665603UL;
        void Mix(string s)
        {
            foreach (char c in s) h = (h ^ c) * 1099511628211UL;
            h = (h ^ 0xFF) * 1099511628211UL;
        }
        foreach (var sex in new[] { Sex.Female, Sex.Male })
        {
            var m = CharacterLibrary.MidModel(sex);
            Mix(m.Path);
            using var f = Godot.FileAccess.Open(m.Path, Godot.FileAccess.ModeFlags.Read);
            Mix(f is null ? "missing" : f.GetLength().ToString());
            Mix(m.SkinCell.ToString());
            Mix(m.MainCell.ToString());
        }
        Mix($"proportions {CharacterProportions.Version} {CharacterProportions.Legs} {CharacterProportions.Torso} {CharacterProportions.Arms} {CharacterProportions.Head} {CharacterProportions.TorsoWidth} {CharacterProportions.LegWidth} {CharacterProportions.ArmDrop}");
        Mix(Godot.FileAccess.GetFileAsString("res://shaders/npc_atlas.gdshader"));
        Mix($"{Columns}x{Rows} {cellPixels} {pitch} {cellMetres} adult {CharacterLibrary.AdultHeight}");
        return h;
    }

    public override void _Ready()
    {
        if (UseCache && TryLoadCache()) return;
        StartRender();
    }

    private bool TryLoadCache()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string path = CachePath;
        if (!Godot.FileAccess.FileExists(path)) return false;
        var image = Image.LoadFromFile(ProjectSettings.GlobalizePath(path));
        if (image is null || image.IsEmpty() || image.GetWidth() != Columns * CellPixels || image.GetHeight() != Rows * CellPixels) return false;
        if (!image.HasMipmaps()) image.GenerateMipmaps();
        Texture = ImageTexture.CreateFromImage(image);
        FromCache = true;
        Dev.FrameTimeProbe.Note("impostor atlas from cache", sw.Elapsed.TotalMilliseconds);
        return true;
    }

    private void StartRender()
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
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var image = _viewport.GetTexture().GetImage();
        if (image is null || image.IsEmpty()) return;
        var toSave = (Image)image.Duplicate();
        image.GenerateMipmaps();
        Texture = ImageTexture.CreateFromImage(image);
        _viewport.QueueFree();
        _viewport = null;
        Dev.FrameTimeProbe.Note("impostor atlas readback", sw.Elapsed.TotalMilliseconds);
        if (!UseCache) return;
        string path = ProjectSettings.GlobalizePath(CachePath);
        string dir = ProjectSettings.GlobalizePath(CacheDir);
        // PNG encoding takes ~10 ms; keep it off the main thread.
        System.Threading.Tasks.Task.Run(() =>
        {
            DirAccess.MakeDirRecursiveAbsolute(dir);
            var err = toSave.SavePng(path);
            if (err != Error.Ok) GD.PushWarning($"impostor atlas cache not written ({err}): {path}");
        });
    }
}
