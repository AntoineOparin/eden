using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class IntentionValidatorTests
{
    private static (World w, int first, int second) Pair()
    {
        var w = new World(new SimConfig { Seed = 12 });
        return (w, w.Npcs.IdOf(0), w.Npcs.IdOf(1));
    }

    private static GoalIntention Goal(int id, ActionKind a, string? target = null, int priority = 5)
        => new(id, 0, IntentionSource.Llm, a, target, null, priority);

    private static ConceptProposal Concept(string id = "ember-keeping", IReadOnlyList<string>? prereqs = null, params ConceptEffect[] effects)
        => new(id, "Fire", "Keeping a flame alive.", prereqs ?? [], 0.3f,
            effects.Length > 0 ? effects : [new ConceptEffect(ConceptEffectKind.UnlockAction, Action: ActionKind.Craft)]);

    [Fact]
    public void Valid_goal_is_accepted_and_becomes_a_bonus_not_a_forced_action()
    {
        var (w, a, _) = Pair();
        Assert.Null(w.Apply(Goal(a, ActionKind.Pray, priority: 8)));
        Assert.Equal(ActionKind.Pray, w.Npcs.GoalAction[0]);
        Assert.True(w.Npcs.GoalBonus[0] > 0);
        Assert.True(w.Npcs.GoalUntil[0] > w.Tick);
    }

    [Fact]
    public void Dead_or_unknown_npc_is_rejected()
    {
        var (w, a, _) = Pair();
        Assert.Equal("no such npc", w.Validator.Reject(Goal(12345, ActionKind.Eat)));
        w.Kill(0, DeathCause.Accident);
        Assert.Equal("no such npc", w.Validator.Reject(Goal(a, ActionKind.Eat)));
    }

    [Theory]
    [InlineData(ActionKind.Build)]
    [InlineData(ActionKind.Raid)]
    [InlineData(ActionKind.Invent)]
    public void Locked_actions_are_rejected(ActionKind action)
    {
        var (w, a, _) = Pair();
        Assert.Contains("not unlocked", w.Validator.Reject(Goal(a, action)));
    }

    [Fact]
    public void Out_of_vocabulary_values_are_rejected()
    {
        var (w, a, b) = Pair();
        Assert.Equal("unknown action", w.Validator.Reject(Goal(a, (ActionKind)99)));
        Assert.Equal("priority out of range", w.Validator.Reject(Goal(a, ActionKind.Eat, priority: 11)));
        Assert.Equal("unknown target", w.Validator.Reject(Goal(a, ActionKind.Gather, "unobtainium")));
        Assert.Equal("target is self", w.Validator.Reject(Goal(a, ActionKind.Talk, a.ToString())));
        Assert.Null(w.Validator.Reject(Goal(a, ActionKind.Talk, b.ToString())));
        Assert.Null(w.Validator.Reject(Goal(a, ActionKind.Drink, "water")));
    }

    [Fact]
    public void Far_away_target_is_out_of_range()
    {
        var (w, a, b) = Pair();
        w.Npcs.X[1] = w.Npcs.X[0] + w.Config.SearchRadius * 3;
        Assert.Equal("target npc out of range", w.Validator.Reject(Goal(a, ActionKind.Talk, b.ToString())));
    }

    [Fact]
    public void Belief_importance_and_text_are_checked()
    {
        var (w, a, _) = Pair();
        Assert.NotNull(w.Validator.Reject(new BeliefIntention(a, 0, IntentionSource.Llm, "The sky watches.", 0)));
        Assert.NotNull(w.Validator.Reject(new BeliefIntention(a, 0, IntentionSource.Llm, "", 5)));
        Assert.NotNull(w.Validator.Reject(new BeliefIntention(a, 0, IntentionSource.Llm, new string('x', 281), 5)));
        Assert.Null(w.Apply(new BeliefIntention(a, 0, IntentionSource.Llm, "The sky watches.", 8)));
        Assert.Contains(w.Memories.Of(0), m => m.Kind == MemoryKind.Belief && m.Importance == 8);
    }

    [Fact]
    public void Speech_becomes_a_memory_for_npcs_in_earshot()
    {
        var (w, a, b) = Pair();
        w.Npcs.X[1] = w.Npcs.X[0] + 1;
        w.Npcs.Z[1] = w.Npcs.Z[0];
        w.Step(); // refresh the spatial index
        Assert.Null(w.Apply(new SayIntention(a, w.Tick, IntentionSource.Llm, "Look at the stars.", b)));
        Assert.Contains(w.Memories.Of(1), m => m.Kind == MemoryKind.Speech && m.Text.Contains("Look at the stars."));
    }

    [Fact]
    public void Invention_follows_the_concept_schema_rules()
    {
        var (w, a, _) = Pair();
        Assert.Equal("concept id is not an identifier", w.Validator.Reject(new InventionIntention(a, 0, IntentionSource.Llm, Concept("Fire!"))));
        Assert.Equal("concept id is not an identifier", w.Validator.Reject(new InventionIntention(a, 0, IntentionSource.Llm, Concept("fire\n"))));
        Assert.Equal("unknown prerequisite metal", w.Validator.Reject(new InventionIntention(a, 0, IntentionSource.Llm, Concept(prereqs: ["metal"]))));
        Assert.Equal("bad yieldMod", w.Validator.Reject(new InventionIntention(a, 0, IntentionSource.Llm,
            Concept(effects: new ConceptEffect(ConceptEffectKind.YieldMod, Resource: "berry", Factor: 5f)))));
        Assert.Equal("bad unlockAction", w.Validator.Reject(new InventionIntention(a, 0, IntentionSource.Llm,
            Concept(effects: new ConceptEffect(ConceptEffectKind.UnlockAction, Action: ActionKind.Craft, Text: "extra")))));
    }

    [Fact]
    public void Invention_is_inserted_and_its_unlocks_apply_to_the_inventor()
    {
        var (w, a, b) = Pair();
        Assert.Null(w.Apply(new InventionIntention(a, 0, IntentionSource.Llm, Concept())));
        Assert.Equal(0.3f, w.Proficiency(0, "ember-keeping"));
        Assert.True(w.IsActionUnlocked(0, ActionKind.Craft));
        Assert.False(w.IsActionUnlocked(1, ActionKind.Craft));
        Assert.Equal("concept already exists", w.Validator.Reject(new InventionIntention(b, 0, IntentionSource.Llm, Concept())));

        // Prerequisites must be mastered (>= 0.8 for inventions, 50-knowledge-data.md), not just known.
        var smelting = Concept("smelting", ["fire"]);
        Assert.Equal("prerequisite fire not mastered", w.Validator.Reject(new InventionIntention(a, 0, IntentionSource.Llm, smelting)));
        w.SetKnowledge(0, "fire", 0.85f);
        Assert.Null(w.Validator.Reject(new InventionIntention(a, 0, IntentionSource.Llm, smelting)));
    }

    [Fact]
    public void Queue_refuses_npcs_known_to_be_dead_and_never_blocks()
    {
        using var host = new SimHost(new SimConfig { Seed = 12 }, maxIntentionQueue: 2);
        int alive = host.Latest.Npcs.Span[0].Id;
        Assert.True(host.Intentions.TryPush(Goal(alive, ActionKind.Rest)));
        Assert.True(host.Intentions.TryPush(Goal(alive, ActionKind.Rest)));
        Assert.False(host.Intentions.TryPush(Goal(alive, ActionKind.Rest)), "queue should be full");
        host.StepSync(1);
        Assert.Empty(host.Diagnostics);

        var snap = (WorldSnapshot)host.Latest;
        Assert.False(snap.IsKnownDead(alive));
        Assert.False(snap.IsKnownDead(1)); // never existed: accepted, rejected later by the validator
    }

    [Fact]
    public void Snapshot_knows_which_ids_died_even_after_their_slot_is_reused()
    {
        var w = new World(new SimConfig { Seed = 12 });
        int dead = w.Npcs.IdOf(0);
        w.Kill(0, DeathCause.Accident);
        w.SpawnAdult(w.Npcs.X[1], w.Npcs.Z[1], Sex.Female, 20); // reuses slot 0 with a new version
        int reborn = w.Npcs.IdOf(0);
        Assert.NotEqual(dead, reborn);

        var snap = w.Snapshot();
        Assert.True(snap.IsKnownDead(dead));
        Assert.False(snap.IsKnownDead(reborn));
        Assert.True(snap.IsAlive(reborn));
        Assert.False(snap.IsAlive(dead));
        Assert.False(new IntentionQueue(8, () => snap).TryPush(Goal(dead, ActionKind.Rest)));
    }
}
