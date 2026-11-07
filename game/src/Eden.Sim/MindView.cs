using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// Extra read-only facts Eden.Mind needs for the spotlight and prompts that are not part of the
/// <see cref="IWorldSnapshot"/> contract. Implemented by <see cref="WorldSnapshot"/>; consumers must cope with a
/// snapshot that does not implement it (FakeSimHost). Never influences the simulation.
/// </summary>
public interface IMindSnapshot
{
    int TicksPerHour { get; }
    int HoursPerDay { get; }

    /// <summary>Ids of living settlement leaders.</summary>
    IReadOnlyList<int> LeaderIds { get; }

    /// <summary>The last Chronicle entries (up to 32, oldest first), independent of what earlier snapshots drained.</summary>
    IReadOnlyList<ChronicleEntry> RecentChronicle { get; }

    /// <summary>Name of a living settlement, or null.</summary>
    string? SettlementName(int settlementId);

    /// <summary>Tick of the NPC's newest memory with importance ≥ 7 (lightning, deaths, the god...), or -1.</summary>
    long LastDramaTick(int npcId);

    /// <summary>M7: how a living settlement stands with the god, or null (unknown settlement, or a snapshot without it).</summary>
    GodStandingView? GodStanding(int settlementId) => null;
}

/// <summary>
/// M7: the god's reputation in one settlement, for prompts. <paramref name="Stance"/> is "favourable", "fearful",
/// "hostile" or "neutral"; the envied/feared names are the neighbour that feeling points at (null when faint).
/// </summary>
public readonly record struct GodStandingView(
    float Favour, float Awe, float Envy, float Fear, string? EnviedSettlement, string? FearedSettlement, string Stance, string? Religion);

/// <summary>Data behind <see cref="IMindSnapshot"/>, built once per snapshot on the sim thread.</summary>
public sealed record MindExtras(
    int TicksPerHour, int HoursPerDay, int[] Leaders, string?[] SettlementNames, long[] DramaTicks,
    IReadOnlyList<ChronicleEntry> RecentChronicle)
{
    public static readonly MindExtras Empty = new(10, 24, [], [], [], []);

    /// <summary>M7: god standing per settlement id (null entries for dead settlements).</summary>
    public GodStandingView?[] GodStandings { get; init; } = [];

    public GodStandingView? GodStandingOf(int settlementId)
        => settlementId >= 0 && settlementId < GodStandings.Length ? GodStandings[settlementId] : null;
}
