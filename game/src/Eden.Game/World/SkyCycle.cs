using Godot;

namespace Eden.Game;

/// <summary>
/// Day and night from the snapshot's <c>HourOfDay</c>: sun elevation and colour (warm dawn and dusk), a dim cool moon
/// light at night, procedural sky colours, ambient and fog tint. The sim runs a day in 12 real seconds at 1×, so the
/// cycle is shown in full only while the clock runs at most <see cref="FullCycleHoursPerSecond"/>; faster than
/// <see cref="FrozenHoursPerSecond"/> the light settles on a fixed afternoon instead of strobing. Night stays readable.
/// </summary>
public partial class SkyCycle : Node
{
    [Export] public NodePath SunPath { get; set; } = "../Sun";
    [Export] public NodePath MoonPath { get; set; } = "../Moon";
    [Export] public NodePath EnvironmentPath { get; set; } = "../WorldEnvironment";
    [Export] public float FullCycleHoursPerSecond { get; set; } = 2.5f;
    [Export] public float FrozenHoursPerSecond { get; set; } = 12f;
    [Export] public float SettledHour { get; set; } = 14f;
    [Export] public float SunEnergy { get; set; } = 1.35f;
    [Export] public float MoonEnergy { get; set; } = 0.32f;

    private DirectionalLight3D? _sun, _moon;
    private Godot.Environment? _env;
    private ProceduralSkyMaterial? _sky;
    private float _lastHour = -1, _rate;

    private static readonly Color DaySkyTop = new(0.30f, 0.58f, 0.92f), DayHorizon = new(0.72f, 0.85f, 0.95f);
    private static readonly Color DuskSkyTop = new(0.32f, 0.36f, 0.62f), DuskHorizon = new(0.98f, 0.66f, 0.42f);
    private static readonly Color NightSkyTop = new(0.04f, 0.07f, 0.17f), NightHorizon = new(0.12f, 0.17f, 0.30f);
    private static readonly Color SunNoon = new(1.0f, 0.95f, 0.86f), SunLow = new(1.0f, 0.66f, 0.40f);

    /// <summary>Hour the lighting currently shows (after rate settling), for tests.</summary>
    public float ShownHour { get; private set; } = 12f;

    /// <summary>0 at night, 1 in full day.</summary>
    public float Daylight { get; private set; } = 1f;

    public override void _Ready()
    {
        _sun = GetNodeOrNull<DirectionalLight3D>(SunPath);
        _moon = GetNodeOrNull<DirectionalLight3D>(MoonPath);
        _env = GetNodeOrNull<WorldEnvironment>(EnvironmentPath)?.Environment;
        _sky = _env?.Sky?.SkyMaterial as ProceduralSkyMaterial;
    }

    /// <summary>Call once per frame with the snapshot hour.</summary>
    public void Apply(float hourOfDay, double delta)
    {
        if (_lastHour >= 0 && delta > 0)
        {
            float step = hourOfDay - _lastHour;
            if (step < 0) step += 24f;
            float instant = step / (float)delta;
            _rate = Mathf.Lerp(_rate, instant, 1f - MathF.Exp(-2f * (float)delta));
        }
        _lastHour = hourOfDay;
        float settle = Mathf.SmoothStep(FullCycleHoursPerSecond, FrozenHoursPerSecond, _rate);
        float hour = LerpHour(hourOfDay, SettledHour, settle);
        ShownHour = hour;

        // Sun path: rises at 6, sets at 18, peaks at 58° elevation and swings across the sky during the day.
        float dayPhase = (hour - 6f) / 12f; // 0 sunrise, 1 sunset
        float elevation = MathF.Sin(dayPhase * MathF.PI) * 58f;
        float azimuth = Mathf.Lerp(-70f, 70f, Math.Clamp(dayPhase, 0f, 1f));
        float day = Mathf.SmoothStep(-6f, 12f, elevation);
        Daylight = day;
        float low = 1f - Mathf.SmoothStep(8f, 30f, elevation);

        if (_sun is not null)
        {
            // A light shines along its -Z: yaw near 0 lights the faces turned to the god camera (which looks along -Z);
            // the +35° offset keeps it from behind-left so shadows fall forward-right where the player sees them.
            _sun.Rotation = new Vector3(Mathf.DegToRad(-MathF.Max(elevation, 4f)), Mathf.DegToRad(azimuth * 0.6f + 35f), 0);
            _sun.LightColor = SunNoon.Lerp(SunLow, low);
            _sun.LightEnergy = SunEnergy * day;
            _sun.ShadowEnabled = day > 0.05f;
            _sun.Visible = day > 0.01f;
        }
        if (_moon is not null)
        {
            _moon.LightEnergy = MoonEnergy * (1f - day);
            _moon.Visible = day < 0.99f;
        }
        if (_sky is not null)
        {
            var top = NightSkyTop.Lerp(DaySkyTop, day).Lerp(DuskSkyTop, low * day * 0.6f);
            var horizon = NightHorizon.Lerp(DayHorizon, day).Lerp(DuskHorizon, low * day * 0.8f);
            _sky.SkyTopColor = top;
            _sky.SkyHorizonColor = horizon;
            _sky.GroundHorizonColor = horizon;
            _sky.GroundBottomColor = top.Darkened(0.4f);
        }
        if (_env is not null)
        {
            _env.AmbientLightEnergy = Mathf.Lerp(0.55f, 1.0f, day);
            _env.FogLightColor = NightHorizon.Lerp(DayHorizon, day).Lerp(DuskHorizon, low * day * 0.5f);
        }
    }

    private static float LerpHour(float from, float to, float t)
    {
        float d = to - from;
        if (d > 12f) d -= 24f;
        if (d < -12f) d += 24f;
        float h = from + d * t;
        return (h % 24f + 24f) % 24f;
    }
}
