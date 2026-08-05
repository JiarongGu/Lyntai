using Dapper;
using Lyntai;
using Lyntai.Cortex;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

/// <summary>F1 (feature toggles): a DISABLED storage feature lands no table. Selective migration is
/// driven by per-migration <c>[Tags(nameof(StorageFeature.X))]</c> + the runner's active tag set.</summary>
public class FeatureToggleTests : IDisposable
{
    private readonly List<TempDbPath> _dbs = [];

    private string FreshPath()
    {
        var db = new TempDbPath("features"); // fresh un-migrated path — selective migration is the point
        _dbs.Add(db);
        return db.Path;
    }

    public void Dispose()
    {
        foreach (var db in _dbs) db.Dispose();
    }

    private static bool TableExists(SqliteConnectionFactory factory, string table)
    {
        using var conn = factory.Open();
        return conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@table", new { table }) > 0;
    }

    [Fact]
    public void Selective_migration_lands_only_the_selected_features_tables()
    {
        var path = FreshPath();
        MigrationRunnerService.MigrateUp(path, StorageFeature.Score | StorageFeature.Conversation);
        var factory = new SqliteConnectionFactory(path);

        Assert.True(TableExists(factory, "lyntai_score_result"));  // Score selected
        Assert.True(TableExists(factory, "lyntai_thread"));         // Conversation selected
        Assert.True(TableExists(factory, "lyntai_message"));
        Assert.False(TableExists(factory, "lyntai_kv"));            // KeyValue NOT selected → no table
        Assert.False(TableExists(factory, "lyntai_memory_entry"));  // Memory NOT selected
        Assert.False(TableExists(factory, "lyntai_job"));           // Jobs NOT selected
        Assert.True(TableExists(factory, "lyntai_version_info"));   // version table always
    }

    [Fact]
    public void All_migrates_every_feature_the_historical_default()
    {
        var path = FreshPath();
        MigrationRunnerService.MigrateUp(path); // == StorageFeature.All
        var factory = new SqliteConnectionFactory(path);

        foreach (var t in new[] { "lyntai_kv", "lyntai_thread", "lyntai_message", "lyntai_memory_entry",
            "lyntai_score_result", "lyntai_run_trace", "lyntai_prompt_version", "lyntai_job", "lyntai_curated_memory", "lyntai_curated_meta" })
            Assert.True(TableExists(factory, t), $"{t} should exist under All");
    }

    [Fact]
    public async Task UseSqliteStorage_with_a_subset_registers_only_those_stores_and_lands_only_those_tables()
    {
        var path = FreshPath();
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseSqliteStorage(path, StorageFeature.Score));
        using var sp = services.BuildServiceProvider();

        // the Score store is registered and works...
        var scores = sp.GetRequiredService<IScoreStore>();
        await scores.SaveAsync("s", [new ScoredResult("a", "A", "g", false, 0.5)]);
        Assert.Single(await scores.GetAsync("s"));

