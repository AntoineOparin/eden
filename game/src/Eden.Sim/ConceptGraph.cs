using System.Text.Json;
using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>A craftable tool from <c>game/data/tools.json</c>. Owning one multiplies the yield of <see cref="Resource"/>.</summary>
public sealed record ToolDef(string Id, string Name, IReadOnlyList<string> Materials, string? Resource, float Factor);

/// <summary>One concept from <c>concepts.json</c> (or an invention) with its tags, which carry the category and honing actions.</summary>
public sealed record ConceptDef(ConceptProposal Concept, IReadOnlyList<string> Tags);

/// <summary>
/// The static knowledge data shipped with the game: <c>game/data/{concepts,resources,buildings,tools}.json</c>,
/// embedded into Eden.Sim at build time. Parsed once per process with <see cref="JsonDocument"/> (no reflection).
/// </summary>
public sealed class KnowledgeData
{
    public IReadOnlyList<ConceptDef> Concepts { get; }
    public IReadOnlySet<string> Resources { get; }
    public IReadOnlySet<string> Buildings { get; }
    public IReadOnlyList<ToolDef> Tools { get; }

    /// <summary>
    /// M8: hash of the four JSON texts (16 hex digits, process-independent). Saves record it so a world can tell
    /// which concept set it was created with (<see cref="SaveHeader.ConceptSet"/>).
    /// </summary>
    public string SetHash { get; private init; } = "";

    /// <summary>The override folder this set was read from, or null for the shipped data.</summary>
    public string? Source { get; private init; }

    private KnowledgeData(IReadOnlyList<ConceptDef> concepts, IReadOnlySet<string> resources, IReadOnlySet<string> buildings, IReadOnlyList<ToolDef> tools)
    {
        Concepts = concepts;
        Resources = resources;
        Buildings = buildings;
        Tools = tools;
    }

    private static readonly string[] Files = ["concepts.json", "resources.json", "buildings.json", "tools.json"];

    private static readonly Lazy<KnowledgeData> _default = new(() => Parse(
        Embedded("concepts.json"), Embedded("resources.json"), Embedded("buildings.json"), Embedded("tools.json")));

    /// <summary>The shipped data. Thread-safe, parsed on first use.</summary>
    public static KnowledgeData Default => _default.Value;

    /// <summary>The data a world with this config uses: the override folder if one is set, else the shipped data.</summary>
    public static KnowledgeData For(SimConfig config)
        => config.DataOverrideDir is { Length: > 0 } dir ? FromDirectory(dir) : Default;

