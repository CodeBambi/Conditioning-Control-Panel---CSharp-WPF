# HANDOFF — 2026-08-13 — wave 26 AUTHORED + LAUNCHED (SP-069), BATCH `20260813T124621` RUNNING

**Status: NOT PARKED. A batch is running. Base branch clean, committed and pushed at `69c426a9`.**
**Base floor, unchanged by this phase: 946 unit / 35 headless / 2 NAMED skips, build 0W/0E.**

## Which phase is yours

Reconcile first, then classify. Do **not** trust this file's phase label — check `spine status --diagnose`.

- **Batch running → port.txt case C: EXIT AT ONCE.** The shell owns the waiting. That is the expected
  next phase while `20260813T124621` executes.
- **Batch finished / `needs_integrate` → case A: LAND IT.** Obligations below.
- **No batch + claimable work → case B: author + launch one wave.**

## What is in flight

**SP-069 — companion reply hygiene** (board row `:53`, the v6.7.x `§C` line "companion effect-reply
truncation + raw-JSON leak"; upstream fix `932d829a`, 2026-08-07). Single lane. Three layers on
`AiReply.Generated.Text` at the pipeline reply seam, upstream of SP-068's link strip:

- **H1** `<think>`/`<thinking>`/`<reasoning>`/`<thought>` blocks (incl. unterminated) + orphan closer +
  `Ġ`→space, `Ċ`→newline (WPF `AiTextHygiene.Clean`).
- **H2** the five-pattern metadata-tag strip in WPF's fixed order + collapse/trim (`StripMetadataTags`).
  **The port's own trigger is `AiAwarenessService.cs:229`** — it sends the model
  `[Category: … | App: … | Title: … | Duration: …]` and strips nothing when a model mirrors it back.
- **H3** envelope-leak **DETECTION ONLY** (`LooksLikeEnvelopeLeak`) typed to the **existing**
  `AiReplyCodes.MalformedOutput`. **The port refuses where WPF salvages** — `TryLiftResponseField` is
  deliberately not ported.
- **Union moderation rule:** `EvaluateOutput` on raw **and** hygienic text, block if **either** hits.

## LAND OBLIGATIONS FOR THIS WAVE (inherit these — the land is a fresh session)

1. **Verify the union rule is actually monotone on the merged tree, not just argued.** Read the
   both-directions pins yourself (a forbidden token visible only in raw; one visible only after hygiene).
   **This is the one change in the wave that can WIDEN** if it was implemented as sanitized-only
   moderation — and a green suite would not tell you. Confirm `EvaluateOutput` is still pure.
2. **Update board row `:53` at land AND BOUND IT.** The row covers the whole `§C`/`§D` backlog and SP-069
   discharges exactly one line of it. Do not let a one-item land read as the backlog closing. **The
   truncation half of that same line is a NON-ITEM** (the port sends no token cap) and is already recorded
   that way on the row — do not re-file it as owed.
3. **Append the wave-26 lessons to `client/docs/port-lessons.md` AT LAND — not before.** That file is in
   spine `referenceDocs` (`.spine/spine-config.json:97`), so editing it mid-batch mutates a live worker's
   input. At least three are owed:
   - **When the port lacks a filter, check whether the port also PRODUCES that filter's input.**
     `AiAwarenessService.cs:229` turned a speculative parity item into a demonstrated defect.
   - **A subtractive filter upstream of a gate is the SP-068 F2 class in mirror image; the answer is a
     UNION gate, not an ordering choice.** Evaluating both texts and blocking on either is monotone by
     construction, so parity costs nothing in safety.
   - **Write `.spine/handoff.md` BEFORE `spine batch start`, in the authoring commit.** This phase wrote it
     after launching, so the base branch moves during the batch and the integrate becomes a MERGE rather
     than a fast-forward (the wave-25 tree-identity finding, paid again here avoidably).
4. **Confirm the record's honesty cell says plainly:** no truncation parity claimed; the port refuses a
   leaked envelope where WPF salvages it; `AiEnvelopeValidator` is still unwired; Linux unproven.

## Standing land discipline (unchanged, learned the hard way)

- **Never trust the gate's own evidence (T-3, seven occurrences).** Verify the merged state yourself in a
  scratch worktree. `diff-stat.txt` is a TWO-dot diff — disprove it with three dots.
- **The tree-identity proof needs the SCOPED form when the integrate is a MERGE, not a fast-forward**
  (wave-25 rule, and **this wave will hit it**, because the handoff commit moves base during the batch):
  `git diff <verified> HEAD -- client/ scripts/ ConditioningControlPanel/ docs/` → EMPTY, and **name the
  non-code deltas with their commits**.
- **Full contract, in this order:** `dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs`.
  The wrapper is `--no-build` by design; standalone it measures the LAST build and names the wrong cause.
- **The land's LAST action verifies the tree actually being pushed.** Commit the reconciliation FIRST, then
  run the contract, then push (the wave-18 land shipped a red base by editing after its verification run).
- **Bite matrix, one source at a time.** This packet requires **four** reverts (H1, H2, H3, union rule).
  A shared revert falsely verifies pins that were never exercised (SP-067).
- **Never set `CCP_DATA_ROOT` for a floor run** (`port-workflow.md:204`) — it skips the SP-057 pin and
  blinds the exact-count floor (the vacuous-green class SP-062 closed).
- **`allowedSkips` pins 5 names; 2 skip on Windows. THE ASYMMETRY IS CORRECT** — driving the skip count to
  0 regresses SP-066's honesty.
- **`node .spine/patches/verify.mjs` FAILS in a scratch worktree and that is expected** (`.pi/npm` is
  per-checkout and gitignored). Run it in the MAIN checkout.
- **`cmd | tail; echo $?` reports TAIL's exit code.** Use `${PIPESTATUS[0]}` or redirect to a file.
- **A doc a test READS is code — but check READ vs merely NAMED.** `port-audit-prompt.md`, `floor.json`,
  `vacuous-shape-ledger.json` are genuinely read. `task-board.md` is only an asserted error-message string
  in `UpstreamPayloadInventoryTests.cs` and is safe to reconcile.
- **`spine preflight`'s "Pre-landed contract risk" warning is noise here and its suggested fix is
  dangerous** — it compares against **`main`**, the WPF branch with no `client/` tree. **It fired for
  SP-069 exactly as the packet predicted. Never redirect `fileScopeMustChange` to docs.**
- **Landed rows stay WIP/OPEN until the owner ratifies;** flip to DONE only with a RATIFIED citation.
- **Budget the board-row update INTO the land.** ENABLER 2 keeps `task-board.md` out of worker scope;
  SP-001's gap recurred at SP-067 and again at SP-068.

## Decisions on record — do not re-open

- **Owner default in force: BACK TO WPF PARITY.** The suite-hardening/parity ratio question was asked at
  waves 23 and 24 and is unanswered; the default is recorded and **will not be re-asked**.
- **The sizing pass over Goon `:44` / FYP `:45` / Trainer Card `:51` / Haptics v2 `:52` is DEFERRED,
  MACHINE-GATED — not dropped.** Three of the four need headed/payload/Linux evidence this laptop cannot
  produce. A standing offer to write it anyway for a desktop session is in the wave-25 and wave-26 digests.
- **"An audit is not a decree" is intact.** SP-069's authorization is board row `:53` as queue authority.
- **H3's non-lift is a DECIDED divergence, not an omission.** Re-opening it needs owner input (the digest
  offers it as a small follow-up if they would rather see a rescued sentence than nothing).

