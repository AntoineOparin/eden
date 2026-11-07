using Eden.Sim.Contracts;

namespace Eden.Sim;

public sealed partial class World
{
    /// <summary>One NPC, one tick: needs, survival, ageing, pregnancy, then behaviour.</summary>
    private void UpdateNpc(int s, bool night)
    {
        var cfg = Config;
        var n = Npcs;
        var stage = StageOf(s);
        float perTick = 1f / cfg.TicksPerDay;
        int nb = s * Needs.Count;

        float decay = 1f;
        if (n.BlessUntil[s] > Tick) decay = n.Blessing[s] > 0 ? 0.7f : 1.4f;
        float waterWeather = Weather switch { WeatherKind.Rain => 0.7f, WeatherKind.Drought => 1.4f, _ => 1f };
        bool sleeping = n.Action[s] == ActionKind.Rest && n.Phase[s] == ActionPhase.Acting;

        if (stage == LifeStage.Infant && TryGetCarer(s, out int carer, out bool nursing))
        {
            // Carried. A nursing mother feeds the infant from her own reserves.
            n.X[s] = n.X[carer];
            n.Z[s] = n.Z[carer];
            n.Heading[s] = n.Heading[carer];
            if (nursing && n.Needs[carer * Needs.Count] > 0.15f && n.Needs[carer * Needs.Count + 1] > 0.15f)
            {
                n.Needs[nb] = 1f;
                n.Needs[nb + 1] = 1f;
                n.Needs[carer * Needs.Count] -= cfg.FoodDecayPerDay * perTick * 0.4f;
                n.Needs[carer * Needs.Count + 1] -= cfg.WaterDecayPerDay * perTick * 0.4f;
            }
        }

        float stageFood = stage == LifeStage.Infant ? 0.8f : stage == LifeStage.Child ? 0.85f : 1f;
        n.Needs[nb] -= cfg.FoodDecayPerDay * perTick * decay * stageFood * (sleeping ? 0.6f : 1f);
        n.Needs[nb + 1] -= cfg.WaterDecayPerDay * perTick * decay * waterWeather * (sleeping ? 0.6f : 1f);
        if (!sleeping) n.Needs[nb + 2] -= cfg.RestDecayPerDay * perTick * decay;
        n.Needs[nb + 3] += night && !sleeping ? -cfg.WarmthDecayPerDay * perTick * 2f : perTick * 2f;
        n.Needs[nb + 4] += perTick * 0.6f; // safety recovers on its own
        n.Needs[nb + 5] -= cfg.SocialDecayPerDay * perTick * decay;
        n.Needs[nb + 6] -= cfg.PurposeDecayPerDay * perTick * decay;
        for (int i = 0; i < Needs.Count; i++) n.Needs[nb + i] = Math.Clamp(n.Needs[nb + i], 0f, 1f);

        // Survival windows.
        bool starving = n.Needs[nb] <= 0f, thirsty = n.Needs[nb + 1] <= 0f;
        n.StarveTicks[s] = starving ? n.StarveTicks[s] + 1 : 0;
        n.ThirstTicks[s] = thirsty ? n.ThirstTicks[s] + 1 : 0;
        if (n.StarveTicks[s] >= cfg.StarvationTicks) { Kill(s, DeathCause.Starvation); return; }
        if (n.ThirstTicks[s] >= cfg.DehydrationTicks) { Kill(s, DeathCause.Thirst); return; }

        // Health: worn down by hunger, thirst and cold, restored when needs are met.
        float h = n.Health[s];
        if (starving) h -= 1f / cfg.StarvationTicks;
        if (thirsty) h -= 1f / cfg.DehydrationTicks;
        if (n.Needs[nb + 3] <= 0f) h -= 0.0005f;
        if (!starving && !thirsty && n.Needs[nb] > 0.3f && n.Needs[nb + 1] > 0.3f)
            h += (n.BlessUntil[s] > Tick && n.Blessing[s] > 0 ? 0.004f : 0.002f);
        // Starvation and thirst kill through their windows above; health alone kills only through wounds.
        if (starving || thirsty) h = Math.Max(h, 0.05f);
        n.Health[s] = Math.Clamp(h, 0f, 1f);
        if (n.Health[s] <= 0f) { Kill(s, DeathCause.Wounds); return; }

        if ((Tick + s) % cfg.TicksPerDay == 0 && DailyDeathRoll(s, stage)) return;

        if (n.PregnantUntil[s] > 0 && Tick >= n.PregnantUntil[s]) _pendingBirths.Add(s);

        if (stage == LifeStage.Infant) { n.Phase[s] = ActionPhase.Done; return; }

        UpdateKnowledge(s, stage);

        if (_possessed != Npcs.None && n.IdOf(s) == _possessed) { TickPossessed(s, stage); return; } // M6: World.Possession.cs

        if (n.Phase[s] == ActionPhase.Done || (Tick + s) % cfg.DecisionIntervalTicks == 0) Decide(s, stage, night);
        Execute(s, stage);
    }

