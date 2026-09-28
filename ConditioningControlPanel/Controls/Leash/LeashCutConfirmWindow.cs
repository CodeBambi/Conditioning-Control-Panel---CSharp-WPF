using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// "Cut the leash?" after the panic key was held five seconds. Topmost, centred on the primary
/// screen, two buttons. Keys do nothing here on purpose: the finger that held the key is often
/// still on it, and its repeats must not answer the question.
/// </summary>
internal sealed class LeashCutConfirmWindow : Window
{
    private static LeashCutConfirmWindow? _open;

    public static void Ask(string? holderName, Action onYes)
    {
        if (_open != null) { try { _open.Activate(); } catch { } return; }
        var w = new LeashCutConfirmWindow(holderName, onYes);
        _open = w;
        w.Closed += (_, _) => _open = null;
        w.Show();
        w.Activate();
    }

    private LeashCutConfirmWindow(string? holderName, Action onYes)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        PreviewKeyDown += (_, e) => e.Handled = true;

        var stack = new StackPanel { Margin = new Thickness(26, 22, 26, 20), MaxWidth = 380 };
        var title = FriendsLook.Label(Loc.Get("leash_cut_confirm_title"), 22, FriendsLook.TextBrush, FriendsLook.Display, FontWeights.SemiBold);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(title);
        var body = FriendsLook.Label(
            string.IsNullOrWhiteSpace(holderName) ? Loc.Get("leash_cut_confirm_body_anon") : Loc.GetF("leash_cut_confirm_body", holderName!.Trim()),
            13, FriendsLook.MutedBrush);
        body.TextWrapping = TextWrapping.Wrap;
        body.TextTrimming = TextTrimming.None;
        body.TextAlignment = TextAlignment.Center;
        body.Margin = new Thickness(0, 8, 0, 16);
        stack.Children.Add(body);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var no = FriendsLook.Pill(Loc.Get("leash_cut_confirm_no"), FriendsLook.ButtonBrush, FriendsLook.TextBrush,
            FriendsLook.Line2Brush, 10, new Thickness(18, 7, 18, 7), FriendsLook.ButtonHoverBrush);
        no.Focusable = false;
        no.Tag = "leash-cut-confirm:no";
        no.Click += (_, _) => Close();
        var yes = FriendsLook.Pill(Loc.Get("leash_cut_confirm_yes"), FriendsLook.RedBrush, LeashLook.InkBrush,
            FriendsLook.RedBrush, 10, new Thickness(18, 7, 18, 7), FriendsLook.PinkBrush);
        yes.Focusable = false;
        yes.Margin = new Thickness(10, 0, 0, 0);
        yes.Tag = "leash-cut-confirm:yes";
        yes.Click += (_, _) =>
        {
            Close();
            try { onYes(); } catch (Exception ex) { App.Logger?.Debug("Leash cut from the hold failed: {E}", ex.Message); }
        };
        row.Children.Add(no);
        row.Children.Add(yes);
        stack.Children.Add(row);

        Content = new Border
        {
            Background = FriendsLook.GlassBrush,
            BorderBrush = FriendsLook.RedBrush,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(16),
            Child = stack,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 0, Opacity = 0.6, Color = Colors.Black },
            Margin = new Thickness(24),
        };
    }
}
