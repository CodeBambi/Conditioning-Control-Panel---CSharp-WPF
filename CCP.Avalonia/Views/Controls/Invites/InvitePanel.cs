using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Invites;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Invites;

/// <summary>
/// PORTED from ConditioningControlPanel/Controls/Invites/InvitePanel.cs + InviteRedeemBox.cs: the
/// invites section at the foot of the Exclusives tab, one of three faces - a subscriber's codes and
/// the reward ladder, a redeem box for a signed-in account without premium, or nothing (signed out,
/// offline, premium without codes, a server without invites).
/// </summary>
public sealed class InvitePanel : Border
{
    public static readonly TimeSpan RefreshGap = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan RedeemedHold = TimeSpan.FromMinutes(2);

    private static readonly IBrush CardBg = B("#EE1A1A2E"), RowBg = B("#252542"), Edge = B("#3D3D60"), Pink = B("#FF69B4"),
        Gold = B("#E5C76B"), Good = B("#7FD8A6"), Warn = B("#FFB347"), Text = B("#E0E0E0"), Muted = B("#B8B1CC");
    private enum Face { None, Inviter, Redeem, Redeemed }

    /// <summary>The wire (tests swap it; the XAML-built card uses the seeded Core default).</summary>
    internal Func<IInviteApi> Api { get; set; }
    private readonly Func<bool> _signedIn, _premium;
    private readonly StackPanel _body = new();
    private DateTime _lastReadUtc = DateTime.MinValue, _redeemedAtUtc = DateTime.MinValue;
    private Face _face = Face.None;
    private bool _reading, _busy;
    private TextBox? _box;
    private Button? _go;
    private TextBlock? _result;

    /// <summary>Raised after each server read, so the header ticket follows the card.</summary>
    public event Action<InviteMine>? Read;

    public InvitePanel() : this(null) { }

    internal InvitePanel(Func<IInviteApi>? api, Func<bool>? signedIn = null, Func<bool>? premium = null)
    {
        Api = api ?? (() => new InviteApi());
        _signedIn = signedIn ?? (() => InviteApi.DefaultIdentity() != null);
        _premium = premium ?? (() => CoreAccount.HasPremiumAccess);
        IsVisible = false;
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
        if (!_signedIn()) { Hide(); return; }
        if (_reading || (_face == Face.Redeemed && DateTime.UtcNow - _redeemedAtUtc < RedeemedHold)) return;
        if (!force && DateTime.UtcNow - _lastReadUtc < RefreshGap) return;

        _reading = true;
        try
        {
            var mine = await Api().MineAsync();
            _lastReadUtc = DateTime.UtcNow;
            try { Read?.Invoke(mine); }
            catch (Exception ex) { Log.Debug("[Invites] read observer failed: {E}", ex.GetType().Name); }
            if (_face == Face.Redeemed && DateTime.UtcNow - _redeemedAtUtc < RedeemedHold) return;
            InviteRewards.Apply(mine.ConvertedTotal);
            if (mine.Snapshot != null) ShowInviter(mine.Snapshot);
            else if (mine.Reachable && !_premium()) { if (_face != Face.Redeem) ShowRedeem(); }
            else Hide();
        }
        catch (Exception ex)
        {
            Log.Debug("[Invites] panel refresh failed: {E}", ex.GetType().Name);
            Hide();
        }
        finally { _reading = false; }
    }

    private void Hide()
    {
        _face = Face.None;
        IsVisible = false;
    }

    // ============================== inviter ==============================

    private void ShowInviter(InviteSnapshot snap)
    {
        _body.Children.Clear();
        var open = 0;
        foreach (var s in snap.Slots) if (s.State == InviteSlotState.Open) open++;
        var right = snap.ResetsAtUtc is DateTime r ? Loc.GetF("invites_resets", r.ToLocalTime().ToString("d MMM")) : null;
        _body.Children.Add(Header(Loc.Get("invites_inviter_title"), right));
        _body.Children.Add(Para(Loc.Get("invites_inviter_body")));
        _body.Children.Add(Para(Loc.GetF("invites_left", open, snap.Slots.Count), Text, 13, new Thickness(0, 6, 0, 10)));
        foreach (var slot in snap.Slots) _body.Children.Add(SlotRow(slot));
        _body.Children.Add(Ladder(snap.ConvertedTotal));
        _face = Face.Inviter;
        IsVisible = true;
    }

