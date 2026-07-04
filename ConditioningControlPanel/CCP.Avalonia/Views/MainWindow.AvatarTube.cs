using System;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.AvatarTube;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views;

public partial class MainWindow
{
    private AvatarTubeWindow? _avatarTubeWindow;

    /// <summary>Exposes the initialized avatar tube window so the shared <see cref="IAvatarWindowService"/>
    /// can reuse it instead of creating a duplicate.</summary>
    public AvatarTubeWindow? AvatarTube => _avatarTubeWindow;

    /// <summary>Single-owner accessor for the avatar tube: creates the window on demand
    /// (respecting <c>AvatarEnabled</c>) and returns it. The shared
    /// <see cref="IAvatarWindowService"/> MUST route through this instead of newing up its
    /// own <see cref="AvatarTubeWindow"/> - two independent owners was the root cause of the
    /// duplicate avatar seen on theme changes (both instances subscribe to
    /// <c>ActiveModChanged</c> and repaint side by side).</summary>
    public AvatarTubeWindow? GetOrCreateAvatarTube()
    {
        InitializeAvatarTube();
        return _avatarTubeWindow;
    }

    private void InitializeAvatarTube()
    {
        if (_avatarTubeWindow != null)
            return;

        if (_settingsService?.Current?.AvatarEnabled != true)
            return;

        try
        {
            var win = new AvatarTubeWindow(this);
            _avatarTubeWindow = win;

            // A closed window can never be shown again in Avalonia - drop the reference so
            // the next Show/GetOrCreate call builds a fresh tube instead of throwing on a
            // dead one (the shared IAvatarWindowService clears its reference the same way).
            win.Closed += (_, _) =>
            {
                if (ReferenceEquals(_avatarTubeWindow, win))
                    _avatarTubeWindow = null;
            };

            if (_settingsService.Current.AvatarMuted)
                win.SetMuted(true);

            if (IsVisible && WindowState != WindowState.Minimized)
            {
                win.ShowTube();
                win.StartPoseAnimation();
            }

            _logger?.LogInformation("Avatar Tube Window initialized (visible={Visible}, pos={Pos}, size={Size})",
                win.IsVisible, win.Position, win.ClientSize);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize Avatar Tube Window");
        }
    }

    public void ShowAvatarTube()
    {
        if (_settingsService?.Current?.AvatarEnabled != true)
            return;

        if (_avatarTubeWindow == null)
        {
            InitializeAvatarTube();
        }
        else
        {
            _avatarTubeWindow.ShowTube();
            _avatarTubeWindow.StartPoseAnimation();
        }
    }

    public void HideAvatarTube()
    {
        if (_avatarTubeWindow != null)
        {
            _avatarTubeWindow.StopPoseAnimation();
            _avatarTubeWindow.HideTube();
        }
    }

    public void WakeBambiUp()
    {
        if (_avatarTubeWindow == null)
            InitializeAvatarTube();

        if (_avatarTubeWindow != null)
        {
            _avatarTubeWindow.Show();
            _avatarTubeWindow.StartPoseAnimation();
        }
    }

    private void WireAvatarEnabledChange()
    {
        if (_settingsService?.Current is not { } settings) return;
        settings.PropertyChanged -= OnAvatarSettingsPropertyChanged;
        settings.PropertyChanged += OnAvatarSettingsPropertyChanged;
    }

    private void OnAvatarSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppSettings.AvatarEnabled)) return;

        if (_settingsService?.Current?.AvatarEnabled == true)
            ShowAvatarTube();
        else
            HideAvatarTube();
    }

    /// <summary>
    /// Shift the main window right enough that the attached avatar tube is not clipped
    /// by the left edge of the working area. Keeps the dashboard + companion pair fully
    /// visible on smaller screens.
    /// </summary>
    public void EnsureAvatarTubeFitsOnScreen()
    {
        if (_avatarTubeWindow == null) return;

        try
        {
            var screen = Screens.ScreenFromWindow(this);
            if (screen == null) return;

            const int tubeWidthEstimate = 550;
            const int padding = 10;
            int minLeft = (int)(screen.WorkingArea.X + padding + tubeWidthEstimate);
            int maxLeft = (int)(screen.WorkingArea.Right - ClientSize.Width - padding);

            int desiredLeft = Math.Max(Position.X, minLeft);
            if (maxLeft > minLeft)
                desiredLeft = Math.Min(desiredLeft, maxLeft);

            if (desiredLeft != Position.X)
            {
                Position = new PixelPoint(desiredLeft, Position.Y);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to ensure avatar tube fits on screen");
        }
    }
}
