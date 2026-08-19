# SP-108 — plan checkpoint (written BEFORE the first product edit)

Branch `lane/SP-108-non-drawing-module`, base `c547f1c1`. Pin **1477 unit / 90 headless**.

---

## 1. THE CANDIDATE: **Intensity Ramp** — WPF's TIMING group, rack key `ramp`

`Views/Tabs/StudioTabView.xaml.cs:534-541`:

```
_layout.Add("st4_studio_group_timing");
Add("scheduler", ...);
Add("ramp", "📈", null, "Intensity Ramp", "section_intensity_ramp", HostRamp, PanelRamp, "SchedulerRamp",
    () => App.Settings?.Current?.IntensityRampEnabled,
    toggle: () => FlipMasterCheckBox(PanelRamp?.Inner.ChkEnabled));
```

`section_intensity_ramp` = `"⚡ Intensity Ramp"` (`Localization/Languages/en.json:1447`);
group label `st4_studio_group_timing` = `"TIMING"` (`en.json:4819`).

### It hits all three of the packet's "distance" criteria at once

| Packet criterion | Evidence |
|---|---|
| observable **without** an overlay surface | The whole mechanism is `settings.SpiralOpacity = newVal` etc. — a settings write, no window, no draw (`MainWindow/MainWindow.StartStop.cs:504-540`). WPF's own comment at `:451-456`: "the settings write is the whole job now… the Studio panels repaint off PropertyChanged" |
| driven by **session progress** rather than a repaint cadence | `elapsed = (DateTime.Now - _rampStartTime).TotalMinutes; progress = Math.Min(elapsed / duration, 1.0)` (`:484-493`). The 2 s timer (`:426-431`) is a SAMPLING cadence, not a firing schedule — the ramp is never "due" |
| **interacts with an existing module** rather than running beside it | It moves the dials of two modules the port has already landed: `settings.SpiralOpacity` (`:517`) and `settings.PinkFilterOpacity` (`:523`), which are exactly `SpiralPresetDocument.OpacityPercent` and `PinkFilterPresetDocument.OpacityPercent` |

### Behaviour to port, with citations

- **Start** `StartRampTimer` (`MainWindow.StartStop.cs:413-435`): capture base values, `_rampStartTime = DateTime.Now`, 2 s `DispatcherTimer`. Started by `StartEngine` at `:265-269`, gated on `settings.IntensityRampEnabled`.
- **Tick** `RampTimer_Tick` (`:481-556`): `progress = Math.Min(elapsed/duration, 1.0)`; `eased = RampCurves.ApplyCurve(progress, curve)`; `currentMult = 1.0 + (multiplier - 1.0) * eased`; per link `newVal = (int)Math.Min(base * currentMult, cap)`. Caps: flash 100 (`:509`), spiral 100 (`:517`), pink **50** (`:523`), master 100 (`:529`), sub 100 (`:537`). `(int)` is a TRUNCATION, not a round — kept.
- **Curve** `Helpers/RampCurves.cs:47-73`, five shapes, endpoints preserved, input clamped 0..1. Persisted by ordinal, missing = Linear (`AppSettings.cs:2631-2639`).
- **Stop** `StopRampTimer` (`:437-479`): stop the timer and **restore every captured base value**, then clear the map.
- **Auto-stop** (`:547-555`): `progress >= 1.0 && EndSessionOnRampComplete` → tray notification + `StopEngine()`. The completion test uses RAW linear progress, not eased — WPF says so at `:495-497`.
- **Clamps** `CCP.Core/Models/AppSettings.cs:2581-2586` duration `[10,180]` default 60; `:2468-2472` multiplier `[1.0,3.0]` **default 1.0**; `:2575-2580` enabled default false; `:2625-2630` end-at-complete default false; `:2590-2622` five link flags, all default false.

### Why the other candidates are not this

