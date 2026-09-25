using Lyntai.Inference;
using Lyntai.Inference.Cli;

namespace Lyntai.Providers.Basic;

/// <summary>What every CLI provider in this package composes the same way, so a second CLI provider cannot
/// declare or wire it differently.</summary>
internal static class CliComposition
{
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
