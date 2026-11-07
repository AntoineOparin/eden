using Godot;

namespace Eden.Game;

/// <summary>
/// The one <see cref="Theme"/> for the whole HUD (spec 45, "UI readability"): Nunito (OFL) at 15 px body / 17 px bold
/// headings, warm-tinted translucent panels with 10 px corners and 8 px padding, buttons with hover / pressed /
/// toggled / disabled states in the terracotta accent, readable tooltips, 8 px container separation. Built in code
/// because the font is read from game/assets (no import step, see game/assets/README.md). Also hands out the
/// toolbar icons: Kenney game-icons PNGs plus glyphs drawn here where no CC0 icon exists (bolt, speech, sun, moon).
/// </summary>
public static class UiTheme
{
    public const int BodySize = 15;
    public const int HeadingSize = 17;
    public const int Grid = 8;

    public static readonly Color Ink = new(0.97f, 0.94f, 0.88f);
    public static readonly Color InkDim = new(0.80f, 0.75f, 0.68f);
    public static readonly Color Panel = new(0.17f, 0.13f, 0.10f, 0.86f);
    public static readonly Color PanelBorder = new(1f, 0.88f, 0.70f, 0.16f);
    public static readonly Color ButtonBg = new(0.29f, 0.23f, 0.18f, 0.95f);
    public static readonly Color ButtonHover = new(0.38f, 0.30f, 0.23f, 0.98f);
    public static readonly Color Accent = new(0.80f, 0.40f, 0.22f, 1f);
    public static readonly Color AccentHover = new(0.88f, 0.48f, 0.28f, 1f);

    private static Theme? _theme;
    private static readonly Dictionary<string, Texture2D?> Icons = new();

    public static FontFile? BodyFont { get; private set; }

