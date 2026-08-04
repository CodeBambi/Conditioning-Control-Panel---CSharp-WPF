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
| 1 | `spine_review_step --step 1 --type plan` | **SKIPPED** (SP-195: nested reviewer spawn blocked in worker session; engine runs reviews after .DONE). Artifact `.reviews/1-20260804T181349.md` |
| 2 | `spine_review_step --step 2 --type plan` | **SKIPPED** (SP-195, same). Artifact `.reviews/2-20260804T181814.md` |
| 3 | `spine_review_step --step 3 --type plan` | **SKIPPED** (SP-195, same). Artifact `.reviews/3-20260804T184028.md` |
| 4 | `spine_review_step --step 4 --type plan` | **SKIPPED** (SP-195, same). Artifact `.reviews/4-20260804T184033.md` |

## 4. Implementation (Step 2)

**Product seam (additive-only, per-change justification):**

| Change | File | Justification |
|--------|------|---------------|
| Optional ctor param `ISoundClock? clock = null`, default `new SystemSoundClock()` | `DtrhNativeEffects.cs` ctor | The raw `System.Threading.Timer` is the only wall-clock element in the product path under test; determinism is impossible without a seam. Reuses the EXISTING `Audio/AudioSeams.cs:109-133` seam built for exactly this (2026-07-22 consult binding), already consumed cross-feature by `Companion/BarkPipeline.cs:116` — no new coupling class, no DTRH→arbitration ownership wiring. |
| `_videoCapTimer` type `Timer?` → `IDisposable?`, armed via `_clock.Schedule(...)` | `DtrhNativeEffects.cs` `PlayVideoFile` | The only timer-arming site. `SystemSoundClock.Schedule` wraps the identical real `System.Threading.Timer` (same due, one-shot; clamps due<=0 to 0 so zero/negative-cap behavior cannot regress). Production call site `DtrhHostWindow.axaml.cs:298` compiles unchanged; real-clock behavior identical. |

**Test conversions (`DtrhNativeEffectsTests.cs`):**

- New private `ManualClock` fake (the proven `SoundArbitrationTests.cs:551` pattern:
  due+fire capture, in-order Advance firing, CancelHandle honors dispose-cancels).
- `Make()` gains `ManualClock? clock = null` (internally `??= new()`) — EVERY test in the
  class now runs on an injected manual clock; no real timer is ever armed (the consult-
  caught latent-15s-timer surface closed). All existing call-site deconstructions unchanged.
- `FirePayload_Video_PlaysFromPool_RaisesStarted_CapsAtSegment`: toy 0.05s cap + 5s
  wall-clock poll REPLACED by the REAL SEGMENT_SEC=15 parity value on the fake clock.
  Assertions: started/played/non-consumed unchanged; `StopCalls == 0` before advancing and
  at cap−0.1s (strengthening — time-driven, never immediate, never early); after the exact
  remainder advance, `StopCalls == 1` and `ended == 1` (unchanged meaning: the cap stops
  the tape, VideoEnded rides the stop). A wrongly-scheduled cap cannot fire inside the
  exact-15s window.
- `MediaLogging_UserMediaRoot_...` direct construction injects a `ManualClock` (hygiene;
  assertions untouched).
- **Zero assertions weakened/loosened/deleted; zero timeouts widened; no new tests added
  (the seam needed none — the floor stays 537/29 EXACT); suite serialization untouched.**

Step-2 gate: DTRH class 17/17 green; full suite 537/537 + 29/29; build 0W/0E.

## 5. Stability proof (Step 3) — transcripts in `evidence/`

**10 consecutive full-suite runs (both test projects, the suite's NORMAL parallel load —
no serialization switches), ZERO reds of any class:**

| Chain run | Main | Headless |
|-----------|------|----------|
| 1 (18:24Z) | 537/537 | 29/29 |
| 2 (18:25Z) | 537/537 | 29/29 |
| 3 (18:26Z) | 537/537 | 29/29 |
| 4 (18:27Z) | 537/537 | 29/29 |
| 5 (18:28Z) | 537/537 | 29/29 |
| 6 (18:29Z) | 537/537 | 29/29 |
| 7 (18:31Z) | 537/537 | 29/29 |
| 8 (18:32Z) | 537/537 | 29/29 |
| 9 (18:33Z) | 537/537 | 29/29 |
| 10 (18:34Z) | 537/537 | 29/29 |

Transcripts: `evidence/stability-run-{1..10}-{main,headless}.txt` (20 files, full output).
No non-cap-timer flake appeared in the chain (nothing to name via TRX — the wave-4 rule
discharged vacuously). Post-chain process probe: the only `dotnet.exe` processes are
MSBuild node-reuse daemons (`/nodemode:1 /nodeReuse:true`) — ZERO leaked test hosts
(SP-041 zombie rule).

