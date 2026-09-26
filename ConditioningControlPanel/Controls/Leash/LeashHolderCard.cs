using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// THE LEASH CARD, holder side: pinned at the top of the friends drawer, one per account held.
/// Head (their avatar with the gold tag, "on your leash, day N", online), three figures (minutes
/// ring toward the day's goal, quests, lock time and tab when they have Chaster), the 7-day strip,
/// and four buttons: Assign, Reward, Punish, Tug. Assign / Reward / Punish open their preset
/// sheet in place, under the buttons. Punishments the leashed side's intensity does not allow are
/// HIDDEN (owner call), never greyed. Every send is worded under the sheet for a few seconds.
/// </summary>
public sealed class LeashHolderCard : Border
{
    private readonly Func<ILeashService?> _svc;
    private HeldLeash _h;
    private readonly Grid _root = new();
    private readonly StackPanel _body = new();
    private readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false };
    private FrameworkElement? _tag;

    /// <summary>"assign", "punish", "reward" or null.</summary>
    private string? _sheet;

    /// <summary>The punishment or assignment waiting for a video id, or null.</summary>
    private string? _videoFor;
    private int _videoCap = LeashVideoCap.Default;

    private (string Text, bool Good)? _result;
    private DispatcherTimer? _resultTimer;

    /// <summary>Raised when the holder asks to see the snap again.</summary>
    public event Action<HeldLeash>? ReplaySnapRequested;

    /// <summary>True for the leashed side's "what they see" preview: no tools, no buttons.</summary>
    private readonly bool _readOnly;

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
        Effect = FriendsLook.Glow(FriendsLook.Gold, 18, 0.18);
        _root.Children.Add(_body);
        _root.Children.Add(_fx);
        Child = _root;
        Render();
    }

    public string LeashedId => _h.Who.Id;
    internal HeldLeash Held => _h;
    internal string? OpenSheet => _sheet;
    internal string? ResultText => _result?.Text;

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
            var q = LeashLook.Wrap(FriendsLook.Label(Loc.GetF("leash_holder_dnd", _h.Who.Name, LeashUiRules.DndUntilText(until)),
                11.5, FriendsLook.LilacBrush));
            q.Margin = new Thickness(2, 8, 0, 0);
            q.Tag = "leash-holder-dnd";
            _body.Children.Add(q);
        }
        if (_readOnly)
        {
            var can = LeashLook.Wrap(FriendsLook.Label(Loc.Get("leash_preview_buttons"), 11, FriendsLook.DimBrush));
            can.Margin = new Thickness(2, 10, 0, 0);
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
            var line = LeashLook.Wrap(FriendsLook.Label(r.Text, 12, r.Good ? FriendsLook.MintBrush : FriendsLook.GoldBrush,
                FriendsLook.Display, FontWeights.Medium));
            line.Margin = new Thickness(2, 8, 0, 0);
            line.Tag = "leash-result";
            _body.Children.Add(line);
        }
    }

    private FrameworkElement Head(bool dnd)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var av = new Grid { Width = 48, Height = 56, Margin = new Thickness(0, 0, 10, 0) };
        var face = FriendsLook.Avatar(_h.Who.Name, _h.Who.AvatarUrl, 46, _h.Online);
        face.VerticalAlignment = VerticalAlignment.Top;
        av.Children.Add(face);
        _tag = LeashLook.HeartTag(22);
        _tag.HorizontalAlignment = HorizontalAlignment.Left;
        _tag.VerticalAlignment = VerticalAlignment.Bottom;
        _tag.Margin = new Thickness(-2, 0, 0, -4);
        av.Children.Add(_tag);
        LeashFx.Swing(_tag);
        g.Children.Add(av);

        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(FriendsLook.Label(_h.Who.Name, 18, FriendsLook.TextBrush, FriendsLook.Display, FontWeights.SemiBold));
        var d = FriendsLook.Label(Loc.GetF("leash_holder_day", _h.Day), 11.5, FriendsLook.GoldBrush, null, FontWeights.SemiBold);
        d.Tag = "leash-holder-day";
        who.Children.Add(d);
        var st = FriendsLook.Label(dnd ? Loc.Get("leash_state_quiet") : _h.Online ? Loc.Get("leash_state_online") : Loc.Get("leash_state_offline"),
            10.5, dnd ? FriendsLook.LilacBrush : _h.Online ? FriendsLook.MintBrush : FriendsLook.DimBrush, FriendsLook.Mono);
        st.Tag = "leash-holder-state";
        who.Children.Add(st);
        Grid.SetColumn(who, 1);
        g.Children.Add(who);

        if (_readOnly) return g;
        var tools = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        tools.Children.Add(LeashLook.Help(LeashExplainRole.Holder));
        var more = FriendsLook.Pill(new TextBlock { Text = "…", FontSize = 13, Foreground = FriendsLook.MutedBrush, HorizontalAlignment = HorizontalAlignment.Center },
            Brushes.Transparent, FriendsLook.MutedBrush, Brushes.Transparent, 11, new Thickness(0));
        more.Width = 22;
        more.Height = 22;
        more.Margin = new Thickness(4, 0, 0, 0);
        more.Tag = "leash-holder-more";
        more.Click += (_, _) =>
        {
            var m = Menu();
            m.PlacementTarget = more;
            m.Placement = PlacementMode.Bottom;
            m.IsOpen = true;
        };
        tools.Children.Add(more);
        Grid.SetColumn(tools, 2);
        g.Children.Add(tools);
        return g;
    }

    private ContextMenu Menu()
    {
        var m = new ContextMenu();
        var replay = new MenuItem { Header = Loc.Get("leash_menu_replay") };
        replay.Click += (_, _) => ReplaySnapRequested?.Invoke(_h);
        m.Items.Add(replay);
        var release = new MenuItem { Header = Loc.GetF("leash_menu_release", _h.Who.Name) };
        release.Click += async (_, _) =>
        {
            try { if (_svc() is { } s) await s.ReleaseAsync(_h.Who.Id); } catch { }
        };
        m.Items.Add(release);
        return m;
    }

    private FrameworkElement Figures()
    {
        var g = new UniformGrid { Columns = 3, Margin = new Thickness(0, 10, 0, 0) };
        var r = _h.Report;
        int goal = LeashUiRules.MinutesGoal(_h.Assignment);
        int minutes = r?.Minutes ?? 0;
        g.Children.Add(Fig(LeashLook.Ring(LeashUiRules.RingFraction(minutes, goal), r == null ? "-" : minutes.ToString(), FriendsLook.Pink, 48),
            Loc.Get("leash_fig_minutes"), null, "leash-fig-minutes"));
        g.Children.Add(Fig(Figure(r == null ? "-" : r.QuestsDone.ToString(), r == null ? "" : "/" + r.QuestsTotal, FriendsLook.TextBrush),
            Loc.Get("leash_fig_quests"), r == null ? null : Loc.GetF("leash_fig_streak", r.Streak), "leash-fig-quests"));
        if (r is { ChasterLinked: true })
        {
            var lockTxt = r.LockLeftSeconds is int s ? LeashUiRules.LockLeft(s) : "-";
            var tab = r.TabSeconds is int t ? LeashUiRules.Tab(t) : null;
            g.Children.Add(Fig(Figure(lockTxt, "", FriendsLook.GoldBrush, 17), Loc.Get("leash_fig_lock"),
                tab == null ? null : Loc.GetF("leash_fig_tab", tab), "leash-fig-lock", tab != null && r.TabSeconds > 0 ? FriendsLook.RedBrush : FriendsLook.MintBrush));
        }
        else
        {
            g.Children.Add(Fig(Figure(r == null ? "-" : r.Streak.ToString(), r == null ? "" : "d", FriendsLook.LilacBrush),
                Loc.Get("leash_fig_streak_label"), null, "leash-fig-streak"));
        }
        return g;
    }

    private static FrameworkElement Figure(string big, string small, Brush fg, double size = 21)
    {
        var t = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, FontFamily = FriendsLook.Mono, FontWeight = FontWeights.Bold };
        t.Inlines.Add(new System.Windows.Documents.Run(big) { FontSize = size, Foreground = fg });
        if (small.Length > 0) t.Inlines.Add(new System.Windows.Documents.Run(small) { FontSize = size * 0.7, Foreground = FriendsLook.DimBrush });
        return new Grid { Height = 48, Children = { new Border { VerticalAlignment = VerticalAlignment.Center, Child = t } } };
    }

    private static FrameworkElement Fig(FrameworkElement visual, string label, string? extra, string tag, Brush? extraBrush = null)
    {
        var box = new Border
        {
            Background = LeashLook.ShadeBrush,
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
        l.Margin = new Thickness(0, 4, 0, 0);
        sp.Children.Add(l);
        if (extra != null)
        {
            var e = LeashLook.Caption(extra, extraBrush ?? FriendsLook.MutedBrush);
            e.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(e);
        }
        box.Child = sp;
        return box;
    }

    private FrameworkElement AssignmentLine(Assignment a)
    {
        string what = AssignText(a.Kind, a.Size, a.Watch);
        var (key, brush) = a.Status switch
        {
            AssignStatus.Done => ("leash_task_done", FriendsLook.MintBrush),
            AssignStatus.Missed => ("leash_task_missed", FriendsLook.RedBrush),
            _ => ("leash_task_open", FriendsLook.GoldBrush),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 10, 0, 0), Tag = "leash-holder-task" };
        var ic = LeashLook.Icon("task", brush, 13);
        ic.Margin = new Thickness(0, 0, 6, 0);
        row.Children.Add(ic);
        row.Children.Add(FriendsLook.Label(Loc.GetF(key, what), 11.5, brush, null, FontWeights.SemiBold));
        return row;
    }

    internal static string AssignText(AssignKind k, int size, LeashWatch? w) => k switch
    {
        AssignKind.Minutes => Loc.GetF("leash_assign_minutes_n", size),
        AssignKind.Quests => Loc.GetF("leash_assign_quests_n", size),
        _ => Loc.Get("leash_assign_video_n"),
    };

    private FrameworkElement Week()
    {
        var g = new UniformGrid { Columns = 7, Margin = new Thickness(0, 10, 0, 0), Tag = "leash-week" };
        var days = _h.Week.Count > 0 ? _h.Week : Array.Empty<WeekDay>();
        foreach (var d in days.TakeLast(7))
            g.Children.Add(LeashLook.WeekCell(new WeekDayView(d.Mark, LeashUiRules.DayLetter(d.Day))));
        return g;
    }

    private FrameworkElement Buttons(bool dnd)
    {
        var g = new UniformGrid { Columns = 4, Margin = new Thickness(0, 10, 0, 0) };
        Add(g, "assign", Loc.Get("leash_btn_assign"), LeashLook.Tone.Gold, "task", !dnd);
        Add(g, "reward", Loc.Get("leash_btn_reward"), LeashLook.Tone.Mint, "star", true);
        Add(g, "punish", Loc.Get("leash_btn_punish"), LeashLook.Tone.Red, "bolt", !dnd);
        var tug = LeashLook.Chunky(Loc.Get("leash_btn_tug"), LeashLook.Tone.Ghost, "hand", stacked: true, size: 12);
        tug.Tag = "leash-btn:tug";
        tug.Margin = new Thickness(3, 0, 0, 0);
        tug.IsEnabled = !dnd;
        tug.Click += async (_, _) => await TugAsync();
        g.Children.Add(tug);
        return g;
    }

    private void Add(UniformGrid g, string id, string text, LeashLook.Tone tone, string icon, bool enabled)
    {
        var b = LeashLook.Chunky(text, tone, icon, stacked: true, size: 12);
        b.Tag = "leash-btn:" + id + (_sheet == id ? ":open" : "");
        b.Margin = new Thickness(g.Children.Count == 0 ? 0 : 3, 0, 3, 0);
        b.IsEnabled = enabled;
        if (_sheet == id) b.BorderBrush = FriendsLook.TextBrush;
        b.Click += (_, _) => ToggleSheet(id);
        g.Children.Add(b);
    }

    internal void ToggleSheet(string id)
    {
        _sheet = _sheet == id ? null : id;
        _videoFor = null;
        Render();
        if (_sheet != null && _body.Children.Count > 0 && LeashFx.Amount > 0)
            Services.MotionFx.StaggerIn(new[] { (FrameworkElement)_body.Children[_body.Children.Count - (_result != null ? 2 : 1)] });
    }

    // ---- sheets -----------------------------------------------------------------------

    private Border Sheet(string title, string tag)
    {
        var b = new Border
        {
            Background = FriendsLook.Frozen(FriendsLook.Rgb(0x24, 0x18, 0x38)),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10),
            Tag = tag,
        };
        var sp = new StackPanel();
        var head = LeashLook.Caption(title);
        head.Margin = new Thickness(0, 0, 0, 6);
        head.TextTrimming = TextTrimming.CharacterEllipsis;
        sp.Children.Add(head);
        b.Child = sp;
        return b;
    }

    private static StackPanel Rows(Border sheet) => (StackPanel)sheet.Child;

    /// <summary>One sheet row: a name on the left and its size chips on the right.</summary>
    private FrameworkElement SizeRow(string label, string icon, Brush accent, IEnumerable<(string Text, string Tag, Func<Task> Send)> chips)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ic = LeashLook.Icon(icon, accent, 13);
        ic.Margin = new Thickness(0, 0, 6, 0);
        name.Children.Add(ic);
        name.Children.Add(FriendsLook.Label(label, 12.5, FriendsLook.TextBrush, FriendsLook.Display, FontWeights.Medium));
        g.Children.Add(name);
        var chipsPanel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, tag, send) in chips)
        {
            var c = FriendsLook.Pill(new TextBlock { Text = text, FontFamily = FriendsLook.Mono, FontSize = 11.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                FriendsLook.ButtonBrush, FriendsLook.TextBrush, FriendsLook.Line2Brush, 8, new Thickness(7, 4, 7, 4));
            c.MinWidth = 40;
            c.Margin = new Thickness(4, 0, 0, 0);
            c.Tag = tag;
            c.Click += async (_, _) =>
            {
                LeashFx.Pop(c);
                LeashFx.Sparks(_fx, c, (int)(8 * LeashFx.Amount), ((SolidColorBrush)accent).Color, FriendsLook.Gold);
                await send();
            };
            chipsPanel.Children.Add(c);
        }
        Grid.SetColumn(chipsPanel, 1);
        g.Children.Add(chipsPanel);
        return g;
    }

    private FrameworkElement PunishSheet()
    {
        var levelName = Loc.Get(LeashUiRules.Key(_h.Intensity));
        var sheet = Sheet(Loc.GetF("leash_sheet_punish", Math.Min(_h.PunishedToday, 3), levelName), "leash-sheet:punish");
        var rows = Rows(sheet);
        if (_h.PunishedToday >= 3)
        {
            rows.Children.Add(LeashLook.Wrap(FriendsLook.Label(Loc.Get("leash_sheet_punish_cap"), 12, FriendsLook.MutedBrush)));
            return sheet;
        }
        if (_h.Pending.Count > 0)
        {
            var w = LeashLook.Wrap(FriendsLook.Label(Loc.GetF("leash_sheet_waiting", _h.Pending.Count), 11, FriendsLook.GoldBrush));
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
            rows.Children.Add(SizeRow(Loc.Get(LeashUiRules.Key(k)), PunishIcon(k), FriendsLook.RedBrush, chips));
            if (k == PunishKind.Video && _videoFor == "punish")
            {
                _videoCap = Math.Min(_videoCap, _h.VideoMax);
                var cap = new LeashCapSlider(Loc.Get("leash_video_cap_label"), _videoCap, "leash-video-cap")
                {
                    Ceiling = _h.VideoMax,
                    Margin = new Thickness(20, 2, 0, 6),
                    ToolTip = Loc.Get("leash_video_max_hint"),
                };
                cap.Committed += v => _videoCap = v;
                rows.Children.Add(cap);
                rows.Children.Add(VideoPicker(w => SendPunishAsync(PunishKind.Video, _videoCap, w)));
            }
        }
        return sheet;
    }

    private FrameworkElement AssignSheet()
    {
        var sheet = Sheet(Loc.GetF("leash_sheet_assign", _h.Who.Name), "leash-sheet:assign");
        var rows = Rows(sheet);
        if (_h.Assignment is { Status: AssignStatus.Open } a)
        {
            var w = LeashLook.Wrap(FriendsLook.Label(Loc.GetF("leash_sheet_replaces", AssignText(a.Kind, a.Size, a.Watch)), 11, FriendsLook.GoldBrush));
            w.Margin = new Thickness(0, 0, 0, 4);
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
                FriendsLook.GoldBrush, chips));
            if (k == AssignKind.Video && _videoFor == "assign") rows.Children.Add(VideoPicker(w => SendAssignAsync(AssignKind.Video, 1, w)));
        }
        return sheet;
    }

    private FrameworkElement RewardSheet()
    {
        var sheet = Sheet(Loc.Get("leash_sheet_reward"), "leash-sheet:reward");
        var rows = Rows(sheet);

        // Stickers: the discs themselves are the buttons.
        var stickers = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 6) };
        int i = 0;
        foreach (var id in LeashUiRules.Stickers)
        {
            var sid = id;
            var disc = LeashLook.StickerDisc(id, 32, i++);
            var b = FriendsLook.Pill(disc, Brushes.Transparent, FriendsLook.TextBrush, Brushes.Transparent, 18, new Thickness(2));
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Tag = "leash-reward:sticker:" + id;
            b.ToolTip = Loc.Get("leash_rew_sticker_" + id);
            b.Click += async (_, _) =>
            {
                LeashFx.Pop(b);
                LeashFx.Sparks(_fx, b, (int)(10 * LeashFx.Amount), FriendsLook.Gold, FriendsLook.Mint);
                await SendRewardAsync(RewardKind.Sticker, sid, null);
            };
            stickers.Children.Add(b);
        }
        rows.Children.Add(stickers);

        rows.Children.Add(SizeRow(Loc.Get("leash_rew_praise"), "heart", FriendsLook.PinkBrush,
            LeashUiRules.Praise.Select(p => (Loc.Get("leash_praise_" + p), "leash-reward:praise:" + p, (Func<Task>)(() => SendRewardAsync(RewardKind.Praise, p, null))))));
        rows.Children.Add(SizeRow(Loc.Get("leash_rew_pardon"), "scissors", FriendsLook.LilacBrush,
            new[] { (Loc.Get("leash_give"), "leash-reward:pardon", (Func<Task>)(() => SendRewardAsync(RewardKind.Pardon, null, null))) }));
        if (_h.Report?.ChasterLinked == true)
        {
            rows.Children.Add(SizeRow(Loc.Get("leash_rew_credit"), "lock", FriendsLook.MintBrush,
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

    private Task OpenVideo(string forWhat)
    {
        _videoFor = _videoFor == forWhat ? null : forWhat;
        Render();
        return Task.CompletedTask;
    }

    /// <summary>The video picker: the friends watch grammar (catalogue entries, or a Hypnotube
    /// number). The number box filters to digits as it is typed; nothing else crosses the wire.</summary>
    private FrameworkElement VideoPicker(Func<LeashWatch, Task> send)
    {
        var sp = new StackPanel { Margin = new Thickness(20, 2, 0, 6), Tag = "leash-video-picker" };
        foreach (var (id, title) in CatalogueWatches.List().Take(5))
        {
            var b = FriendsLook.Pill(title, FriendsLook.ButtonBrush, FriendsLook.TextBrush, FriendsLook.Line2Brush, 8, new Thickness(8, 4, 8, 4));
            b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(0, 0, 0, 3);
            b.Click += async (_, _) => await send(new LeashWatch("catalogue", id, title));
            sp.Children.Add(b);
        }
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = new TextBox
        {
            MaxLength = 8,
            FontFamily = FriendsLook.Mono,
            FontSize = 12.5,
            Foreground = FriendsLook.TextBrush,
            Background = FriendsLook.Frozen(FriendsLook.Ground),
            BorderBrush = FriendsLook.Line2Brush,
            CaretBrush = FriendsLook.LilacBrush,
            Padding = new Thickness(6, 3, 6, 3),
            ToolTip = Loc.Get("leash_video_ht_hint"),
            Tag = "leash-ht-box",
        };
        var go = FriendsLook.Pill(Loc.Get("leash_send"), FriendsLook.GoldBrush, LeashLook.InkBrush, FriendsLook.GoldBrush, 8, new Thickness(10, 3, 10, 3), FriendsLook.GoldBrush);
        go.Margin = new Thickness(6, 0, 0, 0);
        go.IsEnabled = false;
        box.TextChanged += (_, _) =>
        {
            var n = FriendsDrawerRules.NormaliseHtId(box.Text);
            if (n != box.Text) { box.Text = n; box.CaretIndex = n.Length; }
            go.IsEnabled = n.Length > 0;
        };
        go.Click += async (_, _) =>
        {
            var id = FriendsDrawerRules.NormaliseHtId(box.Text);
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
        Word(r, Loc.GetF("leash_done_assign", _h.Who.Name));
        return r.Status;
    }

    internal async Task<LeashSendStatus> SendRewardAsync(RewardKind k, string? stickerOrPoke, int? size)
    {
        var r = await Guarded(s => s.RewardAsync(_h.Who.Id, k, stickerOrPoke, size));
        if (LeashUiRules.IsGood(r.Status)) _sheet = null;
        Word(r, Loc.GetF("leash_done_reward", _h.Who.Name));
        return r.Status;
    }

    internal async Task<LeashSendStatus> TugAsync()
    {
        LeashFx.Tug(this);
        LeashFx.Jingle();
        var r = await Guarded(s => s.TugAsync(_h.Who.Id));
        Word(r, Loc.GetF("leash_done_tug", _h.Who.Name));
        return r.Status;
    }

    private async Task<LeashSendResult> Guarded(Func<ILeashService, Task<LeashSendResult>> call)
    {
        try
        {
            var s = _svc();
            if (s == null) return new LeashSendResult(LeashSendStatus.Off);
            var r = await call(s);
            if (LeashUiRules.IsGood(r.Status)) LeashFx.Sent();
            return r;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[Leash] send failed: {E}", ex.Message);
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
