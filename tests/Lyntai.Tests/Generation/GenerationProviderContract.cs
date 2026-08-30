using System.Net;
using System.Text;

using Lyntai.Generation;

namespace Lyntai.Tests.Generation;

/// <summary>Backend-agnostic facts every <see cref="IGenerationProvider"/> satisfies.
///
/// <para><b>Why this file exists.</b> The five shipped backends had no shared contract: <c>GenerationContractTests</c>
/// pins the RECORD defaults and everything about BACKEND behaviour lived in per-backend files, which is the
/// shape <c>pitfalls.md</c> §"Second doors" names as the defect and which <c>VectorStoreContract</c> /
/// <c>MemoryGraphStoreContract</c> / <c>JobStoreContract</c> are the in-tree fix for. Two duplicated-reader
/// defects had already shipped past every per-backend test (an extension→MIME table missing entries on one
/// side, a scalar-id reader accepting a JSON number on one side only). Added 2026-08-17 (archive Part 86).</para>
///
/// <para>Each fact is the seam's OWN written promise. The two that carry the most: a backend is
/// <b>contractually fail-safe</b> — "a transport or backend failure is a verdict, never a throw" — and a
/// failure is CLASSIFIED rather than flattened, because the router acts on the difference (NotConfigured
/// skips blamelessly, AuthFailed benches the backend for the cooldown window, Failed advances and takes a
/// strike).</para></summary>
public static class GenerationProviderContract
{
    /// <summary>A backend is addressable and says what it can do. An empty id cannot be named as a candidate;
    /// an empty kind or delivery list makes the router's capability pre-filter exclude it from everything, so
    /// it would be registered and permanently unreachable.</summary>
    public static void It_declares_a_usable_identity(IGenerationProvider provider)
    {
        Assert.False(string.IsNullOrWhiteSpace(provider.Id));
        Assert.NotEmpty(provider.Capabilities.Kinds);
        Assert.NotEmpty(provider.Capabilities.Deliveries);
    }

    /// <summary><b>A declared delivery mode must be backed by the interface that serves it.</b> The router
    /// pre-filters on <see cref="GenerationCapabilities.Deliveries"/> and then casts, so declaring a mode the
    /// type does not implement is a configuration fault that surfaces at the worst moment — after a candidate
    /// has been selected and every alternative discarded. <c>GenerationRouter</c> names this case explicitly
    /// on its stream door (<c>docs/DECISIONS.md</c> D67).</summary>
    public static void Its_declared_deliveries_are_backed_by_the_interfaces_it_implements(
        IGenerationProvider provider)
    {
        foreach (var delivery in provider.Capabilities.Deliveries)
        {
            switch (delivery)
            {
                case GenerationDelivery.Job:
                    Assert.True(provider is IGenerationJobProvider,
                        $"{provider.Id} declares Job delivery but does not implement IGenerationJobProvider");
                    break;
                case GenerationDelivery.Stream:
                    Assert.True(provider is IGenerationStreamProvider,
                        $"{provider.Id} declares Stream delivery but does not implement IGenerationStreamProvider");
                    break;
                case GenerationDelivery.Inline:
                    break;   // served by IGenerationProvider itself, which every backend implements
            }
        }
    }

    /// <summary>A backend that cannot serve inline says so with <see cref="GenerationVerdict.Unsupported"/>
    /// rather than hiding a poll loop behind <c>GenerateAsync</c>. Unsupported is the one verdict the routing
    /// policy SURFACES rather than advancing on (<c>docs/DECISIONS.md</c> D3), because trying the next
    /// candidate cannot fix a capability mismatch.</summary>
    public static async Task An_inline_call_to_a_job_only_backend_is_Unsupported(
        IGenerationProvider provider, GenerationRequest ask)
    {
        if (provider.Capabilities.Deliveries.Contains(GenerationDelivery.Inline)) return;

        var result = await provider.GenerateAsync(ask);

        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
    }

    /// <summary><b>Contractually FAIL-SAFE: a backend failure is a verdict, never a throw.</b> The router's
    /// own trust boundary (D64) catches a throwing backend, but that exists for a BYO one — a shipped backend
    /// that threw would be the thing the boundary is defending against, and on the SUBMIT path a throw is
    /// reported <c>Inconclusive</c> and SURFACES rather than advancing, because submitting commits the spend.
    /// So a shipped backend that throws where it could have answered costs the caller its whole candidate
    /// chain.</summary>
    public static async Task A_backend_failure_is_a_verdict_rather_than_a_throw(
        IGenerationProvider provider, GenerationRequest ask)
    {
        var result = await provider.GenerateAsync(ask);

        Assert.NotEqual(GenerationVerdict.Ok, result.Verdict);
        Assert.False(string.IsNullOrWhiteSpace(result.Detail),
            $"{provider.Id} reported {result.Verdict} with no detail — the caller is told nothing actionable");
    }

