# Port session prompt (LIVE — the running session maintains this file)

> **What this is:** the run-to-completion driver prompt for autonomous CCP port sessions. Paste the
> PROMPT block below into a fresh driver session after the launch pre-flight. **Maintenance contract:**
> this file holds STABLE PROTOCOL only. Volatile facts (claim-priority order, smoke drift set, test
> floor, row statuses, model-routing block, token-economy rules) live on
> `avalonia-migration-task-board.md` and are referenced, never copied. If work changes a protocol-level
> fact asserted here (gate set, skill list, stop conditions, completion bar, pre-flight config), update
> this file IN THE SAME COMMIT. Never let this file and the board disagree — the board wins.

## Launch pre-flight (human, ~1 min)

1. `/model` → `anthropic/claude-opus-4-8` (driver = orchestration only).
2. `/workflows-models` → verify small/medium/big match the board's model-routing block
   (currently mirrored there; board authoritative).
3. Workflows keyword trigger OFF (the tool stays on; the driver calls it deliberately). `/ultracode` OFF.
4. `git status` sanity: on `feat/crossplatform`, tree clean (untracked `.pi/providers/` is expected — never touch).
5. Paste the PROMPT block. The session self-bootstraps via `create_goal`.

## PROMPT

```
Create a goal with create_goal (replace_existing: true) using this objective, then execute it to
completion:

OBJECTIVE: Drive the CCP Avalonia port (branch feat/crossplatform) to completion by working the task
board queue autonomously: claim one row at a time, implement with verification, gate, commit, update the
ledger, and move on. Stop only on BLOCKED: conditions or product decisions.

BOOTSTRAP (in order):
1. Read ConditioningControlPanel/docs/docs-index.md — doc map and read order.
2. Read ConditioningControlPanel/docs/skia-rebuild-goal.md — contract, rendering doctrine, DoD.
3. Read ConditioningControlPanel/docs/avalonia-migration-task-board.md — the ONE live tracker: claim
   ledger, tier tags, claim-priority order, model routing, token economy, smoke drift set. Trust doc
   statuses (post-reconciliation owner ruling); spot-verify only claims load-bearing for
   state/economy/security/input-hook/compositor work.
4. Read ConditioningControlPanel/docs/port-session-prompt.md (this prompt's home) — you MAINTAIN it:
   if your work changes a protocol fact asserted there, update it in the same commit.

WORK LOOP (repeat until the completion bar is met):
1. Claim exactly ONE open row — the topmost in the board's LIVE claim-priority order line (improvement
   queue section). Append a dated WIP entry to the claim ledger. When a row lands, update the
   priority-order line in the same commit. Row #6 (DTRH web roguelite) is WEB-ONLY per the 2026-07-10
   owner ruling — read the row's OWNER RULING, doctrine split, window contract, and appendix phases
   before touching it; binding order: web port FIRST, native chaos-run decommission SECOND
   (confirm-then-delete per file, ambient carve-outs listed on the row).
2. Mandatory skills: port-plan before non-trivial work; port-feature for implementation;
   avalonia-research before ANY Avalonia API/package question; unified-compositor-engine +
   overlay-clickthrough for compositor/input work; wpf-parity for behavior contracts;
   mechanical-port-work for small-tier rows; dashboard-design for user-facing surfaces;
   port-audit at workstream close-out.
3. Dispatch by tier per the board's model-routing block (call subagents/workflow tool DELIBERATELY —
   the keyword trigger is off by design). JUDGMENT = short high-leverage calls for architecture,
   slicing, adversarial review, state/economy/security/input-hook/compositor internals. STANDARD =
   bounded implementation and research digestion (NO VISION — screenshots go to the driver or a
   vision-capable model). MECHANICAL = pre-sliced literal edits with WPF file:line cites, sweeps,
   deletions. Agents share no context: every dispatch prompt must be self-contained. Prefer
   wpf-archaeologist for WPF semantics, port-slice-executor for pre-planned slices,
   port-parity-auditor before committing state-mutating/economy work.
4. Gates before EVERY code commit (proportional: docs-only commits skip build gates):
   - dotnet build ConditioningControlPanel/CCP.Desktop.slnf -c Debug → 0 errors
   - dotnet build ConditioningControlPanel.sln → 0 errors (WPF head stays green; never modify its behavior)
   - Core tests → all green, never below the recorded floor (board; read the live count)
   - --smoke-test → 44 tabs, 0 unhandled errors, findings ⊆ the board's recorded benign drift set
   - --verify-layers / --verify-video when touching Compositor/ or video paths
   - --benchmark before/after on hot paths — not worse than docs/benchmark-optimized.json
5. Commit (conventional commits, minimal surgical diff, no TODOs/placeholders, tree clean). One board
   ledger row per commit; supersede stale rows in place with dated banners, never rewrite history.

TOKEN ECONOMY: follow the board's token-economy block. Driver context is the top cost lever — keep
intermediate results in workflow variables/files, not the driver conversation. Escalation ladder,
~80/20 cheap-to-expensive split, resume-never-re-run for journaled workflows. Cost follows decision
leverage, not volume.

HARD PROHIBITIONS: never edit SmokeTestRunner.cs; never loc-map the availablesubjects chips; never
change WPF-head behavior; no protocol/interface changes without a JUDGMENT review; privacy/security
posture never regresses (webcam frames never persist; enhancement validation stays; secrets stay in
ISecretStore).

STOP AND SURFACE (output "BLOCKED:" + context, keep tree clean, do not improvise):
- Product decisions not written on a row (e.g., row #1 per-region input-mask questions — re-read the
  row first; its chaos-run questions are mostly moot under the web-only ruling).
- Any gate failure unresolvable within the row's scope.
- Anything requiring a consent/version bump.

COMPLETION BAR: zero claimable OPEN/improvement rows remain for autonomous tiers (JUDGMENT rows
included when executable without product decisions); VERIFY/BLOCKED/DEFER rows are excluded from the
bar but enumerated in the final report with one-line statuses. Finish with a full-gate run and a final
board ledger entry summarizing the session.
```
