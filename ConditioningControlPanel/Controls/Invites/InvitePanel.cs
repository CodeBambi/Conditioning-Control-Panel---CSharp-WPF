using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Invites;

namespace ConditioningControlPanel.Controls.Invites;

/// <summary>
/// The invites section at the foot of the Exclusives tab. Built in code, one of three faces:
/// <list type="bullet">
/// <item>a subscriber's codes, what became of each, and the reward ladder;</item>
/// <item>a redeem box for a signed-in account without premium;</item>
/// <item>nothing at all: signed out, offline, premium without codes, or a server that does not
/// have invites yet (so the section stays dark until CCP-Server ships the endpoints).</item>
/// </list>
/// </summary>
public sealed class InvitePanel : Border
{
    /// <summary>A tab show re-reads the server at most this often.</summary>
    public static readonly TimeSpan RefreshGap = TimeSpan.FromSeconds(30);

    private static readonly Brush CardBg = Frozen("#EE1A1A2E");
    private static readonly Brush RowBg = Frozen("#252542");
    private static readonly Brush Edge = Frozen("#3D3D60");
    private static readonly Brush Pink = Frozen("#FF69B4");
    private static readonly Brush Gold = Frozen("#E5C76B");
    private static readonly Brush Good = Frozen("#7FD8A6");
    private static readonly Brush Warn = Frozen("#FFB347");
    private static readonly Brush Text = Frozen("#E0E0E0");
    private static readonly Brush Muted = Frozen("#B8B1CC");
    private enum Face { None, Inviter, Redeem, Redeemed }

    private readonly Func<IInviteApi> _api;
    private readonly StackPanel _body = new();
    private DateTime _lastReadUtc = DateTime.MinValue;
    private Face _face = Face.None;
    private bool _reading;
    private DateTime _redeemedAtUtc = DateTime.MinValue;

    /// <summary>A confirmed redeem holds the card this long before a re-read may replace it.</summary>
    public static readonly TimeSpan RedeemedHold = TimeSpan.FromMinutes(2);

    public InvitePanel(Func<IInviteApi>? api = null)
    {
        _api = api ?? (() => new InviteApi());
        Visibility = Visibility.Collapsed;
        CornerRadius = new CornerRadius(14);
        Background = CardBg;
        BorderBrush = Edge;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(22, 18, 22, 20);
        Child = _body;
    }

    /// <summary>Re-read the server and repaint. Throttled unless forced; never throws.</summary>
    public async Task RefreshAsync(bool force = false)
    {
        // Signed out (or switched away) always wins, even over a confirmation on screen.
        if (BackRoomApi.AppIdentity() == null) { Hide(); return; }
        // A just-confirmed redeem owns the card for a moment: the grant it applies repaints the
        // tab, and a re-read then would collapse the confirmation (the account is now premium).
        if (_reading || (_face == Face.Redeemed && DateTime.UtcNow - _redeemedAtUtc < RedeemedHold)) return;
        if (!force && DateTime.UtcNow - _lastReadUtc < RefreshGap) return;

        _reading = true;
        try
        {
            var mine = await _api().MineAsync();
            _lastReadUtc = DateTime.UtcNow;
            if (_face == Face.Redeemed && DateTime.UtcNow - _redeemedAtUtc < RedeemedHold) return;
            InviteRewards.Apply(mine.ConvertedTotal);
            if (mine.Snapshot != null)
                ShowInviter(mine.Snapshot);
            else if (mine.Reachable && App.Patreon?.HasPremiumAccess != true)
            {
                // Rebuilding would wipe a half-typed code or the last refusal.
                if (_face != Face.Redeem) ShowRedeem();
            }
            else
                Hide();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[Invites] panel refresh failed: {E}", ex.GetType().Name);
            Hide();
        }
        finally { _reading = false; }
    }

    private void Hide()
    {
        _face = Face.None;
        Visibility = Visibility.Collapsed;
    }

    // ============================== inviter ==============================

    private void ShowInviter(InviteSnapshot snap)
    {
        _body.Children.Clear();
        var open = 0;
        foreach (var s in snap.Slots) if (s.State == InviteSlotState.Open) open++;

        var right = snap.ResetsAtUtc is DateTime r
            ? Loc.GetF("invites_resets", r.ToLocalTime().ToString("d MMM"))
            : null;
        _body.Children.Add(Header(Loc.Get("invites_inviter_title"), right));
        _body.Children.Add(Para(Loc.Get("invites_inviter_body")));
        _body.Children.Add(Para(Loc.GetF("invites_left", open, snap.Slots.Count), Text, 13, new Thickness(0, 6, 0, 10)));

        foreach (var slot in snap.Slots)
            _body.Children.Add(SlotRow(slot));

        _body.Children.Add(Ladder(snap.ConvertedTotal));
        _face = Face.Inviter;
        Visibility = Visibility.Visible;
    }