    /// <summary>Who carries an infant: the mother (nursing), else the father, else no one.</summary>
    private bool TryGetCarer(int s, out int carer, out bool nursing)
    {
        nursing = false;
        if (Npcs.TryGetSlot(Npcs.Mother[s], out carer)) { nursing = true; return true; }
        return Npcs.TryGetSlot(Npcs.Father[s], out carer);
    }

    private bool DailyDeathRoll(int s, LifeStage stage)
    {
        var cfg = Config;
        float years = AgeYears(s);
        float perYear = stage switch
        {
            LifeStage.Infant => cfg.InfantDeathPerYear,
            LifeStage.Child => cfg.ChildDeathPerYear,
            _ => cfg.AdultDeathPerYear,
        };
        float oldFrom = Npcs.LifespanYears[s] - 10f;
        float old = years > oldFrom ? 0.5f * Sq((years - oldFrom) / 10f) : 0f;
        if (Npcs.BlessUntil[s] > Tick && Npcs.Blessing[s] < 0) perYear += 0.1f;
        float p = (perYear + old) / cfg.DaysPerYear;
        if (!Rng.Chance(p)) return false;
        Kill(s, Rng.NextFloat() * (perYear + old) < old ? DeathCause.OldAge : DeathCause.Accident);
        return true;
    }

    private static float Sq(float v) => v * v;

    /// <summary>Create an adult with random genes. Used for genesis and god spawns.</summary>
    public int SpawnAdult(float x, float z, Sex sex, float ageYears)
    {
        int s = Npcs.Allocate();
        Memories.Reset(s);
        EnsureKnowledgeSlot(s);
        _knowledge[s]?.Clear();
        GrantStarterKnowledge(s);
        Npcs.X[s] = x;
        Npcs.Z[s] = z;
        Npcs.Sex[s] = sex;
        Npcs.BirthTick[s] = Tick - (long)(ageYears * Config.TicksPerYear);
        Genetics.Randomize(Npcs.GenesOf(s), ref Rng);
        Npcs.LifespanYears[s] = LifespanFor(s);
        Npcs.Name[s] = Names.Next(ref Rng, sex == Sex.Female);
        Npcs.Food[s] = 2;
        OnNpcCreated(s, born: false);
        return s;
    }

    private float LifespanFor(int s)
        => Config.BaseLifespanYears + (Npcs.Gene(s, Gene.Lifespan) - 0.5f) * 2f * Config.LifespanGeneRangeYears;

    private bool CanConceive(int female)
    {
        if (Npcs.Sex[female] != Sex.Female || Npcs.PregnantUntil[female] > 0) return false;
        float years = AgeYears(female);
        if (years < Config.AdultAgeYears || years > Config.FemaleFertileUntilYears) return false;
        return Tick - Npcs.LastBirth[female] >= (long)(Config.PostpartumYears * Config.TicksPerYear);
    }

    /// <summary>Conception roll after a courtship between partners. Child genes are fixed at conception.</summary>
    private void TryConceive(int a, int b)
    {
        int f = Npcs.Sex[a] == Sex.Female ? a : b, m = f == a ? b : a;
        if (Npcs.Sex[m] != Sex.Male || !CanConceive(f)) return;
        float fert = 0.4f + 0.6f * 0.5f * (Npcs.Gene(f, Gene.Fertility) + Npcs.Gene(m, Gene.Fertility));
        float age = AgeYears(f);
        float ageFactor = age > 35 ? Math.Max(0.1f, 1f - (age - 35f) / 12f) : 1f;
        float bless = 1f;
        if (Npcs.BlessUntil[f] > Tick) bless = Npcs.Blessing[f] > 0 ? 1.5f : 0.5f;
        if (!Rng.Chance(Config.ConceptionChance * fert * ageFactor * bless)) return;
        Genetics.Crossover(Npcs.GenesOf(f), Npcs.GenesOf(m), Npcs.UnbornOf(f), ref Rng, Config.MutationRate, Config.MutationSigma);
        Npcs.UnbornFather[f] = Npcs.IdOf(m);
        Npcs.PregnantUntil[f] = Tick + Math.Max(1, (long)(Config.PregnancyYears * Config.TicksPerYear));
    }

