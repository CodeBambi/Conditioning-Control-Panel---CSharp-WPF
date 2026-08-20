# SP-123 — plan checkpoint (Step 1). Review Level 3: nothing under `client/src/**` changed yet.

Branch `lane/SP-123-census-findings`, worktree
`.claude/worktrees/agent-a668fce2f4d392846`, base `145632a1`.

Read before writing this: `Audio/AudioSeams.cs:113-137`, `Scheduling/ScheduleClock.cs:1-93`,
`Session/SessionClock.cs:1-70`, `Companion/BarkPipeline.cs:60-135,160-330,478-497`,
`Companion/DefaultBarkRules.cs`, `Features/Dtrh/DtrhHostWindow.axaml.cs:1-270`,
`Features/Dtrh/DtrhNativeEffects.cs:20-60`, `Audio/SoundArbitration.cs:650-700,770-990`,
`tests/SystemSessionClockTests.cs`, `tests/SystemScheduleClockTests.cs`,
`tests/BarkPipelineTests.cs:540-700`, `docs/execution-census.md:150-280`, and the WPF
resolver `ConditioningControlPanel/Services/Companion/BarkService.cs:1407-1432`.

---

## 1. The containment shape, and whether the two siblings agree

**They agree, exactly — character for character in the part that matters.**

| | `SystemSessionClock` (`Session/SessionClock.cs:45-70`) | `SystemScheduleClock` (`Scheduling/ScheduleClock.cs:68-92`) | `SystemSoundClock` (`Audio/AudioSeams.cs:127-137`) |
|---|---|---|---|
| ctor | `(Action<Exception>? onCallbackFault = null)` | identical | **none** (no reporter seam at all) |
| `Schedule` null-guard | `ArgumentNullException.ThrowIfNull(fire);` | identical | **absent** |
| clamp | `Math.Max(0, (long)due.TotalMilliseconds)` | identical | identical |
| timer | `new Timer(_ => Run(fire), null, ms, Timeout.Infinite)` | identical | `new Timer(_ => fire(), ...)` — **bare** |
| `Run` | `try { fire(); } catch (Exception ex) { onCallbackFault?.Invoke(ex); }` | identical, same comment verbatim | **absent** |

The only divergence between the two siblings is the *reading* member — `UtcNow`
(DateTimeOffset, UTC) versus `LocalNow` (DateTime, Local) — and that divergence is the entire
reason `IScheduleClock` was declared separately rather than widening `ISessionClock`
(`Scheduling/ScheduleClock.cs:5-31`). It is not part of the containment shape. So there is no
"which sibling do I copy" question: I copy the one shape both hold.

**A finding that falls out of the comparison, and it is not the guard.** `SystemSoundClock`
is missing the siblings' `ArgumentNullException.ThrowIfNull(fire)` as well. Today
`Schedule(due, null!)` returns a live timer and then NREs **on the pool thread** — the same
process kill, deferred, with a stack pointing at a timer instead of at the caller who passed
null. Adding only the try/catch would convert that into a *contained, reported* NRE, which is
strictly worse diagnostics than the siblings' fail-fast on the caller's thread. So I copy
both lines, not one.

### What I will change (Fix #1)

`Audio/AudioSeams.cs`: `SystemSoundClock` gains `(Action<Exception>? onCallbackFault = null)`,
`ArgumentNullException.ThrowIfNull(fire)`, `new Timer(_ => Run(fire), ...)` and the private
`Run` — the sibling shape, with its own words for *why* (the callbacks it really guards are
`SoundArbitration.RunRecoveryProbe` `:780`, `OnPacingFire` `:903`, the duck watchdog `:986`,
and `DtrhNativeEffects` video-cap `:332`; every one of those runs while the user is doing
something else).

### Where the reporter gets wired at the three default sites

`grep -rn "new SystemSoundClock" client/src` returns exactly three, as the packet says.

