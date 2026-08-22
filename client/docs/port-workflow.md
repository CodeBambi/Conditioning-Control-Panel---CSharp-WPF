# Greenfield Client Workflow

This is the durable operating contract for work on the Windows/Linux Avalonia client. It is
workflow-harness neutral: no particular tool, model, prompt archive, command runner, or historical
execution record is required. Use `client/docs/port-session-prompt.md` to begin a fresh session.

## Authority And Scope

Authority descends from owner decisions in `client/docs/architecture.md` and
`client/docs/capability-inventory.md`, to `client/docs/task-board.md`, then repository instructions
and task-specific evidence. The task board is the only live product queue. When sources conflict,
resolve the smallest authoritative document before continuing.

The shipping WPF product is read-only behavioral evidence. The greenfield implementation, tests,
assets, and build work belong under `client/`. Port user-visible behavior, not classes, service
locators, timing assumptions, or platform internals from an older implementation. The contract is
what the user can do, what the system visibly shows, and how the feature behaves in regular use;
WPF internals are not the product contract.

The active workflow harness remains in active use throughout the port. It should continue operating,
learn from each verified result, and create or revise the skills, agents, roles, templates, or rules
it needs to carry the work forward. Older proven workflow assets may be re-used when they still fit
this port; dead historical state should be retired without blocking the working harness.

## Workflow Lifecycle

1. **Reconcile:** inspect the governing documents, current branch and worktree state, relevant
   history, uncommitted work, and the next unblocked board row.
2. **Bound:** state one outcome, behavior evidence, permitted file scope, exclusions, platform
   expectations, verification, and shared chokepoint ownership.
3. **Research:** gather narrow WPF behavior evidence and current official Avalonia and platform
   sources before choosing an implementation.
4. **Implement:** make one coherent change in an isolated workspace or worktree. Keep unrelated
   changes out of the slice.
5. **Review:** use a fresh independent context for architecture, dependencies, platform seams,
   privacy, security, lifecycle, and other high-risk work. The producer does not certify its own
   completion.
6. **Verify:** run focused checks, task-specific mechanical tests, and required headed evidence.
7. **Reconcile:** update the matching task-board row with evidence or the exact blocker. Verify the
   tree actually being integrated after any late documentation or JSON change.
8. **Improve:** when verified lessons reveal a recurring coordination or technical problem, create
   or revise the reusable workflow assets supported by the current harness, such as instructions,
   skills, role definitions, templates, or checks. Keep the improvement evidence-based, narrowly
   scoped, and independent of a named vendor or model.

## Concurrency

There is no fixed maximum number of concurrent workflows. Launch work only when all active slices
have distinct isolated workspaces or worktrees, disjoint file scope, explicit ownership of each
shared chokepoint, suitable current CPU, memory, disk, and provider capacity, and a practical path
to validation.

The coordinator owns shared state such as the task board, test floor pin, release metadata, and
merge decisions. A worker owns one bounded change. A researcher gathers evidence without changing
the product. An independent reviewer checks the completed result against its contract. These are
responsibilities, not named tools or required automation.

Build and test concurrency is deliberately separate from workflow concurrency. Use the existing
gate-slot semaphore for concurrent expensive commands:

```powershell
node client/tools/gate/with-slot.mjs --slots <current-limit> -- node client/tests/floor/check-warnings.mjs
node client/tools/gate/with-slot.mjs --slots <current-limit> -- node client/tests/floor/check-floor.mjs
```

Choose `<current-limit>` from actual machine conditions rather than copying a historical value.
Never run conflicting builds or tests in the same workspace.

## Evidence And Review

Use primary sources and observable behavior rather than claims from a workflow. Current official
Avalonia documentation is required for v12 API, styling, rendering, input, lifecycle, windowing,
packaging, native interop, and dependency decisions. WPF investigation establishes behavior, not
architecture.

Independent review is required when the risk justifies it. Review must identify the contract,
changed files, verification result, unresolved Windows/Linux implications, and missing evidence.
Agreement is not proof; tests, source research, scoped diffs, and headed captures remain the
evidence.

Do not claim Windows/Linux support from a build, stub, no-op fallback, Windows-only test, or a
single screenshot. Distinguish X11 and Wayland where their behavior differs. A task lacking the
required manual, platform, or headed evidence remains `WIP` or `BLOCKED` with the exact gate named.

## Verification Floor

Use the smallest check that can falsify the current change while working. At task close, run the
checks required by its contract. The standard client gates are:

```powershell
node client/tests/floor/check-warnings.mjs
node client/tests/floor/check-floor.mjs
```

The warning gate performs the required non-incremental build. Run it with `--cold` when the change
touches a project file, MSBuild property or target, or lock file. The floor consumes fresh build
output and enforces exact test results; a bare `dotnet test` does not replace it. Change the exact
floor count only with the corresponding test change and a recorded reason.

Use headed verification whenever a claim depends on composited pixels, geometry, scaling, occlusion,
z-order, focus, input, media, animation, or window behavior. Headless frames can establish drawing
logic but do not establish presentation. Do not introduce wall-clock waits in tests, and never
export `CCP_DATA_ROOT` process-wide.

## Safety And Completion

Never broaden webcam, biometric, secret, path, logging, capture, moderation, consent, or network
boundaries without an owner decision. Do not send sensitive user data or logs to external services.

Pause rather than improvise when a source conflict cannot be reconciled, a safety or privacy
decision is unresolved, verification repeatedly fails for the same cause, scope must expand beyond
the board row, or required platform evidence is unavailable. Preserve the current state, record the
specific blocker and evidence, and leave the board honest.

Close a row only when its behavior, implementation, verification, and documented evidence agree.
Do not use a green compilation result, workflow output, or a historical status claim as a substitute
for that agreement.
