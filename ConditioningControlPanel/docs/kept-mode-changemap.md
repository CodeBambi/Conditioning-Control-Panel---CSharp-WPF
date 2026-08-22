# "Kept" Mode — Change Map & Mode Architecture Audit

> **Discovery / mapping document only.** No mechanics, gameplay, session, or runtime
> behavior is changed by this document. It maps every touchpoint required to add a new
> themed mode ("Kept" — a goon / chastity / beta / locked cluster) as **assets + wording**,
> and identifies what would have to change in code vs. what is already data-driven.
>
> **No explicit/pornographic media is bundled, referenced, or scaffolded anywhere.** The
> app ships original first-party assets only; all flash/video/gif/subliminal-image content
> is **user-supplied** through the existing assets pipeline (see §6). "Kept" reuses that
> same pipeline unchanged.

---

## TL;DR — The single most important finding

**Modes are not a feature. They are `.ccpmod` mods.** Sissy, Drone, Bambi Sleep, and the
neutral "CCP Default" are all defined as `ModManifest` objects in
`Models/BuiltInMods.cs`, registered into `Services/ModService.cs`, and served to the
entire app through a `ActiveMod → CCP Default` fallback chain. The Drone mode is *literally*
shipped as a bundled `DroneMod/drone-mode.ccpmod` zip that overrides its own hardcoded
manifest at runtime.

Everything that differs per mode — theme colors, companion name, the user term, the 27
companion phrase pools, subliminals, lock-card phrases, triggers, attention-check messages,
browser defaults, enhancement-tree labels, avatar art, achievement art, **and** the
wholesale renaming of every global achievement/feature/skill via `TextReplacements` — is
**manifest data**, resolved through `ModService` accessors and `ModResourceResolver` asset
lookups.

So **"Kept" can ship as a built-in mode with effectively zero mechanics code**: one new
`ModManifest` factory in `BuiltInMods.cs` (or a bundled `.ccpmod`), an asset folder, and a
handful of localization keys for the onboarding/mode-picker chrome. The only things that are
*not* yet pure data are listed in the deltas (§7): a hardcoded `CompanionId` enum, an
unwired `Personalities` manifest section, and a few built-in-only secondary-color/onboarding
hooks.

---

## 1. Mode architecture — definition, registration, selection, switching