| Group | Row | Why not |
|---|---|---|
| GAMES & CARDS | Bubble Pop | `Services/BubbleService.cs` is 4918 lines of spawn timer + per-bubble `DispatcherTimer` hops (`:192,392,430,796,1779`) driving **clickable** moving windows. D84's class exactly — a moving-glyph module at a cadence the confirm-everything `Paint` cannot be spent at, plus windows that must CATCH clicks, which the port's overlay refuses by design |
| | Bubble Count | Needs **video playback** and interactive message windows (`Services/BubbleCountService.cs:30-39`, `_regularVideos`, `_messageWindows`). No video capability in the port |
| | Lock Card | `LockCardWindow.ShowOnAllMonitors(phrase, repeats, strict, isTest, voice)` (`Services/LockCard/LockCardService.cs:299`) — an **input-capturing modal on every monitor**. The port's overlay is click-through by construction |
| | Bouncing Text | Already refused with evidence at SP-106 (**D83/D84**) |
| IMMERSION | Mind Wipe | Reachable (the port has real audio), but it is a **PACED** module — random intervals off a frequency-per-hour dial, NAudio one-shots (`Services/LockCard/MindWipeService.cs:18-30`). `PacedSessionEffect<TFiring>` fits it. It would test the AUDIO capability, not the spine. **Runner-up, and it is less distant** |
| | Brain Drain | Same paced-audio shape (`Services/LockCard/BrainDrainService.cs:13-30`) plus a screen-capture compositor layer that exists only in the CCP.* tree |
| | Haptics | Device backends — Buttplug/Intiface, Lovense (`Services/Haptics/ButtplugProvider.cs`, `LovenseProvider.cs`). A capability the port does not have. Also the rack's one **paid** row (`StudioTabView.xaml.cs:528`, `tier: 1`) |
| TIMING | Scheduler | **Structurally outside the spine**: it starts the engine from OUTSIDE a session (`MainWindow.StartStop.cs:562-620`), needs tray minimize + notification, and runs when nothing is running. It cannot be an `ISessionEffect` at all |

**So the finding the packet offered as an escape is NOT taken.** One candidate in another group is fully in scope, and it is the one furthest from the four proven seams.

---

## 2. PREDICTIONS, stated before the first product edit

| # | Prediction |
|---|---|
| P1 | **`ISessionEffect` fits a FIFTH time, unchanged.** No member edited |
| P2 | **`OwnedSessionEffect` fits unchanged.** `WorkIsRunning`/`Engage`/`ReleaseWork` are still the three right questions |
| P3 | **`PacedSessionEffect<TFiring>` does not fit — for the SECOND time and a DIFFERENT reason.** SP-105 rejected it because a continuous module has no interval. This module HAS a real interval (2 s) and still does not fit, because its `WorkIsRunning` would be `ScheduleArmed` (a clock claim, which would read `Live` for a ramp holding nothing) and its `ReleaseWork` drops a one-shot and leaves the world alone — and this module's release must GIVE BACK the dials it took. **Having a timer is not sufficient to be paced** |
| P4 | **NO overlay surface, no presenter, no `OverlaySurfaceSet`.** The cadence lives in the MODULE, which is a third home for a cadence and does not contradict SP-106: that distinction was never about the file, it was about what the cadence is FOR |
| P5 | **The dot gains a FOURTH meaning and the enum does not gain a fourth member.** `Live` = a claim about **CUSTODY** |
| P6 | The module needs no new shared-code change at all — the only additions are its own files plus three reason codes |
| P7 | The port can link **two** dials where WPF links five, because three have no dial in this port (flash opacity, master volume, subliminal volume are not ported). They must be ABSENT, not greyed (§9 D7) |

**The dot, decided and defended (trap 1).**

- Paced `Live` = a claim about the **CLOCK** (SP-101/SP-105).
- Continuous `Live` = a claim about the **SCREEN** (SP-105).
- Moving `Live` = the screen **and** that it will differ a moment from now (SP-106).
- **Non-drawing `Live` = a claim about CUSTODY:** this module holds dials that belong to other modules, is driving them, and owes them back.

```
Running = the progress cadence is scheduled
       && this module holds at least one linked dial's base value
```

Why not the other candidates:

- **Not the clock.** A ramp whose links are all off has a tick scheduled forever and will never change anything anywhere. `ScheduleArmed` would read `Live` for a module the user cannot observe by any means — the same class of lie as a `Live` Linux tint.
- **Not "change".** At progress 1.0 the ramp stops changing anything, and at multiplier 1.0× it never changed anything — yet in both cases it is genuinely running: it holds your dials, a mid-session multiplier move starts them climbing with no re-arm, and STOP will put them back. SP-106's moving-module rule ("Live means it will differ a moment from now") gives the WRONG answer here, and that is the finding.
- **Multiplier 1.0× and a zero base take the Subliminals-empty-pool answer, not the Pink-Filter-zero-opacity answer**: a `Degraded` ARM with a typed reason, and a `Live` dot — because unlike the tint at 0 %, there really is a live machine holding real state, just at neutral gain.
- The negative control that makes it bite: **no linked dial → holds nothing → nothing to give back → `Armed`, never `Live`.**

