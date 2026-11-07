namespace Eden.Sim;

/// <summary>Every tunable the simulation reads. Defaults are the ones in docs/specs/10-sim-core.md.</summary>
public sealed record SimConfig
{
    public int Seed { get; init; } = 1;
    public int TicksPerHour { get; init; } = 10;
    public int HoursPerDay { get; init; } = 24;
    public int DaysPerYear { get; init; } = 10;
    public int WorldWidth { get; init; } = 256;
    public int WorldHeight { get; init; } = 256;
    public float CellSize { get; init; } = 2f;
    public float SeaLevel { get; init; } = 0f;
    public int ChunkSize { get; init; } = 32;
    public int StartingNpcs { get; init; } = 2;
    public int MemoryCap { get; init; } = 500;
    public float PerceptionRadius { get; init; } = 24f;
    public int AdultAgeYears { get; init; } = 14;
    public int BaseLifespanYears { get; init; } = 70;

    /// <summary>Real-time ticks per second at 1× (so 1 in-game hour = 0.5 s, 1 year = 2 min).</summary>
    public int TicksPerRealSecond { get; init; } = 20;

    // ---- M1 life-cycle and balance tunables -------------------------------------------------

    /// <summary>Genes shift lifespan by up to ± this many years around <see cref="BaseLifespanYears"/>.</summary>
    public int LifespanGeneRangeYears { get; init; } = 15;

    /// <summary>Probability per gene that a child's value is mutated instead of plainly inherited.</summary>
    public float MutationRate { get; init; } = 0.03f;

    /// <summary>Std-dev of the Gaussian nudge applied to a mutated gene (then clamped to [0,1]).</summary>
    public float MutationSigma { get; init; } = 0.2f;

    public float PregnancyYears { get; init; } = 0.75f;

    /// <summary>Minimum time after a birth before the mother can conceive again.</summary>
    public float PostpartumYears { get; init; } = 0.6f;

    public int FemaleFertileUntilYears { get; init; } = 45;

    /// <summary>Base chance that a courtship between partners conceives (scaled by fertility genes).</summary>
    public float ConceptionChance { get; init; } = 0.25f;

    /// <summary>Need decay per in-game day, in need units (needs are 0 = desperate .. 1 = satisfied).</summary>
    public float FoodDecayPerDay { get; init; } = 0.55f;
    public float WaterDecayPerDay { get; init; } = 0.9f;
    public float RestDecayPerDay { get; init; } = 0.7f;
    public float WarmthDecayPerDay { get; init; } = 0.5f;
    public float SocialDecayPerDay { get; init; } = 0.45f;
    public float PurposeDecayPerDay { get; init; } = 0.25f;

    /// <summary>Ticks an NPC survives with food at zero. Default: 4 in-game days.</summary>
    public int StarvationTicks { get; init; } = 4 * 24 * 10;

    /// <summary>Ticks an NPC survives with water at zero. Default: 2 in-game days.</summary>
    public int DehydrationTicks { get; init; } = 2 * 24 * 10;

    /// <summary>Food need restored by eating one unit of food.</summary>
    public float FoodPerUnit { get; init; } = 0.35f;

    public float MaxCarriedFood { get; init; } = 8f;

    /// <summary>Walking speed in metres per tick before gene and terrain modifiers.</summary>
    public float WalkSpeed { get; init; } = 2f;

    /// <summary>NPCs re-evaluate their action at least this often (staggered by slot).</summary>
    public int DecisionIntervalTicks { get; init; } = 10;

    /// <summary>A competing action must beat the current one's score by this margin to interrupt it.</summary>
    public float Hysteresis { get; init; } = 0.1f;

    /// <summary>Background (accident) death probability per year by life stage.</summary>
    public float InfantDeathPerYear { get; init; } = 0.04f;
    public float ChildDeathPerYear { get; init; } = 0.01f;
    public float AdultDeathPerYear { get; init; } = 0.006f;

    /// <summary>Probability the mother dies giving birth.</summary>
    public float ChildbirthDeathChance { get; init; } = 0.015f;

