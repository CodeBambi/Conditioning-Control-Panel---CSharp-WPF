# Architecture proposal — greenfield client topology

**Date:** 2026-07-18 · **Task:** SP-002 (task-board row 1) · **Status:** proposal, pending owner review

This document instantiates the owner decisions A-001…A-014 in [`architecture.md`](architecture.md) into concrete project topology, a package baseline, and a composition-root shape under `client/`. It decides nothing new. Gaps the decisions cannot resolve are flagged as owner questions (§5) or deferred to a named Phase 1 row (§6). Research inputs and current Avalonia v12 facts: [`row-1-research-inputs.md`](row-1-research-inputs.md) (official sources, accessed 2026-07-18).

## 1. Project topology

```
client/
├── CcpClient.sln
├── src/
│   └── CcpClient.Desktop/          # single executable desktop head (Windows + Linux)
│       ├── CcpClient.Desktop.csproj
│       ├── Program.cs              # entry point, composition root
│       ├── App.axaml(.cs)          # Application, lifetime hook
│       └── Views/MainWindow.axaml(.cs)  # placeholder window
└── tests/
    └── CcpClient.Tests/            # xunit v3
        └── CcpClient.Tests.csproj
```

- **One desktop head for both platforms.** The first attempt's per-OS head projects (`CCP.Avalonia.Desktop.Windows/.Linux/.macOS`) existed to host native seam implementations; per-OS heads are **not** recreated. Trigger condition for splitting a head: the first real platform seam with a consumer (e.g., a `[DllImport]`-backed window-chrome or single-instance implementation required by a feature contract). This follows A-014's YAGNI constraint and A-001's rule that capture/host boundaries are decided from feature contracts, not copied from the first attempt. Disposition of the first-attempt topology lesson: **REJECT** per-OS heads by default (`first-attempt-systemic-lessons.md` — capability-by-registration patterns lived in those heads).
- **No `CcpClient.Core` portable library yet.** Proposed future home for portable rules/persistence/startup contracts, created only when rows 2–5 produce its first real consumer. Until then the test project references the `CcpClient.Desktop` project directly (supported on modern .NET), so rows 2–4 have a landing spot for tested code before Core exists. Disposition: **ADAPT** the "unwired foundation" lesson — infrastructure lands only with its consumer (`first-attempt-systemic-lessons.md` — "'Unwired but verified' is not a shippable intermediate state").
- **Tests** live under `client/tests/`, mirroring the repo's existing `tests/` convention, xunit v3 per the v12 headless-testing requirement and repo precedent.
- The legacy `ConditioningControlPanel/` tree (WPF + first attempt) is untouched read-only evidence (constitution hard rule).

## 2. Package baseline

Admitted for the scaffold (empirically resolved from live feeds in SP-001, 2026-07-18):

| Package | Version | Why |
|---|---|---|
| `Avalonia` | 12.1.0 | Latest stable; 12.1 released 2026-07-08 |
| `Avalonia.Desktop` | 12.1.0 | Windows + Linux desktop backends |
| `Avalonia.Themes.Fluent` | 12.1.0 | Theme |
| `xunit.v3` (+ runner) | latest stable | v12 headless testing requires xUnit v3 |

Target framework: **net10.0** (Avalonia v12 recommendation; required for any future mobile head; SDK 10.0.302 verified on this machine and available for Ubuntu 26.04).

**Deliberate delta from the SP-001 template output:** the generated template csproj also referenced `Avalonia.Fonts.Inter` and `AvaloniaUI.DiagnosticsSupport 2.2.3`. Both are dropped — no consumer exists yet (YAGNI). `UsePlatformDetect()` configures text shaping transparently, so no explicit `Avalonia.HarfBuzz` is needed unless a later requirement forces explicit `UseSkia()` (row-1-research-inputs §1, breaking changes).

