using Lyntai.Prompts;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Core;

public class PromptRegistryTests
{
    private readonly InMemoryKeyValueStore _kv = new();
    private const string Default = "Summarize {input} in {lang}.";

    [Fact]
    public async Task No_override_renders_default_with_vars()
    {
        var registry = new PromptRegistry(_kv);

        var rendered = await registry.RenderAsync("summary", Default,
            new Dictionary<string, string> { ["input"] = "the text", ["lang"] = "English" });

        Assert.Equal("Summarize the text in English.", rendered);
    }

    [Fact] // A3: substitution is SINGLE-PASS — a var VALUE containing a placeholder must stay literal
    public async Task Var_values_containing_placeholders_are_not_resubstituted()
    {
        var registry = new PromptRegistry(_kv);

        var rendered = await registry.RenderAsync("p", "{a} and {b}", new Dictionary<string, string>
        {
            ["a"] = "literal {b} inside a value",
            ["b"] = "B",
        });

        // sequential Replace used to re-scan a's substituted value and turn its "{b}" into "B"
        // (order-dependent injection); single-pass keeps values inert
        Assert.Equal("literal {b} inside a value and B", rendered);
    }

    [Fact]
    public async Task Override_wins_when_it_keeps_all_placeholders()
    {
        _kv.Data[PromptRegistry.DefaultKeyPrefix + "summary"] = "TL;DR of {input} ({lang}):";
        var registry = new PromptRegistry(_kv);

        var rendered = await registry.RenderAsync("summary", Default,
            new Dictionary<string, string> { ["input"] = "x", ["lang"] = "en" });

        Assert.Equal("TL;DR of x (en):", rendered);
    }

    [Fact]
    public async Task Override_dropping_a_placeholder_is_rejected_falls_back_to_default()
    {
        // documented decision: reject + warn + use the default (fail-open, no silent content loss)
        _kv.Data[PromptRegistry.DefaultKeyPrefix + "summary"] = "TL;DR of {input}:"; // dropped {lang}
        var registry = new PromptRegistry(_kv);

        var rendered = await registry.RenderAsync("summary", Default,
            new Dictionary<string, string> { ["input"] = "x", ["lang"] = "en" });

        Assert.Equal("Summarize x in en.", rendered);
    }

    [Fact]
    public async Task Unknown_placeholder_is_left_literal()
    {
        var registry = new PromptRegistry(_kv);

        var rendered = await registry.RenderAsync("summary", Default,
            new Dictionary<string, string> { ["input"] = "x" }); // no {lang} var

        Assert.Equal("Summarize x in {lang}.", rendered);
    }

    [Fact]
    public async Task No_store_configured_renders_default()
    {
        var registry = new PromptRegistry(kv: null);

        var rendered = await registry.RenderAsync("summary", "plain");

        Assert.Equal("plain", rendered);
    }

    [Fact]
    public void ValidateOverride_reports_the_exact_dropped_placeholders()
    {
        var registry = new PromptRegistry(kv: null);

        // keeps both placeholders (extra prose is fine) → valid
        Assert.Empty(registry.ValidateOverride(Default, "In {lang}, summarize {input} tersely."));

        // drops {lang} → the admin save-flow can reject with the exact token
        Assert.Equal(["lang"], registry.ValidateOverride(Default, "Summarize {input}."));
    }
}
