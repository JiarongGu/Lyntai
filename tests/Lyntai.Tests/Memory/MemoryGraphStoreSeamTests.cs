using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Which <see cref="IMemoryGraphStore"/> members a BYO store must write. A default body is a silent
/// answer, so a member whose silence switches a registered feature off must not have one.</summary>
public class MemoryGraphStoreSeamTests
{
    [Fact]
    public void KnownSubjectsAsync_takes_no_default_body_because_the_subject_channel_reads_only_it()
    {
        var member = typeof(IMemoryGraphStore).GetMethod(nameof(IMemoryGraphStore.KnownSubjectsAsync))!;

        Assert.True(member.IsAbstract);
    }
}
