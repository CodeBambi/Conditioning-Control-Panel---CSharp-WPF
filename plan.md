# Lane plan — scripted session rack: filter, sort, search

## What is reachable today (verified, not assumed)
- `Lifecycle/CompositionRoot.cs` builds the participant; `Session/SessionParticipant.cs` builds the
  run; `Views/MainWindow.axaml.cs` resolves it and hands it to `StudioPage`.
- `StudioPage.BuildScriptedSessionRack()` builds four rows once from `ScriptedSession.ReadBuiltIns()`.
- `SessionRackHeadlessTests` boots a real MainWindow, clicks DoorStudio -> RowScriptedSession and
  pins the four rows in file-name order.
- So: start / stop / pause / recap / history all reach a user. The rack has NO toolbar.

## Chosen: filter + sort + search (the cheapest of the four named gaps, done whole)
Left deliberately: the session editor (largest, a whole window), custom/imported sessions (needs a
data-root folder decision + a source model outside this scope), the corner-GIF window, the XP award
(landed argued refusal).

## Build
1. `Session/ScriptedSessionRack.cs` — pure filter+sort+search over `IReadOnlyList<ScriptedSession>`,
   upstream `MainWindow/MainWindow.SessionIO.cs:274-352`; labels/count/empty lines from `en.json:61-76`.
2. `Views/Pages/StudioPage.axaml` — toolbar above `ScriptedSessionRackPanel`: count, four difficulty
   checkboxes, sort ComboBox (filled in code), search TextBox with a native Watermark.
3. `Views/Pages/StudioPage.axaml.cs` — `BuildScriptedSessionRack` becomes an idempotent repaint
   (upstream's `RepaintSessionRack`), selection preserved across it.
4. Amend the three absence claims that would otherwise lie on screen and in source.

## Recorded divergences
- `recent` and `xp` sorts omitted (nothing creates a session file here, and the row deliberately
  shows no XP cell).
- Source chips omitted: this build has no custom or imported sessions to filter by.
- Nothing persists: upstream persists source+sort in AppSettings; a new store for one token is not
  justified here.

## Evidence
- Unit facts for the pure function; headless facts driving real input through a real MainWindow.
- Headed: a NEW capture surface would need `client/tools/verify/capture.ps1` + `checks.json`, which
  this packet says to coordinate rather than edit. Plan: run the EXISTING session-row /
  session-start captures as a regression, and report the filter states as pixel-UNPROVEN.
