using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ConditioningControlPanel;

/// <summary>
/// Popup window shown when a quest is completed
/// </summary>
public partial class QuestCompletePopup : Window
{
    private readonly DispatcherTimer _autoCloseTimer;

    public QuestCompletePopup(string questName, int xpAwarded)
    {
        InitializeComponent();

        TxtQuestName.Text = questName;
        TxtXPAwarded.Text = $"+{xpAwarded} XP";

        PositionWindow();

        // Auto-close after 5 seconds
        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoCloseTimer.Tick += (s, e) =>
        {
            _autoCloseTimer.Stop();
            FadeOutAndClose();
        };
        _autoCloseTimer.Start();

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
            App.Logger?.Error(ex, "Failed to position quest complete popup");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
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
        _autoCloseTimer.Stop();
        FadeOutAndClose();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        try { DragMove(); }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _autoCloseTimer.Stop();
        base.OnClosed(e);
    }
}
