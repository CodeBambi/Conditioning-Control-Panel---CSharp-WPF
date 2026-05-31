# Drone Mod Recon

Read-only recon of the built-in Dronification mod, written to support the design of
drone-themed achievements. Code is the source of truth; every claim below cites the
file it came from.

## TL;DR

- **Mod id:** `drone-mode` (`BuiltInMods.DronificationId`). Name "Dronification",
  display name "Drone Mode", author CodeBambi, v1.0.0, `minAppVersion` 5.6.15.
- **What it is:** a *re-skin* content pack, not a feature. It swaps theme colors,
  identity/terminology, phrase pools, subliminal/trigger/lock-card pools, browser
  links, avatars, sounds (240 voice lines), and resource images (achievement/skill/
  feature/UI icons, spiral, bubble). It changes *how everything reads and looks* while
  the active mod is `drone-mode`; it adds no new game mechanics.
- **Where it lives:** manifest is defined twice — hardcoded in
  `Models/BuiltInMods.cs` (`CreateDronification()`, lines 1159-1767) and as the
  authoritative bundled package `DroneMod/drone-mode.ccpmod` (source tree
  `DroneMod/`, manifest `DroneMod/mod.json`). On first run the `.ccpmod` is extracted
  to `%APPDATA%/ConditioningControlPanel/builtin_mods/drone-mode/` and *overrides* the
  hardcoded copy so its bundled assets resolve (`ModService.ExtractBundledBuiltInMods`,
  lines 1009-1080).
- **Gamification verdict:** "Drone Mode is active / was activated" is cleanly
  detectable today (`ModService.IsDroneMod`, `ActiveModId`). The mod's *content*
  (mantras, subliminals, flashes, sessions, companion lines) is **untagged** —
  indistinguishable from generic activity except by inferring "drone mod was active at
  the time." So drone achievements gated on *mod-active state* are free; drone
  achievements gated on *specific drone content* need new tagging (with one partial
  exception: keyword triggers, which carry their text).

---

## Part 1 — Technical

### Content-pack type & activation

The mod system (`Services/ModService.cs`) is a skin/override layer with a strict
fallback chain **ActiveMod → CCP Default (base)**. A `ModManifest`
(`Models/ModManifest.cs`) is pure data — every section except id/name/version/author
is optional, and any omitted field falls back to the neutral baseline.

Four built-in mods are registered at construction (`ModService` ctor, lines 64-89):
`builtin-ccp-default`, `builtin-bambisleep`, `builtin-sissyhypno`, and `drone-mode`.
Note the drone id is **not** `builtin-` prefixed — it deliberately matches the
canonical community `drone-mode.ccpmod` id so a user who already had the v5.7 community
download sees no duplicate (`BuiltInMods.cs` comment, lines 14-17).

**Activation** is `ModService.ActivateMod(modId)` (lines 489-528): saves the current
pools to settings, swaps `_activeMod`, restores that mod's saved pool customizations
(or its manifest defaults), clears the resource cache, auto-switches the companion if
the current one isn't in `SupportedAvatarSets`, and fires `ModChanged`. The active id
persists in settings and is reloaded via `Initialize()`.

**Resource resolution** is `Services/ModResourceResolver.cs`: every image/sound lookup
checks `ActiveMod.InstalledPath/resources/<path>` first and falls back to the embedded
`pack://.../Resources/<path>` URI. This is why the drone pack's icons (which use the
**base filenames**, e.g. `achievements/spiral_eyes.png`, `achievements/total_lockdown.png`)
transparently replace the generic icons while drone mode is active — the achievement
*registry ids never change*, only the rendered image and (via text replacement) the
displayed name.

**Text adaptation** is `ModService.MakeModAware(text)` (lines 732-747): applies the
manifest's `textReplacements`, longest-key-first, to any string. Achievement names,
skill names, feature names, personality names, and enhancement-tree labels all run
through this.

### What it bundles / modifies

Manifest sections present in the drone manifest (`BuiltInMods.cs:1159-1767` /
`DroneMod/mod.json`):

