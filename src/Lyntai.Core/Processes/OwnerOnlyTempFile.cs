namespace Lyntai.Processes;

/// <summary>A temp file for a spawned CLI's configuration that may carry a credential — a bearer token, a
/// server's secret environment — and its removal. Created OWNER-ONLY on Unix, so another local user cannot
/// read it during the spawn's window; on Windows the per-user <c>%TEMP%</c> ACL already does that, and the
/// Unix mode is not settable there. Every writer of such a file uses this one, so the permission rule cannot
/// diverge between them.</summary>
public static class OwnerOnlyTempFile
{
    /// <summary>Write <paramref name="content"/> to a fresh file, <c>lyntai-{kind}-{guid}.json</c> in the
    /// temp directory, and return its path. The caller deletes it (<see cref="TryDelete"/>) when the spawn
    /// that reads it ends.</summary>
    /// <param name="kind">A short tag for the file name, e.g. <c>mcp</c>.</param>
    /// <param name="content">The file's full content.</param>
    /// <exception cref="ArgumentException"><paramref name="kind"/> is empty or is not a plain file-name
    /// fragment.</exception>
    public static string Write(string kind, string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentNullException.ThrowIfNull(content);
        if (kind.IndexOfAny(['/', '\\']) >= 0 || kind.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException($"'{kind}' is not a plain file-name fragment.", nameof(kind));

        var path = Path.Combine(Path.GetTempPath(), $"lyntai-{kind}-{Guid.NewGuid():N}.json");
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var writer = new StreamWriter(new FileStream(path, options));
        writer.Write(content);
        return path;
    }

    /// <summary>Delete a file <see cref="Write"/> produced, never throwing for an IO or access failure:
    /// cleanup runs on the way out of a spawn, including a failed one, and a lingering temp file must not
    /// replace the caller's result with an exception.</summary>
    /// <param name="path">The path <see cref="Write"/> returned.</param>
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
