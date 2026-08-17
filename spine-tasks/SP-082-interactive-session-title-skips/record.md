# SP-082 — record

Branch `sp-082-interactive-session-title-skips`, worktree `.claude/worktrees/agent-sp082`.
Executed against the approved plan (`.port/plans/SP-082-interactive-session-title-skips/plan-round-1.md`, round 1, APPROVE with 6 non-blocking suggestions).

## 1. Census (Step 1) — re-run at execution, not inherited

Reviewer suggestion 4 required re-running the census rather than inheriting the plan-time observation, because the plan's decision branch keys on machine state, not on a property of the tree. Re-run:

```
dotnet build client/CcpClient.sln -c Debug --nologo   → Build succeeded. 0 Warning(s) 0 Error(s)
node client/tests/floor/check-floor.mjs
  → FLOOR OK: CcpClient.Tests: 1028/1028 total, 2 skipped
      [SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked,
       ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps];
    CcpClient.HeadlessTests: 35/35 total, 0 skipped
```

Targeted run of the two packet facts:

```
Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2
```

**The reproduction does NOT reproduce.** Both facts EXECUTE and PASS. A foreground window exists on this machine; the session is interactive. Neither observed skip is a packet target — both are the pre-existing Linux-gated pins.

This is the packet's Step 1 "if they do NOT fail, say so plainly" branch. The wave-31 evidence at `ee6c9164` is not in doubt; the machine is simply in the other state. The state observed at execution matches the state observed at plan time.

### Consequence: Step 5's premise is inverted, and I did not write its sentence

Step 5 instructs me to write, in those words, that "on this machine tonight the interactive arm CANNOT be exercised." **That is false here and I have not written it.** The truth is the exact inversion:

- The **interactive arm executed and passed.** It is the arm that is verified.
- The **skip arm** is what could not be exercised naturally, because no locked/disconnected/secure-desktop session was available. It is covered by simulation (§3 E1a/E4a) and by nothing else.

