using System.Text.RegularExpressions;

namespace Lyntai.Llm.Cli;

/// <summary>Reads what a CLI's <c>--version</c> banner can tell us. Tolerant by design: the line is the
/// backend's own free-form text (<c>"2.1.220 (Claude Code)"</c>), so anything unrecognized yields null
/// rather than a throw or a guess — the raw line is still handed back to the caller as
/// <see cref="ProviderProbeResult.Detail"/>. Shared by every CLI dialect that doesn't override
/// <see cref="ICliProviderDialect.ParseVersionLine"/>.</summary>
internal static partial class CliVersionLine
{
    /// <summary>Extract the dotted version number and — only if the line explicitly labels one — a model id.</summary>
    public static (string? Version, string? Model) Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return (null, null);
        var version = VersionPattern().Match(line);
        var model = ModelPattern().Match(line);
        return (version.Success ? version.Groups[1].Value : null,
                model.Success ? model.Groups[1].Value : null);
    }

    /// <summary>The first non-empty, trimmed line of a process's output (a banner can be followed by
    /// update notices / blank lines).</summary>
    public static string FirstLine(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return "";
    }

    /// <summary>A dotted version number, optionally <c>v</c>-prefixed and with a prerelease suffix
    /// (<c>2.2.0-beta.1</c>). Anchored on a non-word boundary so a version inside a longer token isn't
    /// half-matched.</summary>
    [GeneratedRegex(@"(?<![\w.])v?(\d+(?:\.\d+)+(?:-[0-9A-Za-z.\-]+)?)")]
    private static partial Regex VersionPattern();

    /// <summary>An explicitly LABELLED model id (<c>model: x</c> / <c>model=x</c>). Only a label counts —
    /// inferring a model from an unlabelled token would invent data.</summary>
    [GeneratedRegex(@"model\s*[:=]\s*""?([A-Za-z0-9._\-]+)""?", RegexOptions.IgnoreCase)]
    private static partial Regex ModelPattern();
}
