using System.Text.Json;
using Lyntai.Text;

namespace Lyntai.Llm;

/// <summary>
/// Structured output over the front door (design §6): schema-constrained call, tolerant JSON
/// extraction from prose/code fences, one retry on parse failure, else a Failed verdict.
/// </summary>
public static class LlmStructuredExtensions
{
    /// <summary>Complete and return a reply whose <c>Text</c> is a single parseable JSON object.
    /// Providers that support it get the schema natively (<see cref="LlmRequest.JsonSchema"/>);
    /// either way the reply text is extracted and validated here — an Ok verdict guarantees
    /// <c>JsonDocument.Parse(reply.Text)</c> succeeds.</summary>
    public static async Task<LlmReply> CompleteJsonAsync(this ILlmClient client, LlmRequest req, CancellationToken ct = default)
    {
        var current = req;
        for (var attempt = 0; ; attempt++)
        {
            var reply = await client.CompleteAsync(current, ct).ConfigureAwait(false);
            if (reply.Verdict != LlmVerdict.Ok) return reply;

            var json = JsonExtract.ExtractObject(reply.Text);
            if (json is not null && IsParseable(json))
                return reply with { Text = json };

            if (attempt > 0)
                return new LlmReply("", LlmVerdict.Failed, reply.Usage,
                    "no parseable JSON object in the reply after one retry");

            // the retry must DIFFER from the first shot, or a deterministic (temperature-0) provider
            // just repeats the same prose: feed back its own reply + a corrective instruction.
            current = req with
            {
                Messages =
                [
                    .. req.Messages,
                    LlmMessage.Assistant(reply.Text),
                    LlmMessage.User("That was not valid JSON. Reply with ONLY a single JSON object — no prose, no code fences."),
                ],
            };
        }
    }

    private static bool IsParseable(string json)
    {
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
