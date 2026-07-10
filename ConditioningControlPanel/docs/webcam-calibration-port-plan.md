# Webcam Calibration Window — Avalonia Port Plan (+ human-testing mode)

> Umbrella driver: [`docs/skia-rebuild-goal.md`](skia-rebuild-goal.md) — THE spirit, workflow model, and acceptance gate. Live work rows live in [`docs/avalonia-migration-task-board.md`](avalonia-migration-task-board.md).

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

### Open-source model LICENSE audit 2026-07-05 (VERIFIED with parent web tools — live-fetched GitHub/LICENSE)
The load-bearing Tier-2 fact: for a COMMERCIAL (Patreon-paid) product, **CODE licenses are permissive but
WEIGHTS are almost universally non-commercial** because they inherit the training-dataset license.

| Project | Code license | Weights (as shipped) | Commercial-shippable? |
|---|---|---|---|
| **Gaze360 dataset** (erkil1452) | — | — | **NO — "non-commercial research use only"** (verified LICENSE) |
| MPIIGaze / MPIIFaceGaze (MPI) | — | — | **NO** — research-only |
| ETH-XGaze (ETH) | — | — | **NO** — CC BY-NC-SA |
| GazeCapture (MIT CSAIL) | — | — | **NO** — research-only |
| **L2CS-Net** (Ahmednull) | **MIT** | `L2CSNet_gaze360.pkl` → **Gaze360-trained** | code YES, **weights NO** |
| **MobileGaze** (yakhyo) ResNet/MobileNet/MobileOne ONNX, real-time CPU | **MIT** | **"All models trained only on Gaze360"** | code YES, **weights NO** |
| **OpenSeeFace** (emilianavt) MobileNetV3, ONNX, 30-60fps CPU, proven in VTube Studio | **BSD-2** | **BSD-2 (models incl.)** but gaze model trained on **MPIIGaze**+UnityEyes | **pragmatic YES** (maintainer BSD-2-licenses the models; used commercially at scale) — **but MPIIGaze provenance is a gray area** a careful counsel would flag; and its gaze is **landmark-rough (eye-openness + coarse gaze), NOT SOTA appearance-invariant gaze** — roughly what CCP already gets from MediaPipe Iris. Strength = robustness (glasses/low-light/wide-pose) + blink. |
| MediaPipe BlazeFace/FaceMesh/Iris (already shipped in CCP) | **Apache-2.0** | Apache-2.0 | **YES** (already vetted, in-product) |

