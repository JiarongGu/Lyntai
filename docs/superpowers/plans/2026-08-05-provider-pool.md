# Provider Pool (GEN12) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Lyntai a domain-agnostic provider **pool** so an app whose backend configuration is owned
externally (an end user, a database) can keep many configurations live at once, swap them safely while
renders are in flight, and keep cooldown and admission bookkeeping attached to the *configuration* rather
than the instance.

**Architecture:** A new `Lyntai.Lifecycle` namespace in `Lyntai.Core` holds an identity interface, a
configuration key, the `IProviderPool<TProvider>` seam and two strategies (`Bounded`, `Transient`). Both
routers learn an optional cooldown-key delegate, and new router factories compose a fully-governed router
over a caller-chosen provider set. Everything is additive — an app that never touches the pool behaves
bit-for-bit as it does today.

**Tech Stack:** .NET 10, C# 13, xUnit, `Microsoft.Extensions.DependencyInjection`,
`Microsoft.Extensions.Logging.Abstractions`. No new package and no new third-party dependency.

**Spec:** `docs/superpowers/specs/2026-08-05-provider-pool-design.md` — read it before starting. Section
references below (§4.5 etc.) point into it.

## Global Constraints

- **Read `.claude/rules/dev-conventions.md` first.** Package layout, DI-collection variation points, and
  the naming vocabulary are load-bearing here.
- **Never `Dto` in a type, member, file or namespace name** — and not in prose either. The tree contains
  zero `Dto` identifiers; keep it that way. Established suffixes: `*Options`, `*Request`/`*Reply`,
  `*Result`, `*Entry`/`*Record`, `*Row`, `*Event`, `*Args`, `*Policy`.
- **Everything in this plan lives in `src/Lyntai.Core`.** No new package, so `check-packages` and
  `check-bundle` need no changes. Never add a third-party dependency to Core.
- **Additive only.** `ILlmProvider` and `IGenerationProvider` are frozen 1.0 surface. Gaining a base
  interface and gaining *optional* constructor parameters are both compatible; changing an existing
  signature is not.
- **A warning in `src/` fails the build** (`check-warnings`). Every new public type and member needs XML
  docs, and every `<see cref="..."/>` must resolve.
- **TDD, every task:** failing test → run it and watch it fail → minimal implementation → run it and watch
  it pass → commit. A test that has never been seen to fail has not been shown to test anything.
- **Never commit without the user's approval.** Each task ends with a commit step; ask first.
- **No dev-machine paths or private tokens** in any tracked file or commit message — the pre-commit guard
  enforces this.
