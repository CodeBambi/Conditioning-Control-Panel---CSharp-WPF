// PORTED from WPF 7.1.5 Controls/Leash/LeashHolderCard.cs (756 lines): THE LEASH CARD, holder side,
// one per account held. Head (avatar with presence, name, day, online / offline / quiet, the "..."
// menu with Replay the snap + Let go), three figures (minutes ring toward the day's goal, quests,
// lock time and tab with Chaster, else streak), today's task line, the 7-day strip, the quiet-until
// line, four buttons (Assign, Reward, Punish, Tug) whose preset sheets open in place under them, the
// worded result for 3 s, and the receipts timeline ("what you sent" with its steps). Punishments the
// leashed side's intensity does not allow are HIDDEN, never greyed. No free text: every send is a
// preset id; the only box is the Hypnotube number, filtered to digits as it is typed.
// Flat on this head: no Effect glow on the card (Avalonia effect/cache rule).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public sealed class LeashHolderCard : Border
{
    private readonly Func<ILeashService?> _svc;
    private HeldLeash _h;
    private readonly Grid _root = new();
    private readonly StackPanel _body = new();
    private readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false };

    /// <summary>"assign", "punish", "reward" or null.</summary>
    private string? _sheet;

    /// <summary>The punishment or assignment waiting for a video id, or null.</summary>
    private string? _videoFor;
    private int _videoCap = LeashVideoCap.Default;

    private (string Text, bool Good)? _result;
    private DispatcherTimer? _resultTimer;

    /// <summary>True for the leashed side's "what they see" preview: no tools, no buttons.</summary>
    private readonly bool _readOnly;

    /// <summary>Raised when the holder asks to see the snap again (WPF event).</summary>
    public event Action<HeldLeash>? ReplaySnapRequested;

    /// <summary>Seam r11 fills with LeashSnapCard's replay; used when nobody listens to the event.</summary>
    internal static Action<HeldLeash>? ReplaySnap { get; set; }

    public LeashHolderCard(HeldLeash held, Func<ILeashService?> service, bool readOnly = false)
    {
        _readOnly = readOnly;
        _h = held;
        _svc = service;
        Background = LeashLook.CardBrush;
        BorderBrush = LeashLook.GoldDeepBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12);
        Margin = new Thickness(0, 4, 0, 6);
        Tag = "leash-holder-card:" + held.Who.Id;
        // WPF: Effect = FriendsLook.Glow(Gold, 18, 0.18). FX: a sibling BoxShadow glow (CardGlow) if wanted.
        _root.Children.Add(_body);
        _root.Children.Add(_fx);
        Child = _root;
        Render();
    }

    public string LeashedId => _h.Who.Id;
    internal HeldLeash Held => _h;
    internal string? OpenSheet => _sheet;
    internal string? ResultText => _result?.Text;
    internal StackPanel Body => _body;

    public void Update(HeldLeash held)
    {
        _h = held;
        Render();
    }

    // ---- drawing ----------------------------------------------------------------------

    internal void Render()
    {
        _body.Children.Clear();
        var now = DateTimeOffset.UtcNow;
        bool dnd = LeashUiRules.DndOn(_h.DndUntil, now);

        _body.Children.Add(Head(dnd));
        _body.Children.Add(Figures());
        if (_h.Assignment is { } a) _body.Children.Add(AssignmentLine(a));
        _body.Children.Add(Week());
        if (dnd && _h.DndUntil is { } until)
        {
            var q = LeashLook.Wrap(FriendsDrawer.Label(Loc.GetF("leash_holder_dnd", _h.Who.Name, LeashUiRules.DndUntilText(until)), 11.5, FriendsDrawer.Lilac));
            q.Margin = new Thickness(2, 8, 0, 0);
            q.Tag = "leash-holder-dnd";
            _body.Children.Add(q);
        }
        if (_readOnly)
        {
            var can = LeashLook.Wrap(FriendsDrawer.Label(Loc.Get("leash_preview_buttons"), 11, FriendsDrawer.Dim));
            can.Margin = new Thickness(2, 10, 0, 0);
            can.Tag = "leash-preview-buttons";
            _body.Children.Add(can);
            return;
        }
        _body.Children.Add(Buttons(dnd));
        if (_sheet != null)
        {
            var sheet = _sheet switch
            {
                "assign" => AssignSheet(),
                "reward" => RewardSheet(),
                _ => PunishSheet(),
            };
            sheet.Margin = new Thickness(0, 8, 0, 0);
            _body.Children.Add(sheet);
        }
        if (_result is { } r)
        {
            var line = LeashLook.Wrap(FriendsDrawer.Label(r.Text, 12, r.Good ? FriendsDrawer.Mint : FriendsDrawer.Gold, FriendsDrawer.Display, FontWeight.Medium));
            line.Margin = new Thickness(2, 8, 0, 0);
            line.Tag = "leash-result";
            _body.Children.Add(line);
        }
        if (SentSection() is { } sent) _body.Children.Add(sent);
    }

    // ---- receipts: what was sent and how far it got ------------------------------------

    /// <summary>True once the server has shown it speaks receipts (CONTRACT "Receipts").</summary>
    private bool ReceiptsOn
    {
        get
        {
            try { return _svc()?.ReceiptsSupported == true; } catch { return false; }
        }
    }

    /// <summary>The last few things sent to them, each with its steps. Absent until the server
    /// speaks receipts, so an older server draws exactly what it drew before.</summary>
    private Control? SentSection()
    {
        if (!ReceiptsOn) return null;
        IReadOnlyList<LeashSentItem> items;
        try { items = _svc()?.SentTo(_h.Who.Id) ?? Array.Empty<LeashSentItem>(); }
        catch { return null; }
        if (items.Count == 0) return null;
        var now = DateTimeOffset.UtcNow;
        var sp = new StackPanel { Margin = new Thickness(2, 10, 0, 0), Tag = "leash-sent" };
        var head = LeashLook.Caption(Loc.Get("leash_sent_title"));
        head.Margin = new Thickness(0, 0, 0, 3);
        sp.Children.Add(head);
        foreach (var i in items.Take(LeashUiRules.SentRows)) sp.Children.Add(SentRow(i, now));
        return sp;
    }

    private static IBrush StepBrush(LeashStep s) => s switch
    {
        LeashStep.Sent => FriendsDrawer.Dim,
        LeashStep.Arrived or LeashStep.Skipped => FriendsDrawer.Lilac,
        LeashStep.Seen => FriendsDrawer.Gold,
        LeashStep.Done or LeashStep.Accepted => FriendsDrawer.Mint,
        _ => FriendsDrawer.Red,
    };

    private static string ItemIcon(LeashSentItem i) => i.Kind switch
    {
        LeashItemKind.Punish => i.Punish is { } k ? PunishIcon(k) : "bolt",
        LeashItemKind.Assign => "task",
        LeashItemKind.Reward => "star",
        LeashItemKind.Tug => "hand",
        _ => "link",
    };

    private static Control SentRow(LeashSentItem i, DateTimeOffset now)
    {
        var accent = StepBrush(i.Step);
        var g = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Margin = new Thickness(0, 2, 0, 2),
            Tag = $"leash-sent-row:{i.Kind.ToString().ToLowerInvariant()}:{i.Step.ToString().ToLowerInvariant()}",
            Background = Brushes.Transparent,
        };
        ToolTip.SetTip(g, LeashUiRules.Timeline(i, now, Loc.Get));

        var ic = LeashLook.Icon(ItemIcon(i), FriendsDrawer.Muted, 12);
        ic.Margin = new Thickness(0, 0, 6, 0);
        ic.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(ic);

        var what = FriendsDrawer.Label(LeashUiRules.ItemText(i, Loc.Get), 11.5, FriendsDrawer.Text);
        Grid.SetColumn(what, 1);
        g.Children.Add(what);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        var lit = LeashUiRules.Lit(i);
        var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Tag = "leash-steps" };
        for (int k = 0; k < lit.Length; k++)
        {
            dots.Children.Add(new Ellipse
            {
                Width = 6,
                Height = 6,
                Margin = new Thickness(k == 0 ? 0 : 3, 0, 0, 0),
                Fill = lit[k] ? accent : Brushes.Transparent,
                Stroke = lit[k] ? accent : FriendsDrawer.Line2,
                StrokeThickness = 1,
                Tag = $"leash-step-dot:{k}:{(lit[k] ? "on" : "off")}",
            });
        }
        right.Children.Add(dots);
        var word = FriendsDrawer.Label(Loc.Get(LeashUiRules.StepKey(i.Step)), 10.5, accent, FriendsDrawer.Mono, FontWeight.SemiBold);
        word.Margin = new Thickness(6, 0, 0, 0);
        word.Tag = "leash-step-word";
        right.Children.Add(word);
        var when = FriendsDrawer.Label(LeashUiRules.StepTime(i.StepAt, now), 10, FriendsDrawer.Dim, FriendsDrawer.Mono);
        when.Margin = new Thickness(5, 0, 0, 0);
        right.Children.Add(when);
        Grid.SetColumn(right, 2);
        g.Children.Add(right);
        return g;
    }

    private Control Head(bool dnd)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };

        var av = new Grid { Width = 48, Height = 56, Margin = new Thickness(0, 0, 10, 0), Tag = "leash-holder-avatar" };
        var face = FriendsDrawer.Avatar(_h.Who.Name, 46, _h.Online, _h.Who.AvatarUrl);
        face.VerticalAlignment = VerticalAlignment.Top;
        av.Children.Add(face);
        var tag = LeashLook.HeartTag(22);
        tag.HorizontalAlignment = HorizontalAlignment.Left;
        tag.VerticalAlignment = VerticalAlignment.Bottom;
        tag.Margin = new Thickness(-2, 0, 0, -4);
        av.Children.Add(tag);
        LeashFx.Swing(tag);
        g.Children.Add(av);

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

        if (_readOnly) return g;
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top, Tag = "leash-holder-tools" };
        tools.Children.Add(LeashLook.Help(LeashExplainRole.Holder));
        var more = FriendsDrawer.Pill(new TextBlock { Text = "…", FontSize = 13, Foreground = FriendsDrawer.Muted, HorizontalAlignment = HorizontalAlignment.Center },
            Brushes.Transparent, FriendsDrawer.Muted, "leash-holder-more");
        more.Width = 22;
        more.Height = 22;
        more.Padding = new Thickness(0);
        more.Margin = new Thickness(4, 0, 0, 0);
        more.Cursor = FriendsDrawer.Hand();
        more.ContextMenu = Menu();
        more.Click += (_, _) => more.ContextMenu?.Open(more);
        tools.Children.Add(more);
        Grid.SetColumn(tools, 2);
        g.Children.Add(tools);
        return g;
    }

    /// <summary>WPF Menu(): Replay the snap, then Let go (asks first: it ends the leash for both and
    /// sets a 24 h cooldown).</summary>
    internal ContextMenu Menu()
    {
        var m = new ContextMenu { Placement = PlacementMode.Bottom };
        var replay = new MenuItem { Header = Loc.Get("leash_menu_replay"), Tag = "leash-menu-replay" };
        replay.Click += (_, _) => RequestReplay();
        m.Items.Add(replay);
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

    internal void RequestReplay()
    {
        if (ReplaySnapRequested != null) ReplaySnapRequested(_h);
        else ReplaySnap?.Invoke(_h);
    }

    /// <summary>Lets go. The cut sound and the line play only when the server took it.</summary>
    internal async Task<bool> ReleaseAsync()
    {
        bool ok = false;
        try { if (_svc() is { } s) ok = await s.ReleaseAsync(_h.Who.Id); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] release failed: {E}", ex.Message); }
        if (ok) LeashFx.Cut();
        else LeashFx.Denied();
        try
        {
            App.Notifications.Show(Loc.GetF(ok ? "leash_release_done" : "leash_release_failed", _h.Who.Name),
                ok ? Helpers.NotificationType.Info : Helpers.NotificationType.Warning);
        }
        catch { }
        return ok;
    }

    // ---- figures, task, week ------------------------------------------------------------

    private Control Figures()
    {
        var g = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0), Tag = "leash-figures" };
        var r = _h.Report;
        int goal = LeashUiRules.MinutesGoal(_h.Assignment);
        int minutes = r?.Minutes ?? 0;
        g.Children.Add(Fig(LeashHoldRing.Ring(LeashUiRules.RingFraction(minutes, goal), r == null ? "-" : minutes.ToString(), LeashFx.PinkC, 48),
            Loc.Get("leash_fig_minutes"), null, "leash-fig-minutes"));
        g.Children.Add(Fig(Figure(r == null ? "-" : r.QuestsDone.ToString(), r == null ? "" : "/" + r.QuestsTotal, FriendsDrawer.Text),
            Loc.Get("leash_fig_quests"), r == null ? null : Loc.GetF("leash_fig_streak", r.Streak), "leash-fig-quests"));
        if (r is { ChasterLinked: true })
        {
            var lockTxt = r.LockLeftSeconds is int s ? LeashUiRules.LockLeft(s) : "-";
            var tab = r.TabSeconds is int t ? LeashUiRules.Tab(t) : null;
            g.Children.Add(Fig(Figure(lockTxt, "", FriendsDrawer.Gold, 17), Loc.Get("leash_fig_lock"),
                tab == null ? null : Loc.GetF("leash_fig_tab", tab), "leash-fig-lock", tab != null && r.TabSeconds > 0 ? FriendsDrawer.Red : FriendsDrawer.Mint));
        }
        else
        {
            g.Children.Add(Fig(Figure(r == null ? "-" : r.Streak.ToString(), r == null ? "" : "d", FriendsDrawer.Lilac),
                Loc.Get("leash_fig_streak_label"), null, "leash-fig-streak"));
        }
        return g;
    }

    private static Control Figure(string big, string small, IBrush fg, double size = 21)
    {
        var t = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontFamily = FriendsDrawer.Mono, FontWeight = FontWeight.Bold, Inlines = new global::Avalonia.Controls.Documents.InlineCollection() };
        t.Inlines.Add(new global::Avalonia.Controls.Documents.Run(big) { FontSize = size, Foreground = fg });
        if (small.Length > 0) t.Inlines.Add(new global::Avalonia.Controls.Documents.Run(small) { FontSize = size * 0.7, Foreground = FriendsDrawer.Dim });
        var g = new Grid { Height = 48 };
        g.Children.Add(new Border { VerticalAlignment = VerticalAlignment.Center, Child = t });
        return g;
    }

    private static Control Fig(Control visual, string label, string? extra, string tag, IBrush? extraBrush = null)
    {
        var box = new Border
        {
            Background = LeashLook.Shade,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4, 8, 4, 7),
            Margin = new Thickness(3, 0, 3, 0),
            Tag = tag,
        };
        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        visual.HorizontalAlignment = HorizontalAlignment.Center;
        sp.Children.Add(visual);
        var l = LeashLook.Caption(label);
        l.HorizontalAlignment = HorizontalAlignment.Center;
        l.TextAlignment = TextAlignment.Center;
        l.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(l);
        if (extra != null)
        {
            var e = LeashLook.Caption(extra, extraBrush ?? FriendsDrawer.Muted);
            e.HorizontalAlignment = HorizontalAlignment.Center;
            e.TextAlignment = TextAlignment.Center;
            e.Tag = tag + ":extra";
            sp.Children.Add(e);
        }
        box.Child = sp;
        return box;
    }

    private Control AssignmentLine(Assignment a)
    {
        string what = AssignText(a.Kind, a.Size, a.Watch);
        var (key, brush) = a.Status switch
        {
            AssignStatus.Done => ("leash_task_done", FriendsDrawer.Mint),
            AssignStatus.Missed => ("leash_task_missed", FriendsDrawer.Red),
            _ => ("leash_task_open", FriendsDrawer.Gold),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 10, 0, 0), Tag = "leash-holder-task" };
        var ic = LeashLook.Icon("task", brush, 13);
        ic.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(ic);
        row.Children.Add(FriendsDrawer.Label(Loc.GetF(key, what), 11.5, brush, null, FontWeight.SemiBold));
        return row;
    }

    internal static string AssignText(AssignKind k, int size, LeashWatch? w) => k switch
    {
        AssignKind.Minutes => Loc.GetF("leash_assign_minutes_n", size),
        AssignKind.Quests => Loc.GetF("leash_assign_quests_n", size),
        _ => Loc.Get("leash_assign_video_n"),
    };

    private Control Week()
    {
        var g = new UniformGrid { Columns = 7, Margin = new Thickness(0, 10, 0, 0), Tag = "leash-week" };
        foreach (var d in _h.Week.TakeLast(7))
            g.Children.Add(LeashLook.WeekCell(d.Mark, LeashUiRules.DayLetter(d.Day)));
        return g;
    }

    // ---- buttons ------------------------------------------------------------------------

    private Control Buttons(bool dnd)
    {
        var g = new UniformGrid { Columns = 4, Margin = new Thickness(0, 10, 0, 0), Tag = "leash-holder-buttons" };
        Add(g, "assign", Loc.Get("leash_btn_assign"), LeashLook.Tone.Gold, "task", !dnd);
        Add(g, "reward", Loc.Get("leash_btn_reward"), LeashLook.Tone.Mint, "star", true);
        Add(g, "punish", Loc.Get("leash_btn_punish"), LeashLook.Tone.Red, "bolt", !dnd);
        var tug = LeashLook.Stacked(Loc.Get("leash_btn_tug"), LeashLook.Tone.Ghost, "hand", "leash-btn:tug");
        tug.Margin = new Thickness(3, 0, 0, 0);
        tug.IsEnabled = !dnd;
        tug.Click += async (_, _) => await TugAsync();
        g.Children.Add(tug);
        return g;
    }

    private void Add(UniformGrid g, string id, string text, LeashLook.Tone tone, string icon, bool enabled)
    {
        var b = LeashLook.Stacked(text, tone, icon, "leash-btn:" + id + (_sheet == id ? ":open" : ""));
        b.Margin = new Thickness(g.Children.Count == 0 ? 0 : 3, 0, 3, 0);
        b.IsEnabled = enabled;
        if (_sheet == id) b.BorderBrush = FriendsDrawer.Text;
        b.Click += (_, _) => ToggleSheet(id);
        g.Children.Add(b);
    }

    internal void ToggleSheet(string id)
    {
        _sheet = _sheet == id ? null : id;
        _videoFor = null;
        Render();
        if (_sheet != null && _body.Children.Count > 0 && LeashFx.Amount > 0)
        {
            int at = _body.Children.Count - (_result != null ? 2 : 1);
            if (at >= 0) LeashFx.SheetIn(_body.Children[at]);
        }
    }

    // ---- sheets -----------------------------------------------------------------------

    private static (Border Sheet, StackPanel Rows) Sheet(string title, string tag)
    {
        var sp = new StackPanel();
        var b = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x24, 0x18, 0x38)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Tag = tag,
            Child = sp,
        };
        var head = LeashLook.Caption(title);
        head.Margin = new Thickness(0, 0, 0, 6);
        head.TextWrapping = TextWrapping.NoWrap;
        head.TextTrimming = TextTrimming.CharacterEllipsis;
        sp.Children.Add(head);
        return (b, sp);
    }

    /// <summary>One sheet row: a name on the left and its size chips on the right.</summary>
    private Control SizeRow(string label, string icon, IBrush accent, IEnumerable<(string Text, string Tag, Func<Task> Send)> chips)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2, 0, 2) };
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ic = LeashLook.Icon(icon, accent, 13);
        ic.Margin = new Thickness(0, 0, 6, 0);
        name.Children.Add(ic);
        name.Children.Add(FriendsDrawer.Label(label, 12.5, FriendsDrawer.Text, FriendsDrawer.Display, FontWeight.Medium));
        g.Children.Add(name);
        var chipsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, tag, send) in chips)
        {
            var c = FriendsDrawer.Pill(new TextBlock { Text = text, FontFamily = FriendsDrawer.Mono, FontSize = 11.5, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                LeashLook.ButtonBg, FriendsDrawer.Text, tag, FriendsDrawer.Line2);
            c.MinWidth = 40;
            c.Margin = new Thickness(4, 0, 0, 0);
            c.Padding = new Thickness(7, 4, 7, 4);
            c.HorizontalContentAlignment = HorizontalAlignment.Center;
            c.Cursor = FriendsDrawer.Hand();
            c.Click += async (_, _) =>
            {
                LeashFx.Pop(c);
                LeashFx.Sparks(_fx, c, (int)(8 * LeashFx.Amount), accent is ISolidColorBrush ab ? ab.Color : LeashFx.GoldC, LeashFx.GoldC);
                await send();
            };
            chipsPanel.Children.Add(c);
        }
        Grid.SetColumn(chipsPanel, 1);
        g.Children.Add(chipsPanel);
        return g;
    }

    private Control PunishSheet()
    {
        var levelName = Loc.Get(LeashUiRules.Key(_h.Intensity));
        var (sheet, rows) = Sheet(Loc.GetF("leash_sheet_punish", Math.Min(_h.PunishedToday, 3), levelName), "leash-sheet:punish");
        if (_h.PunishedToday >= 3)
        {
            var cap = LeashLook.Wrap(FriendsDrawer.Label(Loc.Get("leash_sheet_punish_cap"), 12, FriendsDrawer.Muted));
            cap.Tag = "leash-sheet-cap";
            rows.Children.Add(cap);
            return sheet;
        }
        if (_h.Pending.Count > 0)
        {
            var w = LeashLook.Wrap(FriendsDrawer.Label(Loc.GetF("leash_sheet_waiting", _h.Pending.Count), 11, FriendsDrawer.Gold));
            w.Margin = new Thickness(0, 0, 0, 4);
            w.Tag = "leash-sheet-waiting";
            rows.Children.Add(w);
        }
        foreach (var k in LeashUiRules.VisiblePunishments(_h.Intensity, _h.Report?.ChasterLinked == true))
        {
            var kind = k;
            var chips = LeashUiRules.Sizes(k).Select(sz => (
                k == PunishKind.Video ? Loc.Get("leash_pick") : LeashUiRules.SizeText(k, sz),
                $"leash-punish:{k.ToString().ToLowerInvariant()}:{sz}",
                (Func<Task>)(() => k == PunishKind.Video ? OpenVideo("punish") : SendPunishAsync(kind, sz, null))));
            rows.Children.Add(SizeRow(Loc.Get(LeashUiRules.Key(k)), PunishIcon(k), FriendsDrawer.Red, chips));
            if (k == PunishKind.Video && _videoFor == "punish")
            {
                _videoCap = Math.Min(_videoCap, _h.VideoMax);
                var cap = new LeashCapSlider(Loc.Get("leash_video_cap_label"), _videoCap, "leash-video-cap")
                {
                    Ceiling = _h.VideoMax,
                    Margin = new Thickness(20, 2, 0, 6),
                };
                ToolTip.SetTip(cap, Loc.Get("leash_video_max_hint"));
                cap.Committed += v => _videoCap = v;
                rows.Children.Add(cap);
                rows.Children.Add(VideoPicker(w => SendPunishAsync(PunishKind.Video, _videoCap, w)));
            }
        }
        return sheet;
    }

    private Control AssignSheet()
    {
        var (sheet, rows) = Sheet(Loc.GetF("leash_sheet_assign", _h.Who.Name), "leash-sheet:assign");
        if (_h.Assignment is { Status: AssignStatus.Open } a)
        {
            var w = LeashLook.Wrap(FriendsDrawer.Label(Loc.GetF("leash_sheet_replaces", AssignText(a.Kind, a.Size, a.Watch)), 11, FriendsDrawer.Gold));
            w.Margin = new Thickness(0, 0, 0, 4);
            w.Tag = "leash-sheet-replaces";
            rows.Children.Add(w);
        }
        foreach (var k in LeashUiRules.AssignOrder)
        {
            var kind = k;
            var chips = LeashUiRules.Sizes(k).Select(sz => (
                k == AssignKind.Video ? Loc.Get("leash_pick") : sz.ToString(),
                $"leash-assign:{k.ToString().ToLowerInvariant()}:{sz}",
                (Func<Task>)(() => k == AssignKind.Video ? OpenVideo("assign") : SendAssignAsync(kind, sz, null))));
            rows.Children.Add(SizeRow(Loc.Get(LeashUiRules.Key(k)), k switch { AssignKind.Minutes => "clock", AssignKind.Quests => "task", _ => "play" },
                FriendsDrawer.Gold, chips));
            if (k == AssignKind.Video && _videoFor == "assign") rows.Children.Add(VideoPicker(w => SendAssignAsync(AssignKind.Video, 1, w)));
        }
        return sheet;
    }

    private Control RewardSheet()
    {
        var (sheet, rows) = Sheet(Loc.Get("leash_sheet_reward"), "leash-sheet:reward");

        // Stickers: the discs themselves are the buttons.
        var stickers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6), Tag = "leash-reward-stickers" };
        int i = 0;
        foreach (var id in LeashUiRules.Stickers)
        {
            var sid = id;
            var disc = LeashLook.StickerDisc(id, 32, i++);
            var b = FriendsDrawer.Pill(disc, Brushes.Transparent, FriendsDrawer.Text, "leash-reward:sticker:" + id);
            b.CornerRadius = new CornerRadius(18);
            b.Padding = new Thickness(2);
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Cursor = FriendsDrawer.Hand();
            ToolTip.SetTip(b, Loc.Get("leash_rew_sticker_" + id));
            b.Click += async (_, _) =>
            {
                LeashFx.Pop(b);
                LeashFx.Sparks(_fx, b, (int)(10 * LeashFx.Amount), LeashFx.GoldC, LeashFx.MintC);
                await SendRewardAsync(RewardKind.Sticker, sid, null);
            };
            stickers.Children.Add(b);
        }
        rows.Children.Add(stickers);

        rows.Children.Add(SizeRow(Loc.Get("leash_rew_praise"), "heart", FriendsDrawer.Pink,
            LeashUiRules.Praise.Select(p => (Loc.Get("leash_praise_" + p), "leash-reward:praise:" + p, (Func<Task>)(() => SendRewardAsync(RewardKind.Praise, p, null))))));
        rows.Children.Add(SizeRow(Loc.Get("leash_rew_pardon"), "scissors", FriendsDrawer.Lilac,
            new[] { (Loc.Get("leash_give"), "leash-reward:pardon", (Func<Task>)(() => SendRewardAsync(RewardKind.Pardon, null, null))) }));
        if (_h.Report?.ChasterLinked == true)
        {
            rows.Children.Add(SizeRow(Loc.Get("leash_rew_credit"), "lock", FriendsDrawer.Mint,
                LeashUiRules.CreditSizes.Select(s => ("-" + LeashUiRules.Clock(s), "leash-reward:credit:" + s, (Func<Task>)(() => SendRewardAsync(RewardKind.Credit, null, s))))));
        }
        return sheet;
    }

    private static string PunishIcon(PunishKind k) => k switch
    {
        PunishKind.Lines => "lock",
        PunishKind.Pink => "eye",
        PunishKind.Bubbles => "bubble",
        PunishKind.Detention => "clock",
        PunishKind.Video => "play",
        _ => "bolt",
    };

    internal Task OpenVideo(string forWhat)
    {
        _videoFor = _videoFor == forWhat ? null : forWhat;
        Render();
        return Task.CompletedTask;
    }

    /// <summary>The video picker: the friends watch grammar (catalogue entries, or a Hypnotube
    /// number). The number box filters to digits as it is typed; nothing else crosses the wire.</summary>
    private Control VideoPicker(Func<LeashWatch, Task> send)
    {
        var sp = new StackPanel { Margin = new Thickness(20, 2, 0, 6), Tag = "leash-video-picker" };
        IReadOnlyList<(string Id, string Title)> list;
        try { list = FriendsInviteCodes.CatalogueWatches(); } catch { list = Array.Empty<(string, string)>(); }
        foreach (var (id, title) in list.Take(5))
        {
            var b = FriendsDrawer.Pill(title, LeashLook.ButtonBg, FriendsDrawer.Text, "leash-video-catalogue:" + id, FriendsDrawer.Line2);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Padding = new Thickness(8, 4, 8, 4);
            b.Margin = new Thickness(0, 0, 0, 3);
            b.Cursor = FriendsDrawer.Hand();
            b.Click += async (_, _) => await send(new LeashWatch("catalogue", id, title));
            sp.Children.Add(b);
        }
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        var box = new TextBox
        {
            FontFamily = FriendsDrawer.Mono,
            FontSize = 12.5,
            Foreground = FriendsDrawer.Text,
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x0A, 0x1E)),
            BorderBrush = FriendsDrawer.Line2,
            CaretBrush = FriendsDrawer.Lilac,
            Padding = new Thickness(6, 3, 6, 3),
            Tag = "leash-ht-box",
        };
        ToolTip.SetTip(box, Loc.Get("leash_video_ht_hint"));
        var go = FriendsDrawer.Pill(Loc.Get("leash_send"), FriendsDrawer.Gold, LeashLook.InkBrush, "leash-ht-send", FriendsDrawer.Gold);
        go.Margin = new Thickness(6, 0, 0, 0);
        go.Padding = new Thickness(10, 3, 10, 3);
        go.IsEnabled = false;
        // Text property, not TextChanged: Avalonia raises that one later (FriendsDrawer.Pickers HtBox).
        box.PropertyChanged += (_, e) =>
        {
            if (e.Property != TextBox.TextProperty) return;
            var n = global::ConditioningControlPanel.Controls.Friends.FriendsDrawerRules.NormaliseHtId(box.Text);
            if (n != box.Text) { box.Text = n; box.CaretIndex = n.Length; }
            go.IsEnabled = n.Length > 0;
        };
        go.Click += async (_, _) =>
        {
            var id = global::ConditioningControlPanel.Controls.Friends.FriendsDrawerRules.NormaliseHtId(box.Text);
            if (id.Length > 0) await send(new LeashWatch("ht", id, null));
        };
        g.Children.Add(box);
        Grid.SetColumn(go, 1);
        g.Children.Add(go);
        sp.Children.Add(g);
        return sp;
    }

    // ---- sends ------------------------------------------------------------------------

    internal async Task<LeashSendStatus> SendPunishAsync(PunishKind k, int size, LeashWatch? w)
    {
        var r = await Guarded(s => s.PunishAsync(_h.Who.Id, k, size, w));
        if (LeashUiRules.IsGood(r.Status)) { _sheet = null; _videoFor = null; }
        Word(r, Loc.GetF(r.Status == LeashSendStatus.Queued ? "leash_done_punish_queued" : "leash_done_punish", _h.Who.Name));
        return r.Status;
    }

    internal async Task<LeashSendStatus> SendAssignAsync(AssignKind k, int size, LeashWatch? w)
    {
        var r = await Guarded(s => s.AssignAsync(_h.Who.Id, k, size, w));
        if (LeashUiRules.IsGood(r.Status)) { _sheet = null; _videoFor = null; }
        Word(r, Honest("leash_done_assign"));
        return r.Status;
    }

    internal async Task<LeashSendStatus> SendRewardAsync(RewardKind k, string? stickerOrPoke, int? size)
    {
        var r = await Guarded(s => s.RewardAsync(_h.Who.Id, k, stickerOrPoke, size));
        if (LeashUiRules.IsGood(r.Status)) _sheet = null;
        Word(r, Honest("leash_done_reward"));
        return r.Status;
    }

    /// <summary>The line after a good send. With receipts the steps below say when they see it,
    /// so the line only says it went; without them it keeps its old wording.</summary>
    private string Honest(string oldKey) => ReceiptsOn ? Loc.Get("leash_res_sent") : Loc.GetF(oldKey, _h.Who.Name);

    /// <summary>One tug per friend every ten seconds, answered here before the wire.</summary>
    internal static LeashTugThrottle TugThrottle { get; set; } = new();

    internal async Task<LeashSendStatus> TugAsync()
    {
        if (!TugThrottle.TryTug(_h.Who.Id, DateTime.UtcNow))
        {
            var slow = new LeashSendResult(LeashSendStatus.TooFast);
            LeashFx.Denied();
            Word(slow, Loc.GetF("leash_done_tug", _h.Who.Name));
            return slow.Status;
        }
        LeashFx.Tug(this);
        LeashFx.Jingle();
        var r = await Guarded(s => s.TugAsync(_h.Who.Id), sentCue: false);
        Word(r, Honest("leash_done_tug"));
        return r.Status;
    }

    private async Task<LeashSendResult> Guarded(Func<ILeashService, Task<LeashSendResult>> call, bool sentCue = true)
    {
        try
        {
            var s = _svc();
            if (s == null) return new LeashSendResult(LeashSendStatus.Off);
            var r = await call(s);
            if (LeashUiRules.IsGood(r.Status)) { if (sentCue) LeashFx.Sent(); }
            else if (r.Status is LeashSendStatus.TooFast or LeashSendStatus.Dnd or LeashSendStatus.Failed) LeashFx.Denied();
            return r;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("[Leash] send failed: {E}", ex.Message);
            LeashFx.Denied();
            return new LeashSendResult(LeashSendStatus.Failed);
        }
    }

    private void Word(LeashSendResult r, string goodText)
    {
        bool good = LeashUiRules.IsGood(r.Status);
        string text = good ? goodText
            : r.Status == LeashSendStatus.Dnd && r.DndUntil is { } u ? Loc.GetF("leash_res_dnd_until", _h.Who.Name, LeashUiRules.DndUntilText(u))
            : Loc.Get(LeashUiRules.ResultKey(r.Status));
        _result = (text, good);
        _resultTimer?.Stop();
        _resultTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _resultTimer.Tick += (_, _) => { _resultTimer?.Stop(); _result = null; Render(); };
        _resultTimer.Start();
        Render();
    }
}
