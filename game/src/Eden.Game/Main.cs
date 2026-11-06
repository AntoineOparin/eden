using Eden.Sim;
using Eden.Sim.Contracts;
using Godot;

namespace Eden.Game;

/// <summary>Root node of scenes/main.tscn. Owns the sim host; everything else reads Latest from here.</summary>
public partial class Main : Node
{
    [Export] public int Seed { get; set; } = 1;

    public ISimHost? Host { get; private set; }

    public override void _Ready()
    {
        var host = new SimHost(new SimConfig { Seed = Seed });
        host.Start();
        Host = host;
        var s = host.Latest;
        GD.Print($"Eden boot ok: seed {s.World.Seed}, world {s.World.Width}x{s.World.Height}, tick {s.Tick}, population {s.Population}");
    }

    public override void _ExitTree()
    {
        Host?.Dispose();
        Host = null;
    }
}
