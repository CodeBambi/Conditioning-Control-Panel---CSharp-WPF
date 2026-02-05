using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel;

/// <summary>
/// Popup window shown when Pink Rush activates, with a live countdown timer
/// </summary>
public partial class PinkRushPopup : Window
{
    private readonly DispatcherTimer _countdownTimer;

    public PinkRushPopup()
    {
        InitializeComponent();

        PositionWindow();

        // Countdown timer ticks every second
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += CountdownTimer_Tick;
        _countdownTimer.Start();

        // Update immediately
        UpdateCountdown();

        // Fade in
        Opacity = 0;
        Loaded += (s, e) =>
        {
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
            BeginAnimation(OpacityProperty, fadeIn);
        };
    }

    private void PositionWindow()
    {
        try
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 20;
            Top = workArea.Bottom - Height - 20;
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to position Pink Rush popup");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void CountdownTimer_Tick(object? sender, EventArgs e)
    {
        UpdateCountdown();
    }

    private void UpdateCountdown()
    {
        var endTime = App.Settings?.Current?.PinkRushEndTime;
        if (endTime == null)
        {
            _countdownTimer.Stop();
            FadeOutAndClose();
            return;
        }

        var remaining = endTime.Value - DateTime.Now;
        if (remaining.TotalSeconds <= 0)
        {
            _countdownTimer.Stop();
            FadeOutAndClose();
            return;
        }

        TxtCountdown.Text = $"{(int)remaining.TotalSeconds}s remaining";
    }

    private void FadeOutAndClose()
    {
        try
        {
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s, e) =>
            {
                try { Close(); }
                catch { }
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
        catch
        {
            try { Close(); } catch { }
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        _countdownTimer.Stop();
        FadeOutAndClose();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _countdownTimer.Stop();
        base.OnClosed(e);
    }
}