- **Files are BOM-less UTF-8.** Write them with the file-writing tools, never by echoing through the
  console (this machine's console is GBK and mangles em-dashes and CJK).
- **Test commands:** `node devtools/dev.mjs test -- --filter "FullyQualifiedName~<Name>"` for one class,
  `node devtools/dev.mjs verify` for the full gate (build → test → e2e → leak scan).

## File Structure

| File | Responsibility |
|---|---|
| `src/Lyntai.Core/Lifecycle/IProviderIdentity.cs` | The one member both provider seams already have |
| `src/Lyntai.Core/Lifecycle/ProviderKey.cs` | The configuration key + its builder |
| `src/Lyntai.Core/Lifecycle/IProviderPool.cs` | The seam, its statistics record, its options |
| `src/Lyntai.Core/Lifecycle/TransientProviderPool.cs` | Never-reuse strategy |
| `src/Lyntai.Core/Lifecycle/BoundedProviderPool.cs` | Reuse + LRU + idle eviction strategy |
| `src/Lyntai.Core/Lifecycle/ProviderAdmission.cs` | Keyed concurrency limiting + its options |
| `src/Lyntai.Core/Lifecycle/ProviderRegistration.cs` | The `(key, factory)` pair the factories take |
| `src/Lyntai.Core/Generation/Routing/GenerationRouterFactory.cs` | Composes a governed generation router |
| `src/Lyntai.Core/Llm/Routing/LlmRouterFactory.cs` | Composes a governed LLM router |
| `src/Lyntai.Core/Lifecycle/ProviderPoolBuilderExtensions.cs` | `UseProviderPool` / `UseTransientProviders` / `ConfigureProviderAdmission` |
| `tests/Lyntai.Tests/Lifecycle/*.cs` | One test class per component above |

---

### Task 1: `IProviderIdentity`, adopted by both provider seams

**Files:**
- Create: `src/Lyntai.Core/Lifecycle/IProviderIdentity.cs`
- Modify: `src/Lyntai.Core/Generation/IGenerationProvider.cs:16`
- Modify: `src/Lyntai.Core/Llm/ILlmProvider.cs:6`
- Modify: `tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt`
- Test: `tests/Lyntai.Tests/Lifecycle/ProviderIdentityTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `Lyntai.Lifecycle.IProviderIdentity` with `string Id { get; }`. Every later task constrains its
  generics with `where TProvider : class, IProviderIdentity`.

- [ ] **Step 1: Write the failing test**

Create `tests/Lyntai.Tests/Lifecycle/ProviderIdentityTests.cs`:

```csharp
using Lyntai.Generation;
using Lyntai.Lifecycle;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Lifecycle;

public class ProviderIdentityTests
{
    [Fact]
    public void Both_provider_seams_are_provider_identities()
    {
        Assert.True(typeof(IProviderIdentity).IsAssignableFrom(typeof(IGenerationProvider)));
        Assert.True(typeof(IProviderIdentity).IsAssignableFrom(typeof(ILlmProvider)));
    }

    // The whole point of reusing the member both seams already declare: nothing that exists has to change.
    [Fact]
    public void An_existing_implementor_satisfies_it_unchanged()
    {
        IProviderIdentity generation = new FakeGenerationProvider { Id = "a1111" };
        IProviderIdentity llm = new FakeLlmProvider("openai");

        Assert.Equal("a1111", generation.Id);
        Assert.Equal("openai", llm.Id);
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~ProviderIdentityTests"`
Expected: compile error — `The type or namespace name 'Lifecycle' does not exist in the namespace 'Lyntai'`.

- [ ] **Step 3: Create the interface**

Create `src/Lyntai.Core/Lifecycle/IProviderIdentity.cs`:

```csharp
namespace Lyntai.Lifecycle;

/// <summary>The one thing every pluggable backend has: a stable id the router matches candidates against.
///
/// <para>It exists so lifetime machinery — <see cref="IProviderPool{TProvider}"/> above all — can be written
/// once for every provider seam instead of once per domain. <see cref="Lyntai.Llm.ILlmProvider"/> and
/// <see cref="Lyntai.Generation.IGenerationProvider"/> both already declared exactly this member, so adopting
/// it as a base changed no implementation anywhere.</para></summary>
public interface IProviderIdentity
{
    /// <summary>The candidate id routing selects on (<c>"claude-cli"</c>, <c>"a1111"</c>,
    /// <c>"local-diffusion"</c>). Stable across the process lifetime.</summary>
    string Id { get; }
}
```

- [ ] **Step 4: Adopt it on both seams**

In `src/Lyntai.Core/Generation/IGenerationProvider.cs`, change the declaration to
`public interface IGenerationProvider : Lyntai.Lifecycle.IProviderIdentity` and replace the `Id` member
with `/// <inheritdoc/>` above `string Id { get; }` — keep the member declared so the existing doc comment's
intent survives, or delete it and rely on the base. Prefer **deleting** the duplicate declaration and
letting the base carry it.

In `src/Lyntai.Core/Llm/ILlmProvider.cs`, do the same:
`public interface ILlmProvider : Lyntai.Lifecycle.IProviderIdentity`, deleting the local `string Id { get; }`.

- [ ] **Step 5: Run the test and watch it pass**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~ProviderIdentityTests"`
Expected: PASS, 2 tests.

- [ ] **Step 6: Update the API baseline**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~ApiSurfaceTests"`
Expected: FAIL for `Lyntai.Core` — the surface gained a type and moved a member.

Copy the emitted `tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt.actual` over
`tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt`, delete the `.actual` file, then re-run the same filter.
Expected: PASS.

**Read the diff before accepting it.** It must show only the new `IProviderIdentity` type and `Id` moving
to the base interface. Anything else means something unintended changed.

- [ ] **Step 7: Full build, then commit**

```bash
node devtools/dev.mjs build
git add src/Lyntai.Core/Lifecycle/IProviderIdentity.cs src/Lyntai.Core/Generation/IGenerationProvider.cs src/Lyntai.Core/Llm/ILlmProvider.cs tests/Lyntai.Tests/Lifecycle/ProviderIdentityTests.cs tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt
git commit -m "feat(lifecycle): add IProviderIdentity as the shared base of both provider seams"
```

---

### Task 2: `ProviderKey` and its builder

**Files:**
- Create: `src/Lyntai.Core/Lifecycle/ProviderKey.cs`
- Test: `tests/Lyntai.Tests/Lifecycle/ProviderKeyTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `readonly record struct ProviderKey(string Slot, string Fingerprint)`;
  `ProviderKey.For(string slot)` → `ProviderKeyBuilder`; builder methods
  `With(string, string?)`, `With(string, int)`, `With(string, double)`, `With(string, bool)`,
  `WithSecret(string, string?)`, `Build()` → `ProviderKey`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Lyntai.Tests/Lifecycle/ProviderKeyTests.cs`:

```csharp
using Lyntai.Lifecycle;

namespace Lyntai.Tests.Lifecycle;

public class ProviderKeyTests
{
    private static ProviderKey Key(string url, string? apiKey, string? model = "dall-e-3") =>
        ProviderKey.For("openai-images")
            .With("baseUrl", url)
            .With("model", model)
            .WithSecret("apiKey", apiKey)
            .Build();

    [Fact]
    public void Identical_configurations_produce_equal_keys()
    {
        Assert.Equal(Key("https://api.example.invalid/v1", "sk-abc"),
                     Key("https://api.example.invalid/v1", "sk-abc"));
    }

    [Fact]
    public void A_changed_value_produces_a_different_key()
    {
        Assert.NotEqual(Key("https://api.example.invalid/v1", "sk-abc"),
                        Key("https://other.example.invalid/v1", "sk-abc"));
    }

    // The rotation case: the saved settings look identical except for the credential, and a pool that
    // missed it would keep calling with a revoked secret.
    [Fact]
    public void A_rotated_credential_produces_a_different_key()
    {
        Assert.NotEqual(Key("https://api.example.invalid/v1", "sk-abc"),
                        Key("https://api.example.invalid/v1", "sk-xyz"));
    }

    [Fact]
    public void A_missing_credential_differs_from_a_present_one()
    {
        Assert.NotEqual(Key("https://api.example.invalid/v1", null),
                        Key("https://api.example.invalid/v1", "sk-abc"));
    }

    [Fact]
    public void The_secret_never_appears_in_the_key()
    {
        var key = Key("https://api.example.invalid/v1", "sk-super-secret");

        Assert.DoesNotContain("sk-super-secret", key.Fingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("sk-super-secret", key.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_slot_is_carried_verbatim()
    {
        Assert.Equal("openai-images", Key("https://api.example.invalid/v1", "sk-abc").Slot);
    }

    // Name-and-length framing: without it, With("a","bc") and With("ab","c") would fold to the same bytes.
    [Fact]
    public void Contributions_cannot_collide_by_concatenation()
    {
        var left  = ProviderKey.For("x").With("a", "bc").Build();
        var right = ProviderKey.For("x").With("ab", "c").Build();

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Different_slots_with_identical_values_differ()
    {
        Assert.NotEqual(ProviderKey.For("a1111").With("baseUrl", "http://localhost:7860").Build(),
                        ProviderKey.For("comfyui").With("baseUrl", "http://localhost:7860").Build());
    }

    [Fact]
    public void Order_of_contributions_is_significant_and_stable()
    {
        var built = ProviderKey.For("x").With("a", "1").With("b", "2").Build();
        var again = ProviderKey.For("x").With("a", "1").With("b", "2").Build();

        Assert.Equal(built, again);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_slot_is_rejected(string slot) =>
        Assert.Throws<ArgumentException>(() => ProviderKey.For(slot));
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~ProviderKeyTests"`
Expected: compile error — `ProviderKey` does not exist.

- [ ] **Step 3: Implement**

Create `src/Lyntai.Core/Lifecycle/ProviderKey.cs`:

```csharp
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

    /// <summary>A short, log-safe rendering. Contains no secret material.</summary>
    public override string ToString() => $"{Slot}#{Fingerprint[..Math.Min(12, Fingerprint.Length)]}";
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
        // name AND length are both folded in, so With("a","bc") cannot produce the digest of With("ab","c")
        _parts.Append(name).Append('=')
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
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~ProviderKeyTests"`
Expected: PASS, 11 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Lyntai.Core/Lifecycle/ProviderKey.cs tests/Lyntai.Tests/Lifecycle/ProviderKeyTests.cs
git commit -m "feat(lifecycle): add ProviderKey, the configuration key reuse and cooldown are keyed on"
```

---

### Task 3: The `IProviderPool<TProvider>` seam and the `Transient` strategy

**Files:**
- Create: `src/Lyntai.Core/Lifecycle/IProviderPool.cs`
- Create: `src/Lyntai.Core/Lifecycle/TransientProviderPool.cs`
- Test: `tests/Lyntai.Tests/Lifecycle/TransientProviderPoolTests.cs`

**Interfaces:**
- Consumes: `IProviderIdentity` (Task 1), `ProviderKey` (Task 2).
- Produces: `IProviderPool<TProvider>` with `GetOrAdd(ProviderKey, Func<TProvider>)`,
  `TryGetKey(TProvider, out ProviderKey)`, `Retire(ProviderKey)` → `bool`, `RetireSlot(string)` → `int`,
  `Statistics` → `ProviderPoolStatistics`. Also
  `readonly record struct ProviderPoolStatistics(int Live, long Created, long Reused, long Retired)` and
  `sealed class ProviderPoolOptions { int? MaxEntries; TimeSpan? IdleTimeout; }`, and
  `sealed class TransientProviderPool<TProvider> : IProviderPool<TProvider>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Lyntai.Tests/Lifecycle/TransientProviderPoolTests.cs`:

```csharp
using Lyntai.Lifecycle;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Lifecycle;

public class TransientProviderPoolTests
{
    private static ProviderKey Key(string slot = "fake-generation", string value = "a") =>
        ProviderKey.For(slot).With("v", value).Build();

    [Fact]
    public void Every_call_builds_a_new_instance()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();
        var key = Key();

        var first = pool.GetOrAdd(key, () => new FakeGenerationProvider());
        var second = pool.GetOrAdd(key, () => new FakeGenerationProvider());

        Assert.NotSame(first, second);
    }

    // Without this, cooldown and admission fall back to the provider id under Transient and the whole
    // configuration-scoped story collapses for the strategy that needs it most.
    [Fact]
    public void The_key_of_a_built_instance_is_recoverable()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();
        var key = Key(value: "specific");

        var instance = pool.GetOrAdd(key, () => new FakeGenerationProvider());

        Assert.True(pool.TryGetKey(instance, out var found));
        Assert.Equal(key, found);
    }

    [Fact]
    public void An_instance_the_pool_never_built_has_no_key()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();

        Assert.False(pool.TryGetKey(new FakeGenerationProvider(), out _));
    }

    [Fact]
    public void An_id_that_disagrees_with_the_slot_is_rejected()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();

        var ex = Assert.Throws<ArgumentException>(() =>
            pool.GetOrAdd(Key("a1111"), () => new FakeGenerationProvider { Id = "comfyui" }));

        Assert.Contains("comfyui", ex.Message, StringComparison.Ordinal);
        Assert.Contains("a1111", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_retained_so_retiring_reports_nothing_to_retire()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();
        pool.GetOrAdd(Key(), () => new FakeGenerationProvider());

        Assert.False(pool.Retire(Key()));
        Assert.Equal(0, pool.RetireSlot("fake-generation"));
        Assert.Equal(0, pool.Statistics.Live);
    }

    [Fact]
    public void Statistics_count_every_construction_and_never_a_reuse()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();
        pool.GetOrAdd(Key(), () => new FakeGenerationProvider());
        pool.GetOrAdd(Key(), () => new FakeGenerationProvider());

        Assert.Equal(2, pool.Statistics.Created);
        Assert.Equal(0, pool.Statistics.Reused);
    }

    [Fact]
    public void A_throwing_factory_propagates()
    {
        var pool = new TransientProviderPool<FakeGenerationProvider>();

        Assert.Throws<InvalidOperationException>(() =>
            pool.GetOrAdd(Key(), () => throw new InvalidOperationException("bad config")));
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~TransientProviderPoolTests"`
Expected: compile error — `IProviderPool` / `TransientProviderPool` do not exist.

- [ ] **Step 3: Create the seam**

Create `src/Lyntai.Core/Lifecycle/IProviderPool.cs`:

```csharp
namespace Lyntai.Lifecycle;

/// <summary>Owns the LIFETIME of backend instances built from externally-owned configuration.
///
/// <para>Where configuration belongs to the deployment, register backends with <c>Add*</c> and ignore this
/// entirely. Where it belongs to an END USER or a store the process polls, three things follow: the
/// settings change at any moment, the CHOICE of backend is one of those settings, and several
/// configurations of the same backend are live at once. Resolving configuration per call then quietly
/// becomes constructing a backend per call — which churns instances and, far worse, makes every consumer
/// rewrite the same cache and get the key subtly wrong.</para>
///
/// <para><b>A pool is not a router.</b> Routing FALLS BACK; a pool must not. "The user chose this
/// configuration" has to FAIL rather than silently succeed against a different one and bill that
/// credential. A pool selects what was chosen; a router selects what is healthy.</para>
///
/// <para>Two strategies ship: <see cref="BoundedProviderPool{TProvider}"/> reuses while the key is
/// unchanged, and <see cref="TransientProviderPool{TProvider}"/> never reuses. Both keep
/// <see cref="TryGetKey"/> answering, which is what lets cooldown and admission stay keyed on the
/// configuration under either. Implement this interface for anything else — a pool shared across
/// processes, one reporting to the host's telemetry, or a lease-based one when a provider ever owns
/// something needing prompt release (see the remarks on <see cref="Retire"/>).</para></summary>
/// <typeparam name="TProvider">The provider seam being pooled — <see cref="Lyntai.Llm.ILlmProvider"/>,
/// <see cref="Lyntai.Generation.IGenerationProvider"/>, or a concrete backend.</typeparam>
public interface IProviderPool<TProvider> where TProvider : class, IProviderIdentity
{
    /// <summary>The instance for this configuration, building one through <paramref name="factory"/> when
    /// the strategy has none to reuse. Thread-safe: a UI request and a background job arrive together.</summary>
    /// <param name="key">The configuration. See <see cref="ProviderKeyBuilder"/> for what belongs in it.</param>
    /// <param name="factory">Builds the instance. Invoked only when needed; exceptions propagate to the
    /// caller and nothing is stored.</param>
    /// <exception cref="ArgumentException">The built instance's <see cref="IProviderIdentity.Id"/> does not
    /// match <see cref="ProviderKey.Slot"/> — which would leave the instance unroutable, because routers
    /// match candidates on the id.</exception>
    TProvider GetOrAdd(ProviderKey key, Func<TProvider> factory);

    /// <summary>Which configuration produced an instance. This is how a router attributes dead-host
    /// cooldown and admission to the CONFIGURATION rather than to the backend id — without it, one
    /// tenant's exhausted quota benches every other tenant sharing that backend.</summary>
    /// <returns>False for an instance this pool never built.</returns>
    bool TryGetKey(TProvider instance, out ProviderKey key);

    /// <summary>Retire one configuration: remove it from the lookup so subsequent calls build afresh.
    ///
    /// <para><b>Retiring never disposes.</b> In-flight callers hold their own reference and finish
    /// normally; the runtime reclaims the instance once the last of them is done. This is not squeamishness
    /// — without leases a pool cannot know when the last caller finished, so disposing on retirement means
    /// disposing while calls may still be running, and a render legitimately outlives the configuration
    /// that started it. A provider owning something that needs prompt release wants a lease-based pool,
    /// which is what implementing this interface is for.</para></summary>
    /// <returns>True if an entry was retired.</returns>
    bool Retire(ProviderKey key);

    /// <summary>Retire EVERY configuration of one backend — the backend the user removed outright.</summary>
    /// <returns>How many entries were retired.</returns>
    int RetireSlot(string slot);

    /// <summary>Counters for diagnostics and tests.</summary>
    ProviderPoolStatistics Statistics { get; }
}

/// <summary>What a pool has been doing. Counters are cumulative; <see cref="Live"/> is a point-in-time
/// reading.</summary>
/// <param name="Live">Entries currently held. Always 0 for a pool that does not reuse.</param>
/// <param name="Created">Instances built.</param>
/// <param name="Reused">Calls answered from an existing entry.</param>
/// <param name="Retired">Entries removed, whether explicitly or by eviction.</param>
public readonly record struct ProviderPoolStatistics(int Live, long Created, long Reused, long Retired);

/// <summary>Bounds for <see cref="BoundedProviderPool{TProvider}"/>.
///
/// <para>Defaults ship rather than forcing a choice: an unconfigured pool that grows without limit is a
/// worse default than one with generous bounds, and a host wanting unbounded can say so by setting both to
/// null.</para></summary>
public sealed class ProviderPoolOptions
{
    /// <summary>Live entries before the least-recently-used one is retired. Null = unbounded.</summary>
    public int? MaxEntries { get; set; } = 64;

    /// <summary>Retire an entry unused for this long, evaluated on access. Null = never on idle.
    ///
    /// <para>A long-idle entry is a credential sitting in memory for no reason, which is the same argument
    /// a connection pool makes for its idle timeout.</para></summary>
    public TimeSpan? IdleTimeout { get; set; } = TimeSpan.FromMinutes(10);
}
```

- [ ] **Step 4: Implement the Transient strategy**

Create `src/Lyntai.Core/Lifecycle/TransientProviderPool.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace Lyntai.Lifecycle;

/// <summary>The NEVER-REUSE strategy: every call builds a fresh instance.
///
/// <para>For the host that wants configuration re-read and applied on every single call. Registering this
/// instead of <see cref="BoundedProviderPool{TProvider}"/> changes that behaviour without touching a
/// single call site — which is the point of pooling being a strategy rather than something the library
/// imposes.</para>
///
/// <para>It still records each instance's key, so dead-host cooldown and admission control remain keyed on
/// the CONFIGURATION. That is what makes the trade honest: a consumer can rebuild a provider every call and
/// still accumulate bench state correctly, which is exactly what rebuilding by hand fails to do.</para></summary>
/// <typeparam name="TProvider">The provider seam being pooled.</typeparam>
public sealed class TransientProviderPool<TProvider> : IProviderPool<TProvider>
    where TProvider : class, IProviderIdentity
{
    // weak keys: the pool must not be the reason a transient instance stays alive
    private readonly ConditionalWeakTable<TProvider, StrongBox<ProviderKey>> _keys = [];
    private long _created;

    /// <inheritdoc/>
    public TProvider GetOrAdd(ProviderKey key, Func<TProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var instance = factory() ?? throw new InvalidOperationException(
            $"the factory for '{key}' returned null");
        ProviderPoolGuard.EnsureIdMatchesSlot(instance, key);

        _keys.AddOrUpdate(instance, new StrongBox<ProviderKey>(key));
        Interlocked.Increment(ref _created);
        return instance;
    }

    /// <inheritdoc/>
    public bool TryGetKey(TProvider instance, out ProviderKey key)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_keys.TryGetValue(instance, out var box)) { key = box.Value; return true; }
        key = default;
        return false;
    }

    /// <inheritdoc/>
    /// <remarks>Always false — nothing is retained, so there is never an entry to retire.</remarks>
    public bool Retire(ProviderKey key) => false;

    /// <inheritdoc/>
    /// <remarks>Always 0 — see <see cref="Retire"/>.</remarks>
    public int RetireSlot(string slot) => 0;

    /// <inheritdoc/>
    public ProviderPoolStatistics Statistics =>
        new(Live: 0, Created: Interlocked.Read(ref _created), Reused: 0, Retired: 0);
}

/// <summary>The one validation every pool owes its caller.</summary>
internal static class ProviderPoolGuard
{
    /// <summary>A provider whose id disagrees with its slot is unroutable — routers match candidates on the
    /// id, so the mismatch would present as "this backend is not registered" long after the registration
    /// that caused it. Fail where the mistake is.</summary>
    internal static void EnsureIdMatchesSlot<TProvider>(TProvider instance, ProviderKey key)
        where TProvider : class, IProviderIdentity
    {
        if (!string.Equals(instance.Id, key.Slot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"the provider built for slot '{key.Slot}' reports Id '{instance.Id}'. A router matches " +
                $"candidates on Id, so this instance could never be routed to.", nameof(key));
    }
}
```

- [ ] **Step 5: Run the tests and watch them pass**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~TransientProviderPoolTests"`
Expected: PASS, 7 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Lyntai.Core/Lifecycle/IProviderPool.cs src/Lyntai.Core/Lifecycle/TransientProviderPool.cs tests/Lyntai.Tests/Lifecycle/TransientProviderPoolTests.cs
git commit -m "feat(lifecycle): add the IProviderPool seam and its never-reuse strategy"
```

---

### Task 4: `BoundedProviderPool` — reuse, LRU, idle eviction

**Files:**
- Create: `src/Lyntai.Core/Lifecycle/BoundedProviderPool.cs`
- Test: `tests/Lyntai.Tests/Lifecycle/BoundedProviderPoolTests.cs`

**Interfaces:**
- Consumes: everything from Task 3, plus `Lyntai.Tests.Fakes.MutableClock` in tests.
- Produces: `sealed class BoundedProviderPool<TProvider>(ProviderPoolOptions? options = null, Func<DateTimeOffset>? clock = null) : IProviderPool<TProvider>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Lyntai.Tests/Lifecycle/BoundedProviderPoolTests.cs`:

```csharp
using Lyntai.Lifecycle;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Lifecycle;

public class BoundedProviderPoolTests
{
    private static ProviderKey Key(string value, string slot = "fake-generation") =>
        ProviderKey.For(slot).With("v", value).Build();

    private static BoundedProviderPool<FakeGenerationProvider> Pool(
        int? maxEntries = null, TimeSpan? idleTimeout = null, MutableClock? clock = null) =>
        new(new ProviderPoolOptions { MaxEntries = maxEntries, IdleTimeout = idleTimeout },
            clock is null ? null : clock.Get);

    [Fact]
    public void The_same_key_returns_the_same_instance()
    {
        var pool = Pool();
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());
        var second = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        Assert.Same(first, second);
        Assert.Equal(1, pool.Statistics.Created);
        Assert.Equal(1, pool.Statistics.Reused);
    }

    [Fact]
    public void A_changed_key_builds_a_new_instance_and_replaces_the_old()
    {
        var pool = Pool();
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());
        var second = pool.GetOrAdd(Key("b"), () => new FakeGenerationProvider());

        Assert.NotSame(first, second);
    }

    // The multi-configuration requirement: one backend id, several credentials, all live at once.
    [Fact]
    public void Several_configurations_of_one_backend_are_live_simultaneously()
    {
        var pool = Pool();
        var tenantA = pool.GetOrAdd(Key("tenant-a"), () => new FakeGenerationProvider());
        var tenantB = pool.GetOrAdd(Key("tenant-b"), () => new FakeGenerationProvider());

        Assert.NotSame(tenantA, tenantB);
        Assert.Same(tenantA, pool.GetOrAdd(Key("tenant-a"), () => new FakeGenerationProvider()));
        Assert.Same(tenantB, pool.GetOrAdd(Key("tenant-b"), () => new FakeGenerationProvider()));
        Assert.Equal(2, pool.Statistics.Live);
    }

    [Fact]
    public void Retire_drops_the_entry_so_the_next_call_rebuilds()
    {
        var pool = Pool();
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        Assert.True(pool.Retire(Key("a")));
        Assert.False(pool.Retire(Key("a")));

        Assert.NotSame(first, pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider()));
    }

    [Fact]
    public void RetireSlot_drops_every_configuration_of_that_backend_only()
    {
        var pool = Pool();
        pool.GetOrAdd(Key("a", "a1111"), () => new FakeGenerationProvider { Id = "a1111" });
        pool.GetOrAdd(Key("b", "a1111"), () => new FakeGenerationProvider { Id = "a1111" });
        pool.GetOrAdd(Key("c", "comfyui"), () => new FakeGenerationProvider { Id = "comfyui" });

        Assert.Equal(2, pool.RetireSlot("a1111"));
        Assert.Equal(1, pool.Statistics.Live);
    }

    // Trap 7.1: a configuration change must never abort work already running on the old instance.
    [Fact]
    public void A_retired_instance_is_still_usable_by_whoever_holds_it()
    {
        var pool = Pool();
        var inFlight = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        pool.Retire(Key("a"));

        var result = inFlight.GenerateAsync(new GenerationRequest("image", "a cat")).GetAwaiter().GetResult();
        Assert.True(result.IsOk);
    }

    // The pool disposes nothing, ever — see the spec's 4.5. Pinned so a later "helpful" disposal fails here.
    [Fact]
    public void Retiring_never_disposes()
    {
        var pool = new BoundedProviderPool<DisposableProvider>();
        var instance = pool.GetOrAdd(ProviderKey.For("disposable").With("v", "a").Build(),
            () => new DisposableProvider());

        pool.Retire(ProviderKey.For("disposable").With("v", "a").Build());

        Assert.False(instance.Disposed);
    }

    [Fact]
    public void Least_recently_used_is_retired_at_the_entry_cap()
    {
        var pool = Pool(maxEntries: 2);
        var a = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());
        pool.GetOrAdd(Key("b"), () => new FakeGenerationProvider());
        pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());   // touch a, so b is now least recent
        pool.GetOrAdd(Key("c"), () => new FakeGenerationProvider());   // evicts b

        Assert.Equal(2, pool.Statistics.Live);
        Assert.Same(a, pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider()));
    }

    [Fact]
    public void An_entry_idle_past_the_timeout_is_retired_on_the_next_access()
    {
        var clock = new MutableClock();
        var pool = Pool(idleTimeout: TimeSpan.FromMinutes(10), clock: clock);
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        clock.Advance(TimeSpan.FromMinutes(11));

        Assert.NotSame(first, pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider()));
    }

    [Fact]
    public void An_entry_used_within_the_timeout_survives()
    {
        var clock = new MutableClock();
        var pool = Pool(idleTimeout: TimeSpan.FromMinutes(10), clock: clock);
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        clock.Advance(TimeSpan.FromMinutes(9));

        Assert.Same(first, pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider()));
    }

    [Fact]
    public void Both_bounds_unset_means_nothing_is_ever_evicted()
    {
        var clock = new MutableClock();
        var pool = Pool(clock: clock);
        for (var i = 0; i < 200; i++)
            pool.GetOrAdd(Key($"k{i}"), () => new FakeGenerationProvider());

        clock.Advance(TimeSpan.FromDays(30));

        Assert.Equal(200, pool.Statistics.Live);
    }

    // Two requests for a newly-configured tenant arriving together must not each get their own instance,
    // each accumulating half the history.
    [Fact]
    public void Concurrent_calls_on_one_key_build_exactly_once()
    {
        var pool = Pool();
        var built = 0;
        var instances = new FakeGenerationProvider[32];

        Parallel.For(0, instances.Length, i =>
            instances[i] = pool.GetOrAdd(Key("a"), () =>
            {
                Interlocked.Increment(ref built);
                return new FakeGenerationProvider();
            }));

        Assert.Equal(1, built);
        Assert.All(instances, x => Assert.Same(instances[0], x));
    }

    [Fact]
    public void A_throwing_factory_leaves_the_existing_entry_intact()
    {
        var pool = Pool();
        var existing = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider());

        Assert.Throws<InvalidOperationException>(() =>
            pool.GetOrAdd(Key("b"), () => throw new InvalidOperationException("bad config")));

        Assert.Same(existing, pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider()));
        Assert.Equal(1, pool.Statistics.Live);
    }

    [Fact]
    public void An_id_that_disagrees_with_the_slot_is_rejected_and_stores_nothing()
    {
        var pool = Pool();

        Assert.Throws<ArgumentException>(() =>
            pool.GetOrAdd(Key("a", "a1111"), () => new FakeGenerationProvider { Id = "comfyui" }));

        Assert.Equal(0, pool.Statistics.Live);
    }

    private sealed class DisposableProvider : Lyntai.Lifecycle.IProviderIdentity, IDisposable
    {
        public string Id => "disposable";
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
```

Add `using Lyntai.Generation;` at the top of the file for `GenerationRequest`.

- [ ] **Step 2: Run the tests and watch them fail**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~BoundedProviderPoolTests"`
Expected: compile error — `BoundedProviderPool` does not exist.

- [ ] **Step 3: Implement**

Create `src/Lyntai.Core/Lifecycle/BoundedProviderPool.cs`:

```csharp
using System.Runtime.CompilerServices;

namespace Lyntai.Lifecycle;

/// <summary>The default strategy: REUSE while a configuration is unchanged, within declared bounds.
///
/// <para>Reuse is what stops a per-call configuration read from turning into a per-call backend
/// construction. The bounds are what stop a store with many tenants from growing the pool without limit —
/// the same two bounds, for the same two reasons, that a database connection pool carries.</para>
///
/// <para><b>Eviction is evaluated on access</b>, not by a timer: a library should not start a background
/// thread, and a clock read inside <see cref="GetOrAdd"/> keeps tests deterministic. The consequence, worth
/// knowing, is that a pool nobody calls stops evicting — acceptable, because an idle process is not holding
/// anything contended and the next call cleans up before doing anything else.</para>
///
/// <para><b>Nothing is ever disposed</b> — see <see cref="IProviderPool{TProvider}.Retire"/> for why that
/// follows from refusing to break in-flight work.</para></summary>
/// <typeparam name="TProvider">The provider seam being pooled.</typeparam>
/// <param name="options">Bounds. Null = the <see cref="ProviderPoolOptions"/> defaults.</param>
/// <param name="clock">Time source, injected so idle eviction is testable. Null = UTC now.</param>
public sealed class BoundedProviderPool<TProvider>(
    ProviderPoolOptions? options = null, Func<DateTimeOffset>? clock = null) : IProviderPool<TProvider>
    where TProvider : class, IProviderIdentity
{
    private readonly ProviderPoolOptions _options = options ?? new ProviderPoolOptions();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    private readonly Dictionary<ProviderKey, Entry> _entries = [];
    private readonly ConditionalWeakTable<TProvider, StrongBox<ProviderKey>> _keys = [];
    private readonly Lock _lock = new();

    private long _created;
    private long _reused;
    private long _retired;
    private long _ticket;   // monotonic recency stamp: cheaper and clearer than reordering a list

    private sealed class Entry(TProvider instance, DateTimeOffset lastUsed, long ticket)
    {
        public TProvider Instance { get; } = instance;
        public DateTimeOffset LastUsed { get; set; } = lastUsed;
        public long Ticket { get; set; } = ticket;
    }

    /// <inheritdoc/>
    public TProvider GetOrAdd(ProviderKey key, Func<TProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (_lock)
        {
            EvictIdle();

            if (_entries.TryGetValue(key, out var existing))
            {
                Touch(existing);
                _reused++;
                return existing.Instance;
            }
        }

        // Built OUTSIDE the lock would let two callers race to construct; built inside it serializes
        // construction per pool. Construction is cheap (a wrapper over options), and building once is the
        // invariant that matters — two instances would each carry half the history.
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var raced))
            {
                Touch(raced);
                _reused++;
                return raced.Instance;
            }

            var instance = factory() ?? throw new InvalidOperationException(
                $"the factory for '{key}' returned null");
            ProviderPoolGuard.EnsureIdMatchesSlot(instance, key);

            _entries[key] = new Entry(instance, _clock(), ++_ticket);
            _keys.AddOrUpdate(instance, new StrongBox<ProviderKey>(key));
            _created++;

            EvictOverflow();
            return instance;
        }
    }

    /// <inheritdoc/>
    public bool TryGetKey(TProvider instance, out ProviderKey key)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (_keys.TryGetValue(instance, out var box)) { key = box.Value; return true; }
        key = default;
        return false;
    }

    /// <inheritdoc/>
    public bool Retire(ProviderKey key)
    {
        lock (_lock)
        {
            if (!_entries.Remove(key)) return false;
            _retired++;
            return true;
        }
    }

    /// <inheritdoc/>
    public int RetireSlot(string slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        lock (_lock)
        {
            var doomed = _entries.Keys
                .Where(k => string.Equals(k.Slot, slot, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var key in doomed) _entries.Remove(key);
            _retired += doomed.Count;
            return doomed.Count;
        }
    }

    /// <inheritdoc/>
    public ProviderPoolStatistics Statistics
    {
        get { lock (_lock) return new(_entries.Count, _created, _reused, _retired); }
    }

    private void Touch(Entry entry)
    {
        entry.LastUsed = _clock();
        entry.Ticket = ++_ticket;
    }

    /// <summary>Retire entries unused past the timeout. Caller holds the lock.</summary>
    private void EvictIdle()
    {
        if (_options.IdleTimeout is not { } timeout) return;

        var cutoff = _clock() - timeout;
        var doomed = _entries.Where(e => e.Value.LastUsed <= cutoff).Select(e => e.Key).ToList();
        foreach (var key in doomed) _entries.Remove(key);
        _retired += doomed.Count;
    }

    /// <summary>Retire least-recently-used entries down to the cap. Caller holds the lock.</summary>
    private void EvictOverflow()
    {
        if (_options.MaxEntries is not { } max || max <= 0) return;

        while (_entries.Count > max)
        {
            var oldest = _entries.OrderBy(e => e.Value.Ticket).First().Key;
            _entries.Remove(oldest);
            _retired++;
        }
    }
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~BoundedProviderPoolTests"`
Expected: PASS, 14 tests.

- [ ] **Step 5: Sabotage-verify the two invariants that matter**

Temporarily break each, confirm the right test fails, then revert:

1. In `GetOrAdd`, delete the first `_entries.TryGetValue` fast path **and** the raced re-check → expect
   `The_same_key_returns_the_same_instance` and `Concurrent_calls_on_one_key_build_exactly_once` to fail.
2. In `GetOrAdd`, key the dictionary on `key.Slot` instead of `key` → expect
   `Several_configurations_of_one_backend_are_live_simultaneously` and
   `A_changed_key_builds_a_new_instance_and_replaces_the_old` to fail.

A test that does not fail when you break the thing it names is not testing that thing.

- [ ] **Step 6: Commit**

```bash
git add src/Lyntai.Core/Lifecycle/BoundedProviderPool.cs tests/Lyntai.Tests/Lifecycle/BoundedProviderPoolTests.cs
git commit -m "feat(lifecycle): add BoundedProviderPool with LRU and idle eviction"
```

---

### Task 5: `ProviderAdmission` — concurrency limiting keyed on the configuration

**Files:**
- Create: `src/Lyntai.Core/Lifecycle/ProviderAdmission.cs`
- Test: `tests/Lyntai.Tests/Lifecycle/ProviderAdmissionTests.cs`

**Interfaces:**
- Consumes: `ProviderKey` (Task 2).
- Produces: `sealed class ProviderAdmissionOptions { int Default; IDictionary<string,int> BySlot; }` and
  `sealed class ProviderAdmission(ProviderAdmissionOptions? options = null)` with
  `ValueTask<IDisposable> EnterAsync(ProviderKey key, CancellationToken ct = default)`.

**This task ships the mechanism; Task 6 is where it gets applied** (inside each router's attempt path).
Nothing calls `EnterAsync` until then, which is deliberate — the two are separately reviewable, and this
one is pure logic testable with no router at all.

- [ ] **Step 1: Write the failing tests**

Create `tests/Lyntai.Tests/Lifecycle/ProviderAdmissionTests.cs`:

```csharp
using Lyntai.Lifecycle;

namespace Lyntai.Tests.Lifecycle;

public class ProviderAdmissionTests
{
    private static ProviderKey Key(string value, string slot = "local-diffusion") =>
        ProviderKey.For(slot).With("v", value).Build();

    [Fact]
    public async Task With_no_limit_configured_everything_is_admitted_immediately()
    {
        var admission = new ProviderAdmission();
        var held = new List<IDisposable>();

        for (var i = 0; i < 50; i++) held.Add(await admission.EnterAsync(Key("a")));

        Assert.Equal(50, held.Count);
        foreach (var h in held) h.Dispose();
    }

    [Fact]
    public async Task A_slot_limit_bounds_concurrent_entries_for_that_configuration()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);

        var first = await admission.EnterAsync(Key("a"));
        var second = admission.EnterAsync(Key("a"), CancellationToken.None);

        Assert.False(second.IsCompleted);       // blocked behind the first
        first.Dispose();
        (await second).Dispose();               // released, so it completes
    }

    // The whole reason admission is keyed rather than carried on an instance: one tenant must not throttle
    // another, while two consumers of the SAME engine must share its capacity.
    [Fact]
    public async Task Different_configurations_of_one_backend_do_not_block_each_other()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);

        var tenantA = await admission.EnterAsync(Key("tenant-a"));
        var tenantB = admission.EnterAsync(Key("tenant-b"), CancellationToken.None);

        Assert.True(tenantB.IsCompleted);
        tenantA.Dispose();
        (await tenantB).Dispose();
    }

    [Fact]
    public async Task The_same_configuration_shares_capacity_across_callers()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);

        var one = await admission.EnterAsync(Key("shared"));
        var two = admission.EnterAsync(Key("shared"), CancellationToken.None);

        Assert.False(two.IsCompleted);
        one.Dispose();
        (await two).Dispose();
    }

    [Fact]
    public async Task The_default_limit_applies_to_a_slot_with_no_entry()
    {
        var admission = new ProviderAdmission(new ProviderAdmissionOptions { Default = 1 });

        var first = await admission.EnterAsync(Key("a", "unlisted"));
        var second = admission.EnterAsync(Key("a", "unlisted"), CancellationToken.None);

        Assert.False(second.IsCompleted);
        first.Dispose();
        (await second).Dispose();
    }

    [Fact]
    public async Task Releasing_twice_is_harmless()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);

        var handle = await admission.EnterAsync(Key("a"));
        handle.Dispose();
        handle.Dispose();               // must not over-release the semaphore

        var next = admission.EnterAsync(Key("a"), CancellationToken.None);
        Assert.True(next.IsCompleted);
        (await next).Dispose();

        var third = admission.EnterAsync(Key("a"), CancellationToken.None);
        Assert.True(third.IsCompleted);   // still exactly one permit, not two
        (await third).Dispose();
    }

    [Fact]
    public async Task A_waiting_caller_observes_cancellation()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["local-diffusion"] = 1;
        var admission = new ProviderAdmission(options);
        using var cts = new CancellationTokenSource();

        var held = await admission.EnterAsync(Key("a"));
        var waiting = admission.EnterAsync(Key("a"), cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await waiting);
        held.Dispose();
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~ProviderAdmissionTests"`
Expected: compile error — `ProviderAdmission` does not exist.

- [ ] **Step 3: Implement**

Create `src/Lyntai.Core/Lifecycle/ProviderAdmission.cs`:

```csharp
using System.Collections.Concurrent;

namespace Lyntai.Lifecycle;

/// <summary>How many calls may be in flight against one CONFIGURATION at a time.
///
/// <para>Limits are declared per SLOT because capacity is a property of the backend kind ("a local engine
/// renders one image at a time"), and enforced per KEY because the contended resource belongs to a
/// configuration. That split is what produces the behaviour worth having: two consumers pointing at the
/// same self-hosted engine share its capacity, while two tenants on different hosted endpoints never
/// throttle each other.</para>
///
/// <para><b>Why this is not a field on the provider.</b> A semaphore carried by an instance bounds nothing
/// once instances are per-call: under <see cref="TransientProviderPool{TProvider}"/> every call builds its
/// own limiter and every caller is admitted at once, so the engine the limit exists to protect thrashes
/// exactly as it would with no limit configured. Keyed and shared is the only shape that survives both
/// pooling strategies.</para>
///
/// <para>Distinct from rate limiting: a rate limiter bounds calls per unit TIME, this bounds calls at
/// once.</para></summary>
/// <param name="options">Limits. Null = unlimited everywhere.</param>
public sealed class ProviderAdmission(ProviderAdmissionOptions? options = null)
{
    private static readonly IDisposable Unlimited = new NoopHandle();

    private readonly ProviderAdmissionOptions _options = options ?? new ProviderAdmissionOptions();
    private readonly ConcurrentDictionary<ProviderKey, SemaphoreSlim> _gates = new();

    /// <summary>Wait for a permit for this configuration, then return the handle that releases it.
    ///
    /// <para>When the slot's limit is 0 (unlimited) this completes synchronously and returns a no-op
    /// handle — never null, so a call site is one <c>using</c> with no branch.</para></summary>
    /// <param name="key">The configuration being called.</param>
    /// <param name="ct">Cancels the wait.</param>
    public ValueTask<IDisposable> EnterAsync(ProviderKey key, CancellationToken ct = default)
    {
        var limit = LimitFor(key.Slot);
        if (limit <= 0) return ValueTask.FromResult(Unlimited);

        var gate = _gates.GetOrAdd(key, _ => new SemaphoreSlim(limit, limit));
        var wait = gate.WaitAsync(ct);
        return wait.IsCompletedSuccessfully
            ? ValueTask.FromResult<IDisposable>(new Handle(gate))
            : Awaited(wait, gate);

        static async ValueTask<IDisposable> Awaited(Task wait, SemaphoreSlim gate)
        {
            await wait.ConfigureAwait(false);
            return new Handle(gate);
        }
    }

    private int LimitFor(string slot) =>
        _options.BySlot.TryGetValue(slot, out var limit) ? limit : _options.Default;

    /// <summary>Releases exactly once, however many times it is disposed — a double dispose would otherwise
    /// hand out a permit that was never taken, quietly raising the limit for the life of the process.</summary>
    private sealed class Handle(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }

    private sealed class NoopHandle : IDisposable
    {
        public void Dispose() { }
    }
}

/// <summary>Concurrency limits per backend. See <see cref="ProviderAdmission"/> for the slot/key split.</summary>
public sealed class ProviderAdmissionOptions
{
    /// <summary>Concurrent calls allowed per configuration when the slot has no entry in
    /// <see cref="BySlot"/>. 0 = unlimited, which is the default: the HTTP backends are stateless and a
    /// limit would only ever slow them down.</summary>
    public int Default { get; set; }

    /// <summary>Per-slot limits — the one that matters in practice is a locally-run engine, where several
    /// simultaneous renders contend for one CPU or GPU.</summary>
    public IDictionary<string, int> BySlot { get; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~ProviderAdmissionTests"`
Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Lyntai.Core/Lifecycle/ProviderAdmission.cs tests/Lyntai.Tests/Lifecycle/ProviderAdmissionTests.cs
git commit -m "feat(lifecycle): key concurrency admission on the configuration, not the instance"
```

---

### Task 6: Both routers learn the configuration of a provider, and apply cooldown + admission from it

**Files:**
- Modify: `src/Lyntai.Core/Generation/Routing/GenerationRouter.cs:30` (constructor), `:129` (`AttemptAsync`), `:145` (`IsBenched`), `:152` (`CooldownKey`), and the call sites at `:49`, `:55`, `:70`, `:73`, `:101`, `:105`, `:111`, `:117`
- Modify: `src/Lyntai.Core/Llm/Routing/LlmRouter.cs:12` (constructor), `:257`, `:261` (`TryCompleteAsync`), `:306`, `:320`
- Test: `tests/Lyntai.Tests/Lifecycle/RouterCooldownKeyTests.cs`

**Interfaces:**
- Consumes: `ProviderKey` (Task 2), `ProviderAdmission` (Task 5).
- Produces: `GenerationRouter(IEnumerable<IGenerationProvider> providers, GenerationRoutingPolicy? policy = null, DeadHostTracker? deadHosts = null, Func<IGenerationProvider, ProviderKey?>? configuration = null, ProviderAdmission? admission = null)`
  and `LlmRouter(IEnumerable<ILlmProvider> providers, DeadHostTracker deadHosts, LyntaiOptions options, ILogger<LlmRouter>? logger = null, IModelRoutingStore? modelRouting = null, Func<ILlmProvider, ProviderKey?>? configuration = null, ProviderAdmission? admission = null)`.

**Why one delegate for two jobs.** Cooldown attribution and admission both need the same answer — "which
configuration is this provider?" — so they share `Func<TProvider, ProviderKey?>`. Returning null (the
default, and the case for any container-composed provider) keeps today's `p => p.Id` cooldown key and skips
admission entirely.

**Why the router applies admission rather than a decorator wrapping the provider.** A wrapper implementing
only `IGenerationProvider` **erases the optional capability interfaces** the generation seam is built on:
`GenerationRouter.SubmitAsync` type-tests `if (provider is not IGenerationJobProvider job) continue;`
(`:100`), so a decorated `FalQueueProvider` silently stops accepting queued video renders while every
inline image render keeps working — and every test that only covers inline generation stays green. Entering
admission inside the router's own attempt path touches no provider type at all.

**Note on parameter position:** the new parameters go **last** on both, after every existing optional one.
Anything else silently changes the meaning of positional arguments at existing call sites.

- [ ] **Step 1: Write the failing tests**

Create `tests/Lyntai.Tests/Lifecycle/RouterCooldownKeyTests.cs`:

```csharp
using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Lifecycle;

public class RouterCooldownKeyTests
{
    private static GenerationRequest Request() => new("image", "a cat");

    private static List<GenerationCandidate> Candidates(params string[] ids) =>
        [.. ids.Select(id => new GenerationCandidate(id))];

    // Default behaviour must be untouched: an app that never supplies a delegate benches by provider id,
    // exactly as it does today.
    [Fact]
    public async Task Without_a_delegate_the_cooldown_key_is_still_the_provider_id()
    {
        var tracker = new DeadHostTracker(threshold: 1);
        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);
        var spare = new FakeGenerationProvider { Id = "comfyui" };
        var router = new GenerationRouter([failing, spare], null, tracker);

        await router.GenerateAsync(Candidates("a1111", "comfyui"), Request());

        Assert.True(tracker.IsDead("generation::a1111"));
    }

    // Two instances of the SAME backend id on different configurations must bench independently —
    // otherwise one tenant's exhausted quota takes out every other tenant.
    [Fact]
    public async Task A_configuration_delegate_separates_two_configurations_of_one_backend_id()
    {
        var tracker = new DeadHostTracker(threshold: 1);
        var cfgA = ProviderKey.For("openai-images").With("tenant", "a").Build();
        var cfgB = ProviderKey.For("openai-images").With("tenant", "b").Build();

        var tenantA = new FakeGenerationProvider { Id = "openai-images" };
        tenantA.Verdicts.Enqueue(GenerationVerdict.RateLimited);

        var router = new GenerationRouter([tenantA], null, tracker, _ => cfgA);
        await router.GenerateAsync(Candidates("openai-images"), Request());

        Assert.True(tracker.IsDead($"generation::{cfgA}"));
        Assert.False(tracker.IsDead($"generation::{cfgB}"));
        Assert.False(tracker.IsDead("generation::openai-images"));
    }

    // A null return is what a container-composed provider yields, and must mean "behave as before".
    [Fact]
    public async Task A_delegate_returning_null_falls_back_to_the_provider_id()
    {
        var tracker = new DeadHostTracker(threshold: 1);
        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.RateLimited);

        var router = new GenerationRouter([failing], null, tracker, _ => null);
        await router.GenerateAsync(Candidates("a1111"), Request());

        Assert.True(tracker.IsDead("generation::a1111"));
    }

    // The rev-2 regression: the limit must bound calls even though instances may be per-call.
    [Fact]
    public async Task Admission_bounds_concurrent_attempts_for_one_configuration()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["a1111"] = 1;
        var admission = new ProviderAdmission(options);
        var key = ProviderKey.For("a1111").With("v", "a").Build();

        var backend = new BlockingGenerationProvider { Id = "a1111" };
        var router = new GenerationRouter([backend], null, new DeadHostTracker(), _ => key, admission);

        var first = router.GenerateAsync(Candidates("a1111"), Request());
        await backend.Entered.Task;                       // first attempt is inside the provider
        var second = router.GenerateAsync(Candidates("a1111"), Request());

        Assert.False(second.IsCompleted);                 // held at the gate, not at the backend
        Assert.Equal(1, backend.Concurrent);

        backend.Release();
        await first;
        await second;
    }

    [Fact]
    public async Task The_llm_router_honours_the_delegate_too()
    {
        var tracker = new DeadHostTracker(threshold: 1);
        var provider = new FakeLlmProvider("openai");
        provider.Replies.Enqueue(new LlmReply("nope", LlmVerdict.RateLimited));
        var cfg = ProviderKey.For("openai").With("tenant", "a").Build();

        var router = new LlmRouter([provider], tracker, new LyntaiOptions(), configuration: _ => cfg);

        await router.CompleteAsync([new LlmCandidate("openai")],
            new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.True(tracker.IsDead(cfg.ToString()));
        Assert.False(tracker.IsDead("openai"));
    }

    /// <summary>Blocks inside GenerateAsync so a second caller can be observed queueing at the gate.</summary>
    private sealed class BlockingGenerationProvider : IGenerationProvider
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _concurrent;

        public string Id { get; init; } = "a1111";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Concurrent => Volatile.Read(ref _concurrent);

        public GenerationCapabilities Capabilities { get; } = new()
        {
            Kinds = [GenerationKinds.Image],
            Deliveries = [GenerationDelivery.Inline],
        };

        public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new GenerationProbeResult(true, "ready"));

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _concurrent);
            Entered.TrySetResult();
            await _gate.Task.ConfigureAwait(false);
            Interlocked.Decrement(ref _concurrent);
            return GenerationResult.Success([new GenerationArtifact("image/png", Data: [0x89])]);
        }

        public void Release() => _gate.TrySetResult();
    }
}
```

Add `using Lyntai;` (for `LyntaiOptions`), `using Lyntai.Lifecycle;`, and `using Lyntai.Llm;` (for
`LlmReply`, `LlmVerdict`, `LlmRequest`, `LlmMessage`, `LlmCandidate`) to the file's usings.
`LlmRequest` has a `required IReadOnlyList<LlmMessage> Messages`, hence the object-initializer form.

- [ ] **Step 2: Run the tests and watch them fail**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~RouterCooldownKeyTests"`
Expected: compile error — no such constructor overload.

