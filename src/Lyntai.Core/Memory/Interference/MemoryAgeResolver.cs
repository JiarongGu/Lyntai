namespace Lyntai.Memory.Interference;

/// <summary>The Interference domain's composition as one engine applies it: every registered
/// <see cref="IMemoryAgePolicy"/>, folded by one <see cref="IMemoryAgeCompositionPolicy"/> (<c>docs/DECISIONS.md</c>
/// D48 — the engine composes nothing itself).
/// <para><b>One rule for every age axis</b> — an entry's age, its connection-strength age and an edge's age:
/// a <see cref="MemoryAgeKind.Derivable"/> policy projects from the stored primitives
/// (<see cref="MemoryAgeSample"/>), an <see cref="MemoryAgeKind.Accumulating"/> one reads the store's own
/// <c>Advance</c>-driven accumulator. The shipped default (one Accumulating <see cref="BurstDampenedAgePolicy"/>)
/// therefore composes exactly the store's accumulator on every axis.</para></summary>
internal sealed class MemoryAgeResolver
{
    private readonly IReadOnlyList<IMemoryAgePolicy> _policies;
    private readonly IMemoryAgeCompositionPolicy _composition;

    /// <param name="policies">The registered age policies; null or empty takes a single burst-damped per-write
    /// policy — the damping is not optional garnish, since an undamped count-based policy lets a bulk ingest
    /// wipe everything stored before it.</param>
    /// <param name="composition">How several combine; null takes <see cref="SummedAgeCompositionPolicy"/>.</param>
    /// <exception cref="ArgumentException">More than one policy is <see cref="MemoryAgeKind.Accumulating"/>.</exception>
    internal MemoryAgeResolver(IEnumerable<IMemoryAgePolicy>? policies, IMemoryAgeCompositionPolicy? composition)
    {
        _policies = Normalize(policies);
        _composition = composition ?? new SummedAgeCompositionPolicy();
    }

    /// <summary>Whether any registered policy is <see cref="MemoryAgeKind.Derivable"/> — in which case the
    /// store's accumulator is not what this resolver reads, and a prune must evaluate ages itself.</summary>
    internal bool AnyDerivable => _policies.Any(c => c.Kind == MemoryAgeKind.Derivable);

    /// <summary>The ONE tick a write hands the store.
    /// <para><b>Encoding composes across every policy</b>: it is multiplied once into the write's initial
    /// stability and never re-derived, so nothing double-counts. <b>Position composes across the
    /// Accumulating policy alone when one is registered</b> — a Derivable policy's contribution is already
    /// recorded exactly in the primitives, and writing it into the accumulator too is the double count
    /// <see cref="Compose"/> would then read a second time. With no Accumulating policy, position composes
    /// across every tick, since nothing reads the accumulator then.</para>
    /// <para>Every policy's <see cref="IMemoryAgePolicy.Advance"/> is still called, so a stateful policy keeps
    /// its own per-engine bookkeeping current.</para></summary>
    internal MemoryTick Advance(MemoryWrite write, string engine)
    {
        var ticks = new MemoryTick[_policies.Count];
        for (var i = 0; i < ticks.Length; i++) ticks[i] = _policies[i].Advance(write, engine);
        var all = _composition.Advance(ticks);

        var accumulating = new List<MemoryTick>(ticks.Length);
        for (var i = 0; i < ticks.Length; i++)
            if (_policies[i].Kind == MemoryAgeKind.Accumulating) accumulating.Add(ticks[i]);

        var position = accumulating.Count > 0 ? _composition.Advance(accumulating).Position : all.Position;
        return new MemoryTick(position, all.Encoding);
    }

    /// <summary>One age axis, composed across every policy: each Derivable policy projects
    /// <paramref name="sample"/>, each Accumulating one contributes <paramref name="accumulated"/>.</summary>
    /// <param name="sample">The axis's stored primitives.</param>
    /// <param name="accumulated">The store's accumulator for the same axis.</param>
    internal double Compose(MemoryAgeSample sample, double accumulated) =>
        _composition.Age([.. _policies.Select(c => c.Kind == MemoryAgeKind.Derivable ? c.Age(sample) : accumulated)]);

    /// <summary>Rejects a second Accumulating policy: the store's position accumulator is ONE number, and two
    /// path-dependent quantities cannot share it without silently blending their histories.</summary>
    private static IReadOnlyList<IMemoryAgePolicy> Normalize(IEnumerable<IMemoryAgePolicy>? policies)
    {
        var list = policies?.ToList() ?? [];
        if (list.Count == 0) return [new BurstDampenedAgePolicy(new PerWriteAgePolicy())];

        var accumulating = list.Count(c => c.Kind == MemoryAgeKind.Accumulating);
        if (accumulating > 1)
            throw new ArgumentException(
                $"{accumulating} Accumulating age policies were registered ({string.Join(", ", list
                    .Where(c => c.Kind == MemoryAgeKind.Accumulating).Select(c => c.GetType().Name))}); " +
                "at most one is supported. The store's position accumulator is a single number, and two " +
                "path-dependent (Accumulating) policies cannot share it without silently blending their " +
                "distinct histories together.", "agePolicies");
        return list;
    }
}
