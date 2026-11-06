// Contract: intentions flow from Eden.Mind (LLM output, validated) into Eden.Sim.
// The sim executes them with its own rules; it never blocks on their production.
// Source of truth: docs/specs/contracts/Intention.cs (linked into Eden.Sim).
// Keep ActionKind and ConceptEffectKind in sync with think.schema.json / concepts.schema.json;
// Eden.Sim.Tests/ContractTests.cs fails if they drift.

using System;
using System.Collections.Generic;

namespace Eden.Sim.Contracts;

/// <summary>Fixed vocabulary of goals an NPC can be asked to pursue. Mirrors think.schema.json#/$defs/action.</summary>
public enum ActionKind
{
    Idle,
    Eat,
    Drink,
    Rest,
    Gather,
    Hunt,
    Build,
    Farm,
    Craft,
    Court,
    Teach,
    Learn,
    Trade,
    Raid,
    Flee,
    Pray,
    Explore,
    Talk,
    Give,
    Settle,
    Invent,
}

/// <summary>Where an intention came from. The sim may weigh sources differently (player dialogue first).</summary>
public enum IntentionSource
{
    Rules,
    Llm,
    Player,
}

/// <summary>Mirrors concepts.schema.json#/$defs/effect/type. The only ways knowledge can change the world.</summary>
public enum ConceptEffectKind
{
    YieldMod,
    UnlockBuilding,
    UnlockTool,
    UnlockAction,
    Belief,
}

/// <summary>One effect primitive. Exactly the fields relevant to <see cref="Kind"/> are set; others are null.</summary>
public sealed record ConceptEffect(
    ConceptEffectKind Kind,
    string? Resource = null,
    float? Factor = null,
    string? Building = null,
    string? Tool = null,
    ActionKind? Action = null,
    string? Text = null);

/// <summary>A concept proposed by an NPC (LLM invention) or loaded from data/concepts.json.</summary>
public sealed record ConceptProposal(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> Prerequisites,
    float Difficulty,
    IReadOnlyList<ConceptEffect> Effects);

/// <summary>Base of everything the Mind can push into the sim.</summary>
public abstract record Intention(int NpcId, long IssuedTick, IntentionSource Source);

/// <summary>Pursue an action, optionally at/with a target (resource id, concept id, NPC id as string).</summary>
public sealed record GoalIntention(
    int NpcId, long IssuedTick, IntentionSource Source,
    ActionKind Action, string? Target, string? Reason, int Priority)
    : Intention(NpcId, IssuedTick, Source);

/// <summary>Say something out loud; becomes a memory for every NPC in earshot.</summary>
public sealed record SayIntention(
    int NpcId, long IssuedTick, IntentionSource Source,
    string Text, int? ListenerId)
    : Intention(NpcId, IssuedTick, Source);

/// <summary>Adopt a belief (reflection output). Importance 1..10; 7+ can seed religion.</summary>
public sealed record BeliefIntention(
    int NpcId, long IssuedTick, IntentionSource Source,
    string Text, int Importance)
    : Intention(NpcId, IssuedTick, Source);

/// <summary>Propose a new concept for the live concept graph. Sim validates prerequisites and vocabulary.</summary>
public sealed record InventionIntention(
    int NpcId, long IssuedTick, IntentionSource Source,
    ConceptProposal Concept)
    : Intention(NpcId, IssuedTick, Source);

/// <summary>Implemented by the sim. Thread-safe; callers never block.</summary>
public interface IIntentionSink
{
    /// <summary>Queue an intention. Returns false if the queue is full or the NPC no longer exists.</summary>
    bool TryPush(Intention intention);
}

/// <summary>Validates intentions against the live world before they are applied. Lives in Eden.Sim.</summary>
public interface IIntentionValidator
{
    /// <summary>Returns null when valid, otherwise a short reason used for logging.</summary>
    string? Reject(Intention intention);
}
