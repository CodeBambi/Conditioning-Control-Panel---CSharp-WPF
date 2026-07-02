# Avalonia port: calibration / eye-tracking overhaul (merge fce9713d + dfbe20c4)

Status: **CORE PORTED (data model + per-frame algorithm). Remaining surface: calibration window UX + 3 diagnostic/focus services. Live-webcam verification still required before release.**

## What is ported (this effort)
The heart of the v6.2.5 "eye tracking rebuilt" change — the data model and the per-frame
gaze pipeline — is mirrored faithfully from the WPF source into the Avalonia Windows head:
- `WebcamCalibrationData`: `IrisRange` / `AxisCorrection` / `LightSource` / `GazeTrim` fields,
  the `IrisRangeData` / `AxisCorrectionData` / `GazeTrimData` classes, and `WithGazeTrim`.
- `AvaloniaWebcamTrackingService`: all 13 algorithm hunks — tightened mouth/tongue timing
  (`MarSpeechFloor`, `MinMouthOpenMs` 250, tongue ratios), the two-eye disagreement gate,
  the stateful gaze lock-on/attractor (`SetGazeAttractor`/`ClearGazeAttractor`/`ApplyGazeLock`/
  `LastPreLockGaze`), motion-shaping follower (`ShapeCursorMotion`), soft screen edges
  (`SoftEdge`), the calibrated iris-range clamp with lighting-shadow margin, bubble-test
  gaze trim (`ApplyGazeTrim` + runtime application), and post-polynomial axis residual
  correction (`ApplyAxisCurve`). Build green; Core tests 108/108.

The earlier "BLOCKED STOP / stack divergence" note in this doc was **disproven**: the active
fields of the two `WebcamCalibrationData.cs` files align (the line-count gap was comments +
the merge additions + one retired legacy field), so the port was a faithful diff-mirror.

## Remaining surface (follow-up)
- `WebcamCalibrationWindow` (+641 / −110): the calibration flow that *populates* the new
  `IrisRange`/`AxisCorrection`/`GazeTrim`/`LightSource` data, plus the dim-room warning that
  replaced the lighting picker (dfbe20c4).
  **BLOCKED by a pre-existing Avalonia gap (not the merge):** the Avalonia
  `WebcamCalibrationWindow.axaml.cs` is a non-functional UI shell — it animates the dot
  grid + runs gesture checks but collects NO iris samples, fits NO polynomial, and calls
  NO `ApplyCalibration` (there are zero `OnRawIris` handlers and zero `ApplyCalibration`
  calls in all of `CCP.Avalonia`). The WPF window's finalize additions (IrisRange min/max,
  `BuildAxisCorrection`, the bubble test) layer onto a polynomial-fit pipeline that does
  not exist in Avalonia. Porting them therefore requires FIRST implementing the Avalonia
  calibration sample-collection + polynomial-fit pipeline — a separate feature effort
  that predates the merge, out of scope here. Until then the new algorithm branches are
  reachable but not yet driven by a calibration run.
- `GazeDriftCorrectionService` (+239): **DONE** — fully ported to the Windows head
  (commit 591e898d): click-driven residual nudging, fixation detection, persistence, adapted
  to the Avalonia `IMouseHook` / `Dispatcher.UIThread` / `ISettingsService` seams.
