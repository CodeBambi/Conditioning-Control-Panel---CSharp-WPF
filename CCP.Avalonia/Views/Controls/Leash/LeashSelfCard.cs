// PORTED from WPF 7.1.5 Controls/Leash/LeashSelfCard.cs: the leashed side's own card at the top of
// the friends drawer. The scissors come first and are ONE click (never priced, never gated, no
// "are you sure"); the cut runs Core LeashService.CutAsync, whose LeashCutSafety turns Strict Lock
// off, panic on and ends remote + Lockdown locally before the wire. Then what waits, the intensity
// switch, Do not disturb and the panic-hold hint.
// ponytail: cut sparks (LeashFx), the "Watch it" video-task button (ILeashTaskRunner), the video cap
// slider (LeashCapSlider), the sticker shelf and the "what they see" preview.
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public sealed class LeashSelfCard : Border
{
    private readonly Func<ILeashService?> _svc;
    private MyLeash _me;
    private readonly StackPanel _body = new();

    /// <summary>Raised the moment the cut is pressed, before the server answers.</summary>
    public event Action? CutPressed;

    public LeashSelfCard(MyLeash me, Func<ILeashService?> service)
    {
        _me = me;
        _svc = service;
        Background = LeashLook.SelfCardBrush;
        BorderBrush = LeashLook.SelfBorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12);
        Margin = new Thickness(0, 4, 0, 6);
        Tag = "leash-self-card";
        Child = _body;
        Render();
    }

    internal MyLeash Me => _me;

    public void Update(MyLeash me) { _me = me; Render(); }

    internal void Render()
    {
        _body.Children.Clear();
        _body.Children.Add(Head());
        _body.Children.Add(CutButton());
        _body.Children.Add(Waiting());
        _body.Children.Add(Switches(DateTimeOffset.UtcNow));
    }

    private Control Head()
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var av = FriendsDrawer.Avatar(_me.Holder.Name, 40, null, _me.Holder.AvatarUrl);
        av.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(av);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { FontFamily = FriendsDrawer.Display, FontSize = 16, Foreground = FriendsDrawer.Text, TextWrapping = TextWrapping.Wrap, Tag = "leash-self-title" };
        t.Inlines!.Add(new Run(_me.Holder.Name) { Foreground = FriendsDrawer.Gold, FontWeight = FontWeight.SemiBold });
        t.Inlines.Add(new Run(" " + Loc.Get("leash_self_holds")));
        who.Children.Add(t);
        var day = FriendsDrawer.Label(Loc.GetF("leash_self_day", _me.Day), 11, FriendsDrawer.Pink, FriendsDrawer.Mono);
        day.Margin = new Thickness(0, 3, 0, 0);
        day.Tag = "leash-self-day";
        who.Children.Add(day);
        Grid.SetColumn(who, 1);
        g.Children.Add(who);
        return g;
    }

    /// <summary>The scissors. Mint, full width, first thing under the name.</summary>
    private Control CutButton()
    {
        var b = LeashLook.Chunky(Loc.Get("leash_cut"), LeashLook.Tone.Mint, "leash-cut", 14);
        b.Margin = new Thickness(0, 10, 0, 0);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        ToolTip.SetTip(b, Loc.Get("leash_cut_tip"));
        b.Click += async (_, _) => await CutAsync();
        return b;
    }

    /// <summary>Cuts. Never refused: the host drops the card at once. The live service goes through
    /// LeashHead.Cut (CutDone, the service cut with its local safety, the done line); a service
    /// handed in (tests) is cut directly.</summary>
    internal async Task CutAsync()
    {
        try { CutPressed?.Invoke(); } catch { }
        try
        {
            var s = _svc();
            if (s == null || ReferenceEquals(s, Platform.LeashHead.Service)) Platform.LeashHead.Cut();
            else await s.CutAsync();
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] cut call failed (service retries): {E}", ex.Message); }
    }

    private Control Waiting()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Tag = "leash-self-waiting" };
        int gates = _me.Pending.Count(p => p.Kind != PunishKind.Chaster);
        if (gates > 0) sp.Children.Add(Line(FriendsDrawer.Red, Loc.GetF("leash_self_pending", gates), "leash-self-line:pending"));
        if (_me.Assignment is { } a)
        {
            var what = AssignText(a.Kind, a.Size);
            var (key, brush) = a.Status switch
            {
                AssignStatus.Done => ("leash_self_task_done", FriendsDrawer.Mint),
                AssignStatus.Missed => ("leash_self_task_missed", FriendsDrawer.Dim),
                _ => ("leash_self_task_open", FriendsDrawer.Gold),
            };
            sp.Children.Add(Line(brush, Loc.GetF(key, what, _me.Holder.Name), "leash-self-line:task"));
        }
        if (_me.Pardons > 0) sp.Children.Add(Line(FriendsDrawer.Lilac, Loc.GetF("leash_self_pardons", _me.Pardons), "leash-self-line:pardons"));
        if (sp.Children.Count == 0) sp.Children.Add(Line(FriendsDrawer.Muted, Loc.Get("leash_self_clear"), "leash-self-line:clear"));
        return sp;
    }

    /// <summary>WPF LeashHolderCard.AssignText.</summary>
    internal static string AssignText(AssignKind k, int size) => k switch
    {
        AssignKind.Minutes => Loc.GetF("leash_assign_minutes_n", size),
        AssignKind.Quests => Loc.GetF("leash_assign_quests_n", size),
        _ => Loc.Get("leash_assign_video_n"),
    };

    private static Control Line(IBrush brush, string text, string tag)
    {
        var dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = brush, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var label = LeashLook.Wrap(FriendsDrawer.Label(text, 12, brush, null, FontWeight.SemiBold));
        var row = new DockPanel { Margin = new Thickness(2, 2, 0, 2), Tag = tag };
        DockPanel.SetDock(dot, Dock.Left);
        row.Children.Add(dot);
        row.Children.Add(label);
        return row;
    }

    private Control Switches(DateTimeOffset now)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var h1 = LeashLook.Caption(Loc.Get("leash_self_level"));
        h1.Margin = new Thickness(2, 0, 0, 4);
        sp.Children.Add(h1);
        sp.Children.Add(LeashLook.Segmented(
            new[] { Loc.Get("leash_level_soft"), Loc.Get("leash_level_standard"), Loc.Get("leash_level_strict") },
            (int)_me.Intensity, i => _ = SetLevelAsync((LeashIntensity)i), "leash-level"));

        bool dnd = LeashUiRules.DndOn(_me.DndUntil, now);
        var h2 = LeashLook.Caption(dnd && _me.DndUntil is { } u ? Loc.GetF("leash_self_quiet_until", LeashUiRules.DndUntilText(u)) : Loc.Get("leash_self_quiet"),
            dnd ? FriendsDrawer.Lilac : null);
        h2.Margin = new Thickness(2, 8, 0, 4);
        h2.Tag = "leash-self-quiet";
        sp.Children.Add(h2);
        sp.Children.Add(LeashLook.Segmented(
            new[] { Loc.Get("leash_dnd_off"), Loc.Get("leash_dnd_1h"), Loc.Get("leash_dnd_4h"), Loc.Get("leash_dnd_today") },
            dnd ? -1 : 0,
            i => _ = SetDndAsync(i switch { 1 => LeashDnd.OneHour, 2 => LeashDnd.FourHours, 3 => LeashDnd.Today, _ => LeashDnd.Off }),
            "leash-dnd", 11.5));

        string? panicKey = null;
        try { panicKey = CoreSettings.Current?.PanicKey; } catch { }
        var hold = LeashLook.Caption(Loc.GetF("leash_self_hold_hint", LeashHoldToCut.KeyLabel(panicKey)), FriendsDrawer.Muted);
        hold.Margin = new Thickness(2, 10, 0, 0);
        hold.Tag = "leash-self-hold";
        sp.Children.Add(hold);
        return sp;
    }

    internal async Task SetLevelAsync(LeashIntensity level)
    {
        if (level == _me.Intensity) return;
        _me = _me with { Intensity = level };
        Render();
        try { if (_svc() is { } s) await s.SetIntensityAsync(level); } catch { }
    }

    internal async Task SetDndAsync(LeashDnd dnd)
    {
        try { if (_svc() is { } s) await s.SetDndAsync(dnd); } catch { }
    }
}
