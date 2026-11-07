using Eden.Sim.Contracts;

namespace Eden.Sim;

/// <summary>
/// C1: the Chronicle history (up to <see cref="ChronicleHistorySize"/> entries) saved as its own tagged section
/// <c>CHRN</c>, written after the body sections (same framing as World.Persistence.cs, found with SectionsOf) and
/// read back after <see cref="ReadBody"/>, which keeps unknown tags and ignores them. Self-contained so the save
/// format work in SaveWriter / SaveReader only carries one registration line each. Not part of the state hash: the
/// Chronicle never influences a tick. "load" notices are session-only and are not written, so saving a freshly
/// loaded world gives the same bytes. A save without the section (before C1) leaves the history as it was.
/// </summary>
public sealed partial class World
{
    internal const string ChronicleSectionTag = "CHRN";

    internal void WriteChronicleSection(BinaryWriter w) => Section(w, ChronicleSectionTag, bw =>
    {
        var entries = ChronicleTail(_historyCount);
        int n = 0;
        foreach (var e in entries) if (e.Kind != "load") n++;
        bw.Write(n);
        foreach (var e in entries)
        {
            if (e.Kind == "load") continue;
            bw.Write(e.Tick);
            bw.Write(e.Kind);
            bw.Write(e.Text);
            bw.Write(e.Importance);
            bw.Write(e.Subjects.Count);
            foreach (int id in e.Subjects) bw.Write(id);
        }
    });

    /// <summary>
    /// Scans the save body for the <c>CHRN</c> section and, if present, replaces the history with it, keeping the
    /// "load" notice <see cref="ReadBody"/> just added at the end. Entries restored here are history, not news: they
    /// are not re-published in <see cref="IWorldSnapshot.NewChronicle"/> (readers use ChronicleSince).
    /// </summary>
    internal void ReadChronicleSection(byte[] body, int formatVersion = SaveWriter.FormatVersion)
    {
        // M8 framing: sections are found through World.SectionsOf (handles the v2 flags byte and Brotli).
        if (!SectionsOf(body, formatVersion).TryGetValue(ChronicleSectionTag, out var found)) return;
        byte[] section = found.body;

        using var br = new BinaryReader(new MemoryStream(section), System.Text.Encoding.UTF8);
        int n = br.ReadInt32();
        if (n < 0) throw new InvalidDataException("bad chronicle section");
        var restored = new List<ChronicleEntry>(Math.Min(n, ChronicleHistorySize));
        for (int i = 0; i < n; i++)
        {
            long tick = br.ReadInt64();
            string kind = br.ReadString(), text = br.ReadString();
            int importance = br.ReadInt32();
            int m = br.ReadInt32();
            if (m < 0 || m > 1 << 16) throw new InvalidDataException("bad chronicle section");
            var subjects = new int[m];
            for (int j = 0; j < m; j++) subjects[j] = br.ReadInt32();
            restored.Add(new ChronicleEntry(tick, kind, text, importance, subjects));
        }

        ChronicleEntry? notice = _historyCount > 0 ? _history[(_historyNext - 1 + _history.Length) % _history.Length] : null;
        if (notice is not null && notice.Kind != "load") notice = null;
        Array.Clear(_history);
        _historyCount = _historyNext = 0;
        int skip = Math.Max(0, restored.Count - (ChronicleHistorySize - (notice is null ? 0 : 1)));
        for (int i = skip; i < restored.Count; i++) PushHistory(restored[i]);
        if (notice is not null) PushHistory(notice);
        _publishedHistory = null;
    }

    private void PushHistory(ChronicleEntry e)
    {
        _history[_historyNext] = e;
        _historyNext = (_historyNext + 1) % _history.Length;
        if (_historyCount < _history.Length) _historyCount++;
    }
}
