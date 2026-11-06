using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>Standing job of an adult settlement member. Jobs weight the scorer, they never force an action.</summary>
public enum Job : byte { None, Forager, Farmer, Builder }

public sealed partial class World
{
    public Job JobOfSlot(int s) => s < _job.Length ? (Job)_job[s] : Job.None;

    /// <summary>
    /// Daily, per settlement: builders from open construction sites, farmers from finished farm plots,
    /// everyone else forages. Strong adults build, patient adults farm.
    /// </summary>
    private void AssignJobs(int k)
    {
        var n = Npcs;
        var b = Buildings;
        int sites = 0, plots = 0;
        for (int i = 0; i < b.HighWater; i++)
        {
            if (!b.Alive[i] || b.Settlement[i] != k) continue;
            if (b.Progress[i] < 1f) sites++;
            else if (b.Kind[i] == BuildingKinds.FarmPlot) plots++;
        }
        _queue.Clear();
        foreach (int s in MembersOf(k))
        {
            if (!n.Alive[s] || SettlementOfSlot(s) != k) continue;
            if (StageOf(s) is LifeStage.Adult or LifeStage.Elder) { _queue.Add(s); _job[s] = (byte)Job.Forager; }
            else _job[s] = (byte)Job.None;
        }
        int adults = _queue.Count;
        int builders = Math.Min(sites * 2, adults / 3 + 1);
        int farmers = plots == 0 ? 0 : Math.Min(Math.Max(1, (plots + 1) / 2), Math.Max(1, adults / 2));
        Pick(Job.Builder, builders, Gene.Strength);
        Pick(Job.Farmer, farmers, Gene.Patience);

        void Pick(Job job, int count, Gene g)
        {
            for (int c = 0; c < count; c++)
            {
                int best = -1;
                float bestV = -1f;
                foreach (int s in _queue)
                {
                    if (_job[s] != (byte)Job.Forager) continue;
                    float v = Trait(s, g) + (job == Job.Farmer ? 0.3f * n.GatherSkill[s] : 0f);
                    if (v > bestV) { bestV = v; best = s; }
                }
                if (best < 0) return;
                _job[best] = (byte)job;
            }
        }
    }
}
