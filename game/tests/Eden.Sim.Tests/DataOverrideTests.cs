using System.Text.Json.Nodes;

namespace Eden.Sim.Tests;

/// <summary>M8, knowledge roadmap item 5: <see cref="SimConfig.DataOverrideDir"/> replaces the shipped data files.</summary>
public sealed class DataOverrideTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"eden-data-{Environment.ProcessId}-{Guid.NewGuid():N}");

    public DataOverrideTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Shipped(string file)
    {
        using var s = typeof(World).Assembly.GetManifestResourceStream("Eden.Sim.Data." + file)!;
        return new StreamReader(s).ReadToEnd();
    }

    /// <summary>The shipped concepts with Fire renamed, so the override is visible in the world.</summary>
    private void WriteRenamedFire()
    {
        var concepts = JsonNode.Parse(Shipped("concepts.json"))!.AsArray();
        foreach (var c in concepts)
            if ((string?)c!["id"] == "fire") c["name"] = "Hearth fire";
        File.WriteAllText(Path.Combine(_dir, "concepts.json"), concepts.ToJsonString());
    }

    [Fact]
    public void An_override_folder_replaces_the_shipped_concepts_and_missing_files_fall_back()
    {
        WriteRenamedFire(); // only concepts.json: resources, buildings and tools come from the shipped data
        var w = new World(new SimConfig { Seed = 4, DataOverrideDir = _dir });
        Assert.True(w.Graph.TryIndex("fire", out int fire));
        Assert.Equal("Hearth fire", w.Graph[fire].Name);
        Assert.Equal(_dir, w.Graph.Data.Source);
        Assert.NotEqual(KnowledgeData.Default.SetHash, w.Graph.Data.SetHash);
        Assert.Equal(KnowledgeData.Default.Tools.Count, w.Graph.Data.Tools.Count);
        Assert.Equal(new World(new SimConfig { Seed = 4 }).Graph.Count, w.Graph.Count);
    }

    [Fact]
    public void A_save_records_the_override_set_and_loads_back_with_it()
    {
        WriteRenamedFire();
        var a = new World(new SimConfig { Seed = 4, DataOverrideDir = _dir });
        for (int t = 0; t < 3000; t++) a.Step();
        using var ms = new MemoryStream();
        SaveWriter.Write(a, ms);
        ms.Position = 0;
        Assert.Equal(a.Graph.Data.SetHash, SaveReader.ReadHeader(ms).ConceptSet);
        ms.Position = 0;
        var b = SaveReader.Load(ms);
        Assert.Equal(a.StateHash(), b.StateHash());
        Assert.Equal(_dir, b.Config.DataOverrideDir);
        Assert.Equal(a.Graph.Data.SetHash, b.Graph.Data.SetHash);
        for (int t = 0; t < 1000; t++) { a.Step(); b.Step(); }
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void Broken_json_is_reported_with_the_folder_name()
    {
        File.WriteAllText(Path.Combine(_dir, "concepts.json"), "[ { \"id\": ");
        var ex = Assert.Throws<InvalidDataException>(() => new World(new SimConfig { Seed = 1, DataOverrideDir = _dir }));
        Assert.StartsWith($"data override {_dir}:", ex.Message);
    }

    [Fact]
    public void A_graph_problem_is_reported_like_the_shipped_checks()
    {
        var concepts = JsonNode.Parse(Shipped("concepts.json"))!.AsArray();
        foreach (var c in concepts)
            if ((string?)c!["id"] == "tracking") c["prerequisites"] = new JsonArray("no-such-concept");
        File.WriteAllText(Path.Combine(_dir, "concepts.json"), concepts.ToJsonString());
        var ex = Assert.Throws<InvalidDataException>(() => KnowledgeData.FromDirectory(_dir));
        Assert.StartsWith($"data override {_dir}:", ex.Message);
        Assert.Contains("no-such-concept", ex.Message);
    }

    [Fact]
    public void A_missing_folder_is_an_error_and_no_folder_means_the_shipped_data()
    {
        var ex = Assert.Throws<InvalidDataException>(() => KnowledgeData.FromDirectory(Path.Combine(_dir, "absent")));
        Assert.Contains("folder not found", ex.Message);
        Assert.Same(KnowledgeData.Default, KnowledgeData.For(new SimConfig()));
        Assert.Null(KnowledgeData.Default.Source);
    }
}
