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

(to be filled as steps land)

## Engine reviews

(to be filled — `spine_review_step` results per step; T-2 tracking)
