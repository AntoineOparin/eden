using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// M7 myth and god (docs/specs/00-architecture.md, milestone M7). Everything here hangs off small hooks in the
/// shared partials: god-action handlers (World.Commands.cs) call <see cref="OnGodAct"/>, Talk's gossip
/// (World.Behaviour.cs) calls <see cref="NoteContact"/> and <see cref="Retell"/>, Pray calls
/// <see cref="ScoreMyth"/>/<see cref="BeginPray"/>/<see cref="CompletePray"/>, and <see cref="SocietyDaily"/> calls
/// <see cref="MythDaily"/> once per in-game day. All per-day work is O(NPCs + settlements²); nothing runs per tick
/// except the Pray score check, which only reads arrays.
/// <para>Randomness: myth rolls (belief formation, spread, names) use their own seeded stream so a world where the
/// god never acts follows the same main-stream trajectory as before M7, except where religions emerge on their own.
/// Gossip distortion keeps using the main stream's single roll per retelling, as in M1.</para>
/// </summary>
public sealed partial class World
{
    public GodStanding Standing { get; private set; } = new();
    public Religions Religions { get; private set; } = new();

    private Rng _mythRng;
    private bool _mythRngReady;

    /// <summary>Per faction: tick since the capital's Favour has opposed the religion's stance, or -1.</summary>
    private long[] _schismSince = [];

    /// <summary>Per NPC slot: settlement and NPC id as of the last daily update (people who move carry news). Saved.</summary>
    private int[] _mythHome = [], _mythHomeId = [];

    /// <summary>Per settlement: its first finished shrine (building id) as of the last daily update, or -1. Saved.</summary>
    private int[] _shrineOf = [];

    // Daily scratch (per settlement), not state.
    private int[] _mythAdults = [], _mythFaithful = [], _mythShrines = [], _mythShrineSites = [];

    // ---- totals for the runner and tests (hashed and saved) -----------------------------------
    public int TotalPrayers { get; private set; }
    public int TotalShrines { get; private set; }
    public int TotalDistorted { get; private set; }
    public int TotalAttributed { get; private set; }
    public int TotalSchisms { get; private set; }
    public int TotalGodBeliefsFormed { get; private set; }
    public int TotalPrayerBeliefs { get; private set; }
    public int TotalMindGodBeliefs { get; private set; }

    private const float BaseSubjectSwap = 0.15f, BaseImportanceDrift = 0.35f;
    private const int GodBeliefImportance = 6;

    private ref Rng MythRng
    {
        get
        {
            if (!_mythRngReady)
            {
                _mythRng = new Rng((ulong)Config.Seed * 0x9E3779B97F4A7C15UL ^ 0x4D59544855UL);
                _mythRngReady = true;
            }
            return ref _mythRng;
        }
    }

    /// <summary>Religion of a settlement's faction, or -1.</summary>
    public int ReligionOfSettlement(int set)
    {
        if (!Settlements.IsAlive(set)) return -1;
        int f = Settlements.Faction[set];
        return Factions.IsAlive(f) ? Factions.Religion[f] : -1;
    }

    public GodStance StanceOf(int set)
    {
        Standing.Ensure(Settlements.Count);
        return Standing.StanceOf(set, Config);
    }

    /// <summary>Number of living factions that follow some religion.</summary>
    public int ReligiousFactions
    {
        get
        {
            int n = 0;
            for (int f = 0; f < Factions.Count; f++) if (Factions.Alive[f] && Factions.Religion[f] >= 0) n++;
            return n;
        }
    }

    /// <summary>Finished shrines standing right now.</summary>
    public int ShrinesStanding
    {
        get
        {
            int n = 0;
            for (int b = 0; b < Buildings.HighWater; b++)
                if (Buildings.IsComplete(b) && Buildings.Kind[b] == BuildingKinds.Shrine) n++;
            return n;
        }
    }

    private void EnsureMythSlots()
    {
        Standing.Ensure(Settlements.Count);
        int ns = Math.Max(16, Settlements.Count);
        if (_shrineOf.Length < ns)
        {
            int old = _shrineOf.Length;
            Array.Resize(ref _shrineOf, ns * 2);
            for (int i = old; i < _shrineOf.Length; i++) _shrineOf[i] = -1;
            Array.Resize(ref _mythAdults, ns * 2); Array.Resize(ref _mythFaithful, ns * 2);
            Array.Resize(ref _mythShrines, ns * 2); Array.Resize(ref _mythShrineSites, ns * 2);
        }
        if (_mythHome.Length < Npcs.Capacity)
        {
            int old = _mythHome.Length;
            Array.Resize(ref _mythHome, Npcs.Capacity);
            Array.Resize(ref _mythHomeId, Npcs.Capacity);
            for (int i = old; i < _mythHome.Length; i++) _mythHome[i] = _mythHomeId[i] = -1;
        }
        int nf = Math.Max(16, Factions.Count);
        if (_schismSince.Length < nf)
        {
            int old = _schismSince.Length;
            Array.Resize(ref _schismSince, nf * 2);
            for (int i = old; i < _schismSince.Length; i++) _schismSince[i] = -1;
        }
    }

