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

    /// <summary>
    /// K1: the same weight raised to an integer power, so hard concepts are rarer by orders of magnitude rather
    /// than by a factor of a few (exponent 1 is the M4 curve).
    /// </summary>
    public static float DiscoveryWeight(float difficulty, int exponent)
    {
        float b = DiscoveryWeight(difficulty), w = b;
        for (int i = 1; i < exponent; i++) w *= b;
        return w;
    }

    /// <summary>yieldMod contribution: full factor at proficiency 1, none at 0.</summary>
    public static float YieldAt(float factor, float proficiency) => 1f + (factor - 1f) * proficiency;

    /// <summary>
    /// Forgetting, one day of it: Δp = −(ratePerYear / daysPerYear) × (p − floor). Never below the floor (the
    /// written level of a readable record), never above p.
    /// </summary>
    public static float Forget(float p, float floor, float ratePerYear, int daysPerYear)
        => p <= floor ? p : Math.Max(floor, p - ratePerYear / Math.Max(1, daysPerYear) * (p - floor));
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

    /// <summary>
    /// K1 forgetting: per slot, per concept index, the tick the NPC last practised (or gained) the concept; -1 when
    /// it does not hold it. Arrays are allocated the first time a slot learns anything and reused when the slot is.
    /// Saved in KNW2.
    /// </summary>
    public long[]?[] LastPractised = new long[]?[64];

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
        Array.Resize(ref LastPractised, n);
    }

    /// <summary>Record practice of concept <paramref name="concept"/> at <paramref name="tick"/>.</summary>
    public void Touch(int slot, int concept, int conceptCount, long tick)
    {
        EnsureSlots(slot);
        var a = LastPractised[slot];
        if (a is null || concept >= a.Length)
        {
            int len = Math.Max(conceptCount, concept + 1);
            int old = a?.Length ?? 0;
            Array.Resize(ref a, len);
            for (int i = old; i < len; i++) a[i] = -1;
            LastPractised[slot] = a;
        }
        a[concept] = tick;
    }

    public void ResetSlot(int slot)
    {
        EnsureSlots(slot);
        UnlockedActions[slot] = 0;
        Craftable[slot] = 0;
        Owned[slot] = 0;
        Candidates[slot] = 0;
        TargetSlot[slot] = TargetConcept[slot] = TargetTool[slot] = TargetNode[slot] = -1;
        if (LastPractised[slot] is { } lp) Array.Fill(lp, -1L);
    }
}
