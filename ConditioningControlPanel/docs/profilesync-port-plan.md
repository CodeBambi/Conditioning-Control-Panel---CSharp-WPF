# ProfileSyncService — Slice-by-Slice Port Plan

> **Status:** IN PROGRESS — **Slices 1–3 landed unwired.** Slice 1 (`c4b2583a`): seam + 18 DTOs +
> HMAC `SignRequest` + 5 tests. Slice 2 (`a3215fc9`): injectable-`HttpMessageHandler` test ctor +
> heartbeat + 4 tests. Slice 3 (`fafd22b0`): pull + full merge — `LoadProfileAsync` +
> `MergeCloudProfile`/`MergeV2CloudStatsIntoLocalProgress`/`MergeV2SyncResponse`/`ApplyForceStreakOverride`/
> `ApplyForceSkillsReset` + 3 real `IQuestService` DIM methods + 5 tests, **independently adversarially
> reviewed (SAFE TO BANK, all 5 never-lower invariants byte-faithful; one drift found+fixed).** **Core
> 184/184, still NOT in DI (byte-identical app behavior).** Slices 4–7 remain (fresh-context). This is the sole remaining WS0 merge-`5ce70de6`
> re-open (parity-matrix row 1). Execution is a **fresh-context task**: the surface is ~2,800 LOC
> and **security-sensitive** (HMAC anti-cheat signing, GDPR delete/export, and the P0 privacy
> `ExcludedBackupProperties` list — omitting it leaks the auth token to the server). It must be
> implemented with full review capacity, slice-by-slice, each slice `slnf 0 · WPF sln 0 · Core
> tests green · smoke baseline-clean` before the next. **Do NOT land a partial (slice-1-only)
> registration** — a DIM-default `IProfileSyncService` in the DI graph that silently no-ops is a
> stub and REGRESSES the current clean "absent + documented-deferred" state (DoD line 290 forbids
> stubs for shipped features). Ship slices 1–7 as one coherent effort (or at minimum never leave a
> live no-op registered between sessions).
>
> **Provenance:** produced 2026-07-04 by a read-only archaeology sweep of the WPF
> `ConditioningControlPanel/Services/Settings/ProfileSyncService.cs` (~2,900 LOC). All facts are
> file:line-grounded against `feat/crossplatform`. The server contract lives IN the WPF code — this
> is a faithful port, not a design-from-scratch; no external server docs are required.

**Scope:** Port the WPF server-sync surface into `CCP.Core` (+ Avalonia DI wiring). The **local**
prestige/season-reset behavior already landed in **lot 7** and must **not** be re-ported — the
ported service CALLS those seams.

**Base URL (const, WPF line 25):** `https://codebambi-proxy.vercel.app`

---

## 1. Public Surface Inventory

WPF class: `public class ProfileSyncService : IDisposable` (line 23). No interface today. Static
singleton in WPF `App.xaml.cs`: `public static ProfileSyncService ProfileSync` (`:318`),
instantiated `:1405` with no ctor deps — reaches everything through `App.*` statics (`App.Settings`,
`App.Patreon`, `App.Discord`, `App.Progression`, `App.Achievements`, `App.Quests`,
`App.ActivityTracker`, `App.IsLoggedIn`, `App.IsSessionRunning`).

