using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Storage.Relational;

/// <summary>The Governance prerequisite, enforced at WIRING time. The response-cache, usage and vector tables
/// ship in ONE Governance migration, so a feature subset omitting it would leave a Governance-backed helper
/// registering a store over a table that was never created. Why the check is EAGER, what a lazy one would
/// have accepted, and its two scope rules (order-independent across the storage/helper PAIR only; applies
/// only where Lyntai owns the schema) are <c>docs/DECISIONS.md</c> D150.</summary>
internal static class GovernanceGuard
{
    private sealed record Call(string Method);

    /// <summary>A Governance-backed helper: recorded for a wiring that comes later, and checked against the
    /// wiring registered so far.</summary>
    public static void Require(LyntaiBuilder builder, RelationalBackend backend, string method)
    {
        builder.Services.AddSingleton(new Call(method));
        // the last selection SO FAR — eager, so not necessarily the one the app ends with (D150)
        if (builder.Services.LastOrDefault(d => !d.IsKeyedService && d.ServiceType == typeof(FeatureSelection))
                ?.ImplementationInstance is FeatureSelection selection)
            Verify(backend, selection, method);
    }

    /// <summary>A wiring: every helper called before it is checked against it.</summary>
    public static void Wired(LyntaiBuilder builder, RelationalBackend backend, FeatureSelection selection)
    {
        foreach (var descriptor in builder.Services
                     .Where(d => !d.IsKeyedService && d.ServiceType == typeof(Call))
                     .ToList())
            Verify(backend, selection, ((Call)descriptor.ImplementationInstance!).Method);
    }

    private static void Verify(RelationalBackend backend, FeatureSelection selection, string method)
    {
        if (!selection.LyntaiMigrates) return;   // the app owns the schema — no migration was going to run
        if (selection.Features.HasFlag(StorageFeature.Governance)) return;
        throw new InvalidOperationException(
            $"{method} needs StorageFeature.Governance, but {backend.StorageMethod} was called with a feature set " +
            $"that omits it. Governance carries {backend.GovernanceTables}, so the store would be registered over " +
            "a table that was never created and the failure would surface at the first call instead of here. Add " +
            $"StorageFeature.Governance to the {backend.StorageMethod} feature set, or drop the {method} call.");
    }
}
