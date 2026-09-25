namespace Lyntai.Generation.Providers;

/// <summary>What the two local-process backends — <c>sd-cli</c> and piper — share: the presence check a probe
/// and a call both make, an argv flag looked up by what it MEANS, and the silence window a spawn runs under.</summary>
internal static class LocalEngine
{
    /// <summary>Why the engine cannot run — its executable or its model file absent on disk — or null when
    /// both are present. The host provisions both (D20), so this is a setup answer, never a failure.</summary>
    /// <param name="binary">The configured executable path.</param>
    /// <param name="model">The configured model path.</param>
    /// <param name="engine">The engine's name, as the answer spells it (<c>sd-cli</c>).</param>
    /// <param name="modelNoun">What the model file is to this engine (<c>model file</c>, <c>voice</c>).</param>
    public static string? Missing(string? binary, string? model, string engine, string modelNoun) =>
        binary is not { Length: > 0 } || !File.Exists(binary)
            ? $"not configured: no {engine} binary at '{binary}' (the host provisions it — D20)"
            : model is not { Length: > 0 } || !File.Exists(model)
                ? $"not configured: the engine is present but its {modelNoun} is missing at '{model}'"
                : null;

    /// <summary>The argv token for <paramref name="name"/>: the host's override, else the shipped spelling.</summary>
    public static string Flag(
        IDictionary<string, string> overrides, IReadOnlyDictionary<string, string> defaults, string name) =>
        overrides.TryGetValue(name, out var flag) ? flag : defaults[name];

    /// <summary>The silence window, never above the absolute budget: a caller who shortens the budget must not
    /// end up with a silence detector that can never fire.</summary>
    public static TimeSpan Inactivity(TimeSpan timeout, TimeSpan inactivity) =>
        inactivity < timeout ? inactivity : timeout;
}

/// <summary>The <c>"WxH"</c> size hint, read ONE way by every backend that takes one.</summary>
internal static class SizeHint
{
    /// <summary>A hint with both sides POSITIVE, or false. <c>"0x0"</c> parses as numbers and is still no size a
    /// backend can render — the half every size-taking backend shares, whatever it then does with a usable
    /// one (Automatic1111 forwards it, <c>sd-cli</c> clamps it).</summary>
    public static bool TryParse(string? size, out int width, out int height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(size)) return false;
        var parts = size.Split('x', 'X');
        return parts.Length == 2 && int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height) &&
               width > 0 && height > 0;
    }
}
