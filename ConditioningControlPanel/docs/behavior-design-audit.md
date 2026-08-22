# CCP — Engagement, Retention & Monetization Audit

**Scope:** Read-only audit of the live code surface against behavior-design frameworks (Hook Model, Self-Determination Theory, Cialdini persuasion principles, monetization/pricing, measurement). Code is treated as source of truth; docs/comments were not trusted where they conflicted.

**Date:** 2026-06-01 · **Branch state:** v6.0.3 (Deeper Drop)

> This document maps what EXISTS and flags gaps/opportunities. It contains **no implementation and proposes no new tracking.** It is a brainstorming input.

---

# PART 1 — INVENTORY OF THE SURFACE

## 1.1 Gamification

### XP & Levels — `Services/ProgressionService.cs`
- **What:** Central progression backbone. `AddXP()` awards XP to both player and active companion. Five-tier level curve (linear 800→2500 XP/level for L1–80, escalating to 3% compound growth past L150). Each level grants 1 skill point. Anti-idle guard suppresses passive XP for idle users (`ProgressionService.cs:38-47`).
- **Trigger:** `AddXP(XPSource)` called from feature modules (Flash, Subliminal, BouncingText, Session, Quest, Other).
- **Gating:** Free. Level *unlocks* (spiral, pink filter, bubbles) are level-gated, not paid.
- **Purpose / status:** Active, not vestigial. Drives achievements, season stats, leaderboard rank.

### Achievements — `Services/AchievementService.cs`, `Models/Achievement.cs`, `AchievementPopup`
- **What:** ~42 achievements across Progression / TimeSessions / Minigames / Hardcore / Deeper / Creator. 28 free + 14 patron-exclusive (`IsExclusive`). Time-based (pink-filter minutes, spiral minutes, deeper playback), action-based, and combo (e.g. "System Overload" = bubbles+text+spiral simultaneously). Auto-popup + haptic on unlock. Auto-saves every 30s.
- **Trigger:** `TryUnlock(id)`; level-up checks; 1s DispatcherTimer for time tracking; routed events via `GamificationBridge`.
- **Gating:** 14 exclusive achievements require `HasPremiumAccess` via `TryUnlockExclusive()`. Earned-then-downgraded users keep them.
- **Status:** Active. One hidden/parked: `directors_cut` (gated on `Metadata.Featured`, which no code path sets — wired for a future server-driven source). No random reward in achievements themselves.

### Quests — `Services/QuestService.cs`, `QuestDefinitionService.cs`, `Models/Quest.cs`, `QuestCompletePopup`
- **What:** Daily quests (1 active, **max 3 completions/day**) + weekly quests (1/week, Mon–Sun UTC). Reroll: 1 daily + 1 weekly free, **+2 each for Patreon**. XP reward scales with player level (+4%/level), streak (+3%/day), and skill multipliers. Remote-definable via server (`QuestDefinitionService`), embedded fallback. **Daily-quest streak calendar** with auto-fill "streak shields" and "Oopsie Insurance."
- **Trigger:** 1-minute timer checks day/week rollover; progress tracked incrementally; auto-completes at target.
- **Gating:** Reroll bonus only. Definitions free.
- **Status:** Active. **This is the core daily-return hook.** Quest *selection* is randomized from a pool (mild variable element); rewards are deterministic-but-scaled.

### Skill Tree — `Services/SkillTreeService.cs`, `Models/SkillTree.cs`
- **What:** Server-authoritative skill purchases (via `ProfileSyncService`). Effects: XP multipliers (Sparkle Boost +10/15/20%, Streak Power +0.5%/day cap 15%, Night Shift / Early Bird +50% time-windowed), **Pink Rush** (random 50% roll every 10 min → 60s ×3 XP burst), **Lucky Flash** (5% → 10× XP), **Lucky Bubble** (5% → 20× points), streak shields, Oopsie Insurance (500 XP, once/season), Perfect Bimbo Week milestones (7/14/30 days).
- **Trigger:** Purchase → server → `ApplySkillEffects`; lucky procs rolled at feature events; Pink Rush on a 10-min timer.
- **Gating:** No skill is paid-gated; all bought with free skill points.
- **Status:** Active. **This is where nearly all the genuine variable/random reward in the product lives** (lucky procs, Pink Rush).

