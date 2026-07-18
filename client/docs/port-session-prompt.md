# Restart-safe greenfield port session prompt

This is the stable entry prompt for starting or recovering the Windows/Linux greenfield Avalonia port. It intentionally contains no live task status, package baseline, branch name, completion percentage, active-agent claim, or next feature. Those facts must be discovered from the workspace at session start.

The port runs on **pi-spine**: deterministic task packets under `spine-tasks/` executed by real pi workers in worktree lanes, with cross-model review, contract verify, and integrate gates. The orchestrating pi session (this session) is the outer loop; pi-spine deliberately ships no autonomous supervisor. Workers are full pi sessions in this repository, so they inherit the project skills (`port-plan`, `port-feature`, `wpf-parity`, `avalonia-research`, `dashboard-design`, `overlay-clickthrough`, `unified-compositor-engine`), the project agents, `bpx-consult`, and the MCP configuration. Packet authoring uses the `create-spine-tasks` skill; orchestration follows the `spine-orchestrate-waves` skill; `docs/constitution.md` is every worker's standing orders.

## Operator entry

Give this file to a trusted pi session in the repository root:

```text
Orchestrate the greenfield port per @client/docs/port-session-prompt.md
```

There is no single run-creation slash command. The session reconciles first, then drives spine explicitly. `spine` is not on bash PATH by default on this workstation: `export PATH="$PATH:/c/Users/Micha/.pi/agent/npm/node_modules/.bin"` first.

## Reconciliation (mandatory first task)

Initialize or recover without trusting this chat, an old session summary, or static status text.

Do not implement or plan a feature yet. First perform a read-only CURRENT-STATE RECONCILIATION from durable evidence:

1. Read repository instructions and the current files under `client/docs/` (architecture.md, capability-inventory.md, first-attempt-lessons.md, first-attempt-systemic-lessons.md, port-workflow.md, task-board.md) plus `docs/constitution.md` and `spine-tasks/CONTEXT.md`.
2. Discover the currently available Pi skills, agents, workflows, loops, MCP servers, and model routes from disk and live tool registries. Do not assume names, versions, availability, or successful connections from this prompt.
3. Inspect git status, current branch/worktree, recent relevant commits, and scoped diffs. Never overwrite or auto-commit unrelated or unexplained changes.
4. Inspect spine state: `spine status --diagnose`, `spine plan pending`, `spine-tasks/SP-*/STATUS.md`, and `.spine/runtime/` evidence. The client task board is product-queue authority; spine state is execution/recovery state. Neither silently overwrites the other.
5. Inspect active loops (`LoopList`), monitors (`MonitorList`), and background agents. Do not infer that a persisted claim is actively running. Never duplicate an in-flight batch; never start a second engine on the same batch.
6. Classify the recovered state as exactly one of:
   A. ACTIVE_BATCH: a spine batch is valid and resumable (`spine batch resume`, retry, or wait);
   B. NEEDS_LAND: a batch finished and awaits the land loop (evidence review → gate approve → integrate → complete);
   C. ORPHANED_WIP: docs/spine state claims work but no valid batch owns it — reconcile via `spine status --diagnose` and `spine batch retry`;
   D. CLEAN_START: no valid unfinished execution exists;
   E. BLOCKED: conflicting, unsafe, or ambiguous state requires owner input.

Return a concise RECONCILIATION REPORT: classification; git/worktree state and unexplained changes; batch/task identifiers; board row and last proven evidence; available skills/agents/workflows/MCPs; stale or conflicting docs; exact next safe action; and whether it is safe to continue.

## Orchestration program

### Mission

Build a new Avalonia desktop client under repository-root `client/`, targeting Windows and Linux only. WPF is read-only behavioral evidence. The first Avalonia attempt under `ConditioningControlPanel/CCP.*` is read-only lessons and failure evidence, not implementation architecture. Port user-observable purpose and outcomes, not old mechanics.

### Authority order

1. Owner decisions in `client/docs/architecture.md` and `capability-inventory.md`.
2. `client/docs/task-board.md`, the only product work queue.
3. Repository instructions and `docs/constitution.md`.
4. Current relevant Pi skills and admitted tooling policy.
5. The task packet (`PROMPT.md`).
6. Advisor, MCP, and model suggestions.

Empirical evidence can prove a document stale; fix the smallest authoritative document before continuing.

### Cycle

Repeat until the active phase in `spine-tasks/CONTEXT.md` has no claimable work:

