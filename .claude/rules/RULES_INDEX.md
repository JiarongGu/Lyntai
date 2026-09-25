# RULES_INDEX — the routing table for the discovery workflow

Every rule, knowledge document and skill below is this repository's own. `doc-loader` and `skills-workflow`
route off the **applies when** column, so a document missing from this table is a document nothing routes
to — add a row when you add one.

**Core rules: all twelve of `.claude/rules/*.md` are auto-loaded IN FULL every session.** Read them; do not
look them up here. A table summarizing documents already in context is a second, worse copy.

## Knowledge (read on demand — NOT auto-loaded)

| Document | Applies when |
|---|---|
| [pitfalls](../knowledge/pitfalls.md) | **before extending or refactoring anything** — the traps that pass the build, and usually the tests, while being wrong |
| [extending-lyntai](../knowledge/extending-lyntai.md) | adding a provider, generation backend, storage backend, scorer, CLI backend, or migration |
| [llm-and-router](../knowledge/llm-and-router.md) | the router, a provider, the front door, streaming, cooldown, admission, or the CLI process runner |
| [storage](../knowledge/storage.md) | writing SQL, adding a migration, or extending a `Lyntai.Storage.*` backend |
| [sql-storage](../knowledge/sql-storage.md) | a query, a migration, or full-text search — the traps that return wrong data rather than failing |
| [library-api-design](../knowledge/library-api-design.md) | designing or changing any public API, or when a consumer asks for a feature |
| [generic-library](../knowledge/generic-library.md) | a task arrives as "app X needs Y" — any consumer-requested feature or new public surface |
| [memory](../../docs/memory.md) | touching `Lyntai.Memory*`, an `IMemory*Policy`, a memory engine's wiring, or a store's graph members — the memory CONTRACT, headed by the five invariants no gate holds |
| [model-decoupling](../knowledge/model-decoupling.md) | any feature that uses — or could use — a language model, an embedder, or any AI service |
| [input-is-thinking-not-doctrine](../knowledge/input-is-thinking-not-doctrine.md) | recording something the owner said into a spec, a decision, a schema, or a commit |

## Skills (invoke by name)

| Skill | Use when |
|---|---|
| [doc-loader](../skills/doc-loader/SKILL.md) | the START of any non-trivial task — load what it actually touches |
| [pattern-finder](../skills/pattern-finder/SKILL.md) | before writing a unit shaped like something already here — find the exemplar |
| [post-feature](../skills/post-feature/SKILL.md) | the implementation looks done — audit every layer it touched |
| [fix-log](../skills/fix-log/SKILL.md) | after landing a non-trivial fix — record root cause, fix, verification |
| [archive-task](../skills/archive-task/SKILL.md) | a `TASKS.md` item is complete and needs moving into the archive |
| [add-provider](../skills/add-provider/SKILL.md) | a new `IModelProvider`, or bridging an `Microsoft.Extensions.AI` `IChatClient` |
| [add-storage-backend](../skills/add-storage-backend/SKILL.md) | a `Lyntai.Storage.*` package over one or more domain interfaces |
| [add-migration](../skills/add-migration/SKILL.md) | a schema change — numbering, SQLite constraints, the FTS trigger pattern |
| [add-scorer](../skills/add-scorer/SKILL.md) | a new `IScorer`, deterministic or an LLM judge |
| [caveman](../skills/caveman/SKILL.md) | asked to be brief — terse output that keeps every technical detail exact |

The rule template is `.claude/templates/rule-template.md` — a thing to COPY, not a rule to follow, which is
why it is not in the always-on tier.
