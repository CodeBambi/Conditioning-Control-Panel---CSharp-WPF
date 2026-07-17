# Restart-safe greenfield port session prompt

This is the stable entry prompt for starting or recovering the Windows/Linux greenfield Avalonia port. It intentionally contains no live task status, package baseline, branch name, completion percentage, active-agent claim, or next feature. Those facts must be discovered from the workspace at session start.

## Operator entry

Do not copy sections from this file into chat. Use the file directly:

```text
/task-auto @client/docs/port-session-prompt.md
```

Use that command only to create the run. After a crash, restart, cancellation, or paused failure, use:

```text
/task-auto-resume
```

The installed `pi-task` implementation always plans a fresh AUTO journal when `/task-auto` is invoked; it does not redirect that command to an existing journal. Therefore one literal command cannot safely mean both start and resume. `/task-auto-resume` is the deterministic recovery command and automatically selects the active run's first unfinished task.

Do not start a second `/task-auto` merely because the chat restarted. The files in `.pi-tasks/`, git, and `client/docs/task-board.md` survive the chat.

Before the first real run, use `/task-config` to verify the current settings rather than relying on this file for defaults. Keep verification and orientation enabled. Keep remote disabled unless deliberately needed on a trusted network. Keep auto-commit disabled whenever the tree is not clean or its one-task commit behavior has not been proven for this run.

## Reconciliation instructions

```text
Initialize or recover the greenfield CCP Avalonia port without trusting this chat, an old session summary, or static status text.

Do not implement or plan a feature yet. First perform a read-only CURRENT-STATE RECONCILIATION from durable evidence:

1. Read repository instructions and the current files under client/docs/, especially architecture.md, capability-inventory.md, first-attempt-lessons.md, first-attempt-systemic-lessons.md, port-workflow.md, task-board.md, and this file.
2. Discover the currently available Pi skills, agents, workflows, loops, MCP servers, project packages, and model routes from disk and live tool registries. Do not assume names, versions, availability, or successful connections from this prompt.
3. Inspect git status, current branch/worktree, recent relevant commits, and scoped diffs. Never overwrite or auto-commit unrelated or unexplained changes.
4. Inspect `.pi-tasks/TASK_AUTO_*.md` and `.pi-tasks/TASK_*.md` if present. Reconcile their checked/unfinished state with git history and client/docs/task-board.md. The client task board is product queue authority; `.pi-tasks` is execution/recovery state. Neither may silently overwrite the other.
5. Inspect active workflow/agent/terminal/monitor state and `.pi/loops/`. Do not infer that a persisted claim is actively running. Do not duplicate an in-flight process. Never poll or sleep; use completion notifications.
6. Verify the installed `@mjasnikovs/pi-task` behavior and current configuration before depending on it. Verify current Avalonia v12 facts through `avalonia-research`; do not trust stale first-attempt or MCP-generated API claims.
7. Classify the recovered state as exactly one of:
   A. ACTIVE_AUTO: an auto run is valid and resumable;
   B. ACTIVE_TASK: one unfinished single task is valid and resumable;
   C. ORPHANED_WIP: docs/task state claims work but no valid run owns it;
   D. CLEAN_START: no valid unfinished execution exists;
   E. BLOCKED: conflicting, unsafe, or ambiguous state requires owner input.

For ORPHANED_WIP, reconstruct the smallest safe continuation from the task row, task journal, diff, commits, and verification evidence. Never restart completed phases or discard valid work. If ownership or correctness cannot be proven, mark the row blocked rather than guessing.

Before recommending execution, assess whether the current orchestration setup is fit for the next work:
- Load only skills relevant to the next task.
- Reuse existing agents/workflows when they fit.
- Create or adjust a Pi agent, skill, workflow, or loop only when current evidence shows a concrete missing capability, stale instruction, unsafe behavior, or repeated failure.
- Customization changes are bounded tooling tasks with their own validation. Do not spend the port continually rewriting its orchestration.
- Never weaken owner decisions, client-only product-code boundaries, Windows/Linux acceptance, privacy/security rules, official Avalonia v12 research, or headed verification.
- Treat the Avalonia MCP as optional advisory review under A-013, never as v12 authority or generated production code.

Return a concise RECONCILIATION REPORT containing:
- classification;
- git/worktree state and unexplained changes;
- active/resumable task-auto or task identifiers;
- task-board row and last proven evidence;
- currently available relevant skills/agents/workflows/MCPs;
- stale/conflicting docs or customization findings;
- exact next safe action;
- whether this new run is safe to continue or must stop because older resumable work or ambiguous state exists.

Do not claim that a slash command ran unless Pi actually executed it. When this text is running inside `/task-auto`, make reconciliation the first task and do not begin product implementation until it passes.
```

## Program supplied to `/task-auto`

When this file is referenced by a new `/task-auto` run, the planner must create an ordered, restart-safe program from the state that exists at that moment. It must not convert every heading below into a task or repeat already completed work.

### Mission

Build a new Avalonia desktop client under repository-root `client/`, targeting Windows and Linux only. WPF is read-only behavioral evidence. The first Avalonia attempt under `ConditioningControlPanel/CCP.*` is read-only lessons and failure evidence, not implementation architecture. Port user-observable purpose and outcomes, not old mechanics.

### Dynamic planning invariant

The first generated task must reconcile current state unless the initialization report already produced fresh, cited reconciliation evidence. Every later task must re-read its task-board row, relevant architecture decision, current diff, and latest commits before editing. If durable state changed since decomposition, stop and update/replan the remaining AUTO titles rather than following stale assumptions.

