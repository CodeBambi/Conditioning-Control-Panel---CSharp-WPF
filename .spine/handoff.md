# HANDOFF — 2026-08-14 — wave 27 AUTHORED + LAUNCHED (SP-070), nothing landed

**Status: BATCH LAUNCHED.** This file is written in the authoring commit, **before** `spine batch start`
(the wave-26 lesson: writing it after launch moves the base branch mid-batch and turns the integrate into a
merge instead of a fast-forward). Wave 26 (SP-069) is landed, integrated at `6feb11e4`, archived.
**Base floor at launch: 996 unit / 35 headless / 2 NAMED skips, build 0W/0E.**

## Which phase is yours

Reconcile first, then classify. Do **not** trust this file's phase label — check `spine status --diagnose`.

- **Batch running → port.txt case C: EXIT AT ONCE.** The shell owns the waiting.
- **Batch finished / `needs_integrate` (`macroPhase: "gating"`) → case A: LAND IT.** The wave-27 obligations
  below are yours. A finished batch is not a landed one — wave 26's sat at the gate ~6.5 h.
- **No batch + claimable work → case B.** SP-070's own row work would then be either landed or abandoned;
  reconcile from git and the board before authoring anything new.

## What is in flight (SP-070 — the audio session-disable is permanent and must not be)

Board row `:53`, the v6.7.x `§C` audio line (upstream fix `d33b5d8d`, 2026-08-03, `#778`/`#779`). Single lane.

`SoundArbitration.Initialize` sets `_audioDisabledForSession = true` on zero endpoints (`:214`) or a failed
`TryInit` (`:236`); `ReadyLocked` (`:601`) then refuses **every** play on **every** channel for the rest of
the process; and the **only** product caller of `Initialize` runs once, during DTRH host-window construction
(`DtrhHostWindow.axaml.cs:213-220`). WPF fixed exactly this: *"`_waveOutPermanentlyUnavailable` is no longer
permanent."* **The defect is PERMANENCE, not disabling.**

Delivered shape: a consecutive-failure counter that success resets, a cooldown from the already-injected
`ISoundClock`, and a **single-flight** lazy re-probe reusing `Initialize` — triggered only by a play attempt
whose cooldown has elapsed. No timer thread, no background service, no wall clock, no new seam. One product
file (`client/src/CcpClient.Desktop/Audio/SoundArbitration.cs`).

**Three halves of that same upstream commit are NON-ITEMS** and are recorded on board row `:53` already so
they are never re-filed as owed: the MTA one-shot worker thread (that NAudio idiom does not exist in this
port), the 10-concurrent cap (already landed as `MaxSfxVoices = 8` drop-on-overflow), and the
`IMMNotificationClient` endpoint watcher (**its own row, owed at this land**).

## LAND OBLIGATIONS FOR THIS WAVE

1. **Verify the healthy-session negative control yourself, on the merged tree.** The one way this wave goes
   wrong invisibly is by turning a lazy re-probe into a background re-probe loop, and a green suite would not
   tell you. Read three pins directly: one init call and no extra device calls when nothing fails;
   single-flight (N concurrent play attempts → exactly one init call); no busy loop (exactly one attempt per
   cooldown window). **This is the wave-26 union-rule check in its wave-27 form.**
2. **Confirm the direction argument survived implementation.** SP-068 and SP-069 were subtractive; this one
   is **restorative**, so "every change narrows" is the wrong test and a worker may have over-applied it. The
   bound is: a recovery may only restore what a healthy endpoint would already have permitted, and may never
   override teardown, panic, or an explicit stop. Check the panic and teardown facts exist and bite.
3. **File the `IMMNotificationClient` endpoint-watcher row** (Windows-only native; re-arms recovery the
   instant a device returns instead of waiting for the next play attempt). Named as a non-item *for this
   packet* only — it becomes phantom debt unless it is filed at the land.
4. **Update board row `:53` and BOUND it** — one more `§C` line discharged, not the backlog closing. The
   three non-items are already on the row; keep them there.
5. **Append the wave-27 lessons to `client/docs/port-lessons.md` AT LAND — not before.** That file is in
   spine `referenceDocs` (`.spine/spine-config.json:97`), so editing it mid-batch mutates a live worker's input.
6. **Check whether the worker recorded a needed contract wording** for an expiring-disable state
   (`runtime-capability-contract.md` / `async-lifecycle-fault-contract.md` were read-only for it). Policy text
   lands via the orchestrator (SP-059 precedent).

## Standing land discipline (unchanged, learned the hard way)

- **Never trust the gate's own evidence (T-3, seven occurrences).** Verify the merged state yourself in a
  scratch worktree. `diff-stat.txt` is a TWO-dot diff — disprove it with three dots.
