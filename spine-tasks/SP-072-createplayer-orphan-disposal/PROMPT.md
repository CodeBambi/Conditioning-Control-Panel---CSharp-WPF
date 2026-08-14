# Task: SP-072 — An abandoned player construction must never reach the mixer

## Mission

SP-071 took the backend **teardown** off the UI thread. It deliberately left the other two members of the
same class alone, and named why: `SoundFlowAudioBackend.CreatePlayer` (`:108` → `OffSyncContext.Run`,
`AudioSeams.cs:150`) and `SoundFlowDtrhAudio.CreatePlayer` (`:100`) construct a native `AssetDataProvider`
on the calling thread with **no bound**, and SP-070 established that thread can be the UI thread. On a
wedged endpoint the app stops.

**But the blocking is the symptom, not the hard part.** You cannot simply bound these the way SP-071 bounded
teardown, because `CreatePlayer` returns an object the caller needs — and if the caller stops waiting, the
construction keeps going. Read `CreatePlayerCore` in **both** backends and look at the third line:

```
var provider = new AssetDataProvider(_engine!, path);   // the wedge point
var player   = new SoundPlayer(_engine!, Format, provider) { Volume = volume };
_device!.MasterMixer.AddComponent(player);              // <- the abandoned player becomes AUDIBLE
return new SoundFlowPlayer(player, _device);
```

An abandoned construction that completes late **attaches itself to the live mixer**. That is a ghost play
(a sound nobody asked for, after the moment passed) plus a leak, and disposing it races device teardown —
which SP-071 just moved onto a background thread, so that race is now real and concurrent by construction.

**So the required deliverable of this packet is the ORPHAN INVARIANT, not the bound:** a construction whose
caller has stopped waiting must never reach `MasterMixer`, must never play, must be disposed **exactly
once**, and its disposal must be **ordered** against device teardown rather than racing it. Orphan safety is
the precondition, exactly as "the give-up path must never touch `_backend`" was the precondition in SP-071.

**The bound is conditional and Step 1 decides it with evidence, not preference.** Census every caller of
both seams. The port's established idiom for "no player" is already typed — `SoundOutcome.Unavailable` /
`SoundOutcome.Failed` — and at least one caller already disposes a player that arrived too late
(`SoundArbitration.cs:556-570`, *"Lost the race (teardown or overflow between check and create)"* →
`StopDispose(player)`). **Decision rule, pre-authorized both ways:**

- **If every caller can accept a typed no-player outcome**, the bound lands in THIS packet. Orphan safety
  with nothing that ever abandons is a mechanism no path drives — the board already carries enough of those.
- **If any caller structurally cannot** (it needs the object to proceed and has no typed refusal available),
  bound what you can, and name the remainder as the next row with the caller and the reason. Do not invent a
  refusal semantic for a caller whose contract does not have one.

State which branch you took and why, in `record.md`, before you implement it.

**THE CONSTRAINT THAT DECIDES WHERE THE CODE GOES, and it is a hard one.** `SoundFlowAudioBackend` and
`SoundFlowDtrhAudio` have **zero test coverage** — they construct real SoundFlow engines and devices and
cannot be instantiated headless on any machine this port runs on. The only audio facts in the suite drive
`FakeBackend` through the `IAudioBackend` seam. **If you put the orphan mechanism somewhere a headless fact
cannot reach it, you have shipped an unprovable fix** — and this run has closed that exact class twice now
(SP-067: a shared revert falsely verifies pins never exercised; SP-070: a pin whose fixture cannot reach the
mechanism passes with its own guard reverted). Put the mechanism where a fact can bind it, say so, and name
precisely which residual line (the real `AddComponent` call itself) is verified by reading only.

**Row scope guard:** SP-071's *give-up residue accumulation* row stays **OPEN** and is not this packet.
That row counts outstanding backgrounded teardowns; this one is player lifecycle. If your work makes that
row cheaper, say so in `record.md` — do not close it here.

## Dependencies

