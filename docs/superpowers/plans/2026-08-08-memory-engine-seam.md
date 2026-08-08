# Memory engine seam (MEM1) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for
> tracking.

**Goal:** Ship `IMemoryEngine` — named, composable memory systems resolved by name — so an application can
run several memories at once, blend existing stores, and protect exact facts from being crowded out by
associative recall.

**Architecture:** One interface in `Lyntai.Memory` (inside `Lyntai.Core`), registered as a DI collection and
keyed by `Name` through an `IHttpClientFactory`-shaped factory — the same variation-point shape as
`ILlmProvider` keyed by `Id` and picked by `ILlmRouter`. Thin wrappers adapt the three existing stores; a
composite over member engines is itself an engine, so blending needs no second concept. Composition renders
authoritative and associative material as separate sections with a reserved budget for the former.

**Tech Stack:** .NET 10, xUnit, Microsoft.Extensions.DependencyInjection / .Logging.Abstractions.

**Spec:** `docs/superpowers/specs/2026-08-08-memory-engine-seam-design.md`. **Task:** `TASKS.md` Part 46 /
MEM1. Spec B (MEM2, the graph engine) builds on this and is a separate plan.

## Global Constraints

- **No new package.** Everything lands in `src/Lyntai.Core/Memory/`. Do not create a project, and do not
  touch `devtools/project.config.mjs`.
- **No third-party dependency may be added to `Lyntai.Core`** — it is mandatory for every consumer, so its
  footprint is the smallest of all. DI + Logging abstractions only.
- **Purely additive.** No released signature or semantic changes. `IMemoryStore`, `ISemanticMemory`,
  `ICuratedMemoryStore`, `MemoryPromptComposer` are read, never edited.
- **XML docs on every public member.** `node devtools/dev.mjs check-warnings` fails the build on an
  unresolved `cref`, and it is part of `verify`.
- **Never name a type `*Dto`.** The tree contains zero `Dto` identifiers and that is a measured invariant
  (`.claude/rules/repo-mechanics.md` §Naming). Use `*Options` / `*Request` / `*Result` / `*Entry` / `*Row`.
- **Write files with the Write/Edit tools.** This machine's console is GBK; echoing UTF-8 through it
  corrupts non-ASCII irreversibly.
- **Running tests:** `node devtools/dev.mjs test --filter "~Memory"`. Two traps, both measured: a filter
  matching zero tests **passes vacuously**, and `dev.mjs test -- --filter X` (with the `--`) silently runs
  the whole suite instead. **Always read the matched/total count.**
- **Commit per task. Never commit without the user's approval** (`CLAUDE.md`). Each task's final step
  prepares the commit; ask before running it.
- Branch is `feat/memory-engine-seam`, already created.

## File Structure

| File | Responsibility |
|---|---|
| `src/Lyntai.Core/Memory/IMemoryEngine.cs` | the interface, `MemoryGrade`, `MemoryGrades`, `MemorySources`, `MemoryRef` |
| `src/Lyntai.Core/Memory/MemoryRecords.cs` | `MemoryWrite`, `MemoryQuery`, `MemoryItem`, `MemoryRecall` |
| `src/Lyntai.Core/Memory/MemoryCapabilities.cs` | `IExpandableMemory`, `ILinkableMemory`, `IForgettableMemory` |
| `src/Lyntai.Core/Memory/MemoryContentId.cs` | the SHA-256 id derivation shared by id-less stores |
| `src/Lyntai.Core/Memory/Engines/LexicalMemoryEngine.cs` | adapts `IMemoryStore` |
| `src/Lyntai.Core/Memory/Engines/SemanticMemoryEngine.cs` | adapts `ISemanticMemory` |
| `src/Lyntai.Core/Memory/Engines/CuratedMemoryEngine.cs` | adapts `ICuratedMemoryStore` |
| `src/Lyntai.Core/Memory/MemoryComposition.cs` | budget allocation + section rendering (extension on `IMemoryEngine`) |
| `src/Lyntai.Core/Memory/CompositeMemoryEngine.cs` | blend of members; routing; capability forwarding |
| `src/Lyntai.Core/Memory/IMemoryEngineFactory.cs` | named lookup contract + `MemoryEngineFactory` |
| `src/Lyntai.Core/Memory/MemoryEngineBuilder.cs` | the fluent `UseLexical`/`UseSemantic`/`UseCurated`/`Budget`/`Reserve` |
| `src/Lyntai.Core/Memory/EngineBackedPromptComposer.cs` | adapts an engine to the existing `IPromptComposer` |
| `src/Lyntai.Core/Memory/MemoryEngineRegistration.cs` | `LyntaiBuilder.AddMemory` / `AddMemoryEngine` / `UseMemoryComposer` |
| `tests/Lyntai.Tests/Memory/MemoryEngineContract.cs` | backend-agnostic facts every engine satisfies |
| `tests/Lyntai.Tests/Memory/MemoryEngineContractTests.cs` | runs the contract against all three wrappers + composite |
| `tests/Lyntai.Tests/Memory/MemoryCompositionTests.cs` | budget, reserve, sections, determinism, the accuracy test |
| `tests/Lyntai.Tests/Memory/CompositeMemoryEngineTests.cs` | routing, capability forwarding, grade routing |
| `tests/Lyntai.Tests/Memory/MemoryEngineRegistrationTests.cs` | DI wiring, duplicate names, startup failures |
| `tests/Lyntai.Tests/Memory/FakeMemoryEngines.cs` | test doubles (expandable fake, faulting fake) |

**Note on `AddMemory()`'s default blend.** Spec A §5.1 says "graph if a graph store is available, else
lexical + semantic". `IMemoryGraphStore` does not exist until MEM2, so in this plan `AddMemory()` composes
lexical + semantic only. MEM2 adds the graph arm; do not stub it here.

---

### Task 1: Contract types and the lexical engine

**Files:**
- Create: `src/Lyntai.Core/Memory/IMemoryEngine.cs`
- Create: `src/Lyntai.Core/Memory/MemoryRecords.cs`
- Create: `src/Lyntai.Core/Memory/MemoryCapabilities.cs`
- Create: `src/Lyntai.Core/Memory/MemoryContentId.cs`
- Create: `src/Lyntai.Core/Memory/Engines/LexicalMemoryEngine.cs`
- Test: `tests/Lyntai.Tests/Memory/LexicalMemoryEngineTests.cs`

**Interfaces:**
- Consumes: `Lyntai.Storage.IMemoryStore` (`RememberAsync(taskKey, scope, content, ttl, ct)` returning
  `Task`; `RecallAsync(taskKey, scope, query, limit, ct)` returning
  `Task<IReadOnlyList<MemoryEntry>>`; `MemoryEntry(long Id, string TaskKey, string Scope, string Content,
  DateTimeOffset CreatedAt)`).
- Produces: every type below. Later tasks depend on these exact names.

- [ ] **Step 1: Write the failing test**

`tests/Lyntai.Tests/Memory/LexicalMemoryEngineTests.cs`:

```csharp
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage;

namespace Lyntai.Tests.Memory;

public class LexicalMemoryEngineTests
{
    private sealed class FakeMemoryStore : IMemoryStore
    {
        private readonly List<MemoryEntry> _entries = [];
        private long _next = 1;

        public Task RememberAsync(string taskKey, string scope, string content,
            TimeSpan? ttl = null, CancellationToken ct = default)
        {
            if (!_entries.Any(e => e.TaskKey == taskKey && e.Scope == scope && e.Content == content))
                _entries.Add(new MemoryEntry(_next++, taskKey, scope, content, DateTimeOffset.UtcNow));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
            string? query = null, int? limit = null, CancellationToken ct = default)
        {
            IEnumerable<MemoryEntry> hits = _entries.Where(e => e.TaskKey == taskKey);
            if (scope is not null) hits = hits.Where(e => e.Scope == scope);
            if (!string.IsNullOrWhiteSpace(query))
                hits = hits.Where(e => e.Content.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (limit is int n) hits = hits.Take(n);
            return Task.FromResult<IReadOnlyList<MemoryEntry>>([.. hits]);
        }

        public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
        {
            _entries.RemoveAll(e => e.TaskKey == taskKey && (scope is null || e.Scope == scope));
            return Task.CompletedTask;
        }

        public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null,
            CancellationToken ct = default) => Task.FromResult(0);
    }

    private static LexicalMemoryEngine Engine(out FakeMemoryStore store)
    {
        store = new FakeMemoryStore();
        return new LexicalMemoryEngine("chat/lexical", store);
    }

    [Fact]
    public async Task Remember_then_recall_round_trips_through_the_store()
    {
        var engine = Engine(out _);
        await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline needs approval"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "pipeline"));

        Assert.Single(recall.Items);
        Assert.Equal("the deploy pipeline needs approval", recall.Items[0].Headline);
        Assert.Equal(MemorySources.Lexical, recall.Ran);
    }

    [Fact]
    public async Task The_reference_is_stable_across_write_and_recall()
    {
        var engine = Engine(out _);
        var written = await engine.RememberAsync(new MemoryWrite("t", "s", "stable identity"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "stable"));

        Assert.Equal(written, recall.Items[0].Reference);
        Assert.Equal("chat/lexical", written.Engine);
    }

    [Fact]
    public async Task It_reports_that_it_can_only_store_associative_material()
    {
        var engine = Engine(out _);
        Assert.Equal(MemoryGrades.Associative, engine.Supported);
    }

    [Fact]
    public async Task An_authoritative_write_throws_rather_than_being_downgraded()
    {
        var engine = Engine(out var store);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            engine.RememberAsync(new MemoryWrite("t", "s", "exact fact", Grade: MemoryGrade.Authoritative)));

        Assert.Contains("chat/lexical", ex.Message);
        Assert.Empty(await store.RecallAsync("t"));   // nothing was written
    }

    [Fact]
    public async Task A_storage_fault_during_recall_degrades_to_empty_rather_than_throwing()
    {
        var engine = new LexicalMemoryEngine("chat/lexical", new ThrowingStore());
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "anything"));
        Assert.Empty(recall.Items);
        Assert.Equal(MemorySources.None, recall.Ran);
    }

    private sealed class ThrowingStore : IMemoryStore
    {
        public Task RememberAsync(string t, string s, string c, TimeSpan? ttl = null, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
        public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string t, string? s = null, string? q = null,
            int? limit = null, CancellationToken ct = default) => throw new InvalidOperationException("boom");
        public Task ForgetAsync(string t, string? s = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> PruneAsync(string? t = null, TimeSpan? o = null, CancellationToken ct = default)
            => Task.FromResult(0);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `node devtools/dev.mjs test --filter "~LexicalMemoryEngine"`
Expected: **compile failure** — `IMemoryEngine`, `MemoryWrite`, `MemoryQuery`, `MemorySources`,
`MemoryGrades`, `LexicalMemoryEngine` do not exist. A compile failure is the correct "red" here; do not
proceed until you have seen it.

- [ ] **Step 3: Write the contract types**

`src/Lyntai.Core/Memory/IMemoryEngine.cs`:

```csharp
namespace Lyntai.Memory;

