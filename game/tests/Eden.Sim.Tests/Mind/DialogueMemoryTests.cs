using Eden.Sim.Contracts;

namespace Eden.Sim.Tests.Mind;

/// <summary>The sim side of dialogue: what the god said is an importance-9 memory and spreads by the existing gossip.</summary>
public class DialogueMemoryTests
{
    [Fact]
    public void Gods_words_spread_by_gossip_when_the_npc_talks_to_a_neighbour()
    {
        var w = new World(new SimConfig { Seed = 12 });
        int a = w.Npcs.IdOf(0), b = w.Npcs.IdOf(1);
        w.Apply(new WhisperCommand(a, "Your hill will burn again."));
        w.Apply(new BeliefIntention(a, w.Tick, IntentionSource.Llm, "The Voice warned me about the hill.", 8));
        w.Npcs.X[1] = w.Npcs.X[0];
        w.Npcs.Z[1] = w.Npcs.Z[0];
        w.Apply(new PossessCommand(a));
        for (int i = 0; i < 40 && !w.Memories.Of(1).Any(m => m.Kind == MemoryKind.Gossip && m.Text.Contains("hill")); i++)
            w.Apply(new PossessedInteractCommand(b, "talk"));
        Assert.Contains(w.Memories.Of(1), m => m.Kind == MemoryKind.Gossip && m.Text.Contains("hill") && m.Text.StartsWith(w.Npcs.Name[0]));
    }
}
