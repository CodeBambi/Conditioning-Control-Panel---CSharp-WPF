# Restart-safe greenfield port session prompt

This is the stable entry prompt for starting or recovering the Windows/Linux greenfield Avalonia port. It intentionally contains no live task status, package baseline, branch name, completion percentage, active-agent claim, or next feature. Those facts must be discovered from the workspace at session start.

The port runs on **Claude Code**. Task packets under `spine-tasks/` are executed by `port-slice-executor` subagents in git worktree lanes, with fresh-context review and a mechanical gate before anything merges. The orchestrating session is the outer loop and never hand-edits product code. Lanes inherit the project skills under `.claude/skills/` (`port-plan`, `port-feature`, `wpf-parity`, `avalonia-research`, `dashboard-design`, `overlay-clickthrough`, `unified-compositor-engine`) and the agents under `.claude/agents/`. `docs/constitution.md` is every lane's standing orders.

**Engine history.** pi-spine and the `consult` extension were retired 2026-08-14. pi-spine could not be retargeted: every worker and reviewer it spawns is the literal string `pi`, with no seam to substitute another CLI. `.pi/`, `.spine/`, and the existing `spine-tasks/SP-*/` packets remain on disk as read-only history. The packet path did not move, because `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs` binds `spine-tasks/`, the `PROMPT.md` filename, the `SP-<n>-` directory pattern, and the `| testCommand |` contract row, and fails closed on each.

## Operator entry

Open an interactive Claude Code session in the repository root and give it:

```text
Orchestrate the greenfield port per @client/docs/port-session-prompt.md
```

For an unattended run the phase prompt is `client/port.txt`, driven by `client/tools/port-loop.ps1`. **That script is currently disabled**: it is still the Pi version and refuses to start against the Claude Code prompt rather than launching `pi` with it. Until it is rewritten as the Claude Code lane scheduler, run supervised.

## Reconciliation (mandatory first task)

Initialize or recover without trusting this chat, an old session summary, or static status text.

Do not implement or plan a feature yet. First perform a read-only CURRENT-STATE RECONCILIATION from durable evidence:

1. Read repository instructions and the current files under `client/docs/` (architecture.md, capability-inventory.md, first-attempt-lessons.md, first-attempt-systemic-lessons.md, port-workflow.md, task-board.md) plus `docs/constitution.md` and `spine-tasks/CONTEXT.md`.
2. Discover the currently available skills, agents, and MCP servers from disk and from the live registries. Do not assume names, versions, availability, or successful connections from this prompt. A server that is registered is not a server that is connected.
3. Inspect git status, current branch, `git worktree list`, recent relevant commits, and scoped diffs. Never overwrite or auto-commit unrelated or unexplained changes.
4. Inspect lane state: existing `lane/*` branches, their worktrees, whether each is an ancestor of `feat/crossplatform`, and any packet records. The client task board is product-queue authority; lane state is execution and recovery state. Neither silently overwrites the other.
5. Check for a live wave lock (`.port/WAVE-LOCK`) and for background tasks. Do not infer that a persisted claim is actively running, and never start a second wave while one is live.
6. Classify the recovered state as exactly one of:
   A. ACTIVE_WAVE: lanes are running and owned by a live session;
   B. NEEDS_LAND: lanes finished and await the land sequence (verify merged state, merge, reconcile, push);
   C. ORPHANED_WIP: docs or lane state claim work no live session owns, so reconcile from git and either resume or abandon explicitly;
   D. CLEAN_START: no valid unfinished execution exists;
   E. BLOCKED: conflicting, unsafe, or ambiguous state requires owner input.

Return a concise RECONCILIATION REPORT: classification; git and worktree state and unexplained changes; lane and packet identifiers; board row and last proven evidence; available skills, agents, and MCP connection status; stale or conflicting docs; exact next safe action; and whether it is safe to continue.

## Orchestration program

### Mission

Build a new Avalonia desktop client under repository-root `client/`, targeting Windows and Linux only. WPF is read-only behavioral evidence. The first Avalonia attempt under `ConditioningControlPanel/CCP.*` is read-only lessons and failure evidence, not implementation architecture. Port user-observable purpose and outcomes, not old mechanics.

### Authority order

