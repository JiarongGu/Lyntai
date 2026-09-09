---
name: rule-template
applies_when: writing a new rule or knowledge document — this file is a TEMPLATE to copy, never a rule that applies
enforces: default to the knowledge tier; name the file for what is enforced; give it frontmatter and add its index row
---

# {Rule Title — imperative, what is enforced (not the incident that caused it)}

**One-sentence summary of what is enforced.**

## Why

The reason this rule exists — a past incident, constraint, or strong preference. Future sessions need this
to judge edge cases instead of blindly following the rule.

## How to apply

When it kicks in and what to do: the trigger (file pattern / task type / keyword), the prescribed action,
and the edge cases where it does NOT apply.

## Related

- Links to other rules / docs that interact with this one.

---

**When creating a new rule (delete this section from your copy):**

1. **Default to `.claude/knowledge/{kebab-name}.md`** (on-demand deep dive — the usual home). Only put it in
   `.claude/rules/` (always-loaded core) if it's a genuinely universal-workflow rule needed on nearly every
   task — the core stays tiny.
2. **Copy this file to the tier you picked in step 1, then edit the copy** — including the
   `name`/`applies_when`/`enforces` frontmatter above, which is what the discovery workflow matches on.
   Without it the file renders as `⚠ needs frontmatter` in the index and no skill can route to it. It lives
   under `.claude/templates/` precisely so that copying is a deliberate act: nothing there is loaded, and a
   template sitting in the always-on tier is prose every session pays for and no session follows.
3. **Add a row to `.claude/rules/RULES_INDEX.md`** — the index is what the discovery workflow reads, so a
   rule missing from it is a rule nothing routes to. The path is spelled out because this template no
   longer sits beside it.
4. Name it for *what is enforced*, kebab-case (e.g. `no-global-memory.md`, not `fix-2026-07-bug.md`).
