using Eden.Game.Art;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Draws <see cref="IWorldSnapshot.Resources"/> (trees by biome, stumps, stones, ore, berry bushes, crops on farm plots)
/// and static ground decor (grass tufts, flowers, ferns, mushrooms) through chunked MultiMeshes (shaders/props.gdshader).
/// Resources are re-batched at most every <see cref="RefreshSeconds"/> when the snapshot's array changed; decor is built
/// once per world and only drawn within <see cref="DecorRange"/> of the camera (visibility range per chunk).
/// </summary>
public partial class ResourceRenderer : Node3D
{
    [Export] public float ChunkSize { get; set; } = 128f;
    [Export] public double RefreshSeconds { get; set; } = 0.5;
    [Export] public float DecorChunkSize { get; set; } = 64f;
    [Export] public float DecorRange { get; set; } = 150f;
    /// <summary>Decor per land cell (0..1, scaled per biome). 0 disables decor.</summary>
    [Export] public float DecorDensity { get; set; } = 0.35f;
    [Export] public float WindStrength { get; set; } = 0.012f;

    private PropBatches? _trees, _ground, _decor;
    private ShaderMaterial? _foliage, _solid;
    private ReadOnlyMemory<ResourceView> _drawn;
    private double _lastRefresh = double.NegativeInfinity;
    private WorldInfo? _decorFor;
    private readonly Dictionary<int, float> _plotRotation = new();

    /// <summary>Height of the drawn ground (the terrain's flattened render copy). Null: use the snapshot heights.</summary>
    public Func<float, float, float>? Ground { get; set; }

    /// <summary>Set by Main to the terrain's render version; a change forces a re-batch so props follow the ground.</summary>
    public int GroundVersion { get; set; }

    private int _batchedGroundVersion = -1;

    public int ResourceInstances => (_trees?.InstanceCount ?? 0) + (_ground?.InstanceCount ?? 0);
    public int DecorInstances => _decor?.InstanceCount ?? 0;
    public int Batches => (_trees?.ActiveBatches ?? 0) + (_ground?.ActiveBatches ?? 0) + (_decor?.ActiveBatches ?? 0);

    /// <summary>A2: the terrain, for its dirty chunks and road dirt (decor and props then refresh only what changed). Null: full rebuilds.</summary>
    public TerrainBuilder? Terrain { get; set; }

    /// <summary>A2: animals spread over this radius (metres) around a herd node.</summary>
    [Export] public float HerdRadius { get; set; } = 6f;
    /// <summary>A2: each animal drifts this far (metres) around its own spot in the herd.</summary>
    [Export] public float AnimalDrift { get; set; } = 1.4f;
    [Export] public bool AnimalShadows { get; set; } = true;

    private PropBatches? _animals;
    private ShaderMaterial? _animalMaterial;

    /// <summary>One drawn animal: species, its home spot and drift phases (all from the herd id; the sim does not move herds).</summary>
    private readonly record struct Animal(Species Species, Vector2 Home, float Phase, float Speed, float Scale, float Shade, Basis Tilt);

    private readonly List<Animal> _herdAnimals = new();
    private IReadOnlySet<int>? _pendingDecor;
    private int _pendingDecorWait;

    /// <summary>Animals drawn in the last frame (A2).</summary>
    public int AnimalInstances => _animals?.InstanceCount ?? 0;

    /// <summary>Herd animals currently placed (tests): species and world position.</summary>
    public IEnumerable<(Species Species, Vector3 Position)> AnimalPositions(double nowSeconds, IWorldSnapshot s)
    {
        foreach (var a in _herdAnimals) yield return (a.Species, AnimalTransform(s, a, nowSeconds).Origin);
    }