---

## 3. Shape

**New product files (all inside File Scope)**

| File | What |
|---|---|
| `Effects/RampCurves.cs` | `RampCurve` enum + `ApplyCurve`, ported from `Helpers/RampCurves.cs:47-73` |
| `Effects/IntensityDial.cs` | `IIntensityDial` (`Id`, `Label`, `Ceiling`, `Read`, `Write`, `Reapply`) + the two adapters over the landed Spiral/Pink modules |
| `Effects/IntensityRampEffect.cs` | the module, an `OwnedSessionEffect` |
| `Session/IntensityRampPresetDocument.cs` | its persisted dials, clamps as WPF's |

**Changed** `Session/EffectReasonCodes.cs` (3 codes), `Session/SessionParticipant.cs` (composes it, wires auto-stop), `Views/Pages/StudioPage.axaml` + `.axaml.cs` (the TIMING group, the row, the panel).

**No change** to `Effects/OverlaySurfaceSet.cs`, `Session/OwnedSessionEffect.cs`, `Session/PacedSessionEffect.cs`, `Session/SessionEffect.cs`, `Session/SessionEngine.cs`, or any of the four landed modules.

**Threading, decided up front (this is the hazard SP-106 §4.2 was burned by).** Each `AsyncOperationOwner` owns its own lock (`Lifecycle/OperationRegistry.cs:122`), so the ramp's owner lock and a target's are different objects, and the dial adapters are strictly one-directional (ramp → target; no target ever calls the ramp). The write is split so nothing expensive happens under a lock and nothing touches a window off the UI thread:

- `Write(value)` — the persisted dial only, thread-safe by `PersistenceStore.Mutate`, run SYNCHRONOUSLY on the caller's thread.
- `Reapply()` — the owning module's `Refresh()`, which touches a native surface, run through an injected dispatch (WPF's own `Dispatcher.Invoke` at `MainWindow.StartStop.cs:504`, decomposed).

> ~~This deliberately closes an upstream residue: WPF's window-close path stops the ramp timer WITHOUT restoring (`MainWindow/MainWindow.WindowChrome.cs:167` is a bare `_rampTimer?.Stop()`) and `SaveSettings()` runs five lines earlier at `:162`, so quitting mid-ramp persists the RAMPED opacity.~~
>
> **STRUCK — THIS CLAIM IS FALSE.** Annotated in place at the final review rather than rewritten, so the checkpoint still shows what was planned and on what (mistaken) premise. `:167` runs only inside `if (_exitRequested)` (`MainWindow/MainWindow.WindowChrome.cs:137`) and every writer of that flag calls `StopEngine` first, which restores via `StopRampTimer`. The two numbers are wrong as well: `SaveSettings()` is at `:163` and `_rampTimer?.Stop()` is at `:167`, **four lines later, not five earlier**, over a timer already nulled at `MainWindow.StartStop.cs:440`. **See `record.md` §9.1** for the full source reading and for what the error was in method.

The rest of this paragraph stands, and the split write is correct for a PORT-SIDE reason that needs no claim about WPF: the port restores the values synchronously on every release, so a teardown whose dispatcher is already down can never leave the flush a ramped dial to write. Recorded as a divergence (D95).

**Also deliberately divergent, and recorded:** the ramp re-applies a dial only when the integer actually CHANGES. WPF writes and reconciles every 2 s; in this port `OverlaySurfaceSet.Place` calls `Present`, which walks the OS z-order and toggles click-through in both polarities (SP-106, `Overlay/Win32OverlayPresence.cs:547-576`) — the cost that made a 60 Hz module unportable (D84). Same user-visible outcome, roughly 20 re-places per hour instead of 1800.

**Tests** — new `IntensityRampEffectTests.cs`, `NonDrawingEffectSpineTests.cs` (unit); additions to `StudioSurfaceNoticeTests.cs` (unit) and `StudioRackHeadlessTests.cs` (headless). `ContinuousEffectSpineTests`' rack-order fact gains a fifth member.

**Not attempted:** the `StudioPage.axaml.cs` extraction SP-106 §1.4 recommended. It is an advisory from a prior lane, it is not in this packet, and a refactor of the file carrying all four landed modules' rendered claims is the wrong risk to take in the same wave as a new module. The new module's own text goes in its own file instead, which is the extraction's direction without its blast radius.
