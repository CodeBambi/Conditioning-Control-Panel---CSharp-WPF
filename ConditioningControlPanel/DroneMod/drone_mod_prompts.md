# Drone Mod — Image Generation Prompts

## Art Direction (Apply to ALL prompts)

**Global style prefix** (prepend to every prompt):
> Flat digital icon, dark cyberpunk terminal aesthetic, Matrix-inspired, black background (#0D0D0D), glowing neon green (#00FF41) as primary color, minimal detail, clean vector-like edges, no text, no watermark, square composition, 512x512px

**Palette reference:**
- Primary glow: `#00FF41` (Matrix green)
- Secondary: `#39FF14` (neon green)
- Dark accent: `#008F11` (deep green)
- Background: `#0D0D0D` (near-black)
- Surface: `#1A1A1A` (dark gray panels)

---

## 1. ACHIEVEMENTS (1 needed)

The 28 existing images in `Resources/Modachievements/drone/` cover 28 of the 29 achievement slots. One slot remains unmapped.

### Existing → Slot Mapping
| Existing File | Maps To Slot | Achievement Name |
|---|---|---|
| `initiation_sequence.png` | `achievements/lv_10.png` | Initiation Sequence (Lv10) |
| `blank_slate.png` | `achievements/Dumb_Bimbo.png` | Blank Slate |
| `synthetic_perfection.png` | `achievements/lv_50.png` | Synthetic Perfection (Lv50) |
| `hive_node.png` | `achievements/docile_cow.png` | Hive Node |
| `fully_assimilated.png` | `achievements/perfect_plastic_puppet.png` | Fully Assimilated |
| `format_c.png` | `achievements/BrainwashedSlavedoll.png` | Format C:\ |
| `filtered_perception.png` | `achievements/PlatinumPuppet.png` | Filtered Perception |
| `standby_mode.png` | `achievements/10_hours_pink.png` | Standby Mode (10h) |
| `daily_synchronization.png` | `achievements/daily_maintenance.png` | Daily Synchronization |
| `data_overload.png` | `achievements/retinal_burn.png` | Data Overload |
| `boot_sequence.png` | `achievements/morning_glory.png` | Boot Sequence |
| `task_failed_successfully.png` | `achievements/player_2_disconnected.png` | Task Failed Successfully |
| `display_unit.png` | `achievements/sofa_decor.png` | Display Unit |
| `access_denied.png` | `achievements/look_but_dont_touch.png` | Access Denied |
| `hypno_sync.png` | `achievements/spiral_eyes.png` | Hypno Sync |
| `processing_error.png` | `achievements/Mathematician's_nightmare.png` | Processing Error |
| `defragmentation.png` | `achievements/pop_the_Thought.png` | Defragmentation |
| `transcription_unit.png` | `achievements/typing_tutor.png` | Transcription Unit |
| `overclocked.png` | `achievements/obedience_reflex.png` | Overclocked |
| `glitch_in_the_system.png` | `achievements/Neon_obsession.png` | Glitch in the System |
| `memory_wiped.png` | `achievements/clean_slate.png` | Memory Wiped |
| `perfect_alignment.png` | `achievements/corner_hit.png` | Perfect Alignment |
| `haptic_feedback.png` | `achievements/deep_sleep.png` | Haptic Feedback |
| `terminal_lock.png` | `achievements/total_lockdown.png` | Terminal Lock |
| `reboot_loop.png` | `achievements/relapse.png` | Reboot Loop |
| `absolute_override.png` | `achievements/mercy_beggar.png` | Absolute Override |
| `fatal_exception.png` | `achievements/system_overload.png` | Fatal Exception |
| `unit_online.png` | `achievements/What_panic_button.png` | Unit Online |

### Missing Achievement (1)
**Slot: `achievements/how_many.png`** — "Sensory Overflow" (Bubble count challenge)

**Prompt:**
> Flat digital icon, dark cyberpunk terminal aesthetic, Matrix-inspired, black background, glowing neon green as primary color, clean vector-like edges, no text, no watermark, 512x512. A cluster of glowing green data orbs/spheres floating in a dark void, some with binary numbers inside them, a confused targeting reticle trying to count them all, scan lines, digital noise artifacts at edges.

---

## 2. AVATAR POSES (0 needed — use 5 existing sets)

The 20 existing poses in `Resources/Modposes/drone/` cover sets 1-5 perfectly:
- `Pose1_1..4` → `avatars/avatar_pose1..4.png` (Default)
- `Pose2_1..4` → `avatars/avatar2_pose1..4.png` (Level 20)
- `Pose3_1..4` → `avatars/avatar3_pose1..4.png` (Level 35)
- `Pose4_1..4` → `avatars/avatar4_pose1..4.png` (Level 50)
- `Pose5_1..4` → `avatars/avatar5_pose1..4.png` (Level 125)

Sets 6-7 (Level 150, 175) will be left empty — the app falls back to the highest available set.

---

## 3. FEATURE ICONS (17 needed)

All icons should be **128x128** or **256x256**, same terminal aesthetic.

### 3.1 `features/flash.png` — "Data Injection"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A glowing green syringe or data needle injecting streams of binary code into a circuit board, electrical sparks, digital pulse effect.

### 3.2 `features/mandatory_videos.png` — "Mandatory Playback"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A retro CRT monitor displaying a green eye/iris, with "PLAY" triangle symbol overlaid, scan lines across the screen, cathode ray glow, mandatory viewing feel.

### 3.3 `features/subliminal.png` — "Subliminal Protocol"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. Cascading Matrix-style green characters/code rain falling vertically, with a faint ghost of a word or command barely visible within the stream, subliminal hidden message effect.

### 3.4 `features/bouncing_text.png` — "Floating Directive"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A glowing green terminal text cursor blinking, surrounded by floating rectangular text blocks/command prompts drifting in different directions, zero-gravity data feel.

### 3.5 `features/Pink_filter.png` — "Green Filter"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. An eye viewed through a green-tinted digital lens/HUD overlay, with scan lines and a hexagonal grid pattern, night-vision / thermal-vision aesthetic, green phosphor glow.

### 3.6 `features/spiral_overlay.png` — "Hypno Spiral"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A geometric spiral pattern made of green circuit traces or data streams, rotating inward to a central glowing point, fibonacci/golden-ratio style, digital hypnotic vortex.

### 3.7 `features/brain_drain.png` — "Memory Flush"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A side-view silhouette of a head with green circuit patterns inside the brain, data/memory blocks streaming out and dissolving downward like defragmenting data, RAM dump visualization.

### 3.8 `features/Bubble_pop.png` — "Data Purge"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. Glowing green hexagonal bubbles/nodes floating in space, one mid-pop with digital shatter fragments, particle explosion effect, data packet being destroyed.

### 3.9 `features/Phrase_Lock.png` — "Protocol Lock"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A glowing green padlock with a terminal/command-line interface on its face, green scan lines, surrounded by a hexagonal security grid, encrypted data lock.

### 3.10 `features/Bubble_count.png` — "Enumeration Task"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A grid of small glowing green orbs arranged in rows, with a scanning beam/line sweeping across counting them, digital counter readout in corner, inventory scan feel.

### 3.11 `features/corner_gif.png` — "Peripheral Stimulus"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A small glowing green screen/window positioned in the corner of a larger dark display, showing animated data patterns, picture-in-picture terminal window, subtle corner notification.

### 3.12 `features/audio_whispers.png` — "Audio Uplink"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green glowing sound wave / audio waveform emanating from a small speaker or antenna, with digital static particles, radio signal transmission, encrypted audio feed.

### 3.13 `features/Mind_Wipers.png` — "Sector Wipe"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A hard drive platter being wiped clean by a green laser beam, data fragments disintegrating, secure erase visualization, progress bar showing deletion.

### 3.14 `features/bambi takeover.png` — "System Override"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green glowing hand/claw reaching through a cracked screen from behind, taking control, hostile takeover / remote access, green digital tendrils spreading across a dark interface.

### 3.15 `features/takeover.png` — "System Override Alt"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green command terminal window with "> OVERRIDE INITIATED" implied by a blinking cursor and progress bar, root access icon, admin privilege escalation visual.

### 3.16 `features/vibe.png` — "Haptic Signal"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. Concentric green pulse rings radiating outward from a central point, vibration/resonance pattern, haptic feedback visualization, sonar-like ping waves.

### 3.17 `features/4new.png` — "New Protocols"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green glowing "+" or star symbol inside a hexagonal frame, with small data particles orbiting it, new module / update available indicator, firmware update icon.

---

## 4. SKILL ICONS (22 needed)

All icons **128x128** or **256x256**, same terminal aesthetic.

### 4.1 `skills/pink_hours.png` — "Uptime Hours"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green digital clock face or timer readout with hours ticking up, surrounded by circuit traces, system uptime counter, server runtime visualization.

### 4.2 `skills/ditzy_data.png` — "Corrupted Data"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A file icon with green glitch artifacts, corrupted binary streaming out of it, data corruption visualization, broken/scrambled data blocks.

### 4.3 `skills/sparkle_boost_1.png` — "Overclock I"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A single green upward arrow inside a circuit board chip, overclocking symbol, speed boost indicator, small electrical sparks, performance upgrade tier 1.

### 4.4 `skills/good_girl_streak.png` — "Compliance Streak"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A horizontal chain of green checkmarks or verified badges linked together, growing streak/chain, consecutive compliance record, unbroken sequence.

### 4.5 `skills/hive_mind.png` — "Hive Network"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. Multiple green glowing nodes connected by thin data lines forming a network mesh, decentralized network topology, hive cluster visualization, interconnected drone units.

### 4.6 `skills/trophy_case.png` — "Achievement Cache"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green wireframe trophy or medal inside a glass display case made of grid lines, digital trophy room, achievement database, collected awards archive.

### 4.7 `skills/sparkle_boost_2.png` — "Overclock II"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. Two green upward arrows inside a circuit board chip, stronger electrical arcs, overclocking symbol tier 2, more intense glow, dual boost indicator.

### 4.8 `skills/lucky_bimbo.png` — "RNG Exploit"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green glowing dice or random number display showing favorable results, probability manipulation, luck algorithm, weighted random with green matrix code cascading behind.

### 4.9 `skills/milestone_rewards.png` — "Checkpoint Rewards"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green progress bar hitting a milestone marker/flag, with a reward package icon appearing, checkpoint reached, mission objective completed, data milestone.

### 4.10 `skills/oopsie_insurance.png` — "Error Recovery"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green shield icon with a circular "undo" arrow inside it, error recovery / rollback protection, system restore point, crash insurance.

### 4.11 `skills/popular_girl.png` — "Network Popularity"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A central green node with many connection lines radiating outward to smaller nodes, high connectivity score, popular node in network, social graph hub.

### 4.12 `skills/quest_refresh.png` — "Task Refresh"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green circular refresh/reload arrow surrounding a task list or clipboard icon, mission refresh, new directives loading, queue reset.

### 4.13 `skills/better_quests.png` — "Enhanced Directives"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A task list or mission brief icon with a green upward quality arrow, enhanced/upgraded missions, better directive parameters, improved protocols.

### 4.14 `skills/sparkle_boost_3.png` — "Overclock III"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. Three green upward arrows inside a circuit board chip, intense electrical storm, maximum overclocking, tier 3 boost, brightest glow, peak performance.

### 4.15 `skills/lucky_bubbles.png` — "Lucky Packets"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. Green data packets/orbs with small star/sparkle symbols on them, lucky data bubbles, bonus packets in stream, highlighted special nodes.

### 4.16 `skills/pink_rush.png` — "Green Rush"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A burst of green energy/data exploding outward from center, speed lines, rush of green light, adrenaline/boost activation, system surge.

### 4.17 `skills/streak_power.png` — "Streak Amplifier"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green lightning bolt striking a chain/streak counter, amplified power, streak multiplier, chain reaction boost, electrical surge through connected links.

### 4.18 `skills/reroll_addict.png` — "Recompile Addict"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. Green dice being re-rolled with a circular arrow, recompile/retry loop, obsessive re-randomization, recursive function call visualization.

### 4.19 `skills/perfect_bimbo_week.png` — "Perfect Cycle"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green calendar or 7-day grid with all checkmarks filled, perfect weekly cycle, 100% uptime for 7 days, flawless maintenance record.

### 4.20 `skills/night_shift.png` — "Night Cycle"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A crescent moon made of green circuit traces with small stars as data points, night operation mode, low-power standby cycle, nocturnal processing.

### 4.21 `skills/early_bird_bimbo.png` — "Early Boot"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. A green sun/sunrise icon made of digital rays with a power-on symbol in center, early morning boot sequence, dawn initialization, first-light activation.

### 4.22 `skills/eternal_doll.png` — "Eternal Unit"
> Flat digital icon, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, 256x256. An infinity symbol (∞) made of green circuit traces with a drone/unit silhouette at the center, eternal operation, perpetual loop, immortal process, undying system.

---

## 5. UI ASSETS (11 needed)

### 5.1 `bubble.png` — Poppable Bubble
> Flat digital icon, black background, glowing neon green, no text, 256x256. A green glowing hexagonal data packet or bubble, translucent with a digital grid pattern inside, floating in dark space, poppable data node, clean edges.

### 5.2 `tube.png` — Companion Tube/Container
> Digital art, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, **tall vertical** format (approximately 200x600 or similar). A vertical glass cylinder or stasis pod with green circuit trace decorations, digital readout panels on the sides, containment unit for a drone AI, dark interior with subtle green glow.

### 5.3 `tube2.png` — Companion Tube Alt
> Digital art, dark cyberpunk terminal aesthetic, black background, glowing neon green, no text, **tall vertical** format. An alternate vertical containment tube with a more angular/geometric design, hexagonal cross-section, green energy field inside, more aggressive/militaristic drone pod aesthetic.

### 5.4 `spiral.gif` — Hypno Spiral (animated GIF)
> Animated GIF, seamless loop, black background, glowing neon green. A rotating geometric spiral made of Matrix-style code characters or circuit traces, spinning slowly inward, hypnotic green vortex, digital trance spiral. Frame rate: smooth 15-30fps loop.

### 5.5 `logo.png` — App Logo
> Flat digital icon, black background, glowing neon green (#00FF41), no text, 256x256. A minimalist drone/unit head silhouette (smooth featureless face with two glowing green eye dots), surrounded by a thin hexagonal frame, circuit trace accents, clean and iconic.

### 5.6 `logo2.png` — App Logo Alt
> Flat digital icon, black background, glowing neon green (#00FF41), no text, 256x256. A terminal command prompt cursor (blinking block cursor) inside a green outlined monitor/screen shape, with subtle Matrix code rain in background, DroneOS boot screen feel.

### 5.7 `speechbubble1.png` — Speech Bubble
> Flat UI element, **transparent background (PNG with alpha)**, glowing neon green (#00FF41) border/outline, dark semi-transparent fill (#0D0D0D at 80% opacity). A rounded rectangle speech bubble with a triangular tail pointing downward-left, thin green glowing border, terminal/HUD style, clean vector edges. Size: approximately 300x200.

### 5.8 `speechbubble2.png` — Speech Bubble Alt
> Flat UI element, **transparent background (PNG with alpha)**, glowing neon green (#00FF41) border/outline, dark semi-transparent fill. An angular/hexagonal speech bubble with a triangular tail pointing downward-right, green circuit trace border pattern, more robotic/mechanical feel than speechbubble1. Size: approximately 300x200.

### 5.9 `Cards/fireworks.png` — Lock Card: Fireworks
> Flat digital art, dark background, glowing neon green, no text, 512x300 (card landscape). Green digital fireworks/particle explosions erupting in a dark void, data celebration, mission complete burst, pixelated green sparks cascading downward.

### 5.10 `Cards/hearth.png` — Lock Card: Heart
> Flat digital art, dark background, glowing neon green, no text, 512x300 (card landscape). A heart shape constructed from green circuit traces and data lines, digital love/devotion symbol, motherboard heart, pulsing green glow, heartbeat waveform running through it.

### 5.11 `Cards/spotlight.png` — Lock Card: Spotlight
> Flat digital art, dark background, glowing neon green, no text, 512x300 (card landscape). A green spotlight beam or scanning laser cutting through darkness from above, volumetric light cone, dust particles in beam, surveillance/interrogation spotlight, drone inspection beam.

---

## 6. PREVIEW IMAGE (1 needed)

### 6.1 `preview.png` — Mod Store Preview
> Digital art poster, dark cyberpunk terminal aesthetic, black background (#0D0D0D), glowing neon green (#00FF41), 800x450 (16:9 landscape). A dramatic composition: a drone unit silhouette standing in front of cascading Matrix code rain, green glowing eyes, hexagonal grid floor, terminal readout overlays in corners, "DRONIFICATION" feeling without literal text. Should convey: obedience, technology, cold precision, green-on-black terminal beauty.

---

## Summary: Files to Generate

| Category | Count | Sizes |
|----------|-------|-------|
| Achievement | 1 | 512x512 |
| Feature Icons | 17 | 256x256 |
| Skill Icons | 22 | 256x256 |
| UI: Bubbles/Logos | 4 | 256x256 |
| UI: Tubes | 2 | ~200x600 (tall) |
| UI: Spiral GIF | 1 | 256x256 (animated) |
| UI: Speech Bubbles | 2 | ~300x200 (transparent) |
| UI: Lock Cards | 3 | 512x300 |
| Preview | 1 | 800x450 |
| **TOTAL** | **53** | |

## Rename Map (existing → mod resource path)

### Achievements (copy from `Resources/Modachievements/drone/` to `resources/achievements/`)
```
initiation_sequence.png  →  lv_10.png
blank_slate.png          →  Dumb_Bimbo.png
synthetic_perfection.png →  lv_50.png
hive_node.png            →  docile_cow.png
fully_assimilated.png    →  perfect_plastic_puppet.png
format_c.png             →  BrainwashedSlavedoll.png
filtered_perception.png  →  PlatinumPuppet.png
standby_mode.png         →  10_hours_pink.png
daily_synchronization.png → daily_maintenance.png
data_overload.png        →  retinal_burn.png
boot_sequence.png        →  morning_glory.png
task_failed_successfully.png → player_2_disconnected.png
display_unit.png         →  sofa_decor.png
access_denied.png        →  look_but_dont_touch.png
hypno_sync.png           →  spiral_eyes.png
processing_error.png     →  Mathematician's_nightmare.png
defragmentation.png      →  pop_the_Thought.png
transcription_unit.png   →  typing_tutor.png
overclocked.png          →  obedience_reflex.png
glitch_in_the_system.png →  Neon_obsession.png
memory_wiped.png         →  clean_slate.png
perfect_alignment.png    →  corner_hit.png
haptic_feedback.png      →  deep_sleep.png
terminal_lock.png        →  total_lockdown.png
reboot_loop.png          →  relapse.png
absolute_override.png    →  mercy_beggar.png
fatal_exception.png      →  system_overload.png
unit_online.png          →  What_panic_button.png
[NEW: sensory_overflow.png] → how_many.png
```

### Avatars (copy from `Resources/Modposes/drone/` to `resources/avatars/`)
```
Pose1_1.png → avatar_pose1.png
Pose1_2.png → avatar_pose2.png
Pose1_3.png → avatar_pose3.png
Pose1_4.png → avatar_pose4.png
Pose2_1.png → avatar2_pose1.png
Pose2_2.png → avatar2_pose2.png
Pose2_3.png → avatar2_pose3.png
Pose2_4.png → avatar2_pose4.png
Pose3_1.png → avatar3_pose1.png
Pose3_2.png → avatar3_pose2.png
Pose3_3.png → avatar3_pose3.png
Pose3_4.png → avatar3_pose4.png
Pose4_1.png → avatar4_pose1.png
Pose4_2.png → avatar4_pose2.png
Pose4_3.png → avatar4_pose3.png
Pose4_4.png → avatar4_pose4.png
Pose5_1.png → avatar5_pose1.png
Pose5_2.png → avatar5_pose2.png
Pose5_3.png → avatar5_pose3.png
Pose5_4.png → avatar5_pose4.png
```
