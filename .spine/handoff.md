# HANDOFF — 2026-08-14 — wave 29 AUTHORED + LAUNCHED (SP-072), nothing landed

**Status: BATCH LAUNCHED.** Written in the authoring commit, **before** `spine batch start` — that is what
bought waves 27 and 28 their fast-forwards, and it is the rule: any orchestrator commit to base between
`batch start` and integrate converts a free tree-identity proof into a manual one.
Wave 28 (SP-071) is landed at `d1c69617`, reconciled at `5851179b` + `993d253f`, archived.
**Base floor at launch: 1010 unit / 35 headless / 2 NAMED skips, build 0W/0E.**

## Which phase is yours

Reconcile first, then classify. Do **not** trust this file's phase label — check `spine status --diagnose`.

- **Batch running → port.txt case C: EXIT AT ONCE.** The shell owns the waiting.
- **Batch finished / `needs_integrate` (`macroPhase: "gating"`) → case A: LAND IT.** The obligations below
  are yours. A finished batch is not a landed one.
- **No batch + claimable work → case B.** Reconcile from git and the board before authoring anything new.

## What is in flight (SP-072 — an abandoned player construction must never reach the mixer)

Board row: the two `CreatePlayer` sites, filed at the wave-28 land. Single lane.

Both `CreatePlayerCore` bodies end with `_device.MasterMixer.AddComponent(player)`
(`SoundFlowAudioBackend.cs:118`, `SoundFlowDtrhAudio.cs:112`) before returning. So the blocking is the
*symptom*: a construction whose caller stopped waiting **attaches itself to the live mixer** — ghost play
plus leak — and disposing that orphan races device teardown, which SP-071 just moved onto a background
thread, so the race is now concurrent by construction.

**Required deliverable = the ORPHAN INVARIANT, not the bound.** Never reaches `MasterMixer`, never plays,
disposed exactly once, disposal **ordered** against device teardown. Orphan safety is the precondition the
way "the give-up path must never touch `_backend`" was in SP-071.

**The bound is CONDITIONAL, decided in Step 1 by census against a rule pre-authorized both ways:** if every
caller of both seams can accept the port's existing typed no-player outcome (`SoundOutcome.Unavailable` /
`Failed`, already used at all three `SoundArbitration` sites), the bound lands in-packet; if any caller
structurally cannot, it bounds what it can and names the remainder as the next row.

**The design constraint that is stated rather than discovered:** `SoundFlowAudioBackend` and
`SoundFlowDtrhAudio` have **zero test coverage** and cannot be constructed headless. The packet requires the
mechanism to live where a headless fact can bind it, and the single residual line (the real `AddComponent`)
to be named as verified by reading only.

## LAND OBLIGATIONS FOR THIS WAVE

1. **Check the ordering pin is not vacuous.** The packet forbids a new `IndexOf`-sentinel comparison,
   because that shape is itself an open board row filed at the wave-28 land. Read the assertion; do not
   trust its name. A missing event must FAIL, not pass.
2. **Check WHERE the mechanism landed and whether a fact actually reaches it.** If the orphan logic sits
   inside a backend class that cannot be instantiated headless, the pins are decoration. This is the
   packet's central design constraint and the reviewer's real job at land.
3. **Read the census's decision-rule branch and FILE whatever it left unbounded**, with the caller and the
   reason. An unfiled remainder is phantom debt.
4. **Confirm SP-071's give-up residue row is still OPEN.** The packet is forbidden from closing it; a worker
   that "helpfully" does so has changed a different mechanism.