    /// <summary><b>An authentication rejection is CLASSIFIED, never flattened to
    /// <see cref="GenerationVerdict.Failed"/>.</b> The two acceptable answers are
    /// <see cref="GenerationVerdict.AuthFailed"/> (credentials were rejected) and
    /// <see cref="GenerationVerdict.NotConfigured"/> (none were supplied — a backend nobody has set up yet
    /// must not be penalised on every attempt for a fact known before the call). <c>Failed</c> is neither: it
    /// makes the router advance AND take a dead-host strike, benching a backend whose only problem is a
    /// missing key, and it tells a host nothing it can act on.
    /// <para>This is the fact the divergence that prompted this contract would have failed:
    /// <c>ComfyUiProvider.FetchCoreAsync</c> hardcoded <c>Failed</c> for every failed history read while
    /// <c>FalQueueProvider</c> routed the same class through <c>GenerationVerdictClassifier</c>, so the same
    /// authenticating proxy in front of each produced different verdicts.</para></summary>
    public static void An_authentication_failure_is_classified_rather_than_flattened(
        string door, string providerId, GenerationVerdict verdict) =>
        Assert.True(
            verdict is GenerationVerdict.AuthFailed or GenerationVerdict.NotConfigured,
            $"{providerId}'s {door} reported {verdict} for a 401 — expected AuthFailed (rejected) or "
            + "NotConfigured (never supplied). Failed both benches the backend and tells the host nothing.");

    /// <summary>The caller's own cancellation propagates as an <see cref="OperationCanceledException"/> and is
    /// never turned into a verdict. Both clocks arrive as the same exception type, so a backend that could not
    /// tell them apart would report a caller's deliberate stop as a backend timeout — and on the submit path
    /// that reads as "the backend may hold a billable render", which is the expensive direction.</summary>
    public static async Task Caller_cancellation_propagates_rather_than_becoming_a_verdict(
        IGenerationProvider provider, GenerationRequest ask)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GenerateAsync(ask, cancelled.Token));
    }

    /// <summary><b>A declared INPUT capability must be backed by code that CONSUMES it.</b> The sibling of
    /// <see cref="Its_declared_deliveries_are_backed_by_the_interfaces_it_implements"/>, one axis over:
    /// <see cref="GenerationCapabilities.SupportsInputs"/> is not advisory, because
    /// <see cref="GenerationCapabilities.Supports"/> uses it as an ADMISSION filter. Declaring it is a promise
    /// to the router that this backend reads <see cref="GenerationRequest.Inputs"/>, so a backend that
    /// declares it and ignores them is handed the chained artifact and drops it in silence.
    /// <para>Two answers are acceptable and one is not. SENDING NOTHING is honest — that is a refusal, and
    /// <c>FalQueueProvider</c> refuses a bytes-only input exactly this way. Sending a request that carries the
    /// input is honest. Sending a request that does NOT carry it is the defect: the render is billed, the
    /// result comes back plausible, and nothing in it says the caller's image was discarded.</para>
    /// <para>A backend declaring <c>false</c> is out of scope here and guarded by <c>Supports</c> instead —
    /// the router never routes it an input-carrying request in the first place.</para></summary>
    public static void A_handed_input_is_consumed_or_refused(
        string providerId, IReadOnlyCollection<string> sentBodies, byte[] marker)
    {
        if (sentBodies.Count == 0) return;   // refused before spending anything

        var raw = Encoding.ASCII.GetString(marker);
        var encoded = Convert.ToBase64String(marker);

        Assert.True(
            sentBodies.Any(body => body.Contains(raw, StringComparison.Ordinal)
                || body.Contains(encoded, StringComparison.Ordinal)),
            $"{providerId} declares SupportsInputs and SENT a request for one carrying an input, but neither "
            + "the input's bytes nor their base64 appear in what it sent — the input was silently dropped. "
            + "Either consume it or refuse before the call; dropping it bills a render the caller did not ask "
            + "for and returns a plausible-looking wrong result.");
    }

    /// <summary>A 401 response, for the auth facts above.</summary>
    public static HttpResponseMessage Unauthorized() =>
        new(HttpStatusCode.Unauthorized) { Content = new StringContent("""{"error":"invalid api key"}""") };
}
