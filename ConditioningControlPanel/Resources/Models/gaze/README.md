# Deep-gaze ONNX models (Tier 2 "Deep model" gaze pipeline)

The Avalonia webcam tracker can run an appearance-based deep gaze estimator as an
alternative to the classical MediaPipe-iris pipeline. The model is selectable at
calibration time (mode = **Deep model**) with a backbone dropdown.

These weights are **NOT committed to git** (same policy as `vosk/`, `silero-vad/`,
`sherpa-kws/` — see each folder's `.gitignore`). They are:

- dropped into this folder locally for dev/testing (`dotnet run` copies
  `Resources/Models/**` to the output `Resources/Models/`), and
- bundled by the Windows installer for release (`installer.iss`).

## Source & license

Models: **MobileGaze** (`yakhyo/gaze-estimation`, MIT-licensed code, built on
L2CS-Net). Pre-trained ONNX weights are published as GitHub release assets. The
weights are trained on the Gaze360 dataset (research-use dataset); commercial
licensing is the app owner's responsibility — see
`docs/webcam-calibration-port-plan.md` (Route A/B/C audit).

## I/O contract (identical for every backbone)

- Input node: 1 input, shape `[1,3,448,448]`, float32, **RGB**.
  Preprocess a full-face crop: BGR->RGB, resize 448x448, `/255`, normalize with
  ImageNet mean `[0.485,0.456,0.406]` / std `[0.229,0.224,0.225]`, HWC->CHW.
- Output nodes: exactly 2, each `[1,90]` logits. `outputs[0]` = yaw, `outputs[1]`
  = pitch. Decode: softmax -> `sum(prob_i * i) * 4 - 180` degrees -> radians.

## Backbones (dropdown)

| File                     | Backbone     | Size    | Gaze360 MAE | Notes            |
| ------------------------ | ------------ | ------- | ----------- | ---------------- |
| `mobileone_s0_gaze.onnx` | MobileOne-S0 | ~4.8 MB | 12.58       | default, fastest |
| `mobilenetv2_gaze.onnx`  | MobileNet-V2 | ~9.6 MB | 13.07       |                  |
| `resnet18_gaze.onnx`     | ResNet-18    | ~43 MB  | 12.84       |                  |
| `resnet34_gaze.onnx`     | ResNet-34    | ~82 MB  | 11.33       | best accuracy    |
| `resnet50_gaze.onnx`     | ResNet-50    | ~91 MB  | 11.34       |                  |

## Fetch

Run `sh fetch-gaze-models.sh` from this folder (needs `curl`), or download each
asset manually from:
`https://github.com/yakhyo/gaze-estimation/releases/download/weights/<file>`