    /// <summary>Multiplier on the number of resource nodes world gen places. 0 = barren island.</summary>
    public float ResourceDensity { get; init; } = 1f;

    /// <summary>How far (m) an NPC looks for food, game and mates when deciding.</summary>
    public float SearchRadius { get; init; } = 96f;

    // ---- M3 society tunables ----------------------------------------------------------------

    /// <summary>DBSCAN eps (m) for clustering household homes into settlements.</summary>
    public float SettlementRadius { get; init; } = 40f;

    /// <summary>People a cluster of 2+ households needs before it counts as a settlement.</summary>
    public int SettlementMinMembers { get; init; } = 4;

    /// <summary>A settlement's land reaches this multiple of <see cref="SettlementRadius"/> from its centre.</summary>
    public float TerritoryFactor { get; init; } = 1.6f;

    /// <summary>Weight of the parents' traits in a grown child's traits (the rest is genes).</summary>
    public float UpbringingWeight { get; init; } = 0.3f;

    public int MigrationMinMembers { get; init; } = 22;
    public float MigrationChancePerDay { get; init; } = 0.04f;

    /// <summary>A colony site is this far (up to twice this) from the mother settlement.</summary>
    public float MigrationMinDistance { get; init; } = 90f;

    public int PeoplePerFarmPlot { get; init; } = 6;
    public int MaxFarmPlotsPerSettlement { get; init; } = 10;

    /// <summary>Crop growth per day at fertility 0.4 in spring/summer; 1 = ripe.</summary>
    public float CropGrowthPerDay { get; init; } = 0.4f;

    /// <summary>Food per harvest on average soil.</summary>
    public float FarmYield { get; init; } = 20f;

    /// <summary>Food a settlement can keep without a storehouse.</summary>
    public float BasePileCapacity { get; init; } = 20f;
    public float StoredFoodSpoilage { get; init; } = 0.02f;
    public float PiledFoodSpoilage { get; init; } = 0.1f;

    public int TradeCooldownDays { get; init; } = 10;
    public float TradeMinSurplus { get; init; } = 15f;
    public float TradeSurplusPerCapita { get; init; } = 1.5f;
    public float TradeDeficitPerCapita { get; init; } = 1f;
    public float TradeLoad { get; init; } = 12f;
    public float TradeRange { get; init; } = 320f;

    /// <summary>Raid when grievance + aggression × weight + food pressure − leader friendship reaches this.</summary>
    public float RaidThreshold { get; init; } = 1.3f;
    public float RaidAggressionWeight { get; init; } = 0.6f;
    public int RaidCooldownDays { get; init; } = 30;
    public float RaidRange { get; init; } = 320f;
    public float GrievanceDecayPerDay { get; init; } = 0.97f;

    /// <summary>Grievance a settlement gains per outsider adult on its land per day.</summary>
    public float TrespassGrievance { get; init; } = 0.02f;

    /// <summary>Leaders' mutual affinity at which two factions ally into one.</summary>
    public float AllianceAffinity { get; init; } = 0.6f;

    // ---- M4 knowledge tunables (docs/specs/50-knowledge-data.md) -----------------------------

    /// <summary>Proficiency the first NPCs (and god-spawned strangers) have in the starter concepts.</summary>
    public float StarterProficiency { get; init; } = 0.5f;

    /// <summary>A concept's effects (unlocks, beliefs) apply at this proficiency. yieldMod scales from 0.</summary>
    public float UnlockProficiency { get; init; } = 0.3f;

    /// <summary>All prerequisites must be at least this for a discovery roll to consider a concept.</summary>
    public float DiscoveryThreshold { get; init; } = 0.6f;

    /// <summary>Per-tick discovery chance per eligible concept ≈ curiosity × (1 − difficulty) × this. 0 disables discovery.</summary>
    public float DiscoveryRate { get; init; } = 1e-4f;

    /// <summary>Discovery is rolled every this many ticks per NPC (staggered), with the chance scaled to match.</summary>
    public int DiscoveryIntervalTicks { get; init; } = 60;

