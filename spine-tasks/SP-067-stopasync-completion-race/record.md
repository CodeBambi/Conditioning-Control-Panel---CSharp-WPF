# SP-067 record — The StopAsync completion race: a cancelled heartbeat that reports Completed

Worker session: lane-1, started 2026-08-13. Product code base for the RED: `ed2846bd`.

## Step 1 — Reproduce the race and name the mechanism

### Contract lines, read in the file (not the packet's transcription)

`client/docs/async-lifecycle-fault-contract.md`:

- **§2, line 25:** ``- `Cancelled` — the owner's generation was cancelled (teardown or owner stop); the operation observed the token and terminated. Not an error.``
- **§3 rule 4, line 40:** `4. In-flight operations observe the generation token and terminate with the typed `Cancelled` outcome. Cancellation — never the UI thread — is what unblocks in-flight work (§6 rule 3).`

A heartbeat that leaves its `while (!token.IsCancellationRequested)` loop because it observed the token is the literal §2:25 definition of `Cancelled`.

### Bounded-loop probe

`evidence/probe.cs` — a .NET 10 file-based program (`dotnet run probe.cs [N]`, references the
Desktop csproj, no test runner, no `dotnet test`). Each iteration: fresh registry + boundary +
`HeartbeatParticipant` (1 ms interval), `StartAsync`, then `StopAsync` **immediately** (the
zero-tick route, framing e), await the owned `Completion` with a 10 s hard cap per iteration so
a wedge fails loud instead of spinning. Bounded: fixed iteration count, no wall-clock sleeps in
the measurement path. Records the terminal outcome plus the iteration's `TickCount` so the exit
path is identifiable from the tally.

### RED, captured against unmodified product code (`ed2846bd`)

`evidence/red-unmodified.txt` (second of two runs; both saved runs agree):

- Run A (console, not saved): iterations=500, **Completed=495, all zeroTick**; Cancelled=5, all with exactly 1 tick.
- Run B (saved): iterations=500, **Completed=497, all zeroTick**; Cancelled=3, all with exactly 1 tick; other=0, timeouts=0.

At least one iteration — in fact ~99% of iterations — reports `Completed` where the contract
requires `Cancelled`. RED captured.

### The mechanism, named precisely — framing (a) CONFIRMED

`HeartbeatParticipant.TickLoopAsync` (`client/src/CcpClient.Desktop/Lifecycle/Participants.cs:84-108`)
has two exit paths:

1. **OCE path:** cancellation lands while the loop is parked in `await Task.Delay(_interval, token)`
   → `OperationCanceledException` → `AsyncOperationOwner.RunAsync`'s
   `catch (OperationCanceledException) when (token.IsCancellationRequested)` arm
   (`OperationRegistry.cs:222-225`) → `Cancelled`.
2. **Post-loop path:** the `while (!token.IsCancellationRequested)` check observes the cancelled
   token → loop exits **normally** → `return OperationOutcome.Completed.Instance;` (`:108`).
   This return is reachable only when the token is cancelled, so `Completed` there is wrong by
   construction.

The probe tallies prove which exit produced the lying outcome: **every** `Completed` iteration
had `TickCount == 0`. Control fell straight to the post-loop return — the token was cancelled
(before the `Task.Run` body reached the first `while` check) and the loop body never ran, so no
`Task.Delay` was ever awaited and **no OCE could fire**; there was nothing for the RunAsync arm
to map. The `Cancelled` iterations all ticked exactly once: the body won the start race, entered
`Task.Delay` with an already-cancelled token, and the OCE path produced the honest outcome.
Framing (a)'s reading is confirmed on captured evidence; in these runs the defective interleave
that fired was the zero-tick one — the between-`Delay`-and-recheck window never appeared
(zero `Completed` with ticks > 0 in 1000 total iterations).

### Zero-tick determinism (framing e), with the measurement

The zero-tick path reaches the defect with **no timing window inside the loop** — but
"stop immediately" is still scheduling-dependent in the other direction: the `Task.Run` body
may win the start race and take the OCE path instead (observed: 3/500 and 5/500). Measured hit
rate for the defective path on this machine, this probe: **~99%** (992/1000 across both runs).
After the fix both scheduling outcomes return `Cancelled`, so a start→stop-immediately
assertion is deterministic green **whichever** path the scheduler picks — it pins the outcome
rule, not the interleaving (framing d).

### Pre-approach consult (solo)

