using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>
/// Where a building sits on uneven ground (spec 45, "Placement on uneven ground"): the terrain is sampled at the
/// footprint's four corners and centre; the building stands at the highest sample (<see cref="Top"/>) and a
/// plinth reaches down to the lowest sample minus a margin (<see cref="Bottom"/>). The render heightmap is flattened
/// to <see cref="Top"/> under the footprint plus an apron (<see cref="TerrainBuilder.ApplyFoundations"/>).
/// Pure maths on the snapshot heights; nothing here touches the sim.
/// </summary>
public readonly record struct Foundation(int BuildingId, Vector3 Centre, float Rotation, Vector2 HalfExtents, float Top, float Bottom)
{
    /// <summary>Extra depth below the lowest sample so the plinth always meets the ground.</summary>
    public const float Margin = 0.35f;

    /// <summary>Below this height difference no plinth is drawn (the flattened terrain hides the seam).</summary>
    public const float PlinthThreshold = 0.12f;

    public float Height => Top - Bottom;

    /// <summary>The five footprint samples (corners, then centre) in world XZ.</summary>
    public static Vector2[] Samples(Vector3 centre, float rotation, Vector2 halfExtents)
    {
        var basis = new Basis(Vector3.Up, rotation);
        var result = new Vector2[5];
        int k = 0;
        foreach (var (sx, sz) in new[] { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) })
        {
            var p = centre + basis * new Vector3(sx * halfExtents.X, 0, sz * halfExtents.Y);
            result[k++] = new Vector2(p.X, p.Z);
        }
        result[4] = new Vector2(centre.X, centre.Z);
        return result;
    }

    /// <summary>
    /// Foundation for a building whose mesh spans <paramref name="meshBounds"/> (local, before rotation). Heights are
    /// sampled from <paramref name="heights"/> (the sim's, not the flattened render copy) and never below sea level.
    /// </summary>
    public static Foundation Compute(ReadOnlySpan<float> heights, WorldInfo info, in BuildingView b, Aabb meshBounds)
    {
        var half = new Vector2(
            MathF.Max(MathF.Abs(meshBounds.Position.X), MathF.Abs(meshBounds.End.X)),
            MathF.Max(MathF.Abs(meshBounds.Position.Z), MathF.Abs(meshBounds.End.Z)));
        half = new Vector2(MathF.Max(half.X, 0.5f), MathF.Max(half.Y, 0.5f));
        var centre = new Vector3(b.X, b.Y, b.Z);
        float top = float.MinValue, bottom = float.MaxValue;
        foreach (var s in Samples(centre, b.Rotation, half))
        {
            float h = MathF.Max(info.SeaLevel, HeightField.Sample(heights, info, s.X, s.Y));
            top = MathF.Max(top, h);
            bottom = MathF.Min(bottom, h);
        }
        // Docks reach over water: keep their deck at the shore height and do not raise them out of the sea.
        if (b.Kind == "dock") top = MathF.Max(info.SeaLevel + 0.3f, MathF.Min(top, b.Y + 0.3f));
        // A few kit pieces dip a little below their origin; lift the mesh so its lowest vertex sits on the top.
        float lift = b.Kind == "dock" ? 0f : MathF.Max(0f, -meshBounds.Position.Y); // dock posts reach into the water on purpose
        return new Foundation(b.Id, new Vector3(b.X, top + lift, b.Z), b.Rotation, half, top, bottom - Margin);
    }

    /// <summary>True when the world point (x, z) lies inside the footprint grown by <paramref name="apron"/> metres.</summary>
    public bool Contains(float x, float z, float apron)
    {
        var local = new Basis(Vector3.Up, -Rotation) * new Vector3(x - Centre.X, 0, z - Centre.Z);
        return MathF.Abs(local.X) <= HalfExtents.X + apron && MathF.Abs(local.Z) <= HalfExtents.Y + apron;
    }

    /// <summary>A2: distance (metres) from the world point (x, z) to the footprint rectangle; 0 inside it.</summary>
    public float DistanceOutside(float x, float z)
    {
        var local = new Basis(Vector3.Up, -Rotation) * new Vector3(x - Centre.X, 0, z - Centre.Z);
        float dx = MathF.Max(0f, MathF.Abs(local.X) - HalfExtents.X), dz = MathF.Max(0f, MathF.Abs(local.Z) - HalfExtents.Y);
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
