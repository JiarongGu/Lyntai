using Lyntai.Processes;

namespace Lyntai.Tests.Processes;

/// <summary>The one owner-only temp-file writer: the CLI agent session's <c>--mcp-config</c> document and the
/// MCP tool host's connector files both carry a credential, and both are written through it.</summary>
public class OwnerOnlyTempFileTests
{
    [Fact]
    public void Write_creates_a_fresh_file_holding_exactly_the_content()
    {
        var path = OwnerOnlyTempFile.Write("mcp", """{"token":"x"}""");
        try
        {
            Assert.Equal(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(Path.GetDirectoryName(path)!) + Path.DirectorySeparatorChar);
            Assert.StartsWith("lyntai-mcp-", Path.GetFileName(path), StringComparison.Ordinal);
            Assert.Equal("""{"token":"x"}""", File.ReadAllText(path));
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
        finally
        {
            OwnerOnlyTempFile.TryDelete(path);
        }
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Two_writes_never_share_a_file()
    {
        var a = OwnerOnlyTempFile.Write("settings", "a");
        var b = OwnerOnlyTempFile.Write("settings", "b");
        try
        {
            Assert.NotEqual(a, b);
        }
        finally
        {
            OwnerOnlyTempFile.TryDelete(a);
            OwnerOnlyTempFile.TryDelete(b);
        }
    }

    [Fact]
    public void TryDelete_never_throws_for_a_file_that_is_already_gone()
    {
        OwnerOnlyTempFile.TryDelete(Path.Combine(Path.GetTempPath(), $"lyntai-gone-{Guid.NewGuid():N}.json"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    public void A_kind_that_is_not_a_plain_name_is_refused(string kind)
    {
        Assert.ThrowsAny<ArgumentException>(() => OwnerOnlyTempFile.Write(kind, "x"));
    }
}
