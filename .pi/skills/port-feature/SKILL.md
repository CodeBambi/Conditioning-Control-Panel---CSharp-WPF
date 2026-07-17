---
name: port-feature
description: "Implement or change one approved feature in the greenfield Windows/Linux Avalonia client under client/. Use for controls, dialogs, windows, services, overlays, media, AvatarTube, dashboard behavior, and bug fixes. Requires a client task-board row, WPF behavior archaeology, current Avalonia v12 research, consultation, task-specific Windows/Linux verification, and durable evidence. Never ports the first Avalonia attempt literally."
---

# port-feature

Workflow: understand, plan, consult, implement, prove, review, record.

## 1. Confirm authority and scope

- Read the matching row in `client/docs/task-board.md` and linked sections of `architecture.md` and `capability-inventory.md`.
- Read `client/docs/port-workflow.md` for consultation, task orchestration, model routing, commit, and stop rules.
- Product changes stay under `client/` unless documentation-only tracker updates are required.
- WPF is behavioral evidence. The first Avalonia attempt is lessons only.
- If the row is blocked, lacks observable acceptance, or conflicts with architecture, stop and update the board rather than coding.

## 2. Understand behavior

Use `wpf-parity` to capture inputs/settings, triggers, visible/audible outcome, timing/order when observable, input/focus/window behavior, monitor scope, persistence, failure/teardown, safety, and privacy. Cite focused WPF ranges.

Check `first-attempt-lessons.md` for an applicable `ACCEPT`, `ADAPT`, or `REJECT` disposition. Do not copy first-attempt classes, services, DI, renderer, timers, or package choices merely because they exist.

Also read `first-attempt-systemic-lessons.md`. For non-trivial work, inspect focused git history for the affected WPF and first-attempt paths. Later fixes, reverts, re-openings, crash/leak/race repairs, unwired slices, and deletions can expose missing acceptance. Verify commit leads against final code.

## 3. Verify current technology

Use `avalonia-research` before writing Avalonia/AXAML/windowing/rendering/input code or adding a package. Prefer official v12 docs/API, current releases, recent issues/PRs, and measured spikes. Package admission requires version, license, Windows/Linux support, runtime dependencies, maintenance, necessity, and an approved architecture consequence.

