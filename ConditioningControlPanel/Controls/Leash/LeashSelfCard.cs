using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// THE LEASHED SIDE'S OWN CARD, at the top of the friends drawer while someone holds the leash.
/// The scissors come first and are one click (never priced, never gated, no "are you sure").
/// Then what is waiting (punishments, today's task, pardons), the intensity switch (only this
/// side sets it), Do not disturb, the sticker shelf, and a "what they see" preview that draws
/// the holder's card from this side's own figures.
/// </summary>
public sealed class LeashSelfCard : Border
{
    private readonly Func<ILeashService?> _svc;
    private MyLeash _me;
    private readonly Grid _root = new();
    private readonly StackPanel _body = new();
    private readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false };
    private bool _preview;

    /// <summary>Raised the moment the cut is pressed (before the server answers), so the host
    /// can play the calm release and drop the card.</summary>
    public event Action? CutPressed;

    public LeashSelfCard(MyLeash me, Func<ILeashService?> service)
    {
        _me = me;
        _svc = service;
        Background = LeashLook.SelfCardBrush;
        BorderBrush = FriendsLook.Frozen(Color.FromArgb(0x88, 0xFF, 0x5F, 0xB4));
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(14);
        Padding = new Thickness(12);
        Margin = new Thickness(0, 4, 0, 6);
        Tag = "leash-self-card";
        Effect = FriendsLook.Glow(FriendsLook.Pink, 18, 0.16);
        _root.Children.Add(_body);
        _root.Children.Add(_fx);
        Child = _root;
        Render();
    }

    internal MyLeash Me => _me;
    internal bool PreviewOpen => _preview;

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

    private FrameworkElement Head()
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var av = FriendsLook.Avatar(_me.Holder.Name, _me.Holder.AvatarUrl, 40);
        av.Margin = new Thickness(0, 0, 10, 0);
        g.Children.Add(av);

        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var t = new TextBlock { FontFamily = FriendsLook.Display, FontSize = 16, Foreground = FriendsLook.TextBrush, TextTrimming = TextTrimming.CharacterEllipsis };
        t.Inlines.Add(new System.Windows.Documents.Run(_me.Holder.Name) { Foreground = FriendsLook.GoldBrush, FontWeight = FontWeights.SemiBold });
        t.Inlines.Add(new System.Windows.Documents.Run(" " + Loc.Get("leash_self_holds")));
        t.Tag = "leash-self-title";
        who.Children.Add(t);
        var chainLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        var chain = LeashLook.Chain(44, 8);
        chain.VerticalAlignment = VerticalAlignment.Center;
        chainLine.Children.Add(chain);
        var day = FriendsLook.Label(Loc.GetF("leash_self_day", _me.Day), 11, FriendsLook.PinkBrush, FriendsLook.Mono);
        day.Margin = new Thickness(6, 0, 0, 0);
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
    private FrameworkElement CutButton()
    {
        var b = LeashLook.Chunky(Loc.Get("leash_cut"), LeashLook.Tone.Mint, "scissors", stacked: false, size: 14);
        b.Margin = new Thickness(0, 10, 0, 0);
        b.Tag = "leash-cut";
        b.ToolTip = Loc.Get("leash_cut_tip");
        b.Click += async (_, _) => await CutAsync();
        return b;
    }

    /// <summary>Cuts. Never refused: the host drops the card at once.</summary>
    internal async Task CutAsync()
    {
        LeashFx.SparksAt(_fx, new Point(ActualWidth / 2, 70), (int)(14 * LeashFx.Amount), 80, FriendsLook.Mint, FriendsLook.Lilac);
        try { CutPressed?.Invoke(); } catch { }
        try { if (_svc() is { } s) await s.CutAsync(); }
        catch (Exception ex) { App.Logger?.Debug("[Leash] cut call failed (service retries): {E}", ex.Message); }
    }

    private FrameworkElement Waiting()
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Tag = "leash-self-waiting" };
        int gates = _me.Pending.Count(p => p.Kind != PunishKind.Chaster);
        if (gates > 0) sp.Children.Add(Line("bolt", FriendsLook.RedBrush, Loc.GetF("leash_self_pending", gates)));
        if (_me.Assignment is { } a)
        {
            string what = LeashHolderCard.AssignText(a.Kind, a.Size, a.Watch);
            var (key, brush) = a.Status switch
            {
                AssignStatus.Done => ("leash_self_task_done", FriendsLook.MintBrush),
                AssignStatus.Missed => ("leash_self_task_missed", FriendsLook.DimBrush),
                _ => ("leash_self_task_open", FriendsLook.GoldBrush),
            };
            sp.Children.Add(Line("task", brush, Loc.GetF(key, what, _me.Holder.Name)));
        }
        if (_me.Pardons > 0) sp.Children.Add(Line("scissors", FriendsLook.LilacBrush, Loc.GetF("leash_self_pardons", _me.Pardons)));
        if (sp.Children.Count == 0) sp.Children.Add(Line("heart", FriendsLook.MutedBrush, Loc.Get("leash_self_clear")));
        return sp;
    }

    private static FrameworkElement Line(string icon, Brush brush, string text)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 2, 0, 2) };
        var ic = LeashLook.Icon(icon, brush, 13);
        ic.Margin = new Thickness(0, 0, 7, 0);
        row.Children.Add(ic);
        row.Children.Add(FriendsLook.Label(text, 12, brush, null, FontWeights.SemiBold));
        return row;
    }

    private FrameworkElement Switches(DateTimeOffset now)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var h1 = LeashLook.Caption(Loc.Get("leash_self_level"));
        h1.Margin = new Thickness(2, 0, 0, 4);
        sp.Children.Add(h1);
        sp.Children.Add(LeashLook.Segmented(
            new[] { Loc.Get("leash_level_soft"), Loc.Get("leash_level_standard"), Loc.Get("leash_level_strict") },
            (int)_me.Intensity,
            i => _ = SetLevelAsync((LeashIntensity)i),
            "leash-level"));

        bool dnd = LeashUiRules.DndOn(_me.DndUntil, now);
        var h2 = LeashLook.Caption(dnd && _me.DndUntil is { } u ? Loc.GetF("leash_self_quiet_until", LeashUiRules.DndUntilText(u)) : Loc.Get("leash_self_quiet"),
            dnd ? FriendsLook.LilacBrush : null);
        h2.Margin = new Thickness(2, 8, 0, 4);
        h2.Tag = "leash-self-quiet";
        sp.Children.Add(h2);
        sp.Children.Add(LeashLook.Segmented(
            new[] { Loc.Get("leash_dnd_off"), Loc.Get("leash_dnd_1h"), Loc.Get("leash_dnd_4h"), Loc.Get("leash_dnd_today") },
            dnd ? -1 : 0,
            i => _ = SetDndAsync(i switch { 1 => LeashDnd.OneHour, 2 => LeashDnd.FourHours, 3 => LeashDnd.Today, _ => LeashDnd.Off }),
            "leash-dnd", 11.5));

        var cap = new LeashCapSlider(Loc.Get("leash_video_max"), _me.VideoMax, "leash-video-max")
        {
            Margin = new Thickness(2, 10, 2, 0),
            ToolTip = Loc.Get("leash_video_max_hint"),
        };
        cap.Committed += m => _ = SetVideoMaxAsync(m);
        sp.Children.Add(cap);

        var hold = LeashLook.Caption(Loc.GetF("leash_self_hold_hint", LeashHoldToCut.KeyLabel(App.Settings?.Current?.PanicKey)), FriendsLook.MutedBrush);
        hold.Margin = new Thickness(2, 10, 0, 0);
        hold.TextWrapping = TextWrapping.Wrap;
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

    private FrameworkElement Shelf()
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
            d.ToolTip = Loc.GetF("leash_sticker_from", s.From.Name);
            wrap.Children.Add(d);
        }
        sp.Children.Add(wrap);
        return sp;
    }

    private FrameworkElement PreviewToggle()
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var ic = LeashLook.Icon("eye", FriendsLook.LilacBrush, 13);
        ic.Margin = new Thickness(0, 0, 6, 0);
        content.Children.Add(ic);
        content.Children.Add(FriendsLook.Label(Loc.GetF(_preview ? "leash_self_preview_hide" : "leash_self_preview", _me.Holder.Name),
            12, FriendsLook.LilacBrush, FriendsLook.Display));
        var b = FriendsLook.Pill(content, Brushes.Transparent, FriendsLook.LilacBrush, FriendsLook.Line2Brush, 10, new Thickness(8, 5, 8, 5));
        b.Margin = new Thickness(0, 10, 0, 0);
        b.Tag = "leash-preview-toggle";
        b.Click += (_, _) => { _preview = !_preview; Render(); };
        return b;
    }

    /// <summary>The holder's card, drawn read-only from this side's own report. The strip is the
    /// server's; this side does not have it, so the preview says so instead of guessing.</summary>
    private FrameworkElement Preview()
    {
        DayReport? r = null;
        try { r = LeashLocator.LocalReport(); } catch { }
        var me = new LeashPerson("me", Loc.Get("leash_you"), null);
        var held = new HeldLeash(me, true, _me.Intensity, _me.Since, _me.Day, _me.DndUntil, r,
            Array.Empty<WeekDay>(), _me.Pending, _me.Assignment, 0);
        var card = new LeashHolderCard(held, () => null, readOnly: true) { IsHitTestVisible = false, Opacity = 0.92, Margin = new Thickness(0, 8, 0, 0) };
        card.Tag = "leash-preview";
        var box = new StackPanel();
        box.Children.Add(card);
        var note = LeashLook.Wrap(FriendsLook.Label(Loc.GetF("leash_self_preview_note", _me.Holder.Name), 10.5, FriendsLook.DimBrush));
        note.Margin = new Thickness(2, 4, 0, 0);
        box.Children.Add(note);
        return box;
    }
}
