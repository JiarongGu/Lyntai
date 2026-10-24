using System.Reflection;

using Lyntai.Inference.Budgeting;
using Lyntai.Inference.Caching;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Storage;

/// <summary>
/// The guarantee that every storage contract runs on every shipped backend by STRUCTURE: each contract feeds
/// one theory through <see cref="ContractFactsAttribute"/>, so a new fact needs no wiring, and these facts
/// check what that cannot — a missing backend, a fact the binder cannot call, an exclusion gone stale.
/// </summary>
/// <remarks>The graph store's twin is <c>MemoryGraphStoreCoverageTests</c>.</remarks>
public class StoreContractCoverageTests
{
    /// <summary>The domain interfaces a storage backend implements, bar <see cref="IMemoryGraphStore"/>.</summary>
    private static readonly Type[] Domains =
    [
        typeof(IKeyValueStore), typeof(IConversationStore), typeof(IMemoryStore), typeof(IScoreStore),
        typeof(ITraceStore), typeof(IPromptVersionStore), typeof(IJobStore), typeof(ICuratedMemoryStore),
        typeof(IVectorStore), typeof(IResponseCache), typeof(IUsageTracker), typeof(IReservingUsageTracker),
    ];

    /// <summary>Shipped implementations that are not a backend of their own, by full name, with the reason. An
    /// entry naming no shipped type FAILS, so the list cannot rot into permanent holes.</summary>
    private static readonly Dictionary<string, string> NotABackend = new(StringComparer.Ordinal)
    {
        ["Lyntai.Storage.EnrichingConversationStore"] = "a decorator: every read and write passes to the backend it wraps",
        ["Lyntai.Memory.VectorSpaceStore+Bare"] = VectorSpace,
        ["Lyntai.Memory.VectorSpaceStore+Listable"] = VectorSpace,
        ["Lyntai.Memory.VectorSpaceStore+Readable"] = VectorSpace,
        ["Lyntai.Memory.VectorSpaceStore+Full"] = VectorSpace,
    };

    private const string VectorSpace = "a decorator: it guards each collection by the vector space that wrote it (D199) and passes to the store it wraps";

    private static readonly Type[] TestTypes = typeof(StoreContractCoverageTests).Assembly.GetTypes();

    /// <summary>Each per-contract base (<c>KeyValueStoreContractFacts</c>, …): its store interface and contract.</summary>
    private static (Type Base, Type Store, Type Contract)[] SuiteBases() =>
        [.. TestTypes
            .Where(t => t.IsAbstract && t.BaseType is { IsGenericType: true } b
                && b.GetGenericTypeDefinition() == typeof(StoreContractSuite<>))
            .Select(t => (t, t.BaseType!.GetGenericArguments()[0], ContractOf(t)))];

    private static Type ContractOf(Type suiteBase)
    {
        var theories = suiteBase.GetMethods().Where(m => m.IsDefined(typeof(ContractFactsAttribute))).ToList();
        Assert.True(theories.Count == 1, $"{suiteBase.Name} must declare exactly one [ContractFacts] theory");
        return theories[0].GetCustomAttribute<ContractFactsAttribute>()!.Contract;
    }

    private static Type[] ConcreteSuites(Type suiteBase) =>
        [.. TestTypes.Where(t => t is { IsClass: true, IsAbstract: false } && suiteBase.IsAssignableFrom(t))];

    [Fact]
    public void Every_storage_contract_class_drives_a_suite_base()
    {
        // A contract no base names would run nowhere, and nothing else would say so.
        var driven = SuiteBases().Select(s => s.Contract).ToHashSet();
        var contracts = TestTypes
            .Where(t => t is { IsAbstract: true, IsSealed: true } && t.Name.EndsWith("Contract", StringComparison.Ordinal))
            .Where(t => StoreContract.Declared(t).Any(m => m.GetParameters() is [var first, ..] && Domains.Contains(first.ParameterType)))
            .ToList();

        Assert.NotEmpty(contracts);
        Assert.All(contracts, c => Assert.True(driven.Contains(c), $"{c.Name} is a storage contract no StoreContractSuite base drives"));
    }

