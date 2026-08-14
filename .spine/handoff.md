# HANDOFF — 2026-08-14 — wave 27 LANDED (SP-070), BATCH `20260813T232637` COMPLETE AND ARCHIVED

**Status: PARKED. No batch is running.** SP-070 succeeded, gate approved, integrated into `feat/crossplatform`
at **`9e6498b6` as a FAST-FORWARD** (the integrated tip IS the SHA verified in the scratch worktree — the
first ff since wave 24, bought by writing this file in the authoring commit), batch archived.
**Base floor after this wave: 1005 unit / 35 headless / 2 NAMED skips, build 0W/0E** (996 → 1005: +9 facts).

## Which phase is yours

Reconcile first, then classify. Do **not** trust this file's phase label — check `spine status --diagnose`.

- **No batch + claimable work → case B: author + launch one wave.** That is this file's expected next phase.
- **Batch running → port.txt case C: EXIT AT ONCE.** The shell owns the waiting.
- **Batch finished / `needs_integrate` (`macroPhase: "gating"`) → case A: LAND IT.** The wave-27 obligations
  below are DISCHARGED; a new wave writes its own. A finished batch is not a landed one.

## What landed (wave 27)

**SP-070 — the audio session-disable expires** (board row `:53`, the v6.7.x `§C` audio line; upstream fix
`d33b5d8d`, `#778`/`#779`). Single lane, 0 failures, zero recovery cycles.

The port had faithfully ported WPF's **old** behavior — `_audioDisabledForSession` set on zero endpoints or a
failed `TryInit`, every play on every channel refused for the process lifetime, and the sole product
`Initialize` call made once at DTRH host-window construction. WPF fixed exactly that. Now: a 30s window armed
from the injected `ISoundClock`, and an expired-cooldown play attempt schedules a **single-flight** re-probe
that reuses `Initialize` off the discovering thread — that caller is refused typed, never blocked, so
recovery lands on the FOLLOWING attempt.

**Two things the packet did not contain, both named on the board rather than buried:**
- **The worker's pre-approach consult found a real defect the packet had not:** `ReadyLocked` checked
  `!_initialized` **before** the disable flag, and `_initialized` is set only on success — so after the
  zero-endpoint STARTUP failure (the exact defect) every play refused as "not initialised" and the recovery
  branch was unreachable. Reordered, reasons rewritten honestly.
- **`_initLock` is a SCOPE ADDITION** from the worker's pre-completion consult: the probe is the port's first
  cross-thread `IAudioBackend` access and that seam has no internal synchronization. Landing it was correct.
  Its residual — `Dispose` takes it before backend teardown, and `TeardownBarkPipeline` runs on the **UI
  thread** from the host-window close handler — is **its own P2 row** (land-consult condition).

## LAND OBLIGATIONS FOR THIS WAVE — **ALL DISCHARGED 2026-08-14**

1. **Healthy-session negative control, single-flight and no-busy-loop pins read directly** on the merged tree
   (1 init + 1 enumeration + zero recovery log lines when nothing fails; 32 concurrent attempts → exactly one
   backend init; exactly one probe per cooldown window). **Bite matrix re-run INDEPENDENTLY**, one source at
   a time: single-flight guard → 1 red (its own pin); cooldown gate → 3 reds (both cooldown pins + the
   user-story pin, the documented traversal collateral); suppression-clearing → 5 reds. Tree restored
   byte-identical after each.
2. **Restorative-direction bound survived implementation** — panic and teardown facts exist and bite; the
   probe writes only suppression fields, never player/queue/generation/duck state.
3. **Endpoint-watcher row FILED** (P2, S+, Windows-only, headed/manual gate).
4. **Board row `:53` updated AND BOUNDED** — one more `§C` line discharged, three non-items recorded on the
   row (MTA one-shot idiom absent here; the 10-concurrent cap already landed as `MaxSfxVoices = 8`; the
   watcher is the new row).
5. **Four wave-27 lessons appended to `client/docs/port-lessons.md`** at land, never mid-batch.
6. **Contract-wording check: nothing owed** — neither `runtime-capability-contract.md` nor
   `async-lifecycle-fault-contract.md` quotes the old reason or describes a session-permanent disable.

## Standing land discipline (unchanged, learned the hard way)

- **Never trust the gate's own evidence (T-3, seven occurrences).** Verify the merged state yourself in a
  scratch worktree. `diff-stat.txt` is a TWO-dot diff — disprove it with three dots.
- **Verify BEFORE `spine integrate`**, not after: verifying after means unwinding a merge on the base branch.
- **Write `.spine/handoff.md` BEFORE `spine batch start`, in the authoring commit.** It cost wave 26 its
  fast-forward and it bought wave 27 one. Any orchestrator commit to base between `batch start` and integrate
  converts a free verification into a manual one.
