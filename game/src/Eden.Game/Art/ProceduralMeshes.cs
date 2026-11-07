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

    // ---- A2: clouds, record stela, shrine altar, plank bridge ----------------------------------------------------

    private static readonly Dictionary<string, ArrayMesh> A2Cache = new();

    /// <summary>A procedural piece by name, for <see cref="MeshBaker"/> parts ("proc:altar", "proc:stela"). Null for unknown names.</summary>
    public static ArrayMesh? Named(string name) => name switch
    {
        "altar" => Altar(),
        "stela" => Stela(),
        "plinth" => Plinth(),
        "boat" => Boat(),
        _ => null,
    };

    /// <summary>Number of distinct cloud shapes <see cref="Cloud"/> builds.</summary>
    public const int CloudVariants = 5;

    /// <summary>
    /// A cartoon cumulus: 3–5 flat-shaded low-poly spheres (icosahedron, one subdivision) merged into one surface, the
    /// bottom flattened, about 1 unit across (instances scale it to 18–40 m). White on top shading to a cool grey
    /// underside through the vertex colour; alpha 1. Deterministic per <paramref name="variant"/>.
    /// </summary>
    public static ArrayMesh Cloud(int variant)
    {
        variant = (int)((uint)variant % CloudVariants);
        string key = "g:cloud" + variant;
        if (A2Cache.TryGetValue(key, out var hit)) return hit;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        uint h = (uint)(variant * 7919 + 17);
        float Next() { h = h * 1664525u + 1013904223u; return (h >> 8) / 16777216f; }
        int puffs = 3 + variant % 3;
        var top = Palette.Hex(0xFFFFFF);
        var bottom = Palette.Hex(0xC9D3E0);
        for (int p = 0; p < puffs; p++)
        {
            float t = puffs == 1 ? 0.5f : p / (float)(puffs - 1);
            float r = 0.22f + 0.12f * Next() + (p == puffs / 2 ? 0.08f : 0f);
            var c = new Vector3((t - 0.5f) * 0.62f, r * 0.25f + 0.05f * Next(), (Next() - 0.5f) * 0.22f);
            foreach (var (a, b, d) in IcoFaces())
            {
                Vector3 V(Vector3 u)
                {
                    var q = c + u * r;
                    q.Y = MathF.Max(q.Y, 0f); // flat base
                    return q;
                }
                var va = V(a); var vb = V(b); var vd = V(d);
                float up = ((a + b + d) / 3f).Normalized().Y;
                var col = bottom.Lerp(top, Math.Clamp(up * 0.8f + 0.55f, 0f, 1f));
                Face(st, va, vb, vd, ((a + b + d) / 3f), col);
            }
        }
        st.GenerateNormals();
        var mesh = st.Commit();
        mesh.ResourceName = key;
        A2Cache[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// Record stela: a carved standing stone 1.6 m tall above the ground (0.7 m wide, 0.24 m thick, tapering, with a
    /// pointed cap), a footing that reaches 0.35 m below y = 0 so a tilt never shows a gap, and a glyph on the front
    /// (+Z) face built as thin raised dark inlays: a sun disc over three lines of "writing". The decal is geometry, so it
    /// costs nothing extra and draws in the same batch.
    /// </summary>
    public static ArrayMesh Stela()
    {
        const string key = "g:stela";
        if (A2Cache.TryGetValue(key, out var hit)) return hit;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var stone = Palette.Hex(0xB9B4A6);
        var dark = Palette.Hex(0x5E5A52);
        float w0 = 0.35f, w1 = 0.29f, d0 = 0.12f, d1 = 0.10f, hBody = 1.42f, hTop = 1.6f;
        // Footing (slightly wider) from -0.35 to 0.05.
        Box(st, new Vector3(-0.42f, -0.35f, -0.17f), new Vector3(0.42f, 0.06f, 0.17f), Palette.Hex(0x9E978A));
        // Tapered body: bottom ring at y = 0.05, top ring at hBody.
        var b0 = new[] { new Vector3(-w0, 0.05f, -d0), new Vector3(w0, 0.05f, -d0), new Vector3(w0, 0.05f, d0), new Vector3(-w0, 0.05f, d0) };
        var b1 = new[] { new Vector3(-w1, hBody, -d1), new Vector3(w1, hBody, -d1), new Vector3(w1, hBody, d1), new Vector3(-w1, hBody, d1) };
        var centre = new Vector3(0, hBody * 0.5f, 0);
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            var mid = (b0[i] + b0[j] + b1[i] + b1[j]) * 0.25f;
            FaceQ(st, b0[i], b0[j], b1[j], b1[i], mid - centre, stone);
        }
        // Pointed cap: ridge along X.
        var ridgeA = new Vector3(-w1 * 0.55f, hTop, 0);
        var ridgeB = new Vector3(w1 * 0.55f, hTop, 0);
        FaceQ(st, b1[3], b1[2], ridgeB, ridgeA, new Vector3(0, 0.5f, 1), stone.Lightened(0.06f));
        FaceQ(st, b1[1], b1[0], ridgeA, ridgeB, new Vector3(0, 0.5f, -1), stone.Lightened(0.06f));
        Face(st, b1[0], b1[3], ridgeA, new Vector3(-1, 0.3f, 0), stone);
        Face(st, b1[2], b1[1], ridgeB, new Vector3(1, 0.3f, 0), stone);
        // Glyph inlays on the front face (z ≈ front plane + 0.012).
        float FrontZ(float y) => d0 + (d1 - d0) * ((y - 0.05f) / (hBody - 0.05f)) + 0.012f;
        void Inlay(float x0, float x1, float y0, float y1)
        {
            float za = FrontZ(y0), zb = FrontZ(y1);
            FaceQ(st, new Vector3(x0, y0, za), new Vector3(x1, y0, za), new Vector3(x1, y1, zb), new Vector3(x0, y1, zb), Vector3.Back, dark);
        }
        // Sun disc as an octagon ring of short bars, centre dot.
        const float cy = 1.12f, rr = 0.11f;
        for (int k = 0; k < 8; k++)
        {
            float a = k * MathF.Tau / 8f;
            float x = MathF.Cos(a) * rr, y = cy + MathF.Sin(a) * rr;
            Inlay(x - 0.022f, x + 0.022f, y - 0.022f, y + 0.022f);
        }
        Inlay(-0.03f, 0.03f, cy - 0.03f, cy + 0.03f);
        // Three lines of marks.
        for (int line = 0; line < 3; line++)
        {
            float y = 0.82f - line * 0.16f;
            float x = -0.2f;
            uint g = (uint)(line * 31 + 7);
            while (x < 0.2f)
            {
                g = g * 1103515245u + 12345u;
                float len = 0.03f + ((g >> 16) & 7) * 0.012f;
                Inlay(x, MathF.Min(0.21f, x + len), y, y + 0.035f);
                x += len + 0.03f;
            }
        }
        st.GenerateNormals();
        var mesh = st.Commit();
        mesh.ResourceName = key;
        A2Cache[key] = mesh;
        return mesh;
    }

    /// <summary>
    /// Shrine altar: a low stone table (1.5 × 0.8 m, top at 0.78 m) on two block legs and a footing that reaches 0.2 m
    /// below y = 0, with offerings on top: a clay bowl of red fruit, a loaf, two candles and a yellow flower.
    /// </summary>
    public static ArrayMesh Altar()
    {
        const string key = "g:altar";
        if (A2Cache.TryGetValue(key, out var hit)) return hit;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var stone = Palette.Hex(0xC9C1AE);
        var stoneDark = Palette.Hex(0xA49C8B);
        Box(st, new Vector3(-0.85f, -0.2f, -0.5f), new Vector3(0.85f, 0.08f, 0.5f), stoneDark); // footing
        Box(st, new Vector3(-0.62f, 0.08f, -0.3f), new Vector3(-0.32f, 0.66f, 0.3f), stone);     // legs
        Box(st, new Vector3(0.32f, 0.08f, -0.3f), new Vector3(0.62f, 0.66f, 0.3f), stone);
        Box(st, new Vector3(-0.75f, 0.66f, -0.4f), new Vector3(0.75f, 0.78f, 0.4f), stone.Lightened(0.08f)); // top slab
        // Bowl: an octagonal frustum in terracotta, fruit as small red boxes.
        var clay = Palette.Hex(0xB8643E);
        var bowlC = new Vector3(-0.32f, 0.78f, 0f);
        for (int k = 0; k < 8; k++)
        {
            float a0 = k * MathF.Tau / 8f, a1 = (k + 1) * MathF.Tau / 8f;
            var p0 = bowlC + new Vector3(MathF.Cos(a0) * 0.1f, 0, MathF.Sin(a0) * 0.1f);
            var p1 = bowlC + new Vector3(MathF.Cos(a1) * 0.1f, 0, MathF.Sin(a1) * 0.1f);
            var q0 = bowlC + new Vector3(MathF.Cos(a0) * 0.17f, 0.1f, MathF.Sin(a0) * 0.17f);
            var q1 = bowlC + new Vector3(MathF.Cos(a1) * 0.17f, 0.1f, MathF.Sin(a1) * 0.17f);
            FaceQ(st, p0, p1, q1, q0, new Vector3(MathF.Cos((a0 + a1) / 2), 0, MathF.Sin((a0 + a1) / 2)), clay);
        }
        var fruit = Palette.Berry;
        Box(st, bowlC + new Vector3(-0.08f, 0.06f, -0.06f), bowlC + new Vector3(0.0f, 0.15f, 0.03f), fruit);
        Box(st, bowlC + new Vector3(0.01f, 0.06f, -0.02f), bowlC + new Vector3(0.09f, 0.14f, 0.07f), Palette.Hex(0xE08A2E));
        Box(st, bowlC + new Vector3(-0.04f, 0.06f, 0.03f), bowlC + new Vector3(0.04f, 0.16f, 0.1f), fruit.Darkened(0.1f));
        // Loaf, candles, flower.
        Box(st, new Vector3(0.05f, 0.78f, -0.12f), new Vector3(0.32f, 0.88f, 0.08f), Palette.Hex(0xC08A4A));
        Box(st, new Vector3(0.45f, 0.78f, -0.25f), new Vector3(0.5f, 0.98f, -0.2f), Palette.Hex(0xF2E8CF));
        Box(st, new Vector3(0.45f, 0.78f, 0.2f), new Vector3(0.5f, 0.95f, 0.25f), Palette.Hex(0xF2E8CF));
        Box(st, new Vector3(0.462f, 0.98f, -0.238f), new Vector3(0.488f, 1.02f, -0.212f), Palette.Hex(0xFFC34A));
        Box(st, new Vector3(0.462f, 0.95f, 0.212f), new Vector3(0.488f, 0.99f, 0.238f), Palette.Hex(0xFFC34A));
        Box(st, new Vector3(-0.62f, 0.78f, 0.18f), new Vector3(-0.54f, 0.84f, 0.26f), Palette.Wheat);
        st.GenerateNormals();
        var mesh = st.Commit();
        mesh.ResourceName = key;
        A2Cache[key] = mesh;
        return mesh;
    }

    /// <summary>Deck width of <see cref="BridgeSegment"/> (metres).</summary>
    public const float BridgeWidth = 2.4f;

    /// <summary>
    /// One metre of plank bridge along +Z (z 0..1): three planks across a 2.4 m deck (0.12 m thick, top at y = 0), two
    /// stringers under it and a hand rail on each side (a post at z = 0 and a rail at 0.9 m). Instances are scaled along
    /// Z to the segment length and pitched to follow the deck's arch.
    /// </summary>
    public static ArrayMesh BridgeSegment()
    {
        const string key = "g:bridge-segment";
        if (A2Cache.TryGetValue(key, out var hit)) return hit;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var plank = Palette.Wood;
        var plankLight = Palette.Hex(0xA06C3E);
        var beam = Palette.BarkDark;
        float half = BridgeWidth * 0.5f;
        // Planks run across the deck; three per metre with thin gaps.
        for (int i = 0; i < 3; i++)
        {
            float z0 = i / 3f + 0.01f, z1 = (i + 1) / 3f - 0.01f;
            Box(st, new Vector3(-half, -0.12f, z0), new Vector3(half, 0f, z1), i == 1 ? plankLight : plank);
        }
        // Stringers.
        Box(st, new Vector3(-half + 0.15f, -0.3f, 0f), new Vector3(-half + 0.35f, -0.12f, 1f), beam);
        Box(st, new Vector3(half - 0.35f, -0.3f, 0f), new Vector3(half - 0.15f, -0.12f, 1f), beam);
        // Rails: a post at z = 0 on each side and a rail beam along the segment.
        foreach (float sx in new[] { -1f, 1f })
        {
            float x = sx * (half - 0.08f);
            Box(st, new Vector3(x - 0.06f, 0f, 0f), new Vector3(x + 0.06f, 0.95f, 0.12f), beam);
            Box(st, new Vector3(x - 0.05f, 0.82f, 0f), new Vector3(x + 0.05f, 0.92f, 1f), plankLight);
        }
        st.GenerateNormals();
        var mesh = st.Commit();
        mesh.ResourceName = key;
        A2Cache[key] = mesh;
        return mesh;
    }

    /// <summary>A square timber pile from y = 0 down to y = -1 (0.22 m square); instances scale Y to reach the ground.</summary>
    public static ArrayMesh BridgePile()
    {
        const string key = "g:bridge-pile";
        if (A2Cache.TryGetValue(key, out var hit)) return hit;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Box(st, new Vector3(-0.11f, -1f, -0.11f), new Vector3(0.11f, 0f, 0.11f), Palette.BarkDark);
        st.GenerateNormals();
        var mesh = st.Commit();
        mesh.ResourceName = key;
        A2Cache[key] = mesh;
        return mesh;
    }

    /// <summary>An axis-aligned box with outward faces.</summary>
    private static void Box(SurfaceTool st, Vector3 min, Vector3 max, Color color)
    {
        Vector3 P(int x, int y, int z) => new(x == 0 ? min.X : max.X, y == 0 ? min.Y : max.Y, z == 0 ? min.Z : max.Z);
        FaceQ(st, P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1), Vector3.Up, color);
        FaceQ(st, P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), Vector3.Down, color.Darkened(0.2f));
        FaceQ(st, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), Vector3.Back, color);
        FaceQ(st, P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), Vector3.Forward, color);
        FaceQ(st, P(1, 0, 0), P(1, 0, 1), P(1, 1, 1), P(1, 1, 0), Vector3.Right, color);
        FaceQ(st, P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0), Vector3.Left, color);
    }

    /// <summary>A quad a-b-c-d (in order around its edge) facing <paramref name="outward"/>, whatever order it was listed in.</summary>
    private static void FaceQ(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, Color color)
    {
        Face(st, a, b, c, outward, color);
        Face(st, a, c, d, outward, color);
    }

    /// <summary>A triangle facing <paramref name="outward"/>: reorders the corners so the face normal points that way.</summary>
    private static void Face(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 outward, Color color)
    {
        // Tri() expects the corners counter-clockwise seen from outside: (b - a) x (c - b) along the outward normal.
        if ((b - a).Cross(c - b).Dot(outward) < 0) (b, c) = (c, b);
        Tri(st, a, b, c, color);
    }

    private static List<(Vector3, Vector3, Vector3)>? _ico;

    /// <summary>Unit icosahedron subdivided once (80 faces), vertices on the unit sphere.</summary>
    private static List<(Vector3, Vector3, Vector3)> IcoFaces()
    {
        if (_ico is not null) return _ico;
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        var v = new[]
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        for (int i = 0; i < v.Length; i++) v[i] = v[i].Normalized();
        int[] f =
        [
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
        ];
        var list = new List<(Vector3, Vector3, Vector3)>(80);
        for (int i = 0; i < f.Length; i += 3)
        {
            var a = v[f[i]]; var b = v[f[i + 1]]; var c = v[f[i + 2]];
            var ab = ((a + b) * 0.5f).Normalized(); var bc = ((b + c) * 0.5f).Normalized(); var ca = ((c + a) * 0.5f).Normalized();
            list.Add((a, ab, ca)); list.Add((b, bc, ab)); list.Add((c, ca, bc)); list.Add((ab, bc, ca));
        }
        _ico = list;
        return list;
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
