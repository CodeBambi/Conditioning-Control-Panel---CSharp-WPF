# SP-140 — record

Base `feat/crossplatform` @ `115cf9811`. Lane branch `worktree-agent-aa39ee99227d97dc4`.
Commits: `edcf80f20` (plan checkpoint), `69ad4897e` (the standing test), `ce1680561` (D323-D325).
**All red demonstrations were taken at `69ad4897e`, the commit that holds the test.**

---

## 1. The fixture: provenance, and the circularity verdict

**`client/spikes/CcpSpike.VideoHandoff/fixtures/clip.mp4`** — 46 382 bytes, git-tracked since
`f21a7c011` (SP-018), **122 commits before this packet was authored**.

| | |
|---|---|
| Encoder | **ffmpeg**, gyan.dev full build 2025-06-04, encoding through **x264** |
| Source content | `lavfi`'s synthetic **`testsrc2`** pattern, 96x96 @ 10 fps, 2 s, plus a 440 Hz sine |
| Container / codec | MP4, H.264 + AAC, `+faststart`. `ftyp` brands `isom/iso2/avc1/mp41` |
| SHA-256 | `eb14abd63a02a22029c513a4b512e2cecad34b2b0c9e31994030753c5d769fbc` |
| Recorded at | `client/docs/video-handoff-spike.md:21`, `spine-tasks/SP-018-video-handoff-spike/record.md:82` |
| Licence / privacy | synthetic test pattern — nothing copyrighted, nothing personal |

### Is it circular? NO.

The packet's trap is real and it is worth restating: **a fixture produced by Media Foundation's own
sink writer would prove close to nothing**, because the encoder and the decoder would be the same
stack and a container it cannot handle is exactly what it would never emit. This fixture is not
that. It was produced by an **independent third-party encoder** and Media Foundation had no part in
producing a single byte of it. It also predates the packet by 122 commits, so it cannot have been
shaped to what MF happens to accept.

**The claim is pinned mechanically rather than asserted in prose.**
`RealClipDecodeTests.TheFixtureIsTheFfmpegArtefactWhoseProvenanceIsRecorded_NotAnythingMediaFoundationCouldHaveMade`
recomputes the SHA-256 and binds it to the provenance sentence in `client/docs/video-handoff-spike.md`
(`testsrc2` and the hash prefix). Swap the fixture for a Media-Foundation-encoded file and the hash
fails; delete the provenance sentence from the docs and the doc needle fails. Watched red (§5, M1).

### It is read IN PLACE, not copied

No new binary enters the repository. A copy would need the same `FindRepoRoot()` walk anyway — the
test csproj is not in this packet's File Scope, so no `CopyToOutputDirectory` item is possible — so
copying buys nothing and pins a duplicate instead of the artefact the docs describe. Precedent for a
unit test reading a committed repository file outside its own project:
`ChaosTunnelLoopbackTests.cs:426` reads `ConditioningControlPanel/Resources/web`. Confirmed by the
coordinator at the plan checkpoint.

### What was rejected, so it is not re-litigated

* **Generate with MF's sink writer** — the named trap.
* **Hand-write a JPEG/MJPEG-in-AVI encoder, or a hand-built H.264 I_PCM elementary stream** —
  150-200 lines of test-only encoder to obtain something that already existed with better
  provenance.
* **`clip.webm` (VP8 + Vorbis, same spike) as a "LibVLC yes / MF no" demonstration** — the idea was
  **killed by measurement, not by argument**: Media Foundation opens and decodes it on this machine
  (96x96, 5.00 fps, a frame decoded). Recorded because an idea that dies on measurement is the
  evidence this port keeps failing to produce, and burying it would make the D323 result look
  luckier than it is.

---

## 2. Per-survivor closure

### M-y — *the video processor is never exercised* — **CLOSED**

Every video fact before this packet ran against `TestAvi`'s uncompressed `BI_RGB` AVI, whose native
media type is already `MFVideoFormat_RGB32`. No codec runs, and the source reader's video
processor — enabled by `MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING` at
`client/src/CcpClient.Desktop/Video/MediaFoundationClipSource.cs:140-141` — has nothing to convert.

