namespace Eden.Game;

/// <summary>
/// Biome ids as the renderer interprets <c>IWorldSnapshot.Biomes</c>. The contracts do not define
/// biome ids yet; until they do, this table is the client's assumption and the FakeSimHost uses it.
/// </summary>
public static class Biome
{
    public const byte Ocean = 0;
    public const byte Beach = 1;
    public const byte Grassland = 2;
    public const byte Forest = 3;
    public const byte Mountain = 4;
    public const byte Snow = 5;

    /// <summary>Splat layer weights (sand, grass, forest, rock) packed into a vertex colour. Snow is rock + height whitening in the shader.</summary>
    public static (float Sand, float Grass, float Forest, float Rock) SplatWeights(byte biome) => biome switch
    {
        Ocean or Beach => (1, 0, 0, 0),
        Grassland => (0, 1, 0, 0),
        Forest => (0, 0, 1, 0),
        Mountain or Snow => (0, 0, 0, 1),
        _ => (0, 1, 0, 0),
    };
}
