using Lyntai.Agents;

namespace Lyntai.Providers.Basic;

/// <summary>The plan vocabulary the agent readers share, so two doors cannot map one status word two ways.</summary>
internal static class WirePlan
{
    /// <summary>The status a door's word names — <c>pending</c>, <c>in_progress</c> or <c>completed</c>, which
    /// the Claude to-do list and the protocol plan entry both use — or null for a word not in that set.</summary>
    public static PlanEntryStatus? Status(string? word) => word switch
    {
        "pending" => PlanEntryStatus.Pending,
        "in_progress" => PlanEntryStatus.InProgress,
        "completed" => PlanEntryStatus.Completed,
        _ => null,
    };
}