/// <summary>A named memory system: something that can be told a fact and asked for the relevant ones.
/// Registered as a DI collection and keyed by <see cref="Name"/> through
/// <see cref="IMemoryEngineFactory"/> — the same variation-point shape as <c>ILlmProvider</c> keyed by
/// <c>Id</c> and picked by <c>ILlmRouter</c>. Adding a kind of memory is a class plus a registration.</summary>
public interface IMemoryEngine
{
    /// <summary>Unique within the container. Hierarchical for a member of a composite
    /// ("project/glossary"), which is what lets <see cref="MemoryRef"/> route unambiguously.</summary>
    string Name { get; }

    /// <summary>Which grades this engine can actually store. A store with a grade column reports both; a
    /// store whose grade is a constant of the store reports only that one. A composite reports the union
    /// of its members', and routes an incoming write to a member that can hold it.</summary>
    MemoryGrades Supported { get; }

    /// <summary>Store a fact and return its address. <b>Surfaces failures</b> — a silently lost write is
    /// worse than a throw the caller can see, which is the asymmetry <c>ISemanticMemory</c> already
    /// documents. Throws <see cref="NotSupportedException"/> rather than downgrading a grade this engine
    /// cannot store.</summary>
    Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default);

    /// <summary>Recall relevant facts. <b>Fails open</b> — a storage outage yields an empty result with
    /// <see cref="MemorySources.None"/>, never a throw. Only <see cref="OperationCanceledException"/>
    /// propagates, because cancellation belongs to the caller.</summary>
    Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default);
}

/// <summary>How exact a piece of remembered material is, which decides whether it may decay, be
/// summarised, or be crowded out of a prompt.</summary>
public enum MemoryGrade
{
    /// <summary>Take the grade from the engine's own role. The default, so a caller that does not care
    /// never sees the concept.</summary>
    Inherit = 0,
    /// <summary>Recalled context: may decay, may be summarised to a headline, spends whatever budget is
    /// left.</summary>
    Associative = 1,
    /// <summary>An exact fact: never decays, never truncated, holds a reserved budget ahead of
    /// associative material, and renders in its own labelled section.</summary>
    Authoritative = 2,
}

/// <summary>The set of grades an engine can store — see <see cref="IMemoryEngine.Supported"/>.</summary>
[Flags]
public enum MemoryGrades
{
    /// <summary>Stores nothing (a read-only engine).</summary>
    None = 0,
    /// <summary>Can store <see cref="MemoryGrade.Associative"/> material.</summary>
    Associative = 1,
    /// <summary>Can store <see cref="MemoryGrade.Authoritative"/> material.</summary>
    Authoritative = 2,
}

/// <summary>Which retrieval tiers actually ran, so a caller can tell "nothing matched" from "that source
/// is not configured". Reported on every recall.</summary>
[Flags]
public enum MemorySources
{
    /// <summary>Nothing ran, or everything that ran faulted.</summary>
    None = 0,
    /// <summary>The keyword/full-text tier produced a result.</summary>
    Lexical = 1,
    /// <summary>A semantic-memory member produced hits.</summary>
    Semantic = 2,
    /// <summary>A curated catalog member produced hits.</summary>
    Curated = 4,
    /// <summary>A graph member produced hits (Spec B).</summary>
    Graph = 8,
    /// <summary>Similarity-derived edge enrichment ran (Spec B) — distinct from <see cref="Semantic"/>,
    /// which means the semantic-memory MEMBER produced hits. Both need an embedder and they fail
    /// independently.</summary>
    Similarity = 16,
}

/// <summary>An entry's address. <see cref="Engine"/> is the name of the engine that OWNS the entry — for
/// a member of a composite this is the member's hierarchical name, never the composite's, which is what
/// makes expansion route unambiguously.
/// <para><see cref="Id"/> is store-defined where the store has one. An engine over a store whose write
/// returns no identifier (<c>IMemoryStore</c>, <c>ISemanticMemory</c>) keys by the SHA-256 hex of the
/// content — which is also how those stores define identity, since re-remembering identical content
/// refreshes rather than duplicates.</para></summary>
/// <param name="Engine">The owning engine's <see cref="IMemoryEngine.Name"/>.</param>
/// <param name="Id">Opaque, stable within that engine.</param>
public readonly record struct MemoryRef(string Engine, string Id);
```

`src/Lyntai.Core/Memory/MemoryRecords.cs`:

```csharp
namespace Lyntai.Memory;

/// <summary>A fact to remember, scoped by (<paramref name="TaskKey"/>, <paramref name="Scope"/>) like
/// every other memory surface in the library.</summary>
/// <param name="TaskKey">The consumer/purpose this fact belongs to.</param>
/// <param name="Scope">The variant within that task.</param>
/// <param name="Content">The fact itself.</param>
/// <param name="Headline">An authored one-line form. When null, an engine that supports headlines derives
/// one for ASSOCIATIVE material only — authoritative content is never truncated.</param>
/// <param name="Grade">Explicit grade, which WINS over the engine's role. The default
/// <see cref="MemoryGrade.Inherit"/> takes the role.</param>
/// <param name="Metadata">Arbitrary app-owned extra data. Engines whose store cannot hold it ignore it.</param>
public sealed record MemoryWrite(
    string TaskKey,
    string Scope,
    string Content,
    string? Headline = null,
    MemoryGrade Grade = MemoryGrade.Inherit,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>What to recall.</summary>
/// <param name="TaskKey">The consumer/purpose to recall within.</param>
/// <param name="Scope">Optional variant filter; null recalls across the task's scopes.</param>
/// <param name="Query">Optional relevance query; null or whitespace recalls the most recent.</param>
/// <param name="Limit">Maximum items; null takes the engine's default.</param>
/// <param name="CharBudget">Maximum characters the caller intends to spend; null takes the engine's
/// configured budget.</param>
public sealed record MemoryQuery(
    string TaskKey,
    string? Scope = null,
    string? Query = null,
    int? Limit = null,
    int? CharBudget = null);

/// <summary>One recalled entry.</summary>
/// <param name="Reference">Its address, carrying the owning engine.</param>
/// <param name="Headline">The one-line form. For associative material this may be derived; for
/// authoritative material it is always the full content.</param>
/// <param name="Content">The full content, or null for an associative item that has been recalled but not
/// expanded — that is what makes the first load cheap. ALWAYS populated for authoritative material.</param>
/// <param name="Grade">Resolved grade (never <see cref="MemoryGrade.Inherit"/>).</param>
/// <param name="Relevance">How well it matched, 0..1. Engines that rank by recency report 1.</param>
/// <param name="Retrievability">How well remembered it is, 0..1. Engines with no decay model report 1.</param>
/// <param name="Degree">How many other entries it is linked to. Engines with no graph report 0.</param>
public sealed record MemoryItem(
    MemoryRef Reference,
    string Headline,
    string? Content,
    MemoryGrade Grade,
    double Relevance,
    double Retrievability,
    int Degree);

/// <summary>The result of a recall, with the tiers that actually ran.</summary>
/// <param name="Items">Hits, most relevant first.</param>
/// <param name="Ran">Which tiers produced them — so an empty tier is distinguishable from an absent one.</param>
public sealed record MemoryRecall(IReadOnlyList<MemoryItem> Items, MemorySources Ran)
{
    /// <summary>An empty result from no tier at all.</summary>
    public static MemoryRecall Empty { get; } = new([], MemorySources.None);
}
```

`src/Lyntai.Core/Memory/MemoryCapabilities.cs`:

```csharp
namespace Lyntai.Memory;

/// <summary>An engine whose entries are connected, so one can be expanded into its detail and its
/// neighbours. Implemented by the graph engine (Spec B); the wrappers over the flat stores do not.
/// <para>A composite ALWAYS implements this and routes by <see cref="MemoryRef.Engine"/> to the owning
/// member. Where that member does not implement it, expansion fails OPEN — the entry is returned with no
/// neighbours.</para></summary>
public interface IExpandableMemory
{
    /// <summary>The entry's full content plus its neighbours, ordered by connection strength.</summary>
    Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        CancellationToken ct = default);
}

/// <summary>An engine whose entries can be linked explicitly.
/// <para>A composite ALWAYS implements this and routes by <see cref="MemoryRef.Engine"/>. Where the owning
/// member does not implement it, linking THROWS — unlike expansion, because a silently dropped write is
/// worse than a visible failure.</para></summary>
public interface ILinkableMemory
{
    /// <summary>Connect two entries. Directed unless <paramref name="symmetric"/>.</summary>
    Task LinkAsync(MemoryRef from, MemoryRef to, string? kind = null, double weight = 1.0,
        bool symmetric = false, CancellationToken ct = default);
}