- **Verify BEFORE `spine integrate`**, not after: verifying after means unwinding a merge on the base branch.
- **Tree identity:** fast-forward → `git diff <verified> HEAD` EMPTY. Merge → use the SCOPED form
  `git diff <verified> HEAD -- client/ scripts/ ConditioningControlPanel/ docs/` and name the non-code deltas
  with their commits. **This wave should be a fast-forward** — the handoff was written before launch, so base
  does not move during the batch. If it is a merge anyway, find out what moved base.
- **Full contract, in this order:** `dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs`.
  The wrapper is `--no-build` by design; standalone it measures the LAST build and names the wrong cause.
- **The land's LAST action verifies the tree actually being pushed.** Commit the reconciliation FIRST, then
  run the contract, then push (the wave-18 land shipped a red base by editing after its verification run).
- **Bite matrix, one source at a time.** This packet requires **three** reverts (suppression clear,
  single-flight guard, cooldown gate). A shared revert falsely verifies pins that were never exercised (SP-067).
- **Never set `CCP_DATA_ROOT` for a floor run** (`port-workflow.md:204`) — it skips the SP-057 pin and blinds
  the exact-count floor (the vacuous-green class SP-062 closed).
- **`allowedSkips` pins 5 names; 2 skip on Windows. THE ASYMMETRY IS CORRECT.**
- **`node .spine/patches/verify.mjs` FAILS in a scratch worktree and that is expected** (`.pi/npm` is
  per-checkout and gitignored). Run it in the MAIN checkout. **Re-run it before the NEXT `batch start`** —
  it was run this phase (9 project + 5 engine patches applied, exit 0).
- **`cmd | tail; echo $?` reports TAIL's exit code.** Use `${PIPESTATUS[0]}` or redirect to a file.
- **A doc a test READS is code — but check READ vs merely NAMED.** `port-audit-prompt.md`, `floor.json`,
  `vacuous-shape-ledger.json` are genuinely read. `task-board.md` is only an asserted error-message string
  in `UpstreamPayloadInventoryTests.cs` and is safe to reconcile.
- **`spine preflight`'s "Pre-landed contract risk" warning compares against `main`**, the WPF branch with no
  `client/` tree. It did **not** fire for SP-070; if it appears later, never redirect `fileScopeMustChange`
  to docs.
- **Landed rows stay WIP/OPEN until the owner ratifies;** flip to DONE only with a RATIFIED citation.
- **Budget the board-row update INTO the land.** ENABLER 2 keeps `task-board.md` out of worker scope;
  SP-001's gap recurred at SP-067 and SP-068.

## Decisions on record — do not re-open

- **Owner default in force: BACK TO WPF PARITY.** The suite-hardening/parity ratio question was asked at
  waves 23 and 24, is unanswered, and **will not be re-asked**.
- **The sizing pass over Goon `:44` / FYP `:45` / Trainer Card `:51` / Haptics v2 `:52` is DEFERRED,
  MACHINE-GATED — not dropped.** Three of the four need headed/payload/Linux evidence this laptop cannot
  produce.
- **The endpoint watcher's absence is a DECIDED bound for SP-070**, not an oversight: it is Windows-only
  native with no headless proof here, and the lazy re-probe delivers the user-visible outcome without it.

## Instrument notes

- **Consult truncation is board row T-18.** Wave 27's decomposition verdict surfaced **complete on the first
  call** under a 200-word cap — 7th consecutive wave. **Never stitch a verdict out of reasoning**; an
  unstitched non-verdict is a MISSING consult. Use `mode: "solo"` explicitly (T-7).
- **Verify the advisor's checkable claims before encoding.** Done this phase: `Initialize` does not hold
  `_gate` across the backend calls (so the deadlock half of the hazard is already mitigated and the packet's
  job is to preserve it); `PanicReset` neither sets nor reads `_audioDisabledForSession` (so panic is *not*
  the hazard — the blocking half is); `ISoundClock` already carries `UtcNow` + `Schedule` and the test
  `ManualClock` fires due callbacks on `Advance`.

## Claimable work after this lands (the board is authority, this is a pointer)

Row `:53` keeps every other `§C`/`§D` item. Also open: the endpoint-watcher row this land must file, **T-18**
(consult verdict truncation), the SP-069 hygienic-surface-id row, the `ProcessEnvCollection` co-location
residual, the `CapabilityRegistry` probe row, the `Assert.All` unenumerated shape, the `allowedSkips`
bans-are-text row, T-17's auditor **run**, the named privacy flake, and the standing product queue.

## Machine facts (laptop)

pi-spine 2.10.0 · hermes memory + durable fallback `client/memories/port-status.md` · **WSL zero distros →
every Linux gate is a standing named limit, never faked** · **no audio-endpoint death can be induced here —
the manual gate is named in the packet's honesty cell, never simulated as evidence** · **MCP not re-probed
this phase** (named limit, never a blocker) · `Z:\CCP Vids`, DISPLAY3 and the WSL2 Linux gate are
**DESKTOP-only** · batch launched with `SPINE_WORKER_PI_TIMEOUT_MS=14400000` · 9 project + 5 engine patches
verified applied before authoring (`verify.mjs` exit 0).
