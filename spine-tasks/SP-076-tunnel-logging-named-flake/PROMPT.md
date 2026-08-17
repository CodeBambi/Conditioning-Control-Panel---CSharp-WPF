# SP-076 — Name and fix the tunnel route-class logging flake at the source

## Mission

`ChaosTunnelLoopbackTests.Logging_RouteClassesOnly_NeverFilenameOrQuery` went red **1 time in 15 full-suite runs** in the SP-065 lane and has been green in every run since. It guards a privacy boundary: the tunnel loopback origin may log route CLASSES only, never a filename, never a query string. The board row exists because a transient red WITH a name is a class you can fix, and because the tempting fixes (widen, wait, quarantine) are all forbidden here.

Your outcome: **the mechanism is named from executed evidence, fixed in `ChaosTunnelLoopback.cs`, and pinned by a fact that bites under its own revert.** The five assertions in that test stay byte-identical. Not one of them is touched, reordered, softened, or wrapped in a wait.

**Read this before you form a hypothesis, because the originating record will send you the wrong way.** SP-065's record (`.worktrees/land-w30/spine-tasks/SP-065-test-floor-contract-check/record.md:188-190`) transcribes the TRX failure site as `ChaosTunnelLoopbackTests.cs:143` and then annotates it, in the same sentence, as `Assert.DoesNotContain("index.html", logs)`, concluding "the collecting log contained a filename". **The annotation is off by one and the conclusion does not follow.** The test file has exactly one commit in its history (`05fed4dd`, SP-061) and is byte-identical in HEAD, in the working tree, and in the `land-w30` worktree the record was written from. At line 143 there sits, and has always sat:

```
142	        Assert.Contains("/tunnel/", logs, StringComparison.Ordinal);
143	        Assert.Contains("/vendor/", logs, StringComparison.Ordinal);
144	        Assert.DoesNotContain("index.html", logs, StringComparison.Ordinal);
```

The transcribed line number is primary evidence; the annotation beside it is a secondary reading that is checkable and is refuted. So the observed red is most likely an **expected route class MISSING**, not a filename leaking. That inverts the whole investigation, and it points at a defect that is visible in the source by reading (Step 1).

You still verify this yourself. The TRX is gone (the wrapper writes results to a temp dir outside the worktree), so the line number is a transcription nobody can re-open, and both readings stay live until your evidence closes one. Both are pre-authorized below.

## Dependencies

SP-061 (landed) built the server and the test. SP-065 (landed) built the floor wrapper that made the red loud and filed the row. SP-073 (landed) introduced the floor-delta mechanism this packet uses. Nothing else blocks you.

## Context to Read First

Verified by the orchestrator at authoring. Every line below was opened in the PORT tree and confirmed, not transcribed from the board:

**The row**

- `client/docs/task-board.md:99` — the row itself. Note what it does and does not claim: it names a flake and forbids quarantine. It does NOT claim a filename was observed in a log.
- `client/tests/floor/floor.json`, `admissionRule` — this exact test is one of two names permanently banned from `allowedSkips`. Quarantine is closed by policy, in the pin file, in writing.

**The test (unchanged since SP-061)**

- `client/tests/CcpClient.Tests/ChaosTunnelLoopbackTests.cs:20-39` — the fixture. `_root`, `_log`, `_server` are all **instance** fields and xunit builds a fresh instance per fact, so the board's "shared state does not explain it" is correct. There is no intra-class sharing to find.
- `:51-62` — `CollectingLog`. `Log` appends under `_gate`; `All` joins under the same `_gate`. Memory visibility between the server's thread and the test's thread is therefore not the bug.
- `:136-147` — the flaking fact. Two awaited GETs, then a single `_log.All` snapshot, then five assertions. `:142` `/tunnel/`, `:143` `/vendor/`, `:144-146` the three absences.
- `:26-38` — the only requests this fixture's server can ever see come from this fixture's client, on a port bound in this fixture's constructor.

**The server (the whole file is in your scope)**