    private FrameworkElement SlotRow(InviteSlot slot)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var code = new TextBlock
        {
            Text = slot.Code,
            Foreground = slot.State == InviteSlotState.Open ? Pink : Muted,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        code.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Mono");
        grid.Children.Add(code);

        var (label, brush) = slot.State switch
        {
            InviteSlotState.Converted => (Loc.GetF("invites_slot_converted", slot.InviteeName ?? "?"), Good),
            InviteSlotState.Trying => (Loc.GetF("invites_slot_trying", slot.InviteeName ?? "?", slot.Day ?? 1), Warn),
            _ => (Loc.Get("invites_slot_open"), Muted),
        };
        var state = new TextBlock
        {
            Text = label,
            Foreground = brush,
            FontSize = 12,
            Margin = new Thickness(14, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(state, 1);
        grid.Children.Add(state);

        if (slot.State == InviteSlotState.Open)
        {
            var copy = SmallButton(Loc.Get("invites_copy"));
            copy.Click += (_, _) => CopyCode(slot.Code, copy);
            Grid.SetColumn(copy, 2);
            grid.Children.Add(copy);
        }

        return new Border
        {
            Background = RowBg,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 9, 10, 9),
            Margin = new Thickness(0, 0, 0, 6),
            BorderBrush = slot.State == InviteSlotState.Open ? Frozen("#80FF69B4") : Edge,
            BorderThickness = new Thickness(1),
            Child = grid,
        };
    }

    private static void CopyCode(string code, Button button)
    {
        try
        {
            Clipboard.SetText(code);
            button.Content = Loc.Get("invites_copied");
        }
        catch (Exception ex)
        {
            // The clipboard can be held by another process; say nothing rather than crash.
            Diag.Swallowed(ex, "clipboard busy");
        }
    }

    private FrameworkElement Ladder(int converted)
    {
        var host = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        host.Children.Add(Para(Loc.Get("invites_ladder_title"), Gold, 12, new Thickness(0, 0, 0, 8), bold: true));

        var rungs = new WrapPanel();
        foreach (var rung in InviteRewards.Ladder)
        {
            var reached = converted >= rung.Converted;
            var name = Loc.Get($"achievement_{rung.AchievementId}_name");
            rungs.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(99),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Background = reached ? Frozen("#33E5C76B") : RowBg,
                BorderBrush = reached ? Gold : Edge,
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = (reached ? "✓ " : "") + $"{rung.Converted} · {name}",
                    Foreground = reached ? Gold : Muted,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                },
            });
        }
        host.Children.Add(rungs);

        var next = InviteRewards.Next(converted);
        host.Children.Add(Para(next == null
            ? Loc.Get("invites_ladder_done")
            : Loc.GetF("invites_ladder_next", next.Converted - converted, Loc.Get($"achievement_{next.AchievementId}_name")),
            Muted, 12, new Thickness(0, 2, 0, 0)));
        return host;
    }

    // ============================== redeem ==============================

    private void ShowRedeem()
    {
        _body.Children.Clear();
        _body.Children.Add(Header(Loc.Get("invites_redeem_title"), null));
        _body.Children.Add(Para(Loc.Get("invites_redeem_body")));
        var box = new InviteRedeemBox("redeem", _api) { Margin = new Thickness(0, 10, 0, 0) };
        box.Redeemed += () => { _face = Face.Redeemed; _redeemedAtUtc = DateTime.UtcNow; };
        _body.Children.Add(box);
        _face = Face.Redeem;
        Visibility = Visibility.Visible;
    }

    /// <summary>The loc key that words a server refusal. Unknown words fall back to a generic line.</summary>
    internal static string ReasonKey(string? reason) => reason switch
    {
        "unknown_code" or "used" or "own_code" or "already_had_week" or "already_subscribed"
            or "too_fast" or "offline" or "bad_code" => "invites_err_" + reason,
        _ => "invites_err_generic",
    };

    // ============================== bits ==============================

    private static FrameworkElement Header(string title, string? right)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = title,
            FontFamily = ConditioningControlPanel.Helpers.FontPickerHelper.FredokaFamily,
            FontWeight = FontWeights.SemiBold,
            FontSize = 18,
            Foreground = Brushes.White,
        });
        if (right != null)
        {
            var r = new TextBlock { Text = right, Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(r, 1);
            grid.Children.Add(r);
        }
        return grid;
    }

    private static TextBlock Para(string text, Brush? brush = null, double size = 13, Thickness? margin = null, bool bold = false)
        => new()
        {
            Text = text,
            Foreground = brush ?? Muted,
            FontSize = size,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = margin ?? new Thickness(0),
        };

    /// <summary>The theme's pink pill (Resources/Theme/MainWindow.xaml), so hover, pressed and
    /// disabled stay on-theme instead of falling back to the default light-blue chrome.</summary>
    private static Button SmallButton(string text)
    {
        var b = new Button
        {
            Content = text,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.SetResourceReference(StyleProperty, "SmallPinkButton");
        return b;
    }

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
