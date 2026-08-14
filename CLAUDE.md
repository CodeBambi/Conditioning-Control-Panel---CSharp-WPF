# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Approach
- Read existing files before writing. Don't re-read unless changed.
- Thorough in reasoning, concise in output.
- Skip files over 100KB unless required.
- No sycophantic openers or closing fluff.
- No emojis or em-dashes.
- Do not guess APIs, versions, flags, commit SHAs, or package names. Verify by reading code or docs before asserting.

## Three code trees in one repo

Identify which tree a task belongs to before touching anything. They have different target frameworks, different solutions, and different rules.

| Tree | What it is | TFM / UI | Solution |
|------|-----------|----------|----------|
| `ConditioningControlPanel/` (excluding `CCP.*`) | The shipping product. WPF, Windows-only, v6.8.0 | `net8.0-windows10.0.19041.0`, WPF + WinForms | `ConditioningControlPanel.sln` (app + `Tests/`) |
| `ConditioningControlPanel/CCP.*` | First cross-platform port attempt. Core + Avalonia UI + per-OS heads | `net8.0`, Avalonia 12.1.0 | `ConditioningControlPanel/CCP.Desktop.slnf` |
| `client/` | Greenfield Avalonia rewrite (Windows + Linux), version 0.1.0 | `net10.0`, Avalonia 12.1.1 | `client/CcpClient.sln` |

Both .NET 8 and .NET 10 SDKs are needed to build everything.

For greenfield port work (the `feat/crossplatform` branch, spine packets), `docs/constitution.md` makes the first two trees read-only evidence: the WPF app is behavioral evidence, `CCP.*` is failure/lessons evidence only. Never import `CCP.*` classes, interfaces, timers, or DI topology into `client/`. Ordinary maintenance of the shipping WPF app is separate work and is not bound by that rule.

## Commands

### Legacy WPF app

```bash
dotnet build ConditioningControlPanel.sln
```
```bash
dotnet run --project ConditioningControlPanel/ConditioningControlPanel.csproj
```
```bash
dotnet test Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
```
Single test or class (xunit.v3 through VSTest):
```bash
dotnet test Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj --filter "FullyQualifiedName~AwarenessScorerTests"
```
`CONTRIBUTING.md` still says the project has no automated tests. That is stale; the test project above is real and large.

Installer: `build-installer.bat` publishes self-contained win-x64, then pauses twice for manual Actalis code signing, then compiles `installer.iss` with Inno Setup 6. Its `VERSION` must match the csproj version.

### CCP.* Avalonia attempt

One command validates a change across both Windows trees:
```bash
./ConditioningControlPanel/tools/run-gates.sh
```
Four gates: `CCP.Desktop.slnf` build, `ConditioningControlPanel.sln` build, `CCP.Core.Tests` with a floor of 550 passing, and the Windows head smoke run (expects exactly 44 tabs, 5 findings, 0 unhandled exceptions). `--fast` skips the smoke gate. A fifth gate runs automatically when the diff touches `CCP.Avalonia/AvatarTube/**` or `TubeGeometry*`. The usage comment inside the script says `./Tools/run-gates.sh`; the real path is the one above.

```bash
dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj
```
```bash
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -c Debug -- --smoke-test
```
Linux bring-up runs `ConditioningControlPanel/build-linux.sh` on a Linux box (it installs deps, builds Core + the Linux head, runs Core tests, smoke-runs the head). CI mirror: `.github/workflows/linux-smoke.yml`, currently `continue-on-error: true` and therefore diagnostic, not a gate.

### Greenfield client

Tier 1, runs on every iteration:
```bash
dotnet build client/CcpClient.sln -c Debug --nologo
```
```bash
dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo
```
```bash
dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo
```
The two test projects are separate because `[assembly: AvaloniaTestApplication]` is assembly-wide; keep pure logic tests out of the headless project.

The mechanical suite floor (the real pre-land gate) runs both projects with TRX and compares against the name-anchored pin in `client/tests/floor/floor.json`:
```bash
node client/tests/floor/check-floor.mjs
```
Bump `total` in the same commit as the test change that moves it. `allowedSkips` is not a quarantine list; a test may be listed only when its precondition is a property of the machine or OS. Never export `CCP_DATA_ROOT` process-wide: it makes the SP-057 pin skip and the floor goes blind.

Headed verification (tier 2 capture, tier 3 deterministic check, seeded-regression self-test):
```bash
pwsh client/tools/verify/capture.ps1 -Surface dashboard-card -State lit
```
```bash
dotnet run --project client/tools/verify/CcpVerify/CcpVerify.csproj -- --capture <png|bmp> --surface dashboard-card --state lit
```
```bash
pwsh client/tools/verify/self-test.ps1
```
Publish:
```bash
pwsh client/tools/publish/publish.ps1 -Rid win-x64
```

