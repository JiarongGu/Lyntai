using System.Reflection;
using Lyntai.Generation;
using Lyntai.Lifecycle;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Lifecycle;

public class ProviderIdentityTests
{
    // The declarations that look redundant next to IProviderIdentity.Id are the BINARY compatibility of the
    // frozen 1.0 surface, and nothing else in the build says so. Adding a base interface is binary-safe;
    // deleting the member from the derived interface is not — a consumer assembly compiled against 1.0 emits
    // `callvirt ILlmProvider::get_Id`, and member resolution does not walk base INTERFACES, so that call
    // throws MissingMethodException until the consumer is recompiled. `consumer-smoke` cannot catch it: it
    // rebuilds the consumer from source every time, which is precisely the step a package upgrade skips.
    // Without this test the next "remove the duplicate declaration" cleanup passes every gate we have.
    [Theory]
    [InlineData(typeof(ILlmProvider))]
    [InlineData(typeof(IGenerationProvider))]
    public void Both_seams_still_declare_Id_themselves(Type seam)
    {
        var declared = seam.GetProperty(
            "Id", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotNull(declared);
        Assert.Equal(typeof(string), declared.PropertyType);
        Assert.NotNull(declared.GetMethod);   // the get_Id slot old IL binds to
    }

    [Fact]
    public void Both_provider_seams_are_provider_identities()
    {
        Assert.True(typeof(IProviderIdentity).IsAssignableFrom(typeof(IGenerationProvider)));
        Assert.True(typeof(IProviderIdentity).IsAssignableFrom(typeof(ILlmProvider)));
    }

    // The whole point of reusing the member both seams already declare: nothing that exists has to change.
    [Fact]
    public void An_existing_implementor_satisfies_it_unchanged()
    {
        IProviderIdentity generation = new FakeGenerationProvider { Id = "a1111" };
        IProviderIdentity llm = new FakeLlmProvider("openai");

        Assert.Equal("a1111", generation.Id);
        Assert.Equal("openai", llm.Id);
    }
}
