// PORTED from WPF 7.1.5 Controls/Leash/LeashSelfCard.cs: the leashed side's own card at the top of
// the friends drawer. The scissors come first and are ONE click (never priced, never gated, no
// "are you sure"); the cut runs Core LeashService.CutAsync, whose LeashCutSafety turns Strict Lock
// off, panic on and ends remote + Lockdown locally before the wire. Then what waits (with "Watch it"
// on today's video task), the intensity switch, Do not disturb, the video cap, the panic-hold hint,
// the sticker shelf and the "what they see" preview. The chain under the name and the "?" help.
// Waiting on r10 (merge): the video cap slider (CAP SLIDER below) and LeashHolderCard readOnly.
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
    private readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false };
    private bool _preview;

    /// <summary>Raised the moment the cut is pressed, before the server answers.</summary>
    public event Action? CutPressed;

    /// <summary>Test seam: the toast a refused watch shows (WPF App.Notifications warning).</summary>
    internal static Action<string> Notify = text =>
    {
        try { Platform.OsNotifications.Show("CCP", text); } catch { }
    };

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
        var root = new Grid();
        root.Children.Add(_body);
        root.Children.Add(_fx);
        Child = root;
        Render();
    }

    internal MyLeash Me => _me;
    internal bool PreviewOpen => _preview;
    internal Canvas FxLayer => _fx;

    public void Update(MyLeash me) { _me = me; Render(); }

    internal void Render()
    {
        _body.Children.Clear();
        var now = DateTimeOffset.UtcNow;
        _body.Children.Add(Head());
        _body.Children.Add(CutButton());
        _body.Children.Add(Waiting());
        _body.Children.Add(Switches(now));
        if (_me.Stickers.Count > 0) _body.Children.Add(Shelf());
        _body.Children.Add(PreviewToggle());
        if (_preview) _body.Children.Add(Preview());
    }

    private Control Head()
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var av = FriendsDrawer.Avatar(_me.Holder.Name, 40, null, _me.Holder.AvatarUrl);
        av.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(av);
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { FontFamily = FriendsDrawer.Display, FontSize = 16, Foreground = FriendsDrawer.Text, TextWrapping = TextWrapping.Wrap, Tag = "leash-self-title" };
        t.Inlines!.Add(new Run(_me.Holder.Name) { Foreground = FriendsDrawer.Gold, FontWeight = FontWeight.SemiBold });
        t.Inlines.Add(new Run(" " + Loc.Get("leash_self_holds")));
        who.Children.Add(t);
        var chainLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        var chain = LeashLook.Chain(44, 8);
        chain.VerticalAlignment = VerticalAlignment.Center;
        chainLine.Children.Add(chain);
        var day = FriendsDrawer.Label(Loc.GetF("leash_self_day", _me.Day), 11, FriendsDrawer.Pink, FriendsDrawer.Mono);
        day.Margin = new Thickness(6, 0, 0, 0);
        day.Tag = "leash-self-day";
        chainLine.Children.Add(day);
        who.Children.Add(chainLine);
        Grid.SetColumn(who, 1);
        g.Children.Add(who);
        var help = LeashLook.Help(LeashExplainRole.Leashed);
        help.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(help, 2);
        g.Children.Add(help);
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
        try { LeashFx.SparksAt(_fx, new Point(Bounds.Width / 2, 70), (int)(14 * LeashFx.Amount), 80, LeashFx.MintC, LeashFx.LilacC); } catch { }
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
        if (gates > 0) sp.Children.Add(Line("bolt", FriendsDrawer.Red, Loc.GetF("leash_self_pending", gates), "leash-self-line:pending"));
        if (_me.Assignment is { } a)
        {
            var what = AssignText(a.Kind, a.Size);
            var (key, brush) = a.Status switch
            {
                AssignStatus.Done => ("leash_self_task_done", FriendsDrawer.Mint),
                AssignStatus.Missed => ("leash_self_task_missed", FriendsDrawer.Dim),
                _ => ("leash_self_task_open", FriendsDrawer.Gold),
            };
            sp.Children.Add(Line("task", brush, Loc.GetF(key, what, _me.Holder.Name), "leash-self-line:task"));
            if (LeashAssignRule.OfferWatch(a)) sp.Children.Add(WatchButton(a));
        }
        if (_me.Pardons > 0) sp.Children.Add(Line("scissors", FriendsDrawer.Lilac, Loc.GetF("leash_self_pardons", _me.Pardons), "leash-self-line:pardons"));
        if (sp.Children.Count == 0) sp.Children.Add(Line("heart", FriendsDrawer.Muted, Loc.Get("leash_self_clear"), "leash-self-line:clear"));
        return sp;
    }

    /// <summary>WPF LeashHolderCard.AssignText.</summary>
    internal static string AssignText(AssignKind k, int size) => k switch
    {
        AssignKind.Minutes => Loc.GetF("leash_assign_minutes_n", size),
        AssignKind.Quests => Loc.GetF("leash_assign_quests_n", size),
        _ => Loc.Get("leash_assign_video_n"),
    };

    /// <summary>"Watch it" on today's video task: opens the video and counts the real watch
    /// (the runner's StartAssignmentWatch). Disabled while that very task is running.</summary>
    private Control WatchButton(Assignment a)
    {
        bool running = false;
        try { running = LeashLocator.Runner()?.RunningAid == a.Aid; } catch { }
        var b = LeashLook.Chunky(Loc.Get(running ? "leash_self_watching" : "leash_self_watch"), LeashLook.Tone.Gold, "leash-self-watch", 12.5);
        b.Margin = new Thickness(20, 4, 0, 4);
        b.HorizontalAlignment = HorizontalAlignment.Left;
        b.IsEnabled = !running;
        b.Click += (_, _) => StartWatch(a);
        return b;
    }

    /// <summary>Opens the assignment's video. False (with a line) when it cannot now.</summary>
    internal bool StartWatch(Assignment a)
    {
        var runner = LeashLocator.Runner();
        bool ok = false;
        try { ok = runner?.StartAssignmentWatch(a) == true; }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] assignment watch failed: {E}", ex.Message); }
        if (!ok)
        {
            var key = runner?.IsRunning == true ? "leash_self_watch_busy" : "leash_self_watch_cannot";
            try { Notify(Loc.Get(key)); } catch { }
        }
        else Serilog.Log.Information("[Leash] watching today's video task {Aid}", a.Aid);
        Render();
        return ok;
    }

    private static Control Line(string icon, IBrush brush, string text, string tag)
    {
        var ic = LeashLook.Icon(icon, brush, 13);
        ic.Margin = new Thickness(0, 0, 7, 0);
        ic.VerticalAlignment = VerticalAlignment.Center;
        var label = LeashLook.Wrap(FriendsDrawer.Label(text, 12, brush, null, FontWeight.SemiBold));
        var row = new DockPanel { Margin = new Thickness(2, 2, 0, 2), Tag = tag };
        DockPanel.SetDock(ic, Dock.Left);
        row.Children.Add(ic);
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

        // CAP SLIDER: WPF puts the video cap here, between Do not disturb and the hold hint:
        //   var cap = new LeashCapSlider(Loc.Get("leash_video_max"), _me.VideoMax, "leash-video-max")
        //   { Margin = new Thickness(2, 10, 2, 0) }; ToolTip.SetTip(cap, Loc.Get("leash_video_max_hint"));
        //   cap.Committed += m => _ = SetVideoMaxAsync(m); sp.Children.Add(cap);
        // (r10 owns LeashCapSlider; SetVideoMaxAsync below is ready for it.)

        string? panicKey = null;
        try { panicKey = CoreSettings.Current?.PanicKey; } catch { }
        var hold = LeashLook.Caption(Loc.GetF("leash_self_hold_hint", LeashHoldToCut.KeyLabel(panicKey)), FriendsDrawer.Muted);
        hold.Margin = new Thickness(2, 10, 0, 0);
        hold.Tag = "leash-self-hold";
        sp.Children.Add(hold);
        return sp;
    }

    internal async Task SetVideoMaxAsync(int minutes)
    {
        if (minutes == _me.VideoMax) return;
        _me = _me with { VideoMax = minutes };
        try { if (_svc() is { } s) await s.SetVideoMaxAsync(minutes); } catch { }
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

    /// <summary>The sticker shelf: the newest 14, newest first, each tilted by its place.</summary>
    private Control Shelf()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Tag = "leash-shelf" };
        var h = LeashLook.Caption(Loc.GetF("leash_self_shelf", _me.Stickers.Count));
        h.Margin = new Thickness(2, 0, 0, 4);
        sp.Children.Add(h);
        var wrap = new WrapPanel();
        int i = 0;
        foreach (var s in _me.Stickers.Reverse().Take(14))
        {
            var d = LeashLook.StickerDisc(s.Id, 26, i++);
            d.Margin = new Thickness(0, 0, 5, 5);
            ToolTip.SetTip(d, Loc.GetF("leash_sticker_from", s.From.Name));
            wrap.Children.Add(d);
        }
        sp.Children.Add(wrap);
        return sp;
    }

    private Control PreviewToggle()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var ic = LeashLook.Icon("eye", FriendsDrawer.Lilac, 13);
        ic.Margin = new Thickness(0, 0, 6, 0);
        ic.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(ic);
        content.Children.Add(FriendsDrawer.Label(Loc.GetF(_preview ? "leash_self_preview_hide" : "leash_self_preview", _me.Holder.Name),
            12, FriendsDrawer.Lilac, FriendsDrawer.Display));
        var b = FriendsDrawer.Pill(content, Brushes.Transparent, FriendsDrawer.Lilac, "leash-preview-toggle", FriendsDrawer.Line2);
        b.CornerRadius = new CornerRadius(10);
        b.Padding = new Thickness(8, 5, 8, 5);
        b.Margin = new Thickness(0, 10, 0, 0);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.Cursor = FriendsDrawer.Hand();
        b.Click += (_, _) => TogglePreview();
        return b;
    }

    internal void TogglePreview() { _preview = !_preview; Render(); }

    /// <summary>The holder's card, drawn read-only from this side's own report. The strip is the
    /// server's; this side does not have it, so the preview says so instead of guessing.</summary>
    private Control Preview()
    {
        DayReport? r = null;
        try { r = LeashLocator.LocalReport(); } catch { }
        var me = new LeashPerson("me", Loc.Get("leash_you"), null);
        var held = new HeldLeash(me, true, _me.Intensity, _me.Since, _me.Day, _me.DndUntil, r,
            Array.Empty<WeekDay>(), _me.Pending, _me.Assignment, 0);
        // SEAM (r10 merge): WPF passes readOnly: true; until r10's ctor has it, the null service and
        // the hit-test off keep every button inert.
        var card = new LeashHolderCard(held, () => null) { IsHitTestVisible = false, Opacity = 0.92, Margin = new Thickness(0, 8, 0, 0), Tag = "leash-preview" };
        var box = new StackPanel();
        box.Children.Add(card);
        var note = LeashLook.Wrap(FriendsDrawer.Label(Loc.GetF("leash_self_preview_note", _me.Holder.Name), 10.5, FriendsDrawer.Dim));
        note.Margin = new Thickness(2, 4, 0, 0);
        box.Children.Add(note);
        return box;
    }
}
