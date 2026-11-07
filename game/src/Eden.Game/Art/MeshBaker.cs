using Godot;

namespace Eden.Game.Art;

/// <summary>How a model's colours are rewritten while baking.</summary>
public enum Recolor
{
    /// <summary>Keep the model's colours.</summary>
    None,
    /// <summary>Kenney Nature Kit: map by material name (<see cref="Palette.NatureMaterial"/>).</summary>
    Nature,
    /// <summary>Kenney Fantasy Town Kit, stone cells as cream plaster.</summary>
    TownPlaster,
    /// <summary>Kenney Fantasy Town Kit, stone cells as grey-blue stone.</summary>
    TownStone,
    /// <summary>Kenney Fantasy Town Kit, stone cells as fired brick.</summary>
    TownBrick,
}

/// <summary>
/// One model placed into a baked mesh. <paramref name="Accent"/> marks the whole part as a settlement-colour accent
/// (vertex alpha 0); <paramref name="Leaf"/> marks it as foliage that sways and takes the per-instance tint (alpha 0.5).
/// <paramref name="Tint"/> overrides every colour of the part (multiplied with the texel brightness).
/// </summary>
public readonly record struct Part(string Path, Transform3D Xform, Recolor Recolor = Recolor.None, bool Accent = false,
    bool Leaf = false, Color? Tint = null);

/// <summary>
/// Flattens glTF models into one single-surface <see cref="ArrayMesh"/>: transforms applied, every material's colour
/// (albedo × colormap texel at the vertex UV) baked into the vertex colour (linear) after the palette remap, one index
/// buffer. One surface means one draw call per MultiMesh and one shared shader. Vertex alpha is a role mask read by
/// shaders/props.gdshader: 1 plain, 0.5 foliage (wind + tint), 0 settlement accent.
/// </summary>
public static class MeshBaker
{
    private static readonly Dictionary<string, ArrayMesh> Cache = new();

    /// <summary>Bakes (and caches under <paramref name="key"/>) the parts into one mesh. Returns null when nothing loaded.</summary>
    public static ArrayMesh? Bake(string key, IReadOnlyList<Part> parts)
    {
        if (Cache.TryGetValue(key, out var hit)) return hit;
        var verts = new List<Vector3>(1024);
        var norms = new List<Vector3>(1024);
        var cols = new List<Color>(1024);
        var idx = new List<int>(2048);
        foreach (var part in parts)
        {
            var root = GltfLoader.Template(part.Path);
            if (root is null) continue;
            Append(root, part.Xform, part, verts, norms, cols, idx);
        }
        if (verts.Count == 0) return null;

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = cols.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.ResourceName = key;
        Cache[key] = mesh;
        return mesh;
    }

    public static void ClearCache() => Cache.Clear();

    /// <summary>Axis-aligned bounds of a mesh's single surface (cheap, from the vertex array).</summary>
    public static Aabb Bounds(Mesh mesh) => mesh.GetAabb();

    private static void Append(Node node, Transform3D parentXform, in Part part,
        List<Vector3> verts, List<Vector3> norms, List<Color> cols, List<int> idx)
    {
        var xform = parentXform;
        if (node is Node3D n3) xform = parentXform * n3.Transform;
        if (node is MeshInstance3D mi && mi.Mesh is { } mesh)
        {
            var basis = xform.Basis;
            var normalBasis = basis.Inverse().Transposed();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++)
            {
                var arr = mesh.SurfaceGetArrays(s);
                var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var nn = arr[(int)Mesh.ArrayType.Normal].AsVector3Array();
                var uv = arr[(int)Mesh.ArrayType.TexUV].AsVector2Array();
                var ii = arr[(int)Mesh.ArrayType.Index].AsInt32Array();
                var mat = mi.GetSurfaceOverrideMaterial(s) ?? mesh.SurfaceGetMaterial(s);
                string matName = mat?.ResourceName ?? "";
                var albedo = Colors.White;
                Image? tex = null;
                if (mat is BaseMaterial3D bm)
                {
                    albedo = bm.AlbedoColor;
                    tex = bm.AlbedoTexture?.GetImage() ?? ColormapFallback(part.Path, bm);
                }
                int baseIndex = verts.Count;
                for (int k = 0; k < v.Length; k++)
                {
                    verts.Add(xform * v[k]);
                    norms.Add(nn.Length > k ? (normalBasis * nn[k]).Normalized() : Vector3.Up);
                    var c = albedo;
                    if (tex is not null && uv.Length > k) c *= Sample(tex, uv[k]);
                    c = Remap(c, matName, part);
                    float alpha = part.Accent ? 0f : part.Leaf || IsLeafMaterial(matName) ? 0.5f : 1f;
                    var lin = c.SrgbToLinear();
                    lin.A = alpha;
                    cols.Add(lin);
                }
                if (ii.Length > 0)
                {
                    // A mirrored transform (negative determinant) flips winding; swap two indices to keep faces outward.
                    bool flip = basis.Determinant() < 0;
                    for (int k = 0; k + 2 < ii.Length; k += 3)
                    {
                        idx.Add(baseIndex + ii[k]);
                        idx.Add(baseIndex + (flip ? ii[k + 2] : ii[k + 1]));
                        idx.Add(baseIndex + (flip ? ii[k + 1] : ii[k + 2]));
                    }
                }
                else
                {
                    for (int k = 0; k < v.Length; k++) idx.Add(baseIndex + k);
                }
            }
        }
        foreach (var child in node.GetChildren()) Append(child, xform, part, verts, norms, cols, idx);
    }

    private static bool IsLeafMaterial(string m) => m.StartsWith("leafs", StringComparison.Ordinal);

    private static Color Remap(Color c, string matName, in Part part)
    {
        if (part.Tint is { } tint)
        {
            c.ToHsv(out _, out _, out float v);
            float k = Math.Clamp(0.55f + v * 0.5f, 0.6f, 1.1f);
            return new Color(tint.R * k, tint.G * k, tint.B * k);
        }
        return part.Recolor switch
        {
            Recolor.Nature => Palette.NatureMaterial(matName, c),
            Recolor.TownPlaster => Palette.TownTexel(c, Palette.StoneAs.Plaster),
            Recolor.TownStone => Palette.TownTexel(c, Palette.StoneAs.Stone),
            Recolor.TownBrick => Palette.TownTexel(c, Palette.StoneAs.Brick),
            _ => c,
        };
    }

    /// <summary>When the runtime glTF import could not resolve the external colormap, read it from the pack folder.</summary>
    private static Image? ColormapFallback(string modelPath, BaseMaterial3D mat)
    {
        if (!mat.ResourceName.Contains("colormap", StringComparison.OrdinalIgnoreCase)) return null;
        string dir = modelPath[..(modelPath.LastIndexOf('/') + 1)];
        return GltfLoader.LoadImage(dir + "Textures/colormap.png");
    }

    private static Color Sample(Image img, Vector2 uv)
    {
        int w = img.GetWidth(), h = img.GetHeight();
        float u = uv.X - MathF.Floor(uv.X), v = uv.Y - MathF.Floor(uv.Y);
        int x = Math.Clamp((int)(u * w), 0, w - 1), y = Math.Clamp((int)(v * h), 0, h - 1);
        return img.GetPixel(x, y);
    }
}
