using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class TransmissionTests
{
    /// <summary>Two adults side by side, nothing else going on.</summary>
    private static World Pair(int seed = 5, float discovery = 0)
    {
        var w = new World(new SimConfig { Seed = seed, DiscoveryRate = discovery });
        w.Npcs.X[1] = w.Npcs.X[0] + 1;
        w.Npcs.Z[1] = w.Npcs.Z[0];
        w.Step();
        return w;
    }

    private static void Talk(World w, int speakerSlot, int listenerSlot)
    {
        w.Apply(new PossessCommand(w.Npcs.IdOf(speakerSlot)));
        w.Apply(new PossessedInteractCommand(w.Npcs.IdOf(listenerSlot), "talk"));
        w.Apply(new ReleasePossessionCommand());
    }

    [Fact]
    public void Chatter_is_slow_and_teaching_is_fast()
    {
        var w = Pair();
        w.SetKnowledge(0, "pottery", 0.9f);
        Talk(w, 0, 1);
        float chatter = w.Proficiency(1, "pottery");
        Assert.InRange(chatter, 0.001f, 0.06f);
        // The first thing learned from someone is remembered with them as a subject: that is the recorded contact.
        Assert.Contains(w.Memories.Of(1), m => m.Kind == MemoryKind.Conversation && m.Subjects.Contains(w.Npcs.IdOf(0)) && m.Text.Contains("pottery"));

        w.SetKnowledge(0, "teaching", 0.9f);
        w.Relationships.Adjust(w.Npcs.IdOf(0), w.Npcs.IdOf(1), 0.2f, 0.2f);
        Assert.True(w.TryTeach(w.Npcs.IdOf(0), w.Npcs.IdOf(1), "pottery"));
        float taught = w.Proficiency(1, "pottery") - chatter;
        Assert.True(taught > 2 * chatter, $"lesson {taught} vs chatter {chatter}");
    }

    [Fact]
    public void Teaching_is_trust_gated_and_needs_the_teach_action()
    {
        var w = Pair();
        int a = w.Npcs.IdOf(0), b = w.Npcs.IdOf(1);
        w.SetKnowledge(0, "pottery", 0.9f);
        Assert.False(w.TryTeach(a, b, "pottery")); // teaching not known
        w.SetKnowledge(0, "teaching", 0.9f);
        w.Relationships.Adjust(a, b, 0.5f, -0.3f);
        Assert.False(w.TryTeach(a, b, "pottery")); // distrusted
        w.Relationships.Adjust(a, b, 0f, 0.5f);
        Assert.True(w.TryTeach(a, b, "pottery"));
        Assert.False(w.TryTeach(b, a, "pottery")); // pupil cannot teach the master
    }

    [Fact]
    public void Teachers_choose_to_teach_trusted_neighbours()
    {
        var w = Pair();
        int a = w.Npcs.IdOf(0), b = w.Npcs.IdOf(1);
        w.SetKnowledge(0, "teaching", 0.9f);
        w.SetKnowledge(0, "pottery", 0.9f);
        w.Relationships.Adjust(a, b, 0.3f, 0.3f);
        bool taught = false;
        for (int t = 0; t < 3 * w.Config.TicksPerDay && w.Proficiency(1, "pottery") < 0.3f; t++)
        {
            w.Step();
            taught |= w.Npcs.Alive[0] && w.Npcs.Action[0] == ActionKind.Teach;
        }
        Assert.True(taught, "the teacher never chose to teach");
        Assert.True(w.Proficiency(1, "pottery") >= 0.3f);
    }

    [Fact]
    public void Children_learn_from_their_parents()
    {
        var w = new World(new SimConfig { Seed = 5, DiscoveryRate = 0 });
        w.SetKnowledge(0, "pottery", 0.9f);
        int child = w.SpawnAdult(w.Npcs.X[0], w.Npcs.Z[0], Sex.Female, 6f);
        w.Npcs.Mother[child] = w.Npcs.IdOf(0);
        Assert.Equal(LifeStage.Child, w.StageOf(child));
        for (int t = 0; t < 5 * w.Config.TicksPerDay; t++) w.Step();
        Assert.True(w.Proficiency(child, "pottery") > 0.05f);
        Assert.True(w.Proficiency(child, "pottery") <= 0.9f * 0.9f);
        Assert.Contains(w.Memories.Of(child), m => m.Kind == MemoryKind.Conversation && m.Text.Contains("showed me pottery"));
    }

    [Fact]
    public void Knowledge_dies_with_its_last_holder()
    {
        var w = Pair();
        w.SetKnowledge(0, "bronze-casting", 0.9f);
        Assert.True(w.Graph.KnownAnywhere("bronze-casting"));
        w.Kill(0, DeathCause.Accident);
        Assert.False(w.Graph.KnownAnywhere("bronze-casting"));
        Assert.Contains(w.ChronicleTail(10), e => e.Kind == "knowledge-lost" && e.Text.Contains("bronze casting"));
        Assert.True(w.Graph.KnownAnywhere("fire")); // the survivor still has the basics
    }

    [Fact]
    public void Written_records_keep_knowledge_alive_and_can_be_read()
    {
        var w = Pair();
        w.SetKnowledge(0, "writing", 0.9f);
        w.SetKnowledge(0, "bronze-casting", 0.9f);
        for (int t = 0; t < 2 * w.Config.TicksPerDay; t++) w.Step();
        Assert.True(w.Records.Count >= 1, "the writer kept no records");
        Assert.Contains(w.ChronicleTail(20), e => e.Kind == "records");
        for (int t = 0; t < 30 * w.Config.TicksPerDay && w.Graph.Recorded(Index(w, "bronze-casting")) == 0; t++) w.Step();
        Assert.True(w.Graph.Recorded(Index(w, "bronze-casting")) > 0, "bronze casting was never written down");

        w.Kill(0, DeathCause.Accident);
        Assert.True(w.Graph.KnownAnywhere("bronze-casting"));
        Assert.Equal(0, w.Graph.Holders(Index(w, "bronze-casting")));

        // A reader next to the record learns what is written there.
        var rec = w.Records.All[0];
        w.Npcs.X[1] = rec.X;
        w.Npcs.Z[1] = rec.Z;
        w.SetKnowledge(1, "writing", 0.4f);
        for (int t = 0; t < 3 * w.Config.TicksPerDay && w.Proficiency(1, "bronze-casting") == 0; t++)
        {
            w.Npcs.X[1] = rec.X; // keep the reader at the archive
            w.Npcs.Z[1] = rec.Z;
            w.Step();
        }
        Assert.True(w.Proficiency(1, "bronze-casting") > 0);

        // Destroying the only record of unknown knowledge loses it.
        w.SetKnowledge(1, "bronze-casting", 0f);
        Assert.True(w.Graph.KnownAnywhere("bronze-casting"));
        foreach (var r in w.Records.All.ToList()) w.DestroyRecordSite(r.Id);
        Assert.False(w.Graph.KnownAnywhere("bronze-casting"));
    }

    [Fact]
    public void A_concept_reaches_a_second_group_only_after_a_recorded_contact()
    {
        // M3 settlements are not on this branch: two spatially separated groups stand in for them.
        var w = new World(new SimConfig { Seed = 11, StartingNpcs = 0, SearchRadius = 48f });
        var (a, b) = FarApartFood(w);
        int[] groupA = [Spawn(w, a, Sex.Female), Spawn(w, a, Sex.Male)];
        int[] groupB = [Spawn(w, b, Sex.Female), Spawn(w, b, Sex.Male)];
        var idsA = groupA.Select(s => w.Npcs.IdOf(s)).ToArray();
        int[] idsB = groupB.Select(s => w.Npcs.IdOf(s)).ToArray();
        foreach (int s in groupA)
        {
            w.SetKnowledge(s, "bronze-casting", 0.9f);
            w.SetKnowledge(s, "teaching", 0.9f);
        }

        bool AnyBKnows() => idsB.Any(id => w.Npcs.TryGetSlot(id, out int s) && w.Proficiency(s, "bronze-casting") > 0);
        bool ContactRecorded(int s) => w.Memories.Of(s).Any(m => m.Subjects.Any(x => idsA.Contains(x)));

        // Every B member who knows the concept must remember someone from A: checked every tick, before and after contact.
        void CheckInvariant()
        {
            foreach (int id in idsB)
                if (w.Npcs.TryGetSlot(id, out int s) && w.Proficiency(s, "bronze-casting") > 0)
                    Assert.True(ContactRecorded(s), $"{w.Npcs.Name[s]} learned bronze casting at tick {w.Tick} without a recorded contact");
        }

        // Apart: nothing crosses (they live hundreds of metres away and never meet in this window).
        for (int t = 0; t < 1500; t++)
        {
            w.Step();
            CheckInvariant();
        }
        Assert.False(AnyBKnows());
        Assert.All(idsB, id => Assert.False(w.Npcs.TryGetSlot(id, out int s) && ContactRecorded(s)));

        // Contact: one of B walks over and talks with A.
        Assert.True(w.Npcs.TryGetSlot(idsB[0], out int visitor), "test needs the visitor alive");
        Assert.True(w.Npcs.TryGetSlot(idsA[0], out int host), "test needs the host alive");
        w.Npcs.X[visitor] = w.Npcs.X[host] + 1.5f;
        w.Npcs.Z[visitor] = w.Npcs.Z[host];
        for (int t = 0; t < 4 * w.Config.TicksPerDay && w.Proficiency(visitor, "bronze-casting") == 0; t++)
        {
            if (t % w.Config.TicksPerHour == 0)
                w.Apply(new GoalIntention(idsB[0], w.Tick, IntentionSource.Player, ActionKind.Talk, idsA[0].ToString(), null, 10));
            w.Step();
            CheckInvariant();
        }
        Assert.True(w.Proficiency(visitor, "bronze-casting") > 0, "contact did not transmit");
        Assert.Contains(w.Memories.Of(visitor), m => m.Kind == MemoryKind.Conversation && m.Text.Contains("bronze casting")
            && m.Subjects.Any(x => idsA.Contains(x)));

        // Afterwards it keeps spreading the same way: through people, never through the air.
        for (int t = 0; t < 2000; t++)
        {
            w.Step();
            CheckInvariant();
        }
    }

    [Theory]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(12)]
    public void On_real_settlements_a_concept_crosses_only_through_a_recorded_contact(int seed)
    {
        // M8 re-run of the M4 acceptance on M3 settlements: two villages founded the M3 way (households cluster, a daily
        // update founds a settlement with a faction and a leader), 120-200 m apart on one landmass. Village A knows
        // bronze casting (and teaching). Every first lesson leaves a "X showed/told/taught me ..." memory naming the
        // teacher, so for each person in B who comes to know it we can name who they learned it from: the first one
        // must have learned it from someone in A, and every later one from someone who already knew it.
        var (w, ax, az) = Villages.World(seed);
        var before = AliveIds(w);
        int setA = Villages.Found(w, ax, az);
        var idsA = AliveIds(w).Except(before).ToHashSet();
        var (bx, bz) = Villages.SpotAway(w, ax, az);
        before = AliveIds(w);
        int setB = Villages.Found(w, bx, bz);
        var idsB = AliveIds(w).Except(before).ToHashSet();
        Assert.NotEqual(setA, setB);
        Assert.All(idsA, id => Assert.Equal(setA, w.SettlementOfSlot(Slot(w, id))));
        Assert.All(idsB, id => Assert.Equal(setB, w.SettlementOfSlot(Slot(w, id))));
        foreach (int id in idsA)
        {
            w.SetKnowledge(Slot(w, id), "bronze-casting", 0.9f);
            w.SetKnowledge(Slot(w, id), "teaching", 0.9f);
        }

        var knewAt = new Dictionary<int, long>(); // id -> tick they first knew it (A members: 0)
        foreach (int id in idsA) knewAt[id] = 0;
        int firstInB = -1;
        void Check()
        {
            foreach (int id in idsB)
            {
                if (knewAt.ContainsKey(id) || !w.Npcs.TryGetSlot(id, out int s) || w.Proficiency(s, "bronze-casting") <= 0) continue;
                var lesson = w.Memories.Of(s).FirstOrDefault(m => m.Kind == MemoryKind.Conversation && m.Text.Contains("bronze casting")
                    && m.Subjects.Length == 2 && m.Subjects[1] == id);
                Assert.True(lesson is not null, $"{w.Npcs.Name[s]} of B knows bronze casting at tick {w.Tick} with no lesson memory");
                int teacher = lesson!.Subjects[0];
                Assert.True(knewAt.ContainsKey(teacher), $"{w.Npcs.Name[s]} learned bronze casting from {teacher}, who did not know it");
                if (firstInB < 0)
                {
                    firstInB = id;
                    Assert.Contains(teacher, idsA); // the crossing itself is a contact with the other village
                }
                knewAt[id] = w.Tick;
            }
        }

        // Three days on their own. Real villages do meet (explorers, traders, people out gathering), so the concept may
        // cross already; whenever it does, Check has proved the crossing was a lesson from someone in A.
        for (int t = 0; t < 3 * w.Config.TicksPerDay && firstInB < 0; t++) { w.Step(); Check(); }

        // If it has not crossed yet, make the contact: a B villager walks into A and is told to talk with an A villager.
        int visitor = idsB.First(id => w.Npcs.TryGetSlot(id, out _));
        int host = idsA.First(id => w.Npcs.TryGetSlot(id, out _));
        w.Npcs.X[Slot(w, visitor)] = w.Npcs.X[Slot(w, host)] + 1.5f;
        w.Npcs.Z[Slot(w, visitor)] = w.Npcs.Z[Slot(w, host)];
        for (int t = 0; t < 4 * w.Config.TicksPerDay && firstInB < 0; t++)
        {
            if (t % w.Config.TicksPerHour == 0 && w.Npcs.TryGetSlot(visitor, out _))
                w.Apply(new GoalIntention(visitor, w.Tick, IntentionSource.Player, ActionKind.Talk, host.ToString(), null, 10));
            w.Step();
            Check();
        }
        Assert.True(firstInB >= 0, "contact did not carry bronze casting into village B");
        Assert.True(knewAt[firstInB] > 0);

        // Back home it spreads inside B, always from someone who knew it.
        for (int t = 0; t < 10 * w.Config.TicksPerDay; t++) { w.Step(); Check(); }
    }

    private static HashSet<int> AliveIds(World w)
    {
        var ids = new HashSet<int>();
        for (int s = 0; s < w.Npcs.HighWater; s++) if (w.Npcs.Alive[s]) ids.Add(w.Npcs.IdOf(s));
        return ids;
    }

    private static int Slot(World w, int id) => w.Npcs.TryGetSlot(id, out int s) ? s : throw new InvalidOperationException($"npc {id} is dead");

    private static int Index(World w, string id) => w.Graph.TryIndex(id, out int i) ? i : -1;

    private static int Spawn(World w, (float x, float z) at, Sex sex) => w.SpawnAdult(at.x, at.z, sex, 20f);

    /// <summary>The two berry patches farthest apart on the island (each with neighbours, so nobody has to roam).</summary>
    private static ((float, float), (float, float)) FarApartFood(World w)
    {
        var r = w.Resources;
        var berries = Enumerable.Range(0, r.Count).Where(i => r.Kind[i] == ResourceKind.Berry
            && Enumerable.Range(0, r.Count).Count(j => r.Kind[j] == ResourceKind.Berry && Math.Abs(r.X[j] - r.X[i]) + Math.Abs(r.Z[j] - r.Z[i]) < 40) >= 4).ToList();
        float best = 0;
        (int, int) pair = (0, 0);
        foreach (int i in berries)
            foreach (int j in berries)
            {
                float d = (r.X[i] - r.X[j]) * (r.X[i] - r.X[j]) + (r.Z[i] - r.Z[j]) * (r.Z[i] - r.Z[j]);
                if (d > best) { best = d; pair = (i, j); }
            }
        Assert.True(MathF.Sqrt(best) > 250f, $"island too small for the test: {MathF.Sqrt(best)} m");
        return ((r.X[pair.Item1], r.Z[pair.Item1]), (r.X[pair.Item2], r.Z[pair.Item2]));
    }
}