5. **Append the wave-29 lessons to `client/docs/port-lessons.md` AT LAND — not before** (spine
   `referenceDocs`, `.spine/spine-config.json:97`: editing it mid-batch mutates a live worker's input).
6. **Verify the `.DONE` template fix worked** (this wave is its first live test). A healthy lane-commit
   produces `feat(SP-072): batch <id> worker completion` containing `.DONE`. If the batch is recorded
   `failed` on `GitignoredDirtyWorktree` again, the line did not take — recovery below.
7. **Check whether the worker recorded owed wording** for `async-lifecycle-fault-contract.md` beside §5.6
   (read-only for it). Policy text lands via the orchestrator (SP-059 precedent).

## IF THE BATCH IS RECORDED `failed` ON `GitignoredDirtyWorktree`

The wave-28 class: `laneCommit` (`src/batch/lane-commit.mjs:326-368`) reaches its fail-closed branch **only
when `stageable.length === 0`**, and a worker that commits its own `.DONE` leaves nothing to stage — so the
lane's `.pi/npm/**` (the T-14 hook's per-lane patched install) is all that remains. The auto-sanitize escape
needs **every** ignored path to match `GITIGNORED_ARTIFACT_MARKERS`; `.pi/npm/.gitignore`, `package.json`,
`package-lock.json` and `worktree-setup-hook.log` match none (only paths containing `node_modules/` do).

**Do NOT add `.pi/npm/` to the markers** — that makes `sanitizeGitignoredArtifactsBeforeLaneCommit` DELETE
the lane's patched install and re-creates the SP-035/039 pristine-reinstall failure T-14 exists to prevent.

Recovery, all supported paths, never a `batch-state.json` hand-edit:
1. `git clean -fdX` in the finished lane worktree (it is done with; `verify.mjs` runs in the MAIN checkout
   and land verification uses a fresh scratch worktree).
2. `spine batch force-merge --wave 0` — bypasses the §17.4 mixed-outcome gate; allowed only in phase
   `failed`/`paused`. **It does not schedule a merge by itself** and alone dead-ends on "No pending tasks to
   resume" (`resume` needs a pending task or `phase === "merge_blocked"`).
3. `spine batch retry SP-072` (status → pending, phase → paused), **with the worker's `.DONE` copied into
   the MAIN task folder** so `engine.mjs:310`'s `doneOnDisk` check routes to `skipTaskDoneOnDisk` — the task
   is recorded **succeeded** (journal `task.skipped_done_on_disk`) with no worker re-run.
4. **Delete that `.DONE` again** once the task is recorded succeeded — untracked, it blocks the merge as
   dirt (`merge failed without unmerged paths (dirty: …/.DONE)`). The lane branch carries the real one.
5. `spine batch resume --force` → merge → gate opens.

## Standing land discipline (unchanged, learned the hard way)

- **Never trust the gate's own evidence (T-3).** Verify the merged state yourself in a scratch worktree.
  `diff-stat.txt` is a TWO-dot diff — disprove it with three dots.
- **Verify BEFORE `spine integrate`**, not after: verifying after means unwinding a merge on the base branch.
- **Tree identity:** fast-forward → `git diff <verified> HEAD` EMPTY. Merge → the SCOPED form
  `git diff <verified> HEAD -- client/ scripts/ ConditioningControlPanel/ docs/`, naming non-code deltas.
- **Full contract, in this order:** `dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs`.
  The wrapper is `--no-build` by design; standalone it measures the LAST build and names the wrong cause.
- **The land's LAST action verifies the tree actually being pushed.** Commit the reconciliation FIRST, then
  run the contract, then push (wave 18 shipped a red base by editing after its verification run; the wave-28
  land consult caught the same slip in the plan and it was corrected before the push).
- **A reviewer's "non-blocking" note is a board row, never a post-verification edit** (wave-28 lesson).
- **Bite matrix, one source at a time.** A shared revert falsely verifies pins never exercised (SP-067);
  a pin whose FIXTURE cannot reach the mechanism passes with its own guard reverted (SP-070).
- **Cross-thread packets:** ask for the repeated-run count (>= 20 filtered iterations) and treat any
  timing-dependent test as a defect, not a flake.
- **Never set `CCP_DATA_ROOT` for a floor run** (`port-workflow.md:204`).
- **`allowedSkips` pins 5 names; 2 skip on Windows. THE ASYMMETRY IS CORRECT.**
- **`node .spine/patches/verify.mjs` FAILS in a scratch worktree and that is expected** (`.pi/npm` is
  per-checkout and gitignored). Run it in the MAIN checkout — done this phase, exit 0 (9 project + 5 engine).
- **`cmd | tail; echo $?` reports TAIL's exit code.** Use `${PIPESTATUS[0]}` or redirect to a file.
- **A doc a test READS is code — check READ vs merely NAMED.** `port-audit-prompt.md`, `floor.json`,
  `vacuous-shape-ledger.json` are read. `task-board.md`, `port-lessons.md`, `port-digest.md`,
  `async-lifecycle-fault-contract.md` and `client/memories/**` are not (grep-verified at the wave-28 land).
