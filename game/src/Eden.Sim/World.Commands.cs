using Eden.Sim.Contracts;

namespace Eden.Sim;

public sealed partial class World
{
    public string Weather { get; private set; } = WeatherKind.Clear;
    public long WeatherUntil { get; private set; }

    private int _possessed = Npcs.None;

    /// <summary>Ids in the Mind's spotlight (SetThinkingCommand). Display only: not hashed, not saved.</summary>
    private readonly HashSet<int> _thinking = new();
    private float _possDirX, _possDirZ;
    private bool _possSprint;

    /// <summary>The live concept graph (M4 loads concepts.json into it; M1 only receives inventions).</summary>
    private readonly Dictionary<string, ConceptProposal> _concepts = new(StringComparer.Ordinal);

    /// <summary>Per-slot knowledge (concept id → proficiency 0..1). Sparse: null until an NPC knows something.</summary>
    private Dictionary<string, float>?[] _knowledge;

    public IReadOnlyDictionary<string, ConceptProposal> Concepts => _concepts;

    /// <summary>The possessed NPC id, or -1.</summary>
    public int PossessedId => _possessed;

    // ---- commands ---------------------------------------------------------------------------

    public void Apply(WorldCommand command)
    {
        switch (command)
        {
            case SetTimeScaleCommand:
                break; // handled by SimHost
            case SpawnCommand c:
                Spawn(c);
                break;
            case LightningCommand c:
                Lightning(c.X, c.Z, Math.Clamp(c.Radius, 0.5f, 50f));
                break;
            case WeatherCommand c:
                SetWeather(c.Kind, c.DurationTicks);
                break;
            case TerraformCommand c:
                Terraform(c.X, c.Z, Math.Clamp(c.Radius, 1f, 100f), Math.Clamp(c.DeltaHeight, -20f, 20f));
                break;
            case BlessCommand c:
                Bless(c.TargetId, c.Curse, c.DurationTicks);
                break;
            case DropItemCommand c:
                DropItem(c.ItemId, c.X, c.Z, Math.Clamp(c.Count, 1, 100));
                break;
            case WhisperCommand c:
                Whisper(c.NpcId, c.Text);
                break;
            case PossessCommand c:
                Possess(c.NpcId);
                break;
            case ReleasePossessionCommand:
                Release();
                break;
            case PossessedMoveCommand c:
                (_possDirX, _possDirZ, _possSprint) = (c.DirX, c.DirZ, c.Sprint);
                break;
            case PossessedInteractCommand c:
                PossessedInteract(c.TargetId, c.Verb);
                break;
            case SetThinkingCommand c:
                _thinking.Clear();
                if (c.NpcIds is { } ids)
                    foreach (int id in ids) if (Npcs.IsAlive(id)) _thinking.Add(id);
                break;
            case SaveCommand c:
                Save(c.Path);
                break;
            case LoadCommand c:
                Load(c.Path);
                break;
            default:
                AddLog($"unhandled command {command.GetType().Name}");
                break;
        }
    }

    /// <summary>Oldest rejected-command line not yet taken (for SimHost diagnostics).</summary>
    public bool TryDequeueLog(out string line)
    {
        line = _log.Count > 0 ? _log[0] : "";
        if (_log.Count == 0) return false;
        _log.RemoveAt(0);
        return true;
    }

    private void AddLog(string line)
    {
        if (_log.Count >= 256) _log.RemoveAt(0);
        _log.Add(line);
    }

    private void Save(string path)
    {
        try
        {
            using var f = File.Create(path);
            SaveWriter.Write(this, f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            AddLog($"save failed: {e.Message}");
        }
    }

    private void Load(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            SaveReader.ReadInto(this, f);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            AddLog($"load failed: {e.Message}");
        }
    }