For WPF-shaped UI work, read the current official [migration guide](https://docs.avaloniaui.net/docs/migration/wpf/) and [cheat sheet](https://docs.avaloniaui.net/docs/migration/wpf/cheat-sheet) before editing. The primary 2026 differences are styling/selectors, data-template placement, the strongly typed property system, pointer/routed events, commands, animations, assets, windows, and screen services.

If the Pi Avalonia MCP is admitted and available, use its XAML validation and applicable diagnostic, accessibility, or performance tools as an advisory pass on the smallest relevant snippet. Do not invoke its project, architecture, WPF conversion, custom-control, theme, animation/storyboard, service, data, authentication, or test generators for production code. The upstream knowledge and templates target Avalonia 11.3.1 and can confidently emit invalid v12 or WPF syntax. Redact sensitive content and follow the telemetry gate in `avalonia-research`.

## 4. Plan and consult

Use `port-plan` for non-trivial slices. Before substantive work, run the consultation required by `port-workflow.md` after gathering primary evidence. Council is required for platform seams, dependencies, security/privacy, composition, windowing, input, browser, or capability degradation. Advice is advisory and must be reconciled with evidence.

## 5. Implement the smallest solution

- Implement the observable contract, not WPF internals.
- Prefer standard framework behavior and direct code over speculative abstractions.
- Keep portable rules separate from real platform boundaries, but do not create interfaces before two implementations or a genuine boundary exist.
- Use stable feature identity, not localized text or visual position, for commands.
- Bound scrollable content with a finite viewport; verify wheel/touch/keyboard/thumb behavior.
- Define each window's owner, modality, activation/focus, taskbar/Alt-Tab, topmost, resize, placement, chrome, and close/reuse behavior from its contract.
- For multi-monitor work, enumerate all screens; handle negative coordinates, mixed scaling, orientation, hot-plug, X11, and Wayland as applicable.
- Shared composition is a behavior principle, not permission to copy first-attempt UCE internals.
- No user action is complete merely because a handler ran: service side effects, audible cues, displayed frames, focus, and persistence need observable proof.
- Keep rendered tint below full opacity in every lifecycle state.
- Never broaden webcam, secret, path, logging, capture, or consent boundaries.
- Registration, an OS check, copied assets, a unit test, or a non-throwing fallback cannot claim product support. Prove one composition-root-to-user-outcome path.
- Long-running work needs an owner, cancellation, awaitable completion, typed terminal outcome, and idempotent teardown. Do not detach required work.
- Required operations cannot turn exceptions into apparent success. Classify recoverable, degraded, cancelled, and fatal outcomes.

### Current WPF-to-Avalonia conversion guardrails

- `.xaml` -> `.axaml`; use Avalonia's XAML namespace and verify `using:`/`clr-namespace:` resolution.
- Choose `StyledProperty` only when styling/animation/inheritance is needed; use `DirectProperty` for appropriate non-styled state, not as a mechanical replacement.
- Replace WPF triggers and `VisualStateManager` logic with selectors, classes, pseudo-classes, bindings, converters, and Avalonia animations. Do not recreate a trigger framework.
- Keep data templates in the appropriate `DataTemplates` collection and use `TreeDataTemplate` for hierarchy.
- Use `#element`, `$self`, `$parent[Type]`, and `TemplateBinding` according to the current binding docs; verify default binding mode rather than assuming WPF behavior.
- Convert mouse/preview events to pointer events and tunnel routing; explicitly check button/update kind and preserve `Handled` semantics.
- Replace routed commands/command bindings with direct `ICommand` bindings and stable feature identity.
- Treat dispatcher conversion as asynchronous: prefer `InvokeAsync`/`Post`, never block the UI waiting on work that can await.
- Use Avalonia animations/transitions or frame scheduling rather than timer-based WPF animation translation where appropriate.
- `TransparencyLevelHint="Transparent"` does not give WPF layered-window click-through. Use the overlay/window behavior contract and current platform research.
- Preserve `Visible`, `Collapsed`, and `Hidden` layout intent: `IsVisible=false` removes layout; use another mechanism when space must remain.
- Replace pack URIs with verified Avalonia asset URIs; keep asset lookup testable and case-correct across Windows/Linux.
- DataGrid and missing controls require package/control admission, not automatic dependency addition. Rich text has no built-in direct equivalent.
- Use `TopLevel.Screens`; verify physical coordinates, working area, scaling, and orientation rather than copying WPF screen math.

The cheat sheet maps concepts; it does not prove the converted control behaves correctly. Every mapping still passes the feature contract and target-platform gates.

## 6. Prove the feature

Discover build/test commands from the approved client project when it exists; do not reuse old solution commands automatically.

Verification is task-specific and tiered:

1. **Every iteration:** smallest affected build/unit/headless tests and `git diff --check`; no whole-app launch by default.
2. **Before closing the slice:** one targeted headed path on each claimed platform, plus focused screenshots only when pixels changed or look suspicious.
3. **Milestone/release/shared-infrastructure only:** broader theme/language/window/platform matrices and any whole-client sweep explicitly named by the task board.

Run WPF side by side only for the affected behavior. Run multi-monitor, focus, input, audio, animation, rendered-frame, failure, privacy, and performance evidence only when the contract requires it.

For user-facing surfaces, use `app-visual-verification` at its targeted checkpoint and have exact model `kimi-coding/k3` inspect appearance. Do not run a whole-app screenshot crawl for routine edits. Screenshots supplement, never replace, headed interaction gates.

For changed AXAML, retain a short summary of the advisory MCP findings in task evidence, classify each relevant finding as accepted or rejected, then run the real Avalonia compiler. An MCP "validation passed" result means only that its heuristic checks passed.

Compilation, markup presence, timer ticks, method calls, copied assets, or a no-op fallback do not prove behavior. If a required headed gate cannot run, leave the task `WIP` or `BLOCKED`.

## 7. Review and record

Run the pre-completion consult specified by `port-workflow.md`; use council for P0/high-risk work. Inspect fit-ledger clipping and reconcile advice with tests and primary sources.

Update `client/docs/task-board.md` with exact evidence or blocker. Update architecture, capability inventory, or first-attempt lessons only when facts/decisions changed. One slice, one conventional commit, clean tree. Never bypass hooks or commit unrelated files.

## Stop conditions

Stop for conflicting authority, ambiguous behavior, unapproved package/endpoint/schema, unknown Linux equivalence, privacy/safety expansion, repeated verification failure, unrelated dirty-tree changes, or a task that has grown beyond one verifiable slice.

## Related skills

- `port-plan`, `wpf-parity`, `avalonia-research`, `dashboard-design`, `app-visual-verification`, `overlay-clickthrough`, `unified-compositor-engine`, `port-audit`.