### Streaks — `Models/AchievementProgress.cs`, `QuestService.cs`, `SkillTreeService.cs`
- **What:** Two streaks: a launch/activity `ConsecutiveDays` and a calendar-based `DailyQuestStreak` (contiguous quest-completion days, 90-day window). Missed day breaks it unless a shield/insurance covers the gap. Peak streak captured for season recap.
- **Gating:** Shields are skill-based; insurance costs XP. Not paid.
- **Status:** Active. **Critical daily-return mechanic.**

### Seasons & Recap — `Services/SeasonRecapService.cs`, `Models/SeasonRecap.cs`
- **What:** Monthly UTC seasons (epoch Feb 2026 = Season 0). Live counters (conditioning minutes, sessions started, active days, peak streak, peak rank, per-feature use). On month rollover: snapshot captured → persisted → counters reset. Recap card shows stats + percentile + feature badges.
- **Trigger:** Startup checks month boundary; `CaptureAndRollover()`.
- **Gating:** Free.
- **Status:** Active. (See §1.6 for the shareable card.)

### Leaderboards — `Services/LeaderboardService.cs`
- **What:** Server-backed (proxy `/v3/leaderboard`). Monthly + all-time modes; 6 sort fields (level, xp, bubbles, flashes, video minutes, lock cards). Top 200/page, 30-min auto-refresh, server- or client-computed percentile.
- **Gating:** Free.
- **Status:** Active. No direct daily hook (soft: ranks drift).

### Roadmap — `Services/RoadmapService.cs`, `Models/RoadmapDefinition.cs`
- **What:** Linear 3-track narrative ("The Empty Doll" → "Obedient Puppet" → "Slutty Blowdoll"), 6 steps + boss each. Each step requires a **photo submission** saved to a local diary (`%LocalAppData%/.../roadmap_diary`). Completing a track's boss unlocks the next; finishing all earns "Certified Blowdoll." Tracks time-to-complete, notes, photos.
- **Gating:** Free.
- **Status:** Active. Long-form, multi-week progression. **High personal investment (user-supplied photos).**

### GamificationBridge — `Services/GamificationBridge.cs`
- Single wiring seam translating ~12 feature event streams into achievement unlocks. Active, well-maintained, not user-facing.

---

## 1.2 Companion AI

### Personality — `Services/CompanionService.cs`, `PersonalityService.cs`, `Models/CompanionDefinition.cs`, `PersonalityPresets.cs`, `CompanionPromptSettings.cs`
- **What:** 5 selectable companion identities (each maps to an avatar set + per-companion XP modifiers) and **7 built-in personality presets** (BambiSprite, SlutMode, GentleTrainer, StrictDomme, BimboCoach, HypnoGuide, BimboCow) with modular prompt sections (Personality, ExplicitReaction, KnowledgeBase, ContextReactions, OutputRules; responses capped ~15 words). Users can clone/edit presets.
- **Trigger:** Companion/personality dropdowns; `BambiSprite.GetSystemPrompt()` composes the system prompt fresh per request.
- **Gating:** SlutMode requires explicit-content acknowledgement; AI calls gated by daily limits (below). **Companion level-gating was removed** — all companions available from L1 (`CompanionService.cs:331-344`).

