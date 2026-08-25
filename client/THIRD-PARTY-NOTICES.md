# Third-Party Notices — CCP Client

The greenfield client redistributes third-party software. This file carries the attribution and
licence notices that obligation requires, and it is the first such file in this repository: neither
the shipping WPF product nor the abandoned first Avalonia attempt has one. It is written for the
client tree only.

Everything below was read from the artifact itself — a package's own `.nuspec`, its own bundled
`LICENSE`, or the file header in the bundled source — on **2026-08-25**. Nothing is quoted from a
project website.

## Scope, and how completeness is enforced rather than claimed

A notices file that covers some dependencies is worse than none, because it looks complete. Two
mechanisms keep this one honest, both of which fail loudly instead of drifting:

1. **The managed list is derived, not curated.** `CcpClient.Tests/ThirdPartyNoticesTests` reads the
   restored dependency graph of the shipping project (`obj/project.assets.json`, the full transitive
   closure — 50 packages today, not the 9 direct references) and fails if any package id is not named
   in this file. A lane that adds a dependency and forgets this file gets a red floor.
2. **The redistributed web payloads are swept, not remembered.** The same test walks every `vendor/`
   directory inside the six upstream web payload trees the project globs into `payload/` and fails on
   any library component this file does not name.

What those two mechanisms do **not** cover, stated rather than implied: third-party code bundled
*inside* a package's native binaries (Skia inside `libSkiaSharp`, the VLC plugin tree, miniaudio
inside SoundFlow) cannot be enumerated by walking this repository. Those are listed by hand in §3
and §4 from the packages' own metadata, and a new one arriving inside an existing package version
bump would not be caught automatically.

---

## 1. Managed packages

Every package in the shipping project's transitive closure. Licence text is the SPDX expression each
package declares in its own `.nuspec`, except where the package ships a licence *file* instead — those
four rows say which file was read.

### Avalonia UI — MIT

Copyright 2013-2026 © The AvaloniaUI Project; `Avalonia.Controls.WebView` is Copyright 2019-2026 ©
AvaloniaUI OÜ. <https://avaloniaui.net/>

`Avalonia`, `Avalonia.BuildServices`, `Avalonia.Controls.WebView`, `Avalonia.Desktop`,
`Avalonia.FreeDesktop`, `Avalonia.FreeDesktop.AtSpi`, `Avalonia.HarfBuzz`, `Avalonia.Native`,
`Avalonia.Remote.Protocol`, `Avalonia.Skia`, `Avalonia.Themes.Fluent`, `Avalonia.Win32`,
`Avalonia.X11`.

`Avalonia.Angle.Windows.Natives` is packaged by the Avalonia project but declares a licence **file**
rather than an SPDX expression, and that file is not Avalonia's — see §3.

### MicroCom — MIT

`MicroCom.Runtime`, Copyright 2021 © Nikita Tsukanov.

### SkiaSharp and HarfBuzzSharp — MIT

© Microsoft Corporation. All rights reserved.

`SkiaSharp`, `SkiaSharp.NativeAssets.Linux`, `SkiaSharp.NativeAssets.macOS`,
`SkiaSharp.NativeAssets.WebAssembly`, `SkiaSharp.NativeAssets.Win32`, `HarfBuzzSharp`,
`HarfBuzzSharp.NativeAssets.Linux`, `HarfBuzzSharp.NativeAssets.macOS`,
`HarfBuzzSharp.NativeAssets.WebAssembly`, `HarfBuzzSharp.NativeAssets.Win32`.

The MIT expression covers the managed bindings. The native libraries these packages carry wrap
third-party C/C++ projects under their own terms — see §3.

### Microsoft .NET extension libraries — MIT

© Microsoft Corporation. All rights reserved. <https://dot.net/>

`Microsoft.Extensions.AI.Abstractions`, `Microsoft.Extensions.Caching.Abstractions`,
`Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection`,
`Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Diagnostics.Abstractions`,
`Microsoft.Extensions.FileProviders.Abstractions`, `Microsoft.Extensions.Hosting.Abstractions`,
`Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`,
`Microsoft.Extensions.Primitives`.

### Model Context Protocol C# SDK — Apache-2.0

© Model Context Protocol, a Series of LF Projects, LLC. <https://csharp.sdk.modelcontextprotocol.io/>

`ModelContextProtocol`, `ModelContextProtocol.AspNetCore`, `ModelContextProtocol.Core`.

