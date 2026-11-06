using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class MemoryTests
{
    [Fact]
    public void Cap_evicts_low_importance_old_memories_into_the_summary()
    {
        var store = new MemoryStore(cap: 5, ticksPerDay: 240);
        store.Reset(0);
        store.Add(0, new Memory(0, MemoryKind.God, [MemoryStore.GodId], "The sky split open.", 10), 0);
        for (int i = 1; i <= 10; i++)
            store.Add(0, new Memory(i * 240, MemoryKind.Conversation, [], $"chat {i}", 2), i * 240);

        Assert.Equal(5, store.Of(0).Count);
        Assert.Contains(store.Of(0), m => m.Importance == 10);
        Assert.Equal("Faded memories: 6 conversation.", store.Summary(0));
    }

    [Fact]
    public void God_actions_are_remembered_by_everyone_in_range_with_high_importance()
    {
        var w = new World(new SimConfig { Seed = 12 });
        float x = w.Npcs.X[0], z = w.Npcs.Z[0];
        w.Apply(new LightningCommand(x + 15, z, 2));
        for (int s = 0; s < 2; s++)
            Assert.Contains(w.Memories.Of(s), m => m.Kind == MemoryKind.God && m.Importance >= 8);
        Assert.True(w.Npcs.Need(0, Need.Safety) < 0.8f, "lightning nearby should frighten");
    }

    [Fact]
    public void Whisper_is_an_importance_9_memory_for_that_npc_only()
    {
        var w = new World(new SimConfig { Seed = 12 });
        w.Apply(new WhisperCommand(w.Npcs.IdOf(1), "Build a boat."));
        Assert.Contains(w.Memories.Of(1), m => m.Importance == 9 && m.Text.Contains("Build a boat."));
        Assert.DoesNotContain(w.Memories.Of(0), m => m.Text.Contains("Build a boat."));
    }

    [Fact]
    public void Births_and_deaths_are_witnessed_and_chronicled()
    {
        var w = new World(new SimConfig { Seed = 3 });
        for (int t = 0; t < w.Config.TicksPerYear * 3 && w.TotalBirths == 0; t++) w.Step();
        Assert.True(w.TotalBirths > 0);
        Assert.Contains(w.ChronicleTail(50), e => e.Kind == "birth");
        Assert.Contains(w.Memories.Of(0), m => m.Kind == MemoryKind.Birth);

        int witness = 1;
        w.Npcs.X[0] = w.Npcs.X[witness];
        w.Npcs.Z[0] = w.Npcs.Z[witness];
        w.Step();
        string name = w.Npcs.Name[0];
        w.Kill(0, DeathCause.Accident);
        Assert.Contains(w.ChronicleTail(5), e => e.Kind == "death" && e.Text.StartsWith(name));
        Assert.Contains(w.Memories.Of(witness), m => m.Kind == MemoryKind.Death && m.Text.StartsWith(name));
    }

    [Fact]
    public void Talking_spreads_important_memories_as_gossip()
    {
        var w = new World(new SimConfig { Seed = 12, StartingNpcs = 2 });
        w.Apply(new WhisperCommand(w.Npcs.IdOf(0), "The river will rise."));
        // Keep the pair together and lonely so they talk a lot.
        bool spread = false;
        for (int t = 0; t < w.Config.TicksPerDay * 6 && !spread; t++)
        {
            w.Npcs.X[1] = w.Npcs.X[0] + 1;
            w.Npcs.Z[1] = w.Npcs.Z[0];
            w.Step();
            spread = w.Memories.Of(1).Any(m => m.Kind == MemoryKind.Gossip && m.Text.Contains("river"));
        }
        Assert.True(spread, "the whisper never spread by gossip");
    }
}
