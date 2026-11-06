using System.Collections.Concurrent;
using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// Immutable implementation of <see cref="IWorldSnapshot"/>. Arrays passed in are never mutated afterwards.
/// Inspector details are built on the sim thread for "watched" NPCs: the first
/// <see cref="TryGetNpcDetail"/> for a living NPC that is not watched yet returns false and asks the sim to
/// include it from the next snapshot on (one batch later, i.e. the next frame).
/// </summary>
public sealed class WorldSnapshot(
    long tick, int year, int dayOfYear, float hourOfDay, WorldInfo world,
    float[] heights, byte[] biomes, NpcView[] npcs, BuildingView[] buildings,
    IReadOnlyList<ChronicleEntry> newChronicle,
    IReadOnlyDictionary<int, NpcDetail>? details = null,
    int[]? slotVersions = null,
    ConcurrentQueue<int>? detailRequests = null) : IWorldSnapshot
{
    private readonly IReadOnlyDictionary<int, NpcDetail> _details = details ?? new Dictionary<int, NpcDetail>();

    // Per slot: +version if alive, -version if the slot's last occupant died, 0 never used.
    private readonly int[] _slotVersions = slotVersions ?? [];

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

    public bool TryGetNpcDetail(int npcId, out NpcDetail detail)
    {
        if (_details.TryGetValue(npcId, out detail!)) return true;
        if (IsAlive(npcId)) detailRequests?.Enqueue(npcId);
        return false;
    }

    public bool IsAlive(int npcId)
    {
        int slot = npcId & Eden.Sim.Npcs.SlotMask;
        return npcId >= 0 && slot < _slotVersions.Length && _slotVersions[slot] == npcId >> Eden.Sim.Npcs.SlotBits;
    }

    /// <summary>True when the id belonged to an NPC that has since died (as of this snapshot).</summary>
    public bool IsKnownDead(int npcId)
    {
        if (npcId < 0) return false;
        int slot = npcId & Eden.Sim.Npcs.SlotMask, version = npcId >> Eden.Sim.Npcs.SlotBits;
        if (version == 0 || slot >= _slotVersions.Length) return false;
        int v = _slotVersions[slot];
        int latest = Math.Abs(v);
        return version < latest || (version == latest && v < 0);
    }
}
