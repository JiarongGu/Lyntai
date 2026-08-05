using System.Text.Json;

namespace Lyntai.Providers;

/// <summary>The "read a number off a backend's wire object" helper this package shares — one
/// implementation, because it had three and they did not agree.
///
/// <para><b>The disagreement was not cosmetic.</b> Two copies ended
/// <c>ValueKind == Number ? el.GetInt64() : 0</c>, and that throws <see cref="FormatException"/> on a
/// number that is not an integral <c>long</c> — a fractional token count from a proxy that averages, or
/// anything past <c>long.MaxValue</c>. Every caller is a <c>usage</c> read on an OTHERWISE GOOD reply, and
/// none of them catches it: the JSON guards around them are all <c>catch (JsonException)</c>, so the throw
/// escaped <c>OpenAiCompatibleProvider.CompleteAsync</c>, escaped its streaming enumerator, and broke the
/// "never throws" promise both <c>claude</c> stream-json readers make. The third copy used
/// <see cref="JsonElement.TryGetInt64"/> and read the same field as 0. This is that third behaviour, kept.</para>
///
/// <para>A usage count is telemetry, not the answer, so 0-and-carry-on is the right failure: losing a
/// budget line is a far smaller harm than failing a reply the model already produced and the caller has
/// already paid for.</para></summary>
internal static class WireJson
{
    /// <summary>A numeric property as a <c>long</c> — 0 when the parent is not an object, when the property
    /// is absent or non-numeric, or when the number does not fit a <c>long</c>. Never throws.</summary>
    /// <param name="parent">The object to read from.</param>
    /// <param name="name">The property name.</param>
    /// <remarks>The parent-is-an-object guard lives here rather than at each call site because
    /// <c>TryGetProperty</c> itself throws on a non-object; every caller checks today, and a shared helper
    /// should not depend on all of them continuing to.</remarks>
    internal static long Long(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number &&
        el.TryGetInt64(out var value)
            ? value
            : 0;
}
