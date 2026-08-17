# SP-085 — record

Lane branch `lane/SP-085-tunnel-logging-named-flake`, base `cf9f7143`, worktree `.claude/worktrees/sp085`.

**Prior seat produced no lane diff.** The first attempt reported the wave base `cf9f7143` as its head and declared `-1/-1`; the blocking review proved the tree was byte-identical to base and that no SP-085 commit existed on any ref. Every claim below was produced in this worktree and is reproducible from the commands quoted.

**Final-review revision seat (third seat).** The implement seat's commit `3d0cac62` passed plan and code review; the final review raised two writing-only blocking items and four non-blocking ones, and this seat applied them. It did not redesign the mechanism, re-run §1a's bounded loop, or re-run §1b's size sweep — those tables are the implement seat's measurements and are labelled as such. What this seat did re-measure is the whole revert matrix (§5), because it changed two of the three guards' internals. Its product change is a **comment correction only**: §2's justification was refuted by §6 of this same record, and the source comment on the payload path repeated the same refuted sentence.

## 1. Mechanism, named from executed evidence

**The route-class line was emitted after the response body had already been written, so a client could observe a complete response before the sink contained the corresponding line.** Under a saturated pool the server-side continuation that appends the line gets queued behind the client's own continuation, and the test's `_log.All` snapshot then reads a sink that is one line short. The failing assertion is an *expected route class MISSING*, never a filename present.

Fixed by making both success paths log before they write, which is what `Refuse` has always done. Invariant, in one sentence:

> Every route-class log line is emitted before any byte of the corresponding response can leave the process.

### 1a. Step 1 bounded loop (budget: <= 200 iterations per construction, <= 3 constructions, no sleeps, stop on first reproducing construction)

Harness: a scratch console app outside the repo that links the product file unchanged and replays the fact's exact request pair (`/tunnel/index.html?probe=shh-secret` then `/vendor/three/three.module.min.js`), snapshots the sink, and evaluates all five assertions. Kept out of `client/` so it could never move the floor.

| Construction | Iterations run | Reds | First red |
|---|---|---|---|
| 1 | 200 | 0 | none |
| 2 | 182 | 1 | iteration 182 — `:143 Assert.Contains("/vendor/", logs, StringComparison.Ordinal)` |
| 3 | not run | — | budget rule: stop as soon as a construction reproduces |

**Iteration accounting against the bound:** 200 + 182 = 382 request pairs across 2 of the 3 permitted constructions; no construction exceeded 200. The historical red WAS re-observed, at roughly 1 in 382 pairs here versus the recorded 1 in 15 full-suite runs — the loop is single-threaded and unloaded, so a lower rate than a saturated suite is expected and the two rates are not comparable.

Both `DoesNotContain` assertions passed on the red iteration. Nothing put a filename or a query string into the sink at any point in 382 pairs.

### 1b. By construction, deterministically (the same mechanism without the luck)

Serving a body larger than any send buffer and never draining it parks the server's write, which on the unfixed tree parks the log line with it:

| Body | `-> 200` line present when headers observable | still absent 400 ms later | present after draining |
|---|---|---|---|
| 1 MB | no | yes | yes |
| 2 MB | no | yes | yes |
| 4 MB | no | yes | yes |
| 8 MB | no | yes | yes |

On the fixed tree the same probe reports the line present at headers at every size. So a client can observe a response before the sink mentions it, by construction, independent of scheduling.

**Accounting, since the table did not say it.** This sweep is **four probes, one per size**, each a single request that is never drained — the three columns are three observations of that one request, not three runs. It is **Step 4 discriminator sizing, not Step 1 reproduction**, so it does not count against Step 1's budget of 200 iterations per construction across at most 3 constructions; that budget is spent in §1a and is accounted there (200 + 182, two constructions). Step 4 sets no iteration bound. The sweep terminated because parking was already total at the smallest size swept, so a larger size could only be more deterministic, not less.

### 1c. Branch selection

