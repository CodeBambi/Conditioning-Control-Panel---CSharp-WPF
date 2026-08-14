# SP-071 record — Host close must not wait on a wedged native audio probe

## The defect, proven before fixed

`SoundArbitration.Dispose` took `_initLock` (pre-fix `SoundArbitration.cs:1087-1091`) before
`_backend.Dispose()` (`:1093`) on the calling thread. The pre-fix RED is captured in
`evidence/pre-fix-red.txt` (probe source: `evidence/pre-fix-probe.cs`, a throwaway console
harness OUTSIDE `client/tests/**` — never committed as a test): a fake parked inside `TryInit`
on one thread (the recovery probe's exact path: `RunRecoveryProbe` → `Initialize` →
`_initLock`), `Dispose` on another. **RED: `Dispose` did not return within 3 s while `TryInit`
was parked (probe exit 1).** Post-fix the same probe is GREEN: give-up line at the 2 s budget,
`Dispose` returned, backgrounded teardown completed after release with the completion line.

## Caller chain (re-derived, own cites)

- `DtrhHostWindow.axaml.cs:125` — `Closing +=` handler (Avalonia fires it on the **UI thread**);
  `:153` calls `TeardownBarkPipeline()` (method at `:255`), whose `:258` is
  `_barkArbitration?.Dispose()`. The three store waits beside it (`:257,:259,:260`) are already
  bounded at 2 s — the local precedent for this packet's budget.
- `DtrhLaunchCoordinator.cs:167` — `window.Show(_owner)`: the host is a **non-modal child
  window**, so closing it is not process exit; a wedged close leaves the whole app's dispatcher
  stopped while the process lives on.
- Each host window constructs its OWN backend + arbitration (`DtrhHostWindow.axaml.cs:213-214`)
  — load-bearing for the reopened-host answer below.

## The census (evidence/census.md — the full table)

18 sites classified from the 45 raw grep hits (`string.Join` false positives removed;
`lock (_initLock)` in `Dispose` added by hand). Full per-site table with thread, boundedness,
wait target, consequence, and verdict is in `evidence/census.md` and summarized here:

- **Site 1 (FIXED HERE):** `SoundArbitration.Dispose`'s `_initLock` wait — UI-reachable,
  unbounded, waits on a native device init, wedged the dispatcher.
- **Sites 2+3 (SEPARATE PACKET, named by the packet and re-confirmed here):**
  `SoundFlowAudioBackend.CreatePlayer` (`:108` → `OffSyncContext.Run`, `AudioSeams.cs:150`) and
  `SoundFlowDtrhAudio.CreatePlayer` (`:100`) block the calling thread — SP-070 established it
  can be the UI thread — inside a native `AssetDataProvider` construction, unbounded. They are
  their own packet because they change a **synchronous seam contract** (`CreatePlayer` returns
  `IAudioPlayer`) and bounding them creates an **orphan hazard**: a late-completing construction
  adds itself to `MasterMixer` (ghost play + leak) and disposing it races device teardown. That
  packet's central acceptance is orphan disposal.
- **BOUNDED-OK:** the 2 s/3 s store flush/stop waits (`DtrhHostWindow.axaml.cs:257-260`,
  `IntakeHostContext.cs:126-130`) and the secret-tool calls (`SecretStores.cs:145,158,170` —
  effectively bounded: `Run` carries a 5 s CTS, `:269-279`).
- **NON-BLOCKING:** `.Result` on provably-completed tasks (`DtrhHostWindow.axaml.cs:1198`,
  `DtrhLoomWindow.axaml.cs:344,348` — `ContinueWith` after `IsFaulted` check;
  `PersistenceStore.cs:282` — after `Task.WhenAny` already returned `tail`).
- **EXIT-PATH:** `App.axaml.cs:92`, `Program.cs:157,165,168,263` — startup/shutdown/panic
  drains; the process is dying, a hang there is the survivable class.
