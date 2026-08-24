# Checkpoint plan — the typed mantra minigame's door

## The decision: (a), the removal was INCIDENTAL. Build the door.

Upstream did not retire the surface. It removed what it believed was a DUPLICATE card, was wrong
about this one card, and said so in writing three times.

1. `a9859e7b6` (2026-08-12 18:24, "0812 five-fix batch"), the relayout's own commit message:
   "Play page: drop Mantras/Subjects/Deeper/Inspection Bureau/Showcase cards ... **(MantraWindow
   entry point orphaned - re-home pending owner call)**". An orphaning recorded as an unresolved
   consequence is not a retirement.
2. `Views/Tabs/PlayTabView.xaml:20-27` states the removal's PREMISE: "Every one of those features
   is still built, still shipped and still reachable by its own door / rail entry / dashboard tile;
   only the duplicate Play-page card is gone." That premise is TRUE for the other four and FALSE
   for Mantras — the Play card was its ONLY caller.
3. `MainWindow/MainWindow.PlayTab.cs:262-286` keeps `StartMantraSession` alive on purpose, calls
   re-homing "exactly one StartMantraSession(reps) call", and ends "Owner call: WHERE the game
   should live now." Where, not whether.

The shipping product still SELLS the surface it cannot open:
- `Services/Program/BuiltInPrograms.Kept.cs:349,705,1149` — three built-in program tasks verified by
  `QuestCategory.Mantra`, whose task card prints `programs_howto_mantra`
  (`MainWindow/MainWindow.ProgramsTab.cs:411`): "type them in the mantra minigame, or say one out
  loud". That line exists because "support reports showed users guessing wrong about what actually
  satisfies a task (e.g. spoken vs typed mantras)" (`:390-393`). Nine languages, still shipping.
- `MantraService.BreakStreak` (`:106`) has exactly one caller, `Windows/MantraWindow.xaml.cs:204`,
  and `MantraCompleted`/`StreakChanged` are raised only inside `TryCompleteMantra` (`:92,:101,:110`).
  `CreditExternalMantra` (`:46-60`) raises none of them. So three shipped bark packs
  (`builtin-bambisleep`, `builtin-locked`, `builtin-sissyhypno` `bark_rules.json`) carry authored
  voiced lines for `MantraCompleted`/`MantraStreakChanged`/`MantraStreakBroken` that CANNOT fire in
  v6.8.4, and `AutonomyService.cs:1963`'s "typed-minigame credit if it happens to be running" branch
  is dead.
- The card's four loc keys (`pl6_mantra_title/blurb/reps_label/start`) survive in all nine language
  files, like `rf_play_zone_ritual` — "left unused rather than deleted".
- The card SHIPPED: `SlotMantra` is in the tree at the v6.8.0 version-ritual commit `0cbe9874b`
  (2026-08-12 15:22); the relayout lands 3h later and is first in v6.8.1.

Contrast with what a deliberate retirement looks like in this repo: Arcademy, where upstream shut
the door on purpose and the port keeps it shut (`client/docs/port-digest.md:49-51`).

The one contrary line, and its resolution: `PlayTabView.xaml:26` says "Do NOT 'restore' them here:
two doors to one feature is what the restructure exists to stop", and `PlayTabView.Cards.cs:31-33`
repeats it. That is a DE-DUPLICATION rule with a stated reason, and the reason does not obtain in
the port: the port has ZERO doors to this surface, so the card is the only one, not a second one.

## Scope discovery (report, do not hide)

`MantraLaunch(ApplicationHost, Window)` is composed where every sibling launcher is composed —
`Views/MainWindow.axaml.cs` (`Loom` :55, `Dtrh` :72, `Arcademy` :78, `Intake` :82, `Goon` :90,
`new PlayPage(Dtrh, Goon, Arcademy)` :141) — and NOT in `Lifecycle/CompositionRoot.cs`, which is the
file the packet excluded. PlayPage is constructor-injected and cannot reach a host any other way.
So the door needs three lines in `Views/MainWindow.axaml.cs`, a file the packet names on neither
list. Taken as an explicit, reported scope extension, kept off the lines the StudioPage/SystemPage
lanes would touch where possible.

## Build

1. `Views/Pages/PlayPage.axaml` — a Mantras card in the port's own card vocabulary: title, blurb,
   a REPS picker (upstream's 4-item ComboBox), a Begin button; a fault line like the Goon card's.
2. `Views/Pages/PlayPage.axaml.cs` — `MantraLaunch` ctor param; Begin -> `mantra.Open(reps)`;
   render `LastFault` through `LaunchFaultText.Compose`.
3. `Views/MainWindow.axaml.cs` — `Mantra = new MantraLaunch(host, this)`, the property, the arg.
4. Headless facts (CcpClient.HeadlessTests): card present + UIA ids; Begin opens with the PICKED
   rep count; second press focuses rather than restarting; fault renders.
5. Headed (`client/tools/verify/**`): a `mantra-window` surface, driven by real input from the Play
   door, UIA-gated BEFORE any pixel (the mantra line, the counters, the answer line after real
   keystrokes = focus acquisition; maximized + topmost read from the window rect against the work
   area), then pixel checks with the inversion and a floor well under the lowest measurement.
6. Census correction in `client/docs/port-completeness-census.md` — NOT in scope. Report it.

## Gates
`node client/tests/floor/check-warnings.mjs`, `node client/tests/floor/check-floor.mjs`.
Floor delta reported, floor.json never opened.
