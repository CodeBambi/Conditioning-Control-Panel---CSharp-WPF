# Slice 3 of the scripted session: GIVE IT A SURFACE

Consuming (not changing) `Session/{ScriptedSession,ScriptedClock,ScriptedSessionDials,
ScriptedSessionRun,ScriptedSessionRamp}.cs` and `tests/CcpClient.Tests/ScriptedSessionTests.cs`.

## The door: STUDIO, and upstream's own door map says so

The brief's design decision is CORROBORATED rather than merely allowed. Upstream's
`MainWindow/MainWindow.TabNavigation.cs:592-593` declares
`("studio", "studio", new[] { "studio", "presets", "haptics" })` — the `presets` tab, which is
where `SessionRackPanel` lives (`Views/Tabs/PresetsTabView.xaml:857`), is inside the STUDIO door's
entry list. `play` is a different door row entirely (`:600-601`). The port already folds a second
Studio-door tab (`haptics`) into the rack as a row, so a third one folds the same way.

## What lands

1. `Views/Pages/SessionRackNotices.cs` (new): every sentence this surface says, as pure functions
   — the row line, the start confirm (`MainWindow.Presets.cs:1465-1471`), the stop confirm
   (`:1893-1899`), the live readout. Pure, so its facts are unit facts, not headless ones.
2. `Views/Pages/StudioPage.axaml` + `.cs`: a `SESSIONS` rack group with one row
   (`RowScriptedSession`) and its module panel — four built-in rows built from
   `ScriptedSession.ReadBuiltIns()`, selection, one START button that doubles as STOP
   (`MainWindow.Presets.cs:1455-1459`), an inline confirm strip, and a live readout driven by
   `ProgressUpdated` off ONE `ReadProgress()`.
3. `Session/SessionParticipant.cs`: `public ScriptedSessionRun Scripted { get; }` composed against
   the real engine, the real eleven stores, the real ramp document and the real clock; plus the
   teardown restore at the head of `FlushAsync` — without it a session running at app exit would
   have its dials flushed OVER the user's, which is upstream defect #471's shape and would break
   the promise the confirm dialog makes.
4. `Lifecycle/CompositionRoot.cs`: `ScriptedClockFactory`, the `ScheduleClockFactory` seam verbatim
   so headless facts drive session time by hand.

## Evidence

- unit facts on the notices + the composed run + the teardown restore;
- headless facts driving REAL input on the REAL shell from a cold composition-root boot;
- headed: new `session-rack` surface in `capture.ps1` with states `idle`/`running`, UIA-gated on
  the panel's own text before any pixel, plus two pixel checks in `checks.json` and the inversion
  that shows each fails on the other state's capture.

## Out, recorded as out

Session editor, custom/imported sessions, rack filter/sort/search, Session Complete recap, session
history, media log, pause and its 100-XP penalty, the XP award (so no `+N XP` on the row — a
promise this build cannot keep), the corner-GIF window.
