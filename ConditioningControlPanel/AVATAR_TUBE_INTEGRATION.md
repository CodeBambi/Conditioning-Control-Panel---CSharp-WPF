# AvatarTubeWindow Integration Guide

This document covers the technical implementation of the Companion Avatar feature.

## Overview

The `AvatarTubeWindow` is a transparent, always-on-top window that displays an animated companion avatar. It can be attached to the main window or detached to float freely on the screen.

## Features

### Core Features
- **Animated poses**: Multiple poses with smooth transitions
- **Speech bubbles**: Display messages with auto-sizing and word wrapping
- **Giggle sounds**: Optional audio feedback
- **Context menu**: Right-click for quick access to controls

### Advanced Features
- **Detachable sprite**: Float freely or attach to main window
- **Trigger Mode**: Display random trigger phrases with matching audio
- **AI Chat**: Double-click for conversational AI (Patreon)
- **Window Awareness**: React to user activities (Patreon)
- **Quick controls**: Mute avatar, whispers, browser audio

---

## Integration with MainWindow

### 1. Add the field (with other private fields):

```csharp
// Avatar Tube Window
private AvatarTubeWindow? _avatarTubeWindow;
```

### 2. Add initialization in MainWindow_Loaded:

```csharp
// Initialize Avatar Tube Window
InitializeAvatarTube();
```

### 3. Add the initialization method:

```csharp
#region Avatar Tube Window

private void InitializeAvatarTube()
{
    try
    {
        _avatarTubeWindow = new AvatarTubeWindow(this);
        _avatarTubeWindow.Show();
        _avatarTubeWindow.StartPoseAnimation();
        App.Logger?.Information("Avatar Tube Window initialized");
    }
    catch (Exception ex)
    {
        App.Logger?.Error("Failed to initialize Avatar Tube Window: {Error}", ex.Message);
    }
}

public void ShowAvatarTube()
{
    _avatarTubeWindow?.Show();
    _avatarTubeWindow?.StartPoseAnimation();
}

public void HideAvatarTube()
{
    _avatarTubeWindow?.StopPoseAnimation();
    _avatarTubeWindow?.Hide();
}

public void SetAvatarPose(int poseNumber)
{
    _avatarTubeWindow?.SetPose(poseNumber);
}

#endregion
```

### 4. Clean up on closing:

```csharp
_avatarTubeWindow?.Close();
```

---

## Context Menu Options

The avatar provides a right-click context menu with the following options:

| Menu Item | Method | Description |
|-----------|--------|-------------|
| Detach/Attach | `ToggleDetached()` | Toggle between attached and floating mode |
| Trigger Mode | `MenuItemTriggerMode_Click` | Enable random trigger phrases |
| Random Bubble | `MenuItemRandomBubble_Click` | Enable random floating bubbles |
| Slut Mode | `MenuItemSlutMode_Click` | Enable explicit AI (Patreon) |
| Mute Avatar | `MenuItemMute_Click` | Silence speech and sounds |
| Mute Whispers | `MenuItemMuteWhispers_Click` | Toggle subliminal audio |
| Pause Browser | `MenuItemPauseBrowser_Click` | Pause browser audio/video |
| Chat with Bambi | `MenuItemChat_Click` | Open AI chat input (Patreon) |
| Dismiss Avatar | `MenuItemDismiss_Click` | Hide the avatar |

---

## Speech Bubble System

### Showing Messages

```csharp
// Simple message
_avatarTubeWindow.Giggle("Hello!");

// With sound
_avatarTubeWindow.Giggle("Hello!", playSound: true);

// Priority message (clears queue)
_avatarTubeWindow.GigglePriority("Important message!");
```

### Speech Queue
Messages are queued if the avatar is already speaking. The queue is processed with a 1-second delay between messages.

### Duration Calculation
Display duration is calculated based on text length:
- Base: 5 seconds
- Per character: +0.05 seconds
- Clamped to 5-14 seconds

---

## Trigger Mode

When enabled, the avatar displays random trigger phrases at set intervals.

