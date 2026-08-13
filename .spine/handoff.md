# HANDOFF — 2026-08-13 — wave 23 LAUNCHED (SP-066 in flight), run continues

**Status: NOT PARKED. Wave 23 is IN FLIGHT.** SP-066 (board row 49 part (1) + T-17 riding) was authored, committed (`1276de71`), pushed, and launched detached. **The next phase is therefore port.txt case C (batch running → exit at once) and then case A (LAND IT)** — it is NOT another author+launch. Everything below the next two sections is the wave-22 land record, still accurate as history.

## Land checks specific to SP-066 (the landing phase has no memory of authoring)

1. **Read `client/tests/floor/floor.json` `allowedSkips` FIRST.** The packet moves the pin from `{passed, skipped}` to `{total, allowedSkips[]}`, which creates a new quarantine temptation. Two names are banned from that list and both bans are in the packet's `## Do NOT`: the **SP-057 pin** (a skip there means `CCP_DATA_ROOT` went process-wide — the vacuous `896/1` green SP-062 closed) and the named flake **`ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery`** (privacy boundary; reproduce and fix at source, never quarantine). Every listed entry must name the machine class where it *does* execute.
2. **Check the commit ORDER, not just the end state.** The schema change had to land BEFORE any `Assert.Skip` conversion; doing it the other way reddens the packet's own contract and the cheap way out is widening the pin — the exact failure this row exists to prevent, reproduced by its own fix.
3. **Expect these non-claims in `record.md` and do not let them be dropped at reconciliation:** the detector is lexical, so **runtime vacuity is NOT detected** (assertions hoisted into helpers read as absent; a loop over an empty collection reads as asserting); the guard binds only enumerated shapes; `allowedSkips` records intent nothing verifies; **T-17's induced-skip auditor RUN is not delivered** (only the `port-audit-prompt.md:12-13` edit + a prompt pin), so T-17 stays OPEN with that residual; Linux unproven.
4. **Verify the merged tree THROUGH the wrapper**, as wave 22 did: in a scratch worktree run `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs` (the wrapper is `--no-build` by design — standalone it measures the last build and names the wrong cause), 3 consecutive greens, then prove `git diff` is EMPTY between the verified tree and the integrated tip.
5. **Next unused task ID after this wave: SP-067.**

## Where the run was at the wave-22 land

- **Landed the previous phase:** SP-065 (board row 49 **part (2) only**) — integrate `09b4b639`, reconcile + push `c799d2cf`. Batch `20260813T032810` completed and archived; it was a CLEAN land (no recovery, unlike wave 21).
- **Floor is now 898 unit / 35 headless / 0 skipped, build 0W/0E**, and it is enforced by machinery rather than by a human comparing numbers: `node client/tests/floor/check-floor.mjs` owns both `dotnet test` invocations and fails the CONTRACT on an unexpected skip or an off-floor count.
- **Next unused task ID at that land: SP-066** (now consumed by SP-066; next unused is SP-067).

## Next claimable work (author ONE wave, then exit)

- **Board row 49 part (1)** — the vacuous-SHAPE enumeration sweep. Untouched, still claimable, sized M+ by its own row; slice it if it does not fit one packet.
- **Board row 50** — injected timeout BUDGETS (4th occurrence of the timing-discipline class). Its site `LoopbackOllamaProviderTests.Truncated_PrefixCut_NeverSurfaced_TypedUnavailable` is a known out-of-scope cold red; raising its 800 ms budget stays BANNED.
- **New rows filed at this land:** T-17 (the blind auditor `client/tools/port-audit-prompt.md:12-13` still runs bare `dotnet test`, so it keeps the detection path SP-065 replaced) and the named flake `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` (1 red in 15 runs; guards a privacy boundary — never weaken, never quarantine).
- The standing product queue is unchanged; nothing above outranks an owner-approved product row.

## Traps that cost time this phase (read before landing anything)

1. **The gate's `evidence/diff-stat.txt` is a TWO-DOT diff.** Base-side commits show up as worker DELETIONS. It showed `client/memories/port-status.md -1` and `spine-tasks/CONTEXT.md -8` — i.e. it looked exactly like the worker reverting the orchestrator's own land obligations. Disprove with `git diff --stat base...orch` (three dots) before reacting. 5th consecutive misleading-gate-evidence occurrence (T-3 class).
2. **`check-floor.mjs` is `--no-build` by design.** Standalone it measures the LAST BUILD, not the working tree. At this land it reported `passed 897 (pin 898)` on the pushed tip purely because the dll predated the merged test file — fails closed, but names the wrong cause. Always run the full contract: `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs`.
3. **`cmd | tail; echo $?` reports TAIL's exit code.** Use `${PIPESTATUS[0]}`. This produced a false "FLOOR_EXIT=0" at this land on a run that had actually failed.
4. **Local-patch anchors must not survive inside their own replacement** (`apply.mjs` detects applied as anchor×0 + replacement×1, so it verifies as `drifted` forever). Anchor across the insertion point; run `apply.mjs && verify.mjs` in the same step as any `manifest.json` edit.
5. **Consult surfacing:** ask narrowly and cap the reply (`under N words, numbered`). A capped solo Opus 5 call surfaced cleanly and caught two real errors this phase, where three uncapped calls last phase truncated.

## Machine facts (laptop)

pi-spine 2.10.0 pinned, 9 local patches green on both roots (`verify.mjs` OK) · hermes memory + durable fallback `client/memories/port-status.md` · WSL zero distros → **every Linux gate is a standing named limit** · MCP 0/3 connected at this phase (cached only) — a named limit, never a blocker · `Z:\CCP Vids`, DISPLAY3, and the WSL2 Linux gate are DESKTOP-only.

**Per-checkout gotcha (new):** the `skill-floor-wrapper-testcommand` patch is `engine:false`, so it lives in the per-checkout `.pi/npm` tree. On any other machine or a fresh clone, run `node .spine/patches/apply.mjs && node .spine/patches/verify.mjs` before authoring, or the packet template will silently lack the wrapper mandate and `FloorWrapperGuardTests` will redden the lane instead.
