using Lyntai.Agents;
using Lyntai.Llm;

namespace Lyntai.Tests.Llm;

/// <summary>Every tag the LIBRARY itself emits must have a constant in <see cref="LlmConsumers"/>.
///
/// <para><b>Why this is a test and not a convention.</b> The four per-consumer dictionaries
/// (<c>TimeoutByConsumer</c>, <c>Budget.PerConsumer</c>, <c>RateLimit.PerConsumer</c>,
/// <c>DefaultModelByConsumer</c>) are how a deployment DECLARES that one workload is latency-critical and
/// another is background. They are keyed on strings, so a tag the library emits without declaring does not
/// fail — it silently opens a bucket no cap covers and no report names, which is what
/// <see cref="LlmConsumers"/>'s own docs warn about. A string literal drifting from its constant is
/// invisible to every gate here.</para></summary>
public class LlmConsumerTagTests
{
    [Fact]
    public void A_chat_turns_default_tag_is_the_declared_constant_not_a_loose_literal()
    {
        // ChatOrchestrator puts this straight onto the LlmRequest it builds, so it reaches the usage
        // tracker and the budget layer. Before the constant existed, `chat` was a fifth library-emitted
        // tag with nothing to key a cap on.
        Assert.Equal(LlmConsumers.Chat, new ChatTurn { Message = "hi" }.Consumer);
    }

    [Fact]
    public void The_declared_tags_are_distinct_so_two_workloads_cannot_share_one_bucket()
    {
        string[] tags = [
            LlmConsumers.Default, LlmConsumers.Scoring, LlmConsumers.Memory,
            LlmConsumers.Agent, LlmConsumers.Chat,
        ];

        Assert.Equal(tags.Length, tags.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_declared_tag_is_lowercase_because_the_lookups_are_ordinal()
    {
        // LyntaiOptions resolves these by ordinal dictionary lookup and the env-var scan upper-cases the
        // suffix, so a capitalised constant would key a bucket no configuration could name.
        foreach (var tag in new[] {
            LlmConsumers.Default, LlmConsumers.Scoring, LlmConsumers.Memory,
            LlmConsumers.Agent, LlmConsumers.Chat })
            Assert.Equal(tag.ToLowerInvariant(), tag);
    }
}
