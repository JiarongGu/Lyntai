using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Inference.Cli;
using Lyntai.Tests.Fakes;
using static Lyntai.Tests.Fakes.FakeProcessRunner;

namespace Lyntai.Tests.Providers;

/// <summary>A CLI spawn's tool provisioning sees the call it serves — on both doors — and a provisioner that
/// predates the request-aware member still runs.</summary>
public class CliToolRequestTests
{
    private sealed class Recording : ICliToolProvisioner
    {
        public List<CliToolRequest> Seen { get; } = [];

        public Task<CliToolSession> ProvisionAsync(CancellationToken ct = default) =>
            throw new InvalidOperationException("the engine must call the request-aware member");

        public Task<CliToolSession> ProvisionAsync(CliToolRequest request, CancellationToken ct = default)
        {
            Seen.Add(request);
            return Task.FromResult(new CliToolSession([]));
        }
    }

    private sealed class RequestBlind : ICliToolProvisioner
    {
        public int Calls { get; private set; }

        public Task<CliToolSession> ProvisionAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new CliToolSession([]));
        }
    }

    private static TextRequest Ask(string consumer) =>
        new() { Messages = [TextMessage.User("hello")], Consumer = consumer };

    [Fact]
    public async Task A_completion_hands_the_provisioner_its_request_and_backend()
    {
        var provisioner = new Recording();
        var engine = new CliProviderEngine(new FakeCliBackend(), new FakeProcessRunner { RunResult = Ok("result:hi") },
            new LyntaiOptions(), command: "fakecli", provisioner: provisioner);

        await engine.CompleteAsync(Ask("study"));

        var seen = Assert.Single(provisioner.Seen);
        Assert.Equal("study", seen.Request.Consumer);
        Assert.Equal("fake-cli", seen.BackendId);
    }

    [Fact]
    public async Task A_stream_hands_the_provisioner_its_request_too()
    {
        var provisioner = new Recording();
        var engine = new CliProviderEngine(new FakeCliBackend(), new FakeProcessRunner(["text:hi", "result:hi"]),
            new LyntaiOptions(), command: "fakecli", provisioner: provisioner);

        await foreach (var _ in engine.StreamAsync(Ask("study"))) { }

        Assert.Equal("study", Assert.Single(provisioner.Seen).Request.Consumer);
    }

    [Fact]
    public async Task A_provisioner_that_predates_the_request_still_runs()
    {
        var provisioner = new RequestBlind();
        var engine = new CliProviderEngine(new FakeCliBackend(), new FakeProcessRunner { RunResult = Ok("result:hi") },
            new LyntaiOptions(), command: "fakecli", provisioner: provisioner);

        await engine.CompleteAsync(Ask("study"));

        Assert.Equal(1, provisioner.Calls);
    }

    [Fact]
    public async Task A_null_request_is_refused_by_the_default_member_as_by_the_host()
    {
        // the default must not silently run request-blind on a null the shipped host refuses
        ICliToolProvisioner provisioner = new RequestBlind();

        await Assert.ThrowsAsync<ArgumentNullException>(() => provisioner.ProvisionAsync((CliToolRequest)null!));
    }
}