Apache-2.0 §4 requires the licence to travel with redistribution; the full text is at
<https://www.apache.org/licenses/LICENSE-2.0>.

### Keincheck — MIT

Keincheck / DVS Productions. <https://github.com/DVSProductions/Keincheck>

`Keincheck`, `Keincheck.Avalonia`, `Keincheck.Client`, `Keincheck.Core`, `Keincheck.Protocol`.

### Buttplug — BSD 3-Clause

Copyright (c) 2016-2024, Nonpolynomial Labs, LLC. <https://buttplug.io/>

`Buttplug`. The `.nuspec` declares a licence **file**; the bundled `LICENSE` reads *"Buttplug C# is
covered under the following BSD 3-Clause License"*. BSD-3-Clause requires the copyright notice, the
condition list and the disclaimer to be reproduced in binary redistribution, and forbids using the
names of the copyright holder or contributors to endorse derived products without written permission.

### Json.NET — MIT

Copyright © James Newton-King 2008. <https://www.newtonsoft.com/json>

`Newtonsoft.Json`. Arrives transitively through Buttplug only; the client's own code uses
`System.Text.Json`.

### SoundFlow — MIT

Copyright (c) 2025 LSXPrime. <https://github.com/LSXPrime/SoundFlow>

`SoundFlow`. The `.nuspec` declares a licence **file**; the bundled `LICENSE.md` is the MIT License.
Bundles a native audio backend — see §3.

### Tmds.DBus — MIT

Tom Deseyn. `Tmds.DBus.Protocol`. Arrives transitively through `Avalonia.FreeDesktop` (Linux D-Bus).

### VideoLAN — LGPL-2.1-or-later

`LibVLCSharp`, `VideoLAN.LibVLC.Windows`. VideoLAN. **These two carry obligations the rest of this
file does not; §4 states them.**

---

## 2. Web payload libraries

The client copies six upstream web payload trees (`dtrh`, `intake`, `tunnel`, `goon`, `arcademy`,
`vendor`) into `payload/` beside the binary. The bytes stay owned by the legacy tree and none is
forked into `client/`, but the client **redistributes** them, so their vendored libraries are listed
here.

| Library | Version / copyright | Licence | Where it ships |
|---|---|---|---|
| **three.js** | r169, Copyright 2010-2024 Three.js Authors | MIT (`SPDX-License-Identifier: MIT` in the file header) | `payload/vendor/three/`, `payload/dtrh/vendor/three/` (with the `postprocessing` and `shaders` addons). |
| **three.js** | r185, Copyright 2010-2026 Three.js Authors | MIT (same header form) | `payload/arcademy/vendor/`. A second, newer copy — deliberately not deduplicated upstream. |
| **omggif** | (c) Dean McNamee, 2013 — <https://github.com/deanm/omggif> | MIT (full text in the file header) | `payload/dtrh/vendor/omggif/`. |
| **gifenc** | mattdesl/gifenc — <https://github.com/mattdesl/gifenc> | MIT | `payload/dtrh/vendor/gifenc/`. **The bundled copy carries NO licence header** — the build that vendored it stripped the banner, so this entry is the only place the attribution travels. |
| **fflate** | MIT License, Copyright (c) 2026 Arjun Barrett | MIT (`LICENSE` shipped beside the module) | `payload/goon/vendor/fflate/`. |
| **mp4-muxer** | MIT License, Copyright (c) 2023 Vanilagy | MIT (`LICENSE` shipped beside the module) | `payload/goon/vendor/mp4-muxer/`. |

No fonts are bundled anywhere in the client or its payloads (checked: no `.ttf`/`.otf`/`.woff*`, and
the one CSS that discusses typefaces states in its own words that it uses no `@font-face` and no
remote assets). Images, audio and session scripts under the payloads and under
`CcpClient.Desktop/Assets/` are product-owned content, not third-party components.

---

## 3. Native libraries bundled inside managed packages

These ship as separate binaries beside the executable and are third-party works in their own right,
distinct from the MIT-licensed managed bindings that carry them.

