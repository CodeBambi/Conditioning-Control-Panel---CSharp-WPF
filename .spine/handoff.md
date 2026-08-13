# HANDOFF — 2026-08-13 — wave 23 LANDED (SP-066), spine IDLE, run continues

**Status: NOT PARKED. No batch is running. Board row 49 is fully landed.** The next phase is
therefore **port.txt case B: author + launch ONE wave**, not a land. Spine is `Idle`, the tree is
clean, and `feat/crossplatform` is pushed and in sync at `851596cf`.

## State the next phase inherits

- **Floor: 900 unit / 35 headless / 2 NAMED skips, build 0W/0E.** Verified by the orchestrator on
  the exact pushed tree: cold fresh-worktree first-ever build 0W/0E + **4 greens through the
  wrapper** (3 on `fdbb0f82`, 1 final on `851596cf` after the evidence commit, so the last
  verification is of the tree actually pushed). Evidence committed at
  `spine-tasks/SP-066-vacuous-shape-sweep/evidence/land-*.txt`.
- **THE 2 SKIPS ARE CORRECT — DO NOT "FIX" THEM.** `SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`
  and `ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps` are Linux-gated and
  pinned by name in `client/tests/floor/floor.json`. Before SP-066 they early-`return`ed and were
  counted as PASSES, so the old "898/0 skipped" floor was scoring vacuity as green. A packet that
  drives the skip count back to 0 is regressing the honesty, not improving the floor.
- **Next unused task ID: SP-067.**

## Land checks that will apply to whatever lands next

1. **`node .spine/patches/verify.mjs` FAILS in a scratch worktree and that is expected** — `.pi/npm`
   is a per-checkout gitignored tree that does not exist there, so the project-root patches are
   uncheckable. All 5 ENGINE patches still verify. Run `verify.mjs` in the MAIN checkout (exit 0
   there), and run build+floor in the scratch worktree. Captured at
   `evidence/land-verify-scratch-npm-absent.txt` so nobody re-discovers it as a scare.
2. **Full contract, in this order:** `dotnet build client/CcpClient.sln -c Debug --nologo && node
   client/tests/floor/check-floor.mjs`. The wrapper is `--no-build` by design — standalone it
   measures the LAST BUILD and names the wrong cause.
3. **Never set `CCP_DATA_ROOT` for a floor run** (port-workflow.md:204). It skips the SP-057 pin and
   blinds the exact-count floor — the vacuous-green class SP-062 closed.
4. **`cmd | tail; echo $?` reports TAIL's exit code.** Use `${PIPESTATUS[0]}` or redirect to a file
   and echo `$?` on the next line.
5. **A doc a test READS is code.** `client/tools/port-audit-prompt.md` is now asserted on by
   `FloorWrapperGuardTests`; `client/tests/floor/floor.json` and `vacuous-shape-ledger.json` are
   asserted on by the floor wrapper and `VacuousShapeGuardTests`. None of these may be edited during
   post-verification reconciliation — that is the wave-18 red-base class in a docs disguise.
6. **Gate evidence is still untrustworthy (T-3, six occurrences).** `evidence/diff-stat.txt` is a
   TWO-DOT diff, so base-side commits appear as worker deletions. Disprove with three dots
   (`git diff --stat base...orch`) before reacting.

## Claimable work (author ONE wave, then EXIT — do not monitor)

**Author the AsyncLifecycle StopAsync race (the first row below) unless the owner has said
otherwise.** That is a recorded recommendation, not an open question: it is the only item in the
current claimable set that is a defect in the PRODUCT's shutdown path rather than scaffolding around
the tests, it has surfaced twice, and it is currently queued behind test tooling — the wrong order.
The digest states this to the owner as a recommendation with an intent to proceed. The question
genuinely left open for them is the RATIO after that (how much of the run goes to suite
trustworthiness vs WPF parity features); if `client/docs/port-digest.md` carries an owner answer,
that answer wins over this note.

- **P1 `AsyncLifecycleTests.Heartbeat_SkipsProjectionUntilBound_ThenFlowsThroughBoundary`** — the
  StopAsync completion race. **Second recorded occurrence** (SP-055, then SP-066 run 0); both times
  under a diff touching no lifecycle code, so it is a real product race, not test noise. It did NOT
  fire in the land's 4 runs — that bounds nothing. Reproduce with a bounded loop, name the mechanism,
  fix at the source. Never weaken the assertion, never allowlist it. Size S.
- **P2 `Assert.All` / expression-lambda silencing shape** — 21 uses, found during the SP-066 sweep and
  deliberately left out of scope. Either extend the detector surface and re-sweep, or pin
  `Assert.NotEmpty`. Size S+.
- **P2 (S) the two `allowedSkips` permanent bans are prose** — ~10 lines of test asserting `floor.json`
  `allowedSkips` contains neither the SP-057 pin nor the `ChaosTunnelLoopbackTests` privacy flake.
  Do NOT widen it into a general admission-rule validator.
- **T-17 (P2, OPEN on its residual)** — the prompt edit and its mechanical pin landed; the induced-skip
  auditor RUN (blind auditor against a broken tree must FAIL, against a clean tree must PASS) is
  undelivered. The row closes on that run.
- **Named flake `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery`** — 1 red in
  15 runs, 0 in SP-066's 5 and 0 in the land's 4. Privacy boundary: never weaken, never quarantine.
- **Board row 50** — injected timeout BUDGETS, 4th occurrence of the timing-discipline class. Its site
  `LoopbackOllamaProviderTests.Truncated_PrefixCut_NeverSurfaced_TypedUnavailable` is a known
  out-of-scope cold red; raising its 800 ms budget stays BANNED.
- The standing product queue is unchanged; nothing above outranks an owner-approved product row.

## Techniques that are now reliable, not lucky

- **Cap the consult reply and ask narrowly** (`under N words, numbered`, verdict first). Third
  consecutive wave with clean surfacing on solo Opus 5 where uncapped calls truncated. This land's
  capped call caught the port-audit-prompt.md read-by-tests trap and contributed a board row.
- **Verify the merged state yourself and make the LAST verification the tree you push.** This land ran
  a 4th confirming green after the evidence commit rather than pushing on the strength of the earlier
  three.
- **Check commit ORDER, not just end state**, when a packet's own contract depends on sequencing.

## Machine facts (laptop)

pi-spine 2.10.0 pinned, 9 local patches — `verify.mjs` OK in the main checkout · hermes memory +
durable fallback `client/memories/port-status.md` (ninth export current) · WSL zero distros →
**every Linux gate is a standing named limit** · MCP not re-probed this phase — treat as a named
limit, never a blocker · `Z:\CCP Vids`, DISPLAY3, and the WSL2 Linux gate are DESKTOP-only.

**Per-checkout gotcha:** the `skill-floor-wrapper-testcommand` patch is `engine:false`, so it lives in
the per-checkout `.pi/npm` tree. On any other machine or a fresh clone, run
`node .spine/patches/apply.mjs && node .spine/patches/verify.mjs` before authoring, or the packet
template will silently lack the wrapper mandate and `FloorWrapperGuardTests` will redden the lane.