    /// <summary>
    /// M8 (knowledge roadmap item 5): read the data files from <paramref name="dir"/>, falling back to the shipped copy
    /// for any file it does not contain, and validate the result with the same parser and graph checks as the shipped
    /// data (ids, references, cycles, reachability). Any problem throws <see cref="InvalidDataException"/> whose message
    /// starts with "data override &lt;dir&gt;:".
    /// </summary>
    public static KnowledgeData FromDirectory(string dir)
    {
        if (!Directory.Exists(dir)) throw new InvalidDataException($"data override {dir}: folder not found");
        try
        {
            var texts = new string[Files.Length];
            for (int i = 0; i < Files.Length; i++)
            {
                string path = Path.Combine(dir, Files[i]);
                texts[i] = File.Exists(path) ? File.ReadAllText(path) : Embedded(Files[i]);
            }
            var parsed = Parse(texts[0], texts[1], texts[2], texts[3]);
            var data = new KnowledgeData(parsed.Concepts, parsed.Resources, parsed.Buildings, parsed.Tools) { SetHash = parsed.SetHash, Source = dir };
            ConceptGraph.Load(data); // throws InvalidDataException listing every graph problem
            return data;
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException
                                       or FormatException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"data override {dir}: {e.Message}", e);
        }
    }

    private static string Embedded(string name)
    {
        using var s = typeof(KnowledgeData).Assembly.GetManifestResourceStream("Eden.Sim.Data." + name)
            ?? throw new InvalidDataException($"game/data/{name} is not embedded in Eden.Sim");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static KnowledgeData Parse(string conceptsJson, string resourcesJson, string buildingsJson, string toolsJson)
    {
        var resources = Ids(resourcesJson, "resources.json");
        var buildings = Ids(buildingsJson, "buildings.json");

        var tools = new List<ToolDef>();
        using (var doc = JsonDocument.Parse(toolsJson))
            foreach (var t in doc.RootElement.EnumerateArray())
            {
                var mats = new List<string>();
                if (t.TryGetProperty("materials", out var m)) foreach (var x in m.EnumerateArray()) mats.Add(x.GetString()!);
                string? res = t.TryGetProperty("resource", out var r) ? r.GetString() : null;
                float factor = t.TryGetProperty("factor", out var f) ? f.GetSingle() : 1f;
                tools.Add(new ToolDef(Str(t, "id"), Str(t, "name"), mats, res, factor));
            }

        var concepts = new List<ConceptDef>();
        using (var doc = JsonDocument.Parse(conceptsJson))
            foreach (var c in doc.RootElement.EnumerateArray())
            {
                var prereqs = new List<string>();
                foreach (var p in c.GetProperty("prerequisites").EnumerateArray()) prereqs.Add(p.GetString()!);
                var effects = new List<ConceptEffect>();
                foreach (var e in c.GetProperty("effects").EnumerateArray()) effects.Add(ParseEffect(e));
                var tags = new List<string>();
                if (c.TryGetProperty("tags", out var tg)) foreach (var t in tg.EnumerateArray()) tags.Add(t.GetString()!);
                var proposal = new ConceptProposal(Str(c, "id"), Str(c, "name"), Str(c, "description"), prereqs,
                    c.GetProperty("difficulty").GetSingle(), effects);
                concepts.Add(new ConceptDef(proposal, tags));
            }
        ulong h = Hash.Offset;
        h = Hash.Mix(h, StableHash.Of(conceptsJson));
        h = Hash.Mix(h, StableHash.Of(resourcesJson));
        h = Hash.Mix(h, StableHash.Of(buildingsJson));
        h = Hash.Mix(h, StableHash.Of(toolsJson));
        return new KnowledgeData(concepts, resources, buildings, tools) { SetHash = h.ToString("X16") };
    }

    private static HashSet<string> Ids(string json, string file)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        foreach (var x in doc.RootElement.EnumerateArray())
            if (!set.Add(Str(x, "id"))) throw new InvalidDataException($"{file}: duplicate id {Str(x, "id")}");
        return set;
    }

    private static string Str(JsonElement e, string name)
        => e.GetProperty(name).GetString() ?? throw new InvalidDataException($"{name} is null");

    private static ConceptEffect ParseEffect(JsonElement e)
    {
        string type = Str(e, "type");
        return type switch
        {
            "yieldMod" => new ConceptEffect(ConceptEffectKind.YieldMod, Resource: Str(e, "resource"), Factor: e.GetProperty("factor").GetSingle()),
            "unlockBuilding" => new ConceptEffect(ConceptEffectKind.UnlockBuilding, Building: Str(e, "building")),
            "unlockTool" => new ConceptEffect(ConceptEffectKind.UnlockTool, Tool: Str(e, "tool")),
            "unlockAction" => new ConceptEffect(ConceptEffectKind.UnlockAction,
                Action: ConceptGraph.TryParseAction(Str(e, "action"), out var a) ? a : throw new InvalidDataException($"unknown action {Str(e, "action")}")),
            "belief" => new ConceptEffect(ConceptEffectKind.Belief, Text: Str(e, "text")),
            _ => throw new InvalidDataException($"unknown effect type {type}"),
        };
    }
}

/// <summary>One node of the live graph. Index is dense and stable for the lifetime of a world (saved in order).</summary>
public sealed class ConceptNode
{
    public required int Index { get; init; }
    public required ConceptProposal Concept { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public required int[] Prereqs { get; init; }
    public required bool IsInvention { get; init; }

    /// <summary>Bit per <see cref="ActionKind"/>: performing that action hones this concept.</summary>
    public uint HoneMask { get; init; }

    /// <summary>Bit per <see cref="ActionKind"/> this concept's unlockAction effects add.</summary>
    public uint ActionMask { get; init; }

    /// <summary>Bit per tool index (see <see cref="ConceptGraph.Tools"/>) this concept's unlockTool effects make craftable.</summary>
    public ulong ToolMask { get; init; }

    public string Id => Concept.Id;
    public string Name => Concept.Name;

    /// <summary>The name with a lower-case first letter, for memory and Chronicle text (cached: built once per node).</summary>
    public string LowerName => _lowerName ??= Name.Length == 0 ? Name : char.ToLowerInvariant(Name[0]) + Name[1..];
    private string? _lowerName;

    public float Difficulty => Concept.Difficulty;
    public string Category => Tags.Count > 0 ? Tags[0] : "misc";
}

/// <summary>
/// The live concept graph: the shipped concepts plus accepted inventions, with derived lookup tables that keep
/// the tick path allocation-free (concepts honed per action, yield modifiers per resource, tool bits). Built once
/// at world creation; inventions append to it. See <c>docs/specs/50-knowledge-data.md</c>.
/// </summary>
public sealed class ConceptGraph
{
    /// <summary>Concepts the first NPCs know; the only ones without prerequisites.</summary>
    public static readonly IReadOnlyList<string> StarterSet = ["foraging", "fire"];