    /// <summary>Proficiency a discoverer starts with (and an LLM inventor).</summary>
    public float DiscoveredProficiency { get; init; } = 0.3f;

    /// <summary>Inventions need every prerequisite at this proficiency or above.</summary>
    public float InventionMastery { get; init; } = 0.8f;

    /// <summary>The Invent action multiplies one discovery roll by this.</summary>
    public float InventBoost { get; init; } = 30f;

    /// <summary>Honing per practice: Δp = rate × (1 − p)².</summary>
    public float HoneRate { get; init; } = 0.01f;

    /// <summary>Proximity chatter (Talk): the listener moves this fraction of the gap toward the speaker.</summary>
    public float ChatterRate { get; init; } = 0.04f;

    /// <summary>Deliberate teaching: fraction of the gap closed per lesson, times (0.5 + pupil curiosity).</summary>
    public float TeachRate { get; init; } = 0.25f;

    /// <summary>Teaching and learning between non-kin need at least this much trust.</summary>
    public float TeachTrustMin { get; init; } = 0.05f;

    /// <summary>A teacher needs this proficiency in the concept they teach.</summary>
    public float TeachMinProficiency { get; init; } = 0.5f;

    /// <summary>Apprenticeship: a child near a parent closes this fraction of the gap per day, per concept.</summary>
    public float ApprenticeRatePerDay { get; init; } = 0.04f;

    /// <summary>Writers need this proficiency in "writing" to write records; readers need <see cref="UnlockProficiency"/>.</summary>
    public float WritingProficiency { get; init; } = 0.5f;

    /// <summary>How close (m) a reader or writer must stand to a record.</summary>
    public float RecordRadius { get; init; } = 16f;

    /// <summary>Reading: fraction of the gap to the written level closed per day, per concept.</summary>
    public float ReadRatePerDay { get; init; } = 0.15f;

    /// <summary>Chance per day that each tool an NPC carries breaks.</summary>
    public float ToolBreakChancePerDay { get; init; } = 0.03f;

    /// <summary>Knowledge-gated actions (build, farm, craft, teach, learn, trade, raid, settle, invent) score 0 until unlocked.</summary>
    public bool KnowledgeGatesActions { get; init; } = true;

    // ---- K1 pace (docs/specs/51-knowledge-roadmap.md item 2) ----

    /// <summary>Discovery weight of a concept is max(0.02, 1 − difficulty) to this power (1 = the M4 linear curve).</summary>
    public int DiscoveryDifficultyExponent { get; init; } = 3;

    // ---- K1 forgetting (docs/specs/51-knowledge-roadmap.md item 1) ----

    /// <summary>Unpractised concepts decay daily: Δp = −rate/DaysPerYear × (p − floor). 0 disables forgetting.</summary>
    public float ForgetRatePerYear { get; init; } = 0.05f;

    /// <summary>No decay until this many years have passed since the NPC last practised (or gained) the concept.</summary>
    public float ForgetGraceYears { get; init; } = 2f;

    /// <summary>Below this proficiency an unpractised concept is gone (set to 0) unless a record floors it higher.</summary>
    public float ForgetDropBelow { get; init; } = 0.05f;

    /// <summary>A literate NPC's floor is the written level in the nearest record within this radius (m).</summary>
    public float ForgetRecordRadius { get; init; } = 64f;

    // ---- M7 myth and god tunables (docs/DECISIONS.md, 2026-11-06 "M7 myth") -------------------

    /// <summary>Daily multiplier on every settlement's Favour (half-life ≈ 14 years, about a generation).</summary>
    public float FavourDecayPerDay { get; init; } = 0.995f;

    /// <summary>Daily multiplier on Awe (half-life ≈ 7 years).</summary>
    public float AweDecayPerDay { get; init; } = 0.99f;

    /// <summary>A blessing moves Favour this fraction of the way to +1 (a curse, toward −1).</summary>
    public float BlessFavour { get; init; } = 0.12f;
    public float BlessAwe { get; init; } = 0.03f;

    /// <summary>Lightning: Awe gained (fraction of the gap to 1) and Favour lost if no rain or blessing follows.</summary>
    public float LightningAwe { get; init; } = 0.15f;
    public float LightningWrath { get; init; } = 0.08f;

