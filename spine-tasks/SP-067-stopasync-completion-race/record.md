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

## Engine-review presence per step

| Step | Plan review call | Verdict |
|------|------------------|---------|
| 1 | (pending at commit time) | — |
