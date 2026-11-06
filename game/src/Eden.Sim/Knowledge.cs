using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// Pure knowledge arithmetic shared by honing, transmission and discovery. Proficiency is 0..1 per concept;
/// a concept is "known" above 0, "usable" (its effects unlock) at <see cref="SimConfig.UnlockProficiency"/>.
/// </summary>
public static class KnowledgeRules
{
    /// <summary>Honing: practice adds less the better you are. Δ = rate × (1 − p)².</summary>
    public static float Hone(float p, float rate)
    {
        float room = 1f - p;
        return Math.Min(1f, p + rate * room * room);
    }

    /// <summary>Transmission moves the learner toward the source's level, never past it.</summary>
    public static float Transfer(float learner, float source, float rate)
        => source <= learner ? learner : Math.Min(source, learner + rate * (source - learner));

    /// <summary>Weight of one eligible concept in a discovery roll: easy concepts come first.</summary>
    public static float DiscoveryWeight(float difficulty) => Math.Max(0.02f, 1f - difficulty);

    /// <summary>yieldMod contribution: full factor at proficiency 1, none at 0.</summary>
    public static float YieldAt(float factor, float proficiency) => 1f + (factor - 1f) * proficiency;
}

/// <summary>
/// Per-NPC state derived from (or attached to) the knowledge dictionaries: the actions and tools each NPC's
/// concepts unlock, the tools it owns, and the targets picked while scoring. Struct-of-arrays
/// indexed by NPC slot, grown on demand, never allocated on the tick path.
/// </summary>
public sealed class KnowledgeState
{
    /// <summary>Bit per <see cref="ActionKind"/> unlocked by concepts this NPC knows at ≥ the unlock level.</summary>
    public uint[] UnlockedActions = new uint[64];

    /// <summary>Bit per tool index this NPC can craft.</summary>
    public ulong[] Craftable = new ulong[64];

    /// <summary>Bit per tool index this NPC carries. Saved.</summary>
    public ulong[] Owned = new ulong[64];

    /// <summary>Concepts this NPC could discover at its last discovery roll (drives the Invent action).</summary>
    public short[] Candidates = new short[64];

    /// <summary>Per-slot targets chosen while scoring Teach/Learn/Craft, used when the action begins.</summary>
    public int[] TargetSlot = new int[64];
    public int[] TargetConcept = new int[64];
    public int[] TargetTool = new int[64];
    public int[] TargetNode = new int[64];

    public void EnsureSlots(int slot)
    {
        if (slot < UnlockedActions.Length) return;
        int n = UnlockedActions.Length;
        while (n <= slot) n *= 2;
        Array.Resize(ref UnlockedActions, n);
        Array.Resize(ref Craftable, n);
        Array.Resize(ref Owned, n);
        Array.Resize(ref Candidates, n);
        Array.Resize(ref TargetSlot, n);
        Array.Resize(ref TargetConcept, n);
        Array.Resize(ref TargetTool, n);
        Array.Resize(ref TargetNode, n);
    }

    public void ResetSlot(int slot)
    {
        EnsureSlots(slot);
        UnlockedActions[slot] = 0;
        Craftable[slot] = 0;
        Owned[slot] = 0;
        Candidates[slot] = 0;
        TargetSlot[slot] = TargetConcept[slot] = TargetTool[slot] = TargetNode[slot] = -1;
    }
}