- [ ] **Step 3: Change `GenerationRouter`**

Add both parameters to the primary constructor (last positions) with these docs:

```csharp
/// <param name="configuration">Which CONFIGURATION a provider is running under, used to key dead-host
/// cooldown and admission. Null (or a null return) = key cooldown on
/// <see cref="IGenerationProvider.Id"/> and apply no admission, which is the historical behaviour and
/// correct for a single-configuration deployment. Supply one when several configurations of a backend id
/// are live at once — otherwise one tenant's rate limit benches every other tenant sharing that backend,
/// and two consumers of one downed self-hosted host fail to share a bench that would have spared them
/// both. <c>IProviderPool.TryGetKey</c> is the intended source.</param>
/// <param name="admission">Bounds concurrent attempts per configuration — for a locally-run engine where
/// simultaneous renders contend for one CPU or GPU. Null = unbounded. Applied HERE rather than by
/// wrapping a provider, because a wrapper implementing only <see cref="IGenerationProvider"/> erases the
/// optional capability interfaces (<see cref="IGenerationJobProvider"/>) this router type-tests, which
/// would silently stop every queued render from routing.</param>
```

Then:

- Add fields resolved once rather than per call:
  ```csharp
  private readonly Func<IGenerationProvider, ProviderKey?> _configuration = configuration ?? (_ => null);
  ```
