using Eden.Sim;
using Godot;

namespace Eden.Game.Dev;

/// <summary>
/// M8, knowledge roadmap item 5: when <c>user://data/</c> exists, its <c>concepts.json</c> / <c>resources.json</c> /
/// <c>buildings.json</c> / <c>tools.json</c> replace the shipped ones (missing files fall back to the shipped copy).
/// The set is validated by Eden.Sim before the world starts; a bad set is reported with <c>GD.PushError</c> and the
/// game runs on the shipped data instead of failing to boot. Hooked from <c>Main._Ready</c> (one line).
/// </summary>
public static class DataOverride
{
    public static SimConfig Apply(SimConfig config)
    {
        string dir = System.IO.Path.Combine(OS.GetUserDataDir(), "data");
        if (!System.IO.Directory.Exists(dir)) return config;
        try
        {
            var data = KnowledgeData.FromDirectory(dir);
            GD.Print($"Eden data override: {dir} (concept set {data.SetHash}, {data.Concepts.Count} concepts)");
            return config with { DataOverrideDir = dir };
        }
        catch (System.IO.InvalidDataException e)
        {
            GD.PushError($"Eden: ignoring {e.Message}");
            return config;
        }
    }
}
