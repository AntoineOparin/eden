using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

public enum OverlayMode { Off, Stance, Religion, Knowledge }

/// <summary>
/// Religion / god-standing / knowledge-spread overlay (M7 UI). One translucent disc per settlement, draped over the
/// rendered terrain (a polar grid sampled from <see cref="Ground"/>, lifted <see cref="Lift"/> m), radius
/// <see cref="SettlementView.TerritoryRadius"/> (C1: the sim's own value, the land trespass and grievance use;
/// <see cref="Radius"/> only when a snapshot leaves it 0), with a brighter rim and the settlement's name floating
/// above the centre. Knowledge mode colours by the share of members who know the concept, from the sim's
/// <see cref="SettlementDetail"/> (through <see cref="SettlementKnowledge"/>). Colours come from <see cref="OverlayColors"/>.
/// Meshes rebuild only when the set of settlements, their centres or the terrain change; colours update on a timer.
/// Off by default; when off, the node is hidden and does no work.
/// </summary>
public partial class TerritoryOverlay : Node3D
{
    private const string ShaderCode = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, blend_mix, fog_disabled;
        instance uniform vec4 tint : source_color = vec4(1.0);
        void fragment() {
            ALBEDO = tint.rgb;
            ALPHA = COLOR.a * tint.a;
        }
        """;

    [Export] public double RefreshSeconds { get; set; } = 0.5;
    [Export] public float Lift { get; set; } = 0.6f;
    [Export] public int Rings { get; set; } = 20;
    [Export] public int Segments { get; set; } = 56;

    /// <summary>Fallback radius for a snapshot without <see cref="SettlementView.TerritoryRadius"/>.</summary>
    public float Radius { get; set; } = DefaultRadius;

    /// <summary>The radius drawn for a settlement: its territory radius from the sim, else <see cref="Radius"/>.</summary>
    public float RadiusOf(in SettlementView v) => RadiusFor(v, Radius);

    /// <summary>Pure form of <see cref="RadiusOf"/>: the view's territory radius, else <paramref name="fallback"/>.</summary>
    public static float RadiusFor(in SettlementView v, float fallback) => v.TerritoryRadius > 0f ? v.TerritoryRadius : fallback;

    public static float DefaultRadius
    {
        get
        {
            var cfg = new Eden.Sim.SimConfig();
            return cfg.SettlementRadius * cfg.TerritoryFactor;
        }
    }

    /// <summary>Rendered ground height at a world point (Main passes the terrain's render sampler).</summary>
    public Func<float, float, float>? Ground { get; set; }

    /// <summary>Bumps when the ground changes (terrain render version), so discs re-drape.</summary>
    public int GroundVersion { get; set; }

    /// <summary>Knowledge for the knowledge-spread mode; shared with the settlement panel.</summary>
    public SettlementKnowledge? Knowledge { get; set; }

    public OverlayMode Mode { get; private set; } = OverlayMode.Off;
    public string? Concept { get; private set; }

    private static Shader? _shader;
    private ShaderMaterial? _material;
    private readonly Dictionary<int, (MeshInstance3D Disc, Label3D Label, float X, float Z, int GroundVersion, float R)> _discs = new();
    private readonly HashSet<int> _seen = new();
    private double _since = double.MaxValue;

    /// <summary>Discs currently drawn (tests).</summary>
    public int DiscCount => _discs.Count;

    /// <summary>Tint of a settlement's disc as last applied (tests, legend).</summary>
    public Color TintOf(int settlementId) => _discs.TryGetValue(settlementId, out var d) ? (Color)d.Disc.GetInstanceShaderParameter("tint") : default;

    /// <summary>Radius of a settlement's disc as last built (tests).</summary>
    public float DrawnRadiusOf(int settlementId) => _discs.TryGetValue(settlementId, out var d) ? d.R : 0f;

    /// <summary>Caption of a settlement's disc as last applied (tests).</summary>
    public string CaptionOf(int settlementId) => _discs.TryGetValue(settlementId, out var d) ? d.Label.Text : "";

    public void SetMode(OverlayMode mode, string? concept = null)
    {
        Mode = mode;
        Concept = concept;
        Visible = mode != OverlayMode.Off;
        _since = double.MaxValue; // refresh on the next update
    }

    /// <summary>Main / GodUi call this every frame; it throttles itself and does nothing while off.</summary>
    public void UpdateFrom(IWorldSnapshot s, double delta)
    {
        if (Mode == OverlayMode.Off) return;
        _since += delta;
        if (_since < RefreshSeconds) return;
        _since = 0;
        Refresh(s);
    }

    public void Refresh(IWorldSnapshot s)
    {
        _material ??= new ShaderMaterial { Shader = _shader ??= new Shader { Code = ShaderCode } };
        _seen.Clear();
        foreach (var v in s.Settlements.Span)
        {
            _seen.Add(v.Id);
            float radius = RadiusOf(v);
            if (!_discs.TryGetValue(v.Id, out var d) || MathF.Abs(d.X - v.X) > 4f || MathF.Abs(d.Z - v.Z) > 4f || d.GroundVersion != GroundVersion
                || MathF.Abs(d.R - radius) > 0.5f)
            {
                if (d.Disc is null)
                {
                    var disc = new MeshInstance3D { Name = $"Territory{v.Id}", MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
                    var label = new Label3D
                    {
                        Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FixedSize = true, PixelSize = 0.0011f, FontSize = 22,
                        OutlineSize = 8, Modulate = UiTheme.Ink, OutlineModulate = new Color(0.1f, 0.07f, 0.05f, 0.9f), NoDepthTest = true,
                    };
                    if (UiTheme.BodyFont is { } font) label.Font = font;
                    AddChild(disc);
                    AddChild(label);
                    d = (disc, label, v.X, v.Z, GroundVersion, radius);
                }
                d.Disc.Mesh = BuildDisc(v.X, v.Z, radius, s);
                d.Label.Position = new Vector3(v.X, HeightAt(v.X, v.Z, s) + 14f, v.Z);
                _discs[v.Id] = (d.Disc, d.Label, v.X, v.Z, GroundVersion, radius);
            }
            var (tint, caption) = Describe(v);
            d = _discs[v.Id];
            d.Disc.SetInstanceShaderParameter("tint", tint);
            d.Label.Text = caption;
        }
        if (_discs.Count != _seen.Count)
            foreach (int id in _discs.Keys.Where(id => !_seen.Contains(id)).ToList())
            {
                _discs[id].Disc.QueueFree();
                _discs[id].Label.QueueFree();
                _discs.Remove(id);
            }
    }

    private (Color Tint, string Caption) Describe(in SettlementView v)
    {
        switch (Mode)
        {
            case OverlayMode.Religion:
                return (OverlayColors.Religion(v.ReligionName), $"{v.Name}\n{v.ReligionName ?? "no faith"}");
            case OverlayMode.Knowledge:
                Knowledge?.Want(v.Id);
                var e = Knowledge?.Get(v.Id);
                if (Concept is null || e is null) return (OverlayColors.KnowledgeUnknown, $"{v.Name}\n…");
                e.Concepts.TryGetValue(Concept, out var c);
                float share = e.Members > 0 ? c.Holders / (float)e.Members : 0f;
                return (OverlayColors.Knowledge(share), $"{v.Name}\n{c.Holders}/{e.Members} know {Concept}{(c.HasRecord ? ", written" : "")}");
            default:
                return (OverlayColors.Stance(v.Stance), $"{v.Name}\n{OverlayColors.StanceWord(v.Stance)}");
        }
    }

    private float HeightAt(float x, float z, IWorldSnapshot s)
        => MathF.Max(s.World.SeaLevel, Ground?.Invoke(x, z) ?? HeightField.Sample(s.Heights.Span, s.World, x, z));

    /// <summary>Polar grid draped on the ground; vertex alpha is a soft fill with a bright rim and a feathered edge.</summary>
    private ArrayMesh BuildDisc(float cx, float cz, float radius, IWorldSnapshot s)
    {
        int rings = Math.Max(2, Rings), segs = Math.Max(8, Segments);
        var verts = new Vector3[1 + rings * segs];
        var colors = new Color[verts.Length];
        verts[0] = new Vector3(cx, HeightAt(cx, cz, s) + Lift, cz);
        colors[0] = new Color(1, 1, 1, OverlayColors.FillAlpha);
        for (int r = 1; r <= rings; r++)
        {
            float t = r / (float)rings;
            float rad = radius * t;
            float a = t < 0.88f ? OverlayColors.FillAlpha : t < 0.97f ? OverlayColors.RimAlpha : 0f;
            for (int k = 0; k < segs; k++)
            {
                float ang = k * MathF.Tau / segs;
                float x = cx + MathF.Cos(ang) * rad, z = cz + MathF.Sin(ang) * rad;
                int i = 1 + (r - 1) * segs + k;
                verts[i] = new Vector3(x, HeightAt(x, z, s) + Lift, z);
                colors[i] = new Color(1, 1, 1, a);
            }
        }
        var idx = new List<int>(segs * 3 + (rings - 1) * segs * 6);
        for (int k = 0; k < segs; k++) { idx.Add(0); idx.Add(1 + (k + 1) % segs); idx.Add(1 + k); }
        for (int r = 1; r < rings; r++)
        for (int k = 0; k < segs; k++)
        {
            int a0 = 1 + (r - 1) * segs + k, a1 = 1 + (r - 1) * segs + (k + 1) % segs;
            int b0 = a0 + segs, b1 = a1 + segs;
            idx.Add(a0); idx.Add(a1); idx.Add(b0);
            idx.Add(a1); idx.Add(b1); idx.Add(b0);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}

/// <summary>Overlay palette and legend (M7 UI). Pure; shared by the overlay and its legend.</summary>
public static class OverlayColors
{
    public const float FillAlpha = 0.15f;
    public const float RimAlpha = 0.75f;

    public static readonly Color Favourable = new(0.98f, 0.78f, 0.28f);
    public static readonly Color Fearful = new(0.62f, 0.45f, 0.95f);
    public static readonly Color Hostile = new(0.90f, 0.26f, 0.20f);
    public static readonly Color Neutral = new(0.80f, 0.80f, 0.76f);
    public static readonly Color NoFaith = new(0.55f, 0.55f, 0.55f, 0.6f);
    public static readonly Color KnowledgeUnknown = new(0.9f, 0.9f, 0.9f, 0.35f);
    public static readonly Color KnowsNone = new(0.62f, 0.50f, 0.45f);
    public static readonly Color KnowsAll = new(0.30f, 0.85f, 0.50f);

    private static readonly Color[] Faiths =
    [
        new(0.30f, 0.65f, 0.95f), new(0.95f, 0.55f, 0.20f), new(0.55f, 0.85f, 0.35f), new(0.90f, 0.40f, 0.70f),
        new(0.35f, 0.85f, 0.80f), new(0.95f, 0.85f, 0.35f), new(0.65f, 0.50f, 0.95f), new(0.85f, 0.35f, 0.30f),
    ];

    public static Color Stance(GodStance s) => s switch
    {
        GodStance.Favourable => Favourable,
        GodStance.Fearful => Fearful,
        GodStance.Hostile => Hostile,
        _ => Neutral,
    };

    public static string StanceWord(GodStance s) => s switch
    {
        GodStance.Favourable => "favourable",
        GodStance.Fearful => "fearful",
        GodStance.Hostile => "hostile",
        _ => "neutral",
    };

    /// <summary>A stable colour per faith name (FNV-1a of the name into an 8-colour palette).</summary>
    public static Color Religion(string? name)
    {
        if (string.IsNullOrEmpty(name)) return NoFaith;
        uint h = 2166136261;
        foreach (char ch in name) h = (h ^ ch) * 16777619;
        return Faiths[h % (uint)Faiths.Length];
    }

    /// <summary>Share of members who know the concept, 0..1 → brown to green.</summary>
    public static Color Knowledge(float share) => KnowsNone.Lerp(KnowsAll, Math.Clamp(share, 0f, 1f));
}
