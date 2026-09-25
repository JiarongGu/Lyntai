namespace Lyntai.Tests.Fakes;

/// <summary>Bounds for awaits that can block — <c>using static</c> it.</summary>
public static class TestTimeouts
{
    /// <summary>How long an await on a GATED call (a permit, a blocked backend) waits before the test fails
    /// outright. Generous enough never to fire on a loaded machine, short enough that the failure is legible.
    /// <para>The regression such a test exists to catch — a permit never returned — makes the waiting caller wait
    /// FOREVER, so an unbounded await turns a red test into an indefinite hang: <c>verify</c> stops producing
    /// output and no test names the problem (<c>pitfalls.md</c>, a test that hangs on the failure it
    /// detects).</para></summary>
    public static readonly TimeSpan GateWait = TimeSpan.FromSeconds(5);
}
