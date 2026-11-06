using Eden.Sim.Contracts;

namespace Eden.Sim;

public sealed partial class World
{
    private const float ArriveDistance = 2.5f;
    private const float InteractDistance = 3f;

    /// <summary>Targets found while building the decision context, reused when the chosen action starts.</summary>
    private struct Targets
    {
        public int FoodNode, GameNode, WaterCell, Mate, TalkTo, Dependent;

        // M3 (World.Society.cs): settlement store, construction site + material node, farm plot.
        public int Store, Site, MatNode, Plot;
    }

    // ---- decision ---------------------------------------------------------------------------

    private void Decide(int s, LifeStage stage, bool night)
    {
        var n = Npcs;
        var cfg = Config;
        int nb = s * Needs.Count, gb = s * Genetics.Count;
        bool adult = stage is LifeStage.Adult or LifeStage.Elder;
        float x = n.X[s], z = n.Z[s];

        var c = new DecisionContext
        {
            Food = n.Needs[nb], Water = n.Needs[nb + 1], Rest = n.Needs[nb + 2], Warmth = n.Needs[nb + 3],
            Safety = n.Needs[nb + 4], Social = n.Needs[nb + 5], Purpose = n.Needs[nb + 6],
            Strength = n.Genes[gb + (int)Gene.Strength], Curiosity = n.Genes[gb + (int)Gene.Curiosity],
            Sociability = n.Genes[gb + (int)Gene.Sociability], Aggression = n.Genes[gb + (int)Gene.Aggression],
            Fertility = n.Genes[gb + (int)Gene.Fertility], Empathy = n.Genes[gb + (int)Gene.Empathy],
            Piety = n.Genes[gb + (int)Gene.Piety], Ambition = n.Genes[gb + (int)Gene.Ambition],
            Stage = stage, CarriedFood = n.Food[s], MaxCarriedFood = cfg.MaxCarriedFood, IsNight = night,
        };
        var t = new Targets { FoodNode = -1, GameNode = -1, WaterCell = -1, Mate = -1, TalkTo = -1, Dependent = -1 };

        if (n.Food[s] < cfg.MaxCarriedFood * 0.95f)
        {
            t.FoodNode = Resources.FindNearest(ResourceKind.Berry, x, z, cfg.SearchRadius, 1f, n.AvoidNode[s], reachable: true);
            c.FoodKnown = t.FoodNode >= 0;
            if (c.FoodKnown) c.FoodDistance = Dist(x, z, Resources.X[t.FoodNode], Resources.Z[t.FoodNode]);
            if (adult)
            {
                t.GameNode = Resources.FindNearest(ResourceKind.Game, x, z, cfg.SearchRadius, 1f, n.AvoidNode[s], reachable: true);
                c.GameKnown = t.GameNode >= 0;
                if (c.GameKnown) c.GameDistance = Dist(x, z, Resources.X[t.GameNode], Resources.Z[t.GameNode]);
            }
        }
        t.WaterCell = Terrain.NearestWater[Terrain.CellIndex(x, z)];
        c.WaterKnown = t.WaterCell >= 0 && Dist(x, z, Terrain.CellX(t.WaterCell), Terrain.CellZ(t.WaterCell)) < cfg.SearchRadius * 2;

        if (adult)
        {
            t.Mate = FindMate(s);
            c.MateAvailable = t.Mate >= 0;
            c.HasPartner = n.Partner[s] != Npcs.None;
            if (c.HasPartner && t.Mate >= 0)
                c.CanConceive = n.Sex[s] == Sex.Female ? CanConceive(s) : CanConceive(t.Mate);
            if (n.Food[s] >= 1) { t.Dependent = FindHungryDependent(s); c.DependentHungry = t.Dependent >= 0; }
        }
        if (n.Needs[nb + 5] < 0.8f)
        {
            t.TalkTo = FindNearestNpc(s, cfg.PerceptionRadius);
            c.TalkPartnerNearby = t.TalkTo >= 0;
        }
        SocietyContext(s, stage, ref c, ref t);
        if (n.GoalUntil[s] > Tick) { c.Goal = n.GoalAction[s]; c.GoalBonus = n.GoalBonus[s]; }
        ApplyGoalTarget(s, ref c, ref t);

        Span<float> scores = stackalloc float[ActionScorer.ActionCount];
        ActionScorer.ScoreAll(in c, scores);
        var best = ActionScorer.Best(scores);

        var current = n.Action[s];
        if (n.Phase[s] != ActionPhase.Done)
        {
            float cur = scores[(int)current];
            n.ActionScore[s] = cur;
            if (best == current || scores[(int)best] < cur + cfg.Hysteresis) return;
        }
        Begin(s, best, scores[(int)best], in t, stage);
    }

