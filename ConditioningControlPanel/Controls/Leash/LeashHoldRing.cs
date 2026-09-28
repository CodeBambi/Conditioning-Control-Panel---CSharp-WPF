using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// The hold-to-cut countdown (2026-09-28): while the panic key is held on a leashed account, a
/// small topmost pill near the bottom of the primary screen counts 5..1 round a ring, so the hold
/// can be seen and not only heard (the tick is muted over a mandatory video). It never takes
/// focus or clicks, and it has no animation of its own: the number and the ring move once a
/// second, which reads the same with motion off. Gone on key-up, and when the question comes up.
/// </summary>
internal sealed class LeashHoldRing : Window
{
    private static LeashHoldRing? _open;
    private readonly Border _ringHost = new() { Width = 46, Height = 46 };
    private int _shown = -1;

    /// <summary>Show (or move) the ring for a hold that has run <paramref name="held"/>.</summary>
    public static void ShowHeld(TimeSpan held)
    {
        try
        {
            _open ??= Create();
            _open.Paint(held);
        }
        catch (Exception ex) { App.Logger?.Debug("Leash hold ring failed: {E}", ex.Message); }
    }

    public static void Dismiss()
    {
        var w = _open;
        _open = null;
        try { w?.Close(); } catch { }
    }

    internal static bool IsShown => _open != null;
    internal static int Number => _open?._shown ?? -1;

    private static LeashHoldRing Create()
    {
        var w = new LeashHoldRing();
        w.Show();
        return w;
    }

    private LeashHoldRing()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = Loc.Get("leash_title");

        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 16, 8) };
        row.Children.Add(_ringHost);
        var words = FriendsLook.Label(Loc.Get("leash_hold_ring"), 14, FriendsLook.TextBrush, FriendsLook.Display, FontWeights.SemiBold);
        words.VerticalAlignment = VerticalAlignment.Center;
        words.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(words);
        Content = new Border
        {
            Background = FriendsLook.GlassBrush,
            BorderBrush = FriendsLook.MintBrush,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(999),
            Child = row,
            Tag = "leash-hold-ring",
        };

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            var ex = GetWindowLong(h, GWL_EXSTYLE);
            SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
        };
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Left + (wa.Width - ActualWidth) / 2;
            Top = wa.Bottom - ActualHeight - 48;
        };
    }

    private void Paint(TimeSpan held)
    {
        var n = LeashHoldTick.SecondsLeft(held);
        if (n == _shown) return;
        _shown = n;
        _ringHost.Child = LeashLook.Ring(LeashHoldTick.Fraction(held), n.ToString(System.Globalization.CultureInfo.InvariantCulture), FriendsLook.Mint, 46);
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
