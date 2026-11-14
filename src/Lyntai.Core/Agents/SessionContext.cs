namespace Lyntai.Agents;

/// <summary>What a <see cref="SessionContextEntry"/> is about. The five kinds are the ones a session carried from one
/// agent to the next needs; a host's own distinctions go in the entry's text.</summary>
public enum SessionContextKind
{
    /// <summary>What was asked: the host's words. Only the host writes it, and the head never cuts it.</summary>
    Work,

    /// <summary>A choice that was made, and why.</summary>
    Decided,

    /// <summary>Something that was learned, ideally with where (<see cref="SessionContextEntry.Subject"/>).</summary>
    Found,

    /// <summary>Where the work stands now: what is done and what is left.</summary>
    Standing,

    /// <summary>What the tree (the working files, however the host keeps them) holds right now.</summary>
    Tree,
}

/// <summary>Who wrote an entry. An agent's entry is a claim: every rendering shows it as one.</summary>
public enum SessionContextAuthor
{
    /// <summary>The host, in its own words.</summary>
    Host,

    /// <summary>An agent, read back from its turn or noted through a tool: unverified.</summary>
    Agent,
}

/// <summary>What a writer submits to <see cref="SessionContext.Append(SessionContextNote, SessionContextAuthor)"/>; the
/// context adds the author and, when none is given, the id.</summary>
/// <param name="Kind">What the entry is about.</param>
/// <param name="Text">The entry, in the writer's words. Not blank.</param>
public sealed record SessionContextNote(SessionContextKind Kind, string Text)
{
    /// <summary>The id a later entry may supersede. Null = the context names it (<c>e1</c>, <c>e2</c>, …). One id is one
    /// entry for the life of the context, with no whitespace or control character in it.</summary>
    public string? Id { get; init; }

    /// <summary>What the entry is about in a few words a reader can look up: a path and lines, a name. Null when there is none.</summary>
    public string? Subject { get; init; }

    /// <summary>The id of the live entry this one replaces. It must be of the same kind and not yet replaced, and an
    /// agent's entry replaces only an agent's. Null = it replaces none.</summary>
    public string? Supersedes { get; init; }
}

/// <summary>One entry of a <see cref="SessionContext"/>. Immutable, and never deleted: a later entry may supersede it,
/// and it then stays in the log, out of <see cref="SessionContext.Live"/>.</summary>
public sealed record SessionContextEntry
{
    /// <summary>The id, unique in the context.</summary>
    public required string Id { get; init; }

    /// <summary>What the entry is about.</summary>
    public required SessionContextKind Kind { get; init; }

    /// <summary>The entry, as its writer wrote it.</summary>
    public required string Text { get; init; }

    /// <summary>Who wrote it.</summary>
    public required SessionContextAuthor Author { get; init; }

    /// <summary>A few words saying what it is about; null when none.</summary>
    public string? Subject { get; init; }

    /// <summary>The id of the earlier entry this one replaces; null when it replaces none.</summary>
    public string? Supersedes { get; init; }
}

/// <summary>The context of a session, kept apart from any one agent: an append-only log of typed entries that
/// any provider's rendering (<see cref="IContextRendering"/>) can carry to its agent. Immutable — an append returns a
/// new context. <b>No model writes it and none rewrites it</b>: the host appends its own entries, and an agent's come
/// from the read-back of a turn (<see cref="IContextRendering.ReadBack"/>) or a tool the host exposes.
/// <para>An entry replaces an earlier one by id; the earlier stays in <see cref="Entries"/>, out of <see cref="Live"/>.
/// The log has no bound; each rendering has one. Where a context is stored is the host's, and
/// <see cref="FromEntries"/> rebuilds one from what it kept.</para></summary>
public sealed class SessionContext
{
    private readonly SessionContextEntry[] _entries;
    private readonly Dictionary<string, int> _index;
    private readonly HashSet<string> _superseded;

    private SessionContext(SessionContextEntry[] entries, Dictionary<string, int> index, HashSet<string> superseded)
    {
        _entries = entries;
        _index = index;
        _superseded = superseded;
    }