1. Owner decisions in `client/docs/architecture.md` and `capability-inventory.md`.
2. `client/docs/task-board.md`, the only product work queue.
3. Repository instructions and `docs/constitution.md`.
4. Current project skills under `.claude/skills/` and admitted tooling policy.
5. The task packet (`PROMPT.md`).
6. Advisor subagents, MCP, and model suggestions.

Empirical evidence can prove a document stale; fix the smallest authoritative document before continuing.

### Cycle

Repeat until the active phase in `spine-tasks/CONTEXT.md` has no claimable work:

1. **Reconcile** (above). Resume or land existing work before creating anything.
2. **Author packets** from unblocked `client/docs/task-board.md` rows in the approved phase scope, using `port-plan`. One board row equals one packet equals one outcome, at `spine-tasks/SP-NNN-slug/PROMPT.md`. Packets embed: the board row, WPF evidence pointers (`wpf-parity`), first-attempt ACCEPT/ADAPT/REJECT lessons, required skills, the advisor gates, a declared `## Review Level: N`, a real scoped `| testCommand |` row routed through the floor wrapper, Windows and Linux acceptance or a documented blocker, and the board-reconciliation step. Never author packets for blocked rows or beyond the owner-approved scope, and never reissue a used task ID.
3. **Validate before launching, mechanically:** `node client/tools/wave/validate-wave.mjs SP-0NN-a SP-0NN-b ...` must exit 0. It checks that every packet parses, that its `testCommand` and `floorDelta` contract rows are present, that its File Scope is glob-disjoint from every other packet in the wave, that `client/tests/floor/floor.json` (and, in a parallel wave, `client/docs/task-board.md`) is disclaimed, and that no task ID is reused. A wave it rejects is fixed in the packets, never in the validator.
4. **Launch the wave:** commit the authoring first, write `.port/WAVE-LOCK`, then start every lane in ONE message so they run concurrently, each a `port-slice-executor` with its packet. Own the wait; there is no detached engine.
5. **Steer** only through the packet. If work drifts from the packet, the board, or the constitution, stop that lane and correct the packet rather than steering by hope.
6. **Exit without landing.** When every lane has reported, record each lane's branch and head SHA, clear the lock, and exit. A fresh session lands the work, because the context that produced it must never certify it.
7. **Land** (the next session): verify the merged state yourself in a scratch worktree, merge, reconcile the board with concise evidence or the exact blocker, write the digest, then push. See Land discipline below.
8. **Advisory gates.** `port-advisor` gates phase decomposition, each packet's pre-approach and pre-completion, and every land of P0 or high-risk work; add `port-advisor-critic` for architecture, dependencies, platform seams, privacy, and security. Record verdicts in the packet evidence. When a phase completes, re-derive the next scope from unblocked board rows, take advice on the decomposition, update `spine-tasks/CONTEXT.md`, and continue. Rows needing genuinely unavailable headed or manual evidence stay `WIP` or `BLOCKED` with the exact gate named. They are not failures and do not stop other work.

### Land discipline

- Never trust a lane's or a reviewer's own evidence. Run the merged state yourself in a scratch worktree: three consecutive `node client/tests/floor/check-floor.mjs` runs with output redirected to files, and prove `git diff` is EMPTY between the tree you verified and the integrated tip.
- **The land's last action verifies the tree actually being pushed.** A reconciliation edit made after the verification run is unverified; the wave-18 land shipped a red base exactly that way. Re-run the guards that read repository documents (`UpstreamPayloadInventoryTests`, `AiOperationContractTests`, `VersionDerivationTests`) after any late doc or JSON edit.
- Landed rows stay `WIP` until the owner ratifies. Flip to `DONE` only with a RATIFIED citation.
- Append three lines to `client/docs/port-digest.md` on every landing phase: what landed, what it does NOT prove, and any owner question raised.

### Learning harvest

At every land, collect lessons from live evidence (lane retries and their causes, REVISE and REPLAN reasons, contract failures, v12 surprises, environment quirks, packet-authoring mistakes) and route each to its sink:

- `client/docs/port-lessons.md`: a one-to-three-line dated entry; prune superseded entries.
- `client/memories/`: durable cross-session state that outlives a crash. Read `port-status.md` at session start and update it whenever routes or state change materially, then commit.
- The packet-authoring pattern: fix it immediately when the next packet is written.

When the SAME lesson appears twice, stop absorbing it manually and file a bounded tooling row to encode it durably: a skill, an agent, a standing order in `docs/constitution.md`, or a mechanical check. One-off lessons stay in port-lessons.md. Do not spend the port continually rewriting its own orchestration.

