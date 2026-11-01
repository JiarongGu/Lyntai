using Lyntai.Agents;
using Lyntai.Guards;
using Lyntai.Providers.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddHttpAgentSession` shows up right on the builder.
namespace Lyntai;

/// <summary>DI entry point for a chat-completions endpoint as an agent: Lyntai's own tool loop is the executor, so
/// the model reached over HTTP is one more provider beside the CLI and protocol doors. A consumer composes it
/// through the builder and never constructs its types by hand.</summary>
public static class HttpAgentBuilderExtensions
{
    /// <summary>Register a chat-completions endpoint as an <see cref="IAgentSession"/> KEYED by <paramref name="id"/>
    /// and as an <see cref="IAgentProvider"/> under the same id (through <see cref="AgentProviderRegistry"/>), with one
    /// door, <c>"http"</c>, of kind <see cref="AgentDoorKinds.Http"/>. Like the protocol door it adds no UNKEYED
    /// <see cref="IAgentSession"/>.
    /// <para><b>A turn runs <see cref="ToolLoop"/> over one pinned route.</b> The loop's tools are the registered
    /// <see cref="ITool"/>s, gated by the registered guards, selector and <see cref="IToolApprover"/>s; the endpoint is
    /// the registration's alone, with no fallback to another, so a retry is <see cref="RetryingAgentSession"/>'s
    /// (<c>AddAgentRetry</c>) and nothing else's. A read-only turn is offered no tool.</para>
    /// <para><b>What a turn's events and failure are:</b> the loop's events at turn granularity, one
    /// <see cref="SessionEnded"/>, and on failure an <see cref="AgentFailure"/> read from the response's own status,
    /// <c>Retry-After</c> and error envelope. The door states what it measurably does (read the descriptor): no resume,
    /// no steering, no plan, no live usage, no account, and a model it carries but cannot promise the endpoint runs.</para>
    /// <para><b>The turn's content goes to the endpoint</b>: its prompt, system prompt and every tool's result. Which
    /// repositories or files that may include is the host's to admit; the door states the fact.</para></summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="id">The provider id and the key of the session, such as <c>"openrouter-agent"</c>.</param>
    /// <param name="configure">The endpoint: <see cref="HttpModelOptions.BaseUrl"/>, the key and the default model. The
    /// door speaks the OpenAI-shaped wire at the URL it is given and never swaps in another, so an Ollama server is
    /// reached at its <c>/v1</c>; <see cref="HttpModelOptions.Produces"/> must stay text.</param>
    /// <param name="httpClient">A BYO client, as <c>AddHttpProvider</c> takes one; you then own its timeout and
    /// lifecycle. Null registers a named client with an infinite timeout, so the per-turn
    /// <see cref="LyntaiOptions.ProviderTimeout"/> owns the deadline.</param>
    /// <exception cref="ArgumentException"><paramref name="id"/> is blank, or the options are ones
    /// <c>AddHttpProvider</c> refuses, or produce something other than text.</exception>
    public static LyntaiBuilder AddHttpAgentSession(
        this LyntaiBuilder builder,
        string id,
        Action<HttpModelOptions> configure,
        Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(configure);
        var config = new HttpModelOptions();
        configure(config);
        HttpModelProvider.Validate(config);
        if (!string.Equals(config.Produces, Inference.ProviderKinds.Text, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"An HTTP agent door runs a chat model, and these options produce '{config.Produces}'.", nameof(configure));

        var descriptor = HttpAgentDoor.Describe(id);
        var resolveClient = HttpProviderBuilderExtensions.ResolveClient(builder, id, httpClient);
        builder.AddAgentProvider(descriptor, sp => new AgentProvider(descriptor,
            _ => sp.GetRequiredKeyedService<IAgentSession>(id)));
        builder.Services.AddKeyedSingleton<IAgentSession>(id, (sp, _) => new HttpAgentSession(
            id,
            config,
            resolveClient.Factory(sp),
            resolveClient.LyntaiOwned,
            sp.GetRequiredService<IToolRegistry>(),
            sp.GetRequiredService<LyntaiOptions>(),
            [.. sp.GetServices<IToolApprover>()],
            sp.GetService<IGuardRail>(),
            sp.GetService<IToolSelector>(),
            sp.GetService<TimeProvider>(),
            sp.GetService<ILogger<HttpModelProvider>>(),
            sp.GetService<ILogger<ToolLoop>>()));
        return builder;
    }
}
