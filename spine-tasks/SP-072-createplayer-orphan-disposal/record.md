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

### Pre-completion (Step 4) — solo

Asked narrowly with a 200-word cap; the tool again did not surface the answering-model id
(mode `solo`, session model `kimi-coding/k3`; complete on-topic verdict, no truncation).

**Verdict: no functional hole in the mechanism; one real fragility + unrun gates.**
(1) FRAGILITY — ADOPTED: the exactly-once pin arms P3 only because the product logs the
abandonment line BEFORE the completed-check; an innocuous reorder would degenerate the pin
to a single-disposer scenario that passes without exercising the latch (the SP-067/SP-070
vacuous class). Fixed with load-bearing-order comments on BOTH ends (product `Create` + the
pin), no behavior change; final-tree green re-run after (run 4). (2) Unrun gates listed —
all now executed and clean (Step 5). (3) `.DONE` uncommitted — followed. "Everything else —
the ordering argument, bite isolation, 20/20, floor 1010→1017, honesty cell — is sound."

## Implementation (Step 2) — per-file git diff summary

- `AudioSeams.cs` (+~230): `PlayerConstructionTimeoutException` (typed no-player outcome);
  `OrphanSafePlayerFactory<TPlayer>` (the mechanism — injected construct/attach/dispose,
  leaf `_lifecycle` lock, CAS latch, bounded caller TryEnter, P3/P4 pool disposers,
  idempotent `Teardown`); `IAudioBackend.CreatePlayer` doc gains the SP-072 contract;
  `OffSyncContext` doc re-pointed (body untouched — its SP-025 pins stay green).
- `SoundFlowAudioBackend.cs`: thin wiring — ctor builds the factory (construct = provider +
  SoundPlayer + wrapper; attach = the residual `AddComponent` line; dispose = wrapper
  Dispose); `CreatePlayer` keeps the TryInit guard then delegates; `Dispose` routes through
  `Teardown`; `CreatePlayerCore` deleted; wrapper gains `internal Player` for attach.
- `SoundFlowDtrhAudio.cs`: same wiring; the inline `Task.Run(...).GetAwaiter().GetResult()`
  OffSyncContext duplicate DELETED (subsumed — construction always on a pool thread).
- `DtrhNativeEffects.cs`: sites #4/#5 gain try/catch mapping construction failure to the
  layer's existing logged-silent-no-op idiom (today those exceptions ESCAPE uncaught);
  `IDtrhAudioBackend.CreatePlayer` doc gains the SP-025/SP-072 contract (signature
  unchanged).
- `SoundArbitration.cs`: **untouched** — sites 1-3 already map any construction exception
  to `SoundOutcome.Failed` / logged drop-continue; the bound rides those catches.

Logging grep (Step 2 checkbox): the only new `_log` calls are the three shown by the diff
grep — one transition line per abandonment (factory) and the two DTRH refusal lines. No
path, no user data (the sfx `name` is a protocol cue token already logged by adjacent
pre-existing lines), nothing persisted, no network/diagnostic calls. No
`SynchronizationContext.Current` capture, no new dispatch primitive, no awaitable UI
dispatch. SP-070/SP-071 properties: untouched by construction (no `SoundArbitration.cs`
edit at all) — proven green in Step 3/5 suite runs.

Worst-case caller wait with the bound: budget (2 s) + one bounded TryEnter (2 s) ≈ 4 s on
the wedge path only (healthy construction is milliseconds) — vs forever today. Sites #3/#4
hold their `_gate` across that bounded wait exactly as they held it across the unbounded
one today; no restructuring (call-site-only scope).

## Engine-review presence

- Step 1 plan review: **skipped in-worker** (nested-spawn block, SP-195) — artifact `.reviews/1-20260814T094516.md`; the engine runs plan/code review post-.DONE.
- Step 2 plan review: **skipped in-worker** (same) — `.reviews/2-20260814T095451.md`.
- Step 3 plan review: **skipped in-worker** (same) — `.reviews/3-20260814T101111.md`.
- Steps 4/5: plan reviews attempted and skipped in-worker identically (recorded before .DONE).

## Bite matrix

