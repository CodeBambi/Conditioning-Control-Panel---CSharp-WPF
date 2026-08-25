# Gaze Model Provenance — The Hash Comparison Nobody Had Run

The task board's P0 gaze row corrected its own licence claim three times, and each correction
narrowed to the same residue, recorded in the row's own words: *"the blocking step is a hash
comparison nobody has run."* This document is that comparison. Everything below was downloaded and
hashed on **2026-08-25**; nothing is quoted from a page.

**Downloads went outside the repository.** No `.tflite` was added to either tree, no package was
added, and the dependency remains refused. This discharges condition 3 of the row's dependency
decision; it does not admit anything.

## 1. What was being asked

The three `.onnx` files the WPF product commits under `ConditioningControlPanel/Resources/Models/`
arrive through two intermediaries:

```
Google MediaPipe (.tflite on storage.googleapis.com)
   └─> patlevin/face-detection-tflite     (fdlite/data/*.tflite, MIT — a CODE licence)
         └─> IntelliProve/face-detection-onnx  (fdlite/data/*.onnx, MIT — the same code licence)
               └─> ConditioningControlPanel/Resources/Models/*.onnx  (committed)
```

Neither intermediary asserts anything about the weights. The board row established that from primary
sources and stopped there, because *whether these bytes are Google's at all* had never been checked —
only that a mirror served them (`ConditioningControlPanel/Resources/Models/README.md:104-109` pins
the MIRROR, and `ConditioningControlPanel/tools/download-webcam-models.ps1:63` pins the mirror's URL).

## 2. Google's own artifacts, verified against Google's own pins

MediaPipe does not store the weights in its repository; `third_party/external_files.bzl` fetches them
from `storage.googleapis.com` and pins each by sha256. That file is auto-generated and its line
numbers move, so each row below names the `http_file` **rule name**, which does not.

Fetched from `google-ai-edge/mediapipe@master`, `third_party/external_files.bzl` (75,753 bytes,
217 `http_file` rules).

| `http_file` rule | Artifact path under `mediapipe-assets/` | sha256 the .bzl pins | sha256 downloaded | Bytes |
|---|---|---|---|---|
| `com_google_mediapipe_modules_face_detection_face_detection_short_range_tflite` | `modules/face_detection/face_detection_short_range.tflite` | `3bc182eb9f33925d9e58b5c8d59308a760f4adea8f282370e428c51212c26633` | **identical** | 229,032 |
| `com_google_mediapipe_modules_face_landmark_face_landmark_tflite` | `modules/face_landmark/face_landmark.tflite` | `c603fa6149219a3e9487dc9abd7a0c24474c77263273d24868378cdf40aa26d1` | **identical** | 1,241,896 |
| `com_google_mediapipe_models_face_landmark_tflite` | `models/face_landmark.tflite` | `2efcb4f4de43c7614b80a3cc3e8a37354b3b3b40f75cce20f6f38f0f25d65493` | **identical** | 2,439,440 |
| `com_google_mediapipe_modules_iris_landmark_iris_landmark_tflite` | `modules/iris_landmark/iris_landmark.tflite` | `d1744d2a09c25f501d39eba4faff47e53ecca8852c5ce19bce8eeac39357521f` | **identical** | 2,640,568 |
| `com_google_mediapipe_models_iris_landmark_tflite` | `models/iris_landmark.tflite` | `d1744d2a09c25f501d39eba4faff47e53ecca8852c5ce19bce8eeac39357521f` | (same bytes as the row above) | 2,640,568 |

Four downloads, four exact matches against Google's own pinned digests. So the artifacts used for
every comparison below are Google-published bytes, established by Google's own hash file rather than
by trusting a CDN response.

## 3. THE TRAP, AND IT POINTS THE OTHER WAY

The dependency decision warned that `face_landmark.tflite` exists under both `models/` and `modules/`
with different sha256, and instructed: **use the `modules/` one**, because comparing the wrong one
yields a false negative.

**The warning about the trap is right. Its direction is backwards, and following it literally would
have produced exactly the false negative it warned about.** Measured:

- `modules/face_landmark/face_landmark.tflite` — `c603fa61…`, **1,241,896 bytes**
- `models/face_landmark.tflite` — `2efcb4f4…`, **2,439,440 bytes**
- patlevin's `fdlite/data/face_landmark.tflite` — `2efcb4f4…`, **2,439,440 bytes**

patlevin ships the **`models/`** variant. The `modules/` file is a different, roughly half-sized model
and matches nothing in the chain. Had this check compared only against `modules/`, it would have
reported a mismatch — and this row's own standing instruction for a mismatch is *"refuse and stop"*,
so the wrong side of a two-sided trap would have stopped the row on a false finding.

The trap is also worse than two-sided. `face_landmark.tflite` has **three** distinct Google-pinned
digests and `face_detection_short_range.tflite` has **two**:

| Basename | `models/` | `modules/` | `tasks/testdata/vision/` |
|---|---|---|---|
| `face_landmark.tflite` | `2efcb4f4…` ← the chain | `c603fa61…` | `1055cb9d4a9ca8b8c688902a3a5194311138ba256bcc94e336d8373a5f30c814` |
| `face_detection_short_range.tflite` | — | `3bc182eb…` ← the chain | `bbff11cebd1eb27a1e004cae0b0e63ec8c551cbf34a4451148b4908b8db3eca8` |
| `iris_landmark.tflite` | `d1744d2a…` ← the chain | `d1744d2a…` (same) | — |

**Rule for anyone repeating this: never pick a MediaPipe artifact by basename. Compare against every
pinned variant of that name and let the bytes choose.** That is what was done here.

## 4. Google `.tflite` → patlevin `.tflite`: EXACT, all three

| patlevin `fdlite/data/` file | sha256 | Bytes | Equals which Google artifact |
|---|---|---|---|
| `face_detection_short_range.tflite` | `3bc182eb9f33925d9e58b5c8d59308a760f4adea8f282370e428c51212c26633` | 229,032 | `modules/face_detection/face_detection_short_range.tflite`. |
| `face_landmark.tflite` | `2efcb4f4de43c7614b80a3cc3e8a37354b3b3b40f75cce20f6f38f0f25d65493` | 2,439,440 | `models/face_landmark.tflite`. |
| `iris_landmark.tflite` | `d1744d2a09c25f501d39eba4faff47e53ecca8852c5ce19bce8eeac39357521f` | 2,640,568 | `modules/iris_landmark/iris_landmark.tflite` and `models/iris_landmark.tflite`, which are the same bytes. |

Three for three, byte-identical (also confirmed with a direct `cmp` on the detector). **patlevin
repackages Google's published artifacts unmodified.** That link is now a fact.

## 5. IntelliProve `.onnx` → the committed `.onnx`: EXACT, all three

The mirror was re-fetched today and hashed against the bytes this repository actually commits.

| File | Committed sha256 | IntelliProve today | `Resources/Models/README.md` pin | Bytes |
|---|---|---|---|---|
| `face_detection_short_range.onnx` | `bb171799a4497f9d07ef40c7d08acd9b2dd5e7d80ed00bfd0ef5ab2443aab643` | identical | identical | 418,536 |
| `face_landmark.onnx` | `71625efd79fd3ce448ba26db9f7f58e4f37daabf36c81a45a661844e3fdb3118` | identical | identical | 2,428,793 |
| `iris_landmark.onnx` | `1298780b3c203331d4c6b6e1e2ae6e31c29bdbef6fee777ce72d9a5849df0da7` | identical | identical | 2,627,277 |

This also closes a smaller thing nobody had checked: the pins in
`ConditioningControlPanel/Resources/Models/README.md:104-109` are honest about the bytes actually in
the tree, and the mirror has not rotated.

## 6. The gap the dependency decision expected to leave — and how far it actually closes

A `.onnx` is a **conversion** of a `.tflite`, not a copy, so no hash can ever join sections 4 and 5.
The decision accepted that and asked only for the chain up to patlevin.

The gap narrows further than that, because a conversion is not an erasure. `tf2onnx` transposes
convolution kernels NHWC→NCHW, but it copies 1-D tensors (biases, normalisation parameters) through
unchanged — so if the committed `.onnx` really was converted from these exact weights, byte windows
from the Google `.tflite` must still be findable inside it. If it was converted from *different*
weights, they cannot be.

Method: sample 24-byte windows (6 float32) at a fixed stride across each `.tflite`, discard
low-entropy windows (fewer than 12 distinct byte values — a zero run would match by luck), and search
the committed `.onnx` for each window verbatim.

| Comparison | Windows found | Rate |
|---|---|---|
| `models/face_landmark.tflite` → committed `face_landmark.onnx` | 120 / 397 | **30.2%** |
| `modules/iris_landmark.tflite` → committed `iris_landmark.onnx` | 202 / 397 | **50.9%** |
| `modules/face_detection_short_range.tflite` → committed `face_detection_short_range.onnx` | 0 / 363 | 0.0% |
| CONTROL: `modules/face_landmark.tflite` (the trap variant) → committed `face_landmark.onnx` | 0 / 394 | 0.0% |
| CONTROL: `iris_landmark.tflite` → committed `face_detection_short_range.onnx` | 0 / 397 | 0.0% |

The controls are the point: the same method run against the wrong model finds **nothing**, so a
30–51% hit rate is not an artefact of the sampling.

**The detector's 0% is explained, not waved away.** Its `.tflite` is 229,032 bytes and its `.onnx` is
418,536 — a ratio of 1.83, which is what a float16→float32 widening looks like after container
overhead. float16→float32 is exact, so the hypothesis is directly testable: widen each 12-byte
float16 window from the `.tflite` to its 24-byte float32 form and search for *that*.

| Comparison | Windows found | Rate |
|---|---|---|
| `face_detection_short_range.tflite`, widened → committed `face_detection_short_range.onnx` | 154 / 361 | **42.7%** |
| CONTROL: `iris_landmark.tflite`, widened → committed `face_detection_short_range.onnx` | 0 / 396 | 0.0% |
| CONTROL: `face_detection_short_range.tflite`, widened → committed `iris_landmark.onnx` | 0 / 361 | 0.0% |

Corroborating and independent: all three committed `.onnx` files carry the producer string
`tf2onnx 1.16.1 15c810` in their protobuf header — one tool, one build of it, one conversion batch.

## 7. What this establishes, and what it does not

**Established.**

1. The three `.tflite` artifacts patlevin redistributes are Google's published MediaPipe artifacts,
   byte-for-byte, verified against Google's own pinned digests.
2. The three `.onnx` files this repository commits are the IntelliProve mirror's bytes, byte-for-byte,
   and match the pins the WPF README already carried.
3. The committed `.onnx` weights **are the Google weights**, demonstrated at byte level across the
   conversion with four negative controls at zero. They were converted, not retrained, not
   substituted, and not fine-tuned — a fine-tune would perturb every value and leave no verbatim run.

So the origin question this row has argued about three times is answered: **these are MediaPipe
weights.** Nobody in the chain invented, replaced or adapted them.

**NOT established, and none of it is a hash's job.**

- **Nothing about licence terms.** Byte provenance establishes *where the weights came from*. It says
  nothing about whether MediaPipe's Apache-2.0 `LICENSE` reaches an artifact the repository fetches
  from a CDN rather than stores. That remains an owner question — but it is now the *narrow* one the
  dependency decision predicted, asked about a known artifact rather than an unknown one.
- **Nothing about training data.** No corpus is named by Google for these three models, and none was
  looked for here. The row's existing asymmetry stands unchanged: the four engines this port
  disqualified have an affirmatively non-commercial corpus (CC BY-NC-SA); these have *no identified
  corpus either way*. Unproven is not disproven, and it is also not proven.
- **Nothing about the dependency.** No package was added, no model file was added, no inference code
  exists, and every product run still reports `Unavailable(camera-no-engine)`.
- **Nothing about the client.** The `.onnx` files live in the WPF tree only. `client/` redistributes
  none of them today; see `client/THIRD-PARTY-NOTICES.md`, which records them as **pending** rather
  than shipped.

## 8. Reproducing this

Nothing here is committed as a test, and that is deliberate: every step needs network access and
6 MB of downloads that this port refuses to add. What *is* on the floor is the part that can rot
locally — `ThirdPartyNoticesTests` re-derives the three committed `.onnx` digests from the WPF tree
on every run and fails if `client/THIRD-PARTY-NOTICES.md` states a digest those bytes do not have.

To repeat the network half: fetch `third_party/external_files.bzl` from `google-ai-edge/mediapipe`,
take the `urls` and `sha256` from each `http_file` rule named in section 2, download and verify, then
hash `fdlite/data/*.tflite` from `patlevin/face-detection-tflite@main` and compare. Expect the digests
in sections 2 and 4. If a digest has moved, upstream rotated an artifact and **that is a finding, not
a reason to update this document to match**.
