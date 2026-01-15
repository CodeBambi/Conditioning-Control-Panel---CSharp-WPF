# 💗 Conditioning Control Panel v3.0

A powerful desktop application for visual and audio conditioning, featuring gamification, scheduling, an interactive companion avatar, and a sleek modern interface.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)
![WPF](https://img.shields.io/badge/WPF-Windows-0078D6?style=flat-square&logo=windows)
![VirusTotal](https://img.shields.io/badge/VirusTotal-0%2F72%20Clean-brightgreen?style=flat-square)

<p align="center">
  <img src="https://raw.githubusercontent.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/main/preview.png" alt="Preview" width="800"/>
</p>

---

## 🔒 Security Verification

**This application is 100% safe and open source.**

[**VirusTotal Scan: 0/69 Detections**](https://www.virustotal.com/gui/file/187927f88cbcafbcb470b75c794f0d0095e2fcf84f3fc134f5137228c46ef334/detection)

- No malware, no telemetry, no data collection
- All code is open source and auditable
- Runs entirely offline (except embedded browser and optional Patreon features)
- No administrator privileges required

---

## ✨ Features

### 🖼️ Flash Images
- Random image popups with customizable frequency
- GIF animation support with smooth playback
- Clickable images with optional "Corruption" mode (hydra effect)
- Adjustable size, opacity, and fade animations
- Multi-monitor support

### 🎬 Mandatory Videos
- Fullscreen video playback on schedule
- **Strict Lock** mode (cannot skip/close)
- Attention check mini-game with clickable targets
- Audio ducking during playback

### 💭 Subliminal Messages
- Customizable text flashes
- Adjustable frequency, duration, and opacity
- Audio whisper support with matching audio files
- Message pool management

### 🤖 Companion Avatar
- Interactive animated companion that reacts to your activities
- **Detachable sprite** - Float freely on screen or attach to window
- **Speech bubbles** with idle chatter, reactions, and triggers
- **Trigger Mode** - Display random trigger phrases with matching audio
- **Quick controls** via right-click context menu
- **AI Chat** (Patreon) - Have conversations with the avatar
- **Window Awareness** (Patreon) - Contextual reactions to your activities

### 🌀 Unlockable Features (Progression System)
- **Level 10**: Spiral Overlay + Pink Filter
- **Level 20**: Bubble Pop mini-game
- **Level 35**: Lock Card (passphrase unlock)
- **Level 50**: Bubble Count challenge
- **Level 60**: Bouncing Text
- **Level 70**: Brain Drain overlay
- **Level 75**: Mind Wipe audio effects
- XP earned through interaction
- Visual level progression with titles

### 🏆 Achievements
- 20+ achievements to unlock
- Track progress across sessions
- Achievement popups on unlock
- Categories: Progression, Time, Minigames, Hardcore

### 📅 Scheduler
- Auto-start/stop based on time windows
- Day-of-week selection
- **Intensity Ramp**: Gradually increase settings over time
- Link multiple parameters to ramp (opacity, volume, etc.)
- End session automatically when ramp completes

### 🎮 Sessions
- Pre-built conditioning sessions with phases
- Morning Drift, Gamer Girl, The Distant Doll, and more
- Custom session creation and sharing
- Difficulty-based XP bonuses

### 🌐 Embedded Browser
- Built-in WebView2 browser
- Quick access to BambiCloud and other sites
- Zoom controls and navigation
- Pause browser audio from companion controls

### ⚙️ System Features
- System tray integration (minimize to tray)
- Global panic key (configurable)
- Windows startup option
- Dual monitor support
- Comprehensive tooltips on all settings

---

## 💜 Patreon Features

Support the project on Patreon to unlock exclusive features:

### 🤖 AI Chat
- Chat directly with the companion avatar
- Personalized AI responses
- Context-aware based on your activities
- Daily request limit

### 👁️ Window Awareness
- Avatar reacts to what you're doing
- Detects active windows and browser tabs
- Contextual comments and reactions
- Customizable reaction cooldown

**Privacy Notice**: Window Awareness reads active window/tab names and sends them to our secure proxy for AI processing. No data is stored permanently.

### 🔥 Slut Mode
- Enable explicit AI responses
- More intense avatar reactions
- Toggle from context menu or settings

[**Support on Patreon**](https://www.patreon.com/CodeBambi)

---

## 📋 Requirements

- **OS**: Windows 10/11 (64-bit)
- **Runtime**: [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Browser**: [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (usually pre-installed on Windows 10/11)

---

## 🚀 Installation

### Option 1: Download Release (Recommended)
1. Go to [Releases](https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases)
2. Download the latest `.zip` file
3. Extract to any folder
4. Run `ConditioningControlPanel.exe`

### Option 2: Build from Source
```bash
# Clone the repository
git clone https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF.git

# Navigate to project
cd Conditioning-Control-Panel---CSharp-WPF

# Restore packages and build
dotnet restore
dotnet build --configuration Release

# Run
dotnet run --project ConditioningControlPanel
```

---

## 📁 Folder Structure

```
ConditioningControlPanel/
├── assets/
│   ├── images/          # Flash images (.jpg, .png, .gif)
│   ├── sounds/          # Flash sounds (.mp3, .wav)
│   ├── startle_videos/  # Mandatory videos (.mp4, .webm)
│   └── spirals/         # Spiral GIFs
├── Resources/
│   ├── sub_audio/       # Subliminal whisper audio
│   └── sounds/          # Avatar sounds
├── browser_data/        # WebView2 cache (auto-created)
├── logs/                # Application logs
├── settings.json        # User settings (auto-created)
└── ConditioningControlPanel.exe
```

### Adding Content
Simply drop your files into the appropriate `assets/` subfolder:
- **Images**: `.jpg`, `.jpeg`, `.png`, `.gif`
- **Sounds**: `.mp3`, `.wav`
- **Videos**: `.mp4`, `.webm`, `.avi`
- **Trigger Audio**: Place in `Resources/sub_audio/` named to match triggers (e.g., `GOOD GIRL.mp3`)

---

## ⌨️ Controls

| Key/Action | Result |
|-----|--------|
| **Escape** (default) | Panic key - Stop engine |
| Double-tap panic key | Force exit application |
| Click flash image | Dismiss (or spawn more in Corruption mode) |
| Click bubble | Pop for XP |
| Click speech bubble | Dismiss |
| Double-click avatar | Open AI Chat (Patreon) |
| Right-click avatar | Context menu |
| Drag avatar (detached) | Reposition |

---

## 🎮 Quick Start

1. **Add Content**: Place images in `assets/images/`, videos in `assets/startle_videos/`
2. **Configure Settings**: Adjust frequencies, sizes, and features in the Settings tab
3. **Meet Your Companion**: The avatar appears next to the window - right-click for options
4. **Click START**: The engine begins running
5. **Minimize**: App continues running from system tray
6. **Panic Key**: Press Escape to stop, double-tap to exit

---

## 📖 Documentation

- [**Detailed Guide**](GUIDE.md) - Complete feature walkthrough
- [**Security Overview**](SECURITY_OVERVIEW.md) - Security analysis and privacy info
- [**Avatar Integration**](ConditioningControlPanel/AVATAR_TUBE_INTEGRATION.md) - Technical avatar documentation

---

## 🔧 Troubleshooting

### "WebView2 Runtime not installed"
Download and install from: [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)

### Videos not playing
- Ensure videos are in `assets/startle_videos/`
- Supported formats: `.mp4`, `.webm`, `.avi`
- Check that video codecs are installed

### Application won't start
- Install [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- Run as administrator if issues persist

### Flash images not appearing
- Check `assets/images/` folder has valid images
- Ensure "Enable" is checked in Flash Images section
- Verify opacity is not set too low

### Trigger audio not playing
- Place audio files in `Resources/sub_audio/`
- Name files to match triggers (e.g., `GOOD GIRL.mp3`)
- Files are matched case-insensitively
- Ensure Audio Whispers is enabled in Settings

---

## 🛡️ Privacy & Security

- **Mostly offline**: Core features work completely offline
- **Patreon features**: AI Chat and Window Awareness send data to our secure proxy server for processing - no data is stored permanently
- **Local storage**: All settings saved locally in `settings.json`
- **Open source**: Full code available for audit
- **No admin rights**: Runs with standard user permissions
- **Privacy controls**: Window Awareness can be disabled at any time

---

## 📝 Changelog

### v3.1 (January 2025)
- **Companion Avatar**: Interactive animated companion with speech bubbles
- **Detachable Sprite**: Avatar can float freely on screen
- **Trigger Mode**: Random trigger phrases with matching audio
- **AI Chat** (Patreon): Conversational AI through the avatar
- **Window Awareness** (Patreon): Contextual reactions to activities
- **Slut Mode** (Patreon): Explicit AI responses
- **Companion Tab**: Dedicated settings for avatar features
- **Quick Controls**: Mute avatar, whispers, and browser audio
- **Achievement System**: 20+ achievements to unlock
- **Session System**: Pre-built and custom conditioning sessions
- Privacy notice for Window Awareness feature

### v3.0 (December 2024)
- Complete rewrite from Python to C# WPF
- Modern dark theme UI with pink accents
- Gamification system (XP, levels, unlockables)
- Scheduler with intensity ramp
- Embedded WebView2 browser
- Comprehensive tooltip system
- Multi-monitor support improvements
- Attention check mini-game
- Bubble pop feature (Level 20 unlock)
- Double-warning dialogs for dangerous features

### v2.x (Legacy Python)
- Original Python/Tkinter implementation

---

## 🤝 Contributing

Contributions are welcome! Please feel free to submit pull requests.

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 💖 Acknowledgments

- Built with [WPF](https://docs.microsoft.com/en-us/dotnet/desktop/wpf/) and [.NET 8](https://dotnet.microsoft.com/)
- Browser powered by [WebView2](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)
- Audio handling via [NAudio](https://github.com/naudio/NAudio)
- Logging with [Serilog](https://serilog.net/)
- AI powered by Claude (Anthropic)

---

<p align="center">
  <b>✨ Good girls condition daily ✨</b>
</p>
