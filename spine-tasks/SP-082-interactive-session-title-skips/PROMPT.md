# SP-082 — Make the two window-title facts skip honestly when the Windows session is not interactive

## Mission

`node client/tests/floor/check-floor.mjs` is the port's ONE mechanical pre-land gate. Right now it is **red for as long as nobody is logged in and looking at a window**, which is exactly the state every unattended run happens in. Two facts in `AiAwarenessTests` require a real foreground window and **assert** rather than **skip** when there is not one.

**The product is not the defect and you must not change it.** `AiWindowTitleCapability.Probe` already returns a typed `Unavailable` carrying `NoForegroundWindowCode` (`"no-foreground-window"`), documented in the product as *"lock screen, secure desktop, or mid-switch — transient, re-probe later"*. That is the honest, correct answer. The tests are what is wrong: on Windows they assert `Available` / `Observed` unconditionally, so the product's correct behaviour reds the floor.

Your outcome: **on Windows, the two facts accept the product's typed no-foreground-window answer as a SKIP with the precondition named in-file, and keep asserting exactly as they do today whenever a foreground window exists.** The floor gate then means the same thing whether or not a human is at the desk.

## Dependencies

None in flight. Board row: the P0 filed at the wave-31 land ("Two floor tests FAIL instead of skipping when the Windows session stops being interactive"). **You do not edit the board.**

## Context to Read First

Every line below was opened in the port tree by the orchestrator at authoring and confirmed, not transcribed:

- `client/tests/CcpClient.Tests/AiAwarenessTests.cs:386-411` — `TitleProbe_PlatformTypedState_WindowsAvailable_LinuxUnavailable`. `:389` calls the real `AiWindowTitleCapability.Probe`. `:399` is `Assert.IsType<CapabilityState.Available>(state)` — **this is the line that fails.** `:401` is `Assert.True(AiWindowTitleCapability.TryCaptureForegroundTitle(out var title))`, which fails for the same reason one line later.
- The comment at `:391-397` **already names the precondition**: *"this arm requires an INTERACTIVE Windows desktop session — under a locked/non-interactive session GetForegroundWindow returns 0 and the probe honestly reports Unavailable (no-foreground-window). A failure here on CI means the session precondition was lost, not a product regression."* The comment is correct. The defect is that a precondition stated in prose is enforced by an assertion.
- `client/tests/CcpClient.Tests/AiAwarenessTests.cs:413-436` — `TitleObservation_GatedByConsentAndCapability_TitleNeverLogged`. `:416-417` register the real capability and run `CapabilityProbeRunner`, so it inherits the same precondition indirectly. `:428` is `Assert.IsType<AiTitleObservation.Observed>(observation)` — **the second failing line.** Its consent arms (`:421-423`) do NOT depend on the session and must keep executing.
- `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs:311-333` — `Probe`. On Windows, no foreground window yields `Unavailable` with `NoForegroundWindowCode`; Linux yields `LinuxUnprobedCode`. **Two different codes: do not collapse them.**
- `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs:295` — `NoForegroundWindowCode = "no-foreground-window"`.
- `client/src/CcpClient.Desktop/Ai/AiAwarenessService.cs:582` — `ObserveForegroundTitle` returns `AiTitleObservation.Unavailable` carrying that **same** code, which is how the second fact identifies the same precondition.
- `client/tests/floor/floor.json` — the orchestrator has **already pinned both fully-qualified names** in `allowedSkips`, with their machine class recorded in `allowedSkipsMachineClasses`. The pin is `may-skip, not must-skip`: a listed test that runs and passes where its precondition holds is green. **So your change makes the gate green on a locked session and leaves it green on an interactive one.**

## EVIDENCE THIS IS REAL, NOT THEORETICAL

Wave 31 landed at `ee6c9164` gating **FLOOR OK 1028/1028 three consecutive times**. About an hour later, the same SHA gated **1026 passed / 2 failed** — these two. Re-running just these two, at that same commit, **in the very worktree that had been green**, reproduces the failure. The tree did not change; the session stopped being interactive.