    // ---- god actions (hooks in World.Commands.cs) ---------------------------------------------

    /// <summary>
    /// A god action happened at (x, z). Bless, curse and whisper touch the target's settlement (or the nearest one in
    /// sight); rain and drought touch every settlement; the rest touch settlements whose centre is within
    /// radius + perception + half a settlement radius.
    /// </summary>
    internal void OnGodAct(GodActKind kind, float x, float z, float radius, int targetSlot = -1)
    {
        EnsureMythSlots();
        var st = Settlements;
        if (kind is GodActKind.Bless or GodActKind.Curse or GodActKind.Whisper)
        {
            int set = targetSlot >= 0 ? SettlementOfSlot(targetSlot) : -1;
            if (set < 0) set = NearestSettlement(x, z, Config.PerceptionRadius + Config.SettlementRadius);
            if (set >= 0) ApplyGodAct(set, kind);
            return;
        }
        float reach = kind is GodActKind.Rain or GodActKind.Drought ? float.PositiveInfinity
            : radius + Config.PerceptionRadius + 0.5f * Config.SettlementRadius;
        for (int k = 0; k < st.Count; k++)
            if (st.Alive[k] && (float.IsInfinity(reach) || Dist(x, z, st.CenterX[k], st.CenterZ[k]) <= reach)) ApplyGodAct(k, kind);
    }

    private int NearestSettlement(float x, float z, float within)
    {
        var st = Settlements;
        int best = -1;
        float bestD = within;
        for (int k = 0; k < st.Count; k++)
        {
            if (!st.Alive[k]) continue;
            float d = Dist(x, z, st.CenterX[k], st.CenterZ[k]);
            if (d <= bestD) { bestD = d; best = k; }
        }
        return best;
    }

    private void ApplyGodAct(int k, GodActKind kind)
    {
        var g = Standing;
        var c = Config;
        ref float f = ref g.Favour[k];
        ref float a = ref g.Awe[k];
        switch (kind)
        {
            case GodActKind.Bless:
                f += c.BlessFavour * (1f - f); a += c.BlessAwe * (1f - a); Forgive(k);
                break;
            case GodActKind.Curse:
                f -= c.BlessFavour * (1f + f); a += c.BlessAwe * (1f - a);
                break;
            case GodActKind.Lightning:
                a += c.LightningAwe * (1f - a); Wrath(k, c.LightningWrath);
                break;
            case GodActKind.Drought:
                a += c.DroughtAwe * (1f - a); Wrath(k, c.DroughtWrath);
                break;
            case GodActKind.Rain:
                f += c.RainFavour * (1f - f); Forgive(k);
                break;
            case GodActKind.Spawn or GodActKind.Terraform:
                a += c.WonderAwe * (1f - a);
                break;
            case GodActKind.Gift:
                f += c.GiftFavour * (1f - f); a += c.BlessAwe * (1f - a);
                break;
            case GodActKind.Whisper:
                a += c.WhisperAwe * (1f - a);
                break;
        }
        f = Math.Clamp(f, -1f, 1f);
        a = Math.Clamp(a, 0f, 1f);
    }

    private void Wrath(int k, float amount)
    {
        // The grace period runs from the first unforgiven harsh act; later ones join the same reckoning.
        if (Standing.Wrath[k] <= 0f) Standing.WrathDue[k] = Tick + (long)Config.WrathGraceDays * Config.TicksPerDay;
        Standing.Wrath[k] += amount;
    }

    /// <summary>Rain or a blessing inside the grace period: the harsh act was a test, not a punishment.</summary>
    private void Forgive(int k)
    {
        Standing.Wrath[k] = 0f;
        Standing.WrathDue[k] = 0;
    }

    // ---- gossip (hooks in World.Behaviour.cs) -------------------------------------------------

    /// <summary>Talk between members of two settlements puts them in contact (news of the god travels both ways).</summary>
    private void NoteContact(int s, int o)
    {
        int a = SettlementOfSlot(s), b = SettlementOfSlot(o);
        if (a < 0 || b < 0 || a == b) return;
        Standing.Ensure(Settlements.Count);
        Standing.Touch(a, b, Tick);
    }

    private const string ToldMe = " told me: ";

    /// <summary>The story inside a memory: a gossip copy's text after "X told me: ", else the whole text.</summary>
    private static ReadOnlySpan<char> StoryOf(Memory m)
    {
        if (m.Kind != MemoryKind.Gossip) return m.Text;
        int i = m.Text.IndexOf(ToldMe, StringComparison.Ordinal);
        return i < 0 ? m.Text : m.Text.AsSpan(i + ToldMe.Length);
    }