| Native | Inside package | Licence | Notice |
|---|---|---|---|
| **ANGLE** (`av_libglesv2.dll`, Windows) | `Avalonia.Angle.Windows.Natives` | BSD-3-Clause | Copyright 2018 The ANGLE Project Authors. All rights reserved. The bundled `LICENSE` names TransGaming Inc., Google Inc. and 3DLabs Inc. Ltd. in its no-endorsement clause; redistribution in binary form must reproduce the copyright notice, condition list and disclaimer. |
| **Skia** (`libSkiaSharp.dll` / `.so`) | `SkiaSharp.NativeAssets.*` | BSD-3-Clause (Skia, © Google Inc.), wrapped by MIT-licensed SkiaSharp | Skia itself bundles further third-party code under its own notices; the package ships `LICENSE.txt`. |
| **HarfBuzz** (`libHarfBuzzSharp.dll` / `.so`) | `HarfBuzzSharp.NativeAssets.*` | "Old MIT" permissive licence (HarfBuzz Project Authors), wrapped by MIT-licensed HarfBuzzSharp | The package ships `LICENSE.txt`. |
| **miniaudio** (`miniaudio.dll`, `libminiaudio.so`) | `SoundFlow` | Public-domain / MIT-0 dual, David Reid | Ships per-RID; `win-x64` and `linux-x64` are the two this client uses. |
| **libvlc / libvlccore + the VLC plugin tree** | `VideoLAN.LibVLC.Windows` | See §4 — this one is not simply permissive. | |

---

## 4. LibVLC and LibVLCSharp — the LGPL obligations, stated

The shipping project file's `LibVLCSharp` comment has carried the note *"LGPL-2.1-or-later — sidecar
note, pending-owner"* since the video-handoff spike deferred it (`client/docs/video-handoff-spike.md`
§1: *"LGPL sidecar/relink obligations = a packaging note, pending-owner"*). This section replaces
that note with the actual obligations. Two of the three are already satisfied by how the client is
built; the third is **not**, and is named rather than assumed.

*(Line-precise citations are deliberately absent from this file: it lives at the client root, which
is outside the corpus `client/tools/citations/intra.mjs` sweeps, so a `:NNN` here would rot unwatched.
Section references do not. The line-precise evidence is in `client/docs/gaze-model-provenance.md`,
which the detector does read.)*

**What ships.** `LibVLCSharp` 3.10.0 (managed, LGPL-2.1-or-later) and, on Windows,
`VideoLAN.LibVLC.Windows` 3.0.23.1 — `libvlc.dll`, `libvlccore.dll`, an `hrtfs/` and `lua/` tree, and
**323 plugin modules** under `plugins/`, in `libvlc/win-x64/` beside the executable. On Linux the
client uses the distribution's own libvlc and bundles nothing.

**One architecture, not three — corrected 2026-08-25 by listing a real publish rather than reading
the project file.** The nuget keys its copy on MSBuild's `$(Platform)`, never on the RID, so under
`AnyCPU` its own targets enabled `win-x64`, `win-x86` **and** `win-arm64` at once: every artifact
carried **956 plugin DLLs across three architectures, 292.8 MB**, of which a `win-x64` apphost can
load exactly one set. Unreachable by construction rather than by policy — LibVLCSharp chooses the
directory from `RuntimeInformation.ProcessArchitecture` (the shipped `LibVLCSharp.dll` references
`ProcessArchitecture` and never `OSArchitecture`, so even an x64 process emulated on an ARM64 host
reads `win-x64`), and a 64-bit process cannot load a 32-bit DLL at all. The project file now enables
only the tree matching the RID. **The decode surface did not change** — `win-x64` is the same 323
modules it always was, unfiltered — and this much stopped being redistributed, measured on real
publishes rather than estimated:

| RID | libvlc before | libvlc after | artifact total |
|---|---|---|---|
| `win-x64` | 292.8 MB, 956 plugin DLLs | 105.8 MB, 323 plugin DLLs | −187.0 MB. |
| `linux-x64` | 292.8 MB, 962 Windows DLLs | none | 979.7 MB → 686.9 MB. |

The Linux row is the sharper one: the nuget's copy is not conditioned on the OS either, so a
`linux-x64` artifact was carrying the entire Windows libvlc tree — three architectures of DLLs that no
Linux process can load — while the client's Linux video path uses the distribution's own libvlc.

**Source availability.** LGPL-2.1 §4 requires the complete corresponding source of the library, or a
written offer for it. VideoLAN publishes both: <https://code.videolan.org/videolan/vlc> for libvlc and
<https://code.videolan.org/videolan/LibVLCSharp> for the binding. The exact versions redistributed
are `3.0.23.1` and `3.10.0`. The full LGPL-2.1 text is at
<https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html>.