    private void Initialise()
    {
        if (_trees is not null) return;
        var shader = GD.Load<Shader>("res://shaders/props.gdshader");
        _foliage = new ShaderMaterial { Shader = shader };
        _foliage.SetShaderParameter("wind_strength", WindStrength);
        _solid = new ShaderMaterial { Shader = shader };
        _trees = new PropBatches(this, _foliage, castShadows: true);
        _ground = new PropBatches(this, _solid, castShadows: true);
        _decor = new PropBatches(this, _foliage, castShadows: false, DecorRange);
        // Animals: the props shader's height-weighted sway, slow and small, reads as heads moving while grazing.
        _animalMaterial = new ShaderMaterial { Shader = shader };
        _animalMaterial.SetShaderParameter("wind_strength", 0.02f);
        _animalMaterial.SetShaderParameter("wind_height", 1.6f);
        _animalMaterial.SetShaderParameter("wind_speed", 0.7f);
        _animals = new PropBatches(this, _animalMaterial, AnimalShadows);
    }

    public void Render(IWorldSnapshot s, double nowSeconds)
    {
        Initialise();
        if (_decorFor != s.World)
        {
            BuildDecor(s, null);
            _pendingDecor = null;
        }
        else if (_decorGroundVersion != GroundVersion)
        {
            // A2: the ground changed under some chunks (a building went up). Refresh only their decor, one frame later,
            // so the terrain rebuild and the decor rebuild do not land in the same frame.
            _decorGroundVersion = GroundVersion;
            var dirty = DirtyChunks(DecorChunkSize, s.World);
            if (dirty is null) BuildDecor(s, null);
            else if (dirty.Count > 0)
            {
                var merged = new HashSet<int>(dirty);
                if (_pendingDecor is not null) merged.UnionWith(_pendingDecor);
                _pendingDecor = merged;
                _pendingDecorWait = 0; // run on the next frame
            }
        }
        else if (_pendingDecor is not null && --_pendingDecorWait < 0)
        {
            BuildDecor(s, _pendingDecor);
            _pendingDecor = null;
        }

        bool groundMoved = GroundVersion != _batchedGroundVersion;
        if (groundMoved || (!s.Resources.Equals(_drawn) && nowSeconds - _lastRefresh >= RefreshSeconds))
        {
            // Only the ground moved: re-place the props of the touched chunks; otherwise rebuild everything.
            IReadOnlySet<int>? only = groundMoved && s.Resources.Equals(_drawn) ? DirtyChunks(ChunkSize, s.World) : null;
            _batchedGroundVersion = GroundVersion;
            _drawn = s.Resources;
            _lastRefresh = nowSeconds;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (only is null || only.Count > 0) Rebatch(s, only);
            Dev.FrameTimeProbe.Note(only is null ? "resource rebatch" : "resource rebatch (dirty chunks)", sw.Elapsed.TotalMilliseconds);
        }
        UpdateAnimals(s, nowSeconds);
    }

    /// <summary>
    /// The prop chunks (of <paramref name="chunkSize"/> metres) that overlap the terrain chunks rebuilt by the last
    /// foundation pass, or null when there is no terrain to ask (tests): then callers rebuild everything.
    /// </summary>
    private HashSet<int>? DirtyChunks(float chunkSize, WorldInfo info)
    {
        if (Terrain is null) return null;
        var result = new HashSet<int>();
        float terrainChunk = Math.Max(1, info.ChunkSize) * info.CellSize;
        int chunksX = Math.Max(1, (int)MathF.Ceiling(info.Width * info.CellSize / chunkSize));
        foreach (var (cx, cz) in Terrain.LastDirtyChunks)
        {
            // The terrain already marks the neighbours of a changed edge vertex, so the chunk box itself is enough.
            float x0 = cx * terrainChunk + 0.01f, x1 = (cx + 1) * terrainChunk - 0.01f;
            float z0 = cz * terrainChunk + 0.01f, z1 = (cz + 1) * terrainChunk - 0.01f;
            for (int pz = Math.Max(0, (int)(z0 / chunkSize)); pz <= (int)(z1 / chunkSize); pz++)
            for (int px = Math.Max(0, (int)(x0 / chunkSize)); px <= (int)(x1 / chunkSize); px++)
                result.Add(pz * chunksX + px);
        }
        return result;
    }

    /// <summary>Rebuilds every resource batch from the snapshot. Public for tests.</summary>
    public void Rebatch(IWorldSnapshot s) => Rebatch(s, null);

