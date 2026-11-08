using Lyntai.Prompts;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Storage;

/// <summary>The registry renders the ACTIVE versioned override (winning over the plain KV key), and
/// a rollback changes what it renders — the audit-trail path end-to-end.</summary>
public class PromptRegistryVersioningTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Active_version_is_rendered_and_beats_the_kv_key()
    {
        var versions = new SqlitePromptVersionStore(_db.Factory);
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync(PromptRegistry.DefaultKeyPrefix + "ask", "KV override: {q}");
        var registry = new PromptRegistry(kv, versions);

        // no version yet → the KV key is used
        Assert.Equal("KV override: hi",
            await registry.RenderAsync("ask", "default {q}", new Dictionary<string, string> { ["q"] = "hi" }));

        // a versioned override takes precedence
        await versions.SaveAsync("ask", "Versioned: {q}", author: "alice");
        Assert.Equal("Versioned: hi",
            await registry.RenderAsync("ask", "default {q}", new Dictionary<string, string> { ["q"] = "hi" }));
    }

    [Fact]
    public async Task Rollback_changes_the_rendered_prompt()
    {
        var versions = new SqlitePromptVersionStore(_db.Factory);
        var registry = new PromptRegistry(kv: null, versions);
        await versions.SaveAsync("ask", "v1: {q}");
        await versions.SaveAsync("ask", "v2: {q}");

        Assert.Equal("v2: x", await registry.RenderAsync("ask", "d {q}", new Dictionary<string, string> { ["q"] = "x" }));

        await versions.RollbackAsync("ask", 1);
        Assert.Equal("v1: x", await registry.RenderAsync("ask", "d {q}", new Dictionary<string, string> { ["q"] = "x" }));
    }

    [Fact]
    public async Task Placeholder_guard_still_applies_to_a_versioned_override()
    {
        var versions = new SqlitePromptVersionStore(_db.Factory);
        var registry = new PromptRegistry(kv: null, versions);
        // this override drops the {lang} placeholder present in the default → rejected, default used
        await versions.SaveAsync("summary", "TL;DR of {input}");

        var rendered = await registry.RenderAsync("summary", "Summarize {input} in {lang}.",
            new Dictionary<string, string> { ["input"] = "x", ["lang"] = "en" });

        Assert.Equal("Summarize x in en.", rendered);
    }

    [Fact]
    public async Task A_rejected_active_version_renders_the_default_not_the_key_value_override()
    {
        var versions = new SqlitePromptVersionStore(_db.Factory);
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync(PromptRegistry.DefaultKeyPrefix + "summary", "KV: {input} in {lang}");
        var registry = new PromptRegistry(kv, versions);
        await versions.SaveAsync("summary", "TL;DR of {input}");   // drops {lang}: rejected

        var rendered = await registry.RenderAsync("summary", "Summarize {input} in {lang}.",
            new Dictionary<string, string> { ["input"] = "x", ["lang"] = "en" });

        Assert.Equal("Summarize x in en.", rendered);
    }

    [Fact]
    public async Task A_rejected_version_is_named_as_a_version_not_as_a_key_value_key()
    {
        var versions = new SqlitePromptVersionStore(_db.Factory);
        var log = new CapturingLogger<PromptRegistry>();
        var registry = new PromptRegistry(kv: null, versions, log);
        await versions.SaveAsync("summary", "TL;DR of {input}");

        await registry.RenderAsync("summary", "Summarize {input} in {lang}.");

        var warning = Assert.Single(log.Messages, m => m.Contains("rejected", StringComparison.Ordinal));
        Assert.Contains("version 1", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(PromptRegistry.DefaultKeyPrefix, warning, StringComparison.Ordinal);
    }
}
