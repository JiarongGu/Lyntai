using System.Text.Json;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>One field of a fixture's schema. <paramref name="Id"/> is named by the application, never by a model
/// (<b>D217</b>). <paramref name="Scoped"/> says a claim may be stated in a named scope. <paramref name="Shape"/> is what
/// the application says the fact IS: only <c>single</c> (one current value) fits the contract, and a <c>list</c> or
/// <c>prose</c> field is reported as not fitting. <paramref name="MayState"/> is the authorities that may state it,
/// <c>user</c> and <c>hypothesis</c> when absent.</summary>
internal sealed record FixtureField(string Id, bool Scoped = false, string Shape = "single", IReadOnlyList<string>? MayState = null);

/// <summary>A (field, scope, current value) triple in a snapshot expectation; a null value is a slot with no current claim.</summary>
internal sealed record FixtureCell(string Field, string? Scope = null, string? Value = null);

/// <summary>A read expected after a step. <c>current</c> and <c>asOf</c> compare <paramref name="Value"/> (null means no
/// claim), and may also compare the winner's <paramref name="Authority"/> and the <paramref name="Rivals"/> and
/// <paramref name="Outranked"/> values. <c>history</c> compares <paramref name="Values"/>, oldest first. <c>snapshot</c>
/// compares every slot of the subject as <paramref name="Cells"/>, ordered by field then scope.</summary>
internal sealed record FixtureRead(
    string Read, string? Field = null, string? Scope = null, DateTimeOffset? At = null, string? Value = null,
    string? Authority = null, IReadOnlyList<string>? Rivals = null, IReadOnlyList<string>? Outranked = null,
    IReadOnlyList<string>? Values = null, IReadOnlyList<FixtureCell>? Cells = null);

/// <summary>One event, in order. <c>set</c> and <c>correct</c> state a claim (<paramref name="Replaces"/> names the earlier
/// step whose claim a correction replaces, <paramref name="Outcome"/> the correction's expected outcome, <c>applied</c> by
/// default); <c>rename</c> moves a field (<paramref name="From"/>, <paramref name="To"/>, optionally <paramref name="Moved"/>);
/// <c>forget</c> removes the subject; <c>export</c> and <c>import</c> move the subject's record through the named
/// <paramref name="File"/> (optionally the <paramref name="Count"/> exported or <paramref name="Imported"/>).
/// <paramref name="Then"/> are the reads expected once the step has run.</summary>
internal sealed record FixtureStep(
    string Id, string Op, string? Field = null, string? Scope = null, string? Value = null, DateTimeOffset? At = null,
    string? Source = null, string? Authority = null, string? Replaces = null, string? Outcome = null, string? From = null,
    string? To = null, int? Moved = null, string? File = null, int? Count = null, int? Imported = null,
    IReadOnlyList<FixtureRead>? Then = null);

/// <summary>
/// An application's profile as DATA: a schema, the events its claims went through, and the reads expected after them
/// (<c>docs/vnext-modular-capabilities.md</c> §8). It is replayed unchanged by <see cref="ProfileFixtureReplay"/> on every
/// revisioned curated store. A malformed file throws <see cref="InvalidDataException"/> naming the step, because a fixture
/// that cannot be read is the author's defect and must never look like a mismatch.
/// </summary>
internal sealed record ProfileFixture(
    string Name, string Kind, string Subject, IReadOnlyList<FixtureField> Schema, IReadOnlyList<FixtureStep> Steps)
{
    private static readonly string[] Ops = ["set", "correct", "rename", "forget", "export", "import"];
    private static readonly string[] Reads = ["current", "asOf", "history", "snapshot"];
    private static readonly string[] Shapes = ["single", "list", "prose"];
    private static readonly string[] Authorities = ["user", "hypothesis"];

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    internal static string Directory { get; } = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Memory", "Prototype", "Fixtures"));

    /// <summary>Every <c>*.profile.json</c> beside the tests, by file name.</summary>
    internal static IReadOnlyList<string> Files =>
        [.. System.IO.Directory.EnumerateFiles(Directory, "*.profile.json").Select(Path.GetFileName).Order()!];

    internal static ProfileFixture Load(string fileName) => Parse(File.ReadAllText(Path.Combine(Directory, fileName)));

    internal static ProfileFixture Parse(string json)
    {
        var fixture = JsonSerializer.Deserialize<ProfileFixture>(json, Options)
            ?? throw new InvalidDataException("a fixture is a JSON object");
        fixture.Validate();
        return fixture;
    }

    private void Validate()
    {
        void Need(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }

        Need(!string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(Subject), "a fixture names itself and its subject");
        Need(Schema.Select(f => f.Id).Distinct().Count() == Schema.Count, "a schema field is declared once");
        foreach (var f in Schema)
        {
            Need(Shapes.Contains(f.Shape), $"field {f.Id}: shape '{f.Shape}' is none of {string.Join(", ", Shapes)}");
            Need((f.MayState ?? []).All(Authorities.Contains), $"field {f.Id}: mayState holds only {string.Join(" and ", Authorities)}");
        }

        Need(Steps.Select(s => s.Id).Distinct().Count() == Steps.Count && Steps.All(s => !string.IsNullOrEmpty(s.Id)),
            "a step has an id, and each id is used once");
        foreach (var s in Steps)
        {
            Need(Ops.Contains(s.Op), $"step {s.Id}: op '{s.Op}' is none of {string.Join(", ", Ops)}");
            (string? Have, string What)[] needs = s.Op switch
            {
                "set" => [(s.Field, "field"), (s.Value, "value"), (s.Source, "source")],
                "correct" => [(s.Field, "field"), (s.Value, "value"), (s.Source, "source"), (s.Replaces, "replaces")],
                "rename" => [(s.From, "from"), (s.To, "to")],
                "export" or "import" => [(s.File, "file")],
                _ => [],
            };
            foreach (var (have, what) in needs) Need(have is not null, $"step {s.Id}: {s.Op} needs {what}");
            Need(s.Op is not ("set" or "correct") || s.At is not null, $"step {s.Id}: {s.Op} needs at");
            Need(s.Authority is null || Authorities.Contains(s.Authority), $"step {s.Id}: authority is {string.Join(" or ", Authorities)}");
            Need(s.Replaces is null || Steps.TakeWhile(p => p.Id != s.Id).Any(p => p.Id == s.Replaces && p.Op is "set" or "correct"),
                $"step {s.Id}: replaces names no earlier set or correct");
            foreach (var r in s.Then ?? [])
            {
                Need(Reads.Contains(r.Read), $"step {s.Id}: read '{r.Read}' is none of {string.Join(", ", Reads)}");
                (string? Have, string What)[] wants = r.Read switch
                {
                    "current" or "asOf" => [(r.Field, "field"), (r.At?.ToString(), "at")],
                    "history" => [(r.Field, "field"), (r.Values?.ToString(), "values")],
                    "snapshot" => [(r.At?.ToString(), "at"), (r.Cells?.ToString(), "cells")],
                    _ => [],
                };
                foreach (var (have, what) in wants) Need(have is not null, $"step {s.Id}: a {r.Read} read needs {what}");
            }
        }
    }
}
