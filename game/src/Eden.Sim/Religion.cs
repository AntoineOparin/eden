namespace Eden.Sim;

/// <summary>
/// M7 religions. A religion is founded by a faction once enough of a settlement's adults hold a belief about the
/// god (World.Myth.cs), spreads to factions in contact, and splits (schism) when the god's favour turns against
/// its stance for a generation. Factions point at their religion through <see cref="Factions.Religion"/>.
/// Ids are never reused; a religion with no faction left is simply not <see cref="Followed"/> any more.
/// </summary>
public sealed class Religions
{
    public int Count { get; private set; }

    public string[] Name = new string[8];
    public GodStance[] Stance = new GodStance[8];
    public long[] Founded = new long[8];
    public int[] Founder = new int[8];
    public string[] FounderName = new string[8];

    /// <summary>The faction and settlement it was founded in.</summary>
    public int[] OriginFaction = new int[8], OriginSettlement = new int[8];

    /// <summary>The religion this one split from, or -1.</summary>
    public int[] Parent = new int[8];

    /// <summary>True once someone has prayed in its name (the first prayer is a Chronicle entry).</summary>
    public bool[] Prayed = new bool[8];

    public bool IsValid(int id) => id >= 0 && id < Count;

    public int Add(string name, GodStance stance, long tick, int founder, string founderName, int faction, int settlement, int parent)
    {
        if (Count == Name.Length)
        {
            int n = Count * 2;
            Array.Resize(ref Name, n); Array.Resize(ref Stance, n); Array.Resize(ref Founded, n);
            Array.Resize(ref Founder, n); Array.Resize(ref FounderName, n);
            Array.Resize(ref OriginFaction, n); Array.Resize(ref OriginSettlement, n);
            Array.Resize(ref Parent, n); Array.Resize(ref Prayed, n);
        }
        int id = Count++;
        Name[id] = name; Stance[id] = stance; Founded[id] = tick;
        Founder[id] = founder; FounderName[id] = founderName;
        OriginFaction[id] = faction; OriginSettlement[id] = settlement;
        Parent[id] = parent; Prayed[id] = false;
        return id;
    }

    /// <summary>True if any living faction follows the religion.</summary>
    public static bool Followed(Factions f, int religion)
    {
        for (int i = 0; i < f.Count; i++) if (f.Alive[i] && f.Religion[i] == religion) return true;
        return false;
    }

    /// <summary>A name like "the Way of Kalora", generated from <see cref="Names"/> with the given stream.</summary>
    public static string MakeName(ref Rng rng, GodStance stance)
    {
        string word = Names.Next(ref rng, rng.Chance(0.5f));
        return stance switch
        {
            GodStance.Fearful => "the Dread of " + word,
            GodStance.Hostile => "the Defiance of " + word,
            _ => "the Way of " + word,
        };
    }

    public ulong Hash(ulong h)
    {
        h = Eden.Sim.Hash.Mix(h, (ulong)Count);
        for (int i = 0; i < Count; i++)
        {
            h = Eden.Sim.Hash.Mix(h, StableHash.Of(Name[i]));
            h = Eden.Sim.Hash.Mix(h, (ulong)Stance[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Founded[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Founder[i]);
            h = Eden.Sim.Hash.Mix(h, StableHash.Of(FounderName[i]));
            h = Eden.Sim.Hash.Mix(h, (ulong)OriginFaction[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)OriginSettlement[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)Parent[i]);
            h = Eden.Sim.Hash.Mix(h, Prayed[i] ? 1UL : 0UL);
        }
        return h;
    }

    public void Write(BinaryWriter w)
    {
        w.Write(Count);
        for (int i = 0; i < Count; i++)
        {
            w.Write(Name[i]); w.Write((byte)Stance[i]); w.Write(Founded[i]); w.Write(Founder[i]); w.Write(FounderName[i]);
            w.Write(OriginFaction[i]); w.Write(OriginSettlement[i]); w.Write(Parent[i]); w.Write(Prayed[i]);
        }
    }

    public static Religions Read(BinaryReader r)
    {
        var rel = new Religions();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            string name = r.ReadString();
            var stance = (GodStance)r.ReadByte();
            long founded = r.ReadInt64();
            int founder = r.ReadInt32();
            string founderName = r.ReadString();
            int faction = r.ReadInt32(), settlement = r.ReadInt32(), parent = r.ReadInt32();
            int id = rel.Add(name, stance, founded, founder, founderName, faction, settlement, parent);
            rel.Prayed[id] = r.ReadBoolean();
        }
        return rel;
    }
}
