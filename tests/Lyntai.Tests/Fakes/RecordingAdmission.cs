using Lyntai.Inference;

namespace Lyntai.Tests.Fakes;

/// <summary>An <see cref="IProviderAdmission"/> that records every key it is asked to admit and every permit
/// handed back, optionally in front of a real one. A permit test asserts <see cref="Entered"/> as well as the
/// release: "nothing is held" is also what a path that never entered admission at all leaves behind.</summary>
public sealed class RecordingAdmission(IProviderAdmission? inner = null) : IProviderAdmission
{
    private int _released;

    /// <summary>Every key entered, in order.</summary>
    public List<ProviderKey> Entered { get; } = [];

    /// <summary>How many permits have been disposed.</summary>
    public int Released => Volatile.Read(ref _released);

    public async ValueTask<IDisposable> EnterAsync(ProviderKey key, CancellationToken ct = default)
    {
        lock (Entered) Entered.Add(key);
        var held = inner is null ? null : await inner.EnterAsync(key, ct).ConfigureAwait(false);
        return new Permit(this, held);
    }

    private sealed class Permit(RecordingAdmission owner, IDisposable? held) : IDisposable
    {
        public void Dispose()
        {
            held?.Dispose();
            Interlocked.Increment(ref owner._released);
        }
    }
}