The new fixture's stream is **H.264 Baseline, level 1.0**, read out of the file's own `avcC` sample
entry (offset 547, payload `01 42 c0 0a`) rather than out of the `ftyp` brand list, which says what
the file *claims* and not what its video track *is*. Its decoder does not produce RGB32, and a BGRX
frame comes back anyway. That conversion is the video processor's work.

**The control that makes this mean something.** Deleting `attributes.SetUINT32(ref processing, 1);`
and re-running:

| Suite | Result under the mutation |
|---|---|
| `RealClipDecodeTests` (new) | **5 of 6 FAIL** — only the provenance fact, which never opens the clip, survives |
| `VideoCapabilityTests` decode facts (the AVI fixture) | **3 of 3 PASS** |

The old fixture **cannot see the defect**. That asymmetry is the whole of M-y, and it is now a
measured fact rather than an inference. The refusal the port produces is exact and typed:

```
video-clip-unreadable: the operating system will not hand 'clip.mp4' back as RGB32
(SetCurrentMediaType returned 0xC00D36B4), so its pictures cannot reach a surface
```

`0xC00D36B4` is `MF_E_INVALIDMEDIATYPE`.

### M-w — *the openable format set is untested against real files* — **NOT CLOSED**

**The board's acceptance is wrong and this is filed as a spec-versus-reality discrepancy.** Board
line 71 says *"ONE compressed fixture closes both survivors"*. It cannot: **one file bounds one
format.** A committed fixture that opens tells you that one container, one codec, one profile and
one level are openable on the machines that run the suite. It says nothing about the set.

What bounds M-w is the **measurement** in §3, and a measurement is not a closure: it quantifies the
cost observed on one library on one machine. M-w stays open, D323 carries the number, and the test
file's own class comment says so where a future reader will hit it.

---

## 3. The measurement — all 54 real videos

### How it ran

`spine-tasks/SP-140-real-clip-decode/measure/` — a console project that opens each file through the
**real `MediaFoundationClipSource`** (not a double, not a copy of it), decodes one frame, and
disposes.