Filtered evidence runs (`dotnet test --filter`, the SP-070 bite-run technique — the wrapper
owns the floor; stated deviation, same as SP-071). Each revert applied to `AudioSeams.cs`
alone, product rebuilt, capture under `evidence/`, then `git checkout` restore (restore
verified: 42/42 SoundArbitrationTests green).

| Revert | Line(s) reverted | Expected RED | Actual | Others green |
|---|---|---|---|---|
| 1 — abandonment decision | `slot.Abandoned = true;` | orphan pin | orphan pin RED at its UntilSync (CONDITION-NEVER-TRUE, polls on schedule, `threadpool-pending=0` — fixture reaches the mechanism); exactly-once + ordering pins RED as the EXPECTED CASCADE (no mark → no disposer can ever fire → every pin awaiting a disposal fails its UntilSync) | negative control, torn-down, both caller facts + all 36 landed audio facts green (39/42) — `evidence/bite-1-abandonment-check.txt` |
| 2 — single-dispose latch | the CAS guard in `DisposeOrphan` | ONLY exactly-once pin | ONLY `Construction_CompletionRacesAbandonment_DisposedExactlyOnce` RED, at the count assertion (Expected 1, Actual 2 — both armed disposers disposed), 3/3 runs | orphan, ordering, negative control, torn-down green every run — `evidence/bite-2-single-dispose-latch.txt` |
| 3 — ordering guard | `lock (_lifecycle)` around the orphan dispose | ONLY ordering pin | ONLY `Construction_OrphanDisposal_OrderedAgainstDeviceTeardown` RED, at its ordering assertion (Expected 0, Actual 1 — the orphan was disposed DURING the parked teardown), 3/3 runs | orphan, exactly-once (latch untouched, still arbitrates), negative control, torn-down green every run — `evidence/bite-3-ordering-guard.txt` |

Bite-1 cascade note (honest): the PROMPT's "only" qualifier is on the latch and ordering
reverts — both achieved. Killing the abandonment mark kills disposal entirely, so every
disposal-awaiting pin reds; the cascade IS the proof each fixture reaches the mechanism.

Fixture-reach proof (SP-070 class): bite 1's reds are CONDITION-NEVER-TRUE with actor state
showing the construct returned and no disposal ever occurring; bites 2/3 red at value
assertions (2 vs 1, 1 vs 0) that can only differ if the fixture drove the mechanism.

## Run table

Contract testCommand = `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs`.
All counts: unit 1017/1017 (1015 passed + 2 skipped), headless 35/35. Skipped names in
every run: exactly the 2 Windows-observed pinned names
(`SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked`,
`ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`).

| Run | Worktree | Cold/warm | Unit | Headless | Results dir |
|---|---|---|---|---|---|
| 1 | lane-1 (final tree) | warm | 1017/1017 | 35/35 | `C:\Users\Micha\AppData\Local\Temp\ccp-floor-csm9mR` |
| 2 | `C:\Code\sp072-cold` (NEW worktree, first-ever build; T-14 `.pi/npm` copied in for verify.mjs and removed after; worktree removed after) | **COLD** | 1017/1017 | 35/35 | `C:\Users\Micha\AppData\Local\Temp\ccp-floor-vR8wlu` |
| 3 | lane-1 | warm | 1017/1017 | 35/35 | `C:\Users\Micha\AppData\Local\Temp\ccp-floor-Knu57i` |
| 4 | lane-1 (final tree, post-consult comment additions) | warm | 1017/1017 | 35/35 | `C:\Users\Micha\AppData\Local\Temp\ccp-floor-chcDlf` |

An earlier full-suite green on the final tree (post-bump, pre-bite-work) also passed
1017/1017 + 35/35 — listed for honesty, not counted toward the 3. Run 4 is the definitive
final-tree green (the tree that .DONE ships; runs 1-3 predate only the two comment
additions the pre-completion consult required). 4 consecutive greens total, 1 cold.