1. `Features/Dtrh/DtrhHostWindow.axaml.cs:217` — `_host.LogDiagnostic` **is** in scope; wire it
   (shape copied from `SchedulerParticipant.cs:63-64`).
2. `Features/Dtrh/DtrhNativeEffects.cs:53` — `_log` **is** in scope and already assigned; wire it.
3. `Companion/BarkPipeline.cs:116` — a property initializer on `BarkPipelineOptions`, with **no
   log in scope**. It stays unreported, and this is defensible rather than lazy: `grep -n
   "Schedule(" client/src/.../BarkPipeline.cs` returns **nothing** — the pipeline only ever
   reads `_options.Clock.UtcNow` (`:344`, `:505`). That instance schedules no callback, so there
   is no callback to fault. I will say exactly that in the comment and nothing more, because a
   doc comment that lies is the other half of this packet. I will NOT widen
   `BarkPipelineOptions.Clock` to nullable to route a log into it: that changes a public
   property's type for a callback that does not exist.

Containment does not depend on the reporter — the sibling fact
`ACallbackThatThrowsWithNoReporter_IsStillContained` (`SystemScheduleClockTests.cs:73`) already
pins that, and I will pin it for this clock too.

### The fact, and the revert-red-restore

New `client/tests/CcpClient.Tests/SystemSoundClockTests.cs`, structured on its two siblings so
the three read side by side. Zero wall-clock: every positive fact waits on a
`TaskCompletionSource` through `TestWait.Until`; the negative one (a disposed handle does not
fire) uses the siblings' ordering **barrier**, not an interval.

The bite proof is the one the packet demands and it is real, not rhetorical: reverting
`Run(fire)` to `fire()` makes the throwing-callback fact take the **test host** down rather
than fail (that is what happened in SP-101). I will run the reverted build and capture what
the runner actually prints — a crashed host is red, and I will show it as such.

## 2. The bark ruling: **the CONTRACT is true and the CODE is wrong**

The question is not a formality and it is decided by evidence outside the greenfield tree.

**WPF is the resolver's behavioural authority and WPF guards it explicitly.**
`ConditioningControlPanel/Services/Companion/BarkService.cs:1411-1413`:

```csharp
private static string? ResolveBarkAudio(string? file)
{
    if (string.IsNullOrWhiteSpace(file)) return null;
```

The shipping product takes `string?`, guards null **and whitespace** at the top, and answers
`null`. Every later step is `File.Exists(...) ? p : null` (`:1420`, `:1427`, `:1431`), and
`File.Exists` never throws. So the user-observable outcome in the product being ported is:
*a null, blank or missing voiceline is silently no-audio, never an exception.* The greenfield
one-liner "missing file -> null, never throws" is a faithful statement of that outcome. Iron
rule 5 says port the outcome; the outcome says the contract stands.

**And the code does not implement it.** `Path.Combine(root, audioFileName)`
(`BarkPipeline.cs:75`) throws `ArgumentNullException` on a null filename, and it also throws if
`root` is null. Today nothing reaches it with null — `BarkPipeline.cs:311` checks
`variant.Audio is null` first and `:316` passes `variant.Audio!` — so the promise is kept by a
guard three files away plus a null-forgiving operator, not by the class that makes it. That is
a promise kept by accident, on a type the census proves has **never executed**.

