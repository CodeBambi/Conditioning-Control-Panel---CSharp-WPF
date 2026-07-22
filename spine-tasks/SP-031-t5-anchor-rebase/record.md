# SP-031 record — T-5: the patch was on the wrong install tree (packet premise falsified by primary evidence)

## Step 1 — failure forensics: the packet's root-cause theory is WRONG; the real one is proven

### The packet's claim (from board row T-5 / wave-1 gate history)

"the engine's `taskFolder` at the `commitLaneWorktree` finalization call is the BASE-repo packet
path while `.reviews/` is written to the LANE WORKTREE's task folder — the rmSync no-ops."

### Falsification (three independent proofs)

1. **Source census (both engine versions).** All 4 `commitLaneWorktree` finalization callers pass
   `taskFolder: taskFolderInWorktree` where `taskFolderInWorktree = path.join(wt, taskFolderRel)` —
   the LANE worktree path. Verified in repo-local 2.10.0 (`engine-lanes.mjs:128,390`,
   `engine-lanes/commit.mjs:68`, `resume-multi-lanes.mjs:94,356`, `resume.mjs:344`) AND in the
   global 2.8.0 tree (`engine-lanes.mjs`, `resume.mjs` 1 call, `resume-multi-lanes.mjs` 2 calls —
   all `taskFolderInWorktree`).

2. **Live event-order proof (wave-1 journal, batch `20260722T101444`).** `commitLaneWorktree`
   computes `donePath = path.join(taskFolder, ".DONE")` and early-returns
   `failureClass: "DirtyWorktree"` with "worker did not finish cleanly" **before any commit** when
   `.DONE` is absent. Wave-1's journal shows `lane.committed` (10:46:27.884Z) THEN `task.failed`
   with the *post-commit* message (10:46:27.924Z) — so the `.DONE` check passed. The base repo's
   `spine-tasks/SP-030-ai-companion-admission/.DONE` did not exist until the 12:06Z merge
   (worker created it in-lane at 10:42Z). **Therefore `taskFolder` was the lane path at the live
   call.** A `path.join(taskFolder, ".reviews")` rmSync in the executing module would have deleted
   the residue. It survived (auto-commit `b9257b58` = `.DONE` only, the 13th identical signature;
   SP-029 `ac223d4e` same). ⇒ **the executing module did not contain the patch.**

3. **Process cmdline proof (decisive).** The batch engine does NOT run from the repo-local tree
   SP-028 patched. The currently-running wave-2 batch's processes:
   `node C:\Users\Micha\.pi\agent\npm\node_modules\pi-spine\bin\spine.mjs batch start SP-031 SP-032 …`
   and both `spine-worker-runner.mjs` workers — all from the **GLOBAL user-level install**
   `C:\Users\Micha\.pi\agent\npm\node_modules\pi-spine` (package.json = **2.8.0**, pin `^2.8.0`).
   Launch convention corroborates: `spine-tasks/CONTEXT.md:124` puts the GLOBAL `.bin` on PATH.
   Repo-local `.pi/npm` (2.10.0, pin `npm:pi-spine@2.10.0` in repo `.pi/settings.json`) is the tree
   pi sessions load skills/tools from — verify.mjs checks it, the engine never executes it.
   SP-028's patch (applied 10:05:13Z, 9.5 min before wave-1's engine process started at 10:14:44Z,
   so a repo-tree engine WOULD have loaded it) sat on a tree the engine doesn't load.
   Global tree state measured live: t5 anchor×1 replacement×0 (patch absent).

**Conclusion:** the existing patch line `fs.rmSync(path.join(taskFolder, ".reviews"), …)` is
CORRECT for every caller in both versions. The failure class is **applied ≠ loaded**: the patch
mechanism targeted the wrong install tree. The SP-028 fixture's taskFolder-inside-worktree shape
was never the flaw — it matches the real callers exactly.

**Uncertainty honestly bounded:** the global tree's mutation HISTORY is not fully reconstructable —
the T-1 post-land gate (2026-07-21) produced pid-named tail artifacts
(`spine-worker-tail-39088.txt`), which match the MANIFEST tail variant, not the per-task-named
hand-variant the global tree carries today; something mutated the global tree between 07-21 and
07-22. What is measured: the tree's state NOW (hand-variants for fsync/dotnet/tail, no t5) and the
engine's load path NOW (global, process-cmdline). The fix depends only on the current state.

