# Webcam Tracking Models

This folder holds the ONNX models used by `WebcamTrackingService` for offline
face/eye/mouth tracking. **All inference runs locally** — these models never
make network calls and the app never transmits frame data anywhere.

## Required files

The pipeline expects exactly these filenames:

| File | Size | Purpose |
|---|---|---|
| `blazeface.onnx` | ~200 KB | Face detection (bounding box) |
| `face_mesh.onnx` | ~3 MB | 468-point face landmarks (no iris, no blendshapes) |
| `iris.onnx` | ~1.5 MB | 5-point iris landmarks per eye |

All three are open-source MediaPipe model exports under Apache 2.0 / BSD-style
licenses. Their derivative uses are well-established in the OSS community.

## Quick way: run the script

From the `ConditioningControlPanel/` directory:

```powershell
.\tools\download-webcam-models.ps1
```

The script downloads all three, computes SHA256 hashes, and writes `.sha256`
sidecars next to each `.onnx`. Re-run with `-Force` to re-download or
`-VerifyOnly` to just print hashes of existing files.

If the script fails (404, file too small, etc.), the URLs in the upstream
community repo have moved. See "Alternative sources" below.

## Alternative sources

If the script's primary URLs fail, the same models are mirrored at:

1. **PINTO_model_zoo** (most common, well-curated)
   - GitHub: <https://github.com/PINTO0309/PINTO_model_zoo>
   - Folders: `030_BlazeFace/`, `032_FaceMesh/`, `033_Iris/`
   - Each folder has its own `download.sh` or release artifacts; ONNX exports
     usually live under a `*_192x192/` or similar resolution sub-folder.

2. **Hugging Face Hub**
   - Search: <https://huggingface.co/models?search=mediapipe+face>
   - Look for ONNX-format face mesh / face detection / iris models from
     well-followed accounts (Xenova, onnx-community, etc.).

3. **MediaPipe official `.task` file → ONNX conversion**
   - Google ships `.task` bundles (TFLite + metadata) at
     <https://storage.googleapis.com/mediapipe-models/face_landmarker/>
   - Convert to ONNX with `tf2onnx` if you need the highest-fidelity source.
     Requires Python; one-time conversion.

After downloading by hand, put the file at exactly:

```
ConditioningControlPanel/Resources/Models/<filename>.onnx
```

Then re-run `tools\download-webcam-models.ps1 -VerifyOnly` to confirm and
print SHA256.

## Why bundled, not downloaded by the app

Bundling preserves the privacy contract: **no internet connection is required
or used by the webcam feature when end users run it.** Users can verify by
running with airplane mode on. Network downloads happen only at developer
build time, never on the user's machine.

## Validation spike (commit #1 in the prototype branch)

Before the full pipeline is built, a one-day spike validates that:

1. `Microsoft.ML.OnnxRuntime` loads each model without error.
2. A static test image yields plausible landmark output.
3. End-to-end CPU inference time per frame is acceptable (target <50 ms).

If this fails or FPS is unworkable, the fallback path is OpenCV's built-in
DNN face detector (res10 SSD, distributed by OpenCV itself) + a 68-point
landmark ONNX (PFLD or similar).

## Privacy guarantees (repeat for clarity)

- These models run **only** when the user has explicitly consented via the
  multi-step consent dialog.
- Frames are processed in RAM and immediately disposed.
- No frame, image, or per-frame coordinate is ever written to disk or
  transmitted.
- Only state strings and counts are ever logged.

See `Services/WebcamTrackingService.cs` for the source of truth.

## Committing models to the repo

For prototype: commit them directly. ~5 MB total; acceptable repo growth.

For long-term: consider Git LFS (`git lfs track "*.onnx"`) if the model set
grows or revisions become frequent.
