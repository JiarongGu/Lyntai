using Lyntai.Agents;

namespace Lyntai.Providers.Basic;

/// <summary>What a reader read nothing from, kept bounded: for each frame type and subtype no reader case took, a count
/// and the first frame as written, cut to <see cref="MaxSample"/>; the type and subtype are cut to <see cref="MaxKind"/>.
/// Past <see cref="MaxEntries"/> kinds a further new kind is dropped whole, and the kinds already listed keep counting.
/// It adds no event and is not a log.</summary>
internal sealed class UnreadFrames
{
    internal const int MaxEntries = 16;
    internal const int MaxSample = 512;
    internal const int MaxKind = 128;

    private readonly object _gate = new();
    private readonly List<(string Type, string? Subtype, int Count, string Sample)> _entries = [];

    public void Note(string type, string? subtype, string line) =>
        Add(Cut(type, MaxKind), subtype is null ? null : Cut(subtype, MaxKind), 1, line);

    /// <summary>Hand what is held to <paramref name="target"/> and forget it: the frames a run met while no turn ran belong to
    /// the turn that begins next.</summary>
    public void MoveTo(UnreadFrames target)
    {
        List<(string Type, string? Subtype, int Count, string Sample)> moved;
        lock (_gate)
        {
            moved = [.. _entries];
            _entries.Clear();
        }

        foreach (var e in moved) target.Add(e.Type, e.Subtype, e.Count, e.Sample);
    }

    public IReadOnlyList<AgentUnreadFrame> Snapshot()
    {
        lock (_gate) return [.. _entries.Select(e => new AgentUnreadFrame(e.Type, e.Subtype, e.Count, e.Sample))];
    }

    private void Add(string type, string? subtype, int count, string sample)
    {
        lock (_gate)
        {
            var i = _entries.FindIndex(e => e.Type == type && e.Subtype == subtype);
            if (i >= 0)
            {
                var e = _entries[i];
                _entries[i] = e with { Count = e.Count + count };
            }
            else if (_entries.Count < MaxEntries)
                _entries.Add((type, subtype, count, Cut(sample, MaxSample)));
        }
    }

    // a surrogate pair is never split
    private static string Cut(string text, int max) =>
        text.Length <= max ? text : text[..(char.IsHighSurrogate(text[max - 1]) ? max - 1 : max)];
}
