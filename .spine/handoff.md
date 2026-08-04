# HANDOFF — 2026-08-04 ~19:50 local — waves 4+5+6 LANDED, run continues

**Status: NOT PARKED.** Three waves landed today: wave 4 (`8efd60b4`), wave 5 (`f4eea79e`), wave 6 (`6255a643`). Floor 537/537 + 29/29. All state pushed.

## Landed 2026-08-04 (one day: floor repair + outage fix + 3 waves + T-14 gate)
- SP-037 (manifest-drift) → __PI_BILLING_HEADER_FIX__ (pi request-shape fix for the 400 outage; re-check after pi upgrades) → SP-035/036 (provider + MCP admission) → SP-038/039 (moderation boundary + T-14 hook) → SP-040/041 (memory + lab harness).
- **T-14 CLOSED** (named gate discharged: lanes arrive pre-patched; keep the MAIN checkout patched — the hook copies it).
- T-15 WIP (lab hardened: fresh-instance-per-bind + leak self-check; 5 consecutive greens). T-16 OPEN (DTRH cap-timer flake class).

## Next claimable work
- **SP-042 = AI companion c5** (awareness per admission §8/§5: consent code-enforced, context packaging through c3's boundary, cooldown mechanism placeholder values, keyword-routing as owned ops) + lane partner TBD (T-16 candidate).
- Next unused task ID: SP-042.

## Owner decrees (2026-08-04, encoded in operating-rules.md)
Improve-freely (no 1:1 copy anywhere; observable-outcome parity; improvements a must) · use all resources actively ALWAYS (MCP seats within SP-036 rules) · hermes caps 10000 · avalonia-live verified (27 tools; laptop headed-evidence substitute — use for UI evidence on this machine).

## Owner questions open
Sentry mitigation intent (SP-036) · WSL distro provisioning (ALL Linux gates named limits) · AI §9.2 ledger ×7 (moderation VALUES, consent/retention defaults — c4 shipped placeholders) · dashboard-priority · SP-039 GPT-5 self-report anomaly (T-7 class).

## Machine facts (laptop, durable)
core.hidedotfiles=false GLOBAL · pi.exe shim · pi-spine 2.10.0 pinned both files + patches green · __PI_BILLING_HEADER_FIX__ · T-14 hook active (copies main's PATCHED .pi/npm) · WSL zero distros · Ollama present (probe-only) · hermes memory (caps raised, restart-effective) · no Z:/DISPLAY3 (avalonia-live substitutes for UI evidence) · traps: bpx-consult two configs (project governs), explicit mode:"solo", zombie test hosts = progressive-flake class, capture lane hook logs BEFORE any clean, structure audit after EVERY board edit.
