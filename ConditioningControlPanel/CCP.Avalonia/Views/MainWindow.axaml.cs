using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.AvatarTube;
using ConditioningControlPanel.Avalonia.Dialogs;
using ConditioningControlPanel.Avalonia.Services.Theme;
using ConditioningControlPanel.Avalonia.ViewModels;
using ConditioningControlPanel.Avalonia.Windows;
using Avalonia.Media;
using ConditioningControlPanel.Core.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services;
using ConditioningControlPanel.Core.Services.Sessions;
using ConditioningControlPanel.Core.Services.Settings;
using ConditioningControlPanel.Core.Services.Video;
using Microsoft.Extensions.DependencyInjection;

namespace ConditioningControlPanel.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly IInputHook? _inputHook;
    private readonly ISettingsService? _settingsService;
    private readonly ISessionService? _sessionService;
    private readonly ISessionManager? _sessionManager;
    private readonly IAudioPlayer? _audioPlayer;
    private readonly ILogger<MainWindow>? _logger;
    private readonly ILockdownService? _lockdownService;

    private DateTime _lastPanicPress = DateTime.MinValue;
    private int _panicPressCount;

    public MainWindow()
    {
        InitializeComponent();

        _inputHook = App.Services?.GetService<IInputHook>();
        _settingsService = App.Services?.GetService<ISettingsService>();
        _sessionService = App.Services?.GetService<ISessionService>();
        _sessionManager = App.Services?.GetService<ISessionManager>();
        _audioPlayer = App.Services?.GetService<IAudioPlayer>();
        _logger = App.Services?.GetRequiredService<ILogger<MainWindow>>();
        _lockdownService = App.Services?.GetService<ILockdownService>();

        var themeService = App.Services?.GetService<AvaloniaThemeService>();
        ApplyPlayerTitleShadow();
        if (themeService != null)
            themeService.ThemeChanged += (_, _) => ApplyPlayerTitleShadow();

        // Remove the native title bar and extend the client area on all platforms.
        var chrome = App.Services?.GetService<IWindowChrome>();
        chrome?.ExtendClientArea(this, true);
        if (themeService != null)
            themeService.ThemeChanged += (_, _) => ApplyWindowChrome();
        ApplyWindowChrome();

        // Window chrome: drag from the custom title bar and double-click to maximize.
        if (TitleBarGrid != null)
        {
            TitleBarGrid.PointerPressed += TitleBar_PointerPressed;
        }

        Closing += OnClosing;
        KeyDown += OnKeyDown;
        Opened += OnOpened;

        AddHandler(DragDrop.DragEnterEvent, MainWindow_DragEnter);
        AddHandler(DragDrop.DragLeaveEvent, MainWindow_DragLeave);
        AddHandler(DragDrop.DragOverEvent, MainWindow_DragOver);
        AddHandler(DragDrop.DropEvent, MainWindow_Drop);

        WirePanicKey();
        WireAvatarEnabledChange();
    }

    private void ApplyWindowChrome()
    {
        var chrome = App.Services?.GetService<IWindowChrome>();
        if (chrome == null) return;

        var handle = this.TryGetPlatformHandle()?.Handle;
        var dark = Application.Current?.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark;
        chrome.SetDarkTitleBar(handle, dark);
    }

    private void ApplyPlayerTitleShadow()
    {
        if (PlayerTitleShadowBorder == null) return;

        var color = (Application.Current?.TryFindResource("TransparentPink60", out var res) == true && res is Color c)
            ? c
            : Color.Parse("#99FF69B4");

        PlayerTitleShadowBorder.BoxShadow = new BoxShadows(new BoxShadow
        {
            OffsetX = 0,
            OffsetY = 0,
            Blur = 8,
            Spread = 0,
            Color = color
        });
    }

    private void WirePanicKey()
    {
        if (_inputHook == null) return;
        _inputHook.KeyPressed += (_, e) =>
        {
            if (!IsPanicKey(e.VirtualKeyCode)) return;

            // The low-level hook runs on a background thread; marshal to the UI thread.
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    HandlePanicKeyPress();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Panic-key handler failed");
                }
            });
        };
    }

    private bool IsPanicKey(int virtualKeyCode)
    {
        var settings = _settingsService?.Current;
        if (settings?.PanicKeyEnabled != true) return false;
        if (string.IsNullOrWhiteSpace(settings.PanicKey)) return false;

        return VirtualKeyToName(virtualKeyCode).Equals(settings.PanicKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string VirtualKeyToName(int virtualKeyCode)
    {
        return virtualKeyCode switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x13 => "Pause",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2C => "PrintScreen",
            0x2D => "Insert",
            0x2E => "Delete",
            >= 0x30 and <= 0x39 => ((char)('0' + (virtualKeyCode - 0x30))).ToString(),
            >= 0x41 and <= 0x5A => ((char)('A' + (virtualKeyCode - 0x41))).ToString(),
            0x60 => "NumPad0",
            0x61 => "NumPad1",
            0x62 => "NumPad2",
            0x63 => "NumPad3",
            0x64 => "NumPad4",
            0x65 => "NumPad5",
            0x66 => "NumPad6",
            0x67 => "NumPad7",
            0x68 => "NumPad8",
            0x69 => "NumPad9",
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            0x90 => "NumLock",
            0x91 => "ScrollLock",
            _ => $"VK{virtualKeyCode:X}"
        };
    }

    private void HandlePanicKeyPress()
    {
        // Lockdown mode owns all key handling; panic key is intentionally disabled.
        if (_lockdownService?.IsActive == true) return;

        _logger?.LogInformation("Panic key pressed");

        var now = DateTime.Now;
        if ((now - _lastPanicPress).TotalMilliseconds > 2000)
            _panicPressCount = 0;
        _panicPressCount++;
        _lastPanicPress = now;

        // First press while running: stop audio and pause the active session.
        // Effects stop via the SessionPaused -> StopEffects wiring in the view model.
        if (_sessionService?.State == SessionState.Running)
        {
            try { _audioPlayer?.Stop(); } catch { /* best effort */ }
            _sessionService.PauseSession();
            RestoreWindow();
            return;
        }

        // First press while an engine-only run is active (plain START, no session):
        // stop the engine, mirroring WPF panic-while-running -> StopEngine
        // (MainWindow.xaml.cs:811).
        if (_sessionService?.State == SessionState.Idle &&
            DataContext is MainWindowViewModel { IsEngineRunning: true } vm)
        {
            try { _audioPlayer?.Stop(); } catch { /* best effort */ }
            vm.StopEngineOnlyRun();
            RestoreWindow();
            return;
        }

        // Second press while stopped: exit the application.
        if (_panicPressCount >= 2)
        {
            _logger?.LogInformation("Double panic: exiting application");
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                try { _audioPlayer?.Stop(); } catch { /* best effort */ }
                _sessionService?.StopSession(completed: false);

                // Synchronous teardown before Shutdown (WPF MainWindow.xaml.cs:849-877):
                // the SessionStopped cleanup is posted to the dispatcher and would race
                // process exit, leaving orphaned LibVLC windows and unsaved settings.
                try { App.Services?.GetService<ISessionEffectOrchestrator>()?.StopEffects(); } catch { /* best effort */ }
                try { App.Services?.GetService<IVideoService>()?.ForceCleanup(); } catch { /* best effort */ }
                try { _settingsService?.SaveImmediate(); } catch { /* best effort */ }

                try { _avatarTubeWindow?.Close(); } catch { /* best effort */ }
                desktop.Shutdown();
            }
        }
    }

    // Start-options flyout (Start normally / Jump right in), WPF MainWindow.StartStop.cs:118-126.
    private void MenuStartNormal_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.StartNormallyCommand.Execute(null);
    }

    private void MenuJumpRightIn_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.JumpRightInCommand.Execute(null);
    }

    private void RestoreWindow()
    {
        try
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            ShowAvatarTube();
        }
        catch
        {
            // window may be shutting down
        }
    }

    private void TitleBar_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        if (e.ClickCount == 2)
        {
            // Double-click toggles maximize.
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            // Restore to normal before dragging; center the window on the cursor.
            var point = e.GetPosition(this) * PixelDensity;
            WindowState = WindowState.Normal;
            Position = new PixelPoint(
                (int)(point.X - Width / 2),
                (int)(point.Y - 15));
        }

        BeginMoveDrag(e);
    }

    private double PixelDensity
    {
        get
        {
            try
            {
                var screen = Screens.ScreenFromWindow(this);
                return screen?.Scaling ?? 1.0;
            }
            catch
            {
                return 1.0;
            }
        }
    }

    private void ResizeBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control control)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var edge = control.Tag?.ToString() switch
        {
            "NorthWest" => WindowEdge.NorthWest,
            "NorthEast" => WindowEdge.NorthEast,
            "SouthWest" => WindowEdge.SouthWest,
            "SouthEast" => WindowEdge.SouthEast,
            "North" => WindowEdge.North,
            "South" => WindowEdge.South,
            "West" => WindowEdge.West,
            "East" => WindowEdge.East,
            _ => (WindowEdge?)null
        };

        if (edge.HasValue)
        {
            BeginResizeDrag(edge.Value, e);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        // Forward tab hotkeys to the view model.
        if (vm.HandleKeyGesture(e.KeyModifiers, e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Prevent re-entrant shutdown from ExitApplicationAsync calling desktop.Shutdown().
        if (e.IsProgrammatic)
        {
            try { _avatarTubeWindow?.Close(); } catch { /* best effort */ }
            return;
        }

        // Unless the user holds Shift or the view model is forcing shutdown,
        // minimize to tray instead of closing.
        if (DataContext is MainWindowViewModel { IsEngineRunning: true } vm)
        {
            e.Cancel = true;
            _ = vm.ExitApplicationCommand.ExecuteAsync(null);
            return;
        }

        e.Cancel = true;
        WindowState = WindowState.Minimized;
    }

    private async void BugReport_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var bugReport = new Windows.BugReportWindow();
            await bugReport.ShowDialog(this);
        }
        catch (Exception ex)
        {
            var logger = App.Services?.GetRequiredService<ILogger<MainWindow>>();
            logger?.LogError(ex, "Failed to open bug report window");
        }
    }

    private void WebcamActivePill_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.StopWebcamCommand?.Execute(null);
        }
    }

    #region Drag-and-Drop Import

    private void MainWindow_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            if (DataContext is MainWindowViewModel vm)
            {
                vm.DropOverlayVisible = true;
            }
        }
    }

    private void MainWindow_DragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
            if (DataContext is MainWindowViewModel vm)
            {
                vm.DropOverlayVisible = true;
            }
        }
    }

    private void MainWindow_DragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.DropOverlayVisible = false;
        }
    }

    private void MainWindow_Drop(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.DropOverlayVisible = false;
        }

        if (!e.DataTransfer.Formats.Contains(DataFormat.File))
            return;

        var files = e.DataTransfer.TryGetFiles()?.ToList();
        if (files == null || files.Count == 0)
            return;

        int imported = 0, failed = 0;
        foreach (var file in files)
        {
            var path = file.TryGetLocalPath();
            if (string.IsNullOrEmpty(path)) continue;

            try
            {
                if (path.EndsWith(".session.json", StringComparison.OrdinalIgnoreCase))
                {
                    var (success, message, session) = _sessionManager?.ImportSession(path) ?? (false, "Session manager not available", null);
                    if (success && session != null)
                    {
                        imported++;
                        _logger?.LogInformation("Drag-drop imported session: {Name}", session.Name);
                    }
                    else
                    {
                        failed++;
                        _logger?.LogWarning("Drag-drop session import failed: {Message}", message);
                    }
                }
                else if (path.EndsWith(".preset.json", StringComparison.OrdinalIgnoreCase))
                {
                    var json = File.ReadAllText(path);
                    var preset = Newtonsoft.Json.JsonConvert.DeserializeObject<Preset>(json);
                    if (preset != null && _settingsService?.Current != null)
                    {
                        if (string.IsNullOrWhiteSpace(preset.Name))
                            preset.Name = Path.GetFileNameWithoutExtension(path).Replace(".preset", "", StringComparison.OrdinalIgnoreCase);
                        _settingsService.Current.UserPresets.Add(preset);
                        _settingsService.Save();
                        imported++;
                        _logger?.LogInformation("Drag-drop imported preset: {Name}", preset.Name);
                    }
                    else
                    {
                        failed++;
                    }
                }
                else
                {
                    failed++;
                    _logger?.LogDebug("Drag-drop ignored unsupported file: {Path}", path);
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger?.LogWarning(ex, "Drag-drop import failed for {Path}", path);
            }
        }

        if (DataContext is MainWindowViewModel vm2)
        {
            if (failed == 0 && imported > 0)
                vm2.AddNotification(Loc.Get("btn_import"), Loc.GetF("msg_imported_items_fmt", imported));
            else if (imported > 0)
                vm2.AddNotification(Loc.Get("btn_import"), Loc.GetF("msg_imported_with_failed_fmt", imported, failed));
            else
                vm2.AddNotification(Loc.Get("btn_import"), Loc.GetF("msg_no_items_imported_fmt", failed));
        }
    }

    #endregion

    #region Startup Dialogs

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            await TryShowWelcomeDialogAsync();
            await TryShowWhatsNewDialogAsync();
            await TryShowSeasonRecapAsync();
        }
        catch (Exception ex)
        {
            var logger = App.Services?.GetRequiredService<ILogger<MainWindow>>();
            logger?.LogWarning(ex, "Failed to present startup dialogs");
        }

        InitializeAvatarTube();
        EnsureAvatarTubeFitsOnScreen();
        ApplyWindowChrome();
    }

    private async Task TryShowWelcomeDialogAsync()
    {
        if (App.Services?.GetService<ISettingsService>() is not { } settingsService)
            return;

        if (settingsService.Current is not { } settings || settings.Welcomed)
            return;

        var dialog = new WelcomeDialog();
        await dialog.ShowDialog<bool?>(this);

        settings.Welcomed = true;
        settingsService.Save();
    }

    private async Task TryShowWhatsNewDialogAsync()
    {
        // Shown once after an update, when LastSeenVersion lags the running version.
        await WhatsNewDialog.ShowIfNeeded(this);
    }

    private async Task TryShowSeasonRecapAsync()
    {
        if (App.Services?.GetService<ISettingsService>() is not { } settingsService)
            return;

        if (settingsService.Current is not { } settings)
            return;

        var currentSeason = DateTime.UtcNow.ToString("yyyy-MM");
        if (!settings.SeasonResetPending && settings.LastSeasonResetSeen == currentSeason)
            return;

        // Only surface the recap for users who have progression to lose.
        if (settings.HighestLevelEver < 2)
        {
            settings.LastSeasonResetSeen = currentSeason;
            settings.SeasonResetPending = false;
            settingsService.Save();
            return;
        }

        var snapshot = SeasonRecapService.CaptureAndRollover(currentSeason);
        if (snapshot == null)
        {
            settings.LastSeasonResetSeen = currentSeason;
            settings.SeasonResetPending = false;
            settingsService.Save();
            return;
        }

        var vm = new SeasonRecapViewModel(snapshot);
        var window = new SeasonRecapWindow(vm);
        await window.ShowDialog(this);

        settings.LastSeasonResetSeen = currentSeason;
        settings.SeasonResetPending = false;
        settingsService.Save();
    }

    #endregion
}
