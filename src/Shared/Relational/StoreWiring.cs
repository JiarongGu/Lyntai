using Lyntai.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lyntai.Storage.Relational;

/// <summary>What one relational adapter hands the shared wiring: its storage method's name, the tables its
/// Governance migration carries, and a constructor per domain store.</summary>
internal sealed record RelationalBackend(
    string StorageMethod,
    string GovernanceTables,
    Func<IDbConnectionFactory, IKeyValueStore> KeyValue,
    Func<IDbConnectionFactory, IPromptVersionStore> PromptVersion,
    Func<IDbConnectionFactory, IConversationStore> Conversation,
    Func<IDbConnectionFactory, IServiceProvider, IMemoryStore> Memory,
    Func<IDbConnectionFactory, IServiceProvider, IMemoryGraphStore> MemoryGraph,
    Func<IDbConnectionFactory, IScoreStore> Score,
    Func<IDbConnectionFactory, ITraceStore> Trace,
    Func<IDbConnectionFactory, IServiceProvider, IJobStore> Jobs,
    Func<IDbConnectionFactory, IServiceProvider, ICuratedMemoryStore> CuratedMemory);

/// <summary>One wiring's feature selection and connection factory. Each adapter compiles its OWN copy of this
/// type, so the SQLite and Postgres selections are distinct services and never read each other's.</summary>
internal sealed record FeatureSelection(StorageFeature Features, bool LyntaiMigrates, IDbConnectionFactory Factory);

/// <summary>Registers one relational wiring into the container.</summary>
internal static class StoreWiring
{
    /// <summary>The selection (which the Governance guard and helpers read), the factory, and the selected
    /// features' stores.
    /// <para><b>Every store is built over THIS wiring's factory</b>, never one resolved from the container:
    /// the stores register first-wins, so an app's own registration wins, and a factory resolved at run time
    /// would hand the first wiring's stores whichever wiring registered last — another file's tables, or the
    /// other dialect's connection.</para></summary>
    public static LyntaiBuilder Wire(LyntaiBuilder builder, RelationalBackend backend, IDbConnectionFactory factory,
        StorageFeature features, bool lyntaiMigrates)
    {
        var selection = new FeatureSelection(features, lyntaiMigrates, factory);
        GovernanceGuard.Wired(builder, backend, selection);
        var services = builder.Services;
        services.AddSingleton(selection);
        services.TryAddSingleton(factory);
        if (features.HasFlag(StorageFeature.KeyValue)) services.TryAddSingleton(_ => backend.KeyValue(factory));
        if (features.HasFlag(StorageFeature.PromptVersion)) services.TryAddSingleton(_ => backend.PromptVersion(factory));
        if (features.HasFlag(StorageFeature.Conversation)) services.TryAddSingleton(_ => backend.Conversation(factory));
        if (features.HasFlag(StorageFeature.Memory))
        {
            services.TryAddSingleton(sp => backend.Memory(factory, sp));
            // the graph tables ship under the same feature tag as the keyword log
            services.TryAddSingleton(sp => backend.MemoryGraph(factory, sp));
        }
        if (features.HasFlag(StorageFeature.Score)) services.TryAddSingleton(_ => backend.Score(factory));
        if (features.HasFlag(StorageFeature.Trace)) services.TryAddSingleton(_ => backend.Trace(factory));
        if (features.HasFlag(StorageFeature.Jobs)) services.TryAddSingleton(sp => backend.Jobs(factory, sp));
        if (features.HasFlag(StorageFeature.CuratedMemory)) services.TryAddSingleton(sp => backend.CuratedMemory(factory, sp));
        return builder;
    }

    /// <summary>The factory of this backend's LAST wiring — the one the Governance guard reads — or, with no
    /// wiring at all, an app-registered one.</summary>
    public static IDbConnectionFactory Factory(IServiceProvider sp) =>
        sp.GetService<FeatureSelection>()?.Factory ?? sp.GetRequiredService<IDbConnectionFactory>();
}