### Public members
| Member | Line | Purpose |
|---|---|---|
| `bool IsSyncEnabled` | 55 | `_syncEnabled && App.IsLoggedIn` gate |
| `DateTime? LastSyncTime` | 60 | Last successful sync (drives cooldown) |
| `string? LastSyncError` | 65 | Last error surfaced to UI |
| `int ConsecutiveSyncFailures` | 70 | Health counter |
| `event EventHandler<int>? SyncHealthChanged` | 76 | Fires on failure-count change |
| `event EventHandler? ProfileLoaded` | 82 | Fires after cloud pull+merge (MainWindow refresh) |
| `void StartHeartbeat()` | 100 | Starts 120 s heartbeat timer |
| `void StopHeartbeat()` | 121 | Stops heartbeat (logout/shutdown) |
| `Task<bool> LoadProfileAsync()` | 206 | **Pull** cloud profile → merge local |
| `Task<bool> SyncProfileAsync()` | 417 | **Push** local progression → cloud |
| `Task<(bool,string?,int?)> UseOopsieInsuranceAsync(string fixDate)` | 1950 | Server-validated streak recovery (−500 XP) |
| `Task<(bool,string?)> PurchaseSkillAsync(string skillId)` | 1997 | Server-authoritative skill purchase |
| `Task<(bool,string?,string?)> ChangeDisplayNameAsync(string newName)` | 2116 | Unique display-name change |
| `Task<(bool,string?)> DeleteAccountAsync()` | 2162 | GDPR account delete (confirmation `"DELETE"`) |
| `Task<(bool,string?,string?)> ExportDataAsync()` | 2208 | GDPR data export (pretty JSON) |
| `Task<bool> BackupSettingsAsync(bool force=false)` | 2409 | Cloud settings backup (gzip+base64, 5-min debounce) |
| `Task<SettingsBackupInfo?> GetSettingsBackupInfoAsync()` | 2524 | Backup metadata probe |
| `Task<AppSettings?> RestoreSettingsFromCloudAsync()` | 2568 | Download+decompress cloud settings |
| `Task<int> RecordEasterEggReadAsync()` | ~2627 | Easter-egg reader counter |
| `void Dispose()` | ~2673 | Stop heartbeat + dispose HttpClient |

