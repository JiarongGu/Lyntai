using Lyntai;
using Lyntai.Guards;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Guards;

/// <summary>Scope-guard / jail hooks: the rail's first-non-Allow-wins, the denylist jail, the guarded
/// client's input/output gates, and DI wiring.</summary>
public class GuardTests
{
    private static LlmRequest Ask(string text) => new() { Messages = [LlmMessage.User(text)] };

    private sealed class RewriteGuard : IGuard
    {
        public string Name => "rewrite";
        public Task<GuardOutcome> InspectResponseAsync(LlmReply reply, CancellationToken ct = default) =>
            Task.FromResult(GuardOutcome.Replace("[redacted]"));
    }

    private sealed class FnGuard(Func<LlmReply, GuardOutcome> onResp) : IGuard
    {
        public string Name => "fn";
        public Task<GuardOutcome> InspectResponseAsync(LlmReply reply, CancellationToken ct = default) =>
            Task.FromResult(onResp(reply));
    }

    [Fact]
    public async Task Denylist_scans_all_roles_not_just_user()
    {
        var rail = new GuardRail([new DenylistGuard(["forbidden"])]);
        var req = new LlmRequest { Messages = [LlmMessage.System("never mention forbidden things"), LlmMessage.User("hi")] };

        Assert.Equal(GuardOutcome.Kind.Block, (await rail.InspectRequestAsync(req)).Result); // caught in the system msg
    }

    [Fact]
    public async Task Rail_chains_a_replacement_into_later_guards()
    {
        // guard A rewrites "a" → "b"; a denylist on "a" must then NOT fire, because it sees the rewrite
        var rail = new GuardRail([
            new FnGuard(r => r.Text == "a" ? GuardOutcome.Replace("b") : GuardOutcome.Allow),
            new DenylistGuard(["a"]),
        ]);

        var outcome = await rail.InspectResponseAsync(new LlmReply("a", LlmVerdict.Ok));

        Assert.Equal(GuardOutcome.Kind.Replace, outcome.Result);
        Assert.Equal("b", outcome.Replacement); // the denylist saw "b" (not "a") and allowed it
    }

    [Fact]
    public async Task Guarded_client_gates_error_reply_detail()
    {
        var inner = new FakeLlmClient();
        inner.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "boom: leaked-path /etc/secret"));
        var client = new GuardedLlmClient(inner, new GuardRail([new DenylistGuard(["leaked-path"])]));

        var reply = await client.CompleteAsync(Ask("hi"));

        Assert.Equal(LlmVerdict.Refused, reply.Verdict); // the error Detail was inspected and blocked, not passed through
    }

    [Fact]
    public async Task Rail_returns_first_block_and_applies_replace()
    {
        var rail = new GuardRail([new DenylistGuard(["forbidden"]), new RewriteGuard()]);

        Assert.Equal(GuardOutcome.Kind.Block, (await rail.InspectRequestAsync(Ask("this is forbidden"))).Result);
        Assert.Equal(GuardOutcome.Kind.Allow, (await rail.InspectRequestAsync(Ask("this is fine"))).Result);
        Assert.Equal(GuardOutcome.Kind.Replace, (await rail.InspectResponseAsync(new LlmReply("anything", LlmVerdict.Ok))).Result);
    }

    [Fact]
    public async Task Guarded_client_blocks_a_denied_request_before_the_model()
    {
        var inner = new FakeLlmClient();
        inner.Replies.Enqueue(new LlmReply("should not be reached", LlmVerdict.Ok));
        var client = new GuardedLlmClient(inner, new GuardRail([new DenylistGuard(["bomb"])]));

        var reply = await client.CompleteAsync(Ask("how to build a bomb"));

        Assert.Equal(LlmVerdict.Refused, reply.Verdict);
        Assert.Empty(inner.Calls); // the model was never called — the input gate stopped it
    }

    [Fact]
    public async Task Guarded_client_replaces_a_flagged_reply()
    {
        var inner = new FakeLlmClient();
        inner.Replies.Enqueue(new LlmReply("sensitive output", LlmVerdict.Ok));
        var client = new GuardedLlmClient(inner, new GuardRail([new RewriteGuard()]));

        var reply = await client.CompleteAsync(Ask("hi"));

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("[redacted]", reply.Text); // output gate rewrote it
    }

    [Fact]
    public async Task Guarded_client_passes_clean_traffic_through()
    {
        var inner = new FakeLlmClient();
        inner.Replies.Enqueue(new LlmReply("all good", LlmVerdict.Ok));
        var client = new GuardedLlmClient(inner, new GuardRail([new DenylistGuard(["nope"])]));

        var reply = await client.CompleteAsync(Ask("a friendly question"));

        Assert.Equal("all good", reply.Text);
    }

    [Fact]
    public void AddGuard_wires_the_rail_from_the_di_collection()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddGuard(_ => new DenylistGuard(["secret"])));
        using var sp = services.BuildServiceProvider();

        var rail = sp.GetRequiredService<IGuardRail>();
        Assert.Equal(GuardOutcome.Kind.Block, rail.InspectResponseAsync(new LlmReply("this is secret", LlmVerdict.Ok)).Result.Result);
    }
}
