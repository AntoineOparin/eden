using System.Text;
using System.Text.Json;

namespace Eden.Sim;

/// <summary>
/// Save header, readable without loading the body (save browser, version checks). <see cref="ConceptSet"/> (M8) is
/// the hash of the knowledge data the world was created with (<see cref="KnowledgeData.SetHash"/>); null in saves
/// written before it existed.
/// </summary>
public sealed record SaveHeader(int Version, int Seed, long Tick, int Population, string? ConceptSet = null);

/// <summary>
/// Save format: <c>"EDEN"</c> magic, int32 header length, UTF-8 JSON <see cref="SaveHeader"/>, then the binary
/// body (little-endian, tagged and versioned sections, see <c>World.Persistence.cs</c>). The version policy is in
/// docs/specs/10-sim-core.md, "Save format and versions".
/// </summary>
public static class SaveWriter
{
    /// <summary>2 (M8): section framing gained a flags byte (Brotli) and MEMS went to section version 2.</summary>
    public const int FormatVersion = 2;

    /// <summary>Oldest format this build still reads.</summary>
    public const int OldestReadable = 1;

    internal static readonly byte[] Magic = "EDEN"u8.ToArray();

    public static void Write(World world, Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        var header = JsonSerializer.SerializeToUtf8Bytes(
            new SaveHeader(FormatVersion, world.Config.Seed, world.Tick, world.Population, world.Graph.Data.SetHash),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        w.Write(header.Length);
        w.Write(header);
        world.WriteBody(w);
        world.WriteChronicleSection(w); // C1: Chronicle history as its own tagged section (World.ChronicleSave.cs)
    }
}
