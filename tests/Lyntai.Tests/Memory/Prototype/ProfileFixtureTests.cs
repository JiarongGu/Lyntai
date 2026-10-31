using Lyntai.Storage.InMemory;
using Lyntai.Tests.Storage;
using Xunit.Abstractions;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// <b>VP2</b>: an application's fixture is DATA, replayed unchanged on every revisioned curated store
/// (<c>docs/vnext-modular-capabilities.md</c> §8). Every <c>*.profile.json</c> beside the tests replays on all four
/// stores; a changed expectation fails with the step, the field and the two values; a fact the contract cannot hold is
/// reported as not fitting and never coerced. The reference fixtures are shape demonstrations and not applications.
/// </summary>
[Collection("postgres")]
public class ProfileFixtureTests(PostgresFixture pg, ITestOutputHelper output)
{
    public static TheoryData<string> Backends => [.. ProfileBackends.All];

    public static TheoryData<string, string> EveryFixtureOnEveryBackend()
    {
        var data = new TheoryData<string, string>();
        foreach (var file in ProfileFixture.Files)
            foreach (var backend in ProfileBackends.All) data.Add(backend, file);
        return data;
    }

    private async Task<FixtureReport> ReplayAsync(string backend, ProfileFixture fixture)
    {
        var (store, lease) = ProfileBackends.Open(backend, pg);
        using (lease) return await ProfileFixtureReplay.RunAsync(fixture, store);
    }

    [Fact]
    public void The_fixture_directory_holds_the_reference_fixtures()
    {
        Assert.Equal(["not-fitting.profile.json", "person-reference.profile.json"], ProfileFixture.Files);
    }

    [SkippableTheory]
    [MemberData(nameof(EveryFixtureOnEveryBackend))]
    public async Task Every_fixture_replays_on_every_revisioned_store_with_no_mismatch(string backend, string file)
    {
        var report = await ReplayAsync(backend, ProfileFixture.Load(file));

        foreach (var nf in report.NotFitting) output.WriteLine($"{backend}/{file}: not fitting {nf.Field}: {nf.Why}");
        Assert.Empty(report.Mismatches.Select(m => m.ToString()));
        Assert.True(report.Replayed > 0 && report.Reads > 0, "a fixture that replays nothing proves nothing");
    }

    [SkippableTheory]
    [MemberData(nameof(Backends))]
    public async Task The_person_reference_replays_all_thirteen_steps_and_holds_every_field(string backend)
    {
        var report = await ReplayAsync(backend, ProfileFixture.Load("person-reference.profile.json"));

        Assert.Empty(report.Mismatches.Select(m => m.ToString()));
        Assert.Empty(report.NotFitting);
        Assert.Equal(13, report.Replayed);
        Assert.Equal(15, report.Reads);
    }

    [SkippableTheory]
    [MemberData(nameof(Backends))]
    public async Task A_changed_expectation_fails_naming_the_step_the_field_and_both_values(string backend)
    {
        var fixture = ProfileFixture.Load("person-reference.profile.json");
        var changed = fixture with
        {
            Steps =
            [
                .. fixture.Steps.Select(s => s.Id != "s5" ? s
                    : s with { Then = [.. s.Then!.Select((r, i) => i == 0 ? r with { Value = "Paris" } : r)] }),
            ],
        };

        var report = await ReplayAsync(backend, changed);

        var miss = Assert.Single(report.Mismatches);
        Assert.Equal(new FixtureMismatch("s5", "address.city@home", "current value", "\"Paris\"", "\"Berlin\""), miss);
        Assert.Equal("step s5, address.city@home: current value: expected \"Paris\", got \"Berlin\"", miss.ToString());
    }