    /// <summary>A goal aimed at a specific NPC makes that NPC the target for Talk/Court/Give.</summary>
    private void ApplyGoalTarget(int s, ref DecisionContext c, ref Targets t)
    {
        if (c.GoalBonus <= 0 || Npcs.GoalTarget[s] == Npcs.None) return;
        if (!Npcs.TryGetSlot(Npcs.GoalTarget[s], out int target)) return;
        switch (c.Goal)
        {
            case ActionKind.Talk: t.TalkTo = target; c.TalkPartnerNearby = true; break;
            case ActionKind.Give: t.Dependent = target; c.DependentHungry = true; break;
            case ActionKind.Court when IsEligibleMate(s, target): t.Mate = target; c.MateAvailable = true; break;
        }
    }

    private void Begin(int s, ActionKind action, float score, in Targets t, LifeStage stage)
    {
        var n = Npcs;
        n.Action[s] = action;
        n.ActionScore[s] = score;
        n.TargetNode[s] = Npcs.None;
        n.TargetNpc[s] = Npcs.None;
        float x = n.X[s], z = n.Z[s];
        if (BeginSociety(s, action, in t, stage)) return;
        switch (action)
        {
            case ActionKind.Eat:
                Act(s, 3);
                break;
            case ActionKind.Drink:
                MoveTo(s, Terrain.CellX(t.WaterCell), Terrain.CellZ(t.WaterCell));
                break;
            case ActionKind.Rest:
                Act(s, IsNightNow() ? 60 : 30);
                break;
            case ActionKind.Gather:
                n.TargetNode[s] = t.FoodNode;
                MoveTo(s, Resources.X[t.FoodNode], Resources.Z[t.FoodNode]);
                break;
            case ActionKind.Hunt:
                n.TargetNode[s] = t.GameNode;
                MoveTo(s, Resources.X[t.GameNode], Resources.Z[t.GameNode]);
                break;
            case ActionKind.Court:
                FollowNpc(s, t.Mate);
                break;
            case ActionKind.Talk:
                FollowNpc(s, t.TalkTo);
                break;
            case ActionKind.Give:
                FollowNpc(s, t.Dependent);
                break;
            case ActionKind.Flee:
            {
                float dx = x - n.ThreatX[s], dz = z - n.ThreatZ[s];
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d < 0.01f) { float a = Rng.NextFloat() * MathF.Tau; dx = MathF.Cos(a); dz = MathF.Sin(a); d = 1; }
                if (!TryMoveTowardWalkable(s, x + dx / d * 40f, z + dz / d * 40f)) Act(s, 5);
                break;
            }
            case ActionKind.Explore:
            {
                bool found = false;
                for (int i = 0; i < 6 && !found; i++)
                {
                    float a = Rng.NextFloat() * MathF.Tau, r = 30f + Rng.NextFloat() * 50f;
                    found = TryMoveTowardWalkable(s, x + MathF.Cos(a) * r, z + MathF.Sin(a) * r);
                }
                if (!found) Act(s, 5);
                break;
            }
            case ActionKind.Pray:
                Act(s, 12);
                break;
            default:
                // Idle: children drift back to whoever looks after them.
                if (stage == LifeStage.Child && TryGetCarer(s, out int carer, out _)
                    && Dist(x, z, n.X[carer], n.Z[carer]) > 8f)
                    MoveTo(s, n.X[carer] + Rng.NextFloat() * 4 - 2, n.Z[carer] + Rng.NextFloat() * 4 - 2);
                else if (stage != LifeStage.Child && n.TryGetSlot(n.Partner[s], out int partner)
                    && Dist(x, z, n.X[partner], n.Z[partner]) > 12f)
                    MoveTo(s, n.X[partner] + Rng.NextFloat() * 4 - 2, n.Z[partner] + Rng.NextFloat() * 4 - 2);
                else Act(s, 5);
                break;
        }
    }

    private bool IsNightNow()
    {
        float hour = (float)(Tick % Config.TicksPerDay) / Config.TicksPerHour;
        return hour >= 21f || hour < 5f;
    }

    private void Act(int s, int ticks)
    {
        Npcs.Phase[s] = ActionPhase.Acting;
        Npcs.ActTicks[s] = ticks;
    }

    private void MoveTo(int s, float x, float z)
    {
        Npcs.TargetX[s] = x;
        Npcs.TargetZ[s] = z;
        Npcs.Phase[s] = ActionPhase.Moving;
        // While moving, ActTicks is the walking budget: generous for a straight line on rough ground,
        // so running out means the NPC is stuck against a coastline.
        Npcs.ActTicks[s] = (int)(Dist(Npcs.X[s], Npcs.Z[s], x, z) / (Config.WalkSpeed * 0.25f)) + 30;
        PlanPath(s, x, z);
    }

    private void FollowNpc(int s, int targetSlot)
    {
        Npcs.TargetNpc[s] = Npcs.IdOf(targetSlot);
        MoveTo(s, Npcs.X[targetSlot], Npcs.Z[targetSlot]);
    }

    private bool TryMoveTowardWalkable(int s, float x, float z)
    {
        x = Math.Clamp(x, 1f, Terrain.MaxX - 1f);
        z = Math.Clamp(z, 1f, Terrain.MaxZ - 1f);
        if (!Terrain.IsLand(Terrain.CellIndex(x, z)) || !Terrain.LineWalkable(Npcs.X[s], Npcs.Z[s], x, z)) return false;
        MoveTo(s, x, z);
        return true;
    }

    // ---- execution --------------------------------------------------------------------------

    private void Execute(int s, LifeStage stage)
    {
        var n = Npcs;
        switch (n.Phase[s])
        {
            case ActionPhase.Moving:
            {
                if (n.TargetNpc[s] != Npcs.None)
                {
                    if (!n.TryGetSlot(n.TargetNpc[s], out int other)) { n.Phase[s] = ActionPhase.Done; return; }
                    n.TargetX[s] = n.X[other];
                    n.TargetZ[s] = n.Z[other];
                }
                if (--n.ActTicks[s] < 0)
                {
                    if (n.TargetNode[s] != Npcs.None) n.AvoidNode[s] = n.TargetNode[s];
                    n.Phase[s] = ActionPhase.Done;
                    return;
                }
                float arrive = n.TargetNpc[s] != Npcs.None ? InteractDistance - 0.5f : ArriveDistance;
                if (Step(s, stage, n.TargetX[s], n.TargetZ[s], arrive))
                    Act(s, ActDuration(n.Action[s]));
                break;
            }
            case ActionPhase.Acting:
                if (n.Action[s] == ActionKind.Rest)
                {
                    n.NeedRef(s, Need.Rest) = Math.Min(1f, n.Need(s, Need.Rest) + 0.02f);
                    n.NeedRef(s, Need.Warmth) = Math.Min(1f, n.Need(s, Need.Warmth) + 0.01f);
                    if (n.Need(s, Need.Rest) >= 1f && !IsNightNow()) n.ActTicks[s] = 0;
                }
                if (--n.ActTicks[s] <= 0)
                {
                    n.Phase[s] = ActionPhase.Done;
                    Complete(s, stage);
                }
                break;
        }
    }

    private static int ActDuration(ActionKind a) => a switch
    {
        ActionKind.Drink => 3,
        ActionKind.Gather => 6,
        ActionKind.Hunt => 12,
        ActionKind.Court => 8,
        ActionKind.Talk => 6,
        ActionKind.Give => 2,
        ActionKind.Rest => 40,
        ActionKind.Build => 12,
        ActionKind.Farm => 10,
        ActionKind.Trade => 4,
        ActionKind.Raid => 6,
        ActionKind.Settle => 2,
        _ => 1,
    };

    /// <summary>Straight-line step on the cost field. Returns true on arrival; a blocked step ends the action.</summary>
    private bool Step(int s, LifeStage stage, float tx, float tz, float arrive)
    {
        if (FollowPath(s, stage)) return false;
        var n = Npcs;
        float x = n.X[s], z = n.Z[s];
        float dx = tx - x, dz = tz - z;
        float d = MathF.Sqrt(dx * dx + dz * dz);
        if (d <= arrive) return true;
        float speed = Speed(s, stage) / Terrain.MoveCost[Terrain.CellIndex(x, z)];
        float step = Math.Min(speed, d);
        float nx = x + dx / d * step, nz = z + dz / d * step;
        n.Heading[s] = MathF.Atan2(dx, dz);
        if (!Terrain.IsWalkable(nx, nz))
        {
            // Shorelines: water targets sit in the sea, so being blocked right next to one counts as arriving.
            if (d <= Terrain.CellSize * 2.5f) return true;
            // Slide along the obstacle on the dominant axis, then the other one.
            float sx = MathF.Sign(dx) * step, sz = MathF.Sign(dz) * step;
            bool xFirst = MathF.Abs(dx) >= MathF.Abs(dz);
            if (Terrain.IsWalkable(xFirst ? x + sx : x, xFirst ? z : z + sz)) { nx = xFirst ? x + sx : x; nz = xFirst ? z : z + sz; }
            else if (Terrain.IsWalkable(xFirst ? x : x + sx, xFirst ? z + sz : z)) { nx = xFirst ? x : x + sx; nz = xFirst ? z + sz : z; }
            else
            {
                // Truly stuck: forget this resource for a while so the next decision picks another.
                if (n.TargetNode[s] != Npcs.None) n.AvoidNode[s] = n.TargetNode[s];
                n.Phase[s] = ActionPhase.Done;
                return false;
            }
        }
        n.X[s] = nx;
        n.Z[s] = nz;
        return d - step <= arrive;
    }

    private float Speed(int s, LifeStage stage)
    {
        float v = Config.WalkSpeed * (0.8f + 0.4f * Npcs.Gene(s, Gene.Stamina));
        return stage switch { LifeStage.Child => v * 0.7f, LifeStage.Elder => v * 0.8f, _ => v };
    }

    private void Complete(int s, LifeStage stage)
    {
        var n = Npcs;
        var cfg = Config;
        if (CompleteSociety(s, stage)) return;
        switch (n.Action[s])
        {
            case ActionKind.Eat:
            {
                float want = (1f - n.Need(s, Need.Food)) / cfg.FoodPerUnit;
                float units = Math.Min(n.Food[s], Math.Max(1f, MathF.Ceiling(want - 0.25f)));
                n.Food[s] -= units;
                n.NeedRef(s, Need.Food) = Math.Min(1f, n.Need(s, Need.Food) + units * cfg.FoodPerUnit);
                break;
            }
            case ActionKind.Drink:
                n.NeedRef(s, Need.Water) = 1f;
                break;
            case ActionKind.Gather:
            {
                int node = n.TargetNode[s];
                if (node < 0 || Resources.Amount[node] < 1f) break;
                float take = MathF.Floor(Math.Min(Resources.Amount[node], 3f + 2f * n.GatherSkill[s]));
                take = Math.Min(take, cfg.MaxCarriedFood - n.Food[s]);
                if (stage == LifeStage.Child) take = Math.Min(take, 2f);
                if (take <= 0) break;
                Resources.Amount[node] -= take;
                n.Food[s] += take;
                n.GatherSkill[s] += 0.02f * (1f - n.GatherSkill[s]);
                n.NeedRef(s, Need.Purpose) = Math.Min(1f, n.Need(s, Need.Purpose) + 0.15f);
                break;
            }
            case ActionKind.Hunt:
            {
                int node = n.TargetNode[s];
                if (node < 0 || Resources.Amount[node] < 1f) break;
                float chance = 0.4f + 0.35f * n.Gene(s, Gene.Strength) + 0.25f * n.HuntSkill[s];
                n.HuntSkill[s] += 0.03f * (1f - n.HuntSkill[s]);
                n.NeedRef(s, Need.Purpose) = Math.Min(1f, n.Need(s, Need.Purpose) + 0.25f);
                if (Rng.Chance(0.03f * (1.2f - n.Gene(s, Gene.Strength))))
                    n.Health[s] = Math.Max(0f, n.Health[s] - 0.3f);
                if (!Rng.Chance(chance)) break;
                Resources.Amount[node] -= 1f;
                n.Food[s] = Math.Min(cfg.MaxCarriedFood, n.Food[s] + 5f);
                break;
            }
            case ActionKind.Court:
                CompleteCourt(s);
                break;
            case ActionKind.Talk:
                if (TargetInReach(s, out int other)) Converse(s, other);
                break;
            case ActionKind.Give:
                if (TargetInReach(s, out int child)) GiveFood(s, child);
                break;
            case ActionKind.Flee:
                n.NeedRef(s, Need.Safety) = Math.Min(1f, n.Need(s, Need.Safety) + 0.2f);
                break;
            case ActionKind.Explore:
                n.NeedRef(s, Need.Purpose) = Math.Min(1f, n.Need(s, Need.Purpose) + 0.05f);
                break;
            case ActionKind.Pray:
                n.NeedRef(s, Need.Purpose) = Math.Min(1f, n.Need(s, Need.Purpose) + 0.35f);
                n.NeedRef(s, Need.Safety) = Math.Min(1f, n.Need(s, Need.Safety) + 0.1f);
                break;
        }
    }

    private bool TargetInReach(int s, out int other)
    {
        if (!Npcs.TryGetSlot(Npcs.TargetNpc[s], out other)) return false;
        return Dist(Npcs.X[s], Npcs.Z[s], Npcs.X[other], Npcs.Z[other]) <= InteractDistance + 1f;
    }

    private void CompleteCourt(int s)
    {
        var n = Npcs;
        if (!TargetInReach(s, out int o)) return;
        int a = n.IdOf(s), b = n.IdOf(o);
        n.NeedRef(s, Need.Social) = Math.Min(1f, n.Need(s, Need.Social) + 0.35f);
        n.NeedRef(o, Need.Social) = Math.Min(1f, n.Need(o, Need.Social) + 0.35f);
        if (n.Partner[s] == b)
        {
            TryConceive(s, o);
            return;
        }
        if (n.Partner[s] != Npcs.None || n.Partner[o] != Npcs.None) return;
        float warmth = 0.08f + 0.12f * 0.5f * (n.Gene(s, Gene.Sociability) + n.Gene(o, Gene.Sociability));
        Relationships.Adjust(a, b, warmth, 0.05f);
        if (Relationships.Get(a, b).Affinity < 0.45f) return;
        n.Partner[s] = b;
        n.Partner[o] = a;
        Events.Publish(new SimEvent(Tick, MemoryKind.Courtship, $"{n.Name[s]} and {n.Name[o]} became partners.", 5,
            [a, b], n.X[s], n.Z[s], Config.PerceptionRadius, "pairing", 3));
    }

    /// <summary>Talk: social need, affinity, and gossip of one high-importance memory with a distortion roll.</summary>
    private void Converse(int s, int o)
    {
        var n = Npcs;
        n.NeedRef(s, Need.Social) = Math.Min(1f, n.Need(s, Need.Social) + 0.35f);
        n.NeedRef(o, Need.Social) = Math.Min(1f, n.Need(o, Need.Social) + 0.25f);
        Relationships.Adjust(n.IdOf(s), n.IdOf(o), 0.04f, 0.03f);
        Gossip(s, o);
    }

    private void Gossip(int speaker, int listener)
    {
        var mems = Memories.Of(speaker);
        if (mems.Count == 0) return;
        // Look at a few random memories; share the first important one the listener does not know.
        for (int tries = 0; tries < 3; tries++)
        {
            var m = mems[Rng.NextInt(mems.Count)];
            if (m.Importance < 6 || m.Kind == MemoryKind.Gossip || Memories.Contains(listener, m)) continue;
            var subjects = m.Subjects;
            int importance = m.Importance;
            float roll = Rng.NextFloat();
            if (roll < 0.15f && subjects.Length > 0)
            {
                // Subject swap: the story now names whoever told it.
                subjects = (int[])subjects.Clone();
                subjects[0] = Npcs.IdOf(speaker);
            }
            else if (roll < 0.35f) importance = Math.Clamp(importance + (Rng.Chance(0.5f) ? 1 : -1), 1, 10);
            string text = $"{Npcs.Name[speaker]} told me: {m.Text}";
            Memories.Add(listener, new Memory(Tick, MemoryKind.Gossip, subjects, text, importance), Tick);
            return;
        }
    }

    private void GiveFood(int s, int o)
    {
        var n = Npcs;
        float units = Math.Min(n.Food[s], 2f);
        if (units <= 0) return;
        n.Food[s] -= units;
        var stage = StageOf(o);
        if (stage == LifeStage.Infant)
        {
            // Infants are fed by hand; water goes with it.
            n.NeedRef(o, Need.Food) = Math.Min(1f, n.Need(o, Need.Food) + units * Config.FoodPerUnit);
            n.NeedRef(o, Need.Water) = Math.Min(1f, n.Need(o, Need.Water) + 0.4f);
        }
        else n.Food[o] = Math.Min(Config.MaxCarriedFood, n.Food[o] + units);
        n.NeedRef(s, Need.Purpose) = Math.Min(1f, n.Need(s, Need.Purpose) + 0.1f);
        if (!IsKin(s, o))
        {
            Relationships.Adjust(n.IdOf(s), n.IdOf(o), 0.1f, 0.1f, -units);
            var m = new Memory(Tick, MemoryKind.Gift, [n.IdOf(s), n.IdOf(o)], $"{n.Name[s]} gave me food.", 4);
            Memories.Add(o, m, Tick);
        }
    }

    // ---- searches (allocation-free) ---------------------------------------------------------

    private bool IsKin(int a, int b)
    {
        var n = Npcs;
        int ia = n.IdOf(a), ib = n.IdOf(b);
        if (n.Mother[a] == ib || n.Father[a] == ib || n.Mother[b] == ia || n.Father[b] == ia) return true;
        return (n.Mother[a] != Npcs.None && n.Mother[a] == n.Mother[b]) || (n.Father[a] != Npcs.None && n.Father[a] == n.Father[b]);
    }

    private bool IsParentChild(int a, int b)
    {
        var n = Npcs;
        int ia = n.IdOf(a), ib = n.IdOf(b);
        return n.Mother[a] == ib || n.Father[a] == ib || n.Mother[b] == ia || n.Father[b] == ia;
    }

    private bool IsEligibleMate(int s, int o)
    {
        var n = Npcs;
        if (o == s || n.Sex[o] == n.Sex[s] || IsParentChild(s, o)) return false;
        var st = StageOf(o);
        if (st is not (LifeStage.Adult or LifeStage.Elder)) return false;
        return n.Partner[o] == Npcs.None || n.Partner[o] == n.IdOf(s);
    }

    /// <summary>The partner if alive, else the nearest unpaired eligible adult (non-siblings preferred).</summary>
    private int FindMate(int s)
    {
        var n = Npcs;
        if (n.Partner[s] != Npcs.None)
            return n.TryGetSlot(n.Partner[s], out int p) ? p : -1;
        float x = n.X[s], z = n.Z[s], r = Config.SearchRadius;
        float best = float.MaxValue;
        int bestSlot = -1;
        _grid.Range(x, z, r, out int bx0, out int bx1, out int bz0, out int bz1);
        for (int bz = bz0; bz <= bz1; bz++)
        for (int bx = bx0; bx <= bx1; bx++)
        for (int o = _grid.Head(bx, bz); o >= 0; o = _grid.Next(o))
        {
            if (!n.Alive[o] || !IsEligibleMate(s, o)) continue;
            float d = Dist(x, z, n.X[o], n.Z[o]);
            if (d > r) continue;
            if (IsKin(s, o)) d += 60f;
            if (d < best) { best = d; bestSlot = o; }
        }
        return bestSlot;
    }

    private int FindNearestNpc(int s, float radius)
    {
        var n = Npcs;
        float x = n.X[s], z = n.Z[s];
        float best = radius;
        int bestSlot = -1;
        _grid.Range(x, z, radius, out int bx0, out int bx1, out int bz0, out int bz1);
        for (int bz = bz0; bz <= bz1; bz++)
        for (int bx = bx0; bx <= bx1; bx++)
        for (int o = _grid.Head(bx, bz); o >= 0; o = _grid.Next(o))
        {
            if (o == s || !n.Alive[o] || StageOf(o) == LifeStage.Infant) continue;
            float d = Dist(x, z, n.X[o], n.Z[o]);
            if (d < best) { best = d; bestSlot = o; }
        }
        return bestSlot;
    }

    /// <summary>Nearest hungry child or infant this adult is kin to (or carries).</summary>
    private int FindHungryDependent(int s)
    {
        var n = Npcs;
        float x = n.X[s], z = n.Z[s], r = Config.SearchRadius;
        float best = float.MaxValue;
        int bestSlot = -1;
        _grid.Range(x, z, r, out int bx0, out int bx1, out int bz0, out int bz1);
        for (int bz = bz0; bz <= bz1; bz++)
        for (int bx = bx0; bx <= bx1; bx++)
        for (int o = _grid.Head(bx, bz); o >= 0; o = _grid.Next(o))
        {
            if (o == s || !n.Alive[o]) continue;
            var st = StageOf(o);
            if (st is LifeStage.Adult or LifeStage.Elder) continue;
            // Children hold food themselves; feed them only when they are hungry and empty-handed.
            if (n.Need(o, Need.Food) > 0.45f || (st == LifeStage.Child && n.Food[o] >= 1f)) continue;
            if (!IsKin(s, o)) continue;
            float d = Dist(x, z, n.X[o], n.Z[o]);
            if (d < best) { best = d; bestSlot = o; }
        }
        return bestSlot;
    }

    private static float Dist(float ax, float az, float bx, float bz)
    {
        float dx = ax - bx, dz = az - bz;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
