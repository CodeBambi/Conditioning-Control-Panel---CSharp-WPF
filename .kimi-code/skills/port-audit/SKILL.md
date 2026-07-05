---
name: port-audit
description: "Health and drift audit for the Avalonia port. Use this skill when asked to audit, review, or check the state/health/status of the port; after merging main into feat/crossplatform; after a batch of feature work or a UCE milestone; before a release or publish; or when something 'feels off' (perf regressions, features silently broken, docs not matching code). Produces a structured report and fresh task-board rows, not ad-hoc observations."
---

# port-audit

An audit answers four questions: does it build and pass, does it still behave like WPF, has anything drifted (code vs docs, perf vs baseline, security posture), and what new work fell out. Run the ladder top to bottom; each rung is cheap until the manual ones.

## 1. Tree and build gate

```bash
git -C E:/Code/Conditioning-Control-Panel status --short   # parallel WIP awareness FIRST
git -C E:/Code/Conditioning-Control-Panel log --oneline -15
dotnet build ConditioningControlPanel/CCP.Desktop.slnf -clp:ErrorsOnly   # must be 0 errors
dotnet test ConditioningControlPanel/tests/CCP.Core.Tests/CCP.Core.Tests.csproj   # all green; count must not decrease
```

A red build is not necessarily yours: check whether untracked/modified files from a parallel session explain it before touching anything (it has happened; for example an in-progress webcam service broke the slnf mid-audit). Never revert someone's WIP.

## 2. Smoke sweep (Windows head, Debug only)

```bash
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -- --smoke-test
```

Catches crashes, raw `{loc:...}` strings, placeholder tabs, dead dialogs across ~44 tabs / ~34 dialogs / 12 feature popups / 5-theme reskin. The harness is `Compile Include`d only in Debug config (`tests/CCP.Avalonia.Desktop.Windows.Smoke/`, no csproj; the flag is inert in Release). Remember its limit: it does NOT prove behavior, only that surfaces open without exceptions.

## 3. Stub and gap hunt

```bash
grep -rinE "TODO|stub|not ported|not wired|placeholder|NotImplemented|No-?op" ConditioningControlPanel/CCP.Avalonia --include=*.cs
```

Floor, not ceiling. Add spot-checks: pick 2-3 features and exercise them side by side with the WPF head (`wpf-parity` has the method). Prioritize recently merged or recently ported areas.

## 4. Doc drift check

Compare the trackers against reality; the docs are updated in batches and DO drift (both directions):

- `crossplatform-rebuild-plan.md` section 1A vs actual code/git log.
- `unified-compositor-engine-plan.md` current-state table vs the compositor code (a known instance: the doc claimed `WS_EX_LAYERED` was removed while `CompositorWindow` deliberately keeps it; code wins).
- `skia-rebuild-goal.md` "Current state" is the active driver's status block and DOES drift as work lands (its 2026-07-04 snapshot lagged the video Phase E / chaos S5–S9 landings until refreshed 2026-07-05); trust its commit-hash evidence over prose. (The former `EXECUTION_GOAL.md`, long superseded and materially stale, was deleted in the 2026-07-05 docs cleanup.)
- `avalonia-ui-parity-matrix.md`: OWNER RULING 2026-07-02: pre-existing `[x]` marks are void (hand-made port, no trusted baseline); rows are re-earned with evidence under `skia-rebuild-goal.md` WS0. During audits, only trust rows carrying WS0-era evidence; flip anything else back to `[ ]`.

Fix the docs as part of the audit; stale trackers poison every future session.

## 5. Perf gate

```bash
dotnet run --project ConditioningControlPanel/CCP.Avalonia.Desktop.Windows/CCP.Avalonia.Desktop.Windows.csproj -- --benchmark
# heavy stress (3 min): -- --max-benchmark
```

Compare against `ConditioningControlPanel/docs/benchmark-optimized.json` (startup ~1977ms, working set ~561MB at 10s, idle ~124 FPS, active ~185 FPS, max-intensity avg ~178 FPS) and the targets in `benchmark-baseline.json` (effect FPS floor 30, aim 60). Also sanity-check vs WPF: Avalonia currently beats WPF on startup (~2.5s vs ~4.2s) and memory (~422MB vs ~1218MB); a port that got heavier than WPF is a defect. `perf-baseline.ps1` automates the A/B. Update the benchmark JSON if you establish a new legitimate baseline.

## 6. Security and privacy posture (must never regress silently)

- Webcam/gaze frames are never written to disk or sent over the network; processing stays in-memory.
- Deeper-enhancement input validation intact (NaN, Infinity, UNC paths, control characters, bounds).
- No UNC or extended-length paths accepted for `--play`/`--edit` CLI arguments.
- Screen-capture exclusion (`WDA_EXCLUDEFROMCAPTURE`) still applied on keyword-highlight overlays in both heads (`KeywordHighlightService` / `AvaloniaKeywordHighlightService`) and on WPF brain-drain windows (self-capture avoidance). Subliminal cards are DELIBERATELY left in capture (`WDA_NONE`, see the comment in WPF `SubliminalService`) so they appear in the user's recordings; their absence from exclusion is not a regression, do not "fix" it.
- Secrets: tokens still go through the secret-store seam (DPAPI on Windows), never plaintext files.

If a diff touched any of these areas, read it, do not assume.

## 7. Cross-head and packaging spot checks

- CI: `ConditioningControlPanel/.github/workflows/build.yml` documents the intended matrix but is NOT active (it sits below repo root, so GitHub never triggers it). Do not claim "CI is green" from it. If off-Windows verification matters for this audit, use the Linux VM flow: `ConditioningControlPanel/docs/linux-vm-testing.md` + `build-linux.sh` (VM GL workarounds: `AVALONIA_D3D11_DISABLED=1`, `LIBGL_ALWAYS_SOFTWARE=1`).
- Version constants live in multiple places and are known to diverge (as of 2026-07: WPF head 6.2.5 in `ConditioningControlPanel.csproj` + `UpdateService.cs` `AppVersion` vs 6.2.2 across the CCP.Core/CCP.Avalonia/CCP.WindowsOnly csprojs). Grep `<Version>` and `AppVersion` fresh each audit and flag mismatches; do not trust these example numbers.
- `Microsoft.WindowsAppSDK` pins still present (`ExcludeAssets="all"`) in CCP.Avalonia + Linux/macOS heads; LibVLC package versions unchanged unless intentional.

## Report format

```
# Port audit YYYY-MM-DD
## Verdict: green / yellow / red (one line why)
## Build & tests: ...
## Smoke: ...
## Behavior spot-checks: <feature>: pass/fail vs WPF
## Doc drift found & fixed: ...
## Perf: numbers vs baseline
## Security posture: intact / findings
## New task-board rows filed: ...
```

Every finding becomes a task-board row (`avalonia-migration-task-board.md`) with priority; the audit is not done until the rows exist. Update tracker checkboxes you invalidated or satisfied.

## Related skills

- `wpf-parity` - side-by-side behavior verification method
- `port-plan` - turning findings into claimed, sequenced work
- `unified-compositor-engine` - compositor-specific validation and FPS expectations
- `avalonia-research` - if an audit finding needs a v12 explanation