/// <summary>An engine that can reap entries. Reaping is always explicit — nothing in this library deletes
/// remembered material as a side effect of decay.</summary>
public interface IForgettableMemory
{
    /// <summary>Reap entries, returning how many were removed.</summary>
    Task<int> PruneAsync(string taskKey, string? scope = null, double? minRetrievability = null,
        TimeSpan? olderThan = null, CancellationToken ct = default);
}
```

`src/Lyntai.Core/Memory/MemoryContentId.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Memory;

/// <summary>Derives a stable <see cref="MemoryRef.Id"/> for an engine whose store returns no identifier
/// on write. The hash covers the same fields those stores dedup on, so the id a write returns is the id a
/// later recall reports for the same entry.</summary>
internal static class MemoryContentId
{
    /// <summary>Length-framed so ("ab","c") and ("a","bc") cannot collide — the same framing rule the
    /// response cache's key uses.</summary>
    public static string For(string taskKey, string scope, string content)
    {
        var framed = $"{taskKey.Length}:{taskKey}|{scope.Length}:{scope}|{content.Length}:{content}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(framed)));
    }
}
```

- [ ] **Step 4: Write the lexical engine**

`src/Lyntai.Core/Memory/Engines/LexicalMemoryEngine.cs`:

```csharp
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Engines;

/// <summary>Adapts the keyword <see cref="IMemoryStore"/> to <see cref="IMemoryEngine"/>. Associative
/// only: the store has no grade column, so an authoritative write is REFUSED rather than downgraded.
/// Entries are addressed by content hash, because the store's write returns no identifier and its own
/// identity is exact content.</summary>
public sealed class LexicalMemoryEngine(
    string name,
    IMemoryStore store,
    ILogger<LexicalMemoryEngine>? logger = null) : IMemoryEngine
{
    private readonly ILogger _logger = logger ?? NullLogger<LexicalMemoryEngine>.Instance;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public MemoryGrades Supported => MemoryGrades.Associative;

    /// <inheritdoc />
    public async Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        if (write.Grade == MemoryGrade.Authoritative)
            throw new NotSupportedException(
                $"Memory engine '{Name}' stores associative material only and cannot hold an authoritative " +
                "write. Route it to an engine whose Supported includes Authoritative, or add one to the " +
                "composite.");

        await store.RememberAsync(write.TaskKey, write.Scope, write.Content, ct: ct).ConfigureAwait(false);
        return new MemoryRef(Name, MemoryContentId.For(write.TaskKey, write.Scope, write.Content));
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        try
        {
            var entries = await store
                .RecallAsync(query.TaskKey, query.Scope, query.Query, query.Limit, ct)
                .ConfigureAwait(false);
            if (entries.Count == 0) return MemoryRecall.Empty;

            var items = new List<MemoryItem>(entries.Count);
            foreach (var entry in entries)
                items.Add(new MemoryItem(
                    new MemoryRef(Name, MemoryContentId.For(entry.TaskKey, entry.Scope, entry.Content)),
                    entry.Content, entry.Content, MemoryGrade.Associative, 1, 1, 0));

            return new MemoryRecall(items, MemorySources.Lexical);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // recall is contractually fail-open — a broken custom store must not sink the caller's prompt
            _logger.LogWarning(ex, "lexical recall failed for {Engine}/{Task}; returning nothing",
                Name, query.TaskKey);
            return MemoryRecall.Empty;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `node devtools/dev.mjs test --filter "~LexicalMemoryEngine"`
Expected: PASS, 5 matched. **Read the matched count** — a filter matching zero passes vacuously.

- [ ] **Step 6: Build clean**

Run: `node devtools/dev.mjs check-warnings`
Expected: no warnings. An unresolved `cref` in the XML docs fails here, and it is part of `verify`.

- [ ] **Step 7: Prepare the commit (ask for approval first)**

```bash
git add src/Lyntai.Core/Memory tests/Lyntai.Tests/Memory
git commit -m "feat(memory): add the IMemoryEngine contract and the lexical engine"
```

---

### Task 2: The semantic and curated engines, behind a shared contract

**Files:**
- Create: `src/Lyntai.Core/Memory/Engines/SemanticMemoryEngine.cs`
- Create: `src/Lyntai.Core/Memory/Engines/CuratedMemoryEngine.cs`
- Create: `tests/Lyntai.Tests/Memory/MemoryEngineContract.cs`
- Create: `tests/Lyntai.Tests/Memory/MemoryEngineContractTests.cs`

**Interfaces:**
- Consumes: `IMemoryEngine`, `MemoryWrite`, `MemoryQuery`, `MemoryRecall`, `MemoryGrades`,
  `MemoryContentId` (Task 1); `Lyntai.Memory.ISemanticMemory`
  (`RememberAsync(taskKey, scope, content, ct)`, `RecallAsync(taskKey, scope, query, k, minScore, ct)`
  returning `IReadOnlyList<SemanticHit>` where `SemanticHit(string Content, double Score)`);
  `Lyntai.Storage.ICuratedMemoryStore` (`AddAsync(kind, content, enabled, taskKey, scope, dedup, metadata, ct)`
  returning `Task<long>`; `SearchAsync(query, kind, taskKey, scope, enabledOnly, limit, metadataMatch, ct)`;
  `ForCompositionAsync(taskKey, scopes, enabledOnly, ct)`).
- Produces: `MemoryEngineContract.All(IMemoryEngine engine, string key)` — the fact set later tasks reuse
  for the composite.

- [ ] **Step 1: Write the failing contract**

`tests/Lyntai.Tests/Memory/MemoryEngineContract.cs`:

```csharp
using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Engine-agnostic facts every <see cref="IMemoryEngine"/> satisfies, run by each engine's test
/// class the way MemoryStoreContract holds three storage backends to one contract. Every method is
/// namespaced by a caller-supplied key so implementations sharing state stay isolated.</summary>
public static class MemoryEngineContract
{
    public static async Task Remember_then_recall_finds_it(IMemoryEngine engine, string key)
    {
        await engine.RememberAsync(new MemoryWrite(key, "s", "the deploy pipeline needs approval"));
        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "pipeline"));
        Assert.Contains(recall.Items, i => i.Headline.Contains("approval", StringComparison.Ordinal));
    }

    public static async Task Every_item_carries_this_engines_name(IMemoryEngine engine, string key)
    {
        await engine.RememberAsync(new MemoryWrite(key, "s", "ownership is recorded on the reference"));
        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "ownership"));
        Assert.All(recall.Items, i => Assert.Equal(engine.Name, i.Reference.Engine));
    }

    public static async Task Recall_reports_the_tier_that_ran(IMemoryEngine engine, string key)
    {
        await engine.RememberAsync(new MemoryWrite(key, "s", "tiers are reported"));
        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "tiers"));
        Assert.NotEqual(MemorySources.None, recall.Ran);
    }

    public static async Task An_unsupported_grade_throws_rather_than_downgrading(IMemoryEngine engine, string key)
    {
        var unsupported = engine.Supported.HasFlag(MemoryGrades.Authoritative)
            ? MemoryGrade.Associative
            : MemoryGrade.Authoritative;
        if (engine.Supported.HasFlag(MemoryGrades.Associative) &&
            engine.Supported.HasFlag(MemoryGrades.Authoritative))
            return; // an engine that holds both has nothing to refuse

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            engine.RememberAsync(new MemoryWrite(key, "s", "graded write", Grade: unsupported)));
    }

    public static async Task An_inherited_grade_resolves_and_is_never_returned_as_Inherit(
        IMemoryEngine engine, string key)
    {
        await engine.RememberAsync(new MemoryWrite(key, "s", "grade resolves on read"));
        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "resolves"));
        Assert.All(recall.Items, i => Assert.NotEqual(MemoryGrade.Inherit, i.Grade));
    }

    public static async Task Authoritative_items_always_carry_full_content(IMemoryEngine engine, string key)
    {
        if (!engine.Supported.HasFlag(MemoryGrades.Authoritative)) return;
        await engine.RememberAsync(new MemoryWrite(key, "s", "the build gate is dev.mjs verify",
            Grade: MemoryGrade.Authoritative));
        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "gate"));
        Assert.All(recall.Items.Where(i => i.Grade == MemoryGrade.Authoritative),
            i => Assert.Equal("the build gate is dev.mjs verify", i.Content));
    }

    public static async Task An_empty_query_does_not_throw(IMemoryEngine engine, string key)
    {
        var recall = await engine.RecallAsync(new MemoryQuery(key, "s", "   "));
        Assert.NotNull(recall.Items);
    }

    public static async Task Cancellation_propagates(IMemoryEngine engine, string key)
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.RecallAsync(new MemoryQuery(key, "s", "anything"), cts.Token));
    }
}
```

`tests/Lyntai.Tests/Memory/MemoryEngineContractTests.cs` — one class per engine, each calling every
contract method. Write all eight calls per engine explicitly; do not loop over reflection.

```csharp
using Lyntai.Memory;
using Lyntai.Memory.Engines;

namespace Lyntai.Tests.Memory;

public class LexicalEngineContractTests
{
    private static IMemoryEngine New() => new LexicalMemoryEngine("lex", new FakeMemoryStore());

    [Fact] public Task Remember_then_recall() => MemoryEngineContract.Remember_then_recall_finds_it(New(), "k1");
    [Fact] public Task Carries_name() => MemoryEngineContract.Every_item_carries_this_engines_name(New(), "k2");
    [Fact] public Task Reports_tier() => MemoryEngineContract.Recall_reports_the_tier_that_ran(New(), "k3");
    [Fact] public Task Refuses_grade() => MemoryEngineContract.An_unsupported_grade_throws_rather_than_downgrading(New(), "k4");
    [Fact] public Task Resolves_grade() => MemoryEngineContract.An_inherited_grade_resolves_and_is_never_returned_as_Inherit(New(), "k5");
    [Fact] public Task Authoritative_full() => MemoryEngineContract.Authoritative_items_always_carry_full_content(New(), "k6");
    [Fact] public Task Empty_query() => MemoryEngineContract.An_empty_query_does_not_throw(New(), "k7");
    [Fact] public Task Cancellation() => MemoryEngineContract.Cancellation_propagates(New(), "k8");
}

public class SemanticEngineContractTests
{
    private static IMemoryEngine New() => new SemanticMemoryEngine("sem", new FakeSemanticMemory());
    // …the same eight [Fact] methods, with keys k1..k8.
}

public class CuratedEngineContractTests
{
    private static IMemoryEngine New() => new CuratedMemoryEngine("cur", new FakeCuratedStore(), kind: "glossary");
    // …the same eight [Fact] methods, with keys k1..k8.
}
```

Move `FakeMemoryStore` out of `LexicalMemoryEngineTests` into
`tests/Lyntai.Tests/Memory/FakeMemoryEngines.cs` as an `internal sealed class`, and add `FakeSemanticMemory`
and `FakeCuratedStore` beside it with the same in-list substring behaviour.

- [ ] **Step 2: Run to verify it fails**

Run: `node devtools/dev.mjs test --filter "~EngineContract"`
Expected: compile failure — `SemanticMemoryEngine` and `CuratedMemoryEngine` do not exist.

- [ ] **Step 3: Write the semantic engine**

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Engines;

/// <summary>Adapts meaning-based <see cref="ISemanticMemory"/> to <see cref="IMemoryEngine"/>. Associative
/// only. <see cref="ISemanticMemory"/> requires a concrete scope (a vector collection is per task+scope)
/// and a non-empty query, so a recall missing either yields nothing rather than throwing.</summary>
public sealed class SemanticMemoryEngine(
    string name,
    ISemanticMemory semantic,
    int defaultK = 10,
    ILogger<SemanticMemoryEngine>? logger = null) : IMemoryEngine
{
    private readonly ILogger _logger = logger ?? NullLogger<SemanticMemoryEngine>.Instance;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public MemoryGrades Supported => MemoryGrades.Associative;

    /// <inheritdoc />
    public async Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        if (write.Grade == MemoryGrade.Authoritative)
            throw new NotSupportedException(
                $"Memory engine '{Name}' stores associative material only and cannot hold an authoritative write.");

        await semantic.RememberAsync(write.TaskKey, write.Scope, write.Content, ct).ConfigureAwait(false);
        return new MemoryRef(Name, MemoryContentId.For(write.TaskKey, write.Scope, write.Content));
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (query.Scope is null || string.IsNullOrWhiteSpace(query.Query)) return MemoryRecall.Empty;

        try
        {
            var hits = await semantic
                .RecallAsync(query.TaskKey, query.Scope, query.Query, query.Limit ?? defaultK, ct: ct)
                .ConfigureAwait(false);
            if (hits.Count == 0) return MemoryRecall.Empty;

            var items = new List<MemoryItem>(hits.Count);
            foreach (var hit in hits)
                items.Add(new MemoryItem(
                    new MemoryRef(Name, MemoryContentId.For(query.TaskKey, query.Scope, hit.Content)),
                    hit.Content, hit.Content, MemoryGrade.Associative,
                    Math.Clamp(hit.Score, 0, 1), 1, 0));

            return new MemoryRecall(items, MemorySources.Semantic);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "semantic recall failed for {Engine}/{Task}; returning nothing",
                Name, query.TaskKey);
            return MemoryRecall.Empty;
        }
    }
}
```

**Note the explicit `ThrowIfCancellationRequested`:** the early return for a missing scope would otherwise
swallow a cancelled token and fail the contract's cancellation fact.

- [ ] **Step 4: Write the curated engine**

```csharp
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Engines;