- `client/src/CcpClient.Desktop/Features/Chaos/ChaosTunnelLoopback.cs:166-173` — the 200 payload path: read the bytes, set the response, `await res.OutputStream.WriteAsync(...)`, and **only then** `_log($"... GET {RouteClass(path)} -> 200")`.
- `:121-126` — `/health`: `await WriteText(...)` and **then** `_log(...)`. Same shape, same file, second occurrence.
- `:210-215` — `Refuse`: `_log(...)` **first**, then the headers, then `WriteText`. The file already does it the other way round on every refusal path. That asymmetry between refusals and successes is the finding.
- `:98-107` — each request is handled on its own `Task.Run`. `:101` swallows `HttpListenerException`, `ObjectDisposedException` and `IOException` as client-went-away. `:105` logs everything else as `{ex.GetType().Name}: {ex.Message}` **into the same privacy-boundary sink**, with the message interpolated raw.
- `:96` — the accept-fault line, which logs the exception TYPE only and no message. Compare with `:105`.
- `:204-208` — `RouteClass`: first segment only. This is correct and it is the privacy primitive. The defect is not what it returns.
- `:56-87` — `Start`: random port in `[49152, 65536)`, fresh listener per attempt. The bound line at `:74` contains no route and no path.
- `client/src/CcpClient.Desktop/Features/Chaos/ChaosTunnelService.cs:179` — the product constructs this server. This is a live user-visible path, not test-only code. That is one of the three reasons this packet is Review Level 3.
- Grep, run at authoring: **zero** consumers of the `chaos-tunnel-loopback:` log strings anywhere outside `ChaosTunnelLoopback.cs`. No test and no product code parses these lines, so their wording is not load-bearing. Re-run it before you rely on it.

**Adjacent and OUT OF SCOPE**

- `client/src/CcpClient.Desktop/Features/Dtrh/LoopbackServer.cs:186` and `:471` — the mirrored DTRH server has the same write-then-log shape on both of its success paths, while its `Refuse` at `:522` logs first. **Do not touch that file.** Its scope belongs to another lane this wave. Report it in `record.md` as an owed board row and stop there.

**The machinery you run inside**