At each land, briefly ask whether already-landed slices could be materially improved by what was just learned. If yes, file an evidence-citing improvement row. Never immediate rework; the board decides when it is worth a slice.

### Delegation

- **Lanes** (`port-slice-executor`, worktree-isolated) implement packets. The orchestrator never hand-edits product code.
- **Review** is `port-plan-reviewer` (plan, Level 1+), `port-code-reviewer` (diff, Level 2+), `port-final-reviewer` (completion, Level 3, PASS / REVISE / REPLAN). Each is a fresh context. A lane never adjudicates its own completion.
- **Advice** is `port-advisor` (default) and `port-advisor-critic` (the costly adversarial seat, spent on architecture, dependencies, platform seams, privacy, security, and decomposition). A council is the orchestrator fanning both out in one round and synthesizing the result itself.
- **Read-only evidence agents:** `wpf-archaeologist` for WPF archaeology without loading the giant files into the orchestrator, `greenfield-foundation-auditor` for foundation and wiring claims, `port-parity-auditor` for the working-tree diff against WPF ground truth.
- Model routing is a per-agent `model:` override and nothing else. There is no tier map. **Named limit: every advisor and reviewer seat is Anthropic**, so agreement between them is weak evidence and blind spots are correlated. Weight the mechanical checks above any verdict, and say so whenever a decision rests on advice.
- **MCPs:** three Avalonia seats are registered at user scope (`avalonia-docs`, `avalonia-ui`, `avalonia-live`). `avalonia-live` connects only while the client runs with `CCP_MCP=1` and reads as disconnected otherwise, by design. All three are advisory: never v12 authority, never production generation, and unavailability never blocks a task. Verify connection at reconciliation rather than assuming it. Durable memory is `client/memories/`.
- Skills load per packet, not wholesale. Current Avalonia v12 facts come only through `avalonia-research`.

### Recovery

After a crash, restart, provider outage, or owner pause: run reconciliation again; prefer resuming an existing lane over authoring a replacement; compare lane branches and packet records with git, verification output, and the client board. Repeated or intermittent failure becomes a `BLOCKED` row or a focused diagnostic packet, not an infinite autofix loop.

### Pause protocol

A saturated model is not a stop condition; the loop passes a fallback model and the phase records which model it actually ran on. Pause when JUDGMENT is exhausted, not capacity: a safety or privacy question, ambiguity no repository source resolves, the same verification failing twice on the same cause, a lane that cannot leave its tree buildable, a wave lock whose owner is gone, or a failed blind audit. Then, in order: park in-flight work, write `.port/handoff.md` with state, evidence paths, and the exact next action, checkpoint `client/memories/`, write `.port/STOP` with the reason, and exit.

### Product boundaries

- New product code, projects, tests, assets, and build scripts stay under `client/`.
- `.claude/` configuration, `spine-tasks/` packets, and `client/docs/` may change only in explicit tooling or docs tasks. `.pi/` and `.spine/` are frozen history and change never.
- Legacy WPF and the first Avalonia attempt are read-only.
- Windows and Linux only; distinguish X11 and Wayland where behavior differs.
- Do not claim support from compilation, a no-op, a stub, a Windows-only test, markup presence, handler invocation, timer ticks, or a single screenshot.
- Never broaden webcam, biometric, secret, path, logging, capture, moderation, consent, or network boundaries without owner approval.
- New board rows may be filed only for discovered parity gaps, blockers, or bounded tooling needs, each citing WPF or capability-inventory evidence. Net-new product features beyond the agreed parity posture require an explicit owner decision; the run never invents scope.

### Verification and completion

Use the tiered gates in `client/docs/port-workflow.md`: fast affected checks per iteration, targeted headed and visual evidence at task close, broad matrices only at named milestones. The mechanical gate is `node client/tests/floor/check-floor.mjs`; a bare `dotnet test` is not it. Never export `CCP_DATA_ROOT` process-wide, which makes the SP-057 pin skip and the suite report a vacuous green. A headed check that cannot be automated leaves the row `WIP` or `BLOCKED` with the exact manual gate named.

Close out a phase only when no claimable packet remains: audit current `client/` code against current contracts, list remaining blocked rows and owner decisions, clear session-only state, and never derive completion from the first attempt's percentages or status markers.
