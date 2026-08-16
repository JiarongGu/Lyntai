namespace Lyntai.Memory.Interference;

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

/// <summary>Which primitive a source of age actually recomputes from — the structural counterpart of the
/// "derivable vs accumulating" distinction (see
/// <see cref="IMemoryAgePolicy.Kind"/>). A declared enum member rather than a type check or an <c>is</c> cast,
/// so a future policy states which it is instead of the engine guessing from its shape.</summary>
public enum MemoryAgeKind
{
    /// <summary>A pure, path-independent function of <see cref="MemoryAgeSample"/> alone — replaying the same
    /// writes through a store always reproduces the same <see cref="IMemoryAgePolicy.Age"/>, because nothing
    /// this policy needs is stored in its own unit. <see cref="PerWriteAgePolicy"/>, <see cref="ContentSizeAgePolicy"/>
    /// and <see cref="ElapsedAgePolicy"/> are all Derivable.</summary>
    Derivable,

    /// <summary>Genuinely path-dependent — needs the burst structure of every intervening write, which no
    /// snapshot of <see cref="MemoryAgeSample"/> can recover (see <see cref="BurstDampenedAgePolicy.Age"/>'s
    /// own remarks). An Accumulating policy's age comes from the store's own <c>Advance</c>-driven position
    /// accumulator (<see cref="GraphNode.Age"/>), unchanged by the primitives existing.
    /// <see cref="BurstDampenedAgePolicy"/> — the engine's DEFAULT — is the one shipped example.</summary>
    Accumulating,
}

/// <summary>How far each of three policy-independent primitives has moved since a node was last used — what
/// every <see cref="IMemoryAgePolicy.Age"/> projects its own view from.
/// <para>A store advances all three UNCONDITIONALLY on every write, regardless of which
/// <see cref="IMemoryAgePolicy"/> is installed: <see cref="Ordinal"/> by one, <see cref="Volume"/> by that
/// write's content length, <see cref="ElapsedDays"/> by real time. None of them is stored in a policy's own
/// unit, so swapping the installed policy never reinterprets a stored number — it only changes which of
/// these three a query is built from.</para></summary>
/// <param name="Ordinal">How many writes have happened to the engine since this entry was last used.</param>
/// <param name="Volume">How many characters have been written to the engine since this entry was last
/// used.</param>
/// <param name="ElapsedDays">How much real time has passed since this entry was last used, in days.</param>
public readonly record struct MemoryAgeSample(double Ordinal, double Volume, double ElapsedDays);

/// <summary>
/// How much "happens" in a memory when something is written to it — the scale decay is measured on.
/// <para>An engine keeps a monotone position that advances by <see cref="MemoryTick.Position"/> on every
/// write, and an entry's age is how far that position has moved since the entry was last used. What the
/// position COUNTS is genuinely open — the number of things written, how much material was written, how
/// much real time passed — so it is supplied rather than assumed. Four implementations ship, so nothing
/// has to be written to use this.</para>
/// <para><b>The property this buys:</b> a rarely-used memory decays slowly and a heavily-used one decays
/// fast, automatically, because the position advances only when that memory is written to. Wall-clock time
/// gets that backwards — it ages a quiet system at the same rate as a busy one. <see cref="ElapsedAgePolicy"/>
/// deliberately gives the property up for engines that want calendar decay.</para>
/// <para>Recall never advances the position: it reinforces what it returned, so a long read-only session
/// costs nothing and a read-only agent never forgets by reading.</para>
/// </summary>
public interface IMemoryAgePolicy
{
    /// <summary>Which primitive <see cref="Age"/> actually recomputes from — <see cref="MemoryAgeKind.Derivable"/>
    /// for a pure function of <see cref="MemoryAgeSample"/>, <see cref="MemoryAgeKind.Accumulating"/> for one
    /// that needs its own write-time accumulator instead. Declared, not inferred: the engine reads this
    /// rather than testing the policy's runtime type, so a future policy states which it is.</summary>
    MemoryAgeKind Kind { get; }

    /// <summary>What <paramref name="write"/> does to the memory, for the named <paramref name="engine"/>.
    /// <para><paramref name="engine"/> is what lets a STATEFUL policy (<see cref="ElapsedAgePolicy"/>,
    /// <see cref="BurstDampenedAgePolicy"/>) key its own bookkeeping per engine, so one registered instance
    /// shared by several engines (the ordinary DI-singleton shape) measures each engine's OWN "since last
    /// write", never another engine's.</para></summary>
    MemoryTick Advance(MemoryWrite write, string engine);