### Persistent memory — `Services/AIService/LocalAiService.cs`, `CompanionPromptSettings.cs`
- **What:** **Local-AI (Ollama) only:** persists up to 50 recent user/assistant dialogue pairs to `local_chat_history.json`, reloaded on launch; fires `PersistentMemoryRecalled` once/session. **Cloud AI (OpenRouter via proxy) is stateless — no cross-session memory.** In-app chat panel caps at 100 messages and does **not** persist to disk.
- **Critical gap:** No persistence of user name, preferences, or any user *facts*. Memory = raw dialogue transcript, local-only.

### Relational hooks — `Models/CompanionProgress.cs`
- **What:** Tracks companion level/XP, FirstActivated, TotalActiveTime — **nothing else**. No affection/intimacy/relationship dimension. No absence-aware greeting, no "missed you," no proactive re-engagement. Idle/trigger/bubble phrases are reactive timers, not relational.
- **Status:** This is the **single largest untapped relatedness surface** — see Parts 2 & 3.

### Avatar window & AI backend — `AvatarTubeWindow.xaml.cs`, `AvatarRandomBubble.cs`, `Services/AiService.cs`, `AIService/AiServiceStrategy.cs`
- Animated avatar (pose set scales with level), typewriter speech bubbles, idle/trigger phrase timers, clickable floating bubbles, occasional giggle (1-in-5).
- AI: cloud default (OpenRouter proxy, **100 free / 1000 Patreon requests/day**), live-switchable to local Ollama (unlimited). Falls back to canned phrases when rate-limited/offline.

---

## 1.3 Session System & Progressive Intensity

### Engine — `Services/SessionEngine.cs`, `SessionManager.cs`, `Models/Session.cs`
- **What:** 1s timer loop ramps feature values via **Lerp(start, end, elapsed/total)** — flash opacity & frequency, pink filter, spiral, bubble frequency (stepwise every 5 min), mind-wipe escalation. Named **phases** fire `PhaseChanged` at minute boundaries. **Pause penalty: −100 XP per pause.** Brain-drain ramp exists but is **disabled for performance** (`SessionEngine.cs:559-567,646`) — partially vestigial.
- **Within-session intensity:** Linear ramp from start→end values over duration.
- **Lifetime intensity:** Only the **XP multiplier** scales with player level (and a level-scaled duration bonus). Session *content* itself is static — no difficulty that auto-escalates across the user's lifetime.

### Authoring — `Models/TimelineSession.cs`, `TimelineEvent.cs`, `SessionEditorWindow`
- Users author sessions as timelines of feature start/stop events with per-event start/end ramp values. Difficulty (Easy/Medium/Hard/Extreme) auto-computed from duration + active features + intensity. **High user investment.**

### Logging & completion — `Services/SessionLogService.cs`, `SessionLog.cs`, `SessionCompleteWindow`
- Logs session metadata + media encountered (timestamps). Completion window shows recap (duration, XP, media list) + sound. No additional rewards at completion.

### Deeper — `MainWindow.DeeperHub.cs`, `Services/Deeper/`, `EnhancementLibrary`
- **CONFIRMED FREE & UNGATED.** No Patreon/premium/level gate anywhere around Deeper. All services eager-initialized unconditionally. `EnableDeeper` is a plain bool (default true). Demo enhancements seeded once on first launch. **Only catalogue *submission* requires an auth token** — browsing/playing/editing enhancements is unrestricted.

---

## 1.4 Notifications / Scheduling (return prompts)

| Mechanism | File | Brings user back? |
|---|---|---|
| In-app toasts (transient + sticky) | `NotificationService.cs` | No — in-app only; **no OS/push notifications anywhere** |
| Tray icon ("Wake Bambi Up!", background run) | `TrayIconService.cs` | Only while app is open/minimized |
| Windows startup launch | `StartupManager.cs` | Auto-launch on boot (opt-in) |
| **Autonomy** (idle/random/context triggers; can fire fullscreen web video) | `AutonomyService.cs` | **Strong — but only while app runs.** **Patreon-gated** (`HasPremiumAccess`) |
| **Scheduler** (auto-starts engine in user-set time windows) | `MainWindow.xaml.cs:435-457` | **Strong recurring** — but starts the *engine*, not the *app* (free) |
| Server marquee (5-min poll) + startup announcement popup | `AnnouncementPopup.xaml.cs`, `MainWindow.xaml.cs:~25750` | Soft; content-dependent. Announcement shows once per ID |
| What's New (version change) / Season Recap (month rollover) | `MainWindow.xaml.cs:15534-15679` | Soft nudge on next open |

