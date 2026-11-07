using Godot;

namespace Eden.Game.Art;

/// <summary>
/// Runtime glTF loading. <c>game/assets/</c> carries a <c>.gdignore</c> (no editor import), so models are read with
/// <see cref="GltfDocument"/> straight from the files and images with <see cref="Image.LoadFromFile"/>. Loaded scenes
/// are cached as templates; callers must not add a template to the tree or change it (use <see cref="Instantiate"/>).
/// Main-thread only.
/// </summary>
public static class GltfLoader
{
    private static readonly Dictionary<string, Node3D> Templates = new();
    private static readonly Dictionary<string, Image> Images = new();

    /// <summary>Errors seen while loading (missing files, parse failures). Tests assert it stays empty.</summary>
    public static List<string> Errors { get; } = new();

    /// <summary>The generated scene for a .glb/.gltf path, cached. Null (and an entry in <see cref="Errors"/>) on failure.</summary>
    public static Node3D? Template(string path)
    {
        if (Templates.TryGetValue(path, out var cached)) return cached;
        if (!Godot.FileAccess.FileExists(path))
        {
            Errors.Add($"missing model {path}");
            return null;
        }
        var doc = new GltfDocument();
        var state = new GltfState();
        var err = doc.AppendFromFile(path, state);
        if (err != Error.Ok)
        {
            Errors.Add($"glTF load failed {path}: {err}");
            return null;
        }
        if (doc.GenerateScene(state) is not Node3D root)
        {
            Errors.Add($"glTF scene generation failed {path}");
            return null;
        }
        Templates[path] = root;
        return root;
    }

    /// <summary>A fresh copy of the template (with animations), ready to add to the tree.</summary>
    public static Node3D? Instantiate(string path) => Template(path)?.Duplicate() as Node3D;

    /// <summary>An image file read without the import system (PNG/JPG). Cached; null on failure.</summary>
    public static Image? LoadImage(string path)
    {
        if (Images.TryGetValue(path, out var img)) return img;
        if (!Godot.FileAccess.FileExists(path))
        {
            Errors.Add($"missing image {path}");
            return null;
        }
        img = Image.LoadFromFile(path);
        if (img is null || img.IsEmpty())
        {
            Errors.Add($"image load failed {path}");
            return null;
        }
        if (img.IsCompressed()) img.Decompress();
        Images[path] = img;
        return img;
    }

    /// <summary>A mipmapped texture from an image file, or null.</summary>
    public static ImageTexture? LoadTexture(string path, bool mipmaps = true)
    {
        var img = LoadImage(path);
        if (img is null) return null;
        var copy = (Image)img.Duplicate();
        if (mipmaps && !copy.HasMipmaps()) copy.GenerateMipmaps();
        return ImageTexture.CreateFromImage(copy);
    }

    /// <summary>Frees every cached template (tests, world reload).</summary>
    public static void Clear()
    {
        foreach (var t in Templates.Values) t.Free();
        Templates.Clear();
        Images.Clear();
    }
}