    /// <summary>Rebuilds the resource batches of <paramref name="only"/> (prop chunk indices), or all of them when null.</summary>
    public void Rebatch(IWorldSnapshot s, IReadOnlySet<int>? only)
    {
        Initialise();
        _plotRotation.Clear();
        foreach (var b in s.Buildings.Span)
            if (b.Kind == "farm-plot") _plotRotation[b.Id] = b.Rotation;

        if (only is null)
        {
            _trees!.Begin();
            _ground!.Begin();
            _herdAnimals.Clear();
        }
        else
        {
            _trees!.Begin(only);
            _ground!.Begin(only);
        }
        var info = s.World;
        var biomes = s.Biomes.Span;
        int chunksX = Math.Max(1, (int)MathF.Ceiling(info.Width * info.CellSize / ChunkSize));
        foreach (ref readonly var r in s.Resources.Span)
        {
            int chunk = (int)(r.Z / ChunkSize) * chunksX + (int)(r.X / ChunkSize);
            if (r.Kind == "game")
            {
                if (only is null) AddHerd(s, r, BiomeAt(biomes, info, r.X, r.Z));
                continue;
            }
            if (only is not null && !only.Contains(chunk)) continue;
            uint h = (uint)r.Variant * 2654435761u ^ (uint)r.Id * 40503u;
            float jx = ((h >> 4) & 0xFF) / 255f - 0.5f, jz = ((h >> 12) & 0xFF) / 255f - 0.5f;
            float yaw = ((h >> 20) & 0xFF) / 255f * MathF.Tau;
            float px = r.X + jx * info.CellSize * 0.8f, pz = r.Z + jz * info.CellSize * 0.8f;
            var pos = new Vector3(px, GroundAt(s, px, pz), pz);
            switch (r.Kind)
            {
                case "tree":
                {
                    if (r.Amount < 0.12f)
                    {
                        if (NatureFactory.Stump() is { } stump) _ground.Add(chunk, stump, Tilted(s, pos, yaw, 1f), Colors.White);
                        break;
                    }
                    byte biome = BiomeAt(biomes, info, r.X, r.Z);
                    var set = NatureFactory.TreeSet(biome);
                    var mesh = NatureFactory.Tree(set[r.Variant % set.Length]);
                    if (mesh is null) break;
                    float scale = (0.7f + 0.3f * r.Amount) * (0.85f + ((h >> 8) & 0x3F) / 63f * 0.35f);
                    float shade = 0.88f + ((h >> 14) & 0x3F) / 63f * 0.24f;
                    // Trees stay upright and sink ~3 % of their height (about 7 m at scale 1) so no gap shows on slopes.
                    _trees.Add(chunk, mesh, Xf(pos - new Vector3(0, Math.Clamp(0.14f * scale + SlopeDrop(s, pos), 0.14f * scale, 0.35f * scale), 0), yaw, scale), new Color(shade, shade * 1.02f, shade * 0.95f, (h & 0xFF) / 255f));
                    break;
                }
                case "stone":
                    if (NatureFactory.Stone(r.Variant) is { } stone)
                        _ground.Add(chunk, stone, Tilted(s, pos - new Vector3(0, 0.1f, 0), yaw, 0.55f + 0.5f * r.Amount), Colors.White);
                    break;
                case "ore":
                    if (NatureFactory.Ore() is { } ore)
                        _ground.Add(chunk, ore, Tilted(s, pos - new Vector3(0, 0.1f, 0), yaw, 0.6f + 0.45f * r.Amount), Colors.White);
                    break;
                case "berry":
                {
                    var mesh = r.Amount < 0.15f ? NatureFactory.BerryBare() : NatureFactory.Berry();
                    if (mesh is not null) _trees.Add(chunk, mesh, Tilted(s, pos - new Vector3(0, 0.05f, 0), yaw, 0.8f + 0.3f * r.Amount), new Color(1f, 1f, 1f, (h & 0xFF) / 255f));
                    break;
                }
                case "grain":
                {
                    if (r.Amount <= 0f) break;
                    int stage = r.Amount >= 1f ? 2 : r.Amount >= 0.45f ? 1 : 0;
                    if (NatureFactory.Crop(stage) is not { } crop) break;
                    int buildingId = r.Id - CropIdBase;
                    float rot = _plotRotation.TryGetValue(buildingId, out float pr) ? pr : 0f;
                    float grow = 0.6f + 0.4f * Math.Min(1f, r.Amount * 1.5f); // crops grow in height; the rows keep their spacing
                    var cropXf = new Transform3D(new Basis(Vector3.Up, rot) * Basis.FromScale(new Vector3(1f, grow, 1f)), new Vector3(r.X, GroundAt(s, r.X, r.Z), r.Z));
                    _trees.Add(chunk, crop, cropXf, new Color(1f, 1f, 1f, (h & 0xFF) / 255f));
                    break;
                }
            }
        }
        _trees.Commit();
        _ground.Commit();
    }

