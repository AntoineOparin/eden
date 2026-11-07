using System.Text;
using System.Text.RegularExpressions;
using Eden.Mind.Contracts;
using Eden.Sim;
using Eden.Sim.Contracts;

namespace Eden.Mind;

/// <summary>
/// Builds the request DTOs from what the snapshot exposes (NpcView, NpcDetail, IMindSnapshot). Everything is
/// clipped to the schema bounds in common.schema.json so the Worker never answers 400.
/// </summary>
public static partial class PromptContext
{
    /// <summary>Cached constant prefix ("world rules") of every prompt.</summary>
    public const string WorldRules =
        "You are a person living on a small island in a young world, in an age before history. " +
        "People gather, hunt, farm, build, trade and sometimes fight; knowledge spreads by talk and teaching. " +
        "A god watches the island and sometimes acts: lightning, rain, blessings, a voice from the sky. " +
        "You know only what you have seen, been told or remember. Speak plainly, as someone of your time. " +
        "Never mention games, AI, models or JSON in what you say. Answer only in the JSON shape requested.";

    public const string ReflectionSituation = "Reflect on your day and state 1–3 beliefs";
    public const string GodName = "The Voice";

    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,47}$")]
    private static partial Regex Identifier();

    private static readonly Dictionary<string, (string High, string Low)> TraitWords = new(StringComparer.Ordinal)
    {
        ["strength"] = ("strong", "slight of build"),
        ["stamina"] = ("tireless", "quick to tire"),
        ["curiosity"] = ("curious", "incurious"),
        ["sociability"] = ("sociable", "solitary"),
        ["aggression"] = ("quick to anger", "gentle"),
        ["empathy"] = ("kind", "cold"),
        ["patience"] = ("patient", "impatient"),
        ["ambition"] = ("ambitious", "content with little"),
        ["piety"] = ("devout", "doubtful of gods"),
        ["creativity"] = ("inventive", "set in their ways"),
    };

    public static NpcContext Npc(NpcDetail d, NpcView? view, IWorldSnapshot snap)
    {
        var extras = snap as IMindSnapshot;
        string? settlement = view is { } v ? extras?.SettlementName(v.SettlementId) : null;
        bool leader = view is { } lv && extras is not null && extras.LeaderIds.Contains(lv.Id);

        var traits = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (k, val) in d.Traits) { if (traits.Count >= 32) break; traits[k] = Clamp01(val); }
        var needs = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (k, val) in d.Needs) { if (needs.Count >= 16) break; needs[k] = Clamp01(val); }
        var knowledge = d.Knowledge.Where(kv => kv.Value >= 0.3f && Identifier().IsMatch(kv.Key))
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(64).Select(kv => kv.Key).ToList();
        var goals = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.CurrentGoal)) goals.Add(Clip(d.CurrentGoal, 280));

        string location = settlement is not null
            ? $"in {settlement}" + (leader ? ", where you are the leader" : "")
            : view is { } p ? $"in the wilds of the island ({p.X:0}, {p.Z:0})" : "somewhere on the island";
        string persona = Persona(d, settlement, leader, view);
        if (OnTheirMind(d, snap) is { } shaken) persona = Clip(persona + " " + shaken, 4000);
        if (GodLine(view, snap) is { } god) persona = Clip(persona + " " + god, 4000);
        return new NpcContext(d.Id, Clip(string.IsNullOrWhiteSpace(d.Name) ? $"Npc {d.Id}" : d.Name, 64),
            Math.Clamp(d.AgeYears, 0, 200), persona, traits, needs, knowledge, goals, Clip(location, 280));
    }

    /// <summary>M7: Awe at or above which the reflection prompt asks for a belief about the god.</summary>
    public const float ReflectAwe = 0.3f;

    /// <summary>
    /// M7: one line on how the NPC's settlement stands with the god ("Your people believe the sky favours them."),
    /// plus the envy or fear felt toward a more favoured neighbour and the faith they keep. Null outside a settlement
    /// or when the snapshot has no standing (FakeSimHost).
    /// </summary>
    public static string? GodLine(NpcView? view, IWorldSnapshot snap)
    {
        if (view is not { } v || (snap as IMindSnapshot)?.GodStanding(v.SettlementId) is not { } g) return null;
        var sb = new StringBuilder(g.Stance switch
        {
            "favourable" => "Your people believe the sky favours them.",
            "fearful" => "Your people fear the sky.",
            "hostile" => "Your people believe the sky has turned against them.",
            _ => "Your people do not know what the sky wants of them.",
        });
        if (g.Religion is { } r) sb.Append(" They keep ").Append(r).Append('.');
        if (g.EnviedSettlement is { } e && g.Envy >= 0.2f && g.Envy >= g.Fear)
            sb.Append(" They envy ").Append(e).Append(", whom the sky favours more.");
        else if (g.FearedSettlement is { } f && g.Fear >= 0.2f)
            sb.Append(" They fear ").Append(f).Append(", whom the sky favours more.");
        return sb.ToString();
    }

    /// <summary>M7: the reflection situation; when the settlement's Awe is high it asks for a belief about the god.</summary>
    public static string ReflectionPrompt(NpcView? view, IWorldSnapshot snap)
    {
        var g = view is { } v ? (snap as IMindSnapshot)?.GodStanding(v.SettlementId) : null;
        return g is { } s && s.Awe >= ReflectAwe
            ? ReflectionSituation + ". One of them should be about the sky and what it wants of your people"
            : ReflectionSituation;
    }

    /// <summary>Hours a dramatic memory (importance ≥ 8) stays "on their mind" in the persona.</summary>
    public const int ShakenHours = 6;

    /// <summary>
    /// The NPC's state of mind: the newest memory of importance ≥ 8 from the last <see cref="ShakenHours"/> in-game
    /// hours, or null. /chat has no situation field, so without this a strike a minute ago is just one of twelve
    /// memory lines and the model often answers past it (seen in the M5 manual runs).
    /// </summary>
    public static string? OnTheirMind(NpcDetail d, IWorldSnapshot snap)
    {
        var extras = snap as IMindSnapshot;
        int tph = extras?.TicksPerHour ?? 10, hpd = extras?.HoursPerDay ?? 24;
        MemoryView? best = null;
        foreach (var m in d.Memories)
            if (m.Importance >= 8 && m.Kind != "summary" && snap.Tick - m.Tick <= (long)ShakenHours * tph && (best is null || m.Tick >= best.Value.Tick))
                best = m;
        return best is { } b ? $"Still shaken by what happened ({When(snap.Tick - b.Tick, tph, hpd).ToLowerInvariant()}): {b.Text}" : null;
    }

    public static string Persona(NpcDetail d, string? settlement, bool leader, NpcView? view)
    {
        var sb = new StringBuilder();
        sb.Append(d.Name).Append(" is ");
        if (view is { } v) sb.Append(v.Sex == Sex.Female ? "a woman" : "a man");
        else sb.Append("a person");
        sb.Append(" of ").Append(d.AgeYears).Append(" years");
        if (leader && settlement is not null) sb.Append(", the leader of ").Append(settlement);
        else if (settlement is not null) sb.Append(" of ").Append(settlement);
        sb.Append('.');
        var words = new List<string>();
        foreach (var (k, val) in d.Traits.OrderByDescending(t => Math.Abs(t.Value - 0.5f)).ThenBy(t => t.Key, StringComparer.Ordinal))
        {
            if (!TraitWords.TryGetValue(k, out var w)) continue;
            if (val >= 0.65f) words.Add(w.High);
            else if (val <= 0.35f) words.Add(w.Low);
            if (words.Count >= 4) break;
        }
        if (words.Count > 0) sb.Append(" They are ").Append(string.Join(", ", words)).Append('.');
        var friend = d.Relationships.Count > 0 && d.Relationships[0].Affinity > 0.3f ? d.Relationships[0].OtherName : null;
        if (friend is not null) sb.Append(" Closest to ").Append(friend).Append('.');
        return Clip(sb.ToString(), 4000);
    }

    /// <summary>
    /// Memory lines for the prompt. Each text gets a "when" prefix ("Just now:", "3 days ago:") so the model can tell a
    /// strike a minute ago from an old tale; the raw tick alone means nothing to it.
    /// </summary>
    public static IReadOnlyList<MemoryLine> Lines(IReadOnlyList<MemoryView> memories, long nowTick = -1, int ticksPerHour = 10, int hoursPerDay = 24)
    {
        var lines = new MemoryLine[memories.Count];
        for (int i = 0; i < lines.Length; i++)
        {
            var m = memories[i];
            string text = nowTick < 0 ? m.Text : $"{When(nowTick - m.Tick, ticksPerHour, hoursPerDay)}: {m.Text}";
            lines[i] = new MemoryLine(Math.Max(0, m.Tick), Clip(text, 280), Math.Clamp(m.Importance, 1, 10));
        }
        return lines;
    }

    public static string When(long ageTicks, int ticksPerHour, int hoursPerDay)
    {
        double hours = Math.Max(0, ageTicks) / (double)Math.Max(1, ticksPerHour);
        if (hours < 1) return "Just now";
        if (hours < 6) return "Earlier today";
        if (hours < hoursPerDay) return "Today";
        int days = (int)(hours / hoursPerDay);
        return days == 1 ? "Yesterday" : days < 30 ? $"{days} days ago" : "Long ago";
    }

    /// <summary>Time of day, the most pressing need, and the last 10 Chronicle entries about this NPC or their settlement.</summary>
    public static string Situation(NpcDetail d, NpcView? view, IWorldSnapshot snap)
    {
        var extras = snap as IMindSnapshot;
        var sb = new StringBuilder();
        float h = snap.HourOfDay;
        string part = h < 5 ? "night" : h < 9 ? "morning" : h < 12 ? "late morning" : h < 14 ? "midday" : h < 18 ? "afternoon" : h < 21 ? "dusk" : "night";
        sb.Append($"It is {part} on day {snap.DayOfYear + 1} of year {snap.Year + 1}.");
        if (d.Needs.Count > 0)
        {
            var low = d.Needs.OrderBy(n => n.Value).First();
            if (low.Value < 0.4f) sb.Append($" Your {low.Key} is low.");
        }
        if (!string.IsNullOrWhiteSpace(d.CurrentGoal)) sb.Append($" You are busy with: {d.CurrentGoal}.");
        if (extras is not null)
        {
            string? settlement = view is { } v ? extras.SettlementName(v.SettlementId) : null;
            var recent = new List<string>();
            var chron = extras.RecentChronicle;
            for (int i = chron.Count - 1; i >= 0 && recent.Count < 10; i--)
            {
                var e = chron[i];
                bool aboutMe = e.Subjects.Contains(d.Id);
                bool aboutHome = settlement is not null && e.Text.Contains(settlement, StringComparison.Ordinal);
                if (aboutMe || aboutHome) recent.Add(e.Text);
            }
            if (recent.Count > 0)
            {
                recent.Reverse();
                sb.Append(" Lately: ").Append(string.Join(" ", recent));
            }
        }
        return Clip(sb.ToString(), 4000);
    }

    /// <summary>Invention needs curiosity ≥ 0.7 and at least two concepts mastered ≥ 0.8.</summary>
    public static bool AllowInvention(NpcDetail d)
        => d.Traits.TryGetValue("curiosity", out float c) && c >= 0.7f && d.Knowledge.Count(k => k.Value >= 0.8f) >= 2;

    public static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    private static float Clamp01(float v) => float.IsNaN(v) ? 0f : Math.Clamp(v, 0f, 1f);
}
