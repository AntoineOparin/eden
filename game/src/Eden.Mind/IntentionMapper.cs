using System.Text.Json;
using Eden.Mind.Contracts;
using Eden.Sim.Contracts;

namespace Eden.Mind;

public enum ThinkMode { Thought, Reflection }

/// <summary>Intentions for the sim, or the reason the whole response was rejected.</summary>
public sealed record MappedResponse(IReadOnlyList<Intention> Intentions, string? Rejected)
{
    public static MappedResponse Reject(string reason) => new([], reason);
}

/// <summary>
/// Turns provider output into <see cref="Intention"/>s. LLM output is untrusted: the response is re-checked
/// against think.schema.json / chat.schema.json bounds here (the C# twin of the Worker's Zod schemas) and
/// rejected whole on any violation. The sim's IntentionValidator then checks each intention against the world.
/// </summary>
public static class IntentionMapper
{
    public const int GoalPriority = 2;
    public const int ReflectionMinImportance = 7;
    /// <summary>Dialogue memories are at least this important, so the existing gossip (importance ≥ 6) spreads them.</summary>
    public const int DialogueMinImportance = 6;
    public const int MaxSayLength = 280;

    public static readonly string[] Moods = ["calm", "happy", "afraid", "angry", "sad", "awed", "suspicious"];

    /// <summary>Parses think.schema.json#/$defs/response; false on malformed JSON, unknown fields or actions.</summary>
    public static bool TryParseThink(string json, out ThinkResponse? response, out string? error)
        => TryParse(json, out response, out error);

    public static bool TryParseChat(string json, out ChatResponse? response, out string? error)
        => TryParse(json, out response, out error);

    private static bool TryParse<T>(string json, out T? value, out string? error) where T : class
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(json, BrainJson.Options);
            error = value is null ? "null response" : null;
            return value is not null;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException or FormatException)
        {
            value = null;
            error = e.Message;
            return false;
        }
    }

    /// <summary>Schema bounds of think.schema.json#/$defs/response. Null when valid.</summary>
    public static string? Check(ThinkResponse? r)
    {
        if (r is null) return "missing response";
        if (r.Plan is null) return "plan is required";
        if (r.Plan.Count > 5) return "plan has more than 5 goals";
        foreach (var g in r.Plan)
        {
            if (g is null) return "null goal";
            if (!Enum.IsDefined(g.Action)) return "unknown action";
            if (g.Target is { Length: > 64 }) return "goal target too long";
            if (g.Reason is { Length: > 200 }) return "goal reason too long";
        }
        if (r.Say is { Length: > MaxSayLength }) return "say too long";
        return CheckBelief(r.Belief, "belief");
    }

    /// <summary>Schema bounds of chat.schema.json#/$defs/response. Null when valid.</summary>
    public static string? Check(ChatResponse? r)
    {
        if (r is null) return "missing response";
        if (string.IsNullOrEmpty(r.Reply) || r.Reply.Length > 600) return "reply empty or too long";
        if (r.Mood is not null && Array.IndexOf(Moods, r.Mood) < 0) return $"unknown mood '{r.Mood}'";
        return CheckBelief(r.Memory, "memory");
    }

    private static string? CheckBelief(ProposedBelief? b, string what)
    {
        if (b is null) return null;
        if (string.IsNullOrEmpty(b.Text) || b.Text.Length > 280) return $"{what} text empty or too long";
        return b.Importance is < 1 or > 10 ? $"{what} importance out of range" : null;
    }

    /// <summary>
    /// Thought: goals (priority 2), optional say, belief, invention (only when allowed). Reflection: only the
    /// belief, raised to importance ≥ 7. Goals beyond <paramref name="maxGoals"/> are dropped.
    /// </summary>
    public static MappedResponse FromThink(ThinkResponse? r, int npcId, long tick, ThinkMode mode,
        bool allowInvention = false, int maxGoals = 3)
    {
        if (Check(r) is { } bad) return MappedResponse.Reject(bad);
        var list = new List<Intention>();
        if (mode == ThinkMode.Reflection)
        {
            if (r!.Belief is { } rb)
                list.Add(new BeliefIntention(npcId, tick, IntentionSource.Llm, rb.Text, Math.Max(ReflectionMinImportance, rb.Importance)));
            return new MappedResponse(list, null);
        }
        int n = 0;
        foreach (var g in r!.Plan)
        {
            if (n++ >= maxGoals) break;
            list.Add(new GoalIntention(npcId, tick, IntentionSource.Llm, g.Action,
                string.IsNullOrWhiteSpace(g.Target) ? null : g.Target.Trim(), g.Reason, GoalPriority));
        }
        if (!string.IsNullOrWhiteSpace(r.Say)) list.Add(new SayIntention(npcId, tick, IntentionSource.Llm, r.Say.Trim(), null));
        if (r.Belief is { } b) list.Add(new BeliefIntention(npcId, tick, IntentionSource.Llm, b.Text, b.Importance));
        if (r.Invention is { } inv)
        {
            if (!allowInvention) return MappedResponse.Reject("invention not allowed for this npc");
            list.Add(new InventionIntention(npcId, tick, IntentionSource.Llm, inv));
        }
        return new MappedResponse(list, null);
    }

    /// <summary>Dialogue: the reply is spoken aloud (neighbours hear it), the memory becomes a belief.</summary>
    public static MappedResponse FromChat(ChatResponse? r, int npcId, long tick)
    {
        if (Check(r) is { } bad) return MappedResponse.Reject(bad);
        var list = new List<Intention> { new SayIntention(npcId, tick, IntentionSource.Llm, Clip(r!.Reply.Trim(), MaxSayLength), null) };
        if (r.Memory is { } m)
            list.Add(new BeliefIntention(npcId, tick, IntentionSource.Llm, m.Text, Math.Max(DialogueMinImportance, m.Importance)));
        return new MappedResponse(list, null);
    }

    /// <summary>True when a belief is about the god (religion seed for M7).</summary>
    public static bool MentionsGod(string text)
    {
        foreach (var w in GodWords)
            if (text.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static readonly string[] GodWords = ["god", "voice", "sky", "heaven", "divine", "spirit", "the one above"];

    public static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 3)].TrimEnd() + "...";
}
