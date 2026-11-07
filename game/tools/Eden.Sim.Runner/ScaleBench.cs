using System.Diagnostics;
using Eden.Sim;

/// <summary>
/// M8 scale pass measurements on real worlds. A world is fast-forwarded once and cached as a save file, so later
/// runs (save/load timing, per-tick cost) start in about a second. <c>--set</c> overrides apply when growing.
/// </summary>
static class ScaleBench
{
    /// <summary>Loads PATH, or grows seed/years and writes PATH; then times save and load (best of 3) and lists section sizes.</summary>
    public static int SaveLoad(string path, int seed, int years)
    {
        var world = LoadOrGrow(path, seed, years);
        ulong hash = world.StateHash();
        Console.WriteLine($"world: seed {world.Config.Seed}, year {world.Clock.Year}, population {world.Population}, hash {hash:X16}");
        double bestSave = double.MaxValue, bestLoad = double.MaxValue;
        byte[] bytes = [];
        for (int i = 0; i < 3; i++)
        {
            var ms = new MemoryStream();
            var sw = Stopwatch.StartNew();
            SaveWriter.Write(world, ms);
            sw.Stop();
            bestSave = Math.Min(bestSave, sw.Elapsed.TotalSeconds);
            bytes = ms.ToArray();
        }
        ulong loadedHash = 0;
        for (int i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            var w2 = SaveReader.Load(new MemoryStream(bytes));
            sw.Stop();
            bestLoad = Math.Min(bestLoad, sw.Elapsed.TotalSeconds);
            loadedHash = w2.StateHash();
        }
        // File round trip too (what a player waits for), cold-ish: one pass each.
        string probe = path + ".resave";
        var fsw = Stopwatch.StartNew();
        using (var f = File.Create(probe)) SaveWriter.Write(world, f);
        double fileSave = fsw.Elapsed.TotalSeconds;
        fsw.Restart();
        using (var f = File.OpenRead(probe)) SaveReader.Load(f);
        double fileLoad = fsw.Elapsed.TotalSeconds;
        // In place, as LoadCommand does it: validated in a scratch world, then read into the live one.
        fsw.Restart();
        using (var f = File.OpenRead(probe)) SaveReader.ReadInto(world, f);
        double inPlace = fsw.Elapsed.TotalSeconds;
        File.Delete(probe);
        Console.WriteLine($"save {bestSave:F3} s, load {bestLoad:F3} s (in memory, best of 3); file save {fileSave:F3} s, file load {fileLoad:F3} s, in-place load {inPlace:F3} s");
        Console.WriteLine($"size {bytes.Length / 1048576.0:F2} MB ({bytes.Length} bytes); loaded hash {(loadedHash == hash ? "equal" : "DIFFERS " + loadedHash.ToString("X16"))}");
        foreach (var (tag, version, len) in Sections(bytes))
            Console.WriteLine($"  {tag} v{version} {len,10} bytes");
        return loadedHash == hash ? 0 : 1;
    }

