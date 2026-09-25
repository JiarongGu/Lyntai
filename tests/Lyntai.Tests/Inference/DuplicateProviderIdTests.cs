using Lyntai.Inference;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Inference;

/// <summary>Nothing refuses two registrations under one id — two default-id ONNX registrations share
/// <c>"onnx"</c> — and every router states that the FIRST registration wins. A named client pooled over that id
/// must follow the same rule rather than failing to resolve.</summary>
public class DuplicateProviderIdTests
{
    [Fact]
    public async Task A_named_client_over_a_duplicated_id_resolves_and_the_first_registration_wins()
    {
        var first = new FakeTextProvider("dup");
        var second = new FakeTextProvider("DUP");
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => first)
            .AddProvider(_ => second)
            .UseDefaultCandidates("dup")
            .AddTextClient("pooled", c => c.UseProviders("dup")));
        using var sp = services.BuildServiceProvider();

        var reply = await sp.GetRequiredService<ITextClientFactory>().Get("pooled")
            .CompleteAsync(new TextRequest { Messages = [TextMessage.User("hi")] });

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Single(first.Calls);
        Assert.Empty(second.Calls);
    }
}
