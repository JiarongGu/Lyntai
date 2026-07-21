# No global memory for project facts — keep them in-repo

**NEVER use global auto-memory (`~/.claude/projects/*/memory/`) for anything project-related.** Global
memory is reserved ONLY for **user-specific** preferences (role, communication style, personal settings)
that are true across every project.

Everything about *Lyntai* — conventions, decisions, invariants, workflow corrections, gotchas — lives
**in the repo**, version-controlled, reviewable, and shared with anyone who clones it. A fact in global
memory is invisible to teammates, unversioned, and silently loaded; a fact in the repo is none of those.

## What goes where

| Information | Home |
|---|---|
| User role / communication style / personal prefs | `~/.claude/projects/*/memory/` (global) — the ONLY thing that belongs there |
| Always-on universal convention (package layout, spawn hygiene, no-leak, task lifecycle) | `.claude/rules/*.md` (auto-loaded core) |
| On-demand deep dive (llm/router, storage, pitfalls, extension how-to) | `.claude/knowledge/*.md` (discovered + `Read` via `RULES_INDEX.md`) |
| A load-bearing DECISION + why (so it isn't relitigated/reverted) | `docs/DECISIONS.md` |
| The interface/semantics contract | `docs/2026-07-17-lyntai-design.md` |
| Release-facing, user-visible change | `CHANGELOG.md` |

## How to apply

- **Learned something durable about Lyntai?** Write it to the repo (the table above), NOT to global
  memory. If it's a genuinely new convention add a `.claude/knowledge/*.md` + a one-line `RULES_INDEX.md`
  row (or a `.claude/rules/*.md` for a universal one); if it's a decision, add a `D<n>` to `docs/DECISIONS.md`.
- **Tempted to `Write` a `type: project`/`type: feedback` memory file?** Don't — that's a repo doc. Only
  `type: user` (cross-project personal prefs) may go to global memory.
- **Keep global memory empty of project facts.** Migrate any that leak in (see the 2026-07-22 migration:
  the old `lyntai-*` memories moved into `docs/DECISIONS.md` D6–D15 and the `.claude/` rules/knowledge).
- The library's OWN memory subsystem (`IMemoryStore`/`ICuratedMemoryStore`/`ISemanticMemory`) is a
  separate thing entirely — this rule is about the *assistant's* memory, not the product's.
