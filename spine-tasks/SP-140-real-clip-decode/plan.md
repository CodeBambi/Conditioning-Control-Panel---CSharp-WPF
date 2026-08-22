# SP-140 — plan checkpoint (Review Level 3, before any product or test edit)

Base `feat/crossplatform` @ `115cf9811`, worktree `.claude/worktrees/agent-aa39ee99227d97dc4`.
No file under `client/src/**` or `client/tests/**` has been edited. What exists so far is this
file and `spine-tasks/SP-140-real-clip-decode/measure/` (deliverable 2's harness, below).

---

## 0. Citations verified against source, not against the packet

| Packet claim | Verified | Result |
|---|---|---|
| `Video/MediaFoundationClipSource.cs:143` = `MFCreateSourceReaderFromURL` | read | EXACT |
| `wpf-surface-reachability.md:1123` = D124 and the quoted cost sentence | read | EXACT, verbatim |
| board line 71 = the real-media row, acceptance "ONE compressed fixture closes both survivors" | read | EXACT |
| `client/port.txt` permits an owner-designated media directory | read | EXACT (*machine-specific, desktop-only unless the owner designates equivalents*) |
| D311-D322 are SP-139's; last row in the file is D322 | read | EXACT — **D323 is free** |
| Floor pin 2616 unit / 152 headless | read `floor.json` | EXACT |
| `C:\Code\ccp media\videos` holds 54 (53 `.mp4`, 1 `.mov`), recursive, subfolders | enumerated | EXACT |
| the `.mov` is media-like-but-not-served by DTRH | `DtrhUserMedia.cs:172-173` | EXACT (`IsMediaLike` lists `.mov`) |
| every video fact to date ran against `TestAvi`'s uncompressed `BI_RGB` AVI | read `TestAvi.cs` | EXACT |

**One packet premise is wrong, in the port's favour.** The packet assumes a compressed fixture has
to be *created* and warns about the provenance trap in creating one. **A real compressed clip with
fully documented third-party provenance is already committed to this repository** — see §1. That
changes the answer to "where does the fixture come from", and it removes the trap rather than
managing it.

---

## 1. The fixture: where it comes from, and whether it is circular

**`client/spikes/CcpSpike.VideoHandoff/fixtures/clip.mp4`** — 46 382 bytes, git-tracked since
`f21a7c011` (SP-018).

Provenance, already written down twice before this packet existed:
* `client/docs/video-handoff-spike.md:21` — *"lavfi-generated (`testsrc2` 96x96@10fps 2s + 440Hz
  sine; license-safe): clip.mp4 (h264+aac, +faststart, SHA-256 `eb14abd6…9fbc`)"*
* `spine-tasks/SP-018-video-handoff-spike/record.md:82` — the full SHA-256
  `eb14abd63a02a22029c513a4b512e2cecad34b2b0c9e31994030753c5d769fbc`, and the generator: **ffmpeg
  gyan.dev full build 2025-06-04**, generated once and committed *"so neither platform needs it at
  runtime"*.

**Is it circular? NO, and this is the strongest available answer.**
* The encoder is **x264 inside ffmpeg**. Media Foundation had no part in producing a single byte of
  it. The MP4 `ftyp` reads `isom/iso2/avc1/mp41` — ffmpeg's own brand set, not MF's.
* It predates this packet by 122 commits, so it cannot have been shaped to what MF happens to
  accept.
* Content is **synthetic `testsrc2`**, so nothing about the owner's library, nothing copyrighted,
  and nothing personal is committed. Privacy constraint untouched.
* It is the **same container and codec family as 53 of the owner's 54 files** (H.264 in MP4), so it
  is representative rather than exotic.

**It is used in place, not copied.** No new binary enters the repo. Precedent for a unit test
reading a committed repo file outside its own project: `ChaosTunnelLoopbackTests.cs:426` reads
`ConditioningControlPanel/Resources/web`. Located with the `FindRepoRoot()` walk that eleven test
files already use (anchor `client/CcpClient.sln`), which **throws rather than skips**.

**The circularity claim is pinned mechanically, not asserted in a comment.** Fact 1 recomputes the
SHA-256 and binds it to the provenance sentence in `client/docs/video-handoff-spike.md`. Swapping in
a Media-Foundation-encoded file reds it.

**Measured, this machine, through the real product class** (`MediaFoundationClipSource`, not a
double): opens `Available`, 96x96, 10.00 fps, 2 s, `BottomUp=False`, a frame decodes to 36 864 bytes
(= 96·96·4) carrying **134 distinct colours** among 256 sampled points.

