using System.Text.Json;
using Lyntai.Llm;
using Lyntai.Text;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Reads what a <c>claude auth status --json</c> document says about the CLI's credentials.
/// Split out from the provider so every shape — signed in, signed out, renamed, malformed, prose — is
/// unit-testable without spawning anything (and without touching a developer's real credentials).
///
/// The shape as of CLI v2.1.220 is
/// <c>{"loggedIn":true,"authMethod":…,"apiProvider":…,"email":…,"orgId":…,"orgName":…,"subscriptionType":…}</c>.
/// Tolerant by design (the document belongs to the CLI and will change): a few plausible key spellings are
/// accepted, and anything unreadable returns <c>null</c> — the provider then reports "not authenticated"
/// with the raw output as the detail rather than GUESSING a signed-in state.</summary>
internal static class ClaudeAuthStatusJson
{
    /// <summary>Parse the CLI's auth document, or null when it can't be read as one. Hand-walked
    /// (<c>docs/DECISIONS.md</c> D17) — no reflection serialization.</summary>
    public static ProviderAuthStatus? Parse(string output)
    {
        // TryParseObject also tolerates a banner/notice line printed ahead of the JSON object
        if (!JsonExtract.TryParseObject(output, out var doc)) return null;
        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            // no state flag at all → we genuinely don't know; say so by returning null
            if (FirstBool(root, "loggedIn", "logged_in", "authenticated") is not { } authenticated) return null;

            return new ProviderAuthStatus(authenticated,
                Method: FirstString(root, "authMethod", "auth_method", "apiProvider", "api_provider"),
                Account: FirstString(root, "email", "account", "orgName", "org_name"));
        }
    }

    private static bool? FirstBool(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return el.GetBoolean();
        return null;
    }

    private static string? FirstString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
            if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String &&
                el.GetString() is { Length: > 0 } value)
                return value;
        return null;
    }
}