    private void Spawn(SpawnCommand c)
    {
        int count = Math.Clamp(c.Count, 1, 1000);
        string kind = c.Kind ?? "";
        if (!Terrain.IsWalkable(c.X, c.Z) || Terrain.IsWater(Terrain.CellIndex(c.X, c.Z)))
        {
            AddLog($"spawn {kind} rejected: not on land");
            return;
        }
        string what;
        if (kind == "human")
        {
            for (int i = 0; i < count; i++)
            {
                var sex = Rng.Chance(0.5f) ? Sex.Female : Sex.Male;
                SpawnAdult(c.X + Rng.NextFloat() * 4 - 2, c.Z + Rng.NextFloat() * 4 - 2, sex, 16 + Rng.NextFloat() * 10);
            }
            what = count == 1 ? "a stranger" : $"{count} strangers";
        }
        else if (kind.StartsWith("animal:", StringComparison.Ordinal))
        {
            Resources.Add(ResourceKind.Game, c.X, c.Z, Math.Min(30, 3 * count));
            what = count == 1 ? $"a {kind[7..]}" : $"{count} {kind[7..]}";
        }
        else if (kind.StartsWith("plant:", StringComparison.Ordinal))
        {
            var rk = kind[6..] is "tree" or "oak" or "pine" ? ResourceKind.Tree : ResourceKind.Berry;
            for (int i = 0; i < count; i++)
                Resources.Add(rk, c.X + Rng.NextFloat() * 6 - 3, c.Z + Rng.NextFloat() * 6 - 3, rk == ResourceKind.Tree ? 20 : 12);
            what = $"{kind[6..]} growing";
        }
        else
        {
            AddLog($"spawn rejected: unknown kind '{kind}'");
            return;
        }
        GodAct($"The god made {what} appear out of nothing.", 8, c.X, c.Z, 0);
    }

