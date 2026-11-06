using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// M4 knowledge: the live <see cref="ConceptGraph"/>, honing, transmission (chatter, teaching, apprenticeship,
/// written records), discovery rolls, inventions, and the knowledge-gated actions (teach, learn, craft, invent).
/// Proficiencies live in the per-slot dictionaries declared in World.Commands.cs; nothing here iterates those
/// dictionaries in an order-dependent way (dictionary order is not stable across a save/load), every scan
/// walks the graph in index order instead.
/// </summary>
public sealed partial class World
{
    /// <summary>The live concept graph: shipped concepts plus accepted inventions.</summary>
    public ConceptGraph Graph { get; private set; } = null!;

    /// <summary>Written records; they keep knowledge alive after its holders die.</summary>
    public RecordStore Records { get; } = new();

    /// <summary>Where unlockBuilding effects are sent. M3 sets its building registry here.</summary>
    public IBuildingUnlocks BuildingUnlocks { get; set; } = NoBuildingUnlocks.Instance;

    internal KnowledgeState KnowledgeState { get; } = new();

    private bool _knowledgeLoading;

    private const string WritingId = "writing";
    private const string PictographsId = "pictographs";
    private const string ApprenticeshipId = "apprenticeship";

    private static readonly uint GatedActions = ConceptGraph.Bit(ActionKind.Build) | ConceptGraph.Bit(ActionKind.Farm)
        | ConceptGraph.Bit(ActionKind.Craft) | ConceptGraph.Bit(ActionKind.Teach) | ConceptGraph.Bit(ActionKind.Learn)
        | ConceptGraph.Bit(ActionKind.Trade) | ConceptGraph.Bit(ActionKind.Raid) | ConceptGraph.Bit(ActionKind.Settle)
        | ConceptGraph.Bit(ActionKind.Invent);

    // ---- hooks called from the shared files -------------------------------------------------

    /// <summary>World constructor: load the shipped graph (parsed once per process) into this world.</summary>
    private void InitKnowledge()
    {
        Graph = ConceptGraph.Load();
        _concepts.Clear();
        foreach (var n in Graph.Nodes) _concepts[n.Id] = n.Concept;
    }

    /// <summary>SpawnAdult: people who appear from nothing know the starter concepts.</summary>
    private void GrantStarterKnowledge(int s)
    {
        KnowledgeState.ResetSlot(s);
        foreach (var id in ConceptGraph.StarterSet)
            if (Graph.Contains(id)) SetKnowledge(s, id, Config.StarterProficiency);
    }

    /// <summary>SetKnowledge: keep holder counts and unlock masks in step, apply threshold effects.</summary>
    private void AfterKnowledgeSet(int slot, string conceptId, float before)
    {
        if (_knowledgeLoading || Graph is null || !Graph.TryIndex(conceptId, out int i)) return;
        Bookkeep(slot, i, before, Proficiency(slot, conceptId));
    }

    /// <summary>Kill: the dead take their knowledge with them unless someone else has it or it is written down.</summary>
    private void OnKnowledgeHolderDied(int s)
    {
        if (s < _knowledge.Length && _knowledge[s] is { Count: > 0 } k)
        {
            for (int i = 0; i < Graph.Count; i++)
            {
                var node = Graph[i];
                if (!k.TryGetValue(node.Id, out float p) || p <= 0) continue;
                Graph.AddHolder(i, -1);
                if (!Graph.KnownAnywhere(i))
                    Chronicle(Tick, "knowledge-lost", $"With {Npcs.Name[s]} died the last knowledge of {Lower(node.Name)}.", 6, [Npcs.IdOf(s)]);
            }
        }
        KnowledgeState.ResetSlot(s);
    }

    /// <summary>Accepted InventionIntention: insert into the live graph before the inventor's knowledge is set.</summary>
    private void OnInvention(ConceptProposal concept)
    {
        if (!Graph.Contains(concept.Id)) Graph.AddInvention(concept);
    }

    /// <summary>IntentionValidator: the extra invention rules of 50-knowledge-data.md.</summary>
    internal string? RejectInventionRules(int slot, ConceptProposal c)
        => Graph.RejectInvention(c, id => Proficiency(slot, id), Config.InventionMastery);

