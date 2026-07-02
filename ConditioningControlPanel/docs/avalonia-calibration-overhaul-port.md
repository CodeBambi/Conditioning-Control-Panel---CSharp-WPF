# Avalonia port: calibration / eye-tracking overhaul (merge fce9713d + dfbe20c4)

Status: **NOT PORTED — blocked. Requires a dedicated, webcam-verified effort that first reconciles stack divergence.**

## BLOCKED STOP (evidence-backed, gathered this session)
The goal's BLOCKED STOP clause applies. Three concrete blockers:
1. **Stack divergence (not a clean mirror):** the Avalonia webcam stack is NOT a pre-merge copy of the WPF one. `WebcamCalibrationData.cs` already diverged (WPF 369 lines vs Avalonia 249 lines) *before* the merge additions. So the merge changes can't be applied as a mechanical diff — the port must reconcile the existing divergence *and* layer the merge on top, across the data model, the 3083-line tracker, and the calibration window.
2. **Unverifiable here:** the completion audit requires a live-webcam behavior comparison + a `--smoke-test` that exercises the gaze tracker. No webcam is available, so even a faithful port can't be audited complete.
3. **Stakes + constraints:** this is the eye-tracker core. An unverified port of diverged+merged gaze math risks silent regressions, which the goal's "preserve behavior" / "no unverified narrowing" / "no dead code" constraints forbid. The feature is also atomic — the algorithm consumes IrisRange/AxisCorrection/GazeTrim that only the calibration window populates, so a partial port is either dead code or a dormant/risky branch.

Full analysis of the merge diff was completed (the 13 tracking hunks + the data-model additions are mapped below), so a dedicated effort can proceed diff-in-hand.
This is the v6.2.5 "eye tracking rebuilt" feature. It is the single largest and
highest-stakes change in the merge (~2,000 insertions / 14 files), and it rewrites the
per-frame gaze pipeline — the core of the eye tracker. A faithful port needs live webcam
verification (impossible in a headless/CI context) so it does not silently regress gaze
accuracy, lock-on, or the disagreement gate.

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
