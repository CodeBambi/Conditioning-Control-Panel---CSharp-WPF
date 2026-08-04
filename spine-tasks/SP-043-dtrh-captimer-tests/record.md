# SP-043 — T-16: DTRH cap-timer tests — deterministic timing discipline

**Task:** make `DtrhNativeEffectsTests.FirePayload_Video_*` deterministic under parallel load.
Fix the timing discipline, NEVER the assertions' meaning. Evidence target: 10 consecutive
full-suite runs, zero cap-timer reds.

## 1. Timing archaeology (Step 1)

### Product cap timer (`client/src/CcpClient.Desktop/Features/Dtrh/DtrhNativeEffects.cs`)

`PlayVideoFile` (`:230-247`) arms a raw `System.Threading.Timer` at
`_options.VideoSegmentCapSec` (production default 15s, EffectPayload SEGMENT_SEC=15 parity);
the callback logs "video segment cap reached" and calls `StopVideo()` (backend `Stop()` +
`VideoEnded` raise — payload-state off rides the video CLOSING, WPF parity).
`_videoCapTimer` is disposed/re-armed on stop-replace, disposed in `StopVideo`,
`OnVideoEnded`, and (via `Teardown`) `Dispose`.

### Test-side timing dependencies (`client/tests/CcpClient.Tests/DtrhNativeEffectsTests.cs`, 426 lines)

Grep-verified: exactly ONE wall-clock site in the file (`Thread.Sleep`/`DateTime.UtcNow`
appear only at :274-275).

| # | Test | Dependency | Classification |
|---|------|-----------|----------------|
| D1 | `FirePayload_Video_PlaysFromPool_RaisesStarted_CapsAtSegment` (:258-279) | `Make(capSec: 0.05)` → product real Timer at 50ms; test polls `video.StopCalls == 0` against a 5s `DateTime.UtcNow` deadline, `Thread.Sleep(20)` | **real timer + wall-clock poll** — THE flake class. SP-041 run-4 red: `Assert.Equal(1, video.StopCalls)` actual 0 (threadpool-starved Timer callback outlasted the 5s poll under full-suite parallel load; re-run green on identical content — wave-4 T-3 precedent profile) |
| — | every other test in the file | synchronous recording fakes only (`RaiseEnded`/`RaiseError` invoked inline; pause/stop calls recorded synchronously) | no timing dependency |

### Latent (non-observed) real timers found during archaeology

- `DtrhNativeEffectsTests` tests that fire a video payload with the default 15s cap
  (`VideoBackend_EndAndError_RaiseVideoEnded`, `MediaLogging_UserMediaRoot_...`) and never
  dispose `fx` arm real 15s `Timer`s that outlive the test. Assertions never observe them
  (synchronous), so no flake potential — but the callbacks later mutate the finished test
  instance's fakes from a pool thread (unsynchronized; consult-caught). Converted to the
  manual clock as hygiene (see §2).
- `DtrhFxRouterTests.cs:34` constructs `DtrhNativeEffects` with the real default and routes
  a fire-payload video message (real 15s timer, never disposed, never observed).
  **Left untouched: outside this task's File Scope.** Recorded as a discovery for the
  orchestrator; zero cross-test state (per-test instances), zero assertion observation.

### Precedents

- c3/q1 injectable-clock seam: `client/src/CcpClient.Desktop/Audio/AudioSeams.cs:109-133` —
  `ISoundClock { UtcNow; Schedule(due, fire) : IDisposable }` + `SystemSoundClock` (real =
  `System.Threading.Timer`; contract: Dispose cancels, a disposed timer's callback never
  runs). Created 2026-07-22 under a pre-approach consult binding for exactly this purpose
  ("watchdog + pacing delays must be unit-testable without real waits"). Already consumed
  cross-feature: `Companion/BarkPipeline.cs:116`.
- `Ai/AiModerationBoundary.cs:189-192` — `Func<DateTimeOffset>? clock = null` ctor default
  (the c3 escalation precedent the board row names).
- Manual-clock fake pattern already proven twice in-suite:
  `SoundArbitrationTests.cs:551-594` and `BarkPipelineTests.cs:703-742`
  (`Schedule` captures due+fire, `Advance` fires due timers in due order, `CancelHandle`
  honors the dispose-cancels contract).

## 2. Design (Step 1) + rejected alternatives

**Per-dependency deterministic shape (D1 — the only one):** injected clock/timer seam per
the c3/q1 precedent.

