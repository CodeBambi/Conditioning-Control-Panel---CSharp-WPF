# Lane plan — regenerate the upstream citation inventory

Scratch checkpoint. Removed before the lane reports complete.

## What the tree says versus the board row

| row claims | tree says |
|---|---|
| window `db3e842f..fb89d087a` | one sync stale. `6a8b4c724` (2026-08-23, v6.8.3 -> v6.8.4) landed after the row was written. Measured: the two windows differ ONLY in the derived `sync` date; no inventory path changed between them. |
| "127 of 297 move — 33/21/73, 4 rename destinations" | reproduces exactly, at both endpoints. |
| "81 tier-1 VERDICTS, judgment" | `81` is the tier-1 ENTRY TOTAL, misread off the tool's own summary line. The judgement set is **24 tier-1 entries that move** (5 added / 10 dropped / 9 rewritten): 15 carry a substantive verdict now at risk, 9 carry the UNREVIEWED sentinel. NEEDS-VERDICT after the pass is 5. |
| "the tool never touches `baseline`, and that keeps it clear of the trap" | true of the TOOL, but regeneration alone is not a coherent end state — measured below. |

## The measurement that decides the shape

Regenerating `changedAtSync` over `db3e842f..6a8b4c724` while leaving `baseline` at
`42286638..db3e842f` moves DELTA-MISMATCH from **1 to 54** — every single changed entry, a
100% fire rate against the class's own recorded 1-of-106. The deltas would describe one
window while the file's own baseline names another. So the regeneration and the baseline
advance are one act, not two.

The sibling `client/docs/upstream-payload-inventory.json` already carries
`upstreamVersion v6.8.4 / merge 6a8b4c724 / recorded 2026-08-23` and names its previous
baseline. That is the house pattern; the citation inventory is the one left behind.

## Plan

1. `--regenerate --write` over `db3e842f..6a8b4c724`.
2. Advance `baseline` to `{v6.8.4, 6a8b4c724}` with `previous {v6.8.0, db3e842f}`.
3. The advance kills the data premise of `TheNeedleMode_ExitsZero_WithANonEmptyReviewList`
   (`--needles --since db3e842f` = 0 rows, measured). Restore it with DATA, not a test edit:
   add needles to `Chaos/ChaosWebViewHost.cs` (tier 1, +354/-7 in the window, 19 port
   citations). Five subjects verified MOVED across the window. The test file stays untouched.
4. The four rename destinations, resolved against the UPSTREAM side of the v6.8.0 merge
   (`79feea844`) rather than against the port branch's own tree:
   - `Models/AppSettings.cs` — upstream **+518/-65**; null is a real silent drop.
   - `Models/AiCommandData.cs`, `Models/CompanionPromptSettings.cs`,
     `Models/KeywordTrigger.cs` — upstream **unchanged**; null is correct.
   Cause: at `db3e842f` the port branch's WPF tree still carried the first-attempt
   `CCP.Core/Models/` refactor. `c9e98f29a` / `b0e97d98a` restored it inside the window, so
   git reports `CCP.Core/Models/X.cs -> Models/X.cs`. Not an upstream rename. Recording
   `status: R091` would assert a rename upstream never made, so the resolution goes in the
   verdict, which the tool never clobbers.
5. Verdicts. Priority order:
   - the **4 sentinels whose delta drops to null** — regeneration would silently retire four
     of the nine outstanding NEEDS-VERDICT rows. Review them at the OLD window or the
     obligation vanishes.
   - the 5 sentinels that stay NEEDS-VERDICT.
   - the 15 substantive verdicts reasoning over a delta this pass changed.
6. `intra.mjs` to exit 0; warning gate; floor.

## Not mine

`OverlayDesktopInputTests` (4 red, another lane). `floor.json`. `task-board.md`.
