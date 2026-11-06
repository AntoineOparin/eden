// Contract: the read-only view of the simulation that Godot renders from.
// Source of truth: docs/specs/contracts/IWorldSnapshot.cs (linked into Eden.Sim).
// The sim publishes a new snapshot per tick batch (double-buffered); Godot only ever reads
// the latest one and never touches sim internals. Everything here must be safe to read from
// the Godot main thread while the sim thread keeps running.

using System;
using System.Collections.Generic;

namespace Eden.Sim.Contracts;

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

/// <summary>Static world description; fixed for the lifetime of a world (terraforming updates the heightmap).</summary>
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

    /// <summary>Heightmap in metres, row-major, length Width*Height.</summary>
    ReadOnlyMemory<float> Heights { get; }

    /// <summary>Biome id per cell, same layout as <see cref="Heights"/>.</summary>
    ReadOnlyMemory<byte> Biomes { get; }

    int Population { get; }
    ReadOnlyMemory<NpcView> Npcs { get; }
    ReadOnlyMemory<BuildingView> Buildings { get; }

    /// <summary>Chronicle entries appended since the previous snapshot. Godot drains them each frame.</summary>
    IReadOnlyList<ChronicleEntry> NewChronicle { get; }

    /// <summary>Inspector data; false if the NPC died or never existed.</summary>
    bool TryGetNpcDetail(int npcId, out NpcDetail detail);
}
