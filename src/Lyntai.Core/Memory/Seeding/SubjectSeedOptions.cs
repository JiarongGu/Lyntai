namespace Lyntai.Memory.Seeding;

/// <summary>Constants for <see cref="SubjectSeedSource"/>. An init-only record taken BY VALUE, matching
/// <see cref="SemanticSeedOptions"/> and the rest of the memory subsystem's own convention — rather than an
/// <c>Action&lt;T&gt;</c> configure callback, which cannot mutate an init-only record and would silently do
/// nothing.
///
/// <para><b>Zero is legal here, unlike <see cref="SemanticSeedOptions.K"/>.</b> A subject exists only
/// because an annotator was already registered and paid for, so this source is meant to be registered
/// unconditionally and switched off by its OWN knob rather than by omission — the asymmetry
/// <see cref="Lyntai.Memory.GraphMemoryOptions.SubjectSeedK"/>'s own remarks already state. Only a negative
/// or non-finite value is rejected.</para></summary>
public sealed record SubjectSeedOptions
{
    private readonly int _k = 5;
    private readonly int _scan = 256;

    /// <summary>How many nodes this source fetches PER MATCHED SUBJECT —
    /// <see cref="IMemoryGraphStore.NodesBySubjectAsync"/>'s own <c>limit</c>, independent of
    /// <see cref="MemorySeedRequest.Limit"/>, which separately caps what this source RETURNS once every
    /// matched subject's nodes are merged and de-duplicated. Narrowing this by the request's limit would
    /// search shallower per subject whenever a recall asks for fewer than this source can hold — the same
    /// coupling <see cref="SemanticSeedOptions.K"/>'s own remarks already refuse.
    /// <para><c>0</c> stops this source from ever fetching, which — together with <see cref="Scan"/> — is
    /// this source's own "off" switch, exactly as <see cref="Lyntai.Memory.GraphMemoryOptions.SubjectSeedK"/>
    /// already behaves.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative or non-finite value.</exception>
    public int K
    {
        get => _k;
        init => _k = (int)MemoryOption.Require(value, MemoryOptionRange.NonNegative, nameof(SubjectSeedOptions),
            "a negative bound cannot fetch anything, which is what zero already means — see this member's " +
            "own remarks on how to switch this source off.");
    }

    /// <summary>How many of a task's already-used handles this source matches its query against, most-used
    /// first — the scan <see cref="K"/> draws from, mirroring
    /// <see cref="Lyntai.Memory.GraphMemoryOptions.SubjectSeedScan"/>.
    /// <para><c>0</c> is this source's other "off" switch: no handles are read, so nothing can ever
    /// match.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to a negative or non-finite value.</exception>
    public int Scan
    {
        get => _scan;
        init => _scan = (int)MemoryOption.Require(value, MemoryOptionRange.NonNegative,
            nameof(SubjectSeedOptions),
            "a negative bound cannot scan anything, which is what zero already means — see this member's " +
            "own remarks on how to switch this source off.");
    }
}