**Relinking — the native half is satisfied.** LGPL-2.1 §6(b) permits distribution against a
*"suitable shared library mechanism"* that lets the user substitute a modified library. libvlc is
unmodified, dynamically loaded, and lives in its own directory beside the binary; replacing
`libvlc/win-x64/libvlc.dll` requires nothing from us.

**Relinking — the managed half, satisfied 2026-08-25, and it was a real defect.** The named publish
strategy is self-contained **single-file** (`client/docs/release-publish-gates.md` §1), which bundles
every managed assembly into the apphost. `LibVLCSharp.dll` went in with the rest, and an assembly
fused into an executable cannot be substituted — which is precisely the right §6 exists to preserve.
`LibVLCSharp.dll` is now marked `ExcludeFromSingleFile` and ships as an ordinary sidecar; the artifact
was already a directory rather than a literal single file (`client/docs/release-publish-gates.md` §1,
native-library layout), so nothing was traded away. The alternative discharge — shipping the complete
corresponding source of LibVLCSharp, or a written offer valid for three years — was not needed.

**Verified against the published bytes, because a build property that silently does nothing is the
failure mode here.** Three checks, each with a control:

- The `win-x64` publish root holds `LibVLCSharp.dll` as a real 229,888-byte file, and the apphost
  shrank by 233,513 bytes — the assembly plus its manifest entry.
- Parsing the apphost's single-file bundle manifest lists **356 entries and none named LibVLCSharp**.
  Within-artifact control: `SoundFlow.dll`, a peer third-party managed assembly published the same
  way, **is** in the manifest and is **not** on disk. So the one copy in the artifact is the file.
- On a minimal probe carrying the identical target, a bundled build reports
  `Assembly.Location == ""` and no sidecar; the excluded build reports the sidecar's real path — and
  **overwriting that sidecar changes what the process loads** (it then fails to resolve
  `LibVLCSharp, Version=3.10.0.0`, with no bundled copy to fall back on). That failure is the
  substitution right working.

**The plugin tree is GPL-2.0-or-later, and no module is excluded — reasoning, not reflex.** The
earlier finding was that the package declares `LGPL-2.1-or-later` while `plugins/` carries
GPL-upstream modules, and it proposed dropping the unreachable ones via `VlcWindowsX64ExcludeFiles`.
Reachability was established first, from the shipped binaries, and it inverts the conclusion.

Each VLC module embeds its own licence header. Scanning all 323 `win-x64` modules for it: **4 carry a
GPL notice, 319 carry LGPL-2.1-or-later** — `codec/libx26410b_plugin.dll`, `lua/liblua_plugin.dll`,
`audio_filter/libdolby_surround_decoder_plugin.dll`, `audio_filter/libheadphone_channel_mixer_plugin.dll`.
Two things follow, and the second is decisive:

- `libzvbi_plugin.dll` is **not** among them: its own header reads *"Licensed under the terms of the
  GNU Lesser General Public License, version 2.1 or later."* The GPL exposure there is the
  statically-linked upstream libzvbi, which the binary never states — so the header scan is a **lower
  bound on GPL content, not an upper one**, and a per-module census cannot be trusted to find it all.
- `codec/libavcodec_plugin.dll` embeds FFmpeg's own configure line, and it contains
  **`--enable-gpl --enable-postproc`**. That plugin is the client's principal decoder and is reached
  by essentially every file it plays. **The aggregate is GPL-2.0-or-later whatever else is removed.**

So excluding modules buys nothing. Dropping `libx26410b_plugin.dll` — an x264 10-bit *encoder*, and
genuinely unreachable here (the client passes exactly `--no-video-title-show` and `--avcodec-hw=none`,
opens media as `FromType.FromPath`, and never sets `:sout`, records, or transcodes) — would leave the
obligation exactly where it is while adding a way to break decoding. The other three are worse
candidates still: the Dolby Surround decoder is a decoder, and lua backs playlist and metadata
handling. **Nothing is excluded from `win-x64`.** What the finding actually obliges is what is done
here: name it, and keep the source available. VideoLAN publishes the complete corresponding source for
every module at <https://code.videolan.org/videolan/vlc> at tag `3.0.23`. The client links only
libvlc's LGPL C API and is never linked against any GPL module; whether that separation is the right
legal characterisation of the aggregate is the owner's call and is not asserted here.

---