| Touchpoint | File | What exists now | For "Kept" | Tag |
|---|---|---|---|---|
| Mode = mod manifest | `Models/ModManifest.cs:10-83` | `ModManifest` is the whole shape of a mode: id/name/version/author + optional `Theme`, `Identity`, `SubliminalPool`, `LockCardPhrases`, `CustomTriggers`, `Triggers`, `Messages`, `Browser`, `Phrases`, `Personalities`, `TextReplacements`, `EnhancementOverrides`, `TubeLayout`, `SupportedAvatarSets`, `CustomAvatarSets`. | Add one `CreateKept()` `ModManifest` (built-in) **or** author a `kept.ccpmod`. | [CONFIG] |
| Built-in mode definitions | `Models/BuiltInMods.cs:9-1768` | Four static factories: `CreateCCPDefault()`, `CreateBambiSleep()`, `CreateSissyHypno()`, `CreateDronification()`. IDs at `:11-17`. `Dronification` even uses the community `drone-mode` id to avoid collision with the on-disk `.ccpmod`. | Add `KeptId` const + `Kept { get; } = CreateKept()` + a `CreateKept()` factory mirroring the existing pattern. | [CONFIG] |
| Registration | `Services/ModService.cs:64-89` | Constructor instantiates each built-in as `new ModPackage(BuiltInMods.X, null, isBuiltIn:true)` into `_installedMods`; CCP Default is `_baseMod` (the fallback root). | Add `var keptMod = new ModPackage(BuiltInMods.Kept, null, true); _installedMods[keptMod.Id] = keptMod;`. | [CODE] (1-line registration) |
| Bundled `.ccpmod` override | `Services/ModService.cs:1009-1080` | `_bundledBuiltInMods` array pairs a shipped `.ccpmod` path with a built-in id; `ExtractBundledBuiltInMods()` extracts to `%APPDATA%/builtin_mods/<id>/`, re-extracts when the package is newer, sanitizes, forces the id, and re-registers the package **with an `InstalledPath`** so assets resolve. `IsBuiltIn` stays true (can't be uninstalled). | If "Kept" ships its own art, add a `("KeptMod/kept.ccpmod", BuiltInMods.KeptId)` tuple and bundle the package. Otherwise the hardcoded manifest alone is fine (assets fall back to app defaults). | [CONFIG] + [ASSET] |
| Active-mod persistence | `Models/AppSettings.cs` `ActiveModId`; `App.xaml.cs` calls `ModService.Initialize(Settings.Current.ActiveModId)` | Persisted string; unknown/uninstalled ids fall back to CCP Default (`ModService.cs:94-105`). | No change — "Kept" id is valid automatically once registered. | — |
| Runtime switching | `Services/ModService.cs:489-528` | `ActivateMod(modId)`: saves current pools for the old mod, swaps `_activeMod`, restores per-mod pools, `ModResourceResolver.ClearCache()`, auto-switches companion if unsupported, fires `ModChanged`. | No change — generic over any registered mod. | — |
| Per-mod pool persistence | `Services/ModService.cs:1115-1158` | `SubliminalPoolByMod` / `AttentionPoolByMod` / `LockCardPhrasesByMod` / `CustomTriggersByMod` keyed by mod id, so each mode keeps user edits independently. | No change. | — |
| Live theme re-apply | `MainWindow.xaml.cs:~1210-1362` `RefreshThemeAwareElements()`; `App.xaml.cs` `ModChanged` subscription | On `ModChanged`, accent/bg/panel/surface/secondary brushes + direct element colors are rewritten via `DynamicResource`; feature names/images/tooltips refreshed. No restart needed. | No change — reads "Kept" theme generically. | — |

**Lifecycle end-to-end:** `BuiltInMods.CreateX()` → `ModPackage` in `ModService._installedMods`
→ (optional) bundled `.ccpmod` extracted over it with an `InstalledPath` → `Initialize()`
selects the persisted `ActiveModId` → all reads go through `ModService` accessors + the
`ActiveMod→CCP Default` fallback chain → `ActivateMod()` swaps live and fires `ModChanged`
→ UI recolors/relabels via `DynamicResource` + `ModResourceResolver.ClearCache()`.

---

## 2. Avatars + AI companion personality

| Touchpoint | File | What exists now | For "Kept" | Tag |
|---|---|---|---|---|
| Built-in companions | `Models/CompanionDefinition.cs:9-16` | `enum CompanionId { OGBambiSprite=0, CultBunny=1, BrainParasite=2, BambiTrainer=3, BimboCow=4 }`, each hardcoded to avatar sets 3–7 with an XP bonus type. | A new *built-in* companion needs a C# enum value — **but this is not required** for a new mode. | [CODE] (only if you want a hardcoded companion) |
| Avatar set support | `Models/ModManifest.cs:78-95`; `Services/ModService.cs:374-396, 750-774` | `SupportedAvatarSets` (which of sets 1–7 appear) and `CustomAvatarSets` (set #≥8, label, unlockLevel). Custom sets validated (≥8, unique, level 1–9999) and **are consumed** (`GetCustomAvatarSets()`, `GetCustomAvatarSetUnlockLevel()`, used in `MainWindow.xaml.cs` / `AvatarTubeWindow.xaml.cs`). | Either restrict to existing sets via `SupportedAvatarSets`, or ship a new look as a `CustomAvatarSet` (set 8+) — **pure mod data, no enum change**. | [CONFIG] + [ASSET] |
| Avatar art files | App defaults in `Resources/avatar{N}_pose{1..4}.png` (set 1 = `avatar_pose{1..4}.png`); mod overrides under `resources/` | Resolved via `ModResourceResolver.ResolveImage("avatarN_poseM.png")` → mod `resources/` first, then `pack://…/Resources/`. Optional animated GIFs. | Drop `avatar8_pose1..4.png` (and optional `animated8_1.gif`) into the mod's `resources/`. | [ASSET] |
| Tube placement | `Models/ModManifest.cs:201-217`; `Services/ModService.cs:777-781` | `TubeLayout` offsets/scale (attached + detached) for the `AvatarTubeWindow`. | Optional per-art tuning. | [CONFIG] |
| Companion identity strings | `Models/ModManifest.cs:121-152`; `Services/ModService.cs:609-633` | `CompanionName`, `UserTerm`, `ModeDisplayName`, `TalkToLabel`, `TakeoverLabel`, `Affirmation`, optional `RankSubject`. | Author "Kept" terms (e.g. companion "Keeper", user term "pet"/"toy", affirmation "Good boy"/"Good pet"). | [STRING] |
| Companion phrases (27 cats) | `ModManifest.Phrases`; `Services/CompanionPhraseService.cs:59-62`; `ModService.GetPhrases()` `:690-708` | All floating/idle/greeting/minigame/etc. lines come from the active mod's `Phrases`, fallback to CCP Default. | Author the 27 categories in "Kept" voice. | [STRING] |
| AI personality presets | `Services/PersonalityPresets.cs` (7 hardcoded: BambiSprite, SlutMode, GentleTrainer, StrictDomme, BimboCoach, HypnoGuide, BimboCow); prompt shape in `Models/CompanionPromptSettings.cs` | The system prompt is built from `CompanionPromptSettings`; preset **display names** are themed via `MakeModAware` / `GetPersonalityDisplayName` (`ModService.cs:713-726`) and renamed in `TextReplacements` (Drone renames "Slut Mode"→"Override Mode", etc.). | Rename presets for "Kept" via `TextReplacements`. A genuinely *new* preset still needs C# today (see delta). | [STRING] (rename) / [CODE] (new preset) |
| **`ModManifest.Personalities`** | `Models/ModManifest.cs:66-67, 258-271` | **Defined but unused at runtime.** Only reference outside the model is validation in `ModService.SanitizeManifest` (`ModService.cs:354-372`). Nothing consumes `.Personalities` to register a preset. | Don't rely on it yet; see §7-B for the hook to wire. | [CODE] (delta) |

---

## 3. Achievements

| Touchpoint | File | What exists now | For "Kept" | Tag |
|---|---|---|---|---|
| Achievement definitions | `Models/Achievement.cs` (`Achievement.All` static dict, ~60 entries) | **Global, hardcoded** in C#: `Id`, `Name`, `Requirement`, `FlavorText`, `ImageName`, `Category`, `IsExclusive`, `IsHidden`. Not loaded from JSON. | No new achievements needed; the set is shared by all modes. | — |
| Per-mode display | `MainWindow.xaml.cs:~15757-15824` | Name/Requirement/FlavorText each passed through `App.Mods.MakeModAware(...)` at display time. | Add `TextReplacements` entries renaming each achievement into "Kept" wording (the Drone manifest already does exactly this at `BuiltInMods.cs:1704-1731`). | [STRING] |
| Achievement art | `Resources/achievements/<ImageName>`; override via `resources/achievements/<ImageName>` | Resolved through `ModResourceResolver.ResolveImage("achievements/<name>.png")` — mod override → app default. | Optional: drop themed PNGs (same filenames) into the mod's `resources/achievements/`. | [ASSET] |
| Unlock conditions | `Services/AchievementService.cs` | Tied to **content-agnostic mechanics**: levels, total bubbles popped, flashes shown, session duration, day streaks, feature-active timers, feature-combination flags. No asset/wording coupling. | No change — they fire identically under "Kept". | — |

**Mechanism is confirmed pure-data:** a new mode themes achievements through `TextReplacements`
(strings) + optional `resources/achievements/` art (assets). **No `AchievementService` change.**

---

## 4. Affirmations / triggers / subliminals / "reminder" text — the primary per-mode payload

All mode-flavored wording lives in the `ModManifest` and is served by `ModService`. There is
**no hardcoded mode wording scattered in mechanics** — the engine reads these accessors.

| Wording bucket | Manifest field | ModService accessor | Example (Drone) | Tag |
|---|---|---|---|---|
| Subliminal pool | `SubliminalPool` (`Dictionary<string,bool>`) | `GetDefaultSubliminalPool()` `:636` | `BuiltInMods.cs:1193-1216` | [STRING] |
| Lock-card phrases | `LockCardPhrases` | `GetDefaultLockCardPhrases()` `:639` | `:1218-1227` | [STRING] |
| Custom triggers | `CustomTriggers` (`List<string>`) | `GetDefaultCustomTriggers()` `:642` | `:1229-1249` | [STRING] |
| Named triggers | `Triggers` (Freeze/Reset/CumAndCollapse/AutonomyOn) | `GetFreezeTriggerText()` … `:646-656` | `:1251-1257` | [STRING] |
| Attention/minigame messages | `Messages` | `GetAttentionCheckFailMessage()` … `:659-666` | `:1259-1264` | [STRING] |
| Companion phrases (27 cats) | `Phrases` | `GetPhrases(category)` `:690-708` | `:1337-1645` | [STRING] |
| Affirmation / praise term | `Identity.Affirmation` / `RankSubject` | `GetAffirmation()` `:624`, `GetRankSubject()` `:628` | "Unit" | [STRING] |
| Browser defaults | `Browser` (DefaultUrl/SiteName/links) | `GetDefaultBrowserUrl()` … `:669-687` | `:1266-1294` | [STRING]/[CONFIG] |
| Enhancement-tree labels | `EnhancementOverrides` | `GetEnhancementTreeTitle()` … `:783-818` | `:1307-1335` | [STRING] |
| Global terminology rename | `TextReplacements` | `MakeModAware(text)` `:732-747` | `:1647-1765` | [STRING] |

**`TextReplacements` is the master lever.** Applied longest-key-first, it rewrites *any* string
the engine routes through `MakeModAware` — achievement names, feature names, skill names,
personality names, enhancement labels, mode/trigger names, and base terminology
("Bambi"→"Unit", "pink"→"green", etc.). For "Kept" this is where most authoring effort goes.

Pool sanitization caps (apply equally to a bundled `.ccpmod`): subliminals ≤500, lock cards
≤200, triggers ≤50, phrase categories ≤50 (≤500 each), text replacements ≤200
(`ModService.cs:285-319, 256-282`).

---

## 5. Theming / aesthetics

| Touchpoint | File | What exists now | For "Kept" | Tag |
|---|---|---|---|---|
| Palette definition | `Models/ModManifest.cs:97-119` `ModTheme` | 7 hex slots: accent / accentLight / accentDark / background / panel / surface / filter. Validated `#RRGGBB` (`ModService.cs:217-234`). | Define a "Kept" palette (e.g. cold steel / cage-grey / chastity-blue, or whatever the cluster wants). | [THEME] |
| Theme accessors | `Services/ModService.cs:550-606` | `GetAccentColorHex()`, RGB variants, background/panel/surface/filter getters. | No change. | — |
| Secondary color | `Services/ModService.cs:588-597` | **Built-in modes get a hardcoded secondary** (`if id == BambiSleepId … DronificationId`); custom mods auto-compute via HSL hue shift `ComputeSecondaryFromAccent()` `:1163`. | Add a `if (_activeMod.Id == BuiltInMods.KeptId) return "#…";` line **or** accept the auto-computed secondary. | [CODE] (1 line, optional) |
| Live application | `MainWindow.xaml.cs:~1210-1362` | `RefreshThemeAwareElements()` rewrites color + brush `DynamicResource` keys (DarkerBg, PanelBg, Pink/DarkPink/Transparent brushes, SecondaryBrush, etc.) and direct element colors; runs on load, on `ModChanged`, and after lockdown. | No change — reads "Kept" palette generically. | — |
| Filter overlay tint | `Browser.FilterColor` → `GetFilterColorHex()` `:575` | Pink-filter / overlay tint per mode (Drone overrides to green). | Optional `filterColor`. | [THEME] |

---

## 6. Content-agnostic mechanics — confirmed user-supplied media pipeline

**Confirmed: no mechanic embeds bundled explicit media; every one reads from the user-supplied
assets folders (or first-party fallback art).** A new "Kept" mode reuses this with **zero**
mechanics changes.

| Mechanic | File:line | Media source |
|---|---|---|
| Flash images | `Services/FlashService.cs:~171` | `App.EffectiveAssetsPath/images` (user-supplied) |
| Mandatory videos | `Services/VideoService.cs:~375` | `App.EffectiveAssetsPath/videos` (user-supplied) |
| Bubble-count minigame | `Services/BubbleCountService.cs:~66` | `App.EffectiveAssetsPath/videos` (user-supplied) |
| Subliminals | `Services/SubliminalService.cs:~128-141, 374-382` | **Text** from the mod's `SubliminalPool` (per-mod settings); **audio** from `Resources/sub_audio` or mod `resources/` |
| Bubble visual | `Services/BubbleService.cs:~178-193` | `ModResourceResolver.ResolveImage("bubble.png")` → mod override → first-party `Resources/bubble.png` |
| Spiral / overlays | `Services/OverlayService.cs:~138, 154-158` | `ModResourceResolver.ResolveUri("spiral.gif")` or `settings.SpiralPath` (user) |
| Gaze / blink trainer | `Lab/GazeMinigame/*`, `Services/BlinkTrainerAssetPool.cs:~48-74` | User-selected asset-pack folders |

**Assets pipeline (App.xaml.cs):** `UserAssetsPath` = `%APPDATA%/ConditioningControlPanel/assets/`
with subfolders **`images/`, `videos/`, `wallpapers/`, `.temp/`**; user spirals under
`UserDataPath/Spirals/`. `EffectiveAssetsPath` = `CustomAssetsPath` (if the user set one) else
`UserAssetsPath`. Imports route through `Services/AssetImportService.cs` (videos→`videos/`,
images→`images/`, zips unpacked preserving structure).

**Gates are orthogonal to sourcing:** `Services/ExplicitContentGate.cs` gates the *AI companion*
explicit-reaction personality variants; `Services/GazeContentScreenPolicy.cs` clamps gaze-reactive
overlays to the calibrated screen. Neither selects or bundles media. **"Kept" needs no change here.**

> **Constraint satisfied:** "Kept" introduces **no** media. It points the same mechanics at the
> same user-supplied folders; only the *wording* (subliminal/lock-card/trigger text) and
> *first-party fallback art* differ, both via the manifest/`resources/`.

---

## 7. Mod system — what a `.ccpmod` can express, and the delta vs. a built-in mode

### What a `.ccpmod` already fully expresses
A `.ccpmod` is a zip of `mod.json` (a serialized `ModManifest`) + a `resources/` tree. Install
flow (`ModService.InstallModAsync` `:112-202`) validates id format (`^[a-z0-9][a-z0-9\-]*[a-z0-9]$`),
rejects the `builtin-` prefix, checks `MinAppVersion`, runs full `SanitizeManifest`, and copies to
`%APPDATA%/mods/<id>/`. `ModResourceResolver` is the **single chokepoint** for themed assets:
mod `resources/<path>` → `pack://…/Resources/<path>`, with `..`/absolute-path rejection and
`.mp3↔.wav` audio fallback.

Recognized `resources/` subfolders (mirrors `GenerateModTemplate` `:954-994` + resolver usage):
`achievements/`, `features/`, `skills/`, `spirals/`, `Cards/`, `sounds/` (incl. `bubbles/`,
`braindrain/`, `mindwipe/`, …), plus root-level `avatarN_poseM.png`, `animatedN_1.gif`,
`bubble.png`, `tube.png`/`tube2.png`, `logo.png`/`logo2.png`, `speechbubble1/2.png`, `preview.png`.

So a `.ccpmod` **can** define: theme, identity, all wording pools, phrases, triggers, messages,
browser, text-replacements, enhancement overrides, tube layout, supported + **custom** avatar sets,
and override every themed asset (avatars, achievement/feature/skill art, cards, spirals, sounds,
logos, tube, bubble, speech bubbles).

### The delta — built-in mode capabilities a `.ccpmod` (or its authoring UI) can't yet reach

| # | Gap | Where | Impact for "Kept" |
|---|---|---|---|
| D1 | **`ModManifest.Personalities` is parsed + validated but never consumed.** No runtime path registers mod-supplied AI personality presets. | `ModManifest.cs:66-67,258-271`; only ref is `ModService.cs:354-372` (validation) | A mod can't add a *new* AI personality; it can only rename the 7 hardcoded presets via `TextReplacements`. [CODE] to wire. |
| D2 | **`CompanionId` is a fixed C# enum** (sets 3–7 + XP bonus types). | `CompanionDefinition.cs:9-16` | A *new hardcoded companion* needs code. **Workaround that needs no code: `CustomAvatarSets` (set 8+)**, which *is* wired. So "Kept" can have new avatar art without an enum change — it just won't have a companion-specific XP-bonus type. [CODE] only if a bonus type is wanted. |
| D3 | **Built-in-only hardcoded secondary color.** | `ModService.cs:588-597` | Custom mods auto-compute secondary; built-ins are special-cased. "Kept" as a built-in either adds a 1-line case or accepts the computed value. [CODE] (trivial/optional). |
| D4 | **Onboarding / mode-picker chrome references specific modes via localization keys** (Bambi/Sissy copy-from buttons, "specific content" blurbs). | `Localization/Languages/en.json` (e.g. `label_bambi_sleep`, `label_sissy_hypno`, `btn_copy_from_sissy/bambi`) | A built-in "Kept" that appears in the same onboarding cards needs parallel keys (see §8). A *user* `.ccpmod` shows its `Name` verbatim (no loc), so this delta is **built-in-only**. [LOCALIZATION] |
| D5 | **Mod Creator UI exposes a subset of the manifest.** Not exposed: `SubliminalPool`, `LockCardPhrases`, `CustomTriggers`, `Personalities`, `EnhancementOverrides`, `TubeLayout`, `Tags`, `MinAppVersion`. Achievement/feature/skill **art** has slots, but *renames* are only via the Text-Replacements tab. | `ModCreatorWindow.xaml.cs` (section builders) vs. `ModManifest.cs` | Community authors must hand-edit `mod.json` for those fields. Doesn't block a built-in "Kept" (authored in C#), but blocks full community parity. [CODE] (UI) |
| D6 | **`ExportCurrentAsModAsync` is lossy.** | `ModService.cs:847-949` | Export omits `Phrases`-beyond-active, `EnhancementOverrides`, `TubeLayout`, `CustomAvatarSets`, `Personalities`, `Tags`. Round-tripping a built-in to a `.ccpmod` loses fidelity. [CODE] |

**Net:** A built-in "Kept" is ~all data + a few trivial code lines (registration D-none, optional
secondary D3, optional companion enum D2). A *fully community-authorable* "Kept" `.ccpmod`
(no core code) additionally needs D1, D5, D6 addressed (and D2 if a bonus-bearing companion is
desired).

---

## 8. Localization

**Mode *content* bypasses localization entirely.** Companion name, phrases, subliminals, triggers,
affirmations, messages, enhancement labels, and `TextReplacements` are authored once per mode in the
manifest and displayed **verbatim** — none routes through `LocalizationManager`. Confirmed: the
`ModService` accessors (`GetModeDisplayName`, `GetPhrases`, `MakeModAware`, …) return raw manifest
strings.

What **does** use localization (`LocalizationManager.Instance.Get(key)` / `{loc:Str key}` across 9
files `Localization/Languages/{en,de,es,fr,ja,ko,pt-BR,ru,zh-CN}.json`): the **fixed UI chrome** —
Mod Manager dialog labels/buttons/tooltips and the onboarding "theme" cards. Mod *names* shown in the
Mod Manager come from `ModPackage.Manifest.Name` (not localized).

Existing mode-specific keys that establish the pattern (en.json): `label_bambi_sleep`,
`label_bambi_sleep_specific_content`, `label_includes_bambicloud_browser`,
`label_bambi_themed_triggers_and_prompts`, `label_sissy_hypno`, `label_generic_sissy_hypno_content`,
`btn_copy_from_sissy`, `btn_copy_from_bambi`, `session_bambi_time_name`. (No `Drone`/`Dronification`
keys exist — Drone is surfaced only via its manifest, a useful precedent: a built-in mode can ship
*without* dedicated onboarding keys.)

**New keys "Kept" would need — only if it joins the localized onboarding/mode-picker cards** (×9
files): `label_kept` ("Kept"), `label_kept_specific_content`, optional `label_kept_*` blurbs,
optional `btn_copy_from_kept`, optional `session_kept_*` if a themed session preset is added. If
"Kept" is surfaced *only* through the generic Mod Manager (like Drone), **zero** new localization keys
are required. [LOCALIZATION]

---

## A) Checklist — ship "Kept" as a built-in mode (assets + strings + config only)

1. **[CONFIG/CODE]** `Models/BuiltInMods.cs`: add `public const string KeptId = "kept";`, a
   `public static ModManifest Kept { get; } = CreateKept();`, and a `CreateKept()` factory
   following the Drone pattern.
2. **[CODE]** `Services/ModService.cs:64-89`: register `Kept` into `_installedMods` (one block,
   mirrors the existing built-ins).
3. **[THEME]** In `CreateKept()`: define `ModTheme` (7 hex colors) for the Kept palette.
   **[CODE-optional]** add a `KeptId` case in `GetSecondaryColorHex()` (`ModService.cs:588-597`)
   or accept the auto-computed secondary.
4. **[STRING]** In `CreateKept()`: author `Identity` (companion name, user term, mode display name,
   talk-to / takeover labels, affirmation, optional rankSubject).
5. **[STRING]** Author the wording payload: `SubliminalPool`, `LockCardPhrases`, `CustomTriggers`,
   `Triggers`, `Messages`, all 27 `Phrases` categories, optional `EnhancementOverrides`,
   optional `Browser` defaults.
6. **[STRING]** Author `TextReplacements` to rename global achievements, features, skills,
   personality presets, enhancement labels, and base terminology into Kept voice (model on
   `BuiltInMods.cs:1647-1765`).
7. **[ASSET]** *(optional, recommended)* Produce first-party themed art and ship it either by
   bundling a `KeptMod/kept.ccpmod` (add a tuple to `_bundledBuiltInMods`,
   `ModService.cs:1009-1012`) or by extending the app `Resources/`: avatars
   (`avatar8_pose1..4.png` + optional `animated8_1.gif`), `achievements/`, `features/`, `skills/`,
   `Cards/`, `spirals/`, `sounds/`, `bubble.png`, `tube*.png`, `logo*.png`, `preview.png`.
8. **[CONFIG/ASSET-optional]** If shipping a new avatar look: add a `CustomAvatarSet`
   (`setNumber ≥ 8`, label, unlockLevel) — **no `CompanionId` enum change needed**.
9. **[LOCALIZATION-optional]** Only if "Kept" appears in the localized onboarding cards: add
   `label_kept` (+ blurbs / copy-from / session keys) to all 9 `Localization/Languages/*.json`.
   If surfaced only via the Mod Manager, skip.
10. **[VERIFY]** Confirm media is untouched: "Kept" reads the same `images/`, `videos/`, spirals,
    and subliminal-audio paths (§6). **No mechanics, session, or gameplay code is modified.**

> Mechanics, gameplay logic, session behavior, and runtime engine code: **unchanged** in all of
> the above. The only C# edits are mode *registration* and the optional 1-line secondary-color case.

## B) Plumbing to expose for fully community-authorable modes (no core code per mode)

To let a `.ccpmod` express *everything* a built-in mode can — so future modes are assets + JSON
only — address these (each maps to a delta in §7):

1. **Wire `ModManifest.Personalities` (D1):** on mod activation, merge mod-supplied personalities
   into the preset list the companion/Session prompt builder reads, so a mod can add (not just
   rename) AI personalities. Validation already exists (`ModService.cs:354-372`).
2. **Decouple avatar look from `CompanionId` (D2):** allow a `CustomAvatarSet` to optionally carry
   a companion display name + XP-bonus type, so a mod can introduce a "companion" without a new
   C# enum value. (Today `CustomAvatarSets` give art + unlock level only.)
3. **Generalize secondary color (D3):** drop the built-in id special-casing in
   `GetSecondaryColorHex()` and let the manifest supply an explicit secondary (fallback to the
   existing HSL auto-compute), so built-in and community modes behave identically.
4. **Make mode chrome mode-agnostic (D4):** drive any "specific content / copy-from" onboarding UI
   from the installed-mod list + manifest metadata instead of hardcoded `label_bambi_*`/`_sissy_*`
   keys, so a new mode needs no new localization keys to appear.
5. **Expand the Mod Creator UI (D5):** add editors for `SubliminalPool`, `LockCardPhrases`,
   `CustomTriggers`, `EnhancementOverrides`, `TubeLayout`, `Personalities`, `Tags`, `MinAppVersion`,
   and a first-class achievement/feature/skill **rename** surface (currently only the
   Text-Replacements tab).
6. **Make export lossless (D6):** have `ExportCurrentAsModAsync` serialize the full active manifest
   (all phrases, enhancement overrides, tube layout, custom avatar sets, personalities, tags) so a
   built-in mode round-trips to a `.ccpmod` without losing fidelity.

---

### Appendix — key files

- `Models/ModManifest.cs` — the mode shape (data contract).
- `Models/BuiltInMods.cs` — the four built-in modes as `ModManifest` factories.
- `Services/ModService.cs` — registration, activation, fallback-chain accessors, `MakeModAware`,
  sanitization, bundled-mod extraction, export/template.
- `Services/ModResourceResolver.cs` — single chokepoint for themed asset resolution (mod → app).
- `Models/Achievement.cs` + `Services/AchievementService.cs` — global achievements; per-mode via
  `TextReplacements` + `resources/achievements/` art.
- `Models/CompanionDefinition.cs` / `PersonalityPresets.cs` / `CompanionPromptSettings.cs` /
  `Services/CompanionPhraseService.cs` — companions, avatar sets, AI personality, phrase serving.
- `App.xaml.cs` — assets-path definitions, `ModService.Initialize`, `ModChanged` subscription.
- `MainWindow.xaml.cs` — mod selector, `RefreshThemeAwareElements`, feature relabel/art.
- `ModManagerDialog.*` / `ModCreatorWindow.*` — install/activate/uninstall + authoring UI.
- `Localization/Languages/*.json` (9) — UI chrome + onboarding keys (mode *content* is not here).
