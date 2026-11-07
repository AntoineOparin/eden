using Eden.Mind;
using Eden.Sim.Contracts;

namespace Eden.Sim.Tests.Mind;

public class RetrievalTests
{
    [Fact]
    public void Relevant_old_memory_beats_irrelevant_recent_one()
    {
        const int tph = 10;
        long now = 200 * tph; // 200 in-game hours
        var old = new MemoryView(0, "god", "Lightning struck the hill.", 5);
        var recent = new MemoryView(now - tph, "gift", "Adam gave me berries.", 5);
        var cache = new EmbeddingCache();
        cache.Put(old.Text, [1f, 0f, 0f]);
        cache.Put(recent.Text, [0f, 1f, 0f]);
        float[] query = [0.95f, 0.1f, 0f]; // "why do you fear the hill?"

        var top = Retrieval.Top([old, recent], now, tph, Retrieval.Relevance("why do you fear the hill?", query, cache), k: 1);
        Assert.Equal(old, Assert.Single(top));

        // Without relevance the recent one wins: recency alone decides between equal importances.
        var plain = Retrieval.Top([old, recent], now, tph, _ => 0, k: 1);
        Assert.Equal(recent, Assert.Single(plain));
    }

    [Fact]
    public void Score_is_recency_plus_importance_plus_relevance()
    {
        Assert.Equal(1 + 0.7 + 0.5, Retrieval.Score(0, 7, 0.5), 9);
        Assert.Equal(Math.Pow(0.995, 100) + 1, Retrieval.Score(100, 10, 0), 9);
    }

    [Fact]
    public void Returns_at_most_twelve_and_skips_the_summary_line()
    {
        var mems = Enumerable.Range(0, 30).Select(i => new MemoryView(i, "speech", $"line {i}", 3)).Prepend(new MemoryView(0, "summary", "Faded memories: 3 gossip.", 10)).ToList();
        var top = Retrieval.Top(mems, 30, 10, _ => 0);
        Assert.Equal(12, top.Count);
        Assert.DoesNotContain(top, m => m.Kind == "summary");
        Assert.Equal(29, top[0].Tick);
    }

    [Fact]
    public void Missing_vectors_fall_back_to_word_overlap()
    {
        var cache = new EmbeddingCache();
        var rel = Retrieval.Relevance("the lightning on the hill", null, cache);
        Assert.True(rel(new MemoryView(0, "god", "Lightning struck the hill.", 5)) > rel(new MemoryView(0, "gift", "Adam gave me berries.", 5)));
        Assert.Equal(1, Retrieval.Cosine([1, 2, 3], [2, 4, 6]), 6);
        Assert.Equal(["a b c", "d e f"], cache.Missing(["a b c", "d e f", "a b c", " "]));
    }
}
