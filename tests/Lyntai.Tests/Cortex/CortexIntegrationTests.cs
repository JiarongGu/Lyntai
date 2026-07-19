using Lyntai;
using Lyntai.Cortex;
using Lyntai.Cortex.Scorers;
using Lyntai.Llm;
using Lyntai.Prompts;
using Lyntai.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Cortex;

/// <summary>Phase 5 acceptance: the whole LLM-ops loop — prompt override → run → score (incl. an LLM
/// judge through the router) → trace → remember/compose — against real SQLite + the provider stub.</summary>
[Collection("provider-cmd-env")] // uses LYNTAI_PROVIDER_CMD for the judge's claude-cli call
public class CortexIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ServiceProvider _sp;

    public CortexIntegrationTests()
    {
        Environment.SetEnvironmentVariable("LYNTAI_PROVIDER_CMD", Providers.ClaudeCliProviderTests.StubCommand);

        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_test-dbs"));
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, $"cortex-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddClaudeCliProvider()
            .UseSqliteStorage(_dbPath)
            .AddScorer<OutcomeScorer>()
            .AddScorer<RelevancyScorer>()
            .DefaultCandidates("claude-cli"));
        _sp = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _sp.Dispose();
        Environment.SetEnvironmentVariable("LYNTAI_PROVIDER_CMD", null);
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(f); } catch { }
        }
    }

    [Fact] // 5.1 — prompt override persisted in SQLite KV changes the rendered prompt
    public async Task Prompt_override_in_sqlite_kv_changes_the_render()
    {
        var kv = _sp.GetRequiredService<IKeyValueStore>();
        var prompts = _sp.GetRequiredService<IPromptRegistry>();

        Assert.Equal("default: x", await prompts.RenderAsync("greet", "default: {v}",
            new Dictionary<string, string> { ["v"] = "x" }));

        await kv.SetAsync(PromptRegistry.DefaultKeyPrefix + "greet", "persisted override: {v}");
        Assert.Equal("persisted override: x", await prompts.RenderAsync("greet", "default: {v}",
            new Dictionary<string, string> { ["v"] = "x" }));
    }

    [Fact] // 5.3 — the LLM judge runs through the router against the stub's SCORING TASK path
    public async Task Llm_judge_scorer_returns_the_stub_verdict()
    {
        var judge = new RelevancyScorer(_sp.GetRequiredService<ILlmClient>());

        var result = await judge.ScoreAsync(
            new ScoreContext { SessionId = "s", Input = "question", Output = "answer" }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(0.8, result.Score);                 // the stub's canned {score:0.8}
        Assert.Equal("stub judge verdict", result.Reason);
    }

    [Fact] // 5.4 — evaluate persists results readable from the score store
    public async Task Evaluate_persists_results_to_the_score_store()
    {
        var scoring = _sp.GetRequiredService<IScoringService>();
        var store = _sp.GetRequiredService<IScoreStore>();

        var results = await scoring.EvaluateAsync(new ScoreContext
        {
            SessionId = "eval-session",
            Input = "the question",
            Output = "the answer",
        });

        Assert.Equal(2, results.Count); // outcome + relevancy (judge via stub)
        var persisted = await store.GetAsync("eval-session");
        Assert.Equal(2, persisted.Count);
        Assert.Contains(persisted, r => r.ScorerId == "outcome" && r.Score == 1.0 && !r.IsLlm);
        Assert.Contains(persisted, r => r.ScorerId == "relevancy" && r.Score == 0.8 && r.IsLlm);
    }

    [Fact] // 5.5 — trace recorder persists steps + totals through the trace store
    public async Task Trace_recorder_persists_and_reads_back()
    {
        var traces = _sp.GetRequiredService<ITraceService>();

        var recorder = traces.Begin("trace-session", "playground");
        recorder.Record(new TraceStep { Kind = "phase", Label = "compose", DurationMs = 5 });
        recorder.Record(new TraceStep { Kind = "llm", Label = "complete", InputTokens = 1200, OutputTokens = 340, CostUsd = 0.012 });
        await recorder.CompleteAsync();

        var loaded = await traces.GetAsync("trace-session");
        Assert.NotNull(loaded);
        Assert.Equal("playground", loaded.Mode);
        Assert.Equal(2, loaded.Steps.Count);
        Assert.Equal(1200, loaded.TotalInputTokens);
        Assert.Equal(0.012, loaded.TotalCostUsd, precision: 10);
        Assert.NotNull(loaded.EndedAt);
    }

    [Fact] // 5.6 — remembered facts surface in a composed prompt
    public async Task Composer_appends_recalled_facts()
    {
        var memory = _sp.GetRequiredService<IMemoryStore>();
        var composer = _sp.GetRequiredService<IPromptComposer>();
        await memory.RememberAsync("deploy", "prod", "always run the smoke suite first");
        await memory.RememberAsync("deploy", "prod", "rollback needs the previous tag");

        var composed = await composer.ComposeAsync("Do the deploy.", "deploy", scope: "prod");

        Assert.StartsWith("Do the deploy.", composed);
        Assert.Contains("## Learned facts (deploy)", composed);
        Assert.Contains("smoke suite", composed);
        Assert.Contains("previous tag", composed);
    }

    [Fact] // 5.6 — outage: a throwing store must not sink the prompt
    public async Task Composer_is_fail_open_on_a_broken_store()
    {
        var composer = new MemoryPromptComposer(new ThrowingMemoryStore());

        var composed = await composer.ComposeAsync("Base prompt.", "task");

        Assert.Equal("Base prompt.", composed);
    }

    [Fact]
    public async Task Composer_without_memories_returns_the_base_prompt()
    {
        var composer = _sp.GetRequiredService<IPromptComposer>();

        Assert.Equal("Plain.", await composer.ComposeAsync("Plain.", "task-with-no-memories"));
    }

    private sealed class ThrowingMemoryStore : IMemoryStore
    {
        public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("db is gone");
        public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
            string? query = null, int? limit = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("db is gone");
        public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("db is gone");
        public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("db is gone");
    }
}
