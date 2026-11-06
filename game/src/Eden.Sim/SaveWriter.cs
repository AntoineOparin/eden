using System.Text;
using System.Text.Json;

namespace Eden.Sim;

/// <summary>Save header, readable without loading the body (save browser, version checks).</summary>
public sealed record SaveHeader(int Version, int Seed, long Tick, int Population);

/// <summary>
/// Save format: <c>"EDEN"</c> magic, int32 header length, UTF-8 JSON <see cref="SaveHeader"/>, then the binary
/// body (little-endian, tagged and versioned sections, see <c>World.Persistence.cs</c>).
/// </summary>
public static class SaveWriter
{
    public const int FormatVersion = 1;
    internal static readonly byte[] Magic = "EDEN"u8.ToArray();

    public static void Write(World world, Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        var header = JsonSerializer.SerializeToUtf8Bytes(
            new SaveHeader(FormatVersion, world.Config.Seed, world.Tick, world.Population),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        w.Write(header.Length);
        w.Write(header);
        world.WriteBody(w);
    }
}