    /// <summary>UpdateNpc, every tick for non-infants: staggered discovery rolls and the daily knowledge pass.</summary>
    private void UpdateKnowledge(int s, LifeStage stage)
    {
        var cfg = Config;
        if (stage is LifeStage.Adult or LifeStage.Elder && cfg.DiscoveryIntervalTicks > 0
            && (Tick + s) % cfg.DiscoveryIntervalTicks == 0)
            DiscoveryRoll(s, 1f);
        if ((Tick + s + cfg.TicksPerDay / 2) % cfg.TicksPerDay == 0) KnowledgeDay(s, stage);
    }

    /// <summary>Converse (Talk): proximity chatter, slow, in both directions.</summary>
    private void ShareKnowledge(int s, int o)
    {
        Chatter(s, o);
        if (Rng.Chance(0.5f)) Chatter(o, s);
    }

    /// <summary>Decide, after the M1 scorer: gate knowledge actions and score teach, learn, craft and invent.</summary>
    private void ScoreKnowledge(int s, LifeStage stage, Span<float> scores)
    {
        var ks = KnowledgeState;
        ks.EnsureSlots(s);
        uint unlocked = ks.UnlockedActions[s];
        if (Config.KnowledgeGatesActions)
            for (int a = 0; a < scores.Length; a++)
                if ((GatedActions & (1u << a)) != 0 && (unlocked & (1u << a)) == 0) scores[a] = 0;
        if ((unlocked & (ConceptGraph.Bit(ActionKind.Teach) | ConceptGraph.Bit(ActionKind.Learn)
            | ConceptGraph.Bit(ActionKind.Craft) | ConceptGraph.Bit(ActionKind.Invent))) == 0) return;

        var n = Npcs;
        bool adult = stage is LifeStage.Adult or LifeStage.Elder;
        float food = n.Need(s, Need.Food), water = n.Need(s, Need.Water);
        float comfort = Math.Min(1, food * 2.5f) * Math.Min(1, water * 2.5f);
        if (comfort <= 0.2f) return;
        float dPurpose = 1 - n.Need(s, Need.Purpose), dSocial = 1 - n.Need(s, Need.Social);
        float curiosity = n.Gene(s, Gene.Curiosity);
        float bonus = n.GoalUntil[s] > Tick ? n.GoalBonus[s] : 0f;
        var goal = n.GoalAction[s];
        // Only look for pupils and teachers when it could matter; the searches are the costly part.
        bool restless = dPurpose > 0.25f || dSocial > 0.3f || bonus > 0;

        if (adult && restless && Has(unlocked, ActionKind.Teach) && FindLesson(s, teaching: true, out int pupil, out int c1))
        {
            ks.TargetSlot[s] = pupil;
            ks.TargetConcept[s] = c1;
            scores[(int)ActionKind.Teach] = (0.1f + 0.35f * dPurpose + 0.15f * dSocial + 0.1f * n.Gene(s, Gene.Empathy)) * comfort
                + (goal == ActionKind.Teach ? bonus : 0);
        }
        if (restless && Has(unlocked, ActionKind.Learn) && FindLesson(s, teaching: false, out int teacher, out int c2))
        {
            scores[(int)ActionKind.Learn] = (0.08f + 0.35f * curiosity * dPurpose + 0.1f * dSocial) * comfort
                + (goal == ActionKind.Learn ? bonus : 0);
            if (scores[(int)ActionKind.Learn] > scores[(int)ActionKind.Teach]) { ks.TargetSlot[s] = teacher; ks.TargetConcept[s] = c2; }
        }
        if (adult && Has(unlocked, ActionKind.Craft) && FindCraft(s, out int tool, out int node))
        {
            ks.TargetTool[s] = tool;
            ks.TargetNode[s] = node;
            scores[(int)ActionKind.Craft] = (0.1f + 0.35f * dPurpose + 0.1f * n.Gene(s, Gene.Ambition)) * comfort
                + (goal == ActionKind.Craft ? bonus : 0);
        }
        if (adult && Has(unlocked, ActionKind.Invent) && ks.Candidates[s] > 0)
            scores[(int)ActionKind.Invent] = (0.04f + 0.3f * curiosity * dPurpose) * comfort + (goal == ActionKind.Invent ? bonus : 0);
    }