**Rejected alternatives, and why (recorded so they are not re-litigated):**
* *Generate with MF's sink writer* — the packet's named trap. Rejected.
* *Hand-write a JPEG/MJPEG-in-AVI encoder, or a hand-built H.264 I_PCM stream* — ~150-200 lines of
  test-only encoder to obtain something that already exists, with weaker provenance than a
  documented ffmpeg artefact.
* *Copy `clip.mp4` into `client/tests/CcpClient.Tests/assets/`* — 46 KB duplicated for nothing. A
  copy would still need the `FindRepoRoot` walk (the csproj is **not** in this packet's File Scope,
  so no `CopyToOutputDirectory` item can be added), so copying buys zero and pins a copy rather than
  the artefact the docs describe.
* *`clip.webm` (VP8+Vorbis, same spike) as a "LibVLC yes / MF no" demonstration* — **measured, and
  it disproved itself: MF opens and decodes it on this machine** (5.00 fps, 96x96, frame decoded).
  Good thing it was measured rather than assumed.

---

## 2. Which survivor each deliverable actually closes

| | M-y — *the video processor is never exercised* | M-w — *the openable format set is untested against real files* |
|---|---|---|
| **Deliverable 1** (standing test, `clip.mp4`) | **CLOSED.** The stream is H.264; the decoder's output is not RGB32; `MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING` (`MediaFoundationClipSource.cs:140-141`) is what converts it to BGRX, and the frame comes back BGRX. Red-watched by deleting those two lines. | **NOT closed, and no single committed fixture can close it.** One file bounds one format. Stated in the test file's own class comment, in `record.md`, and in D323. |
| **Deliverable 2** (the 54-file measurement) | not its job | **QUANTIFIED, not "closed".** A measurement over one library on one machine bounds the cost; it does not certify the format set. The honest verdict goes on the board, not into a green test. |

This is the split the packet asks for and it is deliberate: **one fixture does NOT close both
survivors**, whatever board line 71's acceptance says. Recorded as a spec-versus-reality
discrepancy rather than papered over.

---

## 3. The measurement — already run, and how it stays out of the suite

### It stays out of the suite structurally, not by a skip
`spine-tasks/SP-140-real-clip-decode/measure/Measure.csproj` is a console project that is **not in
`client/CcpClient.sln`**. Verified, not assumed:
* `check-warnings.mjs` builds `client/CcpClient.sln`, which holds exactly four projects
  (`CcpClient.Desktop`, `CcpClient.Tests`, `CcpClient.HeadlessTests`, `CcpVerify`).
* `check-floor.mjs` runs the two test csproj by path.
* Nothing under `client/tools/**` globs for `*.csproj`.
* Both packet enumerators (`FloorWrapperGuardTests.cs:102`, `validate-wave.mjs:544`) look for
  `PROMPT.md` at exactly one level under `spine-tasks/`; a `measure/` subfolder is invisible to both.
* `bin/`/`obj/` are gitignored repo-wide (`.gitignore:31-32`), so the build leaves nothing tracked.

It takes the directory as a **required argument** — it cannot silently target one machine — and it
adds **zero** to either floor total. No test anywhere skips on a missing directory; nothing is added
to `allowedSkips`.

### Privacy, as implemented
Open, decode one frame, dispose. No byte of any file copied anywhere, no frame or still written, no
content logged. The decoded frame is reduced to two integers (byte length; distinct pixels among
≤256 sampled points) before it leaves scope. **Filenames are printed only for a file that refuses** —
a parity defect nobody can identify cannot be fixed — and successes are reported by index and
technical facts only. Nothing from the owner's library will be committed, including names.

### THE NUMBER, reported before any conclusion is drawn from it

```
TOTAL 54 | OPENED 54 | DECODED-A-FRAME 54 | REFUSED 0
```

**54 of 54 open. 54 of 54 decode a frame. Zero refusals. The `.mov` opens and decodes** (640x360,
9:28) — so the two questions about that one file answer differently: `VideoClipPool.cs:53` lists
`.mov` and MF decodes it, while `DtrhUserMedia.IsMediaLike` (`:173`) declines to *serve* it. Both
answers are correct and they are about different subsystems.

**So D124's cost is theoretical on this library.** Four further facts fell out of the same run and
none of them was known before it:

1. **Every one of the 54 reports a POSITIVE stride** (`BottomUp=False`). The port's bottom-up flip
   branch (`MediaFoundationClipSource.cs:364-384`, written because the AVI fixture measured `-1280`)
   is exercised by **no real file in the library**. It is a fixture-only path in practice.
2. **Every one of the 54 declares a frame rate** (24 / 30 / 59.94 / 60). D125's 80 ms rate-less
   fallback is taken by **zero** of them.
