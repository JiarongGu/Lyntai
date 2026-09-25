using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Inference.Cli;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Providers.Basic;

/// <summary>What every CLI provider in this package composes the same way, so a second CLI provider cannot
/// declare or wire it differently.</summary>
internal static class CliComposition
{
    /// <summary>The tool provisioner for the CLI registration <paramref name="id"/>: one keyed on that id
    /// first, then one keyed on the CLI's default id (a connector for this CLI serves every registration of
    /// it), then the unkeyed one — so a hand-rolled <see cref="ICliToolProvisioner"/> with no key still
    /// works, and several CLI providers can host tools side by side.</summary>
    public static ICliToolProvisioner? Provisioner(IServiceProvider sp, string id, string defaultId) =>
        sp.GetKeyedService<ICliToolProvisioner>(id)
        ?? (id == defaultId ? null : sp.GetKeyedService<ICliToolProvisioner>(defaultId))
        ?? sp.GetService<ICliToolProvisioner>();

    /// <summary>A CLI provider's capabilities: text in, text out, buffered or streamed, and request-level tool
    /// calls exactly as the BACKEND declares them — the provider declares (D21), derived from the backend so
    /// the two cannot disagree. The engine streams no tool calls.</summary>
    public static ProviderCapabilities Capabilities(ICliBackend backend) => new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Text],
        Operations = [ProviderOperation.Complete, ProviderOperation.Stream],
        SupportsToolCalls = backend.SupportsToolCalls,
    };
}