**Why I am not "correcting the doc to match the code" instead.** `IBarkAudioResolver` is a
public seam over data (a bark manifest is content, and the manifest schema is WPF's). Weakening
the promise to "throws on null" pushes a null check out to every present and future caller of a
seam whose entire job is to answer *is there a file*, and it walks away from the WPF outcome.
Fixing it costs one line in one place.

**The fix** (`Companion/BarkPipeline.cs`, `DirectoryBarkAudioResolver`):
- `if (string.IsNullOrWhiteSpace(audioFileName)) return null;` — WPF `:1413` verbatim in
  outcome, so blank is a miss rather than a lookup of the directory itself;
- `root` validated at construction (`?? throw new ArgumentNullException`) so the never-throws
  promise on `Resolve` is unconditional and a wiring bug still fails fast where it is caused,
  on the constructing thread — the same fail-fast/contain split the clocks use.

**One thing I will NOT change, and will document instead.** `Path.Combine(root, "C:\\x.mp3")`
returns `C:\x.mp3` — a rooted filename discards the root entirely, and `..\..` traverses out of
it. WPF has the identical property at `BarkService.cs:1419` (same `Path.Combine`, same
untrusted-ish `file`). Adding containment would invent behaviour the ported product does not
have. So I correct the class summary's "over one sounds root" wording to state what it actually
does, cite WPF, and report it as a finding. That is the doc moving to the truth, not the
promise being weakened: "never throws" survives intact and is now enforced.

**Pinned by** new facts in `client/tests/CcpClient.Tests/` driving the **real**
`DirectoryBarkAudioResolver` over a temp directory: hit returns the combined path, miss returns
null, null/blank/whitespace return null instead of throwing, a directory (not a file) is a
miss, and the rooted-path property is pinned as a *named* behaviour so a later change to it is
a decision rather than an accident. Revert-red: dropping the guard makes the null fact throw
`ArgumentNullException`.

## 3. Driving the DTRH composition block without changing what it constructs

`DtrhHostWindow.axaml.cs:213-245` is the sole construction site of exactly four zero-executed
types — `SystemSoundClock`, `UnavailableDuckSink`, `DirectoryBarkAudioResolver` and
`DtrhHostWindow.LogSinkAdapter` — inside an 833-line `Window` that needs a real
`ApplicationHost` carrying a `DtrhParticipant` and a `DtrhSaveSlots`, an Avalonia
`InitializeComponent()`, and an `Opened` handler that boots a real SoundFlow device. The window
is not drivable and I will not pretend otherwise.

**Approach: lift the composition, verbatim, into a type with no window in it.** New
`client/src/CcpClient.Desktop/Features/Dtrh/DtrhBarkComposition.cs`, two static methods that
construct *the same objects, with the same arguments, in the same order*:

- `CreateArbitration(IAudioBackend backend, Action<string> log)` -> `new SoundArbitration(backend,
  new UnavailableDuckSink(), new SystemSoundClock(...), new SoundArbitrationOptions(), log)`
- `CreatePipeline(SoundArbitration, PersistenceStore<CompanionStateDocument>, string dataDirectory,
  Action<string> log)` -> `new BarkPipeline(arb, store, new DirectoryBarkAudioResolver(
  Path.Combine(dataDirectory, CompanionAudioFolder)), BarkRuleLoader.Parse(
  DefaultBarkRules.ManifestJson, log), new BarkPipelineOptions(), log)`

`InitBarkPipeline` then calls those two and keeps everything else exactly where it is — the
`SoundFlowAudioBackend`, the `Initialize(null)` call and its log line, the store construction
with its `LogSinkAdapter`, the `BarkSurfaced` handler, the two diagnostic lines, the m2-test
early return and `TeardownBarkPipeline`. Nothing constructed changes, nothing is added or
dropped, no order moves. It is a refactor with no behaviour in it, which is the only kind of
lift admissible here.

**The fact then drives the real composition**, in `client/tests/CcpClient.Tests/`, with a fake
`IAudioBackend` and a temp data directory:

1. compose exactly as the window does; raise `AttentionCheckFail` (the one rule with a single
   audio variant, `cooldown_ms: 0`, and global-min-gap exempt, so it is deterministic under the
   composition's default `Random.Shared` RNG) with `<tmp>/companion_audio` empty ->
   `SurfacedTextOnly(AudioResolveFailed)`. Drives the **real** resolver's miss path through the
   **real** wiring;
2. drop `attention_check_fail_1.mp3` into `<tmp>/companion_audio` and raise again -> `Surfaced`
   with `AudioPath` equal to that exact file. This is the fact that pins the folder name
   `companion_audio` — today nothing does, and a typo there is silent;
3. `AcquireDuck(0.5f)` on the composed arbitration -> `Held == false` with the typed
   unavailable reason. Drives `UnavailableDuckSink.TryApply` through `SoundArbitration:665-670`;
4. an unknown trigger -> `BarkOutcome.NoRule` (a fifth zero row, and a real product answer);
5. the composition's default `BarkPipelineOptions` clock is a real `SystemSoundClock`, so
   `CommitFire`'s `_options.Clock.UtcNow` (`:344`) executes it for real.

**Rows I expect to move, stated as an expectation because I am not allowed to check.**
`client/tools/**` and `client/docs/execution-census.md` are closed to me and I will **not**
regenerate the census; the regeneration is the orchestrator's at the land. On the row list at
`execution-census.md:196-232` I expect **four** of the 42 to leave:
`Audio.SystemSoundClock` (5), `Audio.UnavailableDuckSink` (5),
`Companion.DirectoryBarkAudioResolver` (5), `Companion.BarkOutcome.NoRule` (1) — 42 -> **38**.

**And the one I expect NOT to move, said plainly.** `DtrhHostWindow.LogSinkAdapter` (2 lines)
is a `private` nested adapter that only a window holding an `ApplicationHost` constructs. I can
reach it two ways and I refuse both: making it `public` widens a Window's API surface for no
reason but a census number, and moving it into the new composition type would delete the row by
renaming rather than by driving it — which is the packet's first trap wearing different
clothes. It stays zero and I will report it as zero. `DtrhHostWindow` itself (833) stays zero
too; the lift removes ~10 lines from it and it remains the census's largest dead surface.

The new `DtrhBarkComposition` will be a driven type, so the universe grows by one (649 -> 650)
while the zero count falls.

## 4. Second-defect watch

The packet says to expect one, and two candidates are already visible before a line is written:
the missing `ThrowIfNull` on `SystemSoundClock.Schedule` (§1) and the rooted-filename root
escape (§2). Both are recorded above with their rulings. I will report whatever else driving
these turns up, and file rather than fix anything landing outside File Scope.

## 5. Files I intend to touch

| file | why |
|---|---|
| `client/src/CcpClient.Desktop/Audio/AudioSeams.cs` | Fix #1 — the containment shape + reporter seam |
| `client/src/CcpClient.Desktop/Companion/BarkPipeline.cs` | Fix #2 — resolver guard + honest summary |
| `client/src/CcpClient.Desktop/Features/Dtrh/DtrhNativeEffects.cs` | wire the reporter at `:53` |
| `client/src/CcpClient.Desktop/Features/Dtrh/DtrhHostWindow.axaml.cs` | wire the reporter at `:217`; call the lifted composition |
| `client/src/CcpClient.Desktop/Features/Dtrh/DtrhBarkComposition.cs` (new) | the lifted, drivable composition |
| `client/tests/CcpClient.Tests/SystemSoundClockTests.cs` (new) | Fix #1's facts |
| `client/tests/CcpClient.Tests/DirectoryBarkAudioResolverTests.cs` (new) | Fix #2's facts |
| `client/tests/CcpClient.Tests/DtrhBarkCompositionTests.cs` (new) | Fix #3's fact |
| `spine-tasks/SP-123-census-findings/{plan,record}.md`, `floor-delta.json` | packet artifacts |

Nothing under `client/src/CcpClient.Desktop/Views/**`, `client/tools/**`,
`client/tests/floor/**`, `client/docs/execution-census.md` or `client/docs/task-board.md`.
No test count is declared yet; `floor-delta.json` is written when the facts exist and are
counted, not before.

**STOPPED HERE for the Level-3 plan review. No product file has been modified.**