- Mode: `solo`, asked narrowly (fix shape + test shape + RunAsync/staleness blind spots + biggest risk), reply length capped.
- Verdict (returned complete, not truncated): **fix and test shape are correct, proceed.** Advisor confirmed the OCE arm does not shadow the return; `StopAsync`→`Cancel()` leaves the generation current so `LastOutcome` takes `Cancelled`; assert on `heartbeat.Completion` (observation is never staleness-gated, §3.3), not `LastOutcome`; awaiting the completion task is a deterministic signal — no `TestWait`, no sleep. Biggest risk named: the `AvatarAnimationEngine` site's injected clock — verify `ManualAvatarClock.Delay` observes the token before writing that fact.
- Advisor cautions verified in-repo: `ManualAvatarClock.Delay` returns `waiter.Task.WaitAsync(cancellationToken)` (`AvatarAnimationEngine.cs:46-55`) — the fake clock observes the token, no hang risk; `FloorWrapperGuardTests.cs:50-51` scans only packet-root `PROMPT.md` files, so `evidence/probe.cs` (raw `Task.Delay` inside) trips no guard.
- **ACTUAL answering model:** not identifiable — the consult tool response carried no model identity. Worker session model is `kimi-coding/k3` (`PI_MODEL` env); the advisor route is not observable from inside the worker. Recorded honestly rather than claimed.

## Step 2 — Fix at the source, and sweep the class

### The fix

`client/src/CcpClient.Desktop/Lifecycle/Participants.cs:105-112` — the post-loop
`return OperationOutcome.Completed.Instance;` replaced with the **existing in-repo shape**,
verbatim the `StatusTickerParticipant.cs:150-152` ternary plus its contract-citing comment:

```csharp
// Typed terminal outcome: observing the token at the loop check is Cancelled too —
// identical semantics to the OCE path RunAsync maps (async contract §2).
return token.IsCancellationRequested
    ? OperationOutcome.Cancelled.Instance
    : OperationOutcome.Completed.Instance;
```

The `Completed` branch is dead on this path (the loop can only exit by observing the token)
and is kept anyway for shape parity with the two correct sites, per the decomposition consult
encoded in the packet's amendments.

### Post-fix probe GREEN

`evidence/green-fixed.txt`: iterations=500 (== the RED iteration count), **Completed=none**,
Cancelled=500 (zeroTick=494, ticked=6 — both scheduling outcomes now agree), other=0,
timeouts=0. The lying outcome is gone across the same iteration count that produced a 99%
RED hit rate.

### Sweep of every owned-operation outcome site in `client/src/**` (framing f)

Re-derived counts: exactly **3** `while (!token.IsCancellationRequested)` loops and **0**
`while (true)` loops in `client/src/**` — matches framing (f) exactly. `Task<OperationOutcome>`-
returning **methods**: re-derived **10** (framing f said 12; the authoring figure was a crude
line grep — the raw pattern matches 22 lines including properties, fields, and parameters;
the amendment already flags the magnitude as non-authoritative). Two additional **inline
owned-op bodies** (lambdas passed to `RunAsync`) were found and dispositioned, since framing
(f)'s rule binds every owned operation, not only named methods.

