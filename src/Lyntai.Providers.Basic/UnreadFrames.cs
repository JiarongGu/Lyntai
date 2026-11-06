using Lyntai.Agents;

namespace Lyntai.Providers.Basic;

/// <summary>What a reader read nothing from, kept bounded: for each frame type and subtype no reader case took, a count
/// and the first frame as written, cut to <see cref="MaxSample"/>. Past <see cref="MaxEntries"/> kinds a further new kind
/// is dropped whole, and the kinds already listed keep counting. It adds no event and is not a log.</summary>
internal sealed class UnreadFrames
{
    internal const int MaxEntries = 16;
    internal const int MaxSample = 512;

    private readonly object _gate = new();
    private readonly List<(string Type, string? Subtype, int Count, string Sample)> _entries = [];

    public void Note(string type, string? subtype, string line)
    {
        lock (_gate)
        {
            var i = _entries.FindIndex(e => e.Type == type && e.Subtype == subtype);
            if (i >= 0)
            {
                var e = _entries[i];
                _entries[i] = e with { Count = e.Count + 1 };
            }
            else if (_entries.Count < MaxEntries)
                _entries.Add((type, subtype, 1, Cut(line)));
        }
    }

    public IReadOnlyList<AgentUnreadFrame> Snapshot()
    {
        lock (_gate) return [.. _entries.Select(e => new AgentUnreadFrame(e.Type, e.Subtype, e.Count, e.Sample))];
    }

    // a surrogate pair is never split
    private static string Cut(string line) =>
        line.Length <= MaxSample ? line : line[..(char.IsHighSurrogate(line[MaxSample - 1]) ? MaxSample - 1 : MaxSample)];
}
