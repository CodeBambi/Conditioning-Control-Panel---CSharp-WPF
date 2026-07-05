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

## Seam (CORRECTED 2026-07-05 — Core DIM seam, NOT direct tracker calls)
**Architectural constraint:** the window lives in shared `CCP.Avalonia` (constructed by shared
viewmodels `BlinkTrainerTabViewModel`/`DeeperTabViewModel`/`LabTabViewModel`; also referenced by
the Windows head's `GazeDriftCorrectionService`). CCP.Avalonia is referenced BY the Windows head,
so it CANNOT see `AvaloniaWebcamTrackingService`, `WebcamCalibrationData`, `SetCalibrationLive`,
or `PolynomialFitData` (all Windows-head). The window drives calibration through a **Core seam**.
- Sample streams (already on the Core seam): `IWebcamService.OnRawIris` (Action<double,double>),
  `OnHeadPose` (Action<double,double>), `OnGazeMove` (Action<Point>, verify phase).
- **NEW Core DTOs** (`CCP.Core/Services/Webcam/`): `CalibrationDotSamples` { double TargetX,TargetY;
  IReadOnlyList<CalibrationIrisSample> Samples } where CalibrationIrisSample = (Dx,Dy,Yaw,Pitch,HasPose);
  and `CalibrationPreviewResult` { bool Success; double RmsX,RmsY; string? Error }.
- **NEW DIM methods on `IWebcamService`** (default no-op bodies so the stub + fakes compile; the
  Windows tracker overrides): `CalibrationPreviewResult BuildCalibrationPreview(IReadOnlyList<CalibrationDotSamples> dots, ScreenInfo screen, string mode)`
  (solves + `SetCalibrationLive` internally, returns quality), `void CommitCalibration()` (persist via
  `ApplyCalibration`), `void CancelCalibrationPreview()` (`SetCalibrationLive(null)`).
- **Windows tracker** owns the math: port `FitCerrolazaPolynomial`/`EvalPolynomial` + build
  `WebcamCalibrationData` INSIDE `AvaloniaWebcamTrackingService` (its `Calibrate()` :887 stub becomes
  the seam impl). Data model `WebcamCalibrationData.cs` (Windows-head) unchanged.
- Linux/macOS stub `AvaloniaWebcamService`: DIM defaults → `Success=false` → window shows the honest
  "not available" panel (webcam is Windows-only anyway).
- Callers already exist (shared VMs new up `WebcamCalibrationWindow`); no head wiring needed.

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
- **S1a — Core seam ✅ DONE `bc6eba94`**: `CalibrationDotSamples`/`CalibrationIrisSample`/
  `CalibrationPreviewResult` DTOs + `BuildCalibrationPreview`/`CommitCalibration`/
  `CancelCalibrationPreview` DIMs on `IWebcamService`.
- **S1b — Windows solver ✅ DONE `5cece1c8`**: `WebcamCalibrationSolver.cs` (Windows head) ports the
  WPF math verbatim (`BuildCalibrationData(dots, screen, mode, out rmsX, out rmsY, out error)`);
  `AvaloniaWebcamTrackingService` overrides the 3 DIMs (`_pendingCalibration` field, SetCalibrationLive
  on preview, ApplyCalibration on commit, Load-revert on cancel).
- **S1c — window flow ✅ DONE `df06d06d`**: `WebcamCalibrationWindow.axaml.cs` real flow — intro →
  16-dot pink-dot grid with per-dot iris sampling (OnRawIris + head-pose pairing, ring fill +
  DispatcherTimer pulse) → `BuildCalibrationPreview` (solve + live-apply) → verify panel (residual)
  → Done=`CommitCalibration` / Recalibrate|ESC=`CancelCalibrationPreview`. ScreenInfo resolved via
  `IScreenProvider` by window `Position`. **Avalonia can create calibrations again (was a stub).**
  Gates green (slnf/WPF/Core 542/smoke). Final gaze-accuracy proof is HUMAN+CAMERA — that is S2.
- **S2 — human-testing/verify mode ✅ DONE**: "Verify Accuracy" runs a logged live-gaze test — a live
  on-screen gaze cursor follows `OnGazeMove` (monitor-local DIP, no conversion), 5 target dots, per-target
  mean error vs target. PRIVACY: only AGGREGATE mean/max error is logged (`_verifyLogger` "webcam-verify:
  mean_err=.. max_err=..", like the fit RMS) — NO per-frame gaze points/iris to disk; cursor is on-screen
  only. Result + verdict shown on the verify panel; user gives the final Done/Recalibrate call.
- **S3 — polish to full contract**: gesture warm-up (blink/mouth/tongue), bubble-test gaze-trim,
  axis-correction/head-pose comp. Each a sub-slice.

## Avalonia v12 UI notes (researched 2026-07-05 — belongs in crossplatform-rebuild-plan.md §21)
- **`Animation.RunAsync` throws by design when `IterationCount` is Infinite** (AvaloniaUI/Avalonia
  Discussion #16757; AvaloniaBook Ch29). For a looping ring pulse use a **`DispatcherTimer`**
  (imperative, matches §21 :518-520 "drive per-frame invalidation from a ~16ms DispatcherTimer")
  or a Style-applied infinite animation — NEVER await an infinite `RunAsync`.
- Window's current screen: `this.Screens.ScreenFromWindow(this)` (v12 `Avalonia.Controls.Screens`;
  docs.avaloniaui.net/docs/app-development/window-management). Match to the Core `ScreenInfo` via
  `IScreenProvider` for the seam call. Use each monitor's own `Scaling` for DIP↔px math (§21 :677).
- Dot placement: `Canvas.SetLeft/SetTop`. Panels: `IsVisible` (not WPF `Visibility`). Async flow:
  `Dispatcher.UIThread` + `Task.Delay`. All already used across the ported windows.
- Solver stays **OpenCvSharp** (already referenced by the Windows tracker) — it IS the WPF behavior
  contract (Cerrolaza polynomial + FindHomography); not a dependency to swap.

## Guardrails
- Never edit the WPF head or tracker internals (only call the tracker's public calib seam).
- Privacy: frames/per-frame data never persisted; only calibration JSON (numbers). The verify
  log stores gaze POINTS + iris VECTORS (derived numbers, transient debug) — write to the local
  log only, never network; document it and gate it behind the verify mode, not normal runs.
- Final gaze-accuracy proof is HUMAN+CAMERA (S2 provides the ritual + evidence).

---

## PIVOT 2026-07-05 (owner): NOT parity — make it MUCH BETTER than WPF (research-driven)

Owner tested the ported calibration/tracking: **"worse than WPF, unacceptable."** Then escalated:
**do not accept WPF-parity quality — rework it into something substantially BETTER; research online;
size/time is no issue; the rework can be as big as needed.** Owner is the camera-in-the-loop tester,
which unlocks bigger algorithmic swings than a can't-see-the-camera agent could otherwise verify.

### Verified findings that reframe the problem (2026-07-05, wpf-archaeologist c36ff52c + self-audit)
- **The Avalonia tracker is a FAITHFUL byte-for-byte port of the WPF runtime gaze path** (poly eval,
  ApplyAxisCurve, One-Euro, SoftEdge, GazeLock, ShapeCursorMotion; one dormant LightSource-margin
  divergence that never triggers). NOT the cause.
- **My solver `WebcamCalibrationSolver.BuildCalibrationData` is COMPLETE** — it already does
  RobustPerDotMean (saccade-settle + MAD), head-pose sample rejection, inverse-spread weighting,
  2-pass outlier-dot rejection, ridge λ + 7-coeff Cerrolaza, **axis-correction**, iris-range. The
  archaeologist's "missing steps B1-B6" were speculative; reading the code shows they're present.
- **Persistence/apply is complete** (BuildCalibrationPreview → SetCalibrationLive → CommitCalibration
  → ApplyCalibration.Save; the full Calibration object incl. AxisCorrection+IrisRange is applied).
- **`OnHeadPose` (AvaTrk:1931) + `OnRawIris` (AvaTrk:2025) both fire** — sampling + B6 work.
- **The one real parity gap is the WPF fit-quality gate** (CalWin:528-568): WPF rejects fits with
  `rms > 20% screen` and offers a redo; its own comment says it was added because bad fits "completed
  but were wildly off / not usable" — **exactly the owner's complaint.** My S1c lacked it.
- Conclusion: WPF's own approach (2nd-order Cerrolaza polynomial + per-row/col axis warp on a
  discrete 16-dot grid) is near its ceiling. To be MUCH better we must change the ALGORITHM/UX,
  not finish the port.

### The rework direction (to be finalized after research + advisor)
Candidate levers (research will rank + source each): **smooth-pursuit / continuous moving-target
calibration** (orders of magnitude more (target,iris) pairs, less tedious, better-conditioned fit);
**a stronger regressor** (Gaussian-Process or RBF/thin-plate vs a 2nd-order polynomial);
**head-pose normalization** (decouple head motion, ETH-XGaze-style) so the user can move;
**appearance-based deep gaze** (L2CS-Net/ETH-XGaze/FAZE few-shot) IF the pipeline exposes eye
crops and a shippable ONNX model exists. Keep the working polynomial path as a fallback/baseline
for A/B until the new path is human-verified better. Implement behind the same `IWebcamService`
calibration seam; one improvement per commit; gate each; hand to owner for camera A/B.

### Research synthesis 2026-07-05 (4 agents: pipeline map + SOTA models + calibration + head-pose/filtering)
(NB web agents answered from training knowledge w/ canonical source URLs — not a live fetch; verify
URLs/numbers with parent web tools before formal citation. Sources: Cheng TPAMI'24 survey arxiv 2104.12668;
Pfeuffer UIST'13 Pursuit Calibration; Vidal UbiComp'13 Pursuits; Drewes MUM'18 pursuit speeds;
Cerrolaza TOCHI'12 polynomials; Zhang ETRA'18 data-normalization; Casiez CHI'12 1€ filter; Salvucci
ETRA'00 I-VT/I-DT; L2CS-Net arxiv 2203.03339; GazeTR 2105.14424; ETH-XGaze 2007.15837; FAZE 1905.01941.)

**Current pipeline signals (codebase-analyzer 50e938f5):** BlazeFace box + FaceMesh 468 landmarks +
MediaPipe Iris (5 iris + 71 lid pts). `OnRawIris` (AvaTrk:2025) = two-eye-avg, **eye-width-normalized**
iris-center−eye-corner-midpoint vector, 3-tap median pre-filtered, **head-pose-NAÏVE**. Head pose = solvePnP
on 6 landmarks (yaw/pitch rad, trusted only as deltas), **gaze comp RETIRED** (PnP jitter > the motion it
corrected; pitch term fought vertical gaze). **Per-eye pixel crops (64×64 iris, 192×192 face) AND the full
640×480 BGR frame + landmarks + eye ROIs all coexist at `ProcessFrame` (:1673) but are discarded** → an
appearance-based deep model is FEASIBLE with only a crop-and-infer insert at :1714-1795. Two plug points:
feature→screen regressor at `ProjectGazeToScreen` (:2395); deep gaze at ProcessFrame.

**Core diagnosis:** the 2nd-order Cerrolaza polynomial **conflates gaze with head pose** — valid only at the
calibration head pose; head motion breaks it. Current calibration copes by REJECTING head-moved samples
(one-pose-only). This is the real ceiling, not a porting gap. Realistic webcam accuracy target: **~2-3° ≈
~120-180px @1080p**; IR trackers do ~0.5-1°. Wild inaccuracy = an UNBOUNDED stage (extrapolation, un-gated
outlier, un-guarded solve, DPI bug) — the code already clamps to IrisRange, guards NaN, gates outliers.

**Rework design — TIER 1 (classical, ships on the existing geometric pipeline; no new model/license; user-A/B-verifiable):**
1. **Smooth-pursuit calibration** (moving dot the eyes follow) REPLACES/augments 16 discrete dots: an
   edge+corner-covering path (raster or edge Lissajous), ~10-12°/s, ~20-30s → **600-1800 dense samples**
   incl. screen edges (where polynomials extrapolate worst). Pearson-correlation gate (target↔gaze, windowed)
   auto-drops blinks/saccades/glances; **cross-correlate to estimate + time-shift the ~100-150ms gaze lag.**
2. **Better regressor** at the fit + `ProjectGazeToScreen`: cheapest-that-wins = **ridge-regularized CUBIC**
   polynomial (dense pursuit data controls the overfit Cerrolaza warns of; single dot-product/frame). Accuracy
   king = **sparse GP** (~50-100 inducing pts → fixed per-frame cost) or regularized **thin-plate-spline/RBF**
   (~30-60 centers). Keep homography + IrisRange clamp as guards; keep the 2nd-order poly as fallback/A-B.
3. **Roll-normalize** the iris vector (rotate by eye-line-tilt θ from the 2 corner landmarks) on top of the
   existing eye-width scale-norm → real small-head-motion freedom, using only 2D landmarks (NO solvePnP →
   avoids the retired-comp trap). Fit + apply in the same normalized frame.
4. **Fixation snap** (I-DT/I-VT) atop the existing One-Euro cascade: dispersion < thr over ~200-350ms → snap
   to fixation centroid (kills rest jitter), release on a velocity spike. Generalizes the existing GazeLock.
5. Robust fit already present (RobustPerDotMean/MAD/outlier-drop/inverse-spread); add Huber/RANSAC if needed.
   Validation/refinement + fit-quality gate already landed (`bfeebd60`).

**Rework design — TIER 2 (deep appearance gaze, highest ceiling, more work + license risk):** full-face CNN
(L2CS-Net/GazeTR → ONNX, or distill MobileNetV3 on ETH-XGaze) → head-pose-INVARIANT gaze vector → small
per-user ridge/GP calibration (kappa correction). Needs data-normalization warp + a crop-and-infer stage at
ProcessFrame + a shippable model. **License:** most pretrained weights are research-only → distill/retrain
our own head OR gate as experimental; the calibration regressor (user's own samples) is ours regardless.

**Recommendation:** Tier 1 first (big grounded win, no license/model risk, keeps the working poly as A/B
fallback, fully camera-verifiable by owner), Tier 2 as a follow-up if Tier 1 isn't enough.

**Sequence (ADVISOR-REORDERED 2026-07-05 — lead with the cheapest/most-independent, NOT smooth pursuit
which is the highest-variance + most-coupled bet):**
- **T1-0 — A/B SWITCH SCAFFOLD FIRST (non-negotiable de-risk):** keep the 2nd-order/16-dot path 100%
  intact as baseline; add the new path behind a mode flag/setting so the owner A/Bs old-vs-new on their
  own camera in one session. Never delete the working path until the owner confirms the new one wins.
- **T1c — roll/scale-normalization** (cheapest, most independent, improves BOTH paths, no solvePnP): rotate
  the iris vector by eye-line tilt from the 2 corner landmarks atop the existing eye-width scale-norm.
- **T1b — cubic-ridge regressor** on the existing 16-dot data (low-risk, improves the current path now).
- **T1a — smooth-pursuit capture + lag-align + correlation gate** ONLY after a normalized feature + capable
  regressor exist to realize its value, and after camera-verified confidence on the cheaper slices.
- **T1d — fixation snap** (independent runtime change, slot anytime).
Gate each; one slice → green → owner camera A/B → next. Never stack two unverified slices between camera tests.

**Before building, verify with PARENT web tools (sub-agents had no live web):** (a) smooth-pursuit works on a
consumer webcam (Pfeuffer/Vidal/WebGazer) before betting the T1a slice; (b) the license of any Tier-2 model
before planning around it. Do not cite the memory-sourced degree/latency numbers as verified.

**Owner decision pending:** Tier 1 incremental first (behind the A/B switch) vs go straight for the Tier-2
deep appearance model. Tier 1 has a real ceiling (~2-3°, still head-pose-sensitive); Tier 2 is the only path
that truly decouples head pose but is weeks + model-sourcing/license work.
