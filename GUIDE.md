# 📚 Conditioning Control Panel - Detailed Guide

This comprehensive guide covers every feature of the Conditioning Control Panel v3.0.

---

## Table of Contents

1. [Getting Started](#getting-started)
2. [Interface Overview](#interface-overview)
3. [Flash Images](#flash-images)
4. [Visuals Settings](#visuals-settings)
5. [Mandatory Videos](#mandatory-videos)
6. [Subliminal Messages](#subliminal-messages)
7. [System Settings](#system-settings)
8. [Audio Settings](#audio-settings)
9. [Browser](#browser)
10. [Progression System](#progression-system)
11. [Scheduler](#scheduler)
12. [Intensity Ramp](#intensity-ramp)
13. [Dangerous Features](#dangerous-features)
14. [Tips & Best Practices](#tips--best-practices)

---

## Getting Started

### First Launch

When you first launch the application:

1. The app creates necessary folders in `assets/`:
   - `images/` - for flash images
   - `sounds/` - for accompanying sounds
   - `startle_videos/` - for mandatory videos
   - `spirals/` - for spiral overlay GIFs

2. Default settings are loaded
3. You start at **Level 1** with 0 XP

### Adding Your Content

Before starting, add your media files:

```
assets/
├── images/
│   ├── image1.jpg
│   ├── image2.png
│   └── animation.gif      # GIFs are fully supported!
├── sounds/
│   ├── sound1.mp3
│   └── sound2.wav
├── startle_videos/
│   ├── video1.mp4
│   └── video2.webm
└── spirals/
    └── spiral.gif
```

**Supported Formats:**
- Images: `.jpg`, `.jpeg`, `.png`, `.gif`
- Sounds: `.mp3`, `.wav`
- Videos: `.mp4`, `.webm`, `.avi`

---

## Interface Overview

The interface is divided into several areas:

```
┌─────────────────────────────────────────────────────────┐
│  💗 Conditioning Dashboard    [Settings] [Progression]  │
│  ═══════════════════════════════════════════════════   │
│  ⭐ Beginner Bimbo                           [Lvl 1]   │
├─────────────────────────────────────────────────────────┤
│  XP: 0/100 ████░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░   │
├───────────────┬───────────────┬─────────────────────────┤
│ LEFT COLUMN   │ MIDDLE COLUMN │ RIGHT COLUMN            │
│               │               │                         │
│ ⚡ Flash      │ 🎬 Video      │ 🌐 Browser              │
│ 👁️ Visuals   │ 💭 Subliminal │                         │
│ [Logo]        │ ⚙️ System     │ 🔊 Audio                │
│ [START]       │               │                         │
│ [Save] [Exit] │               │                         │
├───────────────┴───────────────┴─────────────────────────┤
│  ✨ Good girls condition daily ✨                       │
└─────────────────────────────────────────────────────────┘
```

### Tab Navigation
- **Settings Tab**: Main configuration (default view)
- **Progression Tab**: Unlockables, Scheduler, and Intensity Ramp

---

## Flash Images

### Overview
Flash images appear randomly on your screen based on your settings. They can be static images or animated GIFs.

### Settings

| Setting | Range | Description |
|---------|-------|-------------|
| **Enable** | On/Off | Master toggle for flash images |
| **Clickable** | On/Off | Allow clicking to dismiss images |
| **Corruption** | On/Off | Clicking spawns MORE images (hydra mode) |
| **Per Min** | 1-10 | Flash events per minute |
| **Images** | 1-15 | Images shown per flash event |
| **Max On Screen** | 5-20 | Hard limit on simultaneous images |

### How It Works

1. Every `60 / Per Min` seconds, a flash event triggers
2. `Images` number of random images appear
3. Each image appears at a random position on screen
4. If `Clickable` is on, clicking dismisses the image
5. If `Corruption` is on, clicking spawns 2 more images
6. `Max On Screen` prevents screen flooding

### Multi-Monitor Support
With **Dual Mon** enabled in System settings, images appear on all connected monitors.

---

## Visuals Settings

### Settings

| Setting | Range | Description |
|---------|-------|-------------|
| **Size** | 50-250% | Image scale (100% = original) |
| **Opacity** | 10-100% | Image transparency |
| **Fade** | 0-100% | Fade in/out animation duration |

### Tips
- Lower opacity (30-50%) for subtle background presence
- Higher fade values create smoother transitions
- Size 150-200% for impactful full-screen presence

---

## Mandatory Videos

### Overview
Fullscreen videos that play on a schedule. Videos cannot be minimized or moved while playing.

### Settings

| Setting | Range | Description |
|---------|-------|-------------|
| **Enable** | On/Off | Master toggle for videos |
| **Strict Lock** ⚠️ | On/Off | Cannot close or skip video |
| **Per Hour** | 1-20 | Videos played per hour |

### Mini-Game (Attention Checks)

The mini-game requires you to click targets during video playback to prove attention.

| Setting | Range | Description |
|---------|-------|-------------|
| **Enable** | On/Off | Toggle attention checks |
| **Targets** | 1-10 | Clicks required per video |
| **Duration** | 1-15 sec | How long each target appears |
| **Size** | 30-150 px | Target button size |

**Manage Button**: Edit the phrases shown on target buttons (e.g., "Good Girl", "I Obey")

### Video Playback
- Videos play fullscreen on your primary monitor
- Audio from other apps is ducked (lowered) during playback
- Press the panic key to stop (unless Strict Lock is enabled)

---

## Subliminal Messages

### Overview
Brief text messages that flash on screen. Can be combined with audio whispers.

### Settings

| Setting | Range | Description |
|---------|-------|-------------|
| **Enable** | On/Off | Master toggle |
| **Per Min** | 1-30 | Messages per minute |
| **Frames** | 1-10 | Display duration (lower = faster) |
| **Opacity** | 10-100% | Text visibility |

### Audio Whispers

| Setting | Range | Description |
|---------|-------|-------------|
| **Enable** | On/Off | Play whispered audio |
| **Volume** | 0-100% | Whisper volume |

### Managing Messages
Click **📝 Messages** to edit your message pool. Each message can be individually enabled/disabled.

Default messages include:
- "Good Girl"
- "Obey"
- "Submit"
- "Listen"
- And more...

---

## System Settings

### Settings

| Setting | Description |
|---------|-------------|
| **Dual Mon** | Enable overlays on all monitors |
| **Win Start** | Launch app when Windows starts |
| **Vid Launch** | Force a video on app launch |
| **Auto Run** | Auto-start engine when app opens |
| **Start Hidden** | Launch minimized to system tray |
| **No Panic** ⚠️ | Disable the panic key completely |

### Panic Key
- Default: **Escape**
- Click **🔑 Escape** button to change
- **Single press**: Stop the engine
- **Double-tap** (within 2 seconds): Exit application

### Assets Button
Click **📂 Assets** to open the assets folder in Windows Explorer.

---

## Audio Settings

### Settings

| Setting | Range | Description |
|---------|-------|-------------|
| **Master** | 0-100% | Overall volume level |
| **Audio Duck** | On/Off | Lower other apps during video |
| **Duck %** | 0-100% | How much to reduce other audio |

### How Ducking Works
When a video plays:
1. System volume for other apps reduces by Duck %
2. Video plays at Master volume
3. When video ends, other audio restores

---

## Browser

### Overview
Built-in browser using Microsoft WebView2. Browse the web without leaving the app.

### Navigation
- **URL Bar**: Enter addresses or search terms
- **←**: Go back
- **→**: Go forward
- **🏠**: Go to home page (BambiCloud)
- **🔄**: Refresh current page

### Zoom
The browser defaults to 75% zoom for a compact view. Use Ctrl+Scroll to adjust.

### Browser Data
Browser cookies and cache are stored in `browser_data/` folder. Delete this folder to clear all browser data.

---

## Progression System

### XP & Levels
Earn XP through interaction:
- **Clicking flash images**: +5 XP
- **Popping bubbles**: +2 XP
- **Completing attention checks**: +10 XP
- **Watching videos**: +20 XP

### Level Titles

| Level | Title | XP Required |
|-------|-------|-------------|
| 1-4 | Beginner Bimbo | 0-400 |
| 5-9 | Eager Student | 500-1400 |
| 10-14 | Devoted Follower | 1500-2400 |
| 15-19 | Conditioned Mind | 2500-3900 |
| 20-24 | Perfect Doll | 4000-5900 |
| 25-29 | Mindless Beauty | 6000-7900 |
| 30+ | Eternal Bambi | 8000+ |

### Unlockables

**Level 10 Unlocks:**
- 🌀 **Spiral Overlay**: Animated spiral GIF overlays your screen
- 💗 **Pink Filter**: Tints your entire screen pink

**Level 20 Unlocks:**
- 🫧 **Bubble Pop**: Floating bubbles you can pop for XP

**Level 35 Unlocks:**
- 🔐 **Passphrase Unlock**: Require a passphrase to stop the engine

### Level Up Sound
Place a `lvlup.mp3` in `Resources/` or `Assets/Audio/` to play a sound on level up.

---

## Scheduler

### Overview
Automatically start and stop sessions based on time of day.

### Settings

| Setting | Description |
|---------|-------------|
| **Enable Scheduler** | Master toggle |
| **Active Hours** | Start time → End time (24h format) |
| **Active Days** | Select which days to run |

### How It Works

1. **App starts within scheduled time**: 
   - Automatically minimizes to tray
   - Engine starts immediately
   - Shows notification

2. **Scheduled time begins**:
   - Engine auto-starts
   - Window minimizes to tray
   - Shows notification

3. **Scheduled time ends**:
   - Engine auto-stops
   - Shows notification

4. **Manual stop during schedule**:
   - Engine won't auto-restart until next time window

### Overnight Schedules
Supports schedules that cross midnight (e.g., 22:00 → 02:00)

---

## Intensity Ramp

### Overview
Gradually increase intensity over time during a session.

### Settings

| Setting | Range | Description |
|---------|-------|-------------|
| **Enable Ramp** | On/Off | Master toggle |
| **Duration** | 10-180 min | Time to reach maximum |
| **Multiplier** | 1.0-3.0x | Maximum intensity |
| **End at Ramp Complete** | On/Off | Auto-stop when done |

### Link to Ramp
Select which settings scale with the ramp:
- **Flash α**: Flash image opacity
- **Spiral α**: Spiral overlay opacity
- **Pink α**: Pink filter opacity
- **Master 🔊**: Master volume
- **Sub 🔊**: Subliminal whisper volume

### How It Works

1. Session starts → linked settings at base values
2. Over `Duration` minutes → values gradually increase
3. At end → values reach `base × multiplier`
4. Sliders visually update in real-time
5. If "End at Ramp Complete" is on → session stops

### Example
- Flash Opacity: 50%
- Multiplier: 2.0x
- Duration: 60 min

Result: Opacity goes from 50% → 100% over 60 minutes

---

## Dangerous Features

### ⚠️ Strict Lock
**What it does**: Cannot close, minimize, or skip videos

**Warning**: You will be forced to watch the entire video. Only the panic key can stop it (unless No Panic is also enabled).

A confirmation dialog requires you to:
1. Read the warning
2. Check "I understand the risks"
3. Click "Enable Anyway"

### ⚠️ No Panic
**What it does**: Completely disables the panic key

**Warning**: There will be NO way to stop the engine except:
- Wait for scheduler to end the session
- Force-close via Task Manager (Ctrl+Shift+Esc)

A double-confirmation dialog is required.

### Combining Both
If both Strict Lock AND No Panic are enabled:
- Videos cannot be skipped
- Panic key doesn't work
- Only options: wait for video to end, or Task Manager

**Use extreme caution with these features.**

---

## Tips & Best Practices

### Performance Considerations

**WARNING:** Running many features simultaneously, especially with high frequencies, multiple images, or high-resolution videos/GIFs, can be resource-intensive. If you are using a low-end PC or experience performance issues (stuttering, slow response), consider reducing the number of active features or their intensity/frequency.

### For Beginners
1. Start with low frequencies (2-3 per minute)
2. Keep Clickable enabled
3. Leave panic key active
4. Use the scheduler for structured sessions

### For Intensity
1. Enable Corruption mode for overwhelming presence
2. Use intensity ramp to build gradually
3. Combine multiple features (flash + subliminal + video)
4. Use Strict Lock for commitment

### Performance Tips
1. Limit GIF file sizes (under 5MB each)
2. Keep Max On Screen at 15 or below
3. Use MP4 format for videos (best compatibility)
4. Close other heavy applications

### Multi-Monitor Setup
1. Enable "Dual Mon" in System settings
2. Videos play on primary monitor
3. Flash images appear on all monitors
4. Overlays (spiral, pink) cover all monitors

### Troubleshooting Sessions
- **Too intense**: Press panic key once to stop
- **Need to exit**: Double-tap panic key
- **Completely stuck**: Ctrl+Shift+Esc → End Task

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Panic Key (default: Esc) | Stop engine |
| Double-tap Panic Key | Force exit |
| Click on tray icon | Show window |
| X button | Minimize to tray |

---

## Files & Data

### Settings Location
`settings.json` in application folder - contains all your preferences

### Log Files
`logs/` folder - useful for troubleshooting

### Browser Data
`browser_data/` folder - WebView2 cache and cookies

### Resetting Everything
1. Close the application
2. Delete `settings.json` (resets to defaults)
3. Delete `browser_data/` folder (clears browser)
4. Restart the application

---

## FAQ

**Q: Why don't my GIFs animate?**
A: Ensure GIFs are under 5MB. Very large GIFs may not animate smoothly.

**Q: Can I use this on multiple monitors?**
A: Yes! Enable "Dual Mon" in System settings.

**Q: How do I completely exit the app?**
A: Double-tap the panic key, or right-click tray icon → Exit, or use the Exit button.

**Q: Where do I put my files?**
A: In the `assets/` subfolders: `images/`, `sounds/`, `startle_videos/`

**Q: Is my data sent anywhere?**
A: No. Everything stays local. No telemetry, no analytics, no network.

**Q: Can I use this while gaming?**
A: Yes, but overlays may interfere with fullscreen games. Use borderless/windowed mode for best results.

---

<p align="center">
  <b>💗 Enjoy your conditioning sessions! 💗</b>
</p>
