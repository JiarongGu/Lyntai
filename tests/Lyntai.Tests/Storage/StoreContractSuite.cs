using System.Reflection;

using Lyntai.Tests.Fakes;
using Xunit.Sdk;

namespace Lyntai.Tests.Storage;

/// <summary>The fact set of a static storage contract class: every public static method returning
/// <see cref="Task"/> or <see langword="void"/>, in a stable order. A contract class holds facts ONLY, so this
/// is an exact description of it; a helper belongs in a private member.</summary>
public static class StoreContract
{
    public static MethodInfo[] Declared(Type contract) =>
        [.. contract.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(void) || typeof(Task).IsAssignableFrom(m.ReturnType))
            .OrderBy(m => m.Name, StringComparer.Ordinal)];

    public static MethodInfo Find(Type contract, string fact) =>
        Array.Find(Declared(contract), m => m.Name == fact)
        ?? throw new InvalidOperationException($"'{fact}' is not a fact on {contract.Name}.");
}

/// <summary>Feeds a contract suite's theory with every fact of <paramref name="contract"/>, minus the ones
/// the CONCRETE suite class excludes by <see cref="ExcludesFactAttribute"/>. Resolved per suite, so a fact
/// added to the contract is a new case on every backend the moment it compiles.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ContractFactsAttribute(Type contract) : DataAttribute
{
    public Type Contract { get; } = contract;

    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        var suite = testMethod.ReflectedType!;   // the concrete backend class, not the base declaring the theory
        var excluded = suite.GetCustomAttributes<ExcludesFactAttribute>().Select(e => e.Fact).ToHashSet();
        return StoreContract.Declared(Contract).Where(m => !excluded.Contains(m.Name)).Select(m => new object[] { m.Name });
    }
}

/// <summary>A contract fact this backend deliberately does NOT run, with the reason. Read by the theory source
/// and by <see cref="StoreContractCoverageTests"/>, which fails an exclusion naming no fact.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ExcludesFactAttribute(string fact, string reason) : Attribute
{
    public string Fact { get; } = fact;
    public string Reason { get; } = reason;
}

/// <summary>On a contract fact: it touches state no key it is handed can isolate — the whole table, or a fixed
/// id. A backend whose store is SHARED across cases clears it before running one.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UnscopedAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

/// <summary>Runs one fact of a static storage contract against a store the backend supplies, binding the
/// fact's parameters by type: the store, a key per <see cref="string"/>, the suite's <see cref="Clock"/> or its
/// <see cref="MutableClock.Advance"/>.</summary>
/// <remarks>An isolated backend (a fresh store per case) hands each key its declared default, or <c>"k"</c>.
/// A backend that <see cref="SharesItsStore"/> hands a key unique to the case instead, and is cleared before
/// an <see cref="UnscopedAttribute"/> fact — the Postgres container is one database for the whole run.</remarks>
public abstract class StoreContractSuite<TStore> where TStore : class
{
    protected MutableClock Clock { get; } = new();

    /// <summary>The store for one case; <paramref name="fact"/> is there for the contract whose facts each need
    /// a differently configured store. A backend that cannot run here SKIPS from inside it.</summary>
    protected abstract TStore CreateStore(string fact);

    protected virtual bool SharesItsStore => false;

    /// <summary>Empties the shared store before an unscoped fact. Only called when <see cref="SharesItsStore"/>.</summary>
    protected virtual Task ClearAsync() => Task.CompletedTask;

    protected async Task RunAsync(Type contract, string fact)
    {
        var method = StoreContract.Find(contract, fact);
        var store = CreateStore(fact);
        if (SharesItsStore && method.IsDefined(typeof(UnscopedAttribute))) await ClearAsync();

        var scope = Guid.NewGuid().ToString("N");
        var args = method.GetParameters().Select(p => Bind(p, store, scope)).ToArray();

        // unwrapped, so a synchronous fact's assertion surfaces as itself rather than a TargetInvocationException
        var result = method.Invoke(null, BindingFlags.DoNotWrapExceptions, null, args, null);
        if (result is Task task) await task;
    }

    private object Bind(ParameterInfo p, TStore store, string scope)
    {
        if (p.Position == 0 && p.ParameterType.IsInstanceOfType(store)) return store;
        if (p.ParameterType == typeof(string))
        {
            var declared = p.HasDefaultValue ? (string?)p.DefaultValue : null;
            return SharesItsStore ? $"{declared ?? p.Name}-{scope}" : declared ?? "k";
        }
        if (p.ParameterType == typeof(MutableClock)) return Clock;
        if (p.ParameterType == typeof(Action<TimeSpan>)) return new Action<TimeSpan>(Clock.Advance);
        throw new InvalidOperationException(
            $"{p.Member.DeclaringType!.Name}.{p.Member.Name} takes a {p.ParameterType.Name} ('{p.Name}'), which no "
            + $"{nameof(StoreContractSuite<TStore>)} binds.");
    }
}
