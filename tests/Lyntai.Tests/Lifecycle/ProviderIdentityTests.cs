using Lyntai.Generation;
using Lyntai.Lifecycle;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Lifecycle;

public class ProviderIdentityTests
{
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
