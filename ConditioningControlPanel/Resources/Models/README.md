# Webcam Tracking Models

This folder holds the ONNX models used by `WebcamTrackingService` for offline
face/eye/mouth tracking. **All inference runs locally** — these models never
make network calls and the app never transmits frame data anywhere.

## Required files

| File | Size | Purpose | Source |
|---|---|---|---|
| `blazeface.onnx` | ~200 KB | Face detection (bounding box) | https://github.com/PINTO0309/PINTO_model_zoo (model 030) |
| `face_mesh.onnx` | ~3 MB | 468-point face landmarks | https://github.com/PINTO0309/PINTO_model_zoo (model 032) |
| `iris.onnx` | ~1.5 MB | 5-point iris landmarks per eye | https://github.com/PINTO0309/PINTO_model_zoo (model 033) |

All three are open-source MediaPipe model exports under Apache 2.0 / MIT-style
licenses. Download from the linked PINTO_model_zoo entries (or equivalent
upstream MediaPipe ONNX export).

## Why bundled, not downloaded

Bundling preserves the privacy contract: **no internet connection is required
or used by the webcam feature.** Users can verify this by running the feature
with airplane mode enabled.

## Validation spike (commit #1 in the prototype branch)

Before the rest of the pipeline is built, a one-day spike validates that:

1. `Microsoft.ML.OnnxRuntime` loads each model without error.
2. A static test image yields plausible landmark output.
3. End-to-end CPU inference time per frame is acceptable (target <50 ms).

If this fails or the FPS is unworkable, the fallback path is OpenCvSharp's
Haar cascade + a 68-point landmark ONNX (PFLD or similar) — same pipeline
shape, different upstream model.

## Privacy guarantees (repeat for clarity)

- These models run **only** when the user has explicitly consented via the
  multi-step consent dialog.
- Frames are processed in RAM and immediately disposed.
- No frame, image, or per-frame coordinate is ever written to disk or
  transmitted.
- Only state strings and counts are ever logged.

See `Services/WebcamTrackingService.cs` for the source of truth.
