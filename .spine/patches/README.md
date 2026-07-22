# .spine/patches — durable pi-spine local patches (T-1)

## Why this exists

This repo carries **local patches inside `.pi/npm/node_modules/pi-spine`** that are
load-bearing for running spine batches on Windows. `npm install` / any pi-spine
update **silently wipes them** — and already has: the 2026-07-21 empirical
inventory (SP-020 record.md) found two of the three named patches **lost** to a
past reinstall (dotnet evidence allowlist, worker-tail `@file`), invisible until
the failure mode recurs. This directory is the durable copy plus the mechanism to
re-apply and verify.

## The patches (manifest.json — the machine-readable source of truth)

| id | target | what | why |
|----|--------|------|-----|
| `fsync-r-plus-abort` | `src/batch/abort.mjs` | `openSync(archivePath, "r")` → `"r+"` | Windows EPERMs fsync on read-only handles (port-lessons 2026-07-18) |
| `fsync-r-plus-lifecycle-archive` | `src/batch/lifecycle-archive.mjs` | same | same root cause |
| `dotnet-evidence-allowlist` | `src/batch/evidence-command.mjs` | add `dotnet` to `ALLOWED_EVIDENCE_EXECUTABLES` | this repo's gate-evidence testCommand is `dotnet build/test` (T-1) |
| `worker-tail-at-file` | `bin/spine-worker-runner.mjs` | tails >16KB → `%TEMP%` file, passed as `@file` | inline argv exceeded the Windows 32,767-char CreateProcess limit — SP-004 worker died silently ×3 (port-lessons 2026-07-19) |
| `skill-headed-evidence-sizing` | `skills/create-spine-tasks/SKILL.md` | re-insert the T-11 headed-evidence sizing amendment | task-authoring guidance; dies on reinstall (found empirically as an undocumented 4th patch) |
| `t5-reviews-autoclean` | `src/batch/lane-commit.mjs` | delete `.reviews/` at `commitLaneWorktree` entry | engine review phases write `.reviews/` into the lane post-`.DONE`; git 2.49 `check-ignore --no-index` blank-line quirk mis-skips it → deterministic DirtyWorktree on every Level-2 land (T-5, 18 occurrences) |

## Two roots (SP-031, 2026-07-22 — the wave-1 lesson)

There are **two pi-spine installs** and they are NOT interchangeable:

- **project tree** (`.pi/npm/node_modules/pi-spine`, repo + each lane worktree) — what pi
  sessions load skills/tools from; pi re-syncs it to the `.pi/settings.json` pin on process
  start (currently `npm:pi-spine@2.10.0`).
- **engine tree** (`manifest.engineRoot`, the global CLI install the batch engine actually
  EXECUTES — `spine.mjs` / `spine-worker-runner.mjs` run from there, process-cmdline-proven;
  see `spine-tasks/CONTEXT.md` §Execution-policy PATH note).

SP-028's t5 patch verified green on the project tree while the engine ran the global tree
unpatched — wave `20260722T101444` T-5'd both lanes on first live use (**applied ≠ loaded**).
Patches now carry `"engine": true` when their code runs in the engine process (fsync×2,
dotnet, tail, t5); `apply.mjs`/`verify.mjs` process **both roots** by default (engine root
gets engine-flagged patches only; `--root` keeps single-root scratch semantics; a missing
engine root fails loudly with the remedy). The t11 skill amendment is project-tree-only —
CCP-specific text must not land on the globally shared skill copy. verify.mjs exit 0 now
means applied **==** loadable for engine-behavior patches.

Deliberately **not** patched locally:

- `src/batch/journal.mjs` fsync — upstream already ships `"r+"` at 2.8.0/2.10.0.
- `windowsHide` spawn mass-patch — **absent** from the current install (2 upstream
  occurrences in `terminate-tree.mjs`, identical to pristine) and **not
  load-bearing**: engine reviews fire without it (T-2 CLOSED 2026-07-19).
