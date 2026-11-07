using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// M6 possession. The player drives one NPC through <see cref="PossessedMoveCommand"/> and
/// <see cref="PossessedInteractCommand"/>; the sim stays the authority for its position. Needs, health, ageing and
/// knowledge keep ticking (<see cref="TickNpc"/> runs everything up to the decision step). People who know the
/// NPC notice when it starts acting strangely and when it comes back to itself; the NPC remembers a blackout.
/// Possession is runtime state only: it is not saved, and a load releases it.
/// </summary>
public sealed partial class World
{
    /// <summary>Talk memories between the same pair are recorded at most this often (E can be pressed repeatedly).</summary>
    private const int PossessedTalkMemoryTicks = 20;

    /// <summary>"Work" within this distance of the settlement's store point takes food out of the store.</summary>
    private const float PossessedStoreReach = 12f;

    private bool _possessReacted;
    private long _possWorkUntil = -1;
    private int _possWorkWith = Npcs.None;
    private int _possStore = -1;
    private int _possLastTalkWith = Npcs.None;
    private long _possLastTalkTick = long.MinValue;

    // ---- commands ---------------------------------------------------------------------------

    private void Possess(int npcId)
    {
        if (!Npcs.TryGetSlot(npcId, out int s)) { AddLog($"possess rejected: no npc {npcId}"); return; }
        if (StageOf(s) == LifeStage.Infant) { AddLog($"possess rejected: npc {npcId} is an infant"); return; }
        if (_possessed == npcId) return;
        if (_possessed != Npcs.None) Release();
        ClearPossessionRuntime();
        _possessed = npcId;
        Npcs.Action[s] = ActionKind.Idle;
        Npcs.Phase[s] = ActionPhase.Done;
        Npcs.TargetNpc[s] = Npcs.None;
        _pathLen[s] = 0;
    }

    private void Release()
    {
        if (Npcs.TryGetSlot(_possessed, out int s))
        {
            Memories.Add(s, new Memory(Tick, MemoryKind.God, [MemoryStore.GodId, _possessed],
                "I lost time; my body moved without me.", 6), Tick);
            if (_possessReacted) ReactNearby(s, $"{Npcs.Name[s]} came back to themselves.", 4);
            Npcs.Phase[s] = ActionPhase.Done;
        }
        _possessed = Npcs.None;
        ClearPossessionRuntime();
    }

    /// <summary>Forgets the per-possession state (input, work batch, reaction flag). Also called after a load.</summary>
    private void ClearPossessionRuntime()
    {
        _possDirX = _possDirZ = 0;
        _possSprint = false;
        _possessReacted = false;
        _possWorkUntil = -1;
        _possWorkWith = Npcs.None;
        _possStore = -1;
        _possLastTalkWith = Npcs.None;
        _possLastTalkTick = long.MinValue;
    }

    private void PossessedInteract(int targetId, string verb)
    {
        if (!Npcs.TryGetSlot(_possessed, out int s)) { AddLog("interact rejected: nobody possessed"); return; }
        verb ??= "";
        if (verb == "work")
        {
            if (!StartWork(s, targetId)) AddLog($"interact rejected: nothing to work at near npc {_possessed}");
            return;
        }
        if (!Npcs.TryGetSlot(targetId, out int o) || o == s || Dist(Npcs.X[s], Npcs.Z[s], Npcs.X[o], Npcs.Z[o]) > InteractDistance * 2)
        {
            AddLog($"interact rejected: target {targetId} out of reach");
            return;
        }
        if (verb == "talk") PossessedTalk(s, o);
        else if (verb.StartsWith("give:", StringComparison.Ordinal)) PossessedGive(s, o, verb[5..]);
        else AddLog($"interact rejected: unknown verb '{verb}'");
    }

    /// <summary>Talk: the M1 conversation (social need, affinity, gossip, knowledge chatter) plus a shared memory.</summary>
    private void PossessedTalk(int s, int o)
    {
        Converse(s, o);
        int a = Npcs.IdOf(s), b = Npcs.IdOf(o);
        if (_possLastTalkWith == b && Tick - _possLastTalkTick < PossessedTalkMemoryTicks) return;
        _possLastTalkWith = b;
        _possLastTalkTick = Tick;
        Events.Publish(new SimEvent(Tick, MemoryKind.Conversation, $"{Npcs.Name[s]} came up and talked with {Npcs.Name[o]}.", 4,
            [a, b], Npcs.X[s], Npcs.Z[s], 0f));
    }