### Global-tree census (measured 2026-07-22, vs manifest tri-state)

| patch | global 2.8.0 state | note |
|---|---|---|
| fsync-r-plus-abort | anchor×0 replacement×0 | hand-variant: `openSync(archivePath, "r+")` WITHOUT the ponytail comment; behaviorally identical |
| fsync-r-plus-lifecycle-archive | anchor×0 replacement×0 | same |
| dotnet-evidence-allowlist | anchor×0 replacement×0 | hand-variant adds `"dotnet.exe"` (wider allowlist) |
| worker-tail-at-file | anchor×0 replacement×0 | hand-variant: threshold 16000, `os.tmpdir()`, per-task filename, added `import os` |
| skill-headed-evidence-sizing | anchor×1 replacement×0 | applies cleanly; but CCP-specific skill text — should NOT go on the engine tree (see design) |
| t5-reviews-autoclean | anchor×1 replacement×0 | **applies cleanly — the missing patch that caused wave-1** |

### Corrected design (ratified by pre-approach consult, below)

1. **Manifest:** t5 anchor + replacement path expression UNCHANGED (anchor byte-present in both
   2.8.0 and 2.10.0); comment/rationale rewritten to the real root cause. New top-level
   `"engineRoot"` (absolute path of the CLI install the engine actually loads) + per-patch
   `"engine": true` on the 5 engine-behavior patches (fsync×2, dotnet, tail, t5). The t11 skill
   amendment stays project-tree-only (pi sessions load skills from the project install; the engine
   tree's skill copy is shared by OTHER repos' pi sessions — CCP text does not belong there).
   Engine patches keep their project-tree twins (inert but harmless; preserves SP-020 semantics).
2. **dotnet canonical text gains `"dotnet.exe"`** — matches the proven global hand-variant
   (strictly wider allowlist; every CCP testCommand invokes `dotnet`). Global tree then reads
   tri-state "applied" with ZERO writes; repo tree migrated by ONE single-write line swap.
3. **apply.mjs/verify.mjs multi-root:** process `[installRoot, engineRoot]`; engine root receives
   engine-flagged patches only; `--root` keeps legacy single-root all-patches semantics for scratch
   cycles; missing engineRoot dir = loud FAIL naming the remedy; all-or-nothing validation across
   roots before any write; per-root reporting.
4. **Global-tree normalization, atomic per file** (consult condition — no restore→apply two-step
   window while a live engine could resume-handoff mid-migration): each hand-variant region is
   replaced by the canonical text in ONE write. fsync×2 = comment-only delta. tail = hand block →
   manifest replacement text (proven equivalent: SP-020 stub cycle 20,846B tail → @file; the
   pid-named 07-21 artifacts); the now-unused `import os` stays (harmless, noted).
   t5 applied via apply.mjs after normalization. Mid-batch on-disk patching is safe: the running
   engine holds its module in the ESM cache; only a re-spawned (resume-handoff) engine picks up the
   patch — flipping finalization to the FIXED behavior mid-wave, which is beneficial and recorded
   for the orchestrator.
5. **Fixture v2 (provenance-faithful to the REAL failure):**
   - **two-tree repro (wave-1 shape):** patch on tree A, engine module imported from tree B
     (pristine) → residue survives, DirtyWorktree shape — the actual proven failure mode;
   - **base-path taskFolder cell (the packet's theorized shape):** taskFolder = base-shaped packet
     path OUTSIDE the worktree with lane residue inside → patched engine STILL deletes the lane
     residue? NO — it early-returns "`.DONE` is missing" with NO commit and NO lane.committed —
     the event-order fingerprint that wave-1's journal does NOT show, documenting the falsification
     AND discharging the packet's base-shaped fixture requirement with its true semantics;
   - **patched engine + real caller shape** (taskFolder = taskFolderInWorktree, all 4 callers'
     shape) → clean;
   - **pristine engine** → negative control DirtyWorktree (preserved);
   - the ORIGINAL fixture's in-worktree case still passes (it IS the real caller shape).
6. **Post-land gate re-point (orchestrator acts at land, enabler 2):** the named gate moves to the
   NEXT Level-2 wave after this lands. Honest boundary: THIS wave's engine (started 12:20Z,
   unpatched module in memory) will T-5 SP-031/SP-032 finalizations UNLESS a resume re-spawns it
   after the global on-disk patch — expected, not a reopen. REAL proof = next wave's finalizations
   skipping the manual recovery.