- **`spine preflight`'s "Pre-landed contract risk" warning compares against `main`**, the WPF branch with no
  `client/` tree. Never redirect `fileScopeMustChange` to docs.
- **Landed rows stay WIP/OPEN until the owner ratifies;** flip to DONE only with a RATIFIED citation.
- **Budget the board-row update INTO the land.** ENABLER 2 keeps `task-board.md` out of worker scope.
- **`ctx_batch_execute` runs its commands through PowerShell, not bash** — `export`, `2>/dev/null` and
  `$HOME` break. Use the plain Bash tool for spine and git commands (observed this phase).

## Claimable work after this lands (the board is authority, this is a pointer)

Filed at the wave-28 land and still open: the **five disk-store follow-up waits** from SP-071's 18-site
census (S–M); the **give-up residue accumulation** row (S–M, and SP-072 must not close it); the
**runtime-vacuity CLASS** row (S+) — which the wave-29 decomposition consult named as **wave 30**, since it
is at three occurrences and the recurrence rule says it is overdue. Older: the endpoint-watcher row
(Windows-only, headed gate); row `:53`'s remaining `§C`/`§D` items; T-18; the SP-069 hygienic-surface-id
row; the `ProcessEnvCollection` co-location residual; the `CapabilityRegistry` probe row; the `allowedSkips`
bans-are-text row (S, ~10 lines); T-17's auditor **run**; the named privacy flake.

## Decisions on record — do not re-open

- **Owner default in force: BACK TO WPF PARITY.** Asked at waves 23 and 24, unanswered, **not re-asked**.
- **`_initLock` stays.** SP-071 moved the blocking; it did not remove the lock.
- **The sizing pass over Goon `:44` / FYP `:45` / Trainer Card `:51` / Haptics v2 `:52` is DEFERRED,
  MACHINE-GATED — not dropped.** The standing offer to write it for a desktop session is in the digests.
- **Two lanes are not available while any lane adds a test.** Every test-adding packet bumps
  `client/tests/floor/floor.json` in the same commit as the test, so lane-mates always collide there —
  green alone, RED at merge (the SP-057/SP-058 precedent, re-derived by the wave-29 consult).

## Instrument notes

- **Consult truncation is board row T-18.** Wave 29's decomposition verdict surfaced complete on the first
  call under a 250-word cap (9th consecutive wave). **Never stitch a verdict out of reasoning**; an
  unstitched non-verdict is a MISSING consult. Use `mode: "solo"` explicitly (T-7).
- **Verify the advisor's checkable claims before encoding.** Done this phase: both `AddComponent` calls, the
  lock-free `SoundFlowAudioBackend.Dispose`, the typed no-player idiom at all three `SoundArbitration` call
  sites, and the existing late-player disposal precedent (`SoundArbitration.cs:556-570`) were read in the
  tree before being written into the packet. Two findings the packet deliberately does NOT name (so the
  census is a real read, not a transcription): one of the five call sites constructs **inside a lock**, and
  two of them have no `try`/`catch` at all.
- **hermes memory is FULL** (auto-consolidation timed out). The durable record is
  `client/memories/port-status.md`, current through wave 28. Do not rely on `memory_add` this session.

## Machine facts (laptop)

pi-spine 2.10.0 · durable memory fallback `client/memories/port-status.md` (hermes store full) · **WSL zero
distros → every Linux gate is a standing named limit, never faked** · **no real audio device, endpoint death,
or wedged native construction can be induced here — the manual gate is named in the packet's honesty cell,
never simulated as evidence** · **MCP 0/3 connected this phase** (cached metadata only; named limit, never a
blocker — and SP-072 has no AXAML, so the A-013 advisory step is not a gate) · `Z:\CCP Vids`, DISPLAY3 and
the WSL2 Linux gate are **DESKTOP-only** · batch launched with `SPINE_WORKER_PI_TIMEOUT_MS=14400000` ·
9 project + 5 engine patches verified applied before authoring (`verify.mjs` exit 0).