1. **Reconcile** (above) — resume or land existing work before creating anything.
2. **Author packets** with `create-spine-tasks` from unblocked `client/docs/task-board.md` rows in the approved phase scope. One board row = one packet = one outcome. Packets embed: the board row, WPF evidence pointers (`wpf-parity`), first-attempt ACCEPT/ADAPT/REJECT lessons, required skills, solo/council consult gates, a real scoped `testCommand`, Windows/Linux acceptance or a documented blocker, and the board-reconciliation step. Never author packets for blocked rows or beyond the owner-approved scope.
3. **Validate:** `spine tasks validate pending` → `spine tasks analyze pending` → `spine plan pending` → `spine preflight`. Fix packet errors before launch.
4. **Launch detached:** `spine batch start pending` (never `--attached` from an agent shell). Monitor with `MonitorCreate` running `spine wait --until completed,needs_integrate,failed,aborted,needs_retry --timeout 4h` with `onDone` reporting back — never poll or sleep inline.
5. **Steer** via the bounded steering loop (below) while the batch runs.
6. **Land:** on `needs_integrate`, run the evidence checklist from `spine-orchestrate-waves` (contract verify per task, scoped test output, review artifacts, no out-of-scope files in the merge preview) — then `spine gate approve` → `spine integrate` → `spine batch complete`. Never auto-approve a gate without reading evidence; never accept failed verification to keep a batch moving.
7. **Reconcile the board** — each row updated with concise evidence or the exact blocker before the next batch.
8. **Owner checkpoints:** stop and ask at every gate `spine-tasks/CONTEXT.md` marks owner-held (pilot admission, phase scope approval, architecture-proposal review, consult-probe resolution, MCP admission).

### Steering loop

While a batch is active, maintain exactly ONE pi-loop (hybrid: `monitor:done` event + 20m cron safety net, `readOnly: false`, bounded `maxFires`, deleted at batch close-out). Each fire:

1. Run `spine status --diagnose` and read new `.spine/runtime/` evidence and journal entries.
2. If stalled/failed: follow the diagnosis table in `spine-orchestrate-waves` (`retry`, `resume --force`, packet fix) — never start a second engine, never hand-edit `.spine/batch-state.json`.
3. If work drifts from the packet, board, or constitution: cancel/retry with a corrected packet rather than steering by hope.
4. **Process improvement:** when evidence shows a concrete missing capability, stale instruction, unsafe behavior, or repeated failure — file a bounded tooling task (new/adjusted skill, agent, workflow, or packet template). The loop proposes and drafts; a new skill/agent is adopted only after the orchestrator (or owner, for policy-touching changes) reviews it and validation proves it. Do not spend the port continually rewriting its own orchestration.
5. Record durable findings with `mem_save` (engram) so crashes and restarts do not lose steering context.

### Delegation

- **Workers** implement packets; the orchestrator never hand-edits product code.
- **Read-only subagents/workflows** handle WPF archaeology, current-state audits, and fan-out research or multi-perspective review at gates — use the `pi-dynamic-workflows` tier map (`small`/`medium`/`big`; `big-fallback` = `uva/gpt-5.6-sol:high` when the big route fails). Record fallback use.
- **consult** at the gates in `client/docs/port-workflow.md` §Consultation gates. Until the board's consult-probe row proves the council seats, use **solo** mode and record the caveat; a missing advisor is a failed gate, not silent consensus.
- **MCPs:** engram is memory. The Avalonia MCP is optional advisory review only (A-013), owner-gated, never v12 authority; if unavailable or unadmitted, skip it — that never blocks a task.
- Skills load per packet, not wholesale. Current Avalonia v12 facts come only through `avalonia-research`.

### Recovery

After a crash, restart, provider outage, or owner pause: run reconciliation again; prefer `spine batch resume`/`spine batch retry <taskId>` over new batches; resume at the first unfinished step; compare journal state with git, verification output, and the client board. Repeated or native/intermittent failure becomes a `BLOCKED` row or a focused diagnostic packet, not an infinite autofix loop.

### Product boundaries

- New product code, projects, tests, assets, and build scripts stay under `client/`.
- `.pi/`, `.spine/` config, `spine-tasks/` packets, and `client/docs/` may change only in explicit tooling/docs tasks.
- Legacy WPF and the first Avalonia attempt are read-only.
- Windows and Linux only; distinguish X11 and Wayland where behavior differs.
- Do not claim support from compilation, a no-op, a stub, a Windows-only test, markup presence, handler invocation, timer ticks, or a single screenshot.
- Never broaden webcam, biometric, secret, path, logging, capture, moderation, consent, or network boundaries without owner approval.

### Verification and completion

Use the tiered gates in `client/docs/port-workflow.md` §Verification floor: fast affected checks per iteration, targeted headed/K3 visual evidence at task close, broad matrices only at named milestones. A headed check that cannot be automated leaves the row `WIP`/`BLOCKED` with the exact manual gate named.

Close out a phase only when no claimable packet remains: audit current `client/` code against current contracts, list remaining blocked rows and owner decisions, delete session-only loops and monitors, and never derive completion from the first attempt's percentages or status markers.
