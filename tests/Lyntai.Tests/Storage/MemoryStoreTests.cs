using Lyntai;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

public class MemoryStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly LyntaiOptions _options = new() { MemoryCapPerScope = 3, MemoryRecallLimit = 10 };
    private readonly SqliteMemoryStore _store;

    public MemoryStoreTests() => _store = new SqliteMemoryStore(_db.Factory, _options);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Remember_then_recall_by_substring()
    {
        await _store.RememberAsync("deploy", "prod", "the deploy pipeline requires manual approval");
        await _store.RememberAsync("deploy", "prod", "rollbacks must page the on-call");

        var hits = await _store.RecallAsync("deploy", query: "pipeline");

        Assert.Single(hits);
        Assert.Contains("manual approval", hits[0].Content);
    }

    [Fact]
    public async Task Cjk_substring_recall_proves_trigram()
    {
        await _store.RememberAsync("cortex", "notes", "灵台平台负责智能代理的记忆存储");
        await _store.RememberAsync("cortex", "notes", "另一条无关的记录");

        // unicode61 would index the whole phrase as one token and never match a mid-phrase substring
        var hits = await _store.RecallAsync("cortex", query: "智能代理");

        Assert.Single(hits);
        Assert.Contains("智能代理", hits[0].Content);
    }

    [Fact]
    public async Task Scope_filter_applies()
    {
        await _store.RememberAsync("task", "alpha", "fact in alpha scope");
        await _store.RememberAsync("task", "beta", "fact in beta scope");

        var alpha = await _store.RecallAsync("task", scope: "alpha");

        Assert.Single(alpha);
        Assert.Equal("alpha", alpha[0].Scope);

        var both = await _store.RecallAsync("task");
        Assert.Equal(2, both.Count);
    }

    [Fact]
    public async Task Task_isolation_applies()
    {
        await _store.RememberAsync("task-a", "s", "belongs to a");
        await _store.RememberAsync("task-b", "s", "belongs to b");

        var hits = await _store.RecallAsync("task-a");

        Assert.Single(hits);
        Assert.Equal("task-a", hits[0].TaskKey);
    }

    [Fact]
    public async Task Cap_is_enforced_oldest_trimmed()
    {
        for (var i = 1; i <= 5; i++)
            await _store.RememberAsync("capped", "s", $"entry {i}");

        var hits = await _store.RecallAsync("capped"); // cap is 3

        Assert.Equal(3, hits.Count);
        Assert.Equal(["entry 5", "entry 4", "entry 3"], hits.Select(h => h.Content)); // newest kept
    }

    [Fact]
    public async Task Short_query_falls_back_to_like()
    {
        await _store.RememberAsync("task", "s", "alpha ab beta");
        await _store.RememberAsync("task", "s", "gamma delta");

        var hits = await _store.RecallAsync("task", query: "ab"); // <3 chars → FtsQuery null → LIKE

        Assert.Single(hits);
        Assert.Contains("alpha ab beta", hits[0].Content);
    }

    [Fact]
    public async Task Recall_is_fail_open_empty_query_returns_recent()
    {
        await _store.RememberAsync("task", "s", "newest");

        var hits = await _store.RecallAsync("task", query: "   ");

        Assert.Single(hits);
    }

    [Fact]
    public async Task Forget_clears_a_task()
    {
        await _store.RememberAsync("gone", "s", "x");
        await _store.ForgetAsync("gone");

        Assert.Empty(await _store.RecallAsync("gone"));
    }
}
