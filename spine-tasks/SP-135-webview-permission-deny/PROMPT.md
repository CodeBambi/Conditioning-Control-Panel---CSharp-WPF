# SP-135 — Close D250: deny the WebView2 permission prompt the port cannot currently refuse

## Mission

**D250 is the port's one open capability residual that needs no owner decision, because closing it
only TIGHTENS.**

The finding, from SP-130 and confirmed at the wave-65 land after a blind audit caught the port
overstating it: **the host grants nothing and can deny nothing.** Upstream grants the microphone
unprompted (`GoonHostService.cs:489` answers `Microphone` with `Allow`, `:493` suppresses the
browser's own prompt). This port has **no `PermissionRequested` handler at all**, because Avalonia
exposes `CoreWebView2` only as a raw pointer.

**So the residual is WebView2's own prompt.** The Goon voice screen is reachable from the title menu
and its recorder asks the browser for the microphone directly (`ui/voice/recorder.js:591`), so **a
user who walks the page's own double-locked opt-in can still be asked by the browser** — and this host
can neither grant nor refuse on their behalf.

Your outcome: **a `PermissionRequested` handler that DENIES, so the prompt never reaches the user and
the port's stated posture becomes a mechanism instead of an argument.**

## WHY THIS NEEDS NO OWNER DECISION

`client/docs/capability-inventory.md:70` gates **expanding** sensors, derived data, persistence,
networking, diagnostics or telemetry behind owner review. **This expands nothing.** It removes a path
by which a capture device could be opened. Denying is strictly narrower than today's behaviour, and
narrower still than upstream's, which grants unprompted.

**If you find any reading under which this widens something, STOP and report it.** That would be a
finding worth more than the packet.

## THE HARD PART, AND IT IS INTEROP

`IWindowsWebView2PlatformHandle.CoreWebView2` is an **`IntPtr`**. Every use of it in this tree is
hand-rolled COM vtable interop — `client/src/CcpClient.Desktop/Features/Dtrh/DtrhProcessFailed.cs:26-75`
reaches `add_ProcessFailed` by **vtable slot**, and that file is your model.

**A wrong slot index does not fail loudly — it calls something else.** So:

- **Derive the slot from the published interface definition and cite where you got it.** Do not count
  by eye and do not guess. `DtrhProcessFailed.cs` shows the shape it must take.
- **Prove the handler actually fires** rather than that it compiles. A handler attached at the wrong
  slot compiles perfectly and silently never runs — that is this session's signature defect in the
  one place where the consequence is a microphone.
- **`ProcessFailed` already works in this tree.** If you cannot establish the permission slot with the
  same confidence, **say so and ship nothing** — a wrong vtable call is worse than an open residual.

## THE OTHER TRAPS

### 1. Deny, do not silently allow on error
If the handler cannot be attached, the port must **not** end up quieter about it than before. A failed
attach is a **typed, reported outcome** — D250 stays open and says why. Never a swallow.

### 2. This touches DTRH as well as Goon
Both hosts embed the same WebView2. **Say explicitly which hosts you cover** and do not claim the
others by implication. `client/src/CcpClient.Desktop/Features/Dtrh/` and `Features/Goon/` both exist.

### 3. Evidence class
A compile is not an attach, and an attach is not a denial. **`client/docs/verification-harness.md`
governs.** Reaching the prompt at all requires the voice screen and a real browser — SP-132's headed
harness exists and its capture deliberately walks around the voice screen. **If you cannot demonstrate
a denial, name exactly what you did demonstrate.**

### 4. Do not touch the refusal text
`Features/Goon/GoonDoors.cs`'s VoiceNotes refusal was corrected at the wave-65 land after a blind audit
found it overstated. **If closing D250 makes any sentence in it false, that is a finding you report —
and it probably does**, since it currently says the browser can still ask.

### 5. Standing rules
No wall-clock waits — `TestWait` only. No TODOs. Every guard watched red **at the committed head**,
with the SHA.

### 6. Divergence ids: **D289 onward**

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Features/Dtrh/**`, `client/src/CcpClient.Desktop/Features/Goon/**`, `client/tests/CcpClient.Tests/WebViewPermissionTests.cs` (new), `client/docs/wpf-surface-reachability.md` (divergences ONLY, D289 onward), and `spine-tasks/SP-135-webview-permission-deny/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/Features/Goon/GoonDoors.cs`, `client/tests/CcpClient.Tests/{RealDesktopCollection,VideoOverlayCoexistenceTests,PointerCapabilityTests,InputCapabilityTests,GoonServingTests,GoonPracticeTests}.cs`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-135-webview-permission-deny/floor-delta.json` |
| fileScopeMustChange | `client/tests/CcpClient.Tests/WebViewPermissionTests.cs` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Features/Goon/GoonDoors.cs`, `client/tests/CcpClient.Tests/RealDesktopCollection.cs`, `client/tests/CcpClient.Tests/VideoOverlayCoexistenceTests.cs`, `client/tests/CcpClient.Tests/GoonServingTests.cs`, `client/tests/CcpClient.Tests/GoonPracticeTests.cs`, `client/tools/**`, `client/tests/floor/floor.json`, `client/tests/floor/**`, `client/docs/task-board.md`, `client/docs/capability-inventory.md`, `client/docs/execution-census.md`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-135-webview-permission-deny/record.md`, `spine-tasks/SP-135-webview-permission-deny/plan.md`, `spine-tasks/SP-135-webview-permission-deny/floor-delta.json` |

**Pin: 2573 unit / 152 headless.** `sum-deltas` before deleting any delta file.

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint BEFORE any edit:** where the slot index comes from, **with its source cited**;
   how you will prove the handler fires rather than compiles; which hosts you cover; and what you will
   do if the slot cannot be established with `ProcessFailed`-level confidence.
2. Build the deny handler on the `DtrhProcessFailed.cs:26-75` model.
3. **Demonstrate it fires.** If you cannot, name what you demonstrated instead.
4. Report whether `GoonDoors.cs`'s VoiceNotes text is now false. **Do not edit it.**
5. Divergences **D289 onward**.

## Completion Criteria

- A `PermissionRequested` handler that denies, on the hosts you name.
- The slot index derived from a cited source, never counted by eye.
- Evidence that it **fires**, or an exact statement of what was shown instead.
- A failed attach is typed and reported, never swallowed.
- Both gates green; build 0 warnings / 0 errors.

## Do NOT

- Guess a vtable slot.
- Claim an attach from a compile.
- Swallow a failed attach.
- Edit `GoonDoors.cs`.
- Ship anything if the slot cannot be established — say so instead.
- Use a divergence id at or below D288.

## Git Commit Convention

Conventional commit, `feat(SP-135): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the slot's source, the firing evidence and its class, the hosts covered and not
covered, the failed-attach behaviour, and whether `GoonDoors.cs`'s text is now false.