- `client/tests/floor/check-floor.mjs:253-254` — the wrapper runs `dotnet test --no-build`. It measures the last build, not your working tree.
- `client/tests/floor/floor.json` — the shared pin. READ THE PIN FROM THE FILE, never from this packet: it has already gone stale twice (it said 1018; wave 30 made it 1022 and wave 31 made it 1028). Open `client/tests/floor/floor.json` and use what is there.
- `client/tests/floor/sum-deltas.mjs:18-24` — the fixed delta shape.
- `client/tests/CcpClient.Tests/FloorWrapperGuardTests.cs`, `PacketsAtOrAboveSp073_DeclareAFloorDeltaAndNeverOwnTheSharedPin` — mechanically enforces both halves of the floor-delta rule against this PROMPT.
- `client/tests/CcpClient.Tests/TestTimingGuardTests.cs:20-41` — the forbidden-token list is wider than it looks. Besides `Thread.Sleep(` and `Task.Delay(` it bans `Stopwatch`, `SpinWait`, `SpinUntil`, `Environment.TickCount`, `CancelAfter(`, `CancellationTokenSource(TimeSpan`, `.WaitAsync(TimeSpan`, `.Wait(TimeSpan` and `.WaitOne(TimeSpan`. A gated test sink that blocks on `ManualResetEventSlim.Wait(TimeSpan...)` trips this guard. Plan around it before you write the test, not after it reddens.
- `client/tests/CcpClient.Tests/TestWait.cs` — the only approved bounded wait. `Until(Task signal, ...)` is the deterministic-signal overload.
- `client/tests/floor/vacuous-shape-ledger.json` — a shared file another lane may also be editing. Write your new fact with unconditional top-level assertions and no early return, no platform or environment predicate, and no all-nested assertion body, so you owe this file no entry and never touch it.

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Features/Chaos/ChaosTunnelLoopback.cs`, `client/tests/CcpClient.Tests/ChaosTunnelLoopbackTests.cs`, `spine-tasks/SP-076-tunnel-logging-named-flake/**` |
| Must not change | everything else, and specifically the files named in the contract below |

Your scope is exactly those two source files. It was assigned to be disjoint from every other lane in this wave. If your evidence says the fix cannot live inside it, **stop and say so in `record.md` and in your final report**; do not widen it yourself, and do not reach into `LoopbackServer.cs` because the same shape is there.

## Contract

| Field | Value |
|-------|-------|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-076-tunnel-logging-named-flake/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Features/Chaos/ChaosTunnelLoopback.cs`, `client/tests/CcpClient.Tests/ChaosTunnelLoopbackTests.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-076-tunnel-logging-named-flake/record.md`, `spine-tasks/SP-076-tunnel-logging-named-flake/floor-delta.json` |

**You do not edit `client/tests/floor/floor.json`.** It is the shared pin and concurrent lanes collide on it. Declare your count change in your own folder instead:

```json
{ "packet": "SP-076-tunnel-logging-named-flake", "unit": 0, "headless": 0, "reason": "one line naming the facts you added" }
```

Declare `0`/`0` if you add no tests; omitting the file is not the same as declaring zero. The land sums every packet's delta and applies one bump.

## Review Level: 3 (Plan, Code, Final)

Three triggers, any one of which would be enough: the mechanism is a **concurrency** ordering question; the assertion guards a **privacy** boundary and the sink you are editing is that boundary; and `ChaosTunnelService.cs:179` puts this server on a **live user-visible path**, so a wording or ordering change ships to users.

## Testability constraint, stated now rather than in review

This project has spent three cycles discovering at review time that a mechanism sat where no fact could reach it. Here is where this one sits, decided at authoring:

- The defect is inside `private async Task Handle(HttpListenerContext)`. The only observation seam that already exists is the `Action<string> log` the constructor takes at `:49-54`. **That seam is sufficient and you will not add another.** No new interface, no `internal` hoist of `Handle`, no injected response-writer, no test hook on the product type. A seam that exists only so a test can watch is mechanism no path drives, and it is a worse outcome than the flake.
- The fact must therefore travel through a **real `HttpListener` + `HttpClient` loopback round trip**, exactly as the existing class already does. A direct call into `Handle` would erase the scheduling boundary that is the subject.
- **No headed gate is owed and none is dischargeable.** There is no compositing, no geometry, no window. This is a `draw`-free, pixel-free claim: a pure logic fact in `client/tests/CcpClient.Tests` fully discharges it. Do not request a headed capture and do not move the fact into `CcpClient.HeadlessTests`; nothing Avalonia is involved and `[assembly: AvaloniaTestApplication]` is assembly-wide.
- **Needing a wait is the tell.** If your fixed tree still requires a poll or a `TestWait` before the log line is observable, you have not fixed the ordering, you have tolerated it. State that rule to yourself before you write the test.

## Steps

### Step 1: Reproduce with a bounded loop, then close the mechanism against a rule that is pre-authorized both ways

Bound the search before you start it: **at most 200 iterations per construction and at most 3 constructions**, no sleeps, and stop as soon as a construction reproduces. Record the iteration count and the failing assertion's line for every red you get.

The source reading that motivates this packet is: on the 200 paths (`:172-173` and `:123-124`) the route-class line is emitted **after** the body has been written, so a client can finish reading a response before the server-side continuation has appended anything. Under a full suite, xunit runs collections in parallel and the thread pool is saturated, which is when a continuation that normally runs inline gets queued. That fits every recorded symptom: full-suite only, never isolated, and `Assert.Contains("/vendor/", logs)` at `:143` failing on the LAST request while `/tunnel/` at `:142` passed because a whole extra round trip covered it.

**THE DECISION RULE IS PRE-AUTHORIZED BOTH WAYS. Resolve it on your evidence; do not ask.**

- **Branch A, the expected one: the red is a MISSING route class.** You reproduce, or you demonstrate by construction, that a client can observe a response before the corresponding log line exists. Then the fix is the ordering fix in Step 2 and the assertion at `:143` is the one that was flaking.
- **Branch B: the red is a filename PRESENT in the sink.** If your evidence puts a filename into `_log`, then `:105` is your mechanism, because `RouteClass` provably cannot emit one and `:105` is the only line in the file that interpolates unbounded external text into the sink. That is a genuine privacy leak and it outranks Branch A. Fix it first, keep the assertion byte-identical, and say plainly in `record.md` that the ordering defect is also present and what you did about it.

Both branches are real defects in the one file you own. It is legitimate to land both. It is not legitimate to land neither and call the flake unreproducible.

**If a bounded loop does not reproduce at all**, you do NOT close the packet as "not reproducible". The ordering defect is readable in the source with or without a red, and the deliverable becomes the ordering fix plus the deterministic fact from Step 3. Say in `record.md` that the historical red was not re-observed and that the fix is justified by construction rather than by reproduction. That is an honest outcome. "Could not reproduce, closing" is not.

### Step 2: Fix at the source

Under Branch A, the invariant to establish, in the file's own vocabulary:

> Every route-class log line is emitted before any byte of the corresponding response can leave the process.

`Refuse` at `:210-215` already satisfies it. Make the two success paths (`:121-126` and `:166-173`) satisfy it too. After the change the file is internally consistent and the invariant is a property of the server, not a hope about scheduling.

**The honesty objection to that fix is pre-authorized both ways, so do not stall on it.** Logging `-> 200` before the write means the line is emitted at decision time and a later write fault would leave a served-line with no delivery. Rule:

- If you judge the line is an **audit record of what was served**, which is what `Refuse` already treats it as and what a privacy boundary wants (record before you act), leave the wording alone and say so. The write-fault path already logs separately at `:101`/`:105`.
- If you judge `-> 200` becomes a lie, you **may** change the wording of the served line so it is honest about intent rather than delivery, **provided** the route class it carries is still exactly `RouteClass(path)` and nothing else, and every existing assertion still holds byte-identically. The five assertions constrain `/tunnel/`, `/vendor/` and the three absences; they say nothing about `-> 200`, so both wordings are available to you.

Either choice is pre-approved. State which you took and why, in one paragraph, in `record.md`.

### Step 3: The `:105` message-interpolation vector, also pre-authorized both ways

`:105` writes `ex.Message` verbatim into the privacy-boundary sink. Exception messages carry filesystem paths. `:101` filters the `IOException` family, and `FileNotFoundException` and `DirectoryNotFoundException` are inside it, but not every path-carrying exception is: confirm for yourself where `UnauthorizedAccessException` sits in the hierarchy before you rely on it, then decide:

- **If you can name at least one reachable exception type whose `Message` can carry a payload path and which `:101` does not filter**, narrow `:105` to the type-only shape `:96` already uses. This is a deletion of a leak vector, not new mechanism, and the grep above shows nothing consumes the message.
- **If you can show no reachable non-filtered fault can carry a path**, leave `:105` alone and record the enumeration that proves it.

**Do not add a product seam to make this vector drivable from a test.** If you land the narrowing and cannot write a fact that bites on it, say so in exactly those words in `record.md` and name it as an unpinned defensive change. An unpinned honest narrowing on a privacy sink is acceptable here; an unpinned change presented as covered is not.

### Step 4: Bind it with a discriminator that is deterministic in BOTH directions

A regression test that is 90% red on the unfixed tree is 10% green on it, and a fact that can read green with the mechanism reverted is the vacuity class this run has already hit three times (SP-067, SP-070, and the class SP-072 designed out). So the bar is: **the new fact fails on the unfixed tree every single time, and passes on the fixed tree every single time, with no wall-clock wait deciding either outcome.**

One construction that gets you there, which you must validate rather than assume:

1. Serve a response whose body cannot be accepted in one shot, so the server's `WriteAsync` genuinely cannot complete until the client drains it. Write that fixture file **inside the new test method**, not in the shared constructor, so the other nine facts in the class do not pay for it. Keep it single-digit MB and give it an allowlisted extension so it serves 200.
2. Issue the GET with `HttpCompletionOption.ResponseHeadersRead` and **do not drain the body**.
3. Assert, at the moment the response headers are observable, that the route-class line is already in the sink.

On the fixed tree the line is appended before the first write, so it is present when headers arrive: deterministic green. On the unfixed tree headers flush at the first write and the line is only appended after the whole body is accepted, which the undrained client prevents: deterministic red. Find the size empirically. If the size you pick lets the write complete anyway, the discriminator is not deterministic yet and you keep going.

Then unwind cleanly: dispose the response and client so the parked handler unblocks, and never leave the fixture holding a registered listener, because `LoopbackListenerRegistry` fails the whole assembly at teardown on a leak.

**If, after the bounded search in Step 1's budget, no construction is deterministic in both directions**, fall back to a **structural** fact in the test file: assert against `ChaosTunnelLoopback.cs` itself that every response-emitting path logs before it writes, in the shape the repo already uses for source-reading guards (`FloorWrapperGuardTests`, `DataRootChokePointGuardTests`, `HarnessEntryPointGuardTests`, and `MimeAllowlist_IsDerivedFromTheSweptUpstreamTrees` in this very class). It must report `file:line` and it must refuse to skip. Record that you took the fallback and that a lexical guard is weaker than a behavioural one.

**Every new fact is proven to bite by an INDEPENDENT revert of the single source change it guards, one at a time, with the tree restored byte-identically between reverts.** Record the red count per revert. Two changes reverted together prove nothing about either.

### Step 5: Record

`record.md` carries: the bounded-loop table (construction, iterations, reds, failing assertion line); which branch of Step 1's rule your evidence selected and the evidence that selected it; your Step 2 wording decision and its one-paragraph reason; your Step 3 decision and its enumeration; the revert matrix with red counts; the `LoopbackServer.cs:186`/`:471` finding written up as an owed board row you did not act on; and an honesty section naming what is NOT proven, including whether you re-observed the historical red at all.

`floor-delta.json` with your real counts, in the fixed shape.

### Step 6: Verification

```
dotnet build client/CcpClient.sln -c Debug --nologo
```
```
node client/tests/floor/check-floor.mjs
```

Run them as **separate commands**. The worktree isolation guard refuses compound shell commands (`cd X && ...`), so chain nothing.

**Build immediately before the gate, every time.** The wrapper runs `dotnet test --no-build` (`check-floor.mjs:253-254`), so it measures the last build and not your tree. A stale `bin/` has already reported 1022 against a tree containing 1018 in this repo; the failure names the wrong cause and sends the reader hunting a deleted test.

Your floor run will report a total that does NOT match the pin, because the pin is bumped at land from the summed deltas and not by you. That is expected and is not a failure of your work: confirm that observed equals `pin + your declared delta`, and state both numbers in your report.

## Completion Criteria

- The mechanism is NAMED, with the evidence that named it, and the SP-065 record's off-by-one is either confirmed as an off-by-one or overturned with evidence.
- The fix is in `ChaosTunnelLoopback.cs` and the invariant it establishes is stated in one sentence.
- The five assertions at `:142-146` are byte-identical to `05fed4dd`.
- Every new fact bites under its own independent revert, with red counts recorded.
- `record.md` and `floor-delta.json` exist and are accurate.
- Build 0W/0E.
- `client/tests/floor/floor.json` and `client/docs/task-board.md` are untouched.

## Do NOT

- **Weaken, delete, reorder, retry, or quarantine any of the five assertions.** `floor.json`'s `admissionRule` names this test as one of two permanent bans from `allowedSkips`; adding it there, or to any other suppression, ends the packet.
- **Add a wait, poll, or `TestWait` around the `_log.All` read** to let the log catch up. That converts a real ordering defect in shipping code into a tolerated one and turns a fast flake into a slow one. If the source is fixed, no wait is needed; needing one is the tell that it is not.
- **Call `ThreadPool.SetMinThreads` / `SetMaxThreads`** to force or suppress the race. It is process-wide, the suite runs collections in parallel, and it would poison every other test in the assembly.
- **Add `[Collection]`, serialize the class, or disable parallelization** to make it green. That is quarantine wearing a different hat and it hides the defect from every future run.
- **Change `RouteClass` (`:204-208`).** It is correct and it is the privacy primitive. The defect is WHEN the line is emitted and WHAT ELSE reaches the sink, never what `RouteClass` returns. Moving that boundary is a change nobody reviewed.
- **"Fix" it by flushing or closing the output stream before logging.** That changes delivery semantics and still leaves the log after the point where a client can observe the response.
- **Post the log to another thread, or fire-and-forget it.** That makes the ordering strictly worse and unbounded.
- **Touch `client/src/CcpClient.Desktop/Features/Dtrh/LoopbackServer.cs`.** The same shape is there at `:186` and `:471`; it belongs to another lane's scope this wave. Report it, do not fix it.
- **Add a product seam, an `internal` hoist, or a test hook** to `ChaosTunnelLoopback`. The `Action<string> log` constructor parameter is the seam.
- Add a wall-clock wait. `TestWait.cs` is the only approved helper, and `TestTimingGuardTests` bans far more tokens than `Thread.Sleep` and `Task.Delay` alone (see Context).
- Edit `client/tests/floor/floor.json`, `client/tests/floor/vacuous-shape-ledger.json`, `client/docs/task-board.md`, or anything under `client/docs/`, `.claude/`, `.spine/`, `.pi/`, or `ConditioningControlPanel/`.
- Close, edit, or claim any neighbouring board row.
- Export `CCP_DATA_ROOT` process-wide.
- Leave a TODO, a placeholder, or a partially wired mechanism.

## Git Commit Convention

Conventional commits, `feat(SP-076): ...` (or `fix(SP-076): ...` if the slice is purely the ordering repair). One coherent slice, no unrelated files. Leave the tree buildable at every commit. Commit your own work on your branch; do not merge, do not land, and do not touch the shared pin.

## Documentation Requirements

If your work changes a fact stated in `client/docs/` (the §4 loopback discipline wording is the likely one, since "route-class logging" would gain an ordering clause), say so in `record.md` and quote the wording you believe is owed. **Do not edit the contract document yourself.** Policy-touching text is applied by the orchestrator at land (SP-059 precedent; SP-071, SP-072 and SP-073 all followed it).

The board row correction is also owed and also not yours to apply: `record.md` states, for the orchestrator, that the SP-065 record's `:143` annotation is an off-by-one and what the failing assertion actually was.