    /// <summary>Project this policy's own view of age from the primitives a store now tracks unconditionally
    /// — the DERIVED counterpart to <see cref="Advance"/>'s write-time judgment.
    /// <para>For most implementations this is a pure, path-independent function of <paramref name="sample"/>
    /// alone: replaying the same writes through a store always reproduces the same result, because none of
    /// the three primitives is stored in this policy's own unit. <see cref="BurstDampenedAgePolicy"/> is the
    /// documented exception — see its own override.</para>
    /// <para><b>RETURN A FINITE, NON-NEGATIVE NUMBER.</b> Every shipped implementation is a projection or a
    /// division by a construction-guarded constant, so none can produce otherwise — but this is an interface
    /// member on somebody else's type and there is nowhere to validate it. A non-finite age is no
    /// hypothetical for a BYO implementation: a difference of two unbounded counters, or a division by a
    /// configured scale nobody guarded, reaches it easily.</para>
    /// <para><b>What the library does if you return one anyway, so the behaviour is defined rather than
    /// merely defended:</b> the age composes and reaches
    /// <see cref="Lyntai.Memory.Forgetting.IMemoryRetrievabilityPolicy"/> unchanged — nothing coerces it, and
    /// coercing would silently substitute a number this seam is the sole owner of. What IS defended is
    /// everything downstream that would otherwise persist the poison:
    /// <see cref="Lyntai.Memory.Forgetting.DsrRetrievability.Reinforce"/> leaves both
    /// <see cref="MemoryDecayState.Stability"/> and <see cref="MemoryDecayState.Difficulty"/> untouched
    /// rather than writing a <c>NaN</c> back, and the ranking contract drops a candidate whose retrievability
    /// is non-finite instead of poisoning every other candidate's score. So the entry stops decaying
    /// sensibly — which is your bug to fix — but it cannot corrupt the store, the review log, or anyone
    /// else's recall.</para></summary>
    /// <param name="sample">How far each of the three primitives has moved since the entry was last
    /// used. A shipped store always populates these finitely; a custom
    /// <see cref="IMemoryGraphStore"/> is the other place a non-finite value can enter.</param>
    double Age(MemoryAgeSample sample);
}

/// <summary>Interference by COUNT: every write crowds by one, so an entry's age is "how many things have
/// been remembered since I was last used". The simplest thing with the rarely-used-decays-slowly property.
/// <para>Wrap it in <see cref="BurstDampenedAgePolicy"/> — as the engine does by default — or a bulk ingest
/// advances the position once per item and wipes everything stored before it.</para></summary>
public sealed class PerWriteAgePolicy : IMemoryAgePolicy
{
    /// <inheritdoc />
    public MemoryAgeKind Kind => MemoryAgeKind.Derivable;

    /// <inheritdoc />
    public MemoryTick Advance(MemoryWrite write, string engine) => MemoryTick.One;

    /// <inheritdoc />
    public double Age(MemoryAgeSample sample) => sample.Ordinal;
}

/// <summary>Interference by VOLUME: a write crowds in proportion to how much material it carried, so a long
/// document crowds harder than a one-line note.
/// <para>Wrap it in <see cref="BurstDampenedAgePolicy"/> for the same reason as
/// <see cref="PerWriteAgePolicy"/> — more so, since volume amplifies the burst problem rather than
/// causing it.</para></summary>
public sealed class ContentSizeAgePolicy : IMemoryAgePolicy
{
    private readonly double _perUnit;

    /// <param name="perUnit">Scales characters into positions — the default treats 200 characters as one
    /// unit, so stability constants stay in the same range as <see cref="PerWriteAgePolicy"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="perUnit"/> is zero, negative, or
    /// non-finite (<c>NaN</c>, <c>+Infinity</c>, <c>-Infinity</c>). A silent fallback here
    /// previously let <see cref="Advance"/> and <see cref="Age"/> DISAGREE on what a degenerate
    /// configuration means (one per write vs. the raw, unscaled character count), and once <see cref="Age"/>
    /// feeds real decay the disagreement is a ~200× age jump, not a cosmetic inconsistency — so the invalid
    /// value is refused at construction instead, the same guard this subsystem's options records
    /// (<c>MultiplicativeRankingOptions</c>) already apply to their own constants.</exception>
    public ContentSizeAgePolicy(double perUnit = 200)
    {
        if (!double.IsFinite(perUnit) || perUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(perUnit), perUnit,
                "ContentSizeAgePolicy.perUnit must be a finite, positive number.");
        _perUnit = perUnit;
    }

    /// <inheritdoc />
    public MemoryAgeKind Kind => MemoryAgeKind.Derivable;

    /// <inheritdoc />
    public MemoryTick Advance(MemoryWrite write, string engine)
    {
        ArgumentNullException.ThrowIfNull(write);
        return new MemoryTick(write.Content.Length / _perUnit, 1);
    }

    /// <inheritdoc />
    public double Age(MemoryAgeSample sample) => sample.Volume / _perUnit;
}

