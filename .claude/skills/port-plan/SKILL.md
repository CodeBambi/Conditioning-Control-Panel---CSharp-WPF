---
name: port-plan
description: "Plan and sequence non-trivial work for the greenfield Windows/Linux Avalonia client under client/. Use when the user says plan, what next, continue the port, author a wave, milestone, slice, seam, parallelize, or asks where code should live. Reads the client docs first, treats WPF as behavioral evidence and the first Avalonia attempt as lessons only, and produces bounded task-board work with advisory and verification gates."
---

# port-plan

Plan the greenfield client. Do not continue the old `CCP.Avalonia` migration by inertia.

## Authority order

1. `client/docs/architecture.md` for owner-approved decisions.
2. `client/docs/capability-inventory.md` for observable behavior and acceptance.
3. `client/docs/task-board.md`, the only live greenfield queue.
4. `client/docs/first-attempt-lessons.md` for `ACCEPT`/`ADAPT`/`REJECT` lessons only.
5. `client/docs/port-workflow.md` for wave execution, advisory gates, commits, and stop conditions.
6. `client/docs/port-session-prompt.md` for fresh-session and crash-recovery reconciliation protocol, never live status.

Repository instructions remain mandatory. Generated specs and advisor verdicts are lower authority.

## Scope boundary

- Product code belongs under `client/`; targets are Windows and Linux only.
- WPF under `ConditioningControlPanel/` is read-only behavioral evidence unless explicitly in scope.
- `ConditioningControlPanel/CCP.*` is the first Avalonia attempt: inspect only for a documented lesson or narrow experiment. Never inherit its topology, packages, services, interfaces, timers, fixed layer values, or completion claims by default.
- Do not approve topology, package, platform seam, baseline, or build command before evidence supports it.

## Planning sequence

1. Check `git status --short`, recent relevant commits, `git worktree list`, any live wave lock, and active processes; never absorb unrelated work or assume a new chat means a new run.
2. Reconcile lane branches and packet records with git and `client/docs/task-board.md`. Finish or explicitly abandon an in-flight wave before authoring replacement work; classify orphaned WIP explicitly.
3. Select one approved unblocked row from `client/docs/task-board.md`, or propose one only when no valid work is resumable.
4. Link the relevant capability and owner decisions.
5. Use `wpf-parity` for observable behavior with narrow citations and focused git-history archaeology for non-trivial affected paths.
6. Use `avalonia-research` for every Avalonia v12 API, package, rendering, input, windowing, or platform question.
7. Audit dependencies and platform-specific code before implementation, following the official porting methodology: WPF-only packages/controls, `[DllImport]`, browser/media/camera/audio dependencies, and Windows assumptions become explicit spikes or seams.
8. Define Windows and Linux acceptance separately; distinguish X11/Wayland when relevant. A no-op is not support.
9. List dependencies, blockers, product decisions, security/privacy constraints, and exclusions.
10. Choose a small vertical slice that reaches the screen/behavior early and is independently verifiable: one reviewable diff and one commit.
11. Define automated and headed evidence before implementation.
12. Run the advisory checkpoint in `port-workflow.md`: `port-advisor` for the decomposition, and `port-advisor-critic` additionally for architecture, dependency, platform seam, security, rendering, and input decisions. When two concrete approaches remain viable, put the same question to both and reconcile the disagreement yourself rather than averaging it.

Use the 2026 official migration guide/cheat sheet as the technical starting point. Use the 2024 expert-guide methodology selectively: preparation, dependency audit, incremental slices, frequent small commits, and target-platform testing are accepted; its literal code-commenting migration recipe and "keep existing structure" rule do not override the greenfield architecture.

Never encode live task state, current package versions, active agent names, branch names, or "next work" into a durable starting prompt. Starting prompts define reconciliation protocol; current state is discovered from disk and from git. `client/port.txt` is the phase prompt and carries no status by design. After an interruption, reconcile from the board, `spine-tasks/CONTEXT.md`, git log, and `git worktree list`, and continue the in-flight wave rather than authoring a competing one.

If an Avalonia MCP server is registered and connected, plan a bounded advisory use only where it adds evidence: small AXAML validation, accessibility or layout questions, heuristic performance review. Never make it a required blocker, generator, or primary source; its upstream content targets Avalonia 11.3.1 and requires the safeguards in `avalonia-research`. The `avalonia-docs`, `avalonia-ui`, and `avalonia-live` seats are registered at user scope; confirm the one you want is connected before planning around it, and record a disconnected seat as a named limit rather than a blocker.

## Code placement

Until topology is approved, state responsibilities rather than inventing projects: portable rules/persistence, shared Avalonia UI/rendering, Windows implementation, Linux implementation, and tests/harnesses. A new abstraction needs a real platform boundary or multiple real implementations. Prefer framework/standard-library features over wrappers and dependencies.

## Task orchestration

- **One board row becomes one packet** at `spine-tasks/SP-NNN-slug/PROMPT.md`. That path, the `SP-<n>-` directory name, and the `| testCommand | ... |` contract row are asserted by `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs`, which fails closed. Do not invent a new packet root or filename.
- **A wave is a set of disjoint packets** run concurrently, one lane each, by `port-slice-executor` subagents in their own worktrees. Author the whole wave, commit the authoring, then launch.
- **Review Level 0-3** is declared per packet and drives which of `port-plan-reviewer`, `port-code-reviewer`, and `port-final-reviewer` run. Never plan a level the packet does not declare.
- **The orchestrator lands; a lane never lands itself.** The context that produced work does not certify it.
- Durable status belongs in `client/docs/task-board.md`. Lane branches and packet records are execution state and never substitute for the board.
- Model routing is a per-agent `model:` override and nothing else. There is no tier map. Every advisor seat is Anthropic now, so record the loss of cross-vendor disagreement as a named limit whenever a decision leans on advice.

## Parallel work

Parallelize only disjoint files and independently verifiable slices. Use separate worktrees. Assign shared project files, registration, schemas, localization, shell, and tracker rows to one orchestrator.

## Required plan output

For each slice: outcome; linked decision/contract; WPF evidence; focused history findings; relevant feature/systemic first-attempt lesson; allowed and forbidden areas; Windows acceptance; Linux acceptance or blocker; security/privacy/performance constraints; v12 research; the advisor question and which seat answers it; the declared Review Level; the `| testCommand |` contract row routed through the floor wrapper; end-to-end wiring proof; headed gates; tracker update; dependencies; stop conditions.

## Stop instead of guessing

Stop when sources conflict, behavior is ambiguous, a package/endpoint is needed, Linux equivalence is unknown, privacy/safety broadens, parallel work touches a chokepoint, or verification cannot prove the promise.

## Related skills

- `wpf-parity`, `avalonia-research`, `port-feature`, `mechanical-port-work`, `port-audit`.