    [SkippableTheory]
    [MemberData(nameof(Backends))]
    public async Task A_list_and_a_prose_decision_are_reported_as_not_fitting_and_never_coerced(string backend)
    {
        var (store, lease) = ProfileBackends.Open(backend, pg);
        using (lease)
        {
            var report = await ProfileFixtureReplay.RunAsync(ProfileFixture.Load("not-fitting.profile.json"), store);

            Assert.Equal(["lanes", "decision.storage"], report.NotFitting.Select(n => n.Field));
            Assert.Empty(report.Mismatches.Select(m => m.ToString()));
            var held = await store.ListAsync(taskKey: new ProfileField("x1", "").TaskKey);
            Assert.Equal(["stack.language"], held.Select(r => r.Kind).Distinct());   // only what fits was written
            Assert.Equal(2, held.Count);                                              // the claim and the one it replaced
        }
    }

    [Fact]
    public async Task A_step_outside_the_schema_is_a_mismatch_and_writes_nothing()
    {
        var fixture = ProfileFixture.Parse("""
            { "name": "t", "kind": "k", "subject": "x1",
              "schema": [ { "id": "a", "mayState": ["user"] }, { "id": "b", "scoped": false } ],
              "steps": [
                { "id": "s1", "op": "set", "field": "undeclared", "value": "v", "at": "2026-01-01T00:00:00Z", "source": "c:1" },
                { "id": "s2", "op": "set", "field": "b", "scope": "home", "value": "v", "at": "2026-01-01T00:00:00Z", "source": "c:1" },
                { "id": "s3", "op": "set", "field": "a", "value": "v", "at": "2026-01-01T00:00:00Z", "source": "m:1", "authority": "hypothesis" },
                { "id": "s4", "op": "rename", "from": "a", "to": "undeclared" }
              ] }
            """);
        var store = new InMemoryCuratedMemoryStore();

        var report = await ProfileFixtureReplay.RunAsync(fixture, store);

        Assert.Equal(["s1", "s2", "s3", "s4"], report.Mismatches.Select(m => m.Step));
        Assert.All(report.Mismatches, m => Assert.Equal("schema", m.Check));
        Assert.Empty(await store.ListAsync());
    }

    [Theory]
    [InlineData("""{ "name": "t", "kind": "k", "subject": "x", "schema": [], "steps": [ { "id": "s1", "op": "mutate" } ] }""", "none of")]
    [InlineData("""{ "name": "t", "kind": "k", "subject": "x", "schema": [], "steps": [ { "id": "s1", "op": "forget" }, { "id": "s1", "op": "forget" } ] }""", "used once")]
    [InlineData("""{ "name": "t", "kind": "k", "subject": "x", "schema": [], "steps": [ { "id": "s1", "op": "set", "field": "a", "value": "v", "source": "c" } ] }""", "needs at")]
    [InlineData("""{ "name": "t", "kind": "k", "subject": "x", "schema": [], "steps": [ { "id": "s1", "op": "correct", "field": "a", "value": "v", "at": "2026-01-01T00:00:00Z", "source": "c", "replaces": "s9" } ] }""", "replaces names no earlier")]
    [InlineData("""{ "name": "t", "kind": "k", "subject": "x", "schema": [ { "id": "a", "shape": "table" } ], "steps": [] }""", "shape 'table'")]
    [InlineData("""{ "name": "t", "kind": "k", "subject": "x", "schema": [], "steps": [ { "id": "s1", "op": "forget", "then": [ { "read": "current", "field": "a" } ] } ] }""", "needs at")]
    public void A_malformed_fixture_is_refused_naming_what_is_wrong(string json, string message)
    {
        var ex = Assert.Throws<InvalidDataException>(() => ProfileFixture.Parse(json));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void A_misspelt_key_is_refused_rather_than_ignored()
    {
        Assert.ThrowsAny<Exception>(() => ProfileFixture.Parse(
            """{ "name": "t", "kind": "k", "subject": "x", "schema": [], "steps": [ { "id": "s1", "op": "forget", "thne": [] } ] }"""));
    }
}