- `GazeFocusService` (+47) + `GazeDebugCursorService` (+306): the BASE features are already
  ported (`AvaloniaGazeFocusService` rect-based gaze dwell/blink-pop + `AvaloniaGazeDebugCursorService`,
  both registered in DI + consumed by `BlinkTrainerTabViewModel`, smoke-tested at port time).
  The merge deltas are niche refinements, not core feature gaps:
  - GazeFocusService +47 = the `SetGazeAttractor` consumer wiring (pull the cursor toward the
    dwell target). Low-value in the Avalonia rect-based pop model (pops everything in a 60-DIP
    radius — inherently forgiving, unlike WPF's single-best-target precision pop); a faithful
    port needs a target-presence query (added `IBubbleService`/`IFlashService` surface).
    `SetGazeAttractor`/`ClearGazeAttractor` exist on the tracker (ported) + `SetGazeAttractor`
    takes coords in the OnGazeMove space, so the wiring itself is trivial once a target-presence
    query exists. Documented refinement.
  - GazeDebugCursorService +306 = lock-state visualization on the debug cursor. Documented refinement.

## Verification status
The port mirrors the WPF math exactly (diff-driven, not from memory), compiles clean
(`CCP.Desktop.slnf` 0 errors), and Core tests pass (108/108). The Avalonia Windows head
`--smoke-test` **now runs to completion** (EXIT 0, 45 tabs visited, Chaos run exercised):
0 unhandled/unobserved exceptions. Remaining smoke findings:
- `OverlayClickThrough: No overlay window was created` — **pre-existing test-vs-architecture
  mismatch**, not a code bug: `AvaloniaOverlayService` renders on `CompositorEngine` layers
  (`_pinkTintLayer`/`_spiralLayer`/`_brainDrainLayer`), same design as FlashLayer/BubbleLayer;
  the smoke check looks for separate `*OverlayWindow*` windows (WPF-style) that the Avalonia
  layer design intentionally never creates. Verifying the layer's hit-test-ignoring behaviour
  would require a smoke-harness change, out of merge-port scope.
- 4 `[Info] ChaosRun` lines (run narration, not errors).
A racy `InvalidCastException: RotateTransform → Visual` in `Animation.RunAsync` surfaced in
one run but not another; none of the merge-port files add any `Animation.RunAsync`/transform
animation (confirmed by grep — only SeasonRecapCard/SplashScreen/AttentionCheckControl do,
which this work did not touch), so it is pre-existing.
Still NOT live-verified with a webcam (no device) — that remains a release gate for the
gaze math.

## Source merge commits
- `fce9713d` feat(gaze): calibration overhaul — accuracy corrections, target lock-on, motion shaping, lighting comp
- `dfbe20c4` fix(gaze): drop the lighting picker for a plain dim-room warning; damp bubble-recal scale

## WPF files changed (port from here)
| File | Δ (fce9713d) | Δ (dfbe20c4) | Area |
|------|--------------|--------------|------|
| `Services/Webcam/WebcamTrackingService.cs` | +527/−40 | −40 | **core gaze algorithm** |
| `Services/Webcam/WebcamCalibrationData.cs` | +159/−12 | −12 | calibration data model |
| `Windows/WebcamCalibrationWindow.xaml.cs` | +641 | −110 | calibration flow logic |
| `Windows/WebcamCalibrationWindow.xaml` | +153 | −110 | calibration UI (dim-room warning) |
| `Services/Tracking/GazeDebugCursorService.cs` | +306 | — | on-screen gaze debug cursor |
| `Services/Tracking/GazeDriftCorrectionService.cs` | +239 | — | click-driven drift recal (Core marker `IGazeDriftCorrectionService` + Windows impl already exist from task 4a; reconcile the richer WPF impl) |
| `Services/Tracking/GazeFocusService.cs` | +47 | — | gaze focus economy |
| `Models/AppSettings.cs` | +25 | −12 | new settings (see below) |
| `Services/CalibrationSoundService.cs` | +1 | — | |
| `App.xaml.cs` / `MainWindow.LabTab.cs` / `MainWindow.TabNavigation.cs` / `LabTabView.xaml(.cs)` | small | — | wiring |

## Algorithm areas to mirror (the +527 WebcamTrackingService lines)
1. **Target lock-on / gaze attractor** — stateful contraction toward a bubble center:
   `GazeAttractorTarget(x,y,radius)`, `_lockEngage`, `_lockCenterX/Y`, `SetGazeAttractor`/
   `ClearGazeAttractor`, `LastPreLockGaze`, rates `GazeLockMaxStrength`/`CaptureFrac`/
   `BuildRate`/`LeaveDrain`/`SwayFrac`/`SwayDrain`.
2. **Motion shaping** — EMA + follower smoothing: `_gazeEmaX/Y`, `_swayEma`, `_followX/Y`,
   `ApplyGazeTrim(...)` with `MaxOffset`/`MaxScale` clamps.
3. **Eye-disagreement gating** — one eye darting: `_eyeDisagreementBuffer`, percentile
   spike (`EyeDisagreementRatio`/`Floor`), `_twoEyeStreak`, `_gateSkipStreak`.
4. **Iris clamping / shadow-margin** — robustness vs glints/shadows: `IrisClampMarginFrac`,
   `ShadowMarginFrac`, per-axis min/max margins.
5. **Mouth/tongue timing** — `MarSpeechFloor`, `MinMouthOpenMs`, `TongueEnter/LeaveRatio`,
   `MinTongueOutMs`.

## New AppSettings (net after both commits)
- `WebcamAutoDriftCorrection` (bool, default true) — click-driven implicit recalibration toggle.
- `WebcamLightSource` (string) — retained by dfbe20c4 even after the lighting picker UI was dropped
  (declared during calibration, used by lighting comp in the next calibration).
- (dfbe20c4 removed the per-axis lighting picker settings that fce9713d added.)

## Avalonia target files (port INTO here)
- `CCP.Avalonia.Desktop.Windows/Services/Webcam/AvaloniaWebcamTrackingService.cs` (~3,082 lines) —
  the per-frame pipeline that must gain areas 1–5.
- Avalonia `WebcamCalibrationWindow.axaml(.cs)` — must gain the calibration flow + dim-room warning.
- New Avalonia services for `GazeDebugCursorService` + `GazeFocusService`.
- `IGazeDriftCorrectionService` (Core) + Windows impl already added in task 4a (commit 591e898d) —
  reconcile with the richer WPF `GazeDriftCorrectionService` (+239).

## Why deferred
- **Scale**: ~2,000 insertions / 14 files, including a +527-line rewrite of the core gaze algorithm.
- **Stakes**: this IS the eye tracker; an unverified port risks silent accuracy / lock-on regressions.
- **Verification**: requires a live webcam + the calibration model; not possible headlessly.

## Recommended approach for the dedicated effort
1. Diff `fce9713d` + `dfbe20c4` against the WPF `WebcamTrackingService`/`WebcamCalibrationData`/
   `WebcamCalibrationWindow` to get the exact hunks.
2. Port the data model (`WebcamCalibrationData` + the net AppSettings fields) first.
3. Port algorithm areas 1–5 into `AvaloniaWebcamTrackingService.cs`, matching its existing
   per-frame structure (it already has iris/landmark code from earlier ports).
4. Port the calibration-window flow + dim-room warning UI.
5. Port/reconcile `GazeDebugCursorService`, `GazeFocusService`, `GazeDriftCorrectionService`.
6. **Live-verify** with a webcam before shipping: bubble lock-on, disagreement gating, drift recal.
