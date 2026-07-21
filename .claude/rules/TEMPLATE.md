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
2. Copy this template, replace the content.
3. **Add one row to `RULES_INDEX.md`** — otherwise the rule is invisible to the discovery workflow.
4. Name it for *what is enforced*, kebab-case (e.g. `no-global-memory.md`, not `fix-2026-07-bug.md`).
