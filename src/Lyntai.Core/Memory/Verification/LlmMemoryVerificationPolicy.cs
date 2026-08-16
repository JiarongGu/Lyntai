using System.Globalization;
using System.Text.Json;
using Lyntai.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Verification;

/// <summary>Knobs for <see cref="LlmMemoryVerificationPolicy"/>.</summary>
public sealed class LlmVerificationOptions
{
    /// <summary>The named <see cref="ILlmClient"/> to judge with (<c>AddLlmClient</c>). Null uses the
    /// default client.
    /// <para>Naming one matters more here than anywhere else in this library, because verification runs on
    /// every recall and sits in the latency path of an answer — so the judge should be sized DELIBERATELY
    /// rather than inherited from whichever model the application made default for chat. Which way to size
    /// it is the deployment's call: measured recall quality rises sharply with judge capability (3B captured
    /// ~60-69% of the available improvement, 7B captured 91.4%), so this is a genuine cost/quality trade and
    /// not a case where the cheapest option is obviously right.</para></summary>
    public string? ClientName { get; set; }

    /// <summary>Model id, or null for the backend's own default. Deliberately not defaulted to any
    /// particular model — which model runs is a DEPLOYMENT choice, never part of a feature's definition.
    /// <para><b>Whatever is chosen must be MULTILINGUAL if the application stores non-Latin text.</b> This
    /// library detects no language and passes both query and headlines through verbatim, so an English-only
    /// model silently becomes the thing that decides which Chinese memories are worth returning.</para>
    /// </summary>
    public string? Model { get; set; }
}

