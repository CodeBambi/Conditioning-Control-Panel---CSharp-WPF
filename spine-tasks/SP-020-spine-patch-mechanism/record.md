# SP-020 record — durable pi-spine local-patch mechanism (T-1)

## Step 1 — Empirical patch inventory

**Method:** pristine `npm i pi-spine@2.8.0` in `%TEMP%/sp020-scratch` (exact installed version recorded: **2.8.0**, matching the live tree's version; latest on npm = **2.10.0**, also installed to `%TEMP%/sp020-scratch-210` for anchor drift checks), full recursive `diff -rq` vs the live `E:/Code/Conditioning-Control-Panel/.pi/npm/node_modules/pi-spine`. The worktree's own `.pi/npm` (auto-installed, unpatched 2.8.0 — the T-5 trap materialized as a reference copy) was diffed separately and used only to confirm which tree carries patches: the MAIN repo `.pi` is the patched tree.

### Inventory table (EVERY live-vs-pristine delta)

| # | File (under `.pi/npm/node_modules/pi-spine/`) | Delta vs pristine 2.8.0 | Classification | Load-bearing? |
|---|-----------------------------------------------|--------------------------|----------------|----------------|
| 1 | `src/batch/abort.mjs` | `fs.openSync(archivePath, "r")` → `"r+"` + `// ponytail: Windows EPERMs fsync on read-only handles` (line 60) | **PATCH PRESENT** (fsync) | Yes — Windows fsync on read-only handle EPERMs (port-lessons 2026-07-18) |
| 2 | `src/batch/lifecycle-archive.mjs` | same one-line change (line 30) | **PATCH PRESENT** (fsync) | Yes — same root cause |
| 3 | `skills/create-spine-tasks/SKILL.md` | +2 lines after line 394: T-11 headed-evidence sizing amendment ("dies on pi-spine reinstall, durable copy in spine-tasks/CONTEXT.md §Execution policy") | **PATCH PRESENT** (undocumented 4th local patch — NOT named in the T-1 row; found only by the empirical diff) | Guidance-only (task-authoring prompt), but its own text says it dies on reinstall → included in manifest |
| 4 | `src/batch/evidence-command.mjs` | NO delta — `ALLOWED_EVIDENCE_EXECUTABLES = new Set(["npm", "node", "pnpm", "yarn", "npx"])` (line 16), no `dotnet` | **PATCH LOST** (killed by a past reinstall) | Yes when gate-evidence commands use `dotnet` (this repo's testCommand does) |
| 5 | `bin/spine-worker-runner.mjs` | NO delta — `piArgs.push(tailPrompt)` inline (line 129) | **PATCH LOST** (killed by a past reinstall) | Dormant-load-bearing: tails >16KB go to `%TEMP%` as `@file`; currently masked because referenceDocs scoping (T-4) keeps tails under the 32,767-char CreateProcess limit — recurrence risk stands (SP-004 ×3 silent worker deaths) |
| 6 | `src/batch/journal.mjs` | NO delta — pristine 2.8.0 already has `fs.openSync(filePath, "r+")` (line 200) | **NOISE** — port-lessons line 16 listed journal.mjs among fsync patches; at 2.8.0 upstream already ships `"r+"` | No patch needed |
| 7 | `windowsHide` anywhere in pi-spine | 2 occurrences in `src/process/terminate-tree.mjs`, byte-identical live vs pristine | **ABSENT** — the 86-site spawn mass-patch is not present in pi-spine (nor anywhere else under `.pi/npm`); engine reviews fire without it (T-2 CLOSED, "do not re-litigate") | **Not load-bearing today** — recorded for presence as ordered; excluded from manifest |
| 8 | Version | live = 2.8.0; latest npm = 2.10.0 | **VERSION DRIFT** — all 4 code-site anchors verified byte-identical in 2.10.0 (fsync still `"r"` ×2, allowlist still dotnet-less, tail still inline). None of the fixes upstreamed. SKILL.md anchor region also present in 2.10.0 | Manifest records `testedVersions: ["2.8.0", "2.10.0"]` |

**Honesty notes:** (a) the inventory is the diff, not the T-row text — the T-1 row's "three named patches" empirically decompose into 2 present + 2 lost + 1 undocumented-present + 1 upstream-fixed (journal) + 1 absent-cleared (windowsHide); (b) patches 4/5 being LOST is itself the T-1 problem demonstrated: a past reinstall silently killed them and nothing noticed (the @file loss is invisible while tails stay <32KB; the dotnet loss is masked by T-3 stale-evidence recovery paths running dotnet directly).

### T-12 candidate evaluation (merge-time tracked-ignored scan)

**Code locations:**
- `src/batch/git-helpers.mjs` `filterGitignoredPaths` (line 13; `git check-ignore --no-index --stdin` at line 22) — `--no-index` makes check-ignore report TRACKED files as ignored; every consumer inherits the misclassification.
- `src/batch/engine-lanes/merge.mjs` `tryAutoResolveOutOfScopeMergeConflict` (~lines 205–222) — out-of-scope conflict resolution does `checkout --ours` then, when `filterGitignoredPaths` skips the file, falls back to `git rm --cached -f` — the destructive deletion path for legitimately-tracked `client/tools/**`.
- `src/batch/diagnosis-merge-failure.mjs` `buildGitignoredMergeRepairCommand` — emits the `git rm -r --cached` recovery suggestion the orchestrator twice refused (SP-016/SP-017).

**Decision: STAYS UPSTREAM — not included in the local manifest.** Feasible mechanically (minimal diff: drop `--no-index`, or set-minus `git ls-files --cached` from the ignored set) but NOT safe to include: (1) `filterGitignoredPaths` is a shared helper also feeding lane-commit finalization classification (`lane-commit.mjs:267`, T-5 interplay) — a semantic change ripples beyond the merge site; (2) a merge-path behavioral patch cannot be exercised honestly in scratch without staging a real conflicting merge; (3) the recovery playbook is stable and encoded in steering templates. Upstream fix sketch recorded for the PR: remove `--no-index` (check-ignore then never reports tracked files) or intersect with `git ls-files --cached` before treating a path as gitignored; and `buildGitignoredMergeRepairCommand` must never suggest `git rm --cached` for paths present on the merge target.

### Pre-approach consult (Step 1, solo)

- **Requested route:** solo (council route broken, T-7).
- **ACTUAL answering model:** claude-fable-5 (consistent with requested route).
- **Verdict: APPROVE with corrections.**
  1. Apply pre-validation must be **tri-state with occurrence counting**: anchor present exactly once → applicable; replacement present exactly once → idempotent skip; anything else (anchor absent, anchor >1 occurrence, both present) → fail loudly naming the patch id. *(Applied in apply.mjs/verify.mjs design.)*
  2. fsync replacements must be **byte-exact to the live tree** (incl. the `// ponytail:` comment) or verify.mjs would report the genuinely-present patches as drifted — falsifying the inventory. *(Applied.)*
  3. T-12: exclusion correct, and **reject the text-only variant too** (cost without benefit; orchestrator never auto-executes suggestions; every extra anchor is a future loud break). *(Applied — T-12 stays upstream.)*
  4. @file evidence: check `SPINE_WORKER_PI_AGENT=0` first. **Checked: it exits BEFORE `buildWorkerPiArgs` (runner line 406 vs 415)** — does not traverse the arg builder. Direct invocation of the exported `buildWorkerPiArgs` (exported precisely "for unit tests", runner line 80 comment) is the strongest honest evidence; it is the exact inline-vs-@file decision point feeding the `spawnSync("pi", piArgs)` that hit the 32,767-char limit. **Packet-assumption correction (prominent): `SPINE_WORKER_STUB=1` mode never builds pi args** (stub path writes .DONE directly), so the packet's "stub batch proves @file" framing is empirically wrong — the stub batch proves engine E2E green with patches applied; the @file proof is the arg-builder test + a stub batch whose tail config exceeds 16KB.

## Step 2 — manifest + apply + verify

(pending)

## Step 3 — scratch-cycle evidence

(pending)

## Step 4 — board reconciliation + pre-completion consult

(pending)

## Budgets / surprises

- **Surprise 1:** two of the three T-1-named patches are currently LOST from the live tree (reinstall casualty) — the T-1 problem demonstrated on itself.
- **Surprise 2:** a 4th undocumented local patch exists (SKILL.md T-11 amendment) — found only by the empirical diff.
- **Surprise 3:** the worktree `.pi/npm` is an unpatched 2.8.0 (T-5 trap as reference copy); the running batch is unaffected because the engine runs from the main repo install.
- **Correction:** packet Step 3 assumed a stub batch exercises the worker-tail path; stub mode never builds pi args (verified in runner source).
