using Godot;

namespace Eden.Game.Art;

/// <summary>
/// Low-poly meshes built in code where no CC0 pack has the piece (spec 45, "Procedural assets"). Flat shaded
/// (per-face normals), palette colours in the vertex colour (linear, alpha = role mask as in
/// <see cref="MeshBaker"/>), so they draw with shaders/props.gdshader next to the Kenney meshes.
/// Documented in game/assets/generated/README.md.
/// </summary>
public static class ProceduralMeshes
{
    private static ArrayMesh? _plinth, _earth, _boat;

    /// <summary>
    /// Stone terrace under a building: a unit footprint (x, z in -0.5..0.5) from y = -1 up to 0, so an instance scaled
    /// (width, height, depth) and placed at the foundation top reaches exactly down to top - height. Slightly
    /// battered (wider at the base), a lighter coping course on top, packed-earth top face.
    /// </summary>
    public static ArrayMesh Plinth(bool earth = false)
    {
        if (earth ? _earth is not null : _plinth is not null) return earth ? _earth! : _plinth!;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // Stone terraces under buildings; packed-earth banks with a grass lip under fields.
        var wall = earth ? Palette.Dirt : Palette.Stone;
        var coping = earth ? Palette.GrassDark : Palette.Stone.Lightened(0.12f);
        var top = earth ? Palette.Hex(0xA4774A) : Palette.Hex(0xB89B72);
        const float batter = 0.04f, copeDepth = 0.12f;
        // Corners: top ring (y = 0), coping ring (y = -copeDepth, same size), base ring (y = -1, battered out).
        Vector3 T(float x, float z) => new(x * 0.5f, 0, z * 0.5f);
        Vector3 C(float x, float z) => new(x * 0.5f, -copeDepth, z * 0.5f);
        Vector3 B(float x, float z) => new(x * (0.5f + batter), -1f, z * (0.5f + batter));
        var corners = new (float X, float Z)[] { (-1, -1), (1, -1), (1, 1), (-1, 1) };
        for (int i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            Quad(st, T(a.X, a.Z), T(b.X, b.Z), C(b.X, b.Z), C(a.X, a.Z), coping);
            Quad(st, C(a.X, a.Z), C(b.X, b.Z), B(b.X, b.Z), B(a.X, a.Z), wall);
        }
        Quad(st, T(-1, 1), T(1, 1), T(1, -1), T(-1, -1), top);
        st.GenerateNormals();
        var mesh = st.Commit();
        mesh.ResourceName = earth ? "g:earth-plinth" : "g:plinth";
        if (earth) _earth = mesh;
        else _plinth = mesh;
        return mesh;
    }

    /// <summary>A small rowing boat (hull + thwart), for docks. About 3 m long.</summary>
    public static ArrayMesh Boat()
    {
        if (_boat is not null) return _boat;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var hull = Palette.Wood;
        var inner = Palette.Hex(0xA8743F);
        // Hull: a pointed bow (+Z) and a flat stern (-Z), 8 rim points over a keel line.
        var rim = new[]
        {
            new Vector3(0, 0.45f, 1.5f), new Vector3(0.55f, 0.42f, 0.6f), new Vector3(0.6f, 0.4f, -0.6f), new Vector3(0.45f, 0.42f, -1.4f),
            new Vector3(-0.45f, 0.42f, -1.4f), new Vector3(-0.6f, 0.4f, -0.6f), new Vector3(-0.55f, 0.42f, 0.6f),
        };
        var keel = new[] { new Vector3(0, 0.05f, 1.2f), new Vector3(0, 0f, 0.4f), new Vector3(0, 0f, -0.7f), new Vector3(0, 0.08f, -1.35f) };
        int[] keelOf = [0, 1, 2, 3, 3, 2, 1];
        for (int i = 0; i < rim.Length; i++)
        {
            int j = (i + 1) % rim.Length;
            var ki = keel[keelOf[i]];
            var kj = keel[keelOf[j]];
            if (ki == kj) Tri(st, rim[i], rim[j], ki, hull);
            else Quad(st, rim[i], rim[j], kj, ki, hull);
        }
        // Deck boards seen from above (inside of the hull), slightly below the rim.
        for (int i = 1; i < rim.Length - 1; i++)
            Tri(st, rim[0] - new Vector3(0, 0.08f, 0), rim[i + 1] - new Vector3(0, 0.08f, 0), rim[i] - new Vector3(0, 0.08f, 0), inner);
        st.GenerateNormals();
        _boat = st.Commit();
        _boat.ResourceName = "g:boat";
        return _boat;
    }

    private static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        Tri(st, a, b, c, color);
        Tri(st, a, c, d, color);
    }

    private static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        var lin = color.SrgbToLinear();
        lin.A = 1f;
        st.SetSmoothGroup(uint.MaxValue); // flat: every face its own normal
        // Godot front faces are clockwise as seen from outside; the shapes above are listed counter-clockwise.
        foreach (var v in new[] { a, c, b })
        {
            st.SetColor(lin);
            st.AddVertex(v);
        }
    }
}