    /// <summary>Begin: start a teach, learn, craft or invent action chosen by <see cref="ScoreKnowledge"/>.</summary>
    private void BeginKnowledgeAction(int s, ActionKind action)
    {
        var ks = KnowledgeState;
        switch (action)
        {
            case ActionKind.Teach or ActionKind.Learn:
            {
                int other = ks.TargetSlot[s];
                if (other >= 0 && other < Npcs.HighWater && Npcs.Alive[other] && other != s) FollowNpc(s, other);
                else Act(s, 1);
                break;
            }
            case ActionKind.Craft:
            {
                int node = ks.TargetNode[s];
                if (node < 0 || node >= Resources.Count) { Act(s, 1); break; }
                Npcs.TargetNode[s] = node;
                MoveTo(s, Resources.X[node], Resources.Z[node]);
                break;
            }
            default:
                Act(s, 20);
                break;
        }
    }

    /// <summary>Complete, for every finished action: hone the concepts it practises, then finish knowledge actions.</summary>
    private void CompleteKnowledge(int s, ActionKind action)
    {
        Hone(s, action);
        var n = Npcs;
        var ks = KnowledgeState;
        switch (action)
        {
            case ActionKind.Teach when TargetInReach(s, out int pupil):
                Lesson(s, pupil, ks.TargetConcept[s], Config.TeachRate * (0.5f + n.Gene(pupil, Gene.Curiosity)));
                n.NeedRef(s, Need.Purpose) = Math.Min(1f, n.Need(s, Need.Purpose) + 0.3f);
                break;
            case ActionKind.Learn when TargetInReach(s, out int teacher):
                Lesson(teacher, s, ks.TargetConcept[s], Config.TeachRate * 0.7f * (0.5f + n.Gene(s, Gene.Curiosity)));
                n.NeedRef(s, Need.Purpose) = Math.Min(1f, n.Need(s, Need.Purpose) + 0.25f);
                break;
            case ActionKind.Craft:
                CraftTool(s);
                break;
            case ActionKind.Invent:
                n.NeedRef(s, Need.Purpose) = Math.Min(1f, n.Need(s, Need.Purpose) + 0.3f);
                DiscoveryRoll(s, Config.InventBoost);
                break;
        }
    }

    /// <summary>Yield multiplier for an NPC harvesting an M1 node kind (yieldMod effects and owned tools).</summary>
    public float KnowledgeYieldFactor(int slot, ResourceKind kind) => EffectApplier.YieldFactor(this, slot, kind);

    /// <summary>For M3: can this NPC build <paramref name="buildingId"/>?</summary>
    public bool CanBuild(int slot, string buildingId) => EffectApplier.CanBuild(this, slot, buildingId);

    /// <summary>
    /// One deliberate lesson, outside the Teach action (for M3 jobs, the Mind, tests). Same rules as the action:
    /// the teacher has Teach unlocked and knows the concept well, and the pair are kin or trust each other.
    /// </summary>
    public bool TryTeach(int teacherId, int pupilId, string conceptId)
    {
        if (!Npcs.TryGetSlot(teacherId, out int t) || !Npcs.TryGetSlot(pupilId, out int p) || t == p) return false;
        if (!Graph.TryIndex(conceptId, out int i) || !Has(KnowledgeState.UnlockedActions[t], ActionKind.Teach)) return false;
        if (!IsKin(t, p) && Relationships.Get(teacherId, pupilId).Trust < Config.TeachTrustMin) return false;
        if (Proficiency(t, conceptId) < Config.TeachMinProficiency || Proficiency(p, conceptId) >= Proficiency(t, conceptId)) return false;
        Lesson(t, p, i, Config.TeachRate * (0.5f + Npcs.Gene(p, Gene.Curiosity)));
        return true;
    }

    /// <summary>Put a tool from tools.json into an NPC's hands (god gifts, tests).</summary>
    public bool GiveTool(int slot, string toolId)
    {
        int t = Graph.ToolIndex(toolId);
        if (t < 0 || slot < 0 || slot >= Npcs.HighWater || !Npcs.Alive[slot]) return false;
        KnowledgeState.EnsureSlots(slot);
        KnowledgeState.Owned[slot] |= 1UL << t;
        return true;
    }

    /// <summary>Tools this NPC carries (ids from tools.json).</summary>
    public IReadOnlyList<string> ToolsOf(int slot)
    {
        var list = new List<string>();
        ulong owned = slot < KnowledgeState.Owned.Length ? KnowledgeState.Owned[slot] : 0;
        for (int t = 0; t < Graph.Tools.Count; t++) if ((owned & (1UL << t)) != 0) list.Add(Graph.Tools[t].Id);
        return list;
    }

