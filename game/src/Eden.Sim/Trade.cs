using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// Trade between settlements: when one has a food surplus and another within travel range (same landmass)
/// has a deficit, the surplus side sends a trader with a load of food. On arrival the food goes into the
/// buyer's stock, the buyer pays in wood or stone (or owes a debt), grievances ease and the trader is trusted.
/// </summary>
public sealed partial class World
{
    /// <summary>Daily: start at most one trade run per settlement, respecting a cooldown.</summary>
    private void PlanTrade(int a)
    {
        var st = Settlements;
        if (Tick - st.LastTrade[a] < (long)Config.TradeCooldownDays * Config.TicksPerDay) return;
        if (st.Members[a] == 0 || st.Food[a] < Config.TradeMinSurplus) return;
        float perCapA = st.Food[a] / st.Members[a];
        if (perCapA < Config.TradeSurplusPerCapita) return;
        int region = Pathing.Region[Terrain.CellIndex(st.CenterX[a], st.CenterZ[a])];
        int best = -1;
        float bestD = Config.TradeRange * Config.TradeRange;
        for (int b = 0; b < st.Count; b++)
        {
            if (b == a || !st.Alive[b] || st.Members[b] == 0) continue;
            if (st.Grievance(a, b) > Config.RaidThreshold) continue;
            float perCapB = st.Food[b] / st.Members[b];
            if (perCapB >= Config.TradeDeficitPerCapita && st.Hungry[b] < 0.3f) continue;
            float d = Sq(st.CenterX[b] - st.CenterX[a]) + Sq(st.CenterZ[b] - st.CenterZ[a]);
            if (d >= bestD || Pathing.Region[Terrain.CellIndex(st.CenterX[b], st.CenterZ[b])] != region) continue;
            bestD = d;
            best = b;
        }
        if (best < 0) return;

        int trader = -1;
        float bestSoc = -1f;
        foreach (int s in MembersOf(a))
        {
            if (!Npcs.Alive[s] || SettlementOfSlot(s) != a || _order[s] != (byte)Order.None) continue;
            if (StageOf(s) != LifeStage.Adult || Npcs.Health[s] < 0.6f || Npcs.PregnantUntil[s] > 0) continue;
            float v = Trait(s, Gene.Sociability) + 0.5f * Trait(s, Gene.Curiosity);
            if (v > bestSoc) { bestSoc = v; trader = s; }
        }
        if (trader < 0) return;
        float load = MathF.Floor(Math.Min(Config.TradeLoad, st.Food[a] * 0.4f));
        st.Food[a] -= load;
        _cargo[trader] = load;
        _order[trader] = (byte)Order.Trade;
        _orderTarget[trader] = best;
        _orderUntil[trader] = Tick + 4L * Config.TicksPerDay;
        Npcs.Phase[trader] = ActionPhase.Done;
        st.LastTrade[a] = Tick;
    }

    /// <summary>The trader reached the buyer's store.</summary>
    private void CompleteTrade(int s)
    {
        var st = Settlements;
        int buyer = _orderTarget[s], seller = SettlementOfSlot(s);
        float cargo = _cargo[s];
        _cargo[s] = 0;
        _order[s] = (byte)Order.Return;
        _orderUntil[s] = Tick + 3L * Config.TicksPerDay;
        if (!st.IsAlive(buyer) || cargo <= 0) return;
        var (x, z) = StorePoint(buyer);
        if (Dist(Npcs.X[s], Npcs.Z[s], x, z) > StoreReach) { if (st.IsAlive(seller)) AddFood(seller, cargo); return; }

        float delivered = AddFood(buyer, cargo);
        float leftover = cargo - delivered;
        if (leftover > 0) Npcs.Food[s] = Math.Min(Config.MaxCarriedFood, Npcs.Food[s] + leftover);
        string payment = "on credit";
        if (st.IsAlive(seller))
        {
            float wood = MathF.Floor(Math.Min(st.Wood[buyer], delivered / 3f));
            float stone = MathF.Floor(Math.Min(st.Stone[buyer], delivered / 3f - wood));
            st.Wood[buyer] -= wood; st.Wood[seller] += wood;
            st.Stone[buyer] -= stone; st.Stone[seller] += stone;
            if (wood + stone > 0) payment = wood > 0 ? $"for {wood:0} wood" : $"for {stone:0} stone";
            ref float gab = ref st.GrievanceRef(seller, buyer);
            gab = Math.Max(0f, gab - 0.4f);
            ref float gba = ref st.GrievanceRef(buyer, seller);
            gba = Math.Max(0f, gba - 0.6f);
        }
        int id = Npcs.IdOf(s);
        int leader = st.Leader[buyer];
        if (Npcs.IsAlive(leader))
            Relationships.Adjust(id, leader, 0.12f, 0.12f, debt: payment == "on credit" ? delivered / 4f : 0f);
        TotalTrades++;
        string from = st.IsAlive(seller) ? st.Name[seller] : "a vanished village";
        Events.Publish(new SimEvent(Tick, MemoryKind.Gift, $"{Npcs.Name[s]} of {from} brought {delivered:0} food to {st.Name[buyer]} {payment}.", 4,
            Npcs.IsAlive(leader) ? [id, leader] : [id], Npcs.X[s], Npcs.Z[s], Config.PerceptionRadius, "trade", 3));
    }
}
