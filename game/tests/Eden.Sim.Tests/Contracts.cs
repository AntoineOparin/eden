namespace Eden.Sim.Tests;

/// <summary>Locates docs/specs/contracts/ from the test binary by walking up to the repo root.</summary>
public static class Contracts
{
    public static readonly string Dir = Find();
    public static string Fixture(string name) => Path.Combine(Dir, "fixtures", name);
    public static string Schema(string name) => Path.Combine(Dir, name);

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "docs", "specs", "contracts");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("docs/specs/contracts not found above " + AppContext.BaseDirectory);
    }
}
