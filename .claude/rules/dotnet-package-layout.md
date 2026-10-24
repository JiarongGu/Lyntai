---
name: dotnet-package-layout
description: Contract in core, implementation in an adapter, never adapter-to-adapter; split by dependency footprint; DI collections over conditionals; naming.
applies_when: adding or moving a project, naming a public type, or adding a variation point
---

# .NET library layout — this repository's binding

**The rule is the shared `dotnet-package-layout` (`AGENTS.md`); its concrete names here are
`repo-mechanics.md` §Package layout and §Naming. One line OVERRIDES it.**

## Naming

**`*Response`, never `*Reply`**: one suffix for what a CALL returns, because two spellings for one role <!-- drift-ok: the rule quotes the suffix it bans -->
is how the text and media sides came to name the same thing differently. `*Result` is an operation's
outcome, not a call's.
