using Eden.Mind;
using Eden.Mind.Contracts;
using Eden.Sim.Contracts;

namespace Eden.Sim.Tests.Mind;

public class IntentionMapperTests
{
    public static IEnumerable<object[]> ThinkResponseFixtures()
        => Directory.GetFiles(Path.Combine(Contracts.Dir, "fixtures"), "think.response.*.json").Order()
            .Select(f => new object[] { Path.GetFileName(f) });

    [Theory]
    [MemberData(nameof(ThinkResponseFixtures))]
    public void Every_think_response_fixture_maps_or_is_rejected_as_named(string file)
    {
        bool expectOk = file.Contains(".ok", StringComparison.Ordinal);
        var json = File.ReadAllText(Contracts.Fixture(file));
        bool parsed = IntentionMapper.TryParseThink(json, out var response, out var error);
        var mapped = parsed ? IntentionMapper.FromThink(response, 7, 100, ThinkMode.Thought) : MappedResponse.Reject(error!);
        if (expectOk) Assert.Null(mapped.Rejected);
        else Assert.NotNull(mapped.Rejected);
    }

    [Fact]
    public void Ok_fixture_becomes_goals_say_and_belief()
    {
        IntentionMapper.TryParseThink(File.ReadAllText(Contracts.Fixture("think.response.ok.json")), out var r, out _);
        var m = IntentionMapper.FromThink(r, 7, 100, ThinkMode.Thought);
        var goals = m.Intentions.OfType<GoalIntention>().ToList();
        Assert.Equal([ActionKind.Gather, ActionKind.Talk], goals.Select(g => g.Action));
        Assert.All(goals, g => Assert.Equal(IntentionMapper.GoalPriority, g.Priority));
        Assert.All(m.Intentions, i => Assert.Equal(IntentionSource.Llm, i.Source));
        Assert.Equal("berries", goals[0].Target);
        Assert.Equal("The sky struck the hill. We should not sleep there.", m.Intentions.OfType<SayIntention>().Single().Text);
        Assert.Equal(7, m.Intentions.OfType<BeliefIntention>().Single().Importance);
    }

    [Fact]
    public void Empty_plan_maps_to_nothing()
    {
        IntentionMapper.TryParseThink(File.ReadAllText(Contracts.Fixture("think.response.ok.2.json")), out var r, out _);
        Assert.Empty(IntentionMapper.FromThink(r, 7, 100, ThinkMode.Thought).Intentions);
    }

    [Fact]
    public void Reflection_keeps_only_the_belief_at_importance_seven_or_more()
    {
        var r = new ThinkResponse([new PlannedGoal(ActionKind.Eat, null, null)], "hi", new ProposedBelief("The voice in the sky watches us.", 4), null);
        var m = IntentionMapper.FromThink(r, 7, 100, ThinkMode.Reflection);
        var b = Assert.IsType<BeliefIntention>(Assert.Single(m.Intentions));
        Assert.Equal(7, b.Importance);
        Assert.True(IntentionMapper.MentionsGod(b.Text));
        Assert.False(IntentionMapper.MentionsGod("Berries grow by the river."));
    }

    [Fact]
    public void Invention_only_when_allowed_and_extra_goals_are_dropped()
    {
        var concept = new ConceptProposal("smoke-signals", "Smoke signals", "Talking with smoke.", ["fire"], 0.3f,
            [new ConceptEffect(ConceptEffectKind.Belief, Text: "Smoke carries words.")]);
        var plan = Enumerable.Range(0, 5).Select(_ => new PlannedGoal(ActionKind.Gather, "wood", null)).ToList();
        var r = new ThinkResponse(plan, null, null, concept);
        Assert.NotNull(IntentionMapper.FromThink(r, 7, 1, ThinkMode.Thought, allowInvention: false).Rejected);
        var ok = IntentionMapper.FromThink(r, 7, 1, ThinkMode.Thought, allowInvention: true, maxGoals: 3);
        Assert.Equal(3, ok.Intentions.OfType<GoalIntention>().Count());
        Assert.Single(ok.Intentions.OfType<InventionIntention>());
    }

    [Fact]
    public void Invention_json_with_type_discriminator_and_tags_deserialises()
    {
        const string json = """
            {"plan":[],"invention":{"id":"irrigation","name":"Irrigation","description":"Channels.","prerequisites":["farming"],
             "difficulty":0.5,"effects":[{"type":"yieldMod","resource":"grain","factor":1.5}],"tags":["farming"]}}
            """;
        Assert.True(IntentionMapper.TryParseThink(json, out var r, out var e), e);
        var eff = Assert.Single(r!.Invention!.Effects);
        Assert.Equal(ConceptEffectKind.YieldMod, eff.Kind);
        Assert.Equal(1.5f, eff.Factor);
    }

    [Fact]
    public void Chat_reply_is_spoken_and_memory_is_kept_important_enough_to_gossip()
    {
        IntentionMapper.TryParseChat(File.ReadAllText(Contracts.Fixture("chat.response.ok.json")), out var ok, out _);
        var m = IntentionMapper.FromChat(ok, 2, 50);
        Assert.Null(m.Rejected);
        Assert.Single(m.Intentions.OfType<SayIntention>());
        Assert.Equal(8, m.Intentions.OfType<BeliefIntention>().Single().Importance);

        var low = IntentionMapper.FromChat(new ChatResponse("Yes.", new ProposedBelief("It spoke to me.", 2), null), 2, 50);
        Assert.Equal(IntentionMapper.DialogueMinImportance, low.Intentions.OfType<BeliefIntention>().Single().Importance);

        Assert.False(IntentionMapper.TryParseChat(File.ReadAllText(Contracts.Fixture("chat.response.bad.json")), out var bad, out _)
                     && IntentionMapper.Check(bad) is null);
    }

    [Fact]
    public void Long_replies_are_clipped_to_what_the_sim_accepts()
    {
        var m = IntentionMapper.FromChat(new ChatResponse(new string('a', 600), null, "awed"), 2, 50);
        Assert.Equal(IntentionMapper.MaxSayLength, m.Intentions.OfType<SayIntention>().Single().Text.Length);
    }
}
