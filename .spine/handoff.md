# HANDOFF — 2026-08-14 — wave 28 AUTHORED + LAUNCHED (SP-071), nothing landed

**Status: BATCH LAUNCHED.** Written in the authoring commit, **before** `spine batch start` — that is what
bought wave 27 its fast-forward, and it is the rule now: any orchestrator commit to base between
`batch start` and integrate converts a free tree-identity proof into a manual one.
Wave 27 (SP-070) is landed at `9e6498b6`, reconciled at `c4c979df`, archived.
**Base floor at launch: 1005 unit / 35 headless / 2 NAMED skips, build 0W/0E.**

## Which phase is yours

Reconcile first, then classify. Do **not** trust this file's phase label — check `spine status --diagnose`.

- **Batch running → port.txt case C: EXIT AT ONCE.** The shell owns the waiting.
- **Batch finished / `needs_integrate` (`macroPhase: "gating"`) → case A: LAND IT.** The obligations below
  are yours. A finished batch is not a landed one.
- **No batch + claimable work → case B.** Reconcile from git and the board before authoring anything new.

## What is in flight (SP-071 — host close must not wait on a wedged native audio probe)

Board row: the `_initLock` teardown residual filed at the wave-27 land. Single lane.

`SoundArbitration.Dispose` takes `_initLock` (`:1087-1091`) before `_backend.Dispose()` (`:1093`), and
`TeardownBarkPipeline` (`DtrhHostWindow.axaml.cs:255-262`) is called from the host-window close handler
(`:153`) **on the UI thread** — in exactly the dead-endpoint scenario SP-070's recovery exists for. The
host is a **non-modal child window** (`DtrhLaunchCoordinator.cs:167`), so close is not process exit.

