# SP-111 — record

Branch `lane/SP-111-video-capability`, base `68bbab0a`.
Floor: pin **1648 unit / 104 headless**; observed **1742 unit / 107 headless**; declared delta
**+94 unit / +3 headless** (`floor-delta.json`). 1648 + 94 = 1742 and 104 + 3 = 107, confirmed by
`node client/tests/floor/sum-deltas.mjs --check --packets SP-111-video-capability`. The floor run
therefore REPORTS a violation against the pin, and that is the expected shape: the orchestrator sums
the deltas and applies one bump. Two skips, both pre-existing
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`); none added, none widened.
Build: 0 errors, 0 warnings. `client/tests/floor/floor.json` was never opened.

**A process defect, recorded rather than only fixed.** The round-1 sweep driver wrote its summary to
`sweep-results.txt` in the SHARED CHECKOUT ROOT rather than inside this worktree. It was never
committed and the coordinator deleted it, but during a parallel wave another lane's `git add -A`
would have swept it into an unrelated commit. **Every artifact belongs inside the lane's worktree.**
The three sweep logs now live beside this record as `sweep-round1.log`, `sweep-round2.log` and
`sweep-round3.log`, which is where §4's counts are checkable from.

---

## 0. THE HEADLINE — the chain, and it stops one step short of a screen

I did not design and then hope. **Before the first product edit** I wrote a throwaway probe and asked
this operating system what it will actually say about a video frame reaching a surface (`plan.md`
§0 carries the raw output). Every GUID and vtable slot in the shipped interop was read out of the
Windows SDK headers on this machine (10.0.26100.0), not recalled.

### The provable chain, and where it stops

| # | Fact | API asked | Measured |
|---|---|---|---|
| **V1** | the OS's own media stack opens a container, reports a video stream and its frame size, and hands pictures back | `MFStartup` / `MFCreateSourceReaderFromURL` / `GetNativeMediaType` / `SetCurrentMediaType(RGB32)` / `ReadSample` | `320x240`, `MF_MT_FRAME_RATE` = 10 fps, `MF_PD_DURATION` = 300 ms, 3 frames then end-of-stream. **This is the TRAP level and is named as such** |
| **V2** | the OS holds the surface: it exists, is visible, holds the requested rectangle, and its own z-order walk puts it above every ordinary window | `IsWindow` / `IsWindowVisible` / `GetWindowRect` / `GetTopWindow`+`GetWindow` | index **1 of 20** visible windows, first ordinary at 6; held bounds exactly the requested `393,244 400x240` |
| **V3** | **the window manager routes a point inside the surface TO the surface** | `WindowFromPoint` | ours — **and it BIT before the capability existed**: with a plain topmost window it answered `HwndWrapper[ConditioningControlPanel;;…]`, the SHIPPING WPF PRODUCT |
| **V4** | **the OS's own copy of the surface carries the DECODED picture, over a bar that reads back exactly its own colour, and CHANGED when a different picture was handed over** | `GetDC(hwnd)` + `GetPixel` differential | **9 of 9** sampled points matched on every frame; the bar control point matched; the surface advanced on all three |
| **V4b** | the OS's own RENDERING of the window agrees, through a call the product never makes | `PrintWindow` into a bitmap of the harness's | **96 000 of 96 000 pixels** equal the port's own composition, on every frame |
| **V5** | **the COMPOSITED DESKTOP carries the decoded picture where the surface is, and it changes frame by frame** | `BitBlt(SRCCOPY\|CAPTUREBLT)` from the screen DC, DPI-mapped (`FlashPixelProbe`) | top and bottom halves read back **exactly** the decoded colours, all three frames, all six values distinct |

**V4 is why this is not the shape the packet forbids.** Nothing in it counts decoded buffers. The
number is the OS's own copy of the surface, compared against the bytes handed over, and it carries
its own negative control MEASURED rather than assumed: hand the SAME picture over twice and the
surface does NOT advance (asserted), while the bar control point is what proves the paint happened at
all (SP-110's M-t lesson, imported).

### WHERE THE CHAIN STOPS — stated plainly, and it stops early

1. **No human watched anything.** `watched-verified` is a **named manual gate** and **no automated
   step on any platform discharges it, Windows included.**
2. **A window read-back is not a monitor.** Measured and ASSERTED rather than confessed in prose: a
   surface at `(-8000,-8000)` — a rectangle no monitor covers — passes every window check AND still
   reads back the decoded picture, because `GetDC(hwnd)` answers about the copy the OS holds FOR THE
   WINDOW. `AWindowREADBACKIsAboutTheOperatingSystemsCopy_NOTAboutAnyMonitor_AndThatLimitIsMeasured`
   pins it. The class that does reach a screen is V5, and V5 is machine-conditional.
3. **V5 is machine-conditional and the condition is measured, never a skip.** A foreign topmost
   window can own the point a surface is at, and it DID: `RealDesktopCollection`'s own doc names that
   residue, and the shipping WPF product produced it unaided here. The harness measures the machine
   with a LANDED capability as its control (a click-through overlay at a disjoint rectangle painted a
   known colour) and asserts V5 only where that control is visible in the same capture.
4. **Nothing measures CADENCE, ORDER or TIMING.** Every fact drives the frame advance by hand on the
   injected clock. A clip that played at half speed, or backwards, satisfies every check here.
5. **Nothing measures SOUND**, because this row has none (D121).
6. **Nothing was ever run against real media.** `Z:\CCP Vids` **does not exist on this machine and
   neither does the Z: volume** — see §2.
7. **Linux is unproven**, refuses in type on BOTH halves, and its six-step gate is undischarged.

The evidence class is written up in `client/docs/verification-harness.md` §"Video evidence class
(SP-111)" with all four classes (`clip-decodable`, `frame-on-surface`, `desktop-composited`,
`watched-verified`) and the three things tier 1 cannot cover for video.

---

## 1. `Z:\CCP Vids` — **it does not exist**, and that is a named limit

```
Z:\CCP Vids exists = False
  drive C:\ type=Fixed ready=True
