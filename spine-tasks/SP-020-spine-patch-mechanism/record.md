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

### Engine-review presence log (T-2 heading-format check)

| Call | Step/Type | Result |
|------|-----------|--------|
| 1 | Step 1 plan | `skipped=true, spawnFailed=false, reviewLevel=2` — nested_spawn_blocked BY DESIGN (SP-195/SP-278); reviewLevel parsed 2 correctly (structured heading works); engine runs reviews post-.DONE |
| 2 | Step 2 plan | `skipped=true, spawnFailed=false, reviewLevel=2` — same by-design skip |
| 3 | Step 3 plan | `skipped=true, spawnFailed=false, reviewLevel=2` — same by-design skip |
| 4 | Step 4 plan | `skipped=true, spawnFailed=false, reviewLevel=2` — same by-design skip |

## Step 2 — manifest + apply + verify

(pending)

## Step 3 — scratch-cycle evidence (all OUTSIDE the repo, `%TEMP%`)

**Cycle (chronological, `%TEMP%/sp020-scratch2`, fresh `npm i pi-spine@2.8.0`):**

1. **Negative control** — `verify.mjs --root scratch2`: all 5 patches `missing`, exit 1. (Fresh install provably patch-free.)
2. **apply** — 5 applied, 0 skipped, exit 0. **verify** — all applied, exit 0.
3. **Idempotence** — second apply: 0 applied, 5 "already applied", exit 0; verify exit 0. No double-patch (occurrence-counted tri-state prevents it).
4. **Byte-parity cross-check** — patched scratch2 `abort.mjs` / `lifecycle-archive.mjs` / `SKILL.md` `diff`-identical to the live main-repo tree (the manifest reproduces the live patches exactly).
5. **Scratch `spine preflight` GREEN** — minimal spine project scaffolded in scratch2 via the scratch install's own `spine init --force` (15/15 checks ✅ incl. tasks-validate, plan).
6. **dotnet allowlist proven** — `parseEvidenceCommandChain("dotnet build … && dotnet test …")` ACCEPTED on patched, rejected on pristine negative control with `evidence executable not allowed: dotnet` (both in one run of the proof script).
7. **@file worker-tail proven** — exported `buildWorkerPiArgs` (runner line 80: "exported for unit tests") on the PATCHED install with a 20KB referenceDoc: last arg `@…\spine-worker-tail-<pid>.txt`, temp file 20,771B (>16KB), total argv 162 chars (<32,767). Small tail (685B) stays inline. PRISTINE negative control: same config → 20,771B tail pushed INLINE (total argv 20,871 — the SP-004 bug shape). **Airtight provenance re-run with the batch's actual config** (`loadSpineConfig(scratch2)` → `referenceDocs: ["docs/fat-reference.md"]`): tail 20,846B → `@file`, argv 294 chars.
8. **STUB batch E2E GREEN** — `SPINE_WORKER_STUB=1 spine batch start SP-999 --attached` → stub worker `.DONE` → `gate approve` → `integrate` (merge `ff1e32d`: `.DONE` + STATUS.md into main via lane→orch) → `batch complete` → `status`: Idle. The batch ran under a spine-config whose referenceDocs produce a >16KB tail (item 7's provenance re-run). Two earlier batch aborts exercised the fsync-patched `abort.mjs`/`lifecycle-archive.mjs` archive paths ("aborted and archived", zero EPERM).
9. **Loud-fail (all-or-nothing) proven** — drift injected into `abort.mjs` of a throwaway copy: apply fails `FAIL fsync-r-plus-abort: anchor×0 replacement×0 — version drift`, exit 1, **zero files modified** (the other 4 applicable patches validated but nothing written).
10. **Cross-version** — apply+verify against pristine **2.10.0**: 5 applied, verify exit 0 (anchors hold across 2.8.0→2.10.0).

**Environment surprise (recorded, NOT a new patch):** `git init` in `%TEMP%` created a hidden `.git` dir (git-for-Windows default; the real repo sets local `core.hidedotfiles=false`), `git worktree add` propagated H to the worktree `.git` pointer, and Node `writeFileSync` EPERMs opening hidden files — breaking the engine's `normalizeLaneWorktreeGitPaths` rewrite. Fixed in scratch via `git config core.hidedotfiles false` (matching the real repo). Latent upstream fragility worth knowing; out of T-1 scope (the real-repo flow never produces hidden `.git`).

**Packet-assumption correction (from pre-approach consult):** stub mode never builds pi args (runner stub path writes `.DONE` directly) and `SPINE_WORKER_PI_AGENT=0` exits before `buildWorkerPiArgs` — so "stub batch proves @file" is empirically impossible; the honest evidence is item 7 (the exact inline-vs-@file decision point feeding `spawnSync("pi", piArgs)`) + item 8 (engine E2E green with the >16KB-producing config).

## Step 4 — board reconciliation + pre-completion consult

### Pre-completion consult (Step 4, solo)

- **Requested route:** solo (council route broken, T-7).
- **ACTUAL answering model:** claude-fable-5 (consistent with requested route).
- **Verdict: APPROVE — mechanism and evidence chain sound; three record-level additions (applied below); no code changes.**
  1. **pi-side `@file` expansion = inherited evidence, not fresh proof** (recorded — see Evidence-honesty notes below).
  2. **Live tree is missing 2 patches RIGHT NOW** — the post-land gate is when `dotnet-evidence-allowlist` and `worker-tail-at-file` are applied to the real `.pi/npm` for the FIRST time; until then any dotnet gate-evidence run or >16KB tail hits unpatched paths (sharpened in board row limit + post-land checklist below).
  3. Hidden-.git EPERM: recorded surprise is the right home, NOT a 6th patch (fails the strictly-load-bearing admission rule; real repo carries `core.hidedotfiles=false`; worse blast-radius-to-benefit than T-12). Port-lessons one-liner sanctioned.

### Evidence-honesty notes (consult additions)

- **@file end-to-end:** the proof stops at the arg-builder by design (spawning pi in scratch = nested spawn). pi-side `@file` expansion of the TAIL arg is INHERITED evidence: (a) the same argv already carries `@${promptPath}` for PROMPT.md and `pi -p` demonstrably expands it (every batch runs this way), and (b) the lost original patch used this exact shape and worked (SP-004 fix, port-lessons 2026-07-19). The 32KB CreateProcess failure mode lives at `spawnSync("pi", piArgs)` — exactly what the arg-builder proof covers (argv 294 chars patched vs 20,871 inline pristine).
- **Tri-state coverage:** the drift demo exercised anchor×0/replacement×0 (apply) and verify's missing/drifted/applied states; the "both present" branch shares the same else-path (trivially covered). `String.replace` `$`-pattern hazards are empirically excluded: verify counts the exact replacement string post-apply (passed) and patched scratch2 files diffed byte-identical to the live tree.
- **Post-land orchestrator gate checklist (copy-pasteable):**
  1. Park the run; reinstall/update pi-spine in the real repo `.pi/npm` (the ONLY reinstall moment).
  2. `node .spine/patches/apply.mjs && node .spine/patches/verify.mjs` (exit 0 required).
  3. Record: this is the FIRST application of `dotnet-evidence-allowlist` + `worker-tail-at-file` to the real tree — until this gate, the real install runs unpatched for those two.
  4. Wire `node .spine/patches/verify.mjs` as a pre-launch step in steering-loop templates (the loud missing-patch check).

## Budgets / surprises

- **Surprise 1:** two of the three T-1-named patches are currently LOST from the live tree (reinstall casualty) — the T-1 problem demonstrated on itself.
- **Surprise 2:** a 4th undocumented local patch exists (SKILL.md T-11 amendment) — found only by the empirical diff.
- **Surprise 3:** the worktree `.pi/npm` is an unpatched 2.8.0 (T-5 trap as reference copy); the running batch is unaffected because the engine runs from the main repo install.
- **Correction:** packet Step 3 assumed a stub batch exercises the worker-tail path; stub mode never builds pi args (verified in runner source).
