# RULES_INDEX — the routing table for the discovery workflow

Every rule, knowledge document and skill below is this repository's own. `doc-loader` and `skills-workflow`
route off the **applies when** column, so a document missing from this table is a document nothing routes
to — add a row when you add one, and keep the wording in step with the file's own frontmatter.

`TEMPLATE.md` is deliberately absent: it is the template for writing a new rule, not a rule to follow.

## Core (always loaded)

| Rule | Applies when | Enforces |
|---|---|---|
| [code-commentary](code-commentary.md) | writing or reviewing any comment — an XML doc on a public member, or a `//` note on a line of code | three tiers, three jobs — the XML doc is the CONTRACT, a `//` comment ANNOTATES the code beneath it, and the DESIGN argument lives in a record; prose that outgrows the code it explains has stopped being a comment |
| [dotnet-package-layout](dotnet-package-layout.md) | adding or moving a project, naming a public type, or adding a variation point | contract in core, implementation in an adapter, never adapter-to-adapter; split by dependency footprint; DI collections over conditionals |
| [file-tool-discipline](file-tool-discipline.md) | inspecting files, or running a destructive or irreversible command | use the dedicated read/search/find tools, not shell equivalents; never route a command through a side channel to skip approval |
| [no-global-memory](no-global-memory.md) | about to save a project fact to the assistant's global or cross-project memory | project facts live in the repository, versioned and reviewable; global memory is user preferences only |
| [no-tmp-for-repo-files](no-tmp-for-repo-files.md) | composing a repository file, or needing a scratch, probe, or dump file | compose finals with the file-writing tools; scratch goes in a gitignored repo directory, never OS temp |
| [persist-working-state](persist-working-state.md) | any multi-step task — at each decision, finding, or milestone | checkpoint in-progress state to its durable home in the repository as you go, not at the end |
| [repo-mechanics](repo-mechanics.md) | applying a general rule in this repository — package names, version authorship, the dev loop, guard scripts, or scratch paths | contract in Lyntai.Core with adapters, never adapter-to-adapter; zero Dto identifiers; never hand-edit VersionPrefix or the Unreleased heading, and never NAME a version that has not shipped; scratch under `devtools/_*` |
| [sensitive-info](sensitive-info.md) | writing any tracked file or commit message, or rewriting history | no machine paths, no private repo names, no credentials; a committed leak is a history problem |
| [skills-workflow](skills-workflow.md) | starting any non-trivial task, and whenever a follow-up changes its scope | run the discovery skills before exploring code, actually read what they route you to, and re-run them when the scope moves |
| [task-lifecycle](task-lifecycle.md) | adding or finishing a task, editing the backlog, or labelling something blocked | the backlog holds OPEN work only and never summarizes the archive; a finished task MOVES to it; a blocked item names its blocker's KIND and is re-checked against that kind |
| [windows-machine](windows-machine.md) | running any shell command, script, or file write on a Windows development machine | never round-trip text through PowerShell 5; BOM and encoding traps; exit codes that lie; never kill a shared runtime by name |

## Knowledge (read on demand)

| Document | Applies when | Enforces |
|---|---|---|
| [extending-lyntai](../knowledge/extending-lyntai.md) | adding an LLM provider, a storage backend, a scorer, a CLI tool-hosting dialect, a generation backend, or a migration | an interface in Lyntai.Core + an implementation in an adapter (never adapter→adapter) + one `LyntaiBuilder` extension; a new package only when the dependency footprint earns one |
| [generic-library](../knowledge/generic-library.md) | a task arrives as "app X needs Y" — any consumer-requested feature, or any new public surface | ship the general need, never the consumer's shape; neutral need in Core, provider-specifics in the adapter; additive and defaulted; vary via seams, never `if(appName)` |
| [input-is-thinking-not-doctrine](../knowledge/input-is-thinking-not-doctrine.md) | recording something the owner said into a spec, a decision record, a schema, or a commit | default to recording it as an open working position with its reversal cost, rather than as a settled rule |
| [library-api-design](../knowledge/library-api-design.md) | designing or changing any public API, or a consumer asks for a feature | generalize the request, never ship its shape; no consumer vocabulary in the library; seams over flags; every public type earns its keep |
| [llm-and-router](../knowledge/llm-and-router.md) | touching the router, a provider, the front door, streaming, dead-host cooldown, admission, or the CLI process runner | classify through the one `LlmVerdictClassifier`; a blameless verdict never masks a real failure; no fallback after the first content token; timeouts are inactivity clocks |
| [model-decoupling](../knowledge/model-decoupling.md) | building any feature that uses a language model, an embedding model, or any AI service | specify the feature without naming a model; select the provider by deployment; report which tier ran |
| [pitfalls](../knowledge/pitfalls.md) | before extending or refactoring any area of Lyntai — tooling, LLM/router, provider lifetime, storage, DI, or tests | don't reintroduce the measured traps — each one passed the build (and usually the tests) while being wrong |
| [sql-storage](../knowledge/sql-storage.md) | writing a query, adding a migration, or touching full-text search | cast affinity-typed columns on read; never reuse a migration number; trigram FTS for non-Latin text; open connections with explicit pragmas |
| [storage](../knowledge/storage.md) | writing SQL, adding or changing a migration, or adding/extending a Lyntai storage backend | alias every SELECT and CAST affinity-typed columns; open connections only through the factory; three FTS trigram triggers plus a backfill; never dedup the Sqlite/Postgres pair |

## Skills (invoke by name)

| Skill | Use when |
|---|---|
| [add-migration](../skills/add-migration/SKILL.md) | adding or changing a database schema in `Lyntai.Storage.Sqlite` — safe numbering, SQLite constraints, the FTS trigger pattern |
| [add-provider](../skills/add-provider/SKILL.md) | adding a new LLM provider behind `ILlmProvider`, or bridging an existing `Microsoft.Extensions.AI` `IChatClient` |
| [add-scorer](../skills/add-scorer/SKILL.md) | adding an evaluation scorer to the cortex layer — a new `IScorer`, deterministic or an LLM judge |
| [add-storage-backend](../skills/add-storage-backend/SKILL.md) | adding a `Lyntai.Storage.*` package implementing one or more of the twelve domain interfaces |
| [archive-task](../skills/archive-task/SKILL.md) | a task in `TASKS.md` is complete and needs moving into `docs/task-archive.md` |
| [caveman](../skills/caveman/SKILL.md) | asked to be brief or token-efficient — terse output that keeps every technical detail exact, with safety carve-outs |
| [doc-loader](../skills/doc-loader/SKILL.md) | the START of any non-trivial task — load the documents it actually touches, since knowledge is not auto-loaded |
| [fix-log](../skills/fix-log/SKILL.md) | after landing a non-trivial bug or regression fix — record root cause, fix and verification before moving on |
| [pattern-finder](../skills/pattern-finder/SKILL.md) | before writing a new unit shaped like something already here — find the exemplar to mirror |
| [post-feature](../skills/post-feature/SKILL.md) | the implementation looks done — audit every layer the change touched before proposing a commit |
