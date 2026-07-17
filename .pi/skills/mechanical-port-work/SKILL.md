---
name: mechanical-port-work
description: "Execute a fully planned, no-judgment task for the greenfield client under client/. Use only when a client task-board row and reviewed /task spec already define exact scope, behavior evidence, Windows/Linux acceptance, verification, and stop conditions. Faithful execution only: if design, package, platform, privacy, or parity judgment appears, stop and escalate through port-plan and consult."
---

# mechanical-port-work

Execute an approved specification exactly. This skill is not allowed to design the client.

## Entry gate

Proceed only when all exist:

- an approved unblocked row in `client/docs/task-board.md`;
- linked owner decision and behavior contract;
- a reviewed `/task` spec with allowed files, exclusions, dependencies, VERIFY steps, and headed gates;
- clean or intentionally isolated working tree/worktree;
- no unresolved product/platform decision.

Otherwise stop and use `port-plan`.

## Boundaries

- Edit only the declared `client/` slice and required `client/docs/` tracker evidence.
- WPF and the first Avalonia attempt are read-only.
- Do not add packages/endpoints, change schemas/consent/privacy, invent abstractions, choose platform degradation, or alter architecture.
- Do not use localized labels as identity, silent no-ops as support, or method calls as behavioral proof.
- Never reset, clean, stash, revert, or commit unrelated work. Never bypass git hooks.
- Do not hardcode test counts, versions, benchmark numbers, dates, commit hashes, screen counts, or old project commands. Read current approved values dynamically.

## Execution loop

1. Mark the matching client task row `WIP` with the active scope.
2. Re-read the spec and cited contract/WPF evidence.
3. Implement only what is specified, following existing client conventions.
	For WPF-shaped syntax, use the current official WPF migration cheat sheet named by `port-feature`; never improvise from memory or translate triggers/windows mechanically.
4. Add the specified tests or verification instrumentation.
5. Run the exact fast VERIFY block. Fix only failures caused by the slice; do not substitute a slow whole-app smoke/layer run.
6. At the task's explicit close gate, run only the named targeted headed/visual states. Broad screenshot matrices belong to milestones/releases. A compile-only result cannot satisfy UI, rendering, focus, input, audio, animation, windows, or multi-monitor acceptance.
7. Run the pre-completion consultation required by the spec. Reconcile concrete findings; any new judgment is an escalation.
8. Update the task row with exact commands/results, headed evidence, deviations, and blockers.
9. Produce one scoped conventional commit only if the admitted workflow permits it.

## When to escalate

Stop when:

- the spec conflicts with architecture, WPF evidence, current v12 research, or code reality;
- any requirement is ambiguous;
- files outside scope are needed;
- Linux behavior cannot be proven;
- a new dependency, interop mechanism, endpoint, schema, or setting is needed;
- privacy, security, tint, focus, input, capture, or consent boundaries might change;
- a required gate fails twice for the same cause;
- the fix requires choosing among materially different approaches;
- advisor dissent exposes a real unresolved decision.

Record the blocker in the same `client/docs/task-board.md` row and leave unrelated work untouched. A precise blocker is a successful mechanical outcome.

## Reporting

Return: task row; files changed; behavior implemented; Windows evidence; Linux evidence; automated verification; headed verification; consult result and reconciliation; unresolved blockers; proposed commit message.

## Related skills

- `port-plan` for judgment and slicing.
- `port-feature` for full feature workflow.
- `wpf-parity` and `avalonia-research` when the approved evidence proves insufficient.
