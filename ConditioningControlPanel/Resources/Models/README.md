# Webcam Tracking Models

Three MediaPipe ONNX models + one precomputed-anchors JSON, used by
`Services/WebcamTrackingService.cs` for offline face landmark and iris
detection. **All inference runs locally on the CPU** via
`Microsoft.ML.OnnxRuntime`. The app never makes network calls and never
transmits frame data anywhere.

## Required files

| File | Size | Purpose |
|---|---|---|
| `face_detection_short_range.onnx` | ~409 KB | BlazeFace face detector (128×128 input) |
| `face_landmark.onnx`              | ~2.3 MB | FaceMesh 468-point landmark model (192×192 input) |
| `iris_landmark.onnx`              | ~2.5 MB | Iris model — 71 eyelid contour + 5 iris points per eye (64×64 input) |
| `blazeface_anchors.json`          | ~17 KB  | 896 precomputed SSD anchor centers for BlazeFace |

Total bundled-model footprint: ~5.3 MB. ONNX runtime native DLL adds another
~12 MB. Net installer growth over the previous Haar-cascade build: ~17 MB.

## Source

All three .onnx files are mirrored from
[`IntelliProve/face-detection-onnx`](https://github.com/IntelliProve/face-detection-onnx)
(MIT-licensed), which packages clean MediaPipe ONNX exports under
`fdlite/data/`. The download script pins SHA256s so any upstream rotation
is caught at build time.

`blazeface_anchors.json` is **generated locally**, not downloaded — the
anchor-centers math is a port of `_ssd_generate_anchors` from
`fdlite/face_detection.py` with `SSD_OPTIONS_SHORT` (input 128×128,
strides [8, 16, 16, 16], interpolated_scale_aspect_ratio = 1.0). The
generator script is `tools/generate-blazeface-anchors.py`.

## Pipeline contract

| Stage | Input | Output |
|---|---|---|
| **BlazeFace** | full BGR webcam frame, letterbox-resized to 128×128 RGB [-1, 1] | top-1 face bbox in source-frame pixels (sigmoid score > 0.5) |
| **FaceMesh** | 1.5×-expanded SquareLong face crop, resized to 192×192 RGB [0, 1] | 468 landmarks in source-frame pixels (face presence sigmoid > 0.5) |
| **Iris**     | 2.3×-expanded SquareLong eye-corner crop, resized to 64×64 RGB [0, 1] (right eye flipped, output un-flipped) | 71 eyelid contour + 5 iris points per eye in source pixels |

The pipeline replaces the previous Haar-cascade + darkest-pixel approach,
which couldn't deliver stable gaze (Haar's eye box jittered ±5 px frame to
frame) or reliable blink detection (pixel-variance EAR proxy was confused
by glasses, lighting, head angles).

## What we get with this pipeline

- **Face presence + bbox** (BlazeFace).
- **468 face landmarks** (FaceMesh) — used for eye-corner reference frames,
  EAR blink detection, and as the seed crop for the iris model.
- **EAR blink detection** — standard 6-point Eye Aspect Ratio
  (Soukupová & Čech 2016) on FaceMesh's eyelid landmarks (indices
  33/160/158/133/153/144 left, 263/387/385/362/380/373 right). Per-eye
  rolling 90-frame max baseline; blink fires on both-eyes closed→open
  transition with 50–400 ms closed window and 700 ms cooldown.
- **Exact iris-center gaze vector** (Iris model) — the iris landmark at
  index 0 ("CENTER") of the 5 iris points, normalized against the eye-corner
  midpoint and scaled by corner-to-corner distance, gives a head-pose-stable
  gaze vector roughly in [-0.5, +0.5].

## What we don't get

- **Lip landmarks** for mouth-open detection — actually present in
  FaceMesh's 468-point set, but the prototype scope deferred that trigger
  to v2.
- **Iris diameter for distance estimation** — not yet wired up.
- **Pose rotation in iris ROI** — IntelliProve's pipeline rotates the eye
  crop based on corner-to-corner angle. We use axis-aligned crops, which
  costs a small amount of accuracy at large head tilts and is fine for
  typical webcam-at-monitor setups.

## Box 1 / Box 2 implications

| Feature | Status |
|---|---|
| Box 1 — Blink trigger | Works (EAR on FaceMesh eyelid landmarks) |
| Box 1 — Long stare trigger | Works (5-point calibration + iris-center vector) |
| Box 1 — Mouth-open trigger | **Deferred to v2** |
| Box 1 — Stare-to-pop bubble | Works |
| Box 2 — Focus Training (left/right gaze) | Works (most accurate use case) |

## Quick way: run the script

From the `ConditioningControlPanel/` directory:

```powershell
.\tools\download-webcam-models.ps1
```

Downloads the three .onnx files, verifies size + SHA256 against pinned
values, writes `.sha256` sidecars. Use `-Force` to re-download or
`-VerifyOnly` to print hashes of existing files.

To regenerate the anchors JSON:

```bash
python tools/generate-blazeface-anchors.py
```

## Pinned SHA256s

| File | SHA256 |
|---|---|
| `face_detection_short_range.onnx` | `bb171799a4497f9d07ef40c7d08acd9b2dd5e7d80ed00bfd0ef5ab2443aab643` |
| `face_landmark.onnx`              | `71625efd79fd3ce448ba26db9f7f58e4f37daabf36c81a45a661844e3fdb3118` |
| `iris_landmark.onnx`              | `1298780b3c203331d4c6b6e1e2ae6e31c29bdbef6fee777ce72d9a5849df0da7` |
| `blazeface_anchors.json`          | `b547db0bda568fc8863135db2f6ffa0bd82753d4759cfac00a4c2cfb3e12a985` |

## Why bundled, not downloaded by the app

Bundling preserves the privacy contract: **no internet connection is
required or used by the webcam feature when end users run it.** Network
downloads happen only at developer build time, never on the user's machine.

## Privacy guarantees (repeated for clarity)

- Models run **only** when the user has explicitly consented via the
  multi-step consent dialog.
- Frames are processed in RAM and immediately disposed.
- No frame, image, or per-frame coordinate is ever written to disk or
  transmitted over the network.
- Only state strings and counts are ever logged.
- The only persisted webcam-related data is the calibration JSON
  (`%APPDATA%/.../webcam-calibration.json`) — homography coefficients
  and reference iris-vectors, all numbers.

See `Services/WebcamTrackingService.cs` for the source of truth (privacy
contract is enforced by the banner block at the top of that file).

## Backup sources

If `IntelliProve/face-detection-onnx` ever goes dark, the same
**face detector** model is mirrored at
[`unity/inference-engine-blaze-face`](https://huggingface.co/unity/inference-engine-blaze-face)
on HuggingFace (Apache-2.0). For the **face mesh** and **iris** models, no
verified-clean-license public mirror is currently known — accept a pause to
find a new source rather than pulling from a license-questionable repo.

## Committing models to the repo

Both .onnx and .json files live in this folder and are committed directly.
Total ~5.3 MB; trivial repo growth. If we ever add more models or revise
frequently, switch to Git LFS (`git lfs track "*.onnx"`).
