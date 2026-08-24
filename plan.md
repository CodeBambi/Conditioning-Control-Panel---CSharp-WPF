# Packet: typed mantra minigame (census #108)

## Upstream shape (read in full)

821 lines exactly, three files:

- `ConditioningControlPanel/Services/MantraService.cs` (154) — the state machine.
- `ConditioningControlPanel/Windows/MantraWindow.xaml` (194) + `.xaml.cs` (473) — the game surface.

Findings that change the port:

1. **Upstream's window HAS NO CALLER.** `MainWindow/MainWindow.PlayTab.cs:262` says so in
   capitals: the Play page's Mantras card came off in the 2026-08-12 relayout and
   `StartMantraSession(int)` (`:287`) is kept only so the knowledge of the
   `StartSession(n)`-before-`Show()` order is not lost. So the census's "what the user loses"
   overstates: the shipping product has already lost it.
2. **No microphone anywhere in these 821 lines.** `MantraEntry`/`MantraVoiceService` are the
   SPOKEN mantra path (`AutonomyService.cs:1882-1990`) and are owner-blocked; the only touchpoint
   is `MantraService.CreditExternalMantra` (`:46-60`), which is not in this packet.
3. **The audio is synthesised.** `MantraWindow.xaml.cs:362-438` builds NAudio `SignalGenerator`
   oscillators (90 Hz + 180 Hz drone, 400+streak*20 / 200 / 523 Hz tones). This build's audio seam
   is `Audio/IAudioPresence.cs:52` `AudioCue(Slot, Path, Volume)` — file paths only, no oscillator,
   and `Audio/**` is out of scope. REFUSE, named at the call site.
4. **The anti-cheat is mechanical + rate-limited.** `MantraWindow.xaml.cs:46-52` cancels paste,
   kills the undo stack, and `:239` shares `LockCardWindow.IsBlockedInputGesture`. The port's lock
   card already answered this by having no edit control at all (`Effects/LockCardTyping.cs:31-40`),
   which removes the clipboard, undo and context menu structurally. Same door here.

## Where it goes and why

NEW feature area `client/src/CcpClient.Desktop/Features/Mantra/`. Upstream's pair is a plain
service plus a window opened from a Play-tab card; nothing in `SessionEngine` drives it, so it is
not an `Effects/` module, and it is not a page. `Features/<name>/` with its own launch type is the
shape `Features/Goon`, `Features/Arcademy` and `Features/Dtrh` already use for exactly this.

- `MantraSession.cs` — ported `MantraService` + the typing rules. Injected `Func<DateTimeOffset>`
  and `Random`. Grants XP itself, at upstream's own call site.
- `MantraIntensity.cs` — the pure streak -> visual numbers (`xaml.cs:310-360`).
- `MantraWindow.axaml(.cs)` — the surface. No `TextBox`: characters arrive on `TextInput`,
  Backspace/Escape on `KeyDown`.
- `MantraLaunch.cs` — opens the ledger for the window's life, shows it, disposes on close, and
  carries upstream's "already typing? focus it" guard (`MainWindow.PlayTab.cs:294-303`).

## XP decision

`ProgressionLedger.Open`'s remarks (`:180-185`): "ONE file per install, opened by more than one
host, each with its own uniquely-named registry owner. The three hosts that call this are modal
windows the user opens one at a time." A typed-mantra window is that shape — opened by a door,
owned by the window, disposed with it — unlike `PopQuizEffect`, which refused a fourth store
because it is session-lifetime (`Session/SessionParticipant.cs:481-489`). So: OPEN, owner name
`MantraProgression`, held by `MantraLaunch` for one window's life.

## Refusals (named, at the call site)

- The drone and the three tones — no oscillator in this build's audio seam (see 3 above).
- No persisted mantra pool: `Storage/**` is out of scope and there is no editor surface to feed
  one, so the pool is a constructor argument with upstream's five defaults
  (`Models/AppSettings.cs:6317-6323`) as the built-in.
- No storyboard animation (pulse/shake/glow): unverifiable in this packet, which has no headed
  gate available.

## Surface

BLOCKED. The door belongs on `Views/Pages/PlayPage.axaml.cs` — upstream's own home for it — and
`Views/Pages/**` is excluded from this packet's file scope. So the window ships unreachable, which
is also upstream's current state (finding 1), and re-homing it costs exactly one `MantraLaunch.Open`
call. No headed evidence is therefore possible: `client/tools/verify/capture.ps1` drives the real
app through real input and cannot reach a surface with no door.
