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
/// A harvestable thing standing in the world, for drawing only (A1). <see cref="Kind"/> is a resource id from
/// <c>game/data/resources.json</c>: node kinds <c>tree</c>, <c>stone</c>, <c>ore</c>, <c>berry</c>, <c>game</c>, plus
/// <c>grain</c> for the crop on a finished farm plot. <see cref="Amount"/> is 0..1 of the node's maximum (for
/// <c>grain</c>: crop growth, 0 fallow, 1 ripe). <see cref="Variant"/> is a stable per-node byte the renderer uses to
/// pick a model and jitter; it carries no game meaning. Ids are stable for the lifetime of the node.
/// </summary>
public readonly record struct ResourceView(
    int Id,
    string Kind,
    float X, float Y, float Z,
    float Amount,
    byte Variant);

/// <summary>How a settlement regards the god (M7). Byte values match the sim's own stance enum.</summary>
public enum GodStance : byte { Neutral, Favourable, Fearful, Hostile }

/// <summary>
/// One living settlement with the god's standing there (M7 UI). <see cref="X"/>/<see cref="Z"/> is the centre in
/// world metres. <see cref="FactionId"/> and <see cref="LeaderId"/> are -1 when there is none. <see cref="Favour"/> is
/// −1..1, <see cref="Awe"/>, <see cref="Envy"/> and <see cref="Fear"/> 0..1; <see cref="EnviedId"/> /
/// <see cref="FearedId"/> are the settlement that envy or fear points at, or -1 when the feeling is faint.
/// <see cref="ReligionName"/> is the faith its faction follows, or null.
/// <see cref="Food"/> (C1) is the food the settlement holds right now, in the sim's food units (one unit restores
/// <c>SimConfig.FoodPerUnit</c> = 0.35 of a person's food need; an adult eats about 1.6 a day): its shared stock (the
/// pile and storehouses) plus what its members carry (households keep no separate larder).
/// <see cref="TerritoryRadius"/> (C1) is the radius in metres of the land the sim treats as the settlement's own
/// (<c>SettlementRadius × TerritoryFactor</c>, the circle trespass and grievance use). Both are defaulted so older
/// positional constructors keep compiling.
/// </summary>
public readonly record struct SettlementView(
    int Id,
    string Name,
    float X, float Z,
    int Members,
    int FactionId,
    int LeaderId,
    float Favour, float Awe, float Envy, float Fear,
    int EnviedId, int FearedId,
    GodStance Stance,
    string? ReligionName,
    float Food = 0f,
    float TerritoryRadius = 0f);

/// <summary>
/// What one settlement knows and owns (C1), built on request like <see cref="NpcDetail"/>: see
/// <see cref="IWorldSnapshot.TryGetSettlementDetail"/>. <see cref="Members"/> and <see cref="Adults"/> (adults and
/// elders) are counted when the detail was built. <see cref="Concepts"/> lists every concept at least one member knows
/// (proficiency above 0), most holders first. <see cref="Buildings"/> counts the settlement's finished buildings by
/// kind, most first. <see cref="Food"/> is the same quantity as <see cref="SettlementView.Food"/>;
/// <see cref="FoodPerDay"/> is what the members eat in a day at the sim's rates (food need decay over food per unit,
/// children and infants at their lower rates, awake all day: an upper bound), in the same units.
/// </summary>
public sealed record SettlementDetail(
    int Id,
    string Name,
    int Members,
    int Adults,
    IReadOnlyList<SettlementConceptView> Concepts,
    IReadOnlyList<BuildingCount> Buildings,
    float Food,
    float FoodPerDay);

/// <summary>
/// One concept in a <see cref="SettlementDetail"/>: how many members know it, the best proficiency among them (0..1)
/// and whether a written record holding it lies on the settlement's land (within its territory radius, or in one of
/// its buildings).
/// </summary>
public readonly record struct SettlementConceptView(string ConceptId, int Holders, float BestProficiency, bool HasRecord);

/// <summary>Number of finished buildings of one kind (a <c>game/data/buildings.json</c> id).</summary>
public readonly record struct BuildingCount(string Kind, int Count);

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

/// <summary>
/// One memory. <see cref="Generation"/> (M7 UI) is 0 for a first-hand memory and n for a story heard through n
/// retellings (gossip; each retelling may distort it). Defaulted so older constructors keep compiling.
/// </summary>
public readonly record struct MemoryView(long Tick, string Kind, string Text, int Importance, int Generation = 0);

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

    /// <summary>Resource nodes and farm-plot crops, for the renderer (A1). Order is stable between snapshots.</summary>
    ReadOnlyMemory<ResourceView> Resources { get; }

    /// <summary>Living settlements with the god's standing (M7 UI), ordered by id. Rebuilt with every snapshot.</summary>
    ReadOnlyMemory<SettlementView> Settlements { get; }

    /// <summary>
    /// Chronicle entries appended since the previous snapshot. Kept for compatibility: the sim may publish several
    /// snapshots between two frames, so readers that must not miss entries use <see cref="ChronicleSince"/>.
    /// </summary>
    IReadOnlyList<ChronicleEntry> NewChronicle { get; }

    /// <summary>
    /// C1: every Chronicle entry the sim still holds with <c>Tick &gt; tick</c>, oldest first (newest last). The sim
    /// keeps a bounded history (2,048 entries in C1), saved with the world, so after a load this includes the history
    /// from before the session. Entries can be added at the tick of the snapshot after it was published (god commands
    /// run between ticks), so a reader that wants everything passes its last seen tick − 1 and drops duplicates by
    /// tick and text. Allocates the returned list; call it once per frame at most.
    /// </summary>
    IReadOnlyList<ChronicleEntry> ChronicleSince(long tick);

    /// <summary>
    /// Inspector data. Details are built on request, not for every NPC every snapshot: the first call
    /// for a living NPC may return false and asks the sim to watch that NPC, and snapshots from the next
    /// batch on return true. Returns false for dead or unknown ids. Callers poll each frame while a
    /// panel is open; the sim keeps a bounded watch list (32 in M1).
    /// </summary>
    bool TryGetNpcDetail(int npcId, out NpcDetail detail);

    /// <summary>
    /// C1: settlement data for the settlement panel and the knowledge overlay, with the same "request now, present on
    /// a later snapshot" semantics as <see cref="TryGetNpcDetail"/>: the first call for a living settlement that is
    /// not watched returns false and asks the sim to watch it (as does <c>WatchSettlementCommand</c>), and snapshots
    /// from the next batch on return true. The sim keeps a bounded watch list (8 in C1, least recently requested
    /// dropped first) and refreshes a watched detail at most once per in-game hour (one refresh per snapshot).
    /// Returns false for abandoned or unknown ids.
    /// </summary>
    bool TryGetSettlementDetail(int id, out SettlementDetail detail);
}
