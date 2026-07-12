# WPF Window-Awareness → AI Reaction — Port Contract (AI-1/AI-2/AI-10)

Extracted read-only from the frozen WPF head (wpf-archaeologist, 2026-07-12). Citations = `ConditioningControlPanel/` WPF reference. The port must preserve this contract; WPF is never modified.

## Engine (AI-1)
- **Poll:** `DispatcherTimer`, Interval **1.5s**, started in `Start()`. `Services/UI/WindowAwarenessService.cs:344-350`.
- **Foreground read:** `GetForegroundWindow()` → `GetWindowText()` (Unicode, 512 buf). **Window TITLE only** — no process name, no PID. `:65-70`, `:545-553`.
- **Idle:** title unchanged AND `(now - lastActivityChange) >= 5min` → category `Idle` name "being idle". `IdleThresholdMinutes=5` `:96`, logic `:479-490`.
- Classification runs **only on title change** (`:493-495`). No background process scan (deliberately removed) `:498-499`.

### Classification `CategorizeWindow(title)` `:563-641`
Lowercases title, substring `Contains` against keyword→display dicts (`OrdinalIgnoreCase`), **first match wins**, priority:
1. Gaming `:573-578` → 2. Learning `:581-588` → 3. Shopping `:591-598` → 4. Social `:601-608` → 5. Media `:611-618` → 6. Working `:621-628` → 7. browser fallback (chrome/firefox/edge/safari/opera/brave → `Browsing`, extract tab) `:631-638` → 8. else `(Unknown,"something","","")` `:640`.
Categories 2–6: `ExtractPageNameWithService()` `:696-720` splits on ` - ` / ` — ` / ` | `, first segment = PageTitle, display "{firstPart} on {serviceName}". Browser tab strip+trim 50 `:646-682`.
`IsCategoryEnabled` always true `:729-733`. Fine-grained `(appCluster,appId)` via `AppClusterMap.Classify` `:532-535` (map contents not extracted — extract separately if barks need exact ids).

### Events (both `EventHandler<ActivityChangedEventArgs>`) `:69-70`
Payload `:29-57`: Category, PreviousCategory, DetectedName, ServiceName(→DetectedName fallback), PageTitle, IsNewService, PreviousServiceName, AppCluster, AppId.
- **ActivityChanged** fires in `SetActivity()` `:531-542` **only when category OR detectedName changed** (`:531` = debounce).
- **StillOnActivity** milestones `{1,5,10}` min via `_stillOnTimer` `:410`; reset index on every change, arm only if category not Unknown/Idle `:416-427`; each tick fires only if still non-Unknown/Idle `:459-476`.
- **Lifecycle:** `Start()` no-ops unless `AwarenessModeEnabled && AwarenessConsentGiven` `:337-341`. `Stop()` stops+nulls both timers, category→Unknown `:359-373`. `Dispose()`→Stop `:735-742`.
- **Cooldown gate (service-side):** `CanReact()`/`CanStillOnReact()` vs `AwarenessReactionCooldownSeconds`; `MarkReaction()`/`MarkStillOnReaction()` stamp `:376-405`.

## Enable / consent (AI-1) — REUSE, do not invent
- Both flags true: **`AwarenessModeEnabled` AND `AwarenessConsentGiven`** `CCP.Core/Models/AppSettings.cs:2934,:2945`.
- Consent auto-granted on enable toggle (no dialog) `MainWindow.Patreon.cs:1142-1143`.
- **NO awareness consent VERSION** (plain bool). **Port must reuse `AwarenessConsentGiven` — no new consent surface / no ConsentVersion bump.**
- **NOT premium** — "free for all users" `MainWindow.Patreon.cs:862-863`.
- Cooldown: `AwarenessReactionCooldownSeconds` default **10**, clamp 10–600 `AppSettings.cs:2951-2959`.

## Consumers (AI-2) — after engine
1. **Avatar reaction on ActivityChanged** `AvatarTube/AvatarTubeWindow.Reactions.cs:34-117`: UI-marshal; gates = startup cooldown, skip if speech bubble visible, IsCategoryEnabled, CanReact→MarkReaction; if `AiChatEnabled && Ai.IsAvailable` → `Ai.GetAwarenessReactionAsync(displayName, category, serviceName, pageTitle)` `:79`; deliver via speech bubble (PlayDoubleBounce+GigglePriority; preset fallback Giggle) `:100-107`.
2. **Avatar reaction on StillOnActivity** `:123-207`: same gates w/ CanStillOnReact; 50/50 ServiceName vs PageTitle `:150-153`; `GetStillOnReactionAsync(displayName,category,duration)` `:163`.
3. **Double-click 1-in-4 comment** `AvatarTube/AvatarTubeWindow.ChatInput.cs` `TriggerActivityCommentAsync`: TriggerMode shows custom trigger first `:191-200`; else `_interactionCount++; if %4!=0` preset+return, every 4th → AI using CURRENT awareness ctx `:206-212,:224-245`; not CanReact-gated (user-initiated). (subsumes the current canned-Giggle stub AI-9.)

## Barks (AI-10) — after engine
`Services/Companion/BarkService.cs:536-549`: if awareness present, `ActivityChanged`→`Raise("ActivityChanged", activity=ServiceName, category, app_cluster=AppCluster, app=AppId)` `:538-545`; `StillOnActivity`→`Raise("StillOnActivity", activity=ServiceName, still_minutes)` `:546-548`. **No raw title in bark context.**

## PRIVACY (hard constraint — preserve exactly)
1. Raw foreground title: **memory-only** (`_lastWindowTitle` for change detection `:493-494`), **never disk, never network, never logged raw**. Class doc "never logs or stores window titles" `:60-62`; log uses derived name not title `:526-527`.
2. **Derived/truncated** service+page title IS transmitted to the cloud AI (`AiService.GetAwarenessReactionAsync` builds `[Category | App | Title | Duration]` `AiService.cs:150-164` → POST codebambi-proxy) — **only when** `AiChatEnabled && Ai.IsAvailable` and a reaction fires. AI off → nothing leaves the machine.
3. Nothing awareness-derived is ever written to settings.json or any file.
4. **DROP** the WPF debug line `:333-336` that logs the FULL raw title to Serilog (contradicts stated posture) — do NOT port it, to strictly preserve the memory-only posture.

## Out-of-scope flags (note, don't port yet)
`AwarenessIgnoreOwnUi`, `AwarenessLoopProtectionEnabled/Ms` `AppSettings.cs:4357-4380` (self-UI / loop guards) — not wired in the traced sites; port only if loop protection is needed. `AppClusterMap` table not extracted.