Note: the cap-timer red this task kills was a 1-in-10 flake under load; 10 consecutive
greens post-fix vs 1 red in SP-041's 10-run chain pre-fix is the empirical acceptance the
board row names. The fix is structural (no wall-clock remains in the class), so the green
chain is expected, not lucky.

## 6. Contract verification (Step 4)

- `node .spine/patches/verify.mjs` → **OK** (all patches applied on all roots, exit 0).
- `dotnet build client/CcpClient.sln -c Debug --nologo` → **0 Warning(s), 0 Error(s)**.
- `dotnet test` both projects → **537/537 + 29/29** (floor 537/29 met EXACTLY; zero new
  tests — recorded: the deterministic seam added none).
- `git diff --check` → clean.
- `git status --short` → only File Scope paths (`client/tests/CcpClient.Tests/DtrhNativeEffectsTests.cs`,
  `client/src/CcpClient.Desktop/Features/Dtrh/DtrhNativeEffects.cs`, `spine-tasks/SP-043-dtrh-captimer-tests/**`).
- `fileScopeMustNotChange` honored: `ConditioningControlPanel/**`, `client/CcpClient.sln`,
  `client/spikes/**`, `.spine/**`, `client/docs/task-board.md`, `client/docs/port-lessons.md`,
  `client/src/CcpClient.Desktop/Ai/**` all untouched (enabler 2 respected).
- WSL2 named limit: laptop WSL zero distros — Windows-only evidence, never faked.

## 7. Durable-lesson candidates (for the orchestrator's port-lessons reconciliation)

1. **The repo already owns the fix for every timer flake class:** `ISoundClock`
   (`Audio/AudioSeams.cs`) was built 2026-07-22 for exactly this. Before designing any new
   timing seam, grep for `ISoundClock`/`ManualClock` — the proven fake pattern now exists
   in THREE test classes (`SoundArbitrationTests`, `BarkPipelineTests`,
   `DtrhNativeEffectsTests`).
2. **Latent real timers are a class even when no assertion observes them:** any test that
   arms a product `Timer` and never disposes the fixture leaves a pool-thread callback
   that outlives the test. Injecting the manual clock CLASS-WIDE (not just in the
   observed-flaky test) closes the whole surface at once.
3. **Toy durations hide under fake clocks:** with an injected clock the test can use the
   REAL parity value (SEGMENT_SEC=15) at zero runtime cost — the exact-advance window then
   verifies the due value itself, a stronger assertion than the toy-0.05s wall-clock poll
   ever was.
4. **Discovery (out of scope, recorded):** `DtrhFxRouterTests.cs:34` still constructs
   `DtrhNativeEffects` with the real clock default and routes a fire-payload video message
   — a latent non-observed 15s timer. Zero flake potential (synchronous assertions,
   per-test instances), but the next touch of that file should inject a `ManualClock`.

### Pre-completion solo consult (Step 3)

- **Route:** in-session `consult` tool, mode `solo`. As with the pre-approach call, the
  tool response does not echo its answering-model identity — **actual answering model
  honestly unidentifiable from tool output** (SP-037 record precedent).
- **Verdict:** **DONE — evidence holds, no assertion-meaning drift found.** The advisor
  independently re-derived the original assertion chain and confirmed it preserved and
  strengthened (started/played/non-consumed unchanged; cap-stops-tape + VideoEnded-rides-
  stop unchanged; never-immediate/never-early assertions are pure strengthenings). Checked
  and cleared: `TimeSpan.FromSeconds(14.9)+FromSeconds(0.1)` is exact in ticks (no float
  hazard — and 10 empirical greens corroborate); `SystemSoundClock.Schedule` preserves the
  real `Timer` semantics (same one-shot due, same pool-thread callback, returned Timer is
  the handle so GC cannot collect it early; the only behavioral delta is due<=0 clamping,
  which widens tolerance for a negative cap no call site can produce — cap is a positive
  literal); `ManualClock.Advance` removes the entry before firing so the `StopVideo`→
  `Dispose` reentrancy is safe; the due value is genuinely verified by the exact-15s
  advance window; no other test's behavior changed (no other test ever observed the cap
  timer firing).
- **Adopted strengthening:** capture the structural "zero wall-clock remains" grep as a
  durable artifact → `evidence/zero-wallclock-proof.txt` (only matches: the fake clock's
  own deterministic fields + comments; no `Thread.Sleep`/`DateTime.UtcNow`/`Task.Delay`/
  real `Timer` anywhere in the class or the product file).
- **Noted, no action (advisor reasoning accepted):** with every test on `ManualClock`, the
  production default (`SystemSoundClock`) path is test-dark — but it is pre-existing,
  shipped since 2026-07-22 (SP-029), consumed by `BarkPipeline` in production, and thin by
  construction; adding a wall-clock test for it would reintroduce the exact class this
  task kills. Recorded honestly here.

### Step 3/4 completion

Pre-completion consult verdict recorded above; STATUS.md accurate; contract green (§6);
engine-review presence logged per call (Review Level heading, T-2 format).