- Change `CooldownKey` from `private static string CooldownKey(string providerId)` to:
  ```csharp
  private string CooldownKey(IGenerationProvider provider) =>
      $"generation::{_configuration(provider)?.ToString() ?? provider.Id}";
  ```
- Change `IsBenched(string providerId, int capableCount)` to `IsBenched(IGenerationProvider provider, int capableCount)`, calling `CooldownKey(provider)`.
- Update every call site to pass `provider` instead of `provider.Id`: lines 49, 55, 70, 73, 101, 111, 117.
- Make `AttemptAsync` an instance method (it is `private static` today) and enter admission around the
  provider call. `SubmitAsync`'s attempt at line 105 gets the same treatment — submitting is what commits
  the money, so it must respect the same bound:
  ```csharp
  private async Task<GenerationResult> AttemptAsync(
      IGenerationProvider provider, GenerationRequest request, CancellationToken ct)
  {
      // no admission configured, or no configuration known for this provider → no gate at all
      var key = _configuration(provider);
      using var permit = admission is not null && key is { } k
          ? await admission.EnterAsync(k, ct).ConfigureAwait(false)
          : null;

      var started = Stopwatch.GetTimestamp();
      // ... the existing body, unchanged
  }
  ```

- [ ] **Step 4: Change `LlmRouter`**

