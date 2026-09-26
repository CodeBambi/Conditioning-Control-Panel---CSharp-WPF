using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// THE ASK, leashed side: a full card, never a toast. "Juno wants to hold your leash", then the
/// explainer slot (four pictures and two lines; a later lane owns what goes in it, this card only
/// keeps the room), then the intensity switch with the punishments it allows lighting up as it
/// moves, then "Not now" and "Put it on". The mint panel with the scissors is always in view.
/// </summary>
public sealed class LeashAskCard : Border
{
    private readonly LeashOffer _offer;
    private readonly Func<ILeashService?> _svc;
    private LeashIntensity _level = LeashIntensity.Standard;
    private readonly StackPanel _body = new();
    private readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false };
    private Border? _switchHost;
    private WrapPanel? _allowed;
    private bool _busy;

    /// <summary>Raised after the answer went out: true = accepted (the host plays the snap).</summary>
    public event Action<LeashOffer, bool, LeashIntensity>? Answered;

    /// <summary>Raised on "Not now"-less close (Escape): the offer stays for later.</summary>
    public event Action? Dismissed;

    /// <summary>The explainer's room: 4 panels and 2 lines sit here, above the switch. The
    /// explain lane replaces <see cref="Border.Child"/>; until then it holds a plain version.</summary>
    public Border ExplainerSlot { get; } = new() { Tag = "leash-explainer-slot" };

    /// <summary>The explain lane's builder for the slot. Null = the plain four panels.</summary>
    public static Func<LeashOffer, FrameworkElement>? ExplainerFactory { get; set; }

    public LeashAskCard(LeashOffer offer, Func<ILeashService?> service)
    {
        _offer = offer;
        _svc = service;
        Width = 380;
        Background = FriendsLook.GlassBrush;
        BorderBrush = LeashLook.GoldDeepBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(18);
        Tag = "leash-ask:" + offer.From.Id;
        Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 34, ShadowDepth = 10, Direction = 270, Opacity = 0.6 };
        var root = new Grid();
        root.Children.Add(_body);
        root.Children.Add(_fx);
        Child = root;
        Build();
    }

    internal LeashIntensity Level => _level;

    /// <summary>"Put it on". The explainer (LeashAskIntro) may take over its IsEnabled; this card
    /// only ever turns it off while the answer is in flight, and back on after.</summary>
    public Button? PutItOnButton { get; private set; }

    private void Build()
    {
        _body.Children.Clear();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var av = FriendsLook.Avatar(_offer.From.Name, _offer.From.AvatarUrl, 60);
        av.Margin = new Thickness(0, 0, 14, 0);
        head.Children.Add(av);
        var t = new TextBlock { FontFamily = FriendsLook.Display, FontSize = 21, FontWeight = FontWeights.SemiBold, Foreground = FriendsLook.TextBrush, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        t.Inlines.Add(new System.Windows.Documents.Run(_offer.From.Name) { Foreground = FriendsLook.GoldBrush });
        t.Inlines.Add(new System.Windows.Documents.Run(" " + Loc.Get("leash_ask_title")));
        t.Tag = "leash-ask-title";
        Grid.SetColumn(t, 1);
        head.Children.Add(t);
        var help = LeashLook.Help(LeashExplainRole.Ask);
        help.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(help, 2);
        head.Children.Add(help);
        _body.Children.Add(head);

        ExplainerSlot.Margin = new Thickness(0, 14, 0, 0);
        ExplainerSlot.Child = ExplainerFactory?.Invoke(_offer) ?? PlainExplainer();
        _body.Children.Add(ExplainerSlot);

        var cap = LeashLook.Caption(Loc.Get("leash_ask_level"));
        cap.Margin = new Thickness(2, 14, 0, 5);
        _body.Children.Add(cap);
        _switchHost = new Border();
        _body.Children.Add(_switchHost);
        _allowed = new WrapPanel { Margin = new Thickness(0, 8, 0, 0), Tag = "leash-ask-allowed" };
        _body.Children.Add(_allowed);
        PaintLevel();

        var acts = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        acts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        acts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        var no = LeashLook.Chunky(Loc.Get("leash_ask_no"), LeashLook.Tone.Ghost, size: 15);
        no.Tag = "leash-ask-no";
        no.Margin = new Thickness(0, 0, 5, 0);
        no.Click += async (_, _) => await AnswerAsync(false);
        var yes = LeashLook.Chunky(Loc.Get("leash_ask_yes"), LeashLook.Tone.Gold, "link", size: 15);
        yes.Tag = "leash-ask-yes";
        yes.Margin = new Thickness(5, 0, 0, 0);
        yes.Click += async (_, _) => await AnswerAsync(true);
        PutItOnButton = yes;
        Grid.SetColumn(yes, 1);
        acts.Children.Add(no);
        acts.Children.Add(yes);
        _body.Children.Add(acts);
    }

    /// <summary>Four pictures with a few words each, and two plain lines. Placeholder until the
    /// explain lane lands; it holds the same room the real explainer will.</summary>
    private static FrameworkElement PlainExplainer()
    {
        var sp = new StackPanel { Tag = "leash-explainer-plain" };
        var grid = new UniformGrid { Columns = 2 };
        grid.Children.Add(Panel("eye", FriendsLook.LilacBrush, Loc.Get("leash_ask_p_sees"), false));
        grid.Children.Add(Panel("scale", FriendsLook.GoldBrush, Loc.Get("leash_ask_p_rewards"), false));
        grid.Children.Add(Panel("task", FriendsLook.PinkBrush, Loc.Get("leash_ask_p_tasks"), false));
        grid.Children.Add(Panel("scissors", FriendsLook.MintBrush, Loc.Get("leash_ask_p_cut"), true));
        sp.Children.Add(grid);
        foreach (var key in new[] { "leash_ask_b_level", "leash_ask_b_quiet" })
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 6, 0, 0) };
            row.Children.Add(FriendsLook.Label("•", 12, FriendsLook.DimBrush));
            var l = LeashLook.Wrap(FriendsLook.Label(Loc.Get(key), 12, FriendsLook.MutedBrush));
            l.Margin = new Thickness(6, 0, 0, 0);
            l.MaxWidth = 320;
            row.Children.Add(l);
            sp.Children.Add(row);
        }
        return sp;
    }

    private static FrameworkElement Panel(string icon, Brush accent, string text, bool cut)
    {
        var b = new Border
        {
            Background = cut ? LeashLook.CutPanelBrush : FriendsLook.Frozen(FriendsLook.Rgb(0x24, 0x18, 0x38)),
            BorderBrush = cut ? FriendsLook.Frozen(LeashLook.MintDeep) : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(3),
            MinHeight = 78,
            Tag = cut ? "leash-ask-panel-cut" : "leash-ask-panel",
        };
        var sp = new StackPanel();
        var ic = LeashLook.Icon(icon, accent, 26);
        ic.HorizontalAlignment = HorizontalAlignment.Left;
        sp.Children.Add(ic);
        var l = LeashLook.Wrap(FriendsLook.Label(text, 12, cut ? FriendsLook.MintBrush : FriendsLook.TextBrush, null, FontWeights.SemiBold));
        l.Margin = new Thickness(0, 6, 0, 0);
        sp.Children.Add(l);
        b.Child = sp;
        return b;
    }

    private void PaintLevel()
    {
        if (_switchHost == null || _allowed == null) return;
        _switchHost.Child = LeashLook.Segmented(
            new[] { Loc.Get("leash_level_soft"), Loc.Get("leash_level_standard"), Loc.Get("leash_level_strict") },
            (int)_level, i => SetLevel((LeashIntensity)i), "leash-ask-level", 13);
        _allowed.Children.Clear();
        foreach (var k in LeashUiRules.PunishOrder)
        {
            bool on = LeashUiRules.Allowed(k, _level);
            var chip = new Border
            {
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 6, 6),
                BorderThickness = new Thickness(1),
                BorderBrush = on ? FriendsLook.Frozen(FriendsLook.Rgb(0x6B, 0x27, 0x48)) : FriendsLook.Frozen(FriendsLook.Rgb(0x3A, 0x2B, 0x54)),
                Background = on ? FriendsLook.Frozen(FriendsLook.Rgb(0x3A, 0x1A, 0x33)) : FriendsLook.Frozen(FriendsLook.Rgb(0x24, 0x18, 0x38)),
                Tag = "leash-allow:" + k.ToString().ToLowerInvariant() + (on ? ":on" : ""),
                Child = FriendsLook.Label(Loc.Get(LeashUiRules.Key(k)), 11.5, on ? FriendsLook.TextBrush : FriendsLook.DimBrush, null, FontWeights.SemiBold),
            };
            if (!on) chip.BorderBrush = FriendsLook.Frozen(FriendsLook.Rgb(0x3A, 0x2B, 0x54));
            _allowed.Children.Add(chip);
        }
    }

    internal void SetLevel(LeashIntensity level)
    {
        bool up = (int)level > (int)_level;
        _level = level;
        PaintLevel();
        if (up && _allowed != null)
            foreach (UIElement c in _allowed.Children)
                if (c is Border b && b.Tag is string s && s.EndsWith(":on")) LeashFx.Pop(b);
    }

    internal async Task AnswerAsync(bool accept)
    {
        if (_busy) return;
        _busy = true;
        bool wasEnabled = PutItOnButton?.IsEnabled ?? true;
        if (PutItOnButton != null) PutItOnButton.IsEnabled = false;
        bool ok = false;
        try { if (_svc() is { } s) ok = await s.AnswerAsync(_offer.From.Id, accept, _level); }
        catch (Exception ex) { App.Logger?.Debug("[Leash] answer failed: {E}", ex.Message); }
        _busy = false;
        if (PutItOnButton != null) PutItOnButton.IsEnabled = wasEnabled;
        Answered?.Invoke(_offer, accept && ok, _level);
    }

    internal void Dismiss() => Dismissed?.Invoke();
}