- **Task:** SP-071 (landed `d1c69617`) — the backgrounded backend teardown this must be ordered against.
- SP-070 (`9e6498b6`) — `_initLock` and the established fact that the calling thread can be the UI thread.
- SP-025 — `OffSyncContext` and the dump-proven dispatcher deadlock that makes off-context construction
  a binding rule, not a preference.
- SP-029 / SP-017 — the arbitration core, the channel ownership it enforces, and the F1 device discipline.

## Context to Read First

- `client/src/CcpClient.Desktop/Audio/SoundFlowAudioBackend.cs` — `CreatePlayer` (`:95-110`),
  `CreatePlayerCore` (`:112-119`, where `AddComponent` is), `Dispose` (`:120-128`: `_device.Stop()` →
  `_device.Dispose()` → `_engine.Dispose()`, **with no lock of any kind**), and the `SoundFlowPlayer` wrapper
- `client/src/CcpClient.Desktop/Features/Dtrh/SoundFlowDtrhAudio.cs` — `CreatePlayer` (`:88-104`) and
  `CreatePlayerCore` (`:106-113`). **Note it does not call `OffSyncContext`** — it inlines the same
  `Task.Run(...).GetAwaiter().GetResult()` logic. Say whether that duplication is worth removing here
- `client/src/CcpClient.Desktop/Audio/AudioSeams.cs` — `IAudioBackend.CreatePlayer`'s doc comment (`:62-68`,
  which already *requires* off-context construction) and `OffSyncContext` (`:135-153`)
