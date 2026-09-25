using Lyntai.Processes;

namespace Lyntai.Tests.Processes;

/// <summary>The stderr tail a failure message quotes: the END of the log, trimmed, bounded.</summary>
public class ProcessResultTailTests
{
    [Fact]
    public void A_short_log_is_returned_whole_and_trimmed()
    {
        Assert.Equal("model not found", new ProcessResult(1, "", "  model not found\r\n").StdErrTail());
    }

    [Fact]
    public void A_long_log_keeps_its_last_characters()
    {
        var log = new string('x', 1000) + "fatal: out of memory\n";

        var tail = new ProcessResult(1, "", log).StdErrTail(max: 20);

        Assert.Equal("fatal: out of memory", tail);
    }

    [Fact]
    public void The_bound_is_applied_after_trimming()
    {
        // trailing whitespace must not eat the budget and cut the reason off
        var tail = new ProcessResult(1, "", "abcdef" + new string(' ', 50)).StdErrTail(max: 3);

        Assert.Equal("def", tail);
    }
}