- **FOLLOW-UP candidates (named, unfixed — out of File Scope):** unbounded in-process disk-store
  starts on UI-reachable paths — `DtrhHostWindow.axaml.cs:228`, `DtrhSaveSlots.cs:467,469`,
  `IntakeHostContext.cs:84,95`, `AssetSelectionDocument.cs:61`, `AiMemoryStore.cs:272`
  (UI-reachable via `CompanionViewModel.cs:315`, holds the store gate while waiting).

## The invariant (written first) and the design that satisfies it

- **I1** exactly one thread ever disposes the backend — the backgrounded teardown thread started
  by the FIRST `Dispose` (the `_tornDown` latch under `_gate`).
- **I2** never concurrent with a native call — `_tornDown` is set before any wait, so
  `InitializeCore`'s early return guarantees no NEW native call starts; the background thread's
  `_initLock` acquisition drains an ALREADY in-flight call before `_backend.Dispose()`.
- **I3** the caller's wait is bounded — `teardown.Join(_options.TeardownBudget)`.
- **I4** the give-up path never touches `_backend` — on expiry it logs ONE line and returns.
- **I5** after a give-up the backgrounded teardown still completes and disposes exactly once
  (proven by `Teardown_GiveUp_BackgroundCompletes_ExactlyOneDispose_CompletionLogged`).
- **I6** idempotent — a second `Dispose` returns promptly, starts nothing, disposes nothing.

