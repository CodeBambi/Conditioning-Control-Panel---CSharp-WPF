# Task: SP-070 — Audio comes back when the endpoint comes back: the session-disable is permanent and must not be

## Mission

When the playback device fails to come up, the port turns audio off **for the rest of the process**.
`SoundArbitration.Initialize` sets `_audioDisabledForSession = true` on zero render endpoints
(`SoundArbitration.cs:214`) and on a failed `TryInit` (`:236`); `ReadyLocked` (`:601`) then refuses **every**
play on **every** channel forever after, with the typed reason `"audio disabled for the session"`. Nothing
in the process ever clears that flag except a fresh `Initialize` call, and the only product caller of
`Initialize` runs **once**, during DTRH host-window construction (`DtrhHostWindow.axaml.cs:213-220`,
grep-verified at authoring). Unplug a headset, let Windows restart the Audio Endpoint Builder service, or
open the host while the endpoint is momentarily gone, and the app is silent until relaunch.

That behavior is not an accident — it is **faithful parity with WPF's old code**, cited in the port's own
comments as `AudioService.cs:129-131`. **WPF then fixed exactly this.** Commit `d33b5d8d`
(2026-08-03, `#778`/`#779`, "stop the one-shot playback runaway that killed the app when Windows audio
died") states it in one line: *"`_waveOutPermanentlyUnavailable` is no longer permanent."* Upstream's
recovery is a circuit breaker plus an endpoint watcher: consecutive failures suppress playback for a
cooldown, the cached device resolution is dropped, and the next attempt re-resolves against a freshly
enumerated default device. The port inherited the disable and none of the recovery. That is this packet.

**The defect is PERMANENCE, not disabling.** Refusing playback while the endpoint is dead is correct and
stays. What must change is that the refusal is terminal. Do not remove the disable; make it expire.

**THE DIRECTION OF THIS CHANGE IS THE OPPOSITE OF THE LAST TWO WAVES — read this before you reach for their
licence.** SP-068 and SP-069 were subtractive: every change had to narrow. This one makes the app play audio
in situations where it is silent today, so "narrows" is the wrong test and applying it will produce a wrong
implementation. The correct safety argument is bounded restoration: **a recovery may only restore what a
healthy endpoint would already have permitted, and it may never override teardown, panic, or an explicit
stop.** Everything outside that sentence is a widening and is a stop condition.

**THE HAZARD THAT DECIDES THE IMPLEMENTATION (raised by the decomposition consult, then verified against the
tree).** `IAudioBackend.EnumerateDevices` and `TryInit` are native calls into the audio stack. WPF's own
root cause in `d33b5d8d` was that `waveOutOpen` **does not fail fast when the endpoint is gone — it blocks
on the audio-service RPC**, and every clip fired while it was dead parked another blocked thread. Today the
port cannot hit that: it fails fast forever. A naive fix — "re-probe from the play path" — makes every bark
call block on a dead driver, and the play path's calling thread is not established. So the re-probe must be
**gated by the cooldown before it is attempted**, must be **single-flight**, must **never run while
`_gate` is held**, and must not block the caller that discovered it was due. `Initialize` today takes
`_gate` only in short locks and calls the backend outside them (`:200-249`, verified at authoring) — keep
that property; do not fork a second init path.

**Scope is the recovery only. The rest of `d33b5d8d` is NOT owed, and the record must say so** so it is
never re-filed as debt (the SP-069 truncation-non-item precedent):

- The **one-shot MTA worker thread** replacing ~15 copies of the `new WaveOutEvent()` + `Thread.Sleep` idiom:
  **non-item.** That idiom does not exist in this port at all — playback is SoundFlow through
  `IAudioBackend`/`IAudioPlayer`, and player construction already runs off-sync-context
  (`AudioSeams.cs:143` `OffSyncContext`, `SoundFlowAudioBackend.cs:108`).
- The **10-concurrent one-shot cap** and drop-not-queue: **already landed.** `SoundArbitrationOptions.MaxSfxVoices = 8`
  with typed drop-on-overflow (SP-025/SP-029, `SoundArbitration.cs:69-70`). Do not add a second cap.
- The **`IMMNotificationClient` endpoint watcher** (WPF `AudioService.Playback.cs:553`), which re-arms the
  breaker the instant a device returns instead of waiting for the cooldown: **out of scope, its own board
  row.** It is Windows-only native code with no headless proof on this machine, and the lazy re-probe
  delivers the user-visible outcome (audio returns by itself) without it. **Do not implement it here, and do
  not fake a Linux or headed equivalent.**

## Dependencies

- **Task:** SP-069 (landed `6feb11e4`) — the current floor and the tree this packet edits.
- SP-029 / SP-025 — the landed arbitration core, its channel ownership, the SFX cap and the `ISoundClock`
  seam this packet reuses. Nothing about channel ownership, ducking, queueing or panic changes here.
- SP-043 — the timing discipline that made `ISoundClock` injectable in tests (`ManualClock`). **There is no
  excuse for a wall-clock wait in this packet's facts.**

## Context to Read First

- `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs` — **the whole deliverable lives in this one file.**
  `Initialize` (`:200-249`, the two disable sites `:214`/`:236` and the clearing success path `:243-247`),
  the public `AudioDisabledForSession` (`:183`), `SetPreferredDevice` (`:257-260`, already
  stop-then-re-`Initialize` — **reuse it, do not fork a second init path**), `ReadyLocked` (`:595-608`),
  `CreatePlayer` (`:611-631`, the play seam that consults it), `PanicReset` (`:541`),
  `SoundArbitrationOptions` (`:67-77`, where the two new knobs belong beside `MaxSfxVoices`), `_clock` (`:104`)
- `client/src/CcpClient.Desktop/Audio/AudioSeams.cs:113-135` — `ISoundClock` (`UtcNow` **and** the one-shot
  `Schedule`) and `:143` `OffSyncContext`. **Read-only context: everything you need is already injectable.**
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs:205-222` — `InitBarkPipeline`, the
  **only** product construction site, and the single `Initialize(null)` call. **Read-only, out of File Scope**
- `client/src/CcpClient.Desktop/Companion/BarkPipeline.cs:150-200` — the live consumer whose calling thread
  you must establish in Step 1
- `client/tests/CcpClient.Tests/SoundArbitrationTests.cs` — the `Make` factory (`:18`), `FakeBackend` (`:465`)
  and `ManualClock` (`:551`, `Schedule` + `Advance`, fires due callbacks in the same pass). **The failure and
  recovery you are asked to prove are fully constructible here with no device and no real time.**
- `ConditioningControlPanel/Services/Audio/AudioService.Playback.cs` — WPF's recovery: the breaker region
  (`:373`), `NoteOutputSuccess` (`:379`), `NoteOutputFailure` (`:393`), `OutputFailuresToTrip = 5` (`:101`),
  `OutputCooldown = 30s` (`:104`), `InvalidateOutputDeviceCache` (`:152`, `:414`, `:536`), and the
  `EndpointWatcher : IMMNotificationClient` (`:553`) that is **deliberately not ported here**
- `ConditioningControlPanel/Services/AudioService.cs` — the `_waveOutPermanentlyUnavailable` site with its
  new "NOT permanent any more (#779)" comment, and the `PlaySound` catch that now calls `StopSound()` +
  `NoteOutputFailure` instead of stranding an open device (#778)
- `client/docs/upstream-sync.md` §C — the backlog line this packet acts on (**read-only**)
- `client/docs/async-lifecycle-fault-contract.md` — typed outcomes and cancellation discipline
- `client/docs/runtime-capability-contract.md` — the truthful-capability rules the typed audio states serve
- `client/tests/CcpClient.Tests/TestWait.cs` — the shared wait/budget helper; **add no waits outside it**
- `client/tests/floor/floor.json` and `client/tests/floor/check-floor.mjs` — the floor, its `bumpRule`, the
  5 pinned skip names
- `client/docs/port-workflow.md` — §Verification floor and the `CCP_DATA_ROOT` rule at `:204`
- `docs/constitution.md` — standing orders

## File Scope

- `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs` — **the only product file.** The state, the knobs
  on `SoundArbitrationOptions`, and the re-probe live here
- `client/tests/CcpClient.Tests/SoundArbitrationTests.cs` (and any other existing test file that legitimately
  moves — name each one and why in the record)
- `client/tests/floor/floor.json` (count bump only)
- `spine-tasks/SP-070-audio-endpoint-recovery/**`
- **NOT in scope:** every other path under `client/src/**` — in particular `AudioSeams.cs`,
  `SoundFlowAudioBackend.cs`, `Companion/BarkPipeline.cs` and everything under `Features/` —
  plus `client/tools/**`, `client/spikes/**`, `ConditioningControlPanel/**`, `client/docs/**`,
  `docs/constitution.md`, `.spine/**`, `.pi/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/spikes/**`, `client/tools/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**` |
| artifactsMustExist | `spine-tasks/SP-070-audio-endpoint-recovery/record.md` |

`check-floor.mjs` is `--no-build` by design, so the explicit `dotnet build` ahead of it is **not optional** —
standalone the wrapper measures the last build, not the working tree, and fails closed naming the wrong
cause (SP-065 land finding). `FloorWrapperGuardTests` binds every packet with ID >= SP-065: **never** call
`dotnet test` outside the wrapper.

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. Scored: blast radius 2 (the app-wide arbitration core — voice,
whisper and SFX channels, consumed by `BarkPipeline`, `DtrhNativeEffects` and the DTRH host), pattern
novelty 2 (**the port has no failure-recovery or breaker state machine anywhere today** — grep-verified at
authoring), security 0, reversibility 0 → **Level 2**. **T-2 heading format is load-bearing** — record
engine-review presence/absence per call in `record.md`. **Authoring rule (SP-034 defect):
`grep -c "Review Level" PROMPT.md` >= 2 before launch.** Review Level 2 applies to every step below.

## Steps

### Step 1: Establish the two facts the design depends on, then design the recovery

- [ ] Update STATUS.md before starting work
- [ ] **Re-derive WPF's recovery by symbol, not by the offsets above** (a landed audit ages into a map, not a
      citation — SP-068/SP-069 both proved offsets move while semantics hold). For the breaker: the trip
      threshold, the cooldown, what a success does to the counter, what a failure does to the cached device
      resolution, and how the next attempt re-resolves. Record **found-vs-given** for every anchor
- [ ] State plainly which parts of `d33b5d8d` are **non-items in this port and why** (the one-shot idiom, the
      concurrency cap, the endpoint watcher). If you find that any of the three is actually applicable here,
      **stop and report it** rather than widening the packet
- [ ] **FACT 1 — the calling thread of the play seam.** Trace every caller that reaches `CreatePlayer`
      (`SoundArbitration.cs:611`) from product code, and say whether a UI thread can be on that path. Cite
      the call chain. **This fact decides the re-probe shape** and must be written down, not assumed
- [ ] **FACT 2 — panic and teardown are separate mechanisms.** Read `PanicReset` (`:541`) and the teardown
      path and state whether either sets or reads `_audioDisabledForSession`. Confirm that a recovery cannot
      resurrect anything panic cleared or a torn-down instance. If it can, that is a stop condition — say so
      before implementing
- [ ] Design the recovery and write it in the record before writing code: a consecutive-failure counter that
      **success resets**, a cooldown expiry computed from the injected `ISoundClock`, and a **single-flight**
      re-probe that reuses `Initialize` with the remembered preferred device NAME. Constraints that are not
      negotiable: **cooldown checked before the attempt** (never a retry per play call); **`_gate` never held
      across a backend call**; **the caller that discovers the cooldown has expired is never blocked by the
      native probe**; no polling timer, no background service, no wall clock
- [ ] State the WPF-cited defaults you adopt for the two knobs and where they live
      (`SoundArbitrationOptions`, beside `MaxSfxVoices`), and whether you diverge from WPF's 5 / 30s. A
      divergence is allowed but must be argued from the port's own shape, not preferred
- [ ] **Bounded-restoration clearance, written out:** for each of teardown, panic, explicit stop, an active
      device change, and a healthy session, state what the recovery does. "Nothing" is the expected answer
      for most of them and must be provable by a fact
- [ ] **Pre-approach solo consult** (`mode: "solo"` — a bare `consult` hits the council-roster trap, T-7;
      Opus 5 main, Fable 5 fallback). Verdict + **ACTUAL answering model** in `record.md`. **The tool has
      repeatedly returned reasoning-only or mid-sentence-truncated verdicts (board row T-18) — ask narrowly,
      cap the reply length, record exactly what surfaced, and never stitch a verdict out of reasoning.** An
      unstitched non-verdict is a MISSING consult: re-ask it

### Step 2: Implement the recovery in one file

- [ ] The disable becomes **expiring, not terminal**: the two sites at `:214`/`:236` record a failure and arm
      the suppression window; the success path continues to clear everything it clears today
- [ ] The play seam attempts a re-probe only when suppressed **and** the cooldown has elapsed **and** no
      re-probe is already in flight. Single-flight must be provable: N concurrent play attempts produce
      exactly **one** backend init call
- [ ] The re-probe reuses `Initialize` with the remembered preferred NAME (never a stored device Id — F1's
      process-fatal class, `SoundArbitration.cs:196-232`), and never runs while `_gate` is held
- [ ] A successful re-probe clears the suppression and resets the counter; a failed one re-arms the window
      so failure cannot become a busy loop
- [ ] **Teardown and panic are untouched.** No recovery path may run after teardown, and none may re-arm,
      restart, or resurrect anything `PanicReset` cleared
- [ ] **Log on transitions only, never per attempt.** WPF logs the trip once and the recovery once; a line
      per refused play on a dead endpoint is a log-spam regression. Device NAMEs are hardware endpoints
      (already logged today, SP-017 A6) — **no new user data, no file path, no reply text, nothing new
      observed, persisted or transmitted.** Grep your own diff for new log/diagnostic/persist/network calls
      and show the result in the record
- [ ] Typed outcomes only — no exception is allowed to escape the play seam, and the reason string a refusal
      carries must remain honest about which state produced it
- [ ] Summarize the `git diff` for the product file in the record; confirm no edit outside File Scope

### Step 3: Bind the behavior, one source at a time

- [ ] The user story, as a fact: zero endpoints at init → play refused → an endpoint appears → after the
      cooldown, the next play attempt path recovers and audio plays again. **No real device, no real time**
- [ ] Failure-counting and reset: consecutive failures reach the threshold; a success resets the counter so
      the next failure starts from zero
- [ ] **Cooldown is enforced BEFORE the attempt:** with the cooldown unexpired, `FakeBackend`'s init call
      count does **not** move no matter how many plays are attempted
- [ ] **Single-flight:** concurrent play attempts across channels produce exactly one init call
- [ ] **No busy loop:** repeated failures produce exactly one init attempt per cooldown window
- [ ] **Panic:** a panic followed by a recovery-eligible play attempt does not restart a stopped player or
      clear anything panic set; **teardown:** no re-probe happens after teardown, ever
- [ ] **Negative control — a healthy session is byte-for-byte unaffected:** when nothing fails, the backend
      is initialised exactly once and no extra device call, log line, or state transition occurs. This is the
      fact that stops a recovery from becoming a background re-probe loop nobody asked for
- [ ] **BITE TEST, one source at a time:** revert the suppression-clearing alone → only the recovery pins go
      red; revert the single-flight guard alone → only its pin; revert the cooldown gate alone → only the
      no-busy-loop pin. Capture each RED under `evidence/` naming the reverted line and confirming the other
      pins stayed green. **A shared revert is not acceptable evidence** — SP-067's land proved it falsely
      verifies pins that were never exercised
- [ ] Confirm the landed arbitration facts still assert exactly what they asserted before — **zero assertions
      weakened, zero tolerances widened** — with a per-file `git diff` summary. The channel-ownership,
      ducking, queueing and panic suites must be untouched in meaning
- [ ] Bump `floor.json` `total` in the **same commit** as the new facts, reason in the message.
      `allowedSkips`, `admissionRule`, `skipSemantics` untouched

### Step 4: Record + pre-completion consult

- [ ] `record.md`: WPF anchors found-vs-given; the three non-items with the reason each is a non-item; **FACT
      1** (the play-seam calling thread, with the call chain) and **FACT 2** (panic/teardown separation) with
      their evidence; the design and the constraint it satisfies; the adopted knob values with their WPF
      cites and any argued divergence; the bounded-restoration clearance table; the **bite matrix** (three
      separate reverts, three REDs); the floor bump with its reason; the run table with exact counts and
      skipped names; consults + **ACTUAL answering models**; engine-review presence per step; intended board
      filings (state them, set no row state)
- [ ] **Honesty cell — required.** At minimum: (1) this packet delivers recovery **only** — the endpoint
      watcher is not ported, so recovery waits for the next play attempt after the cooldown rather than
      firing the instant a device returns, and a user who never triggers another sound will not hear the
      recovery happen; (2) **no real audio-endpoint death was exercised** — every fact is constructed through
      `FakeBackend`, so what is proven is the state machine, not the driver's behavior when it dies (name
      that as the manual gate it is); (3) whether any behavior was verified by execution vs by reading;
      (4) **Linux unproven** (zero WSL distros on this machine — do not fake a Linux run); (5) the direction
      of this change is restorative, and the one-sentence bound that keeps it safe, stated plainly
- [ ] If the named flake (`ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery`) fired in
      any run, it is recorded by name with run number and TRX path, and was **not** retried away
- [ ] **Pre-completion solo consult**; verdict + actual model in `record.md`
- [ ] STATUS.md accurate before `.DONE`; intended board filings named per ENABLER 2 (set no state)

### Step 5: Testing & Verification

- [ ] Contract testCommand passes **through the wrapper** (`verify.mjs` exit 0, build 0W/0E, new exact unit
      count / 35 headless, skip set exactly the 2 Windows-observed pinned names)
- [ ] **3 consecutive full-suite greens**, >= 1 a **fresh-checkout first-ever build** ("cold" is a NEW
      worktree, not a rebuild in place — port-lessons 2026-08-12). Per-run table: run, worktree, cold/warm,
      unit + headless counts, skipped names, TRX path
- [ ] The bite matrix is complete: **three separate reverts, three separate REDs**, each naming the reverted
      line and the pins that went red — and confirming the others stayed green
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows **no new ignored artifact** produced by any run

## Completion Criteria

- The session-disable expires: a failed device init no longer silences the process for its lifetime, and the
  recovery is proven with no real device and no real time
- The cooldown is checked **before** the probe, the probe is single-flight, `_gate` is never held across a
  backend call, and the discovering caller is not blocked by the native probe — each pinned by a fact
- A success resets the failure counter; a failure re-arms the window; repeated failure produces exactly one
  attempt per window
- Teardown and panic are untouched in behavior and proven so
- A healthy session is unaffected: one init call, no extra device calls, no new log lines
- The three non-items (one-shot idiom, concurrency cap, endpoint watcher) are recorded with their reasons,
  and the endpoint watcher is named as its own future row rather than silently missing
- Each behavior is bound by its own revert — three reverts, three REDs
- Zero assertions weakened, zero tolerances widened, nothing quarantined, nothing added to `allowedSkips`
- `floor.json` `total` bumped in the same commit as the facts that moved it, reason in the message
- 3 consecutive full-suite greens at the stated exact counts, >= 1 fresh-checkout first-ever build
- The record states plainly that no real endpoint death was exercised and that the endpoint watcher is not ported

## Do NOT

- Implement the `IMMNotificationClient` endpoint watcher, a device-change event subscription, or any native
  audio-session callback — that is a separate board row (Windows-only, unprovable on this machine)
- Add a polling timer, a background service, a retry loop, or any periodic re-probe. The only trigger is a
  play attempt whose cooldown has expired
- Block the play seam on a native device call, or hold `_gate` across `EnumerateDevices` / `TryInit` /
  `CreatePlayer` — WPF's own root cause was a device call that blocks instead of failing fast
- Fork a second init path — reuse `Initialize`; `SetPreferredDevice` already shows the sanctioned shape
- Pass a stored device Id anywhere (F1's process-fatal native crash class); the preferred **NAME** is the
  only thing that may be remembered
- Remove or weaken the disable itself, or make the port play through a failure it should refuse
- Re-arm, restart, or resurrect anything `PanicReset` cleared, or run any recovery after teardown
- Change channel ownership, ducking, the voice queue, pacing, the SFX cap, or any other landed arbitration
  behavior; add a second concurrency cap
- Add a `Thread.Sleep`, `Task.Delay`, wall-clock read, or `DateTime.UtcNow` in product or test code — the
  injected `ISoundClock` exists for exactly this (SP-043/SP-059/SP-063); add no waits outside `TestWait`, and
  no timeout literal that trips SP-063's `"Timeout = TimeSpan."` guard token without a marker + pin
- Log per refused play, log a file path or any user data, or add any observation, persisted field,
  diagnostic, or network call
- Edit any product file other than `SoundArbitration.cs` — including `AudioSeams.cs`,
  `SoundFlowAudioBackend.cs`, `BarkPipeline.cs` and `DtrhHostWindow.axaml.cs`
- Weaken, retry, quarantine, or allowlist any test; add anything to `allowedSkips`; touch `admissionRule`,
  `skipSemantics`, or the 5 pinned names
- "Fix" the 2 Windows-observed skips or drive the skip count to 0 — **the asymmetry is correct** and driving
  it to 0 regresses SP-066's honesty
- Call `dotnet test` outside `check-floor.mjs` (`FloorWrapperGuardTests` binds this packet)
- Export `CCP_DATA_ROOT` process-wide for a suite run — it skips the SP-057 pin and blinds the exact-count
  floor (the vacuous-green class SP-062 closed)
- Create any new file under `client/tools/` (`.gitignore:168` — it would pass in-lane and vanish from the
  merged tree)
- Claim a Linux result, a real-device result, or a headed result you did not produce
- Edit `client/docs/task-board.md`, `client/docs/port-lessons.md`, `client/docs/upstream-sync.md`,
  `docs/constitution.md`, `.spine/**`, `.pi/**`, `AGENTS.md`, `CLAUDE.md`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs;
  clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes
- Commit build output (`bin/`, `obj/`) or any results file

## Git Commit Convention

- `feat(SP-070): complete Step N — <summary>`

## Documentation Requirements

**Must Update:** `spine-tasks/SP-070-audio-endpoint-recovery/record.md`, `STATUS.md`
**Check If Affected:** `client/docs/runtime-capability-contract.md` and
`client/docs/async-lifecycle-fault-contract.md` (**read-only for this packet** — if either needs wording for
an expiring-disable state, state the exact wording in `record.md` as a finding for the orchestrator; do not
edit them)
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`,
`client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`

## Amendments

- 2026-08-14 (authoring, orchestrator): **wave 27 runs this row ALONE.** Owner default in force: back to WPF
  parity. A lane-mate was rejected for the standing reason: any second lane that adds or removes a test
  collides on `floor.json` and the exact count — green alone, RED at merge (the SP-054/SP-058 class).
- 2026-08-14 (authoring, orchestrator): **decomposition consult (solo, Opus 5) — complete verdict on the
  first call under a 200-word cap** (the T-18 cap technique holding for a 7th consecutive wave; recorded as a
  technique that works, never as evidence the tool is fixed). Verdict: **proceed**, with three corrections,
  all encoded: **(1) name the defect as PERMANENCE, not disabling** — the port fix is to clear the flag on a
  bounded re-probe, and the one-shot/MTA/cap halves are non-items to be recorded like SP-069's truncation
  non-item so they are never re-filed as owed; **(2) the trigger is a lazy re-probe at the play seam gated by
  the injected clock plus a consecutive-failure counter that success resets — no timer thread, no background
  task — and it must reuse `Initialize` rather than fork a second init path**; **(3) the real widening is not
  panic, it is re-entrancy and blocking** — a re-probe invoked from the play path can re-enter `_gate` or
  block the caller inside a native init exactly as WPF's `waveOutOpen` did, so the cooldown must be enforced
  before the attempt and the probe must run outside the lock and off any UI thread.
- 2026-08-14 (authoring, orchestrator): **the advisor's checkable claims were verified before encoding, not
  trusted.** `SetPreferredDevice` (`:257-260`) does call `StopAllChannels` then `Initialize`, so the
  reuse-don't-fork instruction has a live precedent. `Initialize` (`:200-249`) does **not** hold `_gate`
  across `EnumerateDevices`/`TryInit` — it takes short locks around the flag writes only — so the deadlock
  half of the hazard is already mitigated by the existing shape and the packet's job is to preserve it; the
  blocking half is real and unmitigated. `PanicReset` (`:541`) neither sets nor reads
  `_audioDisabledForSession`, which is why the packet asks the worker to **confirm** panic separation rather
  than assume it is the hazard. `ISoundClock` (`AudioSeams.cs:113-135`) already carries both `UtcNow` and a
  one-shot `Schedule`, and the test `ManualClock` (`SoundArbitrationTests.cs:551`) fires due callbacks on
  `Advance` — so no new seam, no new test primitive and no wall clock are needed for any fact in Step 3.
- 2026-08-14 (authoring, orchestrator): **the port's own trigger is `DtrhHostWindow.axaml.cs:213-220`** — the
  single product `Initialize(null)` call, made once during window construction. That is what makes the
  permanence a live user-visible defect rather than a theoretical one: there is no second call, on any path,
  for the lifetime of the process.
- 2026-08-14 (authoring, orchestrator): **Size M.** The product change is small and lives in one file; the
  weight is evidence — a calling-thread trace, a panic/teardown separation proof, a single-flight fact, a
  no-busy-loop fact, a byte-identical healthy-session negative control, and three independent bite reverts.
- 2026-08-14 (authoring, orchestrator): **`spine preflight`'s `prelanded-file-scope` warning, if it fires,
  must not be obeyed.** It compares `fileScopeMustChange` against **`main`** (`validate-prompt.mjs:196`), and
  `main` is the still-shipping WPF branch carrying **no `client/` tree at all**, while the contract verifier
  uses `baseBranch` from `.spine/spine-config.json` (`feat/crossplatform`). Following its hint (redirect
  `fileScopeMustChange` to delivery artifacts) would manufacture the contract-passes-on-docs-only class
  (SP-214/SP-457). **`fileScopeMustChange` stays pointed at `SoundArbitration.cs`.**
- 2026-08-14 (authoring, orchestrator): machine posture — laptop; **zero WSL distros, so Linux is a standing
  named gate** (do not fake a Linux run); **no audio-endpoint death can be induced here** — the manual gate
  is named in the honesty cell, never simulated as evidence; **MCP not re-probed this phase** — a named
  limit, never a blocker. This packet touches **no AXAML**, so the A-013 advisory step is not a gate for it.
  **`## Review Level: 2` heading present + grep-verified >= 2 (SP-034 authoring rule).**
- 2026-08-14 (authoring, orchestrator): **worker board-row obligation.** SP-001's gap recurred at SP-067 and
  SP-068. ENABLER 2 keeps `task-board.md` out of worker scope, so the row update is **budgeted into the land**
  by the orchestrator. Name your intended filings precisely in `record.md` — that text is what lands.
