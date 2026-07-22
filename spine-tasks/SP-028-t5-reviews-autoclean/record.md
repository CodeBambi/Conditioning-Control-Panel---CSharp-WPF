# SP-028 record — T-5 local anchor-patch: eliminate the .reviews/ DirtyWorktree land tax

## Step 1 — call-chain archaeology (READ-ONLY, live pi-spine 2.10.0 tree)

### Which `resolvePostLaneCommitPorcelain` copy is live

TWO copies exist:

- `src/batch/lane-dirty-check.mjs:433` — **LIVE**. Imported by `src/batch/engine-lanes/commit.mjs:9`
  (`} from "../lane-dirty-check.mjs";`), which is the only caller of the function
  (`grep -rn "resolvePostLaneCommitPorcelain" src/` → import + call at commit.mjs:117;
  other importers of `lane-dirty-check.mjs`: contract-exec.mjs, diagnosis-merge-failure.mjs,
  lane-commit.mjs — none import the dead copy).
- `src/batch/lane-dirty-check-commit.mjs:310` — **DEAD**. `grep -rn "lane-dirty-check-commit" src/ bin/ test/`
  outside its own file: zero hits (exit 1). Nothing imports it.

### The T-5 event order (journal-proven, batch 20260722T051051 / SP-027)

```
08:44:03  lane.completed
08:44:03  review.started  (code,  artifactPath=...lane-1/spine-tasks/SP-027-dtrh-host-b5/.reviews/5-....md)
08:46:23  review.completed (code,  APPROVE)
08:47:13  review.started  (final, artifactPath=.../.reviews/final-....md)
08:48:26  review.completed (final, PASS)
08:48:27  lane.committed (commitSha 0a1d8075)
08:48:27  task.failed (classification DirtyWorktree, 39 ms later)
```

Pipeline code order (`src/batch/engine-lanes.mjs:335-405`): `lane.completed` →
`runCodeReviewPhase` → `runFinalReviewPhase` → `commitLaneAndValidateWorktree`.
Review artifacts are written into the LANE worktree task folder
(`taskFolderInWorktree`, engine-lanes.mjs:128) by the review spawns
(`review-step-run.mjs` / `review-shared.mjs` `buildReviewArtifactPath` →
`<taskFolder>/.reviews/{step|final}-<ts>.md`). Verdicts are journaled
(`review.completed`) before finalization runs.

### Root cause — sharpened past the board row's "classification bug" framing

`commitLaneAndValidateWorktree` (engine-lanes/commit.mjs) → `commitLaneWorktree`
(lane-commit.mjs) → `filterGitignoredPaths` (git-helpers.mjs:14) runs
`git check-ignore --no-index --stdin` over the dirty paths. `git status --porcelain`
lists the untracked review dir WITH A TRAILING SLASH (`?? spine-tasks/SP-xxx/.reviews/`).

**Empirical (this worktree, git 2.49.0.windows.1):**

```
$ printf 'spine-tasks/foo/\nclient/\nfoo/.reviews/\n' | git check-ignore -v --no-index --stdin
.gitignore:179:	spine-tasks/foo/
.gitignore:179:	client/
.gitignore:179:	foo/.reviews/
```

`.gitignore:179` is a BLANK line (verified `awk NR==179`). git 2.49 `--no-index`
false-positive-matches ANY trailing-slash directory input against the blank line
(no pattern). File paths (`.reviews/x.md`) and non-trailing-slash dirs do NOT match.

Consequence chain, 100% deterministic on every Level-2 batch:

1. Review stage writes `.reviews/` into the lane AFTER the worker's last commit.
2. Lane auto-commit: `filterGitignoredPaths` mis-classifies `?? .reviews/` as
   gitignored → `skipped`; only `.DONE` (+ any other drift) is staged and committed.
3. Post-commit `resolvePostLaneCommitPorcelain` re-runs `git status --porcelain`
   (which does NOT apply the blank-line quirk — it is a check-ignore-`--no-index`
   artifact only) → `?? .reviews/` still present → `filterOutOfScopeCoveragePorcelain`
   keeps it (not a coverage path) → DirtyWorktree.