Add the same two parameters last, with the equivalent doc comments. Then:

- Add `private readonly Func<ILlmProvider, ProviderKey?> _configuration = configuration ?? (_ => null);`
- Change `private string CooldownKey(string providerId, string? effectiveModel)` to take
  `ILlmProvider provider` and use `_configuration(provider)?.ToString() ?? provider.Id` where `providerId`
  was used. The `CooldownScope.ProviderAndModel` branch keeps appending the model — the two concerns
  compose, because granularity by model is orthogonal to identity by configuration.
- Update the call site at line 257 (`yield return (provider, effectiveModel, CooldownKey(provider, effectiveModel));`)
  and at line 320 inside `SelectLive` (`deadHosts.IsDead(CooldownKey(provider, effectiveModel))`).
- Enter admission in `TryCompleteAsync` (line 261) exactly as in Step 3. **Do not gate `StreamAsync`'s
  attempt**: a stream holds the permit for the whole response, and pairing that with the router's
  no-fallback-after-first-token rule risks holding a permit across a consumer that stops enumerating. Note
  this in the `admission` parameter's doc comment so the asymmetry is deliberate and visible.

- [ ] **Step 5: Run the tests and watch them pass**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~RouterCooldownKeyTests"`
Expected: PASS, 5 tests.

- [ ] **Step 6: Prove nothing regressed, then update the baseline**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~GenerationRouterTests|FullyQualifiedName~LlmRouter"`
Expected: PASS — every existing router test, unchanged.

