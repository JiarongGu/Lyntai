using Lyntai.Inference;
using Lyntai;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

[Collection("provider-cmd-env")] // serialized with other tests that set LYNTAI_PROVIDER_CMD
public class AddClaudeCliProviderTests
{
    [Fact]
    public async Task Registered_provider_serves_through_the_router_by_id()
    {
        Environment.SetEnvironmentVariable("LYNTAI_PROVIDER_CMD", ClaudeCliProviderTests.StubCommand);
        try
        {
            var services = new ServiceCollection();
            services.AddLyntai(b => b
                .AddClaudeCliProvider()
                .UseDefaultCandidates("claude-cli"));
            using var sp = services.BuildServiceProvider();

            var provider = sp.GetServices<IModelProvider>().Single();
            Assert.Equal("claude-cli", provider.Id);

            var router = sp.GetRequiredService<ITextRouter>();
            var reply = await router.CompleteAsync([new("claude-cli")],
                new TextRequest { Messages = [TextMessage.User("via router")] });

            Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
            Assert.Equal("stub reply: via router", reply.Text);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LYNTAI_PROVIDER_CMD", null);
        }
    }
}
