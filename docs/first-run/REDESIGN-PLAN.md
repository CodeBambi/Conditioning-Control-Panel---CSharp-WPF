# First-run experience redesign - implementation plan

Branch: `feat/first-run-redesign` (worktree `C:/wt-firstrun`). Sep 10 2026.
Design source: the "CCP First Run Redesign" canvas (owner accepted the Proposed direction).

## What the owner complained about

- Too many popups/cards in the first 30 s on a new PC (worst case: 14 modal stops + 11 non-modal pops).
- "The Circe one" still pops later (the flavour/mod picker re-arms after an offline showing; the live server announcement replays on every new device).
- Season-rollover box on re-login on a new PC (fresh settings file has no `LastSeasonResetSeen`, first sync sets the server season, recap fires with no data).
- Two tutorials back to back (wizard step 3 "Seven doors" list, then "Take the tour" starts ShortWalk, and the EMI knock later).

## Target (from the canvas)

**Proposed timeline on a fresh install:** Splash -> Welcome (18+ / language / content folder) -> Flavour -> Home, usable at once, packs download in the background. Zero pops in the first ten minutes. One tour, offered once, by EMI, non-blocking, from her chip.

1. **One gate.** The age check is the Welcome screen's button, not a MessageBox in front of it. Language and content folder live on the same screen. The wizard is two steps.
2. **One tour.** The seven-doors step leaves the wizard. ShortWalk is the only walk. EMI offers it once, from her chip, non-modally. The knock's re-offer is retired.
3. **One queue.** A single `StartupPresenter` owns every startup surface. Modal things go one at a time by priority. Non-modal things become Inbox items while the quiet window is on (first launch: 10 min; also while a tour or a session runs, or while a modal is up).
4. **Server truths.** A new device adopts the server's season silently (no recap, no box). Announcement dismissals are stored per account so "The Spiral is open" never replays on a second PC.
5. **Welcome back.** A returning user on a new PC gets ONE sheet (restore cloud backup toggle, bring-the-flavour toggle, What's New collapsed, server-only season line) replacing the What's New dialog, the three cloud-restore MessageBoxes and the season box.

## Ground rules for every lane

- .NET 8 WPF. Build: `dotnet build ConditioningControlPanel/ConditioningControlPanel.csproj -c Debug -nologo -v q` from the worktree root. MSB3027 file-lock errors mean the app is running, not a compile error.
- Tests: xunit v3 in `Tests/ConditioningControlPanel.Tests`. Run a filtered subset: `dotnet test Tests/ConditioningControlPanel.Tests --filter "FullyQualifiedName~<YourClass>" -nologo -v q`. Pure-logic classes get pure tests (no WPF, no `App`). Render tests use `WpfRenderHarness.OnStaThread` and the `CompanionWpfRenderCollection` collection (see `FirstRunWizardRenderTests.cs`).
- Localization: `Loc.Get(key)`; new keys go in `ConditioningControlPanel/Localization/Languages/en.json` only (other languages fall back). Wizard code uses the `Str(key, english)` fallback helper; copy that pattern.
- `DispatcherPriority.Normal`, never `Loaded`, for startup work (Loaded is starved in this app and silently never runs).
- Never add a second `IsStartupDialogShowing`-style flag. The presenter drives the existing static `MainWindow.IsStartupDialogShowing` so every existing 500 ms poller keeps working.
- No em-dashes in strings, comments or docs. Plain hyphens.
- Commit on your lane branch with clear messages. Do not touch files outside your lane's list unless the brief says so; if you must, say so in your report.
- Do not run the app. Build + tests only.

## Shared contract: `StartupPresenter` (lane C builds it; A, B, D, E code against it)

```csharp
namespace ConditioningControlPanel.Services.Startup;

/// Pure core, no WPF: ordering + quiet-window rules. Unit tested.
public sealed class StartupQueueCore { ... }

/// WPF shell. Singleton on App: App.Startup (static property, created in App.OnStartup before MainWindow).
public sealed class StartupPresenter
{
    // ---- modal ladder ----
    // show(owner) runs on the UI thread, must block until the surface is gone (ShowDialog).
    // The presenter sets MainWindow.IsStartupDialogShowing = true before show and false after,
    // runs ONE at a time, lower priority number first, FIFO within a priority, and never
    // starts a modal while App.IsUpdateDialogActive or App.Tutorial.IsActive.
    public void EnqueueModal(string key, int priority, Action<System.Windows.Window?> show);
    public bool IsModalUp { get; }

    // ---- quiet window ----
    public bool IsQuiet { get; }               // modal up || tutorial active || session running || first-launch window open
    public void BeginFirstLaunchQuiet(TimeSpan span);   // called by the wizard's far side
    public event Action? QuietChanged;

    // ---- inbox ----
    public void PresentOrInbox(InboxItem item);   // !IsQuiet => item.Open() now, else Inbox.Add(item) (dedupe on Key)
    public System.Collections.ObjectModel.ObservableCollection<InboxItem> Inbox { get; }
    public int UnreadCount { get; }
    public void OpenItem(InboxItem item);     // removes from Inbox, runs Open
    public void DismissItem(InboxItem item);  // removes from Inbox, runs Dismiss if any
}

public sealed class InboxItem
{
    public string Key { get; init; }          // dedupe key, e.g. "announcement:ann_spiral_launch_690"
    public string Title { get; init; }
    public string Summary { get; init; }
    public string Glyph { get; init; } = "";  // one emoji/char for the row
    public Action Open { get; init; }         // shows the original surface (the popup, the card, the ceremony)
    public Action? Dismiss { get; init; }     // the surface's own dismissal bookkeeping (e.g. record DismissedAnnouncementId)
    public DateTime PostedUtc { get; init; } = DateTime.UtcNow;
}
```

Modal priorities (lower runs first):

| Priority | Surface |
|---|---|
| 10 | failed-update report (`App.ReportFailedUpdateAttemptAsync`) |
| 20 | `FirstRunWizard` |
| 30 | `WelcomeBackSheet` (fresh device + cloud identity) or `WhatsNewDialog` (upgrader) |
| 40 | Season recap card / notice |
| 50 | Upgrader mod picker (`ModPickerDialog.ShowIfNeeded`) |
| 70 | Deeper enhance nudge (`MainWindow.DeeperTab.cs:492`) |
| 80 | Update-available dialog (`App.ShowUpdateNotification`) |

Inbox routing (non-modal surfaces that call `PresentOrInbox`): server announcement popup, weekly intake nudge popup, `FeatureIntroPopup` cards (daily-free, one-account, remote-media, possession), premium celebration, Descent ceremony offer, wardrobe item-unlock toasts. Nav door header pulses (`StartNavDoorHeaderPulse`) simply skip while quiet.

## Lanes

### Lane A - the two-step wizard (worktree `C:/wt-fr-a`, branch `fr/a-wizard`)

Files: `Windows/FirstRunWizard.xaml`, `Windows/FirstRunWizard.xaml.cs`, `App.xaml.cs` (age-gate block only, ~line 2963), `Localization/Languages/en.json`, `Tests/.../FirstRunWizardRenderTests.cs`, plus a new `Tests/.../FirstRunGateTests.cs` if you extract pure logic.

1. **Step 1 = Welcome** (match the canvas "Welcome" board): heading "Welcome.", one line of body ("Two quick choices and she is all yours. Everything else can wait until you ask for it."), then three rows:
   - **Language** combo (reuse `MainWindow.PopulateLanguageCombo` logic; changing it applies immediately via the same path `CmbLanguagePill_SelectionChanged` uses so the wizard re-renders its own strings).
   - **Your own content** folder row (optional; the existing deferred `PickAssetsFolderRequested` mechanism stays).
   - **Age checkbox**: "I am 18 or older and I have read the content policy." with the policy link (`https://app.cclabs.app/policies/content` - check `ContentPolicyWarningDialog.PolicyUrl` and reuse the same constant).
   - Primary button **Enter** is disabled until the checkbox is ticked. Ticking + Enter sets `HasAcceptedAgeVerification = true` and saves. Small muted line under it: "Not for you? Just close this window."
   - Closing the window (X, Esc) on step 1 without accepting = `Application.Current.Shutdown()`, exactly what the old MessageBox "No" did. Hand the first run back first (`HandBackFirstRun("age gate declined")`) so a later launch gets the screen again.
   - Remove the "tips" and "performance" blurbs from step 1 (they move to the ? help panel, which already has them).
2. **Step 2 = Flavour** (canvas "Flavour" board): keep the existing card list and download machinery. Buttons: secondary **Keep the default**, primary **Enter with {mod name}** (or "Enter" when CCP Default is selected). No Back-to-step-1 age re-check. Footer hint "Offline? The download waits. No second ask."
   - **Kill the re-arm.** `SetModStepOffline` must NOT set `ModPickerShown = false` any more. An offline first run keeps `ModPickerShown = true` and the picker never fires standalone later; the Mod Manager / Library owns downloads from then on. Keep `ModPickerOfflineOffers` incrementing for diagnostics only. Update `ModPickerOfflineLatchTests` expectations for the wizard path (the standalone `ModPickerDialog.ShowIfNeeded` upgrader path may keep its own re-arm; do not change `Dialogs/ModPickerDialog.xaml.cs`).
3. **Delete step 3** entirely: `Step3` grid, `Doors`, `BuildDoorRows`, `StartTourRequested`, the tour launch in `Run`, `fr8_tour_*` strings. `StepCount = 2`.
4. **Age gate in `App.xaml.cs`**: keep the MessageBox ONLY for the population `Welcomed == true && HasAcceptedAgeVerification != true` (an old install that somehow never accepted). When `Welcomed == false` the wizard owns the gate, so skip the MessageBox.
5. **Far side of the wizard** (`Run`): after `ShowDialog` returns and the folder picker (if requested) has run, call `App.Startup?.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10))`. Lane C creates that API; if it is not on your branch yet, leave a clearly marked one-line `// LANE C: App.Startup?.BeginFirstLaunchQuiet(...)` comment at the exact spot and say so in your report.
6. Tests: render test still passes with two steps; the age checkbox gates Enter; close-without-accept hands the first run back (pure logic: extract a `FirstRunGate` static with `Decide(accepted, closedWithoutEnter)` if that keeps it testable without WPF).

### Lane B - EMI offers the walk directly, knock retired (worktree `C:/wt-fr-b`, branch `fr/b-emi-offer`)

Files: `Services/EmiDesk/EmiKnock.cs`, `Services/EmiDesk/EmiDeskService.cs` (knock region ~990-1090 and the summon hand-off ~340-350), `Services/EmiDesk/EmiState.cs`, `Controls/EmiDock.xaml.cs` (knock region), `Resources/emi/desk-lines.json` (firstContact pools), `docs/emi-desk/WAVE1-CONTRACT.md` (update), `Tests/.../EmiKnockMachineTests.cs`.

Today: chip flashes 3 times; if the user clicks, she is summoned and opens with `firstContact` whose ask carries `tour:shortwalk`; a shrug earns one re-offer on a later launch (`OfferCap = 2`, `firstContactLater`).

Target: **she asks directly, once, non-modally.** When `MayKnock` says yes, the chip pulses AND EMI Desk is summoned straight away (`why: "knock"`) opening with the `firstContact` moment and its ask ("Walk with me? 90 s" style copy; Yes -> `tour:shortwalk`, No -> nothing). No click needed. There is never a second offer: `OfferCap = 1`, delete the `firstContactLater` beat and `LaterMoment`, and `Population` returns `None` for upgraders (the upgrade tour is offered by What's New / the Welcome-back sheet, lane E). A dismissed desk after the ask counts as "no".

- Keep `EmiKnockMachine` pure. Keep brakes 1 (answered), 2 (now cap 1), 4 (tour done). Keep all gates in `MayKnock` and ADD one: `w.Quiet` must be false is WRONG here - the offer is the ONE thing allowed inside the quiet window, so do not gate on it. Do gate on `WizardUp`, `UpdateDialogUp`, `SessionRunning`, `TutorialOpen`, `WindowUsable`, `DeskEnabled`, `AlreadyOut` as today.
- `EmiDeskService.TryKnock`: after `NoteKnocked` and `KnockRequested`, call the summon path directly with the contact moment (do not wait for `OnChipClick`). Make sure the existing "knock answered" hand-off in the summon (~line 342) still routes the moment.
- Write the `firstContact` ask copy so the three or four pool lines all end with the walk offer (check `EmiOffers.cs` for how an ask renders its Yes/No chips and the `tour:` effect).
- Tests: update `EmiKnockMachineTests` for cap 1, no later moment, upgraders = None. Add a test that a `Knocked` state with 1 offer is never owed again.

### Lane C - StartupPresenter, quiet window, Inbox (worktree `C:/wt-fr-c`, branch `fr/c-presenter`)

Files (new): `Services/Startup/StartupQueueCore.cs`, `Services/Startup/StartupPresenter.cs`, `Services/Startup/InboxItem.cs`, `Controls/InboxFlyout.xaml(.cs)`, `Tests/.../StartupQueueCoreTests.cs`.
Files (edit): `App.xaml.cs` (create `App.Startup` early in OnStartup; route `ReportFailedUpdateAttemptAsync`, the update-available dialog wait at ~3818-3860, `CheckCloudSettingsRestoreAsync` wait at ~3612, remote-media intro at ~285), `MainWindow/MainWindow.xaml` (Inbox glyph + badge in the title bar next to the existing ? / update button), `MainWindow/MainWindow.xaml.cs` (constructor startup ladder ~535-672, `QueueEmiKnock` ~4022), `MainWindow/MainWindow.Marquee.cs` (`TryPresentSeasonRecap`, `ShowWhatsNewIfNeeded`, `CheckServerAnnouncement`, `CheckIntakePassNudge`), `MainWindow/MainWindow.DeeperTab.cs:492`, `MainWindow/MainWindow.Patreon.cs:990` (`MaybeShowPremiumCelebration`), `MainWindow/MainWindow.TabNavigation.cs` (`StartNavDoorHeaderPulse`, `OnDashboardTabVisibilityChanged`), `Windows/FeatureIntroPopup.xaml.cs` (`ShowWhenStartupSettles`, `ShowCore`), `Services/Descent/DescentMigrationService.cs` (~380: hold the ceremony while quiet, replay on `QuietChanged`), `App.xaml.cs:3262` (item-unlock toasts).

1. **Core** (`StartupQueueCore`, pure): a priority queue of `(key, priority, seq)`; `Next()`; quiet-window state machine with inputs `modalUp, tutorialActive, sessionRunning, firstLaunchUntilUtc, now`; `ShouldInbox(kind)`. Tests cover ordering, FIFO within priority, dedupe by key, quiet expiry, and "modal never starts while update dialog / tutorial active".
2. **Presenter** (WPF shell): `EnqueueModal` posts a pump at `DispatcherPriority.Normal`; the pump waits (500 ms polls, max 5 min per surface, same idiom the code uses today) for `App.IsUpdateDialogActive`, `App.Tutorial.IsActive` and `MainWindow.IsLoaded`, then runs one `show(owner)` inside `IsStartupDialogShowing = true/false`. Owner = `App.MainWindowRef ?? Application.Current.MainWindow`.
3. **Ladder rewrite in `MainWindow` ctor**: keep `FirstRunWizard.ShouldRunAndClaim()` and the `knockSeenVersion` snapshot exactly where they are. First-launch branch: `EnqueueModal("first-run-wizard", 20, owner => FirstRunWizard.Run(owner))` and keep the EMI HOLD/ReleaseHold around it. Else branch: What's New -> `EnqueueModal("whats-new", 30, ...)`, season recap -> `EnqueueModal("season-recap", 40, ...)` (only if the recap's own predicate says it fires; keep the predicate synchronous where it is and only move the presentation into the lambda), upgrader mod picker -> `EnqueueModal("mod-picker", 50, ...)`. The 1500 ms `Task.Delay` + 600-iteration polls that exist only to wait for the ladder go away; `QueueEmiKnock` becomes "after the ladder drains" (subscribe to a presenter `Drained` event or poll `IsModalUp`).
4. **Inbox UI**: a title-bar button with a count badge, hidden at 0. Clicking opens `InboxFlyout` (a `Popup`, dark theme, `Foreground` explicit, minimal): rows of glyph + title + summary, each with Open and a small dismiss "x". Opening runs the original surface. Keep it tiny and easy on the eyes (feedback_minimal_ui). Strings via `Loc.Get` with English fallback (`inbox_title` "Inbox", `inbox_empty`, `inbox_open`, `inbox_dismiss`).
5. **Routing**: each surface listed under "Inbox routing" above wraps its `Show()`/`ShowDialog()` in an `InboxItem` and calls `App.Startup.PresentOrInbox(item)`. The surface's own one-shot bookkeeping must stay on the surface (spend the flag when the popup actually opens, exactly as today), never at inbox time. For `FeatureIntroPopup.ShowWhenStartupSettles` replace the 1 s timer with `PresentOrInbox`; `ShowCore` keeps its existing guards.
6. **Descent ceremony**: while `IsQuiet`, take the existing `_offerHold` (`DescentMigrationService.HoldOffers/ReleaseOffers` or equivalent) and release on `QuietChanged` when quiet ends; also post an Inbox item "The Descent" whose Open releases the hold immediately.
7. Nav pulses skip while quiet. Item-unlock toasts (`App.xaml.cs:3262`) go to the Inbox as one summarised item while quiet.

### Lane D - server truths (worktrees `C:/wt-fr-d` on branch `fr/d-server-truths`, and `C:/wt-ccp-server-firstrun` on `feat/announcement-dismissal` off CCP-Server `main`)

Client files: `Services/Progression/SeasonRecapService.cs` (+ pure helper), `MainWindow/MainWindow.Marquee.cs` (ONLY the small insert in `TryPresentSeasonRecap` and the `CheckServerAnnouncement` filter), `Windows/AnnouncementPopup.xaml.cs` (`DismissAndClose`), `Services/Settings/ProfileSyncService.cs` (new `DismissAnnouncementAsync(string id)` next to the other V2 calls, using the existing `X-Auth-Token` helper at ~3952), `Models/AppSettings.cs` (nothing new unless needed), `Tests/.../SeasonKeyAdoptionTests.cs` (+ new cases).
Server file: `C:/wt-ccp-server-firstrun/proxy/server.js` (announcement region ~20150-20345, and a new `/v2/announcement/dismiss`).

1. **Season adopt silently.** New pure helper `SeasonRecapService.ShouldAdoptSilently(string? lastSeasonSeen, string? statsSeason, bool serverConfirmed)` = `serverConfirmed && string.IsNullOrEmpty(lastSeasonSeen) && string.IsNullOrEmpty(statsSeason)`. In `TryPresentSeasonRecap`, right after `lastSeasonSeen` is read and BEFORE `monthRolled`: if `ShouldAdoptSilently(...)`, set `LastSeasonResetSeen = currentSeason`, `SeasonStatsSeason = currentSeason`, `SeasonResetPending = false`, save, log "adopted server season {S} silently (fresh settings)", return. A fresh device therefore never announces a rollover it did not witness. Tests: fresh + server-confirmed adopts; fresh + wall-clock does not; existing user with `lastSeasonSeen` set still rolls.
2. **Per-account announcement dismissal.**
   - Server: `POST /v2/announcement/dismiss` body `{ unified_id, announcement_id }`, `isValidUnifiedId` + `validateAuthToken(req, user)` like the other V2 routes, rate-limit with the `rateLimitIncr` helper (INCR + EXPIRE NX; never SET-NX + INCR), cap `announcement_id` at 50 chars, store on the user record as `dismissed_announcements: string[]` (most recent 20, dedupe). `GET /config/announcement?unified_id=`: after loading the user, if the candidate id (per-user OR global) is in `dismissed_announcements`, return `{ enabled: false }` for it (fall through from per-user to global if only the per-user one is dismissed). No new top-level `require('crypto')`.
   - Client: `ProfileSyncService.DismissAnnouncementAsync(id)` fire-and-forget (log at Debug on failure). `AnnouncementPopup.DismissAndClose` default branch: after recording `DismissedAnnouncementId`, call it when `UnifiedId` is set. Also treat the primary action (link open) as a dismissal for this purpose.
   - Keep the local `DismissedAnnouncementId` as the offline fallback.
   - Do NOT deploy. Commit on the server branch and report the exact `npx vercel --prod` step for the owner (see memory: git push does not deploy).

### Lane E - Welcome-back sheet (runs AFTER lane C merges; worktree `C:/wt-fr-e`, branch `fr/e-welcome-back`)

Files (new): `Dialogs/WelcomeBackSheet.xaml(.cs)`, `Tests/.../WelcomeBackSheetRenderTests.cs`, `Tests/.../WelcomeBackDecisionTests.cs`.
Files (edit): `App.xaml.cs` (`CheckCloudSettingsRestoreAsync` becomes the trigger for the sheet), `Localization/Languages/en.json`.

Trigger population: `Settings.WasSettingsFileMissing == true`, not a factory reset, and a cloud identity is present (existing 5 s wait, then also re-check when `ProfileSync.ProfileLoaded` fires for the first time this launch, because on a new PC the sign-in usually happens after the wizard). Show at most once per launch; enqueue at priority 30 via `App.Startup.EnqueueModal("welcome-back", 30, ...)`.

Sheet content (canvas "WelcomeBack" board): "Welcome back, {display name}." subline "Level {n} · {active mod from the backup} · backup from {date}". Toggle rows: (1) **Restore my settings from the cloud backup** (on by default when a backup exists; hidden when none) - explains it replaces defaults on this PC and the content folder stays; (2) **Bring the flavour with me** (only when the backup's `ActiveModId` maps to a pack via `ModPackCatalog.PackIdForMod` and it is not installed) - downloads in the background after Enter via the existing `PendingModActivation` + `ReleaseContent` download path the wizard uses; (3) **What changed since you were last here** collapsed expander with the patch notes (`UpdateService.CurrentPatchNotes`) and a "Show me around (60 s)" link that starts `TutorialType.UpgradeTour` after the sheet closes (same deferred pattern the What's New dialog uses); (4) one muted line for the season, ONLY when the server said so: reuse the text from `TryPresentSeasonRecap`'s rotation branch, gated on `SeasonRecapService.IsSeasonKeyServerConfirmed`. Primary button **Let's go**.

On Let's go: apply restore via the existing `ProfileSync.RestoreSettingsFromCloudAsync` + `App.ApplyRestoredSettings` (no follow-up MessageBoxes; a failed restore becomes an Inbox item "Restore failed" with an Open that retries). Peek the backup BEFORE showing so the sheet can print level and mod: call `RestoreSettingsFromCloudAsync` once to fetch (it does not apply), keep the object, apply only if the toggle is on.

Decision logic (`WelcomeBackDecision.Decide(...)`, pure) is unit tested: no identity -> no sheet; factory reset -> no sheet; identity + no backup + level < 2 -> no sheet; identity + backup -> sheet with restore row; mod row only when pack maps and not installed.

## Integration (main session, after lanes report)

Merge order: C, then A, B, D into `feat/first-run-redesign`; build + full test run; then launch E; merge E; final build + tests; update `docs/first-run/REDESIGN-PLAN.md` status; PR against `main`.

## Manual verification checklist (owner, real machine)

- Fresh settings file, offline: Welcome -> Flavour -> Home; no age MessageBox; no picker later; no recap box; EMI asks once after the wizard; Inbox badge shows the announcement instead of a popup.
- Fresh settings file, sign in with an account that has a backup: Welcome-back sheet once, nothing else; season line only if the server rolled.
- Existing install upgrading: What's New once, recap only if the server rolled, EMI does not knock, announcements already dismissed on another PC do not replay.
