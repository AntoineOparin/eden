using Eden.Sim.Contracts;

namespace Eden.Game.Dev;

/// <summary>Immutable snapshot published by <see cref="FakeSimHost"/>. Inspector details are synthesised on demand.</summary>
public sealed class FakeWorldSnapshot(
    long tick, int year, int dayOfYear, float hourOfDay, WorldInfo world,
    float[] heights, byte[] biomes, NpcView[] npcs, IReadOnlyList<ChronicleEntry> newChronicle,
    ResourceView[]? resources = null, BuildingView[]? buildings = null, SettlementView[]? settlements = null) : IWorldSnapshot
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
        var knowledge = new Dictionary<string, float>
        {
            ["fire"] = 0.4f,
            ["foraging"] = 0.2f + IslandGenerator.Hash01(npcId, 3, 3) * 0.6f,
        };
        int known = Math.Min(FakeConcepts.Length, 3 + 2 * Math.Max(0, span[index].SettlementId));
        for (int c = 2; c < known; c++)
            if (IslandGenerator.Hash01(npcId, c, 29) < 0.7f) knowledge[FakeConcepts[c]] = 0.1f + 0.85f * IslandGenerator.Hash01(npcId, c, 31);
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
        };
        memories.Sort((a, b) => b.Importance.CompareTo(a.Importance));
        detail = new NpcDetail(
            npcId, NameOf(npcId), 14 + npcId % 50, needs, traits, knowledge, rel, memories,
            Goals[(int)((npcId + Tick / 200) % Goals.Length)],
            span[index].IsThinking ? Thoughts[npcId % Thoughts.Length] : null);
        return true;
    }
}
