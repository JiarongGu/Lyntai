namespace Lyntai.Inference;

/// <summary>The capability predicates the library selects on outside the text door, one home each. The
/// composition checks (<c>AddSemanticMemory</c>, the declaration-versus-implementation refusal) and the routers
/// that select at run time ask the SAME predicate, so a condition added to one cannot leave composition passing
/// while the router selects nothing.</summary>
internal static class ProviderShapes
{
    /// <summary>Text in, vectors out, one reply — what semantic recall and the tool selector embed through.</summary>
    internal static bool Embeds(ProviderCapabilities capabilities) =>
        capabilities.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text);

    /// <summary>Text in, scores out, one reply — what scoring verification reranks through.</summary>
    internal static bool Scores(ProviderCapabilities capabilities) =>
        capabilities.Supports(ProviderKinds.Score, ProviderOperation.Complete, accepts: ProviderKinds.Text);
}
