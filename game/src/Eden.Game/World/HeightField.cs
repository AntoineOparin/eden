using Eden.Sim.Contracts;

namespace Eden.Game;

/// <summary>
/// Heightmap helpers shared by the renderer and the dev host. Convention (assumed until the contracts say
/// otherwise): row-major, cell (x, z) at index z*Width + x, positioned at world (x*CellSize, h, z*CellSize).
/// </summary>
public static class HeightField
{
    public static float Sample(ReadOnlySpan<float> heights, WorldInfo info, float worldX, float worldZ)
    {
        if (info.Width == 0 || info.Height == 0 || heights.Length < info.Width * info.Height) return info.SeaLevel;
        float fx = Math.Clamp(worldX / info.CellSize, 0, info.Width - 1);
        float fz = Math.Clamp(worldZ / info.CellSize, 0, info.Height - 1);
        int x0 = (int)fx, z0 = (int)fz;
        int x1 = Math.Min(x0 + 1, info.Width - 1), z1 = Math.Min(z0 + 1, info.Height - 1);
        float tx = fx - x0, tz = fz - z0;
        float top = heights[z0 * info.Width + x0] + (heights[z0 * info.Width + x1] - heights[z0 * info.Width + x0]) * tx;
        float bottom = heights[z1 * info.Width + x0] + (heights[z1 * info.Width + x1] - heights[z1 * info.Width + x0]) * tx;
        return top + (bottom - top) * tz;
    }
}
