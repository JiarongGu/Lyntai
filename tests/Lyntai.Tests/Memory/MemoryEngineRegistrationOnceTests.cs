using Lyntai.Memory;
using Lyntai.Memory.Seeding;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>Several <c>AddMemoryEngine</c> calls register several ENGINES and one of everything that is a
/// fact about the container — the factory above all, which runs the wiring report when it is built.</summary>
public class MemoryEngineRegistrationOnceTests
{
    [Fact]
    public void Three_engines_register_one_factory_and_one_of_each_default()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseInMemoryStorage()
            .AddMemoryEngine("a", e => e.UseLexical())
            .AddMemoryEngine("b", e => e.UseGraph())
            .AddMemoryEngine("c", e => e.UseGraph()));

        Assert.Single(services, d => d.ServiceType == typeof(IMemoryEngineFactory));
        Assert.Equal(3, services.Count(d => d.ServiceType == typeof(IMemoryEngine)));
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IMemorySeedSource)));

        using var sp = services.BuildServiceProvider();
        Assert.Single(sp.GetServices<IMemoryEngineFactory>());
    }
}