**Flagged candidates — not admitted** (each gated on its own board row's spike):

- `Avalonia.Controls.WebView` 12.0.1 — DTRH host candidate (A-002); admission requires the WebView spike on Windows/X11/Wayland.
- LibVLCSharp 3.10.0 — video/audio backend candidate (A-003, A-009); admission requires the video-geometry and audio-backend spikes.
- `Avalonia.Wayland` — native Wayland backend, experimental in 12.1, opt-in with no automatic fallback (owner question §5.1).
- `Avalonia.Headless.XUnit` — candidate for the tiered verification harness (row 7).
- `CommunityToolkit.Mvvm` — deferred to the first real view-model; repo precedent exists but the scaffold has no consumer.

## 3. Composition-root shape

```
Program.Main
  → AppBuilder.Configure<App>()
       .UsePlatformDetect()
       .StartWithClassicDesktopLifetime(args)
App.OnFrameworkInitializationCompleted
  → assigns MainWindow (placeholder)
```

- **Explicit manual construction only.** No DI container, no static `App.Services` locator, no constructor side effects, no background work started from constructors. The scaffold constructs the window directly; **container admission is row 2's decision**, not this proposal's. Disposition: **REJECT** global service location and static bridge wiring (`first-attempt-systemic-lessons.md` — "Startup order and hidden globals became architecture").
- **Lifetime shape is provisional.** `StartWithClassicDesktopLifetime` suffices for the scaffold. Row 2 (startup/shutdown contract) may replace it with the manual `Start(AppMain, args)` + `app.Run(cts.Token)` path if the settings-flush-before-disposal ordering or a `TerminateProcess`-analogue requirement demands it (row-1-research-inputs §4 Q7/Q8).
- The ordered, cancellable startup-phase state machine, composition-root validation, typed initialization failures, and per-participant ownership are **row 2's deliverables**; this scaffold only proves the entry shape compiles and runs.

## 4. Decision-by-decision instantiation

| Decision | Structural consequence in this proposal | Lesson disposition |
|---|---|---|
| **A-001** Unified composition domain | None in scaffold. Composition host is a vertical-slice deliverable; the single-head topology does not pre-judge one-host vs multi-host capture affinity. Rendering primitive deferred per A-001 (Skia lease API is unstable in 12.1). | ACCEPT unified-composition principle; REJECT copying first-attempt compositor classes/timers/z-values (`first-attempt-lessons.md` §unified-composition). Tint-below-100% safety and rendered-opacity verification noted as slice acceptance, not scaffold scope. |
| **A-002** DTRH hosted web product | None in scaffold. `Avalonia.Controls.WebView` flagged candidate; payload copy and loopback origin await the WebView spike row. | ADAPT (copy payload, preserve protocol; REJECT WebView2 host abstraction, classic fallback, assets-present-means-supported). |
| **A-003** Single-decode video fan-out | None in scaffold. LibVLCSharp flagged candidate; geometry/handoff spikes gate. | ADAPT shared-frame UCE ordering; REJECT browser fullscreen/capture mirroring and per-monitor decoders. |
| **A-004** Stable feature identities, one command path | None in scaffold. Shapes the future dashboard card design (stable IDs, no localized-title dispatch). | REJECT localized-title dispatch and right-click context-menu substitution; ADAPT WPF interaction outcomes. |
| **A-005** Window semantics precede chrome | Placeholder window carries no chrome decisions. Per-window manifest row gates any shared shell. | REJECT blanket window styling/lifecycle; ADAPT per-window contracts. |
| **A-006** Avatar rendered-frame validation | None in scaffold. Animation scheduling research (`TopLevel.RequestAnimationFrame` vs timers) deferred to the AvatarTube row. | REJECT code-level liveness claims; ADAPT WPF visual outcomes. |
| **A-007** Pi task orchestration | **Superseded** — owner replaced `@mjasnikovs/pi-task` with `pi-spine` 2026-07-18 (task-board gate history). Cited, not instantiated. | — |
| **A-008** Multi-model consultation | Active: this proposal passed a pre-approach solo consult (Fable 5); a pre-completion solo consult gates SP-002's close. Council remains off pending the probe row. | ACCEPT evidence-aware gates; consultation remains advisory. |
| **A-009** Explicit audio channels | None in scaffold. Backend spike row governs. | REJECT one generic replace-on-play player; ADAPT explicit channel ownership. |
| **A-010** Provider-neutral AI boundary | None in scaffold. AI contract row governs. | ADAPT typed outcomes/cancellation/strict commands; REJECT lenient repair into effect authority. |
| **A-011** Local deep-learning camera | None in scaffold. Provenance/capture/accuracy spike rows govern. | ACCEPT local inference direction; REJECT Windows-only, silent fallback, unverified provenance. |
| **A-012** Official migration baseline | This proposal cites the 2026 migration guide/cheat-sheet via row-1-research-inputs; rows translate WPF behavior through it. | ACCEPT guide/cheat-sheet; ADAPT expert-guide methodology; REJECT literal structure preservation. |
| **A-013** Avalonia MCP advisory | Admitted by owner 2026-07-18 as advisory only (board). Not used for this scaffold; 11.3.1-pinned heuristics treated skeptically against the 12.1.0 baseline. | ADAPT bounded redacted review; never authority. |
| **A-014** Explicit foundation | The topology embodies it: single head, deferred Core, no speculative seams, no locator. The six foundation contracts are rows 2–9 (§6). | ACCEPT all eight required foundation gates; implement only with a consumer. |

Systemic-lesson dispositions not tied to one A-###: capability-by-probe (row 5), persistence-as-transaction (row 4), lifecycle ownership (row 3), UI-thread ownership (row 3), one asset manifest (row 8), Release/publish as separate gates (row 9), git-history archaeology in every feature task (workflow rule) — all **ACCEPT**ed as row scopes, none instantiated in the scaffold.

## 5. Flagged owner questions

Only questions A-001…A-014 genuinely cannot resolve and that are not a named row's deliverable. Existing board "Decisions needed" entries are cross-referenced, not duplicated.

1. **Wayland opt-in policy.** `UsePlatformDetect()` yields X11 (XWayland under Wayland desktops) by default; the 12.1 native Wayland backend is experimental, opt-in via `Avalonia.Wayland` + `.UseWayland()`, with **no automatic fallback** if no compositor is present. Options: (a) X11-only for Milestone 1; (b) conditional opt-in on `WAYLAND_DISPLAY` from day one. Board already asks which backends are release requirements — this narrows it to a policy choice the scaffold build matrix needs.
2. **Is WSL2 Ubuntu 26.04 (WSLg) a release target or only the dev/CI Linux environment?** Affects whether WSLg quirks (X11/Wayland hybrid, no real multi-monitor, portal sandboxing) gate acceptance. Board's distro question remains open alongside.
3. **Is single-instance a product requirement for the new client?** WPF's mutex + ack handshake is Windows-specific; the requirement (vs mechanism) needs owner confirmation before row 2 designs a cross-platform replacement.

## 6. Deferred to named rows (not owner questions)

| Topic | Owning row |
|---|---|
| DI container admission, composition-root validation, startup-phase machine, lifetime shape, splash/crash-sentinel/hang-watchdog triage, single-instance mechanism, shutdown ordering (incl. `TerminateProcess` analogue) | Row 2 — startup/shutdown/integration contract |
| Serializer choice (Newtonsoft per-member tolerance vs `System.Text.Json`), atomic write/debounce/backup/quarantine parity | Row 4 — persistence and migration contract |
| Typed capability states and runtime probes | Row 5 — runtime capability contract |
| Version authority location (`Directory.Build.props` vs csproj), publish strategy (self-contained single-file vs Native AOT given future LibVLC/OpenCV/ONNX native deps) | Row 9 — Release and publish gates |
| `Avalonia.Headless.XUnit` admission, targeted visual verification harness | Row 7 — verification harness |
| Asset/localization manifest | Row 8 — asset and packaged-output manifest |
| Dispatcher discipline, cancellation generations, out-of-order completion policy | Row 3 — async lifecycle and fault policy |

## 7. Proposed spine testing commands

For `.spine/spine-config.json` (orchestrator applies at land time; workers never edit `.spine/`):

- `testing.build`: `dotnet build client/CcpClient.sln -c Debug --nologo`
- `testing.test`: `dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo`
