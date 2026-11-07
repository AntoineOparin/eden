using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/world/terrain.tscn. Turns the snapshot heightmap into one <see cref="ArrayMesh"/> per
/// ChunkSize x ChunkSize cell chunk (shared edge vertices, smooth normals, biome splat weights in the vertex colour,
/// a dirt weight for yards and roads in UV2.x) rendered with shaders/terrain.gdshader.
/// <para>
/// A1: the mesh is built from a render copy of the heights. <see cref="ApplyFoundations"/> flattens that copy under
/// building footprints (plus a one-cell apron) to the foundation height, paints packed earth under them and dirt roads
/// from each building to its settlement's centre, and rebuilds only the chunks that changed. The sim's heightmap is
/// never modified. <see cref="SampleRender"/> gives props and NPCs the ground they are drawn on.
/// </para>
/// <para>
/// A2: the flattened footprint blends back into the slope over <see cref="YardBlend"/> metres; straight painted roads
/// gave way to a planned road network (<see cref="Roads"/>, drawn as draped meshes by <see cref="RoadRenderer"/>) with a
/// dirt underlay; <see cref="LastDirtyChunks"/> tells decor which chunks to refresh.
/// </para>
/// </summary>
public partial class TerrainBuilder : Node3D
{
    [Export] public ShaderMaterial? Material { get; set; }
    [Export] public float SnowHeight { get; set; } = 25f;
    [Export] public bool CastShadows { get; set; } = false;

    private ReadOnlyMemory<float> _builtHeights;
    private WorldInfo? _info;
    private float[] _render = [];
    private float[] _dirt = [];
    private byte[] _biomes = [];
    private readonly Dictionary<(int, int), MeshInstance3D> _chunks = new();

    public int ChunkCount { get; private set; }
    public WorldInfo? Info => _info;

    /// <summary>Chunks rebuilt by the last <see cref="ApplyFoundations"/> (diagnostics, tests).</summary>
    public int LastRebuiltChunks { get; private set; }

    /// <summary>Bumped each time the render heights change.</summary>
    public int RenderVersion { get; private set; }

    /// <summary>Vertices in one chunk mesh: (chunk+1)^2, edges shared with neighbours.</summary>
    public static int ChunkVertexCount(int chunkSize) => (chunkSize + 1) * (chunkSize + 1);

    public static int ChunkIndexCount(int chunkSize) => chunkSize * chunkSize * 6;

    /// <summary>Builds (or rebuilds when the heightmap instance changed) every chunk. Cheap no-op otherwise.</summary>
    public void EnsureBuilt(IWorldSnapshot snapshot)
    {
        if (_info == snapshot.World && _builtHeights.Equals(snapshot.Heights)) return;
        Build(snapshot);
    }

    public void Build(IWorldSnapshot snapshot)
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        _chunks.Clear();
        _roadCache.Clear();
        _roads = new();
        var info = snapshot.World;
        _info = info;
        _builtHeights = snapshot.Heights;
        ChunkCount = 0;
        if (info.Width < 2 || info.Height < 2 || snapshot.Heights.Length < info.Width * info.Height) return;
        _render = snapshot.Heights.ToArray();
        _dirt = new float[_render.Length];
        _biomes = snapshot.Biomes.Length >= _render.Length ? snapshot.Biomes.ToArray() : new byte[_render.Length];
        RenderVersion++;

        Material?.SetShaderParameter("sea_level", info.SeaLevel);
        Material?.SetShaderParameter("snow_height", info.SeaLevel + SnowHeight);
        BindTextures();

        int chunk = Math.Max(1, info.ChunkSize);
        int chunksX = (info.Width - 1 + chunk - 1) / chunk;
        int chunksZ = (info.Height - 1 + chunk - 1) / chunk;
        for (int cz = 0; cz < chunksZ; cz++)
        for (int cx = 0; cx < chunksX; cx++)
        {
            var mesh = BuildChunkMesh(_render, _biomes, info, cx, cz, _dirt);
            if (mesh is null) continue;
            var instance = new MeshInstance3D
            {
                Name = $"Chunk_{cx}_{cz}",
                Mesh = mesh,
                MaterialOverride = Material,
                CastShadow = CastShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(instance);
            _chunks[(cx, cz)] = instance;
            ChunkCount++;
        }
        // A2: a rebuilt heightmap (terraforming) keeps its yards and roads: re-apply the last foundations.
        if (_lastFoundations is not null) ApplyFoundationsCore(_lastFoundations, _lastBuildings, snapshot.Heights.Span);
    }