    /// <summary>Per-tick cost on a saved world: 50 warm-up ticks, then N ticks timed one by one; allocations on this thread.</summary>
    public static int World(string path, int ticks)
    {
        World world;
        using (var f = File.OpenRead(path)) world = SaveReader.Load(f);
        for (int i = 0; i < 50; i++) world.Step();
        using var types = Environment.GetEnvironmentVariable("EDEN_ALLOC_TYPES") == "1" ? new AllocTypes() : null;
        var times = new double[ticks];
        var allocs = new long[ticks];
        var sw = new Stopwatch();
        for (int i = 0; i < ticks; i++)
        {
            long a = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            world.Step();
            sw.Stop();
            allocs[i] = GC.GetAllocatedBytesForCurrentThread() - a;
            times[i] = sw.Elapsed.TotalMilliseconds;
        }
        int tpd = world.Config.TicksPerDay;
        var sorted = (double[])times.Clone();
        Array.Sort(sorted);
        double sum = 0, quietSum = 0, dailySum = 0;
        long allocSum = 0, quietAlloc = 0;
        int quiet = 0, daily = 0, zeroAlloc = 0;
        for (int i = 0; i < ticks; i++)
        {
            sum += times[i];
            allocSum += allocs[i];
            long t = world.Tick - ticks + i + 1;
            if (t % tpd == 0) { daily++; dailySum += times[i]; continue; }
            quiet++;
            quietSum += times[i];
            quietAlloc += allocs[i];
            if (allocs[i] == 0) zeroAlloc++;
        }
        Console.WriteLine($"{world.Population} NPCs, year {world.Clock.Year}, {ticks} ticks: mean {sum / ticks:F3} ms/tick, median {sorted[ticks / 2]:F3}, p99 {sorted[(int)(ticks * 0.99)]:F3}, max {sorted[^1]:F3}");
        Console.WriteLine($"  non-daily ticks: {quietSum / Math.Max(1, quiet):F3} ms and {quietAlloc / Math.Max(1, quiet)} bytes each, {zeroAlloc}/{quiet} allocated nothing");
        Console.WriteLine($"  daily ticks ({daily}): {dailySum / Math.Max(1, daily):F1} ms each; all ticks {allocSum / ticks} bytes/tick on average");
        types?.Print();
        return 0;
    }

    private static World LoadOrGrow(string path, int seed, int years)
    {
        if (File.Exists(path))
        {
            using var f = File.OpenRead(path);
            return SaveReader.Load(f);
        }
        var sw = Stopwatch.StartNew();
        var w = new World(RunnerConfig.For(seed));
        for (int y = 1; y <= years; y++)
        {
            for (int t = 0; t < w.Config.TicksPerYear; t++) w.Step();
            if (y % 10 == 0) Console.Error.WriteLine($"  year {y}: population {w.Population}, {sw.Elapsed.TotalSeconds:F0} s");
        }
        Console.WriteLine($"grew seed {seed} to year {years} in {sw.Elapsed.TotalSeconds:F1} s");
        using (var f = File.Create(path)) SaveWriter.Write(w, f);
        return w;
    }

    private static List<(string tag, short version, int len)> Sections(byte[] bytes)
    {
        // Format version 2 framing: tag, version (int16), flags (byte), stored length (int32), [raw length (int32)].
        int headerLen = BitConverter.ToInt32(bytes, 4);
        int p = 8 + headerLen;
        var list = new List<(string, short, int)>();
        while (p + 11 <= bytes.Length)
        {
            string tag = System.Text.Encoding.ASCII.GetString(bytes, p, 4);
            short v = BitConverter.ToInt16(bytes, p + 4);
            byte flags = bytes[p + 6];
            int len = BitConverter.ToInt32(bytes, p + 7);
            int extra = (flags & 1) != 0 ? 4 : 0;
            list.Add((flags != 0 ? tag + "*" : tag, v, len));
            p += 11 + extra + len;
        }
        return list;
    }
}

/// <summary>Sampled allocation types (GCAllocationTick, ~100 KB per sample) for <c>EDEN_ALLOC_TYPES=1</c>.</summary>
sealed class AllocTypes : System.Diagnostics.Tracing.EventListener
{
    private readonly Dictionary<string, int> _counts = new();

    protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
            EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Verbose, (System.Diagnostics.Tracing.EventKeywords)0x1);
    }

    protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs e)
    {
        if (e.EventName is null || !e.EventName.StartsWith("GCAllocationTick") || e.Payload is null) return;
        int i = e.PayloadNames!.IndexOf("TypeName");
        string name = i >= 0 ? e.Payload[i]?.ToString() ?? "?" : "?";
        lock (_counts) _counts[name] = _counts.GetValueOrDefault(name) + 1;
    }

    public void Print()
    {
        lock (_counts)
            foreach (var (k, v) in _counts.OrderByDescending(p => p.Value).Take(25)) Console.WriteLine($"  {v,6}  {k}");
    }
}
