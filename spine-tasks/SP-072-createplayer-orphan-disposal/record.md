# SP-072 record — An abandoned player construction must never reach the mixer

## Pre-fix observation

Captured in `evidence/pre-fix-observation.txt` (probe source: `evidence/probe/Probe.cs`,
run from a TEMP copy so no build artifacts land in the repo). Summary: with a wedged
construction at the one headless-drivable seam (`SoundArbitration.PlaySfx` + parking fake),
TODAY (1) the caller cannot stop waiting — still blocked 2 s into the wedge, no bound
exists; (2) when the wedge clears late, the construction proceeds straight to attachment
and playback (`Started`, `Playing=True`) — a ghost play; (3) nothing would ever dispose it
(`Disposed=False`). Real backends are unobservable headless (real SoundFlow engine +
device); the difference is named there, and the real
`_device!.MasterMixer.AddComponent(player)` lines (`SoundFlowAudioBackend.cs:118`,
`SoundFlowDtrhAudio.cs:112`) are verified by reading only — both attach UNCONDITIONALLY.

## Census — every caller of both `CreatePlayer` seams (own grep, 2026-08-14)

Implementations:

| Impl | CreatePlayer | Core (attach line) | Bound today | Off-context today |
|---|---|---|---|---|
| `SoundFlowAudioBackend` | `:95-110` | `:112-119`, attach `:118` | none | `OffSyncContext.Run` ✓ |
| `SoundFlowDtrhAudio` | `:88-104` | `:106-113`, attach `:112` | none | inline `Task.Run(...).GetAwaiter().GetResult()` duplicate (`:97-102`) |

Call sites (5):

| # | Site | Seam | Reaching thread(s) | Held while constructing | Typed no-player path today | Cost of a wedge beyond the calling thread |
|---|---|---|---|---|---|---|
| 1 | `SoundArbitration.PlaySfx` `:548` | `IAudioBackend` | caller thread (BarkPipeline hand-off; SP-070: can be UI) | nothing (check-then-create race already handled `:556-570`) | ✓ catch → `SoundOutcome.Failed` (`:550-554`) | calling thread only |
| 2 | `SoundArbitration.CreatePlayer` helper `:747-770` ← `PlayVoice` `:386`, `PlayWhisper` `:463` | `IAudioBackend` | caller thread (BarkPipeline.HandOff `:562` — UI thread via the bark page-message route; whisper channel has no product caller yet, tests only) | nothing | ✓ catch → `SoundOutcome.Failed`; pre-check → `Unavailable` | calling thread only |
| 3 | `SoundArbitration.OnPacingFire` `:877` | `IAudioBackend` | `ISoundClock` timer thread (`SystemSoundClock` = ThreadPool) | **`_gate` HELD across construction** | ✓ catch → logged + `continue` (queued line dropped, `:879-884`) | **the whole arbitration core wedges**: every channel, panic, and `Dispose`'s `PanicReset` all take `_gate` |
| 4 | `DtrhNativeEffects.PlaySfx` `:112` | `IDtrhAudioBackend` | UI thread (`DtrhFxRouter.Handle` ← `DtrhHostWindow.HandleWebMessageBody`, dispatcher-posted `:478`; `DtrhLoomWindow.axaml.cs:243`) | **`_gate` HELD across construction** (`:108-114`) | ✗ **no try/catch** — an exception escapes to the router/host handler today | UI thread frozen **and** the whole effects router wedges (sfx/voice/video routing, `ActiveSfxVoices` all take `_gate`) |
| 5 | `DtrhNativeEffects.PlayWhisper` `:439` | `IDtrhAudioBackend` | UI thread (`PlayWhisperFromPool` via `Dispatcher.UIThread.Post`, `DtrhHostWindow.axaml.cs:451`; harness drive) | nothing | ✗ **no try/catch** — exception escapes | calling (UI) thread |

Authoring-note discrepancy, recorded honestly: the packet says "one of the five call sites
constructs inside a lock" — the census found **two** (#3 `_gate`, #4 `_gate`). "Two have no
try/catch at all" confirmed exactly (#4, #5).

Seam contracts: `IAudioBackend.CreatePlayer` doc (`AudioSeams.cs:62-68`) already REQUIRES
off-context construction; `IDtrhAudioBackend.CreatePlayer` doc (`DtrhNativeEffects.cs:703-704`)
says nothing about thread or failure — gap closed in Step 2 (doc only, signature unchanged).

