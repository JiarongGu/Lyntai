namespace Lyntai.Lifecycle;

/// <summary>One backend a caller wants routed: the CONFIGURATION it should run under, and how to build it
/// if the pool has nothing to reuse.
///
/// <para>A pair rather than a constructed instance, because construction is the pool's decision — that is
/// what lets the same call site reuse or rebuild depending only on which strategy is registered.</para></summary>
/// <typeparam name="TProvider">The provider seam.</typeparam>
/// <param name="Key">The configuration. See <see cref="ProviderKeyBuilder"/> for what belongs in it.</param>
/// <param name="Create">Builds the backend. Invoked only when the pool has no instance for
/// <paramref name="Key"/>.
///
/// <para>It runs while the pool holds its internal lock (see
/// <see cref="IProviderPool{TProvider}.GetOrAdd"/>), so it must not block, await, or call back into the
/// pool — construction only.</para></param>
public readonly record struct ProviderRegistration<TProvider>(ProviderKey Key, Func<TProvider> Create)
    where TProvider : class, IProviderIdentity;