    public const int MaxTools = 64;

    private static readonly Dictionary<string, ActionKind> ActionNames = BuildActionNames();

    private readonly List<ConceptNode> _nodes = new();
    private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _toolIndex = new(StringComparer.Ordinal);
    private int[][] _honedBy = [];
    private readonly Dictionary<string, (int concept, float factor)[]> _yields = new(StringComparer.Ordinal);
    private (int concept, float factor)[][] _nodeYields = [];
    private readonly ulong[] _nodeTools = new ulong[(int)ResourceKind.Game + 1];

    public KnowledgeData Data { get; }
    public IReadOnlyList<ToolDef> Tools => Data.Tools;
    public int Count => _nodes.Count;
    public ConceptNode this[int index] => _nodes[index];
    public IReadOnlyList<ConceptNode> Nodes => _nodes;

    private ConceptGraph(KnowledgeData data)
    {
        Data = data;
        if (data.Tools.Count > MaxTools) throw new InvalidDataException($"tools.json has {data.Tools.Count} tools, max {MaxTools}");
        for (int i = 0; i < data.Tools.Count; i++) _toolIndex[data.Tools[i].Id] = i;
        for (int k = 0; k <= (int)ResourceKind.Game; k++) _nodeTools[k] = ToolsBoosting(ResourceId((ResourceKind)k));
    }

    /// <summary>Build a graph without validating it (tests use this to check that bad data is caught).</summary>
    public static ConceptGraph Unchecked(KnowledgeData data)
    {
        var g = new ConceptGraph(data);
        foreach (var c in data.Concepts) g.Insert(c.Concept, c.Tags, invention: false);
        g.Rebuild();
        return g;
    }

    /// <summary>The shipped graph, validated: throws <see cref="InvalidDataException"/> on any problem.</summary>
    public static ConceptGraph Load(KnowledgeData? data = null)
    {
        data ??= KnowledgeData.Default;
        var g = new ConceptGraph(data);
        foreach (var c in data.Concepts) g.Insert(c.Concept, c.Tags, invention: false);
        g.Rebuild();
        var problems = g.Problems();
        if (problems.Count > 0) throw new InvalidDataException("concepts.json: " + string.Join("; ", problems));
        return g;
    }

    /// <summary>Rebuild a graph from saved concepts in saved order (the shipped tags are reattached by id).</summary>
    public static ConceptGraph FromSaved(KnowledgeData data, IReadOnlyList<(ConceptProposal concept, bool invention)> concepts)
    {
        var tags = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var c in data.Concepts) tags[c.Concept.Id] = c.Tags;
        var g = new ConceptGraph(data);
        foreach (var (c, inv) in concepts)
            g.Insert(c, tags.TryGetValue(c.Id, out var t) ? t : InventionTags(c), inv);
        g.Rebuild();
        return g;
    }

    // ---- world counters (maintained by World; derived state, rebuilt after a load) -----------

    private int[] _holders = [];
    private int[] _recorded = [];
    private bool[] _everKnown = [];

    /// <summary>
    /// True while a living NPC knows the concept or a written record holds it. Knowledge dies with its
    /// holders unless it was written down.
    /// </summary>
    public bool KnownAnywhere(string id) => TryIndex(id, out int i) && KnownAnywhere(i);

    public bool KnownAnywhere(int index) => _holders[index] > 0 || _recorded[index] > 0;

    /// <summary>Number of concepts currently known by someone or held in a record.</summary>
    public int KnownAnywhereCount()
    {
        int n = 0;
        for (int i = 0; i < _nodes.Count; i++) if (KnownAnywhere(i)) n++;
        return n;
    }

    /// <summary>Living NPCs who know concept <paramref name="index"/>.</summary>
    public int Holders(int index) => _holders[index];

    /// <summary>Records that hold concept <paramref name="index"/>.</summary>
    public int Recorded(int index) => _recorded[index];