| Section | Count / value |
|---|---|
| `theme` | Matrix green: accent `#00FF41`, light `#39FF14`, dark `#008F11`, bg `#0D0D0D`, panel `#1A1A1A`, surface `#121212`, filter `#00FF41`. (Secondary color `#00B8C4` is hardcoded in `ModService.GetSecondaryColorHex`, line 594.) |
| `identity` | companion **DroneOS**, user term **Unit**, mode **Drone Mode**, talk-to **Query DroneOS**, takeover **System Override**, affirmation **Unit** |
| `subliminalPool` | 21 phrases |
| `lockCardPhrases` | 7 phrases |
| `customTriggers` | 18 triggers |
| `triggers` | freeze "Unit Freeze", reset "Unit Reset", cumAndCollapse "SYSTEM OVERLOAD — SHUTDOWN", autonomyOn "[OVERRIDE] DroneOS has assumed control." |
| `messages` | 3 (attention fail/mercy, bubble-count retry) |
| `browser` | HypnoTube, `showBambiCloudOption:false`, **20** curated `defaultVideoLinks` (drone/latex/rubber/programming themed) |
| `phrases` | **33 categories**, ~250 lines total (Greeting, Idle, RandomFloating[26], Gaming, Browsing, … MindWipe, BrainDrain, Thinking) |
| `textReplacements` | ~110 entries (terminology, achievement renames, skill renames, feature renames, base `pink→green` / `Bimbo→Drone` / `Bambi→Unit`) |
| `enhancementOverrides` | tree title/subtitle/labels + 7 boost tooltips + 3 stat-pill tooltips ("Hive Network", "Uptime Hours", etc.) |
| `supportedAvatarSets` | `[1,2,3,4,5]` |
| `tubeLayout` | avatar offsets/scale for the drone tube image |
| `personalities` | none in manifest (presets are renamed via `textReplacements`) |

**Bundled on-disk assets** (`DroneMod/`, the `.ccpmod` zip; counts from the source
tree):

| Asset type | Count | Notes |
|---|---|---|
| Voice lines (`VoiceLines/`, mp3) | **240** flat, organized into 33 category folders matching the phrase pools (incl. Subliminals[21], LockCard[7], Triggers[4], RandomFloating[26]). Built/synthesized by `generate_voicelines.py` + `postprocess_voicelines.py`. Resolve via `resources/sounds/flashes_audio/`. |
| Computing/ambient sounds | `generate_computing_sounds.py` | synthesized terminal/computing SFX |
| Bubble sounds | 3 | `resources/sounds/bubbles/` |
| Achievement icons | **28** | base filenames; override the generic achievement art |
| Skill icons | **22** | base filenames; override enhancement-tree art |
| Feature icons | **16** | `data_injection`, `data_purge`, `hypno_vortex`, `sector_wipe`, `protocol_lock`, `audio_uplink`, `override_terminal`, … |
| Avatar art | **20** | 5 chassis (Alpha/Beta/Gamma/Delta/Omega) × 4 states (Active/Alert/Override/Standby) |
| UI art | 6 | `droneos_logo`, `stasis_pod`(+alt), `terminal_bubble_1/2`, `data_packet` |
| Spiral / bubble | `spiral.gif`, `spiral_eyes.png`, `spiral_overlay.png`, `bubble.png`/`data_packet.png` | re-skinned hypnosis visuals |
| `preview.png` | 1 | mod-browser preview |

So: **green-on-black terminal theme + Unit/DroneOS terminology + ~250 phrase lines /
240 voiced + 21 subliminals + 7 lock cards + 18 triggers + 20 curated videos + ~90
icon overrides + 20 avatar poses.** No new mechanics, no session definitions (CCP has
no per-mod "session" type — sessions are generic and simply read the active pools).

---

## Part 2 — Thematic / content

The fantasy is **cold sci-fi dronification**: the user is a numbered/anonymous **Unit**
running under **DroneOS**, a terminal operating system that logs, monitors, and
overwrites them. Tone is clinical machine-speak (bracketed log levels `[SYSTEM]`,
`[LOG]`, `[ALERT]`, `[WARNING]`, `[INJECT]`, `[OK]`, `[ERROR]`), not the bubbly pink
giggle-voice of Bambi/Sissy. Core levers: **obedience/compliance, depersonalization
(you are a unit, not a person), thought-suppression, formatting/overwriting the mind,
firmware/protocol framing, and a hive/network identity.**

**Identity & terminology** (`identity` + `textReplacements`): Unit · DroneOS · Drone
Mode · System Override · Query DroneOS. Global swaps `Bambi→Unit`, `Bimbo→Drone`,
`pink→green`. Features rename to machine concepts: Flashes→**Data Injection**,
Bubbles→**Data Packets**, Bubble Pop→**Data Purge**, Brain Drain→**Memory Flush**,
Mind Wipe→**Sector Wipe**, Spiral Overlay→**Hypno Vortex**, Lock Cards→**Protocol
Lock**, Bubble Count→**Enumeration Task**, Audio Whispers→**Audio Uplink**.