**Branch A.** The reproduced red is a missing expected class (`Assert.Contains` for `/vendor/`), not a filename present, and no filename or query ever reached the sink in 382 pairs. Branch B's precondition (evidence putting a filename into `_log`) was never met, so Branch B did not outrank anything. The `:105` vector was still closed on its own merits — see §3.

### 1d. The SP-065 record's off-by-one: CONFIRMED as an off-by-one

SP-065's record transcribes the failure site as `ChaosTunnelLoopbackTests.cs:143` and annotates it in the same sentence as `Assert.DoesNotContain("index.html", logs)`, concluding "the collecting log contained a filename".

- The transcribed **line number is correct**: `:143` is `Assert.Contains("/vendor/", logs, StringComparison.Ordinal)`, and that is exactly the assertion this lane reproduced red.
- The **annotation is off by one**: `Assert.DoesNotContain("index.html", ...)` is `:144`.
- The **conclusion is refuted**: the assertion that fired is a presence check for an expected route class, so the observed red means a class was missing, not that a filename leaked.

Independent corroboration, not relied on but agreeing exactly: `client/docs/task-board.md:103` records a fresh wave-32-land reproduction — "failed exactly 1 of 1028 on this test, at `Assert.Contains` for `/vendor/` in the collected log (`...ChaosTunnelLoopbackTests.cs:143`)" — and states the hypothesis that the fact "reads the log buffer without waiting for the server to have recorded the second". Same site, same mechanism, arrived at independently.

**That hypothesis was carried in as a lead and checked against source, not adopted.** It is exactly this mechanism, and the fix closes it without the fact needing to wait for anything. Both calls at `ChaosTunnelLoopbackTests.cs:138-139` use the default `HttpCompletionOption.ResponseContentRead`, so an awaited `GetAsync` cannot return until the whole body has been read; the body cannot be read before the server writes it; and after the fix the server appends the line at `ChaosTunnelLoopback.cs:203` strictly before the write at `:204`, on the same continuation, under `CollectingLog`'s lock. So by the time the second `GetAsync` has returned, both classes are already in the sink and the `_log.All` snapshot on the next line cannot be short. The race the board row hypothesised is a real ordering window and it no longer exists — which is why the correct response was the source fix, not the wait the row's wording invites.

## 2. Step 2 wording decision: `-> 200` KEPT

I took the audit-record reading and left the wording alone. The line is a record of what the server *decided to serve*, which is precisely how `Refuse` has always used the identical shape — it logs `-> {code}` before writing a single byte, and nobody has ever read that as a delivery receipt. Treating the success lines the same way makes the file internally consistent instead of carrying two different meanings for one format, and it is what a privacy boundary wants: record the decision, then act, so the record cannot be lost by whatever happens next. Changing the wording would also have been a user-visible string change on a live path (`ChaosTunnelService.cs:179-180`) bought for nothing, since no test and no product code parses these strings.

**Correction, applied at final review: the compensating control this paragraph originally claimed does not exist.** The first version of §2 answered the honesty objection with "a post-log write fault is reported separately by the handler-fault path, so the pair of lines is strictly more informative than a single line emitted late." That is false in the dominant case, and §6 of this same record measured the case that refutes it. `ChaosTunnelLoopback.cs:101-104` catches `HttpListenerException or ObjectDisposedException or IOException` with an **empty body and no log line at all** — the client-went-away class. Only faults *outside* that filter reach the handler-fault line at `:113`. A post-log write fault caused by a client disconnect is therefore swallowed in silence, never reported, and §6's executed observation is exactly that: after parking a handler and disconnecting, the sink held `origin bound...` and `GET /tunnel/ -> 200` and nothing else. The packet's own Context asserted the same premise ("the write-fault path already logs separately at `:101`/`:105`"); it is half-wrong, `:101` swallows, and the first version of this record adopted it without checking.

