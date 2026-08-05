using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Lyntai.Llm;
using Lyntai.Providers.OpenAiCompatible;
using Lyntai.Providers.OpenAiCompatible.Payloads;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace Lyntai.Tests.Providers;

/// <summary>Vision on the Ollama-NATIVE flavour (<c>/api/chat</c>), which had no attachment arm at all: an
/// image on a user turn was dropped on the floor, with nothing on the wire and nothing logged, while the
/// same message through <see cref="OpenAiPayload"/> rendered <c>image_url</c> parts.
///
/// <para>The two halves are different promises. An attachment carrying BYTES is deliverable — Ollama takes
/// it as raw base64 in a sibling <c>images</c> array — so dropping it was simply wrong. An attachment
/// carrying only a remote <c>Uri</c> is NOT deliverable (that schema has no URL form), so the fix there is
/// to REPORT it, the way the CLI paths report every capability they cannot honour, rather than to keep
/// failing in silence.</para></summary>
public class OllamaAttachmentTests
{
    private static readonly byte[] Png = Encoding.UTF8.GetBytes("fake-png-bytes");
    private static string Base64 => Convert.ToBase64String(Png);

    [Fact]
    public void An_inline_image_rides_the_user_turn_as_raw_base64()
    {
        var req = new LlmRequest { Messages = [LlmMessage.UserWithImage("describe", Png, "image/png")] };

        var msg = OllamaPayload.Build(req, "llava", stream: false)["messages"]!.AsArray()[0]!;

        Assert.Equal("user", (string)msg["role"]!);
        Assert.Equal("describe", (string)msg["content"]!);      // text stays a plain string, not a parts array
        var images = msg["images"]!.AsArray();
        Assert.Single(images);
        Assert.Equal(Base64, (string)images[0]!);
    }

    [Fact]
    public void The_base64_carries_no_data_url_prefix()
    {
        // `data:image/png;base64,…` is the OpenAI image_url shape; Ollama wants the payload alone, and a
        // prefixed string decodes to garbage rather than failing loudly
        var req = new LlmRequest { Messages = [LlmMessage.UserWithImage("describe", Png, "image/png")] };

        var image = (string)OllamaPayload.Build(req, "llava", stream: false)["messages"]!
            .AsArray()[0]!["images"]!.AsArray()[0]!;

        Assert.DoesNotContain("data:", image, StringComparison.Ordinal);
        Assert.DoesNotContain("base64,", image, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_images_all_travel_in_one_array()
    {
        var second = Encoding.UTF8.GetBytes("second-image");
        var req = new LlmRequest
        {
            Messages =
            [
                new LlmMessage("user", "compare these")
                {
                    Attachments = [new LlmAttachment("image/png", Png), new LlmAttachment("image/png", second)],
                },
            ],
        };

        var images = OllamaPayload.Build(req, "llava", stream: false)["messages"]!.AsArray()[0]!["images"]!.AsArray();

        Assert.Equal(2, images.Count);
        Assert.Equal(Base64, (string)images[0]!);
        Assert.Equal(Convert.ToBase64String(second), (string)images[1]!);
    }

    [Fact]
    public void A_turn_with_no_attachments_is_untouched()
    {
        var req = new LlmRequest { Messages = [LlmMessage.User("plain text")] };

        var msg = OllamaPayload.Build(req, "llama3", stream: false)["messages"]!.AsArray()[0]!;

        Assert.Null(msg["images"]); // no empty array on the wire for a turn that carries nothing
        Assert.Equal("plain text", (string)msg["content"]!);
    }

    [Fact]
    public void An_attachment_on_a_non_user_role_is_not_sent()
    {
        // /api/chat documents images on the USER turn; mirrors OpenAiPayload's same restriction
        var req = new LlmRequest
        {
            Messages = [new LlmMessage("assistant", "sure") { Attachments = [new LlmAttachment("image/png", Png)] }],
        };

        var msg = OllamaPayload.Build(req, "llava", stream: false)["messages"]!.AsArray()[0]!;

        Assert.Null(msg["images"]);
        Assert.Equal("sure", (string)msg["content"]!);
    }

    [Fact]
    public void A_uri_only_attachment_is_reported_as_undeliverable_rather_than_dropped_in_silence()
    {
        var logs = new List<string>();
        var req = new LlmRequest { Messages = [LlmMessage.UserWithImageUrl("describe", "https://example.com/i.jpg")] };

        var msg = OllamaPayload.Build(req, "llava", stream: false, logger: new CapturingLogger(logs))["messages"]!
            .AsArray()[0]!;

        Assert.Null(msg["images"]);                       // nothing to inline, so nothing invented
        var warning = Assert.Single(logs);
        Assert.Contains("images", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/v1", warning, StringComparison.Ordinal);  // names the surface that CAN take a URL
    }

    [Fact]
    public void A_mixed_turn_sends_what_it_can_and_reports_what_it_cannot()
    {
        var logs = new List<string>();
        var req = new LlmRequest
        {
            Messages =
            [
                new LlmMessage("user", "compare these")
                {
                    Attachments =
                    [
                        new LlmAttachment("image/png", Png),
                        new LlmAttachment("image/jpeg", Uri: "https://example.com/i.jpg"),
                    ],
                },
            ],
        };

        var msg = OllamaPayload.Build(req, "llava", stream: false, logger: new CapturingLogger(logs))["messages"]!
            .AsArray()[0]!;

        var images = msg["images"]!.AsArray();
        Assert.Single(images);                      // the deliverable one went
        Assert.Equal(Base64, (string)images[0]!);
        Assert.Single(logs);                        // the other one was reported, not silently discarded
    }

    [Fact]
    public void No_logger_configured_still_builds_the_payload()
    {
        // the report is diagnostics, never a precondition — a provider with no logger must not change shape
        var req = new LlmRequest { Messages = [LlmMessage.UserWithImageUrl("describe", "https://example.com/i.jpg")] };

        var msg = OllamaPayload.Build(req, "llava", stream: false)["messages"]!.AsArray()[0]!;

        Assert.Equal("describe", (string)msg["content"]!);
        Assert.Null(msg["images"]);
    }

    [Fact]
    public async Task The_ollama_flavour_provider_puts_the_image_on_the_wire()
    {
        // pins the WIRING as well as the payload: the provider must reach the Ollama arm (and hand it the
        // logger), or the payload fix ships while every real call still sends text only
        var handler = new StubHttpHandler().Enqueue(HttpStatusCode.OK,
            """{"message":{"role":"assistant","content":"a cat"},"done":true,"prompt_eval_count":7,"eval_count":3}""");
        var config = new OpenAiCompatibleOptions { BaseUrl = "http://localhost:11434", ApiKey = null };
        var provider = new OpenAiCompatibleProvider("ollama", config,
            () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });

        var reply = await provider.CompleteAsync(new LlmRequest
        {
            Messages = [LlmMessage.UserWithImage("what is this?", Png, "image/png")],
            Model = "llava",
        });

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal(new Uri("http://localhost:11434/api/chat"), handler.Requests[0].Uri);
        Assert.Contains("\"images\"", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains(Base64, handler.Requests[0].Body, StringComparison.Ordinal);
    }

    /// <summary>Warning-and-above sink, so a test can assert a capability was REPORTED rather than dropped.
    /// The twin in <c>CodexAgentSessionTests</c> is private to that class, so it is not shareable.</summary>
    private sealed class CapturingLogger(List<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? ex,
            Func<TState, Exception?, string> fmt)
        {
            if (level >= LogLevel.Warning) sink.Add(fmt(state, ex));
        }
    }
}
