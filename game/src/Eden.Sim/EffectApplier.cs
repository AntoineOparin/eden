using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// Where unlockBuilding effects go. M3's building registry implements it; until then the world uses
/// <see cref="NoBuildingUnlocks"/>. Called once per NPC when that NPC's proficiency in a concept with an
/// unlockBuilding effect first reaches <see cref="SimConfig.UnlockProficiency"/>. The id passed is the NPC id
/// in M4 (no settlements yet); M3 may map it to the NPC's settlement.
/// </summary>
public interface IBuildingUnlocks
{
    void Unlock(int settlementOrNpcId, string buildingId);
}

public sealed class NoBuildingUnlocks : IBuildingUnlocks
{
    public static readonly NoBuildingUnlocks Instance = new();

    public void Unlock(int settlementOrNpcId, string buildingId) { }
}

/// <summary>
/// The only way knowledge changes the world: the five primitives of <c>concepts.schema.json</c>.
/// <list type="bullet">
/// <item>yieldMod: <see cref="YieldFactor(World,int,ResourceKind)"/> multiplies yields, scaled by proficiency.</item>
/// <item>unlockBuilding: <see cref="IBuildingUnlocks.Unlock"/>, and <see cref="CanBuild"/> for M3 to query.</item>
/// <item>unlockTool: the tool becomes craftable (Craft action), owned tools add their own yield factor.</item>
/// <item>unlockAction: the action enters the NPC's candidate set (<see cref="KnowledgeState.UnlockedActions"/>).</item>
/// <item>belief: an importance-6 memory of kind belief.</item>
/// </list>
/// </summary>
public static class EffectApplier
{
    public const int BeliefImportance = 6;

    /// <summary>Yield multiplier for an NPC harvesting an M1 node kind. Allocation-free.</summary>
    public static float YieldFactor(World w, int slot, ResourceKind kind)
    {
        float f = 1f;
        foreach (var (concept, factor) in w.Graph.YieldsFor(kind))
        {
            float p = w.Proficiency(slot, w.Graph[concept].Id);
            if (p > 0) f *= KnowledgeRules.YieldAt(factor, p);
        }
        return f * ToolFactor(w, slot, w.Graph.ToolsBoosting(kind));
    }

    /// <summary>Yield multiplier for any resource id (derived goods such as grain for M3 farming).</summary>
    public static float YieldFactor(World w, int slot, string resourceId)
    {
        float f = 1f;
        foreach (var (concept, factor) in w.Graph.YieldsFor(resourceId))
        {
            float p = w.Proficiency(slot, w.Graph[concept].Id);
            if (p > 0) f *= KnowledgeRules.YieldAt(factor, p);
        }
        return f * ToolFactor(w, slot, w.Graph.ToolsBoosting(resourceId));
    }

    private static float ToolFactor(World w, int slot, ulong boosting)
    {
        if (slot >= w.KnowledgeState.Owned.Length) return 1f;
        ulong owned = w.KnowledgeState.Owned[slot] & boosting;
        float f = 1f;
        while (owned != 0)
        {
            int t = System.Numerics.BitOperations.TrailingZeroCount(owned);
            owned &= owned - 1;
            f *= w.Graph.Tools[t].Factor;
        }
        return f;
    }

    /// <summary>True when the NPC knows a concept that unlocks <paramref name="buildingId"/> well enough to use it.</summary>
    public static bool CanBuild(World w, int slot, string buildingId)
    {
        foreach (var n in w.Graph.Nodes)
        {
            if (w.Proficiency(slot, n.Id) < w.Config.UnlockProficiency) continue;
            foreach (var e in n.Concept.Effects)
                if (e.Kind == ConceptEffectKind.UnlockBuilding && e.Building == buildingId) return true;
        }
        return false;
    }

    /// <summary>The NPC's proficiency in <paramref name="node"/> crossed the unlock level (up when <paramref name="gained"/>).</summary>
    internal static void OnThresholdCrossed(World w, int slot, ConceptNode node, bool gained)
    {
        RecomputeUnlocks(w, slot);
        if (!gained) return;
        int npcId = w.Npcs.IdOf(slot);
        foreach (var e in node.Concept.Effects)
        {
            switch (e.Kind)
            {
                case ConceptEffectKind.UnlockBuilding:
                    w.BuildingUnlocks.Unlock(npcId, e.Building!);
                    break;
                case ConceptEffectKind.Belief:
                    w.Memories.Add(slot, new Memory(w.Tick, MemoryKind.Belief, [npcId], e.Text!, BeliefImportance), w.Tick);
                    break;
                // yieldMod is read live by YieldFactor; unlockTool and unlockAction live in the masks below.
            }
        }
    }

    /// <summary>Rebuild the NPC's unlocked-action and craftable-tool masks from its knowledge.</summary>
    internal static void RecomputeUnlocks(World w, int slot)
    {
        var ks = w.KnowledgeState;
        ks.EnsureSlots(slot);
        uint actions = 0;
        ulong tools = 0;
        float u = w.Config.UnlockProficiency;
        foreach (var n in w.Graph.Nodes)
        {
            if ((n.ActionMask | n.ToolMask) == 0 || w.Proficiency(slot, n.Id) < u) continue;
            actions |= n.ActionMask;
            tools |= n.ToolMask;
        }
        ks.UnlockedActions[slot] = actions;
        ks.Craftable[slot] = tools;
    }
}
