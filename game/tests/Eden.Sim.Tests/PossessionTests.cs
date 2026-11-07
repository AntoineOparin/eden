using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

public class PossessionTests
{
    /// <summary>Two adults who know each other, side by side, plus a stranger 4 m away. Slots 0, 1, 2.</summary>
    private static World Trio(int seed = 5)
    {
        var w = new World(new SimConfig { Seed = seed, DiscoveryRate = 0 });
        w.Npcs.X[1] = w.Npcs.X[0] + 1;
        w.Npcs.Z[1] = w.Npcs.Z[0];
        w.Relationships.Adjust(w.Npcs.IdOf(0), w.Npcs.IdOf(1), 0.1f);
        w.SpawnAdult(w.Npcs.X[0], w.Npcs.Z[0] + 4, Sex.Female, 30);
        w.Step();
        return w;
    }

    private static int Id(World w, int slot) => w.Npcs.IdOf(slot);

    private static bool Remembers(World w, int slot, string fragment)
        => w.Memories.Of(slot).Any(m => m.Text.Contains(fragment, StringComparison.Ordinal));

    [Fact]
    public void Needs_still_tick_while_possessed()
    {
        var w = Trio();
        w.Npcs.Food[0] = 0; // nothing for the body's instinct to eat
        float food = w.Npcs.Need(0, Need.Food), water = w.Npcs.Need(0, Need.Water);
        w.Apply(new PossessCommand(Id(w, 0)));
        for (int i = 0; i < 120; i++) w.Step();
        Assert.Equal(Id(w, 0), w.PossessedId);
        Assert.True(w.Npcs.Need(0, Need.Food) < food - 0.1f, $"food {food} -> {w.Npcs.Need(0, Need.Food)}");
        Assert.True(w.Npcs.Need(0, Need.Water) < water - 0.1f || w.Npcs.Need(0, Need.Water) == 1f);
    }

    [Fact]
    public void Known_neighbours_notice_the_first_possessed_tick_and_the_release()
    {
        var w = Trio();
        string name = w.Npcs.Name[0];
        w.Apply(new PossessCommand(Id(w, 0)));
        w.Step();
        Assert.Contains(w.Memories.Of(1), m => m.Text == $"{name} stopped and stared, as if seeing everything for the first time." && m.Importance == 5);
        Assert.False(Remembers(w, 2, "stopped and stared")); // a stranger does not notice anything odd
        int before = w.Memories.Of(1).Count(m => m.Text.Contains("stopped and stared"));
        for (int i = 0; i < 5; i++) w.Step();
        Assert.Equal(before, w.Memories.Of(1).Count(m => m.Text.Contains("stopped and stared"))); // once, not per tick

        w.Apply(new ReleasePossessionCommand());
        Assert.Equal(-1, w.PossessedId);
        Assert.True(Remembers(w, 1, $"{name} came back to themselves."));
        Assert.Contains(w.Memories.Of(0), m => m.Text == "I lost time; my body moved without me." && m.Kind == MemoryKind.God);
        Assert.False(Remembers(w, 2, "came back"));
    }

    [Fact]
    public void Walking_on_the_first_tick_reads_as_walking_oddly()
    {
        var w = Trio();
        w.Apply(new PossessCommand(Id(w, 0)));
        w.Apply(new PossessedMoveCommand(0, 1, false));
        w.Step();
        Assert.True(Remembers(w, 1, "began walking oddly"));
    }

    [Fact]
    public void Release_without_a_tick_leaves_only_the_blackout()
    {
        var w = Trio();
        w.Apply(new PossessCommand(Id(w, 0)));
        w.Apply(new ReleasePossessionCommand());
        Assert.False(Remembers(w, 1, "came back"));
        Assert.True(Remembers(w, 0, "I lost time"));
    }

