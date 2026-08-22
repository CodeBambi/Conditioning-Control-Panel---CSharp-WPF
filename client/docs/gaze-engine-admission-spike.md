# Third-Party Gaze Engine — Admission Spike

Evidence for the owner decision A-011 requires before a second gaze engine can be admitted:
*"provider/model, local or remote execution, commercial weight and training-data rights,
performance, packaging, and Windows/Linux behavior."*

This is a research record. **No code was written, no dependency added, and nothing is admitted.**

## The headline

Every mainstream appearance-based gaze model examined fails A-011's commercial-rights bar — and
**not on its code licence, on its training data**. The code is often permissive while the weights
are derived from datasets whose terms forbid commercial use, including derivative models. A project
can be BSD-licensed and still ship weights this product may not use.

That distinction is the whole finding. A-011 asks for "commercial weight **and** training-data
rights" as two things, and the second is what disqualifies the field.

## What the shipping product already uses

The WPF product's engine is itself a third-party deep-learning stack, run fully locally:
ONNX Runtime + OpenCvSharp driving MediaPipe **BlazeFace**, **FaceMesh** (468 landmarks) and
**Iris** (71 eye-contour + 5 iris points), all shipped in the installer with *"No internet at
runtime"* (`Services/Webcam/WebcamTrackingService.cs:41-47`). Gaze comes from the iris-centre
landmark; blink from an Eye Aspect Ratio over the iris model's eyelid contour with a 90th-percentile
baseline and hysteresis.

MediaPipe is Apache-2.0 on **both code and model weights**, which permits commercial use. That is
almost certainly why the shipping product landed there, and it makes the incumbent the only option
in this document with clean rights.

## The candidates, and why each fails

| Engine | Code licence | Weights trained on | Commercially usable | Blocking fact |
|---|---|---|---|---|
| **MediaPipe** (incumbent) | Apache-2.0 | Google-internal | **YES** | — already shipping |
| **L2CS-Net** | permissive code | Gaze360 | **NO** | Gaze360's licence names *"models trained on dataset, other derivative works"* as covered, and restricts to non-commercial research |
| **PureGaze / GazeTR** | permissive code | ETH-XGaze | **NO** | ETH-XGaze is CC BY-NC-SA 4.0 |
| **OpenSeeFace** (gaze model) | BSD 2-clause | MPIIGaze + UnityEyes synthetic | **NO** for gaze | MPIIGaze is CC BY-NC-SA 4.0; the BSD code licence does not launder the weights |
| **OpenFace 2.0** | research licence | — | **NO** | non-commercial by its own terms |

OpenSeeFace deserves a note because it is the closest call: BSD 2-clause, ONNX, MobileNetV3, CPU
30–60 fps, and it already emits head pose, eye gaze, blink and mouth-open — a near-exact match for
the semantic event contract. Its *face landmark* models are trained on LS3D-W/WFLW/WIDER FACE; its
**gaze and blink model is trained on MPIIGaze**, which is the non-commercial one. Its README also
states some *"additional custom data"* used in training is *"not redistributable."*

## What this leaves the owner

1. **Do not admit a second engine.** Close A-011's alternative as unavailable with this evidence
   recorded. The local engine is already deep-learning, already commercially licensed, and already
   the default. This is the honest outcome of the spike as run, and it costs nothing.
2. **Buy a commercial licence.** Tobii, Pupil Labs and Seeing Machines sell commercial gaze SDKs.
   Not researched here: price, Linux support, whether they require their own hardware, and whether
   any permits redistribution inside a desktop app.
3. **Train on permissive or synthetic data.** UnityEyes is synthetic, and synthetic corpora avoid
   the consent and licence problems entirely. This is a research project, not a port task.
4. **Request a commercial grant.** Several of these authors grant commercial terms on request. A
   grant would need to be in writing and recorded here before anything is admitted.

## What was NOT established

- **Performance and packaging were not measured for any candidate.** They were not reached: the
  rights question disqualified them first, and measuring a model this product may not ship would be
  work spent on an answer that cannot change the outcome.
- **Linux behaviour was not tested for any candidate**, for the same reason. OpenSeeFace's README
  documents Windows specifics and does not state Linux support either way.
- **Commercial SDK pricing and terms were not investigated.** That is option 2 and needs an owner
  who wants it before it is worth the time.
- **The incumbent's Apache-2.0 weight licence was taken from Google's published terms, not from a
  legal review.** The shipping product already redistributes these models, so this risk is
  pre-existing and owned by the WPF product rather than introduced by the port — but it is a
  statement about a licence, and a licence question is ultimately the owner's.

## Standing requirements if anything is ever admitted

From A-011 and the board row, unchanged by this spike: the persisted engine names its exact
model/version and execution location; calibration is keyed to camera, monitor, engine, model
hash/version, feature and preprocessing; switching engines invalidates incompatible positional
calibration and **never silently falls back**; a remote engine needs separate owner approval before
any frame, crop, landmark or biometric derivative leaves the device.

The no-silent-fallback rule is not hypothetical. The first Avalonia attempt registered a real
Windows tracker only, gave Linux a stub, and let deep-model failure fall back to iris *despite a
calibration feature mismatch* — the user kept a gaze feature that was quietly measuring something
else.

## Sources

- [Gaze360 licence](https://github.com/erkil1452/gaze360/blob/master/LICENSE.md) — non-commercial, explicitly covering models trained on the dataset
- [ETH-XGaze](https://github.com/xucong-zhang/ETH-XGaze) — CC BY-NC-SA 4.0
- [MPIIGaze](https://www.mpi-inf.mpg.de/departments/computer-vision-and-machine-learning/research/gaze-based-human-computer-interaction/appearance-based-gaze-estimation-in-the-wild/) — CC BY-NC-SA 4.0, non-commercial scientific purposes only
- [OpenSeeFace README](https://github.com/emilianavt/OpenSeeFace/blob/master/README.md) — BSD 2-clause code and models; gaze/blink model trained on MPIIGaze
- [MediaPipe Face Landmarker](https://ai.google.dev/edge/mediapipe/solutions/vision/face_landmarker) — Apache-2.0
- [L2CS-Net paper](https://arxiv.org/pdf/2203.03339) — trained on Gaze360/MPIIGaze
