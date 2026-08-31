using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Verification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lyntai.Memory;

/// <summary>Reports a memory wiring that compiles, registers, resolves — and can never run.
///
/// <para><b>Why it exists.</b> Every finding below is a seam that is registered and then silently not there:
/// nothing throws, no result is missing, and the only symptom is recall quality, which a consumer enabling a
/// feature for the first time has no baseline for. Two were reported by adopters on 3.0.0 — a semantic member
/// under a graph member that never received a write, and <c>AddMemoryVerification()</c> on a blend with no
/// member that consults a judge.</para>
///
/// <para><b>It only reports what is CERTAIN.</b> A finding that is usually wrong trains a reader to ignore
/// the channel it arrives on, which is worse than having no check — so the policy findings stay silent the
/// moment a member is an engine this library does not recognise, and the routing finding is derived purely
/// from <see cref="IMemoryEngine.Supported"/>, which holds for a BYO engine too.</para></summary>
internal static class MemoryWiring
{
    /// <summary>Log every finding, then throw if any engine asked for strict wiring.</summary>
    internal static void Report(IReadOnlyList<IMemoryEngine> engines, IServiceProvider sp, bool strict)
    {
        var findings = Inspect(engines,
            Registered<IMemoryVerificationPolicy>(sp), Registered<IMemoryAnnotationPolicy>(sp));
        if (findings.Count == 0) return;

        var logger = sp.GetService<ILogger<MemoryEngineFactory>>();
        foreach (var finding in findings) logger?.LogWarning("memory wiring: {Finding}", finding);

        if (strict)
            throw new InvalidOperationException(
                "Memory wiring is strict (MemoryEngineBuilder.StrictWiring) and found " +
                $"{findings.Count} problem(s):{Environment.NewLine}- " +
                string.Join($"{Environment.NewLine}- ", findings));
    }

    /// <summary>The findings, as finished sentences. Pure, so a test drives it without a container.</summary>
    /// <param name="engines">Every registered engine, blends included.</param>
    /// <param name="verification">Whether an <see cref="IMemoryVerificationPolicy"/> is registered.</param>
    /// <param name="annotation">Whether an <see cref="IMemoryAnnotationPolicy"/> is registered.</param>
    internal static List<string> Inspect(IReadOnlyList<IMemoryEngine> engines, bool verification,
        bool annotation)
    {
        var findings = new List<string>();
        foreach (var engine in engines.OfType<CompositeMemoryEngine>())
            findings.AddRange(Unwritable(engine));

        // Derived from registrations the engine itself can see — no BYO ambiguity of the kind that keeps the
        // policy findings below silent, so this one always speaks.
        foreach (var member in engines.SelectMany(Flatten).OfType<GraphMemoryEngine>())
        {
            if (member.EmbedsWithoutSeeding)
                findings.Add(
                    $"graph member '{member.Name}' is wired with an embedder and a vector store but no " +
                    "IMemorySeedSource named 'semantic' — every write is embedded, for novelty and " +
                    "similarity linking, and no recall considers those neighbours. Call " +
                    "AddMemorySemanticSeeds(), or drop the embedder registration; as wired you pay an " +
                    "embedding per write for nothing a recall reads.");

            // The same defect on the other index, and it was live for the whole life of the annotation seam:
            // handles were recorded, paid for per write, and readable by nothing but the writer.
            if (member.RecordsSubjectsWithoutSeeding)
                findings.Add(
                    $"graph member '{member.Name}' is wired with an annotator but no IMemorySeedSource " +
                    "named 'subject' — every write pays a model call to record what the fact is about, and " +
                    "no recall can reach an entry through a subject. Call AddMemorySubjectSeeds(), or drop " +
                    "the annotator; as wired the handles are readable only by the write path's own linking.");
        }

        if (verification && CertainlyUnconsulted(engines))
            findings.Add(
                "an IMemoryVerificationPolicy is registered and no engine consults one — only a graph member " +
                "(UseGraph) asks a judge which candidates answered. Add one, or drop AddMemoryVerification; " +
                "as wired, the judge never runs and a recall reports Answered = null, which is exactly what " +
                "it reports when no judge is registered at all.");

        if (annotation && CertainlyUnconsulted(engines))
            findings.Add(
                "an IMemoryAnnotationPolicy is registered and no engine consults one — only a graph member " +
                "(UseGraph) annotates what a written fact is about. Add one, or drop the registration; as " +
                "wired, nothing is annotated and no subject links are written.");

        return findings;
    }

    /// <summary>Members that cannot receive a write, because every grade they support is already claimed by
    /// an earlier member and the blend routes rather than fans out.</summary>
    private static IEnumerable<string> Unwritable(CompositeMemoryEngine engine)
    {
        // Fanning out makes every capable member a target, so nothing here can be inert by routing.
        if (engine.WriteRouting != MemoryWriteRouting.FirstCapable) yield break;

        var claimed = MemoryGrades.None;
        var first = true;
        foreach (var member in engine.Members)
        {
            // The first member takes an Inherit write whatever it supports, so it is never inert; a member
            // supporting NOTHING makes no claim this check can reason about.
            var inert = !first && member.Supported != MemoryGrades.None &&
                        (member.Supported & ~claimed) == MemoryGrades.None;
            if (inert)
                yield return
                    $"member '{member.Name}' of engine '{engine.Name}' can never receive a write: every " +
                    $"grade it supports ({member.Supported}) is already taken by an earlier member, and this " +
                    "blend routes each write to the FIRST capable member. Call FanOutWrites() to write to " +
                    "every capable member, reorder the members, or fill this one through its own store.";

            claimed |= member.Supported;
            first = false;
        }
    }

    // The two model-backed seams are consulted by GraphMemoryEngine alone. Reported only when every member is
    // an engine this library ships and is known NOT to consult them — a BYO engine may well do so, and a
    // warning that fires on correct wiring is worse than no warning.
    private static bool CertainlyUnconsulted(IReadOnlyList<IMemoryEngine> engines) =>
        engines.SelectMany(Flatten)
            .All(m => m is LexicalMemoryEngine or SemanticMemoryEngine or CuratedMemoryEngine);

    private static IEnumerable<IMemoryEngine> Flatten(IMemoryEngine engine) =>
        engine is CompositeMemoryEngine composite
            ? composite.Members.SelectMany(Flatten)
            : [engine];

    // IServiceProviderIsService answers without CONSTRUCTING the service, which matters here: the shipped
    // verification policy resolves an ILlmClientFactory with GetRequiredService, so asking for the instance
    // would turn a diagnostic into the startup failure it is meant to describe. A container that does not
    // offer it leaves both checks silent rather than guessing.
    private static bool Registered<T>(IServiceProvider sp) =>
        sp.GetService<IServiceProviderIsService>()?.IsService(typeof(T)) ?? false;
}