**Key finding:** There is **no cross-session re-engagement** — nothing reaches the user once the app is closed. Every "return" mechanism requires the app to already be running (or set to launch at boot). This is the **single biggest structural gap in the retention loop.**

---

## 1.5 Onboarding / First-Run (activation)

Sequential: **Splash** (`SplashScreen.xaml.cs`) → **Welcome dialog** (`WelcomeDialog.xaml.cs`, sets `Welcomed`) → **Tutorial** (`TutorialService.cs` + `TutorialOverlay`, `FullTour` with coachmarks, ~15 tutorial types, demo actions fired live). Identity setup is **optional and user-triggered**: unified `LoginDialog` (Discord / Patreon / email device-code), or `OfflineUsernameDialog`; `DisplayNameDialog` for cloud display name. App is fully usable offline. Tutorial completion is tracked by step index but **not persisted** (re-runnable).

---

## 1.6 Tiers & Entitlement / Paywall

### Tier model — `Services/PatreonService.cs`, `Models/PatreonModels.cs`
- **Tiers:** `None` (0), `Level1` (≈$5, "AI Chatbot"), `Level2` (≈$10, "AI + Window Awareness"). Server-driven (`/patreon/validate`), 24h cache + a **14-day grace** (`PatreonPremiumValidUntil` / `HasCachedPremiumAccess`).
- **Whitelist = the only "lifetime"-like path:** `IsWhitelisted` server flag → treated as Level2. No purchasable lifetime/one-time option exists in-app.
- **⚠️ Major finding:** `HasAiAccess` and `HasPremiumAccess` have **identical definitions** — both are simply `CurrentTier >= Level1 || IsWhitelisted || cached` (`PatreonService.cs:122,127`). **Level2 unlocks nothing in code that Level1 doesn't.** The two-tier split is presentational only; the "Window Awareness = Level 2" claim in comments is not enforced by any code gate found.

### What's gated (behind Level1+ / `HasPremiumAccess`)
- **Autonomy / Bambi Takeover** (`AutonomyService.cs:487`) — translucent `BambiTakeoverGate` overlay when locked.
- **Keyword Triggers** (`KeywordTriggerService.cs:306`).
- **Haptics** (`MainWindow.xaml.cs:10361` — opacity 0.3, hit-test off).
- **Remote Control, Blink Trainer, Lockdown, Window Awareness** — gate overlays (`RemoteControlGate`, `BlinkTrainerGate`, `LockdownGate`, `AwarenessGate`).
- **14 exclusive achievements.**
- **AI chat:** not gated on/off by tier — free users get it after login at 100/day, Patreon at 1000/day.
- **NOT gated:** XP/levels, skill tree, quests (need login, any provider), Deeper, mods, wallpaper, leaderboards, season recap, roadmap.

### Paywall path (the actual conversion UX)
1. Free user sees locked feature with a **"✨ Premium" badge** (`submenu_premium_badge`) and/or a gate overlay with subtitle `gate_premium_subtitle` ("Unlock to interact with the controls below").
2. Clicks **"✨ Unlock with Patreon"** (`gate_unlock_with_patreon`, `BtnGateUnlock_Click` → `ShowAppInfoPopup()`).
3. Lands in the **App Info & Data popup** containing `PatreonLoginCard` → `BtnPatreonLogin_Click` → `HandleQuickPatreonLoginAsync()` → **OAuth opens the external browser** to Patreon's own checkout.

