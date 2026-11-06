using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class UtilityScoringTests
{
    private static float Score(ActionKind a, DecisionContext c) => ActionScorer.Score(a, in c);

    private static ActionKind Best(DecisionContext c)
    {
        Span<float> scores = stackalloc float[ActionScorer.ActionCount];
        ActionScorer.ScoreAll(in c, scores);
        return ActionScorer.Best(scores);
    }

    [Fact]
    public void Hungry_npc_scores_eat_and_gather_above_idle()
    {
        var c = DecisionContext.Calm;
        c.Food = 0.1f;
        c.CarriedFood = 3;
        c.FoodKnown = true;
        c.FoodDistance = 30;
        Assert.True(Score(ActionKind.Eat, c) > Score(ActionKind.Idle, c));
        Assert.True(Score(ActionKind.Gather, c) > Score(ActionKind.Idle, c));
        Assert.Equal(ActionKind.Eat, Best(c));

        c.CarriedFood = 0;
        Assert.Equal(0, Score(ActionKind.Eat, c));
        Assert.Equal(ActionKind.Gather, Best(c));
    }

    [Fact]
    public void Fearful_npc_scores_flee_above_court()
    {
        var c = DecisionContext.Calm;
        c.Social = 0.2f;
        c.MateAvailable = true;
        Assert.True(Score(ActionKind.Court, c) > Score(ActionKind.Idle, c), "a calm lonely adult should want to court");

        c.Safety = 0.1f;
        Assert.True(Score(ActionKind.Flee, c) > Score(ActionKind.Court, c));
        Assert.Equal(ActionKind.Flee, Best(c));
    }

    [Fact]
    public void Thirsty_npc_drinks()
    {
        var c = DecisionContext.Calm;
        c.Water = 0.05f;
        Assert.Equal(ActionKind.Drink, Best(c));
    }

    [Fact]
    public void Tired_npc_rests_at_night()
    {
        var c = DecisionContext.Calm;
        c.Rest = 0.4f;
        c.IsNight = true;
        Assert.Equal(ActionKind.Rest, Best(c));
    }

    [Fact]
    public void Content_npc_idles_or_wanders()
    {
        var c = DecisionContext.Calm;
        Assert.Contains(Best(c), new[] { ActionKind.Idle, ActionKind.Explore });
    }

    [Fact]
    public void Children_do_not_court_or_hunt()
    {
        var c = DecisionContext.Calm;
        c.Stage = LifeStage.Child;
        c.Social = 0;
        c.MateAvailable = true;
        c.GameKnown = true;
        c.Food = 0.3f;
        Assert.Equal(0, Score(ActionKind.Court, c));
        Assert.Equal(0, Score(ActionKind.Hunt, c));
    }

    [Fact]
    public void Partners_court_only_when_a_child_is_possible()
    {
        var c = DecisionContext.Calm;
        c.MateAvailable = true;
        c.HasPartner = true;
        c.CanConceive = false;
        Assert.Equal(0, Score(ActionKind.Court, c));
        c.CanConceive = true;
        Assert.True(Score(ActionKind.Court, c) > 0.3f);
    }

    [Fact]
    public void Goal_bonus_tips_the_choice_but_needs_still_win_when_urgent()
    {
        var c = DecisionContext.Calm;
        c.Goal = ActionKind.Pray;
        c.GoalBonus = 0.5f;
        Assert.Equal(ActionKind.Pray, Best(c));

        c.Water = 0f;
        Assert.Equal(ActionKind.Drink, Best(c));
    }

    [Fact]
    public void Later_milestone_actions_score_zero()
    {
        var c = DecisionContext.Calm;
        foreach (var a in new[] { ActionKind.Build, ActionKind.Farm, ActionKind.Craft, ActionKind.Teach, ActionKind.Learn,
                     ActionKind.Trade, ActionKind.Raid, ActionKind.Settle, ActionKind.Invent })
            Assert.Equal(0, Score(a, c));
    }
}
