---
name: sql-storage
applies_when: writing a query, adding a migration, or touching full-text search
enforces: cast affinity-typed columns on read; never reuse a migration number; trigram FTS for non-Latin text; open connections with explicit pragmas
---

# SQL storage — the traps that return wrong data rather than failing

Hand-written SQL over a micro-ORM is the right default for a library: it is predictable, reviewable, and
has no query-generation surprises. The traps below are the ones that do not throw — they return a wrong
value, skip a migration, or silently find nothing, and every one produced a bug that looked like something
else: a score truncated to zero, a table never created, a search that returned nothing for a language
written without spaces — and "no results" reads like "no data", not like "broken index".

**Each is stated once, in `storage.md`, with this repository's concrete fix:**

| Trap | Where |
|---|---|
| A dynamically typed column reads back as an integer and truncates | `storage.md` §The integer-affinity trap |
| A mapper's type map and handler registry are PROCESS-GLOBAL, so two backends in one process share them | `storage.md` §Dapper + snake_case |
| A connection opened outside the factory silently loses foreign-key enforcement | `storage.md` §Per-connection pragmas |
| A reused migration number is skipped silently; a renumbered one re-runs | `storage.md` §Migrations |
| A structure correct only for rows written after it shipped | `storage.md` §Migrations |
| A word-boundary tokenizer finds nothing in a script written without spaces; an index kept by app code drifts | `storage.md` §FTS5 trigram external-content |
| Raw user text passed to a full-text matcher; ranking left to insertion order | `storage.md` §FTS5 trigram external-content |
| A bare `catch (OperationCanceledException)` turns a fail-open store fail-closed | `storage.md` §Conventions |
