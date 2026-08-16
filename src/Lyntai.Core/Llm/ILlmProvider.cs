namespace Lyntai.Llm;

/// <summary>One LLM backend (CLI spawn, HTTP endpoint, MEAI bridge, …). Implementations are
/// registered into DI as an <see cref="IEnumerable{ILlmProvider}"/> keyed by <see cref="Id"/>;
/// the router selects among them by candidate id — never an if/else over provider kinds.</summary>
public interface ILlmProvider : Lyntai.Lifecycle.IProviderIdentity
{
    /// <summary>Stable id the router matches candidates against ("claude-cli", "openai", "ollama", …).</summary>
    /// <remarks><b>Declared here as well as on <see cref="Lyntai.Lifecycle.IProviderIdentity"/> on purpose,
    /// and <c>new</c> only to silence CS0108.</b> Adding the base interface is binary-compatible; DELETING
    /// this declaration is not. A pre-compiled caller emits <c>callvirt ILlmProvider::get_Id</c>, and member
    /// resolution does not walk base INTERFACES, so every consumer assembly built against 1.0 would throw
    /// <see cref="MissingMethodException"/> until recompiled. Pinned by
    /// <c>ProviderIdentityTests.Both_seams_still_declare_Id_themselves</c> — do not "clean it up".</remarks>
    new string Id { get; }

    /// <summary>Cheap availability probe (binary on PATH, endpoint configured, …).</summary>
    bool IsAvailable { get; }

    /// <summary>Whether this provider supports native (structured) tool-calling — i.e. it sends
    /// <see cref="LlmRequest.Tools"/> to the model and surfaces the model's calls on
    /// <see cref="LlmReply.ToolCalls"/>. Default false; the <see cref="Agents.IToolLoop"/> uses its
    /// prompt-based fallback for providers that don't. Coarse (provider-level, not per-model): a model
    /// that ignores tools just answers in prose, which the loop treats as a final answer.</summary>
    bool SupportsToolCalls => false;

    /// <summary>Whether this provider's STREAM delivers native tool calls — i.e. <see cref="StreamAsync"/>
    /// yields <see cref="LlmChunkKind.ToolCall"/> chunks carrying complete calls. Default false, and the
    /// default is the safe answer: a provider that does native tool-calling but has not implemented the
    /// streaming half keeps <see cref="Agents.IToolLoop"/> on its buffered path, exactly as before 3.0.</summary>
    /// <remarks>SEPARATE from <see cref="SupportsToolCalls"/> because the two are genuinely independent: a
    /// provider can surface calls on <see cref="LlmReply.ToolCalls"/> while its stream drops them, which is
    /// what every provider here did until 3.0. Answering one for the other would make an agentic turn look
    /// like a plain answer — the model asks for a tool, no call chunk arrives, and the loop reports the empty
    /// prose as its final answer. That failure is silent, which is why this is its own question.</remarks>
    bool SupportsStreamingToolCalls => false;

    Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default);

    IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default);
}
