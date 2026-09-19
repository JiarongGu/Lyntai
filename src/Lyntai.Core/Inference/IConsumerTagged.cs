namespace Lyntai.Inference;

/// <summary>A request that says WHO is asking — the same attribution tag <see cref="TextRequest.Consumer"/>
/// carries, as an interface so the generic router can read it off any shape (<c>docs/DECISIONS.md</c>
/// <b>D163</b>).
///
/// <para><b>Implementing this is how a call shape opts into governance.</b> A factory-built
/// <see cref="ProviderRouter{TRequest,TResponse}"/> budgets, rate-limits and records spend only for a
/// request it can attribute; a shape that carries no tag has said nothing about who is spending, so the
/// wallet cannot bill it and the router leaves it ungoverned. <see cref="VectorRequest"/> and
/// <see cref="ScoreRequest"/> implement it; an application-defined kind adds the one property (null is
/// billed to <c>"default"</c>, exactly as an untagged text request is).</para></summary>
public interface IConsumerTagged
{
    /// <summary>The consumer tag, or null for the default bucket. The library's own tags are
    /// <see cref="ProviderConsumers"/>.</summary>
    string? Consumer { get; }
}