    [Fact]
    public void Movement_follows_input_at_the_shared_speed_formula()
    {
        var w = Trio();
        w.Apply(new PossessCommand(Id(w, 0)));
        float x0 = w.Npcs.X[0], z0 = w.Npcs.Z[0];
        w.Apply(new PossessedMoveCommand(3, 4, false)); // not normalised on purpose
        w.Step();
        float moved = MathF.Sqrt((w.Npcs.X[0] - x0) * (w.Npcs.X[0] - x0) + (w.Npcs.Z[0] - z0) * (w.Npcs.Z[0] - z0));
        if (w.Terrain.IsWalkable(x0 + 0.6f, z0 + 0.8f))
            Assert.Equal(World.PossessedSpeed(w.Config, LifeStage.Adult, false), moved, 3);
        Assert.Equal(MathF.Atan2(0.6f, 0.8f), w.Npcs.Heading[0], 3);

        w.Apply(new PossessedMoveCommand(0, 0, true));
        float x1 = w.Npcs.X[0];
        w.Step();
        Assert.Equal(x1, w.Npcs.X[0]); // no input, no drift
        Assert.Equal(2.2f, World.PossessedSpeed(w.Config, LifeStage.Adult, true) / World.PossessedSpeed(w.Config, LifeStage.Adult, false), 3);
    }

    [Fact]
    public void Talk_converses_and_both_remember_it()
    {
        var w = Trio();
        w.Apply(new PossessCommand(Id(w, 0)));
        w.Npcs.NeedRef(1, Need.Social) = 0.2f;
        float affinity = w.Relationships.Get(Id(w, 0), Id(w, 1)).Affinity;
        w.Apply(new PossessedInteractCommand(Id(w, 1), "talk"));
        w.Apply(new PossessedInteractCommand(Id(w, 1), "talk")); // pressed twice: one memory
        Assert.True(w.Npcs.Need(1, Need.Social) > 0.2f);
        Assert.True(w.Relationships.Get(Id(w, 0), Id(w, 1)).Affinity > affinity);
        string text = $"{w.Npcs.Name[0]} came up and talked with {w.Npcs.Name[1]}.";
        Assert.Single(w.Memories.Of(0), m => m.Text == text);
        Assert.Single(w.Memories.Of(1), m => m.Text == text);
    }

    [Fact]
    public void Give_hands_over_food_and_builds_affinity_and_debt()
    {
        var w = Trio();
        int a = Id(w, 0), b = Id(w, 2); // the stranger
        w.Npcs.X[2] = w.Npcs.X[0] + 1.5f;
        w.Npcs.Z[2] = w.Npcs.Z[0];
        w.Npcs.Food[0] = 5;
        w.Npcs.Food[2] = 0;
        w.Apply(new PossessCommand(a));
        w.Apply(new PossessedInteractCommand(b, "give:food"));
        Assert.Equal(3f, w.Npcs.Food[0]);
        Assert.Equal(2f, w.Npcs.Food[2]);
        var r = w.Relationships.Get(a, b);
        Assert.True(r.Affinity > 0 && r.Trust > 0 && r.Debt != 0);
        string text = $"{w.Npcs.Name[0]} gave {w.Npcs.Name[2]} 2 food.";
        Assert.Contains(w.Memories.Of(0), m => m.Text == text && m.Kind == MemoryKind.Gift);
        Assert.Contains(w.Memories.Of(2), m => m.Text == text && m.Kind == MemoryKind.Gift);

        w.Npcs.Food[0] = 0;
        w.Apply(new PossessedInteractCommand(b, "give:food"));
        Assert.Equal(2f, w.Npcs.Food[2]);
        Assert.True(w.TryDequeueLog(out _));
    }

    [Fact]
    public void Out_of_reach_and_unknown_verbs_are_logged()
    {
        var w = Trio();
        w.Apply(new PossessCommand(Id(w, 0)));
        w.Npcs.X[2] = w.Npcs.X[0] + 40;
        w.Apply(new PossessedInteractCommand(Id(w, 2), "talk"));
        w.Apply(new PossessedInteractCommand(Id(w, 1), "dance"));
        Assert.True(w.TryDequeueLog(out var first));
        Assert.Contains("out of reach", first);
        Assert.True(w.TryDequeueLog(out var second));
        Assert.Contains("unknown verb", second);
    }

