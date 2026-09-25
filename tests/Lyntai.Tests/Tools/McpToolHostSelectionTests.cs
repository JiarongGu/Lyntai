using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Tools.Mcp;
using Lyntai.Tools.Mcp.Hosting;
using ModelContextProtocol.Client;

namespace Lyntai.Tests.Tools;

/// <summary><see cref="McpToolHostOptions.ToolsByConsumer"/>: a consumer absent from it gets every tool, an empty
/// list gets none and no host, names get that subset — chosen by configuration, so a fallback to an HTTP
/// backend never sees a request whose meaning changed.</summary>
public class McpToolHostSelectionTests
{
    private sealed class Connector : IMcpCliConnector
    {
        public string ProviderId => "fake-cli";
        public McpEndpoint? Seen { get; private set; }

        public ValueTask<IReadOnlyList<string>> BuildArgsAsync(McpCliContext context, CancellationToken ct = default)
        {
            Seen = context.Endpoint;
            return ValueTask.FromResult<IReadOnlyList<string>>(["--tools"]);
        }
    }

    private static readonly ITool Echo = new FunctionTool("echo", (a, _) => Task.FromResult(a));
    private static readonly ITool Fetch = new FunctionTool("fetch", (a, _) => Task.FromResult(a));

    private static CliToolRequest For(string consumer) =>
        new(new TextRequest { Messages = [TextMessage.User("hi")], Consumer = consumer }, "fake-cli");

    private static async Task<IReadOnlyList<string>> HostedNames(McpEndpoint endpoint)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(endpoint.Url),
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {endpoint.AuthToken}" },
        });
        await using var client = await McpClient.CreateAsync(transport);
        return [.. (await McpToolset.FromClientAsync(client)).Select(t => t.Name).Order()];
    }

    [Fact]
    public async Task A_consumer_absent_from_the_map_gets_every_tool()
    {
        var connector = new Connector();
        var options = new McpToolHostOptions { ToolsByConsumer = { ["plain"] = [] } };

        await using var session = await new McpToolHostProvisioner([Echo, Fetch], connector, options)
            .ProvisionAsync(For("study"));

        Assert.Equal(["echo", "fetch"], await HostedNames(connector.Seen!));
    }

    [Fact]
    public async Task An_empty_list_starts_no_host_and_adds_no_args()
    {
        var connector = new Connector();
        var options = new McpToolHostOptions { ToolsByConsumer = { ["plain"] = [] } };

        await using var session = await new McpToolHostProvisioner([Echo, Fetch], connector, options)
            .ProvisionAsync(For("PLAIN"));                         // keys compare ignoring case, as TimeoutByConsumer

        Assert.Empty(session.ExtraArgs);
        Assert.Null(connector.Seen);                                // the connector is never consulted
    }

    [Fact]
    public async Task Names_host_only_that_subset()
    {
        var connector = new Connector();
        var options = new McpToolHostOptions { ToolsByConsumer = { ["study"] = ["fetch"] } };

        await using var session = await new McpToolHostProvisioner([Echo, Fetch], connector, options)
            .ProvisionAsync(For("study"));

        Assert.Equal(["fetch"], await HostedNames(connector.Seen!));
    }

    [Fact]
    public async Task The_request_blind_member_still_hosts_every_tool()
    {
        var connector = new Connector();
        var options = new McpToolHostOptions { ToolsByConsumer = { ["study"] = ["fetch"] } };

        await using var session = await new McpToolHostProvisioner([Echo, Fetch], connector, options).ProvisionAsync();

        Assert.Equal(["echo", "fetch"], await HostedNames(connector.Seen!));
    }

    [Fact]
    public void A_name_no_registered_tool_has_is_refused_at_construction()
    {
        var options = new McpToolHostOptions { ToolsByConsumer = { ["study"] = ["fetch", "fecth"] } };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new McpToolHostProvisioner([Echo, Fetch], new Connector(), options));

        Assert.Contains("fecth", ex.Message);
        Assert.Contains("echo", ex.Message);                        // and names what IS registered
    }
}
