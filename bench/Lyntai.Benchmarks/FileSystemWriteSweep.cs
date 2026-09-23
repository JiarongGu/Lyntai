using System.Diagnostics;
using System.Text;
using Lyntai.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Benchmarks;

/// <summary>
/// <c>storage-scan --writes</c> — what one recall's write-back costs the file-system graph store, against the
/// layout it rejected (<c>docs/DECISIONS.md</c> D174): rewriting each recalled memory's file.
/// <para>Times three things, 300 repeats each: one atomic rewrite of a ~600-byte record (temp file, flush to
/// disk, rename — what every record write does); one 20-line append flushed to disk; and the SHIPPED store's
/// <c>WriteBackAsync</c> for a recall-sized batch — 10 touches, the C(5,2) co-activation edges, 10 review rows —
/// over 1,000 memories, resolved through <c>UseFileSystemStorage</c>.</para>
/// <para><b>Pre-registered</b>: the journal is justified when the shipped write-back's p50 is at most a THIRD of
/// ten atomic rewrites' p50.</para>
/// </summary>
internal static class FileSystemWriteSweep
{
    private const int Repeats = 300;

    public static int Run(string[] args)
    {
        var root = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_storage-scan", "writes"));
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);

        Console.WriteLine();
        Console.WriteLine("storage-scan --writes — what a recall's write-back costs the file graph store");
        Console.WriteLine($"  root  {root}");
        Console.WriteLine("  bar   PRE-REGISTERED: shipped write-back p50 <= (10 atomic rewrites' p50) / 3");
        Console.WriteLine();

        var body = Encoding.UTF8.GetBytes(new string('x', 600));
        var files = Enumerable.Range(0, Repeats).Select(i => Path.Combine(root, $"{i:D6}.md")).ToList();
        foreach (var f in files) File.WriteAllBytes(f, body);
        var rewrite = Time(i => Atomic(files[i], body));

        var log = Path.Combine(root, "journal.jsonl");
        var lines = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 20).Select(i =>
            $$"""{"edge":[{{i}},{{i + 1}},""],"weight":3,"position":1234.5,"ordinal":99,"chars":45678,"at":"2026-09-23T12:00:00.0000000+00:00"}""" + "\n")));
        var append = Time(_ =>
        {
            using var s = new FileStream(log, FileMode.Append, FileAccess.Write, FileShare.Read);
            s.Write(lines);
            s.Flush(flushToDisk: true);
        });

        var services = new ServiceCollection();
        services.AddLyntai(b => b.UseFileSystemStorage(o => o.Root = Path.Combine(root, "store")));
        using var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<IMemoryGraphStore>();
        var ids = new List<long>();
        for (var i = 0; i < 1_000; i++)
            ids.Add(store.UpsertAsync(new GraphNodeWrite("bench", "task", "scope", $"memory {i}",
                $"memory {i}: {new string('m', 280)}", MemoryGrade.Associative, 3, 1, null)).GetAwaiter().GetResult());
        var writeBack = Time(i =>
        {
            var hits = ids.Skip(i * 10 % 990).Take(10).ToList();
            var top = hits.Take(5).ToList();
            var edges = (from x in Enumerable.Range(0, 5) from y in Enumerable.Range(x + 1, 4 - x)
                         select new GraphEdgeWrite(top[x], top[y], Symmetric: true)).ToList();
            store.WriteBackAsync("bench", new GraphWriteBack(
                [.. hits.Select(h => new GraphTouch(h, 4))], edges,
                [.. hits.Select(h => new MemoryReviewWrite(h, Guid.Empty, 1, 3, 5, 0, 0, 3, 4, 5))], 10_000))
                .GetAwaiter().GetResult();
        });

        Console.WriteLine($"  {"operation",-48} {"p50",9} {"p90",9} {"max",9}");
        Row("atomic rewrite of a ~600 B record", rewrite);
        Row("append 20 lines + flush to disk", append);
        Row("shipped WriteBackAsync (10 touch, 10 pairs, 10 reviews)", writeBack);
        var bar = P(rewrite, 0.5) * 10 / 3;
        var verdict = P(writeBack, 0.5) <= bar;
        Console.WriteLine();
        Console.WriteLine($"  verdict  shipped p50 {P(writeBack, 0.5):0.00} ms vs bar {bar:0.00} ms -> {(verdict ? "PASS" : "FAIL")}");
        return 0;
    }

    private static void Atomic(string file, byte[] bytes)
    {
        var temp = file + ".tmp";
        using (var s = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            s.Write(bytes);
            s.Flush(flushToDisk: true);
        }
        File.Move(temp, file, overwrite: true);
    }

    private static List<double> Time(Action<int> action)
    {
        var ms = new List<double>(Repeats);
        for (var i = 0; i < Repeats; i++)
        {
            var watch = Stopwatch.StartNew();
            action(i);
            ms.Add(watch.Elapsed.TotalMilliseconds);
        }
        ms.Sort();
        return ms;
    }

    private static double P(List<double> sorted, double q) => sorted[(int)(q * (sorted.Count - 1))];

    private static void Row(string label, List<double> ms) =>
        Console.WriteLine($"  {label,-48} {P(ms, 0.5),6:0.00} ms {P(ms, 0.9),6:0.00} ms {ms[^1],6:0.00} ms");
}
