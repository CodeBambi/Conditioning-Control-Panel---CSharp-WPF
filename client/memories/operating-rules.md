# Operating Rules (owner-established, cross-session)

## Consult routes — REWIRED 2026-08-04 (v2: Opus-main)

Model access is limited to **Opus 5, Fable 5, Kimi K3** (uva/sol/luna and zai/glm
routes are gone — zai failed with provider balance error 1113). Config:
`.pi/bpx-consult.json` (project-level, committed).

**Owner hierarchy (2026-08-04)**: anthropic Opus 5 > Fable 5 > Sonnet 5; kimi K3 >
highspeed. Opus 5 ≈ Fable 5 in quality (small gap) but Fable 5 is the **costliest
per 1M tokens** — Fable is spent only where its reasoning/knowledge edge pays.
Opus 5 is better at code and agentic workflows; Fable 5 at reasoning/knowledge.

- **solo/advisor**: `anthropic/claude-opus-5` (high) — MAIN route.
- **fallback solo**: `anthropic/claude-fable-5` (when Opus errors/caps).
- **gut-check**: `kimi-coding/kimi-for-coding-highspeed` (low, terse) — the cheap
  fast model, deliberately the dumb one.
- **council**: architect+tester+performance `anthropic/claude-opus-5` (code/agentic
  seats), critic `anthropic/claude-fable-5` (single reasoning/knowledge seat —
  cost-bounded), simplifier+security `kimi-api/k3`;
  **synthesizer `kimi-api/k3` (max)** — owner-specified.
- **debate**: advocate=architect (opus-5), critic=critic (fable-5).
- **spine reviewer**: repinned fable-5 → `anthropic/claude-opus-5`
  (`.spine/spine-config.json`, plan+code review — code strength, lower cost).
- History: before the 2026-08-04 rewires, solo Fable 5 was the ONLY working route;
  Sol fallback dead; council probes #4/#5 failed on uva/* seats. **Owner lifted ALL
  gates** (recorded 2026-07-21) — gates no longer block, but the pause protocol
  below still applies to consult-route failures.

## Pause protocol

If the solo Fable 5 consult route fails (rate limit / error): **pause all work and
prepare a save spot.** Owner invoked this 2026-07-22 ~16:30 UTC. Save-spot = commit
everything, handoff state durable, board/STATUS honest.

## Compaction fallback

If smart compaction fails or has issues: switch to the **Luna or Sol** model to
handle compaction, then return to the primary model (kimi k3 primary for the
continuous-run sessions).

## Owner preferences

- **Improve-freely mandate (2026-08-04, chat — "remember this" + extensions):** the client does NOT copy WPF 1:1 ANYWHERE — UI visuals, function implementations, behavior implementations are ALL open design space; "you have full control"; **any improvement that can be made is a MUST.** The remaining constraint = user-observable outcome (what the app does; behavior-visible formulas/clamps/timings/ordering keep WPF parity). Better-than-WPF behavior changes go through evidence-cited board rows (retrospective rule), never silent redesign mid-port. WPF evidence = behavior contracts, not visual templates. dashboard-design's "preserve the five-theme grammar" posture becomes "evolve it". First applies to c7/dashboard packets.
- **Use all resources actively, ALWAYS (2026-08-04, chat):** the Avalonia MCP seats (admitted subset per SP-036) + agents/workflows are standard instruments for orchestrator AND spine workers — docs lookups during avalonia-research passes, ValidateXaml on AXAML drafts, avalonia-live inspection for UI evidence. Within SP-036 binding rules (advisory-only; redact-BEFORE-calling; ValidateXaml PASS ≠ API-validity proof; usage recorded accept/reject + reasons in the using packet's record).
- **Hermes memory caps raised 5000→10000 (owner-authorized 2026-08-04):** `~/.pi/agent/hermes-memory-config.json` (`memoryCharLimit`/`userCharLimit`/`projectCharLimit`) — applies at session restart; re-apply if a reinstall wipes it.
- **Test media**: `Z:\CCP Vids` (desktop-only path) — real video/image/gif files.
  Use these for port work needing real media (video playback, image flash, GIF
  animation) instead of synthetic-only fixtures.
- **Headed evidence targets DISPLAY3** (desktop monitor). Never trust stale
  screenshots/board claims — verify against reality.
- **Parallelism plan (approved 2026-07-21)**: DTRH host chain stays SERIAL through
  b5 (done), then 2-lane waves starting with the quips/sound + AI companion era
  (waves 1–3 executed this way).

## Evidence & verification conventions

- Spine wave cycle per `client/docs/port-session-prompt.md`; land/recovery playbook
  is law.
- Solo Fable 5 consult before gate approval (when gates were active); record
  consult verdicts in the packet record.
- Every Avalonia MCP call is advisory-only and must be recorded accept/reject +
  reasons in the using packet's record.md. MCP never substitutes
  docs/compilation/K3 pixels/headed gates.
- **avalonia-live VERIFIED END-TO-END 2026-08-04 (laptop):** 27 tools enumerated live (control query, semantic/visual/logical trees, window/control/set-of-marks screenshots, synthetic input, binding errors, wait-for) + `describe_screen` on the real dashboard (screenshot + semantic tree + bounds). **Laptop substitute for DISPLAY3-class headed evidence on UI work** — admission refined per SP-036 §7 (first live enumeration): the 27 tools = inspection/drive evidence tools, admitted-advisory for evidence work; env-gate binding condition stands (`CCP_MCP=1`).
- dotnet is on the evidence allowlist (local patch post-SP-002).
- WSL2 gates run from the desktop WSL install (Linux head evidence).

## AppData cleanup convention (2026-07-22)

~72 GB freed on desktop (C: 41 GB → 113 GB free). Safe caches cleaned: VS/MSBuild
staging, RDP auto-traces, June hang dumps (CCP logs\hang_20260616_*.dmp 5.9 GB),
NVIDIA DXCache. **WSL gate-tree debris convention established** — clean spine
worktrees/gate trees per that convention, not ad-hoc.
- **bpx-consult configs aligned 2026-08-05:** BOTH the project (`.pi/bpx-consult.json`) and the global (`~/.pi/agent/bpx-consult.json`) now say solo = `anthropic/claude-opus-5` (the 2026-08-04 rewire updated only the project file; aligned at the wave-12 reconcile). Project shadows global in this repo. Worker/orchestrator records dated before 2026-08-05 may cite fable-5 as "the configured solo route" — correct-as-of-then.
