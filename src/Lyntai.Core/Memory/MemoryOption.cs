using System.Globalization;
using System.Runtime.CompilerServices;

namespace Lyntai.Memory;

/// <summary>
/// The ONE domain guard every memory options record validates through — the same discipline
/// <see cref="MemorySignals"/> already applies to salience, applied to option DOMAINS.
///
/// <para><b>Why one function and not a copy per property.</b> The shape was written out at 31 sites across
/// five files, and three of those files had ALREADY extracted a private local helper — one per file, each
/// with its own signature. The check itself never drifted, but two things around it had: the sites disagreed
/// about what <see cref="ArgumentException.ParamName"/> should report (an inline guard passes the
/// literal <c>"value"</c>, because that is what <c>nameof(value)</c> means inside an <c>init</c> accessor;
/// the extracted helpers passed the PROPERTY name, which is the one a caller can act on), and every
/// message's domain phrase was hand-written beside a check nothing tied it to. A property whose text says
/// <c>[0, 1]</c> while its code tests <c>&lt; 0 || &gt;= 1</c> would compile, pass, and mislead.</para>
///
/// <para><b>The domain phrase is DERIVED from the range that is actually tested</b>
/// (<see cref="MemoryOptionRange.Describe"/>), so the sentence a consumer reads and the comparison that
/// rejected them cannot disagree. Only the WHY stays per-property, because that is the part that is
/// genuinely about the property rather than about its arithmetic.</para>
///
/// <para><b>Internal, deliberately.</b> The behaviour that is public is the exception; this is how it gets
/// thrown. A BYO policy with its own options record writes its own guard, exactly as it writes its own
/// options — <c>library-api-design.md</c>'s "every public type earns its keep", and a guard nobody has
/// asked for does not.</para>
/// </summary>
internal static class MemoryOption
{
    /// <summary>Accept a value inside <paramref name="range"/>, or throw naming the property, the domain it
    /// violated, and why that domain exists.</summary>
    /// <param name="value">The incoming value — pass the <c>init</c> accessor's own <c>value</c>.</param>
    /// <param name="range">The domain, which also supplies the message's phrasing.</param>
    /// <param name="owner">The declaring record's name, so the message identifies the option fully.</param>
    /// <param name="why">What a value outside the domain does to the subsystem — the part a reader cannot
    /// derive from the range. State the CONSEQUENCE, not the rule: "must be positive" is already in the
    /// derived phrase.</param>
    /// <param name="member">Supplied by the compiler; do not pass. Inside an <c>init</c> accessor this is
    /// the property's own name, which removes the last thing a copy-paste could get wrong — a guard bearing
    /// the name of the property it was copied FROM compiles and reads perfectly.</param>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside the range, which includes every
    /// non-finite value: <c>NaN</c> compares false against every bound, so a bare comparison would let it
    /// through silently (<c>.claude/knowledge/pitfalls.md</c> §Storage — a clamp is not a finiteness
    /// guard).</exception>
    public static double Require(double value, MemoryOptionRange range, string owner, string why,
        [CallerMemberName] string member = "")
    {
        if (range.Contains(value)) return value;
        throw new ArgumentOutOfRangeException(member, value,
            $"{owner}.{member} must be {range.Describe()} — {why}");
    }
}

/// <summary>A closed, half-open or unbounded interval over the FINITE doubles, carrying enough to both test
/// a value and describe itself in a message. Every factory here excludes <c>NaN</c> and both infinities;
/// that is not a property of the bounds but of <see cref="Contains"/>, which asks
/// <see cref="double.IsFinite(double)"/> first.</summary>
/// <param name="Min">Lower bound.</param>
/// <param name="MinInclusive">Whether <paramref name="Min"/> itself is accepted.</param>
/// <param name="Max">Upper bound.</param>
/// <param name="MaxInclusive">Whether <paramref name="Max"/> itself is accepted.</param>
internal readonly record struct MemoryOptionRange(
    double Min, bool MinInclusive, double Max, bool MaxInclusive)
{
    /// <summary>Any finite number — no domain beyond finiteness. The honest choice where a knob has a
    /// sensible range that no measurement has justified fixing: an out-of-range FINITE value exaggerates the
    /// knob and stays diagnosable, while a non-finite one makes every comparison downstream meaningless.</summary>
    public static MemoryOptionRange Finite { get; } =
        new(double.NegativeInfinity, false, double.PositiveInfinity, false);

    /// <summary>Finite and strictly above zero.</summary>
    public static MemoryOptionRange Positive { get; } = new(0, false, double.PositiveInfinity, false);

    /// <summary>Finite and strictly below zero.</summary>
    public static MemoryOptionRange Negative { get; } = new(double.NegativeInfinity, false, 0, false);

    /// <summary>Finite and at or above zero.</summary>
    public static MemoryOptionRange NonNegative { get; } = new(0, true, double.PositiveInfinity, false);

    /// <summary>Finite and at or above <paramref name="min"/>.</summary>
    public static MemoryOptionRange AtLeast(double min) => new(min, true, double.PositiveInfinity, false);

    /// <summary><c>[min, max]</c> — both ends accepted.</summary>
    public static MemoryOptionRange Closed(double min, double max) => new(min, true, max, true);

    /// <summary><c>[min, max)</c> — the lower end accepted, the upper end not.</summary>
    public static MemoryOptionRange FromInclusive(double min, double max) => new(min, true, max, false);

    /// <summary><c>(min, max]</c> — the upper end accepted, the lower end not.</summary>
    public static MemoryOptionRange ToInclusive(double min, double max) => new(min, false, max, true);

    /// <summary>Whether the value is finite AND within the bounds. Finiteness is asked FIRST and separately:
    /// every comparison against <c>NaN</c> is false, so <c>value &lt; Min</c> alone would accept it.</summary>
    public bool Contains(double value) =>
        double.IsFinite(value)
        && (MinInclusive ? value >= Min : value > Min)
        && (MaxInclusive ? value <= Max : value < Max);

    /// <summary>The domain as a message fragment, derived from the very bounds <see cref="Contains"/>
    /// tests — which is the point of deriving it rather than writing it beside the check.</summary>
    public string Describe()
    {
        var noLower = double.IsNegativeInfinity(Min);
        var noUpper = double.IsPositiveInfinity(Max);
        // The three zero-anchored shapes get their English names first — "a finite positive number" is what
        // every one of these messages already said, and an arm ordered after the generic bounded case would
        // be unreachable rather than merely unused.
        if (noLower && noUpper) return "a finite number";
        if (noLower && Max == 0 && !MaxInclusive) return "a finite negative number";
        if (noUpper && Min == 0) return MinInclusive ? "a finite non-negative number" : "a finite positive number";
        if (noUpper) return $"a finite number {(MinInclusive ? "at or above" : "above")} {N(Min)}";
        if (noLower) return $"a finite number {(MaxInclusive ? "at or below" : "below")} {N(Max)}";
        return $"a finite number in {(MinInclusive ? '[' : '(')}{N(Min)}, {N(Max)}{(MaxInclusive ? ']' : ')')}";
    }

    // Invariant culture: this text reaches an exception message, and a comma decimal separator would make
    // "in [0,5, 1)" out of a bound that reads "[0.5, 1)" on the machine that wrote it.
    private static string N(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);
}