    /// <summary>A gossip copy is retold again while it is young enough and the listener has not heard the story.</summary>
    private bool CanRetell(int listener, Memory m)
        => m.Generation < Config.MaxGossipGeneration && !Memories.ContainsStory(listener, StoryOf(m));

    /// <summary>
    /// Copy one memory to the listener as gossip with one distortion roll (main stream, one draw as in M1):
    /// under 0.15 the teller swaps themselves in as the story's first subject (and in the text); under 0.35 the
    /// importance drifts by one; under 0.35 + <see cref="SimConfig.AttributionMax"/> × the teller's settlement Awe,
    /// a death, raid or discovery that did not involve the god is credited to it. At most one distortion per hop;
    /// the copy's <see cref="Memory.Generation"/> is one more than the original's.
    /// </summary>
    private void Retell(int speaker, int listener, Memory m)
    {
        var subjects = m.Subjects;
        int importance = m.Importance;
        var distortion = MemoryDistortion.None;
        string story = m.Kind == MemoryKind.Gossip ? StoryOf(m).ToString() : m.Text;
        string teller = Npcs.Name[speaker];
        int tellerId = Npcs.IdOf(speaker);
        float roll = Rng.NextFloat();
        if (roll < BaseSubjectSwap && subjects.Length > 0)
        {
            if (subjects[0] != tellerId)
            {
                string old = subjects[0] == MemoryStore.GodId ? "The god" : NameOf(subjects[0]);
                subjects = (int[])subjects.Clone();
                subjects[0] = tellerId;
                int at = story.IndexOf(old, StringComparison.OrdinalIgnoreCase);
                if (at >= 0 && old != "someone") story = string.Concat(story.AsSpan(0, at), teller, story.AsSpan(at + old.Length));
                distortion = MemoryDistortion.Subject;
            }
        }
        else if (roll < BaseImportanceDrift)
        {
            int d = Rng.Chance(0.5f) ? 1 : -1;
            if (importance + d > 10) d = -1;
            importance = Math.Clamp(importance + d, 1, 10);
            distortion = MemoryDistortion.Importance;
        }
        else if (roll < BaseImportanceDrift + AttributionChance(speaker) && Attributable(m, story))
        {
            subjects = new int[m.Subjects.Length + 1];
            subjects[0] = MemoryStore.GodId;
            m.Subjects.CopyTo(subjects, 1);
            story += AttributionSuffix(SettlementOfSlot(speaker));
            distortion = MemoryDistortion.Attribution;
            TotalAttributed++;
        }
        if (distortion != MemoryDistortion.None) TotalDistorted++;
        var copy = new Memory(Tick, MemoryKind.Gossip, subjects, $"{teller}{ToldMe}{story}", importance)
        {
            Generation = (byte)Math.Min(255, m.Generation + 1),
            Distortion = distortion,
        };
        Memories.Add(listener, copy, Tick);
    }

    private float AttributionChance(int speaker)
    {
        int set = SettlementOfSlot(speaker);
        return set >= 0 && set < Standing.Capacity ? Config.AttributionMax * Standing.Awe[set] : 0f;
    }

    /// <summary>Deaths, raids and discoveries (or retellings of them) that do not already involve the god.</summary>
    private static bool Attributable(Memory m, string story)
        => Array.IndexOf(m.Subjects, MemoryStore.GodId) < 0
           && m.Kind is MemoryKind.Death or MemoryKind.Fight or MemoryKind.Discovery or MemoryKind.Gossip
           && !GodStanding.MentionsGod(story);

    private string AttributionSuffix(int set) => (set >= 0 ? StanceOf(set) : GodStance.Neutral) switch
    {
        GodStance.Favourable => " The god willed it.",
        GodStance.Fearful or GodStance.Hostile => " The angry sky did this.",
        _ => " The god's hand was in it.",
    };

    // ---- prayer (hooks in World.Behaviour.cs) -------------------------------------------------

    /// <summary>Decision hook: Pray scores higher within reach of one's own settlement's shrine. Allocation-free.</summary>
    private void ScoreMyth(int s, Span<float> scores)
    {
        if (scores[(int)ActionKind.Pray] <= 0f) return;
        if (ShrineNear(s, Config.ShrineRange) < 0) return;
        ref float pray = ref scores[(int)ActionKind.Pray];
        pray *= Config.ShrinePrayBoost;
        if (Memories.LastGodBeliefTick(s) >= 0 && ReligionOfSettlement(SettlementOfSlot(s)) >= 0)
            pray += Config.FaithPrayBonus * Trait(s, Gene.Piety);
    }

