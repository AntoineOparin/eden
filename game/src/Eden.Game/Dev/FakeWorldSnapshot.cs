using Eden.Sim.Contracts;

namespace Eden.Game.Dev;

/// <summary>Immutable snapshot published by <see cref="FakeSimHost"/>. Inspector details are synthesised on demand.</summary>
public sealed class FakeWorldSnapshot(
    long tick, int year, int dayOfYear, float hourOfDay, WorldInfo world,
    float[] heights, byte[] biomes, NpcView[] npcs, IReadOnlyList<ChronicleEntry> newChronicle,
    ResourceView[]? resources = null, BuildingView[]? buildings = null, SettlementView[]? settlements = null,
    IReadOnlyList<ChronicleEntry>? history = null) : IWorldSnapshot
{
    private static readonly string[] FirstNames =
    [
        "Ada", "Bren", "Cato", "Dara", "Eli", "Fen", "Gwen", "Hale", "Ira", "Jun", "Kai", "Lior",
        "Mara", "Nils", "Oona", "Pell", "Quin", "Rhea", "Soren", "Tova", "Ulla", "Vale", "Wren", "Yara",
    ];
    private static readonly string[] NeedNames = ["food", "water", "rest", "warmth", "safety", "social", "purpose"];
    private static readonly string[] TraitNames = ["curiosity", "sociability", "aggression", "empathy", "patience", "ambition"];
    private static readonly string[] Goals = ["gather berries", "fetch water", "rest by the fire", "explore the hills", "visit a friend", "chop wood"];
    private static readonly string[] Thoughts =
    [
        "The sea looks calm today.", "I wonder what lies past the ridge.", "My feet ache.",
        "Someone should build a shelter.", "The sky felt watchful this morning.",
    ];

    public long Tick { get; } = tick;
    public int Year { get; } = year;
    public int DayOfYear { get; } = dayOfYear;
    public float HourOfDay { get; } = hourOfDay;
    public WorldInfo World { get; } = world;
    public ReadOnlyMemory<float> Heights { get; } = heights;
    public ReadOnlyMemory<byte> Biomes { get; } = biomes;
    public int Population => Npcs.Length;
    public ReadOnlyMemory<NpcView> Npcs { get; } = npcs;
    public ReadOnlyMemory<BuildingView> Buildings { get; } = buildings ?? ReadOnlyMemory<BuildingView>.Empty;
    public ReadOnlyMemory<ResourceView> Resources { get; } = resources ?? [];
    public ReadOnlyMemory<SettlementView> Settlements { get; } = settlements ?? [];
    public IReadOnlyList<ChronicleEntry> NewChronicle { get; } = newChronicle;

    /// <summary>Concepts a fake settlement may know; settlement k knows the first 3 + 2k (people know a random subset).</summary>
    private static readonly string[] FakeConcepts =
    [
        "foraging", "fire", "tracking", "stone-knapping", "shelter", "clothing", "herbal-lore", "fishing",
        "pottery", "agriculture", "weaving", "carpentry", "archery", "baking",
    ];

    public static string NameOf(int id) => $"{FirstNames[id % FirstNames.Length]} {id}";

    /// <summary>
    /// Made-up but stable proficiency of a fake NPC in <see cref="FakeConcepts"/>[c] (0 = unknown): everyone knows fire
    /// and foraging, settlement k's people know a random 70 % of the next 1 + 2k concepts. Shared by the NPC and the
    /// settlement details so the two agree.
    /// </summary>
    private static float FakeProficiency(int npcId, int settlementId, int c)
    {
        if (c == 0) return 0.2f + IslandGenerator.Hash01(npcId, 3, 3) * 0.6f; // foraging
        if (c == 1) return 0.4f; // fire
        int known = Math.Min(FakeConcepts.Length, 3 + 2 * Math.Max(0, settlementId));
        return c < known && IslandGenerator.Hash01(npcId, c, 29) < 0.7f ? 0.1f + 0.85f * IslandGenerator.Hash01(npcId, c, 31) : 0f;
    }

    // ---- C1 ---------------------------------------------------------------------------------

    private readonly IReadOnlyList<ChronicleEntry> _history = history ?? newChronicle;

    public IReadOnlyList<ChronicleEntry> ChronicleSince(long tick)
    {
        int n = 0;
        for (int i = 0; i < _history.Count; i++) if (_history[i].Tick > tick) n++;
        if (n == 0) return [];
        var result = new ChronicleEntry[n];
        for (int i = 0, k = 0; i < _history.Count; i++) if (_history[i].Tick > tick) result[k++] = _history[i];
        return result;
    }

    /// <summary>
    /// Fake settlement detail, synthesised on demand (no watch list: the fake answers at once). Holders and best
    /// proficiency come from the same made-up knowledge as the NPC details; food matches the view; buildings and the
    /// record markers are stable inventions.
    /// </summary>
    public bool TryGetSettlementDetail(int id, out SettlementDetail detail)
    {
        detail = null!;
        SettlementView? found = null;
        foreach (var v in Settlements.Span) if (v.Id == id) { found = v; break; }
        if (found is not { } view) return false;
        int members = 0, adults = 0;
        var holders = new int[FakeConcepts.Length];
        var best = new float[FakeConcepts.Length];
        foreach (var n in Npcs.Span)
        {
            if (n.SettlementId != id) continue;
            members++;
            if (n.Stage is LifeStage.Adult or LifeStage.Elder) adults++;
            for (int c = 0; c < FakeConcepts.Length; c++)
            {
                float p = FakeProficiency(n.Id, id, c);
                if (p <= 0f) continue;
                holders[c]++;
                best[c] = MathF.Max(best[c], p);
            }
        }
        var concepts = new List<SettlementConceptView>();
        for (int c = 0; c < FakeConcepts.Length; c++)
            if (holders[c] > 0) concepts.Add(new SettlementConceptView(FakeConcepts[c], holders[c], best[c], c < 2 + id / 2));
        concepts.Sort((a, b) => a.Holders != b.Holders ? b.Holders.CompareTo(a.Holders) : b.BestProficiency.CompareTo(a.BestProficiency));
        var buildings = new List<BuildingCount> { new("hut", Math.Max(1, members / 4)) };
        if (members >= 20) buildings.Add(new BuildingCount("farm-plot", members / 20));
        buildings.Add(new BuildingCount("storehouse", 1 + id % 2));
        if (id % 2 == 0) buildings.Add(new BuildingCount("shrine", 1));
        detail = new SettlementDetail(id, view.Name, members, adults, concepts, buildings, view.Food, FakeFoodPerDay(members));
        return true;
    }

    /// <summary>Fake daily eating: the real sim's adult rate (0.55 need per day / 0.35 per unit) per member.</summary>
    public static float FakeFoodPerDay(int members) => members * 0.55f / 0.35f;

    public bool TryGetNpcDetail(int npcId, out NpcDetail detail)
    {
        var span = Npcs.Span;
        // Fake ids are dense (id == index); fall back to a scan for safety.
        int index = npcId >= 0 && npcId < span.Length && span[npcId].Id == npcId ? npcId : -1;
        if (index < 0)
            for (int i = 0; i < span.Length; i++)
                if (span[i].Id == npcId) { index = i; break; }
        if (index < 0) { detail = null!; return false; }

        var needs = new Dictionary<string, float>();
        for (int i = 0; i < NeedNames.Length; i++)
            needs[NeedNames[i]] = 0.5f + 0.5f * MathF.Sin(Tick * 0.01f + npcId * 1.7f + i * 0.9f);
        var traits = new Dictionary<string, float>();
        for (int i = 0; i < TraitNames.Length; i++)
            traits[TraitNames[i]] = IslandGenerator.Hash01(npcId, i, 17);
        var knowledge = new Dictionary<string, float>();
        for (int c = 0; c < FakeConcepts.Length; c++)
            if (FakeProficiency(npcId, span[index].SettlementId, c) is float p and > 0f) knowledge[FakeConcepts[c]] = p;
        var rel = new List<RelationshipView>();
        int count = Math.Max(1, span.Length);
        for (int k = 1; k <= 3; k++)
        {
            int other = (npcId + k * 37) % count;
            if (other == npcId) continue;
            rel.Add(new RelationshipView(other, NameOf(other), IslandGenerator.Hash01(npcId, other, 1) * 2 - 1, IslandGenerator.Hash01(npcId, other, 2), 0));
        }
        var memories = new List<MemoryView>
        {
            new(0, "birth", "I woke on the shore of the island.", 7),
            new(Math.Max(0, Tick - 120), "sight", "I saw a strange light over the sea.", 5),
            new(Math.Max(0, Tick - 30), "talk", $"I spoke with {NameOf((npcId + 37) % count)}.", 3),
            new(Math.Max(0, Tick - 60), "gossip", "They say the sky itself struck the old oak, because the elders forgot the god.", 6, 2),
            new(0, "conversation", $"{NameOf((npcId + 11) % count)} showed me fire.", 3), // C1: the inspector's "from" column
        };
        memories.Sort((a, b) => b.Importance.CompareTo(a.Importance));
        detail = new NpcDetail(
            npcId, NameOf(npcId), 14 + npcId % 50, needs, traits, knowledge, rel, memories,
            Goals[(int)((npcId + Tick / 200) % Goals.Length)],
            span[index].IsThinking ? Thoughts[npcId % Thoughts.Length] : null);
        return true;
    }
}
