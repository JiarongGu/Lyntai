# Lyntai with Microsoft.Extensions.AI and Agent Framework

Lyntai ships no adapter for `Microsoft.Extensions.AI` (MEAI) or Microsoft Agent Framework, in either direction
(`docs/DECISIONS.md` D146), and the public seams cover both. This page is the composition: two small classes,
copied below from the files the test suite runs, so what is shown is what is tested.

| You have | You want | Use |
|---|---|---|
| an `IChatClient` — a vendor SDK's native client, `OllamaSharp`, your own | it as a Lyntai backend, routed and failed over like any other | [`ChatClientBackend`](#an-ichatclient-as-a-lyntai-backend) through `AddBridgeProvider` |
| Lyntai | to hand it to code that takes an `IChatClient` — an MEAI pipeline, an Agent Framework agent | [`TextClientChatClient`](#lyntai-as-an-ichatclient) |
| an OpenAI-shaped endpoint | it as a backend | neither: `AddHttpProvider` (its `httpClient` argument takes your auth handler) |

Reach for the first only when the vendor's native API is what you want. A vendor's OpenAI-compatibility
endpoint is `AddHttpProvider`'s, and it may not carry what the native client does: Anthropic documents its
compatibility layer as not intended for production, and says it ignores `response_format` and `strict` and
has no prompt caching (<https://platform.claude.com/docs/en/api/openai-sdk>, read 2026-10-25).

**A file that opens both `Lyntai.Storage` and `Microsoft.Extensions.AI` meets two `ChatMessage` types**, which
is why both recipes alias the MEAI one.

**The dependency is yours.** The recipes need `Microsoft.Extensions.AI.Abstractions`. The `Lyntai` package
brings it through its MCP half, so a consumer of that bundle adds nothing; one on `Lyntai.Core` plus
`Lyntai.Providers.Basic` adds the reference itself. Nothing in Lyntai's core or in a dependency-free adapter
references it, and a package would be the way to put it there (`docs/DECISIONS.md` D25).

## An `IChatClient` as a Lyntai backend

<!-- recipe: tests/Lyntai.Tests/Inference/ChatClients/ChatClientBackend.cs -->
```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using Lyntai.Inference;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage; // Lyntai.Storage has one too

/// <summary>An <see cref="IChatClient"/> as a Lyntai backend: the mapping <c>AddBridgeProvider</c> asks you to
/// write, once. Pass the three members to the builder; Lyntai adds routing, fallback, cooldown, admission and the
/// provider timeout around them.
/// <para>Never put <c>UseFunctionInvocation()</c> under it: the client would run the tools itself, and
/// Lyntai's tool loop and approver would never see a call.</para></summary>
public static class ChatClientBackend
{
    /// <summary>What this mapping carries on the wire. Tools stream and complete; a JSON schema and images are
    /// opt-in, because only the wrapped client knows whether it honours them.</summary>
    public static ProviderCapabilities Capabilities(bool structuredOutput = false, bool vision = false) => new()
    {
        Accepts = vision ? [ProviderKinds.Text, ProviderKinds.Image] : [ProviderKinds.Text],
        SupportsToolCalls = true,
        SupportsStreamingToolCalls = true,
        SupportsStructuredOutput = structuredOutput,
    };

    public static async Task<TextResponse> CompleteAsync(IChatClient client, TextRequest req, CancellationToken ct)
    {
        try
        {
            var response = await client.GetResponseAsync(ToMessages(req), ToOptions(req), ct);
            var usage = ToUsage(response.Usage);
            var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Select(ToCall).ToList();

            if (response.FinishReason == ChatFinishReason.ContentFilter)
                return new TextResponse(response.Text, ProviderVerdict.Refused, usage, "content filter");
            // a tool-call turn is a successful reply with no text
            if (calls.Count > 0)
                return new TextResponse(response.Text, ProviderVerdict.Ok, usage) { ToolCalls = calls };
            if (response.Text.Length == 0)
                return new TextResponse("", ProviderVerdict.Failed, usage, "empty reply");
            return new TextResponse(response.Text, ProviderVerdict.Ok, usage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // add your SDK's typed status here (e.g. a 429 as RateLimited); the classifier reads text otherwise
            return new TextResponse("", ProviderVerdictClassifier.FromException(ex), Detail: ex.Message);
        }
    }

    public static async IAsyncEnumerable<TextChunk> StreamAsync(
        IChatClient client, TextRequest req, [EnumeratorCancellation] CancellationToken ct)
    {
        await using var updates = client.GetStreamingResponseAsync(ToMessages(req), ToOptions(req), ct)
            .GetAsyncEnumerator(ct);
        TextUsage? usage = null;
        var produced = false;
        while (true)
        {
            ChatResponseUpdate? update = null;
            TextChunk? failure = null;
            try
            {
                if (await updates.MoveNextAsync()) update = updates.Current;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                failure = TextChunk.Error(ProviderVerdictClassifier.FromException(ex), ex.Message);
            }
            if (failure is not null)
            {
                yield return failure;
                yield break;
            }
            if (update is null) break;

            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case TextContent { Text.Length: > 0 } text:
                        produced = true;
                        yield return TextChunk.Content(text.Text);
                        break;
                    case FunctionCallContent call: // a client yields each call whole, never as fragments
                        produced = true;
                        yield return TextChunk.Tool(ToCall(call));
                        break;
                    case UsageContent reported:
                        usage = ToUsage(reported.Details);
                        break;
                }
            }
            if (update.FinishReason == ChatFinishReason.ContentFilter)
            {
                yield return TextChunk.Error(ProviderVerdict.Refused, "content filter");
                yield break;
            }
        }
        yield return produced ? TextChunk.Final(usage) : TextChunk.Error(ProviderVerdict.Failed, "empty stream");
    }

    private static IEnumerable<ChatMessage> ToMessages(TextRequest req) => req.Messages.Select(m => m.Role switch
    {
        "tool" => new ChatMessage(ChatRole.Tool, [new FunctionResultContent(m.ToolCallId ?? "", m.Content)]),
        _ => new ChatMessage(new ChatRole(m.Role), ToParts(m)),
    });

    private static List<AIContent> ToParts(TextMessage m)
    {
        var parts = new List<AIContent>();
        if (m.Content.Length > 0) parts.Add(new TextContent(m.Content));
        foreach (var a in m.Attachments ?? [])
            parts.Add(a.Data is { } bytes ? new DataContent(bytes, a.MediaType) : new UriContent(a.Uri!, a.MediaType));
        foreach (var c in m.ToolCalls ?? [])
            parts.Add(new FunctionCallContent(c.Id, c.Name,
                JsonSerializer.Deserialize<Dictionary<string, object?>>(c.ArgumentsJson)));
        return parts;
    }

    private static ChatOptions ToOptions(TextRequest req) => new()
    {
        ModelId = req.Model,
        MaxOutputTokens = req.MaxTokens,
        Temperature = (float?)req.Temperature,
        ResponseFormat = req.JsonSchema is { } schema
            ? ChatResponseFormat.ForJsonSchema(Schema(schema), "response")
            : null,
        Tools = req.Tools?.Select(t => (AITool)AIFunctionFactory.CreateDeclaration(
            t.Name, t.Description ?? "", Schema(t.ParametersJsonSchema ?? """{"type":"object"}"""))).ToList(),
    };

    private static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static TextToolCall ToCall(FunctionCallContent call) =>
        new(call.CallId, call.Name, JsonSerializer.Serialize(call.Arguments ?? new Dictionary<string, object?>()));

    private static TextUsage? ToUsage(UsageDetails? u) => u is null
        ? null
        : new TextUsage(u.InputTokenCount ?? 0, u.OutputTokenCount ?? 0, u.CachedInputTokenCount ?? 0);
}
```

Register it with the client you built. The Anthropic SDK's `AsIChatClient(model)` and `OllamaSharp`'s
`OllamaApiClient` are two that return one:

<!-- usage-of: ChatClientBackend -->
<!-- compile-given: IChatClient client;
     static class ChatClientBackend
     {
         public static ProviderCapabilities Capabilities(bool structuredOutput = false, bool vision = false) => new();
         public static Task<TextResponse> CompleteAsync(IChatClient client, TextRequest req, CancellationToken ct) => null!;
         public static IAsyncEnumerable<TextChunk> StreamAsync(IChatClient client, TextRequest req, CancellationToken ct) => null!;
     } -->
```csharp
services.AddLyntai(cfg => cfg
    .AddBridgeProvider("native",
        (req, ct) => ChatClientBackend.CompleteAsync(client, req, ct),
        (req, ct) => ChatClientBackend.StreamAsync(client, req, ct),
        ChatClientBackend.Capabilities())
    .UseDefaultCandidates("native"));
```

What Lyntai does around it, each pinned by `ChatClientBackendTests`:

- **Verdicts.** A content filter is `Refused` and no other candidate is asked; an empty reply is `Failed` and the
  router advances; a thrown 429 or 401 is classified and the router advances; a context-window message is
  `ContextWindowExceeded`. Exceptions your SDK throws without an `HttpRequestException` status fall to the text
  classifier, so add the SDK's typed status where the comment says.
- **Streaming.** A failure before the first content chunk falls over; a failure after it surfaces and does not
  (`docs/guide.md` §The semantics you're getting). An empty stream is `Failed`.
- **Cancellation and timeout.** The caller's token reaches the client and a cancel propagates, never becoming a
  verdict. `LyntaiOptions.ProviderTimeout` (or the request's own) bounds a completion and each stream read; a
  client that hangs is told to stop and the router advances. That bound is `AddBridgeProvider`'s own.
- **Usage.** Input, output and cached-input tokens come back on `TextUsage`, so the budget and the ledger see them.
  A client that reports none reports none; Lyntai never prices one.
- **Tools and schema.** A `TextTool` becomes an `AIFunctionDeclaration`; the client's `FunctionCallContent` comes
  back whole as a `TextToolCall`. A `JsonSchema` becomes the response format. With `RequireNativeSupport`, a backend
  that declares neither is skipped and the call answers `Unsupported` rather than a prose guess.
- **Lifetime.** Lyntai never disposes a client it was handed. You built it, you dispose it, after the container.

## Lyntai as an `IChatClient`

<!-- recipe: tests/Lyntai.Tests/Inference/ChatClients/TextClientChatClient.cs -->
```csharp
using System.Runtime.CompilerServices;
using System.Text.Json;
using Lyntai.Inference;
using Microsoft.Extensions.AI;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage; // Lyntai.Storage has one too

/// <summary>Lyntai as an <see cref="IChatClient"/>, for code built on Microsoft.Extensions.AI or Agent
/// Framework: every call goes through the front door, so candidates, fallback, cooldown, budget and cache apply.
/// <para>A tool or a schema is sent with <c>RequireNativeSupport</c>, so a candidate that cannot carry it is
/// skipped and the call fails with <see cref="ProviderVerdict.Unsupported"/> rather than answering in prose.
/// <b>Tools stay the caller's</b>: this returns the model's call, and the invoking middleware on top of it
/// (<c>UseFunctionInvocation</c>) runs it.</para>
/// <para>It does not own the <see cref="ITextClient"/>: <see cref="Dispose"/> releases nothing.</para></summary>
public sealed class TextClientChatClient(ITextClient text, string consumer = ProviderConsumers.Default) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var reply = await text.CompleteAsync(ToRequest(messages, options), cancellationToken);
        if (reply.Verdict is not (ProviderVerdict.Ok or ProviderVerdict.Refused))
            throw new TextCallFailedException(reply.Verdict, reply.Detail);

        var contents = new List<AIContent>();
        if (reply.Text.Length > 0) contents.Add(new TextContent(reply.Text));
        foreach (var call in reply.ToolCalls ?? []) contents.Add(ToContent(call));
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))
        {
            Usage = ToUsage(reply.Usage),
            FinishReason = reply.Verdict == ProviderVerdict.Refused ? ChatFinishReason.ContentFilter
                : reply.ToolCalls is { Count: > 0 } ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop,
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var calledTool = false;
        await foreach (var chunk in text.StreamAsync(ToRequest(messages, options), cancellationToken))
        {
            switch (chunk.Kind)
            {
                case TextChunkKind.Content:
                    yield return new ChatResponseUpdate(ChatRole.Assistant, chunk.Text);
                    break;
                case TextChunkKind.ToolCall:
                    calledTool = true;
                    yield return new ChatResponseUpdate(ChatRole.Assistant, [ToContent(chunk.ToolCall!)]);
                    break;
                case TextChunkKind.Final:
                    var done = new ChatResponseUpdate(ChatRole.Assistant, [])
                    {
                        FinishReason = calledTool ? ChatFinishReason.ToolCalls : ChatFinishReason.Stop,
                    };
                    if (ToUsage(chunk.Usage) is { } usage) done.Contents.Add(new UsageContent(usage));
                    yield return done;
                    break;
                case TextChunkKind.Error when chunk.Verdict == ProviderVerdict.Refused:
                    yield return new ChatResponseUpdate(ChatRole.Assistant, []) { FinishReason = ChatFinishReason.ContentFilter };
                    yield break;
                case TextChunkKind.Error:
                    throw new TextCallFailedException(chunk.Verdict, chunk.Detail);
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata("lyntai")
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    public void Dispose()
    {
    }

    private TextRequest ToRequest(IEnumerable<ChatMessage> messages, ChatOptions? options) => new()
    {
        Messages = messages.SelectMany(ToMessages).ToList(),
        Model = options?.ModelId,
        MaxTokens = options?.MaxOutputTokens,
        Temperature = options?.Temperature,
        JsonSchema = options?.ResponseFormat is ChatResponseFormatJson { Schema: { } schema } ? schema.GetRawText() : null,
        Tools = options?.Tools?.OfType<AIFunctionDeclaration>()
            .Select(t => new TextTool(t.Name, t.Description, t.JsonSchema.GetRawText())).ToList(),
        Consumer = consumer,
        RequireNativeSupport = options?.Tools is { Count: > 0 } || options?.ResponseFormat is ChatResponseFormatJson { Schema: not null },
    };

    private static IEnumerable<TextMessage> ToMessages(ChatMessage m)
    {
        // a tool turn can carry several results; each becomes its own message, answering its own call
        foreach (var result in m.Contents.OfType<FunctionResultContent>())
            yield return TextMessage.ToolResult(result.CallId, result.Result?.ToString() ?? "");
        if (m.Role == ChatRole.Tool) yield break;

        var calls = m.Contents.OfType<FunctionCallContent>()
            .Select(c => new TextToolCall(c.CallId, c.Name, JsonSerializer.Serialize(c.Arguments ?? new Dictionary<string, object?>())))
            .ToList();
        var attachments = m.Contents.Select(c => c switch
        {
            DataContent { Data.IsEmpty: false } d => new TextAttachment(d.MediaType, d.Data.ToArray()),
            UriContent u => new TextAttachment(u.MediaType, Uri: u.Uri.ToString()),
            _ => null,
        }).OfType<TextAttachment>().ToList();
        yield return new TextMessage(m.Role.Value, m.Text)
        {
            ToolCalls = calls.Count > 0 ? calls : null,
            Attachments = attachments.Count > 0 ? attachments : null,
        };
    }

    private static FunctionCallContent ToContent(TextToolCall call) => new(call.Id, call.Name,
        JsonSerializer.Deserialize<Dictionary<string, object?>>(call.ArgumentsJson));

    private static UsageDetails? ToUsage(TextUsage? u) => u is null ? null : new UsageDetails
    {
        InputTokenCount = u.InputTokens,
        OutputTokenCount = u.OutputTokens,
        TotalTokenCount = u.InputTokens + u.OutputTokens,
        CachedInputTokenCount = u.CacheReadTokens,
    };
}

/// <summary>A Lyntai call that did not answer, carrying the verdict an <see cref="IChatClient"/> has no field for.</summary>
public sealed class TextCallFailedException(ProviderVerdict verdict, string? detail)
    : Exception($"{verdict}: {detail}")
{
    public ProviderVerdict Verdict { get; } = verdict;
}
```

Build it from the front door:

<!-- usage-of: TextClientChatClient -->
<!-- compile-given: ITextClient front;
     sealed class TextClientChatClient(ITextClient text, string consumer = "default") : IChatClient
     {
         public Task<ChatResponse> GetResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => null!;
         public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => null!;
         public object? GetService(Type serviceType, object? serviceKey = null) => null;
         public void Dispose() { }
     } -->
```csharp
IChatClient lyntai = new TextClientChatClient(front, consumer: "agent");
```

Tools are the caller's: put `ChatClientBuilder.UseFunctionInvocation()` (the `Microsoft.Extensions.AI` package, which
this page's compile gate does not reference) above it, and MEAI's loop runs them while Lyntai returns the model's
call. `The_invoking_middleware_runs_the_tool_and_the_second_turn_carries_the_call_and_its_result` runs exactly that.

An Agent Framework agent takes any `IChatClient`. The call below is Microsoft's own (the Ollama page,
<https://learn.microsoft.com/en-us/agent-framework/integrations/by-component/model-providers/ollama?pivots=programming-language-csharp>,
read 2026-10-25; the package was still `--prerelease` then), and is the one part of this page that is not compiled
here, because `Microsoft.Agents.AI` is not a dependency of this repository:

<!-- compile-skip: Microsoft.Agents.AI is not referenced by this repository; the call is quoted from Microsoft's documentation, not compiled -->
```csharp
using Microsoft.Agents.AI;

AIAgent agent = lyntai.AsAIAgent(instructions: "You answer in one sentence.", name: "Concise");
Console.WriteLine(await agent.RunAsync("What is a dead-host cooldown?"));
```

What `TextClientChatClient` does, each pinned by `TextClientChatClientTests`:

- **Verdicts.** A call that did not answer throws `TextCallFailedException` carrying the verdict, because an
  `IChatClient` has no field for one; a `Refused` reply returns with `FinishReason = ContentFilter`. Fallback
  happens inside Lyntai, so the caller never sees a candidate that failed first.
- **Tools.** An `AIFunctionDeclaration` becomes a `TextTool` and the model's call returns as a
  `FunctionCallContent`; `UseFunctionInvocation` runs it and sends the result back as a `tool` turn, which the
  next request carries with the assistant's call. A tool or a schema sets `RequireNativeSupport`, so a backend that
  cannot carry it answers `Unsupported` as an exception rather than describing the call in prose.
- **Streaming.** Content, whole tool calls and one final update with usage. An error after content has been read
  throws after it; before, Lyntai has already advanced to the next candidate.
- **Cancellation and lifetime.** The token reaches the backend and a cancel propagates. Disposing the adapter
  releases nothing: the `ITextClient` is the container's.

## What neither recipe carries

Said once, so no one learns it from a call:

- **Request fields with no counterpart.** `TextRequest` has no member for `top_p`, stop sequences or a seed, so
  a `ChatOptions` value for one is dropped on the way in; `TextRequest.Reasoning` is advisory and neither
  recipe forwards it.
- **A finish reason of length.** A reply cut at the token limit returns `Ok`; `TextResponse` carries no finish
  reason.
- **Service-managed conversation history** (a Responses-style API that keeps the thread on the server). The
  recipes send the whole transcript each call.
- **Reflection JSON.** Tool arguments pass through `JsonSerializer` over `Dictionary<string, object?>`, which is
  not trim- or AOT-clean; an application publishing AOT swaps it for a source-generated context.
- **A client the container builds.** `AddBridgeProvider` takes delegates, which cannot see the service provider,
  so a client from MEAI's `AddChatClient` has to be resolved after composition, or built before it. A provider
  class registered with `AddProvider(sp => …)` can resolve it, and then owns its own timeout.
- **Whether your client honours a schema.** `Capabilities()` leaves structured output off; set it only for a
  client you have seen send the response format on the wire.
