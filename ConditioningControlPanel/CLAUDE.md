# Conditioning Control Panel - Project Context

## Overview
A WPF desktop application (.NET 8, Windows-only) that provides a conditioning/hypnosis control panel with various features including flash images, videos, AI avatar companion, achievement system, and more.

## Build & Run
```bash
cd ConditioningControlPanel
dotnet build
dotnet run
```

## Project Structure

### Key Files
- **App.xaml.cs** - Application entry point, initializes all services (Flash, Video, Audio, Subliminal, etc.), manages static service instances
- **MainWindow.xaml/.cs** - Main UI with multiple tabs (Flashes, Videos, Overlays, Subliminals, Sessions, Progression, Settings)
- **AvatarTubeWindow.xaml/.cs** - AI companion avatar window that can be attached/detached from main window, handles speech bubbles, animations, and AI interactions

### Services (Services/)
- **FlashService.cs** - Handles flash image display with GIF animation support, uses images from `App.EffectiveAssetsPath/images`
- **VideoService.cs** - Handles mandatory video playback with attention checks, uses videos from `App.EffectiveAssetsPath/videos`
- **AudioService.cs** - Audio ducking and playback management
- **SubliminalService.cs** - Subliminal text/image overlay display
- **OverlayService.cs** - Screen overlays (BrainDrain blur, edge effects, etc.)
- **BubbleService.cs** - Floating bubble popping minigame
- **BubbleCountService.cs** - Bubble counting video minigame (Level 50+)
- **SessionEngine.cs** - AI-powered session management with OpenRouter integration
- **ProgressionService.cs** - XP and leveling system
- **AchievementService.cs** - Achievement tracking and unlocks
- **UpdateService.cs** - Auto-update using Velopack

### Models (Models/)
- **AppSettings.cs** - All application settings with INotifyPropertyChanged, auto-saves to JSON
- **CompanionPromptSettings.cs** - AI companion personality customization
- **Session.cs** - Session data model

### Key Patterns
- Services are accessed via static properties on `App` class: `App.Flash`, `App.Video`, `App.Audio`, etc.
- Settings via `App.Settings.Current` (AppSettings instance)
- Assets path: `App.EffectiveAssetsPath` returns custom path if set, else default `App.UserAssetsPath`
- User data in `%APPDATA%/ConditioningControlPanel/`

### UI Architecture
- Dark theme with pink/purple accent colors (#FF69B4, #252542, #1A1A2E)
- Custom styles in MainWindow.xaml Resources section
- Tab-based navigation with animated icons
- Avatar tube window positions relative to main window when attached

### Common Issues & Solutions
1. **Crash on resize**: Wrap in try-catch, use `SizeToContent = Manual` before layout changes
2. **Null template on animation**: Check `btn.IsLoaded` and `btn.Template != null` before animations
3. **Duplicate windows**: Only one StartupUri OR manual window creation in App.xaml.cs, not both
4. **Screen enumeration crash**: Always check `Screen.AllScreens.Length > 0` before accessing - can return empty during certain system states
5. **Fire-and-forget Task crashes**: Always wrap `Task.Delay().ContinueWith()` callbacks with `if (Application.Current?.Dispatcher == null) return;` and try-catch
6. **MainWindow null during session**: SessionEngine holds reference to MainWindow - use `IsMainWindowValid` check before calling window methods
7. **Event handlers on closed windows**: Check `Application.Current.Dispatcher.HasShutdownStarted` before triggering UI operations in event handlers

### Crash Logging
- Crashes are logged to `logs/crash.log` with full stack traces
- Check this file first when debugging random crashes
- Global exception handlers catch: DispatcherUnhandledException, AppDomain.UnhandledException, TaskScheduler.UnobservedTaskException

### Dependencies
- NAudio - Audio playback
- Serilog - Logging
- XamlAnimatedGif - GIF animation support
- Velopack - Auto-updates
- System.Windows.Forms - Screen enumeration, dialogs

### Version
Check `ConditioningControlPanel.csproj` for current version in `<Version>` tag.
