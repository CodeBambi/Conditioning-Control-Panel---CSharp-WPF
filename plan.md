# Trainer Card: headed evidence for the record states

Board row (P1) remainder: only `no-runs-yet` has headed evidence. Read, Unreadable and an
earned row have none.

## What the source says (read before scoping)

`Features/Progression/TrainerCard.cs` has three record states: `NoRunsYet` (file absent),
`Read`, `Unreadable`. The card renders them through `Views/Pages/IntakePage.axaml.cs`
`RenderTrainerCard`: `TrainerCardRecordNote` is visible only when the record has something to
say (absent or unreadable), and `TrainerCardAwards` binds the model's four typed rows.

**A Read record with NOTHING earned is not producible by this build.**
`GradedRunAwards.RecordGradedRun` awards `top_of_the_class` FIRST and UNCONDITIONALLY on a
top-marks run, and the file is written only when something was awarded or a category was new.
So every reachable Read record already carries an earned row - "Read" and "an earned row" are
the same state here, and the three states this delivers are:

| state | seeded record | what the card says |
|---|---|---|
| `read` | one top-marks run: `top_of_the_class`, category `bambi` | Top of the Class Earned., Honor Roll "Not earned yet. 1 of 3 categories cleared at top marks." |
| `earned` | three distinct categories: `top_of_the_class` + `honor_roll` | both ledger rows Earned. |
| `unreadable` | malformed bytes | record note carries the reason; both ledger rows "This build cannot tell" |

## Shape

New surface `trainer-card-record` in `client/tools/verify/`, states `read` / `unreadable` /
`earned`. One crop derivation shared by all three (anchored on `TrainerCardPortraitNote`,
fixed DIP size) so the inversions compare the same rectangle. UIA gate on the card's own text
BEFORE any pixel, as the landed `trainer-card` surface does. Two checks per state - one ink,
one ground, disjoint colour bands - so no uniform capture can pass any state, and the
conjunction is unique to the state.

Bands to be MEASURED from a real capture, not chosen.

## Files

- `client/tools/verify/capture.ps1` (surface + seeding + gates)
- `client/tools/verify/checks.json` (six checks)
- `client/tests/CcpClient.Tests/` (disjointness + band facts)

Out of scope and left alone: `Features/Progression/**` (read only), floor.json, task-board.md.