The direction is inverted too, and that is the honest way to state the cost. On the **unfixed** tree a client-disconnect write fault produced **no line at all**, because the log call sat after the write and was never reached. On the **fixed** tree it produces a bare `-> 200` for a response that never left the process. So the pair is not "strictly more informative"; in that case the single line is strictly more *misleading*. The wording decision stands — it is pre-authorized both ways and consistency with `Refuse` is still the better shape — but it is taken with that residual open and named (§9), not with a compensating control that was never there. The refuted sentence was also repeated in the source comment on the payload path and has been corrected there in the same commit. Closing the silent swallow itself is filed as an owed row (§7) and deliberately not done here: it would widen the diff on a shipping path past what this packet scoped.

**Plan-gate condition #4, discharged here because it belongs to this decision.** Sharpening the stderr consequence: the sink this file logs into is synchronous on the live path. `ChaosTunnelService.cs:179-180` constructs the server with `msg => _host.LogDiagnostic(msg)`; `ApplicationHost.cs:78` forwards that to `ILogSink.Log`; the default `DebugLogSink` (`Lifecycle/CompositionRoot.cs:15-19`) is a synchronous `Console.Error.WriteLine` followed by `System.Diagnostics.Debug.WriteLine`. Before this change that pair ran *after* the last byte of a 200; after it, it runs *before the first byte* of every asset the tunnel origin serves — index.html plus every import-map dependency the embedded surface pulls, on every session. So a blocked, full, or slowly-redirected stderr now delays the start of a response instead of trailing its end. `Refuse` already carried exactly this shape, but only on refusals, which are rare; this change puts it on the common path. It is a real, user-visible behavioural reposition on a shipping path and it is accepted deliberately: the alternative is either a log the ordering invariant cannot rely on, or an asynchronous post that the packet forbids for making the ordering strictly worse and unbounded. Recorded rather than discovered later.

Grep re-run in this worktree, as the packet required. **Before this change** `chaos-tunnel-loopback:` appeared only in `ChaosTunnelLoopback.cs` across `client/` — zero external consumers, which is what made the wording safe to change had I wanted to. **After this change there are two files**, because the new structural guard anchors on those exact literals. So the packet's premise held at decision time, and I am recording the consequence rather than leaving the stale claim: the wording is now load-bearing for `EveryResponseEmittingPath_RecordsItsRouteClassBeforeItWrites`, and anyone who rewords a route-class line must re-anchor that guard in the same commit. The guard fails loud and names the anchor if they do not — it asserts each anchor matches exactly one line and reports the count when it does not.

## 3. Step 3 decision: `:105` NARROWED to type-only

Decision: **narrow**, because a reachable, non-filtered, path-carrying exception type exists and is named.

Enumeration (measured in this worktree, not recalled):

| Type | Filtered by the client-went-away catch? | Message can carry a path |
|---|---|---|
| `FileNotFoundException` | yes (is `IOException`) | — |
| `DirectoryNotFoundException` | yes (is `IOException`) | — |
| `PathTooLongException` | yes (is `IOException`) | — |
| **`UnauthorizedAccessException`** | **no** — chain is `UnauthorizedAccessException -> SystemException -> Exception` | **yes** |
| `SecurityException` | no — chain is `SecurityException -> SystemException -> Exception` | yes |

The first four rows are types `File.ReadAllBytesAsync` can actually raise on this path. **`SecurityException` is the exception: it is listed for hierarchy contrast, not as a reachable type.** It came from CAS-era file APIs and modern .NET does not raise it from `File.ReadAllBytesAsync`; nothing in the decision rests on it, because `UnauthorizedAccessException` alone satisfies the packet's "name at least one reachable, non-filtered, path-carrying type". Read the table as an enumeration of the *filter* boundary, with reachability carried by the row in bold.

Executed control: raising a real `UnauthorizedAccessException` from a file read printed `filteredBy101=False`, `messageCarriesPath=True`, message `Access to the path '<full path>' is denied.`

Reachability: `File.ReadAllBytesAsync(file, ...)` on the payload path runs after `File.Exists(file)` has already returned true, so an ACL-denied or permission-changed payload file raises `UnauthorizedAccessException` with the full payload path in its message, straight into the sink that is forbidden even a bare filename. Narrowing to the type-only shape the accept-fault line already uses deletes the vector and adds no mechanism.

