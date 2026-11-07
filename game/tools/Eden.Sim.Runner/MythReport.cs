using Eden.Sim;
using Eden.Sim.Contracts;

/// <summary>
/// M7 runner report: <c>--myth [--seed N] [--years Y] [--bless] [--strike]</c>. Prints, per decade, the religions
/// followed (name and stance), shrines standing, prayers, distorted and god-attributed gossip, god beliefs formed,
/// and the most favoured settlement; then the myth Chronicle (religion, schism, shrine, first prayer).
/// <c>--bless</c> has the god bless the leader of the first settlement once a day for an in-game year as soon as it
/// exists (the M7 acceptance scenario); <c>--strike</c> does the same with lightning at the settlement's edge.
/// </summary>
internal static class MythReport
{
    public static int Run(string[] args)
    {
        int seed = 7, years = 100;
        bool bless = false, strike = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--seed": seed = int.Parse(args[++i]); break;
                case "--years": years = int.Parse(args[++i]); break;
                case "--bless": bless = true; break;
                case "--strike": strike = true; break;
            }
        }
        var w = new World(new SimConfig { Seed = seed });
        var cfg = w.Config;
        string mode = bless ? ", the god blesses the first settlement for a year" : strike ? ", the god strikes the first settlement for a year" : "";
        Console.WriteLine($"Eden seed {seed}, {years} years, myth report{mode}");
        Console.WriteLine(" year  pop  settl  shrines  prayers  distorted  attributed  beliefs  favoured (favour/awe)        religions (stance)");
        int target = -1, acts = 0;
        int prayers = 0, distorted = 0, attributed = 0, beliefs = 0;
        var myths = new List<ChronicleEntry>();
        for (int y = 1; y <= years; y++)
        {
            for (int d = 0; d < cfg.DaysPerYear; d++)
            {
                if ((bless || strike) && acts < cfg.DaysPerYear) GodAct(w, ref target, ref acts, bless);
                for (int t = 0; t < cfg.TicksPerDay; t++) w.Step();
            }
            foreach (var e in w.Snapshot().NewChronicle)
                if (e.Kind is "religion" or "schism" or "shrine" or "prayer") myths.Add(e);
            if (y % 10 != 0 && y != years && w.Population > 0) continue;
            Console.WriteLine($" {y,4} {w.Population,4} {w.SettlementCount,6} {w.ShrinesStanding,8} {w.TotalPrayers - prayers,8} {w.TotalDistorted - distorted,10}"
                + $" {w.TotalAttributed - attributed,11} {w.TotalGodBeliefsFormed + w.TotalPrayerBeliefs - beliefs,8}  {Favoured(w),-28} {Faiths(w)}");
            prayers = w.TotalPrayers; distorted = w.TotalDistorted; attributed = w.TotalAttributed;
            beliefs = w.TotalGodBeliefsFormed + w.TotalPrayerBeliefs;
            if (w.Population == 0) { Console.WriteLine($" extinct in year {y}"); break; }
        }
        Console.WriteLine($"\nreligions founded {w.Religions.Count}, schisms {w.TotalSchisms}, shrines raised {w.TotalShrines}, "
            + $"beliefs from the Mind {w.TotalMindGodBeliefs}, state hash {w.StateHash():X16}");
        Console.WriteLine("\nMyth Chronicle:");
        foreach (var e in myths)
            Console.WriteLine($"  y{e.Tick / cfg.TicksPerYear,-3} [{e.Kind}] {e.Text}");
        return 0;
    }

    private static void GodAct(World w, ref int target, ref int acts, bool bless)
    {
        if (target < 0 || !w.Settlements.IsAlive(target))
        {
            target = -1;
            for (int k = 0; k < w.Settlements.Count && target < 0; k++) if (w.Settlements.Alive[k]) target = k;
            if (target < 0) return;
        }
        var st = w.Settlements;
        if (bless)
        {
            if (!w.Npcs.IsAlive(st.Leader[target])) return;
            w.Apply(new BlessCommand(st.Leader[target], false, w.Config.TicksPerDay));
        }
        else w.Apply(new LightningCommand(st.CenterX[target] + 30f, st.CenterZ[target], 1f));
        acts++;
    }

    private static string Favoured(World w)
    {
        int best = -1;
        for (int k = 0; k < w.Settlements.Count; k++)
            if (w.Settlements.Alive[k] && (best < 0 || w.Standing.Favour[k] > w.Standing.Favour[best])) best = k;
        if (best < 0) return "-";
        var g = w.Standing;
        string env = g.Envy[best] > 0.05f || g.Fear[best] > 0.05f ? $" e{g.Envy[best]:0.0}/f{g.Fear[best]:0.0}" : "";
        return $"{w.Settlements.Name[best]} ({g.Favour[best]:+0.00;-0.00}/{g.Awe[best]:0.00}){env}";
    }

    private static string Faiths(World w)
    {
        var seen = new List<string>();
        for (int f = 0; f < w.Factions.Count; f++)
        {
            int r = w.Factions.Religion[f];
            if (!w.Factions.Alive[f] || r < 0) continue;
            string s = $"{w.Religions.Name[r]} ({GodStanding.StanceName(w.Religions.Stance[r])})";
            if (!seen.Contains(s)) seen.Add(s);
        }
        return seen.Count == 0 ? "-" : string.Join(", ", seen);
    }
}
