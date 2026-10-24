using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lyntai.Benchmarks;

/// <summary>The one per-question result shape every wired bench writes, so two runs can be compared PAIRED
/// by <c>node devtools/dev.mjs bench-compare</c> rather than by eye across two console tables.
///
/// <para>One JSONL row per arm × question into <c>devtools/_bench-results/&lt;bench&gt;-&lt;stamp&gt;.jsonl</c>:
/// <c>bench</c>, <c>metric</c>, <c>arm</c>, <c>question</c>, <c>hit</c> (<c>null</c> = not decidable for this
/// arm, which a pairing must skip rather than score as a miss), optional <c>score</c> / <c>latencyMs</c>,
/// <c>configuration</c>, <c>run</c> and <c>commit</c>. The schema is read by
/// <c>devtools/scripts/bench-compare.mjs</c>; change both or neither.</para>
///
/// <para><c>configuration</c> hashes the run's arguments and every <c>LYNTAI_*</c> variable (the model
/// names and endpoints live there), so two runs that differ in either refuse to pair unless told.</para>
/// </summary>
internal sealed class BenchResults(string bench, string metric, IEnumerable<string> args)
{
    private readonly List<string> _rows = [];
    private readonly string _run = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
    private readonly string _configuration = Hash(bench, args);
    private readonly string? _commit = GitCommit();

    /// <summary>One arm's outcome on one question. <paramref name="question"/> must be stable across runs
    /// and samples — a dataset id, never a position in this run's sample.</summary>
    internal void Add(string arm, string question, bool? hit, double? score = null, double? latencyMs = null)
    {
        var row = new Dictionary<string, object?>
        {
            ["bench"] = bench, ["metric"] = metric, ["arm"] = arm, ["question"] = question, ["hit"] = hit,
        };
        if (score is { } s) row["score"] = Math.Round(s, 4);
        if (latencyMs is { } l) row["latencyMs"] = Math.Round(l, 1);
        row["configuration"] = _configuration;
        row["run"] = _run;
        if (_commit is not null) row["commit"] = _commit;
        _rows.Add(JsonSerializer.Serialize(row));
    }

    /// <summary>Writes the file and says where; writes nothing, and says so, when no row was added.</summary>
    internal async Task WriteAsync()
    {
        if (_rows.Count == 0)
        {
            Console.WriteLine($"\n  results: no per-question rows in this mode, so no {bench} results file.");
            return;
        }

        var dir = Path.Combine("devtools", "_bench-results");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{bench}-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
        await File.WriteAllLinesAsync(file, _rows);
        Console.WriteLine($"\n  results: {_rows.Count} row(s), configuration {_configuration} → {file}");
    }

    private static string Hash(string bench, IEnumerable<string> args)
    {
        var environment = Environment.GetEnvironmentVariables().Keys.OfType<string>()
            .Where(k => k.StartsWith("LYNTAI_", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(k => $"{k}={Environment.GetEnvironmentVariable(k)}");
        var text = string.Join("\n", [bench, .. args, .. environment]);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
    }

    private static string? GitCommit()
    {
        try
        {
            using var git = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            });
            if (git is null) return null;
            var sha = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit();
            return git.ExitCode == 0 && sha.Length > 0 ? sha : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
