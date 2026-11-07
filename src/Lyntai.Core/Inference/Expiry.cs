namespace Lyntai.Inference;

/// <summary>An expiry instant that saturates. <c>TimeSpan.MaxValue</c> is the natural way to say "never", and
/// <c>now + window</c> throws for it.</summary>
internal static class Expiry
{
    /// <summary><paramref name="now"/> plus <paramref name="window"/>, or <see cref="DateTimeOffset.MaxValue"/>
    /// when the sum is past the end of the representable range.</summary>
    internal static DateTimeOffset After(DateTimeOffset now, TimeSpan window) =>
        window > DateTimeOffset.MaxValue - now ? DateTimeOffset.MaxValue : now + window;
}
