# WPF v6.3.1 "Deeper Down" → Avalonia Port Reconciliation

Merge `81d34bec` brought `origin/main` (v6.3.1, 9 commits `c7332fcb..e55f8370`) into `feat/crossplatform` with zero conflicts. This doc records **every** WPF/shared C# change in that release and its port disposition, so nothing is forgotten.

**Web DTRH** (`Resources/web/dtrh/**` — Cheshire VN, biome VO, chaos HUD/field, save-slot JS) is shared and merged; the Avalonia head loads the same game via WebView, so those changes land for free.

## Disposition legend
- **IMPLEMENT** — port equivalent exists and needs the same change.
- **NA-WPF** — WPF-UI/WPF-runtime only; port uses a different path.
- **NA-WEB** — the shared web game now handles it; no native port action.
- **AUTO** — shared Core change already merged.

## Changed files

| WPF file | Change | Disposition | Port action |
|---|---|---|---|
| `Services/Chaos/ChaosMetaState.cs` (+5) | Adds `TutorialStage` int (Cheshire VN arc position, persisted + snapshotted to web) | **IMPLEMENT / S** 🔴 | `CCP.Core/Services/Chaos/ChaosMetaState.cs`: add `public int TutorialStage { get; set; } = 0;` |
| `Services/Chaos/DtrhMetaBridge.cs` (+9) | `set-num` persists `tutorialStage` (climb-only 0..16); `reset-onboarding` zeroes it + purges `cheshire:` SeenNarrativeLines | **IMPLEMENT / S** 🔴 | `CCP.Core/Services/Chaos/DtrhMetaBridge.cs`: port both edits (set-num case + reset-onboarding case) verbatim |
| `Services/Progression/AchievementService.cs` (+42) | `TrackBubblesPopped(int count)` batch method (whole DtRH run's bubbles in one increment; crosses 100-bubble sparkle milestones + `pop_the_thought`) | **IMPLEMENT / S** 🔴 | `CCP.Core/Services/Progression/IAchievementService.cs` + `AchievementService.cs`: add the interface method + impl |
| `Services/Progression/QuestService.cs` (+10) | `TrackBubblesPopped(int count)` advances Bubbles quest by a batch | **IMPLEMENT / S** 🔴 | `CCP.Core/Services/Progression/IQuestService.cs` + `QuestService.cs`: add method |
| (caller wiring) | WPF `DtrhHostService.cs:469-474` reads `sessionStats.bubblesPopped` at run-end and calls the batch methods | **IMPLEMENT / S** 🔴 | `CCP.Core/Services/Chaos/DtrhHostOrchestrator.cs` `OnRunEnded` (~:263): inject `IAchievementService`/`IQuestService`, call `TrackBubblesPopped(count)`. Without this the two methods above are inert on the port |
| `Services/Chaos/DtrhHostService.cs` (+43) | (a) `report-bug` page msg → BugReportWindow; (b) run payout credits bubblesPopped; (c) `ownedHabitIds` in run config | **IMPLEMENT / M** 🟡 | (c) `CCP.Avalonia/Chaos/DtrhNativeEffects.cs` `BuildRunConfigJson` (~:215-268): add `ownedHabitIds`; (b) folded into the caller-wiring row above; (a) route `report-bug` in `DtrhHostOrchestrator` to a head bug-report seam |
| `Services/Update/UpdateService.cs` | Version bump 6.3.0 → **6.3.1**, new "Deeper Down" patch notes | **IMPLEMENT / S** 🔴 | `CCP.Core/Services/Update/UpdateService.cs`: `CurrentVersion` (L38), `AppVersion` (L44), `CurrentPatchNotes` (L50) → 6.3.1. **Also** bump the desktop-head csproj `<Version>` (GetCurrentVersion reads entry assembly) per AGENTS.md. Else update popup every launch |
| `Models/AppSettings.cs` (+12) | `ChaosActiveSlot` (int 1-3) backing native slot machinery | **NA-WPF / S** | Native slot machinery is WPF-only (see below); port only needs it if slots are ported |
| `Services/Chaos/ChaosMetaStore.cs` (+177) | Three slot files `chaos_meta.slot1..3.json` + Load/Save/Summary/Delete/MigrateLegacy | **NA-WPF** | Native-only; web does not handle slots. Deferred feature (below) |
| `Services/Chaos/ChaosUpgrades.cs` (+48) | `ChaosMeta` facade slot methods (ActiveSlot/SwitchSlot/AllSlotSummaries/DeleteSlot) | **NA-WPF** | Native-only; deferred feature |
| `Chaos/ChaosSlotPickerWindow.xaml.cs` (+252, new) | WPF modal slot-picker shown before the hole opens | **NA-WPF** | WPF UI; deferred feature |
| `Services/Video/VideoService.cs` (+32) | 3 LibVLC volume/window fixes (Mute-guard drop, etc.) | **NA-WPF** | WPF LibVLC path; the Avalonia head has its own video service. Note as a watch-item if Avalonia video shows the same volume bug |
| `MainWindow/MainWindow.Lab.cs` (+25) | WPF Lab DTRH launch orchestration (focus-if-active, slot flow) | **NA-WPF** | WPF UI; port `LabTabViewModel.StartChaosAsync` is the port path |

## Deferred feature (parity gap, NOT required for v6.3.1 popup/version parity)
**3 local save slots on Avalonia.** Slots are host-side (the web game does not handle them). To bring to the port: port `ChaosMetaStore` multi-slot logic to Core, extend `IChaosMetaService` (ActiveSlot/SwitchSlot/AllSlotSummaries/DeleteSlot), build an Avalonia slot-picker dialog, wire into `LabTabViewModel.StartChaosAsync`. Effort **M-L**. Tracked as a task-board row.

## Implementation status
Implement set dispatched as `round2_v631_parity` on a fresh worktree off `81d34bec` (Core-only gates; smoke run serially after merge). Criticals 🔴 first, `DtrhHostService` 🟡 second.
