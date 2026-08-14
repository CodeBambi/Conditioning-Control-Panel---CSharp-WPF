# Conditioning-Control-Panel — Constitution

**Last Updated:** 2026-08-14

Standing orders for every lane worker and reviewer on the greenfield Avalonia port. Task packets override nothing here; they narrow it.

**Engine amendment, 2026-08-14.** pi-spine and the `consult` extension are retired. Execution moved to Claude Code: lanes are `port-slice-executor` subagents in git worktrees, review is `port-plan-reviewer` / `port-code-reviewer` / `port-final-reviewer`, and advice is `port-advisor` with `port-advisor-critic` as the adversarial seat, all defined under `.claude/agents/`. pi-spine could not be retargeted: every lane worker and reviewer it spawns is the literal string `pi`, with no config, env, or plugin seam to substitute another CLI. `.pi/`, `.spine/`, and the 74 existing `spine-tasks/SP-*/` packets remain on disk as history and are read-only. Everything below this line that does not name an engine is unchanged and still binds.

**Named limit created by that amendment, recorded rather than hidden:** every advisor and reviewer seat is now an Anthropic model. The cross-vendor council that used to disagree with itself is gone, so agreement between seats is much weaker evidence than it was and blind spots are correlated. Weight the mechanical checks (the floor wrapper, a 0W/0E build, the scoped diff, tree identity at land) above any verdict.

---

## Mission

Build a new Avalonia v12 desktop client for Conditioning Control Panel under repository-root `client/`, targeting Windows and Linux only. Port user-observable purpose and outcomes — never old mechanics.

---

## Authority order (descending)

1. Owner decisions in `client/docs/architecture.md` and `client/docs/capability-inventory.md`.
2. `client/docs/task-board.md` — the only live product queue. Packets execute board rows; they do not replace the board.
3. Repository instructions (`CLAUDE.md`, `.claude/CLAUDE.md` if present).
4. Relevant project skills under `.claude/skills/`: `port-plan`, `port-feature`, `wpf-parity`, `avalonia-research`, `dashboard-design`, `overlay-clickthrough`, `unified-compositor-engine`, `first-attempt` lessons docs.
5. The task packet (`PROMPT.md`).
6. Advisor subagents (`port-advisor`, `port-advisor-critic`), MCP, and model suggestions.

Empirical evidence can prove a document stale; fix the smallest authoritative document, then continue. Lower sources never override higher ones.

## Hard rules

- **Read-only zones:** `ConditioningControlPanel/` (legacy WPF = behavioral evidence; first Avalonia attempt `CCP.*` = lessons/failure evidence only — never import its classes, interfaces, timers, DI topology, or status claims). Never modify `.pi/`, `.spine/`, the existing `spine-tasks/SP-*/` packets, or `CLAUDE.md`. The retired engine's trees stay as history and evidence.
- **Write zones:** new product code, tests, assets, build scripts under `client/` only. Durable port docs under `client/docs/` when the packet allows it. Task state under the task's own `spine-tasks/SP-*/` folder.
- **Avalonia v12 facts** come only from current official sources via the `avalonia-research` skill. No guessed APIs, no v11 assumptions, no MCP-generated authority.
- **WPF parity** claims need narrow behavioral evidence via `wpf-parity`. Port the outcome, not the class.
- **Windows AND Linux** (X11/Wayland distinguished where behavior differs) or a documented product blocker. Compilation, a stub, a no-op fallback, or a Windows-only test never proves cross-platform support.
- **Privacy/security:** never broaden webcam, biometric, secret, path, logging, capture, moderation, consent, or network boundaries. Never send secrets, user media, camera data, or sensitive logs to advisors or MCPs.
- **Verification is task-specific and honest.** A failed check is never accepted to keep a wave moving. Headed/visual gates that automation cannot prove leave the row `WIP`/`BLOCKED` with the exact manual gate named.
- **Advisory gates:** call `port-advisor` at the checkpoints the packet names (pre-approach, pre-completion), and add `port-advisor-critic` for architecture, dependencies, platform seams, privacy, security, and decomposition. An advisor cannot mark work done; the owner and the board can. A required gate left unasked is a failed gate, not a silent pass, and agreement between two Anthropic seats is weak evidence: prefer one claim you check yourself.
- **Review levels** 0-3 are declared per packet and drive `port-plan-reviewer` (plan), `port-code-reviewer` (diff), and `port-final-reviewer` (completion, PASS / REVISE / REPLAN). A lane never adjudicates its own completion, and a reviewer never edits the lane.
- **Board reconciliation:** the matching `client/docs/task-board.md` row must carry concise evidence or the exact blocker before work counts as done. In **serial** waves the lane edits the board before reporting complete; in **parallel** waves (board not in File Scope) the lane records evidence in its own packet folder and the orchestrator reconciles the board at land time — never two lanes editing the board in one wave. Packet state never substitutes for the board.
- **No new wall-clock waits in tests.** Every test wait is a deterministic signal or the shared bounded-window helper with its loud classifier; hard-coded deadline literals, `Thread.Sleep`, `DateTime`/`Environment.TickCount64` polls, and bare `Task.Delay` waits outside the approved helper fail the timing guard. *(SP-059 draft, applied by the orchestrator at land 2026-08-12 — policy-touching text is never applied by the worker. Known remainder: injected timeout **budgets** passed into product code under test are not yet swept or guarded — see the board's fourth-occurrence row.)*
- **One task, one commit slice.** No unrelated files, no scope creep, no TODO placeholders.
