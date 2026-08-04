# Port Status (as of 2026-08-04)

Branch: `feat/crossplatform` (now pushed; see incidents file for the 2026-08-04 force-push).

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
