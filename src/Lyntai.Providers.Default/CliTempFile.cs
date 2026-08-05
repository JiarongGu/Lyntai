namespace Lyntai.Providers;

/// <summary>Writes a CLI config file that may carry a credential, and removes it again.
///
/// <para><b>There is a deliberate twin, and it is not a mistake to be deduped away.</b>
/// <c>McpToolHostProvisioner.WriteTemp</c> in <c>Lyntai.Tools.Mcp.Hosting</c> does the same thing for the
/// in-process tool host. It cannot be shared: a provider package must never reference the hosting package
/// (that is what keeps <c>ModelContextProtocol.Core</c> off the graph of apps using the plain provider —
/// <c>docs/DECISIONS.md</c> D23), and the two have different lifetimes (the host's file lives as long as a
/// <c>CliToolSession</c>; this one lives for one turn). If you change the permission logic here, change it
/// there — the reason for the mode is identical and a divergence would be a silent leak.</para></summary>
internal static class CliTempFile
{
    /// <summary>Write <paramref name="content"/> to a fresh temp file and return its path.</summary>
    /// <param name="kind">A short tag used in the file name, e.g. <c>mcp</c>.</param>
    /// <param name="content">The file's full content.</param>
    public static string Write(string kind, string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lyntai-{kind}-{Guid.NewGuid():N}.json");
        // the file typically carries a bearer token or a server's secret env — create OWNER-ONLY on Unix so
        // another local user cannot read it during the CLI's window (Windows %TEMP% is already per-user
        // ACL'd, and UnixCreateMode throws there)
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var writer = new StreamWriter(new FileStream(path, options));
        writer.Write(content);
        return path;
    }

    /// <summary>Delete a file written by <see cref="Write"/>, never throwing: cleanup runs on the way out of
    /// a turn (including a failed one), and failing to remove a temp file must not replace the caller's
    /// actual result with an IO exception.</summary>
    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
