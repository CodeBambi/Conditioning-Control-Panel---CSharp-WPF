# SP-123 — record. The three defects the census found, fixed rather than admired.

Branch `lane/SP-123-census-findings`, worktree `.claude/worktrees/agent-a668fce2f4d392846`,
base `145632a1`. Plan checkpoint: `plan.md` (same folder), approved before any product edit.

Floor: pin **2270 unit / 141 headless**; observed **2290 unit / 141 headless**; declared delta
**+20 unit / 0 headless** (`floor-delta.json`). 2270 + 20 = 2290, which is the arithmetic the
gate is expected to show and not a failure.

---

## 1. The containment comparison

Both siblings were read in full before anything was written, and **they agree
character-for-character in every part that constitutes the containment**:

| | `SystemSessionClock` (`Session/SessionClock.cs:46-72`) | `SystemScheduleClock` (`Scheduling/ScheduleClock.cs:68-92`) | `SystemSoundClock` BEFORE (`Audio/AudioSeams.cs:127-138`) |
|---|---|---|---|
| ctor | `(Action<Exception>? onCallbackFault = null)` | identical | **none** — no reporter seam at all |
| null guard | `ArgumentNullException.ThrowIfNull(fire);` | identical | **absent** |
| clamp | `Math.Max(0, (long)due.TotalMilliseconds)` | identical | identical |
| timer | `new Timer(_ => Run(fire), null, ms, Timeout.Infinite)` | identical | `new Timer(_ => fire(), ...)` — **bare** |
| `Run` | `try { fire(); } catch (Exception ex) { onCallbackFault?.Invoke(ex); }` | identical, comment verbatim | **absent** |

The only difference between the two siblings is the *reading* member — `UtcNow` (UTC) versus
`LocalNow` (Local) — which is the whole reason `IScheduleClock` was declared separately rather
than widening `ISessionClock` (`Scheduling/ScheduleClock.cs:5-31`). It is not part of the guard.
So "copy the shape" had exactly one answer and there was no sibling to choose between.

**The comparison produced the packet's invited second defect** (see §5.1): the missing
`ThrowIfNull` is the *other* line both siblings carry, and adding the catch without it would have
made things worse rather than better.

### What changed

`Audio/AudioSeams.cs` — `SystemSoundClock` gains the primary-ctor reporter, the `ThrowIfNull`,
`Run(fire)` and the private `Run`, with its own words for why. The callbacks it actually guards
are `SoundArbitration`'s device-recovery probe (`SoundArbitration.cs:780`), the per-item pacing
fire (`:903`), the five-minute duck watchdog (`:986`), and the DTRH segment-cap timer
(`DtrhNativeEffects.cs:338`).

Reporter wiring at the three default sites (`grep -rn "new SystemSoundClock" client/src` — three,
as the packet said):

1. `Features/Dtrh/DtrhBarkRouting.Composition.cs` (lifted from `DtrhHostWindow.axaml.cs:217`) —
   `_host.LogDiagnostic`, shape copied from `SchedulerParticipant.cs:63-64`.
2. `Features/Dtrh/DtrhNativeEffects.cs:53` — the module's own `_log`.
3. `Companion/BarkPipeline.cs` `BarkPipelineOptions.Clock` — **stays unreported, deliberately**.
   `grep -n "Schedule(" Companion/BarkPipeline.cs` returns nothing: the pipeline only reads
   `_options.Clock.UtcNow` (`CommitFire`, `Pace`). That instance schedules no callback, so there
   is none to fault. The XML doc now says exactly that and nothing more. `BarkPipelineOptions.Clock`
   was **not** widened to nullable to route a log into a callback that does not exist.

`ACallbackThatThrowsWithNoReporter_IsStillContained` exercises that null-reporter configuration.
**It does NOT pin the containment, and an earlier draft of this record claimed it did.** See
§5.4 — the claim was refuted by measurement, and the correction is recorded rather than quietly
dropped.

### Revert → red → restore (fix 1a: the containment)

`return new Timer(_ => Run(fire), ...)` → `new Timer(_ => fire(), ...)`, then
`dotnet test --filter SystemSoundClockTests`:

```
[xUnit.net 00:00:03.31]     [FATAL ERROR] System.InvalidOperationException
[xUnit.net 00:00:03.32] Catastrophic failure: System.InvalidOperationException : a pacing callback faulted
[xUnit.net 00:00:23.34]     [FATAL ERROR] System.InvalidOperationException
[xUnit.net 00:00:23.34] Catastrophic failure: System.InvalidOperationException : no reporter
  Failed CcpClient.Tests.SystemSoundClockTests.ACallbackThatThrows_IsContainedAndREPORTED_... [20 s]
   TIMING-VERDICT:CONDITION-NEVER-TRUE — ... the deterministic signal never completed
Failed!  - Failed: 1, Passed: 6, Skipped: 0, Total: 7
```

Restored; 7/7 green. **That `Passed: 6` hides something and §5.4 is the correction:** one of those
six was `ACallbackThatThrowsWithNoReporter_IsStillContained`, which passes with the containment
gone. Exactly one fact reddens here — the one named in the FAIL line — and the second catastrophic
-failure line above is that no-reporter fact's throw escaping without failing it.

**Stated precisely, because the difference matters:** the process did NOT
die here. xunit.v3's runner has an unhandled-exception net that turns the escaping pool-thread
exception into `[FATAL ERROR] ... Catastrophic failure` instead of a process kill — which is why
this reads as two catastrophic-failure lines plus one FAIL rather than as the vanished host
SP-101 saw. **In the shipped app there is no such net.** The evidence available under test is the
unhandled-exception event itself; that it terminates a process without a net is a runtime
property, not something these facts observe.

### Revert → red → restore (fix 1b: the null-callback fail-fast)

`ArgumentNullException.ThrowIfNull(fire);` removed:

```
  Failed ...SystemSoundClockTests.ANullCallback_IsRefusedOnTheCALLERSThread_NotLaterOnAPoolThread [35 ms]
   Assert.Throws() Failure: No exception was thrown
   Expected: typeof(System.ArgumentNullException)
```

Restored; green.

---

## 2. The bark ruling: **the CONTRACT was true, the CODE was wrong**

Decided on evidence outside the greenfield tree, because the greenfield type had never run and
so could not be its own witness.

**WPF, the behavioural authority** — `ConditioningControlPanel/Services/Companion/BarkService.cs:1411-1413`:

```csharp
private static string? ResolveBarkAudio(string? file)
{
    if (string.IsNullOrWhiteSpace(file)) return null;
```

It takes `string?`, guards null **and whitespace** at the top, and answers null. Every later step
is `File.Exists(p) ? p : null` (`:1420`, `:1428`, `:1432`), and `File.Exists` never throws. So the
outcome in the product being ported is: a null, blank or missing voiceline is silently no-audio,
never an exception. Iron rule 5 says port the outcome, and the outcome backs the contract.

**The greenfield tree agrees from the inside, which is the corroboration that settles it.**
`BarkPipeline.SafeResolve` (`Companion/BarkPipeline.cs:540-551`) wraps the resolver in a
try/catch and logs `"bark: audio resolver faulted (...) — typed resolve-failed"`. The port's only
consumer already treats a resolver throw as a DEFECT. Worse, it reports that defect in the
vocabulary of bad CONTENT — a reader of the log would conclude the manifest was wrong when the
port was. Two consequences:

- the contract is not merely defensible, it is the one the consumer was written against; and
- **the bite could only ever be shown on the resolver directly, never through the pipeline**,
  because `SafeResolve` converts the throw into the same null the fix produces. A
  pipeline-level fact would have been green either way. That is exactly how a defect survives
  in a type with 100% "integration coverage" and zero execution.

**Why not correct the doc instead.** `IBarkAudioResolver` is a public seam over data whose schema
is WPF's. Weakening the promise to "throws on null" pushes a null check out to every present and
future caller of a seam whose entire job is to answer *is there a file*, and abandons the WPF
outcome. Fixing it costs one line in one place.

