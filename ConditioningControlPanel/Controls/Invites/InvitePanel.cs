using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
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

    /// <summary>Raised on the UI thread after each server read, so the header ticket follows the card.</summary>
    public event Action<InviteMine>? Read;

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
            try { Read?.Invoke(mine); }
            catch (Exception ex) { App.Logger?.Debug("[Invites] read observer failed: {E}", ex.GetType().Name); }
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

        var next = InviteRewards.Next(converted);
        var rungs = new UniformGrid { Columns = InviteRewards.Ladder.Count, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var rung in InviteRewards.Ladder)
            rungs.Children.Add(RungTile(rung, converted, ReferenceEquals(rung, next)));
        host.Children.Add(rungs);

        host.Children.Add(Para(next == null
            ? Loc.Get("invites_ladder_done")
            : Loc.GetF("invites_ladder_next", next.Converted - converted, Loc.Get($"achievement_{next.AchievementId}_name")),
            Muted, 12, new Thickness(0, 2, 0, 0)));
        return host;
    }

    /// <summary>
    /// One rung as a picture tile: the badge, its name, how many friends it takes and the wardrobe
    /// piece it unlocks. Earned rungs are in full colour on a gold edge; the rest are grey under a
    /// lock, and the next one to reach carries a thin progress bar.
    /// </summary>
    internal static FrameworkElement RungTile(InviteRung rung, int converted, bool isNext)
    {
        var reached = converted >= rung.Converted;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };

        // Badge, rounded like the achievement cards, grey and locked until earned.
        var badge = new Grid { Width = 60, Height = 60, HorizontalAlignment = HorizontalAlignment.Center };
        var art = BadgeImage(rung.AchievementId, grey: !reached);
        if (art != null)
        {
            var img = new Image
            {
                Source = art,
                Width = 60,
                Height = 60,
                Stretch = Stretch.UniformToFill,
                Opacity = reached ? 1 : 0.45,
                Clip = new RectangleGeometry(new Rect(0, 0, 60, 60), 10, 10),
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            badge.Children.Add(img);
        }
        if (!reached)
        {
            badge.Children.Add(new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                Background = Frozen("#CC12121F"),
                BorderBrush = Edge,
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -4, -4),
                Child = new TextBlock
                {
                    Text = "", // Segoe MDL2 lock
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 10,
                    Foreground = Muted,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            });
        }
        stack.Children.Add(badge);

        stack.Children.Add(new TextBlock
        {
            Text = Loc.Get($"achievement_{rung.AchievementId}_name"),
            Foreground = reached ? Brushes.White : Text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 15,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            MaxHeight = 30,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 7, 0, 0),
        });
        stack.Children.Add(new TextBlock
        {
            Text = rung.Converted == 1 ? Loc.Get("invites_ladder_friends_one") : Loc.GetF("invites_ladder_friends", rung.Converted),
            Foreground = reached ? Gold : Muted,
            FontSize = 11,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        });

        // The wardrobe piece it pays out: a small picture and its registry name (a proper noun).
        var item = WardrobeCatalog.Find(rung.WardrobeItemId);
        if (item != null)
        {
            var unlock = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
                ToolTip = Loc.GetF("invites_ladder_unlocks", item.Name),
                Opacity = reached ? 1 : 0.6,
            };
            var piece = TrimToContent(WardrobeArt.GetImage(item.Id));
            if (piece != null)
            {
                var pimg = new Image { Source = piece, Width = 22, Height = 22, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(pimg, BitmapScalingMode.HighQuality);
                unlock.Children.Add(pimg);
            }
            unlock.Children.Add(new TextBlock
            {
                Text = item.Name,
                Foreground = Muted,
                FontSize = 10.5,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 100,
            });
            stack.Children.Add(unlock);
        }

        if (isNext && !reached)
        {
            var have = Math.Max(0, Math.Min(converted, rung.Converted));
            var track = new Grid { Height = 4, Margin = new Thickness(6, 8, 6, 0) };
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(have, 0.0001), GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(rung.Converted - have, 0.0001), GridUnitType.Star) });
            var trackBg = new Border { CornerRadius = new CornerRadius(2), Background = Edge };
            Grid.SetColumnSpan(trackBg, 2);
            track.Children.Add(trackBg);
            if (have > 0) track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Pink });
            stack.Children.Add(track);
            stack.Children.Add(new TextBlock
            {
                Text = Loc.GetF("invites_ladder_progress", have, rung.Converted),
                Foreground = Pink,
                FontSize = 10.5,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0),
            });
        }

        var tile = new Border
        {
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 10, 8, 10),
            Margin = new Thickness(0, 0, 8, 6),
            Background = reached ? Frozen("#26E5C76B") : RowBg,
            BorderBrush = reached ? Gold : isNext ? Frozen("#80FF69B4") : Edge,
            BorderThickness = new Thickness(reached ? 1.5 : 1),
            Child = stack,
            Cursor = Cursors.Hand,
            ToolTip = Loc.Get($"achievement_{rung.AchievementId}_req"),
        };
        tile.MouseLeftButtonUp += (_, _) =>
        {
            try { App.MainWindowRef?.ShowTab("achievements"); }
            catch (Exception ex) { Diag.Swallowed(ex, "open achievements from invite ladder"); }
        };
        return tile;
    }

    /// <summary>
    /// Decorations are authored on the full avatar canvas (a crown sits in the top third), so at
    /// icon size the piece itself would be a speck. Crop to the opaque pixels for the preview.
    /// </summary>
    private static ImageSource? TrimToContent(ImageSource? source)
    {
        if (source is not BitmapSource bmp) return source;
        try
        {
            var src = bmp.Format == PixelFormats.Bgra32 || bmp.Format == PixelFormats.Pbgra32
                ? bmp
                : new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            src.CopyPixels(px, stride, 0);
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    if (px[y * stride + x * 4 + 3] > 24)
                    {
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
            if (x1 < x0 || y1 < y0) return source;
            var crop = new CroppedBitmap(src, new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
            crop.Freeze();
            return crop;
        }
        catch (Exception ex)
        {
            Diag.Swallowed(ex, "invite wardrobe preview crop");
            return source;
        }
    }

    private static ImageSource? BadgeImage(string achievementId, bool grey)
    {
        try
        {
            var file = Models.Achievement.All.TryGetValue(achievementId, out var a) && !string.IsNullOrEmpty(a.ImageName) ? a.ImageName : achievementId + ".png";
            var src = Services.ModResourceResolver.ResolveImageDecoded($"achievements/{file}", 128) as BitmapSource;
            if (src == null || !grey) return src;
            var g = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);
            g.Freeze();
            return g;
        }
        catch (Exception ex)
        {
            Diag.Swallowed(ex, "invite badge art");
            return null;
        }
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
            or "too_fast" or "offline" or "bad_code" or "signin" => "invites_err_" + reason,
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
