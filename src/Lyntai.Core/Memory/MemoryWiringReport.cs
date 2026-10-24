namespace Lyntai.Memory;

/// <summary>What the memory wiring check found: every seam registered and then silently not there. Resolve it
/// from the container — it is registered with the first <c>AddMemoryEngine</c> — to read the findings without a
/// log and without <see cref="MemoryEngineBuilder.StrictWiring"/>; building <see cref="IMemoryEngineFactory"/>
/// still logs each one at Warning and, under strict wiring, fails. Read-only: it reports, it never repairs.
/// <para>It reports only what is CERTAIN (<c>docs/DECISIONS.md</c> D85), so an empty list is not a proof of good
/// wiring: a check that cannot be sure, around an engine this library does not ship, stays silent.</para></summary>
public sealed class MemoryWiringReport
{
    internal MemoryWiringReport(IReadOnlyList<MemoryWiringFinding> findings) => Findings = findings;

    /// <summary>Every finding, in a stable order; empty when the check found nothing.</summary>
    public IReadOnlyList<MemoryWiringFinding> Findings { get; }

    /// <summary>Whether <see cref="Findings"/> is empty.</summary>
    public bool IsClean => Findings.Count == 0;
}

/// <summary>One thing the wiring check found.</summary>
/// <param name="Problem">What kind of problem it is — the field to branch on.</param>
/// <param name="Engine">The engine it is about, by the name <see cref="IMemoryEngineFactory.Get(string)"/>
/// resolves (a blend's member by its hierarchical name); <c>null</c> for a finding about the container as a
/// whole.</param>
/// <param name="Message">The finding as a finished sentence, naming the fix — the text that is logged. Its wording
/// is not a contract; branch on <paramref name="Problem"/>.</param>
public sealed record MemoryWiringFinding(MemoryWiringProblem Problem, string? Engine, string Message)
{
    /// <summary>The <see cref="Message"/>.</summary>
    public override string ToString() => Message;
}

/// <summary>The kinds of wiring the check reports. Values may be added.</summary>
public enum MemoryWiringProblem
{
    /// <summary>A blend member no write can reach: every grade it supports is claimed by an earlier member and the
    /// blend routes each write to the first capable one.</summary>
    UnwritableMember = 1,

    /// <summary>A graph member embeds every write, and no seed source declaring
    /// <see cref="Seeding.MemorySeedKind.Semantic"/> lets a recall read those vectors.</summary>
    EmbedsWithoutSemanticSeeding = 2,

    /// <summary>A graph member annotates every write with subjects, and no seed source declaring
    /// <see cref="Seeding.MemorySeedKind.Subject"/> lets a recall read them.</summary>
    AnnotatesWithoutSubjectSeeding = 3,

    /// <summary>An <see cref="Verification.IMemoryVerificationPolicy"/> is registered and no engine consults one.</summary>
    VerificationUnconsulted = 4,

    /// <summary>An <see cref="Annotation.IMemoryAnnotationPolicy"/> is registered and no engine consults one.</summary>
    AnnotationUnconsulted = 5,

    /// <summary>A graph member pins <see cref="GraphMemoryOptions.VectorProviderId"/> and has no
    /// <see cref="IVectorStore"/>, so nothing it writes is ever embedded.</summary>
    PinnedVectorProviderWithoutIndex = 6,
}
