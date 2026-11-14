using Lyntai.Agents;

namespace Lyntai.Tests.Agents;

/// <summary>The log of typed entries: append-only, an id a later entry may supersede, nothing deleted, and the rules that
/// keep the host's words out of an agent's reach. Pure; no model, no clock, no I/O.</summary>
public class SessionContextTests
{
    private static SessionContextNote Note(SessionContextKind kind, string text, string? id = null, string? supersedes = null) =>
        new(kind, text) { Id = id, Supersedes = supersedes };

    [Fact]
    public void An_append_returns_a_new_context_and_leaves_the_old_one_alone()
    {
        var empty = SessionContext.Empty;
        var one = empty.Append(Note(SessionContextKind.Work, "Fix the login test."), SessionContextAuthor.Host);

        Assert.Equal(0, empty.Count);
        Assert.Equal(1, one.Count);
        var entry = Assert.Single(one.Entries);
        Assert.Equal(("e1", SessionContextKind.Work, "Fix the login test.", SessionContextAuthor.Host), (entry.Id, entry.Kind, entry.Text, entry.Author));
    }

    [Fact]
    public void Ids_the_context_names_skip_one_a_writer_took()
    {
        var c = SessionContext.Empty
            .Append(Note(SessionContextKind.Found, "a", id: "e2"), SessionContextAuthor.Host)
            .Append(Note(SessionContextKind.Found, "b"), SessionContextAuthor.Host)
            .Append(Note(SessionContextKind.Found, "c"), SessionContextAuthor.Host);

        Assert.Equal(["e2", "e3", "e4"], c.Entries.Select(e => e.Id));
    }

    [Fact]
    public void A_superseded_entry_stays_in_the_log_and_leaves_the_live_set()
    {
        var c = SessionContext.Empty
            .Append(Note(SessionContextKind.Decided, "Use SQLite.", id: "d1"), SessionContextAuthor.Host)
            .Append(Note(SessionContextKind.Standing, "Started."), SessionContextAuthor.Host)
            .Append(Note(SessionContextKind.Decided, "Use Postgres.", id: "d2", supersedes: "d1"), SessionContextAuthor.Host);

        Assert.Equal(3, c.Count);
        Assert.Equal(["e2", "d2"], c.Live.Select(e => e.Id));
        Assert.Equal(["d1"], c.Replaced.Select(e => e.Id));
        Assert.True(c.IsSuperseded("d1"));
        Assert.False(c.IsSuperseded("d2"));
        Assert.Equal("d2", c.SupersededBy("d1"));
        Assert.Null(c.SupersededBy("d2"));
        Assert.Equal("Use SQLite.", c.Find("d1")!.Text);
        Assert.Null(c.Find("nope"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public void A_blank_text_is_refused(string text) =>
        Assert.Throws<ArgumentException>(() => SessionContext.Empty.Append(Note(SessionContextKind.Found, text), SessionContextAuthor.Host));

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a\tb")]
    public void An_id_with_whitespace_or_none_is_refused(string id) =>
        Assert.Throws<ArgumentException>(() => SessionContext.Empty.Append(Note(SessionContextKind.Found, "x", id: id), SessionContextAuthor.Host));

    [Fact]
    public void A_repeated_id_is_refused()
    {
        var c = SessionContext.Empty.Append(Note(SessionContextKind.Found, "x", id: "f1"), SessionContextAuthor.Host);
        Assert.Throws<ArgumentException>(() => c.Append(Note(SessionContextKind.Found, "y", id: "f1"), SessionContextAuthor.Host));
    }

    [Fact]
    public void Superseding_an_unknown_replaced_or_other_kind_entry_is_refused()
    {
        var c = SessionContext.Empty
            .Append(Note(SessionContextKind.Found, "x", id: "f1"), SessionContextAuthor.Host)
            .Append(Note(SessionContextKind.Found, "x2", id: "f2", supersedes: "f1"), SessionContextAuthor.Host);

        Assert.Throws<ArgumentException>(() => c.Append(Note(SessionContextKind.Found, "z", supersedes: "nope"), SessionContextAuthor.Host));
        Assert.Throws<ArgumentException>(() => c.Append(Note(SessionContextKind.Found, "z", supersedes: "f1"), SessionContextAuthor.Host));
        Assert.Throws<ArgumentException>(() => c.Append(Note(SessionContextKind.Decided, "z", supersedes: "f2"), SessionContextAuthor.Host));
        Assert.Throws<ArgumentException>(() => c.Append(Note(SessionContextKind.Found, "z", id: "f3", supersedes: "f3"), SessionContextAuthor.Host));
    }

    [Fact]
    public void An_agent_cannot_write_the_work_or_replace_the_hosts_entry()
    {
        var c = SessionContext.Empty.Append(Note(SessionContextKind.Decided, "Use SQLite.", id: "d1"), SessionContextAuthor.Host);

        Assert.Throws<ArgumentException>(() => c.Append(Note(SessionContextKind.Work, "Do something else."), SessionContextAuthor.Agent));
        Assert.Throws<ArgumentException>(() => c.Append(Note(SessionContextKind.Decided, "Use Postgres.", supersedes: "d1"), SessionContextAuthor.Agent));
        // the host may replace an agent's, and an agent its own
        var claimed = c.Append(Note(SessionContextKind.Found, "It is slow.", id: "f1"), SessionContextAuthor.Agent);
        Assert.Equal(3, claimed.Append(Note(SessionContextKind.Found, "It is fast.", supersedes: "f1"), SessionContextAuthor.Agent).Count);
        Assert.Equal(3, claimed.Append(Note(SessionContextKind.Found, "Not slow.", supersedes: "f1"), SessionContextAuthor.Host).Count);
    }

    [Fact]
    public void Since_returns_the_entries_from_a_position_and_refuses_one_outside()
    {
        var c = SessionContext.Empty
            .Append(Note(SessionContextKind.Work, "a"), SessionContextAuthor.Host)
            .Append(Note(SessionContextKind.Decided, "b"), SessionContextAuthor.Host)
            .Append(Note(SessionContextKind.Found, "c"), SessionContextAuthor.Host);

        Assert.Equal(["e2", "e3"], c.Since(1).Select(e => e.Id));
        Assert.Empty(c.Since(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.Since(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => c.Since(-1));
    }

    [Fact]
    public void A_stored_log_rebuilds_the_same_context_and_a_broken_one_is_refused()
    {
        var c = SessionContext.Empty
            .Append(Note(SessionContextKind.Decided, "Use SQLite.", id: "d1"), SessionContextAuthor.Host)
            .Append(new SessionContextNote(SessionContextKind.Decided, "Use Postgres.") { Id = "d2", Supersedes = "d1", Subject = "storage" }, SessionContextAuthor.Host);

        var back = SessionContext.FromEntries(c.Entries);

        Assert.Equal(c.Entries, back.Entries);
        Assert.Equal(["d2"], back.Live.Select(e => e.Id));
        Assert.Throws<ArgumentException>(() => SessionContext.FromEntries(c.Entries.Reverse()));
    }
}
