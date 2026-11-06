using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>Immutable implementation of <see cref="IWorldSnapshot"/>. Arrays passed in are never mutated afterwards.</summary>
public sealed class WorldSnapshot(
    long tick, int year, int dayOfYear, float hourOfDay, WorldInfo world,
    float[] heights, byte[] biomes, NpcView[] npcs, BuildingView[] buildings,
    IReadOnlyList<ChronicleEntry> newChronicle) : IWorldSnapshot
{
    private readonly Dictionary<int, NpcDetail> _details = new();

    public long Tick { get; } = tick;
    public int Year { get; } = year;
    public int DayOfYear { get; } = dayOfYear;
    public float HourOfDay { get; } = hourOfDay;
    public WorldInfo World { get; } = world;
    public ReadOnlyMemory<float> Heights { get; } = heights;
    public ReadOnlyMemory<byte> Biomes { get; } = biomes;
    public int Population => Npcs.Length;
    public ReadOnlyMemory<NpcView> Npcs { get; } = npcs;
    public ReadOnlyMemory<BuildingView> Buildings { get; } = buildings;
    public IReadOnlyList<ChronicleEntry> NewChronicle { get; } = newChronicle;

    public bool TryGetNpcDetail(int npcId, out NpcDetail detail) => _details.TryGetValue(npcId, out detail!);
}