    /// <summary>Must match Eden.Sim's <c>World.CropViewIdBase</c> (documented on <see cref="ResourceView"/>).</summary>
    public const int CropIdBase = 1 << 24;

    private int _decorGroundVersion = -1;

    /// <summary>Decor cells scanned by the last decor build (diagnostics, tests: a building rebuilds only its chunks).</summary>
    public int LastDecorCellsScanned { get; private set; }

    private void BuildDecor(IWorldSnapshot s, IReadOnlySet<int>? only)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { BuildDecorCore(s, only); }
        finally { Dev.FrameTimeProbe.Note(only is null ? "decor rebuild" : $"decor rebuild ({only.Count} chunks)", sw.Elapsed.TotalMilliseconds); }
    }

    /// <summary>
    /// Ground decor. With <paramref name="only"/> (decor chunk indices) just those chunks are scanned and re-uploaded:
    /// A1 rescanned all ~65k cells whenever any building finished. Grass stays off yards, terraces and roads.
    /// </summary>
    private void BuildDecorCore(IWorldSnapshot s, IReadOnlySet<int>? only)
    {
        _decorFor = s.World;
        _decorGroundVersion = GroundVersion;
        if (only is null) _decor!.Begin();
        else _decor!.Begin(only);
        LastDecorCellsScanned = 0;
        var info = s.World;
        var heights = s.Heights.Span;
        var biomes = s.Biomes.Span;
        if (DecorDensity <= 0 || heights.Length < info.Width * info.Height || biomes.Length < heights.Length)
        {
            _decor.Commit();
            return;
        }
        var sets = new Dictionary<byte, ArrayMesh?[]>();
        int chunksX = Math.Max(1, (int)MathF.Ceiling(info.Width * info.CellSize / DecorChunkSize));
        int cellsPerChunk = Math.Max(1, (int)MathF.Ceiling(DecorChunkSize / info.CellSize));
        void Scan(int zFrom, int zTo, int xFrom, int xTo, ReadOnlySpan<float> heights, ReadOnlySpan<byte> biomes)
        {
            for (int z = Math.Max(1, zFrom); z < Math.Min(info.Height - 1, zTo); z++)
            for (int x = Math.Max(1, xFrom); x < Math.Min(info.Width - 1, xTo); x++)
            {
                LastDecorCellsScanned++;
                int i = z * info.Width + x;
                byte biome = biomes[i];
                float chance = biome switch
                {
                    Biome.Grassland => 1f,
                    Biome.Forest => 0.8f,
                    Biome.Beach => 0.12f,
                    Biome.Mountain => 0.2f,
                    _ => 0f,
                } * DecorDensity;
                if (chance <= 0 || heights[i] <= info.SeaLevel + 0.4f) continue;
                uint h = Hash((uint)info.Seed, (uint)x, (uint)z);
                if ((h & 0xFFFF) / 65535f >= chance) continue;
                if (!sets.TryGetValue(biome, out var meshes)) sets[biome] = meshes = NatureFactory.Decor(biome);
                if (meshes.Length == 0) continue;
                var mesh = meshes[(h >> 16) % (uint)meshes.Length];
                if (mesh is null) continue;
                float wx = (x + ((h >> 3) & 0xFF) / 255f - 0.5f) * info.CellSize, wz = (z + ((h >> 11) & 0xFF) / 255f - 0.5f) * info.CellSize;
                int chunk = (int)(wz / DecorChunkSize) * chunksX + (int)(wx / DecorChunkSize);
                if (only is not null && !only.Contains(chunk)) continue;
                float y = HeightField.Sample(heights, info, wx, wz);
                if (y <= info.SeaLevel + 0.3f) continue;
                if (Ground is not null && MathF.Abs(Ground(wx, wz) - y) > 0.05f) continue; // yards and terraces stay clear
                if (Terrain is not null && Terrain.DirtAt(wx, wz) > 0.3f) continue; // and roads
                float yaw = ((h >> 24) & 0xFF) / 255f * MathF.Tau;
                float scale = 0.75f + ((h >> 19) & 0x1F) / 31f * 0.5f;
                _decor.Add(chunk, mesh, Xf(new Vector3(wx, y - 0.03f, wz), yaw, scale), new Color(1f, 1f, 1f, ((h >> 7) & 0xFF) / 255f));
            }
        }
        if (only is null) Scan(0, info.Height, 0, info.Width, heights, biomes);
        else
            foreach (int c in only)
            {
                int cx = c % chunksX, cz = c / chunksX;
                // One cell of overlap: a cell's jittered decor can land in the neighbouring chunk.
                Scan(cz * cellsPerChunk - 1, (cz + 1) * cellsPerChunk + 1, cx * cellsPerChunk - 1, (cx + 1) * cellsPerChunk + 1, heights, biomes);
            }
        _decor.Commit();
    }

    // ---- A2: herds ------------------------------------------------------------------------------------------------

    /// <summary>
    /// A few animals per herd node, spread deterministically from the node id over <see cref="HerdRadius"/> (golden
    /// angle spiral with jitter): 2 to 5 by the herd's amount, none when hunted out. Species by biome and variant.
    /// </summary>
    private void AddHerd(IWorldSnapshot s, in ResourceView r, byte biome)
    {
        if (r.Amount < 0.05f) return;
        int count = 2 + (int)MathF.Round(r.Amount * 3f);
        var species = AnimalLibrary.SpeciesFor(biome, r.Variant);
        uint h = Hash(0xA2u, (uint)r.Id, (uint)r.Variant);
        float baseAngle = (h & 0xFFFF) / 65535f * MathF.Tau;
        for (int k = 0; k < count; k++)
        {
            uint hk = Hash(h, (uint)k, 7u);
            float radius = HerdRadius * MathF.Sqrt((k + 0.5f) / count) * (0.75f + ((hk >> 4) & 0xFF) / 255f * 0.4f);
            float angle = baseAngle + k * 2.39996f + ((hk >> 12) & 0xFF) / 255f * 0.5f;
            var home = new Vector2(r.X + MathF.Cos(angle) * radius, r.Z + MathF.Sin(angle) * radius);
            float phase = ((hk >> 20) & 0xFFF) / 4095f * MathF.Tau;
            float speed = 0.05f + ((hk >> 8) & 0xFF) / 255f * 0.05f; // radians per second around the drift circle
            float scale = 0.9f + ((hk >> 16) & 0x3F) / 63f * 0.2f;
            float shade = 0.9f + ((hk >> 2) & 0x3F) / 63f * 0.15f;
            // The ground's lean at the animal's spot, worked out once: the drift is too small for it to change.
            var n = NormalAt(s, home.X, home.Y);
            float lean = MathF.Min(MathF.Acos(Math.Clamp(n.Y, -1f, 1f)), Mathf.DegToRad(MaxTiltDegrees));
            var axis = Vector3.Up.Cross(n);
            var tilt = axis.LengthSquared() < 1e-6f ? Basis.Identity : new Basis(axis.Normalized(), lean);
            _herdAnimals.Add(new Animal(species, home, phase, speed, scale, shade, tilt));
        }
    }

    /// <summary>
    /// Where an animal stands at <paramref name="now"/>: a slow drift on a lopsided loop around its home spot (never
    /// leaving the herd's radius), facing the way it drifts, on the drawn ground, tilted with it up to
    /// <see cref="MaxTiltDegrees"/>, sunk 3 cm so the hooves never float.
    /// </summary>
    private Transform3D AnimalTransform(IWorldSnapshot s, in Animal a, double now)
    {
        float t = (float)(now * a.Speed) + a.Phase;
        var offset = new Vector2(MathF.Sin(t), MathF.Sin(t * 0.73f + 1.3f) * 0.8f) * AnimalDrift;
        var velocity = new Vector2(MathF.Cos(t), 0.73f * 0.8f * MathF.Cos(t * 0.73f + 1.3f));
        float yaw = MathF.Atan2(velocity.X, velocity.Y);
        float x = a.Home.X + offset.X, z = a.Home.Y + offset.Y;
        var pos = new Vector3(x, GroundAt(s, x, z) - 0.03f, z);
        return new Transform3D(a.Tilt * new Basis(Vector3.Up, yaw).Scaled(new Vector3(a.Scale, a.Scale, a.Scale)), pos);
    }

    private void UpdateAnimals(IWorldSnapshot s, double now)
    {
        if (_animals is null) return;
        _animals.Begin();
        foreach (var a in _herdAnimals)
        {
            if (AnimalLibrary.Mesh(a.Species) is not { } mesh) continue;
            _animals.Add(0, mesh, AnimalTransform(s, a, now), new Color(a.Shade, a.Shade, a.Shade, a.Phase / MathF.Tau));
        }
        _animals.Commit();
    }

    private float GroundAt(IWorldSnapshot s, float x, float z)
        => Ground?.Invoke(x, z) ?? MathF.Max(s.World.SeaLevel, HeightField.Sample(s.Heights.Span, s.World, x, z));

    /// <summary>Ground normal from central differences over one cell.</summary>
    private Vector3 NormalAt(IWorldSnapshot s, float x, float z)
    {
        float e = s.World.CellSize;
        float hl = GroundAt(s, x - e, z), hr = GroundAt(s, x + e, z), hd = GroundAt(s, x, z - e), hu = GroundAt(s, x, z + e);
        return new Vector3(hl - hr, 2f * e, hd - hu).Normalized();
    }

    /// <summary>How far a 1 m-radius base drops on the downhill side, so upright trunks do not show a gap.</summary>
    private float SlopeDrop(IWorldSnapshot s, Vector3 pos)
    {
        var n = NormalAt(s, pos.X, pos.Z);
        return Math.Clamp(MathF.Sqrt(1f - n.Y * n.Y) / MathF.Max(0.2f, n.Y), 0f, 1.2f) * 0.6f;
    }

    /// <summary>Small props lean with the ground, at most <see cref="MaxTiltDegrees"/>.</summary>
    private Transform3D Tilted(IWorldSnapshot s, Vector3 pos, float yaw, float scale)
    {
        var n = NormalAt(s, pos.X, pos.Z);
        float angle = MathF.Min(MathF.Acos(Math.Clamp(n.Y, -1f, 1f)), Mathf.DegToRad(MaxTiltDegrees));
        var axis = Vector3.Up.Cross(n);
        var tilt = axis.LengthSquared() < 1e-6f ? Basis.Identity : new Basis(axis.Normalized(), angle);
        return new Transform3D(tilt * new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale, scale, scale)), pos);
    }

    public const float MaxTiltDegrees = 15f;

    private static byte BiomeAt(ReadOnlySpan<byte> biomes, WorldInfo info, float x, float z)
    {
        int cx = Math.Clamp((int)MathF.Round(x / info.CellSize), 0, info.Width - 1);
        int cz = Math.Clamp((int)MathF.Round(z / info.CellSize), 0, info.Height - 1);
        int i = cz * info.Width + cx;
        return i < biomes.Length ? biomes[i] : Biome.Grassland;
    }

    private static Transform3D Xf(Vector3 pos, float yaw, float scale)
        => new(new Basis(Vector3.Up, yaw).Scaled(new Vector3(scale, scale, scale)), pos);

    internal static uint Hash(uint seed, uint x, uint z)
    {
        uint h = seed * 0x9E3779B1u ^ x * 0x85EBCA77u;
        h = (h << 13) | (h >> 19);
        h ^= z * 0xC2B2AE3Du;
        h ^= h >> 16;
        h *= 0x7FEB352Du;
        h ^= h >> 15;
        h *= 0x846CA68Bu;
        h ^= h >> 16;
        return h;
    }
}
