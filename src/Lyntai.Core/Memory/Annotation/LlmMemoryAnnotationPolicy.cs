using System.Text.Json;
using Lyntai.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Annotation;

/// <summary>Knobs for <see cref="LlmMemoryAnnotationPolicy"/>.</summary>
public sealed class LlmAnnotationOptions
{
    /// <summary>The named <see cref="ILlmClient"/> to annotate with (<c>AddLlmClient</c>). Null uses the
    /// default client.
    /// <para>Naming one is the point of the factory existing: annotation runs on EVERY write, so it belongs
    /// on a small fast backend rather than on whichever model the application made default for chat.</para>
    /// </summary>
    public string? ClientName { get; set; }

    /// <summary>Model id, or null for the backend's own default. Deliberately not defaulted to any
    /// particular model — which model runs is a DEPLOYMENT choice, never part of a feature's definition.
    /// <para><b>Whatever is chosen must be MULTILINGUAL if the application stores non-Latin text.</b> This
    /// library detects no language and passes content through verbatim, so an English-only model silently
    /// becomes the thing that decides whether Chinese facts get linked.</para>
    /// <para><b>A CANDIDATE that pins a model OUTRANKS this, so setting it is not a guarantee.</b> The
    /// router resolves <c>candidate.Model ?? request.Model</c>, and <c>docs/DECISIONS.md</c> <b>D87</b>
    /// derives a named client's candidates from <c>LyntaiOptions.DefaultCandidates</c>, keeping any model
    /// pinned there — so on a deployment that pins models globally this is inert, silently, because
    /// annotation is fail-open. <b>To pin the annotator's model with certainty, name a client whose
    /// candidates pin it</b> (<see cref="ClientName"/>) rather than setting this.</para></summary>
    public string? Model { get; set; }

    /// <summary>The most subjects to accept from one reply. Bounds how many edges one write can create when
    /// a model returns an over-eager list.</summary>
    public int MaxSubjects { get; set; } = 4;

    /// <summary>Whether the model may suggest a grade. Off by default: a suggested grade decides whether a
    /// fact is admitted UNCONDITIONALLY for the rest of its life, which is a larger promise than "these two
    /// facts are related" and deserves to be opted into separately.</summary>
    public bool SuggestGrade { get; set; }
}

