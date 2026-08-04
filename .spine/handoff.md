# HANDOFF — 2026-08-04 ~16:45 local — wave 4 LANDED, run continues

**Status: NOT PARKED.** Wave 4 (SP-035 + SP-036) landed `8efd60b4`; floor now 492/492 + 29/29. All state pushed.

## Landed today (2026-08-04, laptop resume session)
- SP-037 (manifest-drift floor repair, substitution-norm land under the anthropic-400 outage) — `7e2fd5b8`.
- The 400 outage ROOT-CAUSED + FIXED: pi request-shape defect (missing `x-anthropic-billing-header` system[0], hermes-agent #48176) — local patch `__PI_BILLING_HEADER_FIX__` on the nested pi-ai (re-check after pi upgrades; npm wipes it).
- WAVE 4: SP-035 (LoopbackOllamaProvider — first real AI provider; LAB 26/26 Windows; WSL named limit) + SP-036 (bounded MCP admission; Sentry empirically LIVE = de-facto option 3, owner question open; avalonia-live PROVISIONAL with binding CCP_MCP=1 condition). Integrate `8efd60b4`; engine reviews all green on the fixed route.

## Next claimable work
- **SP-038 = AI companion c3** (moderation boundary per `client/docs/ai-companion-admission.md` §8 — read §8 c3 BEFORE authoring).
- Lane partner TBD: evaluate T-14 (worktree-setup hook for lane re-patching — small tooling) vs product rows.
- Next unused task ID: SP-038 → 039 after authoring c3.

## Owner questions open
1. Sentry mitigation intent (option 3 de-facto vs patch-and-rebuild / hosts-block) — SP-036 record §5/§11.
2. WSL distro provisioning on the laptop (ALL Linux gates currently named limits).
3. AI admission §9.2 ledger ×7 (moderation policy VALUES — c3 needs the placeholder discipline).
4. Dashboard-priority question (2026-07-22).

## Machine facts (laptop, durable — full list in the previous handoff revision + memories/incidents)
- `core.hidedotfiles=false` GLOBAL set; `pi.exe` shim installed; pi-spine pinned 2.10.0 both files + patches green; `__PI_BILLING_HEADER_FIX__` applied; WSL zero distros; Ollama present (probe-only); hermes memory; no Z:/DISPLAY3.
- Traps: bpx-consult two configs (project governs); consult() needs explicit mode:"solo"; fresh lanes need apply.mjs (T-14).
