namespace Eden.Game.Tests;

using Eden.Game.Dev;
using Eden.Sim.Contracts;
using GdUnit4;
using static GdUnit4.Assertions;

/// <summary>C1: the client's <see cref="ChronicleLog"/> reads <see cref="IWorldSnapshot.ChronicleSince"/>: no burst loss, history on load.</summary>
[TestSuite]
public class ChronicleTests
{
    private static Eden.Sim.World Fresh(int seed) => new(new Eden.Sim.SimConfig { Seed = seed, StartingNpcs = 2 });

    [TestCase]
    public void Hundred_entries_published_between_two_reads_are_all_kept()
    {
        var w = Fresh(3);
        var log = new ChronicleLog();
        log.Ingest(w.Snapshot());
        int before = log.Count;

        // Several snapshots are published between two frames; the frame only reads the last one.
        for (int i = 0; i < 100; i++)
        {
            w.Step();
            w.Chronicle(w.Tick, "birth", $"child {i} was born", 3, Array.Empty<int>());
            if (i % 10 == 9) _ = w.Snapshot();
        }
        w.Step();
        log.Ingest(w.Snapshot());

        AssertThat(log.Count).IsGreaterEqual(before + 100); // plus whatever the two people did meanwhile
        var texts = new HashSet<string>();
        for (int i = 0; i < log.Count; i++) AssertThat(texts.Add(log[i].Tick + ":" + log[i].Text)).IsTrue(); // no duplicates
        texts = new HashSet<string>(texts.Select(t => t[(t.IndexOf(':') + 1)..]));
        for (int i = 0; i < 100; i++) AssertThat(texts.Contains($"child {i} was born")).IsTrue();
        for (int i = 1; i < log.Count; i++) AssertThat(log[i].Tick).IsGreaterEqual(log[i - 1].Tick);
    }

    [TestCase]
    public void Entries_added_at_the_tick_of_a_read_snapshot_are_not_lost_or_doubled()
    {
        var w = Fresh(3);
        var log = new ChronicleLog();
        w.Step();
        w.Chronicle(w.Tick, "god", "first", 8, Array.Empty<int>());
        log.Ingest(w.Snapshot());
        w.Chronicle(w.Tick, "god", "second, same tick (a god command between ticks)", 8, Array.Empty<int>());
        w.Step();
        log.Ingest(w.Snapshot());
        log.Ingest(w.Snapshot());
        int first = 0, second = 0;
        for (int i = 0; i < log.Count; i++)
        {
            if (log[i].Text == "first") first++;
            if (log[i].Text.StartsWith("second")) second++;
        }
        AssertThat(first).IsEqual(1);
        AssertThat(second).IsEqual(1);
    }

    [TestCase]
    public void A_load_brings_the_saved_history_and_drops_the_replaced_world()
    {
        string path = Path.Combine(Path.GetTempPath(), $"eden-client-chronicle-{System.Environment.ProcessId}.eden");
        try
        {
            var saved = Fresh(4);
            for (int i = 0; i < 40; i++) saved.Chronicle(i, "trade", $"old trade {i}", 3, Array.Empty<int>());
            for (int t = 0; t < 50; t++) saved.Step();
            saved.Apply(new SaveCommand(path));

            // A session on another world, then the player loads the save.
            var w = Fresh(9);
            var log = new ChronicleLog();
            for (int t = 0; t < 120; t++) w.Step();
            w.Chronicle(w.Tick, "raid", "a raid in the replaced world", 7, Array.Empty<int>());
            log.Ingest(w.Snapshot());
            w.Apply(new LoadCommand(path));
            log.Ingest(w.Snapshot());

            var texts = new List<string>();
            for (int i = 0; i < log.Count; i++) texts.Add(log[i].Text);
            for (int i = 0; i < 40; i++) AssertThat(texts.Contains($"old trade {i}")).IsTrue();
            AssertThat(texts.Contains("a raid in the replaced world")).IsFalse();
            AssertThat(log[log.Count - 1].Kind).IsEqual("load");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase]
    public void Fake_host_keeps_a_history_for_late_readers()
    {
        using var host = new FakeSimHost(10);
        for (int d = 0; d < 3; d++) host.StepSync(240); // three fake days, three published batches of three entries
        var log = new ChronicleLog();
        log.Ingest(host.Latest);
        AssertThat(log.Count).IsGreaterEqual(10); // genesis + 9, though NewChronicle held only the last batch
        AssertThat(host.Latest.ChronicleSince(host.Latest.Tick).Count).IsEqual(0);
    }
}
