using System.Text.Json;
using Eden.Sim.Contracts;

namespace Eden.Sim;

public sealed partial class World
{
    /// <summary>Order-sensitive hash of everything that affects future ticks. Equal after a save/load round trip.</summary>
    public ulong StateHash()
    {
        ulong h = Hash.Offset;
        h = Hash.Mix(h, (ulong)Tick);
        h = Hash.Mix(h, Rng.StateHash());
        foreach (var v in Terrain.Heights) h = Hash.Mix(h, v);
        foreach (var v in Terrain.Moisture) h = Hash.Mix(h, v);
        h = Resources.Hash(h);
        h = Npcs.Hash(h);
        h = Memories.Hash(h, Npcs);
        h = Relationships.Hash(h);
        h = KnowledgeHash(h);
        h = KnowledgeExtrasHash(h);
        h = Hash.Mix(h, StableHash.Of(Weather));
        h = Hash.Mix(h, (ulong)WeatherUntil);
        h = Hash.Mix(h, (ulong)_possessed);
        h = Hash.Mix(h, _possDirX);
        h = Hash.Mix(h, _possDirZ);
        h = Hash.Mix(h, _possSprint ? 1UL : 0UL);
        h = Hash.Mix(h, (ulong)TotalBirths);
        h = Hash.Mix(h, (ulong)TotalDeaths);
        h = Hash.Mix(h, (ulong)MaxLineage);
        h = SocietyHash(h);
        return h;
    }