### What changed (`Companion/BarkPipeline.cs`)

- `Resolve` opens with `if (string.IsNullOrWhiteSpace(audioFileName)) return null;` — WPF
  `:1413` outcome-for-outcome.
- `root` is validated at construction (`root ?? throw new ArgumentNullException(nameof(root))`),
  so `Resolve`'s "never throws" is unconditional rather than conditional on how the object was
  built, and a wiring bug still fails fast on the thread that caused it. Same fail-fast/contain
  split as the clock seams.
- The nullable ANNOTATION on `Resolve(string audioFileName)` was deliberately left alone. Making
  it `string?` would have forced `BarkPipelineTests.RecordingResolver` (`:593`) to move with it or
  the warning gate would go non-zero; a runtime guard answers the real question without the
  question.
- The class summary was rewritten to stop claiming a containment it does not have (below), and
  the interface's `Resolve` doc now names null as the ONLY miss channel.

### Revert → red → restore (fix 2, the null guard)

Guard disabled:

```
  Failed ...DirectoryBarkAudioResolverTests.ANullFileName_IsAMiss_NotAnArgumentNullException [6 ms]
   System.ArgumentNullException : Value cannot be null. (Parameter 'path2')
     at System.IO.Path.Combine(String path1, String path2)
     at CcpClient.Desktop.Companion.DirectoryBarkAudioResolver.Resolve(String audioFileName)
Failed!  - Failed: 1, Passed: 7, Skipped: 0, Total: 8
```

**Exactly one fact bit, and that is the honest result.** `Path.Combine(root, "")` returns `root`
and `File.Exists` on a directory is false, so blank and whitespace answered null even with the
guard gone. Those facts are kept as CONTRACT STATEMENTS — WPF guards them explicitly and a future
rewrite must keep the outcome — and the test comment says in so many words that they are not the
bite. Restored; 8/8.

### Revert → red → restore (fix 2b, the root guard)

`root ?? throw` → `root`:

```
  Failed ...ANullRoot_IsRefusedAtCONSTRUCTION_SoResolveIsUnconditionallySafe [10 ms]
   Assert.Throws() Failure: No exception was thrown
```

Restored; 8/8.

### The divergence I did NOT fix, with its end condition

`Path.Combine(root, absolute)` returns the absolute path, so a manifest naming an absolute file
escapes the sounds root entirely, and `..` traverses out of it. **WPF has the identical property**
at `BarkService.cs:1419` (same `Path.Combine`, same manifest-supplied `file`), and the port's only
manifest today is compiled in (`DefaultBarkRules.ManifestJson`), so there is no untrusted input
and no consumer for a containment check. Inventing one would be behaviour the ported product does
not have. So the code stands and the **doc moved to the truth**: the class summary now names the
property, cites WPF, and carries an **END CONDITION** — *the first time the port loads a user- or
mod-supplied bark manifest, this becomes a live decision and must be re-taken rather than
inherited.* A deferral with a trigger is a decision; one without rots.

The fact that pins it (`AnABSOLUTEFileName_DISCARDSTheRoot_...`) is written **OS-neutrally**: the
escaping name is a real absolute path the platform produces (`Path.GetFullPath` over a second temp
directory, the shape `Ai/AiCommandEnvelope.cs:485` already uses in product code), asserted with
`Path.IsPathFullyQualified`. A hardcoded `"C:\\x.mp3"` would pass on Windows and fail on Linux,
where it is neither rooted nor separator-bearing, and `floor.json`'s `allowedSkips` is closed to
this packet and is not a rescue for that anyway.

**And it is ABSOLUTE, not merely `Path.IsPathRooted`.** `Lifecycle/CompositionRoot.cs:189` already
records that `IsPathRooted` *"wrongly accepts"* drive-relative values like `C:foo` — rooted by that
predicate, yet NOT root-discarding the way `C:\foo` is. Phrasing the fact as "rooted implies the
root is discarded" would have pinned a subtly different and false property. The next reader of the
divergence learns this from the test comment, which is where it will be needed.

---

## 3. Driving the DTRH composition block

