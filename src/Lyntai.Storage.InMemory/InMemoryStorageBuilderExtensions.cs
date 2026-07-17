using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Lives in the Lyntai namespace so `UseInMemoryStorage` shows up right on the builder.
namespace Lyntai;

public static class InMemoryStorageBuilderExtensions
{
    /// <summary>Wire every storage domain to zero-dependency in-memory stores — for tests, ephemeral
    /// use, or as one backend in a mixed composition. Uses <c>TryAdd</c>, so a domain already
    /// registered by another backend (e.g. <c>UseSqliteStorage</c>) wins: call this AFTER a partial
    /// backend to fill only the gaps, or alone for a fully in-memory stack.</summary>
    public static LyntaiBuilder UseInMemoryStorage(this LyntaiBuilder builder)
    {
        builder.Services.TryAddSingleton<IKeyValueStore, InMemoryKeyValueStore>();
        builder.Services.TryAddSingleton<IPromptVersionStore, InMemoryPromptVersionStore>();
        builder.Services.TryAddSingleton<IConversationStore, InMemoryConversationStore>();
        builder.Services.TryAddSingleton<IMemoryStore>(sp => new InMemoryMemoryStore(sp.GetRequiredService<LyntaiOptions>()));
        builder.Services.TryAddSingleton<IScoreStore, InMemoryScoreStore>();
        builder.Services.TryAddSingleton<ITraceStore, InMemoryTraceStore>();
        builder.Services.TryAddSingleton<IJobStore>(_ => new InMemoryJobStore());
        return builder;
    }
}