    private void Lightning(float x, float z, float radius)
    {
        Resources.Burn(x, z, radius);
        int killed = 0;
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            float d = Dist(x, z, Npcs.X[s], Npcs.Z[s]);
            float fearRadius = radius + Config.PerceptionRadius;
            if (d > fearRadius) continue;
            Npcs.ThreatX[s] = x;
            Npcs.ThreatZ[s] = z;
            Npcs.NeedRef(s, Need.Safety) = Math.Max(0f, Npcs.Need(s, Need.Safety) - 0.8f * (1f - d / fearRadius) - 0.2f);
            Npcs.Phase[s] = ActionPhase.Done; // react now
            if (d <= radius * 0.5f) { Kill(s, DeathCause.Lightning); killed++; }
            else if (d <= radius) Npcs.Health[s] = Math.Max(0.05f, Npcs.Health[s] - 0.5f);
        }
        string text = killed > 0 ? $"Lightning struck from a clear sky and killed {killed}." : "Lightning struck from a clear sky.";
        GodAct(text, 9, x, z, radius);
    }

    private void SetWeather(string kind, int durationTicks)
    {
        string k = kind switch { "rain" => WeatherKind.Rain, "drought" => WeatherKind.Drought, "clear" => WeatherKind.Clear, _ => "" };
        if (k.Length == 0) { AddLog($"weather rejected: unknown kind '{kind}'"); return; }
        Weather = k;
        WeatherUntil = Tick + Math.Max(1, durationTicks);
        string text = k switch
        {
            WeatherKind.Rain => "The god sent rain.",
            WeatherKind.Drought => "The god withheld the rain; a drought began.",
            _ => "The god cleared the skies.",
        };
        GodAct(text, 8, 0, 0, float.PositiveInfinity);
    }

    private void Terraform(float x, float z, float radius, float delta)
    {
        float r2 = radius * radius;
        for (int cell = 0; cell < Terrain.Heights.Length; cell++)
        {
            float d2 = Sq(Terrain.CellX(cell) - x) + Sq(Terrain.CellZ(cell) - z);
            if (d2 > r2) continue;
            float falloff = 1f - MathF.Sqrt(d2) / radius;
            Terrain.Heights[cell] += delta * falloff * falloff * (3f - 2f * falloff);
        }
        Terrain.RecomputeDerived();
        OnTerrainChanged();
        _publishedHeights = null;
        _publishedBiomes = null;
        // Anyone now standing in deep water swims to the nearest walkable spot.
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s] || Terrain.IsWalkable(Npcs.X[s], Npcs.Z[s])) continue;
            Npcs.X[s] = x + (Npcs.X[s] - x) * 2f;
            Npcs.Z[s] = z + (Npcs.Z[s] - z) * 2f;
            Npcs.Phase[s] = ActionPhase.Done;
        }
        GodAct(delta > 0 ? "The god raised the land." : "The god sank the land.", 8, x, z, radius);
    }

    private void Bless(int targetId, bool curse, int durationTicks)
    {
        if (!Npcs.TryGetSlot(targetId, out int s)) { AddLog($"bless rejected: no npc {targetId}"); return; }
        Npcs.BlessUntil[s] = Tick + Math.Max(1, durationTicks);
        Npcs.Blessing[s] = (sbyte)(curse ? -1 : 1);
        if (curse) Npcs.NeedRef(s, Need.Safety) = Math.Max(0f, Npcs.Need(s, Need.Safety) - 0.5f);
        string text = curse ? $"The god cursed {Npcs.Name[s]}." : $"The god blessed {Npcs.Name[s]}.";
        Events.Publish(new SimEvent(Tick, MemoryKind.God, text, 8, [MemoryStore.GodId, targetId],
            Npcs.X[s], Npcs.Z[s], Config.PerceptionRadius, curse ? "curse" : "blessing", 7));
    }

    private void DropItem(string itemId, float x, float z, int count)
    {
        if (!Terrain.IsWalkable(x, z)) { AddLog("drop rejected: not on land"); return; }
        if (itemId is "food" or "berries" or "berry" or "meat" or "bread")
            Resources.Add(ResourceKind.Berry, x, z, 4f * count);
        else if (itemId is "wood" or "log") Resources.Add(ResourceKind.Tree, x, z, 5f * count);
        else if (itemId is "stone" or "rock") Resources.Add(ResourceKind.Stone, x, z, 5f * count);
        else { AddLog($"drop rejected: unknown item '{itemId}'"); return; }
        GodAct($"Gifts of {itemId} fell from the sky.", 8, x, z, 0);
    }

    private void Whisper(int npcId, string text)
    {
        if (!Npcs.TryGetSlot(npcId, out int s)) { AddLog($"whisper rejected: no npc {npcId}"); return; }
        string clean = Truncate(text, 280);
        Memories.Add(s, new Memory(Tick, MemoryKind.God, [MemoryStore.GodId, npcId], $"A voice from the sky said: \"{clean}\"", 9), Tick);
    }

    private void Possess(int npcId)
    {
        if (!Npcs.TryGetSlot(npcId, out int s)) { AddLog($"possess rejected: no npc {npcId}"); return; }
        if (_possessed != Npcs.None) Release();
        _possessed = npcId;
        _possDirX = _possDirZ = 0;
        Npcs.Phase[s] = ActionPhase.Done;
    }

    private void Release()
    {
        if (Npcs.TryGetSlot(_possessed, out int s))
            Memories.Add(s, new Memory(Tick, MemoryKind.God, [MemoryStore.GodId, _possessed],
                "I lost time; my body moved without me.", 6), Tick);
        _possessed = Npcs.None;
    }

    private void MovePossessed(int s)
    {
        float len = MathF.Sqrt(_possDirX * _possDirX + _possDirZ * _possDirZ);
        if (len < 0.01f) { Npcs.Phase[s] = ActionPhase.Done; return; }
        float speed = Speed(s, StageOf(s)) * (_possSprint ? 1.8f : 1f) / Terrain.MoveCost[Terrain.CellIndex(Npcs.X[s], Npcs.Z[s])];
        float nx = Npcs.X[s] + _possDirX / len * speed, nz = Npcs.Z[s] + _possDirZ / len * speed;
        Npcs.Heading[s] = MathF.Atan2(_possDirX, _possDirZ);
        if (!Terrain.IsWalkable(nx, nz)) return;
        Npcs.X[s] = nx;
        Npcs.Z[s] = nz;
        Npcs.Phase[s] = ActionPhase.Moving;
    }

    private void PossessedInteract(int targetId, string verb)
    {
        if (!Npcs.TryGetSlot(_possessed, out int s)) { AddLog("interact rejected: nobody possessed"); return; }
        if (verb == "work")
        {
            int node = Resources.FindNearest(ResourceKind.Berry, Npcs.X[s], Npcs.Z[s], 6f);
            if (node < 0) return;
            Npcs.TargetNode[s] = node;
            Npcs.Action[s] = ActionKind.Gather;
            Complete(s, StageOf(s));
            return;
        }
        if (!Npcs.TryGetSlot(targetId, out int o) || Dist(Npcs.X[s], Npcs.Z[s], Npcs.X[o], Npcs.Z[o]) > InteractDistance * 2)
        {
            AddLog($"interact rejected: target {targetId} out of reach");
            return;
        }
        if (verb == "talk") Converse(s, o);
        else if (verb.StartsWith("give:", StringComparison.Ordinal)) GiveFood(s, o);
        else AddLog($"interact rejected: unknown verb '{verb}'");
    }

    private void GodAct(string text, int importance, float x, float z, float radius)
    {
        Events.Publish(new SimEvent(Tick, MemoryKind.God, text, importance, [MemoryStore.GodId],
            x, z, radius + Config.PerceptionRadius, "god", importance));
    }

    private static string Truncate(string? s, int max) => s is null ? "" : s.Length <= max ? s : s[..max];

    // ---- intentions -------------------------------------------------------------------------

    /// <summary>Validate and apply one intention. Returns the rejection reason or null.</summary>
    public string? Apply(Intention intention)
    {
        var reason = Validator.Reject(intention);
        if (reason is not null) return reason;
        Npcs.TryGetSlot(intention.NpcId, out int s);
        switch (intention)
        {
            case GoalIntention g:
                Npcs.GoalAction[s] = g.Action;
                Npcs.GoalBonus[s] = (0.15f + 0.06f * g.Priority) * (g.Source == IntentionSource.Player ? 1.5f : 1f);
                Npcs.GoalUntil[s] = Tick + Config.TicksPerDay;
                Npcs.GoalTarget[s] = int.TryParse(g.Target, out int t) && Npcs.IsAlive(t) ? t : Npcs.None;
                Npcs.Phase[s] = ActionPhase.Done; // reconsider now, the bonus still has to win
                break;
            case SayIntention say:
            {
                string text = $"{Npcs.Name[s]} said: \"{say.Text}\"";
                Events.Publish(new SimEvent(Tick, MemoryKind.Speech, text, 3,
                    say.ListenerId is { } l ? [intention.NpcId, l] : [intention.NpcId],
                    Npcs.X[s], Npcs.Z[s], Config.PerceptionRadius));
                break;
            }
            case BeliefIntention b:
                Memories.Add(s, new Memory(Tick, MemoryKind.Belief, [intention.NpcId], b.Text, b.Importance), Tick);
                break;
            case InventionIntention inv:
            {
                _concepts[inv.Concept.Id] = inv.Concept;
                OnInvention(inv.Concept);
                SetKnowledge(s, inv.Concept.Id, 0.3f);
                Events.Publish(new SimEvent(Tick, MemoryKind.Discovery, $"{Npcs.Name[s]} came up with {inv.Concept.Name}.", 7,
                    [intention.NpcId], Npcs.X[s], Npcs.Z[s], Config.PerceptionRadius, "invention", 6));
                break;
            }
        }
        return null;
    }

    public float Proficiency(int slot, string conceptId)
        => slot < _knowledge.Length && _knowledge[slot] is { } k && k.TryGetValue(conceptId, out float p) ? p : 0f;

    public void SetKnowledge(int slot, string conceptId, float proficiency)
    {
        EnsureKnowledgeSlot(slot);
        float before = Proficiency(slot, conceptId);
        (_knowledge[slot] ??= new Dictionary<string, float>(StringComparer.Ordinal))[conceptId] = Math.Clamp(proficiency, 0f, 1f);
        AfterKnowledgeSet(slot, conceptId, before);
    }

    /// <summary>Base action set in M1, plus actions unlocked by concepts the NPC knows (≥ 0.3).</summary>
    public bool IsActionUnlocked(int slot, ActionKind action)
    {
        bool adult = StageOf(slot) is LifeStage.Adult or LifeStage.Elder;
        switch (action)
        {
            case ActionKind.Idle or ActionKind.Eat or ActionKind.Drink or ActionKind.Rest or ActionKind.Gather
                or ActionKind.Talk or ActionKind.Flee or ActionKind.Explore:
                return true;
            case ActionKind.Hunt or ActionKind.Court or ActionKind.Give or ActionKind.Pray:
                return adult;
        }
        bool societyAction = action is ActionKind.Build or ActionKind.Farm or ActionKind.Trade or ActionKind.Raid or ActionKind.Settle;
        if (societyAction && !IsSocietyActionUnlocked(slot, action, adult)) return false; // M3: adulthood, settlement membership
        if (societyAction && !Config.KnowledgeGatesActions) return true;
        // M4: everything below here also needs a concept that unlocks the action.
        if (slot >= _knowledge.Length || _knowledge[slot] is not { } known) return false;
        foreach (var (id, p) in known)
        {
            if (p < 0.3f || !_concepts.TryGetValue(id, out var concept)) continue;
            foreach (var e in concept.Effects)
                if (e.Kind == ConceptEffectKind.UnlockAction && e.Action == action) return true;
        }
        return false;
    }

    private void EnsureKnowledgeSlot(int slot)
    {
        if (slot < _knowledge.Length) return;
        int n = _knowledge.Length;
        while (n <= slot) n *= 2;
        Array.Resize(ref _knowledge, n);
    }

    // ---- memory recording -------------------------------------------------------------------

    /// <summary>EventBus handler: Chronicle entry + a memory for every living subject and witness.</summary>
    private void RecordEvent(SimEvent e)
    {
        if (e.ChronicleKind is not null)
            Chronicle(e.Tick, e.ChronicleKind, e.Text, e.ChronicleImportance, e.Subjects);

        Memory? memory = null;
        foreach (int id in e.Subjects)
        {
            if (!Npcs.TryGetSlot(id, out int s) || StageOf(s) == LifeStage.Infant) continue;
            Memories.Add(s, memory ??= new Memory(e.Tick, e.Kind, e.Subjects, e.Text, e.Importance), Tick);
        }
        if (e.WitnessRadius <= 0) return;

        if (float.IsInfinity(e.WitnessRadius))
        {
            for (int s = 0; s < Npcs.HighWater; s++)
                if (Npcs.Alive[s]) Witness(s, in e, ref memory);
            return;
        }
        // The grid is from the start of this tick; newborns are missing from it but infants do not remember.
        float r2 = e.WitnessRadius * e.WitnessRadius;
        _grid.Range(e.X, e.Z, e.WitnessRadius, out int bx0, out int bx1, out int bz0, out int bz1);
        for (int bz = bz0; bz <= bz1; bz++)
        for (int bx = bx0; bx <= bx1; bx++)
        for (int s = _grid.Head(bx, bz); s >= 0; s = _grid.Next(s))
        {
            if (Npcs.Alive[s] && Sq(Npcs.X[s] - e.X) + Sq(Npcs.Z[s] - e.Z) <= r2) Witness(s, in e, ref memory);
        }
    }

    private void Witness(int s, in SimEvent e, ref Memory? memory)
    {
        if (Array.IndexOf(e.Subjects, Npcs.IdOf(s)) >= 0 || StageOf(s) == LifeStage.Infant) return;
        Memories.Add(s, memory ??= new Memory(e.Tick, e.Kind, e.Subjects, e.Text, e.Importance), Tick);
    }
}