/// <summary>
/// Annotates a fact by asking a model what it is ABOUT — the shipped
/// <see cref="IMemoryAnnotationPolicy"/>.
///
/// <para><b>Why a model rather than anything cheaper.</b> The facts that need connecting share no
/// distinguishing word — "my spouse is Alice", "she works as an anaesthetist", "we met in Kyoto" have only
/// pronouns in common, in any language. That is a semantic judgement no tokenizer or ranking change reaches,
/// and being the SAME judgement in every language is what routes it around CJK tokenization rather than
/// adding to it.</para>
///
/// <para><b>The prompt names no language and gives no examples in one.</b> An English-shaped instruction
/// would reintroduce the bias this subsystem spent a release removing: a Chinese fact would get an English
/// subject, a later one a Chinese subject, and the two would not link. Subjects are asked for IN THE
/// LANGUAGE OF THE FACT, so the same entity yields the same handle whatever the content's language.</para>
///
/// <para><b>Stability across writes is what the mechanism rests on</b>, so the prompt asks for short,
/// reusable handles rather than descriptions: two facts link because their subjects MATCH, and a model that
/// phrases the same entity differently each time links nothing while looking like it worked.</para>
///
/// <para><b>Fail-open, always.</b> Any failure yields <see cref="MemoryAnnotation.None"/>, which the engine
/// treats exactly as having no annotator — memory that stops accepting facts because a model is down is
/// worse than memory with no model.</para>
/// </summary>
/// <param name="clients">Resolves the configured client by name.</param>
/// <param name="options">Knobs; null takes the defaults.</param>
/// <param name="logger">Null logs nothing.</param>
public sealed class LlmMemoryAnnotationPolicy(
    ILlmClientFactory clients,
    LlmAnnotationOptions? options = null,
    ILogger<LlmMemoryAnnotationPolicy>? logger = null) : IMemoryAnnotationPolicy
{
    private readonly LlmAnnotationOptions _options = options ?? new LlmAnnotationOptions();
    private readonly ILogger _logger = logger ?? NullLogger<LlmMemoryAnnotationPolicy>.Instance;

    /// <summary>The instruction. Deliberately language-neutral and example-free — see the type's own remarks
    /// on why an English-shaped prompt would reintroduce the bias this subsystem exists to avoid.</summary>
    private const string System = """
        You label a fact with the entities or topics it is about, so that later facts about the same thing
        can be connected.

        Reply with ONLY a JSON object: {"subjects":["..."]}

        Rules:
        - REUSE an existing subject whenever the fact concerns the same thing. Copy it EXACTLY. Inventing a
          new name for something already listed is the main way this fails: two facts connect only when
          their subjects are identical.
        - Only invent a subject when the fact is about something not already listed.
        - Each subject is a SHORT, STABLE handle for one entity or topic — the same handle you would produce
          again for any other fact about that same thing. Prefer the enduring thing over the passing detail:
          a fact about a person's job is about the PERSON, not the job; a fact about where two people met is
          about the PEOPLE, not the place.
        - Write each subject in the SAME LANGUAGE as the fact. Never translate.
        - Resolve pronouns using the earlier facts when they are given.
        - Return an empty list if the fact is about nothing worth connecting.
        """;

    /// <summary>The extra instruction used ONLY when <see cref="LlmAnnotationOptions.SuggestGrade"/> is on.
    /// <para>Kept separate rather than folded into <see cref="System"/> because the option defaults OFF and a
    /// prompt asking for a field nobody reads is waste — worse, it invites the model to volunteer a grade
    /// this policy would then silently discard.</para>
    /// <para><b>It exists at all because the parser read a <c>grade</c> the prompt never requested.</b>
    /// <see cref="System"/> was a const used unconditionally and mentioned only <c>subjects</c>, so
    /// <c>SuggestGrade = true</c> could never fire against a real model while every offline test passed —
    /// the scripted reply supplied a grade nothing had asked for. The wording names the exact token the
    /// parser compares against, so the model is told what to send rather than left to guess a synonym.</para>
    /// </summary>
    private const string GradeInstruction = """

        Also judge whether the fact is an EXACT, durable fact that should always be available verbatim —
        a definition, an identifier, a rule, a stable preference — as opposed to an ordinary observation
        that may fade. Add one more field to the JSON object: {"subjects":["..."],"grade":"authoritative"}
        Use the value "authoritative" for such a fact, and omit the field entirely otherwise. Be sparing:
        an authoritative fact is kept and returned in full for the rest of the memory's life.
        """;

    /// <inheritdoc />
    public async Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var client = _options.ClientName is { } name ? clients.Get(name) : clients.Get();
            var reply = await client.CompleteAsync(new LlmRequest
            {
                Messages =
                [
                    new LlmMessage("system", _options.SuggestGrade ? System + GradeInstruction : System),
                    new LlmMessage("user", Compose(request)),
                ],
                Model = _options.Model,
                // Tagged so memory's spend is separable in the ledger. It was the library's only untagged
                // internal caller besides the verifier — scoring and chat both tag — so memory billed to
                // "default" and could not be capped or observed apart from the app's own traffic.
                Consumer = LlmConsumers.Memory,
                // Same reasoning, and the same measured stakes, as LlmMemoryVerificationPolicy's own
                // Suppress: this call's value is a short structured label, and it sits in the latency path
                // of every WRITE — the higher-traffic seam of the two, since a store takes many more writes
                // than a caller takes recalls. It was missing here while verification had it (found
                // 2026-08-15); a thinking model spent ~25s per judgement against ~1.5s for one that answers
                // directly (docs/memory.md §5). Advisory: a backend that cannot express it ignores it, and
                // the parser below still tolerates a reply that reasons anyway.
                Reasoning = LlmReasoning.Suppress,
            }, ct).ConfigureAwait(false);

            if (reply.Verdict != LlmVerdict.Ok || string.IsNullOrWhiteSpace(reply.Text))
            {
                _logger.LogDebug("annotation returned {Verdict}; storing without subjects", reply.Verdict);
                return MemoryAnnotation.None;
            }

            return Parse(reply.Text);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // FAIL-OPEN: the engine treats this exactly as having no annotator
            _logger.LogWarning(ex, "annotation failed; storing without subjects");
            return MemoryAnnotation.None;
        }
    }

    /// <summary>Earlier facts first, then the one being labelled — the order a reader needs to resolve a
    /// pronoun, and the order the instruction above describes.</summary>
    private static string Compose(MemoryAnnotationRequest request)
    {
        var parts = new List<string>();

        // Existing handles FIRST: the instruction's primary rule is to reuse one, and a model reads what it
        // is asked to choose from before what it is asked to classify.
        if (request.Known.Count > 0)
            parts.Add("Existing subjects — reuse one of these when it fits, copied exactly:\n"
                + string.Join("\n", request.Known.Select(k => "- " + k)));

        if (request.Recent.Count > 0)
            parts.Add("Earlier facts, oldest last:\n"
                + string.Join("\n", request.Recent.Select(r => "- " + r)));

        parts.Add("Fact:\n" + request.Write.Content);
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Reads the subjects out of a reply, tolerating the wrappers a model adds around JSON.
    ///
    /// <para><b>Anything unparseable is NO OPINION, never a guess.</b> Salvaging a malformed reply — taking
    /// the first line, splitting on commas — would invent subjects the model did not commit to, and a wrong
    /// subject links two unrelated facts PERMANENTLY. A missed link costs one recall; a wrong one corrupts
    /// the graph, so the asymmetry decides the behaviour.</para>
    /// </summary>
    private MemoryAnnotation Parse(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return MemoryAnnotation.None;

        try
        {
            using var json = JsonDocument.Parse(text[start..(end + 1)]);
            if (!json.RootElement.TryGetProperty("subjects", out var subjects)
                || subjects.ValueKind != JsonValueKind.Array)
                return MemoryAnnotation.None;

            var handles = subjects.EnumerateArray()
                .Where(s => s.ValueKind == JsonValueKind.String)
                .Select(s => s.GetString()!.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(0, _options.MaxSubjects))
                .ToList();

            var grade = _options.SuggestGrade
                && json.RootElement.TryGetProperty("grade", out var g)
                && g.ValueKind == JsonValueKind.String
                && string.Equals(g.GetString(), "authoritative", StringComparison.OrdinalIgnoreCase)
                    ? MemoryGrade.Authoritative
                    : (MemoryGrade?)null;

            return handles.Count == 0 && grade is null ? MemoryAnnotation.None
                : new MemoryAnnotation(handles, grade);
        }
        catch (JsonException)
        {
            _logger.LogDebug("annotation reply was not JSON; storing without subjects");
            return MemoryAnnotation.None;
        }
    }
}
