using System.Text;
using System.Text.Json;

namespace Eden.Sim;

/// <summary>Reads files written by <see cref="SaveWriter"/>. Loading replaces the world's whole state in place.</summary>
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
        long start = stream.Position;
        var header = ReadHeader(stream);
        stream.Position = start;
        var world = new World(new SimConfig { Seed = header.Seed, StartingNpcs = 0, ResourceDensity = 0 });
        ReadInto(world, stream);
        return world;
    }

    public static void ReadInto(World world, Stream stream)
    {
        // Read fully first so a truncated file throws before the world is touched.
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;
        using var r = new BinaryReader(buffer, Encoding.UTF8);
        var header = ReadHeader(r);
        if (header.Version > SaveWriter.FormatVersion) throw new InvalidDataException($"save version {header.Version} is newer than this build");
        var body = new MemoryStream(r.ReadBytes((int)(buffer.Length - buffer.Position)));
        using var br = new BinaryReader(body, Encoding.UTF8);
        world.ReadBody(br);
    }

    private static SaveHeader ReadHeader(BinaryReader r)
    {
        var magic = r.ReadBytes(4);
        if (!magic.AsSpan().SequenceEqual(SaveWriter.Magic)) throw new InvalidDataException("not an Eden save");
        int len = r.ReadInt32();
        if (len is < 2 or > 1 << 16) throw new InvalidDataException("bad save header");
        return JsonSerializer.Deserialize<SaveHeader>(r.ReadBytes(len), Json) ?? throw new InvalidDataException("bad save header");
    }
}