    /// <summary>Give: hands over up to two units of carried food, raising affinity and trust and leaving a debt.</summary>
    private void PossessedGive(int s, int o, string itemId)
    {
        if (itemId is not ("food" or "berries" or "berry" or "meat" or "bread"))
        {
            AddLog($"give rejected: unknown item '{itemId}'");
            return;
        }
        var n = Npcs;
        float units = MathF.Floor(Math.Min(n.Food[s], 2f));
        if (units < 1f) { AddLog($"give rejected: {n.Name[s]} carries no food"); return; }
        n.Food[s] -= units;
        if (StageOf(o) == LifeStage.Infant)
        {
            n.NeedRef(o, Need.Food) = Math.Min(1f, n.Need(o, Need.Food) + units * Config.FoodPerUnit);
            n.NeedRef(o, Need.Water) = Math.Min(1f, n.Need(o, Need.Water) + 0.4f);
        }
        else
        {
            float room = Math.Max(0f, Config.MaxCarriedFood - n.Food[o]);
            float kept = Math.Min(units, room);
            n.Food[o] += kept;
            // A full pantry eats the rest on the spot.
            if (units > kept) n.NeedRef(o, Need.Food) = Math.Min(1f, n.Need(o, Need.Food) + (units - kept) * Config.FoodPerUnit);
        }
        n.NeedRef(s, Need.Purpose) = Math.Min(1f, n.Need(s, Need.Purpose) + 0.1f);
        int a = n.IdOf(s), b = n.IdOf(o);
        Relationships.Adjust(a, b, 0.1f, 0.1f, -units); // same convention as GiveFood
        Events.Publish(new SimEvent(Tick, MemoryKind.Gift, $"{n.Name[s]} gave {n.Name[o]} {units:0} food.", 5,
            [a, b], n.X[s], n.Z[s], 0f));
    }

    // ---- per tick ---------------------------------------------------------------------------

    /// <summary>The possessed NPC's step, in place of Decide/Execute. Allocation-free except the first-tick reaction.</summary>
    private void TickPossessed(int s, LifeStage stage)
    {
        bool moving = _possDirX * _possDirX + _possDirZ * _possDirZ >= 0.0001f;
        if (!_possessReacted)
        {
            _possessReacted = true;
            string name = Npcs.Name[s];
            ReactNearby(s, moving
                ? $"{name} began walking oddly, as if someone else were moving their legs."
                : $"{name} stopped and stared, as if seeing everything for the first time.", 5);
        }
        if (_possWorkUntil > Tick)
        {
            if (!moving)
            {
                WorkTick(s, stage);
                return;
            }
            _possWorkUntil = -1; // walking away ends the shared work
            _possWorkWith = Npcs.None;
        }
        if (!moving)
        {
            BodyInstinct(s);
            if (Npcs.Phase[s] != ActionPhase.Acting) Npcs.Phase[s] = ActionPhase.Done;
            return;
        }
        MovePossessed(s, stage);
    }

    /// <summary>Speed of the possessed NPC in metres per tick; the client predicts with the same formula.</summary>
    public static float PossessedSpeed(SimConfig cfg, LifeStage stage, bool sprint)
    {
        float v = cfg.PossessedWalkSpeed * (sprint ? cfg.PossessedSprintFactor : 1f);
        return stage switch { LifeStage.Child => v * 0.7f, LifeStage.Elder => v * 0.8f, _ => v };
    }

    private void MovePossessed(int s, LifeStage stage)
    {
        float len = MathF.Sqrt(_possDirX * _possDirX + _possDirZ * _possDirZ);
        float dx = _possDirX / len, dz = _possDirZ / len;
        float step = PossessedSpeed(Config, stage, _possSprint) * Math.Min(1f, len);
        float x = Npcs.X[s], z = Npcs.Z[s];
        Npcs.Heading[s] = MathF.Atan2(dx, dz);
        Npcs.Action[s] = ActionKind.Explore;
        Npcs.Phase[s] = ActionPhase.Moving;
        float nx = x + dx * step, nz = z + dz * step;
        if (!Terrain.IsWalkable(nx, nz))
        {
            // Slide along a coastline on whichever axis is still walkable.
            if (Terrain.IsWalkable(nx, z)) nz = z;
            else if (Terrain.IsWalkable(x, nz)) nx = x;
            else return;
        }
        Npcs.X[s] = nx;
        Npcs.Z[s] = nz;
    }