    // ---- records ----------------------------------------------------------------------------

    /// <summary>Attach a record site to a building (M3's archive) or a plain spot (buildingId -1).</summary>
    public WrittenRecord AddRecordSite(float x, float z, int buildingId = -1) => Records.Create(x, z, buildingId);

    /// <summary>Destroy a record site (fire, raid). Knowledge only it held is lost.</summary>
    public bool DestroyRecordSite(int recordId)
    {
        WrittenRecord? rec = null;
        foreach (var r in Records.All) if (r.Id == recordId) rec = r;
        if (rec is null || !Records.Remove(recordId)) return false;
        foreach (var id in rec.Entries.Keys)
        {
            if (!Graph.TryIndex(id, out int i)) continue;
            Graph.SetRecorded(i, Records.Holding(id));
            if (!Graph.KnownAnywhere(i))
                Chronicle(Tick, "knowledge-lost", $"The records of {Lower(Graph[i].Name)} were destroyed; no one remembers it.", 6, Array.Empty<int>());
        }
        return true;
    }

    /// <summary>Write a concept into a record (writers do this daily; tests and M3 may too).</summary>
    public void WriteRecord(WrittenRecord rec, string conceptId, float level)
    {
        if (!Graph.TryIndex(conceptId, out int i)) return;
        bool added = !rec.Entries.ContainsKey(conceptId);
        rec.Entries[conceptId] = Math.Clamp(level, 0f, 1f);
        if (added) Graph.SetRecorded(i, Graph.Recorded(i) + 1);
    }

    // ---- internals --------------------------------------------------------------------------

    private static bool Has(uint mask, ActionKind a) => (mask & (1u << (int)a)) != 0;

