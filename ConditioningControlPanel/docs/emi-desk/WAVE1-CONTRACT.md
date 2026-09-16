# Ask EMI - Wave 1 ("the knock") build contract

> Owner-acked 2026-08-30. Amended by the first-run redesign, Sep 2026 (lane B): the knock now makes
> the offer directly. This file is the ONLY coordination point between the build lane and the
> writing lane. Ids here are load-bearing: a pool id that does not match a moment id is a silent
> mute, not an error. Nobody renames anything in here without changing both lanes.

## What Wave 1 is

1. On a settled first launch the EMI dock chip pulses **and she is summoned in the same beat**,
   `why: "knock"`. **Once, ever.** No click is needed and none is waited for.
2. She opens with `firstContact`, whose **ask is the offer**: a two-chip question that introduces
   her and ends on the walk.
3. Saying yes runs **the short walk**: 7 spotlit steps, EMI narrating each one. Saying no, or
   sending her away with the ask still up, is a **no** - and it is the last word, because the one
   offer was already spent when she appeared.
4. Tour completion **persists** for the first time (`TutorialService` currently remembers nothing).

### What the redesign changed, and why

The knock used to be **only** three pink pulses on a 40 px ring, with the offer reachable solely if
the user read those six seconds as an invitation and clicked. Almost nobody did, and the offer was
counted at the flash regardless - so the app's only tour was routinely spent on a light nobody knew
was a button. A shrug therefore bought one softer re-offer (`firstContactLater`) on a later launch
to make up for it.

Now she asks out loud the first time, so there is nothing to make up for:

- `OfferCap` is **1**, not 2.
- `firstContactLater` and `EmiKnockMachine.LaterMoment` are **deleted**, moment and pool.
- `Population` returns `None` for **upgraders**. Their launch already carries What's New, which is
  where the upgrade tour is offered from; a companion appearing on top of that to offer a second
  tour is the pile-up the redesign exists to end. `firstContactUpgrade`, `TourFor(Upgrader)` and
  `EffectFor(Upgrader)` all survive as ids for that surface to fire.
- The **pulses stay**, and they are no longer a request for a click. They point at the ring she
  stepped out of, which is the one control a first-run user has to be able to find again after they
  send her away. `EmiDock.Refresh` therefore no longer stops them when she comes out - that rule
  would kill the animation in the frame it started, since `OutChanged` now fires from inside the
  knock's own summon. A click on the chip still cuts them short, and still toggles her away.

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
| `firstContact` | the knock summons her, fresh install, walk not yet taken | pool + ask |
| `firstContactUpgrade` | an upgrader is offered their tour. NOT by the knock any more; the id is kept for What's New / the Welcome-back sheet | pool + ask |
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

`no` replies live inside the ask (`"no": {...}`) as usual, and that reply is the END of it: there is
no next-launch beat behind them any more.

**The ask carries the whole of first contact.** `EmiLineEngine` returns the ask INSTEAD of the pool
line, never as well as it (`PickAsk` hits, `_pendingAsk` is set, the draw returns null). She now
arrives unbidden, so there is no greeting in front of the question either - which means each
`ask.firstContact.*` `q` has to introduce her AND land on the walk, in one line under 60 characters.
The `firstContact` **pool** is the fallback the engine falls through to when `PickAsk` comes back
empty, and on this moment that means the walk is not feasible at all (already taken, a session
running, a tutorial up). Pool lines therefore introduce her and stop; a line there that ends on
"shall we?" is an offer with no chips underneath it. Both halves are pinned by
`EmiKnockLinesFileTests`.

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
| `knockOffers` | int | offers made; hard cap **1**. A ledger carrying 2 from before the redesign reads as over, which is correct - they were asked twice |
| `toursDone` | List&lt;string&gt; | `TutorialType` names completed end to end |

## The four brakes (copied from `EmiNudgeMachine`, deliberately)

The knock is onboarding, not nagging. Any one of these ends it forever:
1. `knockState == 2` - they said YES. A **no** still does not latch state 2, and no longer needs
   to: brake 2 was already spent when she appeared. The latch earns its keep by surviving a QA
   counter reset, so replaying the knock cannot put the same question to somebody who took the
   walk. (The two brakes used to contradict each other under a cap of 2; at a cap of 1 they
   simply agree.)
2. `knockOffers >= 1` - she comes out, she asks, and that was the feature. The offer is counted
   **at the knock, not at the answer**; counting on the answer lets somebody who closes the app
   mid-bubble re-trigger her every launch forever.
3. `limit: {per:"ever", max:1}` - which lives on the **moment definition**, not on the ask.
   No shipped ask carries a `limit` key; the engine reads it off the moment.
4. `toursDone` contains the tour she would offer. An unreadable ledger answers **no** (see
   `EmiState.HasTourDone`): a false no costs one walk offered twice, which brakes 1 and 2
   already cap; a false yes costs a first-run user the feature entirely, silently.

## Gates the knock must pass before it may flash

No knock while: the first-run wizard is up, an update dialog is up (`App.IsUpdateDialogActive`), a
session is running, a tutorial overlay is open, the window is minimised or hidden, EMI Desk is
disabled in settings, or she is already out. Fires at `DispatcherPriority.Normal` behind an
`IsLoaded` check - **never `Loaded` priority**, which is what starved the original app tour into
never running at all.

**The startup quiet window is deliberately NOT a gate.** Every other first-run surface is held or
sent to the Inbox for the first ten minutes; this one offer is the single thing allowed through,
because it *is* the onboarding the quiet window is protecting. It arrives non-modally, in a bubble,
from a companion one click sends away. Gating it on quiet would push the app's only tour offer past
the point where anyone is still wondering what the app does.

## Branching

| population | detected by | offer |
|-----------|-------------|-------|
| fresh install, skipped the wizard's tour | `LastSeenVersion` empty | `Fresh`: `firstContact` -> short walk |
| fresh install, already took the walk | `toursDone` has `ShortWalk` | `Walked`: no ask; greeting only |
| anyone with a version stamp, older or not | `LastSeenVersion` non-empty | `None`: the knock offers nothing. Upgraders get their tour from What's New / the Welcome-back sheet |

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

A line whose `spice` is above its moment's `spiceCeiling` is **unreachable, not merely rare**:
`EmiLineEngine` deals at `Math.Min(moment.SpiceCeiling, UserSpice())`. Check the ceiling of every
moment a pool is wired to before tagging a line 2, or raise the ceiling deliberately.