**Cross-thread repetition:** 20 consecutive filtered runs
(`dotnet test --filter "FullyQualifiedName~SoundArbitrationTests.Construction_"`, 5 matched
facts — the 3 cross-thread pins + negative control + torn-down refusal), **20/20 Passed,
0 flakes**. Stated deviation (SP-071 precedent): the 20× repetition — and nothing else —
used `dotnet test --filter` outside the wrapper; the contract testCommand ran ONLY through
`verify.mjs → build → check-floor.mjs`.

**Named flake:** `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery`
did NOT fire in any run of this packet (3 contract greens + 20 filtered + the bite runs);
nothing was retried away.

## Honesty cell

1. **What is proven** is the abandonment/disposal/ordering logic against a RECORDING FAKE
   driving `OrphanSafePlayerFactory<TPlayer>` — NOT that a real wedged `AssetDataProvider`
   construction behaves as the fake's parked gate does, and NOT that the real
   `_device!.MasterMixer.AddComponent(p.Player)` line is ever exercised: that line (one per
   backend, in the `attach` delegates) plus the real native `SoundPlayer`/provider disposal
   behavior are **verified by reading only**. No real audio device, endpoint death, or
   wedged native construction can be induced on this machine — that manual gate is named,
   not simulated.
2. **Every caller is bounded** — no caller left unbounded, no remainder row needed. Sites
   1-3 (SoundArbitration) ride existing catches → `SoundOutcome.Failed`; sites 4-5 (DTRH
   effects) gained try/catch → the layer's existing logged-silent-no-op idiom. The DTRH
   call-site catches are verified by BUILD + READING only (their test file is not in this
   packet's File Scope).
3. **Execution vs reading:** executed — the 7 new facts + the whole suite; reading only —
   the two real `attach` lines, real native dispose behavior, the DTRH catches, and the
   SoundFlowDtrhAudio inline-duplicate removal (its behavior is factory-bound; the deletion
   itself is compile-verified).
4. **Linux unproven** — zero WSL distros on this machine; no Linux run claimed. The
   mechanism is platform-neutral managed code (locks, CAS, tasks), but that is an argument,
   not evidence.
5. **SP-071's give-up residue row: UNTOUCHED** (not cheaper, not closed). This packet's own
   residue note is a NEW intended filing (below): each wedged-then-abandoned construction
   parks its construct pool thread until the native call returns (forever on a truly wedged
   endpoint) — sibling mechanism, sibling row class.
6. Worst-case caller wait is now budget + one bounded TryEnter (~4 s) on the wedge path —
   vs forever today; sites #3/#4 hold their `_gate` across it exactly as they did across
   the unbounded wait (no restructuring — call-site-only scope).

## Intended board filings (orchestrator reconciles at land; no row state set by the worker)

1. **This row (SP-072)** — evidence: this record + evidence/; orphan invariant pinned
   (orphan / exactly-once / ordering / negative control / torn-down / caller-vocabulary ×2),
   floor 1010 → 1017, bite matrix 3/3 isolated, 20/20 cross-thread repetitions, 3 contract
   greens (1 cold).
2. **NEW ROW — wedged-construction pool-thread residue** (pre-approach consult's named
   second-order): every abandoned construction whose native ctor never returns parks its
   construct task's pool thread permanently; repeated cues on a dead endpoint accumulate
   them. Sibling of SP-071's open give-up-residue row (which counts backgrounded teardowns;
   this counts backgrounded CONSTRUCTIONS). Do not fold into the SP-071 row — different
   mechanism.
3. **SP-071 give-up residue row stays OPEN** — untouched by this packet (honesty 5).
4. **Owed contract wording** (`client/docs/async-lifecycle-fault-contract.md` is read-only
   this packet): a §5.6-sibling clause for abandoned construction, proposed text —
   "An abandoned player construction (caller wait expired) never reaches the mixer, never
   plays, and is disposed exactly once; its disposal is ordered against device teardown by
   the backend's lifecycle lock; caller-side waits on that lock are always bounded."
5. **Census corrections for the board's record:** the packet predicted "one of five call
   sites constructs inside a lock" — the census found TWO (`SoundArbitration.OnPacingFire`
   :877 under `_gate`; `DtrhNativeEffects.PlaySfx` :112 under `_gate`); "two have no
   try/catch" confirmed exactly (:112, :439 — both now contain typed refusals).
