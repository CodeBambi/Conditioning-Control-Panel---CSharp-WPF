# Lane plan — scripted session, PAUSE

## The packet's premise is stale; corrected from source

The packet says "still nothing constructs a `ScriptedSessionRun`". That is slice 2's remainder.
Slice 3 landed 2026-08-24 and closed it. Evidence:

- `client/src/CcpClient.Desktop/Session/SessionParticipant.cs:620` — `Scripted = new ScriptedSessionRun(...)`.
- `client/src/CcpClient.Desktop/Lifecycle/CompositionRoot.cs:275` — builds the participant.
- `client/src/CcpClient.Desktop/App.axaml.cs:102` — builds `MainWindow`.
- `client/src/CcpClient.Desktop/Views/MainWindow.axaml.cs:112,156` — resolves the participant off
  the host (throws if absent) and hands it to `StudioPage`.
- `client/src/CcpClient.Desktop/Views/Pages/StudioPage.axaml.cs:473-498` — the rack, the one
  START/STOP button, the confirmation strip.
- `client/tests/CcpClient.HeadlessTests/SessionRackHeadlessTests.cs` — eleven facts that boot a real
  `MainWindow`, open the Studio door, click START, confirm, and run the session out.

So reachability is already pinned by facts that fail if the door is removed. Nothing to redo.

## What the code actually needs

The absence notice the surface paints (`Views/Pages/SessionRackNotices.cs:79-82`) is the honest
remaining list: the editor, custom/imported sessions, rack filter/sort/search, **pause**, and the
XP award.

- The **XP award** is blocked by a landed, argued refusal: this port has no app-lifetime ledger
  (`Session/SessionParticipant.cs:502-517` — a session-lifetime fourth store over `progression.json`
  would be a stale second writer across the three modal hosts). Re-opening that is its own row.
- The editor / custom sessions / filter-sort-search are new UI surfaces, not door-to-engine wiring.
- **PAUSE is the remaining door-to-engine gap**, and it is what this lane ships.

## Deliverable

1. `ScriptedSessionRun.Pause()` / `Resume()` — upstream `Services/Session/SessionEngine.cs:432-466`
   and `:470-502`, with the elapsed banked BEFORE the flag (`:436-438`) and the monotonic banked the
   way `Stopwatch.Stop()/Start()` banks it. `Tick()` refuses while paused (`:506`).
2. `ScriptedSessionOutcome.PauseCount` + `XpPenalty` — upstream `:2013-2014`. Recorded, not banked,
   because nothing here can bank it.
3. `SessionRackNotices` — the pause confirmation naming the cost (`en.json:3387-3389`), the
   `[PAUSED]` indicator (`MainWindow.Presets.cs:1759`, `en.json:3180`).
4. `StudioPage` — one PAUSE/RESUME button, visible only while running (`:1809`, `:1855`), pause
   confirmed and resume not (`:1919-1940`), through the existing confirm strip.
5. Facts: unit on the run, headless through the real `MainWindow` so the pause door is pinned the
   same way the start door is.
