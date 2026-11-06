namespace Eden.Sim;

/// <summary>Needs, each in [0,1] where 1 is fully satisfied. Index into <see cref="Npcs.Needs"/> with slot * Needs.Count + need.</summary>
public enum Need
{
    Food,
    Water,
    Rest,
    Warmth,
    Safety,
    Social,
    Purpose,
}

public static class Needs
{
    public const int Count = 7;

    public static readonly string[] Names = ["food", "water", "rest", "warmth", "safety", "social", "purpose"];
}
