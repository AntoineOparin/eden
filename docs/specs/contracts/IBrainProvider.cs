// Contract: how Eden.Mind talks to any LLM backend. The default implementation proxies the
// Cloudflare Worker (brain/); direct providers call Anthropic / OpenAI-compatible APIs with the
// player's own key. All DTOs mirror think.schema.json, chat.schema.json and embed.schema.json.
// Source of truth: docs/specs/contracts/IBrainProvider.cs (linked into Eden.Mind).

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Eden.Sim.Contracts;

namespace Eden.Mind.Contracts;

// ---------- shared pieces ----------

public sealed record MemoryLine(
    [property: JsonPropertyName("tick")] long Tick,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("importance")] int Importance);

/// <summary>Who the NPC is right now. Numbers are 0..1; the Worker turns them into prose.</summary>
public sealed record NpcContext(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ageYears")] int AgeYears,
    [property: JsonPropertyName("persona")] string Persona,
    [property: JsonPropertyName("traits")] IReadOnlyDictionary<string, float> Traits,
    [property: JsonPropertyName("needs")] IReadOnlyDictionary<string, float> Needs,
    [property: JsonPropertyName("knowledge")] IReadOnlyList<string> Knowledge,
    [property: JsonPropertyName("currentGoals")] IReadOnlyList<string> CurrentGoals,
    [property: JsonPropertyName("location")] string Location);

public sealed record BrainUsage(
    [property: JsonPropertyName("inputTokens")] int InputTokens,
    [property: JsonPropertyName("outputTokens")] int OutputTokens,
    [property: JsonPropertyName("neurons")] double Neurons,
    [property: JsonPropertyName("model")] string Model);

// ---------- /think ----------

public sealed record ThinkRequest(
    [property: JsonPropertyName("npc")] NpcContext Npc,
    [property: JsonPropertyName("memories")] IReadOnlyList<MemoryLine> Memories,
    [property: JsonPropertyName("worldRules")] string WorldRules,
    [property: JsonPropertyName("situation")] string Situation,
    [property: JsonPropertyName("maxGoals")] int MaxGoals = 3,
    [property: JsonPropertyName("allowInvention")] bool AllowInvention = false);

public sealed record PlannedGoal(
    [property: JsonPropertyName("action")] ActionKind Action,
    [property: JsonPropertyName("target")] string? Target,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record ProposedBelief(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("importance")] int Importance);

public sealed record ThinkResponse(
    [property: JsonPropertyName("plan")] IReadOnlyList<PlannedGoal> Plan,
    [property: JsonPropertyName("say")] string? Say,
    [property: JsonPropertyName("belief")] ProposedBelief? Belief,
    [property: JsonPropertyName("invention")] ConceptProposal? Invention);

public sealed record ThinkResult(ThinkResponse Response, BrainUsage Usage);

// ---------- /chat ----------

public enum Speaker { God, Npc, Possessed }

public sealed record ChatTurn(
    [property: JsonPropertyName("speaker")] Speaker Speaker,
    [property: JsonPropertyName("text")] string Text);

public sealed record ChatRequest(
    [property: JsonPropertyName("npc")] NpcContext Npc,
    [property: JsonPropertyName("memories")] IReadOnlyList<MemoryLine> Memories,
    [property: JsonPropertyName("worldRules")] string WorldRules,
    [property: JsonPropertyName("speaker")] Speaker Speaker,
    [property: JsonPropertyName("speakerName")] string SpeakerName,
    [property: JsonPropertyName("history")] IReadOnlyList<ChatTurn> History,
    [property: JsonPropertyName("message")] string Message);

public sealed record ChatResponse(
    [property: JsonPropertyName("reply")] string Reply,
    [property: JsonPropertyName("memory")] ProposedBelief? Memory,
    [property: JsonPropertyName("mood")] string? Mood);

public sealed record ChatResult(ChatResponse Response, BrainUsage Usage);

// ---------- /embed ----------

public sealed record EmbedRequest(
    [property: JsonPropertyName("texts")] IReadOnlyList<string> Texts);

public sealed record EmbedResponse(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("dimensions")] int Dimensions,
    [property: JsonPropertyName("vectors")] IReadOnlyList<float[]> Vectors);

public sealed record EmbedResult(EmbedResponse Response, BrainUsage Usage);

// ---------- provider ----------

public enum BrainFailure { Network, Auth, Budget, InvalidOutput, Unsupported, Cancelled }

public sealed class BrainException(BrainFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public BrainFailure Failure { get; } = failure;
}

/// <summary>
/// One LLM backend. Implementations must be safe to call concurrently and must never throw
/// anything but <see cref="BrainException"/> or <see cref="OperationCanceledException"/>.
/// Callers (the mind pool) apply timeouts and budgets; providers do not retry on their own
/// except for the single schema-repair retry.
/// </summary>
public interface IBrainProvider
{
    /// <summary>Stable id used in settings and logs: "worker", "anthropic", "openai-compatible", "none".</summary>
    string Id { get; }

    /// <summary>True when several NPC thoughts may be sent in one call (the mind pool batches then).</summary>
    bool SupportsBatching { get; }

    Task<ThinkResult> ThinkAsync(ThinkRequest request, CancellationToken ct);

    Task<ChatResult> ChatAsync(ChatRequest request, CancellationToken ct);

    Task<EmbedResult> EmbedAsync(EmbedRequest request, CancellationToken ct);
}

/// <summary>Settings-panel model for a provider. Keys are stored encrypted in user:// by Eden.Game.</summary>
public sealed record BrainSettings(
    string ProviderId,
    string BaseUrl,
    string Model,
    string? ApiKey,
    int MaxThinkingNpcs = 15,
    double DailyNeuronBudget = 9000,
    int Concurrency = 3);