3. **The longest clip is 42:12**, so D126's one-hour ceiling refuses nothing here.
4. **Frames are large**: up to 2560x1440 = **14 745 600 bytes per frame**, which D128's managed
   nearest-neighbour compositor must move at the container's 30 fps. Never measured, still unmeasured.

Nothing in the measurement exposed a decoder defect, so **`client/src/CcpClient.Desktop/Video/**`
will not be touched by this packet** — the File Scope's conditional product allowance goes unused.

---

## 4. The standing test, and which edit each guard reds on

New file `client/tests/CcpClient.Tests/RealClipDecodeTests.cs`, **six facts**, unit project (pure
logic + MF interop, no Avalonia). Deliberately **not** in `RealDesktopCollection`: it opens no
window, takes no lease, and must not be serialised behind the contended real-desktop family.
Decoder obtained via `VideoPresenceFactory.CreateClipSourceFor(VideoHostPlatform.Windows)`, the same
call its sibling `VideoCapabilityTests` makes. No waits of any kind, so `TestWait` is not needed.

| # | Fact | Reds on |
|---|---|---|
| 1 | the fixture is the ffmpeg artefact whose provenance is recorded — SHA-256 recomputed, and `video-handoff-spike.md` still carries `testsrc2` + the SHA prefix | any change to the fixture bytes (**including replacing it with a Media-Foundation-encoded file**), or deleting the provenance sentence from the doc |
| 2 | MF opens a **real H.264** clip (`avc1` read out of the file's own `stsd`) and hands it back as BGRX | **deleting `MediaFoundationClipSource.cs:140-141`** (`ReaderEnableVideoProcessing`) — `SetCurrentMediaType(RGB32)` then refuses. **This is the M-y demonstration** |
| 3 | every frame decodes to exactly 96·96·4 bytes and the clip then **ENDS** | breaking `ReadFrame`'s end-of-stream handling (`:266-270`) or `CopyOut`'s length guard (`:370-373`) |
| 4 | the first frame is a **picture** (many distinct colours) and a later frame **differs** from it | a decoder or copy path handing back a constant or a repeated buffer |
| 5 | this container's stride is **positive**, so `TestAvi`'s negative-stride fixture and this one cover the two branches jointly | inverting the sign test at `:219` |
| 6 | the frame interval and the duration come **from the container** (100 ms, ~2 s) | breaking the `MF_MT_FRAME_RATE` read (`:196-206`, which would fall back to D125's 80 ms) or `ReadDuration`'s PROPVARIANT offset (`:347`) |

Each is watched red at the committed head and reverted; the head SHA and the exact mutation go in
`record.md`. The product mutation for fact 2 is a temporary red-watch only — `git status` under
`client/src/` will be clean at commit.

**Named limit, stated in the file rather than discovered later:** this fixture cannot prove
ORIENTATION. `TestAvi` can, because the writer chose which half is which; `testsrc2`'s layout has no
independent oracle in this repository, so pinning an observed top/bottom asymmetry would pin
whatever the decoder did today. Orientation stays `TestAvi`'s fact.

**Not proven by any of it, stated plainly:** nothing here is `presentation-verified`. A decoded frame
in memory is not a composited pixel; `client/docs/verification-harness.md` governs and a headless or
in-memory frame never discharges a headed gate. **Cadence, order and timing stay unmeasured — a clip
playing at half speed or backwards passes every fact in this packet.**

## 5. Floor

`spine-tasks/SP-140-real-clip-decode/floor-delta.json` = `{ unit: 6, headless: 0 }`.
Expected observed total **2622 unit / 152 headless** against the 2616/152 pin. The pin is not
touched. `client/tests/floor/floor.json` is never opened.

## 6. Divergences, D323 onward

* **D323** — the D124 measurement: 54 of 54 open and decode, zero refusals, with the named limits
  (one library, one machine, one Windows build, first frame only, decode ≠ presentation).
* **D324** — three of the port's defensive choices that the real library never triggers: the
  negative-stride flip, D125's 80 ms rate-less fallback, D126's one-hour ceiling.
* **D325** — the per-frame byte cost D128's managed compositor must move on this library (up to
  14.7 MB/frame at 30 fps), measured for the first time and still unmeasured as a cost.

Each row: four cells, five unescaped pipes, `|` inside code spans escaped as `\|`, verified by
counting delimiters programmatically before commit.

---

## Checkpoint question for the reviewer

The only judgement call worth a veto: **the fixture is read in place from
`client/spikes/CcpSpike.VideoHandoff/fixtures/clip.mp4` rather than copied into
`client/tests/CcpClient.Tests/assets/`**, which is the path the File Scope nominated. Reasons in §1.
If you want the copy instead, it is a two-line change to the test and a 46 KB add; say so and I will
copy it.
