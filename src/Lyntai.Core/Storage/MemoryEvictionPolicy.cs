namespace Lyntai.Storage;

/// <summary>How over-limit memory entries are chosen for eviction when a bound (count cap / size budget)
/// is exceeded on write.</summary>
public enum MemoryEvictionMode
{
    /// <summary>Evict the oldest-CREATED first — a sliding window (LangChain's buffer-window memory). The
    /// default; needs no per-recall bookkeeping.</summary>
    Fifo,

    /// <summary>Evict the least-recently-RECALLED first (an LRU cache — MemGPT-style working-set eviction).
    /// A <b>queried</b> recall (a targeted lookup) refreshes an entry's recency so hot facts survive; a bare
    /// list-all / compose-all recall does NOT count as "use" (else it would churn the whole set) — so prefer
    /// <see cref="Fifo"/> if your app always composes EVERY fact into the prompt. Needs last-access tracking
    /// (a small best-effort write on the queried recall).</summary>
    Lru,
}

/// <summary>
/// How <see cref="IMemoryStore"/> bounds its size — the consuming app's control over eviction, mirroring
/// how <c>RoutingPolicy</c> makes fallback configurable. Composable knobs — a per-scope count cap, a default
/// TTL, a per-scope size (character) budget — with a choice of <see cref="Mode"/>; the presets name the
/// common shapes. Every limit is scoped per <c>(taskKey, scope)</c>. The default reproduces the historical
/// behavior (a 500-entry FIFO count cap). Set limits to <c>null</c> to disable them (all null =
/// <see cref="Manual"/> — the app owns size via <c>PruneAsync</c>/<c>ForgetAsync</c>).
/// <para><b>Not to be confused with <see cref="Lyntai.Memory.Modulation.IMemoryRetentionPolicy"/></b>, which
/// belongs to the graph memory engine and lengthens an entry's half-life. This type <i>removes</i> entries
/// from <see cref="IMemoryStore"/>; that one changes how fast a graph entry decays.</para>
/// <para>Inspiration: LangChain's buffer-window / token-buffer memories, MemGPT/Letta eviction, mem0.</para>
/// </summary>
public sealed class MemoryEvictionPolicy
{
    /// <summary>Max LIVE entries kept per <c>(taskKey, scope)</c>; entries beyond this are evicted per
    /// <see cref="Mode"/> on write. <c>null</c> or ≤0 = no count cap.</summary>
    public int? MaxEntriesPerScope { get; set; } = 500;

    /// <summary>Eviction order applied by BOTH the count cap and the size budget.</summary>
    public MemoryEvictionMode Mode { get; set; } = MemoryEvictionMode.Fifo;

    /// <summary>A default max age applied to entries remembered WITHOUT a per-call ttl — they expire (drop
    /// from recall, removed by <c>PruneAsync</c>) after this. A per-call ttl still wins. <c>null</c> = no
    /// default expiry (an entry lives until evicted by a size bound).</summary>
    public TimeSpan? DefaultTtl { get; set; }

    /// <summary>Max total content size (in characters) kept per <c>(taskKey, scope)</c>; entries beyond this
    /// budget are evicted per <see cref="Mode"/> on write (a size/token-budget window — LangChain's
    /// token-buffer memory, approximated by character length for portable, deterministic, tokenizer-free
    /// counting). <c>null</c> or ≤0 = no size budget. At least one entry is always kept even if it alone
    /// exceeds the budget.</summary>
    public int? MaxCharsPerScope { get; set; }

    /// <summary>Whether any automatic size bound applies (a count cap or a size budget). When false the store
    /// enforces only per-entry TTL — size is entirely app-managed.</summary>
    public bool HasSizeBound => MaxEntriesPerScope is > 0 || MaxCharsPerScope is > 0;

    /// <summary>Whether recall must refresh last-access recency (only <see cref="MemoryEvictionMode.Lru"/>
    /// needs it — a small best-effort write on recall).</summary>
    public bool TracksAccess => Mode == MemoryEvictionMode.Lru;

    // ── presets: the "multi-way" an app selects ──────────────────────────────────────────────────────

    /// <summary>The default — a 500-entry FIFO count cap per scope (reproduces pre-policy behavior).</summary>
    public static MemoryEvictionPolicy Default => new();

    /// <summary>No automatic size bound — the app owns size entirely via <c>PruneAsync</c>/<c>ForgetAsync</c>
    /// (per-call TTL still applies). A "bring your own eviction / archival" posture.</summary>
    public static MemoryEvictionPolicy Manual => new() { MaxEntriesPerScope = null };

    /// <summary>Keep at most <paramref name="max"/> entries per scope, evicting per <paramref name="mode"/>
    /// (a sliding FIFO window, or an LRU working set).</summary>
    public static MemoryEvictionPolicy CountCap(int max, MemoryEvictionMode mode = MemoryEvictionMode.Fifo) =>
        new() { MaxEntriesPerScope = max, Mode = mode };

    /// <summary>Expire entries after <paramref name="ttl"/> by default (optionally also cap the count).</summary>
    public static MemoryEvictionPolicy TimeToLive(TimeSpan ttl, int? maxEntries = null) =>
        new() { DefaultTtl = ttl, MaxEntriesPerScope = maxEntries };

    /// <summary>Keep the per-scope content under <paramref name="maxChars"/> characters, evicting per
    /// <paramref name="mode"/> (a size/token-budget window).</summary>
    public static MemoryEvictionPolicy SizeBudget(int maxChars, MemoryEvictionMode mode = MemoryEvictionMode.Fifo) =>
        new() { MaxEntriesPerScope = null, MaxCharsPerScope = maxChars, Mode = mode };

    /// <summary>A count cap AND a default TTL together (a bounded, self-expiring window).</summary>
    public static MemoryEvictionPolicy Composite(int maxEntries, TimeSpan ttl,
        MemoryEvictionMode mode = MemoryEvictionMode.Fifo) =>
        new() { MaxEntriesPerScope = maxEntries, DefaultTtl = ttl, Mode = mode };
}
