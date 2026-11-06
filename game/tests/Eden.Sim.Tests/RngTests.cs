namespace Eden.Sim.Tests;

public class RngTests
{
    [Fact]
    public void Same_seed_same_sequence()
    {
        var a = new Rng(42);
        var b = new Rng(42);
        for (int i = 0; i < 1000; i++) Assert.Equal(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void Different_seeds_differ()
    {
        var a = new Rng(1);
        var b = new Rng(2);
        Assert.NotEqual(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void Floats_are_in_unit_interval_and_roughly_uniform()
    {
        var rng = new Rng(7);
        double sum = 0;
        for (int i = 0; i < 100_000; i++)
        {
            float f = rng.NextFloat();
            Assert.InRange(f, 0f, 0.99999994f);
            sum += f;
        }
        Assert.InRange(sum / 100_000, 0.49, 0.51);
    }

    [Fact]
    public void NextInt_covers_full_range()
    {
        var rng = new Rng(3);
        var seen = new bool[10];
        for (int i = 0; i < 1000; i++) seen[rng.NextInt(10)] = true;
        Assert.All(seen, Assert.True);
    }
}
