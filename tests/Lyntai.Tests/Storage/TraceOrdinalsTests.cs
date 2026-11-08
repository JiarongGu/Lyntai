using Lyntai.Cortex;
using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

public class TraceOrdinalsTests
{
    [Fact]
    public void A_mixed_trace_falls_back_per_step_and_keeps_list_order_on_a_tie()
    {
        // the fallback is per step, not per trace: an unset step takes its list position (1), which here
        // equals the recorded one, and the tie keeps the order the steps were handed in
        var stored = TraceOrdinals.Stored(
        [
            new TraceStep { Kind = "llm", Label = "recorded", Sequence = 1 },
            new TraceStep { Kind = "llm", Label = "unset" },
        ]);

        Assert.Equal([1L, 1L], stored.Select(s => s.Sequence));
        Assert.Equal(["recorded", "unset"], stored.Select(s => s.Label));
    }
}
