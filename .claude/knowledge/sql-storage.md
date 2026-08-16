---
name: sql-storage
applies_when: writing a query, adding a migration, or touching full-text search
enforces: cast affinity-typed columns on read; never reuse a migration number; trigram FTS for non-Latin text; open connections with explicit pragmas
---

# SQL storage — the traps that return wrong data rather than failing

Hand-written SQL over a micro-ORM is the right default for a library: it is predictable, reviewable, and
has no query-generation surprises. The traps below are the ones that do not throw — they return a wrong
value, skip a migration, or silently find nothing.

## Why

Every item here produced a bug that looked like something else. A dynamically-typed column returned an
integer where a fraction was expected and a score silently truncated to zero. A reused migration number
meant a table was never created, with no error. A search index built for Latin word boundaries returned
nothing at all for a language that does not use spaces — and "no results" reads like "no data", not like
"broken index".

## How to apply

### Reading

- **Cast affinity-typed columns explicitly on read.** In a database with dynamic typing, a column holding
  `0` and `1`, or a fraction that happens to be stored whole, can come back as an integer and truncate.
  Cast to the real type in the `SELECT`, not in the caller.
- **Map naming conventions once, at the connection.** Underscore-separated columns to Pascal-case members
  is a one-line setting; doing it per-query with aliases is how one query ends up subtly different.
- **Use a distinct materialization type for rows.** A row type is settable-property plumbing that exists
  to be filled by the mapper; keep it separate from the domain type so the domain type can stay immutable
  and validated.

### Connections

- **Open every connection with the same explicit pragmas** — the journal mode you intend, a busy timeout
  so a concurrent writer waits instead of failing instantly, and foreign-key enforcement on (it is
  commonly *off* by default, which means constraints you wrote are silently not enforced). Put this in
  one factory; a connection opened anywhere else will not have them.

### Migrations

- **Number migrations with a sortable timestamp, and never reuse a number.** A duplicate number that has
  not been applied yet is *skipped silently* — the migration never runs, nothing errors, and the missing
  table surfaces much later as an unrelated failure. Generate the next number with a tool rather than by
  hand.
- **Declare constraints inline at table creation** where the database cannot add them afterwards.
  Composite primary keys are the common case; discovering the limitation after the table exists means a
  table rebuild.
- **Backfill in the same migration that adds the structure.** A structure that is only correct for rows
  written after it shipped is a bug waiting for the first old row.

### Full-text search

- **A word-boundary tokenizer does not work for scripts without spaces.** It treats an entire phrase as
  one token, so a substring search finds nothing — and returns empty rather than erroring. Use a
  character-n-gram tokenizer when the corpus may contain such text.
- **Keep the index in sync with triggers on insert, delete, and update**, and backfill existing rows in
  the same migration. An index maintained by application code drifts the first time a row is written by
  anything else.
- **Build the match expression deliberately** — drop tokens shorter than the n-gram size, quote the rest,
  and fall back to a plain pattern match when nothing usable remains. Passing raw user input to a
  full-text matcher is both a syntax error waiting to happen and a way to return nothing for a query the
  user considers reasonable.
- **Rank explicitly.** Insertion order is not relevance.
