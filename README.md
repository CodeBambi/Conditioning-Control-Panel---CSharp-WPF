# 💗 Conditioning Control Panel v3.0

A powerful desktop application for visual and audio conditioning, featuring gamification, scheduling, and a sleek modern interface.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)
![WPF](https://img.shields.io/badge/WPF-Windows-0078D6?style=flat-square&logo=windows)
![VirusTotal](https://img.shields.io/badge/VirusTotal-0%2F72%20Clean-brightgreen?style=flat-square)

<p align="center">
  <img src="https://raw.githubusercontent.com/CodeBambi/Conditioning-Control-Panel---CSharp-WPF/main/preview.png" alt="Preview" width="800"/>
</p>

---

## 🔒 Security Verification

**This application is 100% safe and open source.**

✅ [**VirusTotal Scan: 0/69 Detections**](https://www.virustotal.com/gui/file/187927f88cbcafbcb470b75c794f0d0095e2fcf84f3fc134f5137228c46ef334/detection)

- No malware, no telemetry, no data collection
- All code is open source and auditable
- Runs entirely offline (except embedded browser)
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
- Audio whisper support
- Message pool management

### 🌀 Unlockable Features (Progression System)
- **Level 10**: Spiral Overlay + Pink Filter
- **Level 20**: Bubble Pop mini-game
- **Level 35**: Passphrase Unlock (require passphrase to stop)
- XP earned through interaction
- Visual level progression

### 📅 Scheduler
- Auto-start/stop based on time windows
- Day-of-week selection
- **Intensity Ramp**: Gradually increase settings over time
- Link multiple parameters to ramp (opacity, volume, etc.)
- End session automatically when ramp completes

### 🌐 Embedded Browser
- Built-in WebView2 browser
- Quick access to BambiCloud and other sites
- Zoom controls and navigation

### ⚙️ System Features
- System tray integration (minimize to tray)
- Global panic key (configurable)
- Windows startup option
- Dual monitor support
- Comprehensive tooltips on all settings

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

---

## ⌨️ Controls

| Key | Action |
|-----|--------|
| **Escape** (default) | Panic key - Stop engine / Exit app |
| Double-tap panic key | Force exit application |
| Click flash image | Dismiss (or spawn more in Corruption mode) |
| Click bubble | Pop for XP |

---

## 🎮 Quick Start

1. **Add Content**: Place images in `assets/images/`, videos in `assets/startle_videos/`
2. **Configure Settings**: Adjust frequencies, sizes, and features in the Settings tab
3. **Click START**: The engine begins running
4. **Minimize**: App continues running from system tray
5. **Panic Key**: Press Escape to stop, double-tap to exit

---

## 📖 Documentation

- [**Detailed Guide**](GUIDE.md) - Complete feature walkthrough
- [**Security Overview**](SECURITY_OVERVIEW.md) - Security analysis and privacy info

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

---

## 🛡️ Privacy & Security

- **No telemetry**: Zero data collection or analytics
- **No network**: Works completely offline (except browser)
- **Local storage**: All settings saved locally in `settings.json`
- **Open source**: Full code available for audit
- **No admin rights**: Runs with standard user permissions

---

## 📝 Changelog

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

---

<p align="center">
  <b>✨ Good girls condition daily ✨</b>
</p>