    /// <summary>Known by anyone at any time since genesis (distinguishes a discovery from a rediscovery).</summary>
    public bool EverKnown(int index) => _everKnown[index];

    internal void AddHolder(int index, int delta)
    {
        _holders[index] = Math.Max(0, _holders[index] + delta);
        if (delta > 0) _everKnown[index] = true;
    }

    internal void SetRecorded(int index, int count) => _recorded[index] = count;

    internal void SetEverKnown(int index, bool value) => _everKnown[index] = value;

    internal void ClearCounters()
    {
        Array.Clear(_holders);
        Array.Clear(_recorded);
    }

    private void EnsureCounters()
    {
        if (_holders.Length >= _nodes.Count) return;
        int n = Math.Max(_nodes.Count, _holders.Length * 2);
        Array.Resize(ref _holders, n);
        Array.Resize(ref _recorded, n);
        Array.Resize(ref _everKnown, n);
    }

    public bool TryIndex(string id, out int index) => _index.TryGetValue(id, out index);

    public bool Contains(string id) => _index.ContainsKey(id);

    public int ToolIndex(string id) => _toolIndex.TryGetValue(id, out int i) ? i : -1;

    /// <summary>Bits of the tools whose yield bonus applies to <paramref name="resource"/>.</summary>
    public ulong ToolsBoosting(string resource)
    {
        ulong m = 0;
        for (int i = 0; i < Data.Tools.Count; i++) if (Data.Tools[i].Resource == resource) m |= 1UL << i;
        return m;
    }

    /// <summary>Bits of the tools whose yield bonus applies to an M1 node kind (precomputed).</summary>
    public ulong ToolsBoosting(ResourceKind kind) => _nodeTools[(int)kind];

    /// <summary>Concepts honed by performing <paramref name="action"/>.</summary>
    public int[] HonedBy(ActionKind action) => (int)action < _honedBy.Length ? _honedBy[(int)action] : [];

    /// <summary>yieldMod effects on <paramref name="resource"/>: (concept index, factor).</summary>
    public (int concept, float factor)[] YieldsFor(string resource) => _yields.TryGetValue(resource, out var y) ? y : [];

    /// <summary>yieldMod effects on an M1 resource-node kind, without a string lookup.</summary>
    public (int concept, float factor)[] YieldsFor(ResourceKind kind) => _nodeYields[(int)kind];

    /// <summary>Kebab-case resource id of an M1 node kind ("tree", "stone", "ore", "berry", "game").</summary>
    public static string ResourceId(ResourceKind kind) => kind switch
    {
        ResourceKind.Tree => "tree",
        ResourceKind.Stone => "stone",
        ResourceKind.Ore => "ore",
        ResourceKind.Berry => "berry",
        _ => "game",
    };

    public static bool TryParseResourceKind(string id, out ResourceKind kind)
    {
        for (int k = 0; k <= (int)ResourceKind.Game; k++)
            if (ResourceId((ResourceKind)k) == id) { kind = (ResourceKind)k; return true; }
        kind = default;
        return false;
    }

    public static bool TryParseAction(string camel, out ActionKind action) => ActionNames.TryGetValue(camel, out action);

    /// <summary>Append an accepted invention. Caller has validated it (<see cref="RejectInvention"/>).</summary>
    public int AddInvention(ConceptProposal concept)
    {
        int i = Insert(concept, InventionTags(concept), invention: true);
        Rebuild();
        return i;
    }

    /// <summary>Extra rules for LLM inventions beyond the schema (50-knowledge-data.md). Null when acceptable.</summary>
    public string? RejectInvention(ConceptProposal c, Func<string, float> proficiency, float masteredAt)
    {
        if (Contains(c.Id)) return "concept already exists";
        if (c.Effects.Count > 3) return "inventions may have at most 3 effects";
        foreach (var p in c.Prerequisites)
        {
            if (!Contains(p)) return $"unknown prerequisite {p}";
            if (proficiency(p) < masteredAt) return $"prerequisite {p} not mastered";
        }
        foreach (var e in c.Effects)
        {
            switch (e.Kind)
            {
                case ConceptEffectKind.YieldMod when e.Factor > 1.5f:
                    return "invention yield factor above 1.5";
                case ConceptEffectKind.YieldMod when !Data.Resources.Contains(e.Resource!):
                    return $"unknown resource {e.Resource}";
                case ConceptEffectKind.UnlockBuilding when !Data.Buildings.Contains(e.Building!):
                    return $"unknown building {e.Building}";
                case ConceptEffectKind.UnlockTool when ToolIndex(e.Tool!) < 0:
                    return $"unknown tool {e.Tool}";
                case ConceptEffectKind.Belief when e.Text!.Length > 140:
                    return "invention belief longer than 140 characters";
            }
        }
        return null;
    }

