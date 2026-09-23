using System.Text;
using Lyntai.Storage.FileSystem;

namespace Lyntai.Tests.Storage.FileSystem;

/// <summary>What the file-system backend promises about the FILES — the half no cross-backend contract can
/// see: the record format, the names, ownership, and what happens to a file a person broke.</summary>
public class FileSystemFormatTests
{
    // ---- the record format ----------------------------------------------------------------------------

    [Theory]
    [InlineData("plain")]
    [InlineData("")]
    [InlineData("---\nnot a header\n---\n")]
    [InlineData("line one\r\nline two\r\n")]
    [InlineData("灵台 — 部署密钥\n")]
    public void A_body_round_trips_byte_for_byte(string body)
    {
        var (_, read) = RecordFile.Parse(RecordFile.Write(new RecordHeader().Add("key", "k"), body));

        Assert.Equal(body, read);
    }

    [Fact]
    public void A_header_value_stays_on_one_line_whatever_it_contains()
    {
        var text = RecordFile.Write(new RecordHeader().Add("key", "a\nb: \"c\"\r\n---"), "body");

        var (header, body) = RecordFile.Parse(text);

        Assert.Equal("a\nb: \"c\"\r\n---", header.String("key"));
        Assert.Equal("body", body);
        Assert.Equal(4, text.Split('\n').Length - 1); // ---, lyntai, key, --- — then the body
    }

    [Fact]
    public void Every_field_type_round_trips()
    {
        var when = new DateTimeOffset(2026, 9, 23, 10, 30, 0, TimeSpan.FromHours(8));
        var text = RecordFile.Write(new RecordHeader()
            .Add("s", "灵台").Add("n", 42L).Add("b", true).Add("t", when).Add("none", (string?)null)
            .Add("m", new Dictionary<string, string> { ["z"] = "1", ["a"] = "2" }), "");

        var (h, _) = RecordFile.Parse(text);

        Assert.Equal("灵台", h.String("s"));
        Assert.Equal(42L, h.Long("n"));
        Assert.True(h.Bool("b"));
        Assert.Equal(when, h.Time("t"));
        Assert.Null(h.String("none"));
        Assert.Null(h.Long("absent"));
        Assert.Equal(new Dictionary<string, string> { ["a"] = "2", ["z"] = "1" }, h.Map("m"));
        Assert.Contains("灵台", text, StringComparison.Ordinal); // readable, not \u-escaped
    }

    [Fact]
    public void A_record_from_a_newer_schema_is_refused_rather_than_misread() =>
        Assert.Throws<InvalidDataException>(() => RecordFile.Parse("---\nlyntai: 2\n---\nbody"));

    [Theory]
    [InlineData("no fence at all")]
    [InlineData("---\nlyntai: 1\nnever closed")]
    [InlineData("---\nkey: \"no schema\"\n---\n")]
    [InlineData("---\nlyntai: 1\nnot a field\n---\n")]
    public void Something_that_is_not_a_record_is_a_format_error(string text) =>
        Assert.Throws<FormatException>(() => RecordFile.Parse(text));

    [Fact]
    public void A_header_saved_with_CRLF_by_an_editor_still_parses()
    {
        var (h, body) = RecordFile.Parse("---\r\nlyntai: 1\r\nkey: \"k\"\r\n---\r\nbody");

        Assert.Equal("k", h.String("key"));
        Assert.Equal("body", body);
    }

    // ---- names -------------------------------------------------------------------------------------------

    [Fact]
    public void A_name_is_a_readable_slug_then_a_hash()
    {
        var name = RecordName.For("lyntai.prompt.System");

        Assert.StartsWith("lyntai-prompt-system-", name, StringComparison.Ordinal);
        Assert.Matches("^[a-z-]+-[0-9a-f]{16}$", name);
    }

    [Fact]
    public void Keys_differing_only_in_case_get_different_names_so_a_case_insensitive_volume_keeps_both() =>
        Assert.NotEqual(RecordName.For("Key").ToLowerInvariant(), RecordName.For("key").ToLowerInvariant());

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\windows")]
    [InlineData("CON")]
    [InlineData("nul")]
    [InlineData("trailing dot.")]
    [InlineData("a:b*c?d\"e<f>g|h")]
    [InlineData("")]
    [InlineData("!!!")]
    public void No_consumer_string_becomes_a_path_the_file_system_would_misread(string value)
    {
        var name = RecordName.For(value);

        Assert.DoesNotContain(name, c => c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' or '.');
        Assert.Matches("[0-9a-f]{16}$", name);
        Assert.InRange(name.Length, 16, 49);
    }

    [Fact]
    public void A_long_value_is_cut_to_a_bounded_name() =>
        Assert.InRange(RecordName.For(new string('x', 5000)).Length, 16, 49);

    // ---- the root ------------------------------------------------------------------------------------------

    [Fact]
    public void A_record_is_written_as_BOMless_UTF8_with_LF_header_lines()
    {
        using var temp = new TempRoot();
        var file = temp.Root.Combine("kv", "x.md");

        temp.Root.Write(file, RecordFile.Write(new RecordHeader().Add("key", "灵"), "v"));

        var bytes = File.ReadAllBytes(file);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal("---\nlyntai: 1\nkey: \"灵\"\n---\nv", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void A_second_owner_of_the_same_root_is_refused()
    {
        using var temp = new TempRoot();

        var refused = Assert.Throws<InvalidOperationException>(() => new FileSystemRoot(temp.Directory));
        Assert.Contains("already owned", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_broken_file_is_skipped_and_left_alone_while_its_siblings_load()
    {
        using var temp = new TempRoot();
        var dir = temp.Root.Combine("kv");
        temp.Root.Write(Path.Combine(dir, "good.md"), RecordFile.Write(new RecordHeader().Add("key", "k"), "v"));
        File.WriteAllText(Path.Combine(dir, "broken.md"), "a person's half-finished edit");
        File.WriteAllText(Path.Combine(dir, "nokey.md"), "---\nlyntai: 1\n---\nno key field");

        var keys = temp.Root.Load(dir, (_, h, _) => h.RequiredString("key"));

        Assert.Equal(["k"], keys);
        Assert.True(File.Exists(Path.Combine(dir, "broken.md")));
        Assert.True(File.Exists(Path.Combine(dir, "nokey.md")));
    }

    [Fact]
    public void A_temporary_file_a_crash_left_behind_is_cleared_on_load()
    {
        using var temp = new TempRoot();
        var dir = temp.Root.Combine("kv");
        temp.Root.Write(Path.Combine(dir, "a.md"), RecordFile.Write(new RecordHeader().Add("key", "a"), "new"));
        File.WriteAllText(Path.Combine(dir, "a.md.tmp"), "half a write");

        Assert.Equal(["a"], temp.Root.Load(dir, (_, h, _) => h.RequiredString("key")));
        Assert.False(File.Exists(Path.Combine(dir, "a.md.tmp")));
    }

    [Fact]
    public void A_numbered_file_that_does_not_parse_still_reserves_its_id()
    {
        using var temp = new TempRoot();
        var dir = temp.Root.Combine("curated");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "000007.md"), "broken");

        Assert.Equal(7, FileSystemRoot.MaxId(dir));
    }
}