    private Control SlotRow(InviteSlot slot)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var isOpen = slot.State == InviteSlotState.Open;
        grid.Children.Add(new TextBlock
        {
            Text = slot.Code, Foreground = isOpen ? Pink : Muted, FontSize = 15, FontWeight = FontWeight.SemiBold,
            FontFamily = FriendsDrawer.Mono, VerticalAlignment = VerticalAlignment.Center,
        });
        var (label, brush) = slot.State switch
        {
            InviteSlotState.Converted => (Loc.GetF("invites_slot_converted", slot.InviteeName ?? "?"), Good),
            InviteSlotState.Trying => (Loc.GetF("invites_slot_trying", slot.InviteeName ?? "?", slot.Day ?? 1), Warn),
            _ => (Loc.Get("invites_slot_open"), Muted),
        };
        var state = new TextBlock
        {
            Text = label, Foreground = brush, FontSize = 12, Margin = new Thickness(14, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(state, 1);
        grid.Children.Add(state);
        if (isOpen)
        {
            var copy = SmallButton(Loc.Get("invites_copy"));
            copy.Click += async (_, _) =>
            {
                // The clipboard can be held by another process; say nothing rather than crash.
                try { await (TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(slot.Code) ?? Task.CompletedTask); }
                catch (Exception ex) { Diag.Swallowed(ex, "clipboard busy"); return; }
                ((TextBlock)copy.Content!).Text = Loc.Get("invites_copied");
            };
            Grid.SetColumn(copy, 2);
            grid.Children.Add(copy);
        }
        return new Border
        {
            Background = RowBg, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 9, 10, 9),
            Margin = new Thickness(0, 0, 0, 6), BorderBrush = isOpen ? B("#80FF69B4") : Edge, BorderThickness = new Thickness(1), Child = grid,
        };
    }

    private Control Ladder(int converted)
    {
        var host = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        host.Children.Add(Para(Loc.Get("invites_ladder_title"), Gold, 12, new Thickness(0, 0, 0, 8), bold: true));
        var next = InviteRewards.Next(converted);
        var rungs = new UniformGrid { Columns = InviteRewards.Ladder.Count, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var rung in InviteRewards.Ladder) rungs.Children.Add(RungTile(rung, converted, ReferenceEquals(rung, next)));
        host.Children.Add(rungs);
        host.Children.Add(Para(next == null
            ? Loc.Get("invites_ladder_done")
            : Loc.GetF("invites_ladder_next", next.Converted - converted, Loc.Get($"achievement_{next.AchievementId}_name")),
            Muted, 12, new Thickness(0, 2, 0, 0)));
        return host;
    }