        // ...while a DISABLED domain has no store registered and no table landed
        Assert.Null(sp.GetService<IConversationStore>());
        Assert.False(TableExists(new SqliteConnectionFactory(path), "lyntai_thread"));
    }

    // ---- the Governance prerequisite ------------------------------------------------------------------
    // lyntai_response_cache / lyntai_usage / lyntai_vector all ship in the ONE Governance migration, so the
    // three helpers backed by them would otherwise register a store over a table that was never created and
    // fail at the first call. UseSqliteStorage's contract is that a disabled domain is simply unresolvable
    // and that unresolvability IS the startup signal — these are the calls that could break it.
    //
    // The guard's premise is that LYNTAI was going to create the table and the feature set stopped it, so it
    // holds only where Lyntai owns the schema. The two directions are pinned below.

    [Theory]
    [InlineData("UseSqliteResponseCache")]
    [InlineData("UseSqliteUsageTracking")]
    [InlineData("UseSqliteVectorStore")]
    public void A_governance_backed_helper_fails_at_wiring_when_governance_is_toggled_off(string helper)
    {
        var path = FreshPath();
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p")).UseSqliteStorage(path, StorageFeature.Memory);
            Apply(b, helper);
        }));

        Assert.Contains(helper, ex.Message);                    // names the call that is wrong
        Assert.Contains("StorageFeature.Governance", ex.Message); // and the feature to add
    }

    /// <summary>The Postgres half of the same rule — the two Governance-backed helpers there
    /// (<c>UsePostgresVectorStore</c> is exempt: it creates its own schema lazily). No server is contacted:
    /// <see cref="SchemaMigration.OnFirstUse"/> defers the migration to the first open, and the guard throws
    /// during composition, long before anything is opened.</summary>
    [Theory]
    [InlineData("UsePostgresResponseCache")]
    [InlineData("UsePostgresUsageTracking")]
    public void A_governance_backed_postgres_helper_fails_at_wiring_when_governance_is_toggled_off(string helper)
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"))
                .UsePostgresStorage(UnusedPostgres, StorageFeature.Memory, SchemaMigration.OnFirstUse);
            ApplyPostgres(b, helper);
        }));

        Assert.Contains(helper, ex.Message);
        Assert.Contains("StorageFeature.Governance", ex.Message);
    }

    /// <summary>…but under <see cref="SchemaMigration.None"/> Lyntai runs NO migration, so the feature set
    /// never decided which tables exist and the guard's premise is simply false. An app that created
    /// <c>lyntai_vector</c> itself and passed a narrow feature set worked before the guard and must keep
    /// working — and "add StorageFeature.Governance" would create no table here anyway. Pinned in both call
    /// orders, because the skip has to be as order-independent as the throw.</summary>
    [Fact]
    public void The_guard_is_silent_when_the_APP_owns_the_schema()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseSqliteStorage(FreshPath(), StorageFeature.Memory, SchemaMigration.None)
            .UseSqliteVectorStore()
            .UseSqliteResponseCache()
            .UseSqliteUsageTracking());

        var reversed = new ServiceCollection();
        reversed.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseSqliteVectorStore()                                                   // helper FIRST
            .UseSqliteStorage(FreshPath(), StorageFeature.Memory, SchemaMigration.None));

        var postgres = new ServiceCollection();
        postgres.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UsePostgresStorage(UnusedPostgres, StorageFeature.Memory, SchemaMigration.None)
            .UsePostgresResponseCache()
            .UsePostgresUsageTracking());

        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<Lyntai.Memory.IVectorStore>());                   // wired, not rejected
    }

    /// <summary>Same premise, the other documented app-owns-the-schema route: an app-supplied
    /// <see cref="IDbConnectionFactory"/>. Its own documentation says "Lyntai runs no migrations here; own
    /// the schema", so the Governance check cannot apply to it either.</summary>
    [Fact]
    public void The_guard_is_silent_over_an_app_supplied_connection_factory()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseSqliteStorage(new SqliteConnectionFactory(FreshPath()), StorageFeature.Memory)
            .UseSqliteVectorStore()
            .UseSqliteResponseCache()
            .UseSqliteUsageTracking());

        var postgres = new ServiceCollection();
        postgres.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UsePostgresStorage(new Lyntai.Storage.Postgres.PostgresConnectionFactory(UnusedPostgres),
                StorageFeature.Memory)
            .UsePostgresResponseCache()
            .UsePostgresUsageTracking());

        using var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<Lyntai.Memory.IVectorStore>());
    }

    // never dialled: every Postgres case here throws (or deliberately does not) during DI composition
    private const string UnusedPostgres = "Host=localhost;Database=lyntai_guard_test;Username=u;Password=p";

    /// <summary>The two calls can be written either way round, so the guard must not be defeatable by
    /// swapping two builder lines — whichever lands second detects the contradiction.</summary>
    [Fact]
    public void The_governance_guard_catches_the_reverse_call_order_too()
    {
        var path = FreshPath();
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseSqliteVectorStore()                              // helper FIRST
            .UseSqliteStorage(path, StorageFeature.Memory)));    // selection second

        Assert.Contains("UseSqliteVectorStore", ex.Message);
    }

    /// <summary>A subset that INCLUDES Governance is wired normally and round-trips — the guard rejects the
    /// broken configuration only, never a narrow-but-correct one.</summary>
    [Fact]
    public async Task A_subset_including_governance_wires_the_vector_store_and_recalls()
    {
        var path = FreshPath();
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseSqliteStorage(path, StorageFeature.Governance)   // narrow, but Governance is in
            .UseSqliteVectorStore()
            .UseSqliteResponseCache()
            .UseSqliteUsageTracking()
            .AddSemanticMemory(new FakeEmbedder()));
        await using var sp = services.BuildServiceProvider();

        var memory = sp.GetRequiredService<Lyntai.Memory.ISemanticMemory>();
        await memory.RememberAsync("t", "s", "cancel subscription anytime");
        var hits = await memory.RecallAsync("t", "s", "how to cancel", k: 1);

        Assert.Single(hits);                                     // the lyntai_vector table really is there
        Assert.Contains("cancel", hits[0].Content);
    }

    /// <summary>The default (<see cref="StorageFeature.All"/>) always includes Governance, so the historical
    /// wiring is untouched by the guard.</summary>
    [Fact]
    public void The_default_feature_set_is_unaffected()
    {
        var path = FreshPath();
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseSqliteStorage(path)
            .UseSqliteVectorStore()
            .UseSqliteResponseCache()
            .UseSqliteUsageTracking());
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetService<Lyntai.Memory.IVectorStore>());
        Assert.NotNull(sp.GetService<Lyntai.Llm.Caching.IResponseCache>());
        Assert.NotNull(sp.GetService<Lyntai.Llm.Budgeting.IUsageTracker>());
    }

    private static void Apply(LyntaiBuilder b, string helper)
    {
        switch (helper)
        {
            case "UseSqliteResponseCache": b.UseSqliteResponseCache(); break;
            case "UseSqliteUsageTracking": b.UseSqliteUsageTracking(); break;
            default: b.UseSqliteVectorStore(); break;
        }
    }

    private static void ApplyPostgres(LyntaiBuilder b, string helper)
    {
        if (helper == "UsePostgresResponseCache") b.UsePostgresResponseCache();
        else b.UsePostgresUsageTracking();
    }
}
