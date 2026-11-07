using Godot;

namespace Eden.Game;

/// <summary>
/// Short, purely local visuals for the god powers (M7 UI). Nothing here talks to the sim. Each call builds a few
/// nodes (CPU particles, a ripple ring, a light), tweens them and frees them, so the cost is per click, never per
/// frame. Rain and drought also tint the screen, because weather in the sim is island-wide. The terraform brush
/// preview is one ring that follows the cursor while a terraform power is selected.
/// </summary>
public partial class PowerFx : Node3D
{
    private const string RingShaderCode = """
        shader_type spatial;
        render_mode unshaded, cull_disabled, depth_draw_never, depth_test_disabled, shadows_disabled, blend_mix;
        uniform vec4 tint : source_color = vec4(1.0);
        uniform float progress = 0.0;
        uniform float width = 0.06;
        uniform float fill = 0.12;
        uniform bool hold = false;
        void fragment() {
            vec2 p = UV * 2.0 - 1.0;
            float d = length(p);
            float r = mix(0.2, 1.0, progress);
            float ring = smoothstep(width, 0.0, abs(d - r));
            float inside = d < r ? fill : 0.0;
            ALBEDO = tint.rgb;
            ALPHA = tint.a * clamp(ring + inside, 0.0, 1.0) * (hold ? 0.8 : 1.0 - progress * progress);
        }
        """;

    private static Shader? _ringShader;
    private static readonly Dictionary<Color, StandardMaterial3D> ParticleMaterials = new();

    private MeshInstance3D? _brush;
    private ShaderMaterial? _brushMaterial;
    private CanvasLayer? _tintLayer;
    private ColorRect? _tint;

    /// <summary>Called every frame (Main: the terraform brush preview). Does nothing when null.</summary>
    public Action? Tick { get; set; }

    public override void _Process(double delta) => Tick?.Invoke();

    /// <summary>
    /// A2: raised when a rain or drought effect plays, so the clouds (<see cref="CloudLayer"/>) thicken or thin at once and
    /// stay that way for the weather's duration; the flash tint and the clouds share <see cref="WeatherState"/>'s colours.
    /// </summary>
    public event Action<SkyWeather>? WeatherPlayed;

    /// <summary>Effects started so far (tests).</summary>
    public int Started { get; private set; }

    public static Color ColourOf(GodPower p) => p switch
    {
        GodPower.SpawnHuman => new Color(1f, 0.93f, 0.75f),
        GodPower.SpawnAnimal => new Color(0.80f, 0.60f, 0.40f),
        GodPower.SpawnPlant => new Color(0.55f, 0.90f, 0.45f),
        GodPower.Rain => new Color(0.55f, 0.70f, 0.95f),
        GodPower.Drought => new Color(1f, 0.62f, 0.25f),
        GodPower.RaiseTerrain => new Color(0.85f, 0.70f, 0.45f),
        GodPower.LowerTerrain => new Color(0.45f, 0.60f, 0.80f),
        GodPower.Bless => new Color(1f, 0.85f, 0.35f),
        GodPower.Curse => new Color(0.35f, 0.28f, 0.40f),
        GodPower.DropFood => new Color(0.85f, 0.30f, 0.35f),
        _ => Colors.White,
    };

    /// <summary>Plays the effect of a power at a ground point. <paramref name="radius"/> is the brush (terraform only).</summary>
    public void Play(GodPower power, Vector3 at, float radius = 8f)
    {
        if (!IsInsideTree()) return;
        Started++;
        var c = ColourOf(power);
        switch (power)
        {
            case GodPower.SpawnHuman or GodPower.SpawnAnimal or GodPower.SpawnPlant:
                Particles(at, c, amount: 40, life: 1.4f, up: 6f, spread: 1.5f, size: 0.35f);
                Ripple(at, 6f, c, 1.1f);
                break;
            case GodPower.Rain:
                Particles(at + new Vector3(0, 40, 0), c with { A = 0.7f }, amount: 500, life: 1.6f, up: -30f, spread: 45f, size: 0.12f, stretch: 6f, seconds: 4f);
                ScreenTint(WeatherState.TintOf(SkyWeather.Rain), 4.5);
                WeatherPlayed?.Invoke(SkyWeather.Rain);
                break;
            case GodPower.Drought:
                Particles(at, c with { A = 0.6f }, amount: 260, life: 3f, up: 2.5f, spread: 40f, size: 1.0f, seconds: 3f); // A2: dust large enough to read at 55 m
                ScreenTint(WeatherState.TintOf(SkyWeather.Drought), 4.5);
                WeatherPlayed?.Invoke(SkyWeather.Drought);
                break;
            case GodPower.RaiseTerrain or GodPower.LowerTerrain:
                Ripple(at, radius, c, 1.2f);
                Ripple(at, radius * 0.6f, c, 0.9f, delay: 0.2);
                Particles(at, new Color(0.55f, 0.45f, 0.32f, 0.8f), amount: 60, life: 1.2f, up: power == GodPower.RaiseTerrain ? 5f : -1f,
                    spread: radius * 0.8f, size: 0.5f);
                break;
            case GodPower.Bless:
                Light(at + new Vector3(0, 4, 0), c, 8f, 1.6);
                Particles(at, c, amount: 70, life: 2f, up: 3f, spread: 2.5f, size: 0.18f);
                Ripple(at, 6f, c, 1.4f);
                break;
            case GodPower.Curse:
                Light(at + new Vector3(0, 3, 0), new Color(0.55f, 0.30f, 0.70f), 5f, 1.6);
                // A2: the smoke rises clear of the roofs and a dark ring marks the spot (it read as nothing at 55 m).
                Particles(at + new Vector3(0, 1.5f, 0), new Color(0.45f, 0.34f, 0.55f, 0.85f), amount: 70, life: 3f, up: 3.5f, spread: 2.5f, size: 1.8f);
                Ripple(at, 6f, new Color(0.40f, 0.22f, 0.50f), 1.4f);
                break;
            case GodPower.DropFood:
                Particles(at + new Vector3(0, 18, 0), c, amount: 24, life: 1.2f, up: -14f, spread: 2f, size: 0.3f);
                Ripple(at, 4f, new Color(1f, 0.85f, 0.6f), 0.8f, delay: 1.0);
                break;
        }
    }

