using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Inference;

/// <summary><see cref="TextRequest.RequireNativeSupport"/> (D205): a request carrying a JSON schema or tools,
/// and asking for native support, skips a candidate whose <see cref="ProviderCapabilities"/> say it would degrade
/// either — with the reason named wherever a skip is reported. Without the flag nothing changes.</summary>
public class TextRouterNativeSupportTests
{
    private const string Schema = """{"type":"object"}""";

    private static readonly TextTool Tool = new("lookup", "look something up", """{"type":"object"}""");

    private static TextRouter Router(params IModelProvider[] providers) =>
        new(providers, new DeadHostTracker(), new LyntaiOptions());

    private static TextRequest Ask(bool native, string? schema = null, IReadOnlyList<TextTool>? tools = null) => new()
    {
        Messages = [TextMessage.User("hi")],
        JsonSchema = schema,
        Tools = tools,
        RequireNativeSupport = native,
    };

    private static FakeTextProvider Native(string id) => new(id)
    {
        Capabilities = new FakeTextProvider(id).Capabilities with
        {
            SupportsStructuredOutput = true,
            SupportsToolCalls = true,
            SupportsStreamingToolCalls = true,
        },
    };

    [Fact]
    public async Task By_default_a_schema_request_still_reaches_a_candidate_that_would_degrade_it()
    {
        var degrading = new FakeTextProvider("cli");
        var native = Native("http");

        var reply = await Router(degrading, native).CompleteAsync([new("cli"), new("http")], Ask(false, Schema));

        Assert.Equal("cli default reply", reply.Text);
        Assert.Empty(native.Calls);
    }

    [Fact]
    public async Task Requiring_native_support_skips_a_candidate_that_cannot_honour_the_schema()
    {
        var degrading = new FakeTextProvider("cli");
        var native = Native("http");

        var reply = await Router(degrading, native).CompleteAsync([new("cli"), new("http")], Ask(true, Schema));

        Assert.Equal("http default reply", reply.Text);
        Assert.Empty(degrading.Calls);
    }

    [Fact]
    public async Task The_flag_is_inert_on_a_request_that_carries_neither_a_schema_nor_tools()
    {
        var plain = new FakeTextProvider("cli");

        var reply = await Router(plain).CompleteAsync([new("cli")], Ask(true));

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Single(plain.Calls);
    }

    [Fact]
    public async Task Requiring_native_support_skips_a_candidate_that_declares_no_native_tool_calls()
    {
        var promptOnly = new FakeTextProvider("local");
        var native = Native("http");

        var reply = await Router(promptOnly, native).CompleteAsync([new("local"), new("http")], Ask(true, tools: [Tool]));

        Assert.Equal("http default reply", reply.Text);
        Assert.Empty(promptOnly.Calls);
    }

    [Fact]
    public async Task On_a_stream_the_tools_check_reads_the_STREAMING_capability()
    {
        // buffered tool calls do not make a stream carry them (D71)
        var bufferedOnly = new FakeTextProvider("buffered") { SupportsToolCalls = true };
        var native = Native("http");

        var chunks = await Router(bufferedOnly, native)
            .StreamAsync([new("buffered"), new("http")], Ask(true, tools: [Tool])).ToListAsync();

        Assert.Equal(0, bufferedOnly.StreamCalls);
        Assert.Equal(["http stream"], chunks.Where(c => c.Kind == TextChunkKind.Content).Select(c => c.Text));
    }

    [Fact]
    public async Task When_every_candidate_would_degrade_the_call_is_Unsupported_and_names_each_reason()
    {
        var cli = new FakeTextProvider("cli");
        var local = new FakeTextProvider("local") { SupportsStructuredOutput = true };

        var reply = await Router(cli, local)
            .CompleteAsync([new("cli"), new("local")], Ask(true, Schema, [Tool]));

        Assert.Equal(ProviderVerdict.Unsupported, reply.Verdict);
        Assert.Contains("cli: cannot honour a JSON schema natively", reply.Detail, StringComparison.Ordinal);
        Assert.Contains("local: declares no native tool calls", reply.Detail, StringComparison.Ordinal);
        Assert.Empty(cli.Calls);
        Assert.Empty(local.Calls);
    }

    [Fact]
    public async Task The_streaming_twin_closes_with_the_same_Unsupported_chunk()
    {
        var cli = new FakeTextProvider("cli");

        var chunks = await Router(cli).StreamAsync([new("cli")], Ask(true, Schema)).ToListAsync();

        var error = Assert.Single(chunks);
        Assert.Equal(ProviderVerdict.Unsupported, error.Verdict);
        Assert.Contains("cli: cannot honour a JSON schema natively", error.Detail, StringComparison.Ordinal);
        Assert.Equal(0, cli.StreamCalls);
    }

    [Fact]
    public async Task A_real_failure_on_a_capable_candidate_still_outranks_the_skip()
    {
        var cli = new FakeTextProvider("cli");
        var native = Native("http");
        native.Replies.Enqueue(new TextResponse("", ProviderVerdict.Failed, Detail: "http: 500"));

        var reply = await Router(cli, native).CompleteAsync([new("cli"), new("http")], Ask(true, Schema));

        Assert.Equal(ProviderVerdict.Failed, reply.Verdict);
        Assert.Equal("http: 500", reply.Detail);
    }

    [Fact]
    public async Task The_capability_probe_names_the_candidate_a_native_only_call_would_commit_to()
    {
        var cli = new FakeTextProvider("cli");
        var native = Native("http");

        var caps = await Router(cli, native).GetCapabilitiesAsync([new("cli"), new("http")], Ask(true, Schema));

        Assert.Same(native.Capabilities, caps);
    }
}