    // ---- validation (loader and ConceptGraphTests) ------------------------------------------

    /// <summary>Every problem with the graph: unknown ids, cycles, unreachable concepts, extra roots.</summary>
    public List<string> Problems()
    {
        var problems = new List<string>();
        foreach (var n in _nodes)
        {
            foreach (var p in n.Concept.Prerequisites)
                if (!Contains(p)) problems.Add($"{n.Id}: unknown prerequisite {p}");
            foreach (var e in n.Concept.Effects)
            {
                if (e.Kind == ConceptEffectKind.YieldMod && !Data.Resources.Contains(e.Resource ?? "")) problems.Add($"{n.Id}: unknown resource {e.Resource}");
                if (e.Kind == ConceptEffectKind.UnlockBuilding && !Data.Buildings.Contains(e.Building ?? "")) problems.Add($"{n.Id}: unknown building {e.Building}");
                if (e.Kind == ConceptEffectKind.UnlockTool && ToolIndex(e.Tool ?? "") < 0) problems.Add($"{n.Id}: unknown tool {e.Tool}");
            }
        }
        foreach (var t in Data.Tools)
        {
            foreach (var m in t.Materials)
                if (!TryParseResourceKind(m, out _)) problems.Add($"tool {t.Id}: material {m} is not a resource-node kind");
            if (t.Resource is { } r && !Data.Resources.Contains(r)) problems.Add($"tool {t.Id}: unknown resource {r}");
        }
        if (problems.Count > 0) return problems; // cycle and reachability checks need resolvable ids
        if (FindCycle() is { } cycle) problems.Add("cycle: " + string.Join(" -> ", cycle));
        foreach (var n in _nodes)
            if (n.Prereqs.Length == 0 && !n.IsInvention && !StarterSet.Contains(n.Id)) problems.Add($"{n.Id}: no prerequisites but not in the starter set");
        foreach (var id in Unreachable()) problems.Add($"{id}: not reachable from the starter set");
        return problems;
    }

    /// <summary>A prerequisite cycle as a list of ids, or null. Iterative three-colour DFS.</summary>
    public List<string>? FindCycle()
    {
        var colour = new byte[_nodes.Count];
        var parent = new int[_nodes.Count];
        for (int root = 0; root < _nodes.Count; root++)
        {
            if (colour[root] != 0) continue;
            var stack = new Stack<(int node, int next)>();
            stack.Push((root, 0));
            colour[root] = 1;
            parent[root] = -1;
            while (stack.Count > 0)
            {
                var (v, next) = stack.Pop();
                var pre = _nodes[v].Prereqs;
                if (next >= pre.Length) { colour[v] = 2; continue; }
                stack.Push((v, next + 1));
                int w = pre[next];
                if (colour[w] == 1)
                {
                    var cycle = new List<string> { _nodes[w].Id };
                    for (int x = v; x != w && x >= 0; x = parent[x]) cycle.Add(_nodes[x].Id);
                    cycle.Add(_nodes[w].Id);
                    cycle.Reverse();
                    return cycle;
                }
                if (colour[w] == 0) { colour[w] = 1; parent[w] = v; stack.Push((w, 0)); }
            }
        }
        return null;
    }