Then re-seed `tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt` as in Task 1 Step 6. The diff must show
**only** the two added optional constructor parameters.

- [ ] **Step 7: Commit**

```bash
git add src/Lyntai.Core/Generation/Routing/GenerationRouter.cs src/Lyntai.Core/Llm/Routing/LlmRouter.cs tests/Lyntai.Tests/Lifecycle/RouterCooldownKeyTests.cs tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt
git commit -m "feat(routing): let both routers key cooldown on a configuration rather than a provider id"
```

---

### Task 7: `ProviderRegistration` and the router factories

**Files:**
- Create: `src/Lyntai.Core/Lifecycle/ProviderRegistration.cs`
- Create: `src/Lyntai.Core/Generation/Routing/GenerationRouterFactory.cs`
- Create: `src/Lyntai.Core/Llm/Routing/LlmRouterFactory.cs`
- Modify: `src/Lyntai.Core/Generation/GenerationBuilderExtensions.cs:90-110` (`EnsureRouter`)
- Test: `tests/Lyntai.Tests/Lifecycle/RouterFactoryTests.cs`

**Interfaces:**
- Consumes: `IProviderPool<TProvider>`, `ProviderAdmission`, the Task 6 constructors.
- Produces: `readonly record struct ProviderRegistration<TProvider>(ProviderKey Key, Func<TProvider> Create)`;
  `IGenerationRouterFactory` with `For(IReadOnlyList<ProviderRegistration<IGenerationProvider>>)` and
  `For(IReadOnlyList<IGenerationProvider>)`, both returning `IGenerationRouter`; `ILlmRouterFactory` with
  the same two overloads returning `ILlmRouter`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Lyntai.Tests/Lifecycle/RouterFactoryTests.cs`:

```csharp
using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Lifecycle;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Lifecycle;

public class RouterFactoryTests
{
    private static GenerationRequest Request() => new("image", "a cat");

    private static ProviderKey Key(string value, string slot = "a1111") =>
        ProviderKey.For(slot).With("v", value).Build();

    // every remaining constructor parameter is optional: no policy, no governance, no admission
    private static GenerationRouterFactory Factory(
        IProviderPool<IGenerationProvider> pool, DeadHostTracker? tracker = null) =>
        new(pool, tracker ?? new DeadHostTracker());

    [Fact]
    public async Task The_pooled_overload_routes_over_the_pooled_instance()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var factory = Factory(pool);
        var backend = new FakeGenerationProvider { Id = "a1111" };

        var router = factory.For([new ProviderRegistration<IGenerationProvider>(Key("a"), () => backend)]);
        var result = await router.GenerateAsync([new GenerationCandidate("a1111")], Request());