**Consequence for Tier 2:** "grab an ONNX and infer" is NOT legally available for a paid product — the good
weights (L2CS/MobileGaze/ETH-XGaze) are all Gaze360/MPIIGaze/ETH-XGaze-tainted. A legally-clean deep head
means **training the (MIT/BSD) architecture ourselves on a commercially-usable dataset** — synthetic
(UnityEyes / MS "Fake It Till You Make It") or a permissive real set — with a GPU training + eval pipeline.
That is the real "weeks" cost (the ML, not the integration). **The per-user calibration regressor is 100%
ours regardless** (trained on the user's own live samples at runtime — no dataset license attaches).

**This strengthens Tier-1-first:** Tier 1 has ZERO license exposure (our own math on Apache-2.0 MediaPipe
Iris features). The one pragmatic drop-in (OpenSeeFace, BSD-2) is a robustness/blink upgrade, not a gaze-
accuracy leap, and carries a provenance gray area. So: Tier 1 now; if the owner wants the true head-pose-
invariant ceiling, Tier 2 = train-our-own on synthetic/permissive data (scoped as a distinct ML workstream).

### Route A/B/C training-data audit 2026-07-05 (VERIFIED with parent web tools — live-fetched licenses)
Owner asked to evaluate three routes to a legally-clean deep-model training set. Verified verdict:

**Route A — Synthetic data.** Ready-made synthetic *datasets* are almost all NON-commercial (academics slap
CC-BY-NC on synthetic data too): Microsoft FaceSynthetics = **non-commercial research** (verified);
GazeGene (CVPR'25, 1M imgs, HF `vigil1917/GazeGene`) = **CC-BY-NC-SA + gated** (verified); U2Eyes = **CC-BY-NC-SA**
(verified). So "grab a synthetic dataset" fails the same way as human data. The ONLY clean synthetic path is a
permissive **generator** (you own what you generate): **UnityEyes 2** (`alexanderdsmith/UnityEyes2`, UIUC 2025,
open-source) — but it's a Unity project derived from Cambridge UnityEyes whose **explicit commercial license is
unclear** (no license grant found; would need author confirmation) and needs commercially-licensed 3D face
assets; **NVIDIA Omniverse Replicator** — powerful domain-randomization pipeline, you own the output, but needs
commercially-licensed 3D head assets (MetaHuman is Epic-EULA/Unreal-locked — can't just reuse) + Omniverse
licensing diligence. Net: Route A = a real content+ML pipeline (weeks), not a download.

**Route B — Permissive public datasets.** Searched hard; found **no** CC-BY-4.0/CC0 commercial-use gaze dataset.
EVE (the source's hope) = **CC-BY-NC-SA** (verified); ETH-XGaze = CC-BY-NC-SA; OpenEDS = research. **Essentially
every notable gaze dataset — real OR synthetic — is non-commercial.** The one clean Route-B path is
**self-collected opt-in** data — but for CCP this **collides with the privacy contract** (frames + per-frame
derived data NEVER persisted/networked; only coefficients saved). A training-data collector = persist/upload
eye images = MAJOR posture change: new explicit consent flow, ConsentVersion bump, secure storage/transport,
and biometric-data compliance (GDPR/BIPA). Doable but a distinct policy+compliance workstream, not just code.

**Route C — Permissively-trained model zoos.** "Filter HF by `license:mit/apache`" is **unreliable** — an
arXiv audit (2502.04484) documents HF license tags are frequently wrong/incomplete; a model tagged apache-2.0
can still have Gaze360-trained weights. Tags ≠ provenance; must read the card AND trace training data. Found no
specific clean permissive pretrained *gaze* model. The safe Route-C endpoint IS **MediaPipe Iris/FaceMesh
(Apache-2.0) → our own ridge regressor** — the source calls this "Tier 1.5," and **it is exactly what CCP
already does and what our Tier 1 rework upgrades.** 100% clean out of the box, cross-platform, ships now.

**Bottom line:** Routes A & B mostly collapse (no commercial-use gaze dataset exists off-the-shelf; the clean
variants are heavy own-data generation or own-data collection workstreams). Route C ≡ our Tier 1. So the
owner's own research converges on **Tier 1 (MediaPipe + better regressor) now**; a deep head-pose-invariant
model later requires *building our own training set* (Omniverse generation OR opt-in collection) as a separate,
scoped project. The per-user calibration regressor is ours in every scenario.

### OWNER DECISION 2026-07-05: ship all three tiers for on-camera A/B/C; license deferred
Owner: "don't care about the license — use the best HuggingFace ONNX model that says it's fine to use; build
Tier 1 AND Tier 2 so we can check Current vs Tier 1 vs Tier 2; ONNX weights for the deep model; keep it fast
and high-performance; I'll figure out licensing later; plausible deniability is enough." → Proceeding to build
a 3-way A/B/C switch + Tier 1 (classical) + Tier 2 (deep ONNX).

**CHOSEN DEEP MODEL — MobileGaze (`yakhyo/gaze-estimation`, MIT-licensed code, L2CS-Net-based).** Pre-built ONNX
weights are GitHub-release downloads (no training needed). Trained on Gaze360 (weights research-only — owner
accepts). Variants (MAE on Gaze360, lower=better):

| ONNX file | Backbone | Size | MAE° | Note |
|---|---|---|---|---|
| `mobileone_s0_gaze.onnx` | MobileOne-S0 | **4.8 MB** | 12.58 | **DEFAULT — fastest (~1ms design), tiny asset** |
| `resnet18_gaze.onnx` | ResNet-18 | 43 MB | 12.84 | |
| `resnet34_gaze.onnx` | ResNet-34 | 81.6 MB | **11.33** | best accuracy, swappable |
| `resnet50_gaze.onnx` | ResNet-50 | 91.3 MB | 11.34 | |
| `mobilenetv2_gaze.onnx` | MobileNet-V2 | 9.59 MB | 13.07 | |

(Gaze360 MAE is a hard ±wide-range benchmark; for a frontal seated webcam user + per-user calibration on top,
effective accuracy is much better and systematic bias is absorbed by the calibration regressor.)

**AUTHORITATIVE ONNX I/O CONTRACT** (from repo `onnx_inference.py::GazeEstimationONNX`, verified live):
- **Input:** node name `"input"`, shape `[1,3,448,448]`, float32, **RGB**. Preprocess: BGR→RGB → resize 448×448
  → `/255.0` → normalize ImageNet mean `[0.485,0.456,0.406]` std `[0.229,0.224,0.225]` → HWC→CHW → batch.
- **Output:** exactly 2 nodes. `outputs[0]` = **yaw logits** `[1,90]`; `outputs[1]` = **pitch logits** `[1,90]`.
- **Decode:** softmax each → `angle_deg = Σ(prob_i · i) · binwidth − offset` with **bins=90, binwidth=4, offset=180**
  (range ±180°) → `radians()`. yaw = horizontal, pitch = vertical.
- **3D vector** (if needed): `x=-cos(p)sin(y), y=-sin(p), z=-cos(p)cos(y)`.
- **Face crop:** full-face bbox crop (RetinaFace upstream; CCP already has a face bbox in ProcessFrame — reuse
  it, crop from the full BGR frame). Webcam demo mirrors the frame (`flip(...,1)`) before detect — CCP must
  match whatever mirroring its own pipeline already uses; verify on camera.

**SHARED-FRAMEWORK INSIGHT (drives the A/B/C design):** all three tiers share ONE calibrate→fit→project
framework and differ only in (feature → regressor):
- **Current:** eye-width-normalized iris−corner 2D vector → 2nd-order Cerrolaza polynomial + homography.
- **Tier 1:** roll/scale-normalized iris 2D vector → cubic ridge / GP (+ smooth-pursuit capture + fixation snap).
- **Tier 2:** deep gaze (yaw,pitch) from MobileGaze ONNX → per-user ridge (angles→screen).
So `GazePipelineMode { Current, Tier1, Tier2 }` selects the feature+regressor in BOTH calibration capture/fit
AND runtime `ProjectGazeToScreen`. Current path stays 100% intact as the A/B baseline.

### IMPLEMENTATION PLAN 2026-07-05 (facts verified in-code; download verified; advisor unavailable — proceeding)
Verified: `Microsoft.ML.OnnxRuntime` 1.20.1 already referenced (Windows head csproj); models auto-copy from
`ConditioningControlPanel/Resources/Models/**` → output `Resources/Models/` via `<Content Include>`; loader
pattern `new InferenceSession(path, SessionOptions{ORT_ENABLE_ALL, IntraOpNumThreads=2})` +
`InputMetadata.Keys.First()` (see BlazeFace/FaceMesh/Iris detector classes). `mobileone_s0_gaze.onnx`
download verified = 4,974,521 bytes. Solver `BuildCalibrationData` takes generic `(Dx,Dy)` samples and fits a
2D→screen Cerrolaza polynomial + homography + axis-correction — **feature-agnostic**.

**KEY ARCHITECTURE:** the whole calibrate→fit→project stack (`WebcamCalibrationSolver`,
`EmitGazeEvents`→`ProjectGazeToScreen`→smoothing→lock→shaping) is reused UNCHANGED. Only the 2D feature
computed at `ProcessFrame` and emitted via `OnRawIris` changes per mode:
- **Current** = averaged normalized iris vector (`NormalizeIrisVectorSmoothed`).
- **Tier 2** = deep gaze `(yaw,pitch)` radians from MobileGaze on the face-bbox crop.
- **Tier 1** = roll/scale-normalized iris vector (later).
The active mode is a tracker state (`_activeGazeMode`), set from the calibration window's selector before a
calibrate run, persisted into `WebcamCalibrationData.FeatureMode`, and restored on load so runtime feeds the
matching feature. Backward-compat: absent FeatureMode → Current.

**SEQUENCING (Tier 2 first — smaller change + flagship the owner wants; supersedes prior Tier-1-first order but
keeps its core rule: Current path 100% intact, gate each slice, one slice→green→owner camera A/B→next):**
- **Commit 1 (T0 + Tier 2, atomic & complete):** `GazeFeatureMode` enum + setting + `CalibrationData.FeatureMode`
  + seam `SetGazePipelineMode` + calibration-window mode selector (Current | Deep model) + `MobileGaze` ONNX
  detector class (448×448 ImageNet-norm RGB face crop → softmax·idx·4−180 → yaw/pitch rad) + ProcessFrame branch
  + ship `Resources/Models/*_gaze.onnx`. Owner can A/B Current vs Deep.
- **OWNER ADD 2026-07-05: deep-model BACKBONE dropdown.** Within Tier 2, a dropdown selects the ONNX backbone:
  MobileOne-S0 (4.8MB, fastest, default) | MobileNet-V2 (9.6MB) | ResNet-18 (43MB) | ResNet-34 (81.6MB, best MAE)
  | ResNet-50 (91.3MB). ALL share the identical I/O contract + decode — the detector class is backbone-agnostic;
  the dropdown only swaps the model file path. Backbone is a setting (`WebcamDeepGazeModel`) recorded into
  `CalibrationData` (different backbones carry slightly different systematic bias → switching backbone should
  prompt a re-calibrate; the per-user regressor absorbs the bias). **Installer-size note:** shipping all five =
  ~230MB of model assets in the installer; flagged to owner (alt = ship MobileOne-S0 + lazy-fetch the ResNets on
  first pick, but that breaks the "no internet at runtime" model rule). Proceeding to ship all as requested.
- **Commit 2 (Tier 1 classical):** roll/scale-norm feature + cubic-ridge regressor (+ smooth pursuit + fixation
  snap) as the third selectable option.
**Known tuning knob:** feature-space One-Euro constants are iris-vector-tuned (~±0.5); deep angles are radians
(~±0.3). IrisRange clamp self-scales from calibration min/max; One-Euro may need per-mode tuning — owner A/Bs,
we tune.

**ONNX I/O EMPIRICALLY VERIFIED 2026-07-05 (loaded all 5 with onnxruntime):** every backbone has input node
`input` `[1,3,448,448]` float32 and TWO NAMED outputs `yaw` `[1,90]` + `pitch` `[1,90]`. So C# requests outputs
by name (`"yaw"`,`"pitch"`) — zero order ambiguity. Models live gitignored in `Resources/Models/gaze/`
(README + `.gitignore` + `fetch-gaze-models.sh` committed, matching vosk/silero precedent; binaries fetched
locally + installer-bundled). Downloaded sizes: mobileone_s0 4,974,521 / mobilenetv2 9,790,767 / resnet18
45,066,134 / resnet34 85,491,644 / resnet50 95,425,874 bytes.

### TURN-KEY EDIT CHECKLIST — Commit 1 (T0 + Tier 2 deep model + backbone dropdown)
Exact per-file changes (all facts verified in-code as of HEAD after d56b23c2):
1. **`CCP.Core/Services/Webcam/IWebcamService.cs`**: add `enum GazeFeatureMode { Current, DeepModel }` and
   `enum DeepGazeBackbone { MobileOneS0, MobileNetV2, ResNet18, ResNet34, ResNet50 }`. Add default-impl seam
   members: `GazeFeatureMode GazePipelineMode => GazeFeatureMode.Current;`,
   `void SetGazePipelineMode(GazeFeatureMode mode) { }`, `DeepGazeBackbone DeepGazeModel => DeepGazeBackbone.MobileOneS0;`,
   `void SetDeepGazeModel(DeepGazeBackbone backbone) { }`, and `bool DeepGazeModelAvailable => false;` (window greys
   out Deep option if false). (Tier1 enum value added later — keep values reachable.)
2. **`CCP.Avalonia/Services/Webcam/AvaloniaWebcamService.cs`** (Linux/mac stub): no change needed (defaults
   cover it) — confirm it compiles.
3. **`CCP.Avalonia.Desktop.Windows/Services/Webcam/WebcamCalibrationData.cs`**: add
   `[JsonProperty] public string FeatureMode { get; set; } = "Current";` and
   `[JsonProperty] public string? DeepModel { get; set; }`. Thread BOTH through `WithRuntimeOffset` clone
   (line ~192 object initializer). `WithGazeTrim` reuses WithRuntimeOffset — no change.
4. **`WebcamCalibrationSolver.cs::BuildCalibrationData`**: add params `string featureMode, string? deepModel`
   (or set on the returned object by the caller). Stamp `FeatureMode = featureMode, DeepModel = deepModel` into
   the returned `WebcamCalibrationData` (line ~144). Feature-agnostic math UNCHANGED.
5. **`AvaloniaWebcamTrackingService.cs`**:
   a. Fields: `private volatile GazeFeatureMode _activeGazeMode = GazeFeatureMode.Current;`
      `private volatile DeepGazeBackbone _deepBackbone = DeepGazeBackbone.MobileOneS0;` + `MobileGazeDetector? _deepGaze;`.
   b. Seam impls: `SetGazePipelineMode` sets `_activeGazeMode` (+ lazily load/swap deep model on the capture
      thread via a pending flag, NOT inline — InferenceSession ctor is heavy); `SetDeepGazeModel` sets
      `_deepBackbone` + flags a reload. `GazePipelineMode`/`DeepGazeModel`/`DeepGazeModelAvailable` getters.
   c. `ResolveModelPaths`: also resolve `Resources/Models/gaze/<backbone>_gaze.onnx`; `DeepGazeModelAvailable` =
      the mobileone_s0 file exists (baseline).
   d. `MobileGazeDetector` nested class (mirror IrisDetector ctor pattern): `new InferenceSession(path, so{ORT_ENABLE_ALL,IntraOpNumThreads=2})`.
      `Estimate(Mat bgrFaceCrop) -> (double yaw, double pitch)`: cvtColor BGR->RGB, resize 448x448, /255,
      ImageNet mean/std, HWC->CHW into float[1*3*448*448], Run requesting outputs "yaw","pitch"; softmax each
      [1,90]; angleDeg = sum(prob_i * i)*4 - 180; return radians. Log InputMetadata/OutputMetadata on ctor.
   e. `ProcessFrame` branch: after face+mesh, if `_activeGazeMode==DeepModel && _deepGaze!=null`: crop the face
      bbox (`_lastFaceRect`/`faceRect`) from `bgr`, `var (y,p) = _deepGaze.Estimate(crop);` then
      `EmitGazeEvents(yaw, pitch)` and `return;` (skip iris path). ELSE existing iris path. Keep two-eye gate
      only on the iris path. NOTE `EmitGazeEvents` already emits `OnRawIris` for calibration capture — so deep
      feature flows to the calibration window unchanged.
   f. On calibration build (`BuildCalibrationPreview` seam impl): pass `_activeGazeMode.ToString()` + backbone to
      the solver so the fit is stamped. On load/apply, set `_activeGazeMode` from `Calibration.FeatureMode` +
      `_deepBackbone` from `Calibration.DeepModel` (flag deep reload). Uncalibrated live preview uses the
      window-selected mode via SetGazePipelineMode.
6. **`CCP.Avalonia/Windows/WebcamCalibrationWindow.axaml(.cs)`**: in `IntroPanel`, add a mode selector
   (Current | Deep model) + a backbone `ComboBox` (visible only when Deep) listing the 5 backbones. On Start,
   call `_webcam.SetGazePipelineMode(...)` + `SetDeepGazeModel(...)` BEFORE sampling. Grey out Deep when
   `!DeepGazeModelAvailable`. New en.json loc keys for labels.
   **OWNER REQ — switch-invalidates-calibration notice:** each mode+backbone has its OWN fit; runtime mode
   strictly follows the LOADED calibration (structural guarantee: outside an active calibrate session
   `_activeGazeMode == Calibration.FeatureMode`, so a mere selection change NEVER makes gaze use an
   uncalibrated pipeline). On selection-changed in the intro, compare the selection to the loaded
   calibration's `FeatureMode`/`DeepModel` and update an inline notice + the Start button label:
   - MATCH → "This pipeline is calibrated and active." (Start = "Recalibrate").
   - MISMATCH/uncalibrated → "⚠ This mode/model isn't calibrated yet — gaze keeps using <current> until you
     calibrate it. Press Start to calibrate it now." (Start = "Calibrate <selection>").
   So switching tiers OR backbone tells the user it won't be used until calibrated and points them at the
   recalibrate action. Loc keys for both notice states + the dynamic Start label.
7. **`Localization/Languages/en.json`**: keys e.g. `webcam_cal_pipeline_label`, `webcam_cal_pipeline_current`,
   `webcam_cal_pipeline_deep`, `webcam_cal_backbone_label`, backbone names.
8. **Assets**: DONE — `Resources/Models/gaze/` (gitignored binaries + committed README/.gitignore/fetch script).
   `installer.iss` add for release (later).
**GATES:** slnf 0 errors · WPF sln 0 · Core tests green · smoke exit 0 (baseline 5 findings). Current path 100%
intact (default mode). Commit `feat(av): deep ONNX gaze pipeline (Tier 2) + backbone dropdown, A/B-selectable`.

### STATUS 2026-07-05: Commit 1 (T0 + Tier 2) SHIPPED = `f25018be`
All gates green: desktop slnf 0 · WPF sln 0 · Core 542/542 · smoke exit 0 (baseline 5 findings, 0 novel) ·
en.json valid. Deep pipeline + backbone dropdown (MobileOne-S0..ResNet-50) selectable in the calibration
window; switch-invalidates notice + baseline restore implemented; Current path untouched. 5 ONNX models
dropped locally (gitignored) for the owner's camera test. **READY FOR OWNER CAMERA A/B/C of Current vs Deep
(all 5 backbones).**

### STATUS 2026-07-06: Commit 2 (Tier 1 improved-classical) SHIPPED
All gates green: desktop slnf 0 · WPF sln 0 · Core 542/542 · smoke exit 0 (0 gaze-related findings; the
2 new S1 `quest_flash_rush_d_*` loc misses are a co-agent's quest work, not this change) · en.json valid.
**As-built:** (1) `Tier1` added to `GazeFeatureMode` (`{Current, Tier1, DeepModel}`; string-persisted so enum
ordinal is irrelevant). (2) FEATURE = roll-normalized iris: in `ProcessFrame`, when Tier1, both eyes' iris
vectors are de-rotated by the head-roll angle `atan2` between the two OUTER eye corners (idx 33 & 263) via
`ComputeEyeRoll`/`RotateFeature` — magnitude-preserving (One-Euro + IrisRange unchanged), same angle for both
eyes (disagreement gate consistent), applied before the gate so calibration capture + runtime train/run on the
SAME feature. Upright ⇒ identity ⇒ matches Current; win shows on head tilt. (3) REGRESSOR = full 3rd-order
cubic (10-coeff, symmetric superset of Cerrolaza) selected in the solver only when `featureMode=="Tier1"`,
stored in the EXISTING `PolynomialFitData.X/Y` as length-10; eval dispatches on `.Length` (6/7 → unchanged,
10 → cubic) in BOTH the solver's `EvalPolynomial` and the tracker's `ProjectGazeToScreen`; `BuildAxisCorrection`
accepts 10; trim guard generalized to `p+5` (7→12 unchanged, 10→15). Cubic-row order is CANONICAL and mirrored
in all three sites: `[1, ix, iy, ix², ixiy, iy², ix³, ix²iy, ixiy², iy³]`. (4) Window: 3rd `CmbGazeMode` item
"Tier 1 (improved classic - roll-corrected)"; mode mapping refactored to a decoupled `ModeOrder` array; reuses
the switch-invalidates notice + baseline restore; loc key `window_webcam_cal_pipeline_tier1`.
**Current/DeepModel 100% intact** (Current byte-identical; DeepModel unaffected). No `WebcamCalibrationData`
schema change (cubic reuses `Polynomial`). **RISK NOTE:** the cubic can overfit a 16-dot grid — if the owner
finds Tier1 worse than Current, the first dials are bumping the ridge λ for the cubic or reverting Tier1's
regressor to Cerrolaza (roll-norm feature alone is the monotonic-safe win). **READY FOR OWNER 3-WAY A/B/C.**

### TURN-KEY CHECKLIST — Commit 2 (Tier 1 classical), advisor sequence [IMPLEMENTED — see status above]
Same feature-agnostic reuse; Tier 1 = a third `GazeFeatureMode` whose FEATURE is an improved iris vector and
whose regressor is upgraded. Minimal first cut = roll/scale-norm feature + cubic-ridge regressor (the two
cheapest, most independent wins; no new capture flow). Smooth-pursuit + fixation-snap are later enhancements.
1. **Core `IWebcamService.cs`**: add `Tier1` (or `NormalizedIris`) to `enum GazeFeatureMode`.
2. **Tracker `ProcessFrame`**: when mode==Tier1, compute the iris vector as today BUT roll-normalize it — rotate
   the iris−cornermid vector by the eye-line tilt θ = atan2(Δy,Δx) between the two outer eye corners (2D only,
   NO solvePnP) and keep the eye-width scale-norm; emit via `EmitGazeEvents` (same path). Apply the SAME
   rotation in calibration capture (it already flows through OnRawIris, so it's automatic).
3. **Solver `WebcamCalibrationSolver`**: add an optional cubic (3rd-order) ridge fit selected when
   featureMode==Tier1; keep 2nd-order Cerrolaza + homography + axis-correction + IrisRange as guards/fallback.
   Runtime `ProjectGazeToScreen` already reads `Polynomial` (7-coeff) — extend to accept a cubic coeff set
   (new length) OR store cubic coeffs in a new nullable field read only in Tier1. Keep 6/7-coeff paths intact.
4. **Window**: add "Tier 1 (improved classic)" as a 3rd `CmbGazeMode` item; same switch-notice logic.
5. Gate identically; commit `feat(av): Tier 1 improved-classical gaze (roll-norm + cubic ridge)`.
**Later Tier-1 enhancements (separate commits):** smooth-pursuit calibration (moving dot + lag-align + Pearson
gate — VERIFY consumer-webcam pursuit feasibility with PARENT web tools first) and fixation snap (I-DT/I-VT
atop One-Euro). **Cadence decision (owner):** build Tier 1 now for a full 3-way, OR camera-test Current-vs-Deep
first then build Tier 1 informed by results.

---

## Backlog — folded from avalonia-calibration-overhaul-port.md (merge fce9713d + dfbe20c4)

> Folded from `avalonia-calibration-overhaul-port.md` 2026-07-10 (that source is deleted after this merge). **Reconciliation:** that source predated the Core DIM seam work above (S1a `bc6eba94`, S1b `5cece1c8`, S1c `df06d06d`), so its headline note that the Avalonia calibration window was a non-functional shell is **resolved** — the window now collects iris samples, fits the polynomial, and commits calibration (S1c). `GazeDriftCorrectionService` is **DONE** (`591e898d`). The per-frame gaze algorithm (the 13 hunks: target lock-on, motion shaping, eye-disagreement gating, iris clamping, mouth/tongue timing) already lives in `AvaloniaWebcamTrackingService`. What remains genuinely open is preserved below.

### Window finalize additions (open — fold into S3 sub-slices)
The calibration flow that *populates* `IrisRange`/`AxisCorrection`/`GazeTrim`/`LightSource` data. The WPF window's finalize additions — **IrisRange min/max capture**, **`BuildAxisCorrection`**, and the **bubble test** (GazeTrim) — layer onto the polynomial-fit pipeline that S1c now provides. Also pending: the **dim-room warning that replaced the lighting picker** (`dfbe20c4`). These map onto S3 above ("gesture warm-up, bubble-test gaze-trim, axis-correction/head-pose comp"); IrisRange min/max finalize and the dim-room warning are the two items S3 did not name explicitly.

### GazeFocusService — `SetGazeAttractor` consumer wiring (+47) (open refinement)
The BASE feature is already ported (`AvaloniaGazeFocusService` rect-based gaze dwell/blink-pop, registered in DI and consumed by `BlinkTrainerTabViewModel`, smoke-tested at port time). The merge delta is a niche refinement: pull the cursor toward the dwell target. Low-value in the Avalonia rect-based pop model (pops everything in a 60-DIP radius — inherently forgiving, unlike WPF's single-best-target precision pop); a faithful port needs a target-presence query (added `IBubbleService`/`IFlashService` surface). `SetGazeAttractor`/`ClearGazeAttractor` exist on the tracker (ported) and `SetGazeAttractor` takes coords in the `OnGazeMove` space, so the wiring itself is trivial once a target-presence query exists.

### GazeDebugCursorService — lock-state visualization (+306) (open refinement)
The BASE `AvaloniaGazeDebugCursorService` is already ported (registered in DI, smoke-tested). The merge delta is lock-state visualization on the debug cursor. Documented refinement.

### Live-webcam verification (release gate — open)
The port mirrors the WPF math exactly (diff-driven, not from memory), compiles clean, and Core tests pass. Still **NOT live-verified with a webcam (no device)** — that remains a release gate for the gaze math. S2 above provides the logged human-verify ritual; the owner camera A/B/C of Current vs Tier 1 vs Deep is the standing proof.