    /// <summary>Standing still, the body still eats from what it carries and drinks at the water's edge when desperate.</summary>
    private void BodyInstinct(int s)
    {
        var n = Npcs;
        if (n.Need(s, Need.Food) < 0.2f && n.Food[s] >= 1f)
        {
            n.Food[s] -= 1f;
            n.NeedRef(s, Need.Food) = Math.Min(1f, n.Need(s, Need.Food) + Config.FoodPerUnit);
        }
        if (n.Need(s, Need.Water) < 0.2f)
        {
            int cell = Terrain.NearestWater[Terrain.CellIndex(n.X[s], n.Z[s])];
            if (cell >= 0 && Dist(n.X[s], n.Z[s], Terrain.CellX(cell), Terrain.CellZ(cell)) <= Terrain.CellSize * 3f)
                n.NeedRef(s, Need.Water) = 1f;
        }
    }

    // ---- work alongside ---------------------------------------------------------------------

    /// <summary>Joins the target NPC's job, else the nearest berry bush, else the own village's store, else a construction site.</summary>
    private bool StartWork(int s, int targetId)
    {
        _possWorkWith = Npcs.None;
        bool started = false;
        if (Npcs.TryGetSlot(targetId, out int o) && o != s
            && Dist(Npcs.X[s], Npcs.Z[s], Npcs.X[o], Npcs.Z[o]) <= Config.PerceptionRadius && JoinJob(s, o))
        {
            _possWorkWith = targetId;
            started = true;
            int a = Npcs.IdOf(s);
            Events.Publish(new SimEvent(Tick, MemoryKind.Conversation,
                $"{Npcs.Name[s]} joined {Npcs.Name[o]} at work, without a word.", 4, [a, targetId], Npcs.X[s], Npcs.Z[s], 0f));
        }
        else
        {
            float x = Npcs.X[s], z = Npcs.Z[s];
            int node = Resources.FindNearest(ResourceKind.Berry, x, z, 6f);
            if (node >= 0)
            {
                Npcs.Action[s] = ActionKind.Gather;
                Npcs.TargetNode[s] = node;
                Npcs.TargetNpc[s] = Npcs.None;
                _storeTrip[s] = -1;
                _targetBuilding[s] = -1;
                MoveTo(s, Resources.X[node], Resources.Z[node]);
                started = true;
            }
            else if (NearestOwnStore(s, PossessedStoreReach) is var store and >= 0)
            {
                // At a storehouse of the body's own village: take food out, so it has something to give.
                Npcs.Action[s] = ActionKind.Gather;
                Npcs.TargetNode[s] = Npcs.None;
                Npcs.TargetNpc[s] = Npcs.None;
                _storeTrip[s] = -1;
                _targetBuilding[s] = -1;
                _possStore = store;
                MoveTo(s, Buildings.X[store], Buildings.Z[store]);
                started = true;
            }
            else if (NearestSite(x, z, 10f) is var site and >= 0)
            {
                StartBuild(s, site);
                started = true;
            }
        }
        if (started) _possWorkUntil = Tick + Math.Max(1, Config.PossessedWorkTicks);
        return started;
    }

    /// <summary>Copies the target's current job (gather, hunt, build, farm). False if it is not working at anything.</summary>
    private bool JoinJob(int s, int o)
    {
        var n = Npcs;
        n.TargetNpc[s] = Npcs.None;
        _storeTrip[s] = -1;
        switch (n.Action[o])
        {
            case ActionKind.Gather or ActionKind.Hunt when n.TargetNode[o] >= 0 && _storeTrip[o] < 0:
            {
                int node = n.TargetNode[o];
                if (Resources.Amount[node] < 1f) return false;
                n.Action[s] = n.Action[o];
                n.TargetNode[s] = node;
                _targetBuilding[s] = -1;
                MoveTo(s, Resources.X[node], Resources.Z[node]);
                return true;
            }
            case ActionKind.Build when _targetBuilding[o] >= 0 && Buildings.Alive[_targetBuilding[o]]:
                StartBuild(s, _targetBuilding[o]);
                return true;
            case ActionKind.Farm when _targetBuilding[o] >= 0 && Buildings.Alive[_targetBuilding[o]]:
                n.Action[s] = ActionKind.Farm;
                n.TargetNode[s] = Npcs.None;
                _targetBuilding[s] = _targetBuilding[o];
                MoveTo(s, Buildings.X[_targetBuilding[o]], Buildings.Z[_targetBuilding[o]]);
                return true;
            default:
                return false;
        }
    }

