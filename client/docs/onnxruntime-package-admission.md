# ONNX Runtime — Package Admission Record

**Status: NOT ADMITTED.** No project under `client/` references `Microsoft.ML.OnnxRuntime`, nothing
loads a model, and every product run still reports `Unavailable(camera-no-engine)`. This file is the
*record* the admission needs, not the admission: the reference stays the owner's to add.

`client/port.txt:31` requires researching a dependency before selecting it, and every other native
admission in this tree has a document that does it — `client/docs/buttplug-dependency-admission.md`,
`client/docs/audio-backend-spike.md`, `client/docs/video-handoff-spike.md`,
`client/docs/dtrh-admission.md`. The gaze work had none, and the file that looked like one is not:
`client/docs/gaze-engine-admission-spike.md:7` disclaims itself in its own words — *"No code was
written, no dependency added, and nothing is admitted."* That is an **engine** record about model
rights; it says nothing about a **package**, and citing it as one would be citing a disclaimer.

Everything in the tables below was measured on this machine, from the package as it sits in the
local NuGet cache, not read off a listing page. The package is there because the shipping WPF product
already references it (`ConditioningControlPanel/ConditioningControlPanel.csproj:138`), which is also
why measuring it cost nothing and required no reference to be added and reverted.

## 1. Measured here

| Fact | Result | How |
|---|---|---|
| Version examined | `Microsoft.ML.OnnxRuntime` **1.20.1**, the version upstream pins | `ConditioningControlPanel/ConditioningControlPanel.csproj:138`. |
| Licence | **MIT**, `Copyright (c) Microsoft Corporation` | `LICENSE` in the package root, declared as `<license type="file">LICENSE</license>` in the nuspec. |
| Provenance pin | `github.com/Microsoft/onnxruntime` at commit `5c1b7ccbff7e5141c1da7a9d963d660e5741c319`, branch `rel-1.20.1` | the nuspec's `<repository>` and `<releaseNotes>`. |
| Declared dependency | exactly one: `Microsoft.ML.OnnxRuntime.Managed` at the same version | nuspec `<dependencies>`, every target-framework group. |
| The managed package's dependency | exactly one: `System.Memory` **4.5.5** | `microsoft.ml.onnxruntime.managed.nuspec`, `net8.0` group. |
| Managed target frameworks | `net8.0` and `netstandard2.0` (plus iOS/Android/MacCatalyst) | `lib/` in the managed package. `net8.0` runs on `net10.0`. |
| Native payload | 9 runtime identifiers, **181 MB** on disk in total | `du` over the package's `runtimes/`. |

## 2. The platform bar — it passes, and this is the check that decided it

`client/port.txt:33` says a Windows build never establishes Linux support, and
`client/docs/capability-inventory.md` treats a Windows-only path as *not* the capability. So the
first question about any native package here is whether it can run on both, and it is answered by
looking inside the package rather than by trusting a description.

`Microsoft.ML.OnnxRuntime` 1.20.1 ships these `runtimes/` trees:

| RID | Native artifact | Bytes |
|---|---|---|
| `linux-x64` | `libonnxruntime.so` | 16,559,416 |
| `linux-arm64` | `libonnxruntime.so` | 13,648,152 |
| `win-x64` | `onnxruntime.dll` | 11,569,696 |
| `win-x86`, `win-arm64`, `osx-x64`, `osx-arm64`, `android`, `ios` | — | — |

Both Linux RIDs carry a real shared object of a plausible size, side by side with the Windows ones in
one package. Nothing has been RUN on Linux and this document makes no such claim; what it establishes
is that admitting this package does not, by construction, produce a Windows-only capability — which
is exactly the thing the other candidate did.

**`OpenCvSharp4.runtime.win` 4.9.0.20240103 fails that bar by construction.** Its `runtimes/`
directory contains `win-x64` and `win-x86` and nothing else, its own nuspec describes it as the
*"Internal implementation package for OpenCvSharp to work on Windows except UWP"*, and no
`OpenCvSharp4.runtime.linux*` package is present in this machine's cache or referenced by upstream.
A package whose native half exists only for one operating system cannot be half of a cross-platform
capability, and no amount of managed code above it changes that.

