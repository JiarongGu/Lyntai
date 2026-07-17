using System.Runtime.CompilerServices;
using System.Text.Json;
using Lyntai.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.ExtensionsAi;

/// <summary>
/// Bridges any <see cref="IChatClient"/> (the whole Microsoft.Extensions.AI ecosystem — OpenAI,
/// Azure, Ollama, Anthropic API, …) into a Lyntai <see cref="ILlmProvider"/>: request/option
/// mapping, streaming, usage, and verdict classification from exceptions/finish reasons.
/// Function tools are not bridged in this cut (a declaration-only tool has no invocable AIFunction).
/// </summary>
public sealed class ExtensionsAiProvider(
    string id,
    IChatClient client,
    LyntaiOptions options,
    ILogger<ExtensionsAiProvider>? logger = null) : ILlmProvider
{
    private readonly ILogger _logger = logger ?? NullLogger<ExtensionsAiProvider>.Instance;

    public string Id => id;

    public bool IsAvailable => true; // the client exists; real availability shows up as verdicts

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.ProviderTimeout);
        try
        {
            var response = await client.GetResponseAsync(MapMessages(req), MapOptions(req), timeoutCts.Token)
                .ConfigureAwait(false);

            var text = response.Text;
            var usage = MapUsage(response.Usage);
            if (response.FinishReason == ChatFinishReason.ContentFilter)
                return new LlmReply(text, LlmVerdict.Refused, usage, $"{id}: content filter");
            if (string.IsNullOrEmpty(text))
                return new LlmReply("", LlmVerdict.Failed, usage, $"{id}: empty response");
            return new LlmReply(text, LlmVerdict.Ok, usage);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException)
        {
            return new LlmReply("", LlmVerdict.Timeout, Detail: $"{id}: no response within {options.ProviderTimeout}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Id}: chat client faulted", id);
            return new LlmReply("", LlmVerdictClassifier.FromException(ex), Detail: $"{id}: {ex.Message}");
        }
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.ProviderTimeout);

        LlmUsage? usage = null;
        var sawContent = false;
        var enumerator = client.GetStreamingResponseAsync(MapMessages(req), MapOptions(req), timeoutCts.Token)
            .GetAsyncEnumerator(timeoutCts.Token);
        await using (enumerator.ConfigureAwait(false))
        {
            while (true)
            {
                ChatResponseUpdate? update = null;
                LlmChunk? error = null;
                try
                {
                    update = await enumerator.MoveNextAsync().ConfigureAwait(false) ? enumerator.Current : null;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (OperationCanceledException)
                {
                    error = LlmChunk.Error(LlmVerdict.Timeout, $"{id}: no response within {options.ProviderTimeout}");
                }
                catch (Exception ex)
                {
                    error = LlmChunk.Error(LlmVerdictClassifier.FromException(ex), $"{id}: {ex.Message}");
                }
                if (error is not null)
                {
                    yield return error;
                    yield break;
                }
                if (update is null) break;

                foreach (var content in update.Contents)
                {
                    if (content is UsageContent u) usage = MapUsage(u.Details);
                }
                if (update.Text is { Length: > 0 } text)
                {
                    sawContent = true;
                    yield return LlmChunk.Content(text);
                }
            }
        }

        // the streaming twin of CompleteAsync's empty→Failed: a zero-content stream must surface an
        // error the router can fall over on, not end as a clean empty Final
        if (!sawContent)
            yield return LlmChunk.Error(LlmVerdict.Failed, $"{id}: empty response");
        else
            yield return LlmChunk.Final(usage);
    }

    private static IList<ChatMessage> MapMessages(LlmRequest req) =>
        [.. req.Messages.Select(m => new ChatMessage(new ChatRole(m.Role), m.Content))];

    private static ChatOptions MapOptions(LlmRequest req)
    {
        var chatOptions = new ChatOptions
        {
            ModelId = req.Model,
            MaxOutputTokens = req.MaxTokens,
            Temperature = (float?)req.Temperature,
        };
        if (!string.IsNullOrEmpty(req.JsonSchema))
        {
            try
            {
                using var doc = JsonDocument.Parse(req.JsonSchema);
                chatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(doc.RootElement.Clone(), "result");
            }
            catch (JsonException)
            {
                // unparseable schema — send the request unconstrained rather than failing it
            }
        }
        return chatOptions;
    }

    private static LlmUsage? MapUsage(UsageDetails? usage) =>
        usage is null ? null : new LlmUsage(
            usage.InputTokenCount ?? 0,
            usage.OutputTokenCount ?? 0,
            usage.CachedInputTokenCount ?? 0); // cached tokens bill at discounted rates — keep them
}