- **Tree identity:** fast-forward → `git diff <verified> HEAD` EMPTY. Merge → the SCOPED form
  `git diff <verified> HEAD -- client/ scripts/ ConditioningControlPanel/ docs/`, naming the non-code deltas.
- **Full contract, in this order:** `dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs`.
  The wrapper is `--no-build` by design; standalone it measures the LAST build and names the wrong cause.
- **The land's LAST action verifies the tree actually being pushed.** Commit the reconciliation FIRST, then
  run the contract, then push (the wave-18 land shipped a red base by editing after its verification run).
- **Bite matrix, one source at a time.** A shared revert falsely verifies pins that were never exercised
  (SP-067) — and wave 27 showed the sibling failure: a pin whose FIXTURE made the mechanism unreachable
  passed with its own guard reverted until the bite test caught it.
- **Never set `CCP_DATA_ROOT` for a floor run** (`port-workflow.md:204`) — it skips the SP-057 pin and blinds
  the exact-count floor (the vacuous-green class SP-062 closed).
- **`allowedSkips` pins 5 names; 2 skip on Windows. THE ASYMMETRY IS CORRECT.**
- **`node .spine/patches/verify.mjs` FAILS in a scratch worktree and that is expected** (`.pi/npm` is
  per-checkout and gitignored). Run it in the MAIN checkout, and **re-run it before the NEXT `batch start`**.
- **`cmd | tail; echo $?` reports TAIL's exit code.** Use `${PIPESTATUS[0]}` or redirect to a file.
- **A doc a test READS is code — but check READ vs merely NAMED.** `port-audit-prompt.md`, `floor.json`,
  `vacuous-shape-ledger.json` are genuinely read. `task-board.md` is only an asserted error-message string.
  `port-lessons.md`, `port-digest.md` and `client/memories/**` are read by nothing (re-verified this land).
- **`spine preflight`'s "Pre-landed contract risk" warning compares against `main`**, the WPF branch with no
  `client/` tree. It did not fire for SP-070; never redirect `fileScopeMustChange` to docs.
- **Landed rows stay WIP/OPEN until the owner ratifies;** flip to DONE only with a RATIFIED citation.
- **Budget the board-row update INTO the land.** ENABLER 2 keeps `task-board.md` out of worker scope.

## Decisions on record — do not re-open

- **Owner default in force: BACK TO WPF PARITY.** The suite-hardening/parity ratio question was asked at
  waves 23 and 24, is unanswered, and **will not be re-asked**.
- **The sizing pass over Goon `:44` / FYP `:45` / Trainer Card `:51` / Haptics v2 `:52` is DEFERRED,
  MACHINE-GATED — not dropped.** Three of the four need headed/payload/Linux evidence this laptop cannot
  produce; the standing offer to write it anyway for a desktop session is in the wave-25/26/27 digests.
- **`_initLock` stays.** The race it closes (concurrent native device calls) is the process-fatal class; the
  residual is a filed row, not a reason to remove the lock.

## Instrument notes

- **Consult truncation is board row T-18.** Wave 27: both worker consults and the orchestrator's land consult
  surfaced usable verdicts under a cap. **Neither worker consult surfaced the answering model's identity** —
  recorded as returned, never guessed. **Never stitch a verdict out of reasoning**; use `mode: "solo"` (T-7).
- **Verify the advisor's checkable claims before encoding.** Done this land: `TeardownBarkPipeline`
  (`DtrhHostWindow.axaml.cs:255-262`) is called from the close handler at `:153`, so the land consult's
  UI-thread-teardown claim holds and the row it demanded is justified.

## Claimable work after this lands (the board is authority, this is a pointer)

Row `:53` keeps every other `§C`/`§D` item. Also open: the **endpoint-watcher** row and the **`_initLock`
teardown-blocking** row filed at this land, **T-18** (consult verdict truncation), the SP-069
hygienic-surface-id row, the `ProcessEnvCollection` co-location residual, the `CapabilityRegistry` probe row,
the `Assert.All` unenumerated shape, the `allowedSkips` bans-are-text row, T-17's auditor **run**, the named
privacy flake, and the standing product queue.

## Machine facts (laptop)

pi-spine 2.10.0 · hermes memory + durable fallback `client/memories/port-status.md` · **WSL zero distros →
every Linux gate is a standing named limit, never faked** · **no audio-endpoint death can be induced here —
the manual gate is named on the board row, never simulated as evidence** · **MCP not re-probed this phase**
(named limit, never a blocker) · `Z:\CCP Vids`, DISPLAY3 and the WSL2 Linux gate are **DESKTOP-only** ·
batch launched with `SPINE_WORKER_PI_TIMEOUT_MS=14400000` · 9 project + 5 engine patches verified applied
before authoring (`verify.mjs` exit 0).
