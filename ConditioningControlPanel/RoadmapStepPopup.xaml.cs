using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel;

/// <summary>
/// Popup window shown when a roadmap step is completed
/// </summary>
public partial class RoadmapStepPopup : Window
{
    private readonly DispatcherTimer _autoCloseTimer;

    public RoadmapStepPopup(RoadmapStepDefinition stepDef, RoadmapStepProgress progress)
    {
        InitializeComponent();

        App.Logger?.Debug("Creating RoadmapStepPopup for: {Title}", stepDef.Title);

        // Set content
        TxtStepTitle.Text = stepDef.Title;

        // Get track name
        var trackDef = RoadmapTrackDefinition.GetByTrack(stepDef.Track);
        TxtTrackName.Text = trackDef != null
            ? $"{trackDef.Name} - {trackDef.Subtitle}"
            : stepDef.Track.ToString();

        // Load photo thumbnail if available
        LoadPhotoThumbnail(progress);

        // Position in bottom-right corner of primary screen
        PositionWindow();

        // Auto-close after 5 seconds
        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            FadeOutAndClose();
        };
        _autoCloseTimer.Start();

        // Fade in animation
        Opacity = 0;
        Loaded += (s, e) =>
        {
            App.Logger?.Debug("RoadmapStepPopup loaded, starting fade-in animation");
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            BeginAnimation(OpacityProperty, fadeIn);
        };
    }

    /// <summary>
    /// Position the window in the bottom-right corner of the primary screen
    /// </summary>
    private void PositionWindow()
    {
        try
        {
            // Get the working area of the primary screen (excludes taskbar)
            var workArea = SystemParameters.WorkArea;

            // Position in bottom-right corner with 20px margin
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - Height - 20;

            App.Logger?.Debug("Positioned popup at Left={Left}, Top={Top}", Left, Top);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to position roadmap popup, using defaults");
            // Fallback: center on screen
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void LoadPhotoThumbnail(RoadmapStepProgress progress)
    {
        try
        {
            if (string.IsNullOrEmpty(progress.PhotoPath)) return;

            var fullPath = App.Roadmap?.GetFullPhotoPath(progress.PhotoPath);
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) return;

            App.Logger?.Debug("Loading step photo thumbnail: {Path}", fullPath);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.DecodePixelWidth = 100;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            PhotoBrush.ImageSource = bitmap;
            PhotoEllipse.Visibility = Visibility.Visible;
            CheckmarkIcon.Visibility = Visibility.Collapsed;

            App.Logger?.Debug("Photo thumbnail loaded successfully");
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Failed to load step photo thumbnail");
            // Keep showing checkmark icon
        }
    }

    private void FadeOutAndClose()
    {
        try
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) =>
            {
                try
                {
                    Close();
                }
                catch { /* Ignore close errors */ }
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Error during fade out, closing directly");
            try { Close(); } catch { }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _autoCloseTimer.Stop();
        FadeOutAndClose();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Allow dragging the window
        try
        {
            DragMove();
        }
        catch { /* Ignore drag errors */ }
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        base.OnClosed(e);
        App.Logger?.Debug("RoadmapStepPopup closed");
    }
}