        Assert.True(result.IsOk);
        Assert.Equal(1, backend.GenerateCalls);
    }

    [Fact]
    public void The_pooled_overload_reuses_across_calls()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var factory = Factory(pool);
        var built = 0;

        for (var i = 0; i < 3; i++)
            factory.For([new ProviderRegistration<IGenerationProvider>(Key("a"), () =>
            {
                built++;
                return new FakeGenerationProvider { Id = "a1111" };
            })]);

        Assert.Equal(1, built);
    }

    // The regression this whole task exists for: rebuilding the router per call must NOT reset the bench.
    [Fact]
    public async Task Cooldown_survives_a_router_rebuilt_on_every_call()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var tracker = new DeadHostTracker(threshold: 2);
        var factory = Factory(pool, tracker);

        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);

        for (var i = 0; i < 2; i++)
        {
            var router = factory.For([new ProviderRegistration<IGenerationProvider>(Key("a"), () => failing)]);
            await router.GenerateAsync([new GenerationCandidate("a1111")], Request());
        }

        Assert.True(tracker.IsDead($"generation::{Key("a")}"));
    }

    // Two configurations of one id must bench independently.
    [Fact]
    public async Task Two_configurations_of_one_backend_bench_independently()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var tracker = new DeadHostTracker(threshold: 1);
        var factory = Factory(pool, tracker);

        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.RateLimited);

        var router = factory.For([new ProviderRegistration<IGenerationProvider>(Key("cfg-a"), () => failing)]);
        await router.GenerateAsync([new GenerationCandidate("a1111")], Request());

        Assert.True(tracker.IsDead($"generation::{Key("cfg-a")}"));
        Assert.False(tracker.IsDead($"generation::{Key("cfg-b")}"));
    }

    // The container-composed path keeps today's behaviour exactly: keyed on the id, no pool involved.
    [Fact]
    public async Task The_instance_overload_keys_cooldown_on_the_provider_id()
    {
        var pool = new BoundedProviderPool<IGenerationProvider>();
        var tracker = new DeadHostTracker(threshold: 1);
        var factory = Factory(pool, tracker);

        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.RateLimited);

        var router = factory.For([(IGenerationProvider)failing]);
        await router.GenerateAsync([new GenerationCandidate("a1111")], Request());

        Assert.True(tracker.IsDead("generation::a1111"));
        Assert.Equal(0, pool.Statistics.Created);
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~RouterFactoryTests"`
Expected: compile error — `ProviderRegistration` / `GenerationRouterFactory` do not exist.

- [ ] **Step 3: Add `ProviderRegistration`**

Create `src/Lyntai.Core/Lifecycle/ProviderRegistration.cs`:

```csharp
namespace Lyntai.Lifecycle;

/// <summary>One backend a caller wants routed: the CONFIGURATION it should run under, and how to build it
/// if the pool has nothing to reuse.
///
/// <para>A pair rather than a constructed instance, because construction is the pool's decision — that is
/// what lets the same call site reuse or rebuild depending only on which strategy is registered.</para></summary>
/// <typeparam name="TProvider">The provider seam.</typeparam>
/// <param name="Key">The configuration. See <see cref="ProviderKeyBuilder"/> for what belongs in it.</param>
/// <param name="Create">Builds the backend. Invoked only when the pool has no instance for
/// <paramref name="Key"/>.</param>
public readonly record struct ProviderRegistration<TProvider>(ProviderKey Key, Func<TProvider> Create)
    where TProvider : class, IProviderIdentity;
```

- [ ] **Step 4: Add the generation factory**

Create `src/Lyntai.Core/Generation/Routing/GenerationRouterFactory.cs`. It must reproduce the decorator
order in `GenerationBuilderExtensions.EnsureRouter` exactly — rate limit **inside** budget, so a call
refused for spend never consumes a permit:

```csharp
using Lyntai.Lifecycle;
using Lyntai.Llm.Budgeting;
using Lyntai.Llm.RateLimiting;
using Lyntai.Llm.Routing;
using Microsoft.Extensions.Logging;

namespace Lyntai.Generation.Routing;

/// <summary>Builds a fully-governed <see cref="IGenerationRouter"/> over a provider set the CALLER chooses.
///
/// <para>Needed because a router snapshots its provider set at construction, and once several
/// configurations are live only the caller knows which ones are its own. Building a router per call is
/// cheap; what must NOT be rebuilt is the bookkeeping, so the tracker, the limiter and the usage ledger are
/// injected once and shared by every router this factory hands out. That is precisely what per-call
/// hand-construction gets wrong.</para></summary>
public interface IGenerationRouterFactory
{
    /// <summary>Route over POOLED backends: each registration is resolved through the pool, and each
    /// provider's dead-host cooldown is keyed on its <see cref="ProviderKey"/> — so one tenant's rate
    /// limit never benches another's.</summary>
    IGenerationRouter For(IReadOnlyList<ProviderRegistration<IGenerationProvider>> providers);

    /// <summary>Route over already-constructed backends — the container-composed path. No pool is
    /// involved and cooldown stays keyed on the provider id, exactly as it was before pooling existed.</summary>
    IGenerationRouter For(IReadOnlyList<IGenerationProvider> providers);
}

/// <inheritdoc cref="IGenerationRouterFactory"/>
public sealed class GenerationRouterFactory(
    IProviderPool<IGenerationProvider> pool,
    DeadHostTracker deadHosts,
    GenerationRoutingPolicy? policy = null,
    IRateLimiter? rateLimiter = null,
    IUsageTracker? usage = null,
    LyntaiOptions? options = null,
    ILoggerFactory? loggers = null,
    ProviderAdmission? admission = null) : IGenerationRouterFactory
{
    /// <inheritdoc/>
    public IGenerationRouter For(IReadOnlyList<ProviderRegistration<IGenerationProvider>> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        var instances = new List<IGenerationProvider>(providers.Count);
        foreach (var registration in providers)
            instances.Add(pool.GetOrAdd(registration.Key, registration.Create));

        return Compose(new GenerationRouter(instances, policy, deadHosts,
            p => pool.TryGetKey(p, out var key) ? key : null, admission));
    }

    /// <inheritdoc/>
    public IGenerationRouter For(IReadOnlyList<IGenerationProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        return Compose(new GenerationRouter(providers, policy, deadHosts));
    }

    /// <summary>Governance, in the order the LLM front door uses: the limiter sits INSIDE the budget, so a
    /// call refused for spend never spends a permit.</summary>
    private IGenerationRouter Compose(IGenerationRouter router)
    {
        if (rateLimiter is not null)
            router = new RateLimitedGenerationRouter(router, rateLimiter,
                loggers?.CreateLogger<RateLimitedGenerationRouter>());

        if (usage is not null && options is not null)
            router = new BudgetedGenerationRouter(router, usage, options,
                loggers?.CreateLogger<BudgetedGenerationRouter>());

        return router;
    }
}
```

Both decorator signatures above are verified against the source:
`RateLimitedGenerationRouter(IGenerationRouter inner, IRateLimiter limiter, ILogger<RateLimitedGenerationRouter>? logger = null)`
and `BudgetedGenerationRouter(IGenerationRouter inner, IUsageTracker tracker, LyntaiOptions options, ILogger<BudgetedGenerationRouter>? logger = null)`.

- [ ] **Step 5: Add the LLM factory**

Create `src/Lyntai.Core/Llm/Routing/LlmRouterFactory.cs` with the same shape: `ILlmRouterFactory` with the
two `For` overloads, and `LlmRouterFactory(IProviderPool<ILlmProvider> pool, DeadHostTracker deadHosts,
LyntaiOptions options, ILoggerFactory? loggers = null, IModelRoutingStore? modelRouting = null)`. The LLM
side has no router-level governance decorators (its governance lives on the `ILlmClient` front door), so
there is no `Compose` step — construct `LlmRouter` and return it.

- [ ] **Step 6: Rewire `EnsureRouter` to go through the factory**

In `src/Lyntai.Core/Generation/GenerationBuilderExtensions.cs`, replace the body of the
`TryAddSingleton<IGenerationRouter>` factory with a call to `IGenerationRouterFactory.For(instances)`, and
register `IGenerationRouterFactory` itself with `TryAddSingleton`. One composition path, not two.

Register the pool as an open generic in the same method so the factory can be constructed:

```csharp
builder.Services.TryAddSingleton(typeof(IProviderPool<>), typeof(BoundedProviderPool<>));
builder.Services.TryAddSingleton<DeadHostTracker>();
```

- [ ] **Step 7: Run the tests and watch them pass**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~RouterFactoryTests"`
Expected: PASS, 5 tests.

- [ ] **Step 8: Prove the container path is unchanged**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~GenerationDiTests|FullyQualifiedName~GenerationGovernanceTests|FullyQualifiedName~GenerationProviderWiringTests"`
Expected: PASS — unchanged. These are the tests that would catch a decorator-order regression.

- [ ] **Step 9: Update the baseline and commit**

Re-seed `Lyntai.Core.txt` as in Task 1 Step 6, then:

```bash
git add src/Lyntai.Core/Lifecycle/ProviderRegistration.cs src/Lyntai.Core/Generation/Routing/GenerationRouterFactory.cs src/Lyntai.Core/Llm/Routing/LlmRouterFactory.cs src/Lyntai.Core/Generation/GenerationBuilderExtensions.cs tests/Lyntai.Tests/Lifecycle/RouterFactoryTests.cs tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt
git commit -m "feat(routing): add router factories that compose governance over a caller-chosen provider set"
```

---

### Task 8: DI wiring and the strategy swap

**Files:**
- Create: `src/Lyntai.Core/Lifecycle/ProviderPoolBuilderExtensions.cs`
- Test: `tests/Lyntai.Tests/Lifecycle/ProviderPoolWiringTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: on `LyntaiBuilder` — `UseProviderPool(ProviderPoolOptions? options = null)`,
  `UseTransientProviders()`, `ConfigureProviderAdmission(Action<ProviderAdmissionOptions> configure)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/Lyntai.Tests/Lifecycle/ProviderPoolWiringTests.cs`:

```csharp
using Lyntai.Generation;
using Lyntai.Lifecycle;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Lifecycle;

public class ProviderPoolWiringTests
{
    private static ProviderKey Key(string value) => ProviderKey.For("a1111").With("v", value).Build();

    private static IProviderPool<IGenerationProvider> PoolFrom(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLyntai(configure);
        return services.BuildServiceProvider().GetRequiredService<IProviderPool<IGenerationProvider>>();
    }