- **T-12 (merge-time tracked-ignored scan)** — feasible mechanically, excluded as
  unsafe: `filterGitignoredPaths` (`src/batch/git-helpers.mjs:22`,
  `check-ignore --no-index`) is a shared helper also feeding lane-commit/T-5
  classification, and the merge-path consumer (`engine-lanes/merge.mjs`
  `tryAutoResolveOutOfScopeMergeConflict` → `git rm --cached -f` fallback) cannot
  be honestly exercised in scratch. Upstream fix sketch: drop `--no-index` (or
  set-minus `git ls-files --cached`) and never suggest `git rm --cached` for
  paths present on the merge target (`diagnosis-merge-failure.mjs`
  `buildGitignoredMergeRepairCommand`). See SP-020 record.md. **SP-028 sharpening:
  the T-5 half of that helper's blast radius is now patched downstream
  (`t5-reviews-autoclean` deletes `.reviews/` at `commitLaneWorktree` entry);
  the underlying git 2.49 blank-line `check-ignore --no-index` quirk in
  `filterGitignoredPaths` itself is STILL unpatched (T-12's exclusion stands).**
- **Mechanism note (SP-028):** `apply.mjs` phase 2 writes each patch against the
  ORIGINAL file content — two patches targeting the SAME file would clobber each
  other (latent; all six current patches target distinct files). Re-base to
  sequential re-read before authoring a same-file multi-patch.
- **Re-base note (SP-031):** SP-028's t5 entry was re-based 2026-07-22 after its named
  post-land gate FAILED on first live use (wave `20260722T101444`, both lanes). Root
  cause was NOT the path expression (`taskFolder` is the lane task folder at all 4
  finalization callers in both versions — journal-proven) but the patch sitting on the
  wrong install tree; the fix is the two-root model above plus canonicalizing the
  global tree's hand-patches (dotnet text now includes `dotnet.exe`; tail block unified
  to the manifest text; fsync comments added — all behavior-preserving). Evidence:
  `spine-tasks/SP-031-t5-anchor-rebase/record.md` + fixture v2 (5 cells, 13/13).

## How — after ANY pi-spine install/update

```bash
node .spine/patches/apply.mjs && node .spine/patches/verify.mjs
```

- `apply.mjs` is **idempotent** (re-run = no-op when already applied) and
  **all-or-nothing**: every anchor is validated before any file is written; a
  missing/duplicated anchor fails loudly with the patch id (version drift —
  re-base deliberately, never force).
- `verify.mjs` reports per-patch `applied` / `missing` / `drifted`; **exit 0 only
  when all are applied**. Run it before any batch.

Patches are **anchor-based** (unique surrounding content), never line-number-based:
a pi-spine version bump that touches a patched site fails loudly instead of
mis-patching. Anchors verified byte-identical on pi-spine **2.8.0** (installed)
and **2.10.0** (latest at authoring); `testedVersions` in the manifest records this.

## The trigger — honest automation limit

**Full npm-hook automation is NOT delivered.** A `postinstall` hook would have to
live inside the pi-spine package itself (engine modification) or in the `.pi/npm`
throwaway package.json that the reinstall wipes — both self-defeating. The durable
shape is therefore:

1. **Orchestrator/operator runs apply+verify after any `pi install` / pi-spine
   update** (named manual trigger — recorded on board row T-1 as a named limit);
2. **`verify.mjs` is the loud missing-patch check** — cheap enough to run before
   every batch launch; steering-loop templates should wire it as a pre-launch step;
3. The **real `.pi/npm` reinstall + re-apply evidence is a post-land orchestrator
   gate with the run parked** (a worker reinstalling the engine that hosts it is
   incoherent) — SP-020's in-lane evidence is the full scratch cycle
   (`%TEMP%/sp020-scratch2`, see `spine-tasks/SP-020-spine-patch-mechanism/record.md`).

## Links

- Board row: `client/docs/task-board.md` T-1 (plus T-12 excluded-patch decision, T-2 windowsHide clearance)
- Lessons: `client/docs/port-lessons.md` 2026-07-18 (fsync, allowlist), 2026-07-19 (`@file`, 32KB CreateProcess)
- Evidence: `spine-tasks/SP-020-spine-patch-mechanism/record.md`
