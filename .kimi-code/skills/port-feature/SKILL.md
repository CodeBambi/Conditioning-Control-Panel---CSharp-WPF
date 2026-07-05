---
name: port-feature
description: "End-to-end implementation workflow for porting a WPF feature to Avalonia v12 or building/changing any UI or service in CCP.Avalonia and CCP.Core. Use this skill for ANY implementation task on the Avalonia port: porting a control, dialog, tab, service, or overlay; fixing a ported behavior; adding a new feature to the port. It chains research, WPF archaeology, seam design, the WPF-to-v12 conversion cheatsheet, verification, and tracker updates. If you are about to edit .axaml or a CCP.Avalonia service, you should be in this workflow."
---

# port-feature

The workflow is: understand (research + archaeology) -> plan the slice -> implement -> prove it running -> record. Skipping the first or last step is how the port shipped inert UI once already.

## 1. Understand

- **v12 first**: anything Avalonia-API-shaped goes through the `avalonia-research` skill (local gotcha list, then current web sources). Never write from v11/WPF memory.
- **WPF behavior contract**: extract what the feature actually does via the `wpf-parity` skill (settings, triggers, visuals, input model, multi-monitor, events). The WPF head stays runnable precisely so you can observe it.

## 2. Plan the slice

Non-trivial tasks go through `port-plan` (trackers, claims, chokepoints, seam design). Minimum bar even for small tasks: know where the code lives (Core vs Avalonia vs head), what DI changes are needed, and how you will verify it running.

## 3. Implement: the WPF -> Avalonia v12 conversion cheatsheet

Every entry here is a bug someone already fixed once. Apply them proactively:

**Bindings and XAML**
- Compiled bindings are ON by default (`AvaloniaUseCompiledBindingsByDefault=true`): every `.axaml` needs `x:DataType`; genuinely dynamic paths need `{ReflectionBinding ...}`.
- `ElementName=foo` -> `{Binding #foo.Prop}`; `RelativeSource AncestorType=T` -> `{Binding $parent[T].Prop}`.
- Properties consumed by ElementName-style bindings (for example a `Capabilities` object on the control) must be assigned BEFORE `InitializeComponent()`, or bindings evaluate against null and never update (the Webcam "unavailable" badge bug; same fix applied in Visuals/System/LockCard feature controls).
- `DynamicResource` cannot feed CLR converter properties; use `StaticResource` there (AvatarTubeWindow fix).
- No inline `Hyperlink`: use `HyperlinkButton` inside `InlineUIContainer`.
- Theme accents bind via `DynamicResource` theme keys ONLY, never literal hex (see `dashboard-design`).

**Code-behind and controls**
- `DependencyProperty` -> `StyledProperty`/`DirectProperty`.
- WPF `Preview*` mouse events -> pointer tunnel events; the left-button check does NOT come for free anymore, re-add it from the pointer event args.
- Window chrome: `WindowDecorations` (v12 rename, not `SystemDecorations`); `TransparencyLevelHint` is `IReadOnlyList<WindowTransparencyLevel>` (`new[] { WindowTransparencyLevel.Transparent }`).
- Guard `PlatformImpl != null` before touching possibly-closed windows.
- Popups/toasts: `ShowActivated = false` (+ `SWP_NOACTIVATE` where Win32 applies) or they steal focus.

**Screens and monitors**
- Screen-spanning windows: `Position` takes raw screen PIXELS, `Width`/`Height` take DIPs (divide by scaling); `ScreenWindowHelper.ConstrainToScreen` exists for this.
- Iterate `Screens.All`, never index `[0]`/`[1]`; react to `Screens.Changed`.

**Services and DI**
- Registration lives in `CCP.Avalonia/ServiceCollectionExtensions.cs` (a swarm chokepoint: if working in a lane, hand the DI line to the orchestrator via the task board Hand-off Queue instead of editing).
- Per-head overrides rely on last-registration-wins; do not reorder registrations casually.
- Effect services take `CompositorEngine` as a nullable dependency; without it their layer never exists and they silently no-op. If your feature draws fullscreen effects, it belongs in the compositor (see `unified-compositor-engine`), not a new window. If it needs a real window with click-through, see `overlay-clickthrough`.

**Wiring rule (the inert-UI lesson)**
Every control must reach a live handler or `ICommand` that calls a real Core/platform service, and you must exercise it in the running app. A button that compiles is not a button that works.

**Localization**
New user-visible strings: add keys to `ConditioningControlPanel/tools/new-localization-keys.json` and run `python ConditioningControlPanel/tools/merge-localization-keys.py` (paths from repo root). Never hand-edit `ConditioningControlPanel/Localization/Languages/*.json` directly in parallel sessions. Raw `{loc:...}` strings visible in the UI are a smoke-test failure.

## 4. Prove it running

```bash
# from repo root; check git status first - parallel WIP may already be in the tree
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -clp:ErrorsOnly   # must be 0 errors
dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj
# WPF reference for side-by-side:
dotnet run --project ConditioningControlPanel/ConditioningControlPanel.csproj
```

Verification ladder (stop only when all apply):
1. Build 0 errors, all Core tests green (the count must never decrease).
2. `--smoke-test` (Debug builds only; catches crashes, raw loc keys, placeholder tabs; does NOT prove behavior).
3. Exercise the feature end-to-end in the running Avalonia head, side by side with WPF.
4. Theme sweep: switch all 5 mods (CCP Default, Bambi Sleep, Sissy Hypno, Dronification, Circe's Lock) and confirm the surface re-skins live.
5. Multi-monitor if the feature draws on screen.
6. Perf sanity: no new lag vs WPF; heavier-but-working is a defect.

## 5. Record

- Update `avalonia-ui-parity-matrix.md` (only after step 4's side-by-side) and the task-board row (done/blocked + evidence).
- New v12 facts learned -> plan doc section 21 (per `avalonia-research` step 3).
- Commit: `feat(av): <subject>` / `fix(av): <subject>`, one task per commit, tree green.

## Related skills

- `avalonia-research`, `wpf-parity`, `port-plan` - the inputs to this workflow
- `unified-compositor-engine`, `overlay-clickthrough`, `dashboard-design` - domain guides for effects, overlay input, and UI look
- `port-audit` - run periodically after a batch of features