    [Fact]
    public void Every_fact_takes_a_shape_the_suite_can_bind()
    {
        // The binder knows the store, a string key, a MutableClock and its Advance. Anything else would throw at
        // invoke time on every backend's case rather than be reported as a harness problem.
        foreach (var (suiteBase, store, contract) in SuiteBases())
        {
            var facts = StoreContract.Declared(contract);
            Assert.True(facts.Length > 0, $"{contract.Name} declares no fact, so {suiteBase.Name} is vacuously green");
            foreach (var fact in facts)
            {
                var ps = fact.GetParameters();
                Assert.True(ps.Length > 0 && ps[0].ParameterType.IsAssignableFrom(store),
                    $"{contract.Name}.{fact.Name} does not take the {store.Name} {suiteBase.Name} supplies first");
                Assert.All(ps.Skip(1), p => Assert.True(
                    p.ParameterType == typeof(string) || p.ParameterType == typeof(MutableClock) || p.ParameterType == typeof(Action<TimeSpan>),
                    $"{contract.Name}.{fact.Name} takes a {p.ParameterType.Name}, which the suite cannot bind"));
            }
        }
    }

    [Fact]
    public void Every_SHIPPED_store_is_driven_by_a_suite_of_its_contract()
    {
        // Driving a contract from one theory makes a missing FACT impossible; it does nothing about a missing
        // BACKEND. So every concrete implementation in every Lyntai assembly must have a concrete suite whose
        // name starts with its backend prefix (`SqliteKeyValueStore` -> a `Sqlite*` KeyValueStoreContractFacts).
        var shipped = ShippedTypes();
        Assert.Contains(shipped, t => t.Name == "PostgresKeyValueStore");   // the scan really reaches the packages

        var missing = new List<string>();
        var exempted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (suiteBase, store, contract) in SuiteBases())
        {
            var suites = ConcreteSuites(suiteBase).Select(s => s.Name).ToList();
            var noun = store.Name[1..].Replace("Reserving", string.Empty, StringComparison.Ordinal);
            foreach (var impl in shipped.Where(t => store.IsAssignableFrom(t)))
            {
                if (NotABackend.ContainsKey(impl.FullName!)) { exempted.Add(impl.FullName!); continue; }
                var prefix = impl.Name.Replace(noun, string.Empty, StringComparison.Ordinal);
                if (!suites.Any(s => s.StartsWith(prefix, StringComparison.Ordinal)))
                    missing.Add($"{impl.Name}: no {prefix}* suite derives {suiteBase.Name} ({contract.Name})");
            }
        }

        Assert.True(missing.Count == 0, "shipped stores whose contract never runs against them:\n  " + string.Join("\n  ", missing));
        var stale = NotABackend.Keys.Where(k => !exempted.Contains(k)).ToList();
        Assert.True(stale.Count == 0, $"{nameof(NotABackend)} names no shipped store — delete:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void Every_exclusion_names_a_fact_of_its_suites_contract_and_says_why()
    {
        // An exclusion that stops matching FAILS, the discipline every allowance in this repository carries.
        foreach (var (suiteBase, _, contract) in SuiteBases())
        {
            var declared = StoreContract.Declared(contract).Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var suite in ConcreteSuites(suiteBase))
            foreach (var exclusion in suite.GetCustomAttributes<ExcludesFactAttribute>())
            {
                Assert.True(declared.Contains(exclusion.Fact), $"{suite.Name} excludes {exclusion.Fact}, which {contract.Name} does not declare");
                Assert.False(string.IsNullOrWhiteSpace(exclusion.Reason), $"{suite.Name} excludes {exclusion.Fact} without a reason");
            }
        }
    }

    [Fact]
    public void A_suite_that_shares_its_store_clears_it_before_an_unscoped_fact()
    {
        // An [Unscoped] fact on a shared store reads every other case's rows: without a clear it is flaky at
        // best. A suite that overrides SharesItsStore must therefore override ClearAsync wherever it is owed.
        const BindingFlags declaredOnly = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var (suiteBase, _, contract) in SuiteBases())
        {
            if (!StoreContract.Declared(contract).Any(m => m.IsDefined(typeof(UnscopedAttribute)))) continue;
            foreach (var suite in ConcreteSuites(suiteBase).Where(s => s.GetProperty("SharesItsStore", declaredOnly) is not null))
                Assert.True(suite.GetMethod("ClearAsync", declaredOnly) is not null,
                    $"{suite.Name} shares its store but never clears it, and {contract.Name} has an [Unscoped] fact");
        }
    }

    private static Type[] ShippedTypes()
    {
        var assemblies = Directory.EnumerateFiles(AppContext.BaseDirectory, "Lyntai*.dll")
            .Where(path => !Path.GetFileName(path).StartsWith("Lyntai.Tests", StringComparison.Ordinal))
            .Select(path => Assembly.Load(AssemblyName.GetAssemblyName(path)))
            .Distinct();

        return [.. assemblies.SelectMany(Loadable).Where(t => t is { IsClass: true, IsAbstract: false })];

        // a package whose optional dependency is absent still has its OTHER types scanned
        static IEnumerable<Type> Loadable(Assembly assembly)
        {
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.OfType<Type>(); }
        }
    }
}
