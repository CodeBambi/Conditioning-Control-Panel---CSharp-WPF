# HANDOFF — 2026-08-13 — wave 22 LANDED, run continues

**Status: NOT PARKED. No in-flight batch, no gate pending, no worker running.** Wave 22 landed and is pushed (`c799d2cf`). The next phase of `client/tools/port-loop.ps1` is an AUTHOR + LAUNCH phase (port.txt case B), not a land.

Supersedes the 2026-08-04 handoff (waves 4-7). That revision's machine facts are still broadly accurate; its "next claimable work" is long done.

## Where the run actually is

- **Landed this phase:** SP-065 (board row 49 **part (2) only**) — integrate `09b4b639`, reconcile + push `c799d2cf`. Batch `20260813T032810` completed and archived; it was a CLEAN land (no recovery, unlike wave 21).
- **Floor is now 898 unit / 35 headless / 0 skipped, build 0W/0E**, and it is enforced by machinery rather than by a human comparing numbers: `node client/tests/floor/check-floor.mjs` owns both `dotnet test` invocations and fails the CONTRACT on an unexpected skip or an off-floor count.
- **Next unused task ID: SP-066.**

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