**Proof across history:** every lane auto-commit `feat(SP-xxx): batch * worker completion`
(12 inspected: SP-012…SP-027) contains `.DONE` and NEVER `.reviews/`:

```
0a1d8075 SP-027  .DONE only          451ac55e SP-022  .DONE only
d0e4a1d9 SP-026  .DONE only          fd375b62 SP-019  .DONE only
50b61312 SP-025  .DONE only          83923700 SP-018  .DONE only
a842c639 SP-024  .DONE only          918ac262 SP-015  .DONE + 2 .pi/loops files
49d3ae35 SP-023  .DONE + 1 .pi/loops 49085959 SP-014  .DONE only
64d66d10 SP-013  .DONE + task-board  4243ccef SP-012  .DONE only
```

This is the SAME shared helper (`filterGitignoredPaths`, `check-ignore --no-index`)
that SP-020's T-12 analysis flagged as unsafe to patch — the T-5 and T-12 rows are
two symptoms of one git-quirk-sensitive call site.

### Why the lane auto-commit does not sweep it

`commitLaneWorktree` stages `stageCandidatePaths` = dirty paths minus
`shouldSkipHookIgnorePath` (`.venv` only — `resolveWorktreeSetupIgnorePaths(config)`
= defaults `[".venv"]` + config `[".venv"]`) minus `filterGitignoredPaths`-skipped.
`.reviews/` is NOT hook-ignored and NOT in `.gitignore` (verified
`git check-ignore` without `--no-index` → exit 1, no match); it falls to the
`check-ignore --no-index` quirk above and is silently skipped. The
`GitignoredDirtyWorktree` fail-closed branch only fires when `stageable.length === 0`;
with `.DONE` stageable the commit proceeds and the mis-skip is invisible until the
post-commit re-check classifies DirtyWorktree.

### The review-scope precedent

`src/batch/review-scope.mjs:22` already excludes `.reviews/` from review diff
scoping (`if (normalized.includes("/.reviews/") || normalized.startsWith(".reviews/"))`)
— the engine ALREADY treats `.reviews/` as engine-internal, never product content.

## Step 1 — historical derivation (strictly-load-bearing admission evidence)

Journal enumeration across `.spine/runtime/*/journal/events.jsonl`
(main checkout — read-only; `task.failed` events with DirtyWorktree-family
classification, prior `review.completed` verdict):

| # | timestamp (UTC) | batch | task | classification | prior review |
|---|---|---|---|---|---|
| 1 | 2026-07-18T11:34 | 20260718T112944 | SP-001 | GitignoredDirtyWorktree | none (pre-T-2) |
| 2 | 2026-07-19T00:40 | 20260718T235923 | SP-004 | GitignoredDirtyWorktree | none |
| 3 | 2026-07-19T01:49 | 20260719T010403 | SP-005 | GitignoredDirtyWorktree | none |
| 4 | 2026-07-19T02:50 | 20260719T021531 | SP-006 | GitignoredDirtyWorktree | none |
| 5 | 2026-07-19T22:35 | 20260719T210942 | SP-011 | GitignoredDirtyWorktree (lane `.reviews/` + spike bin, per SP-017-style payload + gate history) | final PASS |
| 6 | 2026-07-20T01:57 | 20260720T004519 | SP-012 | DirtyWorktree | final:PASS |
| 7 | 2026-07-20T05:12 | 20260720T022627 | SP-013 | DirtyWorktree | final:PASS |
| 8 | 2026-07-20T07:09 | 20260720T052700 | SP-014 | DirtyWorktree | final:PASS |
| 9 | 2026-07-21T07:31 | 20260720T072956 | SP-015 | DirtyWorktree | final:PASS |
| 10 | 2026-07-21T12:42 | 20260721T111026 | SP-018 | DirtyWorktree | final:PASS |
| 11 | 2026-07-21T13:53 | 20260721T130248 | SP-019 | DirtyWorktree | final:PASS |
| 12 | 2026-07-21T18:02 | 20260721T174051 | SP-022 | DirtyWorktree (on engine 2.10.0 — upstream SP-601 auto-clean does NOT cover `.reviews/`) | final:PASS |
| 13 | 2026-07-21T20:07 | 20260721T181508 | SP-023 | DirtyWorktree | final:PASS |
| 14 | 2026-07-21T22:40 | 20260721T202836 | SP-024 | DirtyWorktree | final:PASS |
| 15 | 2026-07-22T01:50 | 20260721T225836 | SP-025 | DirtyWorktree | final:PASS |
| 16 | 2026-07-22T04:55 | 20260722T020943 | SP-026 | DirtyWorktree | final:PASS |
| 17 | 2026-07-22T08:48 | 20260722T051051 | SP-027 | DirtyWorktree | final:PASS |

