using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Salience;
using Lyntai.Memory.Verification;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>Reports the same salience for every write.
/// <para>Declares its own provenance bit from the consumer range (32-62), never <c>None</c>: the engine
/// refuses a policy declaring <c>None</c>, since every running policy has an identity. Two instances sharing
/// the bit are not a collision — "did this policy run" stays unambiguous however many instances did.</para></summary>
internal sealed class FixedSaliencePolicy(double salience) : IMemorySaliencePolicy
{
    public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 32);

    public MemorySignals Signals(MemoryWrite write, in SalienceContext context) =>
        MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, salience);
}

/// <summary>Records every write and context a salience policy is shown, and judges nothing. Declares a
/// consumer-range provenance bit for the reason <see cref="FixedSaliencePolicy"/> gives.</summary>
internal sealed class CapturingSalience : IMemorySaliencePolicy
{
    public List<MemoryWrite> Writes { get; } = [];

    public List<SalienceContext> Contexts { get; } = [];

    public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 32);

    public MemorySignals Signals(MemoryWrite write, in SalienceContext context)
    {
        Writes.Add(write);
        Contexts.Add(context);
        return MemorySignals.Empty;
    }
}

/// <summary>Lengthens every half-life by <paramref name="factor"/>, declaring <paramref name="declaredMax"/>
/// (or the factor itself) as its maximum — so a test can make a policy report MORE than it declared.</summary>
internal sealed class FixedRetentionPolicy(double factor, double? declaredMax = null) : IMemoryRetentionPolicy
{
    public string Name => "fixed";

    public double MaxStabilityFactor => declaredMax ?? factor;

    public double StabilityFactor(in MemoryDecayState state) => factor;
}

/// <summary>Exact text to exact vector; anything unscripted is orthogonal to the scripted axes, so an
/// accidental match cannot pass a test.</summary>
internal sealed class MapVectorProvider(IReadOnlyDictionary<string, float[]> map) : FakeVectorProviderBase
{
    public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<float[]>>(
            [.. texts.Select(t => map.TryGetValue(t, out var v) ? v : new[] { 0f, 0f, 1f })]);
}

/// <summary>Captures the verification request verbatim and judges nothing, so ordering is unchanged and the
/// only thing under test is what the engine handed over.</summary>
internal sealed class CapturingVerification : IMemoryVerificationPolicy
{
    public MemoryVerificationRequest? Last { get; private set; }

    public Task<MemoryVerification> VerifyAsync(MemoryVerificationRequest request, CancellationToken ct = default)
    {
        Last = request;
        return Task.FromResult(MemoryVerification.NoOpinion);
    }
}

/// <summary>A verifier that knows the answer: taught, per query text, which engine ids are relevant. No
/// opinion on a query it was not taught; NothingRelevant when none of the candidates is.</summary>
internal sealed class OracleVerifier : IMemoryVerificationPolicy
{
    private readonly Dictionary<string, HashSet<string>> _truth = new(StringComparer.Ordinal);

    public void Teach(string queryText, IEnumerable<string> relevantEngineIds) =>
        _truth[queryText] = [.. relevantEngineIds];

    public Task<MemoryVerification> VerifyAsync(MemoryVerificationRequest request, CancellationToken ct = default)
    {
        if (!_truth.TryGetValue(request.Query, out var relevant))
            return Task.FromResult(MemoryVerification.NoOpinion);

        var hits = request.Candidates.Select(c => c.Id).Where(relevant.Contains).ToList();
        return Task.FromResult(hits.Count == 0
            ? MemoryVerification.NothingRelevant
            : new MemoryVerification(hits));
    }
}

/// <summary>Annotates from a fixed content-to-subjects table — a model that answers perfectly by
/// construction, so a failure is the engine's. Counts its calls and records how much context each was
/// shown, and suggests <paramref name="grade"/> when one is given.</summary>
internal sealed class TableAnnotator(Dictionary<string, string[]> subjectsByContent, MemoryGrade? grade = null)
    : IMemoryAnnotationPolicy
{
    /// <summary>One content, one subject.</summary>
    public TableAnnotator(string content, string subject)
        : this(new Dictionary<string, string[]>(StringComparer.Ordinal) { [content] = [subject] })
    {
    }

    public int Calls { get; private set; }

    public List<int> ContextSizes { get; } = [];

    public Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request, CancellationToken ct = default)
    {
        Calls++;
        ContextSizes.Add(request.Recent.Count);
        return Task.FromResult(subjectsByContent.TryGetValue(request.Write.Content, out var subjects)
            ? new MemoryAnnotation(subjects, grade)
            : MemoryAnnotation.None);
    }
}
