# Webcam Calibration Window — Avalonia Port Plan (+ human-testing mode)

Owner-directed 2026-07-05. Context: the webcam/gaze subsystem has **zero machine
verification** (no `--verify-webcam` harness; smoke only clicks the tabs). Per owner:
treat the webcam subsystem as UNVERIFIED — keep the **WPF behavior contract**, implementation
is free. The one CONFIRMED-missing, foundational, camera-independent-to-BUILD piece is the
**calibration window**: `CCP.Avalonia/Windows/WebcamCalibrationWindow.axaml.cs` is an honest
"not available" stub today, so no new user can calibrate and every gaze feature is dead for them.

## Behavior contract (WPF reference)
WPF `ConditioningControlPanel/Windows/WebcamCalibrationWindow.xaml.cs` (~1893 LoC). Key methods:
- `RunSequenceAsync` (:254) — **4×4 = 16-dot** grid, row-major, `EdgeMargin` inset. Per dot:
  `MoveDotTo` → ready delay (`ReadyMs`/`RetryReadyMs`) → `_collecting=true` for `SampleMs` →
  need ≥ `MinSamplesPerPoint`, up to `MaxAttemptsPerPoint` tries, ring fills, `SettleMs` between.
- `OnRawIris(dx,dy)` (:222) — the sample sink: while `_collecting`, appends to `_allSamples[_activeDotIndex]`.
  Fed by tracker `OnRawIris` event (`AvaloniaWebcamTrackingService` :469, fires :1977).
- `FinalizeCalibrationAsync(positions)` (:372) — per-dot mean of raw iris (`srcMeans`) → screen
  target (`dstPoints`) → `FitCerrolazaPolynomial(...)` → builds `WebcamCalibrationData`
  { Mode, MonitorBounds, Polynomial(X[7],Y[7]), IrisRange, AxisCorrection, ... }.
- `FitCerrolazaPolynomial` (:1209, ~125 LoC) — **the solver**: Cerrolaza 2nd-order poly,
  7 coeffs/axis, ridge-regularized, weighted. Pure math → ports near-verbatim.
- `EvalPolynomial(poly, ix, iy)` (:1361) — project raw iris → screen point.
- `RunValidationPhaseAsync` (:811) + gesture waits (blink/mouth/tongue) — warm-up gestures.
- `RunBubbleTestAsync` (:1530) + `OnBubbleTestGaze` — gaze-trim residual capture (GazeTrim).
- Dot UI: `MoveDotTo` (:1811), `UpdateProgressRing`/`ResetProgressRing`/`Start|StopRingPulse`.

## Seam (already anticipated by the tracker — do NOT edit tracker internals)
- Sample stream: `IWebcamService.OnRawIris` (Action<double,double>).
- Verify stream: `IWebcamService.OnGazeMove` (Action<Point>) — projected gaze (needs a live calib).
- Apply candidate in-memory (no disk): `AvaloniaWebcamTrackingService.SetCalibrationLive(data)` (:984).
- Persist + apply: `ApplyCalibration(data)` (:969, calls `data.Save`).
- Data model: `CCP.Avalonia.Desktop.Windows/Services/Webcam/WebcamCalibrationData.cs`
  (per-head, NOT Core) — model only (no solver). `PolynomialFitData` = X[7]/Y[7].
- Tracker `Calibrate()` (:887) is a STUB; the WINDOW owns the flow. Head wires the window
  to `Calibrate()` (or the calibration button) — find the caller and light it up.

## AXAML shell (already exists, reuse)
`CCP.Avalonia/Windows/WebcamCalibrationWindow.axaml`: `DotCanvas`(Dot+DotRingBg+DotRingFg),
`StatusPanel`(TxtTitle/TxtStatus/TxtProgress), `IntroPanel`(BtnIntroContinue), `ValidationPanel`
(TxtValidationCue/Prompt/Detail/Attempt), `VerifyPanel`(BtnVerifyAccuracy/Recalibrate/Done),
`ErrorPanel`. Topmost maximized black window. Handlers already stubbed in the .axaml.cs.

## Human-testing / verify mode (OWNER IDEA — first-class)
Because gaze accuracy cannot be machine-verified, the verify phase IS the test harness:
1. After solve, `SetCalibrationLive(candidate)` so `OnGazeMove` reflects the new fit.
2. Enter a **logged gaze-test phase**: show a live gaze cursor following `OnGazeMove`; present
   a few known target dots ("look here"); for each event log a structured line:
   `raw iris (dx,dy) | projected gaze (x,y) | target (x,y) | error px`.
3. Write telemetry to a dedicated log (Serilog sink / `logs/webcam-verify-<ts>.log`) so the
   agent can inspect machine evidence (did OnRawIris/OnGazeMove fire? does the fit project
   sensibly? is mean/median error reasonable?).
4. **Pause for the human verdict**: a "Did the dot follow your eyes? Yes/No" confirm in the
   window; the agent also waits for the human's chat feedback and reads the log.
5. Yes → `ApplyCalibration` (persist). No → recalibrate loop.
Optionally add a `--verify-webcam` CLI entry that opens the window straight into this flow
(and logs) so it becomes the standing webcam verification ritual.

## Slicing (gate after each: slnf 0 · WPF 0 · Core 542 · smoke exit 0)
- **S1 — core calibration path**: intro → 16-dot sample loop (OnRawIris) → FitCerrolazaPolynomial
  → build WebcamCalibrationData (Polynomial + IrisRange + MonitorBounds) → SetCalibrationLive →
  minimal Done → ApplyCalibration persist. Port constants + solver verbatim. Wire the head caller.
- **S2 — human-testing/verify mode**: logged gaze-test phase (live cursor + target dots + telemetry
  log) + human-confirm + recalibrate loop. The owner's testing mode.
- **S3 — polish to full contract**: gesture warm-up (blink/mouth/tongue), bubble-test gaze-trim,
  axis-correction/head-pose comp. Each a sub-slice.

## Guardrails
- Never edit the WPF head or tracker internals (only call the tracker's public calib seam).
- Privacy: frames/per-frame data never persisted; only calibration JSON (numbers). The verify
  log stores gaze POINTS + iris VECTORS (derived numbers, transient debug) — write to the local
  log only, never network; document it and gate it behind the verify mode, not normal runs.
- Final gaze-accuracy proof is HUMAN+CAMERA (S2 provides the ritual + evidence).
