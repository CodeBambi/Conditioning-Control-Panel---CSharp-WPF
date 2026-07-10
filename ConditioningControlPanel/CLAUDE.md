# Conditioning Control Panel — Project Context (agent onboarding)

> **Read order for port work:** [`docs/docs-index.md`](docs/docs-index.md) → [`docs/skia-rebuild-goal.md`](docs/skia-rebuild-goal.md) → claim exactly ONE row on [`docs/avalonia-migration-task-board.md`](docs/avalonia-migration-task-board.md). The repo-root [`AGENTS.md`](../AGENTS.md) holds the canonical build/test/run commands and the full version-bump list; this file is quick orientation, not a duplicate of either.

## What this project is

A conditioning/hypnosis desktop app mid-migration from a Windows-only WPF/WinForms head to cross-platform
**Avalonia UI v12**. The contract is functional parity (see `docs/skia-rebuild-goal.md` — "functionality is
the contract, implementation is not"): every WPF feature must work end-to-end in the Avalonia heads on
Windows AND Linux, at least as fast and smooth as WPF.

## Heads (dual-head layout)

| Head | Role | Status |
|---|---|---|
| **`CCP.Core/`** | Portable core: models + platform-agnostic services + seam interfaces (`ISecretStore`, `IOverlaySurface`, `IVideoSurface`, `IAudioPlayer`, `IBrowserHost`, `IWallpaperProvider`, …). Single source of truth for models; referenced by every head. | Live |
| **`CCP.Avalonia/`** | Shared Avalonia UI (views, viewmodels, services, `Compositor/`, `AvatarTube/`, `Chaos/`). DI via `Microsoft.Extensions.DependencyInjection` (`ServiceCollectionExtensions.cs`). | Live (port target) |
| **`CCP.Avalonia.Desktop.{Windows,Linux,macOS}/`** | Per-OS desktop heads; each `Program.cs` overrides the seams it can implement natively. | Windows ~92%, Linux ~45% (see parity matrix) |
| **`CCP.Avalonia.Android/`** | Android head. | Out of port scope (builds stay green) |
| **Legacy WPF head** (`ConditioningControlPanel.csproj`, root `Services/` `Views/` `Models/`) | **Behavior reference ONLY. Never modify its behavior.** | Frozen reference |

> Do **not** put shared source inside the legacy WPF folder — put it in `CCP.Core` and reference it. The WPF
> `.csproj` excludes `CCP.*/` and `tests/`. The legacy `.sln` builds WPF only; use `CCP.Desktop.slnf` or the
> `.slnx` for Avalonia heads + tests.

## Build / test / run

See repo-root [`AGENTS.md`](../AGENTS.md) for the full set. Quick reference:

- Avalonia desktop: `dotnet build ConditioningControlPanel/CCP.Desktop.slnf -c Debug`; run `…/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj`.
- Core tests: `dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` (floor **542** — never decrease; read the live count).
- Legacy WPF (reference only): `dotnet build ConditioningControlPanel/ConditioningControlPanel.csproj`.
- **Gates block every commit:** `CCP.Desktop.slnf` 0 errors; WPF `.sln` 0 errors; Core tests green; `--smoke-test` at baseline (Findings 5); `--verify-layers` / `--verify-video` when touching compositor/video; `--benchmark` before/after on hot paths — not worse than `docs/benchmark-optimized.json`.

## How to work here (NOT the legacy WPF patterns)

- **Services are DI-resolved** in Avalonia (`ServiceCollectionExtensions.cs`), not static `App.Foo`
  properties. The `App.Flash` / `App.Video` / `App.Settings` static-accessor pattern is head-local to the
  legacy WPF reference head only; do not copy it into Avalonia code.
- **Skills are MANDATORY, not optional** — Avalonia v12 is 2026-new and LLM training data about it is stale
  or actively wrong. Always start with `avalonia-research` before any Avalonia API/dependency/unexplained
  exception; use `port-feature` for the implementation workflow + v12 cheatsheet; `wpf-parity` for behavior
  contracts; `unified-compositor-engine` + `overlay-clickthrough` for all media/input/overlay work;
  `dashboard-design` for user-facing surfaces (5-theme reskin is part of done); `mechanical-port-work` for
  small-tier rows; `port-audit` at workstream close-out. Definitions live in `.pi/skills/` (authoritative)
  with `.kimi-code/skills/` mirrors.
- **All real-time visuals render as `IAvaloniaLayer`s** in the one `CompositorEngine` (one topmost window
  per monitor, z-ordered layers, one 60Hz tick, PER-REGION click-through per the 2026-07-09 team review).
  No new per-effect windows, ever. Interactive surfaces (main UI, dialogs, AvatarTube, HUD, lock card) stay
  windows.
- **Acceptance gate:** a ported feature is accepted only when at least as fast and smooth as the WPF head —
  preferably measurably improved. Big changes are encouraged when they win on merit; what/why is recorded in
  the task board.

## Known scars (read before touching these areas)

- **Threading / timers:** UI-thread work uses the Dispatcher; some timers must be `DispatcherTimer`. See the
  threading notes in `CCP.Core/Services/Deeper/IActionDispatcher.cs` and `docs/crossplatform-rebuild-plan.md`
  §21 (v12 gotchas). When in doubt, consult the `avalonia-research` skill — do not guess v12 APIs.
- **Crash logging:** `logs/crash.log` is the first place to look. Global handlers (dispatcher /
  `AppDomain` / `TaskScheduler.UnobservedTaskException`) log full stack traces.
- **Privacy / security (never regress):** webcam frames never hit disk/network (only calibration
  coefficients persist); enhancement validation rejects NaN/Infinity/UNC/absolute paths/control chars;
  overlay capture-exclusion rules stay; secrets stay in the `ISecretStore` seam;
  `Microsoft.WindowsAppSDK` stays pinned. See `AI_AUDIT.md` for the endpoint/prompt audit (paths are
  WPF-era — a task-board row tracks refreshing them).

## Runtime data

- Settings: `%APPDATA%/ConditioningControlPanel/settings.json` (atomic temp-file + rename writes).
- Assets: `App.EffectiveAssetsPath` → `images/` and `videos/` subfolders (user-choosable; default
  `%APPDATA%/ConditioningControlPanel/assets`).
- Logs: `logs/crash.log`. Localization JSON is copied to output at build — rebuild to pick up edits.

## Version bumps

The canonical, always-current list of version locations is in repo-root [`AGENTS.md`](../AGENTS.md)
("Version bumps"). Use `/release X.Y.Z "Subtitle"` to automate it; this file intentionally does not keep a
second copy.
