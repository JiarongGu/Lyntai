using Lyntai;
using Lyntai.Llm;
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
                .DefaultCandidates("claude-cli"));
            using var sp = services.BuildServiceProvider();

            var provider = sp.GetServices<ILlmProvider>().Single();
            Assert.Equal("claude-cli", provider.Id);

            var router = sp.GetRequiredService<ILlmRouter>();
            var reply = await router.CompleteAsync([new("claude-cli")],
                new LlmRequest { Messages = [LlmMessage.User("via router")] });

            Assert.Equal(LlmVerdict.Ok, reply.Verdict);
            Assert.Equal("stub reply: via router", reply.Text);
        }
        finally
        {
            Environment.SetEnvironmentVariable("LYNTAI_PROVIDER_CMD", null);
        }
    }
}
