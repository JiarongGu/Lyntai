using Lyntai.Llm;
using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>Does a REAL model produce STABLE subjects?</b> — the one property the whole annotation mechanism rests
/// on, and the one the corpus measurement structurally cannot see.
///
/// <para>That measurement used a perfect annotator and reported the mechanism's CEILING: Chinese cluster miss
/// 0.6667 → 0.0000. It says the design is sound and says nothing about a model. Two facts link because their
/// subjects MATCH, so a model that phrases the same entity differently each write — "spouse" then "my
/// spouse" then "Alice" — links nothing, and would look EXACTLY like having no annotator at all. That is the
/// likeliest way this disappoints in the field, so it is worth a live check rather than an assumption.</para>
///
/// <para><b>Asserted as a property, never as an exact string.</b> Which handle a model picks is its business
/// and varies by model; that the SAME handle comes back for facts about the same entity is the contract. An
/// assertion on a literal would fail on a model upgrade for a reason that has nothing to do with the
/// mechanism working.</para>
///
/// <para>Runs only when <c>LYNTAI_LIVE_OLLAMA</c> is set AND the endpoint is reachable; otherwise SKIPPED,
/// never a pass that observed nothing. Enable: <c>LYNTAI_LIVE_OLLAMA=1</c>, optionally
/// <c>LYNTAI_OLLAMA_ANNOTATION_MODEL</c> (default <c>llama3.2:3b</c> — annotation runs on every write, so a
/// small model is the realistic deployment).</para>
/// </summary>
public class LlmAnnotationLiveTests
{
    private static string BaseUrl => Lyntai.Tests.Live.OllamaLive.BaseUrl;

    private static string Model =>
        Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_ANNOTATION_MODEL") ?? "llama3.2:3b";

    private static Task<bool> LiveAsync() => Lyntai.Tests.Live.OllamaLive.IsAvailableAsync();

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddOllamaProvider(baseUrl: BaseUrl, defaultModel: Model)
            .UseDefaultCandidates("ollama")
            .AddMemoryAnnotation(o => o.Model = Model));
        return services.BuildServiceProvider();
    }

    /// <summary>Annotate several facts about ONE person — the first naming them, the rest referring back —
    /// and require a handle shared by at least two. Two is the threshold because ONE shared handle is all
    /// linking needs: a later fact attaches to the cluster through it.</summary>
    [SkippableTheory]
    [InlineData("english", "my spouse is Alice", "she works as an anaesthetist at a hospital",
        "we met on a trip to Kyoto")]
    [InlineData("chinese", "我的配偶是爱丽丝", "她在一家医院做麻醉师", "我们是在京都的一次旅行中认识的")]
    public async Task A_real_model_gives_facts_about_one_entity_a_shared_subject(
        string language, string introduces, string pronoun, string shared)
    {
        Skip.IfNot(await LiveAsync(), "LYNTAI_LIVE_OLLAMA not set, or the Ollama endpoint is unreachable");

        using var sp = Build();
        var annotator = sp.GetRequiredService<IMemoryAnnotationPolicy>();

        var facts = new[] { introduces, pronoun, shared };
        var subjectsPerFact = new List<IReadOnlyList<string>>();
        var known = new List<string>();
        foreach (var fact in facts)
        {
            // exactly what the engine passes: recent facts make a pronoun resolvable, and the handles
            // already in use are the reuse candidates that keep the answers from drifting between equally
            // defensible aspects of the same fact
            var recent = facts.Take(subjectsPerFact.Count).ToList();
            var annotation = await annotator.AnnotateAsync(
                new MemoryAnnotationRequest(new MemoryWrite("t", "s", fact), recent, [.. known]));
            subjectsPerFact.Add(annotation.Subjects);
            foreach (var s in annotation.Subjects)
                if (!known.Contains(s, StringComparer.OrdinalIgnoreCase)) known.Add(s);
        }

        Assert.All(subjectsPerFact, s => Assert.NotEmpty(s));

        // at least one handle shared by two or more facts — compared case-insensitively, because case is the
        // likeliest way a model breaks stability without meaning to and the store folds it anyway
        var shares = subjectsPerFact
            .SelectMany(s => s.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() >= 2)
            .Select(g => g.Key)
            .ToList();

        Assert.True(shares.Count > 0,
            $"[{language}] no subject was shared by two facts about the same person, so nothing would link. " +
            $"Model '{Model}' returned: " +
            string.Join(" | ", subjectsPerFact.Select(s => "[" + string.Join(", ", s) + "]")));
    }

    /// <summary><b>The multilingual claim, checked rather than asserted.</b> The shipped prompt asks for
    /// subjects in the language of the fact so that the same entity yields the same handle whatever it is
    /// written in — an English-shaped answer to a Chinese fact would mean a later Chinese fact gets a Chinese
    /// handle and the two never link. This is the prompt's own promise, so it is worth verifying against a
    /// model rather than trusting the wording.</summary>
    [SkippableFact]
    public async Task A_chinese_fact_is_annotated_in_chinese()
    {
        Skip.IfNot(await LiveAsync(), "LYNTAI_LIVE_OLLAMA not set, or the Ollama endpoint is unreachable");

        using var sp = Build();
        var annotation = await sp.GetRequiredService<IMemoryAnnotationPolicy>().AnnotateAsync(
            new MemoryAnnotationRequest(new MemoryWrite("t", "s", "我的配偶是爱丽丝"), []));

        Assert.NotEmpty(annotation.Subjects);
        Assert.Contains(annotation.Subjects, s => s.Any(c => c is >= '一' and <= '鿿'));
    }
}
