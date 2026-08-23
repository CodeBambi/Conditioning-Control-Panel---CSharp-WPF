# Checkpoint plan — Studio door packet (session feature lock, pickers, audio dials)

Worktree: `.claude/worktrees/agent-a8ffef324fb9907e2`, branch `worktree-agent-a8ffef324fb9907e2`.
REMOVE THIS FILE before reporting complete.

## Census of what the source says (read-only pass, done)

### Item 1 — session feature lock
Upstream spec read in full: `MainWindow/MainWindow.SessionFeatureLock.cs` (531) +
`Features/SessionLock.cs` (223) + all 42 `SessionLock.Owned` attribute sites enumerated.

Four rules (`MainWindow.SessionFeatureLock.cs:15-47`):
1. ANY session, not just a program (`:17-28`).
2. DERIVE, NEVER LATCH; fail open (`:30-37`).
3. NEVER a safety control — stop/panic/No-Panic/Strict Lock/withdraw/exit/AUDIO VOLUME (`:39-42`).
4. Dosage locked, comfort not; declared per control (`:44-48`).

Client-side rule that falls out of `Session/ScriptedSessionDials.cs`: a control is owned iff its
document is one of the ELEVEN the run borrows (`:108-121`) AND upstream marks its counterpart
`Owned`. The client restore is a whole-document `Replace` (`:399-417`), so every member of a
borrowed document is discarded at the end — which is why upstream's `Owned` set, not `Apply`'s
write set, is the specification.

LOCK: Flash enable/frequency/images; Visuals scale/opacity/duration; Subliminal enable/frequency;
BouncingText enable/speed/size; PinkFilter enable/opacity; Spiral enable/opacity; BubblePop
enable/frequency/speed; BubbleCount enable/frequency/difficulty; LockCard enable/frequency/repeats;
MindWipe enable/frequency; MandatoryVideo enable/frequency/max-length; Ramp curve picker.

DELIBERATELY LIVE, each with its upstream citation:
- BouncingText opacity — `BouncingTextFeatureControl.xaml:125` unmarked.
- BubblePop size — `BubblePopFeatureControl.xaml:119` unmarked.
- MindWipe volume — `MindWipeFeatureControl.xaml:104` unmarked + rule 3 (audio volume).
- LockCard strict — `LockCardFeatureControl.xaml:124` unmarked + rule 3 (Strict Lock).
- BrainDrain high-refresh — `Views/Controls/Studio/BrainDrainFeatureControl.xaml:216` unmarked,
  and `:48-50` says why in words.
- Whole BrainDrain panel, whole Scheduler panel, whole Haptics panel, Loom, the session strip
  itself — their documents are not among the eleven (Scheduler additionally
  `SchedulerRackPanel.xaml:45-47`: "NOTHING on this panel is SessionLock.Owned").
- Ramp enable/duration/multiplier/end-on-complete/3 links — `RampRackPanel.xaml:24-26`
  ("CmbRampCurve is the ONLY SessionLock.Owned control here").

Lift on every end: `Refresh()` is reached from `_scripted.Ended` (completion, stop) and from
`SessionParticipant.FlushAsync` (`:901-907`, the pre-drain slot) which calls `Scripted.Stop()`.
Derived on every paint, never latched.

### Item 2 — pickers
`Effects/SpiralLibrary.cs` enumerates; `SpiralPresetDocument.Path` is the sink.
`Effects/PinkFilterTint.cs` `PinkFilterColour` parses; `PinkFilterPresetDocument.Colour` is the sink.
No file picker built and none depended on. No `Avalonia.Controls.ColorPicker` package is
referenced and adding one is a csproj change outside scope, so the tint picker is a swatch palette
plus reset (upstream's outcome: choose a tint, reset to default —
`PinkFilterFeatureControl.xaml.cs:176-202`).

### Item 3 — BLOCKED, and this is a spec-versus-code correction
The packet says the audio seams are landed and only need dials. They are landed but they are NOT
app-wide:
- `SoundArbitration` exists only inside the DTRH host window
  (`Features/Dtrh/DtrhHostWindow.axaml.cs:220-256`, whose own comment says "CompositionRoot.cs is
  outside this slice's File Scope — the app-wide lift is a future row"). Nothing in the shell
  holds one.
- `Companion/BarkPipeline` is constructed by that same window (`DtrhBarkRouting.Composition.cs:96`).
- The app-wide audio object the Studio page can reach is `SessionParticipant.Audio`, an
  `IAudioPresence` — which has NO device enumeration, NO preferred-device seam and NO gain seam
  (`Audio/IAudioPresence.cs:93-165`).
- There is no persisted audio settings document; the volume-bearing documents are all per-module.

Wiring the four dials therefore needs `Lifecycle/CompositionRoot.cs`, a new persisted document, and
`Features/Dtrh/**` — all outside File Scope. Reported as a blocker, not improvised.

## Order of work
1. This file, committed. 2. Item 1 + tests. 3. Item 2 + tests. 4. Headed evidence for item 1
(same control, locked and unlocked, same page, same geometry). 5. Gates. 6. Delete this file.
