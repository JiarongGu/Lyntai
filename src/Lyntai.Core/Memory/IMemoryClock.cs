namespace Lyntai.Memory;

/// <summary>What one write does to a memory.</summary>
/// <param name="Position">How far the engine's monotone position moves — how much this write crowds
/// everything already stored. Non-negative; zero means it crowds nothing.</param>
/// <param name="Encoding">How well this write was itself encoded, as a multiplier on its starting
/// half-life. 1 is an ordinary write; below 1 is material taken in under load, which is held more
/// weakly — you do not remember most of what you skim.</param>
public readonly record struct MemoryTick(double Position, double Encoding)
{
    /// <summary>An ordinary, unhurried write: it crowds by one and is fully encoded.</summary>
    public static MemoryTick One { get; } = new(1, 1);
}

/// <summary>
/// How much "happens" in a memory when something is written to it — the scale decay is measured on.
/// <para>An engine keeps a monotone position that advances by <see cref="MemoryTick.Position"/> on every
/// write, and an entry's age is how far that position has moved since the entry was last used. What the
/// position COUNTS is genuinely open — the number of things written, how much material was written, how
/// much real time passed — so it is supplied rather than assumed. Four implementations ship, so nothing
/// has to be written to use this.</para>
/// <para><b>The property this buys:</b> a rarely-used memory decays slowly and a heavily-used one decays
/// fast, automatically, because the position advances only when that memory is written to. Wall-clock time
/// gets that backwards — it ages a quiet system at the same rate as a busy one. <see cref="ElapsedClock"/>
/// deliberately gives the property up for engines that want calendar decay.</para>
/// <para>Recall never advances the position: it reinforces what it returned, so a long read-only session
/// costs nothing and a read-only agent never forgets by reading.</para>
/// </summary>
public interface IMemoryClock
{
    /// <summary>What <paramref name="write"/> does to the memory.</summary>
    MemoryTick Advance(MemoryWrite write);
}

/// <summary>Interference by COUNT: every write crowds by one, so an entry's age is "how many things have
/// been remembered since I was last used". The simplest thing with the rarely-used-decays-slowly property.
/// <para>Wrap it in <see cref="BurstDampenedClock"/> — as the engine does by default — or a bulk ingest
/// advances the position once per item and wipes everything stored before it.</para></summary>
public sealed class PerWriteClock : IMemoryClock
{
    /// <inheritdoc />
    public MemoryTick Advance(MemoryWrite write) => MemoryTick.One;
}

/// <summary>Interference by VOLUME: a write crowds in proportion to how much material it carried, so a long
/// document crowds harder than a one-line note.
/// <para>Wrap it in <see cref="BurstDampenedClock"/> for the same reason as
/// <see cref="PerWriteClock"/> — more so, since volume amplifies the burst problem rather than
/// causing it.</para></summary>
/// <param name="perUnit">Scales characters into positions — the default treats 200 characters as one unit,
/// so stability constants stay in the same range as <see cref="PerWriteClock"/>.</param>
public sealed class ContentSizeClock(double perUnit = 200) : IMemoryClock
{
    /// <inheritdoc />
    public MemoryTick Advance(MemoryWrite write)
    {
        ArgumentNullException.ThrowIfNull(write);
        return new MemoryTick(perUnit <= 0 ? 1 : write.Content.Length / perUnit, 1);
    }
}

/// <summary>Calendar decay: the position advances by the real time elapsed since the previous write, in
/// days.
/// <para><b>This deliberately gives up the rarely-used-decays-slowly property</b> — a memory nobody touches
/// still ages — which is right for a PROJECT memory where "I have not looked at this in three months" is
/// meaningful, and wrong for a burst-mode agent. Choose it per engine, knowing which you are getting.</para>
/// <para>It needs no burst damping: a thousand writes in a second advance the position by almost nothing,
/// because almost no time passed. Elapsed time is self-limiting in a way a count is not.</para>
/// <para>The first write of a process advances by zero — there is no previous write to measure from, and
/// inventing one would age a fresh memory by however long the process had been up.</para></summary>
/// <param name="clock">Time source; null takes the system clock.</param>
public sealed class ElapsedClock(Func<DateTimeOffset>? clock = null) : IMemoryClock
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock _lock = new();
    private DateTimeOffset? _previous;

    /// <inheritdoc />
    public MemoryTick Advance(MemoryWrite write)
    {
        var now = _clock();
        lock (_lock)
        {
            var elapsed = _previous is DateTimeOffset last ? (now - last).TotalDays : 0;
            _previous = now;
            return new MemoryTick(Math.Max(0, elapsed), 1);
        }
    }
}

/// <summary>
/// Handles a large amount of material arriving in a small window, the way a person does: a burst crowds
/// far less than its volume suggests, and is itself held weakly.
/// <para><b>Without this the default clock has a catastrophic-forgetting mode.</b> Advancing once per
/// write is linear, so ingesting a 500-item document ages every pre-existing entry by 500 — at any sane
/// half-life, everything known before is gone. Reading a long document must not erase your memory of the
/// project.</para>
/// <para>So interference SATURATES. The <i>n</i>-th write inside a window crowds by <c>1/n</c> of what the
/// inner clock said, which makes a burst's total advance grow logarithmically: 200 rapid writes advance
/// the position by about 6 rather than 200. Volume still matters — it just stops being ruinous.</para>
/// <para>And the burst is weakly ENCODED: its items start at <c>1/√n</c> of the usual half-life, so
/// skimmed material fades faster than something written in a quiet moment. Together these are why a person
/// can read a book without forgetting their own name, and still not recall most of the book.</para>
/// <para>Real time is used only to DETECT a burst — writes closer together than
/// <paramref name="window"/> belong to the same one. The position it produces is still dimensionless, and
/// still advances only when this memory is written to.</para>
/// </summary>
/// <param name="inner">The clock being damped; null takes <see cref="PerWriteClock"/>.</param>
/// <param name="window">A gap longer than this ends the burst. Null takes five seconds.</param>
/// <param name="clock">Time source for burst detection; null takes the system clock.</param>
public sealed class BurstDampenedClock(
    IMemoryClock? inner = null,
    TimeSpan? window = null,
    Func<DateTimeOffset>? clock = null) : IMemoryClock
{
    private readonly IMemoryClock _inner = inner ?? new PerWriteClock();
    private readonly TimeSpan _window = window ?? TimeSpan.FromSeconds(5);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock _lock = new();
    private DateTimeOffset _last;
    private int _burst;

    /// <inheritdoc />
    public MemoryTick Advance(MemoryWrite write)
    {
        var tick = _inner.Advance(write);
        var now = _clock();

        int n;
        lock (_lock)
        {
            // a gap longer than the window starts a new burst; the default _last of default(DateTimeOffset)
            // is far enough in the past that the first write always starts one
            _burst = now - _last > _window ? 1 : _burst + 1;
            _last = now;
            n = _burst;
        }

        return new MemoryTick(tick.Position / n, tick.Encoding / Math.Sqrt(n));
    }
}
