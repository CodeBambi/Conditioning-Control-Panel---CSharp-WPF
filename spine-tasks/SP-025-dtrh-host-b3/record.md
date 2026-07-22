# SP-025 record — DTRH host slice b3: native SFX/audio/video + freeze + rendered tint safety

**Task:** spine-tasks/SP-025-dtrh-host-b3 · **Review Level:** 2 · **Binding spec:** `client/docs/dtrh-admission.md` §7 (b3 row) + §3.2 (named layering divergence)
**Engine-review presence (T-2):** recorded per `spine_review_step` call below.

---

## Engine-review log (T-2)

| Step | Type | Result | Artifact |
|------|------|--------|----------|
| 1 | plan | **SKIPPED BY DESIGN** (nested reviewer spawn blocked in worker session; `skipped=true, spawnFailed=false` — engine runs reviews after `.DONE`, SP-195) | `.reviews/1-20260721T231721.md` |
| 2 | plan | SKIPPED BY DESIGN (same) | `.reviews/2-20260721T233648.md` |
| 3 | plan | SKIPPED BY DESIGN (same) | `.reviews/3-20260721T234936.md` |

---

## Step 4 — headed/WX evidence + divergence decision executed + board reconciliation

### §3.2 divergence decision — DECIDED WITH EVIDENCE: in-page tint/freeze (platform-identical)

**Decision:** tint and freeze VISUALS render IN-PAGE on both platforms (the §3 transport
+ the unchanged payload); the host never composites over the web surface. The only
host-side freeze effect is the native audio/video PAUSE riding `freeze-state`.
**Evidence:** (a) WPF itself deleted every native layered visual in the 2026-07 cutover
(DtrhHostService.cs:472-477 — "no more native WPF layered windows over the game surface");
tint = fx.js `setTint` (region/Lust-Bleed, engine/fx.js:240-241,540-544), VN portrait CSS
tints (cheshireVn.js:84-96,134), init-carried mod tint (modContent.js:25); freeze visuals
= page-rendered (chaosRun.js:1287-1297). (b) The greenfield serves the identical payload
to both engines — in-page rendering is identical-by-construction; the Linux
NativeWebDialog toplevel diverges on NOTHING visual. (c) Windows pixel evidence: the
hub's pink-tinted tunnel renders (runA-host-live.png / runB-hub.png, dark 1.9%, ~170
colors — the fx.js tint pipeline live).
**Rejected alternative:** host-side layering (a native tint/freeze overlay composited over
the web surface) — Windows-only shape per §3.2 (the Linux dialog is a separate toplevel),
would invent a surface WPF itself deleted, and adds nothing the page doesn't render.
**Consequences (named limits):** (1) the VN portrait tint + the in-run freeze pulse are
page-internal and **b4-gated** — the Cheshire guide's `ensureInit()` needs the b4 `meta`
message (`if (!m) return false`, cheshireGuide.js:82-84) and freeze bubbles need a run
(request-run, b4) — recorded with exact mechanism cites, never faked in-slice; (2) on
Linux the covering video window over the separate WebKitGTK toplevel is best-effort
z-order — a session fact, never a compositing claim (pre-approach consult).

### WH (Windows headed, DISPLAY3 owner convention — SetWindowPos + GetWindowRect-verified before EVERY capture; modal-drive rule honored; transcripts `evidence/wh/`)

- **Run A (`runA.log`, EXIT=0):** fx-drive through the REAL parse+dispatch path.
  - **SFX (backend-event-verified, SP-017 discipline):** `wave_clear`→chime1.mp3 (vol 0.64 = 0.80 master × 0.8 chain), `ripple_cast`→Pop2.mp3 (0.48), `Pop` (0.48) — each `playing` then `completed (backend PlaybackEnded, pool N→N-1/8)` — completion = backend event, never call-return.
  - **Whisper (fire-payload audio):** payload `sub_*.mp3` pool pick → `whisper playing` → `whisper completed (backend PlaybackEnded)` ×2; strength/durationMult logged accepted-non-consumed (WPF parity).
  - **Native video (REAL media `evidence-video-04.mp4`, 1920x1080 h264 29.1s, copied from `Z:\CCP Vids` into packet scratch + staged into the RUN-TIME overlay — product code never references Z:):** libvlc up (vmem, sw decode), `payload-state video on` + covering window, **`vmem frame luma≈149-151 (1280x725)` decoded-content proof from the backend**, `first frame presented`; pixel captures `runA-video-playing/frozen-a/frozen-b/video2-frozen.png` (real content, correct colors, dark ~4%, ~450 colors; frozen pair pixel-diff 0.91% vs 1.31%/2.13% cross-diffs).
  - **Freeze (never wedged):** `freeze ON — video pos 4,7s` → 8s later `OFF — video pos 4,7s` (**position frozen**, decoder-reported; the vmem frame counter keeps counting during pause — libvlc re-displays the held frame, recorded). Resume → segment cap at 15.0s exact → `video stopped — payload-state off` + focus reclaimed (WPF :764-775 parity; the cap→payload-state-off gap found and fixed in-step).
  - **Run-boundary hygiene:** `run-ended` → `run-boundary freeze/duck hygiene applied (message stays Deferred(b4))`.
  - **Teardown unwedge:** second video + `freeze ON (pos 2,9s)` → auto-close mid-freeze → **`teardown mid-freeze — force-resumed video + voice (unwedge)`** → clean flow end, EXIT=0 (WPF :896 parity — never a wedged paused clip).