    [Fact]
    public void Work_alongside_joins_the_partners_gathering_and_yields_food()
    {
        var w = Trio();
        float x = w.Npcs.X[0], z = w.Npcs.Z[0];
        int bush = w.Resources.Add(ResourceKind.Berry, x + 2, z, 40);
        w.Npcs.Food[0] = 0;
        // The partner is gathering at the bush.
        w.Npcs.Action[1] = ActionKind.Gather;
        w.Npcs.TargetNode[1] = bush;
        w.Apply(new PossessCommand(Id(w, 0)));
        w.Apply(new PossessedInteractCommand(Id(w, 1), "work"));
        Assert.Equal(ActionKind.Gather, w.Npcs.Action[0]);
        Assert.Equal(bush, w.Npcs.TargetNode[0]);
        for (int i = 0; i < w.Config.PossessedWorkTicks; i++) w.Step();
        Assert.True(w.Npcs.Food[0] >= 1f, $"food {w.Npcs.Food[0]}");
        Assert.True(Remembers(w, 1, "at work, without a word"));
    }

    [Fact]
    public void Work_with_nobody_uses_the_nearest_bush_and_moving_cancels_it()
    {
        var w = Trio();
        float x = w.Npcs.X[0], z = w.Npcs.Z[0];
        w.Resources.Add(ResourceKind.Berry, x + 1, z, 40);
        w.Npcs.Food[0] = 0;
        w.Apply(new PossessCommand(Id(w, 0)));
        w.Apply(new PossessedInteractCommand(-1, "work"));
        Assert.Equal(ActionKind.Gather, w.Npcs.Action[0]);
        w.Apply(new PossessedMoveCommand(1, 0, false));
        w.Step();
        Assert.Equal(ActionKind.Explore, w.Npcs.Action[0]); // walking away ends the work
    }

    [Fact]
    public void Infants_cannot_be_possessed()
    {
        var w = Trio();
        w.Npcs.BirthTick[2] = w.Tick; // newborn
        w.Apply(new PossessCommand(Id(w, 2)));
        Assert.Equal(-1, w.PossessedId);
        Assert.True(w.TryDequeueLog(out var line));
        Assert.Contains("infant", line);
    }

    [Fact]
    public void Possession_is_not_saved_and_a_load_releases_it()
    {
        var w = Trio();
        w.Apply(new PossessCommand(Id(w, 0)));
        w.Apply(new PossessedMoveCommand(1, 0, true));
        for (int i = 0; i < 10; i++) w.Step();
        using var ms = new MemoryStream();
        SaveWriter.Write(w, ms);
        byte[] first = ms.ToArray();
        ms.Position = 0;
        var loaded = SaveReader.Load(ms);
        Assert.Equal(-1, loaded.PossessedId);
        Assert.Equal(w.Population, loaded.Population);
        for (int i = 0; i < 20; i++) loaded.Step(); // the body goes back to its own decisions
        Assert.Equal(-1, loaded.PossessedId);

        // Saving the reloaded world again gives the same bytes: nothing possession-related leaked into the save.
        var again = SaveReader.Load(new MemoryStream(first));
        using var ms2 = new MemoryStream();
        SaveWriter.Write(again, ms2);
        Assert.Equal(first, ms2.ToArray());
    }

    [Fact]
    public void Same_commands_give_the_same_world()
    {
        ulong Run()
        {
            var w = Trio(9);
            w.Apply(new PossessCommand(Id(w, 0)));
            for (int i = 0; i < 300; i++)
            {
                if (i == 10) w.Apply(new PossessedMoveCommand(1, 0.5f, false));
                if (i == 80) w.Apply(new PossessedMoveCommand(0, 0, false));
                if (i == 90) w.Apply(new PossessedInteractCommand(Id(w, 1), "talk"));
                if (i == 200) w.Apply(new ReleasePossessionCommand());
                w.Step();
            }
            return w.StateHash();
        }
        Assert.Equal(Run(), Run());
    }
}