### Pre-approach consult (BEFORE implementation, per packet Step 1)

- Mode: solo (packet: council route broken — solo Fable 5 only). Tool: `consult` (pi tool).
- Actual answering model: the tool response did not self-identify the model; configured advisor
  per `.pi/bpx-consult.json`. Verdict text below verbatim in substance.
- **Verdict: the design holds.** Falsification logic confirmed airtight (`.DONE`-check +
  `lane.committed` ordering excludes the base-path theory under ANY engine version; patch-on-disk
  9.5 min before engine start excludes the repo tree for wave-1; process cmdline settles the
  global tree). Multi-root verify is the actual fix for the failure class "applied ≠ loaded" — a
  `--root`-only application without verify would repeat SP-028's mistake. Conditions given and
  DISCHARGED in the design above: (1) atomic single-write per-file migrations (no restore→apply
  window); (2) dotnet migration order = manifest change FIRST, then repo-tree line swap; (3) fixture
  must include the base-path cell with its TRUE semantics (early-return fingerprint) — both
  documents the falsification and satisfies the packet's base-shaped requirement; (4) missing
  engineRoot must fail with the remedy in the message; (5) global-tree mutation history recorded as
  uncertain, not overclaimed; (6) t5 comment lands on 2.8.0 — anchor verified byte-present, fine.

### Consumer census re-confirmation (packet Step 3 item, done here with the forensics)

