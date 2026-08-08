using Lyntai.Cortex;

namespace Lyntai.Memory;

/// <summary>Backs the existing <see cref="IPromptComposer"/> with a named engine, so
/// <c>ChatOrchestrator</c> composes from it — with the authoritative/associative split and the reserved
/// budget — instead of from the flat memory dump.</summary>
/// <param name="engine">The engine to compose from.</param>
/// <param name="options">Its composition budget and headings.</param>
public sealed class EngineBackedPromptComposer(IMemoryEngine engine, MemoryCompositionOptions options)
    : IPromptComposer
{
    /// <inheritdoc />
    public Task<string> ComposeAsync(string basePrompt, string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default) =>
        engine.ComposeAsync(basePrompt, new MemoryQuery(taskKey, scope, query, limit), options, ct);
}