## Decision rule — branch taken: **BOUND LANDS IN THIS PACKET**

Every caller can accept a typed no-player outcome with its EXISTING vocabulary:

- Sites 1-3 already map any construction exception to `SoundOutcome.Failed` / a logged
  drop-and-continue. Zero call-site change needed; the bound's expiry rides those catches.
- Sites 4-5 return `void`; their layer's established refusal idiom IS the logged silent
  no-op (pool-full drop `:107-110`, unresolved cue `:98-102`, play-failed catch `:131-136`).
  Mapping a construction failure/timeout to that same idiom (one typed log line, no path)
  invents no new semantic — it contains an exception that today ESCAPES uncaught.

## Orphan invariant (written before code)

1. An abandoned construction **never reaches `MasterMixer`**: only the waiter may attach,
   under the factory lifecycle lock, only when construction completed inside the budget and
   the backend is not torn down. The completing thread can NEVER attach — only dispose.
2. It **never plays**: no reference escapes the factory on the abandoned path.
3. It is disposed **exactly once**: waiter (timeout-with-already-completed) XOR completer
   (late completion), latched by a per-construction slot flag flipped under the lifecycle
   lock.
4. Its disposal is **ordered** against device teardown: orphan disposal AND
   `_device.Stop()/Dispose()` + `_engine.Dispose()` both run under the same factory
   lifecycle lock → strictly serialized, never concurrent. SP-071's backgrounded
   `_backend.Dispose()` enters through `Teardown(...)`, which takes that lock.
5. The non-abandoned path is **observably unchanged**: same wrapper object returned, same
   volume, attach-before-return, same unwrapped exception surface
   (`GetAwaiter().GetResult()`), same log lines (zero new lines on the ordinary path).

## Design — where the mechanism lives, and why (testability)

The real backends cannot be constructed headless (zero coverage today; the only audio facts
drive `FakeBackend`). So the mechanism lives in a NEW testable class in `AudioSeams.cs`:

`OrphanSafePlayerFactory<TPlayer>` — injected `construct` / `attach` / `dispose` delegates +
budget + log. Headless facts bind: the abandonment decision, the exactly-once latch, the
teardown ordering, the typed expiry, the torn-down-during-construction refusal, and the
negative control. The backends become thin wiring.

**Residual read-only lines** (verified by reading, never executed by a fact): each backend's
`attach` delegate body — `_device!.MasterMixer.AddComponent(...)` — and the real native
`SoundPlayer`/provider dispose behavior.

### Deadlock-order argument (vs SP-071's backgrounded teardown)

The factory `_lifecycle` lock is a **leaf lock**: no code executes under it that takes
`_gate`, `_initLock`, or any other lock (attach = mixer list add; dispose = best-effort
native calls on the player; teardown delegate = `_device`/`_engine` native calls — none take
managed locks). Observed orders: `_gate` → `_lifecycle` (site #3 constructs under `_gate`,
exactly as today it blocked under `_gate` — now bounded), nothing → `_lifecycle` (all other
paths). SP-071's teardown thread takes `_initLock`, RELEASES it, then calls
`_backend.Dispose()` → `_lifecycle` — never nested. No cycle exists, so no deadlock is
possible. A wedged construction never holds `_lifecycle` (it is only taken AFTER
construction completes or the budget expires), so teardown can always acquire it.

### SP-025 off-sync-context rule

The factory constructs ALWAYS on a `Task.Run` pool thread, which never carries a
`SynchronizationContext` — the dump-proven dispatcher-deadlock property is preserved and now
lives in ONE place. `SoundFlowDtrhAudio`'s inline `Task.Run(...).GetAwaiter().GetResult()`
duplicate is removed (subsumed); `OffSyncContext` itself stays — it has its own SP-025
regression pins (`SoundArbitrationTests.cs:777,:802`) that must stay green — with its doc
updated to point at the factory for player construction. Duplication verdict: **worth
removing here**, because the factory subsumes it; not worth touching `OffSyncContext`'s
tested body.

### The bound