### Pricing presentation
- **No prices shown anywhere in-app. No anchoring, no decoy, no lifetime option, no tier comparison table.** Tier labels are flattering but uninformative: `label_patreon_tier_level1` = "Basic Subject - All features unlocked!", `level2` = "Prime Subject - All features unlocked!" — i.e., **both say the same thing**, reinforcing that the tiers are not differentiated. Conversion is a pure hand-off to external Patreon checkout with zero in-app value framing.

---

## 1.7 Season Recap / Shareable Card — `Services/SeasonRecapService.cs`, `CardExporter.cs`, `RecapTheme.cs`, `SeasonRecapWindow`
- Monthly recap card (level, XP, active days, peak streak, percentile, per-feature badges) with themes (`RecapTheme.cs`) and an exporter (`CardExporter.cs`) — i.e., a **shareable image artifact exists.** Triggered on month rollover (skipped for brand-new users, HighestLevel < 2). Whether/where sharing is surfaced in UI is worth confirming, but the export capability is present.

## 1.8 Deeper — see §1.3. **Free, ungated, confirmed.**

## 1.9 Catalogue / Mods / Enhancements (UGC)
- **Catalogue** (`CatalogueService.cs`, `CatalogueLookupService.cs`, `CatalogueSubmitDialog`): users **browse (unauthenticated) and submit (auth-token required)** Deeper enhancement bundles (`.ccpenh.json`), keyed by HypnoTube video URL. Server-backed (`app.cclabs.app/api/enhancements`). Moderation is **server-side only** (400 rejection); the `Moderation/` folder guards *companion prompts*, not catalogue.
- **Mods** (`ModService.cs`, `ModCreatorWindow`, `ModManifest.cs`, `BuiltInMods.cs`): a `.ccpmod` ZIP overriding theme colors, identity/labels, phrase pools, text replacements, trigger words, avatars, browser defaults, personalities. Full GUI creator. 4 built-ins (CCPDefault, BambiSleep, SissyHypno, Dronification). **Free, no tier gate.** Export = save file manually; **no in-app mod-sharing endpoint** (unlike catalogue). Creating a rich mod is **high-investment** (assets, tuning).
- **Content Packs** (`ContentPackService.cs`): encrypted asset ZIPs from CDN (`ccp-packs.b-cdn.net`), server manifest, auth required to download (tier check opaque in client).
- **Vestigial:** `CommunityPrompt.cs` + `CommunityPromptService.cs` — full download/install/activate/export implementation, **not wired into any UI.** Superseded by mods.

## 1.10 Community Hooks
- **Discord:** `DiscordService.cs` (OAuth + webhook), `DiscordRichPresenceService.cs` (broadcasts "Conditioning Control Panel" + state every 15s; off in offline mode). In-app Discord invite button. **No in-app contest/event/challenge surface found** (season recap is recap, not a live event). Invite codes exist only as a build-time skill, not an in-app referral surface.

## 1.11 Ambient Features
- **Wallpaper** (`WallpaperService.cs`): user-initiated random desktop wallpaper override from `assets/wallpapers/`, restores on deactivate; `Shuffle()` rotates. Free. **Not passive/idle-driven.**
- **ScreenMirror** (`ScreenMirrorService.cs`): user-initiated clone-mode switch for fullscreen video.
- **No screensaver, no idle slideshow, no cursor theming.** The "desktop presence" ambient surface is minimal and manually triggered.

---

# PART 2 — MAPPED AGAINST THE FRAMEWORKS

## 2.1 Hook Model (Trigger → Action → Variable Reward → Investment)

**External triggers (present):** Windows startup launch; scheduler engine auto-start; server marquee/announcement; Autonomy effects (Patreon); tray "Wake Bambi Up!". **All require the app to be running or boot-launched.**

**Internal triggers (present):** Daily quest cadence + streak-loss anxiety (the strongest internal pull); season-end FOMO; companion presence.

