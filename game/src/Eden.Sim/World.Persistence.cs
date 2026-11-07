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
        h = MythHash(h);
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
            if (!Npcs.Alive[s] || s >= _knowledge.Length || _knowledge[s] is not { Count: > 0 } k) continue; // empty == null: a load leaves it null
            ulong ks = 0;
            foreach (var (id, p) in k) ks += Hash.Combine(StableHash.Of(id), BitConverter.SingleToUInt32Bits(p));
            h = Hash.Mix(h, (ulong)s);
            h = Hash.Mix(h, ks);
        }
        return h;
    }

    // ---- binary body: tagged, versioned, length-prefixed sections (little-endian) -------------
    // Format version 2 (M8) frames each section as: tag (4 ASCII bytes), section version (int16), flags (byte),
    // stored length (int32), and when flags has Brotli the raw length (int32) before the compressed bytes. Format
    // version 1 had no flags byte and no compression. See docs/specs/10-sim-core.md "Save format and versions".

    /// <summary>Version of every section whose layout has not changed since format version 1.</summary>
    private const short SectionVersion = 1;

    [Flags]
    private enum SectionFlags : byte { None = 0, Brotli = 1 }

    /// <summary>Sections at least this large are Brotli-compressed (quality 1: about 10× smaller text at ~100 MB/s).</summary>
    private const int CompressAbove = 4096;

    internal void WriteBody(BinaryWriter w)
    {
        Section(w, "CONF", SectionVersion, bw => bw.Write(JsonSerializer.Serialize(Config)));
        Section(w, "CLCK", SectionVersion, bw =>
        {
            bw.Write(Tick);
            var (a, b, c, d) = Rng.State;
            bw.Write(a); bw.Write(b); bw.Write(c); bw.Write(d);
            bw.Write(TotalBirths); bw.Write(TotalDeaths); bw.Write(MaxLineage);
        });
        Section(w, "TERR", SectionVersion, bw =>
        {
            bw.Write(Terrain.Width); bw.Write(Terrain.Height);
            foreach (var v in Terrain.Heights) bw.Write(v);
            foreach (var v in Terrain.Moisture) bw.Write(v);
        });
        Section(w, "RSRC", SectionVersion, Resources.Write);
        Section(w, "NPCS", SectionVersion, Npcs.Write);
        Section(w, "MEMS", MemoryStore.SectionVersion, bw => Memories.Write(bw, Npcs));
        Section(w, "RELS", SectionVersion, Relationships.Write);
        Section(w, "KNOW", SectionVersion, WriteKnowledge);
        Section(w, "MISC", SectionVersion, bw =>
        {
            bw.Write(Weather); bw.Write(WeatherUntil);
            bw.Write(Npcs.None); bw.Write(0f); bw.Write(0f); bw.Write(false); // M6: possession is not saved (release on load)
        });
        Section(w, "SOCI", SectionVersion, WriteSociety);
        Section(w, "KNW2", SectionVersion, WriteKnowledgeExtras);
        Section(w, "MYTH", SectionVersion, WriteMyth);
    }

    /// <summary>A section at <see cref="SectionVersion"/> (1), for sections whose layout never changed.</summary>
    private static void Section(BinaryWriter w, string tag, Action<BinaryWriter> body) => Section(w, tag, SectionVersion, body);

    /// <summary>
    /// Every section of a body (format 2 framing unless told otherwise), decompressed. For readers of sections written
    /// after <see cref="WriteBody"/> (e.g. a section appended by SaveWriter): find your tag here instead of parsing the
    /// framing yourself.
    /// </summary>
    internal static Dictionary<string, (short version, byte[] body)> SectionsOf(byte[] body, int formatVersion = SaveWriter.FormatVersion)
    {
        using var r = new BinaryReader(new MemoryStream(body, writable: false));
        return ReadSections(r, formatVersion);
    }

    private static void Section(BinaryWriter w, string tag, short version, Action<BinaryWriter> body)
    {
        using var ms = new MemoryStream();
        using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true)) body(bw);
        var raw = new ReadOnlySpan<byte>(ms.GetBuffer(), 0, (int)ms.Length);
        w.Write(System.Text.Encoding.ASCII.GetBytes(tag));
        w.Write(version);
        if (raw.Length >= CompressAbove)
        {
            var packed = new byte[System.IO.Compression.BrotliEncoder.GetMaxCompressedLength(raw.Length)];
            if (System.IO.Compression.BrotliEncoder.TryCompress(raw, packed, out int written, quality: 1, window: 22)
                && written < raw.Length)
            {
                w.Write((byte)SectionFlags.Brotli);
                w.Write(written);
                w.Write(raw.Length);
                w.Write(packed, 0, written);
                return;
            }
        }
        w.Write((byte)SectionFlags.None);
        w.Write(raw.Length);
        w.Write(raw);
    }

    /// <summary>
    /// Split a body into sections. Every length is checked against what is left, so a cut-off file fails here with a
    /// clear message instead of half-way through a section. Unknown tags are kept but ignored (forward-compatible
    /// additions); a section version newer than this build is rejected by its reader.
    /// </summary>
    private static Dictionary<string, (short version, byte[] body)> ReadSections(BinaryReader r, int formatVersion)
    {
        var sections = new Dictionary<string, (short, byte[])>(StringComparer.Ordinal);
        var s = r.BaseStream;
        while (s.Position < s.Length)
        {
            int headerSize = formatVersion >= 2 ? 11 : 10;
            if (s.Length - s.Position < headerSize) throw new InvalidDataException("save is truncated (section header)");
            string tag = System.Text.Encoding.ASCII.GetString(r.ReadBytes(4));
            short version = r.ReadInt16();
            var flags = formatVersion >= 2 ? (SectionFlags)r.ReadByte() : SectionFlags.None;
            if ((flags & ~SectionFlags.Brotli) != 0) throw new InvalidDataException($"section {tag} has unknown flags {(byte)flags}");
            int len = r.ReadInt32();
            int rawLen = 0;
            if ((flags & SectionFlags.Brotli) != 0)
            {
                if (s.Length - s.Position < 4) throw new InvalidDataException($"save is truncated (section {tag})");
                rawLen = r.ReadInt32();
            }
            if (len < 0 || len > s.Length - s.Position) throw new InvalidDataException($"save is truncated (section {tag})");
            byte[] body = r.ReadBytes(len);
            if ((flags & SectionFlags.Brotli) != 0)
            {
                if (rawLen < 0 || rawLen > 1 << 30) throw new InvalidDataException($"section {tag} has a bad length");
                var raw = new byte[rawLen];
                if (!System.IO.Compression.BrotliDecoder.TryDecompress(body, raw, out int got) || got != rawLen)
                    throw new InvalidDataException($"section {tag} is damaged (decompression failed)");
                body = raw;
            }
            sections[tag] = (version, body);
        }
        return sections;
    }

    internal void ReadBody(BinaryReader r, int formatVersion = SaveWriter.FormatVersion)
    {
        var sections = ReadSections(r, formatVersion);
        BeginKnowledgeLoad();
        BinaryReader Open(string tag, short maxVersion = SectionVersion)
        {
            if (!sections.TryGetValue(tag, out var sec)) throw new InvalidDataException($"save is missing section {tag}");
            if (sec.version > maxVersion) throw new InvalidDataException($"section {tag} version {sec.version} is newer than this build");
            return new BinaryReader(new MemoryStream(sec.body));
        }
        byte[]? Optional(string tag)
        {
            if (!sections.TryGetValue(tag, out var sec)) return null;
            if (sec.version > SectionVersion) throw new InvalidDataException($"section {tag} version {sec.version} is newer than this build");
            return sec.body;
        }

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
            int tw = br.ReadInt32(), th = br.ReadInt32();
            if (tw <= 0 || th <= 0 || (long)tw * th * 8 > br.BaseStream.Length) throw new InvalidDataException("TERR: bad size");
            var t = new Terrain(tw, th, Config.CellSize, Config.SeaLevel);
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
        using (var br = Open("MEMS", MemoryStore.SectionVersion)) Memories.Read(br, Npcs, sections["MEMS"].version);
        using (var br = Open("RELS")) Relationships.Read(br);
        using (var br = Open("KNOW")) ReadKnowledge(br);
        using (var br = Open("MISC"))
        {
            Weather = br.ReadString(); WeatherUntil = br.ReadInt64();
            br.ReadInt32(); br.ReadSingle(); br.ReadSingle(); br.ReadBoolean(); // M6: older saves may name one; never restored
            _possessed = Npcs.None;
            ClearPossessionRuntime();
        }
        if (Optional("SOCI") is { } society) { using var br = new BinaryReader(new MemoryStream(society)); ReadSociety(br); }
        else ReadSociety(null);
        EndKnowledgeLoad(Optional("KNW2"));
        ReadMyth(Optional("MYTH"));

        _grid = new SpatialGrid(Terrain.MaxX + Terrain.CellSize, Terrain.MaxZ + Terrain.CellSize, 32f);
        _grid.Rebuild(Npcs);
        _kidsTick = -1; // M8: the kin index belongs to the world before the load
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
        ResetProfRows();
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
