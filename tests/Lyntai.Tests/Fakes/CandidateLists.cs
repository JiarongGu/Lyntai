using Lyntai.Inference;

namespace Lyntai.Tests.Fakes;

/// <summary>Candidate lists for a router call — <c>using static</c> it for <c>Order("a", "b")</c>.</summary>
public static class CandidateLists
{
    /// <summary>One candidate per provider id, in preference order, with no model pinned.</summary>
    public static ProviderCandidate[] Order(params string[] ids) => [.. ids.Select(id => new ProviderCandidate(id))];
}