    /// <summary>One rung as a picture tile: badge, name, friends it takes, the wardrobe piece it pays out,
    /// and a progress bar on the next one. A click (or Enter/Space) opens the Achievements tab.</summary>
    internal static Control RungTile(InviteRung rung, int converted, bool isNext)
    {
        var reached = converted >= rung.Converted;
        var stack = new StackPanel();
        var badge = new Grid { Width = 60, Height = 60, HorizontalAlignment = HorizontalAlignment.Center };
        var file = Achievement.All.TryGetValue(rung.AchievementId, out var a) && !string.IsNullOrEmpty(a.ImageName) ? a.ImageName : rung.AchievementId + ".png";
        if (Helpers.ModArt.TryLoad($"achievements/{file}", 128) is { } art)
            badge.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(10), ClipToBounds = true, Opacity = reached ? 1 : 0.45,
                Child = new Image { Source = art, Width = 60, Height = 60, Stretch = Stretch.UniformToFill },
            });
        if (!reached)
            badge.Children.Add(new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = B("#CC12121F"), BorderBrush = Edge,
                BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -4, -4),
                Child = new TextBlock { Text = "🔒", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            });
        stack.Children.Add(badge);
        stack.Children.Add(new TextBlock
        {
            Text = Loc.Get($"achievement_{rung.AchievementId}_name"), Foreground = reached ? Brushes.White : Text, FontSize = 12,
            FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, LineHeight = 15,
            MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 7, 0, 0),
        });
        stack.Children.Add(new TextBlock
        {
            Text = rung.Converted == 1 ? Loc.Get("invites_ladder_friends_one") : Loc.GetF("invites_ladder_friends", rung.Converted),
            Foreground = reached ? Gold : Muted, FontSize = 11, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 1, 0, 0),
        });
        if (WardrobeCatalog.Find(rung.WardrobeItemId) is { } item)
        {
            var unlock = new StackPanel
            {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0), Opacity = reached ? 1 : 0.6,
            };
            ToolTip.SetTip(unlock, Loc.GetF("invites_ladder_unlocks", item.Name));
            if (TrimToContent(Helpers.ModArt.Wardrobe(item.Id)) is { } piece)
                unlock.Children.Add(new Image { Source = piece, Width = 22, Height = 22, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            unlock.Children.Add(new TextBlock
            {
                Text = item.Name, Foreground = Muted, FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 100,
            });
            stack.Children.Add(unlock);
        }
        if (isNext && !reached)
        {
            var have = Math.Max(0, Math.Min(converted, rung.Converted));
            var track = new Grid { Height = 4, Margin = new Thickness(6, 8, 6, 0) };
            track.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(have, 0.0001), GridUnitType.Star));
            track.ColumnDefinitions.Add(new ColumnDefinition(Math.Max(rung.Converted - have, 0.0001), GridUnitType.Star));
            var trackBg = new Border { CornerRadius = new CornerRadius(2), Background = Edge };
            Grid.SetColumnSpan(trackBg, 2);
            track.Children.Add(trackBg);
            if (have > 0) track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Pink });
            stack.Children.Add(track);
            stack.Children.Add(new TextBlock
            {
                Text = Loc.GetF("invites_ladder_progress", have, rung.Converted), Foreground = Pink, FontSize = 10.5,
                TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 3, 0, 0),
            });
        }
        var tile = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 10, 8, 10), Margin = new Thickness(0, 0, 8, 6),
            Background = reached ? B("#26E5C76B") : RowBg, BorderBrush = reached ? Gold : isNext ? B("#80FF69B4") : Edge,
            BorderThickness = new Thickness(reached ? 1.5 : 1), Child = stack, Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = true, Tag = "invite-rung",
        };
        ToolTip.SetTip(tile, Loc.Get($"achievement_{rung.AchievementId}_req"));
        tile.PointerReleased += (_, _) => OpenAchievements(tile);
        tile.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) OpenAchievements(tile); };
        return tile;
    }

    /// <summary>WPF ShowTab("achievements"). Refused under Lockdown: no veil on this head yet.</summary>
    private static void OpenAchievements(Control from)
    {
        if (Windows.MainShellWindow.LockdownActive) return;
        try { (TopLevel.GetTopLevel(from) as Windows.MainShellWindow)?.ShowTab("achievements"); }
        catch (Exception ex) { Diag.Swallowed(ex, "open achievements from invite ladder"); }
    }

    /// <summary>Decorations are authored on the full avatar canvas, so at icon size the piece would be a
    /// speck: crop to the opaque pixels (WPF TrimToContent).</summary>
    private static IImage? TrimToContent(Bitmap? bmp)
    {
        if (bmp == null) return null;
        try
        {
            int w = bmp.PixelSize.Width, h = bmp.PixelSize.Height, stride = w * 4;
            var px = new byte[stride * h];
            var pin = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { bmp.CopyPixels(new PixelRect(0, 0, w, h), pin.AddrOfPinnedObject(), px.Length, stride); }
            finally { pin.Free(); }
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    if (px[y * stride + x * 4 + 3] > 24)
                    { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
            return x1 < x0 ? bmp : new CroppedBitmap(bmp, new PixelRect(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
        }
        catch (Exception ex) { Diag.Swallowed(ex, "invite wardrobe preview crop"); return bmp; }
    }

    // ============================== redeem ==============================

    private void ShowRedeem()
    {
        _body.Children.Clear();
        _body.Children.Add(Header(Loc.Get("invites_redeem_title"), null));
        _body.Children.Add(Para(Loc.Get("invites_redeem_body")));
        var row = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star) { MaxWidth = 320 });
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        _box = new TextBox
        {
            Background = RowBg, Foreground = Text, BorderBrush = Edge, BorderThickness = new Thickness(1), Padding = new Thickness(10, 7, 10, 7),
            FontSize = 14, MaxLength = 80, FontFamily = FriendsDrawer.Mono, CaretBrush = Pink, Tag = "invite-code",
        };
        _box.TextChanged += (_, _) => { if (_box.Text is { } t && t != t.ToUpperInvariant()) { var c = _box.CaretIndex; _box.Text = t.ToUpperInvariant(); _box.CaretIndex = c; } };
        _box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Redeeming = RedeemAsync(); };
        row.Children.Add(_box);
        _go = SmallButton(Loc.Get("invites_redeem_go"));
        _go.Margin = new Thickness(8, 0, 0, 0);
        _go.Tag = "invite-redeem";
        _go.Click += (_, _) => Redeeming = RedeemAsync();
        Grid.SetColumn(_go, 1);
        row.Children.Add(_go);
        _body.Children.Add(row);
        _result = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), IsVisible = false, Tag = "invite-result" };
        _body.Children.Add(_result);
        _face = Face.Redeem;
        IsVisible = true;
    }

    /// <summary>The last redeem the button or Enter started (tests await it).</summary>
    internal Task? Redeeming { get; private set; }

    private async Task RedeemAsync()
    {
        if (_busy || _box == null || _go == null) return;
        if (InviteRules.NormalizeCode(_box.Text) == null) { Say(Loc.Get("invites_err_bad_code"), Warn); return; }
        _busy = true;
        _go.IsEnabled = false;
        try
        {
            var outcome = await Api().RedeemAsync(_box.Text!);
            if (!outcome.Ok) { Say(Loc.Get(ReasonKey(outcome.Reason)), Warn); return; }
            ApplyGrant(outcome.GrantUntilUtc);
            ((Control)_box.Parent!).IsVisible = false;
            var s = CoreSettings.Current;
            if (s.HasInviteGrant && s.InviteGrantUntil is DateTime until)
                Say(Loc.GetF("invites_redeem_ok", until.ToLocalTime().ToString("d MMM, HH:mm")), Good);
            else
            {
                Log.Warning("[Invites] code accepted but no usable end date yet (sent: {Sent})", outcome.GrantUntilUtc.HasValue);
                Say(Loc.Get("invites_redeem_ok_pending"), Good);
            }
            _face = Face.Redeemed;
            _redeemedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Log.Debug("[Invites] redeem failed: {E}", ex.GetType().Name);
            Say(Loc.Get("invites_err_offline"), Warn);
        }
        finally
        {
            _busy = false;
            _go.IsEnabled = true;
        }
    }

    /// <summary>WPF InviteGrantSync.ApplyNow: record the week on a signed-in account (id + token), save, and tell the gates.
    /// The end needs no timer here: HasInviteGrant reads the clock.</summary>
    private bool ApplyGrant(DateTime? grantUntilUtc)
    {
        var s = CoreSettings.Current;
        if (!_signedIn() || !InviteRules.ApplyGrant(s, grantUntilUtc, DateTime.UtcNow)) return false;
        Log.Information("[Invites] redeem: invite week opens premium until {Until:u}", s.InviteGrantUntil);
        CoreSettings.Save();
        Platform.AccountSeed.Patreon?.NotifyEntitlementRaised(PatreonTier.Level1);
        ArmExpiry();
        return true;
    }

    private static IDisposable? _expiry;

    internal static bool ExpiryArmed => _expiry != null;

    /// <summary>Shell close (and tests): no end-of-week one-shot outlives the window.</summary>
    internal static void CancelExpiry()
    {
        _expiry?.Dispose();
        _expiry = null;
    }

    /// <summary>WPF InviteGrantSync.ArmExpiry: HasPremiumAccess turns false silently at the end of a week,
    /// so a one-shot tells the gates to repaint then. At startup, sign-in and after every grant.</summary>
    internal static void ArmExpiry()
    {
        CancelExpiry();
        if (CoreSettings.Current.InviteGrantUntil is not DateTime end) return;
        var left = end - DateTime.UtcNow;
        if (left <= TimeSpan.Zero || left > InviteRules.MaxGrantAhead) return;
        _expiry = global::Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            _expiry = null;
            Log.Information("[Invites] invite week ended");
            if (Platform.AccountSeed.Patreon is { } p) p.NotifyEntitlementRaised(p.CurrentTier);
        }, left + TimeSpan.FromSeconds(2));
    }

    private void Say(string text, IBrush brush)
    {
        if (_result == null) return;
        _result.Text = text;
        _result.Foreground = brush;
        _result.IsVisible = true;
    }

    /// <summary>The loc key that words a server refusal. Unknown words fall back to a generic line.</summary>
    internal static string ReasonKey(string? reason) => reason switch
    {
        "unknown_code" or "used" or "own_code" or "already_had_week" or "already_subscribed"
            or "too_fast" or "offline" or "bad_code" or "signin" => "invites_err_" + reason,
        _ => "invites_err_generic",
    };

    // ============================== bits ==============================

    private static Control Header(string title, string? right)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6), ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = title, FontFamily = FriendsDrawer.Display, FontWeight = FontWeight.SemiBold, FontSize = 18, Foreground = Brushes.White });
        if (right != null)
        {
            var r = new TextBlock { Text = right, Foreground = Muted, FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(r, 1);
            grid.Children.Add(r);
        }
        return grid;
    }

    private static TextBlock Para(string text, IBrush? brush = null, double size = 13, Thickness? margin = null, bool bold = false) => new()
    {
        Text = text, Foreground = brush ?? Muted, FontSize = size, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
        TextWrapping = TextWrapping.Wrap, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left, Margin = margin ?? new Thickness(0),
    };

    /// <summary>The theme's pink pill; the text sits in a TextBlock (Avalonia reads "_" in Content as an access key).</summary>
    private static Button SmallButton(string text)
    {
        var b = new Button { Content = new TextBlock { Text = text }, VerticalAlignment = VerticalAlignment.Center };
        if (Application.Current?.TryFindResource("SmallPinkButton", out var t) == true && t is ControlTheme theme) b.Theme = theme;
        return b;
    }

    private static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
}
