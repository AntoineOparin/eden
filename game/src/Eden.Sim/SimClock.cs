namespace Eden.Sim;

/// <summary>Converts a tick count into calendar values. Pure.</summary>
public readonly record struct SimClock(long Tick, SimConfig Config)
{
    public int Year => (int)(Tick / Config.TicksPerYear);
    public int DayOfYear => (int)(Tick % Config.TicksPerYear / Config.TicksPerDay);
    public float HourOfDay => (float)(Tick % Config.TicksPerDay) / Config.TicksPerHour;
    public bool IsHourStart => Tick % Config.TicksPerHour == 0;
    public bool IsDayStart => Tick % Config.TicksPerDay == 0;
    public bool IsYearStart => Tick % Config.TicksPerYear == 0;
}
