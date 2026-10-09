// PORTED from WPF 7.1.5 Controls/Leash/LeashCutConfirmWindow.cs: "Cut the leash?" after the panic key
// was held five seconds, and the holder's "Let go of X?". Topmost, centred on the screen, two
// buttons. Keys do nothing here on purpose: the finger that held the key is often still on it.
// The drop shadow is a sibling Border BoxShadow (no Effect, Avalonia effect rule).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

internal sealed class LeashCutConfirmWindow : Window
{
    private static LeashCutConfirmWindow? _open;

    /// <summary>Test seam: replaces showing the window (gets title, tag and the yes action).</summary>
    internal static Action<string, string, Action>? ShowOverride { get; set; }

    public static void Ask(string? holderName, Action onYes) => Show(
        Loc.Get("leash_cut_confirm_title"),
        string.IsNullOrWhiteSpace(holderName) ? Loc.Get("leash_cut_confirm_body_anon") : Loc.GetF("leash_cut_confirm_body", holderName!.Trim()),
        Loc.Get("leash_cut_confirm_yes"), Loc.Get("leash_cut_confirm_no"), "leash-cut-confirm", onYes);

    /// <summary>Any other leash yes/no (the holder letting go).</summary>
    public static void Confirm(string title, string body, string yes, string no, string tag, Action onYes)
        => Show(title, body, yes, no, tag, onYes);

    private static void Show(string title, string body, string yes, string no, string tag, Action onYes)
    {
        if (ShowOverride is { } o) { o(title, tag, onYes); return; }
        if (_open != null) { try { _open.Activate(); } catch { } return; }
        var w = new LeashCutConfirmWindow(title, body, yes, no, tag, onYes);
        _open = w;
        w.Closed += (_, _) => _open = null;
        w.Show();
        w.Activate();
    }

    private LeashCutConfirmWindow(string titleText, string bodyText, string yesText, string noText, string tag, Action onYes)
    {
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Title = titleText;
        KeyDown += (_, e) => e.Handled = true;

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 20), MaxWidth = 380 };
        var title = FriendsDrawer.Label(titleText, 22, FriendsDrawer.Text, FriendsDrawer.Display, FontWeight.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(title);
        var body = LeashLook.Wrap(FriendsDrawer.Label(bodyText, 13, FriendsDrawer.Muted));
        body.TextAlignment = TextAlignment.Center;
        body.Margin = new Thickness(0, 8, 0, 16);
        stack.Children.Add(body);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var no = FriendsDrawer.Pill(noText, FriendsDrawer.Raised, FriendsDrawer.Text, tag + ":no", FriendsDrawer.Line2);
        no.Focusable = false;
        no.CornerRadius = new CornerRadius(10);
        no.Padding = new Thickness(18, 7, 18, 7);
        no.Click += (_, _) => Close();
        var yes = FriendsDrawer.Pill(yesText, FriendsDrawer.Red, LeashLook.InkBrush, tag + ":yes", FriendsDrawer.Red);
        yes.Focusable = false;
        yes.CornerRadius = new CornerRadius(10);
        yes.Padding = new Thickness(18, 7, 18, 7);
        yes.Margin = new Thickness(10, 0, 0, 0);
        yes.Click += (_, _) =>
        {
            Close();
            try { onYes(); } catch (Exception ex) { Serilog.Log.Debug("Leash confirm action failed: {E}", ex.Message); }
        };
        row.Children.Add(no);
        row.Children.Add(yes);
        stack.Children.Add(row);

        Content = new Border
        {
            Background = LeashLook.GlassBrush,
            BorderBrush = FriendsDrawer.Red,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            BoxShadow = BoxShadows.Parse("0 0 28 0 #99000000"),
            Child = stack,
            Margin = new Thickness(24),
        };
    }
}