/// <summary>Adapts the operator-curated <see cref="ICuratedMemoryStore"/> to <see cref="IMemoryEngine"/>.
/// AUTHORITATIVE by construction — the catalog is deliberately managed, has no decay and no cap, so its
/// entries are exact facts. It therefore refuses an associative write: accepting one would let decaying
/// material into the section the composer renders as authoritative.
/// <para>Writes go in under <paramref name="kind"/> with <c>dedup: true</c>, so remembering the same fact
/// twice is idempotent rather than minting a duplicate catalog row.</para></summary>
public sealed class CuratedMemoryEngine(
    string name,
    ICuratedMemoryStore store,
    string kind = "memory",
    ILogger<CuratedMemoryEngine>? logger = null) : IMemoryEngine
{
    private readonly ILogger _logger = logger ?? NullLogger<CuratedMemoryEngine>.Instance;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public MemoryGrades Supported => MemoryGrades.Authoritative;

    /// <inheritdoc />
    public async Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        if (write.Grade == MemoryGrade.Associative)
            throw new NotSupportedException(
                $"Memory engine '{Name}' is a curated catalog and holds authoritative material only.");

        var id = await store.AddAsync(kind, write.Content, enabled: true, taskKey: write.TaskKey,
            scope: write.Scope, dedup: true, metadata: write.Metadata, ct: ct).ConfigureAwait(false);
        return new MemoryRef(Name, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            // no query = "everything that applies to this task", which is the catalog's composition read
            var entries = string.IsNullOrWhiteSpace(query.Query)
                ? await store.ForCompositionAsync(query.TaskKey,
                    query.Scope is null ? [] : [query.Scope], enabledOnly: true, ct).ConfigureAwait(false)
                : await store.SearchAsync(query.Query, kind, query.TaskKey, query.Scope,
                    enabledOnly: true, query.Limit, ct: ct).ConfigureAwait(false);
            if (entries.Count == 0) return MemoryRecall.Empty;

            var items = new List<MemoryItem>(entries.Count);
            foreach (var entry in entries)
                items.Add(new MemoryItem(
                    new MemoryRef(Name, entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    entry.Content, entry.Content, MemoryGrade.Authoritative, 1, 1, 0));

            return new MemoryRecall(items, MemorySources.Curated);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "curated recall failed for {Engine}/{Task}; returning nothing",
                Name, query.TaskKey);
            return MemoryRecall.Empty;
        }
    }
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `node devtools/dev.mjs test --filter "~EngineContract"`
Expected: PASS, 24 matched (3 engines × 8 facts). Read the count.

- [ ] **Step 6: Prepare the commit (ask first)**

```bash
git add src/Lyntai.Core/Memory tests/Lyntai.Tests/Memory
git commit -m "feat(memory): add semantic and curated engines behind a shared engine contract"
```

---

### Task 3: Composition — reserved budget and labelled sections

**Files:**
- Create: `src/Lyntai.Core/Memory/MemoryComposition.cs`
- Test: `tests/Lyntai.Tests/Memory/MemoryCompositionTests.cs`

**Interfaces:**
- Consumes: `IMemoryEngine.RecallAsync`, `MemoryItem`, `MemoryGrade` (Tasks 1–2).
- Produces: `MemoryComposition.ComposeAsync(this IMemoryEngine, string basePrompt, MemoryQuery query,
  MemoryCompositionOptions? options, CancellationToken)` returning `Task<string>`; and
  `MemoryCompositionOptions { int Budget, int AuthoritativeReserve, string AuthoritativeHeading,
  string AssociativeHeading }`.

- [ ] **Step 1: Write the failing test**

```csharp
using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

public class MemoryCompositionTests
{
    private static IMemoryEngine EngineWith(params MemoryItem[] items) => new StaticEngine("e", items);

    private static MemoryItem Item(string text, MemoryGrade grade) =>
        new(new MemoryRef("e", text), text, text, grade, 1, 1, 0);

    [Fact]
    public async Task It_renders_the_two_grades_as_separate_labelled_sections()
    {
        var engine = EngineWith(
            Item("the build gate is dev.mjs verify", MemoryGrade.Authoritative),
            Item("user prefers terse commit messages", MemoryGrade.Associative));

        var composed = await engine.ComposeAsync("BASE", new MemoryQuery("t", "s", "q"));

        Assert.Contains("## Known facts (authoritative)", composed, StringComparison.Ordinal);
        Assert.Contains("## Recalled context (associative", composed, StringComparison.Ordinal);
        Assert.True(composed.IndexOf("## Known facts", StringComparison.Ordinal)
                  < composed.IndexOf("## Recalled context", StringComparison.Ordinal),
            "authoritative material must render first");
    }

    [Fact]
    public async Task Associative_noise_cannot_crowd_out_an_authoritative_fact()
    {
        // THE ACCURACY TEST. 200 high-relevance associative items against a tiny budget: the one exact
        // fact must survive, verbatim, or this whole design is worse than the flat dump it replaced.
        var items = new List<MemoryItem> { Item("the build gate is dev.mjs verify", MemoryGrade.Authoritative) };
        for (var i = 0; i < 200; i++) items.Add(Item($"noise item number {i} which is quite wordy indeed", MemoryGrade.Associative));

        var composed = await EngineWith([.. items]).ComposeAsync("BASE", new MemoryQuery("t", "s", "q"),
            new MemoryCompositionOptions { Budget = 300, AuthoritativeReserve = 100 });

        Assert.Contains("the build gate is dev.mjs verify", composed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authoritative_material_that_does_not_fit_is_reported_not_dropped_silently()
    {
        var items = Enumerable.Range(0, 20)
            .Select(i => Item($"exact fact number {i} stated at some length", MemoryGrade.Authoritative))
            .ToArray();

        var composed = await EngineWith(items).ComposeAsync("BASE", new MemoryQuery("t", "s", "q"),
            new MemoryCompositionOptions { Budget = 200, AuthoritativeReserve = 200 });

        Assert.Matches(@"… \d+ further authoritative facts omitted \(budget\)", composed);
    }

    [Fact]
    public async Task The_authoritative_section_is_byte_identical_across_repeated_recalls()
    {
        var engine = EngineWith(
            Item("alpha is exact", MemoryGrade.Authoritative),
            Item("beta is exact", MemoryGrade.Authoritative),
            Item("gamma is recalled", MemoryGrade.Associative));

        var first = await engine.ComposeAsync("BASE", new MemoryQuery("t", "s", "q"));
        var second = await engine.ComposeAsync("BASE", new MemoryQuery("t", "s", "q"));

        Assert.Equal(Section(first), Section(second));

        static string Section(string composed)
        {
            var start = composed.IndexOf("## Known facts", StringComparison.Ordinal);
            var end = composed.IndexOf("## Recalled context", StringComparison.Ordinal);
            return end < 0 ? composed[start..] : composed[start..end];
        }
    }

    [Fact]
    public async Task No_recall_returns_the_base_prompt_unchanged()
    {
        var composed = await EngineWith().ComposeAsync("BASE", new MemoryQuery("t", "s", "q"));
        Assert.Equal("BASE", composed);
    }

    [Fact]
    public async Task A_faulting_engine_returns_the_base_prompt_rather_than_throwing()
    {
        var composed = await new FaultingEngine("e").ComposeAsync("BASE", new MemoryQuery("t", "s", "q"));
        Assert.Equal("BASE", composed);
    }
}
```

