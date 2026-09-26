using System.Reflection;

namespace Lyntai.Tests.Fakes;

/// <summary>A stand-in for any interface whose EVERY member throws — the store that is down, the backend
/// whose own deadline fired. <c>Throwing.Of&lt;IMemoryStore&gt;(() =&gt; new OperationCanceledException(…))</c>
/// needs no hand-written class per interface, so nothing is edited when the interface grows. The throw is
/// synchronous, as an expression-bodied <c>=&gt; throw</c> member's is.</summary>
public class Throwing : DispatchProxy
{
    private Func<Exception> _error = () => new InvalidOperationException("no error configured");

    /// <param name="error">Builds the exception each call throws — fresh per call, so a test can assert on
    /// it without one throw's stack trace leaking into the next.</param>
    public static T Of<T>(Func<Exception> error) where T : class
    {
        var proxy = Create<T, Throwing>();
        ((Throwing)(object)proxy)._error = error;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw _error();
}
