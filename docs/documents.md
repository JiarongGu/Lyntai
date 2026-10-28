# Lyntai — the documents

Every document a session may need, what kind it is, and whether it still stands. Read at a task's start.
A document not listed here is one no session will find. When a document and a later decision disagree,
the decision wins, and the row says so. The knowledge documents are listed in `.agents/INDEX.md`,
generated, and are not repeated here.

**Kinds.** A *contract* is what a part is and must keep being; it is built against. A *method* is how
something gets built. A *study* is input to a decision, read for its reasoning. *Evidence* is what was
measured, and it stays a record of that moment. A *record* is append-only and read by lookup.

| Document | Kind | For | Where it stands |
|---|---|---|---|
| `docs/2026-07-17-lyntai-design.md` | contract | the design contract — semantics; the API baselines own shape: read the section you touch | current, amended by the decisions it cites |
| `tests/Lyntai.Tests/Api/Baselines/` | contract | the frozen public surface, one file per assembly | current |
| `docs/memory.md` | contract | the memory CONTRACT, headed by the five invariants no gate holds | current |
| `docs/memory-measurements.md` | evidence | the memory EVIDENCE, behind a generated results index whose `ships` and status columns are the two things read wrongly here (D114) | each result a record of its run |
| `docs/agent-provider-layer.md` | study | the agent-provider layer: the evidence, the placement, the contract and the rows (D219; `TASKS.md` Part 376) | proposal; AG1 to AG8 built, AG9 open |
| `docs/vnext-modular-capabilities.md` | study | the vNext RFC mapped item by item against what exists, and where the records win (D218) | proposal folded; the rulings are D220 and D221, VN2 is answered by D224, and what remains is `TASKS.md` Part 375 |
| `docs/model-tasks.md` | study | every model-backed seam by the SHAPE of the question it asks | current |
| `docs/deployment-shapes.md` | study | the same evidence cut by the shape of the deployment | current |
| `docs/glossary.md` | contract | the namespace map and the vocabulary a rename must keep | current |
| `README.md` | contract | the landing page every package ships, linked ABSOLUTELY (D197) | current |
| `docs/AOT.md` | contract | trimming and Native AOT status, per package | current |
| `docs/guide.md` | method | the consuming story | current |
| `docs/generation.md` | method | the media generation guide | current |
| `docs/extensions-ai.md` | method | composing with `Microsoft.Extensions.AI` and Agent Framework: the two recipes the suite runs, and what they do not carry | current |
| `docs/GATES.md` | method | what each gate is for and the numbers it holds | current |
| `daoris.lanes.json` | method | the lanes work is dispatched by, the paths each owns, and what is laneless on purpose (D214); `check-lanes` holds it to the tree | current |
| `docs/DECISIONS.md` | record | the rationale log, present tense, contiguous `D1..Dn`; read §How to read it and its generated index | current |
| `TASKS.md` | record | the OPEN backlog, whose generated roster is the live work: read it, never a copy kept elsewhere | current |
| `docs/task-archive.md` | record | the closed backlog, one Part per task | current |
| `docs/FIXES.md` | record | the fix log | current |
| `CHANGELOG.md` | record | per-release detail | current |
| `docs/ROADMAP.md` | record | one line per version | current |
| `docs/superpowers/INDEX.md` | record | the tracked list of specs and plans, whose records live untracked in `local/superpowers/` | current |