**Honest bound, stated as the packet requires:** the narrowing is pinned only by a lexical guard, not by a behavioural fact. Driving a non-filtered fault through a real round trip needs either a product seam (forbidden here) or an ACL/permission fixture (platform-gated, which the new fact must not be). It is therefore **an unpinned-behaviourally, lexically-pinned defensive change on a privacy sink**, and it is not presented as behaviourally covered.

## 4. What landed

`client/src/CcpClient.Desktop/Features/Chaos/ChaosTunnelLoopback.cs` — three behavioural changes plus one comment correction:

1. `/health`: `_log(...)` moved above `await WriteText(...)` (`:134` logs, `:135` writes).
2. payload 200: `_log(...)` moved above `await res.OutputStream.WriteAsync(...)` (`:203` logs, `:204` writes).
3. handler fault: `{ex.GetType().Name}: {ex.Message}` → `{ex.GetType().Name}`.
4. (final review) the payload-path comment no longer claims a post-log write fault "is reported separately by the handler-fault path". It now names both accepted consequences — the silent client-went-away swallow and the synchronous-stderr reposition — so the file states the cost of its own ordering rather than denying it. Comment only; this is what moved the payload path's line numbers from `:194/:195` to `:203/:204`, and the guards compute those numbers rather than hard-coding them.

`RouteClass` untouched. `Refuse` untouched. No flush, no stream close before logging, no posting the log to another thread, no seam, no `internal` hoist, no `[Collection]`, no `ThreadPool` tuning.

`client/tests/CcpClient.Tests/ChaosTunnelLoopbackTests.cs` — three new facts, `+3` unit:

1. `RouteClassLine_IsAlreadyInTheSink_WhenTheResponseBecomesObservable` — behavioural; 4 MB body, `HttpCompletionOption.ResponseHeadersRead`, never drained; asserts the route-class line is already in the sink when headers are observable, and that the filename never is.
2. `EveryResponseEmittingPath_RecordsItsRouteClassBeforeItWrites` — structural; reads the product source and pins log-line-before-write-line on all three response-emitting paths, reporting `file:line`. **Strengthened at final review** with a completeness half: the SET of response writes in the file is pinned too, so a *fourth* path added later that logs after it writes cannot pass by never being looked at. Every site containing `WriteText(` or `OutputStream.WriteAsync(` must be one of the three checked paths or the shared writer they delegate to; anything else is named with `file:line`.
3. `ThePrivacySink_NeverReceivesAnExceptionMessage` — structural; no `_log(` call site may interpolate `.Message`, with a `>= 4` call-site floor so it cannot pass vacuously if the sink is renamed. **Strengthened at final review**: it reads each call to the parenthesis that closes it, across as many lines as it spans (parentheses inside string literals are not counted, and a call that does not close inside a 40-line cap fails loud). The original per-line scan flagged `.Message` only when it shared a source line with `_log(`, so a multi-line interpolation slipped past it — which on a privacy sink is the leak shape a reviewer would most expect a refactor to introduce.

**The five protected assertions are byte-identical to `05fed4dd`** — verified by diffing lines 136-147 of the SP-061 blob against the working tree: identical, and still at the same line numbers, because the new facts were inserted after that method. Nothing was weakened, reordered, retried, waited on, or quarantined; `floor.json` `allowedSkips` untouched.

## 5. Revert matrix — every fact bites under its own INDEPENDENT revert

One change reverted at a time; the tree restored byte-identically between reverts and the restore verified by SHA-256 (`DE2A0A78CC5666C581DE183ADC022923D69EE4EE46E57DA2E6B4EB6DAC36847F` before and after each of the three).