/// <summary>
/// Judges which of a recall's candidates answered the query, by asking a model — the shipped
/// <see cref="IMemoryVerificationPolicy"/>.
///
/// <para><b>Why this seam is worth a model call when nothing else in the memory path is.</b> Over a full
/// corpus replay, <b>every</b> relevant entry a recall failed to return was a reachable candidate the
/// ranking put below the limit — the miss rate is a ranking failure end to end. The two shipped model-free
/// ranking policies return byte-identical results on that corpus, so policy choice has no headroom left;
/// reordering by something that can actually read the query is the remaining lever. Measured with a PERFECT
/// judge (an oracle on the corpus's own ground truth, so a ceiling no model exceeds) it cut miss
/// <c>0.5357 → 0.2857</c> and pollution <c>0.3331 → 0.1549</c>.</para>
///
/// <para><b>The prompt names no language and gives no examples in one</b> — the same rule
/// <see cref="Lyntai.Memory.Annotation.LlmMemoryAnnotationPolicy"/> follows: "did this answer that" is the
/// same judgement in every language, and an English-shaped instruction would reintroduce the bias this
/// subsystem spent a release removing.</para>
///
/// <para><b>Fail-open, and asymmetrically so.</b> Any failure — refusal, timeout, unparseable output, an id
/// the model invented — yields <see cref="MemoryVerification.NoOpinion"/>, never
/// <see cref="MemoryVerification.NothingRelevant"/>. Those are opposite instructions: no opinion leaves the
/// ranking alone, while "nothing was relevant" would teach the engine the recall failed. Collapsing them
/// would let a model outage unlearn the corpus, which is the worst thing a best-effort seam here could
/// do.</para>
/// </summary>
/// <param name="clients">Resolves the configured client by name.</param>
/// <param name="options">Knobs; null takes the defaults.</param>
/// <param name="logger">Null logs nothing.</param>
public sealed class LlmMemoryVerificationPolicy(
    ILlmClientFactory clients,
    LlmVerificationOptions? options = null,
    ILogger<LlmMemoryVerificationPolicy>? logger = null) : IMemoryVerificationPolicy
{
    private readonly LlmVerificationOptions _options = options ?? new LlmVerificationOptions();
    private readonly ILogger _logger = logger ?? NullLogger<LlmMemoryVerificationPolicy>.Instance;

    /// <summary>The instruction. Language-neutral and example-free — see the type's own remarks.</summary>
    private const string System = """
        You decide which stored notes actually answer a question.

        You are given a question and a numbered list of one-line notes. Reply with ONLY a JSON object:
        {"relevant":[1,4,7]}

        Rules:
        - List the numbers of the notes that genuinely help answer the question, best first.
        - Judge the NOTE against the QUESTION only. Do not use the order they are given in — that order is
          what you are correcting.
        - Be selective. A note that merely shares words with the question, or is about the same general
          area without answering it, is NOT relevant.
        - Return an empty list if none of them answer the question. That is a useful answer, not a failure.
        - Never invent a number that is not in the list.
        - The question and the notes may be in any language, and may be in different ones. Judge meaning,
          never spelling.
        """;

    /// <inheritdoc />
    public async Task<MemoryVerification> VerifyAsync(MemoryVerificationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Candidates.Count == 0 || string.IsNullOrWhiteSpace(request.Query))
            return MemoryVerification.NoOpinion;

        try
        {
            var client = _options.ClientName is { } name ? clients.Get(name) : clients.Get();
            var reply = await client.CompleteAsync(new LlmRequest
            {
                Messages =
                [
                    new LlmMessage("system", System),
                    new LlmMessage("user", Compose(request)),
                ],
                Model = _options.Model,
                // Tagged for the same reason the annotator is: this one fires on EVERY recall, and
                // `docs/memory.md` prices a hosted judge in dollars per thousand recalls, so it is precisely
                // the spend an operator needs to see and cap on its own.
                Consumer = LlmConsumers.Memory,
                // This call's value is a short structured verdict, and it sits in the latency path of every
                // recall — so it asks for no intermediate reasoning. Advisory: a backend that cannot
                // express it ignores it, and Parse below still tolerates a reply that reasons anyway.
                // Measured stakes: a thinking model spent ~25 s per judgement against ~1.5 s for one that
                // answers directly (docs/DECISIONS.md D59).
                Reasoning = LlmReasoning.Suppress,
            }, ct).ConfigureAwait(false);

            if (reply.Verdict != LlmVerdict.Ok || string.IsNullOrWhiteSpace(reply.Text))
            {
                _logger.LogDebug("verification returned {Verdict}; leaving the ranking alone", reply.Verdict);
                return MemoryVerification.NoOpinion;
            }

            return Parse(reply.Text, request);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // FAIL-OPEN, to NoOpinion and never to NothingRelevant — see the type's own remarks.
            _logger.LogWarning(ex, "verification failed; leaving the ranking alone");
            return MemoryVerification.NoOpinion;
        }
    }

    /// <summary>Question first, then the numbered notes.
    /// <para><b>Numbers rather than the engine's own ids.</b> A store id is a long that carries no meaning,
    /// costs tokens, and invites a model to echo a plausible-looking one it never saw. Small ordinals are
    /// cheap, and an out-of-range ordinal is trivially detectable — which is what
    /// <see cref="Parse"/> relies on.</para></summary>
    private static string Compose(MemoryVerificationRequest request)
    {
        var notes = request.Candidates
            .Select((c, i) => string.Create(CultureInfo.InvariantCulture, $"{i + 1}. {c.Headline}"));

        return $"Question:\n{request.Query}\n\nNotes:\n{string.Join("\n", notes)}";
    }

    /// <summary>Reads the ordinals out of a reply and maps them back to ids, tolerating the wrappers a model
    /// puts around JSON.
    /// <para><b>An out-of-range or duplicate ordinal is DROPPED, not fatal.</b> A model that returns one
    /// good index and one hallucinated one has still told us something true; discarding the whole reply
    /// would throw that away. But a reply where NOTHING parsed is <see cref="MemoryVerification.NoOpinion"/>
    /// rather than an empty judgement — unparseable is not the same as "none were relevant".</para></summary>
    private MemoryVerification Parse(string text, MemoryVerificationRequest request)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return MemoryVerification.NoOpinion;

        int[]? ordinals;
        try
        {
            using var doc = JsonDocument.Parse(text.AsSpan(start, end - start + 1).ToString());
            if (!doc.RootElement.TryGetProperty("relevant", out var array)
                || array.ValueKind != JsonValueKind.Array)
                return MemoryVerification.NoOpinion;

            ordinals = [.. array.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.Number)
                .Select(e => e.TryGetInt32(out var n) ? n : -1)];
        }
        catch (JsonException)
        {
            _logger.LogDebug("verification reply was not JSON; leaving the ranking alone");
            return MemoryVerification.NoOpinion;
        }

        // An EMPTY well-formed array is a real verdict: "none of these answered it". That is the
        // observation the review log could never previously contain, so it must survive parsing intact.
        var seen = new HashSet<int>();
        var ids = new List<string>(ordinals.Length);
        foreach (var n in ordinals)
        {
            if (n < 1 || n > request.Candidates.Count || !seen.Add(n)) continue;
            ids.Add(request.Candidates[n - 1].Id);
        }

        return ids.Count == 0 && ordinals.Length > 0
            ? MemoryVerification.NoOpinion   // every ordinal was junk — that is a broken reply, not a verdict
            : new MemoryVerification(ids);
    }
}
