using System.Text.Json;

namespace Lyntai.Generation;

/// <summary>The domain's shared JSON readers — used by the durable job payload and by the agent tools, which is
/// why they live here rather than in either. Hand-walked (<see cref="JsonDocument"/>) rather than
/// reflection-serialized: Core claims trim/AOT compatibility, and a reflection serializer would quietly make
/// that claim false.</summary>
internal static class GenerationJson
{
    /// <summary>Read a string member, or null when it is absent, not a string, or empty. The object-kind test is
    /// part of the contract, not a formality: <c>TryGetProperty</c> THROWS on an element that isn't an object,
    /// so a copy of this reader without it is one careless caller away from an exception.</summary>
    public static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
