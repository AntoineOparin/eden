namespace Eden.Sim.Tests;

public class GeneticsTests
{
    [Fact]
    public void Child_genes_come_from_a_parent_unless_mutated()
    {
        var rng = new Rng(5);
        Span<float> mother = stackalloc float[Genetics.Count], father = stackalloc float[Genetics.Count], child = stackalloc float[Genetics.Count];
        for (int trial = 0; trial < 2000; trial++)
        {
            Genetics.Randomize(mother, ref rng);
            Genetics.Randomize(father, ref rng);
            int mutated = Genetics.Crossover(mother, father, child, ref rng, 0.03f, 0.2f);
            int foreign = 0;
            for (int g = 0; g < Genetics.Count; g++)
            {
                Assert.InRange(child[g], 0f, 1f);
                if (child[g] == mother[g] || child[g] == father[g])
                    Assert.InRange(child[g], Math.Min(mother[g], father[g]), Math.Max(mother[g], father[g]));
                else foreign++;
            }
            Assert.Equal(mutated, foreign);
        }
    }

    [Fact]
    public void Mutation_rate_is_about_three_percent()
    {
        var rng = new Rng(9);
        Span<float> mother = stackalloc float[Genetics.Count], father = stackalloc float[Genetics.Count], child = stackalloc float[Genetics.Count];
        int mutated = 0, total = 0;
        for (int trial = 0; trial < 20_000; trial++)
        {
            Genetics.Randomize(mother, ref rng);
            Genetics.Randomize(father, ref rng);
            mutated += Genetics.Crossover(mother, father, child, ref rng, 0.03f, 0.2f);
            total += Genetics.Count;
        }
        Assert.InRange(mutated / (double)total, 0.027, 0.033);
    }

    [Fact]
    public void Without_mutation_both_parents_contribute_about_half()
    {
        var rng = new Rng(11);
        Span<float> mother = stackalloc float[Genetics.Count], father = stackalloc float[Genetics.Count], child = stackalloc float[Genetics.Count];
        mother.Fill(0f);
        father.Fill(1f);
        int fromFather = 0;
        for (int trial = 0; trial < 5000; trial++)
        {
            Assert.Equal(0, Genetics.Crossover(mother, father, child, ref rng, 0f, 0.2f));
            foreach (float g in child) if (g == 1f) fromFather++;
        }
        Assert.InRange(fromFather / (5000.0 * Genetics.Count), 0.48, 0.52);
    }

    [Fact]
    public void Newborns_in_a_world_inherit_from_their_parents()
    {
        var w = new World(new SimConfig { Seed = 3 });
        for (int i = 0; i < w.Config.TicksPerYear * 8 && w.TotalBirths < 3; i++) w.Step();
        Assert.True(w.TotalBirths >= 1, "no births in 8 years on seed 3");
        for (int s = 0; s < w.Npcs.HighWater; s++)
        {
            if (!w.Npcs.Alive[s] || w.Npcs.Lineage[s] == 0) continue;
            bool parents = w.Npcs.TryGetSlot(w.Npcs.Mother[s], out int m) & w.Npcs.TryGetSlot(w.Npcs.Father[s], out int f);
            Assert.True(parents, "parents of an early child should still be alive");
            int foreign = 0;
            for (int g = 0; g < Genetics.Count; g++)
            {
                float v = w.Npcs.Genes[s * Genetics.Count + g];
                if (v != w.Npcs.Genes[m * Genetics.Count + g] && v != w.Npcs.Genes[f * Genetics.Count + g]) foreign++;
            }
            Assert.True(foreign <= 3, $"child has {foreign} genes from neither parent");
        }
    }
}
