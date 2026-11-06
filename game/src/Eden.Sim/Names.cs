namespace Eden.Sim;

/// <summary>Deterministic syllable names from the sim <see cref="Rng"/>.</summary>
public static class Names
{
    private static readonly string[] Starts = ["A", "Be", "Ca", "Da", "E", "Fa", "Ga", "Ha", "I", "Ka", "La", "Ma", "Na", "O", "Pa", "Ra", "Sa", "Ta", "U", "Va", "Ya", "Za", "Bo", "Ki", "Lu", "Mi", "No", "Ru", "Si", "To"];
    private static readonly string[] Middles = ["", "", "la", "ri", "na", "do", "ve", "mi", "ra", "lo", "si", "ten", "bar", "mel"];
    private static readonly string[] FemaleEnds = ["a", "ia", "e", "elle", "ine", "ah", "ira", "wen"];
    private static readonly string[] MaleEnds = ["n", "r", "k", "m", "s", "an", "or", "us", "ek", "o"];

    public static string Next(ref Rng rng, bool female)
    {
        var ends = female ? FemaleEnds : MaleEnds;
        return Starts[rng.NextInt(Starts.Length)] + Middles[rng.NextInt(Middles.Length)] + ends[rng.NextInt(ends.Length)];
    }
}
