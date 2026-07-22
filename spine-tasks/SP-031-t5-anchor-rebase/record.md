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
