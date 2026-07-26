namespace Lyntai.Tests.Fakes;

/// <summary>A controllable clock cell the store is built over, so lease/retry timing is deterministic.</summary>
public sealed class MutableClock
{
    public DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    public DateTimeOffset Get() => Now;
    public void Advance(TimeSpan by) => Now += by;
}