Gate-history numbering (task-board rows 94-110): SP-027 = "15th — deterministic".
Reconciliation: gate history counts SP-011's `.reviews/` variant and skips the
pre-T-2 bin/obj-only occurrences; SP-016/SP-017's terminal blocks were T-12
(merge-time tracked-ignored scan), NOT T-5 (board row T-5 note). SP-020 landed via
the human_base_diverged path (no T-5); SP-021 was a stub probe (aborted).
**Every Level-2 batch since T-2 closure (SP-012 onward) T-5'd: 12 consecutive,
every one `.reviews/` residue, every one recovered by the identical manual step**
(journal-read verdicts → delete `.reviews/` → retry fast-path → manual orch ff →
hand-written gate record). 15/15 gate-history occurrences total; the
strictly-load-bearing rule is satisfied for the `.reviews/` site specifically by
the 12 consecutive Level-2 occurrences plus SP-011's variant.

## Step 1 — patch-shape design

### Shape (a): teach the porcelain check to exclude `.reviews/`

Patch `resolvePostLaneCommitPorcelain` or `filterGitignoredPaths`. REJECTED:

- `filterGitignoredPaths` is the shared helper SP-020's T-12 analysis declared
  unsafe to patch (feeds lane-commit classification AND the merge path
  `tryAutoResolveOutOfScopeMergeConflict` → `git rm --cached` fallback, which
  cannot be honestly exercised in scratch). Fixing the blank-line quirk there
  changes semantics for every consumer — exactly the T-12 exclusion.
- Filtering `.reviews/` in the post-commit check alone leaves the mis-skip in
  lane-commit (harmless but two-sided), and leaves untracked debris in the lane
  worktree forever.

### Shape (b): delete `.reviews/` in `commitLaneAndValidateWorktree` before the lane commit — SUPERSEDED by (b′)

Mirrors the 15×-proven manual recovery step in one contained function.
Consumer census for the patched function:

- `engine-lanes.mjs:385` — standard lane finalization, called ONLY after
  `runCodeReviewPhase` + `runFinalReviewPhase` succeeded (verdicts journaled).
- `matrix-run.mjs:488` — matrix lane finalization, after all rows merged.
  No other callers (`grep -rn commitLaneAndValidateWorktree`).

Deletion runs AFTER verdict recording, never before — both call sites are
post-review. `fs.rmSync(..., {recursive:true, force:true})` is a no-op when
`.reviews/` is absent (Review Level 0/1 tasks, matrix lanes without reviews).

`.reviews/` on-disk consumer census (what deletion could affect):

- Writers: `review-step-run.mjs` / `review-step.mjs` / `engine-lanes/review.mjs`
  (review artifacts), `contract-exec.mjs:104` (contract-fail logs) — ALL run
  BEFORE `commitLaneAndValidateWorktree` in the pipeline.
- Readers: `review-artifacts.mjs` `findCompletedCodeReview` /
  `findCompletedFinalReview` — journal-FIRST (an APPROVE/PASS `review.completed`
  journal event short-circuits before any disk read); artifact reads are a
  resume fallback. Called only by the review phases (engine-lanes/review.mjs,
  review-step-run.mjs, review.mjs) — all pre-finalization. On a resume after a
  crash between deletion and merge, the journal verdict is honored (source:
  "journal"); no artifact needed.
- Post-finalization: nothing in `bin/` or the merge path reads `.reviews/`
  (`grep -rn "\.reviews" bin/ src/tasks/` → zero hits).
- `.reviews/` was NEVER committed to any lane branch (12/12 auto-commit proof
  above), so no land ever carried it — deletion loses nothing vs the status quo
  of 15 manual recoveries.

