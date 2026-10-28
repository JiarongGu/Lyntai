namespace Lyntai.Tests.Agents;

/// <summary>The usage responses the two CLI doors' readers are held to. Each is marked by where it came from, and
/// neither kind stands for the other: a REAL capture is a line a real binary printed under an empty, signed-out
/// home on this tree's machine; a SOURCE-DERIVED one is written from the shape a sibling repository's evidence notes
/// record for a signed-in account (values invented, the window set and field names theirs), so it validates the
/// reader against that record and not against a binary run here. The signed-in versions are the only ones the doors
/// claim, because only they have a signed-in response on record.</summary>
internal static class UsageCaptures
{
    // ── claude ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>REAL, claude 2.1.312, an empty home (signed out). The initialize answer is trimmed to its envelope.</summary>
    public static readonly string[] ClaudeSignedOut_2_1_312 =
    [
        """{"type":"control_response","response":{"subtype":"success","request_id":"init","response":{"claude_code_version":"2.1.312"}}}""",
        """{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"session":{"total_cost_usd":0,"total_api_duration_ms":0,"total_duration_ms":7527,"total_lines_added":0,"total_lines_removed":0,"model_usage":{}},"subscription_type":null,"rate_limits_available":false,"rate_limits":null,"behaviors":null}}}""",
    ];

    /// <summary>SOURCE-DERIVED, claude 2.1.296, a signed-in team account: 23 percent of the five-hour window, 79 of
    /// the week, and the server's own rows, which the reader does not take.</summary>
    public const string ClaudeSignedIn_2_1_296 =
        """{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"session":{"total_cost_usd":0},"subscription_type":"team","rate_limits_available":true,"rate_limits":{"five_hour":{"utilization":23,"resets_at":"2026-10-12T14:00:00.000000+00:00","limit_dollars":null,"used_dollars":null,"remaining_dollars":null,"locked_reason":null},"seven_day":{"utilization":79,"resets_at":"2026-10-15T09:30:00.000000+00:00","limit_dollars":null,"used_dollars":null,"remaining_dollars":null,"locked_reason":null},"seven_day_opus":null,"seven_day_sonnet":null,"seven_day_oauth_apps":null,"limits":[{"kind":"session","group":"session","percent":23,"severity":"normal","resets_at":"2026-10-12T14:00:00.000000+00:00","scope":null,"is_active":true},{"kind":"weekly_all","group":"weekly","percent":79,"severity":"warning","resets_at":"2026-10-15T09:30:00.000000+00:00","scope":null,"is_active":true}]},"behaviors":null}}}""";

    /// <summary>SOURCE-DERIVED, claude 2.1.296, a max account at the week's limit with the five-hour window not yet
    /// opened (0 percent and no reset time).</summary>
    public const string ClaudeWeekFull_2_1_296 =
        """{"type":"control_response","response":{"subtype":"success","request_id":"usage","response":{"subscription_type":"max","rate_limits_available":true,"rate_limits":{"five_hour":{"utilization":0,"resets_at":null},"seven_day":{"utilization":100,"resets_at":"2026-10-14T08:00:00.000000+00:00"},"seven_day_opus":null}}}}""";

    // ── codex ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>REAL, codex-cli 0.163.0-alpha.10.2, an empty home (signed out). The initialize answer is trimmed to
    /// what carries no path or machine name.</summary>
    public static readonly string[] CodexSignedOut_0_163 =
    [
        """{"id":1,"result":{"userAgent":"Codex Desktop/0.163.0-alpha.10.2","platformFamily":"windows","platformOs":"windows"}}""",
        """{"method":"remoteControl/status/changed","params":{"status":"disabled"}}""",
        """{"error":{"code":-32600,"message":"codex account authentication required to read rate limits"},"id":2}""",
    ];

    /// <summary>SOURCE-DERIVED, codex-cli 0.160.0, a signed-in account: 1 percent of the 300-minute window and 15 of
    /// the 10,080-minute one. The account id the server sends is a placeholder the reader must not surface.</summary>
    public const string CodexSignedIn_0_160_0 =
        """{"id":2,"result":{"accountId":"redacted","ordinaryUsageAllowed":true,"rateLimits":{"limitId":"codex","limitName":null,"normalModelSlug":null,"primary":{"usedPercent":1,"windowDurationMins":300,"resetsAt":1792000000},"secondary":{"usedPercent":15,"windowDurationMins":10080,"resetsAt":1792400000},"credits":{"hasCredits":false,"unlimited":false,"balance":null},"individualLimit":null,"spendControlReached":false,"planType":"plus","rateLimitReachedType":null},"rateLimitsByLimitId":{"codex":{}},"rateLimitResetCredits":{"availableCount":0,"credits":[]},"rateLimitUpsell":null}}""";

    /// <summary>SOURCE-DERIVED, codex-cli 0.160.0, a plan the maker says has no five-hour limit: the primary window is
    /// absent and a weekly one is full.</summary>
    public const string CodexWeekOnly_0_160_0 =
        """{"id":2,"result":{"rateLimits":{"primary":null,"secondary":{"usedPercent":100,"windowDurationMins":10080,"resetsAt":1792400000},"planType":"pro"}}}""";
}
