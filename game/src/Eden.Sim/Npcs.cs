using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>Sub-state of the current action: walk to the target, then act for <see cref="Npcs.ActTicks"/>.</summary>
public enum ActionPhase : byte { Done, Moving, Acting }

/// <summary>
/// Struct-of-arrays NPC storage indexed by dense slot with a LIFO free list. Public ids are
/// generation-tagged handles (<c>version &lt;&lt; 20 | slot</c>) so a reused slot never answers to a dead NPC's id.
/// Sparse per-NPC data (memories, knowledge, relationships) lives in dictionaries/lists elsewhere.
/// </summary>
public sealed class Npcs
{
    public const int SlotBits = 20;
    public const int SlotMask = (1 << SlotBits) - 1;
    private const int VersionMask = (1 << (31 - SlotBits)) - 1;
    public const int None = -1;

    public int Capacity { get; private set; }

    /// <summary>Slots in [0, HighWater) have been used at least once; iterate these and skip dead ones.</summary>
    public int HighWater { get; private set; }

    public int Count { get; private set; }

    public int[] Version = [];
    public bool[] Alive = [];
    public float[] X = [], Z = [], Heading = [];
    public Sex[] Sex = [];
    public long[] BirthTick = [];
    public float[] LifespanYears = [];
    public float[] Genes = [];
    public float[] Needs = [];
    public float[] Health = [];
    public ActionKind[] Action = [];
    public ActionPhase[] Phase = [];
    public int[] ActTicks = [];
    public float[] ActionScore = [];
    public float[] TargetX = [], TargetZ = [];
    public int[] TargetNode = [];
    public int[] AvoidNode = [];
    public int[] TargetNpc = [];
    public int[] Partner = [];
    public long[] PregnantUntil = [];
    public long[] LastBirth = [];
    public float[] Unborn = [];
    public int[] UnbornFather = [];
    public int[] Mother = [], Father = [];
    public int[] Lineage = [];
    public float[] Food = [];
    public int[] StarveTicks = [], ThirstTicks = [];
    public long[] BlessUntil = [];
    public sbyte[] Blessing = [];
    public ActionKind[] GoalAction = [];
    public float[] GoalBonus = [];
    public long[] GoalUntil = [];
    public int[] GoalTarget = [];
    public float[] ThreatX = [], ThreatZ = [];
    public float[] GatherSkill = [], HuntSkill = [];
    public int[] Children = [];
    public string[] Name = [];

    private int[] _free = [];
    private int _freeCount;

    public Npcs(int capacity = 64) => Resize(Math.Max(4, capacity));

    public int IdOf(int slot) => (Version[slot] << SlotBits) | slot;

    public bool TryGetSlot(int id, out int slot)
    {
        slot = id & SlotMask;
        if (id < 0 || slot >= HighWater || !Alive[slot] || Version[slot] != (id >> SlotBits)) { slot = -1; return false; }
        return true;
    }

    public bool IsAlive(int id) => TryGetSlot(id, out _);

    public float Gene(int slot, Gene g) => Genes[slot * Genetics.Count + (int)g];
    public Span<float> GenesOf(int slot) => Genes.AsSpan(slot * Genetics.Count, Genetics.Count);
    public Span<float> UnbornOf(int slot) => Unborn.AsSpan(slot * Genetics.Count, Genetics.Count);

    public ref float NeedRef(int slot, Need n) => ref Needs[slot * Eden.Sim.Needs.Count + (int)n];
    public float Need(int slot, Need n) => Needs[slot * Eden.Sim.Needs.Count + (int)n];

