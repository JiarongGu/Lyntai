using Lyntai.Cortex;
using Lyntai.Memory.Engines;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Lyntai.Memory;

/// <summary>Backs <see cref="IPromptComposer"/> with an engine, so <c>ChatOrchestrator</c> composes from it —
/// with the authoritative/associative split and the reserved budget — and remembers into the SAME engine.
/// <para>It is also the composer <c>AddLyntai</c> registers when no engine is named: a blend over whichever of
/// <see cref="IMemoryStore"/> and <see cref="ISemanticMemory"/> the container holds, keyword member first,
/// with every write fanned out to both.</para></summary>
/// <param name="engine">The engine to compose from and remember into.</param>
/// <param name="options">Its composition budget and headings.</param>
public sealed class EngineBackedPromptComposer(IMemoryEngine engine, MemoryCompositionOptions options)
    : IPromptComposer
{
    /// <summary>The engine name the container-default composer's blend carries.</summary>
    internal const string DefaultEngineName = "composer";

    /// <inheritdoc />
    public Task<string> ComposeAsync(string basePrompt, string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default) =>
        engine.ComposeAsync(basePrompt, new MemoryQuery(taskKey, scope, query, limit), options, ct);

    /// <inheritdoc />
    /// <remarks>Writes at <see cref="MemoryGrade.Inherit"/>, so the engine stores the exchange at its own
    /// role. An engine whose <see cref="IMemoryEngine.Supported"/> is <see cref="MemoryGrades.None"/> is
    /// read-only and is not written to.</remarks>
    public async Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default)
    {
        if (engine.Supported == MemoryGrades.None) return;
        await engine.RememberAsync(new MemoryWrite(taskKey, scope, content), ct).ConfigureAwait(false);
    }

    /// <summary>The composer <c>AddLyntai</c> registers by default. With neither store registered the blend is
    /// empty: it composes nothing and writes nothing.</summary>
    internal static EngineBackedPromptComposer ForContainer(IServiceProvider sp)
    {
        var members = new List<IMemoryEngine>(2);
        if (sp.GetService<IMemoryStore>() is { } store)
            members.Add(new LexicalMemoryEngine($"{DefaultEngineName}/lexical", store,
                sp.GetService<ILogger<LexicalMemoryEngine>>()));
        if (sp.GetService<ISemanticMemory>() is { } semantic)
            members.Add(new SemanticMemoryEngine($"{DefaultEngineName}/semantic", semantic,
                logger: sp.GetService<ILogger<SemanticMemoryEngine>>()));

        var blend = new CompositeMemoryEngine(DefaultEngineName, members,
            sp.GetService<ILogger<CompositeMemoryEngine>>())
        {
            WriteRouting = MemoryWriteRouting.EveryCapable,
        };
        return new EngineBackedPromptComposer(blend, new MemoryCompositionOptions());
    }
}