    [Fact]
    public void The_default_pool_reuses()
    {
        var pool = PoolFrom(_ => { });
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });
        var second = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });

        Assert.Same(first, second);
    }

    // The point of the seam: the SAME call site changes behaviour purely by registration.
    [Fact]
    public void UseTransientProviders_switches_the_strategy_with_no_call_site_change()
    {
        var pool = PoolFrom(b => b.UseTransientProviders());
        var first = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });
        var second = pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });

        Assert.NotSame(first, second);
    }

    [Fact]
    public void UseProviderPool_carries_its_bounds()
    {
        var pool = PoolFrom(b => b.UseProviderPool(new ProviderPoolOptions { MaxEntries = 1 }));
        pool.GetOrAdd(Key("a"), () => new FakeGenerationProvider { Id = "a1111" });
        pool.GetOrAdd(Key("b"), () => new FakeGenerationProvider { Id = "a1111" });

        Assert.Equal(1, pool.Statistics.Live);
    }

    [Fact]
    public void The_pool_is_a_singleton_so_reuse_spans_resolutions()
    {
        var services = new ServiceCollection();
        services.AddLyntai(_ => { });
        var sp = services.BuildServiceProvider();

        Assert.Same(sp.GetRequiredService<IProviderPool<IGenerationProvider>>(),
                    sp.GetRequiredService<IProviderPool<IGenerationProvider>>());
    }

    [Fact]
    public void A_host_registered_pool_wins()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IProviderPool<IGenerationProvider>>(new TransientProviderPool<IGenerationProvider>());
        services.AddLyntai(_ => { });

        var pool = services.BuildServiceProvider().GetRequiredService<IProviderPool<IGenerationProvider>>();
        Assert.IsType<TransientProviderPool<IGenerationProvider>>(pool);
    }

    [Fact]
    public void Admission_options_are_configurable_and_resolvable()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.ConfigureProviderAdmission(o => o.BySlot["local-diffusion"] = 1));
        var admission = services.BuildServiceProvider().GetRequiredService<ProviderAdmission>();

        Assert.NotNull(admission);
    }
}
```

- [ ] **Step 2: Run the tests and watch them fail**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~ProviderPoolWiringTests"`
Expected: compile error — `UseTransientProviders` does not exist.

- [ ] **Step 3: Implement the builder extensions**

Create `src/Lyntai.Core/Lifecycle/ProviderPoolBuilderExtensions.cs`. Note the namespace is `Lyntai`, so
the methods appear on the builder alongside the other `Use*` methods:

```csharp
using Lyntai.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Lives in the Lyntai namespace so the Use*/Configure* methods appear on the builder.
namespace Lyntai;

/// <summary>Wiring for provider lifetime. Relevant only when backend configuration is owned OUTSIDE the
/// deployment — by an end user, or a store the process polls. A deployment-configured app registers
/// backends with <c>Add*</c> and never touches any of this.</summary>
public static class ProviderPoolBuilderExtensions
{
    /// <summary>Reuse a backend instance while its configuration is unchanged, within the given bounds.
    /// This is already the default; call it to set <see cref="ProviderPoolOptions"/> explicitly.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="options">Bounds. Null = the defaults (64 entries, 10-minute idle timeout).</param>
    public static LyntaiBuilder UseProviderPool(this LyntaiBuilder builder, ProviderPoolOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(options ?? new ProviderPoolOptions());
        builder.Services.RemoveAll(typeof(IProviderPool<>));
        builder.Services.AddSingleton(typeof(IProviderPool<>), typeof(BoundedProviderPool<>));
        return builder;
    }

    /// <summary>Build a FRESH backend instance on every call — for a host that wants its configuration
    /// re-read and applied each time.
    ///
    /// <para>Call sites do not change: the same <c>GetOrAdd</c> (and the same router factory above it)
    /// simply stops reusing. Dead-host cooldown and admission keep working, because both are keyed on the
    /// configuration rather than on the instance.</para></summary>
    public static LyntaiBuilder UseTransientProviders(this LyntaiBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.RemoveAll(typeof(IProviderPool<>));
        builder.Services.AddSingleton(typeof(IProviderPool<>), typeof(TransientProviderPool<>));
        return builder;
    }

    /// <summary>Bound how many calls may run against ONE configuration at a time — for a locally-run engine
    /// where simultaneous renders contend for a CPU or GPU. Unlimited by default.</summary>
    public static LyntaiBuilder ConfigureProviderAdmission(
        this LyntaiBuilder builder, Action<ProviderAdmissionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = builder.Services
            .FirstOrDefault(d => d.ServiceType == typeof(ProviderAdmissionOptions))
            ?.ImplementationInstance as ProviderAdmissionOptions;

        if (options is null)
        {
            options = new ProviderAdmissionOptions();
            builder.Services.AddSingleton(options);
        }

        configure(options);
        builder.Services.TryAddSingleton(sp => new ProviderAdmission(
            sp.GetRequiredService<ProviderAdmissionOptions>()));
        return builder;
    }
}
```

`AddLyntai` must also register the defaults so they resolve without any opt-in. Add to
`ServiceCollectionExtensions.AddLyntai` (or to `EnsureRouter` from Task 7, whichever already runs
unconditionally):

```csharp
services.TryAddSingleton(typeof(IProviderPool<>), typeof(BoundedProviderPool<>));
services.TryAddSingleton<ProviderPoolOptions>();
services.TryAddSingleton<ProviderAdmissionOptions>();
services.TryAddSingleton<ProviderAdmission>();
```

- [ ] **Step 4: Run the tests and watch them pass**

Run: `node devtools/dev.mjs test -- --filter "FullyQualifiedName~ProviderPoolWiringTests"`
Expected: PASS, 6 tests.

- [ ] **Step 5: Run the whole gate**

Run: `node devtools/dev.mjs verify`
Expected: build clean (no warnings), all tests pass, e2e 3/3, leak scan clean.

Fix the baseline as in Task 1 Step 6 if `ApiSurfaceTests` fails, reading the diff before accepting it.

- [ ] **Step 6: Commit**

```bash
git add src/Lyntai.Core/Lifecycle/ProviderPoolBuilderExtensions.cs src/Lyntai.Core/DependencyInjection/ServiceCollectionExtensions.cs tests/Lyntai.Tests/Lifecycle/ProviderPoolWiringTests.cs tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt
git commit -m "feat(lifecycle): wire the pool strategies and admission into AddLyntai"
```

---

### Task 9: Documentation and backlog

**Files:**
- Modify: `docs/DECISIONS.md` (append a new numbered entry)
- Modify: `.claude/knowledge/pitfalls.md`
- Modify: `.claude/knowledge/llm-and-router.md`
- Modify: `README.md`
- Modify: `CHANGELOG.md` (under `## Unreleased`)
- Modify: `TASKS.md` (remove GEN12)
- Modify: `docs/task-archive.md` (append GEN12 with its outcome)

- [ ] **Step 1: Add the decisions entry**

Append to `docs/DECISIONS.md` with the next free number (check the file — the last is D36). Record three
things, each with its reason:

1. **Pooling is a registered strategy, not a library behaviour.** `Bounded` reuses, `Transient` does not,
   and the interface is the BYO seam. The call site is identical under either, which is what makes the
   choice reversible.
2. **A pool selects what was chosen; a router selects what is healthy.** A pool must not fall back — "the
   user chose this configuration" has to fail rather than silently succeed against another and bill that
   credential.
3. **Retirement never disposes, and that is an entailment rather than a preference.** You cannot both
   refuse to break in-flight work and dispose deterministically without leases; a lease-based
   `IProviderPool` is the documented escape hatch.

- [ ] **Step 2: Add the traps to `pitfalls.md`**

Add the five from spec §7, each stated as "what looks right / what actually happens":

1. Disposing a replaced instance aborts in-flight work.
2. Cooldown keyed on the provider id benches other tenants.
3. A per-instance concurrency semaphore admits everyone under a no-reuse strategy.
4. A key derived from an options object is correct for the record-shaped options types and silently wrong
   for `LocalDiffusionOptions` (a class, reference equality) and `ComfyUiOptions.Kinds` (a
   reference-compared list).
5. **Decorating a provider erases its optional capability interfaces** — the one with the widest blast
   radius, because it applies to any future wrapper (telemetry, retries, redaction), not just this work.
   A wrapper implementing only `IGenerationProvider` makes a queue backend invisible to
   `GenerationRouter.SubmitAsync`'s type test, so video renders stop routing while image renders and every
   inline-only test keep passing.

- [ ] **Step 3: Update `llm-and-router.md`**

The cooldown key is no longer necessarily the provider id. Document the new optional `configuration`
delegate, its `p => p.Id` default, and when a host wants something narrower — plus the deliberate asymmetry
that admission gates completions but not streams.

- [ ] **Step 4: Update `README.md` and `CHANGELOG.md`**

README: a short section under the generation/routing material showing the per-request call site and the two
registrations from spec §5. CHANGELOG: an entry under `## Unreleased` — **never** hand-stamp a version
heading; the release workflow does that.

- [ ] **Step 5: Archive the task**

Use the `archive-task` skill. Cut GEN12 from `TASKS.md`, append it to `docs/task-archive.md` under the
right heading with the completion date and a one-line outcome, preserving the original wording. Note in the
outcome that the task's premise about where cooldown state lives was corrected during design.

- [ ] **Step 6: Final verification**

Run: `node devtools/dev.mjs verify`
Then: `node devtools/dev.mjs consumer-smoke` — this touched no packaging, but it is the only check that
exercises what actually ships, and the surface grew.

- [ ] **Step 7: Commit**

```bash
git add docs/DECISIONS.md .claude/knowledge/pitfalls.md .claude/knowledge/llm-and-router.md README.md CHANGELOG.md TASKS.md docs/task-archive.md
git commit -m "docs(lifecycle): record the pool decisions, its traps, and archive GEN12"
```

---

## Notes for the implementer

**The generic type argument matters and is easy to get wrong.** Register and resolve
`IProviderPool<IGenerationProvider>` — the *seam* — not `IProviderPool<FakeGenerationProvider>` or
`IProviderPool<OpenAiImageProvider>`. A pool keyed on a concrete type is a different pool, so instances
registered through one are invisible to a router resolving the other. The tests in Task 3 and 4 use
concrete types deliberately because they test the pool in isolation; everything wired through DI uses the
seam.

**`ProviderKey.ToString()` is used as the cooldown key** in `GenerationRouterFactory`. It truncates the
fingerprint to 12 hex characters for legibility in logs. That is 48 bits — ample against accidental
collision across the handful of configurations one process holds, and it is not a security boundary. If
that ever feels tight, widen it there rather than making `ToString` unreadable.

**Do not add a background timer.** Idle eviction is evaluated on access, deliberately (spec §4.5). A
library that starts a thread is a library that has to be shut down, and every test then needs to control
it.
