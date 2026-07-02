using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ConditioningControlPanel.Core.Platform;
using ConditioningControlPanel.Core.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// Avalonia screen enumeration shim using <see cref="Screens"/>.
/// </summary>
public sealed class AvaloniaScreenProvider : IScreenProvider
{
    private readonly Func<TopLevel?>? _getTopLevel;

    public AvaloniaScreenProvider(Func<TopLevel?>? getTopLevel = null)
    {
        _getTopLevel = getTopLevel;
    }

    private EventHandler? _screensChanged;

    public event EventHandler? ScreensChanged
    {
        add
        {
            var hadSubscribers = _screensChanged != null;
            _screensChanged += value;
            if (!hadSubscribers && _screensChanged != null)
            {
                AttachToScreens();
            }
        }
        remove
        {
            _screensChanged -= value;
            if (_screensChanged == null)
            {
                DetachFromScreens();
            }
        }
    }

    private Screens? _attachedScreens;

    private void AttachToScreens()
    {
        DetachFromScreens();
        var screens = GetScreens();
        if (screens == null) return;
        _attachedScreens = screens;
        _attachedScreens.Changed += OnScreensChanged;
    }

    private void DetachFromScreens()
    {
        if (_attachedScreens == null) return;
        _attachedScreens.Changed -= OnScreensChanged;
        _attachedScreens = null;
    }

    private string _lastLayoutSignature = "";

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        // Avalonia raises Screens.Changed liberally — including when full-screen effect windows
        // (flash, video) come and go, which fires display-change events even though the monitor
        // layout is identical. Forwarding those caused the overlay service to tear down and rebuild
        // the pink/spiral windows repeatedly (a visible blink). Only forward when the actual layout
        // (count / bounds / scaling / primary) changes.
        var signature = ComputeLayoutSignature();
        if (signature == _lastLayoutSignature) return;
        _lastLayoutSignature = signature;
        // Mirror the WPF display-change path: quiesce layered-window spawns for a beat while the
        // compositor/overlay surfaces settle after a monitor/DPI change.
        DisplayChangeCoordinator.NotifyDisplayChange("screens-changed");
        _screensChanged?.Invoke(this, EventArgs.Empty);
    }

    private string ComputeLayoutSignature()
    {
        var screens = GetScreens();
        if (screens == null) return "";
        return string.Join("|", screens.All.Select(s =>
            $"{s.Bounds.X},{s.Bounds.Y},{s.Bounds.Width},{s.Bounds.Height},{s.Scaling},{s.IsPrimary}"));
    }

    private Screens? GetScreens()
    {
        if (_getTopLevel?.Invoke() is { } top)
            return top.Screens;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } window)
        {
            return window.Screens;
        }

        if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime single
            && single.MainView is { } view)
        {
            return TopLevel.GetTopLevel(view)?.Screens;
        }

        return null;
    }

    public IReadOnlyList<ScreenInfo> GetAllScreens()
    {
        var screens = GetScreens();
        if (screens is null) return Array.Empty<ScreenInfo>();

        return screens.All.Select(MapScreen).ToList();
    }

    public ScreenInfo? GetPrimaryScreen()
    {
        var screens = GetScreens();
        if (screens?.Primary is not { } primary) return null;
        return MapScreen(primary);
    }

    private static ScreenInfo MapScreen(global::Avalonia.Platform.Screen screen)
    {
        return new ScreenInfo(
            screen.DisplayName ?? string.Empty,
            new ConditioningControlPanel.Core.Platform.PixelRect(screen.Bounds.X, screen.Bounds.Y, screen.Bounds.Width, screen.Bounds.Height),
            new ConditioningControlPanel.Core.Platform.PixelRect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height),
            screen.Scaling);
    }
}
