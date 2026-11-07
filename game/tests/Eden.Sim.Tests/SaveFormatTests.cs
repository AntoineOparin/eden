using System.Buffers.Binary;
using Eden.Sim.Contracts;

namespace Eden.Sim.Tests;

/// <summary>M8 save pass: format version 2, version-1 compatibility, all-or-nothing loads, shared memories.</summary>
public class SaveFormatTests
{
    private static readonly Lazy<(World world, byte[] bytes)> Real = new(() =>
    {
        var w = new World(new SimConfig { Seed = 6 });
        for (int t = 0; t < 12_000; t++) w.Step();
        using var ms = new MemoryStream();
        SaveWriter.Write(w, ms);
        return (w, ms.ToArray());
    });

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void A_version_1_save_still_loads_and_round_trips_through_version_2()
    {
        // 64×64 world, seed 1, one year, written by the M7-era SaveWriter (format version 1, MEMS section version 1).
        byte[] v1 = File.ReadAllBytes(FixturePath("save-v1-seed1-year1.eden"));
        Assert.Equal(1, SaveReader.ReadHeader(new MemoryStream(v1)).Version);
        var a = SaveReader.Load(new MemoryStream(v1));
        Assert.Equal(3, a.Population);
        Assert.Equal(0xC8697D8746218892UL, a.StateHash());

        using var ms = new MemoryStream();
        SaveWriter.Write(a, ms);
        ms.Position = 0;
        Assert.Equal(SaveWriter.FormatVersion, SaveReader.ReadHeader(ms).Version);
        ms.Position = 0;
        var b = SaveReader.Load(ms);
        Assert.Equal(a.StateHash(), b.StateHash());
        for (int t = 0; t < 2000; t++) { a.Step(); b.Step(); }
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void A_truncated_save_is_rejected_and_the_world_is_left_untouched()
    {
        byte[] full = Real.Value.bytes;
        var target = new World(new SimConfig { Seed = 3 });
        for (int t = 0; t < 500; t++) target.Step();
        ulong before = target.StateHash();
        int headerEnd = 8 + BinaryPrimitives.ReadInt32LittleEndian(full.AsSpan(4));
        int[] cuts = [3, 6, headerEnd - 1, headerEnd + 2, headerEnd + 20, full.Length / 4, full.Length / 2, full.Length * 3 / 4, full.Length - 1];
        foreach (int cut in cuts)
        {
            var ex = Assert.Throws<InvalidDataException>(() => SaveReader.ReadInto(target, new MemoryStream(full, 0, cut)));
            Assert.False(string.IsNullOrWhiteSpace(ex.Message));
            Assert.Equal(before, target.StateHash());
            Assert.Equal(500, target.Tick);
        }
        // The untouched world still runs, and the intact save still loads into it.
        target.Step();
        SaveReader.ReadInto(target, new MemoryStream(full));
        Assert.Equal(Real.Value.world.StateHash(), target.StateHash());
    }

    [Fact]
    public void A_damaged_compressed_section_is_rejected_cleanly()
    {
        byte[] bytes = (byte[])Real.Value.bytes.Clone();
        for (int i = bytes.Length / 2; i < bytes.Length / 2 + 64; i++) bytes[i] ^= 0x5A;
        var target = new World(new SimConfig { Seed = 3 });
        ulong before = target.StateHash();
        Assert.Throws<InvalidDataException>(() => SaveReader.ReadInto(target, new MemoryStream(bytes)));
        Assert.Equal(before, target.StateHash());
    }

    [Fact]
    public void Unknown_sections_are_skipped_and_newer_section_versions_are_refused()
    {
        byte[] full = Real.Value.bytes;
        // Appended unknown section (format 2 framing): tag, version, flags, length, body.
        using var extra = new MemoryStream();
        extra.Write(full);
        using (var w = new BinaryWriter(extra, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("ZZZZ"u8.ToArray()); w.Write((short)1); w.Write((byte)0); w.Write(3); w.Write([1, 2, 3]);
        }
        var loaded = SaveReader.Load(new MemoryStream(extra.ToArray()));
        Assert.Equal(Real.Value.world.StateHash(), loaded.StateHash());

        // The first section (CONF) claims a version from the future.
        byte[] newer = (byte[])full.Clone();
        int headerEnd = 8 + BinaryPrimitives.ReadInt32LittleEndian(newer.AsSpan(4));
        Assert.Equal("CONF"u8.ToArray(), newer.AsSpan(headerEnd, 4).ToArray());
        BinaryPrimitives.WriteInt16LittleEndian(newer.AsSpan(headerEnd + 4), 99);
        var ex = Assert.Throws<InvalidDataException>(() => SaveReader.Load(new MemoryStream(newer)));
        Assert.Contains("newer", ex.Message);
    }

    [Fact]
    public void A_save_from_a_newer_format_is_refused()
    {
        var header = System.Text.Encoding.UTF8.GetBytes("{\"version\":99,\"seed\":1,\"tick\":0,\"population\":0}");
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write("EDEN"u8.ToArray()); w.Write(header.Length); w.Write(header);
        }
        ms.Position = 0;
        var ex = Assert.Throws<InvalidDataException>(() => SaveReader.Load(ms));
        Assert.Contains("newer", ex.Message);
    }

    [Fact]
    public void Shared_memories_are_written_once_and_shared_again_after_a_load()
    {
        var (world, bytes) = Real.Value;
        var loaded = SaveReader.Load(new MemoryStream(bytes));
        Assert.Equal(world.StateHash(), loaded.StateHash());

        // Count memory objects referenced from two or more streams, before and after.
        static (int refs, int distinct) Census(World w)
        {
            var seen = new HashSet<Memory>(ReferenceEqualityComparer.Instance);
            int refs = 0;
            for (int s = 0; s < w.Npcs.HighWater; s++)
            {
                if (!w.Npcs.Alive[s]) continue;
                foreach (var m in w.Memories.Of(s)) { refs++; seen.Add(m); }
            }
            return (refs, seen.Count);
        }
        var (refsA, distinctA) = Census(world);
        var (refsB, distinctB) = Census(loaded);
        Assert.Equal(refsA, refsB);
        Assert.True(distinctA < refsA, "witnesses share event memories in a running world");
        Assert.True(distinctB <= distinctA, $"a load keeps the sharing ({distinctB} distinct vs {distinctA})");
    }

    [Fact]
    public void Saves_record_the_concept_set()
    {
        var w = new World(new SimConfig { Seed = 2 });
        using var ms = new MemoryStream();
        SaveWriter.Write(w, ms);
        ms.Position = 0;
        var header = SaveReader.ReadHeader(ms);
        Assert.Equal(KnowledgeData.Default.SetHash, header.ConceptSet);
        Assert.Matches("^[0-9A-F]{16}$", header.ConceptSet!);
    }

    [Fact]
    public void Commands_report_a_damaged_save_in_the_log_and_keep_the_world()
    {
        string path = Path.Combine(Path.GetTempPath(), $"eden-damaged-{Environment.ProcessId}.eden");
        try
        {
            byte[] full = Real.Value.bytes;
            File.WriteAllBytes(path, full.AsSpan(0, full.Length / 2).ToArray());
            var w = new World(new SimConfig { Seed = 5 });
            for (int t = 0; t < 200; t++) w.Step();
            ulong before = w.StateHash();
            w.Apply(new LoadCommand(path));
            Assert.Equal(before, w.StateHash());
            bool logged = false;
            while (w.TryDequeueLog(out var line)) logged |= line.StartsWith("load failed", StringComparison.Ordinal);
            Assert.True(logged);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
