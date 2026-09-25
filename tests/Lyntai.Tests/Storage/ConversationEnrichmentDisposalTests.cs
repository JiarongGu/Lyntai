using System.Reflection;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

/// <summary>Registering an enricher replaces the <see cref="IConversationStore"/> descriptor with the
/// <see cref="EnrichingConversationStore"/> wrapper. The store it wraps must still be disposed by the container
/// that built it — and a store the host handed over as an instance must still be left to the host, which is
/// how the container treats it unwrapped.</summary>
public sealed class ConversationEnrichmentDisposalTests
{
    [Fact]
    public async Task A_store_the_container_built_is_still_disposed_once_an_enricher_wraps_it()
    {
        var store = DisposableStore.Create();
        var services = new ServiceCollection();
        services.AddSingleton<IConversationStore>(_ => (IConversationStore)store);

        await using (var sp = Compose(services))
            Assert.IsType<EnrichingConversationStore>(sp.GetRequiredService<IConversationStore>());

        Assert.True(store.Disposed);
    }

    [Fact]
    public async Task A_store_the_host_registered_as_an_instance_is_left_to_the_host()
    {
        var store = DisposableStore.Create();
        var services = new ServiceCollection();
        services.AddSingleton((IConversationStore)store);

        await using (var sp = Compose(services))
            Assert.IsType<EnrichingConversationStore>(sp.GetRequiredService<IConversationStore>());

        Assert.False(store.Disposed);
    }

    private static ServiceProvider Compose(IServiceCollection services)
    {
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .AddConversationEnricher(_ => new NoOpEnricher()));
        return services.BuildServiceProvider();
    }

    private sealed class NoOpEnricher : IConversationEnricher;

    /// <summary>An <see cref="IConversationStore"/> whose only real behaviour is recording its disposal — none
    /// of the store's members is called by composition.</summary>
    public class DisposableStore : DispatchProxy, IAsyncDisposable
    {
        public bool Disposed { get; private set; }

        public static DisposableStore Create() => (DisposableStore)(object)Create<IConversationStore, DisposableStore>();

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return default;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }
}
