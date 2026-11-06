using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class PathfindingTests
{
    /// <summary>48×48 cells of flat grassland with a deep-water wall at column 24; <paramref name="gap"/> leaves rows 40+ open.</summary>
    private static Terrain Walled(bool gap)
    {
        var t = new Terrain(48, 48, 2f, 0f);
        for (int z = 0; z < 48; z++)
        for (int x = 0; x < 48; x++)
            t.Heights[z * 48 + x] = x is 23 or 24 or 25 && (!gap || z < 40) ? -10f : 5f;
        t.RecomputeDerived();
        return t;
    }

    [Fact]
    public void Path_goes_around_a_wall_of_deep_water()
    {
        var t = Walled(gap: true);
        var p = new Pathfinder(t);
        float sx = 10 * 2f, sz = 10 * 2f, tx = 38 * 2f, tz = 10 * 2f;
        Assert.False(t.LineWalkable(sx, sz, tx, tz));
        Assert.True(p.SameRegion(sx, sz, tx, tz));

        Span<float> xs = stackalloc float[24], zs = stackalloc float[24];
        int n = p.Find(sx, sz, tx, tz, xs, zs, 24, out float length, out bool truncated);
        Assert.True(n > 0);
        Assert.False(truncated);
        Assert.True(length > Math.Abs(tx - sx), "path cannot be shorter than the straight line");
        float ax = sx, az = sz;
        for (int i = 0; i < n; i++)
        {
            Assert.True(t.LineWalkable(ax, az, xs[i], zs[i]), $"segment {i} crosses water");
            (ax, az) = (xs[i], zs[i]);
        }
        Assert.True(t.LineWalkable(ax, az, tx, tz), "last waypoint does not see the target");
        Assert.Contains(zs[..n].ToArray(), z => z >= 40 * 2f - 2f); // it used the gap
    }

    [Fact]
    public void Separate_landmasses_have_no_path()
    {
        var t = Walled(gap: false);
        var p = new Pathfinder(t);
        Assert.False(p.SameRegion(20, 20, 76, 20));
        Span<float> xs = stackalloc float[8], zs = stackalloc float[8];
        Assert.Equal(-1, p.Find(20, 20, 76, 20, xs, zs, 8, out _, out _));
    }

    [Fact]
    public void Same_inputs_give_the_same_path()
    {
        var t = Walled(gap: true);
        float[] Run()
        {
            var p = new Pathfinder(t);
            var xs = new float[24];
            var zs = new float[24];
            int n = p.Find(20, 20, 76, 20, xs, zs, 24, out _, out _);
            return xs[..n].Concat(zs[..n]).ToArray();
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void An_npc_walks_around_a_lake_the_god_dug_between_it_and_its_target()
    {
        // Find a start and a target 50 m away over open land, with room around the midpoint for a lake.
        var (w, x, z) = Villages.World(5);
        float tx = 0, tz = 0, mx = 0, mz = 0;
        bool found = false;
        for (int tries = 0; tries < 12 && !found; tries++)
        {
            if (tries > 0) (x, z) = Villages.SpotAway(w, w.Npcs.X[0], w.Npcs.Z[0], 20f + 15f * tries, 200f);
            for (int a = 0; a < 72 && !found; a++)
            {
                float ang = a * MathF.Tau / 72;
                tx = x + MathF.Cos(ang) * 50f; tz = z + MathF.Sin(ang) * 50f;
                mx = (x + tx) / 2; mz = (z + tz) / 2;
                found = w.Terrain.LineWalkable(x, z, tx, tz) && Ring(w.Terrain, mx, mz, 26f);
            }
        }
        Assert.True(found, "no open ground near the genesis spot");

        w.Apply(new TerraformCommand(mx, mz, 14f, -20f));
        Assert.False(w.Terrain.LineWalkable(x, z, tx, tz), "the lake does not block the straight line");
        Assert.True(w.Pathing.SameRegion(x, z, tx, tz));
        w.Npcs.X[0] = x; w.Npcs.Z[0] = z;
        w.Npcs.Phase[0] = ActionPhase.Done;
        Assert.True(w.WalkForTests(0, tx, tz, 400), "npc did not reach the far shore");
        Assert.True(MathF.Sqrt(MathF.Pow(w.Npcs.X[0] - tx, 2) + MathF.Pow(w.Npcs.Z[0] - tz, 2)) < 3f);
    }

    /// <summary>Walkable land on a ring around a point (so a lake there can be walked around).</summary>
    private static bool Ring(Terrain t, float cx, float cz, float r)
    {
        for (int i = 0; i < 48; i++)
        {
            float a = i * MathF.Tau / 48;
            float px = cx + MathF.Cos(a) * r, pz = cz + MathF.Sin(a) * r;
            if (!t.IsWalkable(px, pz) || t.IsWater(t.CellIndex(px, pz))) return false;
        }
        return true;
    }

    [Fact]
    public void Resource_search_judges_reachability_by_pathfinder_region()
    {
        var w = new World(new SimConfig { Seed = 5 });
        var r = w.Resources;
        Assert.NotNull(r.Region);
        Assert.Same(w.Pathing.Region, r.Region);
    }
}
