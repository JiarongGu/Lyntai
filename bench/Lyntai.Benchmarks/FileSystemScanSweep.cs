using System.Diagnostics;
using System.Text;

using Lyntai.Storage;

namespace Lyntai.Benchmarks;

/// <summary>
/// <c>storage-scan</c> — can a file-per-record store meet the substring-recall contract by SCANNING, or
/// must it keep its own in-process index? The design risk <c>Lyntai.Storage.FileSystem</c> named before
/// anything was built (<c>docs/task-archive.md</c> Part 277): SQLite answers recall from an FTS5 trigram
/// index, and a directory answers it with nothing.
///
/// <para><b>What is timed is the whole recall a scan would do</b> — enumerate the directory, read every
/// file, and match through <see cref="SearchTerms"/>, the split every backend already shares — beside the
/// same match over strings already in memory, which is what an index-backed store pays. Records are sized
/// like a curated fact (~300 characters, UTF-8, English and CJK mixed), under a frontmatter header like the
/// one the package would write.</para>
///
/// <para><b>Pre-registered</b>: a scan is acceptable when a full recall over 10,000 records is ≤ 100 ms at
/// p50; otherwise the package keeps an index. Five repeats per size, and the first is reported separately
/// because it is the one a process pays after start.</para>
/// </summary>
internal static class FileSystemScanSweep
{
    private const double BarMs = 100;
    private const int Repeats = 5;

    public static int Run(string[] args)
    {
        int[] sizes = args.Contains("--large") ? [1_000, 10_000, 100_000] : [1_000, 10_000];
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_storage-scan"));

        Console.WriteLine();
        Console.WriteLine("storage-scan — scan every record file per recall, or keep an in-process index?");
        Console.WriteLine($"  root  {root}");
        Console.WriteLine($"  bar   PRE-REGISTERED: a full scan-recall over 10,000 records <= {BarMs:0} ms at p50");
        Console.WriteLine();
        Console.WriteLine($"  {"records",8} {"first scan",11} {"scan p50",10} {"scan max",10} {"in-memory p50",14} {"hits",6}");

        double? verdictMs = null;
        foreach (var n in sizes)
        {
            var dir = Path.Combine(root, n.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);
            var contents = Enumerable.Range(0, n).Select(Content).ToList();
            for (var i = 0; i < n; i++)
                File.WriteAllText(Path.Combine(dir, $"{i:D6}.md"), Record(i, contents[i]), new UTF8Encoding(false));

            const string query = "credential expiry 凭证过期";
            var terms = SearchTerms.SubstringTerms(query);

            var scans = new List<double>();
            var hits = 0;
            for (var r = 0; r < Repeats; r++)
            {
                var watch = Stopwatch.StartNew();
                hits = Directory.EnumerateFiles(dir, "*.md")
                    .Select(File.ReadAllText)
                    .Count(text => SearchTerms.MatchCount(text, terms, query) > 0);
                scans.Add(watch.Elapsed.TotalMilliseconds);
            }

            var memory = new List<double>();
            for (var r = 0; r < Repeats; r++)
            {
                var watch = Stopwatch.StartNew();
                _ = contents.Count(text => SearchTerms.MatchCount(text, terms, query) > 0);
                memory.Add(watch.Elapsed.TotalMilliseconds);
            }

            var p50 = Median(scans);
            if (n == 10_000) verdictMs = p50;
            Console.WriteLine($"  {n,8:N0} {scans[0],9:F1}ms {p50,8:F1}ms {scans.Max(),8:F1}ms {Median(memory),12:F1}ms {hits,6}");
            Directory.Delete(dir, recursive: true);
        }

        Console.WriteLine();
        Console.WriteLine(verdictMs is not { } ms
            ? "VERDICT WITHHELD — 10,000 was not measured."
            : ms <= BarMs
                ? $"VERDICT: SCAN — {ms:F1} ms at 10,000 records, within the {BarMs:0} ms bar."
                : $"VERDICT: INDEX — {ms:F1} ms at 10,000 records, over the {BarMs:0} ms bar.");
        Console.WriteLine("  NOT measured: a cold OS file cache (the files were just written), other file systems, antivirus");
        Console.WriteLine("  policies other than this machine's, or concurrent writers.");
        return 0;
    }

    private static readonly string[] Words =
        ["deploy", "key", "rotation", "meeting", "spouse", "garden", "invoice", "server", "travel", "budget",
         "部署", "密钥", "会议", "花园", "发票", "服务器", "旅行", "预算"];

    /// <summary>~300 characters of deterministic mixed-script prose; one record in a hundred carries the
    /// query's phrase, whose words appear nowhere else, so the hit count checks the match itself.</summary>
    private static string Content(int i)
    {
        var rng = new Random(i);
        var text = new StringBuilder();
        while (text.Length < 300) text.Append(Words[rng.Next(Words.Length)]).Append(' ');
        if (i % 100 == 0) text.Append("the credential expiry is due 凭证过期");
        return text.ToString();
    }

    private static string Record(int i, string content) =>
        $"---\nlyntai: 1\nid: {i}\ntask: \"bench\"\ncreated: \"2026-09-23T00:00:00Z\"\n---\n{content}";

    private static double Median(List<double> xs)
    {
        var sorted = xs.OrderBy(x => x).ToList();
        return sorted[sorted.Count / 2];
    }
}
