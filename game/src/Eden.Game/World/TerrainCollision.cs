using Godot;

namespace Eden.Game;

/// <summary>
/// M6: terrain collision only around the possessed body, so the god view keeps no physics at all (A1 budgets). A
/// <see cref="StaticBody3D"/> on <see cref="Layer"/> with one <see cref="ConcavePolygonShape3D"/> per terrain chunk in a
/// (2·<see cref="Radius"/>+1)² block around the body (3×3 chunks of 64 m by default), built from
/// <see cref="TerrainBuilder.RenderHeightAt"/> (the drawn, foundation-flattened heights) with the same grid as the render
/// mesh. Shapes are rebuilt only when the body enters another chunk or the render heights change; nothing is baked
/// for navigation (the sim is the authority for where a body may go).
/// </summary>
public partial class TerrainCollision : StaticBody3D
{
    /// <summary>Physics layer 10 (bit 9): terrain for the possessed body and the possession camera's spring arm.</summary>
    public const uint Layer = 1u << 9;

    [Export] public int Radius { get; set; } = 1;

    private readonly Dictionary<(int, int), CollisionShape3D> _shapes = new();
    private readonly Stack<CollisionShape3D> _spare = new();
    private readonly HashSet<(int, int)> _wanted = new();
    private readonly List<(int, int)> _drop = new();
    private (int, int) _centre = (int.MinValue, int.MinValue);
    private int _version = -1;

    public int ShapeCount => _shapes.Count;
    public int Rebuilds { get; private set; }
    public IEnumerable<(int X, int Z)> Chunks => _shapes.Keys;

    public TerrainCollision()
    {
        CollisionLayer = Layer;
        CollisionMask = 0;
    }

    /// <summary>Ensures the chunks around <paramref name="position"/> have collision. Cheap when nothing changed.</summary>
    public void UpdateAround(Vector3 position, TerrainBuilder terrain)
    {
        if (terrain.Info is not { } info || info.ChunkSize <= 0) return;
        float chunkMetres = info.ChunkSize * info.CellSize;
        int cx = (int)MathF.Floor(position.X / chunkMetres), cz = (int)MathF.Floor(position.Z / chunkMetres);
        bool versionChanged = terrain.RenderVersion != _version;
        if ((cx, cz) == _centre && !versionChanged) return;
        _centre = (cx, cz);
        _version = terrain.RenderVersion;

        int chunksX = (info.Width + info.ChunkSize - 1) / info.ChunkSize, chunksZ = (info.Height + info.ChunkSize - 1) / info.ChunkSize;
        _wanted.Clear();
        for (int dz = -Radius; dz <= Radius; dz++)
        for (int dx = -Radius; dx <= Radius; dx++)
        {
            int x = cx + dx, z = cz + dz;
            if (x >= 0 && z >= 0 && x < chunksX && z < chunksZ) _wanted.Add((x, z));
        }
        _drop.Clear();
        foreach (var key in _shapes.Keys) if (!_wanted.Contains(key) || versionChanged) _drop.Add(key);
        foreach (var key in _drop)
        {
            var shape = _shapes[key];
            _shapes.Remove(key);
            shape.Disabled = true;
            _spare.Push(shape);
        }
        foreach (var key in _wanted)
        {
            if (_shapes.ContainsKey(key)) continue;
            var node = _spare.Count > 0 ? _spare.Pop() : NewShapeNode();
            ((ConcavePolygonShape3D)node.Shape).SetFaces(ChunkFaces(terrain, key.Item1, key.Item2));
            node.Disabled = false;
            _shapes[key] = node;
            Rebuilds++;
        }
    }

    /// <summary>Drops every shape (on release).</summary>
    public void Clear()
    {
        foreach (var shape in _shapes.Values)
        {
            shape.Disabled = true;
            _spare.Push(shape);
        }
        _shapes.Clear();
        _centre = (int.MinValue, int.MinValue);
    }

    private CollisionShape3D NewShapeNode()
    {
        // No backface collision: a body that ends up under the surface pops out instead of being trapped.
        var node = new CollisionShape3D { Shape = new ConcavePolygonShape3D { BackfaceCollision = false } };
        AddChild(node);
        return node;
    }

    /// <summary>Two triangles per cell of chunk (<paramref name="chunkX"/>, <paramref name="chunkZ"/>), world coordinates.</summary>
    public static Vector3[] ChunkFaces(TerrainBuilder terrain, int chunkX, int chunkZ)
    {
        var info = terrain.Info!;
        int size = info.ChunkSize;
        int x0 = chunkX * size, z0 = chunkZ * size;
        int x1 = Math.Min(x0 + size, info.Width - 1), z1 = Math.Min(z0 + size, info.Height - 1);
        int cellsX = Math.Max(0, x1 - x0), cellsZ = Math.Max(0, z1 - z0);
        var faces = new Vector3[cellsX * cellsZ * 6];
        float c = info.CellSize;
        int k = 0;
        for (int z = z0; z < z1; z++)
        for (int x = x0; x < x1; x++)
        {
            var a = new Vector3(x * c, terrain.RenderHeightAt(x, z), z * c);
            var b = new Vector3((x + 1) * c, terrain.RenderHeightAt(x + 1, z), z * c);
            var d = new Vector3(x * c, terrain.RenderHeightAt(x, z + 1), (z + 1) * c);
            var e = new Vector3((x + 1) * c, terrain.RenderHeightAt(x + 1, z + 1), (z + 1) * c);
            faces[k++] = a; faces[k++] = b; faces[k++] = d;
            faces[k++] = b; faces[k++] = e; faces[k++] = d;
        }
        return faces;
    }
}