### Draft anchor + replacement (live 2.10.0 tree, tabs)

Target: `.pi/npm/node_modules/pi-spine/src/batch/engine-lanes/commit.mjs`
(fs/path NOT currently imported → two disjoint edit sites → TWO manifest patches
on the same file; requires the apply.mjs phase-2 sequential-write fix below).

Patch 1 `t5-reviews-autoclean-import` — anchor:
```
import { appendJournalEvent } from "../journal.mjs";
import { commitLaneWorktree, gitPorcelain } from "../lane-commit.mjs";
```
replacement: same two lines preceded by
```
import fs from "node:fs";
import path from "node:path";
```

Patch 2 `t5-reviews-autoclean` — anchor:
```
	const preCommitPorcelain = gitPorcelain(worktreePath);
```
replacement: the `fs.rmSync(path.join(taskFolder, ".reviews"), { recursive: true, force: true });`
line with the `// ponytail:` rationale comment block, then the anchor line.

### Required mechanism fix: apply.mjs phase-2 clobbers same-file multi-patch

apply.mjs phase 1 stores ORIGINAL content per patch; phase 2 writes
`content.replace(anchor, replacement)` — for two patches on the SAME file the
second write would revert the first. All 5 existing patches target distinct
files, so this never fired. Fix (in File Scope `.spine/patches/**`): phase 2
re-reads the file at write time (`fs.readFileSync` then replace then write).
All-or-nothing validation is unchanged (phase 1 still validates every anchor
against the pre-write tree; the two new anchors are disjoint by construction).
verify.mjs needs no change (it already re-reads per patch).

### Rejected alternative placements

- `commitLaneWorktree` (lane-commit.mjs — fs/path already imported, single
  patch): REJECTED — 4 consumers (engine-lanes/commit.mjs, resume.mjs:344,
  resume-multi-lanes.mjs:94+356); the resume paths are shared-helper surface
  (T-12 lesson). `commitLaneAndValidateWorktree` has exactly 2 finalization-only
  consumers.
- Hoisted mid-file ESM import to keep one patch: REJECTED — legal but
  non-idiomatic; the apply.mjs fix is 3 lines and benefits the mechanism.

### Shape (b′) — ADAPTED after consult: delete `.reviews/` at the top of `commitLaneWorktree` — CHOSEN

The pre-approach consult approved shape (b) at `commitLaneAndValidateWorktree` but flagged
the resume paths as an open condition. Primary-source follow-up resolved it AGAINST the
original site:

- `resume.mjs:390` and `resume-multi-lanes.mjs:137` run their OWN post-commit dirty checks
  (`filterPorcelain(gitPorcelain(wt))` after `commitLaneWorktree`) and emit DirtyWorktree
  with `resumed:true` hardcoded; engine-lanes/commit.mjs emits no `resumed` flag.
- Journal attribution: SP-013, SP-015, SP-027 (the LATEST), SP-001 failures carry
  `resumed:true` — 4 of 17 occurrences fired on the RESUME path. Patching only
  `commitLaneAndValidateWorktree` leaves the tax alive on the recovery path.
- `commitLaneWorktree` (lane-commit.mjs) has exactly 4 consumers, ALL post-review lane
  finalization: engine-lanes/commit.mjs:68, resume.mjs:344, resume-multi-lanes.mjs:94,:356
  (both resume modules run review phases before the call). fs+path already imported.

One anchor, one patch, no import patch, no apply.mjs change. T-12 exclusion inapplicable:
that was `filterGitignoredPaths` classification semantics with an unexercisable merge-path
consumer; here all 4 consumers are enumerated and deletion semantics are identical at each.
Gut-check re-consult RATIFIED the move (verdict below).

### Draft anchor + replacement (live 2.10.0 tree, tabs) — FINAL

Target: `.pi/npm/node_modules/pi-spine/src/batch/lane-commit.mjs`, top of
`commitLaneWorktree`. Anchor spans the insertion point (consult correction #1: the anchor
must NOT survive as a contiguous substring of the replacement, else post-apply state =
anchor×1 + replacement×1 and verify.mjs reports `drifted` forever):

