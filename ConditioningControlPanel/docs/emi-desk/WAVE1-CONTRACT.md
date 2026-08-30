# Ask EMI - Wave 1 ("the knock") build contract

> Owner-acked 2026-08-30. This file is the ONLY coordination point between the build lane and the
> writing lane. Ids here are load-bearing: a pool id that does not match a moment id is a silent
> mute, not an error. Nobody renames anything in here without changing both lanes.

## What Wave 1 is

1. The EMI dock chip knocks **once, ever** on a settled first launch.
2. Clicking it summons her; she introduces herself and makes **one offer**.
3. Saying yes runs **the short walk**: 7 spotlit steps, EMI narrating each one.
4. Tour completion **persists** for the first time (`TutorialService` currently remembers nothing).

Wave 1 does NOT include the codex. There is no book to open yet, so the offer is two chips, not
three (see "The two-chip law" below).

## The two-chip law (do not fight this)

`EmiLineEngine.PickAsk` drops any ask where `a.Chips.Count != 2`, and
`EmiDeskWindow.Bubble.BuildChips` iterates `for (int i = 0; i < 2; i++)`. Two chips, index 0 = yes.
The pitch's third chip ("give me the book") arrives in Wave 2 with the book itself.

## Ids

### New moments + pools (`Resources/emi/desk-lines.json`)

| id | what fires it | shape |
|----|---------------|-------|
| `firstContact` | she is summoned by the knock, fresh install, walk not yet taken | pool + ask |
| `firstContactUpgrade` | same, but `LastSeenVersion` is non-empty and older | pool + ask |
| `firstContactLater` | they said no; also the single next-launch re-offer | pool |
| `tourStarted` | any tour begins while she is available | pool |
| `tourFinished` | a tour reaches its last step | pool |
| `tourSkipped` | a tour is abandoned part way | pool |
| `tourStep` | per-step fallback when a step has no pool of its own | pool |
| `tour.sw-assets` | short walk step 1 - the content folder | pool |
| `tour.sw-flash` | step 2 - fire one flash | pool |
| `tour.sw-panic` | step 3 - the panic key | pool |
| `tour.sw-dock` | step 4 - the chip she came out of | pool |
| `tour.sw-xp` | step 5 - XP and levels | pool |
| `tour.sw-settings` | step 6 - the settings door | pool |
| `tour.sw-done` | step 7 - the last card | pool |

### New asks

| id prefix | moment | chips (0 = yes) | yes effect |
|-----------|--------|-----------------|------------|
| `ask.firstContact.*` | `firstContact` | e.g. `["show me","later"]` | `tour:shortwalk` |
| `ask.firstContactUpgrade.*` | `firstContactUpgrade` | e.g. `["show me","nah"]` | `tour:upgrade` |

`no` replies live inside the ask (`"no": {...}`) as usual; the `firstContactLater` pool is what she
says on the NEXT launch, not the immediate no-reply.

### Short walk step ids (`TutorialService`)

`sw-assets`, `sw-flash`, `sw-panic`, `sw-dock`, `sw-xp`, `sw-settings`, `sw-done` - in that order.
The narrator maps step id -> pool `tour.<stepId>`, falling back to `tourStep`.

### New effect verbs (`EmiOffers`)

- `tour:shortwalk` -> `MainWindow.StartTutorial(TutorialType.ShortWalk)`
- `tour:upgrade` -> `MainWindow.StartTutorial(TutorialType.UpgradeTour)`

`EffectFeasible` must return false (so the ask is never shown at all) when the main window is gone,
a session is running, a tutorial overlay is already up, or the tour in question is already latched
as done.

### New `EmiState` fields (owned by the knock agent, consumed by the narrator agent)

| json | type | meaning |
|------|------|---------|
| `knockState` | int | 0 never knocked, 1 knocked, 2 answered/spent |
| `knockAtUtc` | long | when the knock fired (ticks); 0 = never |
| `knockOffers` | int | offers made; hard cap 2 (the knock, plus one re-offer next launch) |
| `toursDone` | List&lt;string&gt; | `TutorialType` names completed end to end |

## The four brakes (copied from `EmiNudgeMachine`, deliberately)

The knock is onboarding, not nagging. Any one of these ends it forever:
1. `knockState == 2` - they answered, either way.
2. `knockOffers >= 2` - the knock plus one shrugged re-offer, and never again.
3. The ask's own `limit: {per:"ever", max:1}` in the lines file.
4. `toursDone` contains the tour she would offer.

## Gates the knock must pass before it may flash

No knock while: the first-run wizard is up, an update dialog is up (`App.IsUpdateDialogActive`), a
session is running, a tutorial overlay is open, the window is minimised or hidden, EMI Desk is
disabled in settings, or she is already out. Fires at `DispatcherPriority.Normal` behind an
`IsLoaded` check - **never `Loaded` priority**, which is what starved the original app tour into
never running at all.

## Branching

| population | detected by | offer |
|-----------|-------------|-------|
| fresh install, skipped the wizard's tour | `LastSeenVersion` empty | `firstContact` -> short walk |
| fresh install, already took the walk | `toursDone` has `ShortWalk` | no ask; greeting only |
| upgrader | `LastSeenVersion` non-empty AND older | `firstContactUpgrade` -> `UpgradeTour` |

Never gate on a bare seen-flag: that is the bug that showed every fresh install a migration notice
for a move it never witnessed.

## The wizard hand-off

`FirstRunWizard`'s last step ("Seven doors" / "Take the tour") currently calls
`owner.StartTutorial()` = `FullTour`. Per owner call 1 it now starts `TutorialType.ShortWalk`.
"Explore on my own" is unchanged - and that population is exactly who the knock is for.

## Narration rules

- On tour start, if EMI is available and not out, she is summoned. If EMI Desk is off, missing, or
  muted, **the tour runs exactly as it does today**. Narration is additive and never load-bearing.
- Each `StepChanged` fires `App.EmiDesk?.Fire("tour.<stepId>")`, falling back to `tourStep`.
- The card keeps its title and description. Short-walk descriptions are written terse (one line)
  because EMI carries the colour; every other tour keeps its existing prose untouched.
- She never blocks a step. No ask, no hold, no waiting on her during a tour.

## Localization

English-only for the new strings, per owner call 3. New loc keys go in `Localization/Languages/en.json`
only and ride the documented fallback chain (active language -> English -> key). **Never put a
literal line break inside a language-file string** - write `\n`.

## Line format (writers)

`docs/emi-desk/VOICE.md` is the bible and outranks anything summarised here. Hard rules:
lowercase, one thought, **<= 60 characters**, no em/en dashes, no emoji in `t` (kaomoji go in `face`),
`spice` 0/1/2 with 0 the majority, typos about 1 line in 10 marked `"typo": true`. The word "door"
is on her fence in HER lines - the rail's doors are the app's word, not hers.

Every new pool needs at least 8 lines so the shuffle bag has room; asks need 3+ variants each.
