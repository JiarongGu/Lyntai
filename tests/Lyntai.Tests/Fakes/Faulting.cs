using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Lyntai.Tests.Fakes;

/// <summary>A stand-in for any interface that FORWARDS every call to a real instance, except the members a test
/// names in <see cref="Faults"/>, which throw instead — the transient store fault landing in one window: between
/// a slot acquire and its claim, or after a handler succeeded and before its completion was recorded.
/// <c>Faulting.Over&lt;IJobStore&gt;(inner, out var faults)</c> needs no edit when the interface grows. A name
/// covers every overload; the throw is synchronous, as an expression-bodied <c>=&gt; throw</c> member's is.</summary>
public class Faulting : DispatchProxy
{
    private object _inner = null!;
    private ConcurrentDictionary<string, byte> _faults = null!;

    /// <param name="inner">What every unfaulted call reaches.</param>
    /// <param name="faults">Member names (<c>nameof(IJobStore.CompleteAsync)</c>) that throw an
    /// <see cref="InvalidOperationException"/> while present; add and remove between passes.</param>
    public static T Over<T>(T inner, out FaultSet faults) where T : class
    {
        var proxy = Create<T, Faulting>();
        var self = (Faulting)(object)proxy;
        self._inner = inner;
        self._faults = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        faults = new FaultSet(self._faults);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (_faults.ContainsKey(targetMethod.Name))
            throw new InvalidOperationException($"the store faulted on {targetMethod.Name}");
        try { return targetMethod.Invoke(_inner, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
            throw; // unreachable
        }
    }

    /// <summary>The members a <see cref="Faulting"/> proxy currently throws from.</summary>
    public sealed class FaultSet(ConcurrentDictionary<string, byte> names)
    {
        public void Add(params string[] members) { foreach (var m in members) names[m] = 0; }
        public void Clear() => names.Clear();
    }
}
