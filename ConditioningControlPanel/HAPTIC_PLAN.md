# Haptic Feedback Implementation Plan

## Overview
Add haptic feedback support for Lovense toys and Buttplug.io compatible devices, triggered by in-app events like bubble pops, flash images, videos, and more.

## Architecture

### New Files
1. **Services/HapticService.cs** - Main haptic feedback orchestration service
2. **Services/Haptics/LovenseProvider.cs** - Lovense Local API integration
3. **Services/Haptics/ButtplugProvider.cs** - Buttplug.io integration via Intiface
4. **Services/Haptics/IHapticProvider.cs** - Common interface for haptic providers
5. **Models/HapticSettings.cs** - Configuration for haptic feedback

### Dependencies
- **Buttplug.Client** NuGet package (for Buttplug.io/Intiface integration)
- No external package needed for Lovense (uses HTTP API to Lovense Connect app)

## Integration Points (Events to Hook)

| Event | Source | Default Intensity | Duration |
|-------|--------|-------------------|----------|
| Bubble Popped | BubbleService.OnBubblePopped | Low (20%) | Short pulse (200ms) |
| Flash Displayed | FlashService.FlashDisplayed | Medium (50%) | Medium pulse (500ms) |
| Flash Clicked | FlashService.FlashClicked | High (80%) | Short pulse (300ms) |
| Video Started | VideoService.VideoStarted | Low (30%) | Ramp up over 2s |
| Video Playing | VideoService (continuous) | Variable (30-70%) | Continuous pattern |
| Video Ended | VideoService.VideoEnded | Medium (50%) | Ramp down over 1s |
| Subliminal Displayed | SubliminalService.SubliminalDisplayed | Low (20%) | Brief pulse (150ms) |
| Attention Target Hit | VideoService (attention check) | High (90%) | Reward pulse (400ms) |
| Level Up | ProgressionService.LevelUp | Max (100%) | Celebration pattern (2s) |
| Achievement Unlocked | AchievementService.AchievementUnlocked | High (80%) | Success pattern (1s) |

## Provider Implementations