`commitLaneWorktree` callers, both versions: `engine-lanes/commit.mjs:68` (the only
`commitLaneAndValidateWorktree` site, called from `engine-lanes.mjs:385` with
`taskFolderInWorktree`), `resume-multi-lanes.mjs:94` and `:356`, `resume.mjs:344` — ALL pass
`taskFolder: taskFolderInWorktree`. No caller passes a base-repo packet path. The relative-path
edge cases the packet asked to enumerate (`taskFolder` inside `worktreePath`, `projectRoot`
absent/eq `worktreePath`, `path.relative` escaping) are MOOT: no path arithmetic is needed —
`taskFolder` already IS the lane task folder at every call site. (This census also closes the
packet's "4 callers from SP-028 + edge cases" checkbox.)

## Step 2 — manifest re-base (two-root) + live-tree migration + apply/verify

### Manifest changes (`.spine/patches/manifest.json`)

- New top-level `"engineRoot": "C:\Users\Micha\.pi\agent\npm\node_modules\pi-spine"` — the
  GLOBAL CLI install the batch engine executes (process-cmdline-proven). `note` documents the
  two-root model and the loud-fail remedy.
- `"engine": true` on the 5 engine-behavior patches (fsync×2, dotnet, tail, t5); the t11 skill
  amendment stays project-tree-only (CCP-specific text must not land on the globally shared skill).
- dotnet canonical replacement gains `"dotnet.exe"` (unifies with the proven global hand-variant;
  strictly wider allowlist).
- t5 anchor UNCHANGED (byte-present in 2.8.0 and 2.10.0); replacement path expression UNCHANGED
  (`path.join(taskFolder, ".reviews")` — proven correct); ponytail comment + rationale rewritten
  to the real wave-1 root cause (applied≠loaded, wrong install tree).

### apply.mjs / verify.mjs — multi-root

Default mode processes `[installRoot (all patches), engineRoot (engine-flagged only)]`;
`--root` keeps legacy single-root all-patches semantics for scratch cycles; missing root dir =
loud FAIL naming the remedy; all-or-nothing validation across all roots before any write.

### Live-tree migration transcript (atomic single write per file — consult condition)

`evidence/migrate-trees.mjs` (kept in-packet) + one follow-up single write:

1. repo `evidence-command.mjs`: dotnet line gains `"dotnet.exe"` (58B→72B)
2. repo `lane-commit.mjs`: t5 comment block → corrected comment (702B→712B)
3. global `abort.mjs`: fsync hand-variant → canonical (comment-only, 70B→125B)
4. global `lifecycle-archive.mjs`: same (70B→125B)
5. global `spine-worker-runner.mjs`: tail hand-variant block → canonical manifest text
   (832B→434B; `import os` line 21 becomes unused — harmless, kept deliberately)
6. global `evidence-command.mjs`: dotnet line gains the ponytail comment (script v1 missed that
   the global hand line lacked the comment — caught by apply.mjs's all-or-nothing validation
   refusing to write ANY file: the mechanism worked as designed)

### Apply / verify / idempotence / loud-failure proofs

- `node .spine/patches/apply.mjs` (from the lane): 7 writes — lane project tree 6/6 (pristine
  after pi re-sync at worker start) + engine tree t5 (the 4 engine patches already canonical
  post-migration). Exit 0.
- Idempotence: immediate second run = 11/11 "already applied", 0 writes. Exit 0.
- `node .spine/patches/verify.mjs`: project 2.10.0 6/6 + engine 2.8.0 5/5, exit 0.
- Loud-failure (scratch drift copy, `%TEMP%`, `--root`): t5 replacement×2 → verify reports
  `drifted`, exit 1; apply FAILs all-or-nothing — the revertable dotnet patch was NOT written
  (anchor state asserted after the failed apply). Exit 1.
- Missing-root: bogus `engineRoot` in a scratch manifest copy → both apply and verify FAIL
  closed with the remedy in the message. Exit 1.

### Mid-batch safety note (for the orchestrator)

The currently running wave-2 engine (pid from 12:20Z) holds the UNPATCHED global module in its
ESM cache — SP-031/SP-032 finalizations on THIS wave can still T-5 (manual playbook once more).
Any resume-handoff re-spawning the engine after this timestamp loads the PATCHED module and
finalizations stop self-dirtying mid-wave. The named post-land gate re-points at the NEXT wave.

## Step 3 — provenance-faithful fixture v2 + regression proof

`evidence/fixture-t5-v2.mjs` — **13/13 assertions GREEN across 5 cells** (scratch only, %TEMP%;
repo and both live installs never written; the pristine module tree is a sibling copy inside the
lane's gitignored node_modules for dep resolution, deleted after the run):

| cell | shape | result |
|---|---|---|
| 1 pristine-negative-control | pristine module, real caller shape (taskFolder = lane task folder) | commits `.DONE`, leaves `?? .reviews/` — the T-5 DirtyWorktree signature (preserved from SP-028) |
| 2 patched-real-caller-shape | patched module, same shape — the ONLY shape all 4 callers use in BOTH versions | `.reviews/` deleted inside the finalization call, post-commit porcelain CLEAN |
| 3 two-tree-wave1-repro | patch present on tree A on disk; engine module loaded from tree B (pristine) | residue survives — **the actual wave-1 mechanism: applied ≠ loaded** |
| 4 base-path-no-done | taskFolder = base-shaped packet path OUTSIDE the worktree, no base `.DONE` (wave-1's real base state) | early-return "`.DONE` is missing — worker did not finish cleanly", **NO commit** — the fingerprint wave-1's journal does NOT show (`lane.committed` fired) ⇒ the packet's base-path theory falsified; discharges the packet's base-shaped fixture requirement with its true semantics |
| 5 base-path-planted-done | same with a planted base `.DONE` | commit proceeds, lane residue survives (rmSync no-ops on base/.reviews) — the theory was CONSISTENT with this shape; git history (base `.DONE` absent until the 12:06Z merge) is what falsifies it |

The original SP-028 fixture's in-worktree taskFolder case still passes — it IS cell 2, the real
caller shape (the census found no caller passing anything else, in either engine version).

### Consumer census re-confirmed for the corrected expression

Done in Step 1 (recorded above): all 4 callers pass `taskFolderInWorktree` in BOTH 2.8.0 and
2.10.0 — no path arithmetic, no relative-path edge cases to enumerate; the packet's feared
shapes (`taskFolder` inside `worktreePath`, `projectRoot` absent/eq, `path.relative` escaping)
do not exist in the codebase.

### Proof boundary re-recorded

In-lane proof = fixture v2 cells 1–5 + apply/verify on both roots + idempotence/loud-failure.
REAL proof = the NEXT Level-2 wave's finalizations skipping the manual `.reviews/` recovery —
the named post-land gate re-points there (orchestrator acts at land, enabler 2). THIS wave's
engine (12:20Z process) still holds the unpatched module: SP-031/SP-032 finalizations may T-5
one final time (manual playbook), unless a resume re-spawns the engine after the Step-2 on-disk
patch — expected either way, not a reopen. The tax claim stays parked until the gate fires green.

## Step 4 — docs + pre-completion consult

### `.spine/patches/README.md`

t5 row added to the patch table; new **Two roots (SP-031)** section (project tree vs engine
tree, the wave-1 applied≠loaded lesson, engine-flag semantics, missing-root fail-loud); re-base
note appended to the T-12/mechanism bullets (canonicalization of the global hand-patches:
dotnet +`dotnet.exe`, tail unified to manifest text, fsync comments — all behavior-preserving).

### Pre-completion consult (solo; tool: `consult`; answering model not self-identified —
configured advisor per `.pi/bpx-consult.json`)

**Verdict: the fix is sound and correctly scoped.** Conditions given and discharged:

1. **Global-tree stability is recorded EMPIRICALLY, not by pin-logic** — the global tree's
   hand-patches date from 07-18/07-19 and survived four days of pi process starts (the tree is
   demonstrably not being re-synced; `npm:pi-spine` in the global `settings.json` packages is
   satisfied at 2.8.0). The durable risk is a DELIBERATE global pi-spine update wiping t5 —
   covered by the README re-apply trigger + verify.mjs pre-launch, plus the T-1 npm
   skip-subtlety (same-version reinstall does not re-extract; a true wipe requires removing
   the dir).
2. **NAMED LIMIT: verify proves the two KNOWN roots, not the loaded root.** If the launch
   convention changes (a different `spine` on PATH → a third tree), applied≠loaded reappears
   while verify stays green. **Recommendation to the orchestrator (land-time item):** the
   re-pointed post-land gate's evidence should include a process-cmdline / `where spine` check
   that the running engine resolves inside `engineRoot` — closes the class, not the instance.
3. **In-lane apply now also touches the global tree** (this and future workers): post-land it
   is an idempotent skip; two lanes racing would write identical bytes in a tiny window.
4. **2.8.0-runs / 2.10.0-believed discrepancy: recorded, not fixed here.** The engine upgrade
   stays an owner decision (T-1 row standing order); the manifest hedges both versions
   (anchors byte-present and tested on 2.8.0 + 2.10.0).
5. **Fixture count honesty:** the cell-4 journal pointer was a tautological assertion — removed
   from the count. Fixture v2 = **12 falsifiable assertions, all GREEN**, + 1 named
   journal-evidence pointer (re-run after the edit: GREEN).

### Engine-review presence log (T-2 heading format)

| step | spine_review_step call | result |
|---|---|---|
| 1 | plan, baseline a82775df~1 | **engine-skipped (SP-195)** — nested reviewer spawn blocked inside worker session; verdict null; engine reviews run after .DONE |
| 2 | plan, baseline cdafc78b~1 | engine-skipped (SP-195), same |
| 3 | plan, baseline 0f197288~1 | engine-skipped (SP-195), same |
| 4 | (below, post-commit) | — |

Code + final review: not spawned by the worker (SP-194/SP-195) — the batch engine runs them
after `.DONE`.

### Durable-lesson candidates (orchestrator harvests at land; enabler 2 — worker does NOT
edit port-lessons.md or task-board.md)

1. **applied ≠ loaded**: a patch mechanism must verify the install the process actually loads,
   not the install nearest the repo. Two-tree fingerprint: process cmdlines (`Get-CimInstance
   Win32_Process`), never the repo pin.
2. **The engine version is whatever the `spine` CLI on PATH loads** — the owner believes
   2.10.0 runs; the CLI proves 2.8.0 runs. Upgrade decision recorded on T-1's row, not here.
3. **Fixture-provenance lesson, corrected**: SP-028's fixture was faithful to the real caller
   shape — the flawed provenance was the INSTALL TREE, not taskFolder. "Mirror the real call"
   means the real process, not just the real arguments. (The wave-1 port-lessons entry's
   taskFolder theory is falsified by the `.DONE`-check + `lane.committed` event order —
   orchestrator should correct it at land.)
4. **A wrong root-cause theory can be fixture-consistent**: cell 5 shows the base-path theory
   reproduces the exact wave-1 signature with one planted file. The discriminating evidence was
   git HISTORY (base `.DONE` absent until the merge), not any fixture.
5. **T-5 row CLOSE criteria for the orchestrator**: re-point the named post-land gate at the
   next Level-2 wave + add the process-cmdline engine-root check (named limit 2); THIS wave's
   finalizations may T-5 once more (running engine predates the on-disk patch — expected, not
   a reopen).