## 5. Gaze models — NOT SHIPPED BY THIS CLIENT, recorded ahead of any admission

**The client redistributes none of these files today.** There is no model file and no inference
package under `client/`, the camera capability's admitted-engine list is empty, and every product run
reports `Unavailable(camera-no-engine)`. This entry exists because the gaze row made a notices entry
an acceptance condition *before* any inference dependency lands, and because the byte provenance is
now established and should not have to be re-derived later.

**Origin.** Google MediaPipe. The MediaPipe repository is licensed **Apache-2.0**; the model artifacts
themselves are not stored in that repository but fetched from `storage.googleapis.com` and pinned by
sha256 in `third_party/external_files.bzl`. Apache-2.0 §4 requires the licence and attribution to
travel with redistribution; the full text is at <https://www.apache.org/licenses/LICENSE-2.0>.

**Redistribution chain**, each link verified byte-for-byte on 2026-08-25 (method, controls and every
hash: `client/docs/gaze-model-provenance.md`):

```
Google MediaPipe .tflite  ──byte-identical──>  patlevin/face-detection-tflite  (MIT: a CODE licence)
                                                        │
                                                  tf2onnx 1.16.1 conversion
                                                        ▼
                                          IntelliProve/face-detection-onnx     (MIT: the same code licence)
                                                        ▼
                                 ConditioningControlPanel/Resources/Models/*.onnx
```

Both intermediaries are MIT — and in both cases that is the licence of the Python/mirror **code**,
copyright Patrick Levin, referring to *"the Software"* and *"associated documentation files"*. Neither
asserts anything about the weights, which is precisely why this notice names Google as the origin.

**Pinned SHA-256s.** The Google artifacts, and the `.onnx` files as committed in the WPF tree:

| Artifact | SHA-256 |
|---|---|
| Google `modules/face_detection/face_detection_short_range.tflite` | `3bc182eb9f33925d9e58b5c8d59308a760f4adea8f282370e428c51212c26633` |
| Google `models/face_landmark.tflite` | `2efcb4f4de43c7614b80a3cc3e8a37354b3b3b40f75cce20f6f38f0f25d65493` |
| Google `modules/iris_landmark/iris_landmark.tflite` | `d1744d2a09c25f501d39eba4faff47e53ecca8852c5ce19bce8eeac39357521f` |
| `face_detection_short_range.onnx` | `bb171799a4497f9d07ef40c7d08acd9b2dd5e7d80ed00bfd0ef5ab2443aab643` |
| `face_landmark.onnx` | `71625efd79fd3ce448ba26db9f7f58e4f37daabf36c81a45a661844e3fdb3118` |
| `iris_landmark.onnx` | `1298780b3c203331d4c6b6e1e2ae6e31c29bdbef6fee777ce72d9a5849df0da7` |

The three `.onnx` digests are re-derived from the WPF tree by `ThirdPartyNoticesTests` on every floor
run, so this table cannot silently disagree with the bytes it describes.

**What is verified and what is not.** The weights in those `.onnx` files **are** Google's MediaPipe
weights — proved at byte level across the format conversion, with negative controls at zero. What is
*not* established is whether Apache-2.0 reaches an artifact MediaPipe fetches from a CDN rather than
stores, and no training corpus is identified either way. That is an open owner question, and this
entry states the origin as fact and the terms as unresolved rather than blending the two.

---

## 6. This file travels with the artifact

**Wired 2026-08-25.** Apache-2.0 §4 and LGPL-2.1 §1 oblige the licence and attribution to accompany
**redistribution**, so a notices file that stays in the repository discharges nothing. A `<Content>`
item in `CcpClient.Desktop.csproj` copies this document to both the build output and the publish
directory, and it lands beside the binary in all three artifact modes
(`client/docs/release-publish-gates.md` §3 treats Debug, Release and published as separate gates).
Confirmed on the real `win-x64` publish, by listing the output rather than by reading the project
file: `THIRD-PARTY-NOTICES.md` sits in the publish root next to `CcpClient.Desktop.exe`, and the
artifact's own `--verify-assets` still passes with a clean sweep.

`CcpClient.Tests/ThirdPartyNoticesTests` keeps it that way: one fact requires the file to exist beside
the running test binary — which it can only do by riding the project's copy wiring — and requires that
same project item to declare `CopyToPublishDirectory`. Delete the item and the floor goes red rather
than the artifact going quietly bare.
