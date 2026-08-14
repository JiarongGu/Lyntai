using System.Collections.Frozen;

namespace Lyntai.Memory;

/// <summary>
/// An open, persisted set of named retention signals — the reason a future retention dimension is a new
/// policy plus a registration rather than another breaking widening of <see cref="MemoryDecayState"/>.
/// <para><b>A default instance is valid and empty.</b> Every construction of
/// <see cref="MemoryDecayState"/> that predates signals produces <c>default</c>, so every member here has
/// to tolerate a null backing store rather than throwing — a throw would surface to a consumer as "decay
/// stopped working", with nothing pointing at the cause.</para>
/// <para>Values are <see cref="double"/> and nothing else. A signal exists to be read by an
/// <see cref="Lyntai.Memory.Modulation.IMemoryRetentionPolicy"/>, which may only scale a half-life, so a richer type would buy
/// expressiveness the model has nowhere to spend.</para>
/// </summary>
public readonly record struct MemorySignals
{
    private readonly FrozenDictionary<string, double>? _values;

    private MemorySignals(FrozenDictionary<string, double> values) => _values = values;

    /// <summary>Content equality, because <c>FrozenDictionary</c> does not provide it and a
    /// <c>record struct</c> advertises value semantics. Without this, two bags built from identical inputs
    /// compare unequal — and that propagates into <see cref="MemoryDecayState"/>'s own equality, where it
    /// would surface as a test that mysteriously fails to match.</summary>
    /// <param name="other">The bag to compare with.</param>
    public bool Equals(MemorySignals other)
    {
        if (Count != other.Count) return false;
        foreach (var (name, value) in Values)
            if (!other.Values.TryGetValue(name, out var mine) || !mine.Equals(value))
                return false;
        return true;
    }

    /// <inheritdoc />
    /// <remarks>Order-independent by construction: entry hashes are combined with XOR, so two bags built in
    /// different orders hash alike, as content equality requires.</remarks>
    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var (name, value) in Values)
            hash ^= HashCode.Combine(name, value);
        return hash;
    }

    /// <summary>The signal names this library itself writes. A consumer's own signals need no entry here —
    /// the bag is open — but a name collision would silently change retention, so prefix yours.</summary>
    public static class WellKnown
    {
        /// <summary>How strongly this entry was encoded, written by an <see cref="Lyntai.Memory.Salience.IMemorySaliencePolicy"/>.
        /// <b>Means "this memory does not fade away" — decay resistance AND store admission priority — NOT
        /// "first priority"</b> (2026-08-09 — <c>docs/DECISIONS.md</c> D45, corrected same day by D45): it
        /// lengthens a half-life and orders admission in the store when a candidate set overflows its
        /// budget, both on by default. It can ALSO lift rank in
        /// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> — bounded
        /// logarithmically (<see cref="Lyntai.Memory.Ranking.MultiplicativeRankingOptions.SalienceRankWeight"/>) so it lifts without
        /// dominating — but that is a stronger, separate claim (a salient entry outranking a better textual
        /// match) the owner declined to make the default; a consumer opts in explicitly.</summary>
        public const string Salience = "salience";

        /// <summary>How hard this material is to retain, on FSRS's 1–10 scale where 1 is EASIEST and 10 is
        /// hardest. <b>The neutral value is 5 (the mid-point), NOT 1 — corrected 2026-08-11</b>: absent means
        /// AVERAGE difficulty, not "easiest possible" — see
        /// <see cref="Lyntai.Memory.Forgetting.DsrOptions.NeutralDifficulty"/>'s own remarks for why starting
        /// every unjudged entry at the floor made the axis structurally unable to move, and why 5 is a
        /// stated choice rather than a derivation. (Unlike this signal, <see cref="Salience"/>'s own neutral
        /// genuinely IS its scale's floor — the two conventions differ because a lower salience is never
        /// worse than an unjudged one, while "no information about difficulty" is not the same claim as
        /// "known to be trivial".)
        /// <para><b>Read by the STORE, not by <see cref="Lyntai.Memory.Forgetting.DsrRetrievability"/>
        /// (2026-08-10, fsrs-properly plan Task 2 — before this, <c>Reinforce</c> read this signal directly on
        /// every call, and never wrote anything back).</b> Difficulty is now LIVE state
        /// (<see cref="MemoryDecayState.Difficulty"/>), maintained by the retrievability policy itself across
        /// every reinforcement — exactly the same promotion <c>salience</c> already has, and for the same
        /// reason: a store must materialize this signal into its own column so it can seed and refresh a value
        /// the policy owns going forward, the moment a write actually supplies one.</para>
        /// <para><b>Precedence, stated once so a second reader is never free to invent a second rule</b> (see
        /// <see cref="MemoryDecayState.Difficulty"/>'s own remarks for the full reasoning): a NON-EMPTY
        /// incoming bag on ANY write — a fresh node, or a re-remember — overwrites the live value, so an
        /// application's explicit judgement always wins; an EMPTY incoming bag (a salience policy declining) never
        /// touches it, so the policy's own accumulated tracking survives an unrelated re-remember untouched.
        /// </para>
        /// <para><b>A value outside <c>[1, 10]</c> is CLAMPED into range, never rejected</b> — a writer's own
        /// EXPLICIT value is coerced, not validated: <c>0</c> reads as the floor <c>1</c> (still an explicit
        /// judgement of "trivial", clamped into domain), <c>50</c> reads as <c>10</c> (hardest). A
        /// non-finite value is a DIFFERENT case — it means no real judgement was supplied at all, so it reads
        /// as the neutral <c>5</c>, not the floor (corrected 2026-08-11 alongside
        /// <see cref="Lyntai.Memory.Forgetting.DsrOptions.NeutralDifficulty"/> — the two must agree on what
        /// "no information" means). That rule is <see cref="MemorySignals.Difficulty"/>, and every reader
        /// calls it rather than restating it.</para>
        /// <para><b>Nothing judges this yet.</b> An application that can judge difficulty writes it; the
        /// library ships no policy for it, because judging difficulty is a separate problem from judging
        /// salience and deserves its own design rather than an afterthought.</para>
        /// <para><b>And "writes it" means through <see cref="Lyntai.Memory.Salience.IMemorySaliencePolicy"/>,
        /// which is the ONLY route there is</b> for the INITIAL judgement — <see cref="MemoryWrite"/> carries
        /// no signals, so an application that wants to seed or correct difficulty registers a salience policy and
        /// returns the difficulty signal from it (alongside salience, or instead of it). From then on, the
        /// retrievability policy's own per-review updates are the other route, per the precedence
        /// above.</para></summary>
        public const string Difficulty = "difficulty";
    }

    /// <summary>No signals.</summary>
    public static MemorySignals Empty { get; } =
        new(FrozenDictionary<string, double>.Empty);

    /// <summary>How many signals are set.</summary>
    public int Count => _values?.Count ?? 0;

    /// <summary>Every signal, for persistence. Empty when none are set.</summary>
    public IReadOnlyDictionary<string, double> Values =>
        _values ?? FrozenDictionary<string, double>.Empty;

    /// <summary>This signal's value, or <paramref name="fallback"/> when it is not set.</summary>
    /// <param name="name">The signal.</param>
    /// <param name="fallback">Returned when the signal is absent — a retention policy's neutral value, usually
    /// 1 for a multiplier and 0 for an addend.</param>
    public double Get(string name, double fallback = 0) =>
        _values is not null && _values.TryGetValue(name, out var value) ? value : fallback;

    /// <summary>A copy carrying <paramref name="name"/> set to <paramref name="value"/>, replacing any
    /// existing value for that name. The receiver is unchanged.</summary>
    /// <param name="name">The signal.</param>
    /// <param name="value">Its value.</param>
    public MemorySignals With(string name, double value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var builder = new Dictionary<string, double>(Values, StringComparer.Ordinal) { [name] = value };
        return new MemorySignals(builder.ToFrozenDictionary(StringComparer.Ordinal));
    }

    /// <summary>This bag's salience, coerced the ONE way every reader must coerce it — the same
    /// "compute the bound in exactly one place" shape as <c>ModulatedRetrievability.Declared</c>, but
    /// spanning packages, because the readers are in three of them.
    /// <para><b>Below 1 becomes 1.</b> 1 is the neutral value, and nothing in this model means "less
    /// findable than an entry nobody ever judged" — so a half-judged entry must order LEVEL with an
    /// unjudged one, never beneath it.</para>
    /// <para><b>Non-finite becomes 1.</b> A <see cref="double.NaN"/> does not merely mis-sort: it poisons
    /// every arithmetic consumer downstream. <c>Math.Max(1, NaN)</c> is <c>NaN</c> by IEEE 754:2019, so a
    /// rank multiplied by it is <c>NaN</c>, every comparison against it is false, and a recall whose
    /// candidates all carry one returns nothing at all — a silent, total recall blackout. On SQLite it is
    /// worse still: <c>Microsoft.Data.Sqlite</c> refuses to bind it and the whole write fails.</para>
    /// <para>Both values are reachable through the public <see cref="Lyntai.Memory.Salience.IMemorySaliencePolicy"/> seam, so neither
    /// is hypothetical. <b>Every read site calls this</b> — the two SQL stores' promoted <c>salience</c>
    /// column, the in-process store's admission ordering, and <c>GraphMemoryEngine</c>'s rank boost. They
    /// once normalized the same value three different ways, which made identical data admit differently on
    /// different backends.</para></summary>
    /// <param name="signals">The bag; an empty one reports the neutral 1.</param>
    public static double Salience(in MemorySignals signals)
    {
        var raw = signals.Get(WellKnown.Salience, fallback: 1);
        return double.IsFinite(raw) ? Math.Max(1, raw) : 1;
    }

    /// <summary>This bag's difficulty, coerced the ONE way every reader must coerce it — the same
    /// "compute the bound in exactly one place" shape as <see cref="Salience"/>, and here for the same
    /// reason: <see cref="WellKnown.Difficulty"/> states the rule as a property of the SIGNAL, so a reader
    /// holding a private copy of it is free to drift from what the signal's own doc promises.
    /// <para><b>Coerced, never rejected.</b> Outside <c>[1, 10]</c> is CLAMPED — <c>0</c> reads as the floor
    /// <c>1</c> (an explicit judgement, clamped into domain), <c>50</c> reads as the hardest <c>10</c> — and
    /// an ABSENT signal or a non-finite value reads as the neutral <c>5</c> instead (corrected 2026-08-11;
    /// was the floor <c>1</c> — see <see cref="Lyntai.Memory.Forgetting.DsrOptions.NeutralDifficulty"/>'s own
    /// remarks for why "no information" and "known to be trivial" must not share a value). <b>The
    /// finiteness check has to come FIRST</b>, because <see cref="Math.Clamp(double,double,double)"/>
    /// PROPAGATES <see cref="double.NaN"/> per IEEE 754: a clamp is not a finiteness guard, and the
    /// difference is not cosmetic here. Difficulty scales a stability increase that
    /// <c>GraphMemoryEngine</c> writes straight back to the store, so a <c>NaN</c> would be persisted
    /// PERMANENTLY — and a <c>NaN</c> stability compares false against every threshold, so the entry then
    /// neither ranks, prunes, nor reports as broken.</para>
    /// <para>Read today by each graph store's <c>UpsertAsync</c>, which materializes it into the promoted
    /// <c>difficulty</c> column exactly the way <see cref="Salience"/> already materializes into
    /// <c>salience</c> — <see cref="Lyntai.Memory.Forgetting.DsrRetrievability.Reinforce"/> itself no longer
    /// calls this: it reads and writes the LIVE <see cref="MemoryDecayState.Difficulty"/> field instead
    /// (2026-08-10, fsrs-properly plan Task 2). This exists so the store's promotion cannot invent a second
    /// coercion rule, not because two already disagree.</para></summary>
    /// <param name="signals">The bag; an empty one reports the neutral 5.</param>
    public static double Difficulty(in MemorySignals signals)
    {
        var raw = signals.Get(WellKnown.Difficulty, fallback: 5);
        return double.IsFinite(raw) ? Math.Clamp(raw, 1, 10) : 5;
    }

    /// <summary>Build a bag from named values, ignoring nothing and replacing duplicates last-wins.</summary>
    /// <param name="values">The signals.</param>
    public static MemorySignals From(IEnumerable<KeyValuePair<string, double>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var builder = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var pair in values) builder[pair.Key] = pair.Value;
        return builder.Count == 0 ? Empty : new MemorySignals(builder.ToFrozenDictionary(StringComparer.Ordinal));
    }
}
