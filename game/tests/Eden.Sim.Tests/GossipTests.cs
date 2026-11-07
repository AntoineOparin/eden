using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

/// <summary>M7: gossip distortion (subject swap, importance drift, god attribution) and myth generations.</summary>
public class GossipTests
{
    private static (World w, int set, int speaker, int listener) Village(int seed = 5)
    {
        var (w, x, z) = Villages.World(seed);
        int set = Villages.Found(w, x, z);
        var members = Enumerable.Range(0, w.Npcs.HighWater)
            .Where(s => w.Npcs.Alive[s] && w.SettlementOfSlot(s) == set && w.StageOf(s) is LifeStage.Adult or LifeStage.Elder).ToList();
        return (w, set, members[0], members[1]);
    }

    private static Memory Death(World w, int subjectSlot)
        => new(w.Tick, MemoryKind.Death, [w.Npcs.IdOf(subjectSlot)], $"{w.Npcs.Name[subjectSlot]} died of hunger.", 7);

    private static List<Memory> Copies(World w, int listener, int before) => w.Memories.Of(listener).Skip(before).ToList();

    [Fact]
    public void A_distorted_copy_differs_in_exactly_one_way_and_is_one_generation_older()
    {
        var (w, set, speaker, listener) = Village();
        w.Standing.Awe[set] = 1f; // attribution is possible
        var subject = Enumerable.Range(0, w.Npcs.HighWater).First(s => w.Npcs.Alive[s] && s != speaker && s != listener);
        var original = Death(w, subject);
        int before = w.Memories.Of(listener).Count;
        for (int i = 0; i < 300; i++) w.RetellForTests(speaker, listener, original);
        var copies = Copies(w, listener, before);
        Assert.Equal(300, copies.Count);
        var seen = new HashSet<MemoryDistortion>();
        foreach (var c in copies)
        {
            Assert.Equal(MemoryKind.Gossip, c.Kind);
            Assert.Equal(original.Generation + 1, c.Generation);
            Assert.StartsWith($"{w.Npcs.Name[speaker]} told me: ", c.Text);
            bool subjectChanged = c.Subjects[0] != original.Subjects[0] && c.Subjects.Length == original.Subjects.Length;
            bool importanceChanged = c.Importance != original.Importance;
            bool attributed = c.Subjects.Length == original.Subjects.Length + 1 && c.Subjects[0] == MemoryStore.GodId
                && c.Subjects.Skip(1).SequenceEqual(original.Subjects);
            int changes = (subjectChanged ? 1 : 0) + (importanceChanged ? 1 : 0) + (attributed ? 1 : 0);
            seen.Add(c.Distortion);
            switch (c.Distortion)
            {
                case MemoryDistortion.None:
                    Assert.Equal(0, changes);
                    Assert.Equal(original.Subjects, c.Subjects);
                    break;
                case MemoryDistortion.Subject:
                    Assert.True(subjectChanged && changes == 1);
                    Assert.Equal(w.Npcs.IdOf(speaker), c.Subjects[0]);
                    Assert.Contains($"{w.Npcs.Name[speaker]} died of hunger.", c.Text); // the teller is now the story's subject
                    break;
                case MemoryDistortion.Importance:
                    Assert.True(importanceChanged && changes == 1);
                    Assert.Equal(1, Math.Abs(c.Importance - original.Importance));
                    break;
                case MemoryDistortion.Attribution:
                    Assert.True(attributed && changes == 1);
                    Assert.True(GodStanding.MentionsGod(c.Text.Substring(c.Text.IndexOf(": ", StringComparison.Ordinal))));
                    break;
            }
        }
        Assert.Equal(4, seen.Count); // none, subject, importance and attribution all occurred
        Assert.Equal(copies.Count(c => c.Distortion != MemoryDistortion.None), w.TotalDistorted);
        Assert.Equal(copies.Count(c => c.Distortion == MemoryDistortion.Attribution), w.TotalAttributed);
    }

    [Fact]
    public void Attribution_needs_awe_and_rises_with_it()
    {
        int Attributions(float awe)
        {
            var (w, set, speaker, listener) = Village();
            w.Standing.Awe[set] = awe;
            var original = Death(w, listener);
            for (int i = 0; i < 400; i++) w.RetellForTests(speaker, listener, original);
            return w.TotalAttributed;
        }
        int none = Attributions(0f), some = Attributions(0.3f), many = Attributions(1f);
        Assert.Equal(0, none);
        Assert.True(some > 0 && many > some * 2, $"attributions at awe 0.3: {some}, at 1: {many}");
    }

