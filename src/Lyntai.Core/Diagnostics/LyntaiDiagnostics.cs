using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lyntai.Llm;

namespace Lyntai.Diagnostics;

/// <summary>
/// The library's telemetry surface, following the OpenTelemetry GenAI semantic conventions
/// (the same schema Microsoft.Extensions.AI's OpenTelemetryChatClient emits, so Lyntai's own-seam
/// providers and MEAI-bridged ones land interoperably in one trace backend). Subscribe with
/// <c>AddSource(ActivitySourceName)</c> / <c>AddMeter(MeterName)</c>; nothing is emitted unless a
/// listener is attached, so the overhead without observability wiring is a few null checks.
/// </summary>
public static class LyntaiDiagnostics
{
    public const string ActivitySourceName = "Lyntai.Llm";
    public const string MeterName = "Lyntai.Llm";

    internal static readonly ActivitySource Source = new(ActivitySourceName);
    internal static readonly Meter Meter = new(MeterName);

    /// <summary>gen_ai.client.operation.duration — seconds per provider attempt (streaming: whole stream).</summary>
    internal static readonly Histogram<double> OperationDuration =
        Meter.CreateHistogram<double>("gen_ai.client.operation.duration", unit: "s",
            description: "Duration of one LLM provider attempt");

    /// <summary>gen_ai.client.operation.time_to_first_chunk — seconds to the first streamed content
    /// chunk. Doubly load-bearing in Lyntai: the first chunk is the fallback point of no return.</summary>
    internal static readonly Histogram<double> TimeToFirstChunk =
        Meter.CreateHistogram<double>("gen_ai.client.operation.time_to_first_chunk", unit: "s",
            description: "Time to the first streamed content chunk (the fallback point of no return)");

    /// <summary>gen_ai.client.token.usage — token counts tagged gen_ai.token.type=input|output.</summary>
    internal static readonly Histogram<long> TokenUsage =
        Meter.CreateHistogram<long>("gen_ai.client.token.usage", unit: "{token}",
            description: "Tokens used per LLM call");

    internal static Activity? StartChat(string providerId, string? model)
    {
        var activity = Source.StartActivity($"chat {model ?? "dynamic"}", ActivityKind.Client);
        if (activity is not null)
        {
            activity.SetTag("gen_ai.operation.name", "chat");
            activity.SetTag("gen_ai.system", providerId);
            if (model is not null) activity.SetTag("gen_ai.request.model", model);
        }
        return activity;
    }

    internal static void RecordOutcome(Activity? activity, string providerId, string? model,
        LlmVerdict verdict, LlmUsage? usage, double elapsedSeconds, string? detail = null)
    {
        var errorType = verdict == LlmVerdict.Ok ? null : verdict.ToString();

        if (activity is not null)
        {
            if (errorType is not null)
            {
                activity.SetTag("error.type", errorType);
                activity.SetStatus(ActivityStatusCode.Error, detail);
            }
            if (usage is not null)
            {
                activity.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
                activity.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
                if (usage.CacheReadTokens > 0) activity.SetTag("gen_ai.usage.cache_read_tokens", usage.CacheReadTokens);
                // cost isn't a standard GenAI attribute yet, but a consumer wiring OTel to track spend
                // has no other hook on the router path — the trace layer's CostUsd is separate.
                if (usage.CostUsd is not null) activity.SetTag("gen_ai.usage.cost", usage.CostUsd);
            }
        }

        if (OperationDuration.Enabled)
        {
            var tags = new TagList
            {
                { "gen_ai.operation.name", "chat" },
                { "gen_ai.system", providerId },
                { "gen_ai.request.model", model },
            };
            if (errorType is not null) tags.Add("error.type", errorType);
            OperationDuration.Record(elapsedSeconds, tags);
        }

        if (usage is not null && TokenUsage.Enabled)
        {
            TokenUsage.Record(usage.InputTokens, new TagList
            {
                { "gen_ai.system", providerId }, { "gen_ai.request.model", model }, { "gen_ai.token.type", "input" },
            });
            TokenUsage.Record(usage.OutputTokens, new TagList
            {
                { "gen_ai.system", providerId }, { "gen_ai.request.model", model }, { "gen_ai.token.type", "output" },
            });
        }
    }

    internal static void RecordFirstChunk(string providerId, string? model, double elapsedSeconds)
    {
        if (!TimeToFirstChunk.Enabled) return;
        TimeToFirstChunk.Record(elapsedSeconds, new TagList
        {
            { "gen_ai.system", providerId },
            { "gen_ai.request.model", model },
        });
    }
}
