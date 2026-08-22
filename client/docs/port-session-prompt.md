# Greenfield Client Session Prompt

Use this document to start or recover work on the Windows/Linux Avalonia client under `client/`.
It is compatible with any workflow harness that can provide isolated workspaces and independent
review. It deliberately contains no live status, capacity limit, task identifier, branch, model,
or historical execution state.

## Reconcile First

Before making a plan or change, inspect the current repository:

1. Read `docs/constitution.md`, `client/docs/port-workflow.md`, relevant contracts, and
   `client/docs/task-board.md`.
2. Inspect the working tree, recent relevant commits, active workspaces or worktrees, and existing
   scoped changes. Do not overwrite work with an unclear owner or purpose.
3. Identify the next unblocked board row, its behavior evidence, dependencies, affected shared
   files, and Windows/Linux acceptance criteria.
4. Treat old summaries and local workflow state as clues, not authority. Repair a stale
   authoritative document before depending on it.

Classify the result as work to resume, change to review, bounded work ready to start, blocked work,
or no claimable work. Record the exact next safe action and any unresolved owner decision.

## Operating Contract

- The task board is the only live queue. The client contracts and owner decisions outrank it; lower
  workflow notes never override either.
- Legacy WPF is read-only behavioral evidence. Port user-visible outcomes, not implementation
  topology. The visible usage contract is the source of truth: what the user can do, what they see,
  and how the feature behaves in real interaction. New product code belongs under `client/`.
- The active workflow harness remains in use while the port is underway. It must keep working on the
  port, capture what it learns, and evolve the current skill and agent set as needed. When an older
  skill or agent remains valid, it can be uploaded or reused; when it is obsolete, it is retired without
  blocking the working harness.
- Verify current official Avalonia documentation before choosing a v12 API, package, rendering,
  platform, lifecycle, or native-integration mechanism.
- A feature is not cross-platform because it compiles. Record Windows and Linux evidence separately,
  and keep unavailable manual or headed gates visible as `WIP` or `BLOCKED`.
- Do not broaden safety, privacy, capture, secret, media, consent, or network boundaries without an
  owner decision.

## Work Coordination

Use generic roles as the harness permits: a coordinator for state and shared chokepoints, a worker
for one bounded implementation, a researcher for evidence gathering, and an independent reviewer
for contract and verification review. Roles describe responsibility, not a required tool.

There is no fixed cap on concurrent workflows. Concurrent work is allowed only when every active
change has a distinct isolated workspace or worktree, disjoint file scope, an explicit owner for
each shared chokepoint, sufficient current resources, and its own appropriate validation. Build and
test concurrency remains separately governed by `client/tools/gate/with-slot.mjs`.

Keep a worker's change to one coherent board outcome. A change touching shared floor pins, board
state, release metadata, or composition roots needs a single named owner. Do not solve conflicts by
having multiple workers edit the same file concurrently.

## Completion

For each bounded change:

1. Confirm the relevant WPF behavior and current platform facts.
2. Implement only within the agreed scope.
3. Run focused checks while working and the task's full mechanical verification before handoff.
4. Obtain independent review for high-risk, architecture, platform, privacy, security, lifecycle,
   dependency, or cross-cutting work.
5. Obtain headed evidence for presentation, interaction, windowing, media, input, scaling,
   occlusion, or composition claims.
6. Update the matching board row with concise evidence or the exact blocker.

Use the standard client gates after applicable changes:

```powershell
node client/tests/floor/check-warnings.mjs
node client/tests/floor/check-floor.mjs
```

Run the warning gate with `--cold` after project, property, target, or lock-file changes. Never
export `CCP_DATA_ROOT` process-wide; an isolated per-operation value is required where a test or
capture needs one. A failed gate, conflicting evidence, repeated unexplained failure, unavailable
required platform evidence, or unresolved safety decision is a pause condition, not a reason to
weaken verification.
