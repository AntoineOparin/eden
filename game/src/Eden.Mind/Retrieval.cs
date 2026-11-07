using System.Collections.Concurrent;
using Eden.Sim.Contracts;

namespace Eden.Mind;

/// <summary>
/// Generative Agents memory retrieval: <c>score = α·recency + β·importance + γ·relevance</c> with
/// recency = 0.995^hoursSince, importance = /10, relevance = cosine(query, memory); α = β = γ = 1.
/// Brute force over at most <see cref="MaxCandidates"/> memories. When a vector is missing (provider without
/// embeddings, not fetched yet) relevance falls back to word overlap so retrieval still works.
/// </summary>
public static class Retrieval
{
    public const int TopK = 12;
    public const int MaxCandidates = 500;
    public const double Alpha = 1, Beta = 1, Gamma = 1;

    public static double Score(double hoursSince, int importance, double relevance)
        => Alpha * Math.Pow(0.995, Math.Max(0, hoursSince)) + Beta * importance / 10.0 + Gamma * relevance;

    /// <summary>The top <paramref name="k"/> memories, best first. "summary" lines (faded memories) are skipped.</summary>
    public static IReadOnlyList<MemoryView> Top(IReadOnlyList<MemoryView> memories, long nowTick, int ticksPerHour,
        Func<MemoryView, double> relevance, int k = TopK)
    {
        var scored = new List<(double Score, MemoryView Memory)>(Math.Min(memories.Count, MaxCandidates));
        // Newest first so the candidate cap drops the oldest.
        for (int i = memories.Count - 1; i >= 0 && scored.Count < MaxCandidates; i--)
        {
            var m = memories[i];
            if (m.Kind == "summary" || string.IsNullOrWhiteSpace(m.Text)) continue;
            double hours = (nowTick - m.Tick) / (double)Math.Max(1, ticksPerHour);
            scored.Add((Score(hours, m.Importance, relevance(m)), m));
        }
        scored.Sort((a, b) => b.Score != a.Score ? b.Score.CompareTo(a.Score) : b.Memory.Tick.CompareTo(a.Memory.Tick));
        var result = new MemoryView[Math.Min(k, scored.Count)];
        for (int i = 0; i < result.Length; i++) result[i] = scored[i].Memory;
        return result;
    }

    /// <summary>Relevance from cached vectors when both exist, word overlap otherwise.</summary>
    public static Func<MemoryView, double> Relevance(string query, float[]? queryVector, EmbeddingCache cache)
        => m => queryVector is not null && cache.TryGet(m.Text, out var v) ? Cosine(queryVector, v) : Lexical(query, m.Text);

    public static double Cosine(float[] a, float[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        double dot = 0, na = 0, nb = 0;
        for (int i = 0; i < n; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na <= 0 || nb <= 0 ? 0 : dot / Math.Sqrt(na * nb);
    }

    /// <summary>Jaccard overlap of lower-cased words of 3+ letters; 0..1.</summary>
    public static double Lexical(string a, string b)
    {
        var wa = Words(a);
        if (wa.Count == 0) return 0;
        var wb = Words(b);
        if (wb.Count == 0) return 0;
        int inter = 0;
        foreach (var w in wa) if (wb.Contains(w)) inter++;
        return inter / (double)(wa.Count + wb.Count - inter);
    }

    private static HashSet<string> Words(string s)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        int start = -1;
        for (int i = 0; i <= s.Length; i++)
        {
            bool letter = i < s.Length && char.IsLetter(s[i]);
            if (letter && start < 0) start = i;
            else if (!letter && start >= 0)
            {
                if (i - start >= 3) set.Add(s[start..i].ToLowerInvariant());
                start = -1;
            }
        }
        return set;
    }
}

/// <summary>
/// Memory embeddings, cached per memory text (shared memories, e.g. a lightning strike witnessed by a whole
/// village, share one vector). Lives in the Mind only: embeddings never reach sim state, so they cannot affect
/// determinism. Bounded; cleared wholesale when full.
/// </summary>
public sealed class EmbeddingCache(int capacity = 20_000)
{
    private readonly ConcurrentDictionary<string, float[]> _vectors = new(StringComparer.Ordinal);

    public int Count => _vectors.Count;

    public bool TryGet(string text, out float[] vector) => _vectors.TryGetValue(text, out vector!);

    public void Put(string text, float[] vector)
    {
        if (vector.Length == 0) return;
        if (_vectors.Count >= capacity) _vectors.Clear();
        _vectors[text] = vector;
    }

    /// <summary>Distinct texts without a vector, in input order.</summary>
    public List<string> Missing(IEnumerable<string> texts)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<string>();
        foreach (var t in texts)
            if (!string.IsNullOrWhiteSpace(t) && !_vectors.ContainsKey(t) && seen.Add(t)) list.Add(t);
        return list;
    }
}
