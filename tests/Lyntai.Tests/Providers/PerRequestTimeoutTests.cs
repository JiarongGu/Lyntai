using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Lyntai;
using Lyntai.Llm;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Providers.OpenAiCompatible;

namespace Lyntai.Tests.Providers;

/// <summary>Per-request timeout override (C1): a call can carry its own budget (LlmRequest.TimeoutSeconds)
/// or inherit a per-consumer default, resolved over the global ProviderTimeout — so a long CLI-agent run
/// isn't killed by a short global while short calls don't over-wait.</summary>
public class PerRequestTimeoutTests
{
    private static LlmRequest Req(int? timeoutSeconds = null, string consumer = "default") =>
        new() { Messages = [LlmMessage.User("hi")], Consumer = consumer, TimeoutSeconds = timeoutSeconds };

    // ---- ResolveTimeout precedence + clamp (pure logic) ----

    [Fact]
    public void Request_timeout_wins_over_consumer_and_global()
    {
        var opts = new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) };
        opts.TimeoutByConsumer["study"] = TimeSpan.FromMinutes(5);
        Assert.Equal(TimeSpan.FromSeconds(90), opts.ResolveTimeout(Req(timeoutSeconds: 90, consumer: "study")));
    }

    [Fact]
    public void Consumer_map_then_default_entry_then_global()
    {
        var opts = new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) };
        Assert.Equal(TimeSpan.FromSeconds(30), opts.ResolveTimeout(Req(consumer: "study"))); // global (no map)

        opts.TimeoutByConsumer["default"] = TimeSpan.FromSeconds(45);
        Assert.Equal(TimeSpan.FromSeconds(45), opts.ResolveTimeout(Req(consumer: "study"))); // "default" entry

        opts.TimeoutByConsumer["study"] = TimeSpan.FromMinutes(10);
        Assert.Equal(TimeSpan.FromMinutes(10), opts.ResolveTimeout(Req(consumer: "study"))); // exact consumer wins
    }

    [Fact]
    public void Per_request_value_is_clamped_to_the_ceiling_but_app_config_is_not()
    {
        var opts = new LyntaiOptions
        {
            ProviderTimeout = TimeSpan.FromMinutes(45),      // app-configured global > ceiling: trusted, NOT clamped
            MaxProviderTimeout = TimeSpan.FromMinutes(30),
        };
        opts.TimeoutByConsumer["study"] = TimeSpan.FromMinutes(40); // app-configured consumer > ceiling: NOT clamped

        Assert.Equal(TimeSpan.FromMinutes(30), opts.ResolveTimeout(Req(timeoutSeconds: 99_999)));   // caller value clamped
        Assert.Equal(TimeSpan.FromMinutes(45), opts.ResolveTimeout(Req()));                          // global untouched
        Assert.Equal(TimeSpan.FromMinutes(40), opts.ResolveTimeout(Req(consumer: "study")));         // consumer untouched
        Assert.Equal(TimeSpan.FromMinutes(45), opts.ResolveTimeout(Req(timeoutSeconds: 0)));         // 0/<=0 ignored → global
    }

    // ---- ClaudeCli threads the resolved timeout into the process runner (deterministic) ----

    private sealed class CapturingRunner : IProcessRunner
    {
        public TimeSpan? LastTimeout;
        private const string ResultJson =
            """{"type":"result","result":"ok","usage":{"input_tokens":1,"output_tokens":1}}""";

        public TimeSpan? LastMaxDuration;

        public Task<ProcessResult> RunAsync(string command, IReadOnlyList<string> args, string? stdin = null,
            TimeSpan? timeout = null, TimeSpan? maxDuration = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string>? environment = null, CancellationToken ct = default)
        {
            LastTimeout = timeout;
            LastMaxDuration = maxDuration;
            return Task.FromResult(new ProcessResult(0, ResultJson, "", TimedOut: false));
        }

        public async IAsyncEnumerable<string> StreamLinesAsync(string command, IReadOnlyList<string> args,
            string? stdin = null, TimeSpan? timeout = null, string? workingDirectory = null,
            IReadOnlyDictionary<string, string>? environment = null, [EnumeratorCancellation] CancellationToken ct = default)
        {
            LastTimeout = timeout;
            yield return ResultJson;
            await Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ClaudeCli_passes_the_resolved_timeout_to_the_runner()
    {
        var runner = new CapturingRunner();
        var opts = new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(60) };
        opts.TimeoutByConsumer["study"] = TimeSpan.FromMinutes(15);
        var provider = new ClaudeCliProvider(runner, opts, command: "claude");

        await provider.CompleteAsync(Req());
        Assert.Equal(TimeSpan.FromSeconds(60), runner.LastTimeout);            // global default

        await provider.CompleteAsync(Req(timeoutSeconds: 600));
        Assert.Equal(TimeSpan.FromSeconds(600), runner.LastTimeout);           // per-request override

        await provider.CompleteAsync(Req(consumer: "study"));
        Assert.Equal(TimeSpan.FromMinutes(15), runner.LastTimeout);            // per-consumer default
    }

    [Fact]
    public async Task ClaudeCli_passes_the_absolute_max_duration_backstop_to_the_runner()
    {
        // The buffered path gets BOTH a resolved inactivity window (timeout) AND an absolute ceiling
        // (maxDuration) so a chatty-but-endless child is still bounded. The backstop is MaxProviderTimeout,
        // but never SMALLER than the inactivity window (a consumer budget above the ceiling raises it).
        var runner = new CapturingRunner();
        var opts = new LyntaiOptions
        {
            ProviderTimeout = TimeSpan.FromSeconds(60),
            MaxProviderTimeout = TimeSpan.FromMinutes(30),
        };
        var provider = new ClaudeCliProvider(runner, opts, command: "claude");

        await provider.CompleteAsync(Req());
        Assert.Equal(TimeSpan.FromSeconds(60), runner.LastTimeout);            // inactivity = resolved timeout
        Assert.Equal(TimeSpan.FromMinutes(30), runner.LastMaxDuration);        // backstop = MaxProviderTimeout

        opts.TimeoutByConsumer["agent"] = TimeSpan.FromMinutes(45);            // consumer budget above the ceiling
        await provider.CompleteAsync(Req(consumer: "agent"));
        Assert.Equal(TimeSpan.FromMinutes(45), runner.LastTimeout);
        Assert.Equal(TimeSpan.FromMinutes(45), runner.LastMaxDuration);        // raised to the window, never below it
    }

    // ---- OpenAiCompatible honors the resolved timeout for real (delaying handler) ----

    private const string OkBody =
        """{"choices":[{"message":{"role":"assistant","content":"done"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1}}""";

    private sealed class DelayHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(delay, ct); // honors the provider's timeout-linked token
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(OkBody, Encoding.UTF8, "application/json") };
        }
    }

    private static OpenAiCompatibleProvider Http(TimeSpan delay, TimeSpan global)
    {
        var config = new OpenAiCompatibleOptions { BaseUrl = "https://api.openai.com", ApiKey = "k" };
        return new OpenAiCompatibleProvider("openai", config,
            () => new HttpClient(new DelayHandler(delay)), new LyntaiOptions { ProviderTimeout = global });
    }

    [Fact]
    public async Task A_longer_per_request_timeout_completes_a_call_that_outlasts_the_global()
    {
        // global 200ms times out the 1.5s call; a 5s per-request budget lets it through. Wide margins
        // (~1.3s below the global, ~3.5s above the override) so a late CTS timer under parallel/CI load
        // can't flip the verdict — a tight 100ms/300ms spread flaked exactly that way.
        var provider = Http(delay: TimeSpan.FromMilliseconds(1500), global: TimeSpan.FromMilliseconds(200));

        var timedOut = await provider.CompleteAsync(Req());                    // no override → global → Timeout
        Assert.Equal(LlmVerdict.Timeout, timedOut.Verdict);

        var ok = await provider.CompleteAsync(Req(timeoutSeconds: 5));         // override extends past the global
        Assert.Equal(LlmVerdict.Ok, ok.Verdict);
    }

    [Fact]
    public async Task A_shorter_per_request_timeout_cancels_at_its_own_value()
    {
        // global is a generous 30s, but this call sets 1s and the provider stalls 3s → cancels at ~1s
        var provider = Http(delay: TimeSpan.FromSeconds(3), global: TimeSpan.FromSeconds(30));

        var reply = await provider.CompleteAsync(Req(timeoutSeconds: 1));
        Assert.Equal(LlmVerdict.Timeout, reply.Verdict);
    }
}