### Settings
- `TriggerModeEnabled`: Master toggle
- `TriggerIntervalSeconds`: Time between triggers (10-600s)
- `TriggerPhrases`: List of phrases to display

### Audio Integration
When a trigger is displayed, matching audio from `Resources/sub_audio/` is played:
- Audio files are matched case-insensitively
- Apostrophe variations are handled (`'` and `'`)
- Only plays if `SubAudioEnabled` is true

---

## Window Awareness Integration

The avatar can react to user activities through the `WindowAwarenessService`.

### Events
- `OnActivityChanged`: Called when user switches to a new activity
- `OnStillOnActivity`: Called when user stays on the same activity

### Reaction Logic
1. Check if speech bubble is visible (wait for user to clear it)
2. Generate contextual AI response
3. Display response in speech bubble

---

## Detachable Mode

### Attached Mode (Default)
- Positioned to the right of main window
- Moves with main window
- Cannot be dragged

### Detached Mode
- Floats freely on screen
- Can be dragged to any position
- Stays in position when main window moves

### Toggle Methods
```csharp
_avatarTubeWindow.ToggleDetached();
bool isDetached = _avatarTubeWindow.IsDetached;
```

---

## Sync Methods

### MainWindow → Avatar
```csharp
_avatarTubeWindow.SetMuteAvatar(bool isMuted);
_avatarTubeWindow.SetMuteWhispers(bool isMuted);
_avatarTubeWindow.SetBrowserPaused(bool isPaused);
_avatarTubeWindow.SetSlutMode(bool enabled);
_avatarTubeWindow.UpdateQuickMenuState();
```

### Avatar → MainWindow
```csharp
mainWindow.SyncQuickControlsUI(muteAvatar, muteWhispers, pauseBrowser);
mainWindow.SyncWhispersUI(enabled);
mainWindow.SyncSlutModeUI(enabled);
mainWindow.SyncTriggerModeUI(enabled);
```

---

## Resource Files

### Required Images
Place in `Resources/` folder and set as `Resource` in csproj:
- `tube.png` - Background tube/glow effect
- `avatar_pose1.png` - Pose 1
- `avatar_pose2.png` - Pose 2
- `avatar_pose3.png` - Pose 3
- `avatar_pose4.png` - Pose 4
- `speechbubble1.png` - Speech bubble background

### Sound Files
Place in `Resources/sounds/`:
- `giggle1.MP3` - Giggle sound variant 1
- `giggle2.MP3` - Giggle sound variant 2
- `giggle3.MP3` - Giggle sound variant 3
- `giggle4.MP3` - Giggle sound variant 4

### Trigger Audio
Place in `Resources/sub_audio/`:
- Files should match trigger names (e.g., `GOOD GIRL.mp3`)
- Case-insensitive matching
- Apostrophe normalization

---

## Troubleshooting

### Avatar doesn't appear
1. Check if images exist in `Resources/` folder
2. Verify images are set as `Resource` in csproj
3. Check log files for initialization errors
4. Main window may be near screen edge (avatar off-screen)

### Speech bubbles not showing
1. Check `_isMuted` is false
2. Check `SpeechBubble.Visibility`
3. Verify `TxtSpeech.Text` is being set

### Trigger audio not playing
1. Verify `SubAudioEnabled` is true
2. Check audio files exist in `Resources/sub_audio/`
3. File names should match triggers (case-insensitive)
4. Check `FindLinkedAudio()` returns a path

### Context menu not appearing
1. Right-click on the avatar image, not the tube
2. Check `AvatarContextMenu` is defined in XAML
3. Verify `ContextMenuOpening` event handler

---

## Project File Configuration

```xml
<ItemGroup>
  <Resource Include="Resources\tube.png" />
  <Resource Include="Resources\avatar_pose1.png" />
  <Resource Include="Resources\avatar_pose2.png" />
  <Resource Include="Resources\avatar_pose3.png" />
  <Resource Include="Resources\avatar_pose4.png" />
  <Resource Include="Resources\speechbubble1.png" />
</ItemGroup>

<ItemGroup>
  <Content Include="Resources\sounds\*.MP3">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
  <Content Include="Resources\sub_audio\*.mp3">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```