**Named manual gate (the honest one, inverted from the packet's):** on a locked, disconnected, or secure-desktop Windows session these two facts must **SKIP, not FAIL**, and `check-floor.mjs` must stay green with `total` still 1028. That is the gate this packet exists to satisfy and it is discharged only by simulation here.

The packet's own gate — *on an interactive Windows desktop these two must EXECUTE and PASS, not skip* — **is discharged directly by this run** and by the final verification in §5.

## 2. Decision

Evidence selects the plan's "does not reproduce" branch. Step 4(a) provable; Step 4(b) **not naturally provable** (with a foreground window the facts pass with or without the predicate) and substituted by the simulated experiments; Step 4(c) provable.

### File Scope amendment — granted, and empirically forced

The plan's §0 headline was that `Assert.Skip*` forces the `dynamic-skip` shape (`VacuousShapeDetector.cs:186-195`, `:231-234`), that `VacuousShapeGuardTests.cs:81-86` demands exact shape-set equality, and that both targets are already ledgered without it — so the packet as written was unsatisfiable and needed `client/tests/floor/vacuous-shape-ledger.json` added to May-change. That file is genuinely absent from the machine-checked `fileScopeMustNotChange`.

**R4 below is the empirical proof this was real, not theoretical:** with the code change in place and the ledger reverted, the guard fails naming both sites. Without the amendment SP-082 cannot land green.

`client/tests/floor/floor.json` was **not** edited. `client/src/**` was **not** edited. The board was **not** edited.

## 3. Revert matrix — executed for real

Each mechanism source reverted **one at a time**, tree restored **byte-identically** between reverts and verified by SHA-256 after every restore:

- `AiAwarenessTests.cs` canonical `3d0c7b4a3ec6a98f9bd4b5ce22c0c87f045ae2b34b2d6ae146d5b7b2716abe50`
- `vacuous-shape-ledger.json` canonical `7f8065f6e536c0e349e32df29642be3a1076325b83a46b85bd562368665c348a`

Every row was rebuilt before running (`dotnet test --no-build` otherwise reports a stale count).

### Part A — plain reverts, real machine state (interactive)

| # | Reverted | Facts red | Which | Reading |
|---|---|---|---|---|
| R1 | fact one's `Assert.SkipWhen` | **1** | `VacuousShapeGuardTests` | Both title facts still PASS. On an interactive session the skip has no bite; the only thing that reds is the ledger coupling. |
| R2 | fact two's `Assert.SkipWhen` | **1** | `VacuousShapeGuardTests` | Same. Detector reported the site at `:428`, confirming the refreshed line value. |
| R3 | the reorder (assertions back at tail) | **0** | — | Invisible on an interactive session, and it does **not** move the ledger shapes. Its necessity is provable only under a firing skip: see E4b/E4c. |
| R4 | the ledger shape appends (code untouched) | **1** | `VacuousShapeGuardTests` | Names **both** sites. This is the §0 proof. |

R4's exact output confirms the plan's shape arithmetic, which was the one place the design could have been wrong:

```
:387 TitleProbe...  ledger [assertions-all-nested, platform-predicate]
                  detector [assertions-all-nested, platform-predicate, dynamic-skip]
:428 TitleObservation... ledger [platform-predicate]
                       detector [platform-predicate, dynamic-skip]
```

`assertions-all-nested` **survives** on fact one — the skip token is `continue`d out of the assertion list, so it neither counts as an assertion nor changes the nesting classification. Both ledger edits are pure appends.

### Part B — simulated locked session

The locked session cannot be produced on this machine, so the state was injected inside `AiAwarenessTests.cs` only (no `client/src/**` edit — `AiTitleObservation.Unavailable` and `CapabilityState.Unavailable` are publicly constructible). Each experiment was reverted immediately.

| # | Setup | Result | Proves |
|---|---|---|---|
| E1a | fact one state := `Unavailable(no-foreground-window)` | **SKIP** | the locked-session leg works without a locked session |
| E1b | E1a + skip removed | **1 red** | the wave-31 red reproduced: `Assert.IsType() Failure: Expected Available, Actual Unavailable` |
| E2a | fact one state := `Unavailable("sp082-regression-probe")` | **1 red** | a different reason code is **not** swallowed |
| E2b | E2a + predicate widened to bare `is CapabilityState.Unavailable` | **SKIP** | the `Reason.Code` equality is load-bearing; the forbidden widening is what it would look like |
| E3 | untouched, real `Available` | **PASS** | negative control (the §1 census and §5 final gate) |
| E4a | fact two observation := `Unavailable(no-foreground-window)` | **SKIP** | observation-side leg works |
| E4b | E4a + simulated zero-leak regression (`h.Memory.Appends++`) | **1 red** | with the reorder, a real leak is still caught **even though the session gate fires** |
| E4c | E4b + reorder reverted (assertions back at tail) | **SKIP** | **the identical leak is silently swallowed.** The reorder is load-bearing |
| E5a | fact two observation := `Unavailable(title-dropped-blank)` | **1 red** | a privacy-drop code is **not** swallowed |
| E5b | E5a + predicate widened | **SKIP** | symmetric with E2b |

**E4b/E4c is the row that matters most.** It shows the vacuity the packet's Step 3 did not enumerate: had the two zero-leak assertions been left at the tail, `Assert.Skip*` would abort the fact before reaching them and a genuine zero-leak regression would report as a green-ish SKIP. R3 alone could not show this, because on an interactive session the skip never fires.

## 4. Narrowness — what the predicate accepts

Equality against one named product constant, so every other typed answer falls through to the assertions and still FAILS: `LinuxUnprobedCode`, `UnsupportedPlatform`, `IncognitoDropCode`, `BlankTitleDropCode`, `CapabilityReasonCodes.NotProbed`, and any `Available`. E2a/E5a demonstrate two of these empirically.

The skip is conditioned on **no** OS check, **no** environment variable, **no** CI flag, and **no** `Debugger.IsAttached`. The surrounding `if (OperatingSystem.IsWindows())` is the pre-existing platform **branch**; the skip sits *inside* it and is not conditioned on it.

Fact two keys on the reason **code** rather than the state type because `NoForegroundWindowCode` is reachable two ways — the probe's state handed back verbatim (`AiAwarenessService.cs:573-577`) and a capture miss at observation time (`:579-582`). The code is the only discriminator covering both.

## 5. Final verification

Built immediately before the gate, both through the slot semaphore:

```
node client/tools/gate/with-slot.mjs --slots 3 -- dotnet build client/CcpClient.sln -c Debug --nologo
  → Build succeeded. 0 Warning(s) 0 Error(s)

node client/tools/gate/with-slot.mjs --slots 3 -- node client/tests/floor/check-floor.mjs
  → FLOOR OK: CcpClient.Tests: 1028/1028 total, 2 skipped
      [SecretStoreTests.LinuxProbe_TypedOutcome_NeverFaked,
       ChaosTunnelCapabilityTests.Linux_UnavailableNamesTheTunnelsOwnTwoGaps];
    CcpClient.HeadlessTests: 35/35 total, 0 skipped
```

Pin 1028 + declared delta 0 = observed 1028. Headless 35 + 0 = 35. Skip set unchanged from baseline; neither packet fact skipped, because the session is interactive. **No pin mismatch was reported** — this packet's delta is 0/0, so the gate matches the pin exactly.

## 6. HONESTY — what is NOT proven

1. **No standing guard protects the narrowness.** The plan declared `0/0` and added no new fact, so E2b/E5b — the forbidden widening — are proven only by one-time experiments that have been reverted. A future widening of either predicate would compile, pass, and be caught by **nothing**: not `check-floor.mjs` (the may-skip pin accepts a skip), and not `VacuousShapeGuardTests` (the shape set is `dynamic-skip` either way). E2b and E5b both demonstrated this by producing a SKIP that the gate would have accepted. This is the single largest residual risk in this packet and it is unmitigated by design.

2. **The locked session was simulated, never observed.** E1a/E4a inject the typed state; they do not prove that a real locked Windows session produces exactly `Unavailable(no-foreground-window)` at both sites. That inference rests on reading `AiAwarenessService.cs:343-347` and `:573-582`, plus the wave-31 field evidence. **Manual gate, undischarged:** on a genuinely locked/disconnected/secure-desktop session, confirm both facts SKIP (not fail) and the gate stays green at 1028.

3. **Residual divergence on a blank-titled foreground window** (reviewer suggestion 5). `TryCaptureForegroundTitle` returns **true** for a foreground window whose title is empty, so that state is `Available` at the probe but routes to `BlankTitleDropCode` at observation. On such a session fact one **passes** ("title length 0") while fact two **fails** (`Unavailable(title-dropped-blank)` vs `Assert.IsType<Observed>`). The two facts diverge on that one state. This is packet-mandated behaviour — SP-082 is explicitly forbidden from widening the skip to swallow that code — but it is the next thing that will red this gate, and E5a is exactly its signature. Recorded so it is not rediscovered as a mystery.

4. **R3 proved nothing on its own.** Reverting the reorder red 0 facts. Its value is established only by the E4b/E4c pair, which required an injected regression. If a future edit moves those two assertions back below the gate, no standing test will notice on an interactive machine.

5. **Linux arms unexecuted.** Both facts' `else` arms are Windows-machine-unreachable here; unchanged by this packet and unverified by it.

## 7. Discharge of inherited obligations

The approved plan carried no `## Carried conditions`. The reviewer's six non-blocking suggestions were obligations:

1. **Wrong lock named in §4 — discharged.** The plan named `CapabilityRegistry._gate`; the reorder never calls `GetState`, so that was decoration. The lock actually taken is `CollectingAiDiagnosticsSink._gate` (`client/src/CcpClient.Desktop/Ai/AiDiagnostics.cs:19`), whose `Records` getter returns `_records.ToArray()` **under** the lock — a snapshot-under-lock read that cannot tear even against a hypothetical concurrent `Emit`. Verified and used as the in-file comment's justification.
2. **§7 census wrong — discharged.** Confirmed 6 `Assert.Skip*` sites / 7 call sites, and they are **not** all OS-gated: `DataRootOverrideTests.cs:78` and `:92` are `Assert.SkipWhen` on runtime process state, ledgered with `"shapes": ["dynamic-skip"]`. That is the non-platform precedent this packet needed.
3. **Do not invent a sixth verdict — discharged.** `session-precondition-skip-converted` was **not** used. Both entries keep `"verdict": "not-vacuous"`, matching the precedent in (2), with the session-precondition specifics carried in `reason`.
4. **Re-run the census — discharged.** §1; re-run at execution start, state reported as observed now.
5. **Name the blank-title divergence — discharged.** §6 item 3.
6. **Create `floor-delta.json` — discharged.** `spine-tasks/SP-082-interactive-session-title-skips/floor-delta.json` written (0/0). Ledger `line` values refreshed: fact two 414 → 428 (informational only; fact one stays 387).

## 8. Out of scope — to file

1. Machine-checked `fileScopeMustNotChange` and the prose File Scope disagree (prose says "everything else", the machine list is 8 entries). Reconcile at template level, so a future skip-conversion packet does not ship unsatisfiable again.
2. **No product defect.** The stop condition was not triggered. `client/src/**` is correct as the packet asserts; the tests were the defect.
3. **A standing narrowness guard is the obvious follow-up** to §6 item 1 — a fact asserting that the two skip predicates accept `no-foreground-window` and reject the other codes would convert E2b/E5b from a one-time experiment into a permanent tripwire. It is out of scope here because the approved plan declared `0/0` and adding a fact would have contradicted its own floor-delta declaration. Filing rather than doing it silently.

### Note on the working tree found at start

This worktree contained an **uncommitted prior attempt** at SP-082 that diverged from the approved plan: it hoisted the predicates into private helpers, added standing depth-0 narrowness-control assertions inside both facts, **duplicated** rather than moved the zero-leak assertions, and consequently wrote a ledger that **dropped** `assertions-all-nested` from fact one. R4's detector output above shows that shape set is wrong: the detector still sees `assertions-all-nested` on fact one under the approved mechanism. That work was preserved to scratchpad and reset; this packet implements the approved plan. Its narrowness-control idea is the substance of the §8.3 follow-up.