    private void StartBuild(int s, int site)
    {
        Npcs.Action[s] = ActionKind.Build;
        Npcs.TargetNode[s] = Npcs.None; // labour only: material comes from the settlement's stock
        Npcs.TargetNpc[s] = Npcs.None;
        _targetBuilding[s] = site;
        MoveTo(s, Buildings.X[site], Buildings.Z[site]);
    }

    /// <summary>A finished storehouse of the NPC's own settlement within <paramref name="radius"/>, or -1.</summary>
    private int NearestOwnStore(int s, float radius)
    {
        int set = SettlementOfSlot(s);
        if (set < 0 || !Settlements.IsAlive(set)) return -1;
        var b = Buildings;
        int best = -1;
        float bestD = radius * radius;
        for (int i = 0; i < b.HighWater; i++)
        {
            if (!b.Alive[i] || b.Kind[i] != BuildingKinds.Storehouse || b.Settlement[i] != set || b.Progress[i] < 1f) continue;
            float d = Sq(b.X[i] - Npcs.X[s]) + Sq(b.Z[i] - Npcs.Z[s]);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Up to four units of the settlement's food into the body's hands (like an M3 store trip).</summary>
    private void TakeFromStore(int s, int store)
    {
        int set = Buildings.Settlement[store];
        if (!Settlements.IsAlive(set) || Dist(Npcs.X[s], Npcs.Z[s], Buildings.X[store], Buildings.Z[store]) > ArriveDistance + 2f) return;
        float take = MathF.Floor(Math.Min(Math.Min(4f, Settlements.Food[set]), Config.MaxCarriedFood - Npcs.Food[s]));
        if (take <= 0) { AddLog($"take rejected: the store of {Settlements.Name[set]} is empty"); return; }
        Settlements.Food[set] -= take;
        Npcs.Food[s] += take;
    }

    /// <summary>Any unfinished building within <paramref name="radius"/>, whoever's it is. -1 if none.</summary>
    private int NearestSite(float x, float z, float radius)
    {
        var b = Buildings;
        int best = -1;
        float bestD = radius * radius;
        for (int i = 0; i < b.HighWater; i++)
        {
            if (!b.Alive[i] || b.Progress[i] >= 1f) continue;
            float d = Sq(b.X[i] - x) + Sq(b.Z[i] - z);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    private void WorkTick(int s, LifeStage stage)
    {
        Execute(s, stage);
        if (Npcs.Phase[s] != ActionPhase.Done) return;
        if (_possStore >= 0)
        {
            TakeFromStore(s, _possStore);
            _possStore = -1;
            _possWorkUntil = -1;
            return;
        }
        // One cycle done: keep going with the partner while the batch lasts, else stop.
        if (Npcs.TryGetSlot(_possWorkWith, out int o) && JoinJob(s, o)) return;
        _possWorkUntil = -1;
        _possWorkWith = Npcs.None;
    }

    // ---- reactions --------------------------------------------------------------------------

    /// <summary>
    /// A memory for every non-infant within perception range who knows the NPC (kin, same settlement, or any
    /// relationship). Called on possession and release only, never per tick.
    /// </summary>
    private void ReactNearby(int s, string text, int importance)
    {
        int id = Npcs.IdOf(s);
        float x = Npcs.X[s], z = Npcs.Z[s], r = Config.PerceptionRadius;
        int settlement = SettlementOfSlot(s);
        Memory? memory = null;
        _grid.Range(x, z, r, out int bx0, out int bx1, out int bz0, out int bz1);
        for (int bz = bz0; bz <= bz1; bz++)
        for (int bx = bx0; bx <= bx1; bx++)
        for (int o = _grid.Head(bx, bz); o >= 0; o = _grid.Next(o))
        {
            if (o == s || !Npcs.Alive[o] || StageOf(o) == LifeStage.Infant) continue;
            if (Sq(Npcs.X[o] - x) + Sq(Npcs.Z[o] - z) > r * r) continue;
            if (!Knows(s, o, settlement)) continue;
            Memories.Add(o, memory ??= new Memory(Tick, MemoryKind.Conversation, [id], text, importance), Tick);
        }
    }

    private bool Knows(int s, int o, int settlement)
    {
        if (settlement >= 0 && SettlementOfSlot(o) == settlement) return true;
        if (IsKin(s, o) || Npcs.Partner[s] == Npcs.IdOf(o)) return true;
        var r = Relationships.Get(Npcs.IdOf(s), Npcs.IdOf(o));
        return r.Affinity != 0f || r.Trust != 0f || r.Debt != 0f;
    }
}