`DtrhHostWindow.axaml.cs:213-245` was the sole construction site of four zero-executed types
inside an 833-line `Window` that needs a real `ApplicationHost` carrying a `DtrhParticipant` and a
`DtrhSaveSlots`, an Avalonia `InitializeComponent()`, and an `Opened` handler that boots a real
audio device. The window is not drivable and this packet did not pretend otherwise.

**The lift.** The composition moved, verbatim, into `Features/Dtrh/DtrhBarkRouting.Composition.cs`
— `CreateArbitration(backend, log)` and `CreatePipeline(arbitration, store, dataDirectory, log)`.
Same objects, same arguments, same order. `InitBarkPipeline` keeps everything else exactly where
it was: the `SoundFlowAudioBackend`, `Initialize(null)` and its log line, the persistence store
with its `LogSinkAdapter`, the `BarkSurfaced` subscription, both diagnostics, the m2-test early
return and the whole teardown.

**Why it is a PARTIAL of the existing `DtrhBarkRouting` and not a new type — measured, not
preferred.** It was first written as a standalone `DtrhBarkComposition` and the floor caught it:

```
CcpClient.Tests.ExecutionCensusTests.Census_DenominatorIsAnchoredToTheShippedAssembly
  Assert.Equal() Failure: Values differ
  Expected: 885
  Actual:   884
```

`census.mjs` publishes the assembly's authored-type count and that guard recomputes it by
reflection, requiring exact agreement. **Any new shipped type reds the floor until the census is
regenerated — and both `execution-census.md` and `ExecutionCensusTests.cs` are closed to this
packet.** Hosting the composition on an existing, already-driven type in the same namespace adds
no TypeDef (a partial class is one type; the census unions its coverage by FQN, as its own clause
table states), so the denominator is untouched. It is also the right home semantically:
`DtrhBarkRouting` already IS the DTRH-bark boundary — one half names the events that become
barks, the other names the machine that speaks them.

**One thing I removed after adding it.** The first lift also hoisted a `CompanionStateFile`
constant. It is read only by the window, which builds the store, so no fact can drive it — a
constant only the untestable side reads pins nothing. It went back to a literal at its single use
site, and the composition file says why.

**Rows I expect to move, and I did NOT check by regenerating.** `client/tools/**` and
`client/docs/execution-census.md` are closed to me and the regeneration at land is the
orchestrator's. Against the list at `execution-census.md:196-232` I expect **four of the 42** to
leave — `Audio.SystemSoundClock` (5 lines), `Audio.UnavailableDuckSink` (5),
`Companion.DirectoryBarkAudioResolver` (5), `Companion.BarkOutcome.NoRule` (1) — giving **38**.
The universe should be unchanged at 649 (no type added, none removed).

**The one I expect NOT to move, said plainly.** `DtrhHostWindow.LogSinkAdapter` (2 lines) is a
private nested adapter only a window holding an `ApplicationHost` constructs. Both routes to it
were refused: making it `public` widens a Window's API surface for a census number, and moving it
into the composition would delete the row by RENAMING rather than by driving it, which is Trap 1
in different clothes. It stays zero. `DtrhHostWindow` itself (833) stays zero; the lift removed
about ten lines from it and it remains the census's largest dead surface.

### Revert → red → restore (fix 3), and the defect it found in my own fact

Fix 3 closes no product defect, so its bite is named explicitly: mutate the wiring constant
`CompanionAudioFolder` from `"companion_audio"` to `"companionaudio"` and the composition fact
must go red. First attempt:

```
Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5
```

**Green — the fact was vacuous.** The harness placed the voice asset via
`DtrhBarkComposition.CompanionAudioFolder`, so both sides of the assertion moved with the constant
and the fact was only checking that a string equals itself. This is the same class of defect the
packet is about, in the instrument rather than the product, and it would have shipped as a pin
that pins nothing. The harness now writes the asset under the **literal** `"companion_audio"`, and
the test comment records why. Re-run with the constant still mutated:

```
  Failed ...DtrhBarkCompositionTests.AVoiceAssetPresentUnderCompanionAudio_ResolvesThroughTheRealWiring
   Assert.IsType() Failure: Value is not the exact type
   Expected: typeof(...BarkOutcome+Surfaced)
   Actual:   typeof(...BarkOutcome+SurfacedTextOnly)
Failed!  - Failed: 1, Passed: 4, Skipped: 0, Total: 5
```

Restored; 5/5. The red is also the exact production symptom: a typo in that constant does not
crash, it silently turns every bark text-only with a reason indistinguishable from "no voice
assets shipped yet", which is the port's real current state.

---

## 4. Facts added (+20 unit, 0 headless)

| file | count | what it drives |
|---|---|---|
| `client/tests/CcpClient.Tests/SystemSoundClockTests.cs` | 7 | the real clock: fault contained+reported, contained without a reporter, null-callback fail-fast, immediate fire, negative clamp, dispose-suppresses (barrier), `UtcNow` is UTC |
| `client/tests/CcpClient.Tests/DirectoryBarkAudioResolverTests.cs` | 8 | the real resolver: hit, miss, null→miss, blank/whitespace→miss, directory→miss, absent root→miss, null root refused at construction, absolute-name root discard (OS-neutral) |
| `client/tests/CcpClient.Tests/DtrhBarkCompositionTests.cs` | 5 | the real composition: voice asset under `companion_audio` resolves; absent asset → typed `AudioResolveFailed`; duck refused typed via `UnavailableDuckSink`; unknown trigger → `NoRule`; manifest parsed (8 rules) + null wiring refused at the site |

No wall-clock wait anywhere: every positive clock fact waits on a `TaskCompletionSource` through
`TestWait.Until`, and the negative observation uses the siblings' ordering barrier. No new entry
in `TestTimingGuardTests`' pin list was needed, and none was added.

---

## 5. Second defects found while driving

### 5.1 `SystemSoundClock.Schedule` had no `ArgumentNullException.ThrowIfNull(fire)` — FIXED

Found by the containment comparison, before a test existed. Both siblings carry it. Before the
packet, `Schedule(due, null!)` returned a live timer and NREd on a pool thread — the same process
kill, deferred, with a stack pointing at a timer rather than at the caller. **Adding the catch
alone would have made it worse**: the null would have become a contained, reported NRE, i.e. a
wiring bug demoted to a log line. Both lines were copied. In scope (`Audio/**`), fixed, pinned,
revert-red shown in §1.

### 5.2 My own composition fact was vacuous — FIXED

§3. Constant-versus-constant, caught only because the packet requires the bite to be demonstrated
rather than asserted. Recorded here rather than quietly repaired, because "the fact was green for
the wrong reason" is the same failure the census exists to surface.

### 5.3 A new shipped type reds the floor via the anchored census denominator — WORKED AROUND, and worth filing

`ExecutionCensusTests.Census_DenominatorIsAnchoredToTheShippedAssembly` compares the assembly's
authored-type count by reflection against the number published in `execution-census.md`. It is a
correct and valuable guard — a silently shrinking universe would make the zero count look better
while measuring less — but as written it means **any packet that adds one shipped type cannot pass
the floor**, since the only remedy is regenerating a document that packets are (rightly) forbidden
to touch. This packet dodged it by hosting the lift on an existing type, which happened to be the
better design anyway. That will not always be true.

Not filed on the board (`client/docs/task-board.md` is a shared chokepoint and closed here), so it
is raised in this record and in the lane report for the orchestrator to file or discharge. Suggested
shape: either the census guard reads its denominator from a regenerable side-file the land step
owns, or packet authoring treats "adds a shipped type" as requiring a census regeneration in the
same wave.

---

### 5.4 A claim in this record was refuted by measurement — CORRECTED, and the fact kept

`ACallbackThatThrowsWithNoReporter_IsStillContained` does **not** pin the containment. Measured
directly, with `return new Timer(_ => Run(fire), ...)` reverted to `_ => fire()` and the single
fact run in isolation:

```
[xUnit.net 00:00:03.35]     [FATAL ERROR] System.InvalidOperationException
[xUnit.net 00:00:03.36] Catastrophic failure: System.InvalidOperationException : no reporter
Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1
```

Its only assertion is that a SECOND, unrelated schedule fired, which is true whether or not the
first throw was contained. The escaping exception appears only out-of-band, as the runner's
catastrophic-failure line — which does fail the suite's exit code, but not this fact. My §1
revert run reported `Passed: 6` and that 6 silently included this one, which is exactly how the
false claim survived my own review of it.

**What was wrong is the claim, not the fact.** The fact is kept and not inflated: it exercises a
real product configuration (`BarkPipelineOptions.Clock` supplies no reporter), and the containment
is pinned by `ACallbackThatThrows_IsContainedAndREPORTED`, which does redden. The test's own
comment now states this limit at the site — leaving a false claim in the code while correcting it
only in the record would invert this packet's own rule about doc comments that lie.

**Provenance, because it is not mine.** The shape is copied verbatim from the already-landed
`client/tests/CcpClient.Tests/SystemScheduleClockTests.cs:74`, so the same limit applies there. I
did not edit the sibling files — they are outside this packet's scope — and the orchestrator is
filing them as a board row at the land.

### 5.5 An inherited fact shape asserts a trivially-true condition — NOT fixed, filed

`DisposingTheHandleBeforeItIsDue_SuppressesTheCallback` (`SystemSoundClockTests.cs`) arms the
doomed schedule ten minutes out and then asserts the flag is still false. It would be false
whether or not `Dispose` suppressed anything, so the barrier proves the clock serviced later work
but the assertion does not isolate suppression. The same shape is inherited verbatim from
`SystemSessionClockTests.cs:53` and `SystemScheduleClockTests.cs:122`, so this is a question about
all three files and not about this one.

Not fixed here, deliberately: a real fix means changing the fact's design across three files, two
of which are outside scope, and this packet was told not to touch the fact set. Raised for the
orchestrator to file. Naming it matters more than the fact does — it is the same failure mode as
§5.2 and §5.4 (green for a reason other than the one claimed), now observed three times in one
packet, which suggests the class is worth a guard rather than three more careful readers.

## 6. Divergences

`client/docs/wpf-surface-reachability.md` was NOT edited: nothing here changed which WPF surface is
reachable. The one behavioural divergence this packet touched — the resolver's absolute-name root
discard — is a WPF **match**, not a divergence, and is documented at the code with its end
condition (§2).

---

## 7. What this work does NOT prove

- **Nothing here is headed evidence.** No window was opened, no frame composited, no pixel
  captured. `DtrhHostWindow` was not constructed or run by any fact in this packet, and it remains
  the census's largest zero-execution surface.
- **The real audio path is still unexercised.** Every fact uses a fake `IAudioBackend`. No
  SoundFlow engine, no device, no mixer, no actual sound. `SoundFlowAudioBackend` and
  `SoundFlowDtrhAudio` are untouched by this work.
- **The process-kill claim is inherited, not measured here.** §1 shows the escaping exception
  reaching xunit's unhandled-exception net as a catastrophic failure. That a net-less process
  terminates is a runtime property and an SP-101 observation, not something these facts observe.
- **No Linux leg ran.** The OS-neutral phrasing of the rooted-path fact is reasoned from
  `Path` semantics and the in-tree `AiCommandEnvelope.cs:485` idiom; it has not been executed on
  Linux in this packet.
- **The census row movement is an expectation, not a measurement.** The census was not
  regenerated; that is the orchestrator's at the land.
- **Two of the twenty facts assert less than their names suggest, and both are named above**
  (§5.4 no-reporter, §5.5 dispose-suppression). Both shapes are inherited verbatim from the two
  already-landed sibling clock files; neither was deleted, and neither was inflated to look
  better. Eighteen facts pin what their names say; those two pin a configuration and an ordering
  respectively, and the properties their names imply are pinned elsewhere (§5.4) or not at all
  (§5.5).
- The DTRH host's persistence, meta engine, watchdog, exit flow and native effects are all
  untouched and unproven by anything here.