**Core action:** Open the app and run a session (or let the scheduler/autonomy run one).

**Variable reward (present but narrow):** Lives almost entirely in the **skill tree** — Lucky Flash (5%→10×), Lucky Bubble (5%→20×), Pink Rush (50%/10min → ×3 burst). Quest selection is randomized. The **companion's responses are AI-variable** (a genuine variable-reward surface that's under-leveraged for *return* specifically). Leaderboard rank drift is mild social variability. **Achievements, levels, sessions, recap are all deterministic.**

**Investment (strong — the product's biggest asset):**
- Authored timeline sessions (`TimelineSession`).
- Custom mods (themes, phrases, personalities) — high effort.
- **Roadmap photo diary** — deeply personal, sunk-cost content.
- XP/levels/skill tree/streak calendar — accumulated, partly cloud-synced.
- Companion progression + (local) chat history.
- Catalogue submissions.

**WEAKEST LINK: the external trigger after the app is closed.** Investment and internal triggers are strong, but **nothing re-enters the user's day from outside the app** — no push/OS notification, no scheduled "your streak resets in 2h," no email, no companion-initiated outreach. A user who forgets to open CCP simply loses their streak silently. The loop is well-built *inside* the app and **open at the re-entry point.** Secondary weak link: variable reward is mechanical (RNG procs) rather than relational/narrative, so it doesn't compound with the investment surface.

## 2.2 Self-Determination Theory (Autonomy / Competence / Relatedness)

- **Autonomy (well served):** session authoring, mod creation, personality editing, offline mode, extensive settings, scheduler control, choice of companion. The product strongly respects user control.
- **Competence (well served):** XP, 150+ levels, skill tree, achievements, quests, difficulty tiers, leaderboard, roadmap mastery path, season percentile. Mastery signaling is dense.
- **Relatedness (MOST UNDERSERVED):** Despite a *companion* being the headline feature, the relational layer is thin — **no persistent memory of the user as a person, no affection/relationship progression (`CompanionProgress` tracks only level/time), no proactive or absence-aware contact, cloud companion is stateless.** Community is one-directional (leaderboard ranks, a Discord link, Rich Presence broadcast) with **no in-app social interaction, co-op, gifting, or shared events.** The framework with the clearest headroom is **relatedness** — and it's the one the product's own premise (an AI companion) most implies.

## 2.3 Persuasion Principles (Cialdini)

| Principle | Present? | Where (live UI) |
|---|---|---|
| **Commitment/Consistency** | **Strong** | Streak calendars, roadmap photo-diary commitment, authored sessions, level investment |
| **Authority** | **Moderate** | Companion as Domme/trainer/guide personas (`PersonalityPresets`); structured roadmap "certification" |
| **Scarcity** | **Weak/mechanical** | Season-end reset; Pink Rush 60s window; daily quest cap (3/day); reroll limits. No paid-side scarcity (no limited offers, no countdowns on upgrade) |
| **Social Proof** | **Weak** | Leaderboard (top 200), catalogue view counts, percentile in recap. No "X users active," no testimonials, no popularity signals on mods/enhancements at point of choice |
| **Reciprocity** | **Mostly absent** | Free demo enhancements seeded on first run is the only gesture. No gift/bonus/"here's a free week," no companion gifting, no daily login reward framed as a gift |
| **Liking** | **Moderate** | Companion personality, giggles, custom display name. Undercut by lack of personalization/memory |
| **Unity** ("one of us") | **Largely absent in-app** | Identity/role language ("Subject," "doll," "Certified Blowdoll") creates in-group framing, but no in-app community belonging beyond a Discord link; Rich Presence is the only outward "I'm part of this" signal |

**Most underused for monetization:** Reciprocity, Social Proof, and Scarcity are **absent specifically at the paywall** — the upgrade path has zero persuasion scaffolding (no proof others upgraded, no gift/trial, no time-bound offer, no value framing).

## 2.4 Monetization / Pricing (as the user sees it)

- **Tier structure the user perceives:** A "Premium"/"Exclusives" tab with `✨ Premium` badges on locked features; two flattering tier names that **both say "All features unlocked."** No prices, no comparison, no decoy, no anchor, no lifetime. 
- **In code, the two tiers are functionally identical** (§1.6) — so a user paying $10 (Level2) over $5 (Level1) gets no code-gated benefit; the differentiation, if any, lives entirely on Patreon's own page, invisible in-app.
- **Conversion path:** locked feature → "✨ Unlock with Patreon" → App Info popup → Patreon OAuth → **external browser checkout.** The most important moment (the value pitch + price) happens entirely outside the app, where CCP has no control over framing.
- **Anchoring/decoy/lifetime:** none present.
- **Whitelist** is the only permanent-access concept and is admin-granted, not a sellable SKU.

## 2.5 Measurement (what's knowable today, privacy-respecting)

**Measurable (server/proxy-side or syncable):**
- Per-user level/XP/achievements/quest progress (cloud-synced via `ProfileSyncService`) → can infer progression and broad activity for *logged-in* users.
- Leaderboard sort fields (bubbles, flashes, video minutes, lock cards, level, xp) → coarse engagement proxies.
- Season counters (active days, sessions started, conditioning minutes, peak streak, per-feature use) — captured into snapshots.
- Patreon validation calls → subscription state, tier, whitelist.
- Catalogue submissions, content-pack/auth token exchanges, announcement `unified_id` pings, marquee fetches → server-observable touchpoints.
- Discord Rich Presence / OAuth.

**Blind spots:**
- **Activation funnel:** Welcome/tutorial completion is **not persisted** → no read on whether new users finish onboarding.
- **Retention cohorts:** No cross-session "last seen"/return-frequency signal is collected for offline-mode users; for cloud users it's inferable only indirectly from synced counters.
- **Conversion funnel:** Because the paywall hands off to external Patreon, there's **no in-product signal of who hit a gate, clicked "Unlock," and converted** — the funnel is invisible at exactly the decision point.
- **Churn timing / why:** No mechanism observes *when* a user stops returning or *which* gated feature drove (or failed to drive) an upgrade.
- Local-only users (offline username) are essentially unmeasured by design.

> Local-first/privacy posture is a deliberate strength; the point here is simply to state **what is currently knowable** vs. not — not to propose new tracking.

---

# PART 3 — OPPORTUNITIES + RISK FLAGS

## 3.1 Opportunities (each tagged: framework · surface size)

1. **Close the re-entry gap with a respectful local/OS reminder for streak-at-risk.** A single opt-in Windows toast ("your X-day streak resets at midnight") would repair the Hook Model's weakest link without any new server tracking. *Hook (external trigger) · Small–Medium.*
2. **Give the companion persistent memory of the user (name, a few preferences, last-session reference) and an absence-aware greeting.** This is the highest-leverage move: it directly serves the most-underserved SDT need and turns the AI's existing variable output into a *relational* variable reward. *SDT-Relatedness + Hook (variable reward) + Liking · Medium–Large.*
3. **Add a relationship/affection progression to `CompanionProgress`** (already the obvious home — it tracks only level/time today), with gentle unlocks tied to consistency. *SDT-Relatedness + Commitment · Medium.*
4. **Differentiate the two Patreon tiers in code, or collapse to one.** Today $10 buys nothing $5 doesn't — either give Level2 a real exclusive or stop presenting two tiers. *Monetization integrity · Small (decision) / Medium (if adding tier value).*
5. **Build an in-app value screen before the Patreon hand-off** (what you unlock, with social proof + a clear benefit list). The conversion decision currently happens off-app with zero framing. *Persuasion (social proof, reciprocity) + Monetization · Medium.*
6. **Surface the shareable Season Recap card prominently and make sharing one-click** (the `CardExporter` already exists). *Social Proof + Unity · Small.*
7. **Add social proof at points of choice:** view/like/install counts and "popular this week" on catalogue enhancements and mods. *Social Proof · Small–Medium.*
8. **Introduce a reciprocity gesture** (e.g., a framed daily login bonus, or a one-time "gift from Bambi" of a premium feature trial). *Reciprocity + reduce paywall friction · Small–Medium.*
9. **Persist onboarding/activation completion** so the team can even *see* the activation funnel (read-only flag, no behavioral tracking). *Measurement · Small.*
10. **Lean into Unity with an in-app community surface** (featured community mods/enhancements, contributor credit) rather than only an external Discord link. *Unity + Social Proof · Medium.*
11. **Retire or wire up the vestigial `CommunityPromptService`** — either delete it or fold its sharing model into the catalogue. *Hygiene · Small.*
12. **Optional lifetime/founder SKU as a price anchor** (also serves users who dislike subscriptions) — but see risk flags re: pricing transparency. *Monetization (anchoring) · Medium.*

## 3.2 Risk Flags (near-the-line mechanics)

- **No cross-session re-engagement is *safe today*, but adding aggressive push/notification re-engagement would be the easiest thing to push too far** given the content domain — any reminder system must be opt-in and frequency-capped, or it reads as compulsion engineering. *(Engineered compulsion risk if over-built.)*
- **Streak-loss + Oopsie Insurance (500 XP, once/season) + paid reroll bonuses** sit on the standard "loss-aversion daily streak" line. Currently mild (no money buys streak protection), but **monetizing streak recovery would cross into dark-pattern territory.** *(Compulsion / pay-to-relieve-anxiety.)*
- **Two paid tiers that are functionally identical in code, both labeled "All features unlocked," with no in-app pricing.** A payment processor or user could read undifferentiated tiers + absent pricing as **obscured/misleading value.** *(Obscured pricing / fairness.)*
- **Paywall hands entirely off to external checkout with no in-app price or terms.** Users commit to recurring billing having seen no price inside the product they're upgrading. *(Pricing transparency.)*
- **Autonomy can trigger intrusive fullscreen web video and effects** based on idle/keyword triggers. It is consent-gated (`AutonomyConsentGiven`) and Patreon-only, which is good — but it is the mechanic most likely to be read by a platform as **non-consensual takeover** if the consent/stop affordances ever weaken. *(Platform/consent risk — keep consent + kill-switch prominent.)*
- **Roadmap photo diary** stores personal user photos locally. High-investment and retention-positive, but a **sensitive-data custody responsibility** — any future cloud sync of these would be a significant trust/legal escalation. *(Data sensitivity / trust.)*
- **Content domain + achievement/escalation framing** ("Certified Blowdoll," escalating intensity, hardcore achievements): inherent to the product, but the **combination of escalation mechanics + compulsion loops + a vulnerable use-context** is the cluster a platform reviewer would scrutinize first. Keep escalation user-initiated (it currently is) rather than auto-ramping across lifetime. *(Predatory-perception risk.)*
- **`directors_cut` hidden achievement gated on a `Featured` flag no code sets** — harmless now, but server-driven hidden unlocks can drift toward manipulative "mystery" mechanics if expanded. *(Watch-item.)*

---

## PAUSE

Inventory (Part 1), framework mapping (Part 2), and opportunities + risk flags (Part 3) are complete and grounded in the live code with file/line citations. Two findings worth foregrounding for the brainstorm: **(1)** the retention loop is strong on investment and internal triggers but **open at the re-entry point** (nothing reaches a closed app), and **(2)** **relatedness** — the need the companion premise most implies — is the most underserved framework, while the **two paid tiers are functionally identical in code with no in-app pricing.** Ready to brainstorm from here.