    /// <summary>A context with no entries.</summary>
    public static SessionContext Empty { get; } = new([], new Dictionary<string, int>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));

    /// <summary>Every entry, in the order it was appended — superseded ones included.</summary>
    public IReadOnlyList<SessionContextEntry> Entries => _entries;

    /// <summary>How many entries there are. A rendering reports it, and a host hands it back later as the
    /// position a conversation has been told up to (<see cref="ContextRenderRequest.Seen"/>).</summary>
    public int Count => _entries.Length;

    /// <summary>The entries no later entry supersedes, in the order they were appended.</summary>
    public IReadOnlyList<SessionContextEntry> Live => [.. _entries.Where(e => !_superseded.Contains(e.Id))];

    /// <summary>The entries a later one superseded, in the order they were appended.</summary>
    public IReadOnlyList<SessionContextEntry> Replaced => [.. _entries.Where(e => _superseded.Contains(e.Id))];

    /// <summary>The entry with this id, or null.</summary>
    public SessionContextEntry? Find(string id) => id is not null && _index.TryGetValue(id, out var at) ? _entries[at] : null;

    /// <summary>Whether a later entry supersedes <paramref name="id"/>.</summary>
    public bool IsSuperseded(string id) => id is not null && _superseded.Contains(id);

    /// <summary>The id of the entry that supersedes <paramref name="id"/>; null when none does.</summary>
    public string? SupersededBy(string id) =>
        id is null || !_superseded.Contains(id) ? null : _entries.First(e => string.Equals(e.Supersedes, id, StringComparison.Ordinal)).Id;

    /// <summary>The entries from position <paramref name="seen"/> on: what a conversation told up to there has not heard.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seen"/> is below 0 or above <see cref="Count"/>.</exception>
    public IReadOnlyList<SessionContextEntry> Since(int seen)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seen);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(seen, _entries.Length);
        return _entries[seen..];
    }

    /// <summary>Append one entry; the context returned has it last.</summary>
    /// <param name="note">What to append.</param>
    /// <param name="author">Who wrote it.</param>
    /// <exception cref="ArgumentException">The text is blank; the id is blank, holds whitespace or a control
    /// character, or is already used; the entry supersedes an id that is unknown, already superseded, of another kind or
    /// its own; an agent's entry is of kind <see cref="SessionContextKind.Work"/> or supersedes the host's.</exception>
    public SessionContext Append(SessionContextNote note, SessionContextAuthor author)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (string.IsNullOrWhiteSpace(note.Text)) throw new ArgumentException("An entry's text is blank.", nameof(note));
        if (author == SessionContextAuthor.Agent && note.Kind == SessionContextKind.Work)
            throw new ArgumentException("The work is the host's words: an agent's entry cannot be of that kind.", nameof(note));

        string id;
        if (note.Id is null)
        {
            var n = _entries.Length + 1;
            while (_index.ContainsKey("e" + n)) n++;
            id = "e" + n;
        }
        else
        {
            id = note.Id;
            if (id.Length == 0 || id.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
                throw new ArgumentException($"'{id}' is not an id: it is blank or holds whitespace or a control character.", nameof(note));
            if (_index.ContainsKey(id)) throw new ArgumentException($"The id '{id}' is already used.", nameof(note));
        }

        if (note.Supersedes is { } old)
        {
            if (!_index.TryGetValue(old, out var at)) throw new ArgumentException($"'{old}' supersedes an entry that does not exist.", nameof(note));
            var target = _entries[at];
            if (_superseded.Contains(old)) throw new ArgumentException($"'{old}' is already superseded.", nameof(note));
            if (target.Kind != note.Kind) throw new ArgumentException($"'{old}' is of kind {target.Kind}; an entry replaces only its own kind.", nameof(note));
            if (author == SessionContextAuthor.Agent && target.Author == SessionContextAuthor.Host)
                throw new ArgumentException($"'{old}' is the host's: an agent's entry cannot replace it.", nameof(note));
        }

        var entry = new SessionContextEntry
        {
            Id = id, Kind = note.Kind, Text = note.Text, Author = author, Subject = note.Subject, Supersedes = note.Supersedes,
        };
        var entries = new SessionContextEntry[_entries.Length + 1];
        _entries.CopyTo(entries, 0);
        entries[^1] = entry;
        var index = new Dictionary<string, int>(_index, StringComparer.Ordinal) { [id] = _entries.Length };
        var superseded = new HashSet<string>(_superseded, StringComparer.Ordinal);
        if (note.Supersedes is not null) superseded.Add(note.Supersedes);
        return new SessionContext(entries, index, superseded);
    }

    /// <summary>Rebuild a context from the entries a host kept, in order, by appending each: a stored record that breaks a
    /// rule of the log is refused rather than read.</summary>
    /// <exception cref="ArgumentException">An entry breaks a rule of <see cref="Append"/>.</exception>
    public static SessionContext FromEntries(IEnumerable<SessionContextEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var context = Empty;
        foreach (var e in entries)
            context = context.Append(new SessionContextNote(e.Kind, e.Text) { Id = e.Id, Subject = e.Subject, Supersedes = e.Supersedes }, e.Author);
        return context;
    }
}
