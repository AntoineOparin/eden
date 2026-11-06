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

    // ---- M1 life-cycle and balance tunables -------------------------------------------------

    /// <summary>Genes shift lifespan by up to ± this many years around <see cref="BaseLifespanYears"/>.</summary>
    public int LifespanGeneRangeYears { get; init; } = 15;

    /// <summary>Probability per gene that a child's value is mutated instead of plainly inherited.</summary>
    public float MutationRate { get; init; } = 0.03f;

    /// <summary>Std-dev of the Gaussian nudge applied to a mutated gene (then clamped to [0,1]).</summary>
    public float MutationSigma { get; init; } = 0.2f;

    public float PregnancyYears { get; init; } = 0.75f;

    /// <summary>Minimum time after a birth before the mother can conceive again.</summary>
    public float PostpartumYears { get; init; } = 0.6f;

    public int FemaleFertileUntilYears { get; init; } = 45;

    /// <summary>Base chance that a courtship between partners conceives (scaled by fertility genes).</summary>
    public float ConceptionChance { get; init; } = 0.25f;

    /// <summary>Need decay per in-game day, in need units (needs are 0 = desperate .. 1 = satisfied).</summary>
    public float FoodDecayPerDay { get; init; } = 0.55f;
    public float WaterDecayPerDay { get; init; } = 0.9f;
    public float RestDecayPerDay { get; init; } = 0.7f;
    public float WarmthDecayPerDay { get; init; } = 0.5f;
    public float SocialDecayPerDay { get; init; } = 0.45f;
    public float PurposeDecayPerDay { get; init; } = 0.25f;

    /// <summary>Ticks an NPC survives with food at zero. Default: 4 in-game days.</summary>
    public int StarvationTicks { get; init; } = 4 * 24 * 10;

    /// <summary>Ticks an NPC survives with water at zero. Default: 2 in-game days.</summary>
    public int DehydrationTicks { get; init; } = 2 * 24 * 10;

    /// <summary>Food need restored by eating one unit of food.</summary>
    public float FoodPerUnit { get; init; } = 0.35f;

    public float MaxCarriedFood { get; init; } = 8f;

    /// <summary>Walking speed in metres per tick before gene and terrain modifiers.</summary>
    public float WalkSpeed { get; init; } = 2f;

    /// <summary>NPCs re-evaluate their action at least this often (staggered by slot).</summary>
    public int DecisionIntervalTicks { get; init; } = 10;

    /// <summary>A competing action must beat the current one's score by this margin to interrupt it.</summary>
    public float Hysteresis { get; init; } = 0.1f;

    /// <summary>Background (accident) death probability per year by life stage.</summary>
    public float InfantDeathPerYear { get; init; } = 0.04f;
    public float ChildDeathPerYear { get; init; } = 0.01f;
    public float AdultDeathPerYear { get; init; } = 0.006f;

    /// <summary>Probability the mother dies giving birth.</summary>
    public float ChildbirthDeathChance { get; init; } = 0.015f;

    /// <summary>Multiplier on the number of resource nodes world gen places. 0 = barren island.</summary>
    public float ResourceDensity { get; init; } = 1f;

    /// <summary>How far (m) an NPC looks for food, game and mates when deciding.</summary>
    public float SearchRadius { get; init; } = 96f;

    public int TicksPerDay => TicksPerHour * HoursPerDay;
    public int TicksPerYear => TicksPerDay * DaysPerYear;
}
