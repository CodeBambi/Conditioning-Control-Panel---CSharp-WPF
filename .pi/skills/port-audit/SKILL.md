---
name: port-audit
description: "Audit the health, drift, safety, and verified capability of the greenfield Windows/Linux client under client/. Use after milestones, batches, merges, before release, when docs and code may disagree, or when behavior feels broken. Checks the sole client task board, contracts, architecture, Windows/Linux evidence, first-attempt contamination, privacy, performance, and files new rows instead of trusting old port claims."
---

# port-audit

An audit asks: what exists, what is proven, what drifted, what is unsafe, and what work follows.

## Scope and authority

Audit `client/` and `client/docs/`. WPF is behavior evidence; `ConditioningControlPanel/CCP.*` is historical lessons only. Do not import old parity percentages, checkboxes, test floors, benchmark numbers, packages, or completion claims.

Read `architecture.md`, `capability-inventory.md`, `first-attempt-lessons.md`, `task-board.md`, and `port-workflow.md`. `client/docs/task-board.md` is the only live queue.

## Audit ladder

### 1. Tree and reproducibility

- Inspect `git status`, recent relevant history, project files, package locks, and generated/untracked state.
- Do not modify or revert unrelated work.
- Verify pinned development extensions and ignored generated state when orchestration is in scope.

### 2. Build and automated tests

Discover current client commands from its project files/docs. Start with builds, tests, analyzers, and cheap headless checks. Do not run a legacy-style whole-app smoke/layer/screenshot crawl by default: it is slow and historically missed visible bugs. Run broader app traversal only for a named milestone/release audit with an approved matrix. Report actual outputs; never embed permanent counts.

### 3. Queue integrity

For every task-board claim:

- compare status with code, tests, headed evidence, and blockers;
- reject `DONE` without the row's Windows/Linux acceptance;
- detect implementation rows that lack contracts, stable scope, dependencies, or verification;
- reconcile `.pi-tasks` recovery state only as local evidence, never authority.

### 4. Contract and architecture conformance

Map implemented features to capability/contract sections. Check owner decisions, explicit exclusions, failure behavior, persistence, input/focus/window semantics, multi-monitor behavior, theme behavior, audio, rendered animation, and teardown. Flag copied WPF mechanics or first-attempt topology that was not approved.

### 5. First-attempt contamination

Search for inherited `CCP.Avalonia` classes/interfaces, old plans/trackers, fixed layer values, stale version pins, copied completion comments, old test floors, and Windows-only fallbacks. A useful first-attempt idea must be traceable to an `ACCEPT` or `ADAPT` lesson.

### 6. Windows evidence

Exercise affected capabilities end to end. For UI, verify real pointer/keyboard interaction, focus, ownership, taskbar/Alt-Tab, resize, scrolling, right-click, animation frames, audio, overlays, capture, monitor layouts, failure injection, and cleanup as required. Compare against WPF behavior where the contract preserves it.

Use `app-visual-verification` for targeted screenshots of recent/high-risk/suspicious surfaces reviewed by `kimi-coding/k3`. Run the broader theme/language/scaling/window matrix only at a named milestone or release gate. Do not infer interaction from still images.

### 7. Linux evidence

Run on supported distributions/backends and name them. Distinguish X11 and Wayland/window manager/compositor when relevant. Compilation, an external-browser fallback, disabled feature, or no-op does not count as support. File exact capability gaps as product decisions.

### 8. Security and privacy

Check camera data remains memory-only, secrets use approved storage, sensitive URLs/headers/content are not logged, paths are validated, capture policy matches contracts, consent is not broadened, local HTTP servers bind/protect correctly, and advisor/task transcripts contain no sensitive data.

### 9. Rendering, media, and performance

Check deterministic ordering, tint below full opacity, one-decoder video contract, actual displayed-frame progress, no orphan windows/audio, and abnormal teardown. Compare fresh measurements only against an approved client baseline read dynamically. If no baseline exists, establish evidence without inventing one.

### 10. Documentation drift

Fix factual documentation drift. Every new defect, missing proof, or capability gap becomes a prioritized row in `client/docs/task-board.md` with acceptance and blocker. Do not hide findings in the audit report.

## Consultation gate

Use council review for release verdicts and findings involving architecture, security/privacy, packages, browser/media, shared composition, input/windowing, or cross-platform degradation. Supply primary evidence and inspect the fit ledger. Consensus is advisory; empirical evidence and owner decisions win.

## Report

```text
# Greenfield client audit
## Verdict: green / yellow / red and why
## Scope and environment
## Build and automated tests
## Windows headed evidence
## Linux headed evidence
## Contract and architecture drift
## First-attempt contamination
## Security and privacy
## Rendering/media/performance
## Documentation fixed
## Task-board rows added or changed
## Council verdict, dissent, and evidence reconciliation
## Release blockers
```

The audit is incomplete until findings are represented in the client task board.

## Related skills

- `wpf-parity`, `avalonia-research`, `port-plan`, `dashboard-design`, `app-visual-verification`, `overlay-clickthrough`, `unified-compositor-engine`.
