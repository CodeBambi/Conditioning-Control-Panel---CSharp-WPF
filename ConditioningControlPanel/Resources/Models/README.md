# Webcam Tracking Models

This folder holds the ONNX model used by `WebcamTrackingService` for offline
face/eye tracking. **All inference runs locally** — these models never make
network calls and the app never transmits frame data anywhere.

## Required files

| File | Size | Purpose | Source |
|---|---|---|---|
| `face_detection_yunet.onnx` | ~250 KB | Face detection + 5 keypoints (eyes, nose, mouth corners) | [opencv_zoo](https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet) (official) |

Open-source MIT-licensed model from OpenCV's official model zoo.

## Why YuNet instead of MediaPipe FaceMesh?

The original plan called for three MediaPipe ONNX models (BlazeFace +
FaceMesh + Iris) with 478 landmarks and blendshape outputs. Investigation
revealed that:

- PINTO_model_zoo distributes those models via Google Drive scripts, not
  direct GitHub URLs — no clean PowerShell-friendly path.
- Hugging Face has community ports but provenance and stability vary.
- Converting MediaPipe `.task` files to ONNX requires a Python toolchain.

YuNet is OpenCV's official face detector with a stable Git LFS URL on
GitHub. One small file, one upstream we trust, immediate downloadability.

### What YuNet gives us

- Face bounding box (presence / no-face detection).
- 5 keypoints per face: left eye center, right eye center, nose tip,
  left mouth corner, right mouth corner.

### What YuNet doesn't give us

- Eyelid landmarks (no EAR-based blink — we use eye-region pixel-intensity
  variance instead, which is cruder but functional).
- Lip landmarks (no mouth-open detection — **deferred to v2**).
- Iris landmarks (we approximate via darkest-pixel-in-eye-region heuristic).

### Box 1 / Box 2 implications

| Feature | Status |
|---|---|
| Box 1 — Blink trigger | ✓ Works (heuristic) |
| Box 1 — Long stare trigger | ✓ Works (5-point calibration + pupil heuristic) |
| Box 1 — Mouth-open trigger | **Deferred to v2** (needs lip landmark model) |
| Box 1 — Stare-to-pop bubble | ✓ Works |
| Box 2 — Focus Training (left/right gaze) | ✓ Works (most accurate use case) |

## Quick way: run the script

From the `ConditioningControlPanel/` directory:

```powershell
.\tools\download-webcam-models.ps1
```

The script downloads, verifies file size, computes SHA256, and writes a
`.sha256` sidecar. Re-run with `-Force` to re-download or `-VerifyOnly`
to just print hashes of existing files.

## Why bundled, not downloaded by the app

Bundling preserves the privacy contract: **no internet connection is
required or used by the webcam feature when end users run it.** Network
downloads happen only at developer build time, never on the user's
machine.

## Validation spike

Before the full pipeline is built, the spike validates that:

1. `Microsoft.ML.OnnxRuntime` (or OpenCvSharp's `FaceDetectorYN`) loads
   the model without error.
2. A static test image yields plausible bounding-box + keypoint output.
3. End-to-end CPU inference time per frame is acceptable (<30 ms target).

## Privacy guarantees (repeat for clarity)

- Inference runs **only** when the user has explicitly consented via the
  multi-step consent dialog.
- Frames are processed in RAM and immediately disposed.
- No frame, image, or per-frame coordinate is ever written to disk or
  transmitted.
- Only state strings and counts are ever logged.

See `Services/WebcamTrackingService.cs` for the source of truth.

## Future expansion (v2)

To add mouth-open detection, the cleanest path is to add a second small
ONNX model focused on facial landmarks (e.g., 68-point or PFLD). Candidate
sources:

- [Hugging Face — search "face landmark onnx"](https://huggingface.co/models?search=face+landmark+onnx)
- PFLD ONNX exports from various open-source repos
- DLib's 68-point model (`.dat` format, requires DlibDotNet wrapper)

Whatever's chosen must be small (<5 MB), MIT/Apache-licensed, and have a
stable upstream URL. Update this README and the downloader script when
adding.

## Committing models to the repo

For prototype: commit `face_detection_yunet.onnx` directly. ~250 KB;
trivial repo growth.

For long-term: consider Git LFS (`git lfs track "*.onnx"`) if the model
set grows or revisions become frequent.
