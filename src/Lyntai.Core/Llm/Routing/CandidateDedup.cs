namespace Lyntai.Llm.Routing;

/// <summary>Drops repeat (ProviderId, Model) candidates — first wins, order preserved — so a
/// misconfigured list that re-prepends the primary never retries it.</summary>
public static class CandidateDedup
{
    public static IReadOnlyList<LlmCandidate> Dedup(IEnumerable<LlmCandidate> candidates)
    {
        var seen = new HashSet<(string, string?)>();
        var result = new List<LlmCandidate>();
        foreach (var c in candidates)
        {
            if (seen.Add((c.ProviderId, c.Model))) result.Add(c);
        }
        return result;
    }
}
