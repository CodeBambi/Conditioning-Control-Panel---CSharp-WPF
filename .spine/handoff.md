# HANDOFF — 2026-08-13 — wave 25 LANDED (SP-068, integrate `f2662cd0`), NO BATCH RUNNING

**Status: NOT PARKED. No batch is running. The board has claimable work.**
Base branch clean, verified and pushed. **Floor: 946 unit / 35 headless / 2 NAMED skips, build 0W/0E.**

## Which phase is yours

Reconcile first, then classify. Do **not** trust this file's phase label — check `spine status --diagnose`.

- **No batch + claimable work → port.txt case B: AUTHOR + LAUNCH ONE WAVE.** That is the expected next phase.
  Consult on the decomposition, write the packet, validate/analyze/plan/preflight, commit the authoring,
  `spine batch start pending` **DETACHED**, then EXIT immediately. Do not monitor.
- **Batch running → case C: EXIT AT ONCE.** The shell owns the waiting.
- **Batch finished / `needs_integrate` → case A: LAND IT.**

## What landed

**SP-068 — three SUBTRACTIVE privacy filters** over already-landed port code (board row `:46`, acting on SP-060's
audit). Floor 903 → **946** (+43 facts, each filter with a negative control). `ai-operation-contract.md`,
`AiOperationVocabulary.cs` and `allowedSkips` all **untouched**; both permanent bans still absent.

- **F1** incognito hard-drop. WPF had **two divergent 35-entry marker lists** (15 shared) → port ported the
  **union of 55, one definition**. Blank/whitespace title → typed `Unavailable` (fail-closed, because WPF's net
  behavior is a drop via a downstream `NoTitle` guard the port lacks).
- **F2** verbatim title scrub (emails, `\d{6,}`, control chars, collapse, cap 80) at packaging — **moderation
  sees the RAW field first**, the SCRUBBED value is assembled after.
- **F3** unsanctioned-link strip before memory/bubble/disk; emptied reply → `AiReply.Unavailable("reply-stripped-empty")`
  from the **existing** vocabulary, turn pair never appended.

## State the next phase inherits

- **Floor: 946 unit / 35 headless / 2 NAMED skips, 0W/0E.** The 2 skips are the Linux-gated names
  (`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`,
  `SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`). **`allowedSkips` pins 5 names; 3 execute on Windows.
  THE ASYMMETRY IS CORRECT — driving the skip count to 0 regresses SP-066's honesty.**
- **Next unused task ID: SP-069.**
- **Board row `:46` stays OPEN** — SP-068 discharged three filtered rows of its audit table and answered **none**
  of the audit's 12 owner questions. Do not close it.

## Claimable work (the board is authority, this is a pointer)

Two rows were **filed at this land** and are the freshest:
1. **T-18 — the consult route returns reasoning with no verdict (six occurrences).** Filed after eight waves of
   the orchestrator absorbing it with "ask narrowly, cap the reply". **Do not close it by writing more prose
   telling the orchestrator to cap the reply** — that IS the mitigation this row exists to replace. Size S.
2. **Nothing enforces that a test class building a real `CompositionRoot` joins `ProcessEnvCollection`.** Size S.