| # | Reverted (alone) | Reds | Which facts, and what they reported |
|---|---|---|---|
| R1 | payload-200 ordering | **2** | behavioural fact (`Not found: "/tunnel/ -> 200"`) + structural ordering guard (`ChaosTunnelLoopback.cs:203 — the payload 200 path writes its response at :203 but only records the route class at :204`) |
| R2 | `/health` ordering | **1** | structural ordering guard only (`ChaosTunnelLoopback.cs:134 — the /health path writes its response at :134 but only records the route class at :135`) |
| R3 | `:105` narrowing | **1** | privacy-sink guard only, quoting the offending line at `ChaosTunnelLoopback.cs:113` |

Fixed tree, same filter: 17/17 passed, 0 skipped. R2 is why the structural guard exists rather than being redundant: `/health` serves a two-byte body that no round trip can park, so it is the one path the behavioural fact provably cannot reach, and R2 is the only revert that reddens exactly one fact naming it.

**Re-measured in full at the final-review seat**, because that seat changed the internals of two of the three guards; the matrix above is that seat's run, not a carry-over. R1's line numbers moved from `:194/:195` to `:203/:204` for the comment-only reason in §4 — the guard computes them, so nothing was re-anchored. Two further probes exercise the two strengthenings, each applied alone to the otherwise-fixed tree and reverted before the next:

| # | Injected (alone) | Reds | What it reported |
|---|---|---|---|
| P1 | the handler-fault line rewritten as a **multi-line** `_log(...)` whose second line adds `+ $": {ex.Message}"` | **1** | privacy-sink guard, quoting the whole call across both lines at `ChaosTunnelLoopback.cs:115`. The pre-final-review per-line scan would have passed this — the line holding `_log(` carries no `.Message` |
| P2 | a **fourth** response-emitting path (`/probe-c`: write, then log) inserted above `/health` | **1** | ordering guard's completeness half: `ChaosTunnelLoopback.cs:131: await WriteText(ctx, 200, "probe", "text/plain");` named as a response emitted by a path the guard does not check. The three ordering comparisons themselves stayed green, which is precisely the blind spot |

A first form of P2 whose log text happened to contain the payload anchor as a substring also failed loud, on `SoleMatch` ("anchor matched 2 lines"), so both collision shapes are caught rather than silently making a comparison vacuous. The product file was restored between every revert and probe and the restore verified against the lane head: `git diff` on `ChaosTunnelLoopback.cs` against `3d0cac62` shows the payload-path comment hunk (§4 item 4) and nothing else.

## 6. Carried conditions, discharged