anchor:
```
	fileScopePaths = [],
}) {
	const identityRoot = projectRoot ?? worktreePath;
```
replacement: same first two lines, then the `// ponytail:` T-5 rationale comment block +
`fs.rmSync(path.join(taskFolder, ".reviews"), { recursive: true, force: true });`, then the
identityRoot line. The contiguous anchor is split by the insertion — tri-state invariant holds.

### Tracked-`.reviews/` edge (consult correction #2 — verified)

`git log --all -- "*/.reviews/*"`: SP-020's lane DID commit `.reviews/` files
(worker step commit f7c6883b + auto-commit bf3d7eaf — possible because once the dir has a
tracked file, porcelain lists new files individually WITHOUT the trailing slash, dodging the
git quirk). With the patch, rmSync of tracked files surfaces as staged deletions in the
finalization commit — commit proceeds, tree self-cleans, no failure. Recorded, accepted.

### Mechanism note (moot after b′)

apply.mjs phase-2 writes ORIGINAL content per patch — two patches on the SAME file would
clobber (latent; all 5 existing patches target distinct files). Shape (b′) needs only one
patch on one file, so the mechanism stays untouched. Recorded for the next multi-patch author.

## Step 1 — pre-approach solo consult (Fable 5 requested, solo)

**Consult 1 (pre-approach, solo):** verdict = shape (b) direction correct (contained
deletion over shared-helper/porcelain-filter shapes); THREE binding corrections:
(1) the draft insert-before anchors VIOLATE the manifest tri-state invariant (anchor ⊂
replacement → post-apply verify reports drifted forever) — anchors must span the insertion
point so the replacement SPLITS them; (2) worker step commits CAN track `.reviews/`
(verify — done: SP-020 case above); (3) resume paths bypass `commitLaneAndValidateWorktree`
— if they re-check porcelain, they still T-5 (resolved: they DO, and 4/17 occurrences
incl. the latest fired there → adaptation b′).
Requested route: solo Fable 5. Actual answering model: NOT surfaced in the consult tool
response (recorded honestly per T-7; no model identity claim made).

**Consult 2 (adaptation ratification, gut-check):** "RATIFY the move to
`commitLaneWorktree`. It is the correct root-cause site — the single function where all
four finalization paths converge... A patch that leaves the resume path T-5-able does not
kill the tax — it just moves it to the recovery path." Recorded the PROMPT-target
adaptation explicitly (above). Actual answering model: not surfaced (same as above).

## Engine-review presence log (T-2 discipline)

- Step 1 plan review (`spine_review_step` type=plan): **engine-SKIPPED** (SP-195 — nested
  reviewer spawn blocked inside pi worker session; engine runs reviews post-.DONE).
  skipped:true, spawnFailed:false, artifact 1-20260722T093438.md.
- Step 2 plan review (`spine_review_step` type=plan): **engine-SKIPPED** (SP-195).
  skipped:true, spawnFailed:false, artifact 2-20260722T093923.md.
- Step 3 plan review (`spine_review_step` type=plan): **engine-SKIPPED** (SP-195).
  skipped:true, spawnFailed:false, artifact 3-20260722T094304.md.
- Step 4 plan review (`spine_review_step` type=plan): **engine-SKIPPED** (SP-195).
  skipped:true, spawnFailed:false, artifact 4-20260722T094924.md.

## Step 2 — manifest entry + apply/verify on the live install

Manifest patch `t5-reviews-autoclean` authored (target `src/batch/lane-commit.mjs`,
anchor spanning the insertion point, `testedVersions: [2.8.0, 2.10.0]`).

**testedVersions honesty:** live install = 2.10.0 (anchor count exactly 1, LF-only file).
2.8.0 verified via `npm pack pi-spine@2.8.0` in scratch (`%TEMP%/sp028-scratch`): anchor
byte-present exactly once; the file differs elsewhere from 2.10.0 but the anchor region is
identical — and the FULL apply+verify cycle ran green on the pristine 2.8.0 scratch
install (functional evidence, stronger than byte-presence).

**Live-install proofs (worktree `.pi`):**

- `node .spine/patches/apply.mjs` → 6 applied (the worktree's `.pi` was a pristine copy —
  the 5 pre-existing patches applied here for the first time too); `verify.mjs` → exit 0.
