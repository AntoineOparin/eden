using Eden.Sim;
using Eden.Sim.Contracts;

namespace Eden.Mind;

/// <summary>What the spotlight needs beyond the snapshot: the camera and the player's recent interactions.</summary>
public sealed record SpotlightContext(
    float FocusX, float FocusZ, bool HasFocus,
    IReadOnlyDictionary<int, long> PlayerInteractions,
    float ProximityRange = 200f,
    float NeighbourRadius = 24f);

/// <summary>
/// Who gets an LLM. Every in-game hour every living NPC (infants excluded, the possessed NPC excluded) scores
/// <c>2·proximityToCamera + 3·isLeader + 2·recentDrama(24h) + 4·playerInteraction(1 day decay) + 1·isPossessedNeighbour</c>;
/// the top <c>MaxThinkingNpcs</c> think. Ties break by id so the pick is stable.
/// </summary>
public static class Spotlight
{
    public static double Score(double proximity, bool leader, double drama, double player, bool possessedNeighbour)
        => 2 * proximity + 3 * (leader ? 1 : 0) + 2 * drama + 4 * player + (possessedNeighbour ? 1 : 0);

    /// <summary>1 at the event, 0 after <paramref name="window"/> ticks.</summary>
    public static double Decay(long now, long then, long window)
        => then < 0 || window <= 0 || now - then >= window ? 0 : 1.0 - Math.Max(0, now - then) / (double)window;

    public static IReadOnlyList<(int Id, double Score)> Rank(IWorldSnapshot snap, SpotlightContext ctx)
    {
        var extras = snap as IMindSnapshot;
        int tph = extras?.TicksPerHour ?? 10, hpd = extras?.HoursPerDay ?? 24;
        long day = (long)tph * hpd;
        var leaders = extras is null ? null : new HashSet<int>(extras.LeaderIds);
        var npcs = snap.Npcs.Span;

        float px = 0, pz = 0;
        bool possessed = false;
        foreach (var v in npcs) if (v.IsPossessed) { px = v.X; pz = v.Z; possessed = true; break; }

        var list = new List<(int, double)>(npcs.Length);
        float r2 = ctx.NeighbourRadius * ctx.NeighbourRadius;
        foreach (var v in npcs)
        {
            if (v.Stage == LifeStage.Infant || v.IsPossessed || v.Pose == NpcPose.Dead) continue;
            double proximity = 0;
            if (ctx.HasFocus)
            {
                float dx = v.X - ctx.FocusX, dz = v.Z - ctx.FocusZ;
                proximity = Math.Max(0, 1 - MathF.Sqrt(dx * dx + dz * dz) / ctx.ProximityRange);
            }
            bool leader = leaders?.Contains(v.Id) ?? false;
            double drama = extras is null ? 0 : Decay(snap.Tick, extras.LastDramaTick(v.Id), day);
            double player = ctx.PlayerInteractions.TryGetValue(v.Id, out long t) ? Decay(snap.Tick, t, day) : 0;
            bool neighbour = possessed && (v.X - px) * (v.X - px) + (v.Z - pz) * (v.Z - pz) <= r2;
            list.Add((v.Id, Score(proximity, leader, drama, player, neighbour)));
        }
        list.Sort((a, b) => a.Item2 != b.Item2 ? b.Item2.CompareTo(a.Item2) : a.Item1.CompareTo(b.Item1));
        return list;
    }

    public static int[] Pick(IWorldSnapshot snap, SpotlightContext ctx, int max)
    {
        var ranked = Rank(snap, ctx);
        var ids = new int[Math.Min(Math.Max(0, max), ranked.Count)];
        for (int i = 0; i < ids.Length; i++) ids[i] = ranked[i].Id;
        return ids;
    }
}
