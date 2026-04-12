# Conditioning Control Panel

A desktop conditioning and hypnosis control panel for Windows, featuring flash images, mandatory videos, subliminal messaging, an AI-powered companion avatar, gamification, and session automation.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)
![WPF](https://img.shields.io/badge/WPF-Windows-0078D6?style=flat-square&logo=windows)
![Windows 10/11](https://img.shields.io/badge/Windows-10%2F11-0078D6?style=flat-square&logo=windows)
![VirusTotal](https://img.shields.io/badge/VirusTotal-0%2F72%20Clean-brightgreen?style=flat-square)

*A CC Labs LLC project*

<p align="center">
  <img src="https://raw.githubusercontent.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/main/preview.png" alt="Preview" width="800"/>
</p>

---

## About

The Conditioning Control Panel (CCP) is a fully featured conditioning toolkit for Windows. It combines visual, audio, and interactive conditioning techniques with gamification, AI, and automation to create a comprehensive desktop experience.

The desktop client is open source. Backend services (cloud sync, AI, content delivery) are operated by **CC Labs LLC**.

[Support the project on Patreon](https://www.patreon.com/CodeBambi)

---

## Features

### Conditioning Engine
Flash images with GIF support, mandatory fullscreen videos with attention checks, subliminal text and audio whispers, and screen overlays (spirals, pink filter, brain drain blur, edge effects, bouncing text).

### AI Companion
Animated avatar with speech bubbles, idle chatter, and trigger phrases. Detachable window that can float freely on screen or dock to the main window. AI-powered chat and window awareness with personality customization (Premium).

### Gamification
XP and leveling system with unlockable features at milestone levels. Skill tree, daily and weekly quests, 20+ achievements, and a seasonal leaderboard with cloud sync.

### Sessions & Automation
Pre-built conditioning sessions with phased intensity. Session scheduler with day-of-week selection and intensity ramps. Autonomy mode for hands-free operation. Remote control via session codes with PIN authentication.

### Content & Customization
Downloadable content packs, mod support, custom asset folders, 9 languages, and community prompt sharing. Drop your own images, videos, and audio into the assets folder.

### Hardware & Integration
Haptic device support, dual monitor mode, Discord rich presence, and system tray integration with configurable panic key.

See the [Feature Guide](GUIDE.md) for a full walkthrough.

---

## Premium Features

A [Patreon subscription](https://www.patreon.com/CodeBambi) unlocks:

- **AI Chat** — Conversational AI through the companion avatar with personality customization
- **Window Awareness** — Avatar reacts contextually to your active windows and browser tabs
- **Cloud Sync** — Profile, progression, and achievement sync across devices
- **Content Packs** — Downloadable themed content packs
- **Slut Mode** — Explicit AI responses and intensified reactions

**Privacy**: Window Awareness sends active window/tab names to CC Labs LLC servers for AI processing. No data is stored permanently. The feature can be disabled at any time.

---

## Security & Privacy

[**VirusTotal Scan: 0/72 Clean**](https://www.virustotal.com/gui/file/187927f88cbcafbcb470b75c794f0d0095e2fcf84f3fc134f5137228c46ef334/detection)

- Fully open source and auditable
- Core features work entirely offline
- Cloud features are handled by CC Labs LLC — no permanent data storage
- No administrator privileges required
- Local settings stored in `%APPDATA%/ConditioningControlPanel/`

---

## Getting Started

### Requirements
- **OS**: Windows 10 or 11 (64-bit)
- **Runtime**: [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Browser**: [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/) (pre-installed on most Windows 10/11 systems)

### Install via Installer (Recommended)
1. Download the latest installer from [Releases](https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/releases)
2. Run the installer and follow the prompts
3. Launch **Conditioning Control Panel** from your Start Menu or desktop

### Build from Source
```bash
git clone https://github.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF.git
cd Conditioning-Control-Panel---CSharp-WPF/ConditioningControlPanel
dotnet restore
dotnet build --configuration Release
dotnet run
```

### Quick Start
1. **Add content** — Place images in `assets/images/` and videos in `assets/videos/`
2. **Configure** — Adjust frequencies, sizes, and features in the Settings tab
3. **Meet your companion** — The avatar appears next to the window (right-click for options)
4. **Click START** — The conditioning engine begins
5. **Panic key** — Press Escape to stop, double-tap to exit

---

## Controls

| Action | Result |
|--------|--------|
| **Escape** (default) | Panic key — stop engine |
| Double-tap panic key | Force exit application |
| Click flash image | Dismiss (or spawn more in Corruption mode) |
| Click bubble | Pop for XP |
| Double-click avatar | Open AI Chat (Premium) |
| Right-click avatar | Context menu |
| Drag avatar (detached) | Reposition on screen |

---

## Troubleshooting

**Application won't start** — Install the [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). If issues persist, check `logs/crash.log` for details.

**Videos not playing** — Ensure videos are in `assets/videos/` and are `.mp4`, `.webm`, or `.avi` format.

**Flash images not appearing** — Verify `assets/images/` contains valid images (`.jpg`, `.png`, `.gif`) and the feature is enabled in the Flashes tab.

**WebView2 error** — Download and install the [WebView2 Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/).

---

## Contributing

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, code style, and PR guidelines.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/your-feature`)
3. Make your changes and test thoroughly
4. Submit a pull request

---

## Acknowledgments

- AI powered by [Claude](https://www.anthropic.com/) (Anthropic)
- Video playback via [LibVLCSharp](https://github.com/videolan/libvlcsharp)
- Audio via [NAudio](https://github.com/naudio/NAudio)
- GIF support via [XamlAnimatedGif](https://github.com/XamlAnimatedGif/XamlAnimatedGif)

## License

[MIT License](LICENSE)

---

<p align="center">
  Built by <strong>CC Labs LLC</strong>
</p>
