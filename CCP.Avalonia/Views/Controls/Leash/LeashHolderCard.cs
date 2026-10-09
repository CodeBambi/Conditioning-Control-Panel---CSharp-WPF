// PORTED from WPF 7.1.5 Controls/Leash/LeashHolderCard.cs, the header part: one card per account
// this side holds (avatar with presence, name, day, online / offline / quiet, their quiet-until
// line) and the "..." menu (Let go asks first through LeashCutConfirmWindow: it ends the leash for
// both and sets a 24 h cooldown).
// ponytail: today's report rows, the 7-day strip, receipts timeline, the assign / punish / reward /
// tug sheets (LeashPunishWindow, LeashHoldRing), heart tag swing and "Replay the snap".
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public sealed class LeashHolderCard : Border
{
    private readonly Func<ILeashService?> _svc;
    private HeldLeash _h;
    private readonly StackPanel _body = new();

    public LeashHolderCard(HeldLeash h, Func<ILeashService?> service)
    {
        _h = h;
        _svc = service;
        Background = LeashLook.CardBrush;
        BorderBrush = LeashLook.GoldDeepBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12);
        Margin = new Thickness(0, 4, 0, 6);
        Tag = "leash-holder-card:" + h.Who.Id;
        Child = _body;
        Render();
    }

    internal HeldLeash Held => _h;

    public void Update(HeldLeash h) { _h = h; Render(); }

    internal void Render()
    {
        _body.Children.Clear();
        _body.Children.Add(Head());
        var now = DateTimeOffset.UtcNow;
        if (LeashUiRules.DndOn(_h.DndUntil, now) && _h.DndUntil is { } until)
        {
            var q = LeashLook.Wrap(FriendsDrawer.Label(Loc.GetF("leash_holder_dnd", _h.Who.Name, LeashUiRules.DndUntilText(until)), 11.5, FriendsDrawer.Lilac));
            q.Margin = new Thickness(0, 8, 0, 0);
            q.Tag = "leash-holder-dnd";
            _body.Children.Add(q);
        }
    }

    private Control Head()
    {
        bool dnd = LeashUiRules.DndOn(_h.DndUntil, DateTimeOffset.UtcNow);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var face = FriendsDrawer.Avatar(_h.Who.Name, 46, _h.Online, _h.Who.AvatarUrl);
        face.Margin = new Thickness(0, 0, 10, 0);
        face.VerticalAlignment = VerticalAlignment.Top;
        g.Children.Add(face);

        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(FriendsDrawer.Label(_h.Who.Name, 18, FriendsDrawer.Text, FriendsDrawer.Display, FontWeight.SemiBold));
        var d = FriendsDrawer.Label(Loc.GetF("leash_holder_day", _h.Day), 11.5, FriendsDrawer.Gold, null, FontWeight.SemiBold);
        d.Tag = "leash-holder-day";
        who.Children.Add(d);
        var st = FriendsDrawer.Label(dnd ? Loc.Get("leash_state_quiet") : _h.Online ? Loc.Get("leash_state_online") : Loc.Get("leash_state_offline"),
            10.5, dnd ? FriendsDrawer.Lilac : _h.Online ? FriendsDrawer.Mint : FriendsDrawer.Dim, FriendsDrawer.Mono);
        st.Tag = "leash-holder-state";
        who.Children.Add(st);
        Grid.SetColumn(who, 1);
        g.Children.Add(who);

        var more = FriendsDrawer.Pill(new TextBlock { Text = "…", FontSize = 13, Foreground = FriendsDrawer.Muted, HorizontalAlignment = HorizontalAlignment.Center },
            Brushes.Transparent, FriendsDrawer.Muted, "leash-holder-more");
        more.Width = 22;
        more.Height = 22;
        more.Padding = new Thickness(0);
        more.VerticalAlignment = VerticalAlignment.Top;
        more.Cursor = FriendsDrawer.Hand();
        more.ContextMenu = Menu();
        more.Click += (_, _) => more.ContextMenu?.Open(more);
        Grid.SetColumn(more, 2);
        g.Children.Add(more);
        return g;
    }

    /// <summary>WPF Menu(): Let go (Replay the snap is owed on this head).</summary>
    internal ContextMenu Menu()
    {
        var m = new ContextMenu { Placement = PlacementMode.Bottom };
        var release = new MenuItem { Header = Loc.GetF("leash_menu_release", _h.Who.Name), Tag = "leash-menu-release" };
        release.Click += (_, _) => LeashCutConfirmWindow.Confirm(
            Loc.GetF("leash_release_confirm_title", _h.Who.Name),
            Loc.GetF("leash_release_confirm_body", _h.Who.Name),
            Loc.Get("leash_release_confirm_yes"),
            Loc.Get("leash_cut_confirm_no"),
            "leash-release-confirm",
            () => _ = ReleaseAsync());
        m.Items.Add(release);
        return m;
    }

    /// <summary>Lets go. True when the server took it.</summary>
    internal async Task<bool> ReleaseAsync()
    {
        try { if (_svc() is { } s) return await s.ReleaseAsync(_h.Who.Id); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] release failed: {E}", ex.Message); }
        return false;
    }
}
