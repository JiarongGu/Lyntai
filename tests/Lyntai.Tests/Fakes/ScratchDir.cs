namespace Lyntai.Tests.Fakes;

/// <summary>A per-test directory under <c>devtools/_test-scratch</c> (never OS temp), created on construction
/// and deleted on dispose. Own it for the test's lifetime — a fixture that creates one and never disposes it is
/// how that directory came to hold tens of thousands of leftovers.</summary>
public sealed class ScratchDir : IDisposable
{
    /// <param name="prefix">Names the directory for whoever finds one a crashed run left behind.</param>
    public ScratchDir(string prefix)
    {
        Path = System.IO.Path.Combine(TestPaths.TestScratchDir, $"{prefix}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    /// <summary>The directory's full path.</summary>
    public string Path { get; }

    /// <summary>A path inside the directory; nothing is created.</summary>
    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Writes <paramref name="content"/> to <paramref name="name"/> inside the directory and returns
    /// its full path — an empty file by default, which is all a fake binary or model path needs.</summary>
    public string File(string name, string content = "")
    {
        var path = Combine(name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        // best effort: a handle a subject still holds must not fail the test that was already green
        try { Directory.Delete(Path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