    /// <summary>Ids of concepts that discovery can never reach starting from <see cref="StarterSet"/>.</summary>
    public List<string> Unreachable()
    {
        var reached = new bool[_nodes.Count];
        foreach (var s in StarterSet) if (TryIndex(s, out int i)) reached[i] = true;
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var n in _nodes)
            {
                if (reached[n.Index] || (n.Prereqs.Length == 0 && !n.IsInvention)) continue;
                bool all = true;
                foreach (int p in n.Prereqs) all &= reached[p];
                if (all) { reached[n.Index] = true; changed = true; }
            }
        }
        var result = new List<string>();
        foreach (var n in _nodes) if (!reached[n.Index]) result.Add(n.Id);
        return result;
    }

    // ---- construction -----------------------------------------------------------------------

    private static IReadOnlyList<string> InventionTags(ConceptProposal c) => ["invention"];

    private int Insert(ConceptProposal c, IReadOnlyList<string> tags, bool invention)
    {
        if (_index.ContainsKey(c.Id)) throw new InvalidDataException($"duplicate concept id {c.Id}");
        int idx = _nodes.Count;
        _index[c.Id] = idx;
        uint hone = 0, actions = 0;
        ulong toolMask = 0;
        foreach (var t in tags) if (ActionNames.TryGetValue(t, out var a)) hone |= Bit(a);
        foreach (var e in c.Effects)
        {
            switch (e.Kind)
            {
                case ConceptEffectKind.YieldMod: hone |= Bit(HoneActionFor(e.Resource)); break;
                case ConceptEffectKind.UnlockTool:
                    hone |= Bit(ActionKind.Craft);
                    int ti = ToolIndex(e.Tool ?? "");
                    if (ti >= 0) toolMask |= 1UL << ti;
                    break;
                case ConceptEffectKind.UnlockBuilding: hone |= Bit(ActionKind.Build); break;
                case ConceptEffectKind.UnlockAction when e.Action is { } act: hone |= Bit(act); actions |= Bit(act); break;
                case ConceptEffectKind.Belief: hone |= Bit(ActionKind.Pray) | Bit(ActionKind.Talk); break;
            }
        }
        // Everyone talks: a concept with no other practice can still be honed by talking about it.
        if (hone == 0) hone = Bit(ActionKind.Talk);
        _nodes.Add(new ConceptNode
        {
            Index = idx, Concept = c, Tags = tags, IsInvention = invention, Prereqs = [],
            HoneMask = hone, ActionMask = actions, ToolMask = toolMask,
        });
        return idx;
    }

    /// <summary>Resolve prerequisite indices and rebuild the per-action and per-resource tables.</summary>
    private void Rebuild()
    {
        for (int i = 0; i < _nodes.Count; i++)
        {
            var n = _nodes[i];
            var pre = new List<int>(n.Concept.Prerequisites.Count);
            foreach (var p in n.Concept.Prerequisites) if (_index.TryGetValue(p, out int pi)) pre.Add(pi);
            if (pre.Count != n.Prereqs.Length || !pre.SequenceEqual(n.Prereqs))
                _nodes[i] = new ConceptNode
                {
                    Index = n.Index, Concept = n.Concept, Tags = n.Tags, IsInvention = n.IsInvention, Prereqs = pre.ToArray(),
                    HoneMask = n.HoneMask, ActionMask = n.ActionMask, ToolMask = n.ToolMask,
                };
        }
        int actionCount = ActionScorer.ActionCount;
        var honed = new List<int>[actionCount];
        for (int a = 0; a < actionCount; a++) honed[a] = new List<int>();
        _yields.Clear();
        var yields = new Dictionary<string, List<(int, float)>>(StringComparer.Ordinal);
        foreach (var n in _nodes)
        {
            for (int a = 0; a < actionCount; a++) if ((n.HoneMask & (1u << a)) != 0) honed[a].Add(n.Index);
            foreach (var e in n.Concept.Effects)
                if (e.Kind == ConceptEffectKind.YieldMod && e.Resource is { } r && e.Factor is { } f)
                {
                    if (!yields.TryGetValue(r, out var list)) yields[r] = list = new();
                    list.Add((n.Index, f));
                }
        }
        _honedBy = new int[actionCount][];
        for (int a = 0; a < actionCount; a++) _honedBy[a] = honed[a].ToArray();
        foreach (var (r, list) in yields) _yields[r] = list.ToArray();
        _nodeYields = new (int, float)[(int)ResourceKind.Game + 1][];
        for (int k = 0; k <= (int)ResourceKind.Game; k++) _nodeYields[k] = YieldsFor(ResourceId((ResourceKind)k));
        EnsureCounters();
    }

    private static ActionKind HoneActionFor(string? resource) => resource switch
    {
        "game" or "meat" or "hide" => ActionKind.Hunt,
        "grain" => ActionKind.Farm,
        "cloth" or "copper" or "bronze" or "iron" => ActionKind.Craft,
        _ => ActionKind.Gather,
    };

    public static uint Bit(ActionKind a) => 1u << (int)a;

    private static Dictionary<string, ActionKind> BuildActionNames()
    {
        var d = new Dictionary<string, ActionKind>(StringComparer.Ordinal);
        foreach (var a in Enum.GetValues<ActionKind>())
        {
            string name = a.ToString();
            d[char.ToLowerInvariant(name[0]) + name[1..]] = a;
        }
        return d;
    }
}