    /// <summary>Drought: same as lightning, for every settlement.</summary>
    public float DroughtAwe { get; init; } = 0.1f;
    public float DroughtWrath { get; init; } = 0.08f;

    /// <summary>Rain: Favour gained by every settlement.</summary>
    public float RainFavour { get; init; } = 0.06f;

    /// <summary>Spawning, terraforming and gifts from the sky: Awe gained; a whisper: Awe gained in the listener's settlement.</summary>
    public float WonderAwe { get; init; } = 0.08f;
    public float WhisperAwe { get; init; } = 0.05f;
    public float GiftFavour { get; init; } = 0.04f;

    /// <summary>Days after lightning or drought before the Favour loss lands; rain or a blessing in between forgives it.</summary>
    public int WrathGraceDays { get; init; } = 3;

    /// <summary>Stance thresholds: favourable above +min, fearful at Awe ≥ fear-awe (and not favourable), hostile below −min.</summary>
    public float StanceFavourMin { get; init; } = 0.15f;
    public float StanceFearAwe { get; init; } = 0.35f;

    /// <summary>Two settlements stay "in contact" this many days after a cross-settlement talk or a trade/raid trip.</summary>
    public int ContactDays { get; init; } = 30;

    /// <summary>Envy/Fear move this fraction toward their target per day of contact, and decay by this factor otherwise.</summary>
    public float EnvyRate { get; init; } = 0.15f;
    public float EnvyDecayPerDay { get; init; } = 0.98f;

    /// <summary>Envy breeds grievance toward the envied settlement: this × Envy per day.</summary>
    public float EnvyGrievancePerDay { get; init; } = 0.02f;

    /// <summary>Gossip: the chance a retelling credits the god for a death, raid or discovery is this × the teller's Awe.</summary>
    public float AttributionMax { get; init; } = 0.3f;

    /// <summary>Gossip copies are retold again up to this many hops.</summary>
    public int MaxGossipGeneration { get; init; } = 6;

    /// <summary>Daily chance an adult with a god memory forms a belief about the god: rate × (0.5 + piety) × min(1, Awe + |Favour|).</summary>
    public float GodBeliefRate { get; init; } = 0.08f;

    /// <summary>A god memory older than this many days no longer prompts a belief.</summary>
    public int GodMemoryDays { get; init; } = 100;

    /// <summary>Religion emerges when this share of a settlement's adults (at least <see cref="ReligionMinAdults"/>) hold a god belief.</summary>
    public float ReligionThreshold { get; init; } = 0.4f;
    public int ReligionMinAdults { get; init; } = 3;

    /// <summary>Daily chance a religion crosses to a faction without one, per settlement pair in contact.</summary>
    public float ReligionSpreadChance { get; init; } = 0.03f;

    /// <summary>A faction whose capital's Favour sign opposes its religion's stance for this many years splits off a new faith.</summary>
    public float SchismYears { get; init; } = 14f;

    /// <summary>Pray: score multiplier within <see cref="ShrineRange"/> of a finished shrine of one's own settlement.</summary>
    public float ShrinePrayBoost { get; init; } = 1.6f;
    public float ShrineRange { get; init; } = 60f;

    /// <summary>Near a shrine, a believer in a religious faction also gets this × piety added to Pray.</summary>
    public float FaithPrayBonus { get; init; } = 0.1f;

    /// <summary>Extra purpose from praying at a shrine (on top of the M1 +0.35).</summary>
    public float ShrinePurpose { get; init; } = 0.15f;

    /// <summary>Prayer in a religious faction renews the god belief when the last one is older than this many days.</summary>
    public int PrayerBeliefDays { get; init; } = 5;

    /// <summary>A religious settlement plans a shrine once it has this many members (and a finished storehouse, and is not hungry).</summary>
    public int ShrineMinMembers { get; init; } = 20;

    public int TicksPerDay => TicksPerHour * HoursPerDay;
    public int TicksPerYear => TicksPerDay * DaysPerYear;
}