- `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs` — the three call sites (`:548` sfx, `:761` via the
  `CreatePlayer` helper at `:747`, `:877` the queued/pacing path), the **existing late-player disposal**
  at `:556-570`, `StopDispose` (`:1067`), and SP-071's backgrounded teardown in `Dispose` (`:1096-1150`)
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhNativeEffects.cs` — the two call sites (`:112`, `:439`) and
  the `IDtrhAudioBackend` seam (`:695-706`). **One of the five call sites constructs inside a lock; find
  which, and state what a wedged construction costs there beyond the calling thread**
- `client/tests/CcpClient.Tests/SoundArbitrationTests.cs` — `Make` (`:19`), `FakeBackend` (`:906`), SP-071's
  `ParkedProbe` helper and its five teardown pins. **The parked-fixture technique is the one to reuse**
- `client/docs/async-lifecycle-fault-contract.md` §5 (post-only UI boundary) **and §5.6** — the teardown
  clause landed at the SP-071 land. **Read-only; if wording is owed, name it in `record.md`**
- `client/tests/floor/floor.json` and `client/tests/floor/check-floor.mjs` — the floor and its `bumpRule`
- `client/docs/port-workflow.md` — §Verification floor and the `CCP_DATA_ROOT` rule at `:204`
- `docs/constitution.md` — standing orders

## File Scope

- `client/src/CcpClient.Desktop/Audio/SoundFlowAudioBackend.cs` — **the site that must change**
- `client/src/CcpClient.Desktop/Features/Dtrh/SoundFlowDtrhAudio.cs`
- `client/src/CcpClient.Desktop/Audio/AudioSeams.cs`
- `client/src/CcpClient.Desktop/Audio/SoundArbitration.cs` — call sites only, if the bound lands
- `client/src/CcpClient.Desktop/Features/Dtrh/DtrhNativeEffects.cs` — call sites + seam only, if the bound lands
- `client/tests/CcpClient.Tests/SoundArbitrationTests.cs`
- `client/tests/floor/floor.json` (count bump only)
- `spine-tasks/SP-072-createplayer-orphan-disposal/**`
- **NOT in scope:** every other path under `client/src/**` — in particular `Lifecycle/**`, `Companion/**`,
  `Ai/**` and the rest of `Features/Dtrh/**` — plus `client/tools/**`, `client/spikes/**`,
  `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node .spine/patches/verify.mjs && dotnet build client/CcpClient.sln -c Debug --nologo && node client/tests/floor/check-floor.mjs` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Audio/SoundFlowAudioBackend.cs` |
| fileScopeMustNotChange | `ConditioningControlPanel/**`, `client/spikes/**`, `client/tools/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**` |
| artifactsMustExist | `spine-tasks/SP-072-createplayer-orphan-disposal/record.md` |

`check-floor.mjs` is `--no-build` by design, so the explicit `dotnet build` ahead of it is **not optional** —
standalone the wrapper measures the last build, not the working tree, and fails closed naming the wrong
cause (SP-065 land finding). `FloorWrapperGuardTests` binds every packet with ID >= SP-065: **never** call
`dotnet test` outside the wrapper.

## Review Level: 2 (Plan and Code)

Call `spine_review_step` after each step. Scored: blast radius 2 (two backend implementations, two seams and
up to five call sites across the arbitration core and the DTRH effects router; the failure mode is audible
to the user and leaks native objects), pattern novelty 2 (the port has no generalized abandoned-construction
pattern — one local instance exists at `SoundArbitration.cs:556-570` and nothing else), security 0,
reversibility 0 → **Level 2**. **T-2 heading format is load-bearing** — record engine-review
presence/absence per call in `record.md`. **Authoring rule (SP-034 defect):
`grep -c "Review Level" PROMPT.md` >= 2 before launch.** Review Level 2 applies to every step below.

## Steps

### Step 1: Census the callers, decide the bound, design the orphan invariant

- [ ] Update STATUS.md before starting work
- [ ] **Prove the hazard before fixing it.** Write a bounded probe (not a committed test) that drives a
      construction which completes *after* its caller stopped waiting, and capture what happens today —
      that the player is added to the mixer and nothing disposes it. Save the observation under `evidence/`.
      **A fix without a captured pre-fix observation is the class SP-067 closed.** If the real backends make
      this unobservable headless (they will), say so explicitly and capture the equivalent at the seam you
      can drive, naming the difference
- [ ] **CENSUS — every caller of both `CreatePlayer` seams, verdict per site.** Cite the lines you found,
      not the ones listed above. Per site: file:line, the thread(s) that can reach it, what it holds while
      it waits (a lock? the effects gate?), whether it already has a typed no-player path, and what a wedged
      construction costs beyond the calling thread. Include the two implementations and both seam contracts
- [ ] **Apply the decision rule and record which branch you took**: bound lands here if every caller can
      accept a typed no-player outcome; otherwise bound what you can and name the remainder as the next row
      with the caller and the reason. **Do not invent a refusal semantic for a caller whose contract has
      none**, and do not bound a caller by making it wait somewhere else
- [ ] **Write the orphan invariant before writing code.** At minimum: an abandoned construction never
      reaches `MasterMixer`; never plays; is disposed **exactly once**; its disposal is **ordered** against
      device teardown (SP-071 made that teardown concurrent — say how your ordering holds when
      `_device`/`_engine` are being disposed at that moment); and the non-abandoned path is observably
      unchanged
- [ ] **Decide WHERE the mechanism lives, on testability grounds, and justify it.** `SoundFlowAudioBackend`
      and `SoundFlowDtrhAudio` have zero coverage and cannot be constructed headless. State which lines your
      facts will bind and which single residual line (the real `AddComponent`) is verified by reading only.
      **If the mechanism cannot be reached by a headless fact, it is in the wrong place** — move it
- [ ] Keep the SP-025 rule intact: **all SoundFlow player/provider construction runs off-sync-context.** If
      you touch `OffSyncContext` or the inline duplicate in `SoundFlowDtrhAudio`, the dispatcher-deadlock
      property must be preserved and re-argued, not assumed
- [ ] **Pre-approach solo consult** (`mode: "solo"` — a bare `consult` hits the council-roster trap, T-7;
      Opus 5 main, Fable 5 fallback). Verdict + **ACTUAL answering model** in `record.md`. **The tool has
      repeatedly returned reasoning-only or mid-sentence-truncated verdicts (board row T-18) — ask narrowly,
      cap the reply length, record exactly what surfaced, and never stitch a verdict out of reasoning.** An
      unstitched non-verdict is a MISSING consult: re-ask it

### Step 2: Implement orphan safety, then the bound your census authorized

- [ ] The abandonment decision is made **before** the player can reach the mixer, not cleaned up afterwards.
      A player that was abandoned is disposed on the constructing thread and never added
- [ ] Exactly one path disposes an abandoned player, exactly once — including when device teardown is
      running concurrently. State the ordering primitive and why it cannot deadlock against SP-071's
      backgrounded `_backend.Dispose()` (**a new lock taken on both the construction and teardown paths is
      the obvious shape and also the obvious deadlock risk — argue the order explicitly**)
- [ ] The non-abandoned path is **observably unchanged**: same object returned, same volume, same mixer
      attachment, same wrapper
- [ ] **Only if Step 1 authorized it:** the caller's wait is bounded and expiry produces the port's existing
      typed no-player outcome. **Never a new "empty player" object, never a null the caller must guess at**
- [ ] **Transition-only logging.** One line when a construction is abandoned. Never a line per call, never a
      file path or any user data. **Nothing new observed, persisted, logged as user data, or transmitted** —
      grep your own diff for new log/diagnostic/persist/network calls and show the result in the record
- [ ] No awaitable UI dispatch, no `SynchronizationContext.Current` capture, no new dispatch primitive
      (contract §5 rules 1-2)
- [ ] Everything SP-071 and SP-070 landed still holds: the teardown handoff, the give-up that never touches
      the backend, exactly-once backend disposal, `Dispose` idempotence, the recovery cooldown and
      single-flight probe, and the play seam still never taking `_initLock`
- [ ] Summarize the `git diff` per product file in the record; confirm no edit outside File Scope

### Step 3: Bind the behavior, one source at a time

- [ ] **The orphan fact:** a construction that completes after its caller stopped waiting is **never added
      to the mixer** and **never plays** — assert it from the fake's own record of what it was asked to do,
      not from the absence of an exception
- [ ] **The exactly-once fact:** that abandoned player is disposed exactly once — never zero (leak), never
      twice
- [ ] **The ordering fact:** abandoned-player disposal does not overlap device teardown. Assert the **order**
      the fake records, the way SP-071's ordering pin does — and make the absence of an event a **failure**,
      not a vacuous pass (SP-071's `Array.IndexOf` sentinel shape is an open board row; do not add a fourth
      instance of it)
- [ ] **Negative control — ordinary construction is unchanged:** no abandonment → the player is added once,
      plays, and no abandonment line is logged
- [ ] **Only if the bound landed:** the caller's typed no-player outcome is the existing vocabulary, and the
      caller behaves as it already does for that outcome (proven, not asserted by reading)
- [ ] Every landed SoundArbitration and DTRH-effects fact stays green and **unchanged in meaning** — prove
      it with a per-file `git diff` summary
- [ ] **BITE TEST, one source at a time:** revert the abandonment check alone → the orphan pin reds; revert
      the single-dispose latch alone → only the exactly-once pin; revert the ordering guard alone → only the
      ordering pin, **at its ordering assertion**. Capture each RED under `evidence/` naming the reverted
      line and confirming the others stayed green. **A shared revert is not acceptable evidence** (SP-067),
      and **check that each pin's fixture actually reaches the mechanism** (SP-070)
- [ ] No wall-clock waits, no `Thread.Sleep`, no `Task.Delay` in the committed facts. Cross-thread rendezvous
      uses explicit synchronization primitives, not timing. Add no waits outside `TestWait`
- [ ] Bump `floor.json` `total` in the **same commit** as the new facts, reason in the message.
      `allowedSkips`, `admissionRule`, `skipSemantics` untouched

### Step 4: Record + pre-completion consult

- [ ] `record.md`: the captured pre-fix observation; the **full census table** with a verdict per site; which
      decision-rule branch you took and why; the orphan invariant and the design that satisfies it; the
      testability argument for where the mechanism lives, with the residual read-only line named; the
      deadlock-order argument against SP-071's backgrounded teardown; the **bite matrix**; the floor bump
      with its reason; the run table with exact counts and skipped names; consults + **ACTUAL answering
      models**; engine-review presence per step; intended board filings (state them, set no row state)
- [ ] **Honesty cell — required.** At minimum: (1) what is proven is the abandonment/disposal/ordering logic
      against a fake — **not** that a real wedged `AssetDataProvider` construction behaves as the fake does,
      and **not** that the real `MasterMixer.AddComponent` line is exercised at all (name it); (2) whether
      any caller was left unbounded and which; (3) which behavior was verified by execution vs by reading;
      (4) **Linux unproven** (zero WSL distros — do not fake a Linux run); (5) whether SP-071's give-up
      residue row got cheaper or is untouched
- [ ] If the named flake (`ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery`) fired in
      any run, it is recorded by name with run number and TRX path, and was **not** retried away
- [ ] **Pre-completion solo consult**; verdict + actual model in `record.md`
- [ ] STATUS.md accurate before `.DONE`; intended board filings named per ENABLER 2 (set no state)

### Step 5: Testing & Verification

- [ ] Contract testCommand passes **through the wrapper** (`verify.mjs` exit 0, build 0W/0E, new exact unit
      count / 35 headless, skip set exactly the 2 Windows-observed pinned names)
- [ ] **3 consecutive full-suite greens**, >= 1 a **fresh-checkout first-ever build** ("cold" is a NEW
      worktree, not a rebuild in place). Per-run table: run, worktree, cold/warm, unit + headless counts,
      skipped names, TRX path
- [ ] **The cross-thread facts are run repeatedly** (>= 20 iterations of the orphan/ordering class, filtered)
      with zero flakes, and the count is stated — a concurrency pin that passes once proves less than one
      that passes twenty times
- [ ] The bite matrix is complete: each revert named with the pins it reddened and the pins that stayed green
- [ ] `git diff --check` clean
- [ ] `git status --short` shows only File Scope paths
- [ ] `git status --porcelain --ignored=matching -uall` shows **no new ignored artifact** produced by any run

## Completion Criteria

- An abandoned construction never reaches `MasterMixer`, never plays, and is disposed exactly once — each
  proven by its own pin, against a captured pre-fix observation
- Abandoned-player disposal is ordered against device teardown and cannot deadlock with SP-071's
  backgrounded `_backend.Dispose()`, with the order argued explicitly in the record
- The mechanism lives where a headless fact can bind it, and the single residual read-only line is named
- The ordinary construction path is observably unchanged (negative control)
- The census carries a verdict for every caller of both seams, and the decision-rule branch is recorded with
  its reason; any caller left unbounded is named as the next row, not silently dropped
- Every SP-070 / SP-071 / DTRH-effects fact stays green and unchanged in meaning
- The SP-025 off-sync-context rule still holds for every SoundFlow construction path
- Each behavior is bound by its own revert, and each pin's fixture is shown to reach its mechanism
- No new sentinel-comparison assertion shape (the open vacuity row is not to be fed a fourth instance)
- Zero assertions weakened, zero tolerances widened, nothing quarantined, nothing added to `allowedSkips`
- `floor.json` `total` bumped in the same commit as the facts that moved it, reason in the message
- 3 consecutive full-suite greens at the stated exact counts, >= 1 fresh-checkout first-ever build, plus the
  repeated run of the cross-thread facts

## Do NOT

- Bound `CreatePlayer` without orphan safety in place first — a bounded wait whose abandoned construction
  still reaches `MasterMixer` is strictly worse than today's block: it turns a freeze into a ghost sound
  plus a leak. If you find yourself shipping the bound alone, stop and report
- Return a null, a placeholder, or a silent no-op player to a caller — the port's no-player outcomes are
  typed and already exist
- Remove or weaken `OffSyncContext`, or construct a SoundFlow provider/player on a thread carrying a
  `SynchronizationContext` — SP-025 dump-proved that deadlock; it is a binding rule, not a preference
- Change SP-071's teardown handoff, its bounded give-up, or `_initLock`'s semantics; do not attempt the
  give-up residue accumulation row here (it stays OPEN and is a different mechanism)
- Change channel ownership, ducking, the voice queue, pacing, the SFX cap, drop-on-overflow, or panic
  semantics; do not change what the DTRH effects router observes as completion
- Add an awaitable UI dispatch, capture `SynchronizationContext.Current`, or otherwise re-open the §5
  post-only boundary
- Add a `Thread.Sleep`, `Task.Delay`, wall-clock read, or `DateTime.UtcNow` in product or test code; add no
  waits outside `TestWait`; do not write a concurrency test that depends on timing to be deterministic
- Add a sentinel-comparison assertion (`IndexOf(...) < IndexOf(...)` and relatives) whose absent case passes
  vacuously — that shape is already an open board row
- Log per call, log a file path or any user data, or add any observation, persisted field, diagnostic, or
  network call
- Weaken, retry, quarantine, or allowlist any test; add anything to `allowedSkips`; touch `admissionRule`,
  `skipSemantics`, or the 5 pinned names
- "Fix" the 2 Windows-observed skips or drive the skip count to 0 — **the asymmetry is correct**
- Call `dotnet test` outside `check-floor.mjs` (`FloorWrapperGuardTests` binds this packet)
- Export `CCP_DATA_ROOT` process-wide for a suite run — it skips the SP-057 pin and blinds the exact-count
  floor (the vacuous-green class SP-062 closed)
- Create any new file under `client/tools/` (`.gitignore:168` — it would pass in-lane and vanish from the
  merged tree)
- Claim a Linux result, a real-device result, or a headed result you did not produce
- Edit `client/docs/**` (including the contract docs — name owed wording in `record.md` instead),
  `docs/constitution.md`, `.spine/**`, `.pi/**`, `AGENTS.md`, `CLAUDE.md`, `.gitnexus/`; set any board row state
- Use `consult` council mode (T-7: solo only)
- Run `git clean -fdX` repo-wide in the lane — it deletes the T-14-staged `.pi/npm` that `verify.mjs` needs;
  clean **scoped** (`git clean -fdX -- client/`) and only after the contract passes
- Commit build output (`bin/`, `obj/`) or any results file

## Git Commit Convention

- `feat(SP-072): complete Step N — <summary>`

**Create `.DONE` as the last action, and do NOT commit it.** The batch engine's lane-commit stages it. A
worker that commits its own `.DONE` leaves nothing for that step to stage, and it then fails closed on the
lane's gitignored `.pi/npm` tooling install — that is exactly how the wave-28 batch was recorded `failed`
after every gate had passed (`lane-commit.mjs:326-368`).

## Documentation Requirements

**Must Update:** `spine-tasks/SP-072-createplayer-orphan-disposal/record.md`, `STATUS.md`
**Check If Affected:** `client/docs/async-lifecycle-fault-contract.md` (**read-only for this packet** — §5.6
is the teardown-bounding clause landed at the SP-071 land; if an abandoned-construction rule belongs beside
it, state the exact wording in `record.md` as a finding for the orchestrator; do not edit it)
**Explicitly NOT updated by the worker:** `client/docs/task-board.md`, `client/docs/port-lessons.md`,
`client/docs/upstream-sync.md`, `docs/constitution.md`, `.spine/**`

## Amendments

- 2026-08-14 (authoring, orchestrator): **wave 29 runs this row ALONE.** The decomposition consult killed
  the two-lane plan on a fact I had wrong: I proposed pairing it with the tooling/vacuity row on "disjoint
  scopes", and **every packet that adds a test bumps `client/tests/floor/floor.json` in the same commit as
  the test** (SP-071's own diffstat carries it), so any lane-mate collides there — green alone, RED at
  merge. That is the SP-057/SP-058 `Program.cs` precedent the board already ruled on. It also noted wave 29
  is the first live test of the unverified `.DONE` template fix, which should not be tested on two lanes at
  once.
- 2026-08-14 (authoring, orchestrator): **decomposition consult (solo, Opus 5) — complete verdict on the
  first call under a 250-word cap** (9th consecutive wave the T-18 cap technique has held; recorded as a
  technique that works, never as evidence the tool is fixed). Verdict: **single lane, scope to the orphan
  invariant first, and do NOT pre-decide that the blocking stays.** Encoded as the Step-1 decision rule with
  both branches pre-authorized, so the worker resolves it on census evidence rather than asking. Its
  reasoning is the packet's Mission: orphan safety is the precondition the way "never touch `_backend`" was
  in SP-071, and orphan safety that nothing ever exercises is another mechanism-no-path-drives note.
- 2026-08-14 (authoring, orchestrator): **the advisor's checkable claims were verified before encoding.**
  Both `CreatePlayerCore` bodies do call `_device.MasterMixer.AddComponent(player)` before returning
  (`SoundFlowAudioBackend.cs:118`, `SoundFlowDtrhAudio.cs:112`); `SoundFlowAudioBackend.Dispose` takes **no
  lock at all**; the port's typed no-player idiom exists and is already used at all three
  `SoundArbitration` call sites (`SoundOutcome.Unavailable` / `Failed` / a `continue` on the pacing path);
  and a late-player disposal precedent already exists at `SoundArbitration.cs:556-570`. **Newly found at
  authoring and left for the worker to re-derive: one of the five call sites constructs inside a lock, and
  two of them have no `try`/`catch` at all** — the packet points at the sites without naming the finding, so
  the census is a real read rather than a transcription.
- 2026-08-14 (authoring, orchestrator): **the testability constraint is stated as a design input, not
  discovered mid-lane.** `SoundFlowAudioBackend` and `SoundFlowDtrhAudio` have zero test coverage (grep:
  the only audio test file is `SoundArbitrationTests.cs`, which drives `FakeBackend` through the seam), and
  they cannot be constructed headless. A fix placed inside them is unprovable; the packet requires the
  mechanism to live where a fact can bind it and the residual read-only line to be named.
- 2026-08-14 (authoring, orchestrator): **Size M**, at the top of the band. The product change is small but
  spans two implementations and up to five call sites, and the weight is evidence: a captured pre-fix
  observation, a per-site census, an orphan pin, an exactly-once pin, an ordering pin that must not be
  vacuous, a negative control, repeated cross-thread runs, and independent bite reverts.
- 2026-08-14 (authoring, orchestrator): **`spine preflight`'s `prelanded-file-scope` warning, if it fires,
  must not be obeyed.** It compares `fileScopeMustChange` against **`main`**, the still-shipping WPF branch
  with no `client/` tree, while the contract verifier uses `baseBranch` from `.spine/spine-config.json`.
  Following its hint would manufacture the contract-passes-on-docs-only class (SP-214/SP-457).
- 2026-08-14 (authoring, orchestrator): machine posture — laptop; **zero WSL distros, so Linux is a standing
  named gate** (do not fake a Linux run); **no real audio device, endpoint death, or wedged native
  construction can be induced here** — the manual gate is named in the honesty cell, never simulated as
  evidence; **MCP not connected this phase (0/3)** — a named limit, never a blocker. No AXAML in this
  packet, so the A-013 advisory step is not a gate. **`## Review Level: 2` heading present + grep-verified
  >= 2 (SP-034 authoring rule).**
- 2026-08-14 (authoring, orchestrator): **worker board-row obligation.** ENABLER 2 keeps `task-board.md` out
  of worker scope, so the row update is **budgeted into the land** by the orchestrator. Name your intended
  filings precisely in `record.md` — that text is what lands.