    private ulong KnowledgeHash(ulong h)
    {
        ulong sum = 0;
        foreach (var (id, c) in _concepts)
            sum += Hash.Combine(StableHash.Of(id), StableHash.Of(c.Name), (ulong)c.Effects.Count);
        h = Hash.Mix(h, sum);
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s] || s >= _knowledge.Length || _knowledge[s] is not { } k) continue;
            ulong ks = 0;
            foreach (var (id, p) in k) ks += Hash.Combine(StableHash.Of(id), BitConverter.SingleToUInt32Bits(p));
            h = Hash.Mix(h, (ulong)s);
            h = Hash.Mix(h, ks);
        }
        return h;
    }

    // ---- binary body: tagged, versioned, length-prefixed sections (little-endian) -------------

    private const short SectionVersion = 1;

    internal void WriteBody(BinaryWriter w)
    {
        Section(w, "CONF", bw => bw.Write(JsonSerializer.Serialize(Config)));
        Section(w, "CLCK", bw =>
        {
            bw.Write(Tick);
            var (a, b, c, d) = Rng.State;
            bw.Write(a); bw.Write(b); bw.Write(c); bw.Write(d);
            bw.Write(TotalBirths); bw.Write(TotalDeaths); bw.Write(MaxLineage);
        });
        Section(w, "TERR", bw =>
        {
            bw.Write(Terrain.Width); bw.Write(Terrain.Height);
            foreach (var v in Terrain.Heights) bw.Write(v);
            foreach (var v in Terrain.Moisture) bw.Write(v);
        });
        Section(w, "RSRC", Resources.Write);
        Section(w, "NPCS", Npcs.Write);
        Section(w, "MEMS", bw => Memories.Write(bw, Npcs));
        Section(w, "RELS", Relationships.Write);
        Section(w, "KNOW", WriteKnowledge);
        Section(w, "MISC", bw =>
        {
            bw.Write(Weather); bw.Write(WeatherUntil);
            bw.Write(_possessed); bw.Write(_possDirX); bw.Write(_possDirZ); bw.Write(_possSprint);
        });
        Section(w, "SOCI", WriteSociety);
        Section(w, "KNW2", WriteKnowledgeExtras);
    }

    private static void Section(BinaryWriter w, string tag, Action<BinaryWriter> body)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) body(bw);
        w.Write(System.Text.Encoding.ASCII.GetBytes(tag));
        w.Write(SectionVersion);
        w.Write((int)ms.Length);
        w.Write(ms.GetBuffer(), 0, (int)ms.Length);
    }

    internal void ReadBody(BinaryReader r)
    {
        BeginKnowledgeLoad();
        var sections = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        while (r.BaseStream.Position < r.BaseStream.Length)
        {
            string tag = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));
            short version = r.ReadInt16();
            int len = r.ReadInt32();
            if (version > SectionVersion) throw new InvalidDataException($"section {tag} version {version} is newer than this build");
            sections[tag] = r.ReadBytes(len);
        }
        BinaryReader Open(string tag) => sections.TryGetValue(tag, out var b)
            ? new BinaryReader(new MemoryStream(b))
            : throw new InvalidDataException($"save is missing section {tag}");

        using (var br = Open("CONF"))
            Config = JsonSerializer.Deserialize<SimConfig>(br.ReadString()) ?? throw new InvalidDataException("bad config");
        using (var br = Open("CLCK"))
        {
            Tick = br.ReadInt64();
            Rng = Rng.FromState((br.ReadUInt64(), br.ReadUInt64(), br.ReadUInt64(), br.ReadUInt64()));
            TotalBirths = br.ReadInt32(); TotalDeaths = br.ReadInt32(); MaxLineage = br.ReadInt32();
        }
        using (var br = Open("TERR"))
        {
            var t = new Terrain(br.ReadInt32(), br.ReadInt32(), Config.CellSize, Config.SeaLevel);
            for (int i = 0; i < t.Heights.Length; i++) t.Heights[i] = br.ReadSingle();
            for (int i = 0; i < t.Moisture.Length; i++) t.Moisture[i] = br.ReadSingle();
            t.RecomputeDerived();
            Terrain = t;
        }
        Resources = new ResourceNodes(Terrain);
        using (var br = Open("RSRC")) Resources.Read(br);
        Npcs = new Npcs(64);
        using (var br = Open("NPCS")) Npcs.Read(br);
        Memories = new MemoryStore(Config.MemoryCap, Config.TicksPerDay);
        using (var br = Open("MEMS")) Memories.Read(br, Npcs);
        using (var br = Open("RELS")) Relationships.Read(br);
        using (var br = Open("KNOW")) ReadKnowledge(br);
        using (var br = Open("MISC"))
        {
            Weather = br.ReadString(); WeatherUntil = br.ReadInt64();
            _possessed = br.ReadInt32(); _possDirX = br.ReadSingle(); _possDirZ = br.ReadSingle(); _possSprint = br.ReadBoolean();
        }
        if (sections.TryGetValue("SOCI", out var society)) { using var br = new BinaryReader(new MemoryStream(society)); ReadSociety(br); }
        else ReadSociety(null);
        EndKnowledgeLoad(sections.TryGetValue("KNW2", out var knw2) ? knw2 : null);

        _grid = new SpatialGrid(Terrain.MaxX + Terrain.CellSize, Terrain.MaxZ + Terrain.CellSize, 32f);
        _grid.Rebuild(Npcs);
        _pendingBirths.Clear();
        _watched.Clear();
        _publishedHeights = null;
        _publishedBiomes = null;
        Chronicle(Tick, "load", $"The world was restored at year {Clock.Year}.", 2, Array.Empty<int>());
    }

    private void WriteKnowledge(BinaryWriter w)
    {
        var ids = new List<string>(_concepts.Keys);
        ids.Sort(StringComparer.Ordinal);
        w.Write(ids.Count);
        foreach (var id in ids)
        {
            var c = _concepts[id];
            w.Write(c.Id); w.Write(c.Name); w.Write(c.Description); w.Write(c.Difficulty);
            w.Write(c.Prerequisites.Count);
            foreach (var p in c.Prerequisites) w.Write(p);
            w.Write(c.Effects.Count);
            foreach (var e in c.Effects)
            {
                w.Write((byte)e.Kind);
                WriteOpt(w, e.Resource); WriteOpt(w, e.Building); WriteOpt(w, e.Tool); WriteOpt(w, e.Text);
                w.Write(e.Factor.HasValue); w.Write(e.Factor ?? 0f);
                w.Write(e.Action.HasValue); w.Write((int)(e.Action ?? ActionKind.Idle));
            }
        }
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            var k = s < _knowledge.Length ? _knowledge[s] : null;
            var keys = k is null ? new List<string>() : new List<string>(k.Keys);
            keys.Sort(StringComparer.Ordinal);
            w.Write(keys.Count);
            foreach (var key in keys) { w.Write(key); w.Write(k![key]); }
        }
    }

    private void ReadKnowledge(BinaryReader r)
    {
        _concepts.Clear();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            string id = r.ReadString(), name = r.ReadString(), desc = r.ReadString();
            float difficulty = r.ReadSingle();
            var prereqs = new string[r.ReadInt32()];
            for (int j = 0; j < prereqs.Length; j++) prereqs[j] = r.ReadString();
            var effects = new ConceptEffect[r.ReadInt32()];
            for (int j = 0; j < effects.Length; j++)
            {
                var kind = (ConceptEffectKind)r.ReadByte();
                string? res = ReadOpt(r), building = ReadOpt(r), tool = ReadOpt(r), text = ReadOpt(r);
                bool hasFactor = r.ReadBoolean(); float factor = r.ReadSingle();
                bool hasAction = r.ReadBoolean(); var action = (ActionKind)r.ReadInt32();
                effects[j] = new ConceptEffect(kind, res, hasFactor ? factor : null, building, tool, hasAction ? action : null, text);
            }
            _concepts[id] = new ConceptProposal(id, name, desc, prereqs, difficulty, effects);
        }
        _knowledge = new Dictionary<string, float>?[Math.Max(64, Npcs.Capacity)];
        for (int s = 0; s < Npcs.HighWater; s++)
        {
            if (!Npcs.Alive[s]) continue;
            int count = r.ReadInt32();
            for (int i = 0; i < count; i++) SetKnowledge(s, r.ReadString(), r.ReadSingle());
        }
    }

    private static void WriteOpt(BinaryWriter w, string? s)
    {
        w.Write(s is not null);
        if (s is not null) w.Write(s);
    }

    private static string? ReadOpt(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;
}