    /// <summary>The settlement's finished shrine if it is within <paramref name="range"/> of the NPC, else -1.</summary>
    private int ShrineNear(int s, float range)
    {
        int set = SettlementOfSlot(s);
        if (set < 0 || set >= _shrineOf.Length) return -1;
        int b = _shrineOf[set];
        if (b < 0 || !Buildings.IsComplete(b) || Buildings.Kind[b] != BuildingKinds.Shrine) return -1;
        return Dist(Npcs.X[s], Npcs.Z[s], Buildings.X[b], Buildings.Z[b]) <= range ? b : -1;
    }

    /// <summary>Begin hook: walk to the shrine to pray. False = pray where you stand (M1 behaviour).</summary>
    private bool BeginPray(int s)
    {
        int b = ShrineNear(s, Config.ShrineRange);
        if (b < 0 || Dist(Npcs.X[s], Npcs.Z[s], Buildings.X[b], Buildings.Z[b]) <= ArriveDistance + 1f) return false;
        MoveTo(s, Buildings.X[b], Buildings.Z[b]);
        return true;
    }

    /// <summary>Complete hook: a prayer at a shrine fulfils more; in a religious faction it renews the belief.</summary>
    private void CompletePray(int s)
    {
        TotalPrayers++;
        if (ShrineNear(s, ArriveDistance + 4f) >= 0)
            Npcs.NeedRef(s, Need.Purpose) = Math.Min(1f, Npcs.Need(s, Need.Purpose) + Config.ShrinePurpose);
        int set = SettlementOfSlot(s);
        int r = ReligionOfSettlement(set);
        if (r < 0) return;
        if (!Religions.Prayed[r])
        {
            Religions.Prayed[r] = true;
            Chronicle(Tick, "prayer", $"{Npcs.Name[s]} of {Settlements.Name[set]} offered the first prayer of {Religions.Name[r]}.", 5, [Npcs.IdOf(s)]);
        }
        long last = Memories.LastGodBeliefTick(s);
        if (last >= 0 && Tick - last < (long)Config.PrayerBeliefDays * Config.TicksPerDay) return;
        Memories.Add(s, new Memory(Tick, MemoryKind.Belief, [Npcs.IdOf(s)], PrayerBelief(Religions.Stance[r]), GodBeliefImportance), Tick);
        TotalPrayerBeliefs++;
    }

    private static string PrayerBelief(GodStance stance) => stance switch
    {
        GodStance.Fearful => "We pray so the sky's anger passes us by.",
        GodStance.Hostile => "The sky is cruel, but we will endure it.",
        _ => "The sky hears our prayers and is kind.",
    };

    private static string GodBelief(GodStance stance) => stance switch
    {
        GodStance.Favourable => "The sky watches over us and is kind.",
        GodStance.Fearful => "The sky is angry; we must keep it calm.",
        GodStance.Hostile => "The sky has turned against us.",
        _ => "The sky watches us.",
    };

    // ---- beliefs from the Mind (hook in World.Commands.cs) -------------------------------------

    /// <summary>A BeliefIntention was applied: count beliefs about the god (they count toward religion via the memory).</summary>
    private void OnBeliefIntention(string text)
    {
        if (GodStanding.MentionsGod(text)) TotalMindGodBeliefs++;
    }

    // ---- daily (hook in World.Society.cs) -----------------------------------------------------

    private void MythDaily()
    {
        EnsureMythSlots();
        StandingDaily();
        ContactsFromOrders();
        ContactsFromMoves();
        UpdateEnvy();
        ShrinesDaily();
        BeliefsDaily();
        FoundReligions();
        SpreadReligions();
        Schisms();
        ReligiousConstruction();
    }

    private void StandingDaily()
    {
        var g = Standing;
        var st = Settlements;
        for (int k = 0; k < st.Count; k++)
        {
            if (!st.Alive[k]) continue;
            ref float f = ref g.Favour[k];
            if (g.Wrath[k] > 0f && Tick >= g.WrathDue[k])
            {
                f -= Math.Min(1f, g.Wrath[k]) * (1f + f);
                g.Wrath[k] = 0f;
                g.WrathDue[k] = 0;
            }
            f = Math.Clamp(f * Config.FavourDecayPerDay, -1f, 1f);
            g.Awe[k] = Math.Clamp(g.Awe[k] * Config.AweDecayPerDay, 0f, 1f);
        }
    }