Also open: the `CapabilityRegistry` probe row (SP-067's filing), the `Assert.All` unenumerated shape, the
`allowedSkips` bans-are-text row, T-17's auditor **run**, the named privacy flake, and the standing product queue.

## Standing land discipline (unchanged, learned the hard way)

- **Never trust the gate's own evidence (T-3, seven occurrences).** Verify the merged state yourself in a scratch
  worktree. `diff-stat.txt` is a TWO-dot diff — disprove it with three dots.
- **NEW, from this land: the tree-identity proof needs a SCOPED form when the integrate is a MERGE, not a
  fast-forward.** `git diff <verified> HEAD` was NOT empty here — the merge combined the orch branch with two
  base-branch orchestrator doc commits made during authoring (`.spine/handoff.md`, `spine-tasks/CONTEXT.md`),
  which by construction cannot be in the orch branch. Scope the proof to the paths the contract measures
  (`git diff <verified> HEAD -- client/ scripts/ ConditioningControlPanel/ docs/` → EMPTY) and **name the
  non-code deltas with their commits**. Every merge-integrate will show this; without the scoped form it gets
  waved through the first time it fires.
- **Full contract, in this order:** `dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs`.
  The wrapper is `--no-build` by design; standalone it measures the LAST build and names the wrong cause.
- **The land's LAST action verifies the tree actually being pushed.** Commit the reconciliation FIRST, then run
  the contract, then push (the wave-18 land shipped a red base by editing after its verification run).
- **Never set `CCP_DATA_ROOT` for a floor run** (`port-workflow.md:204`) — it skips the SP-057 pin and blinds the
  exact-count floor (the vacuous-green class SP-062 closed).
- **`node .spine/patches/verify.mjs` FAILS in a scratch worktree and that is expected** (`.pi/npm` is per-checkout
  and gitignored). Run it in the MAIN checkout.
- **`cmd | tail; echo $?` reports TAIL's exit code.** Use `${PIPESTATUS[0]}` or redirect to a file.
- **A doc a test READS is code — but check READ vs merely NAMED.** `port-audit-prompt.md`, `floor.json`,
  `vacuous-shape-ledger.json` are genuinely read. `task-board.md` is only an asserted error-message string in
  `UpstreamPayloadInventoryTests.cs` and is safe to reconcile.
- **`client/docs/port-lessons.md` is in spine `referenceDocs`** (`.spine/spine-config.json:97`) — editing it while
  a batch runs **mutates a live worker's input**. Append at land, never mid-batch.
- **`spine preflight`'s "Pre-landed contract risk" warning is noise here and its suggested fix is dangerous** — it
  compares against **`main`**, the WPF branch with no `client/` tree. Never redirect `fileScopeMustChange` to docs.
- **Landed rows stay WIP/OPEN until the owner ratifies;** flip to DONE only with a RATIFIED citation.
- **Budget the board-row update INTO the land.** ENABLER 2 keeps `task-board.md` out of worker scope; SP-001's
  gap recurred at SP-067 and again at SP-068 (its three-dot diff touched zero files under `client/docs/`).

## Decisions on record — do not re-open

- **Owner default in force: BACK TO WPF PARITY.** The suite-hardening/parity ratio question was asked at waves 23
  and 24 and is unanswered; the default is recorded and **will not be re-asked**.
- **The sizing pass over Goon `:44` / FYP `:45` / Trainer Card `:51` / Haptics v2 `:52` is DEFERRED,
  MACHINE-GATED — not dropped.** Three of the four need headed/payload/Linux evidence this laptop cannot
  produce. Raised again in the wave-25 digest with a standing offer to write it anyway for a desktop session.
- **"An audit is not a decree" is intact.** SP-068's authorization was board row `:46` as queue authority, not
  SP-060's audit. **Do not extend the precedent** to any row the audit verdicted `B` or `BLOCKED-ON-OWNER`
  (A1/A4/A5/A7/A8/A9 — those need owner answers).

## Instrument notes

- **Consult truncation is now board row T-18, not folklore.** The cap (150-250 words) worked again this wave —
  both worker consults and the land consult returned complete verdicts. **Never stitch a verdict out of
  reasoning**; an unstitched non-verdict is a MISSING consult. Use `mode: "solo"` explicitly (T-7).
- **A landed audit ages into a map, not a citation.** SP-060's line cites were stale by wave 25 while every
  semantic was still present. Re-derive by symbol; record found-vs-given.

## Machine facts (laptop)

pi-spine 2.10.0 · hermes memory + durable fallback `client/memories/port-status.md` · **WSL zero distros → every
Linux gate is a standing named limit, never faked** · **MCP not re-probed this phase** (named limit, never a
blocker) · `Z:\CCP Vids`, DISPLAY3 and the WSL2 Linux gate are **DESKTOP-only** · batches launch with
`SPINE_WORKER_PI_TIMEOUT_MS=14400000`.
