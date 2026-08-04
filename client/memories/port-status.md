# Port Status (as of 2026-08-04, second export — laptop resume session)

Branch: `feat/crossplatform` @ SP-037 land (`7e2fd5b8`) + reconcile commit. Pushed.

## New since the first export

- **Resume reconciliation executed (laptop):** waves 1–3 verified; the desktop's parked wave-4 batch `20260722T152755` lane commits NEVER travelled (desktop-local) — wave 4 is a FRESH execution of the packets, not a resume.
- **SP-037 authored + LANDED** (v6.6.3 manifest-drift repair, floor-repair precondition): empirical sweep +7/−1 (board hypothesis said +4 — sweep vindicated), copied-count 1538→1544, floor restored **466/466 + 29/29**; substitution-norm land (engine review chain absent — see park below); row WIP, owner ratifies. Next unused ID = **SP-038**.
- **WAVE 4 LANDED 2026-08-04 (integrate `8efd60b4`; floor now 492/492 + 29/29):** SP-035 (LoopbackOllamaProvider — first real provider; native api/chat; retry default OFF + 5-min WPF-observed timeout via consult corrections; LAB 26/26 Windows; WSL zero-distros named limit) + SP-036 (bounded MCP admission; avalonia-live PROVISIONAL with binding `CCP_MCP=1` condition; Sentry empirically LIVE = de-facto option 3, owner question OPEN; redact-BEFORE-calling binding run-wide). First production wave on the billing-header fix — all engine review spawns green. T-14 filed (lane re-patch recurrence). **Next: SP-038 = AI c3 (moderation boundary, read admission §8 c3 first) + lane partner TBD.**
- **WAVE 5 LANDED 2026-08-04 (integrate `f4eea79e`; floor now 516/516 + 29/29):** SP-038 (c3 moderation boundary — coverage-honesty inventory + tripwire, escalation interactive-only session-scoped, ZERO policy values invented) + SP-039 (T-14 hook — lanes now pre-staged with the main checkout's PATCHED .pi/npm at creation; named gate armed: next wave zero mid-task verify reds). T-15 filed (c2 lab harness hardening — zombie test-host flake class root-caused at T-3). **Next: SP-040 = AI c4 (memory; §4 rule 5 binds moderation-gated persist) + lane partner TBD.**
- **WAVE 4 PARKED (pause protocol, both-routes-failed branch):** anthropic fresh-subprocess route DOWN account-wide (`400 "extra usage"` — engine reviewer spawn + manual `pi -p` probes, opus-5 AND fable-5; in-session consult UNAFFECTED). SP-035+SP-036 stay pending + 2026-08-04-amended (Ollama present on laptop; WSL zero-distros named limit; consult rewire; SP-036 three-seat subject). Owner action: restore spawn capacity (claude.ai/settings/usage) or explicitly accept reduced assurance. Full state: `.spine/handoff.md`. **PARK LIFTED ~15:30 same day: request-shape defect (missing billing-header system[0], hermes #48176), fixed by `__PI_BILLING_HEADER_FIX__` — see incidents file.**
- **Engine restored on laptop:** global pi-spine re-pinned 2.12.2 → admitted 2.10.0 (BOTH settings.json + npm package.json exact), 12 patches applied, verify.mjs green.
- **Laptop bootstrap fixes (durable, in incidents file):** `git config --global core.hidedotfiles false` (hidden-.git EPERM class) + `pi.exe` shim (Node 24 cannot spawn the cmd/shell shims).

## Pause state

~~Parked 2026-08-04 ~13:10 UTC~~ **PARK LIFTED same day ~15:30 local** — the anthropic-400 was a pi request-shape defect (missing `x-anthropic-billing-header` system[0], hermes-agent #48176), fixed by local patch `__PI_BILLING_HEADER_FIX__` on the nested pi-ai (`pi -p` 200 on opus-5 AND fable-5). Wave 4 launches. Re-check the patch after any pi upgrade (npm wipes it).

## Board honesty rule

Landed rows stay WIP until the owner ratifies them. 18 rows were flipped WIP→DONE only with RATIFIED decree citation placed in evidence cells. Never flip without it.

## Landed

- **Run closed out 2026-07-21** — SP-001…SP-020 all landed (19 product/tooling rows,
  ALL WIP pending owner ratification). T-1 CLOSED (durable spine patch mechanism
  delivered by SP-020 and proven on the real tree via post-land reinstall gate).
- **SP-023** (2026-07-21) — DTRH host slice b1, FIRST product-implementation slice.
  FIRST GATE PROVEN: invokeCSharpAction page→host works on NativeWebDialog (WSLg
  transcript) — admission's named risk retired. Host shell: Windows embedded WebView2 12.0.1.
- **SP-024** (2026-07-21) — DTRH host b2: slots/picker/protocol v1. Three save slots =
  4 PersistenceStore<T> instances on SP-005 machinery (index + 3 slots, each its OWN
  named AsyncOperationOwner).
- **SP-025** (2026-07-21) — DTRH host b3: SFX/freeze/tint/video. Backends LIVE-FEED
  admitted (SoundFlow 1.4.1 nupkg-verified + LibVLCSharp 3.10.0 /
  VideoLAN.LibVLC.Windows 3.0.23.1; Linux distro libvlc 3.0.23-1).
  SoundFlow deadlock lesson + rect-persistence binding recorded.
- **SP-026** (2026-07-22) — DTRH host b4: progression/payout/Loom/media. Progression
  rides b2 slot documents — schema stays v1 additive-only, NO parallel meta file.
  Floor 366/29.
- **SP-027** (2026-07-22) — DTRH host b5, FINAL slice. **The b1–b5 slice cut is
  COMPLETE; the DTRH host row is fully sliced and stays WIP with consolidated named
  limits.** Watchdog/exit/injection + ESC forensics delivered.
- **SP-028** (2026-07-22) — T-5 local anchor-patch (parallelism enabler 1).
  `t5-reviews-autoclean` manifest patch: delete .reviews/ inside commitLaneWorktree
  AFTER verdict recording. T-5 CLOSED-by-patch; base install patched.
- **WAVE 1** (2026-07-22, first 2-lane batch, orch tip bff8f037) — SP-029 quips
  arbitration q1 (Audio/SoundArbitration.cs — SP-017 channel ownership verbatim,
  refcounted ducking with panic release-all) + SP-030 admission.
  T-5 post-land gate FAILED (row reopened).
- **WAVE 2** (2026-07-22, integrate 6e1b2f81) — **FIRST AUTO-GATE LAND in project
  history** (engine ran its own merges + opened its own gate).
  SP-031 (T-5 anchor re-base): **the SP-028 premise was FALSIFIED with 3 independent
  proofs** — two-root truth. SP-032: quips q2.
- **WAVE 3** (2026-07-22, integrate 2f77c934) — second consecutive auto-gate land.
  SP-033: AI companion c1 — AiOperationPipeline (SP-004 owned ops); provider seam
  switch = generation invalidation + cancel + stale-drop. SP-034: probe
  (Review-Level authoring defect recorded honestly). **T-5 gate DISCHARGED**
  (full-chain proof on SP-033). T-13 stall-detector DONE.

## Staged / next (wave 4)

- **SP-035** — AI companion slice c2: loopback Ollama provider.
- **SP-036** — audit and admit bounded Avalonia MCP use (A-01...).

## Pause state

2026-07-22 ~16:30 UTC the owner invoked the pause protocol: "Consult fable 5 has
hit limit, pause all work and prepare save spot." Work resumed 2026-08-04 (this
session: git repair + push + this memory export).

## Board honesty rule

Landed rows stay WIP until the owner ratifies them. 18 rows were flipped WIP→DONE
only with RATIFIED decree citation placed in evidence cells. Never flip without it.

## Wave 6 (2026-08-04, integrate 6255a643; floor now 537/537 + 29/29)

- **SP-040 (c4 memory):** AiMemoryStore on SP-005 machinery (own owner; null-on-disk retention discipline; consent placeholder Denied; append-NEVER strengthening; explicit-clear with 3 consult hardenings; named non-claim: persists+clears, context consumption = c7). Row WIP — c5 = awareness next.
- **SP-041 (T-15 lab harness):** ctor ODE race root-caused w/ deterministic repro; fresh-instance-per-bind; leak self-check (static registry + assembly fixture); 5 consecutive greens; zero assertion changes. Row WIP — owner ratifies.
- **T-14 NAMED GATE DISCHARGED → row CLOSED:** hook fired all lanes; lane-1's first red-free contract in 6 packets. Fresh lanes now arrive pre-patched (keep MAIN checkout patched — the hook copies whatever main carries).
- **T-16 filed** (DTRH cap-timer flake class). Next: SP-042 = AI c5 (awareness) + partner TBD.
- **Owner decrees encoded (2026-08-04):** improve-freely mandate (no 1:1 copy anywhere; observable-outcome parity only; improvements a must); use all resources actively ALWAYS (MCP seats within SP-036 rules); hermes caps 5000→10000 (config, restart-effective); avalonia-live verified end-to-end (27 tools; laptop headed-evidence substitute for UI work).