**It is not a test and cannot become one.** It is not in `client/CcpClient.sln`, so
`check-warnings.mjs` (which builds that solution's four projects) never compiles it and
`check-floor.mjs` (which runs the two test csproj by path) never sees it. Nothing under
`client/tools/**` globs for `*.csproj`, and both packet enumerators
(`FloorWrapperGuardTests.cs:102`, `client/tools/wave/validate-wave.mjs:544`) look for `PROMPT.md` at
exactly one level under `spine-tasks/`, so a `measure/` subfolder is invisible to both. `bin/` and
`obj/` are gitignored repo-wide. It takes the directory as a **required argument**, so it cannot
silently target one machine. **It adds zero to either floor total and no test anywhere skips on a
missing directory.**

### Privacy — a deliberate tightening on the packet's own instruction

The packet said *"record filename and outcome"*. **The 54 filenames are not recorded here, in the
divergence rows, in the commits, or anywhere else in the repository.** They are explicit
personal-content descriptors and the weaker reading was not worth taking; the harness prints a
filename **only for a file that refuses** — because a parity defect nobody can identify cannot be
fixed — and none refused, so none was printed. Successes are recorded by index and technical facts.
Nothing was copied, no frame or still was written, no content was logged; the decoded frame is
reduced to two integers (byte length, and distinct pixels among at most 256 sampled points) before
it leaves scope. **The next lane should inherit this stricter reading, not the packet's.**

### THE NUMBERS

```
MediaStackUsable=True  files=54
TOTAL 54 | OPENED 54 | DECODED-A-FRAME 54 | REFUSED 0
```

**54 of 54 open. 54 of 54 decode a frame. Zero refusals. Zero reasons to report, because there were
no failures.**

| idx | ext | MiB | open | frame | WxH | fps | duration | bottom-up | colours (of ≤256) |
|---|---|---|---|---|---|---|---|---|---|
| 1 | .mp4 | 58.6 | yes | yes | 1280x720 | 30.00 | 00:05:50 | no | 254 |
| 2 | .mp4 | 80.6 | yes | yes | 452x826 | 30.00 | 00:06:48 | no | 242 |
| 3 | .mp4 | 196.1 | yes | yes | 854x480 | 30.00 | 00:18:09 | no | 235 |
| 4 | .mp4 | 14.8 | yes | yes | 1080x1920 | 30.00 | 00:00:06 | no | 211 |
| 5 | .mp4 | 7.5 | yes | yes | 270x480 | 30.00 | 00:01:00 | no | 251 |
| 6 | .mp4 | 22.7 | yes | yes | 1916x1080 | 30.00 | 00:00:18 | no | 1 |
| 7 | .mp4 | 31.4 | yes | yes | 1920x1080 | 30.00 | 00:00:27 | no | 252 |
| 8 | .mp4 | 48.2 | yes | yes | 1920x1080 | 60.00 | 00:00:29 | no | 253 |
| 9 | .mp4 | 61.2 | yes | yes | 640x296 | 30.00 | 00:16:16 | no | 211 |
| 10 | .mp4 | 77.3 | yes | yes | 640x360 | 30.00 | 00:15:09 | no | 165 |
| 11 | .mp4 | 161.5 | yes | yes | 1280x720 | 30.00 | 00:11:39 | no | 1 |
| 12 | .mp4 | 123.3 | yes | yes | 720x1280 | 30.00 | 00:01:57 | no | 8 |
| 13 | .mp4 | 130.5 | yes | yes | 540x960 | 30.00 | 00:03:34 | no | 223 |
| 14 | .mp4 | 37.7 | yes | yes | 852x480 | 30.00 | 00:01:15 | no | 235 |
| 15 | .mp4 | 109.1 | yes | yes | 540x960 | 30.00 | 00:02:56 | no | 249 |
| 16 | .mp4 | 116.9 | yes | yes | 540x960 | 30.00 | 00:03:06 | no | 14 |
| 17 | .mp4 | 79.0 | yes | yes | 540x960 | 30.00 | 00:02:09 | no | 248 |
| 18 | .mp4 | 90.0 | yes | yes | 852x480 | 30.00 | 00:03:02 | no | 245 |
| 19 | .mp4 | 201.8 | yes | yes | 1280x720 | 30.00 | 00:10:53 | no | 1 |
| 20 | .mp4 | 15.2 | yes | yes | 480x854 | 30.00 | 00:01:00 | no | 247 |
| 21 | .mp4 | 3.8 | yes | yes | 480x854 | 30.00 | 00:00:21 | no | 230 |
| 22 | .mp4 | 37.2 | yes | yes | 640x360 | 30.00 | 00:09:42 | no | 1 |
| 23 | .mp4 | 6.3 | yes | yes | 854x480 | 30.00 | 00:01:00 | no | 257 |
| 24 | .mp4 | 31.6 | yes | yes | 1080x1920 | 30.00 | 00:00:32 | no | 255 |
| 25 | .mp4 | 47.5 | yes | yes | 1280x720 | 30.00 | 00:02:30 | no | 8 |
| 26 | .mp4 | 8.9 | yes | yes | 854x480 | 30.00 | 00:00:58 | no | 42 |
| **27** | **.mov** | **77.6** | **yes** | **yes** | **640x360** | **30.00** | **00:09:28** | **no** | **14** |
| 28 | .mp4 | 13.1 | yes | yes | 1280x720 | 24.00 | 00:00:54 | no | 228 |
| 29 | .mp4 | 4.6 | yes | yes | 1280x720 | 30.00 | 00:02:01 | no | 1 |
| 30 | .mp4 | 40.2 | yes | yes | 1280x720 | 30.00 | 00:01:07 | no | 207 |
| 31 | .mp4 | 26.9 | yes | yes | 1280x720 | 30.00 | 00:00:46 | no | 207 |
| 32 | .mp4 | 25.6 | yes | yes | 1280x720 | 30.00 | 00:01:33 | no | 57 |
| 33 | .mp4 | 21.0 | yes | yes | 1080x1128 | 30.00 | 00:01:20 | no | 192 |
| 34 | .mp4 | 14.1 | yes | yes | 1280x720 | 30.00 | 00:00:49 | no | 3 |
| 35 | .mp4 | 37.6 | yes | yes | 1080x1182 | 30.00 | 00:00:58 | no | 212 |
| 36 | .mp4 | 37.5 | yes | yes | 1280x720 | 30.00 | 00:01:07 | no | 209 |
| 37 | .mp4 | 12.9 | yes | yes | 480x720 | 60.00 | 00:01:00 | no | 240 |
| 38 | .mp4 | 11.6 | yes | yes | 1920x1080 | 59.94 | 00:00:20 | no | 251 |
| 39 | .mp4 | 2.6 | yes | yes | 1280x720 | 30.00 | 00:00:22 | no | 250 |
| 40 | .mp4 | 185.8 | yes | yes | **2560x1440** | 30.00 | 00:01:45 | no | 233 |
| 41 | .mp4 | 20.3 | yes | yes | 854x480 | 30.00 | 00:01:19 | no | 186 |
| 42 | .mp4 | 46.4 | yes | yes | 1280x720 | 30.00 | 00:03:29 | no | 112 |
| 43 | .mp4 | 157.2 | yes | yes | 1280x720 | 30.00 | 00:05:05 | no | 255 |
| 44 | .mp4 | 93.2 | yes | yes | 540x960 | 30.00 | 00:02:31 | no | 18 |
| 45 | .mp4 | 41.5 | yes | yes | 640x360 | 30.00 | 00:06:34 | no | 32 |
| 46 | .mp4 | 161.7 | yes | yes | 1280x720 | 30.00 | 00:06:55 | no | 236 |
| 47 | .mp4 | 159.3 | yes | yes | 1280x720 | 30.00 | 00:09:46 | no | 256 |
| 48 | .mp4 | 134.9 | yes | yes | 1280x720 | 30.00 | 00:10:28 | no | 231 |
| 49 | .mp4 | 175.1 | yes | yes | 1280x720 | 30.00 | 00:09:31 | no | 232 |
| 50 | .mp4 | 92.2 | yes | yes | 1280x720 | 30.00 | 00:17:53 | no | 236 |
| 51 | .mp4 | 423.1 | yes | yes | 1280x720 | 30.00 | **00:42:12** | no | 195 |
| 52 | .mp4 | 264.5 | yes | yes | 1280x720 | 30.00 | 00:09:38 | no | 241 |
| 53 | .mp4 | 476.6 | yes | yes | 852x480 | 30.00 | 00:16:00 | no | 19 |
| 54 | .mp4 | 230.5 | yes | yes | 1280x720 | 30.00 | 00:10:47 | no | 256 |

### What the number means

**D124's cost is theoretical on this library.** That is a real result and the strongest one
available: there is no video in the owner's library that plays in the shipping app and refuses here
on account of the decoder swap. No board defect row is owed, because there is no defect to file.

**The `.mov` (row 27), which the packet flagged as the interesting case.** Two questions had been
merged and they answer differently, both correctly:
* **Can the port's video decoder open it?** Yes — 640x360, 9:28, a frame decoded. And
  `client/src/CcpClient.Desktop/Effects/VideoClipPool.cs:53` lists `.mov` in upstream's own
  extension set, so **Mandatory Video would play it**.
* **Does the DTRH media manifest serve it?** No —
  `client/src/CcpClient.Desktop/Features/Dtrh/DtrhUserMedia.cs:173` classes `.mov` as
  media-like-but-not-served, which is upstream parity and was already recorded at the SP-111 land.

Different subsystems, different questions. Neither answer corrects the other.

### The four incidental findings (D324, D325)

1. **Zero of 54 report a negative stride.** Every one is top-down. The bottom-up flip at
   `MediaFoundationClipSource.cs:364-384` exists because the AVI fixture measured `-1280`, and **no
   real file in the library takes that branch** — the AVI fixture is the only thing holding it.
2. **Zero of 54 are rate-less.** Every file declares 24, 30, 59.94 or 60 fps, so D125's 80 ms
   fallback is taken by nothing here.
3. **The longest clip is 42:12**, so D126's one-hour ceiling refuses nothing in this library.
4. **The largest picture is 2560x1440 = 14 745 600 bytes per frame** in the port's BGRX layout, at a
   container-declared 30 fps, through D128's managed nearest-neighbour compositor. **Reachable is
   NOT slow**: no frame time, allocation rate or dropped-frame count was measured and none is
   implied. What changed is that the managed path now has a **measured real-world input size**
   instead of a 320x240 fixture's.

None of the four is a defect and none justifies a product change. Findings 1-3 are recorded in D324
with the explicit note that a defence with no observed trigger is still a defence; finding 4 is D325.

---

## 4. Evidence class

**Decode only. Nothing in this packet is `presentation-verified`, and no frame reached a screen.**
`client/docs/verification-harness.md` governs: a headless or in-memory frame never discharges a
headed gate. Every claim here is about buffers the operating system handed back to a managed array.

**Cadence, order and timing remain entirely unmeasured.** A clip playing at half speed, or
backwards, passes every fact SP-140 added and every file in the measurement. That gap is unchanged
from SP-111 and this packet did not narrow it.

**The measurement's own limits.** One library, one machine, one Windows build and codec set, and
**the first decodable frame per file** rather than a whole playthrough. A file that opens, decodes
one frame and then fails at minute nine would be recorded here as a success. Five of the 54 first
frames are a single flat colour (rows 6, 11, 19, 22, 29) — normal for a clip that opens on black,
and the reason the standing fixture uses `testsrc2`, whose first frame carries 134 distinct colours.

**Orientation is not established for the new fixture.** `TestAvi` can prove orientation because its
writer chooses which half is which; `testsrc2`'s layout has no independent oracle in this
repository, so pinning an observed asymmetry would pin whatever the decoder did on the day.
Orientation stays `VideoCapabilityTests`' fact, and the two fixtures are complements: this one
covers the straight-copy stride branch against a real codec, the AVI covers the flip.

---

## 5. Red demonstrations — every fact watched red at `69ad4897e`

Each mutation was applied to the committed head, run, and reverted. `git status` was verified clean
under `client/src/` afterwards; **the landed tree contains no product change**.

| # | Fact | Mutation | Observed |
|---|---|---|---|
| M1 | provenance / anti-circularity | `FixtureParts` pointed at a **different real file** (`clip.webm`) — the fixture-swap threat itself | **3 FAIL** (provenance hash, H.264/`avcC`, container rate), 3 pass |
| M2 | **M-y**, the video processor | delete `attributes.SetUINT32(ref processing, 1);` (`MediaFoundationClipSource.cs:141`) | **5 of 6 FAIL**; `VideoCapabilityTests`' 3 AVI decode facts stay **GREEN** |
| M3 | every frame decodes, clip ends | remove `DecodedFrames++` (`:293`) | **1 FAIL**, exactly the intended fact |
| M4 | the picture is a picture and it moves | remove the `Marshal.Copy` in `CopyOut` (`:380`) — the buffer is handed back unwritten | **1 FAIL**, exactly the intended fact |
| M5 | positive stride | invert the sign test in the `VideoClipInfo` construction (`:219`, `stride < 0` to `stride > 0`) | **1 FAIL**, exactly the intended fact |
| M6a | container frame rate | `if (numerator > 0)` to `if (numerator > 1_000_000)` (`:202`) — the rate is never read | **1 FAIL**, exactly the intended fact |
| M6b | container duration | `Marshal.ReadInt64(buffer, 8)` to `(buffer, 0)` (`:347`) — wrong PROPVARIANT offset | **1 FAIL**, exactly the intended fact |

M6a and M6b are two mutations against one fact because it carries two independent assertions; both
are load-bearing.

---

## 6. Floor, and the before/after failure sets

| Run | Tree | Unit total | Unit failures | Headless |
|---|---|---|---|---|
| **BEFORE** | base `115cf9811`, none of this packet present | **2616** (= the pin) | **8** | 152/152 |
| **AFTER** (floor run 1) | `69ad4897e` | **2622** | **8** | 152/152 |
| **AFTER** (floor run 2) | `ce1680561`, after the divergence rows | **2622** | **8** | 152/152 |
| **AFTER** (floor run 3) | `3aaffdf74`, the final landed head | **2622** | **8** | 152/152 |

**The failure SET is byte-identical across all four runs**, and it is the documented contended
real-desktop family:

```
VideoCapabilityTests.AFrameHandedOverTWICE_LeavesTheSurfaceUnchanged_AndIsStillALivePicture
VideoCapabilityTests.ASurfaceTAKESItsOwnPointBackFromAWindowPlacedOverIt_AndThatIsWhyOcclusionCannotBeStaged
VideoCapabilityTests.DecodedFramesAndFramesTheOperatingSystemHOLDS_AreTwoDifferentNumbers_AndBothAreReported
VideoCapabilityTests.TheOperatingSystemHoldsTheSurfaceAboveEveryOrdinaryWindow_AndNEVERMakesItTheForeground
VideoCapabilityTests.TheOperatingSystemsOwnCopyOfTheSurfaceCarriesTheDECODEDPicture_OverABarItReadsBackEXACTLY
InputOverlayCoexistenceTests.TheCardReallyTookTheForeground_WhichIsWhatMakesTheRestOfThisFileATest
InputOverlayCoexistenceTests.TheOverlayIsStillAboveEveryOrdinaryWindow_AfterACardHasComeAndGone
InputOverlayCoexistenceTests.TheOverlaysOwnOracleStillEarnsAvailable_AfterACardHasHeldTheForeground
```

Every one of them reports `aboveEveryOrdinaryWindow=False`: a foreign topmost window owned the
z-order while the run was in flight, which is exactly the machine condition this repository has
documented since SP-107. Two independent confirmations that they are environmental and not this
packet's:

1. **The base run.** The identical 8 fail at `115cf9811` with **no** SP-140 code present at all.
2. **Same binaries, isolated re-run.** Running both families alone on the SP-140 tree with
   `--no-build`: **46 of 46 PASS**.

**No retry was used to pass a gate, no assertion was weakened, nothing was added to
`allowedSkips`, and `client/tests/floor/floor.json` was never opened.**

### The floor arithmetic

* Pin: **2616 unit / 152 headless**.
* Declared delta (`spine-tasks/SP-140-real-clip-decode/floor-delta.json`): **unit +6, headless 0**.
* Observed: **2622 unit / 152 headless** = pin + declared delta, exactly. The 2 `NotExecuted` are the
  two Linux-machine-class entries already pinned in `allowedSkips`.

`node client/tests/floor/check-warnings.mjs`: **0 warnings, 0 errors across 4 projects in Debug,
forced non-incremental.**

---

## 7. Spec-versus-reality discrepancies found

1. **The packet's central premise was wrong, in the port's favour.** It assumed the compressed
   fixture had to be created and spent its longest section managing the provenance trap in creating
   one. A real compressed clip with better provenance than anything this packet could have produced
   had been committed since SP-018. Confirmed by the coordinator at the plan checkpoint.
2. **Board line 71's acceptance is wrong on half of itself.** *"ONE compressed fixture closes both
   survivors"* — it closes M-y and cannot close M-w, because one file bounds one format. Recorded
   rather than satisfied nominally.
3. **`clip.webm` does not demonstrate the D124 divergence.** Media Foundation opens VP8 on this
   machine. The hypothesis was killed by measurement.
4. **No decoder defect was found**, so `client/src/CcpClient.Desktop/Video/**` was not touched and
   the File Scope's conditional product allowance went unused.

---

## 8. What is NOT proven

* No frame reached a screen. Nothing here is `presentation-verified` and no headed gate is
  discharged.
* Cadence, order and timing are unmeasured; half-speed or reversed playback passes everything.
* The openable format set is not certified — M-w is open, and D323's number bounds one library on
  one machine.
* The measurement decoded the **first** frame of each file, not all of them.
* No performance claim of any kind is made. D325 records a **measured input size** on an
  **unmeasured path**.
* Nothing about the owner's media was copied, written, or named.
