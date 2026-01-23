# Conditioning Control Panel - Project Context

## Overview
A WPF desktop application (.NET 8, Windows-only) that provides a conditioning/hypnosis control panel with various features including flash images, videos, AI avatar companion, achievement system, and more.

## Build & Run
```bash
cd ConditioningControlPanel
dotnet build
dotnet run
```

## Quick File Reference

### Version Locations (ALL must be updated for releases)
| File | What |
|------|------|
| `ConditioningControlPanel.csproj:11` | `<Version>` tag |
| `Services/UpdateService.cs:25` | `AppVersion` constant |
| `Services/UpdateService.cs:31` | `CurrentPatchNotes` |
| `../installer.iss:17` | `MyAppVersion` |
| `../build-installer.bat:10` | `VERSION` |
| `MainWindow.xaml:741` | `BtnUpdateAvailable` Content |
| `../docs/index.html` | Download buttons + URLs |

See `../RELEASE_WORKFLOW.md` for full release process.

### Important Paths
| Path | Purpose |
|------|---------|
| `logs/crash.log` | Crash logs with stack traces - CHECK THIS FIRST |
| `%APPDATA%/ConditioningControlPanel/` | User data, settings, tokens |
| `%APPDATA%/ConditioningControlPanel/assets/` | Default assets folder |
| `App.EffectiveAssetsPath` | User's chosen assets folder (or default) |
| `../docs/` | GitHub Pages website |
| `../releases/` | Velopack release output |
| `../installer-output/` | Inno Setup installer output |
| `../patreon-proxy-server/` | Vercel proxy server code |

### Server Endpoints (codebambi-proxy.vercel.app)
| Endpoint | Purpose |
|----------|---------|
| `POST /patreon/authorize` | OAuth initiation |
| `POST /patreon/token` | Token exchange |
| `GET /patreon/validate` | Validate subscription + whitelist check |
| `POST /patreon/refresh` | Refresh tokens |
| `GET /config/update-banner` | Force update notification banner |
| `POST /pack/download-url` | Get signed pack download URL |
| `GET /pack/status` | Pack download rate limits |

## Common Tasks

### Debug a Crash
1. Check `logs/crash.log` for stack trace
2. Search for the exception type in codebase
3. Common culprits: null references in async callbacks, WPF resource lookup failures

### Add a New Setting
1. Add property to `Models/AppSettings.cs` with `[JsonProperty]`
2. Add UI control in `MainWindow.xaml` (usually in Settings tab)
3. Bind to `App.Settings.Current.YourProperty`

### Add a New Service
1. Create `Services/YourService.cs`
2. Add static property in `App.xaml.cs`: `public static YourService? YourService { get; private set; }`
3. Initialize in `App.OnStartup()` after other services

### Release a New Version
Follow `../RELEASE_WORKFLOW.md` - covers all version locations, build steps, and deployment.

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
- **UpdateService.cs** - Auto-update using Velopack + GitHub releases
- **PatreonService.cs** - Patreon OAuth, subscription validation, whitelist (server-side)
- **ContentPackService.cs** - Download/install encrypted content packs

### Models (Models/)
- **AppSettings.cs** - All application settings with INotifyPropertyChanged, auto-saves to JSON
- **CompanionPromptSettings.cs** - AI companion personality customization
- **Session.cs** - Session data model
- **PatreonModels.cs** - Patreon API response models, cache state

### Key Patterns
- Services are accessed via static properties on `App` class: `App.Flash`, `App.Video`, `App.Audio`, `App.Patreon`, etc.
- Settings via `App.Settings.Current` (AppSettings instance)
- Assets path: `App.EffectiveAssetsPath` returns custom path if set, else default `App.UserAssetsPath`
- User data in `%APPDATA%/ConditioningControlPanel/`
- Patreon features gated by `App.Patreon?.HasPremiumAccess` or `App.Patreon?.HasAiAccess`

### UI Architecture
- Dark theme with pink/purple accent colors (#FF69B4, #252542, #1A1A2E)
- Custom styles in MainWindow.xaml Resources section
- Tab-based navigation with animated icons
- Avatar tube window positions relative to main window when attached
- Converters must be in Window.Resources (not local Grid.Resources) to work in DataTemplates

## Known Issues & Solutions

### WPF Issues
1. **Crash on resize**: Wrap in try-catch, use `SizeToContent = Manual` before layout changes
2. **Null template on animation**: Check `btn.IsLoaded` and `btn.Template != null` before animations
3. **Duplicate windows**: Only one StartupUri OR manual window creation in App.xaml.cs, not both
4. **Resource not found in DataTemplate**: Move converters/resources to Window.Resources, not local Grid.Resources
5. **Screen enumeration crash**: Always check `Screen.AllScreens.Length > 0` before accessing - can return empty during certain system states

### Async/Threading Issues
6. **Fire-and-forget Task crashes**: Always wrap `Task.Delay().ContinueWith()` callbacks with `if (Application.Current?.Dispatcher == null) return;` and try-catch
7. **MainWindow null during session**: SessionEngine holds reference to MainWindow - use `IsMainWindowValid` check before calling window methods
8. **Event handlers on closed windows**: Check `Application.Current.Dispatcher.HasShutdownStarted` before triggering UI operations in event handlers

### Build Issues
9. **Velopack "Access denied"**: Delete `%LOCALAPPDATA%\Temp\Velopack` folder and retry
10. **Build warnings about Screen**: These are CA1416 platform warnings - safe to ignore for Windows-only app

## Crash Logging
- Crashes are logged to `logs/crash.log` with full stack traces
- Check this file first when debugging random crashes
- Global exception handlers catch: DispatcherUnhandledException, AppDomain.UnhandledException, TaskScheduler.UnobservedTaskException

## Dependencies
- NAudio - Audio playback
- Serilog - Logging
- XamlAnimatedGif - GIF animation support
- Velopack - Auto-updates
- System.Windows.Forms - Screen enumeration, dialogs
- LibVLCSharp - Video playback
- WebView2 - Embedded browser
- Newtonsoft.Json - JSON serialization

## Architecture Notes

### Initialization Order (App.OnStartup)
1. Logger (Serilog)
2. Settings (AppSettings.Load)
3. Core services (Flash, Video, Audio, Subliminal, Overlay)
4. Patreon service (async validation)
5. Update service (async check)
6. MainWindow creation
7. Optional services (Autonomy, Discord, etc.)

### Patreon/Whitelist Flow
1. User logs in via OAuth -> tokens stored encrypted (DPAPI)
2. `PatreonService.ValidateSubscriptionAsync()` called on startup
3. Server returns subscription tier + `is_whitelisted` flag
4. `HasPremiumAccess` / `HasAiAccess` properties gate features
5. Results cached for 24 hours

### Update Flow
1. `UpdateService.CheckForUpdatesAsync()` on startup
2. Checks GitHub releases via Velopack
3. Compares `AppVersion` constant with latest release
4. If newer: shows update dialog
5. Downloads `.nupkg`, applies on restart
6. Fallback: Server banner notifies users who missed auto-update