    [Fact]
    public void God_memories_are_never_attributed_again()
    {
        var (w, set, speaker, listener) = Village();
        w.Standing.Awe[set] = 1f;
        var god = new Memory(w.Tick, MemoryKind.God, [MemoryStore.GodId], "Lightning struck from a clear sky.", 9);
        int before = w.Memories.Of(listener).Count;
        for (int i = 0; i < 200; i++) w.RetellForTests(speaker, listener, god);
        Assert.DoesNotContain(Copies(w, listener, before), c => c.Distortion == MemoryDistortion.Attribution);
        Assert.Equal(0, w.TotalAttributed);
    }

    [Fact]
    public void A_retold_story_keeps_its_core_and_counts_generations()
    {
        var (w, set, a, b) = Village();
        var original = Death(w, a);
        w.RetellForTests(a, b, original);
        var first = w.Memories.Of(b)[^1];
        int c = Enumerable.Range(0, w.Npcs.HighWater).First(s => w.Npcs.Alive[s] && s != a && s != b && w.StageOf(s) != LifeStage.Infant);
        w.RetellForTests(b, c, first);
        var second = w.Memories.Of(c)[^1];
        Assert.Equal(1, first.Generation);
        Assert.Equal(2, second.Generation);
        Assert.StartsWith($"{w.Npcs.Name[b]} told me: ", second.Text);
        Assert.Equal(1, second.Text.Split(" told me: ").Length - 1); // not "b told me: a told me: ..."
    }

    [Fact]
    public void Talk_retells_gossip_until_the_generation_limit()
    {
        var (w, _, speaker, listener) = Village();
        // The speaker knows only one important thing, and only as hearsay.
        w.Memories.Reset(speaker);
        w.Memories.Reset(listener);
        var heard = new Memory(w.Tick, MemoryKind.Gossip, [MemoryStore.GodId], "Bo told me: The god walked among us.", 8) { Generation = 2 };
        w.Memories.Add(speaker, heard, w.Tick);
        for (int i = 0; i < 20 && !w.Memories.Of(listener).Any(m => m.Kind == MemoryKind.Gossip); i++) w.ConverseForTests(speaker, listener);
        var got = w.Memories.Of(listener).Where(m => m.Kind == MemoryKind.Gossip).ToList();
        Assert.Single(got); // the listener now knows the story and is not told it twice
        Assert.Equal(3, got[0].Generation);
        Assert.Contains("walked among us.", got[0].Text);

        w.Memories.Reset(speaker);
        w.Memories.Reset(listener);
        var old = new Memory(w.Tick, MemoryKind.Gossip, [MemoryStore.GodId], "Bo told me: The god walked among us.", 8)
        {
            Generation = (byte)w.Config.MaxGossipGeneration,
        };
        w.Memories.Add(speaker, old, w.Tick);
        for (int i = 0; i < 20; i++) w.ConverseForTests(speaker, listener);
        Assert.DoesNotContain(w.Memories.Of(listener), m => m.Kind == MemoryKind.Gossip);
    }

    [Fact]
    public void Gossip_between_settlements_is_contact()
    {
        var (w, x, z) = Villages.World(5);
        int a = Villages.Found(w, x, z);
        var (bx, bz) = Villages.SpotAway(w, x, z);
        int b = Villages.Found(w, bx, bz);
        Assert.Equal(-1, w.Standing.LastContact(a, b));
        w.ConverseForTests(MythTests.Member(w, a), MythTests.Member(w, b));
        Assert.Equal(w.Tick, w.Standing.LastContact(b, a));
    }

    [Fact]
    public void God_words_are_recognised()
    {
        Assert.True(GodStanding.MentionsGod("The sky watches and judges."));
        Assert.True(GodStanding.MentionsGod("Great stones make the gods remember us."));
        Assert.True(GodStanding.MentionsGod("A VOICE spoke to me."));
        Assert.False(GodStanding.MentionsGod("Walls keep our children safe."));
        Assert.Equal(Eden.Mind.IntentionMapper.MentionsGod("Every tree has a spirit."), GodStanding.MentionsGod("Every tree has a spirit."));
    }
}
