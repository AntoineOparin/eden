// Contract: the read-only view of the simulation that Godot renders from.
// Source of truth: docs/specs/contracts/IWorldSnapshot.cs (linked into Eden.Sim).
// The sim publishes a new snapshot per tick batch (double-buffered); Godot only ever reads
// the latest one and never touches sim internals. Everything here must be safe to read from
// the Godot main thread while the sim thread keeps running.

using System;
using System.Collections.Generic;

namespace Eden.Sim.Contracts;

/// <summary>
/// Meaning of each byte in <see cref="IWorldSnapshot.Biomes"/>. Eden.Sim assigns them from height and
/// moisture; Eden.Game maps them to splat layers. Add new values at the end only (saves store the byte).
/// </summary>
public enum BiomeId : byte
{
    Ocean = 0,
    Beach = 1,
    Grassland = 2,
    Forest = 3,
    Mountain = 4,
    Snow = 5,
}

public enum Sex : byte { Female, Male }

public enum LifeStage : byte { Infant, Child, Adult, Elder }

/// <summary>Coarse animation/render state. Fine-grained state machines stay inside the sim.</summary>
public enum NpcPose : byte { Idle, Walking, Working, Fighting, Sleeping, Dead }

/// <summary>Hot per-NPC data, laid out for MultiMesh updates. Positions are in world metres, Y up.</summary>
public readonly record struct NpcView(
    int Id,
    float X, float Y, float Z,
    float Heading,
    Sex Sex,
    LifeStage Stage,
    NpcPose Pose,
    byte Health,
    int SettlementId,
    bool IsThinking,
    bool IsPossessed);

public readonly record struct BuildingView(
    int Id,
    string Kind,
    float X, float Y, float Z,
    float Rotation,
    int SettlementId,
    float Progress);

/// <summary>
/// Static world description; fixed for the lifetime of a world (terraforming updates the heightmap).
/// Grid layout, shared by <see cref="IWorldSnapshot.Heights"/> and <see cref="IWorldSnapshot.Biomes"/>:
/// row-major, cell (x, z) lives at index <c>z * Width + x</c>, the map origin is world (0, 0) and
/// cell (x, z) sits at world position <c>(x * CellSize, height, z * CellSize)</c>, Y up, metres.
/// Heights are absolute metres; cells at or below <see cref="SeaLevel"/> are under water.
/// </summary>
public sealed record WorldInfo(
    int Seed,
    int Width,
    int Height,
    float CellSize,
    float SeaLevel,
    int ChunkSize);

/// <summary>Rich per-NPC data for the inspector. Built on demand, allocation is fine here.</summary>
public sealed record NpcDetail(
    int Id,
    string Name,
    int AgeYears,
    IReadOnlyDictionary<string, float> Needs,
    IReadOnlyDictionary<string, float> Traits,
    IReadOnlyDictionary<string, float> Knowledge,
    IReadOnlyList<RelationshipView> Relationships,
    IReadOnlyList<MemoryView> Memories,
    string? CurrentGoal,
    string? CurrentThought);

public readonly record struct RelationshipView(int OtherId, string OtherName, float Affinity, float Trust, float Debt);

public readonly record struct MemoryView(long Tick, string Kind, string Text, int Importance);

public sealed record ChronicleEntry(long Tick, string Kind, string Text, int Importance, IReadOnlyList<int> Subjects);

/// <summary>
/// Immutable snapshot of the simulation at <see cref="Tick"/>.
/// Returned by <see cref="ISimHost.Latest"/>; a new instance replaces the old one, instances never mutate.
/// </summary>
public interface IWorldSnapshot
{
    long Tick { get; }
    int Year { get; }
    int DayOfYear { get; }
    float HourOfDay { get; }
    WorldInfo World { get; }

    /// <summary>Heightmap in metres, length Width*Height, layout as documented on <see cref="WorldInfo"/>.</summary>
    ReadOnlyMemory<float> Heights { get; }

    /// <summary><see cref="BiomeId"/> per cell as a byte, same layout as <see cref="Heights"/>.</summary>
    ReadOnlyMemory<byte> Biomes { get; }

    int Population { get; }
    ReadOnlyMemory<NpcView> Npcs { get; }
    ReadOnlyMemory<BuildingView> Buildings { get; }

    /// <summary>Chronicle entries appended since the previous snapshot. Godot drains them each frame.</summary>
    IReadOnlyList<ChronicleEntry> NewChronicle { get; }

    /// <summary>
    /// Inspector data. Details are built on request, not for every NPC every snapshot: the first call
    /// for a living NPC may return false and asks the sim to watch that NPC, and snapshots from the next
    /// batch on return true. Returns false for dead or unknown ids. Callers poll each frame while a
    /// panel is open; the sim keeps a bounded watch list (32 in M1).
    /// </summary>
    bool TryGetNpcDetail(int npcId, out NpcDetail detail);
}
