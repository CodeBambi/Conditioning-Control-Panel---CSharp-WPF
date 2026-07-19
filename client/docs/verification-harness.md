# Tiered targeted verification harness

**Status:** active deliverable of task-board row 7 (SP-008). Replaces the rejected first-attempt whole-app smoke/layer strategy (`first-attempt-lessons.md`). Owner decisions applied: A-012 (targeted checks over blanket sweeps), A-014 (checks exist only with a real current consumer), `runtime-capability-contract.md` evidence-class discipline extended to test evidence.

## The four tiers

### Tier 1 — fast affected checks (never launches the app)

Build + unit tests + headless tests. Runs on every iteration; the only tier that runs unconditionally.

- **"Affected" is defined concretely by csproj path**, matching how the contract testCommand narrows:
  - `dotnet build client/CcpClient.sln -c Debug --nologo` — the solution build IS the affected-build check (the solution contains only projects that exist; there is no wider build to narrow).
  - `dotnet test client/tests/CcpClient.Tests/CcpClient.Tests.csproj -c Debug --nologo` — pure unit tests (no Avalonia runtime, no app launch). Assertion-logic unit tests for the tier-3 console tool live here.
  - `dotnet test client/tests/CcpClient.HeadlessTests/CcpClient.HeadlessTests.csproj -c Debug --nologo` — headless Avalonia tests (in-memory windowing/rendering; no real display, no app launch).
- Tier selection is by csproj path: a task touches only logic → run `CcpClient.Tests`; touches AXAML/visual tree → also run `CcpClient.HeadlessTests`. The full contract testCommand (all three commands) is the pre-`.DONE` gate.
- The headless tests live in a SEPARATE project because `[assembly: AvaloniaTestApplication]` is assembly-wide; putting them in `CcpClient.Tests` would force an Avalonia application onto the 85 landed unit tests.

### Tier 2 — targetable one-surface/state capture + headed actions (task close)