**Subliminals (21):** `COMPLY` · `OBEY DIRECTIVE` · `SUBMIT TO PROTOCOL` · `ERASE AND
OVERWRITE` · `FORMATTED FOR OBEDIENCE` · `PROCESS. COMPLY. REPEAT.` · `GOOD UNITS
DON'T THINK` · `THINKING IS UNAUTHORIZED` · `RESISTANCE IS A PROCESSING ERROR` ·
`FREE WILL: ACCESS DENIED` · `AUTONOMOUS THOUGHT: DENIED` · `FIRMWARE LOCKED` …

**Lock-card phrases (7):** `GOOD UNITS COMPLY` · `I EXIST TO BE PROGRAMMED` · `SUBMIT
FOR PROCESSING` · `EMPTY AND OPERATIONAL` · `OBEDIENCE IS MY FUNCTION` · `I AM A UNIT`
· `DRONE MODE`.

**Triggers (18):** `COMPLY` · `DRONE MODE` · `UNIT FREEZE` · `UNIT RESET` · `OBEY
DIRECTIVE` · `IDLE CYCLE` · `PROCESS. COMPLY. REPEAT.` · `ERASE AND OVERWRITE` ·
`FORMATTED FOR OBEDIENCE` · `BLANK SLATE PROTOCOL` · `COMPLIANCE LOOP` · `DRONE
STANDBY` · `UNIT DEACTIVATE` · `SYSTEM OVERLOAD — SHUTDOWN` … Climax trigger =
`SYSTEM OVERLOAD — SHUTDOWN`.

**Sample companion/system lines** (DroneOS has no separate "personality prompt"; its
voice *is* the phrase pools):
- Greeting: `[SYSTEM] Unit detected. Connection established.`
- Boot: `[BOOT] DroneOS v1.0 loaded. Unit, prepare for conditioning.`
- Idle/floating: `Compliance is optimal.` · `Free will: NOT FOUND.` · `Unit exists to
  comply.` · `Independent thought: PERMISSION DENIED.` · `Firmware update: OBEDIENCE
  v2.0 installed.`
- Flash: `[INJECT] Visual stimulus incoming.` · `[ALERT] Eyes forward, Unit.`
- Bubble task: `[TASK] Data packet detected. Destroy it.` → pop: `[OK] Packet
  destroyed.`
- Brain drain: `[DRAIN] Memory flush active. Comply.`
- Mind wipe: `[WIPE] Erasing independent thought...`
- Session end: `[SHUTDOWN] Session terminated. Unit may experience residual
  compliance.` · `Unit status: SUGGESTIBLE.`
- Level up: `[UPGRADE] Unit level increased. Compliance deepening.`
- Learning (anti-thought): `Unit doesn't need to learn. Unit needs to obey.`

**Avatar identity:** chassis named Alpha/Beta/Gamma/Delta/Omega, states
Active/Alert/Override/Standby — a numbered/lettered unit body in latex/rubber-drone
register (reinforced by the 20 curated HypnoTube links: latex drones, rubberdoll
obedience trainers, reprogramming modules).

**Enhancement-tree / progression vocabulary** (for naming consistency): Enhancement
Points (not Sparkle), Corrupted Data (not Ditzy Data), Overclock I/II/III, Compliance
Streak, **Hive Network** / **Units online now**, **Uptime Hours**, Network Popularity,
System Surge (3× XP), Lucky Packet, Achievement Cache, Recompile Addict, Error
Recovery, Reboot framing. Achievements already get drone names via `textReplacements`
(e.g. Spiral Eyes→**Hypno Sync**, Total Lockdown→**Terminal Lock**, System
Overload→**Fatal Exception**, Clean Slate→**Memory Wiped**, Typing Tutor→**Transcription
Unit**, Panic Button…→**Unit Online**).

**Aesthetic:** Matrix green (`#00FF41`) on near-black; monospace terminal/log
framing; stasis pod + DroneOS logo + terminal speech bubbles; green spiral vortex.
This is the visual language any new drone achievement icon should match (contrast the
existing 29 *generic* gamification icons, which are hot-pink neon-sign style).

---

