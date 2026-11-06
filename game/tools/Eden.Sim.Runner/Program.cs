using System.Diagnostics;
using Eden.Sim;
using Eden.Sim.Contracts;

// Usage:
//   dotnet run --project game/tools/Eden.Sim.Runner -- --seed 7 --years 60
//   dotnet run --project game/tools/Eden.Sim.Runner -- --seeds 1..20 --years 60     (one summary line per seed)
//   dotnet run --project game/tools/Eden.Sim.Runner -- --bench 5000                 (ms per tick at N NPCs)
//   dotnet run --project game/tools/Eden.Sim.Runner -- --seed 7 --years 200 --knowledge (concepts known anywhere per decade)

int seed = 7, years = 60, tail = 15, bench = 0, traceDays = 0;
bool knowledge = false;
(int from, int to)? sweep = null;
for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
    switch (args[i])
    {
        case "--seed": seed = int.Parse(Next()); break;
        case "--years": years = int.Parse(Next()); break;
        case "--tail": tail = int.Parse(Next()); break;
        case "--bench": bench = int.Parse(Next()); break;
        case "--trace": traceDays = int.Parse(Next()); break;
        case "--knowledge": knowledge = true; break;
        case "--seeds":
        {
            var parts = Next().Split("..");
            sweep = (int.Parse(parts[0]), int.Parse(parts[^1]));
            break;
        }
        default:
            Console.Error.WriteLine($"unknown argument {args[i]}");
            return 2;
    }
}

if (bench > 0) return Bench(bench);
if (traceDays > 0) return Trace(seed, traceDays);
if (sweep is { } range) return Sweep(range.from, range.to, years);
if (knowledge) return Knowledge(seed, years);

var world = new World(new SimConfig { Seed = seed });
Console.WriteLine($"Eden seed {seed}, {years} years ({world.Config.TicksPerYear} ticks/year)");
Console.WriteLine(" year  pop  births deaths  gen");
var sw = Stopwatch.StartNew();
for (int y = 1; y <= years; y++)
{
    for (int t = 0; t < world.Config.TicksPerYear; t++) world.Step();
    Console.WriteLine($" {y,4} {world.Population,4} {world.TotalBirths,7} {world.TotalDeaths,6} {world.MaxLineage,4}");
    if (world.Population == 0) { Console.WriteLine($" extinct in year {y}"); break; }
}
Console.WriteLine($"\nsimulated in {sw.Elapsed.TotalSeconds:F1} s, state hash {world.StateHash():X16}");
Console.WriteLine($"\nChronicle (last {tail}):");
foreach (var e in world.ChronicleTail(tail))
    Console.WriteLine($"  y{e.Tick / world.Config.TicksPerYear,-3} [{e.Kind}] {e.Text}");
return 0;

static int Sweep(int from, int to, int years)
{
    Console.WriteLine("seed  final  peak  reached50  gen@50  maxGen  outcome");
    var results = new string[to - from + 1];
    Parallel.For(from, to + 1, s =>
    {
        var w = new World(new SimConfig { Seed = s });
        int peak = 0, reachedYear = -1, genAt50 = -1, extinctYear = -1;
        for (int y = 1; y <= years; y++)
        {
            for (int t = 0; t < w.Config.TicksPerYear; t++) w.Step();
            peak = Math.Max(peak, w.Population);
            if (reachedYear < 0 && w.Population >= 50) { reachedYear = y; genAt50 = w.MaxLineage; }
            if (w.Population == 0) { extinctYear = y; break; }
        }
        string outcome = extinctYear > 0 ? $"died out in year {extinctYear}" : reachedYear > 0 ? "grew" : "alive, under 50";
        results[s - from] = $"{s,4}  {w.Population,5}  {peak,4}  {(reachedYear > 0 ? "year " + reachedYear : "-"),9}  {(genAt50 >= 0 ? genAt50.ToString() : "-"),6}  {w.MaxLineage,6}  {outcome}";
    });
    foreach (var line in results) Console.WriteLine(line);
    return 0;
}