    /// <summary>Shows the terraform brush ring at a ground point, or hides it with null.</summary>
    public void ShowBrush(Vector3? at, float radius, bool raise)
    {
        if (at is not { } p)
        {
            if (_brush is not null) _brush.Visible = false;
            return;
        }
        if (_brush is null)
        {
            _brushMaterial = RingMaterial(Colors.White);
            _brushMaterial.SetShaderParameter("progress", 1f);
            _brushMaterial.SetShaderParameter("fill", 0.06f);
            _brushMaterial.SetShaderParameter("width", 0.03f);
            _brushMaterial.SetShaderParameter("hold", true);
            _brush = new MeshInstance3D { Name = "Brush", Mesh = new PlaneMesh { Size = new Vector2(2, 2) }, MaterialOverride = _brushMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(_brush);
        }
        _brushMaterial!.SetShaderParameter("tint", (raise ? ColourOf(GodPower.RaiseTerrain) : ColourOf(GodPower.LowerTerrain)) with { A = 1f });
        _brush.Visible = true;
        _brush.Position = p + new Vector3(0, 0.4f, 0);
        _brush.Scale = new Vector3(radius, 1, radius);
    }

    public bool BrushVisible => _brush?.Visible == true;

    private void Ripple(Vector3 at, float radius, Color c, double seconds, double delay = 0)
    {
        var mat = RingMaterial(c with { A = 0.9f });
        var ring = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(2, 2) }, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        ring.Position = at + new Vector3(0, 0.5f, 0);
        ring.Scale = new Vector3(radius, 1, radius);
        AddChild(ring);
        var t = ring.CreateTween();
        if (delay > 0) t.TweenInterval(delay);
        t.TweenMethod(Callable.From<float>(v => mat.SetShaderParameter("progress", v)), 0f, 1f, seconds).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        t.TweenCallback(Callable.From(ring.QueueFree));
    }

    private void Particles(Vector3 at, Color c, int amount, float life, float up, float spread, float size, float stretch = 1f, float seconds = 0f)
    {
        var p = new CpuParticles3D
        {
            Amount = amount,
            Lifetime = life,
            OneShot = seconds <= 0,
            Explosiveness = seconds <= 0 ? 0.85f : 0f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(spread, 0.3f, spread),
            Direction = new Vector3(0, MathF.Sign(up) == 0 ? 1 : MathF.Sign(up), 0),
            Spread = 12f,
            InitialVelocityMin = MathF.Abs(up) * 0.6f,
            InitialVelocityMax = MathF.Abs(up),
            Gravity = Vector3.Zero,
            Color = c,
            Mesh = new QuadMesh { Size = new Vector2(size, size * stretch), Material = ParticleMaterial() },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = at + new Vector3(0, 0.3f, 0),
            Emitting = true,
        };
        var fade = new Gradient();
        fade.SetColor(0, Colors.White);
        fade.SetColor(1, new Color(1, 1, 1, 0));
        p.ColorRamp = fade;
        AddChild(p);
        double total = (seconds > 0 ? seconds : 0) + life + 0.2;
        var t = p.CreateTween();
        if (seconds > 0)
        {
            t.TweenInterval(seconds);
            t.TweenCallback(Callable.From(() => p.Emitting = false));
            t.TweenInterval(life + 0.2);
        }
        else t.TweenInterval(total);
        t.TweenCallback(Callable.From(p.QueueFree));
    }

    private void Light(Vector3 at, Color c, float energy, double seconds)
    {
        var light = new OmniLight3D { LightColor = c, LightEnergy = energy, OmniRange = 18f, Position = at, ShadowEnabled = false };
        AddChild(light);
        var t = light.CreateTween();
        t.TweenProperty(light, "light_energy", 0f, seconds).SetEase(Tween.EaseType.In);
        t.TweenCallback(Callable.From(light.QueueFree));
    }

    private void ScreenTint(Color c, double seconds)
    {
        if (_tintLayer is null)
        {
            _tintLayer = new CanvasLayer { Name = "WeatherTint", Layer = 0 };
            _tint = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Color = new Color(0, 0, 0, 0) };
            _tint.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _tintLayer.AddChild(_tint);
            AddChild(_tintLayer);
        }
        var rect = _tint!;
        rect.Color = c with { A = 0 };
        var t = rect.CreateTween();
        t.TweenProperty(rect, "color", c, 0.6);
        t.TweenInterval(Math.Max(0, seconds - 1.8));
        t.TweenProperty(rect, "color", c with { A = 0 }, 1.2);
    }

    /// <summary>Current screen tint alpha (tests).</summary>
    public float TintAlpha => _tint?.Color.A ?? 0f;

    private static ShaderMaterial RingMaterial(Color c)
    {
        _ringShader ??= new Shader { Code = RingShaderCode };
        var m = new ShaderMaterial { Shader = _ringShader };
        m.SetShaderParameter("tint", c);
        return m;
    }

    private static StandardMaterial3D ParticleMaterial()
    {
        var key = Colors.White;
        if (ParticleMaterials.TryGetValue(key, out var m)) return m;
        m = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        ParticleMaterials[key] = m;
        return m;
    }
}
