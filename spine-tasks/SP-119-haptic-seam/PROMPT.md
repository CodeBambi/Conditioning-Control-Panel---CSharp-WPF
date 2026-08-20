# SP-119 — The haptic seam, built up to the decision that is not mine

## Mission

Fourteen of fifteen rack rows are ported and **the session effect spine is complete**. Haptics is the last row, and SP-117's census named **four** blockers:

1. a **seventh capability folder**
2. a **NuGet dependency** the csproj does not carry (`client/src/CcpClient.Desktop/CcpClient.Desktop.csproj:24-42`)
3. **app-scope wiring in `Lifecycle/**`** — it outlives any session
4. a **premium gate** through `Entitlement/**` (`ConditioningControlPanel/MainWindow/MainWindow.Haptics.cs:484-503`)

**Only #2 is the owner's to decide.** This packet builds **1, 3 and 4** and stops cleanly at the decision.

Your outcome: **a typed haptic capability that refuses honestly because no provider is admitted — wired app-scope, premium-gated, with the dependency decision's exact cost stated.**

## THE CENTRAL TRAP: a refusal that hides a design that would not have worked

Six capabilities refuse honestly on Linux, and each earned that refusal by **building the Windows half first**. You cannot. **So the risk is a seam shaped around a provider nobody has integrated** — an interface that looks right and fits neither Buttplug nor Lovense.

Defend against it with **evidence from both**, not one: `ConditioningControlPanel/Services/Haptics/IHapticProvider.cs:8-29` is upstream's own contract, `ButtplugProvider.cs` and `LovenseProvider.cs` its two implementations, and **both are clients of a separate server process** — Buttplug over `ws://127.0.0.1:12345`, Lovense over `http://127.0.0.1:20010`. **Show your seam against both**, and say what each would need.

## THE OTHER TRAPS

### 1. D179 is the reason this row exists — do not close it by accident
The thirteen effect modules are **silent to this sink**: `FlashService.cs:1453/1480/1516/1915`, `VideoService.cs:2580/4585/6580`, `SubliminalService.cs:230`. **`Effects/**` is CLOSED.** Giving the modules a haptic limb is a **later** packet; this one builds the capability they will call. **Say so plainly** so nobody reads a landed capability as a working feature.

### 2. `Unavailable` must name the *admitted-provider* gap, not a missing toy
SP-117 measured no listener on 12345/20010/30010. **That is not the blocker** — the four above are. A refusal saying "no device found" would be **false**: the port has no client to look with.

### 3. The premium gate is an entitlement surface and those are owner-held
`Entitlement/**` carries the port's honest-refusal pattern: `Available` / `NotEntitled` / `Unavailable(reason)`, and **"I could not tell" must never render as "you are not a patron."** Two doors already refuse every user pending the owner's bearer decision. **Do not widen that; consume it.**

### 4. Standing rules
Equivalence claims inadmissible until every consumer is enumerated by `grep`. A tolerance is the size of the defect it hides. **Anything compiled but never executed is UNEXECUTED** — SP-118's D188 came from exactly that, twice now.

### 5. No wall-clock waits, and run both gates alone

## File Scope

| | |
|---|---|
| May change | `client/src/CcpClient.Desktop/Haptics/**` (new), `client/src/CcpClient.Desktop/Lifecycle/**`, `client/src/CcpClient.Desktop/Session/**`, `client/src/CcpClient.Desktop/Views/**`, `client/tests/**`, `client/docs/wpf-surface-reachability.md` (divergences ONLY), `client/docs/verification-harness.md` (haptic evidence class ONLY), and `spine-tasks/SP-119-haptic-seam/**` |
| Must not change | everything else, and specifically `client/src/CcpClient.Desktop/{Overlay,Input,Audio,Video,Pointer,Glyph,Effects,Entitlement,Scheduling}/**`, `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj`, `client/tests/floor/floor.json`, both floor scripts, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |

**The csproj is CLOSED. If you find yourself needing the package, stop — that is the decision this packet exists to reach, not to make.**

## Contract

| Field | Value |
|---|---|
| testCommand | `node client/tests/floor/check-floor.mjs` |
| floorDelta | `spine-tasks/SP-119-haptic-seam/floor-delta.json` |
| fileScopeMustChange | `client/src/CcpClient.Desktop/Haptics` |
| fileScopeMustNotChange | `client/src/CcpClient.Desktop/Overlay/**`, `client/src/CcpClient.Desktop/Input/**`, `client/src/CcpClient.Desktop/Audio/**`, `client/src/CcpClient.Desktop/Video/**`, `client/src/CcpClient.Desktop/Pointer/**`, `client/src/CcpClient.Desktop/Glyph/**`, `client/src/CcpClient.Desktop/Effects/**`, `client/src/CcpClient.Desktop/Entitlement/**`, `client/src/CcpClient.Desktop/CcpClient.Desktop.csproj`, `client/tests/floor/floor.json`, `client/docs/task-board.md`, `client/tools/**`, `ConditioningControlPanel/**`, `docs/constitution.md`, `.spine/**`, `.claude/**` |
| artifactsMustExist | `spine-tasks/SP-119-haptic-seam/record.md`, `spine-tasks/SP-119-haptic-seam/floor-delta.json` |

**Pin: 2191 unit / 133 headless.** `sum-deltas` before deleting any delta file. **Keep every artifact inside your worktree.**

## Review Level: 3 (Plan, Code, Final)

## Steps

1. **Plan checkpoint:** the seam **shown against both providers** with citations; your app-scope ownership point; how the premium gate consumes `Entitlement/**` without widening it; and **exactly what the dependency decision would cost** — one file, or a redesign.
2. Build the capability. `Unavailable` **earned and honest about which gap**.
3. Wire it app-scope; premium-gate it.
4. Rack row with a truthful dot, or a recorded reason it has none.
5. **Prove it bites**; sweep every predicate; discharge or withdraw every equivalence claim.
6. Record the haptic evidence class; divergences from D191.

## Completion Criteria

- A typed capability refusing with the **admitted-provider** gap named, not a missing device.
- App-scope ownership following `CompositionRoot`'s discipline.
- Premium gate consuming `Entitlement/**` unchanged.
- The dependency decision's cost stated precisely enough for the owner to answer it.
- Fourteen landed rows' facts pass unchanged; both gates green.

## Do NOT

- Add the NuGet package or edit the csproj.
- Give any effect a haptic limb — `Effects/**` is closed.
- Refuse with "no device found."
- Ship a seam justified against only one provider.

## Git Commit Convention

Conventional commit, `feat(SP-119): ...`. Create `.DONE` last; do NOT commit it.

## Documentation Requirements

`record.md` with the two-provider seam justification, the dependency decision's exact cost, and the sweep; divergences in `client/docs/wpf-surface-reachability.md`; the haptic evidence class in `client/docs/verification-harness.md`.