- Idempotence: second apply → `0 applied, 6 already applied`; verify exit 0.
- Loud-failure (pristine negative control): verify against the unpacked pristine 2.8.0
  scratch install → all 6 `missing`, exit 1.
- Loud-failure (drift): patched scratch copy, one byte flipped inside the patched region
  (`force: true` → `force: false`) → verify reports
  `drifted t5-reviews-autoclean: anchor×0 replacement×0` + FAIL exit; apply refuses
  (`validation failed — NO files were modified (all-or-nothing)`).

**PROMPT-premise deviation (honest):** the packet asserts "the engine tree is tracked" and
checkboxes "Commit includes the patched live engine file". Empirically `.pi/npm/` is
GITIGNORED (`.gitignore:50`, `git check-ignore -v` confirms) — the live engine tree is NOT
tracked and cannot be committed (force-adding tracked-ignored content is exactly the T-12
hazard class). The durable committed copy of the patch IS the manifest entry — that is the
SP-020 mechanism's entire reason to exist. The live install is patched + verify-green;
the manifest + this record are the committed artifacts. Orchestrator/owner may amend the
packet text at land.

## Step 3 — fixture + historical proof

**Fixture** (`evidence/fixture-t5.mjs`, transcript `evidence/fixture-t5-transcript.txt` —
scratch only under `%TEMP%/sp028-fixture`, repo never touched): scratch lane repos carrying
the repo's REAL `.gitignore` (blank line 179 load-bearing — the fixture asserts the git 2.49
quirk reproduces before trusting the run), `.DONE` uncommitted + `.reviews/final-*.md`
verdict artifact written after the worker's last commit (the exact T-5 lane shape), verdict
journal stand-in OUTSIDE the lane. 7/7 assertions GREEN:

- **Negative control (pristine 2.10.0, npm-packed + `npm install --omit=dev`):**
  `commitLaneWorktree` commits `.DONE` and leaves `?? spine-tasks/SP-TEST/.reviews/`
  in the post-commit porcelain — the exact DirtyWorktree residue, reproduced on demand.
- **Patched (live install):** `.reviews/` deleted inside the call, commit succeeds,
  post-commit porcelain CLEAN — finalization passes.
- **Event order:** the verdict artifact existed right up to the finalization call
  (`artifactExistedAtCall: true`); deletion happens only inside `commitLaneWorktree`,
  which every caller invokes post-review — never before verdict recording.
- **Journal durability:** the journal stand-in outside the lane is untouched in both runs.

**Consumer census + no-regression argument (T-12 discipline, shape b′):** the patched
function `commitLaneWorktree` has exactly 4 consumers, ALL post-review lane finalization:

1. `engine-lanes/commit.mjs:68` (`commitLaneAndValidateWorktree`) — fresh finalization,
   after `runCodeReviewPhase` + `runFinalReviewPhase` (verdicts journaled);
   `matrix-run.mjs:488` reaches the same function after row merges.
2. `resume.mjs:344` — after `reviewResult` (resume.mjs:330).
3. `resume-multi-lanes.mjs:94` — after `reviewResult` (verified in source).
4. `resume-multi-lanes.mjs:356` — after `reviewResult` (verified in source).

No-regression: (a) lanes without `.reviews/` — `rmSync force:true` is a documented no-op
(RL0/1 tasks, matrix lanes without reviews, retry fast-path re-inspection which needs no
commit); (b) deletion timing is identical to the 15x-proven manual recovery; (c) every
on-disk `.reviews/` reader (review-artifacts.mjs find* — journal-first, artifact-fallback)
runs strictly before this point in the pipeline; nothing post-finalization reads
`.reviews/` (`grep bin/ src/tasks/` zero hits); (d) tracked-`.reviews/` edge (SP-020 case)
surfaces as staged deletions — commit proceeds, tree self-cleans; (e) resume-path crash
between deletion and merge re-honors journal verdicts (source: "journal"), no artifact
needed.

