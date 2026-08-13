# HANDOFF — 2026-08-13 — wave 24 LANDED (SP-067), spine IDLE, run continues

**Status: NOT PARKED. No batch is running. Board row 98 is landed.** The next phase is therefore
**port.txt case B: author + launch ONE wave**, not a land. Spine is idle, the batch is archived, and
`feat/crossplatform` carries the reconciliation.

## State the next phase inherits

- **Floor: 903 unit / 35 headless / 2 NAMED skips, build 0W/0E.** Verified by the orchestrator on
  the exact pushed tree. Land evidence committed at
  `spine-tasks/SP-067-stopasync-completion-race/evidence/land-*.txt`.
- **THE 2 SKIPS ARE CORRECT — DO NOT "FIX" THEM.** `SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`
  and `ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps` are Linux-gated and
  pinned by name. `allowedSkips` carries **5** names — 3 execute on Windows, 2 are Linux-gated; the
  asymmetry is expected. Driving the skip count to 0 regresses the honesty SP-066 landed.
- **Next unused task ID: SP-068.**

## The decision already made — do not re-open it

**Owner default in force: BACK TO WPF PARITY.** The ratio question (suite-hardening vs parity) was
put to the owner at wave 23 and again at wave 24 and is unanswered. Wave 24 fixed the queue's only
*product* defect; the four remaining suite-hardening rows (`Assert.All` shape, mechanical
`allowedSkips` ban test, T-17 auditor run, named privacy flake) **ride along with parity work rather
than owning a wave**. This is recorded in the digest and in CONTEXT. **Do not ask the owner again**
and do not author another suite-hardening wave without new evidence that one has become urgent.

## Claimable parity work (the authoring phase picks, after its own reconcile + consult)

- **v6.7 upstream surfaces, all P1 OPEN:** Goon Game 1v1 duel host (`:44`), For You Feed desktop host
  + ghost mode (`:45`), Her Room + Awareness reconcile (`:46`), Trainer Card redesign (`:51`),
  Haptics v2 (`:52`), and the itemized v6.7.x backlog (`:53`, `client/docs/upstream-sync.md` §C/§D).
- **CONTEXT.md already carries a constraint on four of these:** *"Goon / FYP / Trainer Card / Haptics
  v2 stay undecomposed until a sizing pass follows"* (wave-16 consult, CONTEXT `:249`). **A sizing
  pass is the natural next authoring step** — do that before decomposing any of them into a packet.
- **Row `:46` (Her Room/Awareness) is NOT a product packet yet** — prior consult recorded that
  adopting upstream's companion redesign over the landed c1–c7 is an **OWNER decree**, so authoring
  product work there now would invent scope. Zero-product-code archaeology only (SP-050 precedent).
- P0 OPEN spikes (`:56` video geometry, `:72`–`:74` camera provenance/acquisition/ONNX gaze) exist,
  but `:72`–`:74` are camera + privacy and want owner framing before a packet.
- The row filed at this land — **`CapabilityRegistry.cs:103` swallowed-cancellation** (P2, S) — is a
  good ride-along candidate: same defect family as SP-067, small, and its acceptance already demands
  a pin proven to bite.

## Land checks that will apply to whatever lands next

1. **Bite-test every new regression pin at its own source, one at a time.** SP-067's land proved this
   is not ceremony: reverting only the Heartbeat fix left the other two pins green, so a single
   shared revert would have "verified" two pins that were never exercised. Five green suite runs
   bound nothing about whether a new pin *can* fail.
2. **Full contract, in this order:** `dotnet build client/CcpClient.sln -c Debug --nologo && node
   client/tests/floor/check-floor.mjs`. The wrapper is `--no-build` by design — standalone it
   measures the LAST BUILD and names the wrong cause.
3. **Never set `CCP_DATA_ROOT` for a floor run** (port-workflow.md:204). It skips the SP-057 pin and
   blinds the exact-count floor — the vacuous-green class SP-062 closed.
4. **`node .spine/patches/verify.mjs` FAILS in a scratch worktree and that is expected** — `.pi/npm`
   is a per-checkout gitignored tree absent there. Run it in the MAIN checkout (exit 0, 5 engine
   patches + 4 project patches at 2.10.0).
5. **`cmd | tail; echo $?` reports TAIL's exit code.** Use `${PIPESTATUS[0]}` or redirect to a file.
6. **A doc a test READS is code — but check READ vs merely NAMED.** `port-audit-prompt.md`,
   `floor.json` and `vacuous-shape-ledger.json` are genuinely read by tests and must not be touched
   in post-verification reconciliation. `task-board.md` is *mentioned* in
   `UpstreamPayloadInventoryTests.cs` only as an asserted **error-message string** — it is safe to
   reconcile. Grep finds both classes; read the test before deciding.
7. **Gate evidence is still untrustworthy (T-3, six occurrences).** Verify the merged state yourself
   in a scratch worktree and prove `git diff` is EMPTY between the tree you verified and the
   integrated tip. `evidence/diff-stat.txt` is a TWO-DOT diff — disprove it with three dots.
8. **The land's LAST action verifies the tree actually being pushed.** Commit the reconciliation
   FIRST, then run the full contract, then push. The wave-18 land shipped a red base by editing
   after its verification run.
9. **Workers do not reliably update their board row** (SP-001's gap, recurred at SP-067 — its
   three-dot diff touched zero files under `client/docs/`). Either put the board row in
   `fileScopeMustChange` when authoring, or budget the update into the land.
10. **`spine preflight`'s "Pre-landed contract risk" warning is a PERMANENT false positive here and
    its suggested fix is actively dangerous** — it compares `fileScopeMustChange` against **`main`**,
    the WPF branch with no `client/` tree at all. Following its hint would manufacture the
    contract-passes-on-docs-only class (SP-214/SP-457). Recorded in port-lessons.
11. Landed rows stay **WIP** until the owner ratifies; flip to DONE only with a RATIFIED citation.

## Machine facts (laptop)

pi-spine 2.10.0, patches verify OK in the main checkout · hermes memory + durable fallback
`client/memories/port-status.md` · **WSL zero distros → every Linux gate is a standing named limit**
· MCP not re-probed this phase — named limit, never a blocker · `Z:\CCP Vids`, DISPLAY3 and the WSL2
Linux gate are DESKTOP-only.
