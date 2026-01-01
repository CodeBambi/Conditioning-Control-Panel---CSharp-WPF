# AvatarTubeWindow Integration Guide

## Changes to MainWindow.xaml.cs

### 1. Add the field (around line 30, with other private fields):

```csharp
// Avatar Tube Window
private AvatarTubeWindow? _avatarTubeWindow;
```

### 2. Add initialization in MainWindow_Loaded (after line 362):

```csharp
// Initialize Avatar Tube Window
InitializeAvatarTube();
```

### 3. Add the initialization method (add anywhere in the class):

```csharp
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
```

### 4. Clean up in the OnClosing override (if it exists) or in BtnClose_Click:

Find where resources are disposed and add:

```csharp
_avatarTubeWindow?.Close();
```

---

## Full example of changes needed:

### In the fields section (around line 17-40):
```csharp
private bool _isRunning = false;
private bool _isLoading = true;
private BrowserService? _browser;
private bool _browserInitialized = false;
private TrayIconService? _trayIcon;
private GlobalKeyboardHook? _keyboardHook;
private AvatarTubeWindow? _avatarTubeWindow;  // <-- ADD THIS LINE
// ... rest of fields
```

### In MainWindow_Loaded method:
```csharp
private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
{
    // Re-center after load in case DPI wasn't available in constructor
    CenterOnPrimaryScreen();
    
    // Update panic key button
    UpdatePanicKeyButton();
    
    // Handle start minimized (to tray)
    if (App.Settings.Current.StartMinimized)
    {
        _trayIcon?.MinimizeToTray();
    }
    
    // Handle auto-start engine
    if (App.Settings.Current.AutoStartEngine)
    {
        StartEngine();
    }
    
    // Auto-initialize browser on startup
    await InitializeBrowserAsync();
    
    // Initialize Avatar Tube Window  // <-- ADD THESE LINES
    InitializeAvatarTube();
}
```

### Add this new method (anywhere in the class):
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

---

## Troubleshooting

If the cylinder/avatar still doesn't show:

1. **Check if images exist**: Make sure `avatar_pose1.png` through `avatar_pose4.png` and `tube.png` are in the `Resources` folder.

2. **Check csproj**: Ensure the images are set as `Resource` (not `Content` or `None`):
```xml
<Resource Include="Resources\tube.png" />
<Resource Include="Resources\avatar_pose1.png" />
<Resource Include="Resources\avatar_pose2.png" />
<Resource Include="Resources\avatar_pose3.png" />
<Resource Include="Resources\avatar_pose4.png" />
```

3. **Check for exceptions**: Look at the log file for any errors during initialization.

4. **Window positioning**: The tube window positions to the RIGHT of the main window. If your main window is near the right edge of the screen, the tube might be off-screen.

5. **Transparency issues**: Make sure your graphics drivers are up to date. Some older systems have issues with WPF transparency.