    /// <summary>Claim a slot and reset every field. Grows storage when full (amortised; spawning is not the hot loop).</summary>
    public int Allocate()
    {
        int slot;
        if (_freeCount > 0) slot = _free[--_freeCount];
        else
        {
            if (HighWater == Capacity) Resize(Capacity * 2);
            slot = HighWater++;
        }
        int v = (Version[slot] + 1) & VersionMask;
        Version[slot] = v == 0 ? 1 : v;
        Alive[slot] = true;
        Count++;
        X[slot] = Z[slot] = Heading[slot] = 0;
        Sex[slot] = default;
        BirthTick[slot] = 0;
        LifespanYears[slot] = 70;
        GenesOf(slot).Fill(0.5f);
        UnbornOf(slot).Clear();
        Needs.AsSpan(slot * Eden.Sim.Needs.Count, Eden.Sim.Needs.Count).Fill(1f);
        Health[slot] = 1f;
        Action[slot] = ActionKind.Idle;
        Phase[slot] = ActionPhase.Done;
        ActTicks[slot] = 0;
        ActionScore[slot] = 0;
        TargetX[slot] = TargetZ[slot] = 0;
        TargetNode[slot] = None;
        AvoidNode[slot] = None;
        TargetNpc[slot] = None;
        Partner[slot] = None;
        PregnantUntil[slot] = 0;
        LastBirth[slot] = long.MinValue / 2;
        UnbornFather[slot] = None;
        Mother[slot] = Father[slot] = None;
        Lineage[slot] = 0;
        Food[slot] = 0;
        StarveTicks[slot] = ThirstTicks[slot] = 0;
        BlessUntil[slot] = 0;
        Blessing[slot] = 0;
        GoalAction[slot] = ActionKind.Idle;
        GoalBonus[slot] = 0;
        GoalUntil[slot] = 0;
        GoalTarget[slot] = None;
        ThreatX[slot] = ThreatZ[slot] = 0;
        GatherSkill[slot] = HuntSkill[slot] = 0;
        Children[slot] = 0;
        Name[slot] = "";
        return slot;
    }

    public void Free(int slot)
    {
        if (!Alive[slot]) return;
        Alive[slot] = false;
        Count--;
        Name[slot] = "";
        if (_freeCount == _free.Length) Array.Resize(ref _free, Math.Max(16, _free.Length * 2));
        _free[_freeCount++] = slot;
    }

    private void Resize(int n)
    {
        Capacity = n;
        Array.Resize(ref Version, n);
        Array.Resize(ref Alive, n);
        Array.Resize(ref X, n);
        Array.Resize(ref Z, n);
        Array.Resize(ref Heading, n);
        Array.Resize(ref Sex, n);
        Array.Resize(ref BirthTick, n);
        Array.Resize(ref LifespanYears, n);
        Array.Resize(ref Genes, n * Genetics.Count);
        Array.Resize(ref Needs, n * Eden.Sim.Needs.Count);
        Array.Resize(ref Health, n);
        Array.Resize(ref Action, n);
        Array.Resize(ref Phase, n);
        Array.Resize(ref ActTicks, n);
        Array.Resize(ref ActionScore, n);
        Array.Resize(ref TargetX, n);
        Array.Resize(ref TargetZ, n);
        Array.Resize(ref TargetNode, n);
        Array.Resize(ref AvoidNode, n);
        Array.Resize(ref TargetNpc, n);
        Array.Resize(ref Partner, n);
        Array.Resize(ref PregnantUntil, n);
        Array.Resize(ref LastBirth, n);
        Array.Resize(ref Unborn, n * Genetics.Count);
        Array.Resize(ref UnbornFather, n);
        Array.Resize(ref Mother, n);
        Array.Resize(ref Father, n);
        Array.Resize(ref Lineage, n);
        Array.Resize(ref Food, n);
        Array.Resize(ref StarveTicks, n);
        Array.Resize(ref ThirstTicks, n);
        Array.Resize(ref BlessUntil, n);
        Array.Resize(ref Blessing, n);
        Array.Resize(ref GoalAction, n);
        Array.Resize(ref GoalBonus, n);
        Array.Resize(ref GoalUntil, n);
        Array.Resize(ref GoalTarget, n);
        Array.Resize(ref ThreatX, n);
        Array.Resize(ref ThreatZ, n);
        Array.Resize(ref GatherSkill, n);
        Array.Resize(ref HuntSkill, n);
        Array.Resize(ref Children, n);
        Array.Resize(ref Name, n);
        for (int i = 0; i < n; i++) Name[i] ??= "";
    }