Add `StaticEngine` and `FaultingEngine` to `FakeMemoryEngines.cs`. `StaticEngine` returns its items
unchanged from `RecallAsync` and throws from `RememberAsync`; `FaultingEngine` throws from `RecallAsync`
(which the extension must catch — the engine contract says recall fails open, but a BYO engine may not
honour it, exactly as `MemoryPromptComposer` already defends against).

- [ ] **Step 2: Run to verify it fails**

Run: `node devtools/dev.mjs test --filter "~MemoryComposition"`
Expected: compile failure — `ComposeAsync` and `MemoryCompositionOptions` do not exist.

- [ ] **Step 3: Write the composition**

```csharp
using System.Text;

namespace Lyntai.Memory;

/// <summary>How an engine's recall is rendered into a prompt.</summary>
public sealed record MemoryCompositionOptions
{
    /// <summary>Total characters the appended sections may use.</summary>
    public int Budget { get; init; } = 4000;

    /// <summary>Characters reserved for authoritative material and allocated BEFORE any associative
    /// content is admitted. Associative content then spends what remains of <see cref="Budget"/>.
    /// A flat first-come budget lets a burst of loosely-relevant recall push a hard constraint out of the
    /// prompt entirely, with nothing reporting it — which is the failure this whole grade split exists to
    /// prevent.</summary>
    public int AuthoritativeReserve { get; init; } = 1000;

    /// <summary>Heading for exact material.</summary>
    public string AuthoritativeHeading { get; init; } = "## Known facts (authoritative)";

    /// <summary>Heading for recalled material.</summary>
    public string AssociativeHeading { get; init; } = "## Recalled context (associative — may be stale or partial)";
}

/// <summary>Renders an engine's recall into a prompt. Composing is something an engine DOES, so there is
/// no separate composer type — "several composers" is already "several engines".</summary>
public static class MemoryComposition
{
    /// <summary>Append this engine's relevant material to <paramref name="basePrompt"/> as two labelled
    /// sections, authoritative first. Never throws: a faulting engine yields the base prompt unchanged.</summary>
    public static async Task<string> ComposeAsync(this IMemoryEngine engine, string basePrompt,
        MemoryQuery query, MemoryCompositionOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var opts = options ?? new MemoryCompositionOptions();

        MemoryRecall recall;
        try { recall = await engine.RecallAsync(query, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch { return basePrompt; }   // a BYO engine that ignores the fail-open contract must not sink the prompt

        if (recall.Items.Count == 0) return basePrompt;

        var exact = recall.Items.Where(i => i.Grade == MemoryGrade.Authoritative).ToList();
        var recalled = recall.Items.Where(i => i.Grade != MemoryGrade.Authoritative).ToList();

        // authoritative first, out of its own reserve — then whatever of the total budget is left over
        var (exactText, omitted) = Fill(exact, Math.Min(opts.AuthoritativeReserve, opts.Budget), verbatim: true);
        var remaining = opts.Budget - exactText.Length;
        var (recalledText, _) = Fill(recalled, Math.Max(0, remaining), verbatim: false);

        if (exactText.Length == 0 && recalledText.Length == 0) return basePrompt;

        var sb = new StringBuilder(basePrompt);
        if (exactText.Length > 0)
        {
            sb.Append("\n\n").Append(opts.AuthoritativeHeading).Append('\n').Append(exactText);
            if (omitted > 0)
                sb.Append("… ").Append(omitted).Append(" further authoritative facts omitted (budget)\n");
        }
        if (recalledText.Length > 0)
            sb.Append("\n\n").Append(opts.AssociativeHeading).Append('\n').Append(recalledText);

        return sb.ToString().TrimEnd();

        static (string Text, int Omitted) Fill(List<MemoryItem> items, int budget, bool verbatim)
        {
            var sb = new StringBuilder();
            var omitted = 0;
            foreach (var item in items)
            {
                // authoritative material is never truncated: a headline reading "the build gate is
                // dev.mjs" when the content says "dev.mjs verify" is worse than no memory at all.
                var text = verbatim ? item.Content ?? item.Headline : item.Headline;
                var line = $"- {text}\n";
                if (line.Length > budget) { omitted++; continue; }
                sb.Append(line);
                budget -= line.Length;
            }
            return (sb.ToString(), omitted);
        }
    }
}
```

**Note `continue`, not `break`, in `Fill`:** a single oversized fact must not hide every shorter one behind
it, and for the authoritative pass every skipped item has to be counted so the omission line is truthful.

- [ ] **Step 4: Run to verify they pass**

Run: `node devtools/dev.mjs test --filter "~MemoryComposition"`
Expected: PASS, 6 matched.

- [ ] **Step 5: Prepare the commit (ask first)**

```bash
git add src/Lyntai.Core/Memory tests/Lyntai.Tests/Memory
git commit -m "feat(memory): compose recall as labelled sections with a reserved authoritative budget"
```

---

### Task 4: The composite engine

**Files:**
- Create: `src/Lyntai.Core/Memory/CompositeMemoryEngine.cs`
- Test: `tests/Lyntai.Tests/Memory/CompositeMemoryEngineTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–3.
- Produces: `CompositeMemoryEngine(string name, IReadOnlyList<IMemoryEngine> members)` implementing
  `IMemoryEngine`, `IExpandableMemory`, `ILinkableMemory`.

- [ ] **Step 1: Write the failing test**

```csharp
using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

public class CompositeMemoryEngineTests
{
    private static CompositeMemoryEngine Composite(params IMemoryEngine[] members) =>
        new("project", members);

    [Fact]
    public async Task It_merges_every_members_items_and_unions_the_tiers()
    {
        var composite = Composite(
            new StaticEngine("project/lexical", [Assoc("project/lexical", "recalled thing")], MemorySources.Lexical),
            new StaticEngine("project/glossary", [Exact("project/glossary", "exact thing")], MemorySources.Curated));

        var recall = await composite.RecallAsync(new MemoryQuery("t", "s", "thing"));

        Assert.Equal(2, recall.Items.Count);
        Assert.Equal(MemorySources.Lexical | MemorySources.Curated, recall.Ran);
    }

    [Fact]
    public async Task Items_keep_the_owning_members_name_not_the_composites()
    {
        var composite = Composite(new StaticEngine("project/lexical", [Assoc("project/lexical", "owned")]));
        var recall = await composite.RecallAsync(new MemoryQuery("t", "s", "owned"));
        Assert.Equal("project/lexical", recall.Items[0].Reference.Engine);
    }

    [Fact]
    public async Task One_faulting_member_does_not_sink_the_others()
    {
        var composite = Composite(
            new FaultingEngine("project/broken"),
            new StaticEngine("project/ok", [Assoc("project/ok", "still here")]));

        var recall = await composite.RecallAsync(new MemoryQuery("t", "s", "still"));

        Assert.Single(recall.Items);
        Assert.Equal("still here", recall.Items[0].Headline);
    }

    [Fact]
    public async Task Supported_is_the_union_of_its_members()
    {
        var composite = Composite(
            new StaticEngine("project/lexical", [], grades: MemoryGrades.Associative),
            new StaticEngine("project/glossary", [], grades: MemoryGrades.Authoritative));

        Assert.Equal(MemoryGrades.Associative | MemoryGrades.Authoritative, composite.Supported);
    }

    [Fact]
    public async Task An_authoritative_write_is_routed_to_a_member_that_can_hold_it()
    {
        var lexical = new RecordingEngine("project/lexical", MemoryGrades.Associative);
        var glossary = new RecordingEngine("project/glossary", MemoryGrades.Authoritative);
        var composite = Composite(lexical, glossary);

        var reference = await composite.RememberAsync(
            new MemoryWrite("t", "s", "exact", Grade: MemoryGrade.Authoritative));

        Assert.Equal("project/glossary", reference.Engine);
        Assert.Empty(lexical.Writes);
        Assert.Single(glossary.Writes);
    }

    [Fact]
    public async Task An_unroutable_write_throws_and_names_what_was_considered()
    {
        var composite = Composite(new RecordingEngine("project/lexical", MemoryGrades.Associative));

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            composite.RememberAsync(new MemoryWrite("t", "s", "exact", Grade: MemoryGrade.Authoritative)));