```

Not a blocker and not a skip. The suite synthesises its own media: `TestAvi` writes an uncompressed
32bpp RIFF/AVI in **pure managed code** — no encoder, no codec, no interop — which Media Foundation
opens as `MFVideoFormat_RGB32`. **The decoder under test is genuinely the operating system's** while
the file contains no lossy step, which is what lets a read-back assert EQUALITY instead of a
tolerance.

**What that costs, stated:** the fixture is natively RGB32, so the source reader's video PROCESSOR is
never exercised. `M-y` — turning `MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING` off — survives the sweep
for exactly that reason, and it is the sharpest single cost of having no real library clip. A real
`.mp4` would close it in one line of fixture.

---

## 2. WHICH MODULE — **Mandatory Video**, and it ships as a SILENT HALF

Of the three rows video blocked: Bubble Count needs video PLUS an interactive counting surface plus
result windows; Visuals is WPF's own dot-less row; **Mandatory Video is the pure video module**, so
every piece of novelty lands on the capability instead of on a second subsystem. It is PACED
(`VideoService.cs:2216-2229`), so `PacedSessionEffect<TFiring>` fits, and its rack position is second
in EFFECTS — which is where BOTH orders that matter put it and they agree (`StudioTabView.xaml.cs:483-488`
for the rack, `MainWindow/MainWindow.StartStop.cs:180-187` for the arm order).

**The clip's SOUND is not ported** (`Audio/**` is closed to this packet, and A/V synchronisation is a
subsystem rather than a line). That gets SP-109's three loud declarations:

1. **The row is titled `Mandatory Video (silent half)`** — the user reads the scope before enabling
   anything, and a headless fact pins the exact string beside the dot it justifies.
2. **The panel LEADS with the missing half** (pinned POSITIONALLY, not only by text), rendering the
   module's own constant verbatim — the same string the arm result's reason carries, so the two
   cannot drift into two accounts of one absence.
3. **`Ready()` returns `Degraded` on EVERY run, however healthy**, carrying `video-silent-half-absent`.
   Every other module in the rack reports a clean `Available` when everything works; this one must
   not, because the absence is a property of the **build**, not of the run.

**And where BOTH causes are true, BOTH travel.** SP-109 shipped that defect once (Brain Drain's
`Ready` replaced the run-level cause with the build-level one). Here an empty pool takes the
`video-no-clip` code — the one the user can fix — and the silent-half notice is carried in the same
detail. `AnEmptyPoolDegradesToo_AndBOTHCausesTravel_TheRunLevelOneFIRST` pins it.

---

## 3. THE DOT'S SEVENTH MEANING — decided: **MOTION**

The six before it were the **clock** (paced), the **screen** (continuous), **change** (moving),
**custody** (non-drawing), **reach** (audio) and **demand** (input).

```
Live  =  a firing is on the clock
      &&  the OS says this process can put a video surface on a display
      &&  ( nothing is playing  OR  the OS's copy of the surface ADVANCED on the last frame )
```

> **The seventh is MOTION: the operating system's own copy of the surface keeps CHANGING — and it is
> the first of the seven that can be false while every call this process made SUCCEEDED.**

**CHANGE (SP-098's third meaning) is the closest and it is a different fact.** "Moving" there is a
claim about this process's own animation state, which this process authors and knows. MOTION is a
claim about what the OS is HOLDING, read back.

**It is deliberately not "the decoder is fed."** That is this packet's central trap in dot form, and
upstream's own worst failure is exactly the state it would light for: "the window stays white and
MediaEnded never fires, wedging cleanup" (`VideoService.cs:2677-2678`), and a frozen final frame
after a suspend (`:1394-1397`). Upstream watches the same thing from the other side, and says so:
its vout watchdog's comment is that **"decode-side health signals (TimeChanged, EndReached) are
useless here — on the white-screen machines the clip 'plays' fine, there is just nothing on screen"**
(`:5538-5540`). That is this rule in upstream's own words.

**The third clause is a DISJUNCTION and that is its own claim.** A session spends almost all of its
time between clips; a bare motion conjunct would darken the dot for 95 % of it, which is the opposite
lie. `M-bh` (requiring motion unconditionally) survived round 1 and is now caught.

| situation | arm result | dot | why |
|---|---|---|---|
| no display / no compositor / no media stack (and Linux) | `Unavailable` / `video-surface-unavailable` | **Armed** | the whole CHANNEL is gone — the Pink Filter answer |
| clip folder empty | `Degraded` / `video-no-clip` | **Live** | a pool is CONTENT, not a channel — the Subliminals answer |
| the silent half | `Degraded` / `video-silent-half-absent` | **Live** | the row is a subset and says so — SP-109's answer |
| a clip up whose picture has STOPPED changing | (nothing new) | **Armed** | the decoder is running and the screen is dead |

---

## 4. PROVING IT BITES — 78 mutations, three rounds, **14 survivors**

Every conjunct and predicate this packet added was mutated one at a time, each file restored
byte-identically afterwards (verified by `git status` after each round). The raw logs are beside
this record: `sweep-round1.log`, `sweep-round2.log`, `sweep-round3.log`, and every count below is
taken from them. The sweep covers the two
observation records, the display observation, the clip info, the decoder's five gates and its stride
flip, the presence's ten gates and its band re-assertion, the letterbox arithmetic and composition,
both fingerprints, the schedule's three clauses, the pool's five, the module's dot, its three
`Compose` refusals, its three `Ready` outcomes and its teardown, and the presenter's six.

**Scope, stated rather than implied.** Each mutation was run against the packet's own five suites
plus `ContinuousEffectSpineTests`, `SecondEffectSpineTests`, `AudioModuleSpineTests`,
`SessionSpineTests` and `StudioSurfaceNoticeTests` — the set that can bite, since every mutated file
is one this packet created or a module only these suites reach. **That is narrower than SP-110's
whole-suite discipline**, and the mitigation is that the full unit suite (1742), the full headless
suite (107) and the floor were all run green afterwards on the restored tree.

### Round 1 — 75 mutations applied, 2 did not patch, **73 evaluated: 50 caught, 23 survived**

The two that did not patch (`M-d`, `M-bs`) had needles that no longer matched the file after an
earlier edit. Both were re-patched with corrected needles in round 2 rather than dropped.

### Round 2 — **28 run: 13 caught, 15 survived**

The 23 round-1 survivors re-run against the closed suite, the 2 that had not patched, and 3 NEW
mutations on clauses this round's own fixes added (`M-bw`, `M-bx`, `M-by`).

### Round 3 — **1 run: 1 caught, 0 survived**

**The books, so a reader need not add up:** 78 distinct mutations; 50 + 13 + 1 = **64 caught**;
**14 survive** (3 equivalent + 11 uncovered); 64 + 14 = 78. Twelve of the round-1 survivors were
real holes and are closed by the facts in the table below.

`M-bw` was the one round-2 survivor a new fact closed inside the same round, so it needed a
re-run to prove the closure rather than assert it. It is caught.

**Twelve were real holes and are now closed** (eleven in round 2, `M-bw` in round 3), each by a
fact that isolates the clause:

| # | mutation | closed by |
|---|---|---|
| M-d | `Confirmed` drops `AboveEveryOrdinaryWindow` | `EveryClauseOfConfirmed_IsLoadBearing` (the round-1 patch text had drifted; re-applied) |
| M-k | `SurfaceAdvanced` drops `Asked` | `AnObservationThatWasNeverASKED_…`, extended to two remembered samples that differ |
| M-x | `Open` skips the video-stream question | `AFileWithNoVIDEOSTREAMSaysSO_RatherThanBlamingThePixelFormat` (a real WAV, opened by the video source) |
| M-bb | the pool stops shuffling | `ThePoolIsASHUFFLEDBAG_…`, extended: two seeds must not deal the same order, and the same seed must |
| M-bh | the dot requires motion with nothing playing | `ALLTHREEClausesOfTheSeventhMeaningAreLoadBearing`, extended to `Showing=false, Running=false` |
| M-bq | `Begin` leaves an empty surface up when the clip decodes nothing | `AClipThatDecodesNOTHING_TakesTheSurfaceStraightBackDOWN` |
| M-br | the max-length cap is ignored | `THEMAXLENGTHCapEndsALongClipEarly_AndZeroMeansNoCapAtAll` |
| M-bs | the presenter keeps feeding a surface that stopped holding the picture | `ASurfaceThatSTOPSHoldingThePicture_EndsTheClipRatherThanFeedingADeadWindow` |
| M-bt | `End` leaks the open decoder | the same three facts, which assert the clip was disposed |
| M-bu / M-bv | `Running` drops either clause | `BOTHClausesOfRUNNINGAreLoadBearing` |
| M-bw | *(new)* `Begin` leaves the surface up when the FIRST picture is refused | `AFirstPictureTheSurfaceREFUSES_AlsoTakesTheSurfaceBackDown` |

Two further new mutations were added and both are caught in round 2: **M-bx** (the letterbox inner
box ignores the margin) and **M-by** (the pool's bag is never filled).

### THE SHARPEST FINDING — M-au / M-av, and it disproved my own comment

`VideoFrame.Sample` and `VideoLetterbox.SamplePoints` both documented their asymmetric fractions as
load-bearing: *"a symmetric grid cannot tell a picture from its own vertical mirror."* **A mutation
that made the point set exactly mirror-invariant SURVIVED.** The reason is that the fold is
ORDER-dependent, so a mirrored picture yields a different SEQUENCE of colours and therefore a
different value from a mirror-invariant set too.

**The claim was corrected rather than defended.** Both doc-comments now say what is true, the
`TheSurfacesSamplePointsAreASYMMETRIC_ForTheSameReason` fact was replaced by one that asserts what
the points are really for (coverage across the picture), and M-au/M-av are reported as **equivalent
mutants with the measurement that makes them equivalent**. A record that had kept the original
sentence would have been a claim the sweep had already refuted.

### THE OTHER FINDING — M-ac, and the occlusion refusal has no positive fact

`video-surface-occluded` is the refusal this capability exists for, and it BIT before the capability
existed (the shipping WPF product owned the surface's point unaided, `plan.md` §0 Q4). **It could not
be staged.** An opaque topmost window placed over the surface's centre and raised after it loses the
point back to the surface's next `Present`, because the presence re-asserts the topmost band
(`MaxBandAttempts`) — which is upstream's own behaviour (`FlashService.cs:206-243`) and is what makes
the capability usable at all. A thief that kept re-asserting would be a race rather than a fact.

So `M-ac` is **UNCOVERED and named**, and the measurement that explains why is itself asserted:
`ASurfaceTAKESItsOwnPointBackFromAWindowPlacedOverIt_AndThatIsWhyOcclusionCannotBeStaged`.

### The fourteen survivors — 3 equivalent + 11 uncovered — and not one is papered over

**Three are EQUIVALENT MUTANTS:**

- **M-a** — `Confirmed` dropping `Asked`. `FrameHeld` is a conjunct of `Confirmed` and carries `Asked`
  itself, so `Confirmed` is false whenever `Asked` is, whatever the other fields say. Redundant, not
  unpinned; kept because each clause reads as its own question.
- **M-au / M-av** — the sample points' asymmetry. See above: the fold's ORDER does the work, the
  comments were wrong, and both are now corrected.

**Eleven are UNCOVERED, and each names why:**

- **M-y** — `MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING` turned off. The fixture is natively RGB32 so
  the video processor is never needed. **The single sharpest cost of `Z:\CCP Vids` being absent**; one
  real compressed clip closes it.
- **M-w** — `Open` ignores `SetCurrentMediaType`'s answer. Needs a file Windows opens but will not
  convert to RGB32; not constructible without an encoder for such a format.
- **M-z / M-z2** — the `CopyOut` length guard and the degenerate-frame-size guard. Defensive against a
  media type the OS reports inconsistently with the buffer it hands back; unreachable with a
  well-formed fixture.
- **M-aa** — `Present` skips the display read. Needs a process with no interactive window station
  (session 0) or no monitor; not constructible in-process, and the display observation is not an
  injectable seam on `Win32VideoPresence` by design (it is what makes the OS the authority).
- **M-ac** — the occlusion refusal. See above.
- **M-ad / M-ae / M-ah** — `Present`/`Show` ignoring `LetterboxHeld` / `FrameHeld`, and the bar control
  point always answering true. Every one needs a live window whose device context does NOT return what
  was painted into it, and **that state was hunted and not found**: even at a rectangle no monitor
  covers the read-back still returns the fill (asserted, §0 stopping point 2). The OUTCOMES they guard
  are covered from the other side — the composed buffer is compared pixel for pixel by `PrintWindow`
  — but the branch itself has no fact.
- **M-al** — `Withdraw` claiming success without re-asking. Needs `ShowWindow(SW_HIDE)` to fail.
- **M-be** — `WorkIsRunning` dropping `ScheduleArmed`. **The same residue SP-110 named as its own
  M-aa**: `OwnedSessionEffect.Dot` returns `Armed` before consulting `WorkIsRunning` when the module
  is disarmed, so isolating the clause needs `ScheduleArmed == false` while armed — which only
  `ReleaseWork` produces, and it is `protected sealed` on `PacedSessionEffect` with the module sealed.
  **Unsealing a product class purely to reach it was rejected**, exactly as SP-110 rejected it.

---

## 5. TRAP 1 — the overlay AND the card, proven unharmed rather than assumed

`Overlay/**`, `Input/**` and `Audio/**` were **not edited**. They were CONSUMED:
`VideoSurfaceObservations.RunCoexistence` builds a real `Win32OverlayPresence` presenting a real
click-through surface AND a real `Win32InputPresence` holding the real foreground, then opens a real
video surface with a real decoded picture on it, then takes it down and disposes it — measuring both
through SP-099's and SP-110's own instruments (`OverlayWindowProbe`, `InputWindowProbe`), unmodified.

Five facts in `VideoOverlayCoexistenceTests`:

1. **Both other capabilities really were up, and the video surface really earned `Available` beside
   them.** Without this leg every fact below would be a test of nothing happening — and without the
   last clause the others would be satisfied by a video capability that politely did nothing.
2. The window manager still routes the overlay's own centre **PAST** it at all three moments, and
   `WS_EX_TRANSPARENT` survives the whole lifecycle. The differential is re-run **on the overlay
   itself**, so "the point went elsewhere" cannot be satisfied by an overlay that stopped existing.
3. The OS's own z-order still puts it **above every ordinary window** at all three moments, it never
   becomes the foreground, and the OS still holds its `LWA_ALPHA` (153, unchanged).
4. **The overlay capability's own `Available` is re-earned** after the video's whole lifecycle.
5. **THE LOCK CARD KEEPS THE FOREGROUND AND THE SYSTEM KEYBOARD FOCUS** while a video surface is up
   AND after it comes down. This is what `WS_EX_NOACTIVATE` is for and it is a real divergence from
   upstream (D122), because the foreground is LENT and Lock Card's whole capability is holding it.

The three rectangles are disjoint, so no surface's hit-test point is occluded by another.

---

## 6. What this work does NOT prove

- **Nothing here proves a human watched anything.** `watched-verified` is undischarged and is not
  dischargeable by this suite or by any automated step on any platform.
- **No headed capture was taken.** `presentation-verified` is untouched. The window read-back is an OS
  query about pixels the OS holds FOR A WINDOW — measured NOT to be monitor-aware — and the
  composited-desktop leg is a screen read from inside the process, not a photograph.
- **Nothing was run against the owner's real media.** `Z:\CCP Vids` does not exist here.
- **Nothing measures cadence, order or timing.** Every frame advance in every fact is driven by hand.
- **FRAME 0's `SurfaceAdvanced` carries no evidence**, and the code review found the comment that
  said otherwise. `Win32VideoPresence.Present` resets BOTH sample slots to zero, so the first
  `Show` after a placement compares its read-back against a SENTINEL rather than against a fold
  taken after the letterbox fill — any non-zero fold advances. The load-bearing advances are
  frames 1 and 2, each measured against the fold of the frame really before it, plus the
  still-frame control, which is the negative half of the same differential. **Behaviour was not
  changed**; the comment at the reset site and the comment on the fact now both say what frame 0
  actually compares.
- **Nothing proves the picture is the RIGHT picture over time.** Each frame is compared against the
  bytes handed over for THAT frame; nothing compares the sequence against the file.
- **Linux video is unproven** on both halves, refuses in type, and the six-step gate in
  `VideoPresenceFactory.LinuxManualGate` is undischarged. **Step 4 is expected to be impossible under
  Wayland**: a client cannot read back its own composited window, so the honest Wayland outcome is
  that the PROOF is unavailable even where the picture works.
- **The decoder is not exercised on a compressed stream** (M-y), so the video-processor path in
  `MediaFoundationClipSource` has no fact behind it.
- **Concurrency is single-threaded.** The presenter's gate is held across no capability call and the
  cadence is driven inline; two threads racing an advance against a teardown are not covered.
- **The GDI composition cost was not profiled.** A 1080p clip into a 55 %-of-4K surface is roughly
  a megapixel of managed nearest-neighbour per frame; measured as fast enough in the harness at
  320x240, not profiled at real sizes on a contended machine. **This is the most likely place the
  port's video will need work.**
- **`SoundArbitration`, `IAudioPresence` and the audio backend are untouched**, and nothing here
  proves a video surface and an audio device coexist under load.

---

## 7. Files changed

**Product — new (`client/src/CcpClient.Desktop/Video/`):** `VideoReasonCodes.cs`, `VideoFrame.cs`,
`IVideoClipSource.cs` (+ `VideoClipInfo`, `IVideoClip`), `MediaFoundationInterop.cs` (raw MF, ComImport,
every slot cited), `MediaFoundationClipSource.cs`, `IVideoPresence.cs` (+ `VideoBounds`,
`VideoSurfaceRequest`, `VideoDisplayObservation`, `VideoSurfaceObservation`), `VideoLetterbox.cs`
(+ `PictureBox`), `Win32VideoInterop.cs`, `Win32VideoPresence.cs`, `UnsupportedVideoPresence.cs`
(+ `UnsupportedVideoClipSource`), `VideoPresenceFactory.cs` (+ `LinuxManualGate`).

**Product — new (effects/session/views):** `Effects/MandatoryVideoSchedule.cs`,
`Effects/VideoClipPool.cs`, `Effects/VideoSurfacePresenter.cs` (+ `IVideoSurface`),
`Effects/MandatoryVideoEffect.cs`, `Session/MandatoryVideoPresetDocument.cs`,
`Views/Pages/VideoPanelNotices.cs`.

**Product — changed:** `Session/EffectReasonCodes.cs` (three additive codes),
`Session/SessionParticipant.cs` (the ninth module, its store, the surface, the rack order, teardown),
`Views/Pages/StudioPage.axaml` + `.axaml.cs` (the row second in EFFECTS, the panel, two dials).

**`Overlay/**`, `Input/**` and `Audio/**` are byte-identical to base.**

**Tests — new.** Counts below are TEST CASES — each `[Theory]` row counted individually, which is
the unit `check-floor.mjs` counts and the unit `floor-delta.json` declares. They sum to the
declared **+94**: 41 + 14 + 10 + 24 + 5 = 94.

| file | cases | what it is |
|---|---|---|
| `TestAvi.cs` | — | the synthesised media writer, pure managed: no encoder, no codec, no interop |
| `VideoSurfaceObservations.cs` | — | the three real-desktop runs (frames, edges, coexistence) |
| `VideoCapabilityTests.cs` | **41** | the OS read-backs, the predicate isolation theories, the Linux refusals |
| `VideoLetterboxTests.cs` | **14** | the letterbox arithmetic and the composition, pure |
| `VideoSurfacePresenterTests.cs` | **10** | the frame cadence, the cap, the endings, the teardown |
| `MandatoryVideoModuleTests.cs` | **24** | the pacing law, the pool, the three arm outcomes, the dot |
| `VideoOverlayCoexistenceTests.cs` | **5** | trap 1: the overlay and the card, across the surface's whole lifetime |

**Tests — changed:** `RealDesktopCollectionGuardTests.cs` (the helper census gains the two video
helpers and the bound controls gain the two new real-desktop classes — a STRENGTHENING),
`AudioModuleSpineTests.cs`, `ContinuousEffectSpineTests.cs`, `SecondEffectSpineTests.cs` (rack-order
and refusal lists grow by one — **0 count change**), `StudioRackHeadlessTests.cs` (the rack list grows
by one; **+3** new cases, which is the whole declared headless delta).

**Docs:** `client/docs/wpf-surface-reachability.md` (§SP-111, D121–D132),
`client/docs/verification-harness.md` (the video evidence class).

---

## 8. Divergences from D121 onward, in one line each

Full rows with citations are in `client/docs/wpf-surface-reachability.md` §SP-111.

| # | In one line |
|---|---|
| **D121** | Mandatory Video ships as its VIDEO half; the clip's SOUND is absent, and it is stated on the row, in the panel and in a `Degraded` arm on every run |
| **D122** | The video surface never takes the foreground (`WS_EX_NOACTIVATE`), where upstream's activates — trap 1, proven by measurement |
| **D123** | A bounded 55 % x 42 % rectangle on the primary display, not fullscreen on every monitor |
| **D124** | The decoder is the OS's own Media Foundation, not the bundled LibVLC — with the cost named: the playable set becomes what Windows can open |
| **D125** | An 80 ms fallback frame interval when the container reports no rate |
| **D126** | A one-hour ceiling on the max-length cap that upstream's setting does not have |
| **D127** | Clips live at `<dataDir>/assets/videos`, the folder is not created, enumeration is RECURSIVE (upstream's) and the draw is upstream's SHUFFLED BAG |
| **D128** | Composition is nearest-neighbour in managed code, blitted 1:1, so the read-back asserts EQUALITY rather than a tolerance |
| **D129** | The picture is inset at least 3 px so the read-back's bar control point always exists |
| **D130** | Linux refuses typed on BOTH halves with a six-step gate whose step 4 Wayland probably cannot pass |
| **D131** | Attention checks, strict/retry, the blur composite, dual-monitor, the browser engine, remote and pack clips, the watchdogs, seek/pause/segment, the duration filter and XP are not ported and are not dead controls |
| **D132** | `DwmGetCompositionTimingInfo` is deliberately not read: measured `MILERR_MISMATCHED_SIZE` against the SDK header's own struct, and its counters are system-wide anyway |