Design: UI-safe work (gate block, timer cancel, flag clear) unchanged on the caller;
`PanicReset()` stays **on the caller, BEFORE the handoff** (ordering-critical — consult point:
players are not what `_initLock` guards, and a player dispose racing the backgrounded device/
engine teardown would be a new concurrent-native-call pair by a different door; sequencing
preserves the pre-SP-071 `PanicReset(); _backend.Dispose();` order and keeps
`WhisperBusyChanged` on the caller's thread). Then a named `IsBackground` thread
(`SoundArbitrationTeardown` — WPF `5a168554` "name the next one" shape) takes `_initLock`
unbounded, disposes once, try/catch so nothing escapes (an unhandled background-thread
exception is process-fatal). The give-up/completion log race is closed by an `Interlocked`
3-state (0 running / 1 completed / 2 gave-up): the completion line is logged only when the
give-up line was (a transition pair); a teardown finishing in the Join race logs neither.

## Why a timeout on `_initLock` is the WRONG fix

A bounded acquisition that CONTINUES on expiry runs `_backend.Dispose()` while a native init is
still in flight — the process-fatal concurrent-native-call class `_initLock` exists to prevent,
arriving through the very code meant to make teardown safer. The implementation contains **no
path that proceeds to the backend after failing to acquire `_initLock`**: the lock wait is
unbounded on the background thread; only the caller's OBSERVATION of completion is bounded.

## Budget

`SoundArbitrationOptions.TeardownBudget`, default **2 s** — the in-repo precedent:
`TeardownBarkPipeline`'s store waits (`DtrhHostWindow.axaml.cs:257,259,260`), the same close
handler that calls this `Dispose`. Lives beside SP-070's knobs. No wall clock anywhere in
tests: the give-up facts inject a 200 ms literal whose elapsing IS the subject (TestWait
population 2) and every rendezvous is a deterministic signal (`ManualResetEventSlim` +
`TestWait.UntilSync`); all other facts get `TestWait.InjectedBudget` (SP-063).

## Reopened host

A reopened host constructs a FRESH backend + arbitration (`DtrhHostWindow.axaml.cs:213-214`) —
it never inherits the torn-down owner. After a give-up, TWO backends can exist momentarily: the
old one (background teardown waiting out the wedged native call; it plays nothing — PanicReset
already ran on the caller) and the new one. Safe by construction: miniaudio devices coexist
today (the DTRH boundary runs a second engine/device). Each give-up leaves at most one
`IsBackground` thread alive; it never blocks process exit and the count is bounded by user
close actions (consult point 5).

## Product-file diff summary (the only product file)

`git diff --stat`: `SoundArbitration.cs` +66/−5. Additions: the `TeardownBudget` knob (beside
`RecoveryFailureThreshold`), the `_teardownState` field, and the handoff inside `Dispose`
(comment block + named thread + bounded Join + give-up line). Removed: the inline
`lock (_initLock) { }` + `_backend.Dispose()` on the caller. Self-grep of the diff for new
log/diagnostic/persist/network calls: exactly three new `_log` lines (give-up, completion-after-
give-up, teardown-threw) — all transition-only, none carrying user data; no persistence, no
network, no new observation. **No edit outside File Scope** (`git status --short` shows only
`SoundArbitration.cs`, `SoundArbitrationTests.cs`, `floor.json`, `spine-tasks/SP-071/**`).

## Bite matrix (one revert per mechanism; full wrapper run each; evidence/bite-revert-*.txt)

| Revert | Reverted line(s) | RED | Stayed green |
|---|---|---|---|
| A — off-thread handoff | the named thread + bounded Join + give-up replaced with the pre-SP-071 inline `lock (_initLock) { } _backend.Dispose();` | the 4 parked-fixture pins (bounded-return, ordering at fixture level, completion, idempotence) — each a bounded 20 s CONDITION-NEVER-TRUE, never a hang | negative control + all 1004 others |
| B — `_initLock` drain in the background thread | `lock (_initLock) { ... }` inside the teardown lambda | the ordering pin **at its ordering assertion** (`Assert.False(DisposedWhileInitInFlight)` — Actual: True); the other 3 parked pins red at their own subjects (with no drain, teardown completes instantly so no give-up occurs — the drain is WHY the caller's wait can expire; the coupling is inherent) | negative control + all 1004 others |
| C — single-dispose latch | the `_tornDown` early return in `Dispose` | **ONLY** the idempotence pin (exactly-once assertion: second teardown disposed twice) | all 1007 others incl. the other four SP-071 pins |

Every pin's fixture is shown to reach its mechanism: `ParkedProbe` drives the REAL probe path
(suppressed → kick → `clock.Advance` on a background thread fires `RunRecoveryProbe` →
`Initialize` → parked `TryInit` holding `_initLock`) and PROVES the park (`TryInitInFlight`)
before `Dispose` is called — the SP-070 lesson (a pin passing with its guard reverted because
its fixture never reached the mechanism) cannot recur: under revert A the fixture's park is
proven and the caller never returns; under revert B the fake records the violation directly.

## SP-070 facts unchanged in meaning

`git diff` of the test file across this packet removes exactly two lines: the `Make` signature
(gained an optional parameter; all call sites unchanged) and the trivial
`FakeBackend.Dispose() { }` (gained instrumentation whose null-gate default is byte-identical
behavior). Every landed fact's body is untouched; the full suite greens at 1010/1010 prove it,
and `Recovery_Teardown_NoProbeAfterDispose_Ever` is among them.

## Floor bump

`floor.json` `total` 1005 → **1010** (+5 unit facts: the five teardown pins), in the SAME
commit as the tests (`cbeabe91`), reason in the message and `lastMovedBy`. `allowedSkips`,
`admissionRule`, `skipSemantics` untouched.

## Run table (3 consecutive full-suite greens at the final tree `85156ec9`; each: verify.mjs OK → dotnet build 0W/0E → check-floor.mjs FLOOR OK)

| Run | Worktree | Cold/warm | Unit | Headless | Skipped (exact names) | TRX dir |
|---|---|---|---|---|---|---|
| 1 | lane-1 (final tree) | warm | 1010/1010 (1008 passed) | 35/35 | `ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps`, `SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked` | `C:\Users\Micha\AppData\Local\Temp\ccp-floor-icXdBf` |
| 2 | `C:\Code\sp071-cold` (NEW worktree, first-ever build; T-14 `.pi/npm` copied in for verify.mjs; removed after) | **COLD** | 1010/1010 | 35/35 | same 2 pinned names | `C:\Users\Micha\AppData\Local\Temp\ccp-floor-kwedcf` |
| 3 | lane-1 | warm | 1010/1010 | 35/35 | same 2 pinned names | `C:\Users\Micha\AppData\Local\Temp\ccp-floor-MRsLvB` |

Plus one earlier full-suite green on the final tree captured mid-bite-work (a revert that
failed to apply ran the unmodified final tree; console capture: FLOOR OK 1010/1010 + 35/35) —
listed for honesty, not counted toward the 3.

**Cross-thread repetition:** 20 consecutive filtered runs
(`dotnet test --filter "FullyQualifiedName~Teardown|FullyQualifiedName~Dispose_"`, the SP-070
bite-run technique — filtered evidence runs; the wrapper owns the floor), 23 matched facts
(the 5 new pins + SP-070's teardown fact + the TeardownFlush/Dispose-named landed facts),
**20/20 Passed, 0 flakes**. **Deviation stated plainly (pre-completion consult):** the 20x
repetition — and nothing else — used `dotnet test --filter` outside the wrapper; the contract
testCommand ran ONLY through `check-floor.mjs` (verify.mjs -> build -> wrapper, 3 times plus
the 3 bite runs).

**Named flake:** `ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` did
NOT fire in any run of this packet (3 full-suite greens + 20 filtered + 3 bite full-suite
runs); nothing was retried away.

## Consults (solo mode only — T-7)

1. **Pre-approach (solo):** verdict **design sound, no hole in the invariant**, with four
   fixes/notes, all encoded: (1) PanicReset ordering-critical BEFORE thread start (commented as
   such); (2) drop the completion event, use `Join(budget)` (no disposal trap); (3) the
   Interlocked 3-state closes the give-up/completion log race; (4) `_backend.CreatePlayer` is
   unguarded by `_initLock` — a pre-existing in-flight-play-vs-teardown race, unchanged here,
   belonging to the census site-2/3 packet; (5) each give-up leaves one unbounded IsBackground
   thread, bounded by user close actions. Complete verdict on the first call, no truncation.
   **Actual answering model:** the tool response did not self-identify; solo mode is configured
   to `anthropic/claude-opus-5` (`.pi/bpx-consult.json`) — recorded from config, not stitched
   from reasoning (T-18 discipline).
2. **Pre-completion (solo):** verdict **no code change — ship after two record edits**, both
   applied above: (1) the residual unbounded UI-thread wait inside `PanicReset`'s player
   dispose (honesty cell 3b + filing 5) — an overclaim gap no fake could see; (2) the
   `dotnet test --filter` deviation stated plainly in the run table. Complete verdict on the
   first call, no truncation. **Actual answering model:** same form as the pre-approach entry
   (not self-identified; solo configured to `anthropic/claude-opus-5`).

## Engine-review presence (Review Level 2; T-2 heading)

- Step 1 plan review: **engine-skipped** (SP-195 — nested reviewer spawn blocked inside the
  worker session; the batch engine runs reviews after `.DONE`). Artifact:
  `.reviews/1-20260814T014526.md`.
- Step 2 plan review: **engine-skipped** (same). Artifact: `.reviews/2-20260814T014939.md`.
- Steps 3-5: no in-worker review spawned; code + final review are engine phases after `.DONE`.

## Owed contract wording (client/docs is read-only for this packet)

`async-lifecycle-fault-contract.md` §5's post-only rule exists "so that no operation can wait
on the UI thread". Suggested addition (for the orchestrator, NOT applied): *"Teardown paths
reachable from the UI thread must bound their wait on any native or backgrounded work; the
unbounded portion runs off the UI thread and the give-up path must not touch the resource the
backgrounded teardown owns (SP-071)."*

## Intended board filings (stated, no row state set)

1. **The SP-071 row itself:** evidence = this record; closes census site 1.
2. **NEW ROW — the two `CreatePlayer` sites** (`SoundFlowAudioBackend.cs:108` →
   `OffSyncContext.Run`, `SoundFlowDtrhAudio.cs:100`): unbounded UI-thread blocking inside a
   native `AssetDataProvider` construction. Central acceptance = **orphan disposal** (a
   late-completing construction adds itself to `MasterMixer` — ghost play + leak — and
   disposing it races device teardown); changes a synchronous seam contract. Include the
   consult finding that `_backend.CreatePlayer` is also unguarded against an in-flight
   teardown (`_initLock` covers device calls only).
3. **FOLLOW-UP candidates (one row, lower severity):** the five unbounded in-process
   disk-store waits on UI-reachable paths (census FOLLOW-UP rows: `DtrhHostWindow:228`,
   `DtrhSaveSlots:467,469`, `IntakeHostContext:84,95`, `AssetSelectionDocument:61`,
   `AiMemoryStore:272`).
4. Contract wording finding above (§5 teardown clause) for the docs owner.
5. **Residual PanicReset native wait** (honesty cell 3b): player `Stop()/Dispose()` on the
   close path can block the UI thread unboundedly on a wedged endpoint; belongs with the
   player-lifecycle-vs-device-teardown question of filing 2.

## Honesty cell

1. **Proven:** the UI caller's `Dispose` returns within the budget with a wedged native call
   parked, the backend is disposed exactly once and never concurrently with the in-flight call
   (ordering asserted from the fake's own record), idempotence, and ordinary-teardown parity —
   all against a RECORDING FAKE. **Not proven:** that a REAL wedged native audio call
   (miniaudio inside a dead Windows audio-service RPC) behaves as the fake's parked `TryInit`
   does. The manual gate: a real endpoint death or wedged native call cannot be induced on this
   machine (authoring posture) — never simulated as evidence.
2. **Give-up residue, stated plainly:** after a give-up the old backend stays alive until the
   native call returns; a reopened host runs a SECOND backend alongside it momentarily (proven
   safe to construct by the DTRH two-device precedent, not proven against a real wedged
   device). One `IsBackground` thread per give-up remains until the wedge resolves; if the
   native call NEVER returns, that thread never exits (it cannot block process exit). The old
   arbitration is inert: `_tornDown` refuses every public seam typed.
3. **Verified by execution:** every behavior in the five pins, the bite matrix, the pre-fix
   RED, the 20× repetition, the 3 greens. **Verified by reading only:** the WPF `5a168554`
   remedy-shape parity (its commit was not re-read this packet — the packet's own archaeology
   was adopted), the census thread attributions for sites not exercised here (call-site reads,
   not runs), and the miniaudio two-device coexistence claim (class doc, not a run).
3b. **Residual unbounded wait INSIDE the same `Dispose` (pre-completion consult finding):**
   `PanicReset()` still runs on the caller before the bounded Join, and its `StopDispose` →
   `player.Stop()/Dispose()` touches native miniaudio (MasterMixer removal against the
   device). On a wedged endpoint that path can itself block the UI thread unboundedly — so
   "`Dispose` returns within a bounded budget" is proven **given players dispose promptly**;
   the fakes' players are trivial and no pin can see the real behavior. Deliberately NOT
   fixed here: moving `PanicReset` would break the player-vs-device teardown ordering and
   move the subscriber-visible `WhisperBusyChanged` thread. Adjacent to the player-lifecycle
   census sites 2/3 packet — intended board filing 5 below.
4. **Linux unproven:** zero WSL distros on this machine — no Linux run was produced or faked.
   The change is plain BCL threading (`Thread`, `Join`, `Interlocked` — no Windows-only API),
   so the Linux risk is limited to SoundFlow/miniaudio behavior this packet does not touch.
5. **This closes one member of the class, not the class:** the two `CreatePlayer` sites remain
   unbounded on the UI thread after this packet (separate packet, filing 2 above), and five
   disk-store sites are named follow-ups. The dispatcher can still be blocked by those paths;
   this packet removes the teardown member only.
