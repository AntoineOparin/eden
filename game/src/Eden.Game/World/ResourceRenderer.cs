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
    }

    public void Render(IWorldSnapshot s, double nowSeconds)
    {
        Initialise();
        if (_decorFor != s.World || _decorGroundVersion != GroundVersion) BuildDecor(s);
        bool groundMoved = GroundVersion != _batchedGroundVersion;
        if (!groundMoved && (s.Resources.Equals(_drawn) || nowSeconds - _lastRefresh < RefreshSeconds)) return;
        _batchedGroundVersion = GroundVersion;
        _drawn = s.Resources;
        _lastRefresh = nowSeconds;
        Rebatch(s);
    }

    /// <summary>Rebuilds every resource batch from the snapshot. Public for tests.</summary>
    public void Rebatch(IWorldSnapshot s)
    {
        Initialise();
        _plotRotation.Clear();
        foreach (var b in s.Buildings.Span)
            if (b.Kind == "farm-plot") _plotRotation[b.Id] = b.Rotation;

        _trees!.Begin();
        _ground!.Begin();
        var info = s.World;
        var biomes = s.Biomes.Span;
        int chunksX = Math.Max(1, (int)MathF.Ceiling(info.Width * info.CellSize / ChunkSize));
        foreach (ref readonly var r in s.Resources.Span)
        {
            int chunk = (int)(r.Z / ChunkSize) * chunksX + (int)(r.X / ChunkSize);
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

    private void BuildDecor(IWorldSnapshot s)
    {
        _decorFor = s.World;
        _decorGroundVersion = GroundVersion;
        _decor!.Begin();
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
        for (int z = 1; z < info.Height - 1; z++)
        for (int x = 1; x < info.Width - 1; x++)
        {
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
            float y = HeightField.Sample(heights, info, wx, wz);
            if (y <= info.SeaLevel + 0.3f) continue;
            if (Ground is not null && MathF.Abs(Ground(wx, wz) - y) > 0.05f) continue; // yards and terraces stay clear
            int chunk = (int)(wz / DecorChunkSize) * chunksX + (int)(wx / DecorChunkSize);
            float yaw = ((h >> 24) & 0xFF) / 255f * MathF.Tau;
            float scale = 0.75f + ((h >> 19) & 0x1F) / 31f * 0.5f;
            _decor.Add(chunk, mesh, Xf(new Vector3(wx, y - 0.03f, wz), yaw, scale), new Color(1f, 1f, 1f, ((h >> 7) & 0xFF) / 255f));
        }
        _decor.Commit();
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
