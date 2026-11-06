namespace Eden.Sim;

/// <summary>
/// Factions group settlements. Every new settlement starts its own faction; factions merge when their
/// leaders are close friends and neither side bears a grudge (an alliance). Raids only cross faction lines.
/// Ids are never reused.
/// </summary>
public sealed class Factions
{
    public int Count { get; private set; }
    public bool[] Alive = new bool[16];
    public string[] Name = new string[16];
    public int[] Capital = new int[16];
    public long[] Founded = new long[16];

    public bool IsAlive(int id) => id >= 0 && id < Count && Alive[id];

    public int Add(string name, int capital, long tick)
    {
        if (Count == Alive.Length)
        {
            int n = Count * 2;
            Array.Resize(ref Alive, n); Array.Resize(ref Name, n); Array.Resize(ref Capital, n); Array.Resize(ref Founded, n);
        }
        int id = Count++;
        Alive[id] = true; Name[id] = name; Capital[id] = capital; Founded[id] = tick;
        return id;
    }

    public ulong Hash(ulong h)
    {
        h = Eden.Sim.Hash.Mix(h, (ulong)Count);
        for (int i = 0; i < Count; i++)
        {
            h = Eden.Sim.Hash.Mix(h, Alive[i] ? 1UL : 0UL);
            h = Eden.Sim.Hash.Mix(h, StableHash.Of(Name[i]));
            h = Eden.Sim.Hash.Mix(h, (ulong)Capital[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Founded[i]);
        }
        return h;
    }

    public void Write(BinaryWriter w)
    {
        w.Write(Count);
        for (int i = 0; i < Count; i++) { w.Write(Alive[i]); w.Write(Name[i]); w.Write(Capital[i]); w.Write(Founded[i]); }
    }

    public void Read(BinaryReader r)
    {
        Count = 0;
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            bool alive = r.ReadBoolean();
            int id = Add(r.ReadString(), r.ReadInt32(), r.ReadInt64());
            Alive[id] = alive;
        }
    }
}

public sealed partial class World
{
    public Factions Factions { get; private set; } = new();

    private void JoinNewFaction(int set)
    {
        Settlements.Faction[set] = Factions.Add("the people of " + Settlements.Name[set], set, Tick);
    }

    /// <summary>An abandoned settlement leaves its faction; a faction with no settlements dissolves.</summary>
    private void LeaveFaction(int set)
    {
        int f = Settlements.Faction[set];
        if (!Factions.IsAlive(f)) return;
        int heir = -1;
        for (int k = 0; k < Settlements.Count; k++)
            if (Settlements.Alive[k] && Settlements.Faction[k] == f && (heir < 0 || Settlements.Members[k] > Settlements.Members[heir])) heir = k;
        if (heir < 0) Factions.Alive[f] = false;
        else if (Factions.Capital[f] == set) Factions.Capital[f] = heir;
    }

    /// <summary>Daily: two settlements of different factions whose leaders are firm friends ally (the smaller joins).</summary>
    private void UpdateAlliances()
    {
        var st = Settlements;
        for (int a = 0; a < st.Count; a++)
        {
            if (!st.Alive[a] || !Npcs.IsAlive(st.Leader[a])) continue;
            for (int b = a + 1; b < st.Count; b++)
            {
                if (!st.Alive[b] || st.Faction[a] == st.Faction[b] || !Npcs.IsAlive(st.Leader[b])) continue;
                if (st.Grievance(a, b) > 0.3f || st.Grievance(b, a) > 0.3f) continue;
                if (Relationships.Get(st.Leader[a], st.Leader[b]).Affinity < Config.AllianceAffinity) continue;
                int big = FactionSize(st.Faction[a]) >= FactionSize(st.Faction[b]) ? st.Faction[a] : st.Faction[b];
                int small = big == st.Faction[a] ? st.Faction[b] : st.Faction[a];
                for (int k = 0; k < st.Count; k++) if (st.Alive[k] && st.Faction[k] == small) st.Faction[k] = big;
                Factions.Alive[small] = false;
                Chronicle(Tick, "alliance", $"{st.Name[a]} and {st.Name[b]} joined as {Factions.Name[big]}.", 6,
                    [st.Leader[a], st.Leader[b]]);
            }
        }
    }

    private int FactionSize(int f)
    {
        int n = 0;
        for (int k = 0; k < Settlements.Count; k++) if (Settlements.Alive[k] && Settlements.Faction[k] == f) n += Settlements.Members[k];
        return n;
    }
}
