using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Eden.Mind.Contracts;

namespace Eden.Mind.Providers;

/// <summary>
/// System/user prompts for the direct providers. A line-for-line port of brain/src/prompts.ts so a thought
/// reads the same whether it goes through the Worker or straight to Anthropic / an OpenAI-compatible server.
/// </summary>
public static partial class PromptBuilder
{
    public static readonly string[] Actions =
        ["idle", "eat", "drink", "rest", "gather", "hunt", "build", "farm", "craft", "court", "teach", "learn", "trade", "raid", "flee", "pray", "explore", "talk", "give", "settle", "invent"];

    private static string Pct(float v) => $"{Math.Round(v * 100)}%";

    private static string DescribeNpc(NpcContext npc)
    {
        var traits = string.Join(", ", npc.Traits.Select(kv => $"{kv.Key} {Pct(kv.Value)}"));
        var needs = string.Join(", ", npc.Needs.Select(kv => $"{kv.Key} {Pct(kv.Value)}"));
        var lines = new[]
        {
            $"You are {npc.Name}, {npc.AgeYears} years old. {npc.Persona}",
            traits.Length > 0 ? $"Traits: {traits}." : "",
            needs.Length > 0 ? $"Needs (100% = fully satisfied): {needs}." : "",
            npc.Knowledge.Count > 0 ? $"You know: {string.Join(", ", npc.Knowledge)}." : "You know almost nothing yet.",
            npc.CurrentGoals.Count > 0 ? $"Current goals: {string.Join("; ", npc.CurrentGoals)}." : "",
            $"You are at {npc.Location}.",
        };
        return string.Join("\n", lines.Where(l => l.Length > 0));
    }

    private static string DescribeMemories(IReadOnlyList<MemoryLine> memories)
        => memories.Count == 0
            ? "You remember nothing notable."
            : "What you remember (most relevant first):\n" + string.Join("\n", memories.Select(m => $"- [importance {m.Importance}] {m.Text}"));

    public static (string System, string User) Think(ThinkRequest req)
    {
        var system = string.Join("\n",
            req.WorldRules,
            "You decide what this person does next. Think like them, not like an assistant.",
            $"Allowed actions: {string.Join(", ", Actions)}.",
            "Respond with ONLY a JSON object of this exact shape:",
            """{"plan":[{"action":"<one allowed action>","target":"<optional short target or null>","reason":"<short reason or null>"}],"say":"<one short line spoken aloud, or null>","belief":{"text":"<a belief formed now>","importance":1-10} or null}""",
            $"plan has at most {req.MaxGoals} entries. No markdown, no commentary, no keys other than plan, say, belief.");
        var user = string.Join("\n\n", DescribeNpc(req.Npc), DescribeMemories(req.Memories), $"Right now: {req.Situation}");
        return (system, user);
    }

    public static (string System, string User) Chat(ChatRequest req)
    {
        string who = req.Speaker == Speaker.God
            ? $"A voice from the sky that calls itself \"{req.SpeakerName}\" speaks to you. You may be awed, afraid or doubtful."
            : $"{req.SpeakerName} speaks to you face to face.";
        var system = string.Join("\n\n",
            req.WorldRules,
            DescribeNpc(req.Npc),
            DescribeMemories(req.Memories),
            who,
            "Reply in character in one to three sentences.",
            $"Respond with ONLY a JSON object: {{\"reply\":\"<what you say>\",\"memory\":{{\"text\":\"<what you will remember of this>\",\"importance\":1-10}} or null,\"mood\":\"<one of {string.Join("|", IntentionMapper.Moods)}> or null\"}}");
        var history = req.History.Select(t => $"{(t.Speaker == Speaker.Npc ? req.Npc.Name : req.SpeakerName)}: {t.Text}");
        var user = string.Join("\n", history.Append($"{req.SpeakerName}: {req.Message}"));
        return (system, user);
    }

    /// <summary>brain/src/json-repair.ts repairPrompt: the single repair turn.</summary>
    public static string Repair(string previous, string problem)
        => string.Join("\n",
            "Your previous answer was not valid. Return ONLY a corrected JSON object, no prose, no markdown.",
            "Problems:",
            $"- (root): {problem}",
            "Previous answer:",
            previous.Length > 2000 ? previous[..2000] : previous);

