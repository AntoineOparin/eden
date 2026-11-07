// Contract: the command side of the sim boundary. Godot (Eden.Game) and the Mind (Eden.Mind)
// talk to the simulation only through this interface; the simulation runs on its own thread.
// Source of truth: docs/specs/contracts/ISimHost.cs (linked into Eden.Sim).

using System;

namespace Eden.Sim.Contracts;

/// <summary>Multiplier applied to in-game time. Paused = 0.</summary>
public enum TimeScale { Paused = 0, X1 = 1, X10 = 10, X100 = 100, X500 = 500 }

/// <summary>Everything the player (as god or possessed) can do to the world. Executed on the sim thread in order.</summary>
public abstract record WorldCommand;

public sealed record SetTimeScaleCommand(TimeScale Scale) : WorldCommand;

public sealed record SpawnCommand(string Kind, float X, float Z, int Count = 1) : WorldCommand; // kind: human|animal:<species>|plant:<species>

public sealed record LightningCommand(float X, float Z, float Radius) : WorldCommand;

public sealed record WeatherCommand(string Kind, int DurationTicks) : WorldCommand; // rain|drought|clear

public sealed record TerraformCommand(float X, float Z, float Radius, float DeltaHeight) : WorldCommand;

public sealed record BlessCommand(int TargetId, bool Curse, int DurationTicks) : WorldCommand;

public sealed record DropItemCommand(string ItemId, float X, float Z, int Count = 1) : WorldCommand;

/// <summary>Whisper to one NPC; becomes an importance-9 memory attributed to the god.</summary>
public sealed record WhisperCommand(int NpcId, string Text) : WorldCommand;

public sealed record PossessCommand(int NpcId) : WorldCommand;

public sealed record ReleasePossessionCommand : WorldCommand;

/// <summary>Movement input for the possessed NPC, applied each tick while possessed.</summary>
public sealed record PossessedMoveCommand(float DirX, float DirZ, bool Sprint) : WorldCommand;

/// <summary>Possessed-NPC interaction with another entity: talk|give:<itemId>|work.</summary>
public sealed record PossessedInteractCommand(int TargetId, string Verb) : WorldCommand;

/// <summary>
/// Sent by Eden.Mind (M5): replaces the set of NPCs flagged <see cref="NpcView.IsThinking"/> (the spotlight).
/// Display only: it never changes behaviour, the state hash or the save file. Unknown or dead ids are ignored.
/// </summary>
public sealed record SetThinkingCommand(System.Collections.Generic.IReadOnlyList<int> NpcIds) : WorldCommand;

public sealed record SaveCommand(string Path) : WorldCommand;

public sealed record LoadCommand(string Path) : WorldCommand;

/// <summary>Owns the sim thread. Implemented in Eden.Sim; constructed once by Eden.Game.</summary>
public interface ISimHost : IDisposable
{
    /// <summary>Most recent published snapshot. Never null after Start; reading it never blocks.</summary>
    IWorldSnapshot Latest { get; }

    /// <summary>Intentions from Eden.Mind land here.</summary>
    IIntentionSink Intentions { get; }

    /// <summary>Queue a command; executed in order on the sim thread. Never blocks.</summary>
    void Enqueue(WorldCommand command);

    void Start();

    void Stop();

    /// <summary>Stable hash of the full sim state, used by determinism tests and save verification.</summary>
    ulong StateHash();
}
