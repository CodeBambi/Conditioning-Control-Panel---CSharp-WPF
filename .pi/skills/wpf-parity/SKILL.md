---
name: wpf-parity
description: "Extract user-observable behavior from the legacy WPF product for the greenfield Windows/Linux client. Use whenever asking what a feature does, defining a contract, investigating a parity gap, reviewing WPF changes, or comparing behavior. Ports the idea and outcome rather than WPF classes or internals, records narrow evidence in client docs, and treats Linux as a first-class separately verified target."
---

# wpf-parity

## Purpose

WPF is the primary evidence for existing Windows product behavior. Extract capability and observable intent, not implementation. Owner-approved greenfield decisions may intentionally supersede WPF; record those differences explicitly.

## The bar

- Windows must satisfy the approved behavior contract.
- Linux is first-class and needs independent evidence. Never claim support from compilation or a no-op.
- Preserve timings/order only when user-observable or safety-relevant.
- Presence of code, XAML, handlers, tests, or assets is not completion evidence.
- First-attempt Avalonia code cannot establish WPF behavior and is never an implementation template.

## Archaeology workflow

1. Define the user capability or exact unresolved question.
2. Search settings declarations and all relevant consumers; do not assume an old shared-model path is architectural.
3. Inspect UI entry points, gestures, commands, help, locks, and accessibility paths.
4. Follow the narrow execution path needed to establish triggers and outcomes.
5. Record visible/audible behavior, timing/order, state transitions, persistence, live-service effects, failures, retries, cancellation, and teardown.
6. For windows/overlays record owner, modality, activation, focus restoration, taskbar/Alt-Tab, topmost, move/resize, click-through/interactivity, capture policy, and monitor scope.
7. Inspect validation, consent, privacy, secret/path handling, and logging.
8. Search all call sites for event ordering and edge cases.
9. Cite focused `file:line-range` evidence. For files over 100KB, grep first and read only meaningful ranges.
10. Separate requirements from mechanics:
   - **Behavior:** what the user observes or relies on.
   - **Mechanic:** WPF window, timer, static service, library, event chain, or rendering API. Mechanics are not port requirements unless independently approved.
11. Label findings `VERIFIED`, `INFERRED`, `UNVERIFIED`, or `PRODUCT DECISION REQUIRED`.
12. Check `client/docs/architecture.md` for decisions that replace old behavior or mechanism.

## Translate only after the contract exists

Once observable behavior is recorded, use the current official [WPF migration guide](https://docs.avaloniaui.net/docs/migration/wpf/) and [cheat sheet](https://docs.avaloniaui.net/docs/migration/wpf/cheat-sheet) to identify Avalonia concepts. Do not translate while still discovering behavior; otherwise WPF mechanics silently become requirements.

Common current mappings to verify in the deeper docs before implementation:

- `DependencyProperty` -> `StyledProperty` when styling/animation/inheritance is required; `DirectProperty` only for appropriate non-styled/performance-sensitive state.
- WPF triggers/VisualStateManager -> selectors, classes, pseudo-classes, bindings, or converters; Avalonia does not use WPF style triggers.
- `ElementName` -> `#name`; ancestor binding -> `$parent[Type]`; templated parent -> `TemplateBinding` where suitable.
- `Preview*` events -> tunnel routing; mouse-specific events -> pointer events with explicit button checks.
- `RoutedCommand`/`CommandBinding` -> direct `ICommand` bindings.
- `HierarchicalDataTemplate` -> `TreeDataTemplate`; templates belong in `DataTemplates` where appropriate.
- `Dispatcher.Invoke/BeginInvoke` -> `Dispatcher.UIThread.InvokeAsync/Post` with asynchronous semantics.
- Storyboards/`CompositionTarget.Rendering` -> Avalonia animations/transitions or `TopLevel.RequestAnimationFrame`; render-thread work requires separate current research.
- WPF transparent windows do not imply click-through in Avalonia.
- `WindowStyle`/`ResizeMode` -> current Avalonia decorations and resize properties, while preserving the window behavior contract.
- `DropShadowEffect` -> often `BoxShadow`; `LayoutTransform` -> `LayoutTransformControl`; `pack://` -> `avares://`.
- screen APIs -> `TopLevel.Screens`, with physical bounds/scaling semantics verified for each use.

These are candidate translations, not permission to copy entire XAML files.

## Contract shape

Capture:

- purpose and entry points;
- settings/inputs and valid ranges;
- triggers and preconditions;
- visible/audible outcome;
- interaction, focus, ownership, and window behavior;
- timing and event ordering;
- multi-monitor/orientation/scaling behavior;
- persistence and migration;
- success, failure, cancellation, and teardown;
- privacy/security/capture constraints;
- Windows acceptance;
- Linux acceptance or exact capability gap;
- narrow evidence and unresolved product decisions.

## Where to record

- `client/docs/capability-inventory.md`: capability summary and evidence.
- `client/docs/architecture.md`: approved intentional differences/mechanisms.
- `client/docs/first-attempt-lessons.md`: first-attempt lesson only.
- `client/docs/task-board.md`: sole live queue, blockers, and verification.
- future `client/docs/contracts/<feature>.md`: detailed contract when created.

Docs never outrank code evidence or owner decisions. Fix drift rather than carrying contradictions forward.

## WPF changes during the port

Treat a WPF diff as new behavioral evidence. Determine whether it changes user-visible behavior, persistence, safety, privacy, or acceptance. Update the relevant client contract/task. Do not merge WPF or first-attempt source into `client/` by default.

## Use focused git history for non-trivial archaeology

For a substantial feature, inspect history for the narrow WPF and first-attempt paths after reading current code. Search later commits for fixes, reverts, re-openings, races, leaks, crashes, stubs, unwired work, and deletions. History often reveals hidden edge behavior and invalidated completion claims. A commit subject is a lead, not proof: inspect the relevant diff/final code and record only behavior or lessons that survive verification. Cite decisive commit IDs in task research; never copy historical architecture merely because it once shipped.

## Consultation

Use council after focused archaeology for disputed parity, platform degradation, privacy/security, overlay/input/window behavior, or high-impact contract decisions. Include client decision links, narrow WPF citations, alternatives, and Windows/Linux consequences. Advisor consensus cannot establish behavior; source and headed observation do.

## Verification

Run WPF and the client side by side when possible. Exercise the complete user path, not just rendering. Compare state, timing, interaction, focus, audio, windows, monitors, persistence, failure, and teardown. Record exact evidence in the matching client task row.

## Related skills

- `port-plan`, `port-feature`, `avalonia-research`, `dashboard-design`, `overlay-clickthrough`.