    [GeneratedRegex(@"<think>[\s\S]*?</think>", RegexOptions.IgnoreCase)]
    private static partial Regex ThinkBlock();

    [GeneratedRegex(@"^[\s\S]*?</think>", RegexOptions.IgnoreCase)]
    private static partial Regex ThinkTail();

    [GeneratedRegex(@"^```(?:json)?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex FenceStart();

    [GeneratedRegex(@"\s*```$")]
    private static partial Regex FenceEnd();

    /// <summary>brain/src/json-repair.ts extractJson: strips &lt;think&gt; blocks and fences, then the outermost object.</summary>
    public static string? ExtractJson(string text)
    {
        var s = ThinkBlock().Replace(text, "").Trim();
        s = ThinkTail().Replace(s, "").Trim();
        s = FenceEnd().Replace(FenceStart().Replace(s, ""), "");
        if (IsJson(s)) return s;
        int start = s.IndexOf('{'), end = s.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        var inner = s[start..(end + 1)];
        return IsJson(inner) ? inner : null;
    }

    private static bool IsJson(string s)
    {
        try { return JsonNode.Parse(s) is JsonObject; }
        catch (System.Text.Json.JsonException) { return false; }
    }

    /// <summary>The JSON Schema handed to the model for /think (brain/src/schemas.ts THINK_OUTPUT_JSON_SCHEMA).</summary>
    public static JsonObject ThinkOutputSchema(bool forAnthropic) => Clean(JsonNode.Parse("""
        {"type":"object","additionalProperties":false,"required":["plan"],"properties":{
          "plan":{"type":"array","maxItems":5,"items":{"type":"object","additionalProperties":false,"required":["action"],"properties":{
            "action":{"type":"string","enum":__ACTIONS__},
            "target":{"type":["string","null"],"maxLength":64},
            "reason":{"type":["string","null"],"maxLength":200}}}},
          "say":{"type":["string","null"],"maxLength":280},
          "belief":{"type":["object","null"],"additionalProperties":false,"required":["text","importance"],"properties":{
            "text":{"type":"string","maxLength":280},"importance":{"type":"integer","minimum":1,"maximum":10}}}}}
        """.Replace("__ACTIONS__", System.Text.Json.JsonSerializer.Serialize(Actions)))!.AsObject(), forAnthropic);

    /// <summary>The JSON Schema handed to the model for /chat (CHAT_OUTPUT_JSON_SCHEMA).</summary>
    public static JsonObject ChatOutputSchema(bool forAnthropic) => Clean(JsonNode.Parse("""
        {"type":"object","additionalProperties":false,"required":["reply"],"properties":{
          "reply":{"type":"string","minLength":1,"maxLength":600},
          "memory":{"type":["object","null"],"additionalProperties":false,"required":["text","importance"],"properties":{
            "text":{"type":"string","maxLength":280},"importance":{"type":"integer","minimum":1,"maximum":10}}},
          "mood":{"type":["string","null"],"enum":["calm","happy","afraid","angry","sad","awed","suspicious",null]}}}
        """.Replace("__ACTIONS__", System.Text.Json.JsonSerializer.Serialize(Actions)))!.AsObject(), forAnthropic);

    private static readonly HashSet<string> UnsupportedByAnthropic =
        ["minLength", "maxLength", "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf", "maxItems", "uniqueItems"];

    /// <summary>
    /// Anthropic structured outputs reject numeric/length bounds (brain/src/providers/anthropic.ts
    /// toStructuredOutputSchema). The bounds still hold: every answer is re-checked by IntentionMapper.
    /// </summary>
    private static JsonObject Clean(JsonObject schema, bool forAnthropic)
    {
        if (!forAnthropic) return schema;
        Strip(schema);
        return schema;

        static void Strip(JsonNode? node)
        {
            if (node is JsonArray arr) { foreach (var n in arr) Strip(n); return; }
            if (node is not JsonObject obj) return;
            foreach (var key in obj.Select(kv => kv.Key).ToList())
            {
                if (UnsupportedByAnthropic.Contains(key)) { obj.Remove(key); continue; }
                if (key is "enum" or "const") continue;
                if (key == "properties" && obj[key] is JsonObject props) { foreach (var p in props) Strip(p.Value); continue; }
                Strip(obj[key]);
            }
        }
    }
}
