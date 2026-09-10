---
name: dotnet-package-layout
description: Contract in core, implementation in an adapter, never adapter-to-adapter; split by dependency footprint; DI collections over conditionals; naming.
applies_when: adding or moving a project, naming a public type, or adding a variation point
---

# .NET library layout — boundaries, names, and variation points

A published library's package graph is a promise to consumers, and **every boundary in it is paid for by
someone else's build.** Each rule below was violated once and the cost landed downstream.

## How to apply

### Package boundaries

- **Contract in the core package, implementation in an adapter.** Every abstraction lives in the core
  package; concrete implementations live in adapter packages that depend only on core (or on one domain
  package). **Never adapter-to-adapter.** This is what lets a new backend be a new package rather than a
  fork.
- **Split by dependency footprint, not by vendor.** Every boundary must answer: *which dependency does
  this isolate?* Implementations needing nothing extra share one package. An implementation gets its own
  package the moment it drags in a native runtime, a platform-specific API, or anything a consumer might
  refuse.
- **The core package is mandatory, so it carries the smallest footprint of all** — never add a third-party
  dependency to it. "Most consumers want this" argues for a metapackage, never for a core dependency.
- **Merging or moving packages must not change namespaces.** A consumer should edit one package reference,
  never an import.

### Naming

- **Never name a type for the layer it crosses.** No `Dto`, no `Model` used as a suffix for "bag of fields
  in transit" — a name says what the thing *is* in the domain. Say "the row type" or "the request record"
  in prose too, or the name creeps back in on the next change.
- Reach for the established suffix vocabulary: `*Options` (configuration), `*Request` / `*Reply` (a call's
  in and out), `*Result` (an operation's outcome), `*Entry` / `*Record` (a stored item), `*Row` (a
  materialization type), `*Event`, `*Args`, `*Policy`.
- Otherwise standard conventions: interfaces prefixed `I`, awaitables suffixed `Async`, PascalCase
  members, underscore-prefixed camelCase private fields.

### Variation points

- **A pluggable set is a DI collection, never a conditional.** Resolve `IEnumerable<T>` and iterate. Adding
  one is a new class plus one registration — never an edit to a `switch` or an `if` chain. **If adding a
  backend requires editing existing code, the seam is in the wrong place.**
- **The public entry point is a single registration extension.** Consumers compose through dependency
  injection; nothing is constructed by hand.

### Shipping a package

- **A new package must enter every registry that governs it** — the solution, the packable-project list,
  the API-surface baseline, the test project's references, the documentation tables. **The misses are
  silent**: a package missing from the API-surface list simply has no API gate, and nothing reports that.
  Automate the check; a checklist in someone's head is not one.
- **A warning in a published project is a defect, not style.** An unfailed trimming or AOT warning is a
  false promise shipped to consumers, and an unresolved documentation reference ships inside the
  documentation they read.
