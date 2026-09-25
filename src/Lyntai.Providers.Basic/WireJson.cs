using System.Text.Json;

namespace Lyntai.Providers.Basic;

/// <summary>Guarded reads off a backend's wire object — the one way this package reads a property. Each
/// answers "absent" for a parent that is not an object, because <see cref="JsonElement.TryGetProperty(string, out JsonElement)"/>
/// itself THROWS on one, and every reader here promises never to throw on well-formed JSON of the wrong
/// shape. A number that does not fit reads as absent, never as a <see cref="FormatException"/>.</summary>
internal static class WireJson
{
    /// <summary>A numeric property as a <c>long</c> — 0 when the parent is not an object, when the property
    /// is absent or non-numeric, or when the number does not fit a <c>long</c>. A usage count is telemetry,
    /// not the answer, so 0-and-carry-on beats failing a reply the caller already paid for.</summary>
    /// <param name="parent">The object to read from.</param>
    /// <param name="name">The property name.</param>
    internal static long Long(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number) is { } el && el.TryGetInt64(out var value) ? value : 0;

    /// <summary>A numeric property as an <c>int</c>, or null when absent, non-numeric, fractional or out of
    /// range.</summary>
    internal static int? Int32(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number) is { } el && el.TryGetInt32(out var value) ? value : null;

    /// <summary>A string-valued property, or null when absent or not a string.</summary>
    internal static string? String(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.String)?.GetString();

    /// <summary>An object-valued property, or null when absent or not an object.</summary>
    internal static JsonElement? Object(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Object);

    /// <summary>An array-valued property, or null when absent or not an array.</summary>
    internal static JsonElement? Array(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Array);

    /// <summary>The backstop every reader here catches: malformed text, and the throws a read of
    /// well-formed JSON of an unexpected shape can still raise past the guarded reads above.</summary>
    internal static bool IsShapeFault(Exception ex) =>
        ex is JsonException or InvalidOperationException or FormatException;

    private static JsonElement? Property(JsonElement parent, string name, JsonValueKind kind) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out var el) && el.ValueKind == kind
            ? el
            : null;
}
