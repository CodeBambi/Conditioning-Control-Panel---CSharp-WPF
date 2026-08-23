# Checkpoint plan — rack Pop Quiz, and route the tier refusal

Scratch file. Removed before the packet reports complete.

## Item 1 — rack Pop Quiz

- `Session/SessionParticipant.cs`: open `session_popquiz.json` beside the other per-module
  documents; construct `PopQuizEffect` against the ONE shared `IInputPresence`; add it to
  `SessionEngine`'s array **between Brain Drain and the ramp**, which is upstream's `StartEngine`
  order (`MainWindow/MainWindow.StartStop.cs:255-258`, after Brain Drain `:241-244`, before the ramp
  `:265-269`) and is UNOPPOSED because upstream has no Studio rack row for this module at all.
- XP: **no ledger**. `ProgressionLedger.Open` is scoped to one-at-a-time modal hosts; a
  session-lifetime second store over `progression.json` would write a stale document over a DTRH or
  intake grant. Racked with `xp: null`, which the module already renders honestly (`BanksXp`).
- `Views/Pages/StudioPage.*`: row in GAMES & CARDS after Lock Card; panel with the two dials, the
  interruption notice, the live line and the capability's own answer.
- `Views/Pages/PopQuizPanelNotices.cs`: the panel's words.
- No Test button: upstream's is `PopQuizService.TestPopQuiz()` -> `ShowPopQuiz(isTest: true)`
  (`MainWindow/MainWindow.Lab.cs:646-649`), and porting it needs a public fire-now on
  `PopQuizEffect`, which is outside this packet's file scope.

## The lock-rule conflict — resolved from source

Both cited facts are true and they do not conflict once PRESCRIPTION is separated from CUSTODY.

- `SessionEngine.cs:1406` is about prescription: the scripted engine reads the USER-level toggle,
  never the per-session `SessionSettings.PopQuizEnabled` (`Models/Session.cs:913-916`, which nothing
  reads).
- `GradedIntakeTabView.xaml:269,286` are about custody: upstream's run SNAPSHOTS both pop quiz
  values at `SessionEngine.cs:919-920` and writes them back at `:1544-1545`, so a mid-session edit
  is silently discarded. That is the harm the lock prevents, and it is exactly the port's own
  criterion.
- In THIS port the pop quiz document is not one of the eleven `ScriptedSessionDials` borrows, so no
  custody is taken and nothing is discarded. The dials stay LIVE, exactly as Brain Drain, the ramp
  sliders, the scheduler and haptics already do — all four also carry upstream `Owned` markers the
  port deliberately does not mirror. The rule needs no new clause; it needs the distinction written
  down, which goes into `SessionOwnedMarker`'s doc comment.

## Item 2 — the tier refusal

Route: upstream's action opens App Info & Data, which this port does not have. NOT built.
What IS built is upstream's own lesson, in its own words at `MainWindow/MainWindow.Lab.cs:282-288`:
"Say no out loud … a bare call here teleported a free account off the Play door with no dialog, no
toast and nothing tying the jump to the card they clicked." The refusal now also speaks through the
shell toast at upstream's severity and timing (`Services/TierGate.cs:133`: Warning, 8 s), carrying
the decision's own message — which already names the route in words (`DtrhGate.UpgradeRoute`) and
admits the gap. No action button: `ToastHost` has none and its destination does not exist.

SCOPE DISCOVERY: the one wiring line is `Views/MainWindow.axaml.cs:140`, outside the packet's File
Scope. Reported.

## Headed evidence

- `toast -State gated`: the refusal toast on a real desktop, Warning accent `#FFB347`, a third
  colour neither existing toast check can accept.
- `popquiz-card -State asking`: the real card, gated on the panel's UIA text and the capability's
  own ink read-back before any pixel.
