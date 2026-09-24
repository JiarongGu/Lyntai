using System.Text.RegularExpressions;

using Lyntai.Inference;
using Lyntai.Memory;

namespace Lyntai.Tests.Api;

/// <summary>A member of an interface the library itself decorates takes no default body (<c>docs/DECISIONS.md</c>
/// D67), so a decorator that does not forward it is a build error rather than a silently ungoverned or degraded
/// path. "Decorated" is <see cref="DecoratedInterfaces"/>'s definition, applied to every packable assembly.</summary>
public class DecoratedInterfaceTests
{
    /// <summary>The default bodies the gate tolerates, each with its reason. An entry that stops matching fails,
    /// so the list only shrinks.</summary>
    private static readonly Dictionary<string, string> Allowances = new(StringComparer.Ordinal)
    {
        ["Lyntai.Storage.IConversationStore.CountThreadsAsync(CancellationToken)"] =
            "the fallback counts through ListThreadsAsync, which a decorator forwards: correct, only slower (O(n))",
        ["Lyntai.Storage.IConversationStore.ListThreadsPageAsync(Int32, ChatThread, CancellationToken)"] =
            "the fallback slices ListThreadsAsync, which a decorator forwards: correct, only slower (O(n))",
        ["Lyntai.Storage.IDbConnectionFactory.OpenAsync(CancellationToken)"] =
            "the fallback runs the decorator's own Open: correct, only loses the inner factory's async open",
    };

    private static readonly Type[] Shipped =
        [.. ApiSurfaceTests.Loaded.Values.Distinct().SelectMany(a => a.GetTypes())];

    [Fact]
    public void No_member_of_an_interface_the_library_decorates_has_a_default_body()
    {
        var unallowed = DecoratedInterfaces.Check(Shipped, Allowances).Unallowed;

        Assert.True(unallowed.Count == 0,
            $"{unallowed.Count} member(s) of a decorated interface have a default body, so a decorator that does "
            + "not forward one compiles and silently runs the default (D67). Remove the body, or allow it in "
            + $"{nameof(Allowances)} with a reason:\n  "
            + string.Join("\n  ", unallowed.Select(f => $"{f.Member} — decorated by {string.Join(", ", f.Decorators)}")));
    }

    [Fact]
    public void Every_allowance_still_names_a_default_body_on_a_decorated_interface()
    {
        var stale = DecoratedInterfaces.Check(Shipped, Allowances).Stale;

        Assert.True(stale.Count == 0,
            $"{stale.Count} allowance(s) no longer match — the member is gone, now abstract, or its interface is no "
            + "longer decorated. Delete them:\n  " + string.Join("\n  ", stale));
        Assert.All(Allowances, a => Assert.False(string.IsNullOrWhiteSpace(a.Value), $"{a.Key} carries no reason"));
    }

    [Fact]
    public void The_scan_sees_a_delegating_base_a_governance_wrapper_and_a_composite()
    {
        // the positive control: a scan that found no decorator would pass the gate above vacuously
        var decorated = DecoratedInterfaces.Find(Shipped);

        Assert.Contains(nameof(DelegatingTextClient), decorated[typeof(ITextClient)]);
        Assert.Contains(nameof(BudgetedMediaRouter), decorated[typeof(IMediaRouter)]);
        Assert.Contains(nameof(CompositeMemoryEngine), decorated[typeof(IMemoryEngine)]);
    }

