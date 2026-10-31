using Lyntai.Storage;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>A step whose outcome or read disagreed with the fixture: where it was, and the two values.</summary>
internal sealed record FixtureMismatch(string Step, string Field, string Check, string Expected, string Actual)
{
    public override string ToString() => $"step {Step}, {Field}: {Check}: expected {Expected}, got {Actual}";
}

/// <summary>A schema field the contract cannot hold. It is named, never coerced: its events and reads are not replayed.</summary>
internal sealed record FixtureNotFitting(string Field, string Why);

internal sealed record FixtureReport(
    string Name, int Replayed, int Reads, IReadOnlyList<FixtureMismatch> Mismatches, IReadOnlyList<FixtureNotFitting> NotFitting);

/// <summary>
/// Replays a <see cref="ProfileFixture"/> through <see cref="ProfileOverCurated"/> over one store: each event in order,
/// then the reads the fixture expects after it. Every read goes through a NEW profile over the same entries, because the
/// profile keeps no state and a later session is what an application has. A mismatch is reported as the step, the field
/// and the two values; a schema field the contract cannot hold is reported as not fitting.
/// </summary>
internal static class ProfileFixtureReplay
{
    internal static async Task<FixtureReport> RunAsync(ProfileFixture fixture, ICuratedMemoryStore store)
    {
        var schema = fixture.Schema.ToDictionary(f => f.Id);
        var notFitting = fixture.Schema.Where(f => f.Shape != "single").Select(f => new FixtureNotFitting(f.Id, WhyNot(f.Shape))).ToList();
        var unfit = notFitting.Select(n => n.Field).ToHashSet();
        var mismatches = new List<FixtureMismatch>();
        var stated = new Dictionary<string, (string Handle, string Field)>();
        var files = new Dictionary<string, string>();
        var (replayed, reads) = (0, 0);

        void Miss(FixtureStep s, string field, string check, string expected, string actual) =>
            mismatches.Add(new FixtureMismatch(s.Id, field, check, expected, actual));

        // a declared, fitting field that the step's scope and authority respect; null when the step is skipped or refused
        ProfileField? Slot(FixtureStep s, string? field, string? scope, string authority)
        {
            if (!schema.TryGetValue(field!, out var declared))
            {
                Miss(s, field!, "schema", "a declared field", "undeclared: only what a schema names is a claim");
                return null;
            }

            if (unfit.Contains(declared.Id)) return null;
            if (scope is not null && !declared.Scoped) { Miss(s, field!, "schema", "an unscoped field", $"scope '{scope}'"); return null; }
            var may = declared.MayState ?? ["user", "hypothesis"];
            if (!may.Contains(authority))
            {
                Miss(s, field!, "schema", $"stated by {string.Join(" or ", may)}", authority);
                return null;
            }

            return new ProfileField(fixture.Subject, declared.Id, scope);
        }

        foreach (var step in fixture.Steps)
        {
            var authority = step.Authority ?? "user";
            var profile = new ProfileOverCurated(store);
            try
            {
                switch (step.Op)
                {
                    case "set" when Slot(step, step.Field, step.Scope, authority) is { } field:
                        var claim = await profile.SetAsync(field, step.Value!, step.At!.Value, step.Source!, Authority(authority));
                        stated[step.Id] = (claim.Handle, field.Field);
                        replayed++;
                        break;
                    case "correct" when Slot(step, step.Field, step.Scope, authority) is { } field:
                        if (!stated.TryGetValue(step.Replaces!, out var replaced))
                        {
                            if (!unfit.Contains(field.Field)) Miss(step, field.Field, "replaces", $"step {step.Replaces} stated a claim", "none held");
                            break;
                        }

                        var outcome = await profile.CorrectAsync(
                            field, step.Value!, step.At!.Value, step.Source!, replaced.Handle, Authority(authority));
                        replayed++;
                        var wanted = step.Outcome ?? "applied";
                        if (!string.Equals(outcome.ToString(), wanted, StringComparison.OrdinalIgnoreCase))
                            Miss(step, field.Field, "correction outcome", wanted, outcome.ToString().ToLowerInvariant());
                        else if (outcome == Correction.Applied)
                        {
                            var id = ProfileClaims.IdOf(field, step.Value!, step.At!.Value, step.Source!, Authority(authority));
                            var written = (await profile.HistoryAsync(field)).FirstOrDefault(c => c.ClaimId == id);
                            if (written is not null) stated[step.Id] = (written.Handle, field.Field);
                        }

                        break;
                    case "rename":
                        var named = new[] { step.From!, step.To! };
                        foreach (var name in named.Where(n => !schema.ContainsKey(n)))
                            Miss(step, name, "schema", "a declared field", "undeclared: only what a schema names is a claim");
                        if (named.Any(n => !schema.ContainsKey(n) || unfit.Contains(n))) break;
                        var moved = await profile.RenameAsync(fixture.Subject, step.From!, step.To!);
                        replayed++;
                        if (step.Moved is { } m && m != moved) Miss(step, $"{step.From} -> {step.To}", "claims moved", $"{m}", $"{moved}");
                        break;
                    case "forget":
                        await profile.ForgetAsync(fixture.Subject);
                        replayed++;
                        break;
                    case "export":
                        using (var file = new StringWriter())
                        {
                            var count = await profile.ExportAsync(fixture.Subject, file);
                            files[step.File!] = file.ToString();
                            replayed++;
                            if (step.Count is { } expected && expected != count) Miss(step, "(subject)", "claims exported", $"{expected}", $"{count}");
                        }

                        break;
                    case "import":
                        var report = await profile.ImportFileAsync(fixture.Subject, new StringReader(files.GetValueOrDefault(step.File!, "")));
                        replayed++;
                        if (step.Imported is { } i && i != report.Imported) Miss(step, "(subject)", "claims imported", $"{i}", $"{report.Imported}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Miss(step, step.Field ?? step.From ?? "(subject)", "the step ran", "no exception", $"{ex.GetType().Name}: {ex.Message}");
            }

            foreach (var read in step.Then ?? [])
            {
                if (read.Field is not null && unfit.Contains(read.Field)) continue;
                reads++;
                try
                {
                    await CheckAsync(step, read, new ProfileOverCurated(store), fixture.Subject, Miss);
                }
                catch (Exception ex)
                {
                    Miss(step, read.Field ?? "(snapshot)", $"{read.Read} read ran", "no exception", $"{ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        return new FixtureReport(fixture.Name, replayed, reads, mismatches, notFitting);
    }

    private static async Task CheckAsync(
        FixtureStep step, FixtureRead read, ProfileOverCurated profile, string subject,
        Action<FixtureStep, string, string, string, string> miss)
    {
        ProfileField? field = read.Field is null ? null : new ProfileField(subject, read.Field, read.Scope);
        var where = read.Field is null ? "(snapshot)" : read.Scope is null ? read.Field : $"{read.Field}@{read.Scope}";
        void Compare(string check, string expected, string actual)
        {
            if (expected != actual) miss(step, where, check, expected, actual);
        }

        switch (read.Read)
        {
            case "current":
                var current = await profile.ReadAsync(field!.Value, read.At!.Value);
                Compare("current value", Show(read.Value), Show(current.Current?.Value));
                if (read.Authority is not null) Compare("authority", read.Authority, current.Current?.Authority.ToString().ToLowerInvariant() ?? "(none)");
                if (read.Rivals is not null) Compare("rivals", Show(read.Rivals), Show(current.Rivals.Select(c => c.Value)));
                if (read.Outranked is not null) Compare("outranked", Show(read.Outranked), Show((current.Outranked ?? []).Select(c => c.Value)));
                break;
            case "asOf":
                Compare("value as of " + read.At!.Value.ToString("O"), Show(read.Value),
                    Show((await profile.AsOfAsync(field!.Value, read.At!.Value))?.Value));
                break;
            case "history":
                Compare("history", Show(read.Values!), Show((await profile.HistoryAsync(field!.Value)).Select(c => c.Value)));
                break;
            case "snapshot":
                var snapshot = await profile.SnapshotAsync(subject, read.At!.Value);
                Compare("slots", Show(read.Cells!.Select(Cell)),
                    Show(snapshot.Fields.Select(f => Cell(new FixtureCell(f.Field, f.Scope, f.Read.Current?.Value)))));
                break;
        }
    }

    private static string Cell(FixtureCell c) => $"{c.Field}@{c.Scope}={Show(c.Value)}";

    private static string Show(string? value) => value is null ? "(none)" : $"\"{value}\"";

    private static string Show(IEnumerable<string> values) => "[" + string.Join(", ", values) + "]";

    private static ProfileAuthority Authority(string name) =>
        name == "hypothesis" ? ProfileAuthority.Hypothesis : ProfileAuthority.User;

    private static string WhyNot(string shape) => shape == "list"
        ? "a field holds one current claim, so a list is one field per member or a joined value"
        : "prose with supersession is curated memory with an end (D215), not a field";
}
