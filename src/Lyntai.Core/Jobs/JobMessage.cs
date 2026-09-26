using System.Text.Json.Nodes;

namespace Lyntai.Jobs;

/// <summary>A job's status line in a form a reader can localize — a stage or a step.
///
/// <para><see cref="Text"/> is always present: it is the line every reader that knows nothing of codes shows, and
/// what a string-based reader still receives. <see cref="Code"/> names the message and <see cref="Arguments"/> fill
/// it; a reporter formats each argument value itself, invariantly, so a reader can parse a number back.</para>
///
/// <para>Two messages are equal when their text, code and arguments are — the arguments compared by content.</para></summary>
/// <param name="Text">The line as the reporter would show it.</param>
public sealed record JobMessage(string Text)
{
    /// <summary>The line as the reporter would show it; never null.</summary>
    public string Text { get; init; } = Text ?? throw new ArgumentNullException(nameof(Text));

    /// <summary>What the message is, for a reader to look its localized form up by.</summary>
    public string? Code { get; init; }

    /// <summary>The values the localized form fills in, formatted invariantly by the reporter.</summary>
    public IReadOnlyDictionary<string, string>? Arguments { get; init; }

    /// <inheritdoc/>
    public bool Equals(JobMessage? other) =>
        other is not null
        && string.Equals(Text, other.Text, StringComparison.Ordinal)
        && string.Equals(Code, other.Code, StringComparison.Ordinal)
        && SameArguments(Arguments, other.Arguments);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Text, Code, Arguments?.Count ?? 0);

    private static bool SameArguments(IReadOnlyDictionary<string, string>? a, IReadOnlyDictionary<string, string>? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.Count == b.Count
            && a.All(pair => b.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal));
    }
}

/// <summary>A message's code and arguments as the JSON the job stores keep beside its text — hand-walked (D14).</summary>
internal static class JobMessageJson
{
    /// <summary><c>{"code","args"}</c>, or null when the message carries neither — a plain line stores nothing extra.</summary>
    public static JsonObject? Write(JobMessage? message)
    {
        if (message is null || (message.Code is null && message.Arguments is null)) return null;
        var detail = new JsonObject();
        Fill(detail, message);
        return detail;
    }

    /// <summary>Add the message's <c>code</c> and <c>args</c>, whichever it carries, to <paramref name="target"/>.</summary>
    public static void Fill(JsonObject target, JobMessage message)
    {
        if (message.Code is { } code) target["code"] = code;
        if (message.Arguments is not { } arguments) return;
        var args = new JsonObject();
        foreach (var (key, value) in arguments) args[key] = value;
        target["args"] = args;
    }

    /// <summary>The message <paramref name="text"/> and a stored <paramref name="detail"/> describe; a detail that is
    /// missing or unreadable leaves the plain text, and an argument that is not a string is skipped.</summary>
    public static JobMessage Read(string text, JsonNode? detail)
    {
        if (detail is not JsonObject o) return new JobMessage(text);
        Dictionary<string, string>? arguments = null;
        if (o["args"] is JsonObject args)
        {
            arguments = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in args)
                if (value is JsonValue v && v.TryGetValue<string>(out var s)) arguments[key] = s;
        }
        var code = o["code"] is JsonValue c && c.TryGetValue<string>(out var parsed) ? parsed : null;
        return new JobMessage(text) { Code = code, Arguments = arguments };
    }
}
