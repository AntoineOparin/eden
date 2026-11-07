using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>Weather the client shows. Mirrors the sim's three kinds (rain, drought, clear).</summary>
public enum SkyWeather { Clear, Rain, Drought }

/// <summary>
/// What the sky should look like (A2). The sim keeps its weather inside <c>World</c> and the snapshot contract has no
/// field for it (backlog row), so the client reads it from what it can see: the god's own casts (<see cref="Begin"/>,
/// from <see cref="PowerFx.WeatherPlayed"/>, ending after the cast's duration in ticks) and the sim's Chronicle lines
/// ("The god sent rain.", "…a drought began.", "The skies cleared.", "The god cleared the skies."). The clouds and the
/// power effects' screen tint both take their colours from here, so they agree.
/// </summary>
public sealed class WeatherState
{
    public SkyWeather Kind { get; private set; } = SkyWeather.Clear;

    /// <summary>Tick at which the current rain or drought is expected to end (-1: until the sim says so).</summary>
    public long UntilTick { get; private set; } = -1;

    /// <summary>Screen tint the power effects flash for a weather cast (shared so clouds and tint agree).</summary>
    public static Color TintOf(SkyWeather k) => k switch
    {
        SkyWeather.Rain => new Color(0.22f, 0.32f, 0.52f, 0.24f),
        SkyWeather.Drought => new Color(0.70f, 0.42f, 0.12f, 0.20f),
        _ => new Color(0, 0, 0, 0),
    };

    /// <summary>Fraction of the cloud set shown: rain thickens the sky, drought clears it.</summary>
    public static float CoverageOf(SkyWeather k) => k switch
    {
        SkyWeather.Rain => 1f,
        SkyWeather.Drought => 0.12f,
        _ => 0.6f,
    };

    /// <summary>Cloud colour multiplier: rain clouds are slate grey, drought's few clouds warm and thin.</summary>
    public static Color CloudTintOf(SkyWeather k) => k switch
    {
        SkyWeather.Rain => new Color(0.46f, 0.50f, 0.58f),
        SkyWeather.Drought => new Color(1.0f, 0.93f, 0.82f),
        _ => new Color(1f, 1f, 1f),
    };

    /// <summary>A rain or drought starts now (the god's cast) and lasts until <paramref name="untilTick"/>.</summary>
    public void Begin(SkyWeather kind, long untilTick)
    {
        Kind = kind;
        UntilTick = kind == SkyWeather.Clear ? -1 : untilTick;
    }

    private long _lastChronicleTick = -1;

    /// <summary>Reads this frame's snapshot: Chronicle weather lines and the expiry tick. Call once per frame.</summary>
    public void Observe(IWorldSnapshot s)
    {
        var news = s.NewChronicle;
        for (int i = 0; i < news.Count; i++)
        {
            var e = news[i];
            if (e.Tick < _lastChronicleTick) continue;
            var k = FromText(e.Text);
            if (k is null) continue;
            _lastChronicleTick = e.Tick;
            if (k == SkyWeather.Clear) Begin(SkyWeather.Clear, -1);
            else if (Kind != k) Begin(k.Value, UntilTick > e.Tick ? UntilTick : -1);
        }
        if (Kind != SkyWeather.Clear && UntilTick >= 0 && s.Tick >= UntilTick) Begin(SkyWeather.Clear, -1);
    }

    /// <summary>The weather a Chronicle line announces, or null. Matches the sim's and FakeSimHost's wording.</summary>
    public static SkyWeather? FromText(string text)
    {
        if (text.Contains("sent rain", StringComparison.Ordinal)) return SkyWeather.Rain;
        if (text.Contains("drought began", StringComparison.Ordinal)) return SkyWeather.Drought;
        if (text.Contains("skies cleared", StringComparison.Ordinal) || text.Contains("cleared the skies", StringComparison.Ordinal)) return SkyWeather.Clear;
        return null;
    }
}
