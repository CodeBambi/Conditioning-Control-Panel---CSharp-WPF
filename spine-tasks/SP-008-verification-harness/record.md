# SP-008 — build tiered targeted verification harness: record

**Task:** task-board row 7 (P0). **Worker:** kimi-coding/k3. **Date:** 2026-07-19.

---

## Pre-approach consult (solo Fable 5, 2026-07-19)

Full planned design submitted (four tiers, draw/presentation evidence-class rule, manifest schema, headless admission spike shape, throwaway-edit self-test, measured budgets). Verdict received complete (no truncation): **design sound — proceed, with corrections (all applied below).**

1. **Headless frame semantics (correction to the evidence-class wording):** in Avalonia v12 headless, actual rendered frames require `UseHeadlessDrawing = false` plus the Skia backend; the planned spike assertions (classes/pseudo-classes, arranged bounds, binding resolution) do NOT need rendered frames, which is the safer claim. The `UseHeadlessDrawing` requirement must be verified during Step-1 v12 research and the harness doc must state frame semantics precisely — headless assertions remain draw-level ONLY either way.
2. **System.Drawing ban nuance (applied):** the ban covers pixel/geometry/assertion logic; raw screen capture on Windows has no portable managed alternative to `CopyFromScreen`, so the PowerShell capture script may use System.Drawing ONLY as capture transport (screen → PNG file). All decode/pixel reads/geometry live in the cross-platform .NET console tool (`Avalonia.Media.Imaging.Bitmap`). SP-007's `Count-BorderPixels` (PS `GetPixel` loop) is exactly the logic that MOVES into the console tool — scripts never read pixels.
3. **Manifest schema — three additions required (all applied):**
   - `evidenceClass` per check (`draw-verified` | `presentation-verified`) — the packet's own rule demands the declaration live on the check.
   - Regions must be **capture-relative edge-band/rect fractions**, never absolute pixels: captures differ across platforms (Windows scale 1.0, WSLg scale 1.5, card 77 vs 71 DIP — SP-007 measured font-metric delta). A check region names e.g. "top 3 pixel rows of the capture" or a fractional rect of the captured surface.
   - Explicit pass criterion: color within `tolerance`, matching-pixel `count >= minPixelCount`. Check `kind` stays explicit (`border-color-band`, `region-color`) so evaluation semantics are unambiguous for both the console tool and K3.
4. **Headless spike shape (applied):** minimal `TestApp` in the HeadlessTests project (Fluent theme only) — the real `App` has no parameterless ctor by design (AVLN3001 suppressed). `AvaloniaTestApplication` uses a TestAppBuilder with `.UseHeadless()`. Toggle assertions must pump the dispatcher; `:pointerover` needs headless input helpers or stays out of the spike (unit/headed already cover it).
5. **Self-test sufficiency:** ONE seeded regression tripping ONE specific named check (lit border brush) suffices — the pipeline proves it can see a real defect; every check TYPE is covered by synthetic-bitmap unit tests in `CcpClient.Tests`. Do not grow a self-test matrix (A-014).

No rejection of the overall outline. Skipped: nothing requested was declined.

## v12 headless research (avalonia-research protocol)

All pages fetched 2026-07-19 from official sources (freshness: v12 docs, current 2026 doc set):

- https://docs.avaloniaui.net/docs/concepts/headless/ (headless platform overview) — VERIFIED:
  - Headless runs the full control tree, layout, styling, and data binding with in-memory windowing/rendering backends.
  - **Default headless drawing = fake backend producing NO pixels.** Rendered frames require `.UseSkia()` + `UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })`, then `window.CaptureRenderedFrame()` returns a `WriteableBitmap` (pixel-lock readable). This VERIFIES the pre-approach consult's frame-semantics correction. The SP-008 spike asserts tree/layout/style/binding facts and needs NO rendered frames — default fake drawing stays.
  - Input simulation: extension methods on `Window` (e.g. `KeyTextInput`) raising the same events real input triggers.
