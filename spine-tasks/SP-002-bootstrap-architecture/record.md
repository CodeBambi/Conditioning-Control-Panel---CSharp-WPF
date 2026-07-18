# SP-002 — Bootstrap discovery and architecture proposal — record

**Date:** 2026-07-18 · **Board row:** 1 (P0) · **Outcome:** proposal + scaffold delivered; row set to `WIP` pending owner review (never `DONE` — owner flips it)

## Proposal summary

`client/docs/architecture-proposal.md` instantiates A-001…A-014 without deciding anything new:

- **Topology:** single desktop head `client/src/CcpClient.Desktop` (Windows + Linux) — first-attempt per-OS heads REJECTED (capability-by-registration lesson); no `CcpClient.Core` library until rows 2–5 produce its first consumer (unwired-foundation lesson ADAPTED); tests under `client/tests/` (xunit v3, repo convention).
- **Package baseline:** Avalonia 12.1.0 / `Avalonia.Desktop` / `Avalonia.Themes.Fluent` on **net10.0** + xunit v3. `Avalonia.Fonts.Inter` and DiagnosticsSupport dropped from the SP-001 template output (no consumer). WebView 12.0.1, LibVLCSharp 3.10.0, `Avalonia.Wayland`, `Avalonia.Headless.XUnit`, CommunityToolkit.Mvvm flagged as candidates only — each gated on its own board row.
- **Composition root:** `Program.Main → AppBuilder.Configure<App>().UsePlatformDetect().StartWithClassicDesktopLifetime(args)`; manual construction, no DI container, no static locator, no constructor side effects. Container admission is row 2's decision.
- **§4:** per-A-### instantiation table with ACCEPT/ADAPT/REJECT lesson dispositions.
- **§5 flagged owner questions:** (1) Wayland opt-in policy (X11 default vs conditional `.UseWayland()`, no automatic fallback); (2) is WSL2 WSLg a release target or dev/CI-only; (3) is single-instance a product requirement (WPF mechanism is Windows-only).
- **§6:** deferred topics mapped to rows 2–9 (DI/lifetime → row 2, serializer → row 4, capability probes → row 5, version/publish → row 9, headless-xunit → row 7, asset manifest → row 8, dispatcher/cancellation → row 3).

## Consult verdicts

- **Pre-approach (solo Fable 5):** ran during the Step 1 session (the `ae3c7dd8` STATUS.md checkbox attests it). **Gap (honest record):** no verdict content survives — the verbatim verdict was not persisted to record.md before that session ended, and the STATUS checkbox claimed record.md persistence prematurely. Recurrence of the SP-001 recorded gap (state claimed before durable write); already covered by the 2026-07-18 port-lessons entry on explicit STATUS/checkbox discipline. Note: the packet amendment in PROMPT.md (worker does not edit `.spine/`; WSL2 attempt as Step 3) came from the **packet-authoring** consult recorded in PROMPT.md's own Amendments section (committed at `e235fa7e`, before Step 1) — a separate event from the Step 1 worker consult.
- **Pre-completion (solo Fable 5):** see §Pre-completion consult below (run on the committed diff + proposal before .DONE).

## Build outputs

**Windows (worktree lane):**

```
dotnet build client/CcpClient.sln -c Debug --nologo
Build succeeded.  0 Warning(s)  0 Error(s)   (~5 s)
dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo
Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

**WSL2 Ubuntu 26.04 (Step 3):**

- Distro is registered as `Ubuntu` (not `Ubuntu-26.04`); `PRETTY_NAME="Ubuntu 26.04 LTS"`. No dotnet present initially.
- Installed **dotnet-sdk-10.0 → SDK 10.0.110** from the native Ubuntu 26.04 apt feed via `wsl -u root` (default user has no passwordless sudo — named environment note, not a gate).
- Scaffold copied to WSL filesystem (`~/ccp-sp002/client`) to avoid clobbering Windows `obj/` artifacts, then:

```
dotnet build CcpClient.sln -c Debug --nologo
Build succeeded.  0 Warning(s)  0 Error(s)   (7.1 s)
dotnet test tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo
Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

- Avalonia 12.1.0 restored from nuget.org inside WSL without issue. Runtime (headed) launch under WSLg was **not** attempted — out of scope for row 1; belongs to the first-visible-slice row's Windows/X11/Wayland evidence.

## Scaffold file list (new, all under `client/`)

- `client/CcpClient.sln`
- `client/README.md`
- `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj` (net10.0, Avalonia 12.1.0, 0 warnings as errors-clean)
- `client/src/CcpClient.Desktop/Program.cs` (entry point / composition root)
- `client/src/CcpClient.Desktop/App.axaml`, `App.axaml.cs`
- `client/src/CcpClient.Desktop/Views/MainWindow.axaml(.cs)` (placeholder window)
- `client/tests/CcpClient.Tests/CcpClient.Tests.csproj` (xunit v3)
- `client/tests/CcpClient.Tests/CompositionRootTests.cs` (one passing test proving the harness runs)
- `client/docs/architecture-proposal.md`

## Proposed spine testing commands (for `.spine/spine-config.json`; orchestrator applies at land time — workers never edit `.spine/`)

- `testing.build`: `dotnet build client/CcpClient.sln -c Debug --nologo`
- `testing.test`: `dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo`

## Surprises

1. **WSL distro name vs version:** the 26.04 distro registers as plain `Ubuntu`; scripts/docs must check `/etc/os-release`, not the distro name.
2. **No passwordless sudo in WSL:** package installation needs `wsl -u root` from the host or an interactive password; recorded for the Linux-environment setup docs.
3. **Ubuntu 26.04 apt carries dotnet-sdk-10.0 natively** (10.0.110) — no Microsoft feed or install script needed.
4. **Pre-approach consult verdict not persisted** before the Step 1 session ended (see Consult verdicts gap above).
5. No restore/build friction from Avalonia 12.1.0 on Linux — the scaffold is genuinely cross-compiling on both targets already.

## Pre-completion consult

**Solo Fable 5, 2026-07-18, on the committed diff + proposal. Verdict: NO BLOCKER** — the package is fit to close SP-002 and to author Phase 1 rows 2–9 against the proposal.

- **Correction applied (caught a false causal claim in this record):** the first draft attributed the PROMPT.md amendment to the Step 1 worker consult; commit order (`e235fa7e` packet authoring precedes `ae3c7dd8` Step 1) proves it was the packet-authoring consult. Fixed above; the advisor noted the conflation would read as fabricated verdict reconstruction — worse than the honest gap.
- Both named gaps confirmed non-blocking: lost verbatim pre-approach verdict (honest record is the correct disposition; no post-hoc re-run) and no headed WSLg launch (Step 3 requires a build attempt only; headed evidence belongs to the first-visible-slice row).
- Proposal judged sound for rows 2–9: every contested decision deferred to a named row, nothing admitted beyond Avalonia 12.1.0 + xunit, the three owner questions are genuine A-001…A-014 gaps.
- Advisor's close-out checklist (applied): record.md correction; STATUS.md Step 1 checkbox reconciliation; board row 1 → `WIP` citing this record; one-line port-lessons entry on the WSL environment facts (distro name, native apt SDK, `wsl -u root`); then Step 5 verification.
