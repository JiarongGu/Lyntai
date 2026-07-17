using Lyntai.Llm;

namespace Lyntai.Agents;

/// <summary>
/// A provider-agnostic tool-calling (ReAct-style) loop over the <see cref="ILlmClient"/> front door:
/// it renders the registered tools into the prompt, asks the model to either call a tool or finish,
/// executes the chosen tool, feeds the observation back, and repeats until the model finishes or the
/// iteration budget is hit. Because it runs over the text contract, it works with <em>any</em>
/// provider (CLI, HTTP, MEAI bridge, local) — no native tool-calling support required, so it fits the
/// "Lyntai behaves like one provider" principle.
/// </summary>
public interface IToolLoop
{
    /// <summary>Run the loop for <paramref name="req"/> (its messages are the task; the tools come from
    /// the registry, not <see cref="LlmRequest.Tools"/>). <paramref name="maxIterations"/> overrides the
    /// configured default budget.</summary>
    Task<ToolLoopResult> RunAsync(LlmRequest req, int? maxIterations = null, CancellationToken ct = default);
}
