# Webcam Tracking Models

This folder holds the OpenCV Haar cascade XMLs used by `WebcamTrackingService`
for offline face and eye detection. **All inference runs locally** — these
models never make network calls and the app never transmits frame data
anywhere.

## Required files

| File | Size | Purpose | Source |
|---|---|---|---|
| `haarcascade_frontalface_default.xml` | ~900 KB | Face detection | [opencv/data/haarcascades](https://github.com/opencv/opencv/tree/master/data/haarcascades) |
| `haarcascade_eye.xml` | ~333 KB | Eye detection within face ROI | same as above |

Both are MIT-licensed, ship with OpenCV itself, and have been the standard
classical-CV face/eye detection pipeline for over 20 years.

## Why Haar cascades

The plan went through three model-strategy iterations:

1. **v1: BlazeFace + FaceMesh + Iris (3 ONNX models, MediaPipe)** —
   PINTO_model_zoo distributes these via Google Drive scripts, not direct
   GitHub URLs. No clean PowerShell-friendly download path.
2. **v2: YuNet ONNX (single model)** — clean upstream URL on opencv_zoo
   exists, but `OpenCvSharp.FaceDetectorYN` wrapper isn't in OpenCvSharp4
   v4.9, and manually decoding YuNet's multi-tensor output is non-trivial.
3. **v3: Haar cascades** (this version) — `OpenCvSharp.CascadeClassifier`
   IS in v4.9, the API is trivial, the XMLs come from the canonical OpenCV
   repo, and accuracy is sufficient for our needs.

For a prototype that needs to work today on a clean codebase, Haar
cascades are the pragmatic choice. We can revisit when v2 features (like
mouth-open detection or higher-precision gaze) genuinely need a different
backbone.

## What we get with Haar

- Face bounding box (presence detection).
- 0–2 eye bounding boxes within the face ROI. Largest two = left/right
  eye, classified by X position.

## What we don't get

- Eyelid landmarks (no EAR-based blink — we use eye-region pixel-intensity
  variance instead).
- Iris landmarks (we approximate with darkest-pixel-in-eye-region).
- Lip landmarks (no mouth-open detection — **deferred to v2**).
- 478-point facial mesh (overkill for our needs anyway).

## Box 1 / Box 2 implications

| Feature | Status |
|---|---|
| Box 1 — Blink trigger | Works (intensity-variance heuristic) |
| Box 1 — Long stare trigger | Works (5-point calibration + pupil heuristic) |
| Box 1 — Mouth-open trigger | **Deferred to v2** (needs lip landmark model) |
| Box 1 — Stare-to-pop bubble | Works |
| Box 2 — Focus Training (left/right gaze) | Works (most accurate use case) |

## Quick way: run the script

From the `ConditioningControlPanel/` directory:

```powershell
.\tools\download-webcam-models.ps1
```

The script downloads both XMLs, verifies size, computes SHA256, and
writes `.sha256` sidecars. Re-run with `-Force` to re-download or
`-VerifyOnly` to print hashes of existing files.

## Why bundled, not downloaded by the app

Bundling preserves the privacy contract: **no internet connection is
required or used by the webcam feature when end users run it.** Network
downloads happen only at developer build time, never on the user's
machine.

## Privacy guarantees (repeat for clarity)

- These cascades run **only** when the user has explicitly consented via
  the multi-step consent dialog.
- Frames are processed in RAM and immediately disposed.
- No frame, image, or per-frame coordinate is ever written to disk or
  transmitted.
- Only state strings and counts are ever logged.

See `Services/WebcamTrackingService.cs` for the source of truth.

## Future expansion (v2)

To add mouth-open detection or higher-precision facial landmarks, swap
or supplement the cascade pipeline with a small ONNX landmark model.
Candidate sources:

- PFLD (Practical Facial Landmark Detector), 68-point ONNX (~5 MB).
- DLib's 68-point shape predictor (`.dat` format, requires DlibDotNet
  wrapper).
- A 5-point ONNX regressor (lightweight, ~1 MB).

Whatever's chosen must be small (<5 MB), MIT/Apache-licensed, and have
a stable upstream URL. Update this README and the downloader script when
adding.

## Committing models to the repo

For prototype: commit both XMLs directly. ~1.2 MB total; trivial repo
growth.

For long-term: consider Git LFS (`git lfs track "*.xml"`) if the model
set grows or revisions become frequent.