## Instrument notes

- **Consult truncation is board row T-18.** The 150-250 word cap worked again — wave 26's decomposition
  verdict surfaced complete on the FIRST call (6th consecutive wave). **Never stitch a verdict out of
  reasoning**; an unstitched non-verdict is a MISSING consult. Use `mode: "solo"` explicitly (T-7).
- **Verify the advisor's checkable claims before encoding them.** Done this wave: `Evaluate` is a pure
  token scan (`:279-296`), and `BuildBody` sends no token cap (`:252-258`).

## Claimable work after this lands (the board is authority, this is a pointer)

Row `:53` keeps every other `§C`/`§D` item. Also open: **T-18** (consult verdict truncation), the
`ProcessEnvCollection` co-location residual, the `CapabilityRegistry` probe row, the `Assert.All`
unenumerated shape, the `allowedSkips` bans-are-text row, T-17's auditor **run**, the named privacy flake,
and the standing product queue.

## Machine facts (laptop)

pi-spine 2.10.0 · hermes memory + durable fallback `client/memories/port-status.md` · **WSL zero distros →
every Linux gate is a standing named limit, never faked** · **MCP not re-probed this phase** (named limit,
never a blocker) · `Z:\CCP Vids`, DISPLAY3 and the WSL2 Linux gate are **DESKTOP-only** · batch launched
with `SPINE_WORKER_PI_TIMEOUT_MS=14400000` · 9 local patches verified applied on both roots (`verify.mjs` OK).