### 1. Lovense Provider (LovenseProvider.cs)
Uses Lovense Connect local API (runs on user's PC/phone):
- **Endpoint**: `https://api.lovense.com/api/lan/v2/command` or local `http://127.0.0.1:20010/command`
- **Discovery**: Query `https://api.lovense.com/api/lan/getToys` or local discovery
- **Commands**: `Vibrate`, `Rotate`, `Pump`, `Thrusting` with intensity 0-20

```csharp
public class LovenseProvider : IHapticProvider
{
    private HttpClient _client;
    private string _localUrl = "http://127.0.0.1:20010";

    public async Task<bool> ConnectAsync();
    public async Task VibrateAsync(int intensity, int durationMs);
    public async Task PatternAsync(string pattern, int durationMs);
    public async Task StopAsync();
    public bool IsConnected { get; }
    public List<string> ConnectedToys { get; }
}
```

### 2. Buttplug Provider (ButtplugProvider.cs)
Uses Buttplug.io protocol via Intiface Central:
- **Connection**: WebSocket to `ws://127.0.0.1:12345` (Intiface default)
- **Protocol**: Buttplug.io v3 message protocol
- **Supports**: Any Buttplug-compatible device (Lovense, We-Vibe, Kiiroo, etc.)

```csharp
public class ButtplugProvider : IHapticProvider
{
    private ButtplugClient _client;

    public async Task<bool> ConnectAsync();
    public async Task VibrateAsync(int intensity, int durationMs);
    public async Task PatternAsync(string pattern, int durationMs);
    public async Task StopAsync();
    public bool IsConnected { get; }
    public List<ButtplugClientDevice> ConnectedDevices { get; }
}
```

## HapticService Design

```csharp
public class HapticService : IDisposable
{
    private IHapticProvider? _activeProvider;
    private readonly LovenseProvider _lovense;
    private readonly ButtplugProvider _buttplug;

    // Events
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<string>? DeviceDiscovered;

    // State
    public bool IsEnabled { get; set; }
    public bool IsConnected => _activeProvider?.IsConnected ?? false;
    public HapticProviderType ActiveProviderType { get; set; }

    // Core Methods
    public async Task<bool> ConnectAsync();
    public async Task DisconnectAsync();
    public async Task TriggerAsync(HapticEvent eventType);
    public async Task TriggerCustomAsync(int intensity, int durationMs);
    public async Task StopAsync();

    // Hook into app events
    public void HookEvents();
    public void UnhookEvents();
}
```

## Settings (HapticSettings.cs)

```csharp
public class HapticSettings : INotifyPropertyChanged
{
    // General
    public bool HapticsEnabled { get; set; } = false;
    public HapticProviderType Provider { get; set; } = HapticProviderType.Lovense;
    public bool AutoConnect { get; set; } = true;

    // Per-Event Intensity (0-100)
    public int BubblePopIntensity { get; set; } = 20;
    public int FlashDisplayIntensity { get; set; } = 50;
    public int FlashClickIntensity { get; set; } = 80;
    public int VideoPlayingIntensity { get; set; } = 50;
    public int SubliminalIntensity { get; set; } = 20;
    public int AttentionTargetIntensity { get; set; } = 90;
    public int LevelUpIntensity { get; set; } = 100;
    public int AchievementIntensity { get; set; } = 80;

    // Per-Event Enable/Disable
    public bool BubblePopEnabled { get; set; } = true;
    public bool FlashDisplayEnabled { get; set; } = true;
    public bool FlashClickEnabled { get; set; } = true;
    public bool VideoPlayingEnabled { get; set; } = true;
    public bool SubliminalEnabled { get; set; } = true;
    public bool AttentionTargetEnabled { get; set; } = true;
    public bool LevelUpEnabled { get; set; } = true;
    public bool AchievementEnabled { get; set; } = true;

    // Connection Settings
    public string LovenseLocalUrl { get; set; } = "http://127.0.0.1:20010";
    public string ButtplugServerUrl { get; set; } = "ws://127.0.0.1:12345";
}
```

## UI Changes (Settings Tab)

Add new "Haptics" section to Settings tab:
- Toggle: Enable Haptic Feedback
- Dropdown: Provider (Lovense / Buttplug.io)
- Button: Connect / Disconnect
- Status: Connected toys list
- Intensity sliders for each event type (collapsible section)

## Implementation Steps

### Phase 1: Core Infrastructure
1. Add `Buttplug.Client` NuGet package to project
2. Create `IHapticProvider` interface
3. Create `HapticSettings.cs` model
4. Add HapticSettings to AppSettings.cs
5. Create `HapticService.cs` skeleton

### Phase 2: Provider Implementations
6. Implement `LovenseProvider.cs` with HTTP API
7. Implement `ButtplugProvider.cs` with WebSocket client
8. Add connection/discovery logic to both providers
9. Test individual provider connections

### Phase 3: Event Integration
10. Add event subscription in HapticService.HookEvents()
11. Hook into BubbleService.OnBubblePopped
12. Hook into FlashService.FlashDisplayed/FlashClicked
13. Hook into VideoService.VideoStarted/VideoEnded
14. Hook into SubliminalService.SubliminalDisplayed
15. Hook into ProgressionService.LevelUp
16. Hook into AchievementService.AchievementUnlocked

### Phase 4: UI Implementation
17. Add Haptics section to Settings tab in MainWindow.xaml
18. Add connection status indicator
19. Add intensity sliders for each event
20. Add test button to verify connection

### Phase 5: Polish
21. Add error handling and reconnection logic
22. Add logging for haptic events
23. Save/restore connection state
24. Add haptic patterns (not just simple vibrations)

## Testing Checklist
- [ ] Lovense Connect app detection and connection
- [ ] Intiface Central connection
- [ ] Bubble pop triggers haptic
- [ ] Flash image triggers haptic
- [ ] Video playing continuous haptic
- [ ] Attention target success haptic
- [ ] Level up celebration haptic
- [ ] Settings persist across restart
- [ ] Graceful disconnect handling
- [ ] No crashes if toy disconnects mid-session

## Notes
- Lovense Connect must be running on user's PC for local connection
- Intiface Central must be running for Buttplug.io connection
- Both apps are free downloads from their respective websites
- User will need to pair toys in the respective apps first
