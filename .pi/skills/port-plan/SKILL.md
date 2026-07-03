---
name: port-plan
description: "Plan, sequence, and coordinate Avalonia port work before writing code. Use this skill at the START of any non-trivial CCP port task: choosing what to work on next, slicing work into committable steps, designing a Core/Avalonia seam, coordinating parallel agents or worktrees, claiming work items, or deciding where new code should live (CCP.Core vs CCP.Avalonia vs a platform head). Also use it when the user says 'plan', 'what next', 'continue the port', or starts a multi-file task."
---

# port-plan

## Step 1: Read the trackers before deciding anything

Ground truth lives in `ConditioningControlPanel/docs/`, in this order:

1. `avalonia-migration-task-board.md` - open rows, Known Functional Gaps, Current Sprint Focus. (Very long single-line ledger rows: Read in <=45-line slices.)
2. `unified-compositor-engine-plan.md` - the UCE effort's ONLY status tracker (the master plan does not track it). If the task is compositor/video related, this is the queue.
3. `crossplatform-rebuild-plan.md` section 1A - phase-level status snapshot.
4. `avalonia-ui-parity-matrix.md` - what is verified vs not.

Two cautions:
- **Docs lag code.** Check `git log --oneline -15` for work that postdates the trackers before trusting a checkbox. Trust code over docs, then fix the doc.
- **Check `git status` first.** Parallel sessions/agents leave WIP in the tree; a red build may not be yours.

## Step 2: Pick the work

Priority order as of the docs: P0 task-board gaps (currently none open), then UCE phases A-E (video parity is the blocker), then sync-from-main deferrals, then mobile/packaging (Android AAB, signing/notarization, touch UX), then P2+ improvements (for example gap M, effect FPS tooling). Cross-platform click-through (Linux/macOS) is explicitly out of scope until Windows parity holds.

## Step 3: Slice into sessions and commits

- One work item = one claim = one conventional commit (`feat(av): ...`, `fix(av): ...`, one task per commit). Every commit leaves the slnf building with 0 errors.
- Slice so each step is independently verifiable in the running app. "Wire the layer" and "make it render" are separate commits if each can be proven.
- Trackers are external memory: write progress into the docs as you go, not just the transcript. Compact context after every finished item, green build, or large file read, and unconditionally at ~50-60% of the window; before compacting, write the in-flight state (task, next step, files touched, re-verify commands) into the task-board row. Full rules: "Context discipline" in `docs/skia-rebuild-goal.md`.

## Step 4: Design the seam before the code

Where code lives:

- **`CCP.Core`**: portable logic, models, service interfaces (`CCP.Core/Platform/` has ~35 seam interfaces). No Avalonia UI types (no controls, windows, views); Core DOES reference the base Avalonia package and uses `Avalonia.Threading` (`Dispatcher.UIThread`, `DispatcherTimer`) directly - a deliberate ponytail-audit outcome. Do not re-wrap dispatching.
- **`CCP.Avalonia`**: shared UI + Avalonia implementations + DI defaults.
- **`CCP.Avalonia.Desktop` / per-OS heads**: platform overrides only.

The DI pattern (registration file: `CCP.Avalonia/ServiceCollectionExtensions.cs`, `ConfigureCoreServices()`):
every seam gets a safe fallback registration in shared DI, specialized per head via
`App.ConfigurePlatformServices` (Windows head: `CCP.Avalonia.Desktop.Windows/Program.cs`) or
`DesktopServiceCollectionExtensions`. **Last registration wins; order matters.** Mobile branches on
`OperatingSystem.IsAndroid()`.

Known intentional exceptions (do not "fix"):
- `IVideoSurface` is NOT DI-registered (needs a VideoView at construction; consumers construct directly).
- `IOverlaySurface` is registered `AddTransient` (one window per consumer).

**The ponytail bar (YAGNI):** a prior audit deleted needless wrappers; do not recreate these phantom seams: `IUiDispatcher`, `IScheduler`, `IAppLogger`, `LibVLCNativeDiscovery` wrapper, `ICaptureService` (folded into `IFrameSource`), `IImageDecoder`/`IImageSourceFactory` (folded into `IAssetLoader`), `IUiTimer` (replaced by framework APIs). `IThumbnailProvider` is different: it was deferred, never created; treat it as a possible future seam and create it only if it clears the bar below. A new abstraction must earn its existence: two real implementations or a real platform boundary. New NuGet deps must earn their weight, be pinned, and be recorded with reasons in the task board. Web-research faster/lighter alternatives before adding anything (see `avalonia-research`).

## Step 5: Multi-agent coordination (when parallelizing)

The swarm protocol from `crossplatform-rebuild-plan.md` section 20, condensed:

- **Lanes** (safe to parallelize): one tab view + VM, one dialog cluster, one feature-control cluster, one Core service area, a Chaos sub-split, AvatarTube, one platform head. One git worktree per porter, wave size 3-6.
- **Chokepoints** (orchestrator-only; porters NEVER edit): `CCP.Avalonia/ServiceCollectionExtensions.cs`, `CCP.Core/Models/AppSettings.cs` and other 4000-line shared files, `App.axaml(.cs)`, all `*.csproj`/`.slnx`/`.slnf`, the MainWindow shell, `Localization/Languages/*.json`, main-branch syncs.
- **Claims**: append-only ledger rows in `avalonia-migration-task-board.md`, committed BEFORE work starts (claim commit). Markers: todo / wip @agentN / review / done / blocked.
- **Hand-off Queue**: porters put needed chokepoint changes (DI lines, csproj assets, loc keys) in the task board's Hand-off Queue for the orchestrator; they do not apply them.
- **Localization**: new keys go in `ConditioningControlPanel/tools/new-localization-keys.json`, merged via `python ConditioningControlPanel/tools/merge-localization-keys.py` (paths from repo root). Never hand-edit the language JSON files in parallel sessions.

## Step 6: Define done before starting

Write down, per item: the WPF behavior contract it must match (get it via `wpf-parity`), the verification you will run (build, Core tests, smoke test, side-by-side exercise, theme sweep, multi-monitor), and which tracker rows/checkboxes you will update. If you cannot state how you will prove it works while running, the slice is wrong.

## Related skills

- `wpf-parity` - extract the behavior contract and keep trackers honest
- `port-feature` - the implementation workflow for each planned slice
- `avalonia-research` - research mandate for any v12 API or new dependency
- `unified-compositor-engine` - if the slice touches the compositor
