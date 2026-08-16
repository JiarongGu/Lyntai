using Lyntai.Memory;
using Lyntai.Memory.Annotation;

namespace Lyntai.Tests.Memory;

/// <summary>Policy-agnostic facts every <see cref="IMemoryAnnotationPolicy"/> satisfies.
///
/// <para><b>Why a contract, when the shipped implementation calls a model.</b> Every promise below is about
/// what happens when the model answers BADLY or not at all — which is precisely the part a caller depends on
/// and the part no live test can force. So the contract is model-free even though its implementations are
/// not: the driver supplies a working policy and a FAILING one, and the facts hold on both.</para>
///
/// <para><b>Best-effort over a model-free floor.</b> Graph memory works with no annotator at all, so a
/// failing or absent one must degrade to "no new links" — never to a failed write and never to no memory.
/// A policy that threw would take a whole remember down for a judgement the engine was prepared to do
/// without.</para>
///
/// <para>Added 2026-08-17 by the pre-3.0 sweep (archive Part 86), with the other three seams that had
/// none.</para></summary>
public static class MemoryAnnotationPolicyContract
{
    private static MemoryAnnotationRequest Request(
        IReadOnlyList<string>? recent = null, IReadOnlyList<string>? known = null) =>
        new(new MemoryWrite("t", "s", "my spouse is Alice"), recent ?? [], known ?? []);

    /// <summary>Never null, and never a null <c>Subjects</c> list. Stated on the member's own
    /// <c>&lt;returns&gt;</c>: "Never null; an implementation with no opinion returns
    /// <see cref="MemoryAnnotation.None"/>". The engine dereferences the result without a check, because that
    /// promise is what lets it.</summary>
    public static async Task It_never_returns_null(IMemoryAnnotationPolicy policy)
    {
        var annotation = await policy.AnnotateAsync(Request());

        Assert.NotNull(annotation);
        Assert.NotNull(annotation.Subjects);
    }

    /// <summary><b>EMPTY CONTEXT IS NOT AN ERROR.</b> <c>Recent</c> is documented as "empty on the first
    /// write, and empty when the engine cannot read them, which an implementation must tolerate rather than
    /// treat as 'no context exists'" — so the very first fact a fresh engine ever stores arrives with nothing
    /// alongside it. A policy that required context would fail exactly once per engine, on the write that
    /// starts every cluster.</summary>
    public static async Task It_tolerates_a_first_write_with_no_context_at_all(IMemoryAnnotationPolicy policy)
    {
        var annotation = await policy.AnnotateAsync(Request(recent: [], known: []));

        Assert.NotNull(annotation);
        Assert.NotNull(annotation.Subjects);
    }

    /// <summary><b>FAIL-OPEN: a broken policy yields no opinion, never an exception.</b> The one fact here
    /// that a live test cannot produce on demand and the one whose violation is worst — memory that stops
    /// accepting facts because a model is down is worse than memory with no model at all.
    /// <para>The driver supplies a policy it has broken however its implementation can be broken (for the
    /// shipped one, an <c>ILlmClient</c> that throws). That indirection is the point: the promise is about
    /// the SEAM, and each implementation knows its own failure mode.</para></summary>
    public static async Task A_failing_policy_yields_no_opinion_rather_than_throwing(
        IMemoryAnnotationPolicy failing)
    {
        var annotation = await failing.AnnotateAsync(Request(recent: ["she works at a hospital"]));

        Assert.NotNull(annotation);
        Assert.Empty(annotation.Subjects);
        Assert.Null(annotation.Grade);
    }

    /// <summary>Cancellation is never swallowed. A remember runs inside the caller's own token, and an
    /// annotator that turned a cancel into "no opinion" would let a cancelled write complete — the caller
    /// asked to stop, and a fail-open rule for MODEL failures must not quietly extend to that.</summary>
    public static async Task Cancellation_propagates_rather_than_becoming_no_opinion(
        IMemoryAnnotationPolicy policy)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => policy.AnnotateAsync(Request(), cancelled.Token));
    }
}
