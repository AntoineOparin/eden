using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

/// <summary>C1: <see cref="IWorldSnapshot.ChronicleSince"/>, the 2,048-entry history and its CHRN save section.</summary>
public class ChronicleHistoryTests
{
    private static World Fresh(int seed = 3) => new(new SimConfig { Seed = seed, StartingNpcs = 2 });

    [Fact]
    public void Since_returns_every_newer_entry_oldest_first_across_snapshots()
    {
        var w = Fresh();
        var a = w.Snapshot();
        long seen = a.Tick;
        for (int i = 0; i < 100; i++) w.Chronicle(w.Tick, "test", $"burst {i}", 3, Array.Empty<int>());
        _ = w.Snapshot(); // published but never read by the client
        w.Step();
        w.Chronicle(w.Tick, "test", "after", 3, Array.Empty<int>());
        var c = w.Snapshot();

        Assert.DoesNotContain(c.NewChronicle, e => e.Text.StartsWith("burst")); // the compatibility list lost them
        var since = c.ChronicleSince(seen - 1);
        var texts = since.Select(e => e.Text).ToList();
        for (int i = 0; i < 100; i++) Assert.Contains($"burst {i}", texts);
        Assert.Equal("after", texts[^1]);
        for (int i = 1; i < since.Count; i++) Assert.True(since[i - 1].Tick <= since[i].Tick);
        Assert.Empty(c.ChronicleSince(c.Tick));
        Assert.Contains(c.ChronicleSince(-1), e => e.Kind == "genesis");
    }

    [Fact]
    public void History_is_bounded_and_snapshots_keep_their_own_copy()
    {
        var w = Fresh();
        for (int i = 0; i < World.ChronicleHistorySize + 50; i++) w.Chronicle(i, "test", $"e{i}", 1, Array.Empty<int>());
        var snap = w.Snapshot();
        var all = snap.ChronicleSince(long.MinValue);
        Assert.Equal(World.ChronicleHistorySize, all.Count);
        Assert.Equal($"e{World.ChronicleHistorySize + 49}", all[^1].Text);

        w.Chronicle(1_000_000, "test", "later", 1, Array.Empty<int>());
        Assert.DoesNotContain(snap.ChronicleSince(long.MinValue), e => e.Text == "later"); // immutable snapshot
        Assert.Contains(w.Snapshot().ChronicleSince(long.MinValue), e => e.Text == "later");
    }

    [Fact]
    public void Unchanged_history_is_shared_between_snapshots()
    {
        var w = Fresh();
        var a = (WorldSnapshot)w.Snapshot();
        var b = (WorldSnapshot)w.Snapshot();
        Assert.Same(a.History, b.History);
        w.Chronicle(w.Tick, "test", "x", 1, Array.Empty<int>());
        Assert.NotSame(b.History, ((WorldSnapshot)w.Snapshot()).History);
    }

    [Fact]
    public void Save_keeps_the_history_and_a_load_shows_it_with_the_load_notice()
    {
        var w = Fresh(4);
        for (int t = 0; t < 600; t++) w.Step();
        w.Chronicle(w.Tick, "test", "Bo wrote the first word.", 6, [1, 2]);
        var before = w.ChronicleTail(World.ChronicleHistorySize).ToList();
        ulong hash = w.StateHash();

        using var ms = new MemoryStream();
        SaveWriter.Write(w, ms);
        ms.Position = 0;
        var loaded = SaveReader.Load(ms);
        Assert.Equal(hash, loaded.StateHash());

        var after = loaded.Snapshot().ChronicleSince(long.MinValue);
        Assert.Equal(before.Count + 1, after.Count); // + "The world was restored…"
        for (int i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].Tick, after[i].Tick);
            Assert.Equal(before[i].Kind, after[i].Kind);
            Assert.Equal(before[i].Text, after[i].Text);
            Assert.Equal(before[i].Importance, after[i].Importance);
            Assert.Equal(before[i].Subjects, after[i].Subjects);
        }
        Assert.Equal("load", after[^1].Kind);
        Assert.DoesNotContain(after, e => e.Text.Contains("No one awoke")); // the throwaway world's genesis is gone
    }

    [Fact]
    public void Loading_in_place_replaces_the_history()
    {
        string path = Path.Combine(Path.GetTempPath(), $"eden-chronicle-test-{Environment.ProcessId}.eden");
        try
        {
            var w = Fresh(5);
            w.Chronicle(w.Tick, "test", "saved entry", 4, Array.Empty<int>());
            w.Apply(new SaveCommand(path));
            for (int t = 0; t < 100; t++) w.Step();
            w.Chronicle(w.Tick, "test", "lost entry", 4, Array.Empty<int>());
            w.Apply(new LoadCommand(path));
            var texts = w.ChronicleTail(World.ChronicleHistorySize).Select(e => e.Text).ToList();
            Assert.Contains("saved entry", texts);
            Assert.DoesNotContain("lost entry", texts);
            Assert.Equal("load", w.ChronicleTail(1)[0].Kind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_save_without_the_section_still_loads()
    {
        var w = Fresh(6);
        using var full = new MemoryStream();
        SaveWriter.Write(w, full);
        // Strip the trailing CHRN section (it is written last) to mimic a save from before C1.
        byte[] bytes = full.ToArray();
        int at = IndexOf(bytes, "CHRN"u8.ToArray());
        Assert.True(at > 0);
        var old = new MemoryStream(bytes[..at]);
        var loaded = SaveReader.Load(old);
        Assert.Equal(w.StateHash(), loaded.StateHash());
        Assert.Equal("load", loaded.ChronicleTail(1)[0].Kind);
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        for (int i = hay.Length - needle.Length; i >= 0; i--)
            if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }
}