        Assert.Contains("project", ex.Message, StringComparison.Ordinal);
        Assert.Contains("project/lexical", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expansion_routes_THROUGH_the_composite_to_the_owning_member()
    {
        // THE CAPABILITY-FORWARDING TEST. Decorating a generation provider erased its optional interfaces
        // once, and every video render stopped routing while every image render kept working and every
        // inline test stayed green. Without this test, the same regression ships invisibly here.
        var expandable = new ExpandableEngine("project/graph");
        var composite = Composite(new RecordingEngine("project/lexical", MemoryGrades.Associative), expandable);

        var expanded = await composite.ExpandAsync(new MemoryRef("project/graph", "42"));

        Assert.Single(expanded.Items);
        Assert.Equal("expanded 42", expanded.Items[0].Headline);
    }

    [Fact]
    public async Task Expanding_a_member_that_cannot_expand_fails_open()
    {
        var composite = Composite(new StaticEngine("project/lexical",
            [Assoc("project/lexical", "flat entry")]));

        var expanded = await composite.ExpandAsync(new MemoryRef("project/lexical", "flat entry"));

        Assert.Empty(expanded.Items);   // no neighbours, and no throw
    }

    [Fact]
    public async Task Linking_through_a_member_that_cannot_link_throws()
    {
        var composite = Composite(new RecordingEngine("project/lexical", MemoryGrades.Associative));

        await Assert.ThrowsAsync<NotSupportedException>(() => composite.LinkAsync(
            new MemoryRef("project/lexical", "a"), new MemoryRef("project/lexical", "b")));
    }

    [Fact]
    public async Task A_reference_naming_no_member_throws_with_the_members_listed()
    {
        var composite = Composite(new RecordingEngine("project/lexical", MemoryGrades.Associative));

        var ex = await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            composite.ExpandAsync(new MemoryRef("project/nope", "1")));

        Assert.Contains("project/lexical", ex.Message, StringComparison.Ordinal);
    }

    private static MemoryItem Assoc(string engine, string text) =>
        new(new MemoryRef(engine, text), text, text, MemoryGrade.Associative, 1, 1, 0);
    private static MemoryItem Exact(string engine, string text) =>
        new(new MemoryRef(engine, text), text, text, MemoryGrade.Authoritative, 1, 1, 0);
}
```

Add `RecordingEngine` (records writes, no capabilities) and `ExpandableEngine` (implements
`IExpandableMemory`, returns one item headlined `expanded {id}`) to `FakeMemoryEngines.cs`. Extend
`StaticEngine`'s constructor with optional `MemorySources ran` and `MemoryGrades grades` parameters.

- [ ] **Step 2: Run to verify it fails**

Run: `node devtools/dev.mjs test --filter "~CompositeMemoryEngine"`
Expected: compile failure — `CompositeMemoryEngine` does not exist.

- [ ] **Step 3: Write the composite**

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory;

/// <summary>A blend of member engines, which is itself an engine — so naming, blending, remembering and
/// expanding stay ONE concept rather than four. Members carry hierarchical names ("project/graph"), and
/// every <see cref="MemoryRef"/> records its owning member, which is what makes routing unambiguous.
/// <para><b>It never guesses about capabilities.</b> It always implements <see cref="IExpandableMemory"/>
/// and <see cref="ILinkableMemory"/> and routes strictly by <see cref="MemoryRef.Engine"/>. A wrapper that
/// implemented only the base interface would make a capable member invisible — the exact regression that
/// shipped once in the generation router, where wrapping a provider silently stopped every queue-backed
/// render from routing while inline renders kept working.</para></summary>
public sealed class CompositeMemoryEngine : IMemoryEngine, IExpandableMemory, ILinkableMemory
{
    private readonly IReadOnlyList<IMemoryEngine> _members;
    private readonly ILogger _logger;

    /// <summary>Compose <paramref name="members"/> under <paramref name="name"/>. Member order is the
    /// render order; authoritative material renders first regardless of position.</summary>
    public CompositeMemoryEngine(string name, IReadOnlyList<IMemoryEngine> members,
        ILogger<CompositeMemoryEngine>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(members);
        Name = name;
        _members = members;
        _logger = logger ?? NullLogger<CompositeMemoryEngine>.Instance;

        var duplicate = members.GroupBy(m => m.Name, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Memory engine '{name}' has two members named '{duplicate.Key}'. Give one an explicit " +
                "label so every entry's reference names exactly one owner.");
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public MemoryGrades Supported =>
        _members.Aggregate(MemoryGrades.None, (acc, m) => acc | m.Supported);

    /// <inheritdoc />
    public async Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        var wanted = write.Grade switch
        {
            MemoryGrade.Authoritative => MemoryGrades.Authoritative,
            MemoryGrade.Associative => MemoryGrades.Associative,
            _ => MemoryGrades.None,
        };

        // Inherit takes the first member; an explicit grade is ROUTED to a member that can hold it.
        // Never downgraded: accepting an authoritative write and storing it as associative is precisely
        // the failure the grade split exists to prevent.
        var target = wanted == MemoryGrades.None
            ? _members.FirstOrDefault()
            : _members.FirstOrDefault(m => m.Supported.HasFlag(wanted));

        if (target is null)
            throw new NotSupportedException(
                $"Memory engine '{Name}' has no member that can store {write.Grade} material. " +
                $"Members considered: {string.Join(", ", _members.Select(m => m.Name))}.");

        return await target.RememberAsync(write, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        var items = new List<MemoryItem>();
        var ran = MemorySources.None;

        foreach (var member in _members)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var recall = await member.RecallAsync(query, ct).ConfigureAwait(false);
                items.AddRange(recall.Items);
                ran |= recall.Ran;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // one broken member must not sink the blend — the others' material is still good
                _logger.LogWarning(ex, "member {Member} of {Engine} failed during recall; continuing",
                    member.Name, Name);
            }
        }

        return items.Count == 0 ? MemoryRecall.Empty : new MemoryRecall(items, ran);
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        CancellationToken ct = default)
    {
        var owner = Owner(reference);
        // fail OPEN: a member that cannot expand yields no neighbours rather than an error, because
        // recall-shaped reads degrade rather than throw everywhere else in this library
        return owner is IExpandableMemory expandable
            ? await expandable.ExpandAsync(reference, hops, charBudget, ct).ConfigureAwait(false)
            : MemoryRecall.Empty;
    }

    /// <inheritdoc />
    public async Task LinkAsync(MemoryRef from, MemoryRef to, string? kind = null, double weight = 1.0,
        bool symmetric = false, CancellationToken ct = default)
    {
        // fail LOUD: losing a write silently is worse than a throw the caller can see
        if (Owner(from) is not ILinkableMemory linkable)
            throw new NotSupportedException(
                $"Memory engine '{from.Engine}' does not support linking, so the link from '{from.Id}' " +
                $"was not recorded.");
        await linkable.LinkAsync(from, to, kind, weight, symmetric, ct).ConfigureAwait(false);
    }

    private IMemoryEngine Owner(MemoryRef reference)
    {
        foreach (var member in _members)
            if (string.Equals(member.Name, reference.Engine, StringComparison.Ordinal))
                return member;
        throw new KeyNotFoundException(
            $"No member of memory engine '{Name}' is named '{reference.Engine}'. " +
            $"Members: {string.Join(", ", _members.Select(m => m.Name))}.");
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `node devtools/dev.mjs test --filter "~CompositeMemoryEngine"`
Expected: PASS, 10 matched.

- [ ] **Step 5: Run the engine contract against the composite too**

Add to `MemoryEngineContractTests.cs`:

```csharp
public class CompositeEngineContractTests
{
    private static IMemoryEngine New() => new CompositeMemoryEngine("blend",
    [
        new LexicalMemoryEngine("blend/lex", new FakeMemoryStore()),
        new CuratedMemoryEngine("blend/cur", new FakeCuratedStore(), kind: "glossary"),
    ]);

    // …the same eight [Fact] methods as the other engines, keys k1..k8.
}
```

Run: `node devtools/dev.mjs test --filter "~EngineContract"`
Expected: PASS, 32 matched (4 engines × 8).

- [ ] **Step 6: Prepare the commit (ask first)**

```bash
git add src/Lyntai.Core/Memory tests/Lyntai.Tests/Memory
git commit -m "feat(memory): add the composite engine with reference-routed capability forwarding"
```

---

### Task 5: The factory, the builder, and registration

**Files:**
- Create: `src/Lyntai.Core/Memory/IMemoryEngineFactory.cs`
- Create: `src/Lyntai.Core/Memory/MemoryEngineBuilder.cs`
- Create: `src/Lyntai.Core/Memory/EngineBackedPromptComposer.cs`
- Create: `src/Lyntai.Core/Memory/MemoryEngineRegistration.cs`
- Test: `tests/Lyntai.Tests/Memory/MemoryEngineRegistrationTests.cs`

**Interfaces:**
- Consumes: `LyntaiBuilder` (`Services`, `Options`); `Lyntai.Cortex.IPromptComposer`
  (`ComposeAsync(basePrompt, taskKey, scope, query, limit, ct)` returning `Task<string>`); everything from
  Tasks 1–4.
- Produces: `IMemoryEngineFactory` (`Get(string)`, `Get()`, `TryGet(string, out IMemoryEngine)`, `Names`);
  `LyntaiBuilder.AddMemory(string name = "default")`,
  `LyntaiBuilder.AddMemoryEngine(string name, Action<MemoryEngineBuilder>? configure = null)`,
  `LyntaiBuilder.UseMemoryComposer(string name)`.

- [ ] **Step 1: Write the failing test**

```csharp
using Lyntai.Cortex;
using Lyntai.Memory;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

public class MemoryEngineRegistrationTests
{
    private static ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddLyntai(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddMemory_registers_one_working_engine_with_no_configuration()
    {
        using var sp = Build(cfg => cfg.AddMemory());
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get();
        Assert.Equal("default", engine.Name);
    }

    [Fact]
    public void Engines_are_addressable_by_name_and_several_coexist()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("chat", e => e.UseLexical())
            .AddMemoryEngine("project", e => e.UseLexical()));

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();