    private void GiveBirth(int mother)
    {
        if (!Npcs.Alive[mother] || Npcs.PregnantUntil[mother] == 0) return;
        int fatherId = Npcs.UnbornFather[mother];
        int motherId = Npcs.IdOf(mother);
        Npcs.PregnantUntil[mother] = 0;
        Npcs.LastBirth[mother] = Tick;

        var sex = Rng.Chance(0.5f) ? Sex.Female : Sex.Male;
        int c = Npcs.Allocate();
        Memories.Reset(c);
        EnsureKnowledgeSlot(c);
        _knowledge[c]?.Clear();
        KnowledgeState.ResetSlot(c);
        Npcs.GenesOf(c).Clear();
        Npcs.UnbornOf(mother).CopyTo(Npcs.GenesOf(c));
        Npcs.X[c] = Npcs.X[mother];
        Npcs.Z[c] = Npcs.Z[mother];
        Npcs.Sex[c] = sex;
        Npcs.BirthTick[c] = Tick;
        Npcs.LifespanYears[c] = LifespanFor(c);
        Npcs.Mother[c] = motherId;
        Npcs.Father[c] = fatherId;
        int lineage = Npcs.Lineage[mother];
        if (Npcs.TryGetSlot(fatherId, out int father))
        {
            lineage = Math.Max(lineage, Npcs.Lineage[father]);
            Npcs.Children[father]++;
        }
        Npcs.Lineage[c] = lineage + 1;
        MaxLineage = Math.Max(MaxLineage, lineage + 1);
        Npcs.Children[mother]++;
        Npcs.Name[c] = Names.Next(ref Rng, sex == Sex.Female);
        OnNpcCreated(c, born: true);
        TotalBirths++;

        int childId = Npcs.IdOf(c);
        string text = $"{Npcs.Name[mother]} gave birth to {(sex == Sex.Female ? "a daughter" : "a son")}, {Npcs.Name[c]}.";
        Events.Publish(new SimEvent(Tick, MemoryKind.Birth, text, 5, [motherId, childId, fatherId],
            Npcs.X[mother], Npcs.Z[mother], Config.PerceptionRadius, "birth", 3));

        if (Rng.Chance(Config.ChildbirthDeathChance)) Kill(mother, DeathCause.Childbirth);
    }

    /// <summary>Remove an NPC: Chronicle + witness memories, widow the partner, free the slot.</summary>
    public void Kill(int s, DeathCause cause)
    {
        if (!Npcs.Alive[s]) return;
        int id = Npcs.IdOf(s);
        string name = Npcs.Name[s];
        int age = (int)AgeYears(s);
        string how = cause switch
        {
            DeathCause.OldAge => $"died of old age at {age}",
            DeathCause.Starvation => "starved to death",
            DeathCause.Thirst => "died of thirst",
            DeathCause.Childbirth => "died in childbirth",
            DeathCause.Lightning => "was struck down by lightning",
            DeathCause.Wounds => "died of wounds",
            _ => age < 14 ? "died of a fever" : "died in an accident",
        };
        Events.Publish(new SimEvent(Tick, MemoryKind.Death, $"{name} {how}.", 6, [id],
            Npcs.X[s], Npcs.Z[s], Config.PerceptionRadius, "death", cause == DeathCause.Lightning ? 7 : 4));

        if (Npcs.TryGetSlot(Npcs.Partner[s], out int p)) Npcs.Partner[p] = Npcs.None;
        if (id == _possessed) _possessed = Npcs.None;
        Relationships.Forget(id);
        Memories.Reset(s);
        OnKnowledgeHolderDied(s);
        _knowledge[s]?.Clear();
        Npcs.Free(s);
        TotalDeaths++;
    }
}