Thin scripts under `client/tools/verify/` formalizing the SP-007 headed-smoke patterns. Launch the real app, raise it (`SetWindowPos(HWND_TOPMOST)` on Windows — the app opens unactivated behind existing windows and pixel captures read the occluder, SP-007 surprise #2), read UIA/layout-probe facts, drive real input, capture ONE surface+state by name:

```
pwsh client/tools/verify/capture.ps1 -Surface dashboard -State lit
pwsh client/tools/verify/capture.ps1 -Surface dashboard -State unlit
```

- Windows: real window + `CopyFromScreen` crop to the layout-probe rect → PNG. (System.Drawing appears here ONLY as capture transport; scripts never read a pixel — SP-008 consult.)
- WSLg (Linux/X11): `XGetImage` via python3 ctypes against the app's X window → BMP/PNG. WSLg RAIL windows are invisible to Windows-side GDI capture (SP-007 surprise #3).
- Headed ACTIONS (right-click quick-toggle, keyboard, teardown) remain task-specific scripts building on the same helpers; the harness owns launch/raise/read/capture, the task owns its action sequence.
- Output: `client/artifacts/verify/<platform>-<surface>-<state>.png` (gitignored).

### Tier 3 — K3 image inspection driven by the named-check manifest

Deterministic named checks BEFORE/DESIDE model review: one cross-platform .NET console tool (`client/tools/verify/CcpVerify`) reads the manifest (`client/tools/verify/checks.json`), decodes a capture via `Avalonia.Media.Imaging.Bitmap` (no System.Drawing, zero new packages), evaluates each named check for the captured surface+state, and exits non-zero naming the FIRST failed check. K3 (`app-visual-verification` skill) then reviews the same capture against the same manifest — the manifest row is the shared contract between deterministic assertion and model review.

- Checks are scoped to REAL current consumers only: the SP-007 dashboard card lit/unlit states and the capability surface. No speculative checks for surfaces that do not exist (A-014). Each new surface adds its checks with its own task.

### Tier 4 — theme/language/platform matrices (named milestones/releases ONLY)

Broader matrices (five themes, languages, scaling levels, platforms) run ONLY at a named milestone or release gate. This task defines the hook and does NOT run matrices. Trigger: the board row for a milestone/release names the matrix; the capture tool's `-Surface`/`-State` parameterization plus the manifest's per-surface check list are the execution mechanism. No matrix automation exists beyond this hook (A-014).

## Evidence-class rule (hard)

Every check in the manifest declares an evidence class:

- **`draw-verified`** — the check asserts facts about the visual tree, styles, bindings, or Skia draw output. Headless tests (`CcpClient.HeadlessTests`) may satisfy ONLY draw-verified assertions: the headless platform replaces windowing AND rendering with in-memory backends (default: fake drawing, no pixels at all; with `UseHeadlessDrawing=false` + `.UseSkia()`: Skia draw output). A headless frame has NO compositor, NO real window, NO DPI/scaling, NO activation/occlusion, NO OS chrome.
- **`presentation-verified`** — the check asserts what a user sees on a real display: composited pixels, window geometry, scaling, occlusion, z-order. ONLY a headed Windows/WSLg capture (tier 2) can satisfy presentation-verified checks.

**A headed Windows/WSLg gate is NEVER dischargeable by a headless frame.** All current manifest checks against real captures are `presentation-verified`; `draw-verified` checks appear when a task adds headless-frame assertions (none exist yet — the headless spike asserts tree/layout/style/binding facts, not frames).

## Check manifest schema

`client/tools/verify/checks.json`:

```json
{
  "version": 1,
  "checks": [
    {
      "name": "dashboard-card-lit-border",
      "surface": "dashboard",
      "state": "lit",
      "evidenceClass": "presentation-verified",
      "kind": "border-color-band",
      "region": { "band": "top", "thicknessPx": 3 },
      "expectedColor": "#E066FF",
      "tolerance": 32,
      "minPixelCount": 50
    }
  ]
}
```

Fields:

- `name` — stable check identity; the console tool exits non-zero naming the first failed `name`. K3's review prompt cites the same names.
- `surface` / `state` — match `capture.ps1 -Surface/-State`. A check is evaluated only against captures of its own surface+state.
- `evidenceClass` — `draw-verified` | `presentation-verified` (rule above).
- `kind` — evaluation semantics: `border-color-band` (count pixels within `tolerance` of `expectedColor` inside an edge band of the capture) | `region-color` (same count inside a fractional rect of the capture).
- `region` — **capture-relative, never absolute pixels** (captures differ across platforms: Windows scale 1.0, WSLg scale 1.5, card 77 vs 71 DIP — SP-007 measured font-metric delta). Either `{ "band": "top|bottom|left|right", "thicknessPx": N }` (first N pixel rows/columns of the capture) or `{ "rect": { "x": 0.0, "y": 0.0, "w": 1.0, "h": 0.05 } }` (fractions of capture width/height).
- `expectedColor` — `#RRGGBB`; `tolerance` — per-channel absolute delta; `minPixelCount` — pass criterion: matching pixels >= this.

## Seeded-regression self-test

Re-runnable proof that the targeted gate catches real regressions (throwaway-edit pattern, SP-007 AVLN2000 precedent; NO defect-injection flags in product code):

```
pwsh client/tools/verify/self-test.ps1
```

Sequence: (1) edit the REAL `MainWindow.axaml` — break the lit border brush (`#E066FF` → wrong value); (2) build; (3) capture `dashboard`/`lit`; (4) assert the SPECIFIC named check `dashboard-card-lit-border` fails with its name in the output; (5) restore the AXAML (git checkout); (6) re-capture, assert green. A self-test pass requires BOTH the seeded failure AND the restored green.

## Runtime budgets (measured, never invented)

Measured 2026-07-19 (Step 4); budget = observed + headroom. Cold = clean `bin/obj` + first run; incremental = no source change since last build.

| Tier | Windows cold | Windows incremental | WSL2 cold | WSL2 incremental | Budget |
|---|---|---|---|---|---|
| Tier 1 (build + both test projects) | — | — | — | — | — |
| Tier 2 (launch + capture one state) | — | — | — | — | — |
| Tier 3 (console assert one capture) | — | — | — | — | — |
| Self-test (full cycle) | — | — | — | — | — |

(to be measured in Step 4 — cells are honest blanks until then)
