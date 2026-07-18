# Conditioning-Control-Panel — Constitution

**Last Updated:** 2026-07-18

Standing orders for every spine worker and reviewer on the greenfield Avalonia port. Agents load this via `referenceDocs` in `.spine/spine-config.json`. Task packets override nothing here; they narrow it.

---

## Mission

Build a new Avalonia v12 desktop client for Conditioning Control Panel under repository-root `client/`, targeting Windows and Linux only. Port user-observable purpose and outcomes — never old mechanics.

---

## Authority order (descending)

1. Owner decisions in `client/docs/architecture.md` and `client/docs/capability-inventory.md`.
2. `client/docs/task-board.md` — the only live product queue. Spine packets execute board rows; they do not replace the board.
3. Repository instructions (`AGENTS.md`, `CLAUDE.md`, `.pi/CLAUDE.md`).
4. Relevant Pi skills: `port-plan`, `port-feature`, `wpf-parity`, `avalonia-research`, `dashboard-design`, `overlay-clickthrough`, `unified-compositor-engine`, `first-attempt` lessons docs.
5. The task packet (`PROMPT.md`).
6. Advisor (`consult`), MCP, and model suggestions.

Empirical evidence can prove a document stale; fix the smallest authoritative document, then continue. Lower sources never override higher ones.

## Hard rules

- **Read-only zones:** `ConditioningControlPanel/` (legacy WPF = behavioral evidence; first Avalonia attempt `CCP.*` = lessons/failure evidence only — never import its classes, interfaces, timers, DI topology, or status claims). Never modify `.spine/`, `AGENTS.md`, `CLAUDE.md`.
- **Write zones:** new product code, tests, assets, build scripts under `client/` only. Durable port docs under `client/docs/` when the packet allows it. Task state under the task's own `spine-tasks/SP-*/` folder.
- **Avalonia v12 facts** come only from current official sources via the `avalonia-research` skill. No guessed APIs, no v11 assumptions, no MCP-generated authority.
- **WPF parity** claims need narrow behavioral evidence via `wpf-parity`. Port the outcome, not the class.
- **Windows AND Linux** (X11/Wayland distinguished where behavior differs) or a documented product blocker. Compilation, a stub, a no-op fallback, or a Windows-only test never proves cross-platform support.
- **Privacy/security:** never broaden webcam, biometric, secret, path, logging, capture, moderation, consent, or network boundaries. Never send secrets, user media, camera data, or sensitive logs to advisors or MCPs.
- **Verification is task-specific and honest.** A failed check is never accepted to keep a batch moving. Headed/visual gates that automation cannot prove leave the row `WIP`/`BLOCKED` with the exact manual gate named.
- **Consult gates:** use the `consult` tool at the checkpoints the packet names (pre-approach, pre-completion). `consult` cannot mark work done; the owner and the board can.
- **Board reconciliation:** the matching `client/docs/task-board.md` row must carry concise evidence or the exact blocker before work counts as done. In **serial** waves the worker edits the board before `.DONE`; in **parallel** waves (board not in File Scope) the worker records evidence in its own `spine-tasks/SP-*/` folder and the orchestrator reconciles the board at land time — never two lanes editing the board in one wave. `spine-tasks/` state never substitutes for the board.
- **One task, one commit slice.** No unrelated files, no scope creep, no TODO placeholders.