- **Run B (`runB.log`, EXIT=0):** real canvas click on the Warren portal → **page-originated `sfx ui_click` (SP-011 W15 native-cue class)** → unresolved → logged silent no-op (greenfield content gap: the payload ships 8 sfx files; WPF's chaos library is WPF-tree content for a future content row — named limit). Portal first-click opened the intro verb-primer card (`runB-vn-a.png` — warren.js:316 openIntro, page-rendered, pixel-verified).
- **Run C (`runC.log`, EXIT=0):** **VN mix gate on the real backend** (`vn-speaking on — SFX stand down`; the injected cue → `skipped — VN owns the mix`; `off — mix released`). **Pool bound:** 10× `Burst` (7.03s clip) at 0.5s spacing → pool 1/8→8/8 → **`pool full (8) — dropping 'Burst'` ×2 (drop-on-overflow, never queued)** → PlaybackEnded reclaim 8→4.

### Forensics / surprise ledger (Windows)

1. **SoundFlow 1.4.1 AssetDataProvider ctor is SYNC-OVER-ASYNC** (`GetResult` on an async metadata read): on a thread carrying a SynchronizationContext (the Avalonia UI thread) it DEADLOCKS THE DISPATCHER (first run-A: UI wedged at the first cue — dotnet-dump stack `Task.InternalWait` under `AvaloniaSynchronizationContext.Wait` inside `AssetDataProvider..ctor`; the spike's console host never saw it). Fixed at the seam: player construction marshaled off-context when `SynchronizationContext.Current` is present. **This is a product-defect class the SP-017 spike shape could not surface — recorded for the quips/sound row.**
2. **fx-drive culture ×2:** the emitted JSON rendered `0,6` (decimal-comma session culture → JsonReaderException — the SP-024 `{0:N0}` lesson's class) and `@10.4` failed `double.TryParse` (steps silently fell to default spacing). Both switched to InvariantCulture with the failure mode cited.
3. **vmem crop-rectangle class renders BLACK (named limit):** owner-pool videos whose h264 CROPS (coded width ≠ visible width: 3832/1916 vs 3840/1920) deliver luma-0 buffers through vmem AT EVERY target size (console AND product — verified with the backend luma diagnostic), while 16-aligned sources (1280x720, 1920x1080) deliver real content (luma 28/149-154). libvlc logs `Failed to create video converter` for the crop class. Also folded in: libvlc re-asks the format callback with jittering proposals (1920x1088→1090) — the output format FREEZES on the first proposal per stream (mid-stream realloc = freed-pin class). The crop-class presentation fix is the unified-video row's (hw-decode/presentation owner); decode+position+freeze evidence is unaffected.
4. **Headed-harness diagnostics:** PowerShell scriptblocks invoked as Win32 enums write to callback-local scope (`$script:` needed) — earlier "no windows" reports were diagnostic-script artifacts, never app behavior (the SP-024 E-series class, different mechanism).
5. **The 4K/1080p black frames were NOT a product-vs-console difference** — the devloop's "works" was frame-COUNT only; content verification (backend luma) is now part of the product's first-3-frames diagnostics.

### WX (WSL2 Ubuntu 26.04, WSLg X11-via-XWayland; `~/ccp-sp025` native ext4, never /mnt/e; NO input automation, NO timing/latency claims — WSLg jitter is the SP-017 named limit; Wayland never claimed §5)

- **Contract testCommand on the synced tree:** sln Rebuild **0W/0E**; **313/313 + 29/29 green** (≥ the 292/27 b2 floor).
- **Linux natives:** `libvlc5`/`libvlccore9` **3.0.23-1** (apt — the SP-018 pin, re-verified on the image); `libminiaudio.so` present in the build output's `runtimes/linux-x64/native/` (SoundFlow bundled natives flow, SP-010 layout confirmed on Linux).
- **SFX mechanism evidence (Linux backend):** `dtrh-audio: 1 render endpoint(s): RDP Sink (default)` (SP-017 A6 single-device session fact — class fact, not a selection claim) → device up → `sfx 'wave_clear' playing` → **`completed (backend PlaybackEnded)`**; 2× overlapping `Burst` (pool 2/8) → both completed. Backend events on the REAL Linux backend; no timing/latency claims made.
- **Whisper:** pool pick playing → `whisper completed (backend PlaybackEnded)`.
- **Native video (same real media `evidence-video-04.mp4`):** libvlc up (vmem, sw decode), `payload-state video on`, **`vmem frame luma≈149-151 (1280x725)` on Linux**, `first frame presented`, **XGetImage `wx-video-playing.png` — real 1920x1080 content with correct colors on X11**. Transient `Failed to create video converter` ×2 mid-stream WITH content still delivered (recorded; format-jitter proposals 1920x1088→1090 handled by the freeze-on-first-proposal discipline).
- **Freeze (session facts, no timing claims):** `freeze ON — video pos 3,7s` → `OFF — pos 3,7s` (position frozen on Linux; frames counter re-displays as on Windows).
- **Teardown mid-freeze on Linux:** second `freeze ON (pos 6,7s)` → auto-close → **`teardown mid-freeze — force-resumed (unwedge)`** → dialog closing/AdapterDestroyed → flow end → **EXIT=0** (`wx-run.log`).
- **§3.2 divergence decision EXECUTED on Linux:** `wx-dialog-tint.png` (XGetImage of the NativeWebDialog) — **the page-rendered pink-tinted tunnel inside the separate WebKitGTK toplevel**: tint is page-rendered on Linux exactly as on Windows; no host compositing exists or is needed. The covering video window is a separate X11 toplevel (best-effort z-order = session fact, never a compositing claim).
- **Session fact (page-side):** `GStreamer element fakevideosink not found` — WebKitGTK's page-side media pipeline on this WSLg image (SP-011-class session fact; the NATIVE path is unaffected).

### Budgets

Product sln build ~15-60s Windows incremental / ~2-4min WSL cold; tests 33s + 11s Windows, 17s + 9s WSL; run A/B/C ~1min each; WX runs ~1.5min; rsync 2min; V3/crop devloop probes ~2min each. Forensics (SoundFlow deadlock dump, vmem crop class, culture ×2) ~1.5h total.

## Step 3 — protocol upgrade Deferred → Handled

- **`DtrhProtocol.Classify`:** `VnSpeaking`/`Sfx`/`FirePayload`/`FreezeState` → `Handled`; **`Bark` re-labeled `Deferred("voice-arbitration (quips row)")`** (consult item 2 — arbitration is the quips/sound-arbitration row's subsystem; the voice CHANNEL landed in b3). b4/b5 deferrals unchanged.
- **`DtrhFxRouter`** (new, UI-free for fake-driven tests): the four upgraded messages → real effects; `TryRunBoundaryHygiene` = the run-started/run-ended freeze/duck cleanup (WPF `:252`/`:259`/`:513` parity) riding the typed `Deferred(b4)` — consult item 4, invoked BEFORE the deferral log.
- **Host window wiring (`DtrhHostWindow.axaml.cs`):** window-scoped effects lifetime (Opened → real backends + `NotifySessionStart` `:71` parity; Closing → `Teardown` `:896` unwedge → effects dispose → SoundFlow dispose (A8 Δ0/Δ0); libvlc release-at-exit skipped V3). Audio init failure → "audio disabled this session" (WPF device-missing parity). payload-state mirror: VideoStarted → `payload-state video on` + covering `DtrhVideoWindow`; VideoEnded → `payload-state video off` + window close + focus reclaim (`:764-775` parity). OnWebMessage refactored to `HandleWebMessageBody(string)` — the REAL parse+dispatch path the fx-drive feeds (consult item 7).
- **`DtrhVideoWindow`** (new): the covering topmost black window presenting vmem frames (event-driven `FramePresented` → `Image.Source`, never a render timer). b3 scope = native PLAYBACK + freeze interplay; true fullscreen multi-monitor presentation = the BLOCKED unified-video board row (scope boundary recorded).
- **File Scope amendment (SP-023/SP-024 norm, documented here + STATUS + record + board row; `fileScopeMustNotChange` untouched):** `Program.cs` + `App.axaml.cs` — `--dtrh-fx-drive "<steps>"` HARNESS-ONLY timed injection of raw page JSON (`sfx:name[:scale]@t; payload:video|audio@t; freeze:on|off@t; vn:on|off@t; run-started@t; run-ended@t`) for headed/WX evidence without gameplay (runs are b4-gated). `DtrhLaunchCoordinator` (in-scope) threads it.
- **Tests:** `DtrhProtocolTests` classification expectations updated (4 upgrades + bark re-label); `DtrhFxRouterTests` (5 new): b3 Handled classification, bark/b4/b5 deferrals unchanged, every upgraded message dispatches to the recorded fake incl. VN-gate ordering, run-boundary hygiene + classification-stays-b4, non-b3 ignored. `DtrhVideoWindowHeadlessTests` (2 new, draw-level): presented frame lands on the Image (black/Uniform/topmost facts), late frame after close never throws. **313/313 unit + 29/29 headless green; sln Rebuild 0W/0E.**

## Step 2 — native effects core (SFX + freeze + tint mechanism)

- **`Features/Dtrh/DtrhNativeEffects.cs`** (contract-named): seams `IDtrhAudioBackend`/`IDtrhAudioPlayer`/`IDtrhVideoBackend` (unit tests run on recording fakes — never the real backends) + the effects owner. SFX: bounded pool **8 max, drop-on-overflow** (packet decree; ChaosSfx.cs:91-107 cap-6 parity cited), scale default 0.6, `wave_clear`/`ripple_cast` special-case candidate chains, case-insensitive file match (Linux-honest), silent-no-op unresolved (logged), VN mix gate (`:223` parity), volume = clamp(master×scale) (`ChaosSfx.cs:96-103`; master = init masterVolume, b2 currently 80 — consult item 8). Voice: exclusive stop-replace + generation/identity token (F2), pause-only-when-Playing/resume-only-when-Paused (Speech.cs:1651-1669). Freeze: idempotent dedup, video `SetPaused` + voice pause/resume, `NotifyRunBoundary()` (`:252`/`:259`/`:513` hygiene), `NotifySessionStart()` (`:71`), `Teardown()` mid-freeze force-resume unwedge (`:896`) then stop+dispose. fire-payload: video → covering-video pool play + 15s segment cap (EffectPayload.cs SEGMENT_SEC parity) + VideoStarted/VideoEnded events for the host's payload-state mirror; audio → one-shot whisper from the payload `sub_*.mp3` pool; other kinds logged-ignored (`:505-510`); strength/durationMult accepted NON-CONSUMED (WPF behavior + first-attempt lesson).
- **`Features/Dtrh/SoundFlowDtrhAudio.cs`**: the real backend on SoundFlow 1.4.1 (API shape mirrors the admitted SP-017 harness): one playback device, per-channel SoundPlayers on MasterMixer, **F1 discipline** (re-enumerate immediately before init, match by NAME, fresh DeviceInfo only, missing name → default), 10 ms period (spike-recorded quantization).
- **`Features/Dtrh/LibVlcDtrhVideo.cs`**: the real backend on LibVLCSharp 3.10.0 with the SP-018 findings as binding disciplines — V1 software decode, V2 vmem vout (frame-level decode proof), V3 one app-lifetime LibVLC+MediaPlayer with release-at-exit SKIPPED; **Stop() on a background thread** (vmem deadlock class); vmem delegates + frame pin rooted for the process lifetime; UI-thread bitmap swap (WriteableBitmap Bgra8888 ← RV32).
- **V3 dev-loop experiment (consult item 3, try-before-leak):** `evidence/devloop/` scratch console (NOT in the sln), product shape (persistent player, media replaced per fire): **5/5 cycles CLEAN — EndReached → background Stop → Media.Dispose, exit 0** (`evidence/devloop/v3-transcript.txt`, reproduced twice). The probe-shape segfault does NOT transfer; media release-on-replace is ENABLED with the transcript as evidence. Trailing libvlc stderr noise at process teardown (converter/vout errors) recorded — release-at-exit stays skipped.
- **csproj pins (File Scope: Desktop head only):** SoundFlow 1.4.1 + LibVLCSharp 3.10.0 + VideoLAN.LibVLC.Windows 3.0.23.1, each with the live-feed admission comment.
- **Tests (16 new, `DtrhNativeEffectsTests.cs`):** pool bound + drop + reclaim-on-PlaybackEnded, special-case chains + dedicated-wins + case-insensitive + silent-no-op, volume curve values, VN gate (sfx gated, whisper NOT — WPF-mirrored) + idempotent transitions, whisper stop-replace + F2 generation token, freeze idempotent dedup + pause/resume-state discipline, run-boundary clears stale freeze+duck, teardown mid-freeze unwedge-then-stop + idempotency, fire-payload video pool/started/segment-cap + empty-pool silent + unknown-kind ignored + backend end/error → VideoEnded. **308/308 unit + 27/27 headless green; sln Rebuild 0W/0E** (one xUnit2013 analyzer warning found on Rebuild per the xUnit1051 lesson — fixed at source).

## Step 1 — archaeology + package admission + design + pre-approach consult

### WPF archaeology (READ-ONLY, `File.cs:line`)

**SFX (`sfx {name, scale}`):**
- Dispatch: `DtrhHostService.cs:222-234` — `_vnSpeaking` gates the whole cue (`:223-224`: VN owns the mix, stingers stay silent); `scale` default **0.6** (`:226`); special-cases `wave_clear` → `ChaosSfx.PlayWaveClear()`, `ripple_cast` → `ChaosSfx.PlayRippleCast()` (`:227-229`); everything else → `ChaosSfx.Play(name, scale)` (`:230`).
- Pool/cap: `ChaosSfx.cs:91-107` — `MAX_VOICES = 6`, `Interlocked` voice count, **drop-on-overflow** ("a one-shot SFX played late is worse than silence"; cap added after WER audio-storm dumps 0xc0000005/0xc0000409). Per-cue `Task.Run` + own `WaveOutEvent` (the incumbent shape the SP-017 channel ownership replaces).
- Resolution: override-then-fallback candidate lists resolved via `ModResourceResolver.ResolveAudioPath` (`ChaosSfx.cs:21-49`); wave_clear chain = `chaos/wave_clear.mp3` → `lvup.mp3` @0.8 (`:24-25`); ripple_cast chain = `chaos/ripple_cast.mp3` → `chaos/snap.mp3` @0.6 (`:46-47`); boon reveal rare `dling→chime1` / else `thud→bubbles/Pop2` (`:30-35`); generic `chaos/{name}.mp3`, **silent no-op when absent** (`:49`, `:75-93`).
- Volume: `master * scale` clamped 0..1 (`ChaosSfx.cs:96-103`; master = `App.Settings.Current.MasterVolume/100`).
- Page send sites (payload): `game/chaosRun.js:499` (`{type:'sfx', name, scale:(scale ?? 0.6) * 0.5 * getLevel('fx')}`), `game/warren.js:90` (hub cues incl. `ui_click`, `ui_denied` — the SP-011 W15 native-cue path). Matches b2 `DtrhProtocol.Sfx(Name, Scale)`.

**Freeze (`freeze-state {on}`):**
- Page: `game/chaosRun.js:1287-1309` — `activateFreeze()` renders the freeze IN-WORLD (field frozen, `setTimeFactor(0.06)` dilation, `❄ FREEZE` HUD announce, `pulse('150,210,255', 0.30)`, `sfx('freeze_catch')`) then sends `freeze-state on:true` (`:1298`); `endFreeze()` reverses + `freeze_shatter` + `on:false` (`:1300-1309`). The page comment at `:1295-1297` states the host contract: "pause any native voiceline + covering video for the freeze window (resumed on endFreeze). Idempotent host-side."
- Host `ApplyWorldFreeze` (`DtrhHostService.cs:671-698`): **idempotent dedup** on `_worldFrozen` (`:675-677`); UI-dispatched; on → `App.Video?.PausePrimary()` + `App.AvatarWindow?.PauseSpokenAudio()`; off → `PlayPrimary()` + `ResumeSpokenAudio()` (`:679-692`).
- Stale cleanup: **run start** — `_worldFrozen = false` reset at Launch (`:71`) AND `ApplyWorldFreeze(false)` in the `run-started` case (`:259`, comment: "a stale freeze from a crashed prior run must not bleed into this descent's dedup state"); **run end** — `ApplyWorldFreeze(false)` first thing in `OnRunEnded` (`:513`, "a run ending mid-freeze must resume native video + voice, not wedge them through the hub"); **teardown unwedge** — `DisposeAll` (`:896`): "Never leave a video or voiceline wedged paused if the window dies mid-freeze" → force-resume both.
- Pause semantics (incumbent): `VideoService.cs:249-265` — `SetPause(true/false)` over ALL screens' players in lockstep, position-preserving, no-op if none; `AvatarTubeWindow.Speech.cs:1651-1669` — `PauseSpokenAudio` pauses only when `Playing`, `ResumeSpokenAudio` resumes only when `Paused` (a paused clip survives the play-loop; play-loop exits only on Stop).

**Native audio/video the host actually plays (what freeze pauses):**
- `fire-payload {kind, strength?, durationMult?}` (`DtrhHostService.cs:471-523`): since the **2026-07 hard cutover** the browser renders every VISUAL effect in-world (`game/payloadFx.js`) — "no more native WPF layered windows over the game surface" (`:472-477`). Only two native kinds remain: **video** → `EffectPayloadFactory.Build(Video)` (a mandatory covering video window) and **audio** → `Build(Audio)` (a one-shot whisper on the native audio path, `_runSubliminalsHeard++` `:493`); any other kind = version mismatch, log + ignore (`:505-510`). `strength` clamp 0..100, `durationMult` clamp 0.1..10 (`:512-513`).
- Video payload (`EffectPayload.cs:139-159` `VideoPayload.Fire`): `ArmRandomSegment(SEGMENT_SEC=15)` (chaos caps the tape at ~15s, random slice — skipped when Ambient, #456/#458) + `TriggerVideo(silentIfEmpty: true)`; dashboard reuse is uncapped.
- Audio payload (`EffectPayload.cs:183-196` `AudioPayload.Fire`): "Plays a one-shot subliminal whisper (reuses the subliminal audio path)" → `App.Subliminal?.FlashSubliminal()`.
- `payload-state {kind:'video', on}` host→page (`:744` started / `:764` ended): the page pauses/ducks while a mandatory video covers it; on end the host reclaims keyboard focus (`FocusWeb`, `:770-775`). Video watch telemetry accumulates per-run (`:727-740` — b4 telemetry, not b3).

**Tint / portrait:**
- Host side = **init-carried only**: `modId` (`DtrhHostService.cs:186-189`, active persona; default `builtin-sissyhypno` — `SafeActiveModId` `:1035-1041`) + `modContent` (`:191-192`, creator-mod DTRH content incl. `tint:{a,b}` — `DtrhModContent.cs:45-46,82-95,253`). The PAGE renders all tint: VN portrait layer tints (`game/cheshireVn.js:84-96` CSS filters + `:134` colored drop-shadow), region/Lust-Bleed tint (`engine/fx.js:240-241,335-339,540-544` `setTint(color, strength)` — eased line/spiral color lerp; driven by heat/blood-toy `game/chaosRun.js:2228-2230`), mod tint consumed from init (`modContent.js:25` `modTint()`). **There is NO host-side tint window in WPF post-cutover.**
- VN mix ownership: page sends `vn-speaking {on}` (`game/cheshireVn.js:610`, `game/chaosRun.js:3814`); host sets `_vnSpeaking` (`DtrhHostService.cs:218-221`) which gates sfx (`:223`) and barks (`:269`); cleared on run-started (`:252`, "never carry a stale duck into a run").
- VN intro is deterministically triggerable headed: first portal click in the Warren → `openIntro` VN beat (`game/warren.js:1523-1530`, one-time `seenIntroGuide` flag) → `vn-speaking` + tinted portrait render.

**Bark:** `RouteBark` (`DtrhHostService.cs:605-672`) maps ~30 events to `App.Bark.NotifyChaos*` hooks (incl. `freeze-caught` → `NotifyChaosFreezeCaught` `:639`); BarkService's own cooldown/weighting picks the actual voiceline. The payload's `assets/barks/manifest.js` pools serve a DIFFERENT surface (the dive's card-open barks), not the chaos events — no clip source exists in the greenfield for chaos bark arbitration (the quips/sound-arbitration board row owns that subsystem; SP-017 named limit 10).

**First-attempt lessons (READ-ONLY):** `CCP.Avalonia/Chaos/DtrhNativeEffects.cs` = DI bridge over first-attempt services (`IBarkService`/`IVideoService`/…) with a giant nullable surface — lessons only (never a template): its `SetWorldFrozen` doc confirms the pause-both-halves parity reading; `FirePayload` records durationMult NON-CONSUMED by video/audio payloads. Greenfield b3 owns the backends directly instead of bridging to nonexistent services.

### Payload field verification (READ-ONLY, tree `40be29df`)

No `protocol.js` exists in the payload (PROMPT's name for the contract file is `bridge.js`, blob `13af3f4d` + `boot.js` handler registrations) — verified against b2's `DtrhProtocol.cs` records:
- `sfx`: `{name, scale}` — chaosRun.js:499 / warren.js:90 → `Sfx(string? Name, double Scale)` default 0.6 ✓
- `freeze-state`: `{on}` — chaosRun.js:1298/1308 → `FreezeState(bool On)` ✓
- `fire-payload`: `{kind, strength?, durationMult?}` — chaosRun.js:550 (`{...spec.payload, strength, durationMult}`) → `FirePayload(string? Kind, int? Strength, double? DurationMult)` ✓
- `vn-speaking`: `{on}` — cheshireVn.js:610 → `VnSpeaking(bool On)` ✓
- `bark`: `{event, ...}` — chaosRun.js:506 / warren.js:92 / lessons.js:75 → `Bark(string? Event, JsonElement Raw)` ✓
- tint/portrait: no message — init-carried `modId`/`modContent` (`BuildInit` ✓); page renders (`modContent.js` shape comment `:13-14` matches `DtrhModContent.BuildInitPayload`).
- W15 = SP-011 spike item (`webview-dtrh-spike.md:64`: real click → `sfx ui_click` native-cue path), NOT a window-manifest row — PROMPT's cite chain recorded corrected.

### Package admission gate (LIVE feed re-confirmed 2026-07-22 — never transcription)

| Package | Live-feed fact (2026-07-22) | Evidence |
|---|---|---|
| `SoundFlow` **1.4.1** | Still current latest (1.4.1 = top of 15-version train). Nuspec: license **file** `LICENSE.md` → nupkg-verified **MIT** text (LSXPrime 2025). TFM **net8.0 only** (runs on net10.0). Nupkg listing: 28 entries — bundled `runtimes/win-x64/native/miniaudio.dll` + `runtimes/linux-x64/native/libminiaudio.so` (+11 more RIDs) → flows into SP-010 natives-beside-exe automatically; zero apt deps (dlopens libpulse/libasound). | `evidence/nuget-soundflow-index.json`, `nuget-soundflow-1.4.1.nuspec`, `soundflow-1.4.1.nupkg` listing + LICENSE dump (this record) |
| `LibVLCSharp` **3.10.0** | Still current latest. License expression **LGPL-2.1-or-later** (nuspec). net10.0 TFM present. SP-018-admitted decoder shape (per-instance player IS the video shape; SP-017's audio-shape rejection inapplicable). LGPL sidecar/relink = SP-010 packaging note (pending-owner, carried). | `evidence/nuget-libvlcsharp-index.json`, `nuget-libvlcsharp-3.10.0.nuspec` |
| `VideoLAN.LibVLC.Windows` **3.0.23.1** | Still current latest; bundled libvlc win-x64. | `evidence/nuget-libvlc-windows-index.json` |
| Linux native | distro `libvlc` 3.0.23-1 via apt (`libvlc5`, `libvlccore9`) — SP-018: already installed on the WSL2 image; NO official Linux libvlc nuget exists (flatcontainer 404, SP-018). Re-verified on the WSL2 image in Step 4. | SP-018 spike doc §1 |

No new candidates introduced — both backends are the SP-017/SP-018 selections, re-confirmed live per the packet gate. csproj pins land in Step 2 (Desktop head only; the sln untouched).

**Video backend decision:** the SP-018-admitted LibVLCSharp shape with its findings as binding disciplines — **V1** software decode (`--avcodec-hw=none`; D3D11VA segfaults this box even on local parse), **V2** vmem memory-callback vout (dummy vout crashes at/after EndReached; vmem is 5/5 stable AND yields frame-level decode proof), **V3** native release segfaults in the probe shape → **one LibVLC + one MediaPlayer owned app-lifetime by the effects service; per-fire only a new `Media`; native release SKIPPED at teardown** (OS reclaims; clean-teardown ownership stays with the unified-video row per V3 — recorded, not claimed solved). vmem display-callback → `WriteableBitmap` → the covering window's `Image` = presentation + pixel-verifiable frames. The cut covers EXACTLY what freeze pauses (the native video window + the voice channel) — freeze evidence is not hollow.

### Design (pre-consult)

**`Features/Dtrh/DtrhNativeEffects.cs`** (contract-named) — the b3 native-effects owner, constructed per DTRH host window (window-scoped lifetime; teardown on Closing):

- **Seams (unit tests use recording fakes — never the real backends):** `IDtrhAudioBackend` (device init w/ F1 discipline, SFX players, voice player, events) + `IDtrhVideoBackend` (open/play/pause/resume/stop, position + frame/end events). Real impls: `SoundFlowDtrhAudio.cs` (SoundFlow 1.4.1) + `LibVlcDtrhVideo.cs` (LibVLCSharp 3.10.0, vmem) — separate files, same folder (File Scope `Features/Dtrh/**`).
- **SFX channel** (SP-017 channel ownership): bounded pool **max 8 simultaneous, drop-on-overflow** (packet decree; WPF ChaosSfx cap 6 / bubble pool 4 recorded as incumbent parity — SP-017 named limit 7 leaves the exact bound to the owner; the packet's 8 IS that decision, recorded). Pool reclaims on real `PlaybackEnded`. Scale default 0.6 (`DtrhHostService.cs:226`); volume = `clamp(master × scale)` with master from init `settings.masterVolume` (ChaosSfx.cs:96-103 parity; SP-017's product-layer curve `pow(channel×master,1.5)` recorded as the WPF curve — the DTRH host path uses the plain master×scale, ported). Special-cases: `wave_clear` and `ripple_cast` resolve through named candidate chains (WPF shape `ChaosSfx.cs:24-25,46-47`); resolution roots: (1) `sfx/` under the DTRH data dir (user/mod override parity — ModResourceResolver outcome), (2) the payload's own `assets/bubbles/sfx/*.mp3` (Burst/GG/Pop/Pop2/Pop3/chime1-3 — served from `MediaRoot`, overlay-first). Unknown/absent → logged silent no-op (ChaosSfx.cs:75-93 parity — never a crash, never a queue).
- **Voice channel** (whisper): exclusive, stop-replace newest-wins, **generation/identity token** (F2 discipline — PlaybackEnded accepted only from the current player); `Pause` only when Playing / `Resume` only when Paused (Speech.cs:1651-1669 parity). fire-payload `audio` → one-shot whisper picked from the payload's `assets/bubbles/voices/sub_*.mp3` pool (AudioPayload = "one-shot subliminal whisper", EffectPayload.cs:183-196; strength/durationMult accepted, recorded non-consumed — first-attempt lesson + WPF behavior).
- **World freeze:** `SetWorldFrozen(on)` — idempotent dedup on a bool (DtrhHostService.cs:675-677); on → video `SetPause(true)` + voice pause; off → video `SetPause(false)` + voice resume. Lifecycle hooks on the seam: `NotifySessionStart()` (Launch `:71` parity — force-clear), `NotifyRunBoundary()` (run-started `:259` / run-ended `:513` parity — exposed NOW, wired by b4 when it owns those messages; the mapping is recorded so b4 cannot miss it), `Teardown()` (DisposeAll `:896` parity — force-resume BOTH before backend teardown; never a wedged paused clip). Host window Opened/Closing wire session start/teardown in b3.
- **Native video (fire-payload `video`):** covering topmost window over the host window (`DtrhVideoWindow`), libvlc vmem→WriteableBitmap; source = a random video from the served media pool (WPF TriggerVideo-from-pool parity; pool = `*.webm/*.mp4` under `MediaRoot` incl. overlay — the b2 hardcoded 1-video manifest is upgraded to enumerate the pool); 15s segment cap (VideoPayload.SEGMENT_SEC=15 chaos parity) then close; `payload-state {kind:'video', on}` to the page on start/end (`:744/:764` parity) + focus reclaim on end (`:770-775`). Empty pool → silent no-op (`silentIfEmpty: true` parity).
- **VN mix gate:** `SetVnSpeaking(on)` — while on, SFX cues and whispers stand down (logged skip; DtrhHostService.cs:223/269 parity); cleared at session start.
- **§3.2 divergence decision (candidate, evidence-backed):** **in-page tint/freeze via the existing protocol/payload rendering — platform-identical.** Evidence: (a) WPF ITSELF renders every visual effect in-world since the 2026-07 cutover (DtrhHostService.cs:472-477 — "no more native WPF layered windows over the game surface"); tint = fx.js/cheshireVn/modContent (init-carried); freeze visuals = page-rendered (chaosRun.js:1287-1297) with the native pause riding `freeze-state`. (b) Therefore NO host compositing over the web surface exists to diverge — the Linux NativeWebDialog toplevel carries the identical page rendering; the host-side natives (SFX/voice/video window) are never layered over the web surface on either platform. **Rejected alternative:** host-side layering (a native tint/freeze overlay composited over the web surface) — Windows-only shape per §3.2, would invent a surface WPF itself deleted, and adds nothing the page doesn't already render. Consequence recorded on the board row: tint/freeze VISUALS are page-rendered on both platforms; host-verified via pixels on Windows + XGetImage session facts on Linux; the only native freeze effect is the audio/video pause, verified by backend events/positions.
- **Protocol upgrade:** `Classify`: `VnSpeaking`/`Sfx`/`FirePayload`/`FreezeState` → `Handled`. **`Bark` re-labeled `Deferred("voice-arbitration (quips row)")`** — b2's "b3 (voice)" mapping corrected with evidence: the voice CHANNEL (whisper + freeze pause) lands in b3, but bark ARBITRATION (event→voiceline selection over CCP bark pools + cooldowns, DtrhHostService.cs:605-672 → BarkService) has no clip source or owning subsystem in the greenfield; the quips/sound-arbitration board row owns it (SP-017 named limit 10). Typed deferral is never a silent drop. run-started/run-ended stay Deferred(b4); freeze-boundary hooks exposed on the seam for b4.
- **Harness drive (headed/WX evidence without gameplay):** `--dtrh-fx-drive "<steps>"` — the host window feeds synthesized page messages through the REAL `OnWebMessage`→dispatch path on a timer (e.g. `sfx:wave_clear`, `payload:video`, `freeze:on`, `freeze:off`, `payload:audio`). Harness-only (like the probe-* channel precedent), both platforms identical, honestly labeled. File Scope amendment per the SP-023/SP-024 norm: `Program.cs` + `App.axaml.cs` (flag threading to the coordinator → host window); `fileScopeMustNotChange` untouched.
- **Real media evidence:** chosen files COPIED from `Z:\CCP Vids` (`/z/CCP Vids`) into packet evidence scratch + staged into the overlay assets dir at RUN time by the harness (never referenced in-place; product code knows only `MediaRoot`).

### Consults

#### Pre-approach consult (Step 1)

**Mode:** solo (council route broken — T-7). **Requested:** Fable 5. **Actual answering model:** NOT surfaced by the consult tool response (no model identity header — recorded honestly, same provenance discipline as SP-022/023/024).

**Verdict (decisive points folded into the design):**
1. **§3.2 divergence DECIDED = in-page tint/freeze** — sound and honestly evidenced; no host-composited surface remains in WPF post-cutover. **Caveat added:** on Linux the covering video window layers over a SEPARATE WebKitGTK toplevel — "covering" there is best-effort z-order, recorded as a session fact / named limit, never claimed beyond. Linux validation via XGetImage of the page-rendered tint/freeze in the dialog.
2. **Bark re-label honest** — `Deferred("voice-arbitration (quips row)")` approved; b3 must NOT build a minimal bark player. b2 tests pinning `Deferred("b3")` for bark get updated with the classification change (typed + logged, never silent).
3. **libvlc teardown: try-before-leak.** App-lifetime LibVLC + MediaPlayer approved; `Stop()` on window close (Stop ≠ release). `Media.Dispose` per fire: ATTEMPT in the Step-2/3 dev loop (V3 was the probe shape; the product shape is new territory) — dispose after EndReached; if a crash is observed, accept the bounded documented leak. Skip LibVLC/player release at process exit. **Keep the frame-buffer GCHandle + vmem delegates rooted for the process lifetime** (shutdown crash from callbacks into freed managed memory). **Call `Stop()` from a background thread, never the UI thread** (libvlc/vmem callback deadlock class); marshal bitmap updates to the UI thread. Slice is NOT gated on solving V3.
4. **Run-boundary freeze hooks: dispatcher special-case.** `run-started`/`run-ended` stay classified `Deferred("b4")`, BUT the dispatcher invokes the freeze/duck hygiene effects (`ApplyWorldFreeze(false)` + `_vnSpeaking=false`, WPF :252/:259/:513 parity) BEFORE logging the typed deferral — the packet's word is "wired", and seam-only hooks b4 might forget are too risky. Test: synthesize run-started mid-freeze → unwedged + classification still Deferred(b4).
5. **SFX user-override dir root = YAGNI, dropped** (no mod system exists in the greenfield; resolution = payload `assets/bubbles/sfx/` pool + named mapping chains only; override seam recorded as future mod-row scope).
6. **Scope guard:** the native video pool enumerates `MediaRoot` directly; the PAGE manifest stays b1-hardcoded (user/mod media = b4). Staged overlay media is then visible to the native pool without touching b4 scope.
7. **`--dtrh-fx-drive` feeds RAW JSON strings through `OnWebMessage`** (the real parse+dispatch path), harness-only labeled.
8. Recorded: b2's init hardcodes `masterVolume: 80` (volume math derives from it); WPF gates sfx + barks while VN speaks but does NOT gate fire-payload whispers — mirrored exactly (no over-gating).
9. SFX pool 8/drop-on-overflow (packet decree vs WPF cap 6) — no objection; the packet IS the owner decision SP-017 named limit 7 deferred to.
