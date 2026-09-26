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

    [Fact]
    public void The_refusal_names_the_consumer_key_that_held_each_bad_name()
    {
        var options = new McpToolHostOptions { ToolsByConsumer = { ["study"] = ["fecth"], ["scoring"] = ["eho"] } };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new McpToolHostProvisioner([Echo, Fetch], new Connector(), options));

        Assert.Contains("[\"study\"] names 'fecth'", ex.Message);
        Assert.Contains("[\"scoring\"] names 'eho'", ex.Message);
    }

    /// <summary>A list whose EVERY name is unknown would otherwise host nothing at all — indistinguishable from
    /// an empty list, which denies on purpose.</summary>
    [Fact]
    public void A_list_whose_every_name_is_unknown_is_refused_rather_than_read_as_empty()
    {
        var options = new McpToolHostOptions { ToolsByConsumer = { ["study"] = ["fecth", "eho"] } };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new McpToolHostProvisioner([Echo, Fetch], new Connector(), options));

        Assert.Contains("'fecth', 'eho'", ex.Message);
    }

    [Fact]
    public void A_null_list_is_refused_at_construction_naming_its_consumer()
    {
        var options = new McpToolHostOptions { ToolsByConsumer = { ["study"] = null! } };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new McpToolHostProvisioner([Echo, Fetch], new Connector(), options));

        Assert.Contains("\"study\"", ex.Message);
    }

    /// <summary>The map is checked when the provisioner is built, so it must also be READ as it was then: a
    /// list the caller still holds could otherwise gain a name the check never saw.</summary>
    [Fact]
    public async Task The_map_is_read_as_it_was_when_the_provisioner_was_built()
    {
        var connector = new Connector();
        List<string> names = ["fetch"];
        var options = new McpToolHostOptions { ToolsByConsumer = { ["study"] = names } };
        var provisioner = new McpToolHostProvisioner([Echo, Fetch], connector, options);

        names.Add("fecth");                                          // a name the construction check never saw
        options.ToolsByConsumer["study"] = ["echo"];
        options.ToolsByConsumer["default"] = [];

        await using var session = await provisioner.ProvisionAsync(For("study"));
        Assert.Equal(["fetch"], await HostedNames(connector.Seen!));
        await using var other = await provisioner.ProvisionAsync(For("scoring"));
        Assert.NotEmpty(other.ExtraArgs);                            // no "default" entry existed when it was built
    }

    [Fact]
    public async Task A_default_entry_applies_to_every_consumer_not_mapped_itself()
    {
        // deny by default: the tiering TimeoutByConsumer uses — consumer entry, then "default", then every tool
        var connector = new Connector();
        var options = new McpToolHostOptions { ToolsByConsumer = { ["default"] = [] } };

        await using var session = await new McpToolHostProvisioner([Echo, Fetch], connector, options)
            .ProvisionAsync(For("study"));

        Assert.Empty(session.ExtraArgs);
        Assert.Null(connector.Seen);
    }

    [Fact]
    public async Task A_consumer_entry_outranks_the_default_entry()
    {
        var connector = new Connector();
        var options = new McpToolHostOptions { ToolsByConsumer = { ["default"] = [], ["study"] = ["fetch"] } };

        await using var session = await new McpToolHostProvisioner([Echo, Fetch], connector, options)
            .ProvisionAsync(For("study"));

        Assert.Equal(["fetch"], await HostedNames(connector.Seen!));
    }
}