    // ---- hashing and persistence ------------------------------------------------------------

    public ulong Hash(ulong h)
    {
        h = Eden.Sim.Hash.Mix(h, (ulong)HighWater);
        h = Eden.Sim.Hash.Mix(h, (ulong)Count);
        for (int i = 0; i < _freeCount; i++) h = Eden.Sim.Hash.Mix(h, (ulong)_free[i]);
        for (int s = 0; s < HighWater; s++)
        {
            h = Eden.Sim.Hash.Mix(h, (ulong)Version[s]);
            if (!Alive[s]) continue;
            h = Eden.Sim.Hash.Mix(h, X[s]);
            h = Eden.Sim.Hash.Mix(h, Z[s]);
            h = Eden.Sim.Hash.Mix(h, Heading[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Sex[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)BirthTick[s]);
            h = Eden.Sim.Hash.Mix(h, LifespanYears[s]);
            for (int g = 0; g < Genetics.Count; g++)
            {
                h = Eden.Sim.Hash.Mix(h, Genes[s * Genetics.Count + g]);
                h = Eden.Sim.Hash.Mix(h, Unborn[s * Genetics.Count + g]);
            }
            for (int n = 0; n < Eden.Sim.Needs.Count; n++) h = Eden.Sim.Hash.Mix(h, Needs[s * Eden.Sim.Needs.Count + n]);
            h = Eden.Sim.Hash.Mix(h, Health[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Action[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Phase[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)ActTicks[s]);
            h = Eden.Sim.Hash.Mix(h, ActionScore[s]);
            h = Eden.Sim.Hash.Mix(h, TargetX[s]);
            h = Eden.Sim.Hash.Mix(h, TargetZ[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)TargetNode[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)AvoidNode[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)TargetNpc[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Partner[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)PregnantUntil[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)LastBirth[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)UnbornFather[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Mother[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Father[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Lineage[s]);
            h = Eden.Sim.Hash.Mix(h, Food[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)StarveTicks[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)ThirstTicks[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)BlessUntil[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Blessing[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)GoalAction[s]);
            h = Eden.Sim.Hash.Mix(h, GoalBonus[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)GoalUntil[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)GoalTarget[s]);
            h = Eden.Sim.Hash.Mix(h, ThreatX[s]);
            h = Eden.Sim.Hash.Mix(h, ThreatZ[s]);
            h = Eden.Sim.Hash.Mix(h, GatherSkill[s]);
            h = Eden.Sim.Hash.Mix(h, HuntSkill[s]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Children[s]);
            h = Eden.Sim.Hash.Mix(h, StableHash.Of(Name[s]));
        }
        return h;
    }

    public void Write(BinaryWriter w)
    {
        w.Write(HighWater);
        w.Write(Count);
        w.Write(_freeCount);
        for (int i = 0; i < _freeCount; i++) w.Write(_free[i]);
        for (int s = 0; s < HighWater; s++)
        {
            w.Write(Version[s]);
            w.Write(Alive[s]);
            if (!Alive[s]) continue;
            w.Write(X[s]); w.Write(Z[s]); w.Write(Heading[s]);
            w.Write((byte)Sex[s]);
            w.Write(BirthTick[s]);
            w.Write(LifespanYears[s]);
            for (int g = 0; g < Genetics.Count; g++) { w.Write(Genes[s * Genetics.Count + g]); w.Write(Unborn[s * Genetics.Count + g]); }
            for (int n = 0; n < Eden.Sim.Needs.Count; n++) w.Write(Needs[s * Eden.Sim.Needs.Count + n]);
            w.Write(Health[s]);
            w.Write((byte)Action[s]); w.Write((byte)Phase[s]); w.Write(ActTicks[s]); w.Write(ActionScore[s]);
            w.Write(TargetX[s]); w.Write(TargetZ[s]); w.Write(TargetNode[s]); w.Write(AvoidNode[s]); w.Write(TargetNpc[s]);
            w.Write(Partner[s]); w.Write(PregnantUntil[s]); w.Write(LastBirth[s]); w.Write(UnbornFather[s]);
            w.Write(Mother[s]); w.Write(Father[s]); w.Write(Lineage[s]);
            w.Write(Food[s]); w.Write(StarveTicks[s]); w.Write(ThirstTicks[s]);
            w.Write(BlessUntil[s]); w.Write(Blessing[s]);
            w.Write((byte)GoalAction[s]); w.Write(GoalBonus[s]); w.Write(GoalUntil[s]); w.Write(GoalTarget[s]);
            w.Write(ThreatX[s]); w.Write(ThreatZ[s]);
            w.Write(GatherSkill[s]); w.Write(HuntSkill[s]);
            w.Write(Children[s]);
            w.Write(Name[s]);
        }
    }

    public void Read(BinaryReader r)
    {
        int highWater = r.ReadInt32();
        int count = r.ReadInt32();
        int cap = 64;
        while (cap < highWater) cap *= 2;
        Version = []; Alive = [];
        Resize(cap);
        Array.Clear(Version);
        Array.Clear(Alive);
        HighWater = highWater;
        Count = count;
        _freeCount = r.ReadInt32();
        _free = new int[Math.Max(16, _freeCount)];
        for (int i = 0; i < _freeCount; i++) _free[i] = r.ReadInt32();
        for (int s = 0; s < HighWater; s++)
        {
            Version[s] = r.ReadInt32();
            Alive[s] = r.ReadBoolean();
            if (!Alive[s]) { Name[s] = ""; continue; }
            X[s] = r.ReadSingle(); Z[s] = r.ReadSingle(); Heading[s] = r.ReadSingle();
            Sex[s] = (Sex)r.ReadByte();
            BirthTick[s] = r.ReadInt64();
            LifespanYears[s] = r.ReadSingle();
            for (int g = 0; g < Genetics.Count; g++) { Genes[s * Genetics.Count + g] = r.ReadSingle(); Unborn[s * Genetics.Count + g] = r.ReadSingle(); }
            for (int n = 0; n < Eden.Sim.Needs.Count; n++) Needs[s * Eden.Sim.Needs.Count + n] = r.ReadSingle();
            Health[s] = r.ReadSingle();
            Action[s] = (ActionKind)r.ReadByte(); Phase[s] = (ActionPhase)r.ReadByte(); ActTicks[s] = r.ReadInt32(); ActionScore[s] = r.ReadSingle();
            TargetX[s] = r.ReadSingle(); TargetZ[s] = r.ReadSingle(); TargetNode[s] = r.ReadInt32(); AvoidNode[s] = r.ReadInt32(); TargetNpc[s] = r.ReadInt32();
            Partner[s] = r.ReadInt32(); PregnantUntil[s] = r.ReadInt64(); LastBirth[s] = r.ReadInt64(); UnbornFather[s] = r.ReadInt32();
            Mother[s] = r.ReadInt32(); Father[s] = r.ReadInt32(); Lineage[s] = r.ReadInt32();
            Food[s] = r.ReadSingle(); StarveTicks[s] = r.ReadInt32(); ThirstTicks[s] = r.ReadInt32();
            BlessUntil[s] = r.ReadInt64(); Blessing[s] = r.ReadSByte();
            GoalAction[s] = (ActionKind)r.ReadByte(); GoalBonus[s] = r.ReadSingle(); GoalUntil[s] = r.ReadInt64(); GoalTarget[s] = r.ReadInt32();
            ThreatX[s] = r.ReadSingle(); ThreatZ[s] = r.ReadSingle();
            GatherSkill[s] = r.ReadSingle(); HuntSkill[s] = r.ReadSingle();
            Children[s] = r.ReadInt32();
            Name[s] = r.ReadString();
        }
    }
}

/// <summary>Process-independent string hash (string.GetHashCode is randomised per process).</summary>
public static class StableHash
{
    public static ulong Of(string? s)
    {
        ulong h = Hash.Offset;
        if (s is null) return h;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h;
    }
}
