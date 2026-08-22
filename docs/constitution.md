# Conditioning Control Panel Constitution

Standing rules for work on the greenfield Avalonia client under `client/`. These rules are
workflow-harness neutral. A task may narrow scope, but it cannot override this document.

## Mission

Build a Windows and Linux Avalonia client that preserves the legacy product's user-observable
purpose and outcomes. The legacy WPF product is behavioral evidence, not an implementation template.

## Authority

1. Owner decisions in `client/docs/architecture.md` and `client/docs/capability-inventory.md`.
2. `client/docs/task-board.md`, the only live product queue.
3. Repository instructions and this constitution.
4. Task-specific contracts, research, and verification evidence.
5. Workflow notes and review output.

When evidence proves a higher source stale, correct the smallest authoritative source before
continuing. Lower sources do not override higher ones.

## Boundaries

- New product code, tests, assets, and build work belong under `client/`.
- `ConditioningControlPanel/` is the shipping WPF product and read-only behavioral evidence for
  greenfield work. Do not copy its architecture, platform interop, service locator, timers, or
  implementation details into the new client.
- Verify current official Avalonia documentation before choosing an Avalonia v12 API, dependency,
  platform integration, lifecycle mechanism, rendering path, or native interop approach.
- Keep Windows and Linux evidence separate. Compilation, a stub, a no-op fallback, markup, or a
  single-platform test does not prove cross-platform support. Distinguish X11 and Wayland where
  behavior differs.
- Do not broaden webcam, biometric, secret, path, logging, capture, moderation, consent, or network
  boundaries without an owner decision. Do not expose sensitive user data to external services.

## Work And Review

- Reconcile the repository and task board before starting work; never trust a historical prompt,
  status summary, or local workflow state over current evidence.
- Keep each change to one coherent outcome with explicit permitted scope, exclusions, platform
  expectations, verification, and ownership of shared files.
- A worker implements in an isolated workspace or worktree. A fresh independent reviewer is used
  for high-risk, architecture, dependency, platform, privacy, security, lifecycle, or cross-cutting
  work. No producer certifies its own completion.
- There is no fixed cap on concurrent workflows. Concurrent work requires disjoint file scope,
  isolated workspaces, one explicit owner per shared chokepoint, adequate current resources, and
  practical task-specific validation. Build/test concurrency stays separately constrained through
  `client/tools/gate/with-slot.mjs`.
- When verified lessons reveal a recurring coordination or technical problem, create or revise the
  reusable workflow assets supported by the current harness, such as instructions, skills, role
  definitions, templates, or checks. Keep the improvement evidence-based, narrowly scoped, and
  independent of a named vendor or model.
- The matching board row records concise evidence or the exact blocker before work is represented as
  complete. Shared board and floor state have a single named owner.

## Verification

- A failed check is never accepted to keep work moving. Use focused checks during implementation and
  the task's mechanical and headed gates before completion.
- Run `node client/tests/floor/check-warnings.mjs` followed by
  `node client/tests/floor/check-floor.mjs` when the change requires the standard client gates.
  Use `--cold` for project, property, target, or lock-file changes.
- Headed/visual claims require headed evidence when composited pixels, geometry, scaling, occlusion,
  z-order, input, focus, media, animation, or window behavior matters. A headless result does not
  discharge presentation evidence.
- No new wall-clock waits in tests. Use deterministic signals or the approved bounded helper; do not
  use `Thread.Sleep`, bare `Task.Delay`, or clock-poll loops. Never export `CCP_DATA_ROOT`
  process-wide.
- When a required platform or manual gate cannot run, leave the work `WIP` or `BLOCKED` with the
  exact missing gate. Do not claim support from a build alone.

## Change Discipline

One task is one coherent commit slice. Do not mix unrelated files, weaken a guard to make a step
pass, introduce placeholders in place of behavior, or extend scope beyond the approved board row.
Pause and seek an owner decision when evidence conflicts, a safety decision remains unresolved, or
the required acceptance cannot be honestly demonstrated.
