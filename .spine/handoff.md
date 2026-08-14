# HANDOFF — 2026-08-14 — wave 28 LANDED (SP-071), nothing in flight

**Status: NO ACTIVE BATCH.** Wave 28 landed at `d1c69617` (fast-forward), reconciled in the commit that
carries this file, archived. **Base floor: 1010 unit / 35 headless / 2 NAMED skips, build 0W/0E.**
**Next unused task ID: SP-072.**

## Which phase is yours

Reconcile first, then classify. Do **not** trust this file's phase label — check `spine status --diagnose`.

- **Batch running → port.txt case C: EXIT AT ONCE.** The shell owns the waiting.
- **Batch finished / `needs_integrate` (`macroPhase: "gating"`) → case A: LAND IT.** A finished batch is not
  a landed one.
- **No batch + claimable work → case B: AUTHOR + LAUNCH ONE WAVE.** This is the expected next phase.
  Reconcile from git and the board before authoring anything new.

## THE ONE-LINE CHANGE WAVE 29's PACKET MUST CARRY

**Tell the worker: create `.DONE`, do NOT commit it — spine's lane-commit stages it.**

Wave 28's worker did everything right and the batch was still recorded `failed`. Spine's post-worker
`laneCommit` (`src/batch/lane-commit.mjs:326-368`) reaches a fail-closed `GitignoredDirtyWorktree` branch
**only when `stageable.length === 0`** — and this worker had committed its own `.DONE`, so the only thing
left in the lane was `.pi/npm/**`, the T-14 hook's per-lane patched install. The auto-sanitize escape needs
**every** ignored path to match `GITIGNORED_ARTIFACT_MARKERS`, and `.pi/npm/.gitignore`, `package.json`,
`package-lock.json` and `worktree-setup-hook.log` match none of them (only paths containing `node_modules/`
do). Wave 27 escaped by accident — its worker left `.DONE` uncommitted, so `stageable` was 1.

**Do NOT add `.pi/npm/` to the markers.** That makes `sanitizeGitignoredArtifactsBeforeLaneCommit` DELETE the
lane's patched install and re-creates the SP-035/039 pristine-reinstall failure T-14 exists to prevent.

**If it happens anyway, the recovery is (all supported paths, never a `batch-state.json` hand-edit):**
1. `git clean -fdX` in the finished lane worktree (it is done with; `verify.mjs` runs in the MAIN checkout
   and land verification uses a fresh scratch worktree).
2. `spine batch force-merge --wave 0` — bypasses the §17.4 mixed-outcome gate. Allowed only in phase
   `failed`/`paused`. **It does not schedule a merge by itself**; alone it dead-ends on
   "No pending tasks to resume" (`resume` needs a pending task or `phase === "merge_blocked"`).
3. `spine batch retry <taskId>` (status → pending, phase → paused), **with the worker's `.DONE` copied into
   the MAIN task folder** so `engine.mjs:310`'s `doneOnDisk` check routes to `skipTaskDoneOnDisk` — the task
   is recorded **succeeded** (journal `task.skipped_done_on_disk`) with no worker re-run.
4. **Delete that `.DONE` again** the moment the task is recorded succeeded — untracked, it blocks the merge
   as dirt (`merge failed without unmerged paths (dirty: …/.DONE)`). The lane branch carries the real one.
5. `spine batch resume --force` → merge → gate opens.

## Claimable work (the board is authority, this is a pointer)

Filed at the wave-28 land, all P2:
- **The two `CreatePlayer` sites** (`SoundFlowAudioBackend.cs:108` → `OffSyncContext.Run`,
  `SoundFlowDtrhAudio.cs:100`) — unbounded UI-thread block inside a native `AssetDataProvider`
  construction. **Orphan disposal is the central acceptance**, not a detail: a late-completing construction
  adds itself to `MasterMixer` (ghost play + leak) and disposing it races device teardown. The residual
  `PanicReset` player wait inside `SoundArbitration.Dispose` (SP-071 honesty cell 3b) and the unguarded
  `CreatePlayer`-vs-in-flight-teardown race ride with this row. Size M.
- **Five disk-store follow-up waits** from SP-071's 18-site census (`DtrhHostWindow:228`,
  `DtrhSaveSlots:467,469`, `IntakeHostContext:84,95`, `AssetSelectionDocument:61`, `AiMemoryStore:272`).
  A verdict per site, never a blanket refactor. Size S–M.
