# Main sync #5 delta ledger (2026-10-08)

Branch `avalonia-port/main-20261008`, from the pushed stack tip.
Pin: `origin/main` = `0d9e83383`.
Last sync pin: `4629b1f4a` (main sync #4). Delta: 5 non-merge commits.

Every non-merge commit has one row. Status legend as in `avalonia-main-sync-20261005.md`:

- **ported-by-merge**: the change landed in a file both heads use (a `CCP.Core` file the stack moved, the language files, or `Assets/web`, which the Avalonia `WebAssetServer` ships through the `..\Assets\web\**` wildcard).
- **needs-port (`lane`)**: WPF-only behaviour the Avalonia head lacks; the lane name is the port lane that owns it. Tick the row when that lane lands.
- **n/a**: no Avalonia counterpart by design, reason given.

Counts: 2 ported-by-merge, 3 needs-port, 0 n/a (total 5).

## needs-port lanes

| lane | rows |
|---|---|
| `descent-ceremony-retire` | `3aa4801fb` |
| `descent-migration` | `fc9640ff7`, `abac4fa02` |

## Rows

| # | sha | subject | WPF files | Avalonia / Core counterpart | status | notes |
|---|---|---|---|---|---|---|
| 1 | `75c078465` | fix(breakout): level 8 keeps an early launch press; pendulum hint clears the prompt | assets: 2; tests: 1 | Assets/web/backroom/stations/breakout (WebAssetServer wildcard) | ported-by-merge | web-only; finale.test.js lands beside the other breakout JS tests |
| 2 | `4d668a96d` | fix(breakout): Endless board name shows on arrival, then fades | assets: 2 | Assets/web/backroom/stations/breakout | ported-by-merge | web-only |
| 3 | `fc9640ff7` | fix(descent): the bonus is for migrated accounts, not every curve_epoch 1 record | `DescentMigration.cs`, `DescentFuseWindow.xaml` (comment); tests: 1 | none yet (DescentMigration is head-only; Avalonia has no migration path) | needs-port (`descent-migration`) | XpBonusFor rule lives in the WPF head; Avalonia shows no cycle XP bonus yet (MainShellWindow.ProfileCard.cs ponytail). Port with the migration. |
| 4 | `3aa4801fb` | chore(descent): delete the dead migration ceremony | `DescentCeremonyWindow.xaml(.cs)`, `DescentCeremonyCopy.cs`, `DescentStageCopy.cs`, `DescentShowDirector.cs`, `MainWindow.StartStop.cs`; tests: 2 | Core `DescentCeremonyCopy.cs` / `DescentStageCopy.cs` (merge); `CCP.Avalonia/Views/Windows/DescentCeremonyWindow.axaml(.cs)` | ported (avalonia-port/main-20261008-compat) | Core side by merge (copy deleted, RomanNumeral in DescentStageCopy). The Avalonia ceremony window (ported from the deleted WPF window, no production opener) and its CoreStandInTests check are deleted in -compat; parity row win-descent-ceremony removed. |
| 5 | `abac4fa02` | fix(descent): retire the migration ceremony - auto-restore on offer, bonus for every migrated account | `DescentMigration.cs`, `DescentMigrationService.cs`, `DescentReceipt.cs`, `DescentShowDirector.cs`, `ProfileSyncService.cs`; tests: 1 | Core `SyncBody` (descent_auto, WPF-only flag); none yet for auto-restore | needs-port (`descent-migration`) | Merge adds `descent_auto` to Core SyncBody, sent by WPF only: Avalonia does not take offers, so it must not claim it does. Auto-restore + bonus rule port with the migration. |

## Merge notes (75b682228) and -compat

- Conflicts: `DescentCeremonyCopy.cs` (stack moved it to Core, main deleted it) -> deleted from Core; `DescentMigration.cs`
  (stack aliased `CycleXpBonus` to Core `DescentCycleXp`) -> kept the alias, main's new doc comment moved to Core;
  `ProfileSyncService.cs` (stack builds a Core `SyncBody`, main added `descent_auto`) -> new `SyncBody.DescentAuto`
  field (`Field.DescentAuto`, inside `All`), set by WPF only. Avalonia `SyncPush.Sent` leaves it out on purpose.
- -compat: deletes the Avalonia ceremony window + its test, updates the Core sync-body goldens for `descent_auto`.
