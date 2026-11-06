namespace Eden.Sim;

/// <summary>Every tunable the simulation reads. Defaults are the ones in docs/specs/10-sim-core.md.</summary>
public sealed record SimConfig
{
    public int Seed { get; init; } = 1;
    public int TicksPerHour { get; init; } = 10;
    public int HoursPerDay { get; init; } = 24;
    public int DaysPerYear { get; init; } = 10;
    public int WorldWidth { get; init; } = 256;
    public int WorldHeight { get; init; } = 256;
    public float CellSize { get; init; } = 2f;
    public float SeaLevel { get; init; } = 0f;
    public int ChunkSize { get; init; } = 32;
    public int StartingNpcs { get; init; } = 2;
    public int MemoryCap { get; init; } = 500;
    public float PerceptionRadius { get; init; } = 24f;
    public int AdultAgeYears { get; init; } = 14;
    public int BaseLifespanYears { get; init; } = 70;

    /// <summary>Real-time ticks per second at 1× (so 1 in-game hour = 0.5 s, 1 year = 2 min).</summary>
    public int TicksPerRealSecond { get; init; } = 20;

    public int TicksPerDay => TicksPerHour * HoursPerDay;
    public int TicksPerYear => TicksPerDay * DaysPerYear;
}