## Architecture

### Legacy WPF

`App.xaml.cs` is the composition root and a static service locator: services are reached as `App.Flash`, `App.Video`, `App.Audio`, `App.Patreon`, settings as `App.Settings.Current`, content as `App.EffectiveAssetsPath`, user data under `%LOCALAPPDATA%/ConditioningControlPanel/`. `ConditioningControlPanel/CLAUDE.md` holds the detailed map: version locations that must move together for a release, service/model inventory, startup order, Patreon and update flows, and the accumulated WPF/threading/localization pitfalls. Read that file before working in this tree rather than rediscovering it.

### CCP.* port attempt

`CCP.Core` (net8.0, no WPF) owns models, portable services, and roughly forty `Platform/I*.cs` capability interfaces (`IFrameSource`, `IOverlaySurface`, `IBrowserHost`, `ISecretStore`, `ITrayIcon`, `IForegroundWindowTitleProvider`, and so on). Implementations live per host: `CCP.WindowsOnly` supplies the WPF-backed set for the legacy shell, and `CCP.Avalonia.Desktop.{Windows,Linux,macOS}` supply the Avalonia heads over the shared `CCP.Avalonia` UI. Adding a platform capability means interface in `CCP.Core/Platform`, one implementation per head, registration in that head's service-collection extension. Linux and macOS backends are specified in `ConditioningControlPanel/docs/*-contract.md` before implementation.

### Greenfield client

One product project (`client/src/CcpClient.Desktop`) with a constructor-injected composition root: `App` and `MainWindow` deliberately have no parameterless constructor and are never built by the runtime XAML loader (AVLN3001 is suppressed for this reason). `client/Directory.Build.props` is the single version authority and also stops the MSBuild props walk from reaching repo root. Web payloads (`dtrh`, `intake`, `tunnel`, `vendor`) are linked read-only out of the legacy tree by csproj globs and copied to `payload/`; the bytes stay owned by the legacy tree and are never forked into `client/`.

Contracts are documents in `client/docs/*.md` and several are enforced by tests that read those documents at runtime (`UpstreamPayloadInventoryTests`, `AiOperationContractTests`, `VersionDerivationTests`). Editing such a doc can turn the suite red, so re-run those guards after any doc or JSON edit that lands late in a change.

## Port governance

Read these before doing greenfield work; the rules are not derivable from the code.

- `docs/constitution.md` is the standing order set. Authority order, descending: owner decisions in `client/docs/architecture.md` and `client/docs/capability-inventory.md`, then `client/docs/task-board.md`, then repository instruction files, then skills, then the spine packet, then advisors.
- `client/docs/task-board.md` is the only live queue. `spine-tasks/SP-*/` and `.spine/` are local execution state and never substitute for the board.
- Work executes as task packets (`spine-tasks/SP-*/PROMPT.md`) run by `port-slice-executor` subagents in git worktrees, base branch `feat/crossplatform`, one task and one commit per slice. `client/docs/port-workflow.md` documents the execution model, advisory gates, review levels, and stop conditions. pi-spine was retired 2026-08-14; `.pi/` and `.spine/` are frozen history.
- Verification is evidence-classed. `draw-verified` claims may be satisfied by headless Avalonia frames; `presentation-verified` claims (composited pixels, geometry, scaling, occlusion, z-order) require a real headed Windows or WSLg capture. A headed gate is never dischargeable by a headless frame. See `client/docs/verification-harness.md`.
- No new wall-clock waits in tests. Use the shared `TestWait` helper (`client/tests/CcpClient.Tests/TestWait.cs`, linked into the headless project). `Thread.Sleep`, bare `Task.Delay`, and `DateTime`/`Environment.TickCount64` polls fail the timing guard.
- A capability is not supported because it compiles. A stub, a no-op fallback, or a Windows-only test never proves cross-platform support; the honest outcome is a `WIP`/`BLOCKED` row naming the exact manual gate.

## Conventions

- Conventional commits (`feat:`, `fix:`, `docs:`, `refactor:`).
- Localization files (`ConditioningControlPanel/Localization/Languages/*.json`, 9 languages) must use escaped `\n`, never a literal line break inside a string, and are LF in git. Do not commit whole-file line-ending diffs.
- Other reference docs: `GUIDE.md` (feature walkthrough), `MODDING.md`, `AI_AUDIT.md`, `ConditioningControlPanel/docs/` (subsystem plans and platform contracts), `client/docs/port-digest.md` (owner-facing landing log).
