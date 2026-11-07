namespace Eden.Sim;

/// <summary>How a settlement (or a religion, at its founding) regards the god.</summary>
public enum GodStance : byte { Neutral, Favourable, Fearful, Hostile }

/// <summary>The god actions that move a settlement's standing (World.Commands.cs hooks).</summary>
public enum GodActKind : byte { Bless, Curse, Lightning, Drought, Rain, Spawn, Terraform, Gift, Whisper }

/// <summary>
/// M7: the god's reputation per settlement. <see cref="Favour"/> (−1..1) and <see cref="Awe"/> (0..1) move with
/// witnessed god actions and decay slowly; <see cref="Envy"/> and <see cref="Fear"/> (0..1) are what a settlement
/// feels toward the most favoured neighbour it is in contact with. Harsh acts (lightning, drought) cost Favour only
/// after a grace period (<see cref="Wrath"/> pending until <see cref="WrathDue"/>), so rain or a blessing that
/// follows reads as a test, not a punishment. Arrays are indexed by settlement id (ids are never reused); the
/// contact matrix holds 1 + the tick of the last contact. Everything here is saved and hashed (World.Myth.cs).
/// </summary>
public sealed class GodStanding
{
    private int _cap = 16;

    public float[] Favour = new float[16], Awe = new float[16];
    public float[] Envy = new float[16], Fear = new float[16];
    public int[] EnvyOf = new int[16], FearOf = new int[16];
    public float[] Wrath = new float[16];
    public long[] WrathDue = new long[16];

    /// <summary>Finished shrines already announced in the Chronicle, per settlement.</summary>
    public int[] ShrinesSeen = new int[16];

    private long[] _contact = new long[16 * 16];

    public int Capacity => _cap;

    public GodStanding()
    {
        Array.Fill(EnvyOf, -1);
        Array.Fill(FearOf, -1);
    }

    /// <summary>Make room for settlement ids below <paramref name="count"/>.</summary>
    public void Ensure(int count)
    {
        if (count <= _cap) return;
        int old = _cap, n = _cap;
        while (n < count) n *= 2;
        Array.Resize(ref Favour, n); Array.Resize(ref Awe, n);
        Array.Resize(ref Envy, n); Array.Resize(ref Fear, n);
        Array.Resize(ref EnvyOf, n); Array.Resize(ref FearOf, n);
        Array.Resize(ref Wrath, n); Array.Resize(ref WrathDue, n);
        Array.Resize(ref ShrinesSeen, n);
        for (int i = old; i < n; i++) EnvyOf[i] = FearOf[i] = -1;
        var c = new long[n * n];
        for (int a = 0; a < old; a++) Array.Copy(_contact, a * old, c, a * n, old);
        _contact = c;
        _cap = n;
    }

    /// <summary>Record contact between two settlements (symmetric).</summary>
    public void Touch(int a, int b, long tick)
    {
        if (a < 0 || b < 0 || a == b || a >= _cap || b >= _cap) return;
        _contact[a * _cap + b] = _contact[b * _cap + a] = tick + 1;
    }

    /// <summary>Tick of the last contact between a and b, or -1.</summary>
    public long LastContact(int a, int b) => a >= 0 && b >= 0 && a < _cap && b < _cap ? _contact[a * _cap + b] - 1 : -1;

    public GodStance StanceOf(int set, SimConfig cfg) => Stance(Favour[set], Awe[set], cfg);

    public static GodStance Stance(float favour, float awe, SimConfig cfg)
    {
        if (favour > cfg.StanceFavourMin) return GodStance.Favourable;
        if (awe >= cfg.StanceFearAwe) return GodStance.Fearful;
        if (favour < -cfg.StanceFavourMin) return GodStance.Hostile;
        return GodStance.Neutral;
    }

    public static string StanceName(GodStance s) => s switch
    {
        GodStance.Favourable => "favourable",
        GodStance.Fearful => "fearful",
        GodStance.Hostile => "hostile",
        _ => "neutral",
    };

    /// <summary>Words that make a belief "about the god". Eden.Mind's IntentionMapper uses the same list.</summary>
    public static readonly string[] GodWords = ["god", "voice", "sky", "heaven", "divine", "spirit", "the one above"];

    /// <summary>True when the text speaks of the god (case-insensitive, allocation-free).</summary>
    public static bool MentionsGod(string text)
    {
        foreach (var w in GodWords)
            if (text.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public ulong Hash(ulong h, int count)
    {
        h = Eden.Sim.Hash.Mix(h, (ulong)count);
        for (int i = 0; i < count && i < _cap; i++)
        {
            h = Eden.Sim.Hash.Mix(h, Favour[i]); h = Eden.Sim.Hash.Mix(h, Awe[i]);
            h = Eden.Sim.Hash.Mix(h, Envy[i]); h = Eden.Sim.Hash.Mix(h, Fear[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)EnvyOf[i]); h = Eden.Sim.Hash.Mix(h, (ulong)FearOf[i]);
            h = Eden.Sim.Hash.Mix(h, Wrath[i]); h = Eden.Sim.Hash.Mix(h, (ulong)WrathDue[i]);
            h = Eden.Sim.Hash.Mix(h, (ulong)ShrinesSeen[i]);
            for (int j = 0; j < count && j < _cap; j++) h = Eden.Sim.Hash.Mix(h, (ulong)_contact[i * _cap + j]);
        }
        return h;
    }

    public void Write(BinaryWriter w, int count)
    {
        w.Write(count);
        for (int i = 0; i < count; i++)
        {
            w.Write(Favour[i]); w.Write(Awe[i]); w.Write(Envy[i]); w.Write(Fear[i]);
            w.Write(EnvyOf[i]); w.Write(FearOf[i]); w.Write(Wrath[i]); w.Write(WrathDue[i]); w.Write(ShrinesSeen[i]);
            for (int j = 0; j < count; j++) w.Write(_contact[i * _cap + j]);
        }
    }

    public static GodStanding Read(BinaryReader r)
    {
        var g = new GodStanding();
        int count = r.ReadInt32();
        g.Ensure(count);
        for (int i = 0; i < count; i++)
        {
            g.Favour[i] = r.ReadSingle(); g.Awe[i] = r.ReadSingle(); g.Envy[i] = r.ReadSingle(); g.Fear[i] = r.ReadSingle();
            g.EnvyOf[i] = r.ReadInt32(); g.FearOf[i] = r.ReadInt32(); g.Wrath[i] = r.ReadSingle(); g.WrathDue[i] = r.ReadInt64();
            g.ShrinesSeen[i] = r.ReadInt32();
            for (int j = 0; j < count; j++) g._contact[i * g._cap + j] = r.ReadInt64();
        }
        return g;
    }
}
