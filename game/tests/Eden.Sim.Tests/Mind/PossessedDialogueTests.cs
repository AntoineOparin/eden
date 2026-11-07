using Eden.Mind;
using Eden.Mind.Contracts;
using Eden.Sim.Contracts;

namespace Eden.Sim.Tests.Mind;

/// <summary>M6: talking while possessed goes to /chat as speaker "possessed" and is spoken aloud by the possessed NPC.</summary>
[Collection(nameof(MindPoolCollection))]
public class PossessedDialogueTests
{
    private sealed class RecordingProvider : IBrainProvider
    {
        public ChatRequest? LastChat;
        public string Id => "fake";
        public bool SupportsBatching => false;

        public Task<ThinkResult> ThinkAsync(ThinkRequest request, CancellationToken ct)
            => Task.FromException<ThinkResult>(new BrainException(BrainFailure.Unsupported, "no thoughts here"));

        public Task<ChatResult> ChatAsync(ChatRequest request, CancellationToken ct)
        {
            LastChat = request;
            return Task.FromResult(new ChatResult(new ChatResponse("You look pale, friend.", null, "suspicious"), new BrainUsage(300, 20, 4, "fake")));
        }

        public Task<EmbedResult> EmbedAsync(EmbedRequest request, CancellationToken ct)
            => Task.FromException<EmbedResult>(new BrainException(BrainFailure.Unsupported, "no embeddings"));
    }

    [Fact]
    public async Task Possessed_speaker_is_named_and_heard_as_speech_not_as_the_god()
    {
        using var host = new SimHost(new SimConfig { Seed = 12 });
        var first = host.Latest.Npcs.Span[0];
        for (int i = 0; i < 8 && host.Latest.Population < 3; i++)
        {
            host.Enqueue(new SpawnCommand("human", first.X + 3 + i, first.Z, 1));
            host.StepSync(1);
        }
        var views = host.Latest.Npcs.ToArray();
        int speaker = views[0].Id;
        int listener = views.OrderBy(v => (v.X - views[0].X) * (v.X - views[0].X) + (v.Z - views[0].Z) * (v.Z - views[0].Z)).Skip(1).First().Id;
        host.Enqueue(new PossessCommand(speaker));
        host.Latest.TryGetNpcDetail(listener, out _);
        host.Latest.TryGetNpcDetail(speaker, out _);
        host.StepSync(1);
        Assert.True(host.Latest.TryGetNpcDetail(speaker, out var speakerDetail));

        var provider = new RecordingProvider();
        var pool = new MindPool(host, BrainDefaults.Settings, (_, _) => provider);
        var reply = await pool.AskAsPossessedAsync(listener, speaker, speakerDetail.Name, "Do you know the way to the river?");

        Assert.Equal("You look pale, friend.", reply.Reply);
        Assert.NotNull(provider.LastChat);
        Assert.Equal(Speaker.Possessed, provider.LastChat!.Speaker);
        Assert.Equal(speakerDetail.Name, provider.LastChat.SpeakerName);
        Assert.Equal([Speaker.Possessed, Speaker.Npc], pool.History(listener).Select(t => t.Speaker).ToArray());

        host.StepSync(1);
        Assert.True(host.Latest.TryGetNpcDetail(listener, out var d));
        Assert.Contains(d.Memories, m => m.Kind == "speech" && m.Text == $"{speakerDetail.Name} said: \"Do you know the way to the river?\"");
        Assert.DoesNotContain(d.Memories, m => m.Text.Contains("A voice from the sky", StringComparison.Ordinal));
        Assert.True(host.Latest.TryGetNpcDetail(speaker, out var s));
        Assert.Contains(s.Memories, m => m.Kind == "speech" && m.Text.Contains("You look pale, friend."));
        Assert.DoesNotContain(host.Diagnostics, x => x.Contains("rejected", StringComparison.Ordinal));
    }
}