## 3. Why OpenCV is refused, measured rather than argued

The refusal rests on how *little* of OpenCV upstream's inference path actually uses. Every `Cv2.*`
call in `Services/Webcam/WebcamTrackingService.cs` — the whole 3,610-line file, enumerated by grep,
not sampled:

| Where | Calls | Status in this port |
|---|---|---|
| Frame probe (`:1393`, `:1405-1406`) | `MeanStdDev`, `Absdiff`, `Mean` | **Already replaced** by `Camera/CameraFrameProbe.cs`, with upstream's constants kept to the digit. |
| Capture loop (`:1629-1630`) | `CvtColor` to grey, `EqualizeHist` | **Not needed.** The greyscale image it produces is consumed only for `.Width`/`.Height` (`:1677`, `:1694-1695`, `:1704`); its pixels are never read again. It is dead work left behind by the retired cascade path. |
| Head pose (`:1894`, `:1899`) | `SolvePnP`, `Rodrigues` | **The one real loss**, named rather than hidden. See below. |
| Mouth classifier (`:2952`, `:2956`) | `FillPoly`, `CvtColor` to HSV | Not ported; a separate behaviour with its own decision to make. |
| **Inference path (`:3190`, `:3372`, `:3531`, `:3536`)** | **three `Resize`, one `Flip`** | **Ported**, as `Gaze/GazePreprocess.cs`. |

That is the entire footprint. The inference path — the part an ONNX admission would need — is four
calls, and they are now about sixty lines of managed arithmetic with no dependency at all: OpenCV's
`INTER_LINEAR` coordinate map, its edge replication, the letterbox pad, the two crop expansions and
the right-eye flip, each cited at its upstream line and each held by a test whose expectation was
derived from upstream's formula rather than from the implementation.

**The loss, stated plainly.** `SolvePnP` and `Rodrigues` are a real numerical routine (an iterative
perspective-n-point solve and a rotation-vector to rotation-matrix conversion) and re-implementing
them is not sixty lines. Head pose is therefore **not** available from this route. That is a
behaviour reduction relative to the WPF product and it belongs to whoever scopes head pose, not to
this file, which records only that refusing OpenCV costs exactly this and nothing else.

## 4. The revisit trigger in the startup/shutdown contract, discharged

`client/docs/startup-shutdown-contract.md:95` records that WPF's `TerminateProcess` workaround is
**not adopted**, and attaches a condition:

> Revisit trigger: the first native dependency admission (LibVLC, OpenCV, ONNX rows) — that row must
> test Release-mode exit for native teardown faults, per the first-attempt Release-native-crash
> lesson.

The lesson it points at is `client/docs/first-attempt-systemic-lessons.md:67`: *"The smoke harness
was Debug-only while native teardown failures occurred in Release"*, with four commits (`:68`)
tracing one intermittent Release native crash that Debug evidence never settled. Its disposition
(`:69`) is blunt: **reject Debug build success as release readiness.**

**The test now exists**: `client/tests/CcpClient.Tests/NativeTeardownExitTests.cs`. It builds a small
probe program `-c Release`, and:

1. proves the process under measurement really is a Release build — from the probe's own
   `AssemblyConfigurationAttribute` and `DebuggableAttribute`, with the same readout applied to the
   Debug test assembly so two different answers show the readout discriminates;
2. loads a native library by name, resolves an export, calls through it, **frees the library**, and
   requires a normal exit code with the whole trail printed, so an exit code of 0 from a process that
   never reached teardown cannot pass;
3. runs the same Release probe again with one argument different, so the call lands on address zero
   and faults *inside* the native library — and requires the harness to report that as a failure
   rather than as success. Without this leg the fact above is "a program that does nothing exits 0".