- **Windows-only provenance of the 4 MB sweep.** The size sweep was measured on Windows only (`http.sys` send buffering); no Linux box was available. This bounds the RED direction only: the claim "an undrained response parks the write" is Windows-measured, and a Linux kernel with a large enough socket buffer could in principle let the write complete and make the revert-red weaker there. The GREEN direction is platform-independent by construction — the line is appended before the write on the same async path, so headers-observable implies line-present on any platform. 4 MB was chosen with margin: parking was already total at 1 MB, the smallest size swept.
- **Probe-iteration accounting against the 200-per-construction bound.** §1a: 200 and 182, two constructions, neither over 200, stopped on reproduction.
- **The teardown `TaskCanceledException` line and its stderr consequence.** Checked, not assumed, and it **does not occur**. Observed sink after parking a handler and tearing the server down: exactly `origin bound on 127.0.0.1 (ephemeral)` and `GET /tunnel/ -> 200`, and nothing else. Disposing the response disconnects the client first, so the parked write fails with an `IOException`/`HttpListenerException` and is absorbed by the existing client-went-away filter as a non-fault. No handler-fault line, no stderr line, and no path in the sink.
- **The stderr consequence of logging first (plan condition #4).** Discharged in §2's final paragraph rather than here, because it is a consequence of the wording/ordering decision and belongs beside it: the sink is synchronous (`ChaosTunnelService.cs:179-180` → `ApplicationHost.cs:78` → `DebugLogSink`, `CompositionRoot.cs:15-19`: `Console.Error.WriteLine` + `Debug.WriteLine`), so after this change a blocked or redirected stderr delays the FIRST byte of a 200 instead of following its last, on every asset the tunnel origin serves. Traced by reading the three files, not assumed. The first version of this record did not mention it at all; that omission is the reason the condition was reopened at final review.
- **The `task-board.md:103` citation.** The packet's Context section cites the row as `client/docs/task-board.md:99`; the row is now at **:103** (the board grew after the packet was authored). Content matches; only the line moved. See also §1d — the row's own wave-32 text corroborates the mechanism.
- **The owed `LoopbackServer.cs` board row.** §7 below. Not acted on; the file was never opened for writing.

## 7. Owed board rows — NOT acted on

### 7a. The DTRH mirror (out of scope, another lane's file this wave)

`client/src/CcpClient.Desktop/Features/Dtrh/LoopbackServer.cs` carries the identical asymmetry, verified by reading it in this worktree:

- `:186` — `/health`: `await WriteText(ctx, 200, "ok", "text/plain");` then `_log.Log("dtrh-loopback: GET /health -> 200");`
- `:471` — the payload path: `_log.Log($"dtrh-loopback: GET {RouteClass(path)} -> {status} ({source})");` after the body has been written
- `:522` — `Refuse`: logs first, exactly like the tunnel server's

Proposed row: *P2 OPEN — the DTRH loopback origin logs its route class after writing the response on both success paths, the defect SP-085 named and fixed in the mirrored tunnel origin. Same privacy sink, same asymmetry against its own `Refuse`. Acceptance: apply the SP-085 ordering invariant at `:186` and `:471`, and extend the shared-invariant pin so a sweep finding one server finds the other. The SP-085 revert matrix is the template.* Size S.

I did not touch that file, did not close or edit any board row, and did not edit any document under `client/docs/`.

### 7b. The silent client-went-away swallow (in this lane's file, deliberately not fixed here)

Raised at final review as the residual §2's original wording denied. `ChaosTunnelLoopback.cs:101-104` catches `HttpListenerException or ObjectDisposedException or IOException` with an empty body and no line, so a response that faults *after* its route-class line has been recorded leaves the sink asserting `-> 200` for bytes that never left the process, with nothing recording the non-delivery. This is not a new defect — the filter predates SP-085 — but SP-085 is what made it observable, because before this change the log call sat after the write and was never reached at all on that path.

Proposed row: *P3 OPEN — the tunnel loopback origin's client-went-away filter absorbs a write fault with no line at all, so a recorded `-> 200` can stand alone for a response that was never delivered. Acceptance: emit a route-class-only non-delivery line (class only, never a filename or query — the SP-085 privacy boundary is unchanged and `ThePrivacySink_NeverReceivesAnExceptionMessage` must stay green), pinned by a fact that bites under its own revert. Note the stderr cost in §2 before adding a second synchronous line to the hot path.* Size S.

Deliberately not done in this lane: the final review scoped the discharge to writing, fixing `:101` would widen a behavioural diff on a shipping path past what this packet reviewed, and a new sink line is exactly the kind of change that wants its own plan gate.

## 8. Documentation owed — NOT applied by this lane (SP-059 precedent)

The §4 loopback-discipline wording in `client/docs/` describes route-class logging without an ordering clause. Wording I believe is owed, for the orchestrator to apply at land:

> Route-class logging is ordered: every route-class line is emitted before any byte of the corresponding response can leave the process, so the record of what was served can never be observed later than the response itself.

Also owed at land, for the orchestrator: the board-row correction in §1d (the SP-065 record's `:143` annotation is an off-by-one; the failing assertion was `Assert.Contains("/vendor/", ...)`, a missing route class, not a leaked filename).

## 9. Honesty — what is NOT proven

- **Frequency is not re-bound.** One red in 382 single-threaded pairs is a reproduction, not a rate. It does not establish that the historical 1-in-15 full-suite hit rate is fully explained by this mechanism, only that this mechanism produces exactly that failure, at exactly that assertion, on this code.
- **The suite-level flake cannot be proven gone by green runs.** A mechanism this rare would pass many suites unfixed. Closure here is mechanistic — the sink is appended before the write, so the window in which a client can observe a response the sink has not recorded no longer exists — plus a fact that is deterministic in both directions. It is not a frequency claim.
- **This lane created one residual and it is accepted, not compensated.** A route-class audit line can now stand alone in the privacy-boundary sink for a response that was never delivered: the line is emitted before the write, and if that write then faults because the client went away, `ChaosTunnelLoopback.cs:101-104` swallows it silently, so nothing records the non-delivery. Measured, not supposed — §6's teardown observation is exactly this case. On the unfixed tree the same event produced no line at all, so the change trades "no record of a served response" for "a record that can outlive the response"; that is the right trade for an audit sink but it is a real loss of precision and it is the cost of the wording decision in §2, not a thing that decision answers. Filed as an owed row at §7b.
- **The `:105` narrowing is lexically pinned only** (§3). No behavioural fact bites on it.
- **Two of the three facts are lexical** and therefore weaker than behavioural ones: they pin the shape of the source, not the runtime ordering. A refactor that preserves the anchors while changing the semantics would pass them. The behavioural fact is the strong one and it covers the payload path only. The final-review strengthening closed the two blind spots a reviewer named — a fourth response-emitting path is no longer invisible, and a multi-line interpolation no longer slips past the sink scan — but both remain lexical, and both are bounded by the same thing: they read the source of ONE file by anchor, so moving a response write into a helper in another file, or reaching the sink through an alias rather than a literal `_log(` call, would still pass. Neither guard can be made behavioural without the product seam this packet forbids.
- **Linux is unverified.** No Linux box was available; the red direction of the behavioural fact is Windows-measured (see §6). The green direction is platform-independent by construction, so the fact should not be flaky on Linux, but that is reasoning, not a run.
- **The other nine facts in the class were not re-audited** for the same ordering assumption beyond what the reproduction and the gate exercise.
- No wall-clock wait, poll, or `TestWait` was added anywhere; the new facts contain none of the tokens `TestTimingGuardTests` bans. The 400 ms observations quoted in §1b and §6 belong to the scratch probe outside the repo and are investigative only — no such construct exists in `client/`.
- The three new facts carry no vacuous silencing shape (no early return, no all-nested assertion body, no platform/env/fs predicate, no dynamic skip), so `client/tests/floor/vacuous-shape-ledger.json` is owed no entry and was not touched.

## 10. Verification

Both run in this worktree through the slot semaphore, build immediately before the gate.

```
node client/tools/gate/with-slot.mjs --slots 3 -- dotnet build client/CcpClient.sln -c Debug --nologo
node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
```

Build: **0 Warning(s), 0 Error(s)**.

Gate: `CcpClient.Tests 1031/1031 total, 2 skipped`; `CcpClient.HeadlessTests 35/35 total, 0 skipped`.

Pin on disk is 1028 unit / 35 headless; declared delta `+3 / 0`; `1028 + 3 = 1031` and `35 + 0 = 35`, so observed equals pin plus declared in both projects. The two skips are the Linux-gated pair already carried in `allowedSkipsMachineClasses`. `client/tests/floor/floor.json` and `client/docs/task-board.md` are untouched.

**Re-run at the final-review seat**, same two commands, same order, build immediately before the gate. Build: **0 Warning(s), 0 Error(s)**. Gate: `CcpClient.Tests` total 1031, 1029 passed, 2 skipped, **0 failed**; `CcpClient.HeadlessTests` total 35, 35 passed, **0 failed** (TRX counters, both projects, from the preserved results directory). The gate exits non-zero and prints `FLOOR VIOLATION — total drift: 1031 result(s) (pin total 1028)`: that is the designed state for a lane that declares its delta instead of touching the shared pin, and the arithmetic closes at `1028 + 3 = 1031`. `floor-delta.json` is unchanged at `+3 / 0` — this seat added and removed no facts; it changed the internals of two existing ones.