/// <summary>Calendar decay: the position advances by the real time elapsed since the previous write, in
/// days.
/// <para><b>This deliberately gives up the rarely-used-decays-slowly property</b> — a memory nobody touches
/// still ages — which is right for a PROJECT memory where "I have not looked at this in three months" is
/// meaningful, and wrong for a burst-mode agent. Choose it per engine, knowing which you are getting.</para>
/// <para>It needs no burst damping: a thousand writes in a second advance the position by almost nothing,
/// because almost no time passed. Elapsed time is self-limiting in a way a count is not.</para>
/// <para>The first write of a process advances by zero — there is no previous write to measure from, and
/// inventing one would age a fresh memory by however long the process had been up.</para>
/// <para><b>RESOLVED: <see cref="Advance"/> is keyed per the write's OWNING ENGINE, not per policy
/// instance.</b> An earlier review recorded that
/// <see cref="Advance"/>'s <c>_previous</c> was scoped to the POLICY INSTANCE — sharing one (the ordinary
/// DI-singleton shape, one <see cref="IMemoryAgePolicy"/> resolved for every engine) tracked the last write
/// across ALL of them, not per engine, while <see cref="Age"/> reads <see cref="MemoryAgeSample.ElapsedDays"/>,
/// which a store derives PER ENGINE by construction (§5.7) — so a shared instance's crowding and its own age
/// projection measured two different things whenever 2+ engines shared it. <see cref="IMemoryAgePolicy.Advance"/>
/// now carries the engine's own name for exactly this reason: <see cref="_previous"/> is a dictionary keyed on
/// it, so one shared instance tracks each engine's "since last write" independently and <see cref="Advance"/>'s
/// crowding agrees with <see cref="Age"/>'s projection again, even when many engines share one
/// instance.</para></summary>
/// <param name="clock">Time source; null takes the system clock.</param>
public sealed class ElapsedAgePolicy(Func<DateTimeOffset>? clock = null) : IMemoryAgePolicy
{
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock _lock = new();
    private readonly Dictionary<string, DateTimeOffset> _previous = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public MemoryAgeKind Kind => MemoryAgeKind.Derivable;

    /// <inheritdoc />
    public MemoryTick Advance(MemoryWrite write, string engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var now = _clock();
        lock (_lock)
        {
            var elapsed = _previous.TryGetValue(engine, out var last) ? (now - last).TotalDays : 0;
            _previous[engine] = now;
            return new MemoryTick(Math.Max(0, elapsed), 1);
        }
    }

    /// <inheritdoc />
    public double Age(MemoryAgeSample sample) => sample.ElapsedDays;
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
/// <param name="inner">The clock being damped; null takes <see cref="PerWriteAgePolicy"/>.</param>
/// <param name="window">A gap longer than this ends the burst. Null takes five seconds.</param>
/// <param name="clock">Time source for burst detection; null takes the system clock.</param>
/// <remarks>Burst state is keyed per engine, the same fix <see cref="ElapsedAgePolicy"/> carries (Task 3): one
/// instance shared across several engines (the ordinary DI-singleton shape) tracks each engine's OWN burst
/// independently, rather than one engine's writes resetting — or extending — another's window.</remarks>
public sealed class BurstDampenedAgePolicy(
    IMemoryAgePolicy? inner = null,
    TimeSpan? window = null,
    Func<DateTimeOffset>? clock = null) : IMemoryAgePolicy
{
    private readonly IMemoryAgePolicy _inner = inner ?? new PerWriteAgePolicy();
    private readonly TimeSpan _window = window ?? TimeSpan.FromSeconds(5);
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private readonly Lock _lock = new();
    private readonly Dictionary<string, (DateTimeOffset Last, int Burst)> _bursts = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public MemoryAgeKind Kind => MemoryAgeKind.Accumulating;

    /// <inheritdoc />
    public MemoryTick Advance(MemoryWrite write, string engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var tick = _inner.Advance(write, engine);
        var now = _clock();

        int n;
        lock (_lock)
        {
            // a gap longer than the window starts a new burst; an engine's first write here has no entry yet,
            // which defaults Last to default(DateTimeOffset) — far enough in the past that it always starts one
            var (last, burst) = _bursts.TryGetValue(engine, out var state) ? state : default;
            burst = now - last > _window ? 1 : burst + 1;
            _bursts[engine] = (now, burst);
            n = burst;
        }

        return new MemoryTick(tick.Position / n, tick.Encoding / Math.Sqrt(n));
    }

    /// <summary>Delegates to the wrapped (inner) policy's own projection — deliberately NOT an attempt to
    /// reconstruct the damping itself.
    /// <para>Damping divides <see cref="Advance"/>'s position by the burst size <c>n</c>, which depends on
    /// the TIMING of every intervening write — a genuinely path-dependent quantity no snapshot of
    /// <paramref name="sample"/> can recover, because the sample carries only two points (now and the
    /// entry's own encoding time), never the sequence between them. That is fine: it is not made worse by
    /// this seam existing, and the DAMPED write-time judgment it protects is still applied at
    /// <see cref="Advance"/> time and baked into the entry's own starting stability, unaffected by
    /// this.</para></summary>
    public double Age(MemoryAgeSample sample) => _inner.Age(sample);
}