**A defect this found in itself, recorded because it is the interesting part.** The first version
passed every mutation except one: pointing the build at `-c Debug` left the previous run's Release
apphost in place, the harness ran *that*, and all three facts stayed green while measuring a build
nobody had asked for. It is the same stale-measurement failure `client/tests/floor/check-floor.mjs`
carries an explicit guard against (`:255-264`), arrived at by a different route. The output tree is
now deleted before every build, and the mutation reds.

**What this test does not establish, kept explicit.** It does not load ONNX Runtime — the package is
not admitted, so there is nothing to load. It does not exercise the client's own Release artifact,
which is a publish gate with its own evidence shape (`client/docs/release-publish-gates.md:42`). It
has been run on Windows only; the fault's crash code is *reported* by the test rather than asserted,
and no claim is made here about any other operating system. What it is, is the gate that exists
before the dependency does, so the first Release teardown crash lands on a red test instead of on a
user.

**Why the probe is generated instead of being the product.** Building this test project in Release
costs **1.2 GB** of duplicated output per worktree — measured, not estimated: 569 MB of per-RID
natives, 521 MB of linked web payload, 101 MB of libvlc — for a claim about process exit that a
200 KB console program makes just as well. Redirecting that build with `-o` instead makes all three
projects in the graph write one directory and race each other's file copies (observed: four `MSB3026`
retries on SkiaSharp natives). The generated probe costs about 1 MB in the OS temp directory and
about two seconds per run.

## 5. What landing the reference would still take

None of this is done here, and each item is a real step rather than a formality:

1. **The owner's narrow licence question**, which is about the weights and not this package: whether
   Apache-2.0 reaches a model artifact MediaPipe pins by hash on a CDN rather than stores in the
   repository. `client/THIRD-PARTY-NOTICES.md` §5 states the origin as fact and the terms as
   unresolved, and `client/docs/gaze-model-provenance.md` carries the byte-level chain.
2. **A notices entry for the package itself.** `ThirdPartyNoticesTests` derives the covered set from
   the restored dependency graph rather than from the csproj's direct references, so adding
   `Microsoft.ML.OnnxRuntime` reds that guard until MIT/Microsoft and the two natives are named. That
   is the guard working, not an obstacle.
3. **A decision about the 181 MB of natives for eight RIDs this product will never load.** The
   libvlc precedent is already in `CcpClient.Desktop.csproj`, which trims three architecture trees to
   one and states why; an inference runtime shipping android and ios binaries in a desktop artifact
   deserves the same treatment and the same written reason.
4. **Model files, which this client does not have.** `client/THIRD-PARTY-NOTICES.md` §5 records them
   as pending and not shipped; a runtime with nothing to run is a dependency that earns nothing.

## 6. What this record does NOT establish

- **No Linux run.** Both Linux natives are present in the package; neither has been loaded, on any
  machine, by anything in this repository. The platform bar is a statement about the package's
  contents and about nothing else.
- **No inference.** No `InferenceSession` has been constructed here, no model has been opened, and
  no output tensor has ever been produced by this client. `Gaze/GazePreprocess.cs` produces inputs;
  nothing consumes them yet.
- **No performance claim.** Upstream's session options — `ORT_ENABLE_ALL`, one inter-op thread, two
  intra-op threads (`Services/Webcam/WebcamTrackingService.cs:3133-3138`) — are recorded as what
  upstream uses, not as anything measured here.
- **No admission.** The reference is not in any `.csproj` under `client/`, and this document does not
  put it there.

## Sources

- The package as cached on this machine: `microsoft.ml.onnxruntime/1.20.1` and
  `microsoft.ml.onnxruntime.managed/1.20.1` — nuspec, `LICENSE`, `lib/`, `runtimes/`.
- `opencvsharp4.runtime.win/4.9.0.20240103` — nuspec and `runtimes/`.
- `ConditioningControlPanel/ConditioningControlPanel.csproj:136-138` — upstream's own three
  references, and the versions this record examined.
- `ConditioningControlPanel/Services/Webcam/WebcamTrackingService.cs` — the `Cv2.*` enumeration in §3.
