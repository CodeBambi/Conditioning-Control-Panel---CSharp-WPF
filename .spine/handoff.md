# HANDOFF — 2026-08-04 ~18:50 local — waves 4+5 LANDED, run continues

**Status: NOT PARKED.** Wave 4 (SP-035+SP-036, `8efd60b4`) and wave 5 (SP-038+SP-039, `f4eea79e`) landed same day; floor now 516/516 + 29/29. All state pushed.

## Landed 2026-08-04 (one day: floor repair + outage fix + 2 waves)
- SP-037 (manifest-drift, substitution land during the 400 outage) — `7e2fd5b8`.
- **`__PI_BILLING_HEADER_FIX__`** — the 400 "extra usage" outage was a pi request-shape defect (missing `x-anthropic-billing-header` system[0], hermes-agent #48176). Patch on the nested pi-ai; `pi -p` 200 on opus-5 AND fable-5. **npm reinstall/upgrade WIPES it — re-check the marker after pi updates.**
- WAVE 4: SP-035 (LoopbackOllamaProvider, LAB 26/26 Windows) + SP-036 (bounded MCP admission; Sentry empirically LIVE = de-facto option 3, owner question open; avalonia-live PROVISIONAL w/ binding CCP_MCP=1 condition).
- WAVE 5: SP-038 (moderation boundary — coverage-honesty inventory + tripwire; escalation interactive-only, session-scoped; zero values invented) + SP-039 (T-14 hook: lane pre-staging of the patched .pi/npm; named gate armed — next wave zero mid-task verify reds).

## Next claimable work
- **SP-040 = AI companion c4** (memory per admission §8/§4 — §4 rule 5 binds moderation-gated persist; c3 kept escalation state serializable for additive persistence) + lane partner TBD (T-15 lab-hardening is a candidate).
- Next unused task ID: SP-040.

## Owner questions open
1. Sentry mitigation intent (option 3 de-facto vs patch-and-rebuild / hosts-block) — SP-036 record §5/§11.
2. WSL distro provisioning (ALL Linux gates named limits; c2 LAB + every U-both-platforms acceptance half-open).
3. AI admission §9.2 ledger ×7 (moderation policy VALUES — c3 shipped typed placeholders).
4. Dashboard-priority question (2026-07-22).
5. SP-039 consult-provenance anomaly (worker self-reported "GPT-5" — T-7 class, flagged).

## Machine facts (laptop, durable — details in memories/incidents)
- core.hidedotfiles=false GLOBAL; pi.exe shim; pi-spine 2.10.0 pinned both files + patches green; __PI_BILLING_HEADER_FIX__; T-14 hook active (copies main's PATCHED .pi/npm into fresh lanes — keep main patched, standing verify rule); WSL zero distros; Ollama present (probe-only); hermes memory; no Z:/DISPLAY3.
- Traps: bpx-consult two configs (project governs); consult() needs explicit mode:"solo"; zombie test hosts = progressive-flake class (kill first); gate-history edits need an immediate structure audit (3 slips today).
