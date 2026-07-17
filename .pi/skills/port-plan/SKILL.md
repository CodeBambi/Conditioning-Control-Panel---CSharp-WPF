---
name: port-plan
description: "Plan and sequence non-trivial work for the greenfield Windows/Linux Avalonia client under client/. Use when the user says plan, what next, continue the port, task-auto, milestone, slice, seam, parallelize, or asks where code should live. Reads the client docs first, treats WPF as behavioral evidence and the first Avalonia attempt as lessons only, and produces bounded task-board work with consultation and verification gates."
---

# port-plan

Plan the greenfield client. Do not continue the old `CCP.Avalonia` migration by inertia.

## Authority order

1. `client/docs/architecture.md` for owner-approved decisions.
2. `client/docs/capability-inventory.md` for observable behavior and acceptance.
3. `client/docs/task-board.md`, the only live greenfield queue.
4. `client/docs/first-attempt-lessons.md` for `ACCEPT`/`ADAPT`/`REJECT` lessons only.
5. `client/docs/port-workflow.md` for `/task`, `/task-auto`, model routing, consult gates, commits, and stop conditions.
6. `client/docs/port-session-prompt.md` for fresh-session and crash-recovery reconciliation protocol, never live status.

Repository instructions remain mandatory. Generated specs and council verdicts are lower authority.

## Scope boundary

- Product code belongs under `client/`; targets are Windows and Linux only.
- WPF under `ConditioningControlPanel/` is read-only behavioral evidence unless explicitly in scope.
- `ConditioningControlPanel/CCP.*` is the first Avalonia attempt: inspect only for a documented lesson or narrow experiment. Never inherit its topology, packages, services, interfaces, timers, fixed layer values, or completion claims by default.
- Do not approve topology, package, platform seam, baseline, or build command before evidence supports it.

## Planning sequence

1. Check `git status --short`, recent relevant commits, current worktree, `.pi-tasks/`, active workflows/processes, and recovery loops; never absorb unrelated work or assume a new chat means a new run.
2. Reconcile execution journals with git and `client/docs/task-board.md`. Resume a valid `/task-auto` or `/task` before creating replacement work; classify orphaned WIP explicitly.
3. Select one approved unblocked row from `client/docs/task-board.md`, or propose one only when no valid work is resumable.
4. Link the relevant capability and owner decisions.
5. Use `wpf-parity` for observable behavior with narrow citations and focused git-history archaeology for non-trivial affected paths.
6. Use `avalonia-research` for every Avalonia v12 API, package, rendering, input, windowing, or platform question.
7. Audit dependencies and platform-specific code before implementation, following the official porting methodology: WPF-only packages/controls, `[DllImport]`, browser/media/camera/audio dependencies, and Windows assumptions become explicit spikes or seams.
8. Define Windows and Linux acceptance separately; distinguish X11/Wayland when relevant. A no-op is not support.
9. List dependencies, blockers, product decisions, security/privacy constraints, and exclusions.
10. Choose a small vertical slice that reaches the screen/behavior early and is independently verifiable: one reviewable diff and one commit.
11. Define automated and headed evidence before implementation.
12. Run the consultation checkpoint in `port-workflow.md`: council for architecture/dependency/platform/security/rendering/input; debate when two concrete approaches remain.

Use the 2026 official migration guide/cheat sheet as the technical starting point. Use the 2024 expert-guide methodology selectively: preparation, dependency audit, incremental slices, frequent small commits, and target-platform testing are accepted; its literal code-commenting migration recipe and "keep existing structure" rule do not override the greenfield architecture.

Never encode live task state, current package versions, active agent names, branch names, or "next work" into a durable starting prompt. Starting prompts define reconciliation protocol; current state is discovered from disk and live registries. Start a new run with `/task-auto @client/docs/port-session-prompt.md`. After interruption, use `/task-auto-resume`; invoking `/task-auto` again creates a competing journal rather than resuming.

If the Pi Avalonia MCP is available, plan a bounded advisory use only where it adds evidence: small AXAML validation, accessibility/layout questions, or heuristic performance review. Never make it a required blocker, generator, or primary source; its upstream content targets Avalonia 11.3.1 and requires the telemetry/version safeguards in `avalonia-research`.

## Code placement

Until topology is approved, state responsibilities rather than inventing projects: portable rules/persistence, shared Avalonia UI/rendering, Windows implementation, Linux implementation, and tests/harnesses. A new abstraction needs a real platform boundary or multiple real implementations. Prefer framework/standard-library features over wrappers and dependencies.

## Task orchestration

- `/task`: one bounded row or spike.
- `/task-auto`: use the restart-safe program in `port-session-prompt.md` or an owner-reviewed milestone naming exact rows, order, blockers, exclusions, consultation, and verification. Never use an unconstrained “implement the port” prompt.
- `/task-auto-resume`: first choice when a valid AUTO journal exists after restart; do not launch a competing AUTO run.
- `/task-resume <id>`: resume a valid unfinished child task when no AUTO run owns it.
- `.pi-tasks/` is local recovery state; durable status belongs in `client/docs/task-board.md`.
- Follow workflow model tiers and fallback procedure in `client/docs/port-workflow.md`.

## Parallel work

Parallelize only disjoint files and independently verifiable slices. Use separate worktrees. Assign shared project files, registration, schemas, localization, shell, and tracker rows to one orchestrator.

## Required plan output

For each slice: outcome; linked decision/contract; WPF evidence; focused history findings; relevant feature/systemic first-attempt lesson; allowed/forbidden areas; Windows acceptance; Linux acceptance/blocker; security/privacy/performance constraints; v12 research; optional bounded Avalonia MCP review and redaction; consultation question; automated VERIFY; end-to-end wiring proof; headed gates; tracker update; dependencies; stop conditions.

## Stop instead of guessing

Stop when sources conflict, behavior is ambiguous, a package/endpoint is needed, Linux equivalence is unknown, privacy/safety broadens, parallel work touches a chokepoint, or verification cannot prove the promise.

## Related skills

- `wpf-parity`, `avalonia-research`, `port-feature`, `mechanical-port-work`, `port-audit`.
