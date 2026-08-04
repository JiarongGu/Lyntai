using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Lifecycle;

/// <summary>Identifies one CONFIGURATION of one backend — the pool's equivalent of a connection string, and
/// the unit reuse, dead-host cooldown and admission control are all keyed on.
///
/// <para>Build one with <see cref="For"/>. The fingerprint is a digest of every named contribution, so two
/// logically identical configurations compare equal while a change anywhere — including a rotated
/// credential — produces a different key.</para></summary>
/// <param name="Slot">The candidate id this configuration is for, carried verbatim so a pool can retire
/// every configuration of one backend.</param>
/// <param name="Fingerprint">A digest of the named contributions. Opaque; compare, never parse.</param>
public readonly record struct ProviderKey(string Slot, string Fingerprint)
{
    /// <summary>Start building a key for a backend id.</summary>
    /// <param name="slot">The candidate id (<c>"openai-images"</c>). Must not be blank.</param>
    /// <exception cref="ArgumentException">The slot is null, empty or whitespace.</exception>
    public static ProviderKeyBuilder For(string slot) => new(slot);

    /// <summary>A short, log-safe rendering. Contains no secret material. Never throws — including for a
    /// <c>default</c> instance (reachable, for example, from a <c>TryGetKey</c>-style out parameter on a
    /// miss), which renders with an <c>(unset)</c> fingerprint instead of faulting.</summary>
    public override string ToString() =>
        string.IsNullOrEmpty(Fingerprint)
            ? $"{Slot}#(unset)"
            : $"{Slot}#{Fingerprint[..Math.Min(12, Fingerprint.Length)]}";
}

/// <summary>Accumulates the values a backend actually reads into a <see cref="ProviderKey"/>.
///
/// <para><b>Every contribution is named</b>, which is the point: a key assembled by concatenating values is
/// one forgotten member away from reusing a provider built from stale configuration, and the omission is
/// invisible in review. Naming each one makes it reviewable.</para>
///
/// <para><b>Include values resolved at RUNTIME, not just the saved settings.</b> A locally-provisioned
/// engine's binary and model paths appear when a download completes — the saved configuration has not
/// changed at all, yet a pooled instance is holding empty paths and will keep failing. Keying only on "the
/// configuration the user typed" looks correct and is not.</para></summary>
public sealed class ProviderKeyBuilder
{
    private readonly string _slot;
    private readonly StringBuilder _parts = new();

    internal ProviderKeyBuilder(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        _slot = slot;
    }

    /// <summary>Fold in a named value. Null is distinct from empty.</summary>
    /// <param name="name">What this value is — appears in the digest, so it cannot collide with a
    /// different member carrying the same text.</param>
    /// <param name="value">The value. Use <see cref="WithSecret"/> for credentials.</param>
    public ProviderKeyBuilder With(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        // name AND value are each length-prefixed before being appended, so neither one's own text can
        // forge a delimiter and be mistaken for a boundary between contributions: With("a=1:X;b", "Y")
        // cannot fold to the same digest as With("a","X").With("b","Y") the way an unlengthed name could.
        _parts.Append(name.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(name)
              .Append('=')
              .Append(value?.Length.ToString(CultureInfo.InvariantCulture) ?? "~")
              .Append(':').Append(value).Append(';');
        return this;
    }

    /// <summary>Fold in a named integer.</summary>
    public ProviderKeyBuilder With(string name, int value) =>
        With(name, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>Fold in a named double, round-trip formatted so equal values always agree.</summary>
    public ProviderKeyBuilder With(string name, double value) =>
        With(name, value.ToString("R", CultureInfo.InvariantCulture));

    /// <summary>Fold in a named flag.</summary>
    public ProviderKeyBuilder With(string name, bool value) => With(name, value ? "true" : "false");

    /// <summary>Fold in a CREDENTIAL: the value is hashed and never retained, so a rotation still changes
    /// the key while the revoked secret is not held alive inside a dictionary key for the life of the
    /// process.</summary>
    public ProviderKeyBuilder WithSecret(string name, string? value) =>
        value is null ? With(name, null) : With(name, "sha256:" + Digest(value));

    /// <summary>Produce the key.</summary>
    public ProviderKey Build() => new(_slot, Digest(_parts.ToString()));

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