        Assert.Equal("chat", factory.Get("chat").Name);
        Assert.Equal("project", factory.Get("project").Name);
        Assert.Equal(["chat", "project"], factory.Names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Members_are_addressable_by_their_hierarchical_name()
    {
        using var sp = Build(cfg => cfg.AddMemoryEngine("project", e => e.UseLexical()));
        var factory = sp.GetRequiredService<IMemoryEngineFactory>();
        Assert.True(factory.TryGet("project/lexical", out _));
    }

    [Fact]
    public void An_unknown_name_throws_and_lists_what_is_registered()
    {
        using var sp = Build(cfg => cfg.AddMemoryEngine("chat", e => e.UseLexical()));
        var ex = Assert.Throws<KeyNotFoundException>(() =>
            sp.GetRequiredService<IMemoryEngineFactory>().Get("nope"));
        Assert.Contains("chat", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_engines_with_the_same_name_fail_at_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(cfg => cfg
            .AddMemoryEngine("chat", e => e.UseLexical())
            .AddMemoryEngine("chat", e => e.UseLexical())));
        Assert.Contains("chat", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_unlabelled_members_of_the_same_kind_fail_at_startup()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(cfg => cfg
            .AddMemoryEngine("project", e => e.UseLexical().UseLexical())));
        Assert.Contains("project/lexical", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_member_whose_backing_store_is_absent_fails_at_startup_naming_the_store()
    {
        // no ICuratedMemoryStore registered — this must NOT resolve to a permanently empty section
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddLyntai(cfg => cfg.AddMemoryEngine("project", e => e.UseCurated()));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.BuildServiceProvider().GetRequiredService<IMemoryEngineFactory>());

        Assert.Contains("ICuratedMemoryStore", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseMemoryComposer_backs_the_prompt_composer_with_the_named_engine()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("chat", e => e.UseLexical())
            .UseMemoryComposer("chat"));

        var store = sp.GetRequiredService<IMemoryStore>();
        await store.RememberAsync("t", "s", "the composer reads this engine");

        var composed = await sp.GetRequiredService<IPromptComposer>()
            .ComposeAsync("BASE", "t", "s", "composer");

        Assert.Contains("the composer reads this engine", composed, StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_no_engine_leaves_the_existing_composer_in_place()
    {
        using var sp = Build(_ => { });
        Assert.IsType<MemoryPromptComposer>(sp.GetRequiredService<IPromptComposer>());
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `node devtools/dev.mjs test --filter "~MemoryEngineRegistration"`
Expected: compile failure — `IMemoryEngineFactory`, `AddMemory`, `AddMemoryEngine`, `UseMemoryComposer`,
`MemoryEngineBuilder` do not exist.

- [ ] **Step 3: Write the factory**

```csharp
namespace Lyntai.Memory;

/// <summary>Resolves memory engines by name, the way <c>IHttpClientFactory</c> resolves clients — the
/// pattern .NET consumers already know.
/// <para><b>Where the analogy stops:</b> <c>CreateClient</c> returns a NEW client each call over pooled
/// handlers, whereas an engine is a stateless singleton over its stores, so the same instance comes back
/// every time. Hence <see cref="Get(string)"/> rather than <c>Create</c>.</para></summary>
public interface IMemoryEngineFactory
{
    /// <summary>The engine registered under <paramref name="name"/>.</summary>
    /// <exception cref="KeyNotFoundException">No engine has that name; the message lists the registered
    /// ones.</exception>
    IMemoryEngine Get(string name);

    /// <summary>The default engine — the one named "default", or the only one when exactly one is
    /// registered.</summary>
    /// <exception cref="KeyNotFoundException">Neither holds.</exception>
    IMemoryEngine Get();

    /// <summary>Look one up without throwing.</summary>
    bool TryGet(string name, out IMemoryEngine engine);

    /// <summary>Every registered name, including hierarchical member names.</summary>
    IReadOnlyList<string> Names { get; }
}

/// <summary>The default <see cref="IMemoryEngineFactory"/> over the registered engine collection.</summary>
public sealed class MemoryEngineFactory : IMemoryEngineFactory
{
    private readonly Dictionary<string, IMemoryEngine> _byName;

    /// <summary>Index <paramref name="engines"/> by name, rejecting duplicates.</summary>
    /// <exception cref="InvalidOperationException">Two engines share a name.</exception>
    public MemoryEngineFactory(IEnumerable<IMemoryEngine> engines)
    {
        ArgumentNullException.ThrowIfNull(engines);
        _byName = new Dictionary<string, IMemoryEngine>(StringComparer.Ordinal);
        foreach (var engine in engines)
            if (!_byName.TryAdd(engine.Name, engine))
                throw new InvalidOperationException(
                    $"Two memory engines are named '{engine.Name}'. Names must be unique — an engine is " +
                    "addressed by name, so a duplicate makes one of them unreachable.");
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Names => [.. _byName.Keys];

    /// <inheritdoc />
    public IMemoryEngine Get(string name) =>
        _byName.TryGetValue(name, out var engine)
            ? engine
            : throw new KeyNotFoundException(
                $"No memory engine named '{name}'. Registered: {string.Join(", ", _byName.Keys)}.");

    /// <inheritdoc />
    public IMemoryEngine Get() =>
        _byName.TryGetValue("default", out var byDefault) ? byDefault
        : _byName.Count == 1 ? _byName.Values.First()
        : throw new KeyNotFoundException(
            _byName.Count == 0
                ? "No memory engine is registered. Call AddMemory() or AddMemoryEngine(name, …)."
                : $"No engine named 'default', and {_byName.Count} are registered — ask for one by name. " +
                  $"Registered: {string.Join(", ", _byName.Keys)}.");

    /// <inheritdoc />
    public bool TryGet(string name, out IMemoryEngine engine) => _byName.TryGetValue(name, out engine!);
}
```

- [ ] **Step 4: Write the builder**

`MemoryEngineBuilder` collects member FACTORIES rather than instances, so nothing is constructed until the
container is built.

```csharp
using Lyntai.Memory.Engines;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Memory;

/// <summary>Collects the members of one named engine inside
/// <c>AddMemoryEngine("name", e => …)</c>. <c>Use*</c> order is the render order; authoritative material
/// renders first regardless of position.</summary>
public sealed class MemoryEngineBuilder
{
    private readonly List<(string Label, Func<IServiceProvider, string, IMemoryEngine> Build)> _members = [];

    internal MemoryEngineBuilder(string name) => Name = name;

    internal string Name { get; }

    internal MemoryCompositionOptions Composition { get; private set; } = new();

    private int _reserve;

    /// <summary>Draw on the keyword <see cref="IMemoryStore"/>. Associative.</summary>
    public MemoryEngineBuilder UseLexical(string label = "lexical")
    {
        _members.Add((label, (sp, full) => new LexicalMemoryEngine(full, Required<IMemoryStore>(sp),
            sp.GetService<ILogger<LexicalMemoryEngine>>())));
        return this;
    }

    /// <summary>Draw on meaning-based <see cref="ISemanticMemory"/>. Associative. Needs an embedder
    /// (<c>AddSemanticMemory</c>).</summary>
    public MemoryEngineBuilder UseSemantic(string label = "semantic")
    {
        _members.Add((label, (sp, full) => new SemanticMemoryEngine(full, Required<ISemanticMemory>(sp),
            logger: sp.GetService<ILogger<SemanticMemoryEngine>>())));
        return this;
    }

    /// <summary>Draw on the operator-curated catalog. AUTHORITATIVE.</summary>
    public MemoryEngineBuilder UseCurated(string kind = "memory", string label = "curated")
    {
        _members.Add((label, (sp, full) => new CuratedMemoryEngine(full,
            Required<ICuratedMemoryStore>(sp), kind, sp.GetService<ILogger<CuratedMemoryEngine>>())));
        return this;
    }

    /// <summary>Total characters this engine's composed sections may use.</summary>
    public MemoryEngineBuilder Budget(int characters)
    {
        Composition = Composition with { Budget = characters };
        return this;
    }

    /// <summary>Characters reserved for authoritative material, allocated before any associative content
    /// is admitted. Applies to the engine, and is stated after the member it is meant for purely for
    /// readability.</summary>
    public MemoryEngineBuilder Reserve(int characters)
    {
        _reserve = characters;
        Composition = Composition with { AuthoritativeReserve = characters };
        return this;
    }

    /// <summary>Materialize the engine. Called by <c>AddLyntai</c> when the container is built, never at
    /// configure time — so a missing backing store surfaces as a startup failure naming the store.</summary>
    internal IMemoryEngine Build(IServiceProvider sp)
    {
        var duplicate = _members.GroupBy(m => m.Label, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Memory engine '{Name}' has two members labelled '{Name}/{duplicate.Key}'. Pass an " +
                $"explicit label (e.g. UseCurated(kind: \"style\", label: \"style\")) so every entry's " +
                "reference names exactly one owner.");

        var built = _members
            .Select(m => m.Build(sp, $"{Name}/{m.Label}"))
            .ToList();

        return built.Count == 1 && _reserve == 0
            ? built[0]
            : new CompositeMemoryEngine(Name, built, sp.GetService<ILogger<CompositeMemoryEngine>>());
    }

    /// <summary>Members are addressable individually, so the factory indexes them too.</summary>
    internal IEnumerable<IMemoryEngine> BuildMembers(IServiceProvider sp) =>
        _members.Select(m => m.Build(sp, $"{Name}/{m.Label}"));

    private static T Required<T>(IServiceProvider sp) where T : class =>
        sp.GetService<T>() ?? throw new InvalidOperationException(
            $"A memory engine member needs {typeof(T).Name}, which is not registered. Wire a storage " +
            $"backend (e.g. UseSqliteStorage(...)) or register your own {typeof(T).Name} before AddLyntai.");
}
```

**Watch the single-member case.** `Build` returns the bare member when there is exactly one and no reserve,
so `AddMemoryEngine("chat", e => e.UseLexical())` yields an engine named `chat/lexical`, not `chat` — which
would fail the "addressable by name" test. Fix it by always wrapping in a composite when the engine name
differs from the single member's name. Change the return to:

```csharp
        return new CompositeMemoryEngine(Name, built, sp.GetService<ILogger<CompositeMemoryEngine>>());
```

and delete the `built.Count == 1` special case entirely. A one-member composite costs one indirection and
keeps naming, routing and `Supported` uniform.

- [ ] **Step 5: Write the prompt-composer adapter and the registration**

`EngineBackedPromptComposer.cs`:

```csharp
using Lyntai.Cortex;

namespace Lyntai.Memory;

/// <summary>Backs the existing <see cref="IPromptComposer"/> with a named engine, so
/// <c>ChatOrchestrator</c> composes from it instead of from the flat memory dump.</summary>
public sealed class EngineBackedPromptComposer(IMemoryEngine engine, MemoryCompositionOptions options)
    : IPromptComposer
{
    /// <inheritdoc />
    public Task<string> ComposeAsync(string basePrompt, string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default) =>
        engine.ComposeAsync(basePrompt, new MemoryQuery(taskKey, scope, query, limit), options, ct);
}
```

`MemoryEngineRegistration.cs`:

```csharp
using Lyntai.Cortex;
using Lyntai.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai;

/// <summary>Registers named memory engines inside <c>services.AddLyntai(cfg => …)</c>.</summary>
public static class MemoryEngineRegistration
{
    /// <summary>Register one working engine with no further configuration — lexical plus semantic when an
    /// embedder is wired, lexical alone otherwise — and back <see cref="IPromptComposer"/> with it. The
    /// one-line path: a seam is an escape hatch, never the answer to "how does this work".</summary>
    public static LyntaiBuilder AddMemory(this LyntaiBuilder builder, string name = "default") =>
        builder.AddMemoryEngine(name, e => e.UseLexical()).UseMemoryComposer(name);

    /// <summary>Register a named engine. Several coexist; address them through
    /// <see cref="IMemoryEngineFactory"/>.</summary>
    public static LyntaiBuilder AddMemoryEngine(this LyntaiBuilder builder, string name,
        Action<MemoryEngineBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var engineBuilder = new MemoryEngineBuilder(name);
        configure?.Invoke(engineBuilder);
        if (!engineBuilder.HasMembers) engineBuilder.UseLexical();

        // NOT TryAdd: a TryAddSingleton reached during configure(builder) beats AddLyntai's own later
        // registration, which once silently swapped a configured DeadHostTracker for parameterless
        // defaults and was missed by 1427 tests. Plain AddSingleton into the collection, resolved by the
        // factory, has no such ordering hazard.
        builder.Services.AddSingleton<IMemoryEngine>(sp => engineBuilder.Build(sp));
        foreach (var index in Enumerable.Range(0, engineBuilder.MemberCount))
            builder.Services.AddSingleton<IMemoryEngine>(sp => engineBuilder.BuildMember(sp, index));

        builder.Services.AddSingleton<IMemoryEngineFactory>(sp =>
            new MemoryEngineFactory(sp.GetServices<IMemoryEngine>()));
        return builder;
    }

    /// <summary>Back <see cref="IPromptComposer"/> — what <c>ChatOrchestrator</c> composes with — using the
    /// named engine. Without this, the existing flat composer stays in place.</summary>
    public static LyntaiBuilder UseMemoryComposer(this LyntaiBuilder builder, string name)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IPromptComposer>(sp =>
        {
            var factory = sp.GetRequiredService<IMemoryEngineFactory>();
            return new EngineBackedPromptComposer(factory.Get(name), CompositionOf(sp, name));
        });
        return builder;
    }

    private static MemoryCompositionOptions CompositionOf(IServiceProvider sp, string name) =>
        sp.GetServices<MemoryEngineComposition>()
            .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal))?.Options
        ?? new MemoryCompositionOptions();
}

/// <summary>Carries a named engine's composition options into the container, so
/// <see cref="MemoryEngineRegistration.UseMemoryComposer"/> can find them without a second builder pass.</summary>
/// <param name="Name">The engine's name.</param>
/// <param name="Options">Its composition options.</param>
public sealed record MemoryEngineComposition(string Name, MemoryCompositionOptions Options);
```

Add to `MemoryEngineBuilder`: `internal bool HasMembers => _members.Count > 0;`,
`internal int MemberCount => _members.Count;`, and
`internal IMemoryEngine BuildMember(IServiceProvider sp, int index)` returning
`_members[index].Build(sp, $"{Name}/{_members[index].Label}")`. Register the
`MemoryEngineComposition` inside `AddMemoryEngine` with
`builder.Services.AddSingleton(new MemoryEngineComposition(name, engineBuilder.Composition));`.

Registering `IMemoryEngineFactory` once per `AddMemoryEngine` call is harmless — last registration wins for
a single-service resolve, and each closure resolves the same full collection. `AddSingleton<IPromptComposer>`
in `UseMemoryComposer` is deliberately **not** `TryAdd`: `RegisterCortex` already `TryAdd`s
`MemoryPromptComposer`, and a plain `AddSingleton` here is the last registration, so it wins.

- [ ] **Step 6: Run to verify they pass**

Run: `node devtools/dev.mjs test --filter "~MemoryEngineRegistration"`
Expected: PASS, 9 matched.

- [ ] **Step 7: Prepare the commit (ask first)**

```bash
git add src/Lyntai.Core/Memory tests/Lyntai.Tests/Memory
git commit -m "feat(memory): resolve named engines through a factory, with a one-line AddMemory"
```

---

### Task 6: API surface baseline, docs, changelog

**Files:**
- Modify: `tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt` (regenerate)
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Modify: `TASKS.md` (nothing yet — MEM1 stays open until MEM2 lands? No: MEM1 closes here)
- Modify: `docs/task-archive.md`

- [ ] **Step 1: Regenerate the API surface baseline**

Run: `node devtools/dev.mjs test --filter "~ApiSurface"`
Expected: **FAIL** on `Lyntai.Core` — the surface grew. Then copy the emitted `.actual` file over the
baseline:

```bash
cp tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt.actual tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt
rm tests/Lyntai.Tests/Api/Baselines/Lyntai.Core.txt.actual
```

**Read the diff before accepting it.** The baseline is the review gate that makes an app-specific leak
visible; a mechanical overwrite defeats it. Every added line should be a type this plan introduced, and
nothing should have been removed.

- [ ] **Step 2: Verify the whole gate**

Run: `node devtools/dev.mjs verify`
Expected: all seven checks pass — build, warnings, packages, bundle, test, e2e, leak scan. `check-packages`
and `check-bundle` should be unchanged, since no package was added and Core took no new dependency.

- [ ] **Step 3: Write the changelog entry**

Under `## Unreleased` in `CHANGELOG.md`, in the **Added** section:

```markdown
- **Named memory engines** (`IMemoryEngine`, `IMemoryEngineFactory`, `AddMemory()` /
  `AddMemoryEngine(name, …)`) — several memory systems can now coexist in one application and are resolved
  by name, the way `IHttpClientFactory` resolves clients. Wrappers adapt the three existing stores; a blend
  is itself an engine, so naming and blending are one concept. Composition renders exact (authoritative)
  and recalled (associative) material as separate labelled sections, with a reserved character budget for
  the former — so a burst of loosely-relevant recall can no longer push a hard constraint out of the prompt.
  An authoritative write is routed to a member that can hold it or throws; it is never silently downgraded.
  Purely additive: `IMemoryStore`, `ISemanticMemory`, `ICuratedMemoryStore` and `MemoryPromptComposer` are
  unchanged, and an application that never calls `AddMemory` sees no difference.
```

- [ ] **Step 4: Add a README section**

Under the memory documentation, add the `AddMemory()` one-liner and the named/blended example from Spec A
§5.2. Keep it to the shape a consumer copies; the reasoning lives in the spec.

- [ ] **Step 5: Archive the task**

Use the `archive-task` skill: cut **MEM1** out of `TASKS.md` Part 46, append it to `docs/task-archive.md`
with the completion date and a one-line outcome. Leave **MEM2** and **MEM-TUNE** open — the summary at the
top of Part 46 must not claim the part is done.

- [ ] **Step 6: Prepare the commit (ask first)**

```bash
git add tests/Lyntai.Tests/Api/Baselines README.md CHANGELOG.md TASKS.md docs/task-archive.md
git commit -m "docs(memory): regenerate the API baseline and record MEM1"
```

---

## Self-Review

**Spec coverage.** §3.1 types → Task 1. §3.2 capabilities → Task 1 (declared), Task 4 (routed). §3.3
factory → Task 5. §3.4 composite + hierarchical names → Tasks 4–5. §4 grade, routing, reserve, rendering →
Tasks 1–4. §5.1 zero config → Task 5. §5.2 builder → Task 5. §5.3 knob budget → `MemoryCompositionOptions`
+ builder methods, Tasks 3 and 5. §5.4 both registration traps → Task 5. §6 error handling → the fail-open
paths in Tasks 1–4. §7 packaging → Task 6. §8 testing → every task's test step; the accuracy test is Task 3,
capability forwarding is Task 4, determinism is Task 3.

**Deliberately not covered here, and why:** §5.1's "graph if available" arm and every `MemorySources.Graph`
/ `Similarity` path need `IMemoryGraphStore`, which MEM2 introduces. The enum values exist from Task 1 so
MEM2 adds no public surface to the enum, but nothing sets them yet.

**Known rough edge to settle during Task 5:** `Reserve` currently reads as per-member in the fluent chain
(`.UseCurated(...).Reserve(1200)`) but is stored per-engine. That matches Spec A §4.3, where the reserve is
an engine-level allocation for authoritative material — but the chaining position is misleading. Either
document it on the method (the plan's XML doc does) or move it out of the member chain. Do not silently
make it per-member; that is a different budget model and would need a spec amendment.
