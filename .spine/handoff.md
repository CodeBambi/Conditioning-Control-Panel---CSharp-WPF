# HANDOFF — 2026-08-13 — wave 25 AUTHORED + LAUNCHED (SP-068), batch RUNNING

**Status: NOT PARKED. A batch IS running — `20260813T102701`, launched 2026-08-13 ~10:27.**
Base branch is clean and pushed at `d8f65fec`.

## Which phase is yours

Reconcile first, then classify. Do **not** trust this file's phase label — check `spine status --diagnose`.

- **Batch still running → port.txt case C: EXIT AT ONCE.** The shell owns the waiting. Do not monitor, do not
  spawn a watcher, do not start a second engine.
- **Batch finished / `needs_integrate` → case A: LAND IT** (evidence review → verify the merged state yourself
  → gate approve → integrate → batch complete → reconcile → commit → push → digest).
- **Batch failed / `needs_retry` → diagnose per `spine-orchestrate-waves`;** prefer `retry`/`resume` over a new
  batch. Never hand-edit `.spine/batch-state.json`.

## What is in flight

**SP-068 — three SUBTRACTIVE privacy filters over already-landed port code** (board row `:46`, acting on
SP-060's landed divergence audit). This is the first parity work under the owner's back-to-parity default.

- **F1** incognito hard-drop (audit row A6, **ADOPT**) — private/incognito titles dropped before anything counts them.
- **F2** title scrub (A10, **ADOPT**) — emails, `\d{6,}`, control chars stripped; whitespace collapsed; cap **80**. WPF values verbatim.
- **F3** unsanctioned-link strip on companion prose (C3, **MERGE — strip half only**).

**Every change NARROWS.** Nothing new is observed, persisted, logged, or transmitted. That is the packet's
hard boundary and its whole justification.

## State the next phase inherits

- **Floor at launch: 903 unit / 35 headless / 2 NAMED skips, build 0W/0E.** This packet ADDS facts; the worker
  bumps `floor.json` `total` in the same commit as the tests that move it.
- **THE 2 SKIPS ARE CORRECT — DO NOT "FIX" THEM.** `allowedSkips` pins **5** fully-qualified names; 3 execute on
  Windows, 2 are Linux-gated. The asymmetry is expected. Driving the skip count to 0 regresses SP-066's honesty.
- **Next unused task ID: SP-069.**

## Land checks that will apply to THIS packet specifically

1. **THREE bite tests, one source at a time — this is the packet's core acceptance.** Revert F1 alone → only F1's
   pins go red; then F2; then F3. **A shared revert is not acceptable evidence** (SP-067's land proved it falsely
   verifies pins that were never exercised). Check the worker actually did three separate reverts.
2. **Check the boundary clearance table, not just the greens.** Every filter must show observed / retained /
   transmitted / logged as **less or equal**. Any "more" is a defect, however small.
3. **`client/docs/ai-operation-contract.md` must be UNEDITED.** The packet forbids the worker touching it; if a
   filter needs contract wording, the worker names it in `record.md` and **the orchestrator applies it at land**
   (SP-059 precedent — policy text never lands via a worker).
4. **F3 sits inside the memory-persist path** (`AiOperationPipeline.cs:344`). Confirm the only persistence change
   is *less text*, and that no new `AiReply` case was invented (that would be a contract change).
5. **Do not let the honesty items dissolve at land:** F1/F2 harden a path **only tests drive today** (sole product
   wiring is `AiModerationBoundary.cs:110`); F3's path **is** live. Deferred halves must be named, not omitted:
   sigil unwrap, C3's title rewrite, the 120-char projection cap, and the three headed rows A11/D6/D11.
6. **Budget the board-row update INTO the land.** ENABLER 2 keeps `task-board.md` out of worker scope, and
   SP-001's gap recurred at SP-067 (its three-dot diff touched zero files under `client/docs/`).

## LAND OBLIGATIONS FOR THIS WAVE (post-launch consult — you have no memory of them otherwise)

1. **File the consult-truncation tooling row.** Six occurrences, still no board row. The recurrence rule (same
   lesson twice → file a bounded tooling task) is being broken by the orchestrator itself, and "cap the reply" is
   a **procedural mitigation** — the class that already failed at SP-052 and SP-057. File it; do not fix it in the land.
2. **Append the wave-25 lessons to `client/docs/port-lessons.md` AT LAND, never mid-batch.** That file is in
   spine `referenceDocs` (`.spine/spine-config.json:97`, verified), so editing it while a batch runs **mutates a
   live worker's input**. Two entries owed: (a) the consult-truncation class + the reply-cap workaround;
   (b) **a landed audit ages into a map, not a citation** — re-derive by symbol, record found-vs-given.
3. **Update board row `:46` at land, and bound the claim.** It still reads OPEN with no in-flight marker. SP-068
   discharges three filtered rows of its audit table; it does **not** answer the audit's 12 owner questions, so
   the row stays OPEN for them. ENABLER 2 makes this the orchestrator's edit — SP-001's gap already recurred at
   SP-067, so it is named here to avoid a third miss.

## Standing land discipline (unchanged, learned the hard way)

- **Never trust the gate's own evidence (T-3, six occurrences).** Verify the merged state yourself in a scratch
  worktree and prove `git diff` is EMPTY between the tree you verified and the integrated tip. `diff-stat.txt` is
  a TWO-dot diff — disprove it with three dots.
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
- **`spine preflight`'s "Pre-landed contract risk" warning is noise here and its suggested fix is dangerous** — it
  compares against **`main`**, the WPF branch with no `client/` tree. It passed before this wave's authoring
  commit and fired right after it. Never redirect `fileScopeMustChange` to docs.
- **Landed rows stay WIP until the owner ratifies;** flip to DONE only with a RATIFIED citation.

## Decisions on record — do not re-open

- **Owner default in force: BACK TO WPF PARITY.** The suite-hardening/parity ratio question was asked at waves 23
  and 24 and is unanswered; the default is recorded and **will not be re-asked**. The four remaining
  suite-hardening rows ride along with parity work rather than owning a wave.
- **The sizing pass over Goon `:44` / FYP `:45` / Trainer Card `:51` / Haptics v2 `:52` is DEFERRED, MACHINE-GATED
  — not dropped.** The wave-16 constraint still stands; three of the four need headed/payload/Linux evidence this
  laptop cannot produce, so a plan authored here would sit unexecuted. Raised in the wave-25 digest with an
  explicit offer to write it anyway if the owner wants it ready for a desktop session.
- **"An audit is not a decree" is intact.** SP-068's authorization is board row `:46` as queue authority, not
  SP-060's audit; narrowing a boundary is not adopting upstream's Her Room redesign. Do not extend this precedent
  to any row the audit verdicted `B` or `BLOCKED-ON-OWNER` (A1/A4/A5/A7/A8/A9 — those need owner answers).

## Instrument notes

- **Consult truncation, 6th occurrence** (waves 17, 21, 22, 23, and this one). The decomposition call returned
  **reasoning with no verdict**; a narrow re-ask **capped at 150 words** surfaced cleanly. The cap is the working
  fix, 4th consecutive wave. **Never stitch a verdict out of reasoning.** Use `mode: "solo"` explicitly (T-7).
- **A landed audit ages into a map, not a citation.** SP-060's line cites are already stale
  (`AwarenessObserverPolicy.cs:319-327` is `ResolveOwnProcessName`, `:277-279` is a `catch`). The semantics were
  all still present. Re-derive by symbol; record found-vs-given.

## Machine facts (laptop)

pi-spine 2.10.0, patches verify OK in the main checkout · hermes memory + durable fallback
`client/memories/port-status.md` · **WSL zero distros → every Linux gate is a standing named limit, never faked**
· **MCP 0/3 connected** (`avalonia-docs`/`avalonia-live` cached, `avalonia-ui` not connected) — named limit, never
a blocker; SP-068 touches no AXAML so the A-013 step is not a gate · `Z:\CCP Vids`, DISPLAY3 and the WSL2 Linux
gate are **DESKTOP-only** · batch launched with `SPINE_WORKER_PI_TIMEOUT_MS=14400000`.
