# Lane plan — AI effect bridge + permissions grid

Checkpoint file. Removed before the final report (per the packet).

## What the source says that the packet does not

1. **The packet's dispatch story is one bridge short.** The packet says "a user can ask the
   companion for a spiral and the validator will accept it, the executor will route it, and nothing
   will happen." The validator is never reached: `Ai/AiOperationPipeline.cs:340-346` refuses any
   reply that looks like an envelope with `AiReplyCodes.MalformedOutput`, and
   `Ai/AiTextHygiene.cs:24-30` records that as a decision — "`AiEnvelopeValidator` stays unwired".
   Grep confirms zero product callers of `AiEnvelopeValidator` / `AiCommandExecutor.Execute`.
   So there are TWO gaps: reply→validator (absent, deliberately refused) and plan→effects (the
   empty handler map). This slice closes the second. The first crosses a recorded refusal plus a
   prompt contract that does not exist; not a lane decision.

2. **`CompanionPage` cannot host the grid.** Its only constructor is
   `CompanionPage(Action showCompanion)` (`Views/Pages/CompanionPage.axaml.cs:11`) and its only
   construction site is `Views/MainWindow.axaml.cs:139` — outside File Scope. The companion
   surface that already owns every companion consent (awareness consent, memory consent, cooldowns,
   the honesty line) is `Features/Companion/CompanionWindow.axaml:128-181`, which resolves the
   participant from the host itself. Grid goes there; the door gets a pointer line.

3. **The executor's handler map needs the rack, and the rack arrives one line outside scope.**
   `Lifecycle/CompositionRoot.cs:263` builds `session` and `:295` builds the companion. One added
   argument connects them. Not mine to make: reported, not improvised.

## Plan

- `Ai/AiEffectBridge.cs` — `IAiEffectHandler` implementations over the LANDED `ISessionEffect`
  rack, plus the named-absence table for every kind with no counterpart in this build.
  A kind is registered ONLY when every field of its command data can be honoured.
  - HANDLERS: Spiral, Pink, Bubbles.
  - NAMED ABSENCES: FlashImage, Subliminal, MantraLockscreen, Bounce, Haptic, Video, Audio,
    GetBackToMe.
- `Ai/AiEffectPermissions.cs` — the typed session-scoped gate state, default `NoneAdmitted`,
  WPF's TEN rows (overlay covers spiral+pink, `AiCommandService.cs:186-187`).
- `Features/Companion/CompanionParticipant.cs` — optional `effects` ctor arg → real handler map;
  `Permissions` property beside `MemoryConsent`.
- `Features/Companion/CompanionViewModel.cs` — grid rows.
- `Features/Companion/CompanionWindow.axaml` — the grid.
- `Views/Pages/CompanionPage.axaml` — one pointer line.
- `Ai/AiCommandExecutor.cs:15` — correct the stale "NO effect backends exist" claim.
- Tests: unit (bridge against REAL effect objects, default-closed, absence completeness),
  headless (grid renders, default closed, a click changes it).
- Headed: new additive surface in `client/tools/verify/capture.ps1` + checks in
  `client/tools/verify/checks.json` (the packet names both as my harness).
