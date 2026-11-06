using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// Grievance and raids. A settlement resents outsiders foraging on its land (more so when hungry) and
/// anyone who raided it. When grievance + the aggression of its people and leader + food pressure crosses
/// <see cref="SimConfig.RaidThreshold"/>, it sends its most aggressive adults to take food from a settlement
/// of another faction. Raiders fight whoever defends the store; wounds can kill.
/// </summary>
public sealed partial class World
{
    /// <summary>Daily: outsiders inside a settlement's territory feed its grievance; everything decays.</summary>
    private void AccrueGrievance()
    {
        var st = Settlements;
        st.DecayGrievances(Config.GrievanceDecayPerDay);
        float territory = Config.SettlementRadius * Config.TerritoryFactor;
        float t2 = territory * territory;
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            int own = SettlementOfSlot(s);
            if (own < 0 || StageOf(s) is not (LifeStage.Adult or LifeStage.Elder)) continue;
            float x = Npcs.X[s], z = Npcs.Z[s];
            for (int k = 0; k < st.Count; k++)
            {
                if (k == own || !st.Alive[k]) continue;
                if (Sq(st.CenterX[k] - x) + Sq(st.CenterZ[k] - z) > t2) continue;
                if (_order[s] == (byte)Order.Trade && _orderTarget[s] == k) continue;
                ref float g = ref st.GrievanceRef(k, own);
                g = Math.Min(5f, g + Config.TrespassGrievance * (1f + 2f * st.Hungry[k]));
            }
        }
    }

    private float FoodPressure(int k)
    {
        var st = Settlements;
        float perCap = st.Members[k] > 0 ? st.Food[k] / st.Members[k] : 0f;
        return 0.5f * Math.Clamp(1f - perCap / 3f, 0f, 1f) + 0.5f * st.Hungry[k];
    }

    /// <summary>The number the raid threshold is compared to, for settlement <paramref name="a"/> against <paramref name="b"/>.</summary>
    public float RaidPressure(int a, int b)
    {
        var st = Settlements;
        float leaderAggr = Npcs.TryGetSlot(st.Leader[a], out int ls) ? Trait(ls, Gene.Aggression) : st.Aggression[a];
        float aggr = 0.5f * leaderAggr + 0.5f * st.Aggression[a];
        float friendship = Npcs.IsAlive(st.Leader[a]) && Npcs.IsAlive(st.Leader[b]) ? Relationships.Get(st.Leader[a], st.Leader[b]).Affinity : 0f;
        return st.Grievance(a, b) + Config.RaidAggressionWeight * aggr + FoodPressure(a) - 0.5f * friendship;
    }

    /// <summary>Daily: maybe launch one raid from <paramref name="a"/>.</summary>
    private void PlanRaid(int a)
    {
        var st = Settlements;
        if (st.Adults[a] < 3 || Tick - st.LastRaid[a] < (long)Config.RaidCooldownDays * Config.TicksPerDay) return;
        int region = Pathing.Region[Terrain.CellIndex(st.CenterX[a], st.CenterZ[a])];
        int target = -1;
        float best = Config.RaidThreshold;
        for (int b = 0; b < st.Count; b++)
        {
            if (b == a || !st.Alive[b] || st.Faction[b] == st.Faction[a] || st.Members[b] == 0) continue;
            float d2 = Sq(st.CenterX[b] - st.CenterX[a]) + Sq(st.CenterZ[b] - st.CenterZ[a]);
            if (d2 > Config.RaidRange * Config.RaidRange) continue;
            if (Pathing.Region[Terrain.CellIndex(st.CenterX[b], st.CenterZ[b])] != region) continue;
            if (st.Food[b] < 4f && st.Grievance(a, b) < 1f) continue;
            float p = RaidPressure(a, b);
            if (p >= best) { best = p; target = b; }
        }
        if (target < 0) return;

        int party = Math.Clamp(st.Adults[a] / 4, 2, 6), sent = 0;
        int leaderSlot = Npcs.TryGetSlot(st.Leader[a], out int lsl) ? lsl : -1;
        for (int c = 0; c < party; c++)
        {
            int pick = -1;
            float bestV = -1f;
            foreach (int s in MembersOf(a))
            {
                if (!Npcs.Alive[s] || SettlementOfSlot(s) != a || _order[s] != (byte)Order.None) continue;
                if (StageOf(s) != LifeStage.Adult || Npcs.Health[s] < 0.6f || Npcs.PregnantUntil[s] > 0) continue;
                float v = Trait(s, Gene.Aggression) + 0.5f * Npcs.Gene(s, Gene.Strength) + (s == leaderSlot ? 0.2f : 0f);
                if (v > bestV) { bestV = v; pick = s; }
            }
            if (pick < 0) break;
            _order[pick] = (byte)Order.Raid;
            _orderTarget[pick] = target;
            _orderUntil[pick] = Tick + 3L * Config.TicksPerDay;
            Npcs.Phase[pick] = ActionPhase.Done;
            sent++;
        }
        if (sent == 0) return;
        st.LastRaid[a] = Tick;
        st.RaidTarget[a] = target;
        st.RaidLanded[a] = false;
    }

    /// <summary>A raider reached the target's store: fight a defender if any, then take food.</summary>
    private void CompleteRaid(int s)
    {
        var st = Settlements;
        var n = Npcs;
        int target = _orderTarget[s], home = SettlementOfSlot(s);
        _order[s] = (byte)Order.Return;
        _orderUntil[s] = Tick + 3L * Config.TicksPerDay;
        if (!st.IsAlive(target)) return;
        var (x, z) = StorePoint(target);
        if (Dist(n.X[s], n.Z[s], x, z) > StoreReach) return;

        bool first = st.IsAlive(home) && st.RaidTarget[home] == target && !st.RaidLanded[home];
        if (first) st.RaidLanded[home] = true;
        int raiderId = n.IdOf(s);

        // Strongest defender of the target within reach.
        int defender = -1;
        float defPower = 0f;
        _grid.Range(n.X[s], n.Z[s], StoreReach, out int bx0, out int bx1, out int bz0, out int bz1);
        for (int bz = bz0; bz <= bz1; bz++)
        for (int bx = bx0; bx <= bx1; bx++)
        for (int o = _grid.Head(bx, bz); o >= 0; o = _grid.Next(o))
        {
            if (!n.Alive[o] || SettlementOfSlot(o) != target || StageOf(o) is not (LifeStage.Adult or LifeStage.Elder)) continue;
            if (Dist(n.X[s], n.Z[s], n.X[o], n.Z[o]) > StoreReach) continue;
            float pw = Power(o);
            if (pw > defPower) { defPower = pw; defender = o; }
        }
        bool won = true;
        int killed = 0, defenderId = Npcs.None;
        if (defender >= 0)
        {
            defenderId = n.IdOf(defender);
            float pr = Power(s);
            won = Rng.NextFloat() * (pr + defPower) < pr;
            int loser = won ? defender : s;
            n.Health[loser] -= 0.3f + 0.3f * Rng.NextFloat();
            Relationships.Adjust(raiderId, defenderId, -0.4f, -0.4f);
            if (n.Health[loser] <= 0f) { killed++; if (st.IsAlive(home)) st.GrievanceRef(target, home) += 0.5f; }
        }
        float loot = 0f;
        if (won && n.Alive[s])
        {
            loot = MathF.Floor(Math.Min(Math.Min(st.Food[target], Config.MaxCarriedFood - n.Food[s]), 6f));
            st.Food[target] -= loot;
            n.Food[s] += loot;
            if (st.Wood[target] >= 1f && st.IsAlive(home)) { float w = Math.Min(st.Wood[target], 3f); st.Wood[target] -= w; st.Wood[home] += w; }
        }
        if (st.IsAlive(home))
        {
            ref float hurt = ref st.GrievanceRef(target, home);
            hurt = Math.Min(5f, hurt + (first ? 0.8f : 0.2f));
            ref float sated = ref st.GrievanceRef(home, target);
            sated = Math.Max(0f, sated - 0.5f);
        }
        // Everyone of the target nearby is frightened and flees from the raider.
        float fear = Config.PerceptionRadius;
        _grid.Range(n.X[s], n.Z[s], fear, out bx0, out bx1, out bz0, out bz1);
        for (int bz = bz0; bz <= bz1; bz++)
        for (int bx = bx0; bx <= bx1; bx++)
        for (int o = _grid.Head(bx, bz); o >= 0; o = _grid.Next(o))
        {
            if (!n.Alive[o] || SettlementOfSlot(o) != target || o == defender) continue;
            n.ThreatX[o] = n.X[s]; n.ThreatZ[o] = n.Z[s];
            n.NeedRef(o, Need.Safety) = Math.Max(0f, n.Need(o, Need.Safety) - 0.5f);
        }

        if (first)
        {
            TotalRaids++;
            string from = st.IsAlive(home) ? st.Name[home] : "nowhere";
            string outcome = defender < 0 ? "and carried off food unopposed" : won ? "and beat back the defenders" : "but were driven off";
            Events.Publish(new SimEvent(Tick, MemoryKind.Fight, $"Raiders from {from} fell upon {st.Name[target]} {outcome}.", 7,
                defenderId != Npcs.None ? [raiderId, defenderId] : [raiderId], n.X[s], n.Z[s], Config.PerceptionRadius, "raid", 6));
        }
        if (defender >= 0 && n.Alive[defender] && n.Health[defender] <= 0f) Kill(defender, DeathCause.Wounds);
        if (n.Alive[s] && n.Health[s] <= 0f) Kill(s, DeathCause.Wounds);
        TotalRaidDeaths += killed;
    }

    private float Power(int s)
        => (0.4f + Npcs.Gene(s, Gene.Strength)) * (0.6f + 0.4f * Trait(s, Gene.Aggression)) * Math.Max(0.1f, Npcs.Health[s])
           * (StageOf(s) == LifeStage.Elder ? 0.6f : 1f);
}
