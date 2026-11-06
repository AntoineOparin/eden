using System.Text.RegularExpressions;
using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// Checks an intention against the live world before it is applied (sim thread only). LLM output is
/// untrusted: anything outside the fixed vocabulary, out of range, or about a dead NPC is rejected.
/// </summary>
public sealed partial class IntentionValidator(World world) : IIntentionValidator
{
    public const int MaxTextLength = 280;

    /// <summary>Words a goal may name instead of an NPC id or concept id.</summary>
    private static readonly Dictionary<string, ResourceKind?> ResourceTargets = new(StringComparer.Ordinal)
    {
        ["food"] = ResourceKind.Berry, ["berry"] = ResourceKind.Berry, ["berries"] = ResourceKind.Berry,
        ["game"] = ResourceKind.Game, ["animal"] = ResourceKind.Game, ["animals"] = ResourceKind.Game,
        ["tree"] = ResourceKind.Tree, ["trees"] = ResourceKind.Tree, ["wood"] = ResourceKind.Tree,
        ["stone"] = ResourceKind.Stone, ["ore"] = ResourceKind.Ore,
        ["water"] = null,
    };

    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,47}\z")]
    private static partial Regex Identifier();

    public string? Reject(Intention intention)
    {
        if (intention is null) return "null intention";
        if (!world.Npcs.TryGetSlot(intention.NpcId, out int slot)) return "no such npc";
        if (!Enum.IsDefined(intention.Source)) return "unknown source";
        return intention switch
        {
            GoalIntention g => RejectGoal(slot, g),
            SayIntention s => RejectSay(slot, s),
            BeliefIntention b => b.Importance is < 1 or > 10 ? "belief importance out of range"
                : BadText(b.Text) ? "belief text empty or too long" : null,
            InventionIntention i => RejectInvention(slot, i.Concept),
            _ => "unknown intention type",
        };
    }

    private string? RejectGoal(int slot, GoalIntention g)
    {
        if (!Enum.IsDefined(g.Action)) return "unknown action";
        if (g.Priority is < 0 or > 10) return "priority out of range";
        if (!world.IsActionUnlocked(slot, g.Action)) return $"action {g.Action} not unlocked for this npc";
        if (g.Reason is { Length: > MaxTextLength }) return "reason too long";
        if (string.IsNullOrEmpty(g.Target)) return null;

        float range = world.Config.SearchRadius * 2;
        float x = world.Npcs.X[slot], z = world.Npcs.Z[slot];
        if (int.TryParse(g.Target, out int targetId))
        {
            if (!world.Npcs.TryGetSlot(targetId, out int t)) return "target npc does not exist";
            if (t == slot) return "target is self";
            float dx = world.Npcs.X[t] - x, dz = world.Npcs.Z[t] - z;
            return dx * dx + dz * dz > range * range ? "target npc out of range" : null;
        }
        if (ResourceTargets.TryGetValue(g.Target, out var kind))
        {
            if (kind is null) return world.Terrain.NearestWater[world.Terrain.CellIndex(x, z)] >= 0 ? null : "no water anywhere";
            return world.Resources.FindNearest(kind.Value, x, z, range) >= 0 ? null : $"no {g.Target} in range";
        }
        if (world.Concepts.ContainsKey(g.Target)) return null;
        return "unknown target";
    }

    private string? RejectSay(int slot, SayIntention s)
    {
        if (BadText(s.Text)) return "speech empty or too long";
        if (s.ListenerId is not { } l) return null;
        if (!world.Npcs.TryGetSlot(l, out int t)) return "listener does not exist";
        float dx = world.Npcs.X[t] - world.Npcs.X[slot], dz = world.Npcs.Z[t] - world.Npcs.Z[slot];
        float r = world.Config.PerceptionRadius;
        return dx * dx + dz * dz > r * r ? "listener out of earshot" : null;
    }

    /// <summary>The concepts.schema.json rules, re-checked in C#, plus "prerequisites mastered" (≥ 0.6).</summary>
    private string? RejectInvention(int slot, ConceptProposal? c)
    {
        if (c is null) return "missing concept";
        if (c.Id is null || !Identifier().IsMatch(c.Id)) return "concept id is not an identifier";
        if (world.Concepts.ContainsKey(c.Id)) return "concept already exists";
        if (string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 48) return "concept name empty or too long";
        if (string.IsNullOrWhiteSpace(c.Description) || c.Description.Length > 400) return "concept description empty or too long";
        if (float.IsNaN(c.Difficulty) || c.Difficulty is < 0 or > 1) return "difficulty out of range";
        if (c.Prerequisites is null || c.Prerequisites.Count > 6) return "too many prerequisites";
        for (int i = 0; i < c.Prerequisites.Count; i++)
        {
            var p = c.Prerequisites[i];
            if (p is null || !Identifier().IsMatch(p)) return "prerequisite is not an identifier";
            for (int j = 0; j < i; j++) if (c.Prerequisites[j] == p) return "duplicate prerequisite";
            if (!world.Concepts.ContainsKey(p)) return $"unknown prerequisite {p}";
            if (world.Proficiency(slot, p) < 0.6f) return $"prerequisite {p} not mastered";
        }
        if (c.Effects is null || c.Effects.Count is < 1 or > 6) return "effects must number 1..6";
        foreach (var e in c.Effects)
        {
            if (e is null) return "null effect";
            string? bad = e.Kind switch
            {
                ConceptEffectKind.YieldMod => !IsId(e.Resource) || e.Factor is not { } f || float.IsNaN(f) || f < 0.5f || f > 3f
                    || e.Building is not null || e.Tool is not null || e.Action is not null || e.Text is not null ? "bad yieldMod" : null,
                ConceptEffectKind.UnlockBuilding => !IsId(e.Building) || e.Resource is not null || e.Factor is not null
                    || e.Tool is not null || e.Action is not null || e.Text is not null ? "bad unlockBuilding" : null,
                ConceptEffectKind.UnlockTool => !IsId(e.Tool) || e.Resource is not null || e.Factor is not null
                    || e.Building is not null || e.Action is not null || e.Text is not null ? "bad unlockTool" : null,
                ConceptEffectKind.UnlockAction => e.Action is not { } a || !Enum.IsDefined(a) || e.Resource is not null
                    || e.Factor is not null || e.Building is not null || e.Tool is not null || e.Text is not null ? "bad unlockAction" : null,
                ConceptEffectKind.Belief => BadText(e.Text) || e.Resource is not null || e.Factor is not null
                    || e.Building is not null || e.Tool is not null || e.Action is not null ? "bad belief" : null,
                _ => "unknown effect type",
            };
            if (bad is not null) return bad;
        }
        return null;
    }

    private static bool IsId(string? s) => s is not null && Identifier().IsMatch(s);

    private static bool BadText(string? s) => string.IsNullOrWhiteSpace(s) || s.Length > MaxTextLength;
}
