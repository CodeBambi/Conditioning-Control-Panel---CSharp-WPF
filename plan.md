# POP QUIZ (census #39) — plan

## Upstream shape, read before writing anything

698 lines / 3 files: `Services/Quiz/PopQuizService.cs` (284),
`Windows/PopQuizWindow.xaml` (129) + `.xaml.cs` (285).

- **Started from the ORDINARY engine** — `MainWindow/MainWindow.StartStop.cs:255-258` (and again,
  redundantly, at `:261-264`), stopped at `:344`, its window force-closed by panic at `:373`. The
  scripted engine also starts it (`Services/Session/SessionEngine.cs:490`, `:1407-1414`) but the
  dials it reads are the USER-level ones, not the session's — `:1406`, *"Pop quiz is a user-level
  toggle (AppSettings), not per-session"*. The packet's reading is confirmed.
- **Pacing**: `60.0 / perHour` minutes, jittered ±30 %, `roll * (max - min) + min`
  (`PopQuizService.cs:113-122`, recomputed every tick at `:163-171`). Its own header says it
  "Follows the same scheduling pattern as LockCardService" (`:11`). No first-card offset, unlike
  Lock Card.
- **Dials**: `PopQuizEnabled` ships false (`Models/AppSettings.cs:3575`), `PopQuizFrequency`
  default 2 clamped `Math.Clamp(value, 1, 100)` (`:3586`). The comment on `:3582` says "(1-10)" and
  is STALE — the slider is `Minimum="1" Maximum="100"` (`Views/Tabs/GradedIntakeTabView.xaml:286`).
- **Source**: 25 hardcoded questions, each 4 answers + 4 affirmations
  (`PopQuizService.cs:23-100`), drawn uniformly (`:242`). Not user-editable, no mod hook.
- **Presentation**: 500x420 ownerless topmost window, centred, answers SHUFFLED into display slots
  by Fisher-Yates (`PopQuizWindow.xaml.cs:54-60`), slot-to-original index kept on `.Tag`
  (`:122-125`), ESC skips (`:128-134`).
- **Answer path**: click a slot, then highlight, chime, XP, `Task.Delay(300)`, show the affirmation
  for the ORIGINAL index (`:170-173`), `Task.Delay(1500)`, close (`:176-177`).
- **Every answer is correct** (`PopQuizService.cs:12`).
- **Single tenancy**: refuses a second quiz (`:183-187`) and cross-checks the lock card (`:194`),
  deferring through the interaction queue - and where there is NO queue it DROPS, upstream's own
  branch at `:212-215`.

## Where it goes, and why

`client/src/CcpClient.Desktop/Effects/` - it is a paced rack module on the ordinary engine's
effect rack (start/stop/panic at the three StartStop sites above), and its own source says it is
Lock Card's scheduler with different content. `Features/` holds host windows and standalone
subsystems (Intake, Dtrh, Arcademy); this is not one.

Files (all new):
- `Effects/PopQuizSchedule.cs` - the interval law and the clamps.
- `Effects/PopQuizQuestion.cs` - the record and the 25-question pool.
- `Effects/PopQuizAsk.cs` - one asked question: the shuffle, the key mapping, the affirmation.
- `Effects/PopQuizPresetDocument.cs` - `Enabled` + `PerHour`. Sibling preset documents live under
  `Session/`; this one sits beside its module because `Session/` is outside this packet's File
  Scope. Reported, not silent.
- `Effects/PopQuizEffect.cs` - the module, `PacedSessionEffect<PopQuizFiring>`.
- `tests/CcpClient.Tests/PopQuizModuleTests.cs` - the facts.

## What is refused, with the named reason at the call site

- **The chime** (`PopQuizWindow.xaml.cs:192-211`): its file is a shipped app resource
  (`Resources/sounds/chime{1,2,3}.mp3`) this port does not ship, and its volume is
  `pow(MasterVolume/100 * 0.5, 1.5)` (`:203`) against an app-wide master volume this port does not
  have at all (`Effects/BrainDrainEffect.cs:63`). Refused with the formula cited so a later lane
  has the number.
- **The mouse**: the port's input capability delivers keystrokes, not clicks, so the four slots are
  keys `1`-`4`. Going through the pointer capability instead would put a SECOND window in front of
  the user that is not mutually exclusive with the Lock Card, losing upstream's own #763 guard.
- **The interaction queue**: absent here as it is for Lock Card and Bubble Count. Upstream's own
  no-queue answer (`:212-215`) is Drop, and the shared `IInputPresence` covers both of upstream's
  guards in one read.
- **The Test button and the "quiz me" voice command** (`MainWindow.Lab.cs:646-649`,
  `AutonomyService.VoiceCommands.cs:431`): both need a panel or a voice capability, both out of
  scope. Upstream withholds XP on the test path (`:157`); nothing here has a test path to withhold
  it on.

## What is NOT refused, against the packet's own expectation

The packet says XP has nowhere to land. **Source disagrees**: `Features/Progression/ProgressionLedger`
banks XP today from three call sites (`ArcademySession.cs:497`, `DtrhMeta.cs:833`,
`IntakeHostWindow.axaml.cs:547`). Upstream's 25 (`PopQuizWindow.xaml.cs:161`) therefore goes to the
real ledger through an optional `ProgressionLedger?`, the same optional shape those callers use. No
number is invented; with no ledger, nothing is banked and the card makes no XP claim.

## Surface

There is none, and there cannot be one inside this File Scope: racking the module means editing
`Session/SessionParticipant.cs` and giving it a panel means editing `Views/Pages/StudioPage.*`,
which the packet forbids. Module + facts ship; the rack row, the panel and the headed gate are the
named remaining work. No visual claim is made, so no headed evidence is owed.
