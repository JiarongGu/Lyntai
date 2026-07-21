# No OS temp for repo files — compose with Write; scratch + tooling under `devtools/`

**Never write repo-related files (final content OR intermediate scratch) into the OS temp dir (`/tmp`,
`C:\Temp`) or the session scratchpad. Compose final files directly with the `Write`/`Edit` tools; put any
unavoidable scratch/probes/dumps under the git-ignored `devtools/_*`, and any reusable dev tooling under
`devtools/`.**

## Why

Files in OS temp are invisible to the user reviewing the workspace, get orphaned when cleanup is forgotten,
aren't reliably reachable across tool calls (a different root / permission boundary on Windows), and can't
be reviewed in the IDE. Repo-local composition is the right choice from the start. This is this repo's
standing convention (`dev-conventions.md` / `CLAUDE.md`: "scratch → `devtools/_*` (gitignored), never OS
temp") elevated to a focused rule.

## How to apply

- **Final files → the `Write` tool** — `Read` the input pieces, build the composed string, write it once;
  no intermediate files for most cases. (`Write`/`Edit` also sidestep the GBK-console mojibake trap — this
  machine's console mangles CJK/em-dashes through `echo`/`Set-Content`; see `pitfalls.md`.)
- **Unavoidable scratch (probes, fixtures, multi-stage dumps) → `devtools/_*`** (git-ignored — e.g. a
  `devtools/_scratch/`, the e2e harness's `devtools/_e2e-*` data dirs). Clean up at end of task.
- **Reusable dev tooling → `devtools/`** (`dev.mjs`, `devtools/scripts/*`) — visible, reviewable, reusable
  across sessions; it is NOT imported by the library and NOT shipped.
- **Never** `> /tmp/foo && cat /tmp/foo … > repo/file` composition, and never OS temp for repo content.
- OS-temp is fine for genuinely non-repo content (a tool that legitimately needs an OS-temp file).

## Related

- `dev-conventions.md` (the scratch convention), `minimise-bash-prompts.md` (compose with `Write`, not shell
  redirection), `pitfalls.md` (GBK console encoding).
