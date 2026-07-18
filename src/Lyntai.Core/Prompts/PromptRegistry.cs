using System.Text.RegularExpressions;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Prompts;

/// <summary>
/// Default registry. Documented decisions:
/// - An override that drops a <c>{placeholder}</c> present in the default is REJECTED — the registry
///   logs a warning and falls back to the default template (fail-open; silent content loss otherwise).
/// - A <c>{placeholder}</c> with no matching var is left literal (callers may fill it downstream).
/// - Override precedence: the active <see cref="IPromptVersionStore"/> revision (if any) wins over
///   the plain <see cref="IKeyValueStore"/> key; neither configured (or a store outage) → the default.
/// </summary>
public sealed partial class PromptRegistry(
    IKeyValueStore? kv = null,
    IPromptVersionStore? versions = null,
    ILogger<PromptRegistry>? logger = null) : IPromptRegistry
{
    public const string KeyPrefix = "lyntai.prompt.";

    private readonly ILogger _logger = logger ?? NullLogger<PromptRegistry>.Instance;

    public async Task<string> RenderAsync(string name, string defaultTemplate,
        IReadOnlyDictionary<string, string>? vars = null, CancellationToken ct = default)
    {
        var template = defaultTemplate;

        var @override = await GetOverrideAsync(name, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(@override))
        {
            var missing = ValidateOverride(defaultTemplate, @override);
            if (missing.Count > 0)
                _logger.LogWarning("prompt override {Key} rejected — drops placeholder(s) {Missing}; using the default",
                    KeyPrefix + name, string.Join(", ", missing));
            else
                template = @override;
        }

        if (vars is not null)
            foreach (var (key, value) in vars)
                template = template.Replace("{" + key + "}", value);
        return template;
    }

    private async Task<string?> GetOverrideAsync(string name, CancellationToken ct)
    {
        try
        {
            // the versioned active revision wins over the plain KV key
            if (versions is not null)
            {
                var active = await versions.GetActiveAsync(name, ct).ConfigureAwait(false);
                if (active is not null) return active.Template;
            }
            return kv is null ? null : await kv.GetAsync(KeyPrefix + name, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "prompt override lookup failed for {Name}; using the default", name);
            return null; // fail-open: a storage outage never blocks prompt rendering
        }
    }

    public IReadOnlyList<string> ValidateOverride(string defaultTemplate, string candidate) =>
        [.. Placeholders(defaultTemplate).Except(Placeholders(candidate))];

    private static HashSet<string> Placeholders(string template) =>
        PlaceholderRegex().Matches(template).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@"\{([A-Za-z_][A-Za-z0-9_]*)\}")]
    private static partial Regex PlaceholderRegex();
}