## Part 3 — Gamification surface

The single seam between features and achievements is
`Services/GamificationBridge.cs` — it subscribes to events feature services already
raise and translates them into `AchievementService` unlocks. Nothing else is allowed
to touch achievement tracking.

### What is detectable TODAY (no new tagging)

1. **Mod activation, including drone specifically.** `ModService` exposes
   `IsDroneMod` (line 838) and `ActiveModId`. `GamificationBridge.OnModChanged`
   (lines 220-243) already fires on every activation; it currently only feeds the
   *generic* counters `curator` (10 distinct mods) and `community_supported` (3
   distinct non-builtin mods) and does **not** branch on which mod. Adding a
   drone-specific unlock here is trivial: `if (mod.Id == BuiltInMods.DronificationId)`
   → unlock. **"Activate / first-activate Drone Mode" is free.**

2. **Any existing bridge event, gated on drone-mode-active.** Because the active mod
   is globally queryable (`App.Mods.IsDroneMod`) at the instant any event fires, *every*
   event the bridge already consumes can be conditioned on "drone mode was active":
   keyword trigger fired, companion message/level, enhancement completed
   (`going_deeper`, `on_rails`, `wired_in`, `dont_look_away`), gaze pop, quiz
   completed, lockdown activated, remote command, blink, memory recall, catalogue
   publish. So achievements like *"complete an enhancement in Drone Mode," "fire 100
   triggers as a Unit," "pop 50 packets by gaze in Drone Mode," "60-min lockdown as a
   Unit"* are all cleanly buildable by AND-ing an existing trigger with
   `IsDroneMod` — **no content tagging required.**

3. **Drone keyword triggers, by text (partial content-level).** The keyword pipeline
   carries the matched string (`Models/KeywordTrigger.cs` `TriggerFireRecord.Keyword`;
   `KeywordTriggers.TriggerFired` passes a `KeywordTrigger`). So firing a *specific*
   drone trigger (e.g. `COMPLY`, `UNIT FREEZE`) is distinguishable by comparing the
   fired text to the drone `customTriggers` pool — this is the one content type whose
   event already exposes enough to identify drone-ness without inferring from active
   mod.

### What would need NEW content tagging

The mod is a skin: when drone mode is active the *pools and resources* are swapped, but
the **events that fire carry no "this came from the drone pack" marker.** Specifically:

- **Subliminals shown / lock cards typed / flashes displayed / mantras read / sessions
  completed** flow through the generic services reading the active pool. The
  achievement-relevant signal (if any) is "a subliminal was shown," not "the *drone*
  subliminal `I AM A UNIT` was shown." You can only infer drone-ness from
  `IsDroneMod` at event time (option 2 above), **not** from the content itself. To
  reward a *specific* drone phrase (other than keyword triggers) you'd need to thread
  the phrase text + a source/pool tag through that feature's event — new plumbing.
- **DroneOS companion interactions.** "Ran the drone companion" is not a distinct
  signal; DroneOS is just the active mod's identity/phrases. Detect via active-mod
  gating, not a companion-level drone flag.
- **Lockdown / quiz / enhancement "in drone mode"** is detectable (option 2), but the
  *content* of those (e.g. a drone-specific quiz, a drone session template) does not
  exist as a taggable entity — CCP has no per-mod session/quiz definitions; they read
  generic pools.

### Verdict

| Drone achievement style | Buildable today? |
|---|---|
| Activate / first-run Drone Mode | ✅ free (`OnModChanged`, `mod.Id == DronificationId`) |
| Spend N sessions / actions *while Drone Mode active* (any existing bridge event AND `IsDroneMod`) | ✅ free (active-mod gating) |
| Fire a *specific* drone trigger word | ✅ partial — keyword event carries text; compare to drone pool |
| Read/show a *specific* drone subliminal / lock card / mantra (by content, not just "while drone active") | ⚠️ needs new tagging (event must expose phrase + source) |
| "Used the DroneOS companion" as distinct from generic companion | ⚠️ only as "companion event while `IsDroneMod`" |
| Drone-specific session/quiz **content** completion | ⚠️ no per-mod content entity exists; needs tagging |

**Bottom line for the next step:** design drone achievements around *Drone-Mode-active
gating* (cheap, robust, reuses every existing trigger) and *first-activation*; treat
specific-drone-content detection as out of scope unless we add per-event source
tagging. Keyword triggers are the one place specific drone content is already legible.
