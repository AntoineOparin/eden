using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class SaveRoundTripTests
{
    private static World Grown(int seed, int ticks)
    {
        var w = new World(new SimConfig { Seed = seed });
        for (int t = 0; t < ticks; t++)
        {
            if (t == 1000) w.Apply(new WeatherCommand("drought", 3000));
            if (t == 2000) w.Apply(new WhisperCommand(w.Npcs.IdOf(0), "You are not alone."));
            w.Step();
        }
        return w;
    }

    [Fact]
    public void Save_then_load_gives_identical_hash_and_identical_future()
    {
        var a = Grown(6, 12_000);
        using var ms = new MemoryStream();
        SaveWriter.Write(a, ms);
        ms.Position = 0;
        var b = SaveReader.Load(ms);

        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(a.Population, b.Population);
        Assert.Equal(a.Tick, b.Tick);

        for (int t = 0; t < 3000; t++) { a.Step(); b.Step(); }
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Header_is_readable_without_the_body()
    {
        var w = Grown(2, 500);
        using var ms = new MemoryStream();
        SaveWriter.Write(w, ms);
        ms.Position = 0;
        var header = SaveReader.ReadHeader(ms);
        Assert.Equal(new SaveHeader(SaveWriter.FormatVersion, 2, 500, w.Population, w.Graph.Data.SetHash), header);
    }

    [Fact]
    public void Save_and_load_commands_restore_the_world_in_place()
    {
        string path = Path.Combine(Path.GetTempPath(), $"eden-save-test-{Environment.ProcessId}.eden");
        try
        {
            var w = Grown(9, 3000);
            w.Apply(new SaveCommand(path));
            ulong saved = w.StateHash();
            for (int t = 0; t < 500; t++) w.Step();
            Assert.NotEqual(saved, w.StateHash());

            w.Apply(new LoadCommand(path));
            Assert.Equal(saved, w.StateHash());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Garbage_is_rejected_without_touching_the_world()
    {
        var w = Grown(4, 100);
        ulong before = w.StateHash();
        using var ms = new MemoryStream("not a save file at all"u8.ToArray());
        Assert.Throws<InvalidDataException>(() => SaveReader.ReadInto(w, ms));
        Assert.Equal(before, w.StateHash());
    }
}