    public static Theme Get()
    {
        if (_theme is not null) return _theme;
        var t = new Theme();
        BodyFont = LoadFont();
        if (BodyFont is not null)
        {
            t.DefaultFont = BodyFont;
            var bold = new FontVariation { BaseFont = BodyFont, VariationOpentype = new Godot.Collections.Dictionary { ["wght"] = 750 } };
            t.SetFont("bold_font", "RichTextLabel", bold);
            t.SetFont("font", "HeaderLabel", bold);
        }
        t.DefaultFontSize = BodySize;
        t.SetTypeVariation("HeaderLabel", "Label");
        t.SetFontSize("font_size", "HeaderLabel", HeadingSize);

        var panel = Box(Panel, 10, Grid, PanelBorder);
        panel.ShadowColor = new Color(0, 0, 0, 0.25f);
        panel.ShadowSize = 4;
        foreach (var type in new[] { "PanelContainer", "Panel", "PopupPanel" }) t.SetStylebox("panel", type, panel);

        t.SetStylebox("normal", "Button", Box(ButtonBg, 6, 6));
        t.SetStylebox("hover", "Button", Box(ButtonHover, 6, 6));
        t.SetStylebox("pressed", "Button", Box(Accent, 6, 6));
        t.SetStylebox("hover_pressed", "Button", Box(AccentHover, 6, 6));
        t.SetStylebox("disabled", "Button", Box(new Color(0.25f, 0.22f, 0.2f, 0.6f), 6, 6));
        t.SetStylebox("focus", "Button", new StyleBoxEmpty());
        t.SetColor("font_color", "Button", Ink);
        t.SetColor("font_hover_color", "Button", Colors.White);
        t.SetColor("font_pressed_color", "Button", Colors.White);
        t.SetColor("font_hover_pressed_color", "Button", Colors.White);
        t.SetColor("font_disabled_color", "Button", new Color(0.6f, 0.56f, 0.52f));
        t.SetColor("icon_normal_color", "Button", Ink);
        t.SetColor("icon_pressed_color", "Button", Colors.White);
        t.SetColor("icon_hover_color", "Button", Colors.White);
        t.SetConstant("h_separation", "Button", 6);
        t.SetConstant("icon_max_width", "Button", 18);

        t.SetColor("font_color", "Label", Ink);
        t.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0.35f));
        t.SetConstant("shadow_offset_y", "Label", 1);
        t.SetColor("default_color", "RichTextLabel", Ink);

        var field = Box(new Color(0.10f, 0.08f, 0.07f, 0.9f), 6, 6, PanelBorder);
        t.SetStylebox("normal", "LineEdit", field);
        t.SetStylebox("focus", "LineEdit", Box(new Color(0.12f, 0.09f, 0.08f, 0.95f), 6, 6, Accent));
        t.SetStylebox("normal", "TextEdit", field);
        t.SetColor("font_color", "LineEdit", Ink);
        t.SetColor("font_placeholder_color", "LineEdit", InkDim);
        t.SetColor("caret_color", "LineEdit", Accent);
        t.SetStylebox("normal", "OptionButton", Box(ButtonBg, 6, 6));
        t.SetStylebox("hover", "OptionButton", Box(ButtonHover, 6, 6));

        t.SetStylebox("background", "ProgressBar", Box(new Color(0.08f, 0.06f, 0.05f, 0.9f), 5, 0));
        t.SetStylebox("fill", "ProgressBar", Box(Accent, 5, 0));
        t.SetColor("font_color", "ProgressBar", Ink);

        t.SetStylebox("panel", "TooltipPanel", Box(new Color(0.12f, 0.09f, 0.07f, 0.96f), 6, Grid, PanelBorder));
        t.SetColor("font_color", "TooltipLabel", Ink);
        t.SetFontSize("font_size", "TooltipLabel", 14);

        foreach (var box in new[] { "HBoxContainer", "VBoxContainer", "BoxContainer" }) t.SetConstant("separation", box, Grid);
        t.SetConstant("h_separation", "GridContainer", Grid);
        t.SetConstant("v_separation", "GridContainer", 4);
        t.SetStylebox("panel", "TabContainer", panel);
        _theme = t;
        return t;
    }

    private static FontFile? LoadFont()
    {
        const string path = "res://assets/ui/fonts/nunito-variable.ttf";
        if (!Godot.FileAccess.FileExists(path)) return null;
        var font = new FontFile();
        if (font.LoadDynamicFont(path) != Error.Ok) return null;
        font.Antialiasing = TextServer.FontAntialiasing.Gray;
        font.Hinting = TextServer.Hinting.Light;
        return font;
    }

    private static StyleBoxFlat Box(Color bg, int radius, int padding, Color? border = null)
    {
        var b = new StyleBoxFlat { BgColor = bg };
        b.SetCornerRadiusAll(radius);
        b.ContentMarginLeft = b.ContentMarginRight = Math.Max(padding, 6) + 2;
        b.ContentMarginTop = b.ContentMarginBottom = padding * 0.6f;
        if (border is { } c)
        {
            b.BorderColor = c;
            b.SetBorderWidthAll(1);
        }
        b.AntiAliasing = true;
        return b;
    }

    // ---- icons ---------------------------------------------------------------------------------

    /// <summary>A toolbar icon by name: pointer, pause, forward, fast-forward, gear (Kenney); bolt, speech, sun, moon (drawn).</summary>
    public static Texture2D? Icon(string name)
    {
        if (Icons.TryGetValue(name, out var hit)) return hit;
        Texture2D? tex = name switch
        {
            "bolt" => Glyph(Bolt),
            "speech" => Glyph(Speech),
            "sun" => Glyph(Sun, new Color(1f, 0.82f, 0.35f)),
            "moon" => Glyph(Moon, new Color(0.82f, 0.86f, 1f)),
            _ => Art.GltfLoader.LoadTexture($"res://assets/ui/icons/{name}.png"),
        };
        Icons[name] = tex;
        return tex;
    }

    private const int GlyphSize = 64;

    /// <summary>Rasterises a coverage function (4×4 supersampled) into a white (or tinted) icon with alpha.</summary>
    private static ImageTexture Glyph(Func<float, float, bool> inside, Color? tint = null)
    {
        var img = Image.CreateEmpty(GlyphSize, GlyphSize, true, Image.Format.Rgba8);
        var c = tint ?? Colors.White;
        for (int y = 0; y < GlyphSize; y++)
        for (int x = 0; x < GlyphSize; x++)
        {
            int hits = 0;
            for (int sy = 0; sy < 4; sy++)
            for (int sx = 0; sx < 4; sx++)
                if (inside((x + (sx + 0.5f) / 4f) / GlyphSize, (y + (sy + 0.5f) / 4f) / GlyphSize)) hits++;
            img.SetPixel(x, y, new Color(c.R, c.G, c.B, hits / 16f));
        }
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    private static readonly Vector2[] BoltPoly =
    [
        new(0.58f, 0.04f), new(0.20f, 0.56f), new(0.46f, 0.56f), new(0.36f, 0.96f), new(0.80f, 0.40f), new(0.53f, 0.40f), new(0.66f, 0.04f),
    ];

    private static bool Bolt(float x, float y) => Geometry2D.IsPointInPolygon(new Vector2(x, y), BoltPoly);

    private static bool Speech(float x, float y)
    {
        // Rounded bubble with a tail at the lower left.
        float dx = MathF.Max(MathF.Abs(x - 0.5f) - 0.30f, 0), dy = MathF.Max(MathF.Abs(y - 0.42f) - 0.18f, 0);
        bool body = dx * dx + dy * dy <= 0.12f * 0.12f;
        bool tail = Geometry2D.IsPointInPolygon(new Vector2(x, y), [new(0.26f, 0.62f), new(0.20f, 0.90f), new(0.46f, 0.66f)]);
        return body || tail;
    }

    private static bool Sun(float x, float y)
    {
        float dx = x - 0.5f, dy = y - 0.5f;
        float r = MathF.Sqrt(dx * dx + dy * dy);
        if (r <= 0.21f) return true;
        if (r < 0.29f || r > 0.46f) return false;
        float a = MathF.Atan2(dy, dx) / MathF.Tau * 8f;
        return MathF.Abs(a - MathF.Round(a)) < 0.16f;
    }

    private static bool Moon(float x, float y)
    {
        float dx = x - 0.48f, dy = y - 0.5f;
        float ex = x - 0.64f, ey = y - 0.40f;
        return dx * dx + dy * dy <= 0.36f * 0.36f && ex * ex + ey * ey > 0.30f * 0.30f;
    }
}
