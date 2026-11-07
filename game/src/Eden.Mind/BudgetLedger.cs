namespace Eden.Mind;

/// <summary>
/// Daily spend, tracked locally (the Worker is stateless and only reports per-request cost). Neurons come from
/// <c>usage.neurons</c> / <c>X-Eden-Neurons-Cost</c>; USD from <c>X-Eden-Usd-Cost</c> or the direct providers'
/// own pricing table. Resets at local midnight. Thread-safe. Eden.Game persists it with <see cref="Snapshot"/> /
/// <see cref="Restore"/>.
/// </summary>
public sealed class BudgetLedger
{
    private readonly object _gate = new();
    private readonly Func<DateTime> _localNow;
    private DateOnly _day;
    private double _neurons, _usd;

    public BudgetLedger(Func<DateTime>? localNow = null)
    {
        _localNow = localNow ?? (() => DateTime.Now);
        _day = DateOnly.FromDateTime(_localNow());
    }

    /// <summary>Raised after every change (from whichever thread made it).</summary>
    public event Action? Changed;

    public double NeuronsToday { get { lock (_gate) { Roll(); return _neurons; } } }
    public double UsdToday { get { lock (_gate) { Roll(); return _usd; } } }

    public void AddNeurons(double neurons)
    {
        if (!(neurons > 0) || double.IsInfinity(neurons)) return;
        lock (_gate) { Roll(); _neurons += neurons; }
        Changed?.Invoke();
    }

    public void AddUsd(double usd)
    {
        if (!(usd > 0) || double.IsInfinity(usd)) return;
        lock (_gate) { Roll(); _usd += usd; }
        Changed?.Invoke();
    }

    public (DateOnly Day, double Neurons, double Usd) Snapshot()
    {
        lock (_gate) { Roll(); return (_day, _neurons, _usd); }
    }

    /// <summary>Restores a persisted day; ignored unless it is today.</summary>
    public void Restore(DateOnly day, double neurons, double usd)
    {
        lock (_gate)
        {
            Roll();
            if (day != _day) return;
            _neurons = Math.Max(_neurons, Math.Max(0, neurons));
            _usd = Math.Max(_usd, Math.Max(0, usd));
        }
        Changed?.Invoke();
    }

    private void Roll()
    {
        var today = DateOnly.FromDateTime(_localNow());
        if (today == _day) return;
        _day = today;
        _neurons = 0;
        _usd = 0;
    }
}