## THE VACUITY TRAP, NAMED AT AUTHORING

An unconditional skip would make the gate green and silently delete two facts. That is the vacuous-green class this project has closed three times, and it is the single thing review will hunt here.

**The skip must be conditioned on the product's own typed answer, never on the OS, never on an environment variable, never on a config flag, and never on `Debugger.IsAttached` or a CI variable.** Concretely: on Windows, when the probe returns `Unavailable` **whose reason code is exactly `NoForegroundWindowCode`**, skip. An `Unavailable` with any OTHER code must still FAIL — that would be a real regression and this packet must not hide it.

## File Scope

| | |
|---|---|
| May change | `client/tests/CcpClient.Tests/AiAwarenessTests.cs`, `spine-tasks/SP-082-interactive-session-title-skips/**` |
| Must not change | everything else, and specifically `client/src/**` (the product is correct — if you believe it is not, STOP and report), `client/tests/floor/floor.json`, `client/docs/task-board.md` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-082-interactive-session-title-skips/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/AiAwarenessTests.cs` |
| fileScopeMustNotChange | `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/src/**`, `ConditioningControlPanel/**`, `client/docs/**`, `docs/constitution.md`, `.spine/**`, `.pi/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-082-interactive-session-title-skips/record.md`, `spine-tasks/SP-082-interactive-session-title-skips/floor-delta.json` |

**Declare `{"packet":"SP-082-interactive-session-title-skips","unit":0,"headless":0,"reason":"..."}`.** No test is added or removed; a skipped test still counts in `total`, so the count stays 1028.

## Review Level: 2 (Plan, Code)

## Steps

1. **Reproduce first.** Run the gate. Record the exact failure output for both facts. If they do NOT fail — i.e. someone is at the desk and a foreground window exists — say so plainly in `record.md`, because it changes what your later evidence can prove, and proceed anyway.
2. **Make the skip conditional on the typed reason**, per the vacuity trap above. Use `Assert.Skip` (the SP-066 conversion pattern already used in this suite — read one of the five existing conversions and match its shape and its in-file comment style). Name the precondition in the skip message so a reader of a TRX knows why.
3. **Keep every session-independent assertion executing.** In the second fact this includes the whole `ConsentNotGiven` arm and the Linux arm. Do not wrap the entire test body in the skip.
4. **Prove it bites, both ways.** (a) With your change in place the gate must be GREEN with exactly the pinned skip set. (b) Revert only your skip condition and show the two failures return. (c) Force the skip condition to be unconditionally true and show that the suite is still green — then say so in `record.md` as the honest limit, because that is the shape a future defect would take and the pin cannot catch it.
5. **Name the manual gate.** On this machine tonight the interactive arm CANNOT be exercised — there is no foreground window. So the assertions you preserve are, tonight, unexecuted. `record.md` must say that in those words and name the gate: *on an interactive Windows desktop these two must EXECUTE and PASS, not skip.* Do not claim you verified the interactive arm.

## Completion Criteria

- The gate exits 0 with the skip set exactly matching `allowedSkips`, and `total` still 1028.
- The skip is conditioned on `NoForegroundWindowCode` and on nothing else.
- An `Unavailable` with a different reason code still fails, and `record.md` shows you checked that.
- `record.md` carries the reproduction, the two-way bite evidence, the unconditional-skip honesty limit, and the named interactive manual gate.

## Do NOT

- Change any file under `client/src/**`. The product is right.
- Edit `client/tests/floor/floor.json`. The names are already pinned; the pin is the orchestrator's alone.
- Skip on `OperatingSystem.IsWindows()`, an env var, a CI flag, or `Debugger.IsAttached`.
- Delete, weaken, or `[Fact(Skip=...)]` either test outright — an always-skipped test is the vacuous green this packet exists to prevent.
- Widen the skip to swallow an `Unavailable` carrying any other reason code.

## Git Commit Convention

Conventional commit, `fix(SP-082): ...`. Create `.DONE` as your last action and do NOT commit it.

## Documentation Requirements

`record.md` only. Do not edit `client/docs/**`; the orchestrator writes the board and the digest at land.
