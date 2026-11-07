using System.Text;
using System.Text.Json;

namespace Eden.Sim;

/// <summary>
/// Reads files written by <see cref="SaveWriter"/>. A load either succeeds completely or throws
/// <see cref="InvalidDataException"/> and leaves the target world exactly as it was (M8): the body is first loaded
/// into a scratch world, and only a body that loaded cleanly there is read into the target.
/// </summary>
public static class SaveReader
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static SaveHeader ReadHeader(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        return ReadHeader(r);
    }

    /// <summary>Construct a world from a save. The seed in the header only seeds the throwaway initial state.</summary>
    public static World Load(Stream stream)
    {
        var (header, body) = Split(stream);
        return LoadScratch(header, body);
    }

    /// <summary>Replace <paramref name="world"/>'s whole state with the save's, or throw and leave it untouched.</summary>
    public static void ReadInto(World world, Stream stream)
    {
        var (header, body) = Split(stream);
        // Validates every section; the scratch world is thrown away. Readers of sections appended after the body
        // (World.SectionsOf) must run in LoadScratch too, so a damaged one fails here before the live world is touched.
        LoadScratch(header, body);
        using var br = new BinaryReader(new MemoryStream(body, writable: false), Encoding.UTF8);
        world.ReadBody(br, header.Version);
        world.ReadChronicleSection(body, header.Version); // C1: Chronicle history section, if the save has one
        world.NoteLoadedConceptSet(header.ConceptSet);
    }

    private static World LoadScratch(SaveHeader header, byte[] body)
    {
        var world = new World(new SimConfig { Seed = header.Seed, StartingNpcs = 0, ResourceDensity = 0 });
        try
        {
            using var br = new BinaryReader(new MemoryStream(body, writable: false), Encoding.UTF8);
            world.ReadBody(br, header.Version);
            world.ReadChronicleSection(body, header.Version); // validated here too, so a damaged section fails before the live world is touched
        }
        catch (InvalidDataException) { throw; }
        catch (Exception e) when (e is EndOfStreamException or IOException or ArgumentException or IndexOutOfRangeException
                                       or OverflowException or FormatException or JsonException or NullReferenceException
                                       or KeyNotFoundException or InvalidOperationException or OutOfMemoryException)
        {
            throw new InvalidDataException($"save is damaged: {e.Message}", e);
        }
        world.NoteLoadedConceptSet(header.ConceptSet);
        return world;
    }

    /// <summary>Read the whole stream, check the magic, header and version; returns the header and the body bytes.</summary>
    private static (SaveHeader header, byte[] body) Split(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        using var r = new BinaryReader(buffer, Encoding.UTF8);
        SaveHeader header;
        try { header = ReadHeader(r); }
        catch (EndOfStreamException) { throw new InvalidDataException("save is truncated (header)"); }
        if (header.Version > SaveWriter.FormatVersion) throw new InvalidDataException($"save version {header.Version} is newer than this build");
        if (header.Version < SaveWriter.OldestReadable) throw new InvalidDataException($"save version {header.Version} is too old for this build");
        return (header, r.ReadBytes((int)(buffer.Length - buffer.Position)));
    }

    private static SaveHeader ReadHeader(BinaryReader r)
    {
        var magic = r.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(SaveWriter.Magic)) throw new InvalidDataException("not an Eden save");
        int len = r.ReadInt32();
        if (len is < 2 or > 1 << 16) throw new InvalidDataException("bad save header");
        var bytes = r.ReadBytes(len);
        if (bytes.Length < len) throw new InvalidDataException("save is truncated (header)");
        try
        {
            return JsonSerializer.Deserialize<SaveHeader>(bytes, Json) ?? throw new InvalidDataException("bad save header");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("bad save header", e);
        }
    }
}