    private IReadOnlyList<Foundation>? _lastFoundations;
    private BuildingView[] _lastBuildings = [];

    private bool _texturesBound;

    /// <summary>The ambientCG detail maps (game/assets/textures, loaded without import).</summary>
    private void BindTextures()
    {
        if (_texturesBound || Material is null) return;
        _texturesBound = true;
        const string dir = "res://assets/textures/";
        foreach (var (param, file) in new[]
        {
            ("tex_sand", "ground093c_color.jpg"), ("tex_grass", "grass004_color.jpg"), ("tex_forest", "ground037_color.jpg"),
            ("tex_rock", "rock030_color.jpg"), ("tex_snow", "snow010a_color.jpg"),
        })
        {
            if (Art.GltfLoader.LoadTexture(dir + file) is { } tex) Material.SetShaderParameter(param, tex);
        }
    }

    public float SampleHeight(IWorldSnapshot snapshot, float x, float z) => HeightField.Sample(snapshot.Heights.Span, snapshot.World, x, z);

    /// <summary>Height of the drawn (flattened) terrain, never below sea level; falls back to 0 before the first build.</summary>
    public float SampleRender(float x, float z)
    {
        if (_info is null || _render.Length == 0) return 0f;
        return MathF.Max(_info.SeaLevel, HeightField.Sample(_render, _info, x, z));
    }

    /// <summary>
    /// M6: render height of grid point (<paramref name="x"/>, <paramref name="z"/>) (clamped to the grid), flattened like
    /// the drawn terrain but not clamped to sea level, so the possessed body can wade. Used by <see cref="TerrainCollision"/>.
    /// </summary>
    public float RenderHeightAt(int x, int z)
    {
        if (_info is null || _render.Length == 0) return 0f;
        x = Math.Clamp(x, 0, _info.Width - 1);
        z = Math.Clamp(z, 0, _info.Height - 1);
        return _render[z * _info.Width + x];
    }

    /// <summary>A2: width (metres) over which the flattened footprint blends back into the natural slope.</summary>
    [Export] public float YardBlend { get; set; } = 2.5f;

    /// <summary>A2: dirt weight painted under roads (the road mesh draws on top; this softens its edges).</summary>
    [Export] public float RoadUnderlay { get; set; } = 0.45f;

    /// <summary>Roads planned by the last <see cref="ApplyFoundations"/> (A2, <see cref="RoadNetwork"/>), over the drawn ground.</summary>
    public IReadOnlyList<Road> Roads => _roads;

    /// <summary>Terrain chunks (cx, cz) whose mesh the last <see cref="ApplyFoundations"/> rebuilt; decor and props refresh only these.</summary>
    public IReadOnlySet<(int X, int Z)> LastDirtyChunks => _lastDirty;

    private List<Road> _roads = new();
    private HashSet<(int X, int Z)> _lastDirty = new();
    private readonly Dictionary<int, (ulong Signature, List<Road> Roads)> _roadCache = new();

