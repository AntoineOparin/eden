namespace Eden.Sim;

/// <summary>The 12 heritable genes, each a float in [0,1]. Index into <see cref="Npcs.Genes"/> with slot * Genetics.Count + gene.</summary>
public enum Gene
{
    Strength,
    Stamina,
    Curiosity,
    Sociability,
    Aggression,
    Fertility,
    Lifespan,
    Empathy,
    Patience,
    Ambition,
    Piety,
    Creativity,
}

/// <summary>Uniform crossover + mutation. Pure functions over spans so tests can drive them directly.</summary>
public static class Genetics
{
    public const int Count = 12;

    public static readonly string[] Names =
    [
        "strength", "stamina", "curiosity", "sociability", "aggression", "fertility",
        "lifespan", "empathy", "patience", "ambition", "piety", "creativity",
    ];

    public static void Randomize(Span<float> genes, ref Rng rng)
    {
        for (int i = 0; i < Count; i++) genes[i] = rng.NextFloat();
    }

    /// <summary>
    /// Each gene comes from one parent chosen uniformly; with probability <paramref name="mutationRate"/>
    /// it is then nudged by a Gaussian of <paramref name="sigma"/> and clamped. Returns the number of mutated genes.
    /// </summary>
    public static int Crossover(ReadOnlySpan<float> mother, ReadOnlySpan<float> father, Span<float> child,
        ref Rng rng, float mutationRate, float sigma)
    {
        int mutated = 0;
        for (int i = 0; i < Count; i++)
        {
            float v = rng.NextFloat() < 0.5f ? mother[i] : father[i];
            if (rng.Chance(mutationRate))
            {
                float nudged = Math.Clamp(v + rng.NextGaussian() * sigma, 0f, 1f);
                // A mutation must change something; push away if the noise or the clamp left it in place.
                if (nudged == v) nudged = v < 0.5f ? Math.Min(1f, v + sigma) : Math.Max(0f, v - sigma);
                v = nudged;
                mutated++;
            }
            child[i] = v;
        }
        return mutated;
    }
}