- https://docs.avaloniaui.net/docs/testing/headless-xunit (XUnit integration) — VERIFIED:
  - **XUnit attributes are `[AvaloniaFact]` / `[AvaloniaTheory]`** (the packet's "`[AvaloniaTest]`" wording is the NUnit attribute — deviation recorded; the XUnit attribute is used).
  - Setup: `[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]` once per assembly + `public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());`. Assembly-wide → confirms the separate-project decision.
  - Test isolation: PerTest default (Application + Dispatcher recreated per test); `[AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]` opt-in. PerTest default kept.
  - A minimal `App` (Application + FluentTheme via AXAML or code) suffices — headless requires SOME theme.
- https://www.nuget.org/packages/Avalonia.Headless.XUnit (package metadata, fetched 2026-07-19) — VERIFIED: version **12.1.0** exists (exact baseline match per packet), targets .NET 8.0 (compatible with the net10.0 test project), prefix-reserved official Avalonia package. The project already runs xunit.v3 3.2.2 in CcpClient.Tests; proposal §2 records v12 headless requires xUnit v3 — compatibility asserted by the baseline, proven empirically by the Step-2 spike.

## Design decisions

- **CcpVerify package references (recorded deviation from a strict "zero new packages" reading):** the console tool references `Avalonia` 12.1.0 (baseline, admitted SP-001/002), plus `Avalonia.Headless` and `Avalonia.Skia` 12.1.0 as EXPLICIT references. Both are already in the admitted dependency graph transitively (`Avalonia.Headless` is a dependency of the admitted `Avalonia.Headless.XUnit@12.1.0`; `Avalonia.Skia` ships with baseline `Avalonia.Desktop`) — the explicit pins add NO new third-party code, same versions. Decode path: `UseSkia().UseHeadless(UseHeadlessDrawing=false).SetupWithoutStarting()` (official v12 pattern), then `Bitmap` decodes PNG and BMP on both platforms. No System.Drawing anywhere in the tool.
- **Assertion logic is pure C#** (`DecodedImage` BGRA buffer + `CheckEvaluator` + `CheckManifest`): unit tests synthesize buffers directly — no Avalonia runtime, no captures, keeping the 85 landed tests unpolluted (9 additive tests in CcpClient.Tests).
- **Pass criterion is `minPixelFraction`, not absolute counts** (consult): one manifest valid at Windows scale 1.0 AND WSLg scale 1.5.
- **Surfaces are named capture scopes:** `dashboard-card` = card rect from the layout probe; `dashboard` = full window. State `lit` is driven through REAL input on Windows (right-click quick-toggle, tick-advance verified before capture) and through the restart-restore path on WSLg (pre-seeded `{"statusTickerEnabled": true}` — a real user path; WSLg has no input automation, SP-007 named gate, recorded in the script header).
- **WSLg crop is window-relative** (surprise, recorded below): the probe's `PointToScreen` output equals the card's offset within the X window (window opens at Avalonia monitor origin; X window = client area). X root coordinates are a different space under WSLg (monitors tiled under one root) — verified empirically (966/1464 border pixels = SP-007's exact count).
- **Artifacts gitignored in-scope:** `client/tools/verify/artifacts/` (repo-root .gitignore is out of File Scope).

### Step 3 evidence summary

- Windows: `capture.ps1` unlit/lit/dashboard full — CAPTURE PASS each (lit state driven by real right-click, tick 2→6 observed). CcpVerify: 3/3 checks PASS against the fresh captures (966/1464 unlit, 958/1464 lit, dashboard background 19195/21320) — pixel counts match SP-007's PS-computed values exactly (cross-validation of the evaluator against the original implementation).
- Fail path: lit check against the unlit capture → `FAIL dashboard-card-lit-border … FIRST FAILED CHECK: dashboard-card-lit-border`, exit 2.
- WSLg: `capture-wslg.sh` dashboard-card unlit + lit CAPTURE PASS (XGetImage; lit via settings restore — probe showed the tick row in layout, card 488x96). CcpVerify ON LINUX against the BMPs: unlit 966/1464 PASS, lit 955/1464 PASS.
- Cross-check vs SP-007 artifacts: unlit PNG 966/1464, lit PNG 958/1464, WSLg full-window BMP dashboard-background 18649/21320 PASS.
- Solution build 0W/0E; CcpClient.Tests 94/94 (85 landed + 9 new).

## Step 2 — headless admission evidence

**ADMITTED: `Avalonia.Headless.XUnit@12.1.0`** (restore/build/real-test green on Windows AND WSL2 — the packet's admission bar).

- Project: `client/tests/CcpClient.HeadlessTests/` (xunit.v3 3.2.2 same as CcpClient.Tests; project reference to CcpClient.Desktop; added to `CcpClient.sln` under `tests/`). Minimal `TestApp` (FluentTheme in code, no AXAML) per consult point 4 — the real `App` is composition-root-constructed by design.
- `[AvaloniaFact]` used (official v12 XUnit attribute; the packet's `[AvaloniaTest]` wording is the NUnit one — deviation recorded in research section).
- Three REAL interaction tests against the dashboard card through the REAL composition root (`CompositionRoot` + `StartupPhaseRunner`, temp settings dir, same boot pattern as the 85 landed tests):
  1. `Card_Toggle_AppliesLitClass_AndStyleResolvesBorderBrush` — compiled `Classes.lit` binding resolves; toggle via the ONE command path applies/removes the `lit` class; the `.lit` selector's BorderBrush (#FFE066FF) wins over the base (#FF3A2F3E) and reverts on toggle-off. Draw-level: tree classes + style resolution.
  2. `Card_ArrangedBounds_GrowWithLoadBearinIsVisible` — arranged DIP card height grows when the tick row enters layout on toggle-on. Draw-level: in-memory layout bounds.
  3. `ElementNameMirror_FollowsLiveTickText` — phase-4 bind through the REAL `AvaloniaUiDispatch` (headless UI thread is a real Dispatcher); the `#TickText.Text` mirror follows an ADVANCING tick. Draw-level: compiled-binding resolution against a changing source.
- Draw-level assertions ONLY — no `CaptureRenderedFrame`, no pixel claims, default fake drawing backend (no pixels exist). Evidence-class rule honored.
- **Windows:** build 0W/0E; 3/3 headless passed; 85/85 landed CcpClient.Tests passed untouched (SDK 10.0.302).
- **WSL2 (Ubuntu, SDK 10.0.110, native `~/ccp-sp008` copy, never /mnt/e):** solution build 0W/0E; 3/3 headless passed; 85/85 landed passed. Session facts: `WAYLAND_DISPLAY=wayland-0`, `DISPLAY=:0` (WSLg; headless needs no display at all — in-memory backends — recorded as environment fact only).

## Engine reviews

- Step 1 plan review: `spine_review_step` → **skipped=true, reviewLevel=0, spawnFailed=false** (eighth consecutive batch with zero engine reviews; T-2 remains open). Fable solo consults are the active quality gate per the packet.
- Step 2 plan review: **skipped=true** (same as Step 1; T-2).
- (further steps appended as they land)