- **Two SP-071 test-shape residuals** the engine code review named non-blocking (an `IndexOf` ordering
  assertion that would pass vacuously if `init-returned` ever stopped being recorded — not vacuous today;
  a plain `List<string>` log read while the teardown thread may append). Test-only. Size S.

Older, still open: the **endpoint-watcher** row (`IMMNotificationClient`, Windows-only, headed gate);
row `:53`'s remaining `§C`/`§D` items; **T-18**; the SP-069 hygienic-surface-id row; the
`ProcessEnvCollection` co-location residual; the `CapabilityRegistry` probe row; the `Assert.All`
unenumerated shape; the `allowedSkips` bans-are-text row; T-17's auditor **run**; the named privacy flake.

## Standing land discipline (unchanged, learned the hard way)

- **Write this file in the AUTHORING commit, before `spine batch start`.** Any orchestrator commit to base
  between `batch start` and integrate converts a free tree-identity proof into a manual one. Waves 27 and 28
  both fast-forwarded because of this.
- **Never trust the gate's own evidence (T-3).** Verify the merged state yourself in a scratch worktree.
  `diff-stat.txt` is a TWO-dot diff — disprove it with three dots.
- **Verify BEFORE `spine integrate`**, not after: verifying after means unwinding a merge on the base branch.
- **Tree identity:** fast-forward → `git diff <verified> HEAD` EMPTY. Merge → the SCOPED form
  `git diff <verified> HEAD -- client/ scripts/ ConditioningControlPanel/ docs/`, naming non-code deltas.
- **Full contract, in this order:** `dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs`.
  The wrapper is `--no-build` by design; standalone it measures the LAST build and names the wrong cause.
- **The land's LAST action verifies the tree actually being pushed.** Commit the reconciliation FIRST, then
  run the contract, then push (wave 18 shipped a red base by editing after its verification run).
- **A reviewer's "non-blocking" note is a board row, never a post-verification edit** (wave-28 lesson).
- **Bite matrix, one source at a time.** A shared revert falsely verifies pins never exercised (SP-067) —
  and a pin whose FIXTURE cannot reach the mechanism passes with its own guard reverted (SP-070). SP-071
  closed that door by asserting the revert reds the pin **at its own ordering assertion**.
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

## Decisions on record — do not re-open

- **Owner default in force: BACK TO WPF PARITY.** Asked at waves 23 and 24, unanswered, **not re-asked**.
- **`_initLock` stays.** SP-071 moved the blocking; it did not remove the lock.
- **The sizing pass over Goon `:44` / FYP `:45` / Trainer Card `:51` / Haptics v2 `:52` is DEFERRED,
  MACHINE-GATED — not dropped.** The standing offer to write it for a desktop session is in the digests.
- **The two `CreatePlayer` sites were OUT of SP-071 by decision**, not oversight — orphan disposal makes
  them their own packet, now a filed row.

## Instrument notes

- **Consult truncation is board row T-18.** Wave 28's recovery consult surfaced a complete verdict on the
  first call under a 250-word cap (9th consecutive wave). **Never stitch a verdict out of reasoning**; an
  unstitched non-verdict is a MISSING consult. Use `mode: "solo"` explicitly (T-7).
- **Verify the advisor's checkable claims before encoding.** Paid off this phase: the advisor predicted
  contract-verify and review had never run for the failed task. The journal proved the opposite —
  `contract.verified ok=true` (10/10 checks), code review APPROVE, final review PASS, all **before** the
  lane-commit that failed. The rest of its verdict (force-merge over retry; reject the marker patch; fix the
  packet template instead) was adopted.
- **hermes memory is FULL** (auto-consolidation timed out). The durable record is
  `client/memories/port-status.md`, current through wave 28. Do not rely on `memory_add` this session.

## Machine facts (laptop)

pi-spine 2.10.0 · durable memory fallback `client/memories/port-status.md` (hermes store full) · **WSL zero
distros → every Linux gate is a standing named limit, never faked** · **no wedged native audio call can be
induced here — SP-071's manual gate is named, never simulated as evidence** · **MCP not re-probed this
phase** (named limit, never a blocker) · `Z:\CCP Vids`, DISPLAY3 and the WSL2 Linux gate are **DESKTOP-only**
· 9 project + 5 engine patches verified applied at this land (`verify.mjs` exit 0).
