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
}

/// <summary>Data behind <see cref="IMindSnapshot"/>, built once per snapshot on the sim thread.</summary>
public sealed record MindExtras(
    int TicksPerHour, int HoursPerDay, int[] Leaders, string?[] SettlementNames, long[] DramaTicks,
    IReadOnlyList<ChronicleEntry> RecentChronicle)
{
    public static readonly MindExtras Empty = new(10, 24, [], [], [], []);
}
