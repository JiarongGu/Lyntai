using Lyntai.Llm;

namespace Lyntai.Agents;

/// <summary>
/// A tool-calling (ReAct-style) loop over the <see cref="ILlmClient"/> front door: it makes the
/// registered tools available to the model, executes the tool the model chooses, feeds the observation
/// back, and repeats until the model finishes or the iteration budget is hit. It prefers <b>native</b>
/// function-calling (when the provider supports it) and falls back to a <b>prompt protocol</b> over the
/// text contract for providers that don't — so it works with <em>any</em> provider (HTTP, MEAI bridge,
/// CLI, local) and stays behind the "Lyntai behaves like one provider" front door either way.
/// </summary>
public interface IToolLoop
{
    /// <summary>Run the loop for <paramref name="req"/> (its messages are the task; the tools come from
    /// the registry, not <see cref="LlmRequest.Tools"/>). <paramref name="maxIterations"/> overrides the
    /// configured default budget.</summary>
    Task<ToolLoopResult> RunAsync(LlmRequest req, int? maxIterations = null, CancellationToken ct = default);
}