    [Fact]
    public void The_readme_lists_exactly_the_interfaces_the_library_decorates()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));
        var block = Regex.Match(readme,
            "<!-- decorated-interfaces:begin -->(.*?)<!-- decorated-interfaces:end -->", RegexOptions.Singleline);
        Assert.True(block.Success, "README.md has lost its decorated-interfaces block");

        var listed = Regex.Matches(block.Groups[1].Value, @"^\| `(I\w+)` \|", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value);

        Assert.Equal(
            DecoratedInterfaces.Find(Shipped).Keys.Select(t => t.Name).Order(StringComparer.Ordinal),
            listed.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_default_body_on_a_decorated_interface_is_named_with_its_decorator()
    {
        var (unallowed, stale) = DecoratedInterfaces.Check([typeof(IGoverned), typeof(GovernedDecorator)], None);

        var finding = Assert.Single(unallowed);
        Assert.Equal(Key(typeof(IGoverned), "ProbeAsync()"), finding.Member);
        Assert.Equal([nameof(GovernedDecorator)], finding.Decorators);
        Assert.Empty(stale);
    }

    [Fact]
    public void An_interface_whose_members_are_all_abstract_passes()
    {
        Type[] scanned = [typeof(IStrict), typeof(StrictDecorator)];
        var (unallowed, stale) = DecoratedInterfaces.Check(scanned, None);

        Assert.Empty(unallowed);
        Assert.Empty(stale);
        Assert.Contains(nameof(StrictDecorator), DecoratedInterfaces.Find(scanned)[typeof(IStrict)]);
    }

    [Fact]
    public void A_composite_over_a_collection_of_the_interface_decorates_it()
    {
        var finding = Assert.Single(DecoratedInterfaces.Check([typeof(IFannedOut), typeof(FanOut)], None).Unallowed);

        Assert.Equal(Key(typeof(IFannedOut), "FlushAsync()"), finding.Member);
    }

    [Fact]
    public void A_default_body_inherited_from_a_base_interface_is_caught()
    {
        var finding = Assert.Single(DecoratedInterfaces.Check(
            [typeof(IBase), typeof(IDerived), typeof(DerivedDecorator)], None).Unallowed);

        Assert.Equal(Key(typeof(IBase), "PingAsync()"), finding.Member);
        Assert.Equal([nameof(DerivedDecorator)], finding.Decorators);
    }

    [Fact]
    public void Neither_an_adapter_nor_a_record_copy_constructor_decorates()
    {
        // the adapter takes an IAdaptee and IS an IStrict; the record's copy constructor takes its own type
        Assert.Empty(DecoratedInterfaces.Find(
            [typeof(IAdaptee), typeof(IStrict), typeof(Adapter), typeof(IOutcome), typeof(Outcome)]));
    }

    [Fact]
    public void An_allowed_default_body_passes()
    {
        var allowances = new Dictionary<string, string> { [Key(typeof(IGoverned), "ProbeAsync()")] = "a fixture" };

        var (unallowed, stale) = DecoratedInterfaces.Check([typeof(IGoverned), typeof(GovernedDecorator)], allowances);

        Assert.Empty(unallowed);
        Assert.Empty(stale);
    }

    [Fact]
    public void An_allowance_that_stops_matching_is_stale()
    {
        string[] expected =
        [
            Key(typeof(IAdaptee), "FlushAsync()"),  // a default body, but nothing decorates the interface
            Key(typeof(IStrict), "GoneAsync()"),    // no such member
            Key(typeof(IStrict), "RunAsync()"),     // abstract
        ];

        var stale = DecoratedInterfaces.Check(
            [typeof(IStrict), typeof(StrictDecorator), typeof(IAdaptee), typeof(Adapter)],
            expected.ToDictionary(k => k, _ => "a fixture")).Stale;

        Assert.Equal(expected.Order(StringComparer.Ordinal), stale.Order(StringComparer.Ordinal));
    }

    private static readonly Dictionary<string, string> None = [];

    private static string Key(Type type, string member) => $"{type.FullName}.{member}";

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Lyntai.slnx"))) return dir.FullName;
        throw new InvalidOperationException("no Lyntai.slnx above the test assembly");
    }

    private interface IGoverned
    {
        Task RunAsync();

        Task<int> ProbeAsync() => Task.FromResult(0);
    }

    private sealed class GovernedDecorator(IGoverned inner) : IGoverned
    {
        public Task RunAsync() => inner.RunAsync();
    }

    private interface IStrict
    {
        Task RunAsync();
    }

    private sealed class StrictDecorator(IStrict inner) : IStrict
    {
        public Task RunAsync() => inner.RunAsync();
    }

    private interface IFannedOut
    {
        Task RunAsync();

        Task FlushAsync() => Task.CompletedTask;
    }

    private sealed class FanOut(IReadOnlyList<IFannedOut> members) : IFannedOut
    {
        public Task RunAsync() => Task.WhenAll(members.Select(m => m.RunAsync()));
    }

    private interface IBase
    {
        Task PingAsync() => Task.CompletedTask;
    }

    private interface IDerived : IBase
    {
        Task RunAsync();
    }

    private sealed class DerivedDecorator(IDerived inner) : IDerived
    {
        public Task RunAsync() => inner.RunAsync();
    }

    private interface IAdaptee
    {
        Task RunAsync();

        Task FlushAsync() => Task.CompletedTask;
    }

    private sealed class Adapter(IAdaptee inner) : IStrict
    {
        public Task RunAsync() => inner.RunAsync();
    }

    private interface IOutcome
    {
        int Usage => 0;
    }

    private sealed record Outcome(string Text) : IOutcome;
}