`PlayerConstructionTimeoutException` (typed, in `AudioSeams.cs`). Budget: 2 s
(`OrphanSafePlayerFactory.DefaultBudget`), the SP-071 `TeardownBudget` precedent
(`DtrhHostWindow.axaml.cs:257-260`). Expiry → one transition log line (no path, no user
data) → typed throw → callers' EXISTING refusal vocabulary (sites 1-3: `SoundOutcome.Failed`
via existing catches; sites 4-5: new try/catch mapping to the layer's existing logged
silent-no-op idiom).

## Consults

### Pre-approach (Step 1) — solo

Asked narrowly with a 220-word cap (T-18 technique). The tool did not surface the
answering-model id in its result envelope; recording what is known: mode `solo`, session
model `kimi-coding/k3` (PI_MODEL), answer arrived complete and on-topic (not a
reasoning-only/non-verdict — no re-ask needed).

**Verdict (paraphrase, full sense preserved):** one REAL flaw of the SP-071 class — running
`Teardown(deviceTeardown)` while HOLDING the lifecycle lock re-introduces an unbounded
caller block: a wedged native `_device.Dispose()` holds the lock forever and every
`Create()` then blocks unbounded on it, on the UI thread. Fix: (1) the exactly-once latch
is LOCK-FREE (`Interlocked.CompareExchange` on the slot); (2) every CALLER-side lock
acquisition is bounded (`Monitor.TryEnter` with the budget) — on failure the caller
abandons (typed timeout, one log line); (3) only the completer/orphan-disposer (pool
threads) may block on the lock indefinitely — that blocking IS the ordering guarantee.
Branch decision (bound lands now, 5/5 typed) — AGREED. Second-order, named not built: each
wedged construction parks a pool thread permanently → residue accumulation, sibling of
SP-071's open give-up-residue row → file it, do not close it here (see Intended board
filings).

**Adopted in full.** Revised design (supersedes the lock-everything sketch):

- Slot: `volatile bool Abandoned` + `int State` (0 Pending / 1 Attached / 2 Disposed),
  transitions by `Interlocked.CompareExchange` (the single-dispose LATCH).
- `Create`: fast-fail if torn down (typed, matches today's pre-construction refusal type);
  `Task.Run` construct (SP-025: pool thread never carries a sync context); a
  `OnlyOnRanToCompletion | ExecuteSynchronously` continuation is the late-completion
  disposer (P4); `task.Wait(budget)`; faulted-within-budget rethrows the INNER exception
  (`GetAwaiter().GetResult()` — today's unwrapped surface preserved).
- Within budget: `Monitor.TryEnter(_lifecycle, budget)` — BOUNDED (a wedged SP-071-era
  native teardown may hold the lock). Under it: torn down → dispose (P1) + typed refusal;
  else attach (only the waiter attaches) + return. TryEnter fails → fall through to
  abandonment (caller never waits unbounded).
- Abandonment (timeout or lock-unavailable): set `Abandoned` (volatile) → ONE transition
  log line → if already completed, spawn exactly one pool disposer (P3) → throw typed
  `PlayerConstructionTimeoutException`. The log-before-check order plus the volatile write
  closes the completer-skipped race: any completion is seen by P4 (sees Abandoned) or by
  the waiter's completed-check (spawns P3); the CAS latch decides when both fire.
- `DisposeOrphan` (P3/P4 shared): the abandonment CHECK (`!Abandoned → return`), the LATCH
  (CAS Pending→Disposed), then `lock (_lifecycle)` dispose (the ORDERING guard — pool
  thread, may wait unbounded; that wait IS the ordering vs device teardown).
- `Teardown(deviceTeardown)`: sets `_tornDown` and runs the native device/engine teardown
  under `_lifecycle`. Only orphan disposers (pool) ever wait on it unbounded — never a
  caller.
- Deadlock order (revised): `_lifecycle` remains a leaf lock; caller paths never block on
  it unbounded; SP-071's teardown thread takes `_initLock`, releases it, then
  `_backend.Dispose()` → `_lifecycle` — never nested. `_gate` → `_lifecycle` only via the
  bounded TryEnter (pacing site), which RELEASES by construction (TryEnter timeout). No
  cycle.

## Engine-review presence

(to be filled per step — T-2 heading format)

## Bite matrix

(to be filled in Step 3)

## Run table

(to be filled in Step 5)

## Honesty cell

(to be filled in Step 4)

## Intended board filings

(to be filled in Step 4)