    private static string Lower(string name) => name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];

    private Dictionary<string, float>? KnowledgeOf(int s) => s < _knowledge.Length ? _knowledge[s] : null;

    private static float Prof(Dictionary<string, float>? k, string id) => k is not null && k.TryGetValue(id, out float p) ? p : 0f;

    /// <summary>Fast path for sim code: set a proficiency by concept index and keep the bookkeeping.</summary>
    private void SetProficiency(int s, int i, float value)
    {
        EnsureKnowledgeSlot(s);
        var k = _knowledge[s] ??= new Dictionary<string, float>(StringComparer.Ordinal);
        string id = Graph[i].Id;
        k.TryGetValue(id, out float before);
        float after = Math.Clamp(value, 0f, 1f);
        k[id] = after;
        if (!_knowledgeLoading) Bookkeep(s, i, before, after);
    }

    private void Bookkeep(int s, int i, float before, float after)
    {
        if (before <= 0 && after > 0) Graph.AddHolder(i, 1);
        else if (before > 0 && after <= 0) Graph.AddHolder(i, -1);
        float u = Config.UnlockProficiency;
        if (before < u != after < u) EffectApplier.OnThresholdCrossed(this, s, Graph[i], gained: after >= u);
    }

    /// <summary>Learn from someone (or something). A first lesson leaves a memory naming the source: the recorded contact.</summary>
    private void Gain(int s, int i, float value, int fromSlot, string how)
    {
        var k = KnowledgeOf(s);
        float before = Prof(k, Graph[i].Id);
        if (value <= before) return;
        SetProficiency(s, i, value);
        if (before > 0 || StageOf(s) == LifeStage.Infant) return;
        string concept = Lower(Graph[i].Name);
        if (fromSlot >= 0)
        {
            int[] subjects = [Npcs.IdOf(fromSlot), Npcs.IdOf(s)];
            Memories.Add(s, new Memory(Tick, MemoryKind.Conversation, subjects, $"{Npcs.Name[fromSlot]} {how} me {concept}.", 3), Tick);
        }
        else Memories.Add(s, new Memory(Tick, MemoryKind.Discovery, [Npcs.IdOf(s)], $"I learned {concept} from the written records.", 3), Tick);
    }

    private void Hone(int s, ActionKind action)
    {
        var k = KnowledgeOf(s);
        if (k is null || k.Count == 0) return;
        var list = Graph.HonedBy(action);
        float rate = Config.HoneRate;
        for (int j = 0; j < list.Length; j++)
        {
            int i = list[j];
            if (k.TryGetValue(Graph[i].Id, out float p) && p > 0 && p < 1f) SetProficiency(s, i, KnowledgeRules.Hone(p, rate));
        }
    }

    /// <summary>Explaining something is practice too: the speaker hones what they talked about.</summary>
    private void Practise(int s, int i, float p)
    {
        if (p > 0 && p < 1f) SetProficiency(s, i, KnowledgeRules.Hone(p, Config.HoneRate));
    }

    private void Chatter(int speaker, int listener)
    {
        if (StageOf(listener) == LifeStage.Infant) return;
        var ks = KnowledgeOf(speaker);
        if (ks is null || ks.Count == 0 || Graph.Count == 0) return;
        var kl = KnowledgeOf(listener);
        int start = Rng.NextInt(Graph.Count);
        for (int j = 0; j < Graph.Count; j++)
        {
            int i = (start + j) % Graph.Count;
            string id = Graph[i].Id;
            float sp = Prof(ks, id);
            if (sp < Config.UnlockProficiency) continue;
            float lp = Prof(kl, id);
            if (lp >= sp - 0.1f) continue;
            Gain(listener, i, KnowledgeRules.Transfer(lp, sp, Config.ChatterRate), speaker, "told");
            Practise(speaker, i, sp);
            return;
        }
    }

    private void Lesson(int teacher, int pupil, int concept, float rate)
    {
        var kt = KnowledgeOf(teacher);
        var kp = KnowledgeOf(pupil);
        if (concept < 0 || concept >= Graph.Count || Prof(kt, Graph[concept].Id) <= Prof(kp, Graph[concept].Id))
            if (!PickLesson(teacher, pupil, out concept)) return;
        string id = Graph[concept].Id;
        Gain(pupil, concept, KnowledgeRules.Transfer(Prof(kp, id), Prof(kt, id), rate), teacher, "taught");
        Practise(teacher, concept, Prof(kt, id));
        var n = Npcs;
        n.NeedRef(pupil, Need.Social) = Math.Min(1f, n.Need(pupil, Need.Social) + 0.2f);
        n.NeedRef(teacher, Need.Social) = Math.Min(1f, n.Need(teacher, Need.Social) + 0.2f);
        Relationships.Adjust(n.IdOf(teacher), n.IdOf(pupil), 0.03f, 0.03f);
    }

    /// <summary>The nearest NPC, if trusted (or kin), and a concept one can teach the other. Allocation-free.</summary>
    private bool FindLesson(int s, bool teaching, out int other, out int concept)
    {
        concept = -1;
        other = FindNearestNpc(s, Config.PerceptionRadius);
        if (other < 0) return false;
        if (!IsKin(s, other) && Relationships.Get(Npcs.IdOf(s), Npcs.IdOf(other)).Trust < Config.TeachTrustMin) return false;
        return teaching ? PickLesson(s, other, out concept) : PickLesson(other, s, out concept);
    }

    private bool PickLesson(int teacher, int pupil, out int concept)
    {
        concept = -1;
        var kt = KnowledgeOf(teacher);
        if (kt is null || kt.Count == 0) return false;
        var kp = KnowledgeOf(pupil);
        int count = Graph.Count;
        int start = (int)((Tick / Config.DecisionIntervalTicks + teacher) % count);
        for (int j = 0; j < count; j++)
        {
            int i = (start + j) % count;
            string id = Graph[i].Id;
            float tp = Prof(kt, id);
            if (tp < Config.TeachMinProficiency || Prof(kp, id) > tp - 0.2f) continue;
            concept = i;
            return true;
        }
        return false;
    }

    private bool FindCraft(int s, out int tool, out int node)
    {
        var ks = KnowledgeState;
        tool = node = -1;
        ulong want = ks.Craftable[s] & ~ks.Owned[s];
        while (want != 0)
        {
            int t = System.Numerics.BitOperations.TrailingZeroCount(want);
            want &= want - 1;
            var def = Graph.Tools[t];
            if (def.Materials.Count == 0 || !ConceptGraph.TryParseResourceKind(def.Materials[0], out var kind)) continue;
            int found = Resources.FindNearest(kind, Npcs.X[s], Npcs.Z[s], Config.SearchRadius, 1f, Npcs.AvoidNode[s], reachable: true);
            if (found < 0) continue;
            tool = t;
            node = found;
            return true;
        }
        return false;
    }

    private void CraftTool(int s)
    {
        var ks = KnowledgeState;
        int t = ks.TargetTool[s], node = Npcs.TargetNode[s];
        if (t < 0 || t >= Graph.Tools.Count || node < 0 || Resources.Amount[node] < 1f) return;
        if (Dist(Npcs.X[s], Npcs.Z[s], Resources.X[node], Resources.Z[node]) > ArriveDistance + 2f) return;
        Resources.Amount[node] -= 1f;
        ks.Owned[s] |= 1UL << t;
        Npcs.NeedRef(s, Need.Purpose) = Math.Min(1f, Npcs.Need(s, Need.Purpose) + 0.3f);
    }

    // ---- discovery --------------------------------------------------------------------------

    private bool Eligible(Dictionary<string, float>? k, int i, float threshold)
    {
        var node = Graph[i];
        if (Prof(k, node.Id) > 0) return false;
        var pre = node.Prereqs;
        for (int j = 0; j < pre.Length; j++)
            if (Prof(k, Graph[pre[j]].Id) < threshold) return false;
        return true;
    }

    /// <summary>
    /// One discovery roll for an NPC: the concepts it does not know whose prerequisites it has all mastered
    /// (≥ <see cref="SimConfig.DiscoveryThreshold"/>), weighted by ease, times curiosity. Allocation-free unless
    /// something is discovered.
    /// </summary>
    private void DiscoveryRoll(int s, float boost)
    {
        var cfg = Config;
        if (cfg.DiscoveryRate <= 0) return;
        var k = KnowledgeOf(s);
        float th = cfg.DiscoveryThreshold;
        float total = 0;
        int eligible = 0;
        for (int i = 0; i < Graph.Count; i++)
        {
            if (!Eligible(k, i, th)) continue;
            total += KnowledgeRules.DiscoveryWeight(Graph[i].Difficulty);
            eligible++;
        }
        KnowledgeState.EnsureSlots(s);
        KnowledgeState.Candidates[s] = (short)Math.Min(eligible, short.MaxValue);
        if (total <= 0) return;
        float chance = Npcs.Gene(s, Gene.Curiosity) * total * cfg.DiscoveryRate * Math.Max(1, cfg.DiscoveryIntervalTicks) * boost;
        if (!Rng.Chance(chance)) return;
        float r = Rng.NextFloat() * total;
        int pick = -1;
        for (int i = 0; i < Graph.Count; i++)
        {
            if (!Eligible(k, i, th)) continue;
            pick = i;
            r -= KnowledgeRules.DiscoveryWeight(Graph[i].Difficulty);
            if (r < 0) break;
        }
        if (pick >= 0) Discover(s, pick);
    }

    /// <summary>The NPC works a concept out on its own. Chronicle-worthy when the world did not know it.</summary>
    private void Discover(int s, int i)
    {
        var node = Graph[i];
        bool first = !Graph.EverKnown(i);
        bool rediscovered = !first && !Graph.KnownAnywhere(i);
        SetProficiency(s, i, Math.Max(Proficiency(s, node.Id), Config.DiscoveredProficiency));
        KnowledgeState.Candidates[s] = (short)Math.Max(0, KnowledgeState.Candidates[s] - 1);
        string who = Npcs.Name[s], what = Lower(node.Name);
        string text = first ? $"{who} discovered {what}." : rediscovered ? $"{who} rediscovered {what}, which had been lost." : $"{who} worked out {what} alone.";
        bool news = first || rediscovered;
        Events.Publish(new SimEvent(Tick, MemoryKind.Discovery, text, news ? 7 : 5, [Npcs.IdOf(s)],
            Npcs.X[s], Npcs.Z[s], Config.PerceptionRadius, news ? "discovery" : null, first ? 6 : 5));
    }

    // ---- the daily pass: apprenticeship and records -----------------------------------------

    private void KnowledgeDay(int s, LifeStage stage)
    {
        WearTools(s);
        if (stage == LifeStage.Child)
        {
            Apprentice(s, Npcs.Mother[s]);
            Apprentice(s, Npcs.Father[s]);
        }
        UseRecords(s);
    }

    /// <summary>Tools wear out, so crafting (and the concepts it hones) never stops for good.</summary>
    private void WearTools(int s)
    {
        if (s >= KnowledgeState.Owned.Length) return;
        ulong owned = KnowledgeState.Owned[s];
        while (owned != 0)
        {
            int t = System.Numerics.BitOperations.TrailingZeroCount(owned);
            owned &= owned - 1;
            if (Rng.Chance(Config.ToolBreakChancePerDay)) KnowledgeState.Owned[s] &= ~(1UL << t);
        }
    }

    /// <summary>A child near a parent picks up what the parent knows, a little every day.</summary>
    private void Apprentice(int child, int parentId)
    {
        if (!Npcs.TryGetSlot(parentId, out int parent)) return;
        if (Dist(Npcs.X[child], Npcs.Z[child], Npcs.X[parent], Npcs.Z[parent]) > Config.PerceptionRadius) return;
        var kp = KnowledgeOf(parent);
        if (kp is null || kp.Count == 0) return;
        float rate = Config.ApprenticeRatePerDay * (0.5f + Npcs.Gene(child, Gene.Curiosity));
        if (Prof(kp, ApprenticeshipId) >= Config.UnlockProficiency) rate *= 2f;
        for (int i = 0; i < Graph.Count; i++)
        {
            string id = Graph[i].Id;
            float target = Prof(kp, id) * 0.9f;
            if (target <= 0) continue;
            float cp = Prof(KnowledgeOf(child), id);
            if (cp >= target) continue;
            Gain(child, i, KnowledgeRules.Transfer(cp, target, rate), parent, "showed");
        }
    }

    /// <summary>Writers write what they know into the nearest record; readers learn what is written there.</summary>
    private void UseRecords(int s)
    {
        var k = KnowledgeOf(s);
        if (k is null) return;
        float writing = Prof(k, WritingId);
        bool literate = writing >= Config.UnlockProficiency || Prof(k, PictographsId) >= Config.DiscoveryThreshold;
        if (!literate) return;
        var cfg = Config;
        float x = Npcs.X[s], z = Npcs.Z[s];
        var rec = Records.Nearest(x, z, cfg.RecordRadius);
        int count = Graph.Count;
        int start = (int)((Tick / cfg.TicksPerDay + s) % count);

        if (writing >= cfg.WritingProficiency)
        {
            if (rec is null && Records.Nearest(x, z, cfg.RecordRadius * 4) is null)
            {
                rec = Records.Create(x, z);
                Chronicle(Tick, "records", $"{Npcs.Name[s]} began keeping written records.", 5, [Npcs.IdOf(s)]);
            }
            if (rec is not null)
            {
                int written = 0;
                for (int j = 0; j < count && written < 2; j++)
                {
                    int i = (start + j) % count;
                    string id = Graph[i].Id;
                    float p = Prof(k, id);
                    if (p < cfg.DiscoveryThreshold) continue;
                    float level = p * 0.9f;
                    if (rec.Entries.TryGetValue(id, out float have) && have >= level - 0.02f) continue;
                    WriteRecord(rec, id, level);
                    written++;
                }
                if (written > 0 && Graph.TryIndex(WritingId, out int wi)) SetProficiency(s, wi, KnowledgeRules.Hone(writing, cfg.HoneRate));
            }
        }
        if (rec is null) return;
        int read = 0;
        for (int j = 0; j < count && read < 2; j++)
        {
            int i = (start + j) % count;
            string id = Graph[i].Id;
            if (!rec.Entries.TryGetValue(id, out float level)) continue;
            float p = Prof(KnowledgeOf(s), id);
            if (level <= p + 0.05f) continue;
            Gain(s, i, KnowledgeRules.Transfer(p, level, cfg.ReadRatePerDay), -1, "");
            read++;
        }
    }

    // ---- persistence (section KNW2, appended after the M1 sections) ---------------------------

    /// <summary>ReadBody, first thing: suppress effects while the M1 KNOW section replays proficiencies.</summary>
    private void BeginKnowledgeLoad() => _knowledgeLoading = true;

    private void WriteKnowledgeExtras(BinaryWriter w)
    {
        w.Write(Graph.Count);
        foreach (var node in Graph.Nodes)
        {
            w.Write(node.Id);
            w.Write(node.IsInvention);
            w.Write(Graph.EverKnown(node.Index));
        }
        Records.Write(w);
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            var tools = ToolsOf(s);
            w.Write(tools.Count);
            foreach (var t in tools) w.Write(t);
            var ks = KnowledgeState;
            bool has = s < ks.Owned.Length;
            w.Write(has ? ks.Candidates[s] : (short)0);
            w.Write(has ? ks.TargetSlot[s] : -1);
            w.Write(has ? ks.TargetConcept[s] : -1);
            w.Write(has ? ks.TargetTool[s] : -1);
            w.Write(has ? ks.TargetNode[s] : -1);
        }
    }

    /// <summary>ReadBody, after every section: rebuild the graph in saved order, then the derived counters and masks.</summary>
    private void EndKnowledgeLoad(byte[]? section)
    {
        var data = KnowledgeData.Default;
        var ordered = new List<(ConceptProposal, bool)>();
        var ever = new HashSet<string>(StringComparer.Ordinal);
        var ks = KnowledgeState;
        ks.EnsureSlots(Math.Max(Npcs.Capacity, 1) - 1);
        for (int s = 0; s < ks.Owned.Length; s++) ks.ResetSlot(s);
        Records.Clear();
        var ownedTools = new List<(int slot, string tool)>();

        if (section is not null)
        {
            using var r = new BinaryReader(new MemoryStream(section));
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                string id = r.ReadString();
                bool invention = r.ReadBoolean(), everKnown = r.ReadBoolean();
                if (everKnown) ever.Add(id);
                if (_concepts.TryGetValue(id, out var c)) ordered.Add((c, invention));
            }
            Records.Read(r);
            for (int s = 0; s < Npcs.HighWater; s++)
            {
                if (!Npcs.Alive[s]) continue;
                int m = r.ReadInt32();
                for (int j = 0; j < m; j++) ownedTools.Add((s, r.ReadString()));
                ks.Candidates[s] = r.ReadInt16();
                ks.TargetSlot[s] = r.ReadInt32();
                ks.TargetConcept[s] = r.ReadInt32();
                ks.TargetTool[s] = r.ReadInt32();
                ks.TargetNode[s] = r.ReadInt32();
            }
        }
        else
        {
            // An M1 save: shipped concepts in file order, then whatever inventions it carried.
            var shipped = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in data.Concepts)
            {
                shipped.Add(c.Concept.Id);
                ordered.Add((_concepts.TryGetValue(c.Concept.Id, out var saved) ? saved : c.Concept, false));
            }
            var extra = new List<string>();
            foreach (var id in _concepts.Keys) if (!shipped.Contains(id)) extra.Add(id);
            extra.Sort(StringComparer.Ordinal);
            foreach (var id in extra) ordered.Add((_concepts[id], true));
        }

        Graph = ConceptGraph.FromSaved(data, ordered);
        _concepts.Clear();
        foreach (var node in Graph.Nodes) _concepts[node.Id] = node.Concept;
        foreach (var (slot, tool) in ownedTools)
        {
            int t = Graph.ToolIndex(tool);
            if (t >= 0) ks.Owned[slot] |= 1UL << t;
        }

        Graph.ClearCounters();
        for (int i = 0; i < Graph.Count; i++)
        {
            string id = Graph[i].Id;
            Graph.SetEverKnown(i, ever.Contains(id));
            Graph.SetRecorded(i, Records.Holding(id));
        }
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            var k = KnowledgeOf(s);
            if (k is null) continue;
            for (int i = 0; i < Graph.Count; i++)
                if (Prof(k, Graph[i].Id) > 0) Graph.AddHolder(i, 1);
            EffectApplier.RecomputeUnlocks(this, s);
        }
        _knowledgeLoading = false;
    }

    private ulong KnowledgeExtrasHash(ulong h)
    {
        h = Hash.Mix(h, (ulong)Graph.Count);
        foreach (var node in Graph.Nodes)
        {
            h = Hash.Mix(h, StableHash.Of(node.Id));
            h = Hash.Mix(h, node.IsInvention ? 1UL : 0UL);
            h = Hash.Mix(h, Graph.EverKnown(node.Index) ? 1UL : 0UL);
        }
        h = Records.Hash(h);
        var ks = KnowledgeState;
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s] || s >= ks.Owned.Length) continue;
            h = Hash.Mix(h, ks.Owned[s]);
            h = Hash.Mix(h, (ulong)ks.Candidates[s]);
            h = Hash.Mix(h, (ulong)ks.TargetSlot[s]);
            h = Hash.Mix(h, (ulong)ks.TargetConcept[s]);
            h = Hash.Mix(h, (ulong)ks.TargetTool[s]);
            h = Hash.Mix(h, (ulong)ks.TargetNode[s]);
        }
        return h;
    }
}
