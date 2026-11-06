namespace Eden.Sim;

/// <summary>2D simplex noise (Gustavson) with a permutation table shuffled by <see cref="Rng"/>. Deterministic.</summary>
public sealed class SimplexNoise
{
    private static readonly float F2 = 0.5f * (MathF.Sqrt(3f) - 1f);
    private static readonly float G2 = (3f - MathF.Sqrt(3f)) / 6f;

    private static readonly sbyte[] GradX = [1, -1, 1, -1, 1, -1, 0, 0];
    private static readonly sbyte[] GradY = [0, 0, 1, 1, -1, -1, 1, -1];

    private readonly byte[] _perm = new byte[512];

    public SimplexNoise(ref Rng rng)
    {
        var p = new byte[256];
        for (int i = 0; i < 256; i++) p[i] = (byte)i;
        for (int i = 255; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (p[i], p[j]) = (p[j], p[i]);
        }
        for (int i = 0; i < 512; i++) _perm[i] = p[i & 255];
    }

    /// <summary>Noise in roughly [-1, 1].</summary>
    public float Sample(float x, float y)
    {
        float s = (x + y) * F2;
        int i = FastFloor(x + s);
        int j = FastFloor(y + s);
        float t = (i + j) * G2;
        float x0 = x - (i - t);
        float y0 = y - (j - t);
        int i1 = x0 > y0 ? 1 : 0;
        int j1 = 1 - i1;
        float x1 = x0 - i1 + G2, y1 = y0 - j1 + G2;
        float x2 = x0 - 1f + 2f * G2, y2 = y0 - 1f + 2f * G2;
        int ii = i & 255, jj = j & 255;
        float n = Corner(_perm[ii + _perm[jj]], x0, y0)
                + Corner(_perm[ii + i1 + _perm[jj + j1]], x1, y1)
                + Corner(_perm[ii + 1 + _perm[jj + 1]], x2, y2);
        return 70f * n;
    }

    /// <summary>Fractal Brownian motion: <paramref name="octaves"/> layers, each double frequency, half amplitude.</summary>
    public float Fbm(float x, float y, int octaves)
    {
        float sum = 0, amp = 1, norm = 0, freq = 1;
        for (int o = 0; o < octaves; o++)
        {
            sum += Sample(x * freq, y * freq) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / norm;
    }

    private static float Corner(int hash, float x, float y)
    {
        float t = 0.5f - x * x - y * y;
        if (t < 0) return 0;
        int g = hash & 7;
        t *= t;
        return t * t * (GradX[g] * x + GradY[g] * y);
    }

    private static int FastFloor(float v) => v >= 0 ? (int)v : (int)v - 1;
}
