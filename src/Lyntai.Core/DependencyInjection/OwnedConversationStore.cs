using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai;

/// <summary>The store an <see cref="EnrichingConversationStore"/> wraps, registered under a descriptor of its
/// own so the container that built it still disposes it — the container disposes only what a descriptor built,
/// and the wrapper replaced the store's. A store the container did not build (a host's instance, or one
/// registered in its own right) is not <paramref name="owned"/> and is left to its owner.</summary>
internal sealed class OwnedConversationStore(IConversationStore store, bool owned) : IAsyncDisposable, IDisposable
{
    public IConversationStore Store => store;

    public ValueTask DisposeAsync()
    {
        if (!owned) return default;
        if (store is IAsyncDisposable async) return async.DisposeAsync();
        (store as IDisposable)?.Dispose();
        return default;
    }

    public void Dispose()
    {
        if (!owned) return;
        if (store is IDisposable sync) sync.Dispose();
        // an async-only store under a synchronous container dispose: blocking at shutdown beats leaking it
        else if (store is IAsyncDisposable async) async.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Build what <paramref name="backend"/> describes, recording whether the container owns it.</summary>
    public static OwnedConversationStore For(IServiceProvider sp, ServiceDescriptor backend)
    {
        if (backend.ImplementationInstance is IConversationStore given) return new(given, owned: false);
        if (backend.ImplementationFactory is { } factory) return new((IConversationStore)factory(sp), owned: true);
        var type = backend.ImplementationType!;
        return sp.GetService(type) is IConversationStore registered
            ? new(registered, owned: false)
            : new((IConversationStore)ActivatorUtilities.CreateInstance(sp, type), owned: true);
    }
}
