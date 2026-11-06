using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Root of scenes/world/terrain.tscn. Turns the snapshot heightmap into one <see cref="ArrayMesh"/> per
/// ChunkSize x ChunkSize cell chunk (shared edge vertices, smooth normals from the full heightfield,
/// biome splat weights in the vertex colour) rendered with shaders/terrain.gdshader.
/// Built once per world; the same heights array means nothing changed.
/// </summary>
public partial class TerrainBuilder : Node3D
{
    [Export] public ShaderMaterial? Material { get; set; }
    [Export] public float SnowHeight { get; set; } = 25f;

    private ReadOnlyMemory<float> _builtHeights;
    private WorldInfo? _info;

    public int ChunkCount { get; private set; }
    public WorldInfo? Info => _info;

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
        var info = snapshot.World;
        _info = info;
        _builtHeights = snapshot.Heights;
        ChunkCount = 0;
        if (info.Width < 2 || info.Height < 2 || snapshot.Heights.Length < info.Width * info.Height) return;

        Material?.SetShaderParameter("sea_level", info.SeaLevel);
        Material?.SetShaderParameter("snow_height", info.SeaLevel + SnowHeight);

        int chunk = Math.Max(1, info.ChunkSize);
        int chunksX = (info.Width - 1 + chunk - 1) / chunk;
        int chunksZ = (info.Height - 1 + chunk - 1) / chunk;
        for (int cz = 0; cz < chunksZ; cz++)
        for (int cx = 0; cx < chunksX; cx++)
        {
            var mesh = BuildChunkMesh(snapshot.Heights.Span, snapshot.Biomes.Span, info, cx, cz);
            if (mesh is null) continue;
            var instance = new MeshInstance3D { Name = $"Chunk_{cx}_{cz}", Mesh = mesh, MaterialOverride = Material };
            AddChild(instance);
            ChunkCount++;
        }
    }

    public float SampleHeight(IWorldSnapshot snapshot, float x, float z) => HeightField.Sample(snapshot.Heights.Span, snapshot.World, x, z);

    /// <summary>
    /// One chunk: vertices at cell samples [cx*chunk, cx*chunk+chunk] (inclusive) clamped to the map, so a
    /// full interior chunk has (chunk+1)^2 vertices and chunk^2*2 triangles. Returns null for an empty chunk.
    /// </summary>
    public static ArrayMesh? BuildChunkMesh(ReadOnlySpan<float> heights, ReadOnlySpan<byte> biomes, WorldInfo info, int chunkX, int chunkZ)
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
        int w = info.Width, h = info.Height;
        float cs = info.CellSize;
        bool hasBiomes = biomes.Length >= w * h;

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
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