`/task-auto` creates only ordered titles; each title receives fresh `/task` refine, research, grill, compose, critique, implementation, and verification. Therefore titles must describe outcomes and authoritative inputs, not freeze guessed files, APIs, package versions, or implementation designs.

### Authority order

1. Current owner decisions in `client/docs/architecture.md` and `capability-inventory.md`.
2. Current `client/docs/task-board.md`, the only product work queue.
3. Repository instructions.
4. Current relevant Pi skills and admitted workflow/tooling policy.
5. The generated task spec.
6. Advisor, MCP, and model suggestions.

Code and empirical evidence can prove a document stale. When they do, fix the smallest authoritative document before continuing. A generated spec never overrides higher authority.

### Required decomposition behavior

1. Reconcile current execution, git, docs, and tooling state.
2. Resume valid unfinished work before selecting new work.
3. If no work is resumable, select the highest-priority unblocked task-board outcome whose prerequisites are satisfied.
4. Split work into independently verifiable vertical slices, one product outcome per task and commit.
5. Include a tooling/customization task only when reconciliation proves it is necessary for upcoming work.
6. Keep blocked rows blocked. Ask the owner only for decisions that cannot be answered from current evidence.
7. Do not create a giant "implement the port" task, parallel edits to shared chokepoints, or speculative platform architecture.
8. Reconcile the task board after every task before the AUTO loop advances.

### Task contract

Every generated task must include:

- one observable or architecture-enabling outcome linked to a current task-board row;
- narrow WPF behavior evidence gathered through `wpf-parity` when parity is relevant;
- applicable first-attempt `ACCEPT`, `ADAPT`, or `REJECT` lesson;
- focused git history for the affected WPF/first-attempt paths, including later fixes, reverts, re-openings, races, leaks, crashes, unwired work, and deletions; commit subjects are leads and final code is verified;
- current official Avalonia v12 research and exact package/API evidence when applicable;
- allowed files and explicit exclusions;
- Windows and Linux acceptance or a documented product blocker;
- privacy, security, performance, window/input/compositor constraints as applicable;
- consultation mode and precise decision question;
- a real task-specific `VERIFY` block;
- targeted headed/visual gates that automation cannot prove;
- task-board and architecture/lesson reconciliation;
- one conventional commit with no unrelated changes.

No product capability may close from isolated code, registration, unit tests, assets copied, or a fallback that does not throw. Each product slice must prove one real composition-root-to-user-outcome path. Foundation slices also apply A-014 and `first-attempt-systemic-lessons.md`.

For AXAML and UX work, use the chain recorded in A-013: contract/reference images, current official v12 sources, smallest hand-authored implementation, optional redacted Avalonia MCP review, real compiler/tests/headed interaction, and targeted K3 screenshot review. MCP validation and screenshots cannot prove interaction.

### Delegation and customization

Use existing Pi workflows, loops, skills, and agents based on their live descriptions. No hard-coded agent roster in this prompt is authoritative.

- Use specialized read-only agents for WPF archaeology, current-state audits, and focused research when available.
- Use implementation agents/workflows for product changes; the orchestration driver does not hand-edit product code.
- Use consultation at the gates defined in `port-workflow.md`.
- Create a new agent only for a recurring isolated role with a clear tool boundary.
- Create or amend a skill only for reusable domain workflow or knowledge not already covered.
- Amend instructions only when the rule applies broadly and repeatedly.
- Use a loop only for low-frequency stall recovery. It must detect active work, never poll, never duplicate a run, have bounded lifetime/fires, and delete itself at close-out.
- Validate customization metadata and behavior before allowing it to guide implementation.

### Recovery behavior

After a crash, restart, provider outage, expired authentication, external process termination, or owner pause:

1. Use the session initialization prompt again.
2. Prefer `/task-auto-resume` for an active AUTO journal and `/task-resume <id>` for an unfinished child task.
3. Resume at the first unchecked/unfinished phase. Do not create replacement tasks for completed journal phases.
4. Compare journal state with git commits, working diff, verification output, and the client task board.
5. If a task failed, preserve diagnostics and narrow the retry. Repeated or native/intermittent failure becomes a blocker or focused diagnostic task, not an infinite autofix loop.
6. Never accept failed verification merely to keep the AUTO loop moving.

### Product boundaries

- New product code, projects, tests, assets, and build scripts stay under `client/`.
- Workflow configuration and durable port documentation may be updated in `.pi/` and `client/docs/` when a task explicitly allows it.
- Legacy WPF and the first Avalonia attempt are read-only unless the owner explicitly authorizes reference-side changes.
- Support Windows and Linux only. Distinguish Linux X11 and Wayland where behavior differs.
- Do not claim support from compilation, a no-op, a stub, a Windows-only test, markup presence, handler invocation, timer ticks, or a single screenshot.
- Never broaden webcam, biometric, secret, path, logging, capture, moderation, consent, or network boundaries without owner approval.

### Verification and completion

Use the tiered gates in `client/docs/port-workflow.md`. Keep iteration checks focused. Use targeted headed and K3 visual evidence before closing affected UI tasks. Run broad matrices only at named milestones/releases.

After each task:

1. inspect the diff and actual verification output;
2. update the task-board row with concise evidence or blocker;
3. update architecture/lessons only when facts changed;
4. ensure the commit contains only that slice;
5. re-evaluate the remaining AUTO titles against current state;
6. continue only when the workspace is safe and the prior task is genuinely complete.

Close out only when no claimable task remains for the selected milestone. Audit current client code against current contracts, list remaining blocked rows and owner decisions, remove session-only recovery loops, and never derive completion from the first attempt's percentages or status markers.