1. **Product seam (additive-only, justified):** `DtrhNativeEffects` ctor gains an optional
   `ISoundClock? clock = null` parameter, defaulting to `new SystemSoundClock()`.
   `_videoCapTimer` changes type `Timer?` → `IDisposable?`, armed via
   `_clock.Schedule(TimeSpan.FromSeconds(cap), …)`. Production call site
   (`DtrhHostWindow.axaml.cs:298`) compiles unchanged and runs the identical real
   `System.Threading.Timer` (what `SystemSoundClock.Schedule` wraps). **Product behavior
   unchanged.** Justification: the raw `Timer` is the only wall-clock element in the
   product path under test; determinism is impossible without a seam, and the repo already
   owns the exact seam built for this purpose.
2. **Test conversion:** a private `ManualClock` fake (the proven `SoundArbitrationTests`
   pattern). `Make()` ALWAYS injects a `ManualClock` (new optional parameter
   `ManualClock? clock = null`, internally `??= new()` — every call site's deconstruction
   unchanged), killing the latent real timers too. The cap test drops the toy 0.05s cap
   and uses the REAL 15s `SEGMENT_SEC` parity value: fire → assert started/played/log
   (unchanged) → assert `StopCalls == 0` before any advance (strengthening: the cap is
   time-driven, never immediate) → `Advance(just under 15s)` → assert still 0 (cap not
   early) → `Advance(remainder)` fires the callback synchronously on the test thread →
   assert `StopCalls == 1`, `ended == 1` (unchanged meaning: the cap stops the tape and
   `VideoEnded` rides the stop). The due value is implicitly verified: a wrongly-scheduled
   cap cannot fire inside an exact-15s advance window. Zero wall-clock anywhere.
3. `MediaLogging_UserMediaRoot_...`'s direct construction also injects a `ManualClock`
   (hygiene; assertions untouched).

**Rejected alternatives:**

- *Tolerant window + loud flake classifier:* unnecessary — full determinism is available
  via an existing seam; a classifier would be a louder version of the same luck.
- *Wider poll window / longer deadline:* forbidden by the packet as a primary fix (buys
  green with more wall-clock luck, changes nothing structural).
- *New DTRH-local clock interface (`IDtrhClock`):* duplicates `ISoundClock` verbatim;
  cross-feature seam reuse has in-repo precedent (`Companion/BarkPipeline.cs` consumes
  `Audio/AudioSeams.cs`), and consuming a clock interface does not wire DTRH into the
  SoundArbitration ownership the SP-029 boundary protects. Reuse + this justification.
- *Serializing the test or the suite:* forbidden (runs must be under normal parallel load).

## 3. Consults

### Pre-approach solo consult (Step 1)

- **Route:** in-session `consult` tool, mode `solo`. Per the 2026-08-04 rewire the intended
  main route is Opus 5 (Fable 5 fallback); the consult tool's response does not echo its
  answering-model identity — **actual answering model honestly unidentifiable from tool
  output** (SP-037 record precedent).
- **Verdict:** **design APPROVED with four hardenings, all adopted:**
  1. Make() must always inject the ManualClock — otherwise the OTHER video-payload tests
     keep latent real 15s timers that outlive the test and mutate finished fixtures from a
     pool thread (the consult-caught hidden timing surface; adopted — `Make` param).
  2. Use the REAL 15s cap in the converted test, not the toy 0.05s — exercises the actual
     SEGMENT_SEC parity value and still runs instantly on a fake clock; the exact-advance
     window implicitly verifies the due value (adopted).
  3. Convert the direct construction in `MediaLogging_UserMediaRoot_...` too (adopted).
  4. Grep every other `new DtrhNativeEffects(` (adopted — found `DtrhFxRouterTests.cs:34`,
     non-observed latent timer, outside File Scope, recorded as a discovery in §1).
  Also confirmed: reusing `ISoundClock` does not violate the SP-029 DTRH-local-ownership
  boundary (clock seam ≠ arbitration ownership; BarkPipeline cross-feature precedent);
  `SystemSoundClock.Schedule` clamps `due <= 0` to 0, so the seam cannot regress
  zero/negative-cap behavior; timer-callback reentrancy (`StopVideo` disposing the firing
  handle) is safe under the fake because `Advance` removes the entry before firing.

## Review Level: 2 — engine-review presence log (T-2 heading format)

| Step | Call | Result |
|------|------|--------|
| 1 | `spine_review_step --step 1 --type plan` | (recorded at step completion) |

<!-- Steps 2-4 sections appended as executed. -->
