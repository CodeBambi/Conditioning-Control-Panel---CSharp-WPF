# Plan — render the level the port already computes

## Upstream shape (established, not taken from the packet)

| Thing | Upstream site |
|---|---|
| the level number, header chip | `MainWindow/MainWindow.UiUpdates.cs:59` — `TxtLevelLabel.Text = $"LVL {level}"` |
| the level number, profile bubble | `MainWindow/MainWindow.UiUpdates.cs:58` — `TxtLevel.Text = $"Lvl {level}"` |
| what the level costs | `MainWindow/MainWindow.UiUpdates.cs:55` — `App.Progression.GetXPForLevel(level)` (ported: `XpCurve.XpForLevel`) |
| the XP readout | `MainWindow/MainWindow.ChromeFx.cs:814` — `$"{(int)xp} / {(int)xpNeeded} XP"` |
| the XP bar fill fraction | `MainWindow/MainWindow.ChromeFx.cs:826` — `Math.Min(1.0, xpNeeded > 0 ? xp / xpNeeded : 0)` |
| the bar markup | `MainWindow/MainWindow.xaml:2168-2170` — `XPBarTrack` (8 DIP) + `XPBar` fill |
| the rank title | `MainWindow/MainWindow.UiUpdates.cs:70-77` — four bands `<20 / <50 / <100 / else`, then `App.Mods?.MakeModAware(t) ?? t` |
| the level-up ceremony | `MainWindow/MainWindow.xaml.cs:763-777` — burst, tray balloon, `lvup.mp3`, avatar; `Services/Progression/ProgressionService.cs:226-255` — haptics, achievements, skill points, Discord, webhook, sync |
| feature level gating | `Models/AppSettings.cs:5439` — `return true;`. NOT ported as a gate; already recorded in `LevelUnlocks`. |

## Where the render model goes, and why

`Features/Progression/TrainerCard.cs`, as a sibling record `TrainerCardLevel` with its own passive
`Read(progressionFilePath)`. Reasons found in the source rather than assumed:

* `TrainerCard.Read` already establishes the discipline this needs — open ONE file, parse, close,
  never `PersistenceStore` (which adopts temps, deletes stale files and QUARANTINES). A passive
  render must not rename a user's ledger either.
* `progression.json` lives in the SAME `<dataDir>` as `graded_run_awards.json`
  (`IntakeHostContext.cs:190` vs `:175-179`), so `IntakeLaunch.ReadTrainerCard`'s path resolution is
  reused exactly.
* A sibling record rather than new fields on `TrainerCard`: the award projection is complete and
  tested, and `From`/`Unreadable`/`Rows` would all grow a level parameter they have no use for.

## Refusals (named, at the call site)

* The level-up ceremony: this surface is a passive read on attach; no grant reaches it.
* `lvup.mp3` is legacy-owned bytes (`ConditioningControlPanel/Resources/sounds/lvup.mp3`), not
  linked into `client/`, and `Audio/**` is outside this row's file scope.
* Tray balloon / avatar / haptics / Discord / skill points: no counterpart subsystem.

## Scope note to report

`client/tools/verify/capture.ps1` and `client/tools/verify/checks.json` are named by the packet as
the required headed harness but are not in the OWN list. Editing them additively (new surface
`trainer-card-level`) is the only way to satisfy the headed hard rule; reported as a scope note.

## Checks

Unit: `TrainerCardLevel` projection (known / missing-file / three unreadable reasons / rank bands /
fill clamp / the honesty rule that Unknown never renders 1).
Headless: the level reaches the mounted page; the unknown case renders no number.
Headed: `trainer-card-level`, UIA-gated before any pixel, with the inversion.
