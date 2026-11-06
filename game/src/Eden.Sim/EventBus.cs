namespace Eden.Sim;

/// <summary>
/// Something happened in the world. Witnesses within <see cref="WitnessRadius"/> of (X, Z) and every
/// living subject remember it; <see cref="ChronicleKind"/> non-null also writes a Chronicle entry.
/// </summary>
public readonly record struct SimEvent(
    long Tick,
    MemoryKind Kind,
    string Text,
    int Importance,
    int[] Subjects,
    float X,
    float Z,
    float WitnessRadius,
    string? ChronicleKind = null,
    int ChronicleImportance = 0);

/// <summary>In-sim synchronous event bus: feeds the Chronicle, the memory recorder and (M3) settlement logic.</summary>
public sealed class EventBus
{
    private readonly List<Action<SimEvent>> _handlers = new();

    public void Subscribe(Action<SimEvent> handler) => _handlers.Add(handler);

    public void Publish(in SimEvent e)
    {
        for (int i = 0; i < _handlers.Count; i++) _handlers[i](e);
    }
}