    /// <summary>
    /// Flattens the render heights under <paramref name="foundations"/> (footprint to the foundation top, blending back
    /// to the natural ground over <see cref="YardBlend"/> metres so a terrace reads as a yard), marks yards as dirt,
    /// plans the settlement roads over the result (<see cref="Roads"/>) with a dirt underlay, and rebuilds only the
    /// changed chunks. Render-side only.
    /// </summary>
    public void ApplyFoundations(IReadOnlyList<Foundation> foundations, ReadOnlySpan<BuildingView> buildings, ReadOnlySpan<float> simHeights)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _lastFoundations = foundations.ToArray();
        _lastBuildings = buildings.ToArray();
        try { ApplyFoundationsCore(foundations, buildings, simHeights); } finally { Dev.FrameTimeProbe.Note("terrain foundations", sw.Elapsed.TotalMilliseconds); }
    }

    private void ApplyFoundationsCore(IReadOnlyList<Foundation> foundations, ReadOnlySpan<BuildingView> buildings, ReadOnlySpan<float> simHeights)
    {
        if (_info is null || _render.Length == 0 || simHeights.Length < _render.Length) return;
        var info = _info;
        int w = info.Width, h = info.Height;
        float cs = info.CellSize;
        var render = simHeights.ToArray();
        var dirt = new float[render.Length];
        // Per cell: the highest footprint top covering it, and the strongest yard blend around footprints.
        var inside = new float[render.Length];
        var blend = new float[render.Length];
        var blendTop = new float[render.Length];
        Array.Fill(inside, float.MinValue);
        float core = cs * 0.5f;

        foreach (var f in foundations)
        {
            bool dock = f.Top <= info.SeaLevel + 0.31f;
            float reach = MathF.Max(f.HalfExtents.X, f.HalfExtents.Y) * 1.42f + core + YardBlend + cs;
            int x0 = Math.Max(0, (int)((f.Centre.X - reach) / cs)), x1 = Math.Min(w - 1, (int)((f.Centre.X + reach) / cs) + 1);
            int z0 = Math.Max(0, (int)((f.Centre.Z - reach) / cs)), z1 = Math.Min(h - 1, (int)((f.Centre.Z + reach) / cs) + 1);
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = z * w + x;
                if (simHeights[i] <= info.SeaLevel) continue; // never raise the sea floor (docks)
                float d = f.DistanceOutside(x * cs, z * cs);
                if (d <= core)
                {
                    if (!dock) inside[i] = MathF.Max(inside[i], f.Top);
                    dirt[i] = MathF.Max(dirt[i], 0.8f);
                }
                else if (d < core + YardBlend)
                {
                    float wgt = 1f - Mathf.SmoothStep(core, core + YardBlend, d);
                    if (!dock && wgt > blend[i]) { blend[i] = wgt; blendTop[i] = f.Top; }
                    dirt[i] = MathF.Max(dirt[i], 0.35f * wgt);
                }
            }
        }
        for (int i = 0; i < render.Length; i++)
        {
            if (inside[i] > float.MinValue) render[i] = inside[i];
            else if (blend[i] > 0f) render[i] = simHeights[i] + (blendTop[i] - simHeights[i]) * blend[i];
        }

        // Roads over the drawn ground (unclamped, so water reads as water), with a dirt underlay off the bridges.
        float Ground(float x, float z) => HeightField.Sample(render, info, x, z);
        _roads = RoadNetwork.Plan(buildings, foundations, Ground, info.SeaLevel, _roadCache);
        foreach (var road in _roads)
        {
            for (int k = 0; k < road.Points.Length; k++)
            {
                if (road.OnBridge(k * RoadNetwork.Step)) continue;
                var p = road.Points[k];
                int x = (int)MathF.Round(p.X / cs), z = (int)MathF.Round(p.Y / cs);
                if (x < 0 || z < 0 || x >= w || z >= h) continue;
                int i = z * w + x;
                if (render[i] <= info.SeaLevel + 0.2f) continue;
                dirt[i] = MathF.Max(dirt[i], RoadUnderlay);
            }
        }

        // Diff against what is drawn and rebuild only the touched chunks.
        int chunk = Math.Max(1, info.ChunkSize);
        var dirty = new HashSet<(int X, int Z)>();
        for (int i = 0; i < render.Length; i++)
        {
            if (render[i] == _render[i] && dirt[i] == _dirt[i]) continue;
            int x = i % w, z = i / w;
            // A vertex on a chunk edge belongs to both neighbours; normals also read the next cell, so mark around it.
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = Math.Clamp((x + dx) / chunk, 0, int.MaxValue), cz = Math.Clamp((z + dz) / chunk, 0, int.MaxValue);
                dirty.Add((cx, cz));
                if ((x + dx) % chunk == 0 && x + dx > 0) dirty.Add(((x + dx) / chunk - 1, cz));
                if ((z + dz) % chunk == 0 && z + dz > 0) dirty.Add((cx, (z + dz) / chunk - 1));
            }
        }
        _render = render;
        _dirt = dirt;
        LastRebuiltChunks = 0;
        _lastDirty = new HashSet<(int X, int Z)>();
        if (dirty.Count == 0) return;
        RenderVersion++;
        foreach (var key in dirty)
        {
            if (!_chunks.TryGetValue(key, out var node)) continue;
            var mesh = BuildChunkMesh(_render, _biomes, info, key.X, key.Z, _dirt);
            if (mesh is null) continue;
            node.Mesh = mesh;
            _lastDirty.Add(key);
            LastRebuiltChunks++;
        }
    }

    /// <summary>A2: the dirt weight (yards, roads) of the cell nearest a world point; 0 before the first build.</summary>
    public float DirtAt(float x, float z)
    {
        if (_info is null || _dirt.Length == 0) return 0f;
        int cx = Math.Clamp((int)MathF.Round(x / _info.CellSize), 0, _info.Width - 1);
        int cz = Math.Clamp((int)MathF.Round(z / _info.CellSize), 0, _info.Height - 1);
        return _dirt[cz * _info.Width + cx];
    }

    /// <summary>Render height at a world point without the sea-level clamp (bridges, piles); 0 before the first build.</summary>
    public float SampleRenderRaw(float x, float z)
    {
        if (_info is null || _render.Length == 0) return 0f;
        return HeightField.Sample(_render, _info, x, z);
    }

    /// <summary>
    /// One chunk: vertices at cell samples [cx*chunk, cx*chunk+chunk] (inclusive) clamped to the map, so a
    /// full interior chunk has (chunk+1)^2 vertices and chunk^2*2 triangles. Returns null for an empty chunk.
    /// <paramref name="dirt"/> (optional, per cell 0..1) goes into UV2.x for yards and roads.
    /// </summary>
    public static ArrayMesh? BuildChunkMesh(ReadOnlySpan<float> heights, ReadOnlySpan<byte> biomes, WorldInfo info, int chunkX, int chunkZ,
        ReadOnlySpan<float> dirt = default)
    {
        int chunk = Math.Max(1, info.ChunkSize);
        int x0 = chunkX * chunk, z0 = chunkZ * chunk;
        int x1 = Math.Min(x0 + chunk, info.Width - 1), z1 = Math.Min(z0 + chunk, info.Height - 1);
        int nx = x1 - x0 + 1, nz = z1 - z0 + 1;
        if (nx < 2 || nz < 2) return null;

        var vertices = new Vector3[nx * nz];
        var normals = new Vector3[nx * nz];
        var colors = new Color[nx * nz];
        var uvs = new Vector2[nx * nz];
        var uv2 = new Vector2[nx * nz];
        int w = info.Width, h = info.Height;
        float cs = info.CellSize;
        bool hasBiomes = biomes.Length >= w * h;
        bool hasDirt = dirt.Length >= w * h;

        for (int z = 0; z < nz; z++)
        for (int x = 0; x < nx; x++)
        {
            int gx = x0 + x, gz = z0 + z;
            int v = z * nx + x;
            float y = heights[gz * w + gx];
            vertices[v] = new Vector3(gx * cs, y, gz * cs);

            float hl = heights[gz * w + Math.Max(gx - 1, 0)];
            float hr = heights[gz * w + Math.Min(gx + 1, w - 1)];
            float hd = heights[Math.Max(gz - 1, 0) * w + gx];
            float hu = heights[Math.Min(gz + 1, h - 1) * w + gx];
            normals[v] = new Vector3(hl - hr, 2f * cs, hd - hu).Normalized();

            // Average the 3x3 neighbourhood so biome borders blend instead of stair-stepping along cells.
            float sand = 0, grass = 0, forest = 0, rock = 0;
            for (int oz = -1; oz <= 1; oz++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int sx = Math.Clamp(gx + ox, 0, w - 1), sz = Math.Clamp(gz + oz, 0, h - 1);
                var wts = Biome.SplatWeights(hasBiomes ? biomes[sz * w + sx] : Biome.Grassland);
                sand += wts.Sand; grass += wts.Grass; forest += wts.Forest; rock += wts.Rock;
            }
            colors[v] = new Color(sand / 9f, grass / 9f, forest / 9f, rock / 9f);
            uvs[v] = new Vector2(gx, gz);
            uv2[v] = new Vector2(hasDirt ? dirt[gz * w + gx] : 0f, 0f);
        }

        var indices = new int[(nx - 1) * (nz - 1) * 6];
        int i = 0;
        for (int z = 0; z < nz - 1; z++)
        for (int x = 0; x < nx - 1; x++)
        {
            int a = z * nx + x, b = a + 1, c = a + nx, d = c + 1;
            // Godot treats clockwise (seen from the front) as front-facing; seen from +Y these are clockwise.
            indices[i++] = a; indices[i++] = b; indices[i++] = c;
            indices[i++] = b; indices[i++] = d; indices[i++] = c;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