**Proof boundary (honest):** this packet's in-lane proof = the fixture above + the
17-occurrence historical derivation + the deterministic root-cause reproduction. The REAL
proof is the NEXT Level-2 batch landing WITHOUT the manual recovery — recorded as the
named post-land gate on board row T-5 (if that land T-5s, the patch is wrong and the row
reopens). Note: SP-028's own finalization runs on the patched live install — if the batch
engine hosting this very lane picks up the patched `lane-commit.mjs`, SP-028 itself may
land without the manual recovery; that is corroboration, not the named gate (the named
gate is the NEXT Level-2 batch, orchestrator-observed). **Module-cache correction
(pre-completion consult, binding):** the batch engine process finalizing THIS lane started
before Step 2 patched the file and holds the OLD `lane-commit.mjs` in ESM module cache —
SP-028's own finalization is EXPECTED to T-5 one final time and need the playbook once
more. That is not a patch failure and must not reopen the row; the named gate is the first
Level-2 batch LAUNCHED AFTER the patch was applied.

## Step 4 — docs + board reconciliation

- `.spine/patches/README.md`: `t5-reviews-autoclean` row in the patch table; T-12 bullet
  annotated (T-5 blast radius patched downstream, the helper quirk itself still excluded);
  latent apply.mjs same-file-multi-patch clobber recorded as a mechanism note.
- `client/docs/port-lessons.md`: T-5-closed entry (sharpened root cause, shape-b′ choice
  + resume-path evidence, journal durability, proof boundary).
- `client/docs/task-board.md` row T-5: `OPEN` → `CLOSED-by-patch` (NOT DONE — per packet
  Do-NOT) with the NAMED POST-LAND GATE: first Level-2 batch after this land must skip
  the manual recovery; a T-5 there reopens the row.

## Step 4 — pre-completion solo consult (Fable 5 requested, solo)

**Verdict: no blocking defect; ONE binding honesty correction (applied):** SP-028's own
land will almost certainly STILL T-5 — the batch engine finalizing this lane started
before Step 2 and holds the OLD `lane-commit.mjs` in ESM module cache. Record.md's
"corroboration" sentence was overstated in the wrong direction; corrected (Step-3 proof
boundary now names the expectation), and the board row's named gate now reads "first
Level-2 batch LAUNCHED AFTER the patch was applied" with the SP-028-own-land carve-out —
keeps the gate falsifiable and prevents a false reopen. Non-blocking confirmations:
rmSync site/comment accurate (incl. the benign committed:false-clean side effect for
.reviews/-only lanes); testedVersions functional evidence stronger than the SP-020
byte-presence convention; CLOSED-by-patch wording correct with the launch-time clause;
both deviations correctly recorded; proof boundary not overstated after the correction.
Step-5 gates enumerated by the consult (contract testCommand, diff-check, status scope,
final STATUS accuracy) — executed in Step 5 below.
Requested route: solo Fable 5. Actual answering model: NOT surfaced in the consult tool
response (recorded honestly per T-7; no model identity claim made).

## Step 5 — verification

- `node .spine/patches/verify.mjs` → exit 0, all 6 patches applied (live 2.10.0).
- `dotnet build client/CcpClient.sln -c Debug --nologo` → 0W/0E.
- `dotnet test CcpClient.Tests` → 391/391 passed, 0 failed.
- `dotnet test CcpClient.HeadlessTests` → 29/29 passed, 0 failed.
  Floor 391/29 met EXACTLY, zero drift (client tree untouched).
- `git diff --check` → clean (exit 0).
- `git status --short` → only File Scope paths (this task folder; docs/patches committed
  at step boundaries). The `.pi/npm` engine edits are gitignored and correctly absent;
  `%TEMP%` scratch never entered the repo.

**Late observation (honesty):** this lane's own `.reviews/` directory (4 skip-artifacts
from the SP-195-skipped plan reviews) was ABSENT at Step-5 time despite the artifactPaths
returned by `spine_review_step` — something in the engine/runtime lifecycle removed it
between the Step-4 review call and the verification run (cause not archaeologized;
candidate: a lane-checkpoint sanitize hitting the same trailing-slash mis-classification).
Consequence: the "SP-028's own land EXPECTED to T-5 one final time" note is an
expectation, not a guarantee — if the residue is gone at finalization, the unpatched
in-memory engine lands clean too. The named post-land gate (first Level-2 batch launched
after the patch) is unaffected either way.