### Private helpers (behavioral core)
| Member | Line | Purpose |
|---|---|---|
| `bool IsPatreonAuth` / `IsDiscordAuth` / `string? GetAccessToken()` | 40/45/50 | Legacy OAuth token selection (Patreon→Discord) |
| `Task SendHeartbeatAsync()` | 132 | V2 `/v2/user/heartbeat` (+ legacy fallback) |
| `Task<bool> TryHealDefaultsFromServerAsync(string unifiedId)` | 355 | Read-only heal when local looks like fresh defaults (#293) |
| `void MergeCloudProfile(CloudProfile)` | 1027 | Take-higher merge of pulled profile |
| `bool MergeV2CloudStatsIntoLocalProgress(Dictionary<string,object>)` | 1633 | Merge V2 stats dict into local progress |
| `void ApplyForceStreakOverride(V2StreakStats)` | 1884 | Adopt server streak even if lower |
| `void ApplyForceSkillsReset(int?)` | 1929 | Clear skills + refund on server flag |
| `static void AddAuthHeader(HttpRequestMessage)` | 2256 | Adds `X-Auth-Token` |
| `Task<bool> HandleUnauthorizedAsync(HttpResponseMessage)` | 2268 | 401 → 5-min-cooldown recovery |
| `Task<bool> TryRecoverAuthTokenAsync()` | ~2300 | `/v2/auth/restore-session` recovery |
| `static void SignRequest(HttpRequestMessage, string body)` | 2355 | HMAC-SHA256 anti-cheat signing |

Public model at file end: `public class SettingsBackupInfo` (line 3011) — **not in Core yet**.

---

## 2. Server Contract (the reference to replicate faithfully)

All calls use `HttpClient` (30 s timeout, ctor lines 88-91: default `X-Client-Version` + `User-Agent`
headers). Base = `ProxyBaseUrl`.

| # | Method · Path | Site | Auth | Request body | Response DTO |
|---|---|---|---|---|---|
| 1 | POST `/v2/user/heartbeat` | 147 | `X-Auth-Token` | `{unified_id, is_active, in_session, app_version}` | (status only) |
| 2 | POST `/user/heartbeat[-discord]` | 177 | `Bearer` OAuth | `{}` | (status only) — legacy |
| 3 | GET `/user/profile[-discord]` | 268 | `Bearer` OAuth | — | `ProfileResponse` — V1 pull fallback |
| 4 | POST `/v2/user/sync` | 548 | `X-Auth-Token` **+ HMAC sign** | big `v2SyncData` (below) | `V2SyncResponse` |
| 5 | POST `/user/sync[-discord]` | 959 | `Bearer` OAuth | `ProfileSyncData` | `SyncResponse` — legacy |
| 6 | POST `/v2/user/use-oopsie` | 1961 | `X-Auth-Token` | `{unified_id, fix_date}` | `OopsieSuccess/ErrorResponse` |
| 7 | POST `/v2/user/purchase-skill` | 2015 (+retry 2028) | `X-Auth-Token` | `{unified_id, skill_id, skill_points}` | `PurchaseSkillResponse` |
| 8 | POST `/v2/user/change-display-name` | 2127 | `X-Auth-Token` | `{unified_id, new_display_name}` | `ChangeDisplayName[Error]Response` |
| 9 | POST `/v2/user/delete-account` | 2173 | `X-Auth-Token` | `{unified_id, confirmation:"DELETE"}` | `DeleteAccount[Error]Response` |
| 10 | POST `/v2/user/export-data` | 2219 | `X-Auth-Token` | `{unified_id}` | raw JSON (pretty-printed) |
| 11 | POST `/v2/user/backup-settings` | ~2495 | `X-Auth-Token` | `{unified_id, settings_data(gzip→b64), app_version}` | (status only) |
| 12 | POST `/v2/user/settings-backup` | 2531 / 2585 | `X-Auth-Token` | `{unified_id}` | `SettingsBackupResponse` |
| 13 | POST `/v2/easter-egg` | ~2635 | `X-Auth-Token` (optional) | `{unified_id}` or `{}` | `EasterEggResponse` |
| 14 | POST `/v2/auth/restore-session` | ~2315 | `X-Auth-Token` | `{unified_id, client_version}` | `{auth_token?}` |

**`v2SyncData` push payload (lines 500-542):** `unified_id, xp, level, achievements[],
stats{completed_sessions, longest_session_minutes, highest_streak, total_flashes, consecutive_days,
total_bubbles_popped, total_video_minutes, total_lock_cards_completed, lifetime_points_spent,
daily_quest_streak, last_daily_quest_date, quest_completion_dates[], total_daily_quests_completed,
total_weekly_quests_completed, total_xp_from_quests, daily_quests_completed_today,
daily_completion_reset_date}, unlocked_skills[], skill_points, total_conditioning_minutes,
companion_progress, allow_discord_dm, show_online_status, share_profile_picture, reset_weekly_quest,
reset_daily_quest, force_streak_override, force_skills_reset`. **This push IS the leaderboard submit**
(server ranks on xp/level).

### Auth / signing
- **V2 primary:** `X-Auth-Token: <settings.AuthToken>` (`AddAuthHeader`, 2256).
- **HMAC-SHA256 signing** (`SignRequest`, 2355) — **applied only to `/v2/user/sync`**: key =
  UTF8(`{unifiedId}:ccp-anticheat-2026`), payload = `{unixSeconds}:{body}`, emits `X-CCP-Timestamp`
  + `X-CCP-Signature` (lowercase hex). The app key is an embedded obfuscation constant, not a real
  secret — port verbatim.
- **Legacy V1:** `Authorization: Bearer <oauthToken>` from `App.Patreon`/`App.Discord`.

### Retry / backoff / conflict resolution
- **Cooldown:** `SyncCooldown = TimeSpan.FromSeconds(30)` (line 415), enforced client + server;
  concurrent calls gated by `SemaphoreSlim _syncGate` (`WaitAsync(0)`, line 432).
- **429:** stamps `LastSyncTime = now` to defer next attempt (lines 562-568).
- **401:** `HandleUnauthorizedAsync` → 5-min cooldown → `TryRecoverAuthTokenAsync`
  (`/v2/auth/restore-session`); token is **kept**, never cleared, on failure. `purchase-skill`
  retries once after recovery (2025-2031).
- **Conflict resolution (take-higher / union, in `SyncProfileAsync` V2SyncResponse handling, lines
  578-670+):** skill_points = `Math.Max(server, local)`; unlocked_skills = union (skipped on
  `level_reset`); `lifetime_points_spent` reconciled monotonically via
  `App.Achievements.ReconcileLifetimePointsSpent`; `force_streak_override`/`force_skills_reset`
  adopt server even if lower (guarded by `PendingSkillsResetAck` to survive crashes); oopsie season
  compared against `SeasonRecapService.CurrentSeasonKey`.

---

## 3. Sync Triggers (when it fires)

- **Startup pull + heartbeat:** `App.xaml.cs:1860-1861` (Patreon), `:1953-1954` (Discord).
- **Heartbeat timer:** `DispatcherTimer`, `HeartbeatIntervalSeconds = 120` (const line 26);
  immediate first tick (line 116).
- **On exit:** `App.xaml.cs:3071` — `SyncProfileAsync().Wait(TimeSpan.FromSeconds(2))` after
  `Settings.SaveImmediate()`.
- **Manual/event-driven push** (`SyncProfileAsync`): login `MainWindow.Login.cs:128/131/139/282`;
  Patreon validation `MainWindow.Patreon.cs:730/766/787`; level-up `ProgressionService.cs:120`;
  skill purchase/reset `SkillTreeService.cs:130/455/836`; quest completion `MainWindow.Quests.cs:78`;
  presets `MainWindow.Presets.cs:1169`; leaderboard open `MainWindow.Leaderboard.cs:303`; UI updates
  `MainWindow.UiUpdates.cs:589`.
- **Settings backup** via `ISettingsService.SaveImmediate` → `_backupProvider.BackupSettingsAsync`
  (already wired in Core `SettingsService.cs:361`).
- **No periodic full-sync timer** — only the heartbeat is timer-driven; full sync is event-driven.

---

## 4. Local-vs-Server Split (what NOT to re-port)

**LOCAL — already ported in lot 7, DO NOT re-port (the ported service CALLS these):**
- Prestige `lifetime_points_spent` monotonic reconcile → `Core
  Services/Progression/AchievementService.cs:773 ReconcileLifetimePointsSpent(long)` (seam
  `IAchievementService.cs:173`).
- `PermanentIds` season-reset skill pruning → `Core Models/SkillTree.cs:49 PermanentIds` +
  `AvaloniaSkillTreeService.OnSeasonReset` (seam `App.cs:161 OnSeasonReset`).
- `SeasonRecapService.TrackPointsSpent` / `CurrentSeasonKey` (Core `Services/SeasonRecapService.cs:20/39`).

**SERVER-BOUND — still absent, this is the port:** profile push (`/v2/user/sync`), profile pull
(`/user/profile` + V2 heal), cloud settings backup/restore, `purchase-skill`, `use-oopsie`,
`change-display-name`, `delete-account`, `export-data`, `easter-egg`, heartbeat, `restore-session`.
**Leaderboard SUBMIT** = the xp/level fields of the sync push (leaderboard **READ** already exists:
`Core Services/Progression/LeaderboardService.cs` → `/v3/leaderboard`, `/user/lookup`).

---

## 5. Data Models

**Already in Core (reuse, do not duplicate):** `AppSettings` (`Core/Models/AppSettings.cs` — has
`AuthToken` :3853 [secure], `UnifiedId` :3842, `CompanionProgressData` :2932, `OfflineMode` :1854,
`ShowOnlineStatus`/`AllowDiscordDm`); `CompanionProgress` (`Core/Models/CompanionProgress.cs`);
`SkillDefinition`/`PermanentIds` (`Core/Models/SkillTree.cs`).

**Must move/recreate in Core** (currently `private` nested in the WPF DTOs region, lines 2682-3009,
+ `SettingsBackupInfo` at 3011): `ProfileResponse`, `CloudProfile`, `SyncResponse`,
`ProfileSyncData`, `V2SyncResponse`, `V2SyncUser`, `V2StreakStats`, `OopsieSuccessResponse`,
`OopsieErrorResponse`, `PurchaseSkillResponse`, `ChangeDisplayNameResponse`,
`ChangeDisplayNameErrorResponse`, `DeleteAccountResponse`, `DeleteAccountErrorResponse`,
`SettingsBackupResponse`, `SettingsBackupData`, `EasterEggResponse`, and **public**
`SettingsBackupInfo`. All use `Newtonsoft.Json` `[JsonProperty]` (Core already depends on Newtonsoft).

---

## 6. Seam Design

**Verdict: the whole service is portable Core.** It needs only `HttpClient` (cross-platform),
`ISettingsService` (`Current.AuthToken`/`UnifiedId` — token security handled transparently, §7),
`ILogger<T>`, and sibling Core services. **No new platform seam** — `ISecretStore` is reached
indirectly via `SecureAuthTokenStore.Wire` behind `AppSettings.AuthToken`.

Closest existing Core precedents to copy:
- `Core Services/RemoteControl/RemoteControlService.cs` — `sealed class : IRemoteControlService`,
  ctor `(ISettingsService, ILogger<T>, optional executors)`, owns its `HttpClient`, `AuthPostAsync`
  helper reads `_settingsService.Current?.AuthToken` → `X-Auth-Token` (:784-796). **Copy verbatim.**
- `Core Services/Progression/LeaderboardService.cs` — registered from Avalonia
  `ServiceCollectionExtensions.cs:182-190` with `ISettingsService` + `IUserIdentityProvider` +
  version + logger.

**Proposed seam:** `CCP.Core/Services/Settings/IProfileSyncService.cs` — all members **DIM-defaulted**
(interface-breakage avoidance so existing test fakes keep compiling):

```csharp
public interface IProfileSyncService
{
    bool IsSyncEnabled { get; }
    DateTime? LastSyncTime { get; }
    string? LastSyncError { get; }
    int ConsecutiveSyncFailures { get; }
    event EventHandler<int>? SyncHealthChanged;   // events cannot be DIMs — impls declare them
    event EventHandler? ProfileLoaded;
    void StartHeartbeat() { }  void StopHeartbeat() { }
    Task<bool> LoadProfileAsync() => Task.FromResult(false);
    Task<bool> SyncProfileAsync() => Task.FromResult(false);
    Task<(bool,string?,int?)> UseOopsieInsuranceAsync(string fixDate) => Task.FromResult((false,(string?)null,(int?)null));
    Task<(bool,string?)> PurchaseSkillAsync(string skillId) => Task.FromResult((false,(string?)null));
    Task<(bool,string?,string?)> ChangeDisplayNameAsync(string newName) => Task.FromResult((false,(string?)null,(string?)null));
    Task<(bool,string?)> DeleteAccountAsync() => Task.FromResult((false,(string?)null));
    Task<(bool,string?,string?)> ExportDataAsync() => Task.FromResult((false,(string?)null,(string?)null));
    Task<bool> BackupSettingsAsync(bool force = false) => Task.FromResult(false);
    Task<SettingsBackupInfo?> GetSettingsBackupInfoAsync() => Task.FromResult<SettingsBackupInfo?>(null);
    Task<AppSettings?> RestoreSettingsFromCloudAsync() => Task.FromResult<AppSettings?>(null);
    Task<int> RecordEasterEggReadAsync() => Task.FromResult(-1);
}
```

> NOTE on the DIM-vs-events tension: the two `event` members cannot be default interface members, so
> any existing `IProfileSyncService` test fake would need to declare them. There is no fake today
> (the seam is new), so this is a non-issue at introduction — but keep the seam new-only and do not
> retrofit it onto an existing faked interface.

Concrete `Core.Services.Settings.ProfileSyncService : IProfileSyncService, IDisposable`. Sibling
access: prefer injected `IProgressionService` (`GetTotalXP`, `App.cs:288`), `IAchievementService`
(`ReconcileLifetimePointsSpent`), `ISkillTreeService`; `App.Quests`/`App.Achievements` are `object?`
in `Core/App.cs` (:76-98) so route through typed seams. Register in Avalonia
`ServiceCollectionExtensions.cs` next to `IRemoteControlService` (:258).

**Reconcile with two existing seams (decisions in §10):**
1. `ISettingsBackupProvider` (`Core/Services/Settings/ISettingsBackupProvider.cs`) — today the
   local-file **stopgap** `AvaloniaSettingsBackupProvider` (its own doc-comment: *"stopgap until full
   cloud profile sync is ported from WPF"*). The ported `BackupSettingsAsync` should become the real
   cloud impl behind this seam.
2. `IV2AuthService`/`AvaloniaV2AuthService` (`CCP.Avalonia/Services/Auth/AvaloniaV2AuthService.cs`)
   already implements `/v2/auth/*`, `GetUserProfileAsync`, `SendHeartbeatAsync`, `DeleteAccountAsync`,
   `ApplyUserDataToSettings` — keep **auth** there; `ProfileSyncService` owns **progression sync +
   backup + purchase + GDPR export**. Ensure a **single** heartbeat owner (avoid double traffic).

---

## 7. Security Constraints (HARD — P0)

- **Token at rest is already solved cross-platform:** `AppSettings.AuthToken` get/set route through
  `Core.Services.SecureAuthTokenStore` (`AppSettings.cs:3855-3856`), which `Wire`s to `ISecretStore`
  per head. The ported service reads `settings.AuthToken` (transparently decrypted) — **never**
  persist the token into JSON or a field.
- **No token logging in the WPF source** — every log uses `{Status}`/`{Error}`/`{UnifiedId}`, never
  the token or `Authorization`/`X-Auth-Token` value. Preserve this; port no plaintext-token log.
  (Grep audit confirms zero token-value logging.)
- **Settings-backup exclusion list is a privacy guardrail — PORT VERBATIM.** `ExcludedBackupProperties`
  (lines 2384-2404) strips `AuthToken, OpenRouterApiKey, UnifiedId, PlayerLevel/XP, SkillPoints,
  UnlockedSkills, HighestLevelEver, IsSeason0Og, CurrentSeason, PendingSkillsResetAck,
  UserDisplayName, PatreonTier/PremiumValidUntil, LastPatreonVerification, CustomAssetsPath,
  DiscordWebhookUrl, LastSeenUtc` before upload. **Omitting it would leak the auth token / API key to
  the server.**
- **The only token write path** (`restore-session` adopting a rotated `auth_token`, ~line 2335) goes
  through the secure setter — safe.
- **Consent/version:** no new data categories, no camera/biometric surface, token handling unchanged
  → **no `ConsentVersion` bump required**.

---

## 8. Slice Breakdown (each build+test-gateable, dependency-ordered)

**Slice 1 — Seam + DTOs + skeleton. ✅ DONE (`c4b2583a`, landed UNWIRED).** Created
`CCP.Core/Services/Settings/IProfileSyncService.cs` (DIM defaults, slice-N tags), `.../ProfileSyncDtos.cs`
(18 DTOs verbatim + public `SettingsBackupInfo`), `.../ProfileSyncService.cs` (ctor `(ISettingsService,
ILogger)`, own `HttpClient`, `AddAuthHeader` + static-internal `SignRequest` HMAC helper + `Dispose`;
async members inherit DIM defaults tagged for later slices), `tests/CCP.Core.Tests/ProfileSyncServiceTests.cs`
(5 tests: SignRequest determinism/known-triple/empty-noop + 2 DTO round-trips). **AMENDMENT applied:
NOT registered in DI and no call site changed** — app behavior byte-identical, so there is NO live
no-op stub (the earlier "do not end a session on slice 1 alone" hazard is avoided precisely because
slice 1 landed unwired). `HandleUnauthorizedAsync`/`TryRecoverAuthTokenAsync` deferred to slice 4
(they need the sync flow). Core 175/175. **Remaining slices 2–7 wire + implement real behavior.**

**Slice 2 — Heartbeat. ✅ DONE (`a3215fc9`, landed UNWIRED).** Added the injectable-`HttpMessageHandler`
internal test ctor (unblocks all later slice tests). `StartHeartbeat`/`StopHeartbeat`/`SendHeartbeatAsync`
→ `/v2/user/heartbeat` with `System.Threading.Timer` (120s + immediate tick, idempotent, disposed).
Guards mirrored (disposed/OfflineMode/!IsSyncEnabled/empty unifiedId); 401 minimal path + `// slice 4`
note for full recovery. `in_session` via optional `ISessionService` seam (`State != Idle`); `is_active`
conservative default + `// slice 7` note (no clean Core idle seam). 4 tests, Core 179/179. **The app
startup + dispose/exit wiring of `StartHeartbeat`/`StopHeartbeat` is deferred to slice 7** (kept unwired).

**Slice 3 — Pull + merge (largest logic slice). ✅ DONE (`fafd22b0`, landed UNWIRED, independently
reviewed).** `LoadProfileAsync` (GET `/user/profile` w/ `X-Auth-Token`, double-parse `ProfileResponse`
+ `V2SyncResponse`, raise `ProfileLoaded`) + `MergeCloudProfile` + `MergeV2CloudStatsIntoLocalProgress`
+ `MergeV2SyncResponse` (extracted from WPF `SyncProfileAsync` V2 block :578-780 — shared home for the
slice-4 push) + `ApplyForceStreakOverride` + `ApplyForceSkillsReset`. Reused `ReconcileLifetimePointsSpent`
+ `CurrentSeasonKey`. **Seam extension:** 3 real `IQuestService` methods added as DIM no-op defaults
(WPF head + consumers still compile) with real `QuestService` overrides — `RecalculateStreak` promoted
private→public (WPF `:1024-1026` tail restored per review), `ForceRegenerate{Daily,Weekly}Quest` wrap
existing generators. All-optional ctor deps. **DEFERRED (additive/edge, don't affect never-lower):**
`TryHealDefaultsFromServerAsync`, Patreon entitlement heal, background sync-up push (→slice 4),
season-recap UI. 5 merge tests, Core 184/184. Adversarial reviewer confirmed all 5 corruption-critical
never-lower invariants byte-faithful.

**Slice 4 — Push.** `SyncProfileAsync` (`/v2/user/sync`), `SignRequest` on body, `_syncGate`, 30 s
cooldown, 429 handling, full `V2SyncResponse` reconciliation. Wire event triggers
(progression/quests/exit) in the Avalonia head. *Success:* push guard blocks fresh-defaults (Level 1,
<100 XP, `!_hasLoadedProfile`); signed request verified in test.

**Slice 5 — Cloud settings backup/restore.** `BackupSettingsAsync` (+`ExcludedBackupProperties`,
gzip+base64, 5-min `Interlocked` debounce), `GetSettingsBackupInfoAsync`,
`RestoreSettingsFromCloudAsync`. Replace the local-file `AvaloniaSettingsBackupProvider` stopgap by
making `ProfileSyncService` the real `ISettingsBackupProvider` cloud impl (or compose). *Success:*
exclusion strip + compress/decompress round-trip test passes; `SaveImmediate` path still green.

**Slice 6 — Server-authoritative actions.** `PurchaseSkillAsync` (+ wire into
`ISkillTreeService.PurchaseSkillAsync`, which delegates to `App.ProfileSync` in WPF
`SkillTreeService.cs:130`), `UseOopsieInsuranceAsync`, `ChangeDisplayNameAsync`. *Success:* purchase
reconciles skill_points (max) + unlocked_skills (union) + `TrackSkillPointsSpent`/
`ReconcileLifetimePointsSpent`; one-shot 401 retry works.

**Slice 7 — GDPR + misc + final wiring.** `DeleteAccountAsync`, `ExportDataAsync`,
`RecordEasterEggReadAsync`; repoint remaining Avalonia head call sites to the seam; remove the
stopgap; update the WPF-parity matrix row 1. *Success:* full-solution build + Windows smoke; parity
matrix marks ProfileSync ported. *(Slices 6+7 may merge if diffs stay small.)*

*V1 OAuth/`Bearer` fallback endpoints #2/#3/#5 are intentionally out of scope — see §10.3.*

---

## 9. Test Strategy (`tests/CCP.Core.Tests`, xUnit v3)

Follow the lot-7 pattern: `[AvaloniaFact]`, self-contained `TestAppEnvironment : IAppEnvironment`,
`DebugLogger<T>`, `Dispatcher.UIThread.RunJobs()` (see `AchievementServiceLot7Tests.cs`,
`SettingsSerializationTests.cs`, `SettingsServiceFileIoTests.cs`). Add `ProfileSyncServiceTests.cs`
with an injectable `HttpMessageHandler` fake (**no live server**):

- **`SignRequest` determinism** — fixed `unifiedId`+body+timestamp → known lowercase-hex HMAC;
  asserts `X-CCP-Timestamp`/`X-CCP-Signature` present. Pure function, highest-value test.
- **`ExcludedBackupProperties` strip** — serialize settings carrying
  `AuthToken`/`UnifiedId`/`OpenRouterApiKey`, run the backup strip, assert none survive;
  gzip→base64→gunzip round-trip fidelity.
- **Merge take-higher/union** — raises skill_points to `Math.Max`, unions skills, never lowers;
  `level_reset` skips union.
- **Push guard** — fresh defaults returns false without a POST; `_syncGate` blocks concurrent;
  30 s cooldown honored.
- **401 handling** — stub 401 → asserts `restore-session` attempted once within the 5-min window and
  token retained.

---

## 10. Risks / Decisions Needed

1. **Overlap with `IV2AuthService`/`AvaloniaV2AuthService`** — already implements heartbeat,
   `GetUserProfileAsync`, `DeleteAccountAsync`, `ApplyUserDataToSettings`. **Decision:** partition
   responsibilities (auth stays there; progression-sync/backup/purchase/GDPR-export go to
   ProfileSyncService) and ensure a **single** heartbeat owner.
2. **`ISettingsBackupProvider` reconciliation** — replace the local-file stopgap with the cloud impl,
   or compose (local snapshot + cloud). Affects `SettingsService.SaveImmediate` (`SettingsService.cs:361`).
3. **V1 OAuth/`Bearer` fallback (#2/#3/#5)** — depends on `App.Patreon`/`App.Discord.GetAccessToken()`
   which have **no Core seam**. **Recommendation:** skip V1 fallback (unified_id/`X-Auth-Token` path
   only) unless a live-server check proves V2-less users still exist; porting it otherwise needs a new
   OAuth-token seam.
4. **Server response shapes** are known **only** from the DTOs in this file (authoritative per the
   task). Unknown fields are silently dropped, matching WPF.
5. **Typed sibling access** — `App.Quests`/`App.Achievements` are `object?` in `Core/App.cs`; confirm
   `IQuestService`/`IAchievementService` expose everything `MergeV2CloudStatsIntoLocalProgress` reads
   (quest completion dates, totals) or route through them.
6. **`ForceSkillsReset` crash-safety** relies on `AppSettings.PendingSkillsResetAck` persistence —
   verify that property exists and persists in Core `AppSettings`.

---

**Verified facts:** base URL, all 14 endpoints, HMAC scheme + embedded key, `SyncCooldown = 30 s`,
heartbeat = 120 s, exit-sync 2 s wait, `SecureAuthTokenStore→ISecretStore` token security, exclusion
list, the two Core precedents (`RemoteControlService`, `LeaderboardService`), and the lot-7 local
reconcile seams.
