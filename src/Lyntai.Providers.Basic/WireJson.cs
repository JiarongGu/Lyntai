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

    /// <summary>A numeric property as a <c>long</c>, or null when absent, non-numeric, fractional or out of
    /// range — for a count whose ABSENCE is itself the answer, where <see cref="Long"/>'s zero would lie.</summary>
    internal static long? Int64(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number) is { } el && el.TryGetInt64(out var value) ? value : null;

    /// <summary>A Unix-seconds stamp as a moment, or null when it is null or past the range of a date (an epoch in
    /// MILLISECONDS written where seconds belong is), where <see cref="DateTimeOffset.FromUnixTimeSeconds"/> throws.</summary>
    /// <param name="seconds">The stamp as the wire gave it.</param>
    internal static DateTimeOffset? UnixSeconds(long? seconds) =>
        seconds is { } s && s is >= -62135596800L and <= 253402300799L ? DateTimeOffset.FromUnixTimeSeconds(s) : null;

    /// <summary>A numeric property as a finite <c>double</c>, or null when absent, non-numeric or not representable
    /// (<c>1e999</c> reads as infinity, which no cost or count is).</summary>
    /// <param name="parent">The object to read from.</param>
    /// <param name="name">The property name.</param>
    internal static double? Finite(JsonElement parent, string name) =>
        Property(parent, name, JsonValueKind.Number) is { } el && el.TryGetDouble(out var value) && double.IsFinite(value) ? value : null;

    /// <summary>A percent from 0 to 100 as the server wrote it, with the least difference its digits show (1 for
    /// <c>23</c>, 0.1 for <c>23.5</c>; null for exponent form). Absent or null is a stated absence and reads true with
    /// both null; false is a value present that is not a number in range.</summary>
    internal static bool Percent(JsonElement parent, string name, out double? value, out double? step)
    {
        value = step = null;
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return true;
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetDouble(out var number) || !double.IsFinite(number) || number is < 0 or > 100)
            return false;
        value = number;

        var text = el.GetRawText();
        if (text.AsSpan().IndexOfAny('e', 'E') >= 0) return true;
        var point = text.IndexOf('.');
        step = point < 0 ? 1 : 1.0 / Math.Pow(10, text.Length - point - 1);
        return true;
    }

    /// <summary>A use written as a fraction from 0 to 1 (<c>0.375</c>) read as a percent, with the step its digits show
    /// (<c>0.88</c> shows 1, <c>0.5</c> shows 10; null for exponent form). Null when absent, null, not a number or out of
    /// range: a report is an event, so a figure it cannot read is left out and the report kept. The digits are read as a
    /// decimal, so <c>0.88</c> is 88 and not 88.00000000000001.</summary>
    internal static (double Percent, double? Step)? Fraction(JsonElement parent, string name)
    {
        if (Property(parent, name, JsonValueKind.Number) is not { } el) return null;
        var text = el.GetRawText();
        if (text.AsSpan().IndexOfAny('e', 'E') >= 0)
            return el.TryGetDouble(out var wide) && double.IsFinite(wide) && wide is >= 0 and <= 1 ? (wide * 100, null) : null;
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number)
            || number is < 0 or > 1)
            return null;
        var point = text.IndexOf('.');
        return ((double)(number * 100), point < 0 ? 100 : 100.0 / Math.Pow(10, text.Length - point - 1));
    }

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