    /// <summary>Traders and raiders on the road are contact between their home and their target.</summary>
    private void ContactsFromOrders()
    {
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s] || _order[s] is not ((byte)Order.Trade or (byte)Order.Raid)) continue;
            Standing.Touch(SettlementOfSlot(s), _orderTarget[s], Tick);
        }
    }

    /// <summary>Someone who now lives in another settlement than yesterday (a migrant, a colonist) links the two.</summary>
    private void ContactsFromMoves()
    {
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            int id = Npcs.IdOf(s), k = SettlementOfSlot(s);
            if (_mythHomeId[s] == id && _mythHome[s] >= 0 && k >= 0 && k != _mythHome[s]) Standing.Touch(_mythHome[s], k, Tick);
            _mythHome[s] = k;
            _mythHomeId[s] = id;
        }
    }

    private bool InContact(int a, int b)
    {
        long t = Standing.LastContact(a, b);
        return t >= 0 && Tick - t <= (long)Config.ContactDays * Config.TicksPerDay;
    }

    /// <summary>
    /// Each settlement looks at the most favoured neighbour it is in contact with: the gap in Favour becomes Envy
    /// (scaled by 1 − that neighbour's Awe) and Fear (scaled by its Awe). Envy feeds grievance toward it.
    /// </summary>
    private void UpdateEnvy()
    {
        var g = Standing;
        var st = Settlements;
        var c = Config;
        for (int a = 0; a < st.Count; a++)
        {
            if (!st.Alive[a]) continue;
            int best = -1;
            float bestGap = 0f;
            float own = Math.Max(0f, g.Favour[a]);
            for (int b = 0; b < st.Count; b++)
            {
                if (b == a || !st.Alive[b] || !InContact(a, b)) continue;
                float gap = g.Favour[b] - own;
                if (gap > bestGap) { bestGap = gap; best = b; }
            }
            if (best < 0)
            {
                g.Envy[a] *= c.EnvyDecayPerDay;
                g.Fear[a] *= c.EnvyDecayPerDay;
                continue;
            }
            float envyT = Math.Clamp(bestGap * (1f - g.Awe[best]), 0f, 1f), fearT = Math.Clamp(bestGap * g.Awe[best], 0f, 1f);
            g.Envy[a] = envyT > g.Envy[a] ? g.Envy[a] + c.EnvyRate * (envyT - g.Envy[a]) : g.Envy[a] * c.EnvyDecayPerDay;
            g.Fear[a] = fearT > g.Fear[a] ? g.Fear[a] + c.EnvyRate * (fearT - g.Fear[a]) : g.Fear[a] * c.EnvyDecayPerDay;
            g.EnvyOf[a] = g.FearOf[a] = best;
            st.GrievanceRef(a, best) += c.EnvyGrievancePerDay * g.Envy[a];
        }
    }

    /// <summary>Cache each settlement's first finished shrine; announce shrines finished since yesterday.</summary>
    private void ShrinesDaily()
    {
        var st = Settlements;
        var b = Buildings;
        for (int k = 0; k < st.Count; k++) { _shrineOf[k] = -1; _mythShrines[k] = 0; _mythShrineSites[k] = 0; }
        for (int i = 0; i < b.HighWater; i++)
        {
            int k = b.Settlement[i];
            if (!b.Alive[i] || k < 0 || k >= st.Count || b.Kind[i] != BuildingKinds.Shrine) continue;
            if (b.Progress[i] < 1f) { _mythShrineSites[k]++; continue; }
            if (_mythShrines[k]++ == 0) _shrineOf[k] = i;
        }
        var g = Standing;
        for (int k = 0; k < st.Count; k++)
        {
            if (_mythShrines[k] > g.ShrinesSeen[k] && st.Alive[k])
            {
                TotalShrines += _mythShrines[k] - g.ShrinesSeen[k];
                int r = ReligionOfSettlement(k);
                string of = r >= 0 ? $" for {Religions.Name[r]}" : "";
                Chronicle(Tick, "shrine", $"A shrine to the sky was raised in {st.Name[k]}{of}.", 6, Array.Empty<int>());
            }
            g.ShrinesSeen[k] = _mythShrines[k];
        }
    }

    /// <summary>
    /// Count adults and believers per settlement. An adult who remembers the god (seen, felt or heard of, within
    /// <see cref="SimConfig.GodMemoryDays"/>) and holds no belief about it yet may form one, more likely the more
    /// pious they are and the stronger their settlement's Awe or Favour; the belief's tone follows the stance.
    /// </summary>
    private void BeliefsDaily()
    {
        var st = Settlements;
        var g = Standing;
        var c = Config;
        long window = (long)c.GodMemoryDays * c.TicksPerDay;
        for (int k = 0; k < st.Count; k++) _mythAdults[k] = _mythFaithful[k] = 0;
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s] || StageOf(s) is not (LifeStage.Adult or LifeStage.Elder)) continue;
            int k = SettlementOfSlot(s);
            if (k < 0) continue;
            _mythAdults[k]++;
            if (Memories.LastGodBeliefTick(s) >= 0) { _mythFaithful[k]++; continue; }
            long seen = Memories.LastGodMemoryTick(s);
            if (seen < 0 || Tick - seen > window) continue;
            float intensity = Math.Min(1f, g.Awe[k] + Math.Abs(g.Favour[k]));
            if (intensity <= 0.01f) continue;
            float p = c.GodBeliefRate * (0.5f + Trait(s, Gene.Piety)) * intensity;
            if (!MythRng.Chance(p)) continue;
            Memories.Add(s, new Memory(Tick, MemoryKind.Belief, [Npcs.IdOf(s)], GodBelief(g.StanceOf(k, c)), GodBeliefImportance), Tick);
            TotalGodBeliefsFormed++;
            _mythFaithful[k]++;
        }
    }

    private void FoundReligions()
    {
        var st = Settlements;
        var c = Config;
        for (int k = 0; k < st.Count; k++)
        {
            if (!st.Alive[k] || _mythAdults[k] < c.ReligionMinAdults) continue;
            int f = st.Faction[k];
            if (!Factions.IsAlive(f) || Factions.Religion[f] >= 0) continue;
            if (_mythFaithful[k] < c.ReligionThreshold * _mythAdults[k]) continue;
            // Believers in contact with a faith (a colony and its mother village, say) take it up rather than found one.
            int from = -1;
            for (int b = 0; b < st.Count; b++)
                if (b != k && st.Alive[b] && ReligionOfSettlement(b) >= 0 && InContact(k, b) && (from < 0 || st.Members[b] > st.Members[from])) from = b;
            if (from >= 0)
            {
                int rb = ReligionOfSettlement(from);
                Factions.Religion[f] = rb;
                _schismSince[f] = -1;
                Chronicle(Tick, "religion", $"The faithful of {st.Name[k]} took up {Religions.Name[rb]}, the faith of {st.Name[from]}.", 6, Array.Empty<int>());
                continue;
            }
            var stance = Standing.StanceOf(k, c);
            if (stance == GodStance.Neutral) stance = Standing.Favour[k] < 0f ? GodStance.Hostile : GodStance.Favourable;
            int r = NewReligion(k, f, stance, -1);
            Chronicle(Tick, "religion",
                $"{Religions.FounderName[r]} of {st.Name[k]} gathered the faithful into {Religions.Name[r]}: {Creed(stance)}.", 8,
                Npcs.IsAlive(Religions.Founder[r]) ? [Religions.Founder[r]] : Array.Empty<int>());
        }
    }

    private static string Creed(GodStance stance) => stance switch
    {
        GodStance.Fearful => "they fear the sky and keep it calm",
        GodStance.Hostile => "they hold that the sky has turned against them",
        _ => "they hold that the sky favours them",
    };

    /// <summary>Found a religion for faction <paramref name="f"/> in settlement <paramref name="k"/>; the founder is its most pious believer.</summary>
    private int NewReligion(int k, int f, GodStance stance, int parent)
    {
        int founder = -1;
        float best = -1f;
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s] || SettlementOfSlot(s) != k || StageOf(s) is not (LifeStage.Adult or LifeStage.Elder)) continue;
            float score = Trait(s, Gene.Piety) + (Memories.LastGodBeliefTick(s) >= 0 ? 1f : 0f);
            if (score > best) { best = score; founder = s; }
        }
        int founderId = founder >= 0 ? Npcs.IdOf(founder) : Npcs.None;
        string founderName = founder >= 0 ? Npcs.Name[founder] : "someone";
        string name = Religions.MakeName(ref MythRng, stance);
        int r = Religions.Add(name, stance, Tick, founderId, founderName, f, k, parent);
        Factions.Religion[f] = r;
        _schismSince[f] = -1;
        return r;
    }

    /// <summary>Knowledge-style transmission: a faith crosses, with a small daily chance, to a faction in contact that has none.</summary>
    private void SpreadReligions()
    {
        var st = Settlements;
        for (int a = 0; a < st.Count; a++)
        {
            if (!st.Alive[a]) continue;
            int ra = ReligionOfSettlement(a);
            if (ra < 0) continue;
            for (int b = 0; b < st.Count; b++)
            {
                if (b == a || !st.Alive[b] || !Factions.IsAlive(st.Faction[b]) || st.Faction[b] == st.Faction[a]) continue;
                if (Factions.Religion[st.Faction[b]] >= 0 || !InContact(a, b)) continue;
                if (!MythRng.Chance(Config.ReligionSpreadChance)) continue;
                Factions.Religion[st.Faction[b]] = ra;
                _schismSince[st.Faction[b]] = -1;
                Chronicle(Tick, "religion", $"{Religions.Name[ra]} spread from {st.Name[a]} to {st.Name[b]}.", 6, Array.Empty<int>());
            }
        }
    }

    /// <summary>When the capital's Favour opposes the faith's stance for <see cref="SimConfig.SchismYears"/>, a new faith splits off.</summary>
    private void Schisms()
    {
        var c = Config;
        long span = (long)(c.SchismYears * c.TicksPerYear);
        for (int f = 0; f < Factions.Count; f++)
        {
            int r = Factions.Religion[f];
            int cap = Factions.Capital[f];
            if (!Factions.Alive[f] || r < 0 || !Settlements.IsAlive(cap)) continue;
            float fav = Standing.Favour[cap];
            bool opposed = Religions.Stance[r] == GodStance.Favourable ? fav < -c.StanceFavourMin : fav > c.StanceFavourMin;
            if (!opposed) { _schismSince[f] = -1; continue; }
            if (_schismSince[f] < 0) { _schismSince[f] = Tick; continue; }
            if (Tick - _schismSince[f] < span) continue;
            var stance = Standing.StanceOf(cap, c);
            if (stance == GodStance.Neutral) stance = fav < 0f ? GodStance.Hostile : GodStance.Favourable;
            int nr = NewReligion(cap, f, stance, r);
            TotalSchisms++;
            Chronicle(Tick, "schism",
                $"{Religions.FounderName[nr]} of {Settlements.Name[cap]} broke with {Religions.Name[r]} and founded {Religions.Name[nr]}: {Creed(stance)}.", 8,
                Npcs.IsAlive(Religions.Founder[nr]) ? [Religions.Founder[nr]] : Array.Empty<int>());
        }
    }

    /// <summary>Religious settlements may build shrines (BuildingKinds.Unlock) and plan one once they are big enough.</summary>
    private void ReligiousConstruction()
    {
        var st = Settlements;
        for (int k = 0; k < st.Count; k++)
        {
            if (!st.Alive[k] || ReligionOfSettlement(k) < 0) continue;
            if (!IsBuildingUnlocked(k, BuildingKinds.Shrine)) BuildingKinds.Unlock(k, BuildingKinds.All[BuildingKinds.Shrine].Id);
            // One shrine (or shrine site) per settlement, and only once the village is established (a finished
            // storehouse, enough people, not hungry): a shrine in a young village starves it of builders and stone.
            if (st.Members[k] < Config.ShrineMinMembers || _mythShrines[k] > 0 || _mythShrineSites[k] > 0) continue;
            if (k >= _storeCount.Length || _storeCount[k] == 0 || st.Hungry[k] > 0.2f) continue;
            TryPlaceNear(k, BuildingKinds.Shrine, st.CenterX[k], st.CenterZ[k], 6f, 18f);
        }
    }

    // ---- Mind view ----------------------------------------------------------------------------

    /// <summary>Per settlement (index = id): the god's standing for Eden.Mind prompts. Not on the tick path.</summary>
    internal GodStandingView?[] GodStandingViews()
    {
        var st = Settlements;
        Standing.Ensure(st.Count);
        var views = new GodStandingView?[st.Count];
        for (int k = 0; k < st.Count; k++)
        {
            if (!st.Alive[k]) continue;
            var g = Standing;
            int r = ReligionOfSettlement(k);
            string? envied = st.IsAlive(g.EnvyOf[k]) && g.Envy[k] > 0.05f ? st.Name[g.EnvyOf[k]] : null;
            string? feared = st.IsAlive(g.FearOf[k]) && g.Fear[k] > 0.05f ? st.Name[g.FearOf[k]] : null;
            views[k] = new GodStandingView(g.Favour[k], g.Awe[k], g.Envy[k], g.Fear[k], envied, feared,
                GodStanding.StanceName(g.StanceOf(k, Config)), r >= 0 ? Religions.Name[r] : null);
        }
        return views;
    }

    // ---- test hooks ---------------------------------------------------------------------------

    /// <summary>Tests: one Talk between two NPC slots (gossip, contact, knowledge chatter).</summary>
    internal void ConverseForTests(int s, int o) => Converse(s, o);

    /// <summary>Tests: retell a specific memory from speaker to listener.</summary>
    internal void RetellForTests(int speaker, int listener, Memory m) => Retell(speaker, listener, m);

    /// <summary>Tests: the factor ScoreMyth applies to a positive Pray score for this NPC right now.</summary>
    internal float PrayBoostForTests(int s)
    {
        Span<float> scores = stackalloc float[ActionScorer.ActionCount];
        scores[(int)ActionKind.Pray] = 1f;
        ScoreMyth(s, scores);
        return scores[(int)ActionKind.Pray];
    }

    /// <summary>Tests: finish a prayer for this NPC where it stands (the M1 effects plus the M7 hook).</summary>
    internal void CompletePrayForTests(int s)
    {
        Npcs.Action[s] = ActionKind.Pray;
        Complete(s, StageOf(s));
    }

    /// <summary>Tests: start a prayer (walks to the shrine if one is in range).</summary>
    internal void BeginPrayForTests(int s) => Begin(s, ActionKind.Pray, 1f, default, StageOf(s));

    internal ulong MythHashForTests() => MythHash(Hash.Offset);

    /// <summary>Tests: run the once-a-day myth update now.</summary>
    internal void RunMythDayForTests() => MythDaily();

    // ---- hashing and persistence (MYTH section, after KNW2) -----------------------------------

    private ulong MythHash(ulong h)
    {
        EnsureMythSlots();
        h = Hash.Mix(h, MythRng.StateHash());
        h = Standing.Hash(h, Settlements.Count);
        h = Religions.Hash(h);
        for (int f = 0; f < Factions.Count; f++)
        {
            h = Hash.Mix(h, (ulong)Factions.Religion[f]);
            h = Hash.Mix(h, (ulong)_schismSince[f]);
        }
        for (int k = 0; k < Settlements.Count; k++) h = Hash.Mix(h, (ulong)_shrineOf[k]);
        for (int s = 0; s < Npcs.HighWater; s++)
            if (Npcs.Alive[s]) { h = Hash.Mix(h, (ulong)_mythHome[s]); h = Hash.Mix(h, (ulong)_mythHomeId[s]); }
        h = Hash.Mix(h, (ulong)TotalPrayers); h = Hash.Mix(h, (ulong)TotalShrines);
        h = Hash.Mix(h, (ulong)TotalDistorted); h = Hash.Mix(h, (ulong)TotalAttributed);
        h = Hash.Mix(h, (ulong)TotalSchisms); h = Hash.Mix(h, (ulong)TotalGodBeliefsFormed);
        h = Hash.Mix(h, (ulong)TotalPrayerBeliefs); h = Hash.Mix(h, (ulong)TotalMindGodBeliefs);
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            foreach (var m in Memories.Of(s))
                if (m.Generation != 0 || m.Distortion != MemoryDistortion.None)
                    h = Hash.Mix(h, ((ulong)m.Generation << 8) | (byte)m.Distortion);
                else h = Hash.Mix(h, 0UL);
        }
        return h;
    }

    private void WriteMyth(BinaryWriter w)
    {
        EnsureMythSlots();
        var (a, b, c, d) = MythRng.State;
        w.Write(a); w.Write(b); w.Write(c); w.Write(d);
        Standing.Write(w, Settlements.Count);
        Religions.Write(w);
        w.Write(Factions.Count);
        for (int f = 0; f < Factions.Count; f++) { w.Write(Factions.Religion[f]); w.Write(_schismSince[f]); }
        w.Write(Settlements.Count);
        for (int k = 0; k < Settlements.Count; k++) w.Write(_shrineOf[k]);
        for (int s = 0; s < Npcs.HighWater; s++)
            if (Npcs.Alive[s]) { w.Write(_mythHome[s]); w.Write(_mythHomeId[s]); }
        w.Write(TotalPrayers); w.Write(TotalShrines); w.Write(TotalDistorted); w.Write(TotalAttributed);
        w.Write(TotalSchisms); w.Write(TotalGodBeliefsFormed); w.Write(TotalPrayerBeliefs); w.Write(TotalMindGodBeliefs);
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            var list = Memories.Of(s);
            w.Write(list.Count);
            foreach (var m in list) { w.Write(m.Generation); w.Write((byte)m.Distortion); }
        }
    }

    /// <summary>Loads the MYTH section, or (for saves from before M7) starts myth from scratch.</summary>
    private void ReadMyth(byte[]? data)
    {
        _schismSince = [];
        _shrineOf = [];
        _mythHome = _mythHomeId = [];
        _mythAdults = _mythFaithful = _mythShrines = _mythShrineSites = [];
        TotalPrayers = TotalShrines = TotalDistorted = TotalAttributed = 0;
        TotalSchisms = TotalGodBeliefsFormed = TotalPrayerBeliefs = TotalMindGodBeliefs = 0;
        if (data is null)
        {
            Standing = new GodStanding();
            Religions = new Religions();
            _mythRngReady = false;
            EnsureMythSlots();
            return;
        }
        using var r = new BinaryReader(new MemoryStream(data));
        _mythRng = Rng.FromState((r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64()));
        _mythRngReady = true;
        Standing = GodStanding.Read(r);
        Religions = Religions.Read(r);
        EnsureMythSlots();
        int nf = r.ReadInt32();
        for (int f = 0; f < nf; f++) { Factions.Religion[f] = r.ReadInt32(); _schismSince[f] = r.ReadInt64(); }
        int ns = r.ReadInt32();
        for (int k = 0; k < ns; k++) _shrineOf[k] = r.ReadInt32();
        for (int s = 0; s < Npcs.HighWater; s++)
            if (Npcs.Alive[s]) { _mythHome[s] = r.ReadInt32(); _mythHomeId[s] = r.ReadInt32(); }
        TotalPrayers = r.ReadInt32(); TotalShrines = r.ReadInt32(); TotalDistorted = r.ReadInt32(); TotalAttributed = r.ReadInt32();
        TotalSchisms = r.ReadInt32(); TotalGodBeliefsFormed = r.ReadInt32(); TotalPrayerBeliefs = r.ReadInt32(); TotalMindGodBeliefs = r.ReadInt32();
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            var list = Memories.Of(s);
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                byte gen = r.ReadByte();
                var dist = (MemoryDistortion)r.ReadByte();
                if (i < list.Count) { list[i].Generation = gen; list[i].Distortion = dist; }
            }
        }
    }
}