static int Bench(int n)
{
    var world = new World(new SimConfig { Seed = 3 });
    int perSpot = Math.Max(1, n / 50);
    var rng = new Rng(99);
    while (world.Population < n)
    {
        float x = rng.NextFloat() * world.Terrain.MaxX, z = rng.NextFloat() * world.Terrain.MaxZ;
        world.Apply(new SpawnCommand("human", x, z, Math.Min(perSpot, n - world.Population)));
    }
    for (int i = 0; i < 200; i++) world.Step();
    long before = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    const int ticks = 500;
    for (int i = 0; i < ticks; i++) world.Step();
    sw.Stop();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Console.WriteLine($"{world.Population} NPCs: {sw.Elapsed.TotalMilliseconds / ticks:F3} ms/tick, {allocated / ticks} bytes allocated/tick (events included)");
    return 0;
}

// Every two in-game hours: each NPC's action, needs and carried food. For balancing.
static int Trace(int seed, int days)
{
    var w = new World(new SimConfig { Seed = seed });
    for (int t = 0; t < days * w.Config.TicksPerDay; t++)
    {
        w.Step();
        if (w.Tick % (w.Config.TicksPerHour * 2) != 0) continue;
        var n = w.Npcs;
        for (int s = 0; s < n.HighWater; s++)
        {
            if (!n.Alive[s]) continue;
            Console.Write($"d{w.Tick / w.Config.TicksPerDay} h{w.Clock.HourOfDay,4:F1} {n.Name[s],-10} {n.Action[s],-7} {n.Phase[s],-6} pos({n.X[s],7:F2},{n.Z[s],7:F2}) tgt({n.TargetX[s],5:F0},{n.TargetZ[s],5:F0}) node {n.TargetNode[s]} av {n.AvoidNode[s]} t{n.ActTicks[s]} carry {n.Food[s],3:F0} ");
            for (int k = 0; k < Needs.Count; k++) Console.Write($"{Needs.Names[k][..2]}={n.Needs[s * Needs.Count + k]:F2} ");
            Console.WriteLine($"hp={n.Health[s]:F2}");
        }
    }
    return 0;
}

// Per decade: population, concepts known anywhere (living knower or written record), records, and what changed.
static int Knowledge(int seed, int years)
{
    var w = new World(new SimConfig { Seed = seed });
    var g = w.Graph;
    Console.WriteLine($"Eden seed {seed}, {years} years, {g.Count} concepts; first NPCs know {string.Join(", ", ConceptGraph.StarterSet)}");
    Console.WriteLine(" year  pop  known  records  gained / lost this decade");
    var before = new bool[g.Count];
    for (int i = 0; i < g.Count; i++) before[i] = g.KnownAnywhere(i);
    Console.WriteLine($" {0,4} {w.Population,4} {g.KnownAnywhereCount(),6} {w.Records.Count,8}");
    for (int y = 1; y <= years; y++)
    {
        for (int t = 0; t < w.Config.TicksPerYear; t++) w.Step();
        if (y % 10 != 0 && y != years && w.Population > 0) continue;
        var gained = new List<string>();
        var lost = new List<string>();
        for (int i = 0; i < g.Count; i++)
        {
            bool now = g.KnownAnywhere(i);
            if (now && !before[i]) gained.Add(g[i].Id);
            if (!now && before[i]) lost.Add("-" + g[i].Id);
            before[i] = now;
        }
        gained.AddRange(lost);
        Console.WriteLine($" {y,4} {w.Population,4} {g.KnownAnywhereCount(),6} {w.Records.Count,8}  {string.Join(" ", gained)}");
        if (w.Population == 0) { Console.WriteLine($" extinct in year {y}"); break; }
    }
    // Frontier: unknown concepts and the best any living NPC does on their weakest prerequisite.
    var frontier = new List<string>();
    for (int i = 0; i < g.Count; i++)
    {
        if (g.KnownAnywhere(i)) continue;
        float best = 0;
        string weakest = "";
        for (int s = 0; s < w.Npcs.HighWater; s++)
        {
            if (!w.Npcs.Alive[s]) continue;
            float min = 1;
            string weak = "";
            foreach (int p in g[i].Prereqs)
            {
                float v = w.Proficiency(s, g[p].Id);
                if (v < min) { min = v; weak = g[p].Id; }
            }
            if (min > best) { best = min; weakest = weak; }
        }
        frontier.Add($"{g[i].Id}({best:F2}{(weakest.Length > 0 ? " " + weakest : "")})");
    }
    if (frontier.Count > 0 && w.Population > 0) Console.WriteLine($"\nnot known (best weakest prerequisite): {string.Join(", ", frontier)}");
    Console.WriteLine($"\nstate hash {w.StateHash():X16}");
    return 0;
}
