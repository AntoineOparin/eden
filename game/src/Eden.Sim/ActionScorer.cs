using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>Everything the utility scorer looks at for one NPC at one decision point. Plain data, no references.</summary>
public struct DecisionContext
{
    // Needs, 0 = desperate .. 1 = satisfied.
    public float Food, Water, Rest, Warmth, Safety, Social, Purpose;

    // Genes (personality), 0..1.
    public float Strength, Curiosity, Sociability, Aggression, Fertility, Empathy, Piety, Ambition;

    public LifeStage Stage;
    public float CarriedFood, MaxCarriedFood;
    public bool IsNight;

    // What the NPC can see / reach right now.
    public bool FoodKnown, GameKnown, WaterKnown;
    public float FoodDistance, GameDistance;
    public bool MateAvailable, HasPartner, CanConceive;
    public bool TalkPartnerNearby;
    public bool DependentHungry;

    // Goal from an intention (Mind or player): a bonus, never a forced action.
    public ActionKind Goal;
    public float GoalBonus;

    /// <summary>A well-fed, rested, calm adult with nothing around. Tests start from this and change fields.</summary>
    public static DecisionContext Calm => new()
    {
        Food = 1, Water = 1, Rest = 1, Warmth = 1, Safety = 1, Social = 1, Purpose = 1,
        Strength = 0.5f, Curiosity = 0.5f, Sociability = 0.5f, Aggression = 0.5f, Fertility = 0.5f,
        Empathy = 0.5f, Piety = 0.5f, Ambition = 0.5f,
        Stage = LifeStage.Adult, MaxCarriedFood = 8, WaterKnown = true,
    };
}

/// <summary>
/// Utility AI: scores every <see cref="ActionKind"/> from needs × personality × context. Pure and allocation-free.
/// Actions whose systems arrive in later milestones (build, farm, craft, teach, learn, trade, raid, settle,
/// invent) score 0 in M1 unless an intention's goal bonus is attached to an executable action.
/// </summary>
public static class ActionScorer
{
    public static readonly int ActionCount = Enum.GetValues<ActionKind>().Length;

    public const float IdleScore = 0.05f;

    public static void ScoreAll(in DecisionContext c, Span<float> scores)
    {
        for (int i = 0; i < ActionCount; i++) scores[i] = Score((ActionKind)i, in c);
        if (c.GoalBonus > 0 && (scores[(int)c.Goal] > 0 || IsSelfContained(c.Goal)))
            scores[(int)c.Goal] += c.GoalBonus;
    }

    /// <summary>Index of the best action; ties go to the lowest enum value so the result is deterministic.</summary>
    public static ActionKind Best(ReadOnlySpan<float> scores)
    {
        int best = 0;
        for (int i = 1; i < scores.Length; i++) if (scores[i] > scores[best]) best = i;
        return (ActionKind)best;
    }

    /// <summary>Actions that need no target in the world, so a goal can always run them.</summary>
    public static bool IsSelfContained(ActionKind a) => a is ActionKind.Idle or ActionKind.Rest or ActionKind.Pray or ActionKind.Explore;

    public static float Score(ActionKind action, in DecisionContext c)
    {
        bool adult = c.Stage is LifeStage.Adult or LifeStage.Elder;
        bool infant = c.Stage == LifeStage.Infant;
        float dFood = 1 - c.Food, dWater = 1 - c.Water, dRest = 1 - c.Rest, dSocial = 1 - c.Social, dPurpose = 1 - c.Purpose;
        float fear = 1 - c.Safety;
        float carryRoom = c.MaxCarriedFood > 0 ? Math.Clamp(1 - c.CarriedFood / c.MaxCarriedFood, 0, 1) : 0;
        // Basic survival gates courtship and leisure.
        float comfort = Math.Min(1, c.Food * 2.5f) * Math.Min(1, c.Water * 2.5f);

        switch (action)
        {
            case ActionKind.Idle:
                return IdleScore;
            case ActionKind.Eat:
                return c.CarriedFood >= 1 && dFood > 0.15f ? 1.3f * Pow15(dFood) + 0.05f : 0;
            case ActionKind.Drink:
                return c.WaterKnown && dWater > 0.1f ? 1.3f * Pow15(dWater) + 0.05f : 0;
            case ActionKind.Rest:
                return c.IsNight ? 0.3f + 0.6f * dRest : 1.1f * Pow15(dRest);
            case ActionKind.Gather:
            {
                if (infant || !c.FoodKnown || carryRoom <= 0.05f) return 0;
                float s = 0.9f * dFood + 0.35f * carryRoom * (0.5f + 0.5f * c.Ambition);
                return s * Near(c.FoodDistance) * (adult ? 1f : 0.6f);
            }
            case ActionKind.Hunt:
            {
                if (!adult || !c.GameKnown || carryRoom <= 0.3f) return 0;
                float s = 0.7f * dFood + 0.3f * carryRoom;
                return s * (0.4f + 0.4f * c.Strength + 0.2f * c.Aggression) * Near(c.GameDistance);
            }
            case ActionKind.Court:
            {
                if (!adult || !c.MateAvailable || (c.HasPartner && !c.CanConceive)) return 0;
                float s = c.HasPartner
                    ? 0.3f + 0.3f * dSocial + 0.2f * c.Fertility
                    : 0.15f + 0.35f * dSocial + 0.2f * c.Sociability + 0.25f * c.Fertility;
                return s * c.Safety * comfort;
            }
            case ActionKind.Talk:
                return c.TalkPartnerNearby && !infant ? dSocial * (0.4f + 0.6f * c.Sociability) * 0.9f * comfort : 0;
            case ActionKind.Give:
                return adult && c.DependentHungry && c.CarriedFood >= 1 ? 0.55f + 0.4f * c.Empathy : 0;
            case ActionKind.Flee:
                return fear > 0.2f && !infant ? 1.6f * Pow15(fear) * (1 - 0.5f * c.Aggression) : 0;
            case ActionKind.Explore:
            {
                if (infant) return 0;
                float s = 0.06f * c.Curiosity + (c.FoodKnown ? 0 : 0.8f * dFood) + (c.WaterKnown ? 0 : dWater);
                return s * (adult ? 1f : 0.4f);
            }
            case ActionKind.Pray:
                return adult ? c.Piety * (0.45f * dPurpose + 0.3f * fear) : 0;
            default:
                return 0; // M3/M4 actions.
        }
    }

    private static float Pow15(float v) => v <= 0 ? 0 : v * MathF.Sqrt(v);

    private static float Near(float distance) => 1f / (1f + distance / 150f);
}