**WPF parity framing:** `5a168554` ("stop the UI thread joining a wedged render thread, and name the next
one"), upstream's pass over this class for the v6.6.3 hang cluster. Port the remedy **shape** — bound the
wait, degrade instead of block, name what cannot be bounded — never its WPF-specific mechanics. The port's
own `async-lifecycle-fault-contract.md` §5 already makes the UI boundary post-only.

**THE TRAP, and it is in the packet's `## Do NOT`:** a timeout on the `_initLock` acquisition that then
continues runs `_backend.Dispose()` while a native init is in flight — the **process-fatal**
concurrent-native-call class `_initLock` exists to prevent. The design is to move the teardown **off the UI
thread**: unbounded lock wait on a background thread, bounded UI-side wait, typed give-up that never
touches the backend, exactly-once disposal even after a give-up, `Dispose` still idempotent.

## LAND OBLIGATIONS FOR THIS WAVE

1. **Read the ORDERING fact yourself, not just the bounded-return fact.** The wave is only safe if the
   backend is provably not disposed while a native call is in flight. A green suite showing `Dispose`
   returning fast is *also* consistent with the process-fatal shape. Check that the fake records the moment
   `TryInit` returns and the moment it is disposed, and that the assertion is about their **order**.
2. **Check each pin's fixture reaches its mechanism.** SP-070's single-flight pin passed with its own guard
   reverted until the fixture was corrected — same class, one wave earlier, on the same file. Re-run the
   bite matrix yourself, one source at a time.
3. **File the `CreatePlayer` row.** `SoundFlowAudioBackend.CreatePlayer` (`:108` → `OffSyncContext.Run`,
   `AudioSeams.cs:150`) and `SoundFlowDtrhAudio.CreatePlayer` (`:100`) block the UI thread inside a native
   `AssetDataProvider` construction, unbounded. The packet censuses them deliberately and does not fix them
   (they change a synchronous seam contract; a late-completing construction adds itself to `MasterMixer` —
   ghost play plus leak, disposal racing device teardown). **Orphan disposal is that row's central
   acceptance.** Unfiled, it is phantom debt.
4. **Use the census.** The packet produces a verdict per blocking wait in `client/src/**` (~30 sites) —
   file what it surfaces as rows rather than letting the table die in `record.md`.
5. **Append the wave-28 lessons to `client/docs/port-lessons.md` AT LAND — not before** (spine
   `referenceDocs`, `.spine/spine-config.json:97`: editing it mid-batch mutates a live worker's input).
6. **Check whether the worker recorded owed wording** for `async-lifecycle-fault-contract.md` §5 (read-only
   for it). Policy text lands via the orchestrator (SP-059 precedent).

## Standing land discipline (unchanged, learned the hard way)

- **Never trust the gate's own evidence (T-3).** Verify the merged state yourself in a scratch worktree.
  `diff-stat.txt` is a TWO-dot diff — disprove it with three dots.
- **Verify BEFORE `spine integrate`**, not after: verifying after means unwinding a merge on the base branch.
- **Tree identity:** fast-forward → `git diff <verified> HEAD` EMPTY. Merge → the SCOPED form
  `git diff <verified> HEAD -- client/ scripts/ ConditioningControlPanel/ docs/`, naming non-code deltas.
- **Full contract, in this order:** `dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs`.
  The wrapper is `--no-build` by design; standalone it measures the LAST build and names the wrong cause.
- **The land's LAST action verifies the tree actually being pushed.** Commit the reconciliation FIRST, then
  run the contract, then push (wave 18 shipped a red base by editing after its verification run).
- **Bite matrix, one source at a time.** A shared revert falsely verifies pins never exercised (SP-067) —
  and a pin whose FIXTURE cannot reach the mechanism passes with its own guard reverted (SP-070).
- **This packet's facts are cross-thread:** ask for the repeated-run count (>= 20 filtered iterations) and
  treat any timing-dependent test as a defect, not a flake.
- **Never set `CCP_DATA_ROOT` for a floor run** (`port-workflow.md:204`).
- **`allowedSkips` pins 5 names; 2 skip on Windows. THE ASYMMETRY IS CORRECT.**
- **`node .spine/patches/verify.mjs` FAILS in a scratch worktree and that is expected** (`.pi/npm` is
  per-checkout and gitignored). Run it in the MAIN checkout — done this phase, exit 0 (9 project + 5 engine).
- **`cmd | tail; echo $?` reports TAIL's exit code.** Use `${PIPESTATUS[0]}` or redirect to a file.
- **A doc a test READS is code — check READ vs merely NAMED.** `port-audit-prompt.md`, `floor.json`,
  `vacuous-shape-ledger.json` are read. `task-board.md`, `port-lessons.md`, `port-digest.md` and
  `client/memories/**` are not.
- **`spine preflight`'s "Pre-landed contract risk" warning compares against `main`**, the WPF branch with no
  `client/` tree. Never redirect `fileScopeMustChange` to docs.
- **Landed rows stay WIP/OPEN until the owner ratifies;** flip to DONE only with a RATIFIED citation.
- **Budget the board-row update INTO the land.** ENABLER 2 keeps `task-board.md` out of worker scope.

## Decisions on record — do not re-open

- **Owner default in force: BACK TO WPF PARITY.** Asked at waves 23 and 24, unanswered, **not re-asked**.
- **`_initLock` stays.** The race it closes is the process-fatal class; this wave moves the blocking, it
  does not remove the lock.
- **The sizing pass over Goon `:44` / FYP `:45` / Trainer Card `:51` / Haptics v2 `:52` is DEFERRED,
  MACHINE-GATED — not dropped.** The standing offer to write it for a desktop session is in the digests.
- **The two `CreatePlayer` sites are OUT of SP-071 by decision**, not by oversight — orphan disposal makes
  them their own packet.

## Instrument notes

- **Consult truncation is board row T-18.** Wave 28's decomposition verdict surfaced complete on the first
  call under a 200-word cap (8th consecutive wave). **Never stitch a verdict out of reasoning**; an
  unstitched non-verdict is a MISSING consult. Use `mode: "solo"` explicitly (T-7).
- **Verify the advisor's checkable claims before encoding.** Done this phase: the `Dispose` lock-then-dispose
  sequence, the close-handler call site, and the non-modal `window.Show(_owner)` were all read in the tree
  before being written into the packet.
- **hermes memory is FULL** (9358/10000 chars; auto-consolidation timed out). The durable record is
  `client/memories/port-status.md`, which is current through wave 27. Do not rely on `memory_add` this
  session; fix or prune the store if a future phase needs it.

## Claimable work after this lands (the board is authority, this is a pointer)

The `CreatePlayer` row this land must file; the **endpoint-watcher** row (Windows-only, headed gate);
row `:53`'s remaining `§C`/`§D` items; **T-18**; the SP-069 hygienic-surface-id row; the
`ProcessEnvCollection` co-location residual; the `CapabilityRegistry` probe row; the `Assert.All`
unenumerated shape; the `allowedSkips` bans-are-text row; T-17's auditor **run**; the named privacy flake.

## Machine facts (laptop)

pi-spine 2.10.0 · durable memory fallback `client/memories/port-status.md` (hermes store full) · **WSL zero
distros → every Linux gate is a standing named limit, never faked** · **no wedged native audio call can be
induced here — the manual gate is named in the packet's honesty cell, never simulated as evidence** ·
**MCP not re-probed this phase** (named limit, never a blocker) · `Z:\CCP Vids`, DISPLAY3 and the WSL2 Linux
gate are **DESKTOP-only** · batch launched with `SPINE_WORKER_PI_TIMEOUT_MS=14400000` · 9 project + 5 engine
patches verified applied before authoring (`verify.mjs` exit 0).
