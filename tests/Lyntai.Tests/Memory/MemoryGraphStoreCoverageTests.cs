using System.Reflection;

using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// The guarantee that per-backend contract coverage is STRUCTURAL — the mechanism that replaced a
/// hand-bumped literal.
/// </summary>
/// <remarks>
/// These facts are about the harness, not about any store. They exist because the defect they close is a
/// harness defect: the old check could not see a fact wired to Postgres alone, and a harness whose failure
/// mode is a false PASS cannot be validated by running it.
/// </remarks>
public class MemoryGraphStoreCoverageTests
{
    /// <summary>Every backend suite that must run the whole contract.</summary>
    private static readonly Type[] Backends =
    [
        typeof(InMemoryMemoryGraphStoreTests),
        typeof(SqliteMemoryGraphStoreTests),
        typeof(Lyntai.Tests.Storage.PostgresStorageTests),
    ];

    [Fact]
    public void The_theory_source_offers_exactly_the_facts_the_contract_declares()
    {
        // If these can drift apart, the theory silently stops covering whatever the filter dropped — the
        // same permissive direction the counted assertion failed in.
        var declared = typeof(MemoryGraphStoreContract)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType))
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var offered = MemoryGraphStoreFacts.Declared().Select(m => m.Name).ToArray();

        Assert.Equal(declared, offered);
        Assert.NotEmpty(offered);   // a source that offered nothing would make every backend vacuously green
    }

    [Fact]
    public void Every_fact_is_resolvable_and_takes_one_of_the_two_known_shapes()
    {
        // RunAsync branches on parameter count. A third shape would fall into the elapsed-clock branch and
        // throw at invoke time on ONE backend's case rather than being reported as a harness problem, so
        // the shape set is pinned here where the message can say so.
        foreach (var method in MemoryGraphStoreFacts.Declared())
        {
            var ps = method.GetParameters();
            Assert.True(ps.Length is 2 or 3, $"{method.Name} takes {ps.Length} parameters; RunAsync knows 2 and 3");
            Assert.Equal(typeof(IMemoryGraphStore), ps[0].ParameterType);
            Assert.Equal(typeof(string), ps[1].ParameterType);
            if (ps.Length == 3) Assert.Equal(typeof(Action<TimeSpan>), ps[2].ParameterType);
        }
    }

    [Fact]
    public void Every_backend_drives_the_contract_from_the_shared_theory_source()
    {
        // THE point of Part 70. Each backend must have a theory fed by MemoryGraphStoreFacts.Names, because
        // that is what makes exhaustiveness hold by construction; a backend that drifted back to hand-wired
        // [Fact]s would silently cover whatever someone remembered.
        foreach (var backend in Backends)
        {
            var fed = backend
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .SelectMany(m => m.GetCustomAttributesData())
                .Where(a => a.AttributeType.Name is "MemberDataAttribute")
                .Any(a => a.ConstructorArguments.Count > 0
                    && (string)a.ConstructorArguments[0].Value! == nameof(MemoryGraphStoreFacts.Names));

            Assert.True(fed,
                $"{backend.Name} does not drive {nameof(MemoryGraphStoreContract)} from "
                + $"{nameof(MemoryGraphStoreFacts)}.{nameof(MemoryGraphStoreFacts.Names)}, so its coverage is "
                + "whatever was wired by hand — the hole Part 70 closed.");
        }
    }

    [Fact]
    public void Every_SHIPPED_graph_store_has_a_suite_in_the_list_above()
    {
        // Closes the one hole the list above cannot close by itself. Driving the three known backends from
        // a shared source makes a missing FACT impossible; it does nothing about a missing BACKEND — a
        // fourth store could ship with no contract suite at all and every check here would stay green,
        // which is the same defect one level up.
        //
        // So the list is checked against the tree rather than trusted: every concrete IMemoryGraphStore a
        // Lyntai package ships must be named by one of the suites. Adding a backend therefore fails this
        // until its suite exists, which is the point.
        var shipped = new[]
        {
            typeof(Lyntai.Storage.InMemory.InMemoryMemoryGraphStore).Assembly,
            typeof(Lyntai.Storage.Sqlite.SqliteMemoryGraphStore).Assembly,
            typeof(Lyntai.Storage.Postgres.PostgresMemoryGraphStore).Assembly,
            typeof(IMemoryGraphStore).Assembly,
        }
            .Distinct()
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IMemoryGraphStore).IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(shipped);

        var suiteNames = string.Join(" ", Backends.Select(b => b.Name));
        foreach (var store in shipped)
        {
            // `SqliteMemoryGraphStore` -> `SqliteMemoryGraphStoreTests`; Postgres's suite is the broader
            // `PostgresStorageTests`, so the match is on the backend PREFIX rather than the whole name.
            var prefix = store.Replace("MemoryGraphStore", string.Empty);
            Assert.True(suiteNames.Contains(prefix, StringComparison.Ordinal),
                $"{store} ships but no suite in {nameof(Backends)} covers it — add one driven from "
                + $"{nameof(MemoryGraphStoreFacts)}.{nameof(MemoryGraphStoreFacts.Names)}, then list it here.");
        }
    }

    [Fact]
    public void No_backend_suite_still_carries_a_hand_bumped_coverage_LITERAL()
    {
        // The literal is the defect, not the count. While one exists someone will bump it, and bumping it is
        // exactly the move that hid a Postgres-only fact.
        foreach (var backend in Backends)
        {
            var counting = backend
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic)
                .Any(m => m.Name.Contains("covered", StringComparison.OrdinalIgnoreCase));

            Assert.False(counting, $"{backend.Name} looks like it still counts coverage rather than deriving it");
        }
    }
}
