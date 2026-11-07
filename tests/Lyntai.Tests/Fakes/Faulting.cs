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
    private ConcurrentDictionary<string, Func<Exception>> _faults = null!;
    private ConcurrentDictionary<string, int> _calls = null!;

    /// <param name="inner">What every unfaulted call reaches.</param>
    /// <param name="faults">Member names (<c>nameof(IJobStore.CompleteAsync)</c>) that throw an
    /// <see cref="InvalidOperationException"/> while present; add and remove between passes.</param>
    public static T Over<T>(T inner, out FaultSet faults) where T : class
    {
        var proxy = Create<T, Faulting>();
        var self = (Faulting)(object)proxy;
        self._inner = inner;
        self._faults = new ConcurrentDictionary<string, Func<Exception>>(StringComparer.Ordinal);
        self._calls = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        faults = new FaultSet(self._faults, self._calls);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        _calls.AddOrUpdate(targetMethod.Name, 1, (_, n) => n + 1);
        if (_faults.TryGetValue(targetMethod.Name, out var fault)) throw fault();
        try { return targetMethod.Invoke(_inner, args); }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(ex.InnerException);
            throw; // unreachable
        }
    }

    /// <summary>The members a <see cref="Faulting"/> proxy currently throws from.</summary>
    public sealed class FaultSet(ConcurrentDictionary<string, Func<Exception>> names, ConcurrentDictionary<string, int> calls)
    {
        public void Add(params string[] members)
        {
            foreach (var m in members) names[m] = () => new InvalidOperationException($"the store faulted on {m}");
        }

        /// <summary>Throw what <paramref name="exception"/> makes — a <see cref="TaskCanceledException"/> a store's
        /// own timeout raises, where <see cref="Add"/> throws an <see cref="InvalidOperationException"/>.</summary>
        public void Add(string member, Func<Exception> exception) => names[member] = exception;

        public void Clear() => names.Clear();

        /// <summary>How many times <paramref name="member"/> was called, faulted or not.</summary>
        public int Calls(string member) => calls.TryGetValue(member, out var n) ? n : 0;
    }
}