| # | Site | Loop? | Disposition |
|---|------|-------|-------------|
| 1 | `Lifecycle/Participants.cs:84` `HeartbeatParticipant.TickLoopAsync` | yes | **DIVERGENT — FIXED** (the defect; post-loop return now typed per §2) |
| 2 | `Features/StatusTickerParticipant.cs:124` `TickLoopAsync` | yes | CORRECT — ternary at `:150-152` returns `Cancelled` on token-observed exit; OCE path maps via `RunAsync`. Unmodified. |
| 3 | `Features/AvatarTube/AvatarAnimationEngine.cs:376` `LoopAsync` | yes | CORRECT — returns `Cancelled` at all four exits (`:404`, `:410`, `:434`, `:438`); the post-loop return is already `Cancelled`. Unmodified. |
| 4 | `Lifecycle/OperationRegistry.cs:195` `AsyncOperationOwner.RunAsync` | no | CORRECT — the mapping boundary itself: body outcome passes through; OCE-when-cancelled → `Cancelled`; other exceptions → typed `Failed` via the owner classifier. |
| 5 | `Persistence/PersistenceStore.cs:201` `Replace` | no | CORRECT — mutates state, returns `Save()`'s owned completion; no token-observed exit of its own. |
| 6 | `Persistence/PersistenceStore.cs:230` `Save` | no | CORRECT — enqueues via `RunAsync`; the writes-disabled early return is a typed `Failed(Degraded)`, not a token exit. |
| 7 | `Persistence/PersistenceStore.cs:252` `SaveImmediate` | no | CORRECT — awaits `Save()` (quiescence) and passes the typed outcome through. |
| 8 | `Persistence/PersistenceStore.cs:444` `WriteOnce` (sync body of the settings-write op) | no | CORRECT — observes the token **before** I/O and returns `Cancelled` (`:449-452`); `Completed` only after the write+rename actually ran (contract §4 rule 6). |
| 9 | `Ai/AiMemoryStore.cs:284` `SaveImmediate` | no | CORRECT — pure delegate to `PersistenceStore.SaveImmediate`. |
| 10 | `Features/Dtrh/DtrhSaveSlots.cs:414` `SelectSlot` | no | CORRECT — no loop; returns the index store's `Save()` completion. |
| 11 | `Features/Dtrh/DtrhSaveSlots.cs:426` `DescendInto` | no | CORRECT — awaits `SelectSlot`; a non-`Completed` outcome (incl. `Cancelled`) is propagated unchanged (`:430-433`). |
| 12 | `Ai/AiOperationPipeline.cs:299` inline op body (`ai.<class>`) | no | CORRECT — post-await explicit check `!_owner.IsLive(generation) \|\| token.IsCancellationRequested` → `Cancelled` (`:311`); `Completed` only for real produced work. |
| 13 | `Capabilities/CapabilityRegistry.cs:100` inline op body (`probe:<name>`) | no | CORRECT — the token is passed into the probe; OCE maps via `RunAsync`; `Completed` is reached only when the probe genuinely returned; the caller's switch handles `Cancelled` as honest not-probed state. |

Disposition method: sites 1–3 verified by **execution** (the probe, plus the Step-3 zero-tick
facts and the existing suite); sites 4–13 dispositioned by **reading** — they have no
loop-exit-by-token-observation path, so the defect class cannot exist there. This is stated
honestly in the Step-4 honesty cell.

### Framing (g) clearance — what depends on the heartbeat's outcome value

- `HeartbeatParticipant.Completion` readers: **tests only** — `AsyncLifecycleTests.cs:131-132`
  and `:202-203`, both asserting `Cancelled` (the fix makes these deterministic, never weaker).
  Product code never reads it: `MainWindow.axaml.cs:36-38` sets only `TickReporter`;
  `CompositionRoot.cs:172` constructs it. The DTRH/Intake `Heartbeat` grep hits are an
  unrelated protocol message, not the participant.
- `LastOutcome` readers: `AsyncLifecycleTests.cs:38,45` assert `Completed` — on the
  `StaleGenerationCompletion` test whose op body **explicitly returns `Completed`** from an
  un-cancelled generation; untouched by the fix. `:65` asserts `Cancelled`, `:84` a `Failed`
  instance, `AiOperationContractTests.cs:372` asserts `Null`. None tied to the heartbeat.
- `OperationOutcome.Completed` assertions across `client/tests/**`: re-derived **49** —
  matches framing (g)'s authoring figure exactly; none greps against a heartbeat or teardown
  path beyond the two `Cancelled` assertions above.
- `OperationRegistry.CancelAndDrainAsync` awaits completions but never branches on
  `Completed` vs `Cancelled` — the drain is value-agnostic.

**Clearance: nothing depends on the old wrong value. No finding to report.**

### Per-file product diff summary (framing n)

`git diff` over product code touches exactly **one** file:
`client/src/CcpClient.Desktop/Lifecycle/Participants.cs` — 1 line removed (the defective
return), 5 added (comment + ternary). Zero renames, zero refactors, zero unrelated edits.
Every changed line traces to framing (a) (the mechanism), (b) (the reused shape), and (f)
(the single divergent site the sweep found).

## Engine-review presence per step

| Step | Plan review call | Verdict |
|------|------------------|---------|
| 1 | `spine_review_step(step=1, type=plan)` — **absent by design**: nested reviewer spawn blocked inside a pi worker session (SP-195); engine runs reviews after `.DONE`. `skipped=true, spawnFailed=false` | n/a |
| 2 | `spine_review_step(step=2, type=plan)` — **absent by design** (SP-195, same as Step 1). `skipped=true, spawnFailed=false` | n/a |
