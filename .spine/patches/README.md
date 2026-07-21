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
  `buildGitignoredMergeRepairCommand`). See SP-020 record.md.

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
