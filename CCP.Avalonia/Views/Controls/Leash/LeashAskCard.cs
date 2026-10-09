// PORTED from WPF 7.1.5 Controls/Leash/LeashAskCard.cs + LeashSurfaces.ShowAsk / LeashOverlayWindow:
// "{name} wants to hold your leash" with the four plain panels (sees, rewards, tasks, cut), how
// strict (only this side picks), which punishments that level allows, Not now / Put it on, and the
// reason when an answer did not go through. Shown in a borderless overlay window over the owner;
// Escape is "later". No free text (Leash CONTRACT).
// Juice as WPF: LeashFx.Ask when it lands, a pop on newly allowed chips, Denied on a refused answer,
// and the snap (LeashSnapCard) after Put it on, through the shared LeashOverlay. The explainer
// (LeashExplainCard, 2 x 2) above the switch, the "?" (Ask), and the "read first" gate on Put it on.
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public sealed class LeashAskCard : Border
{
    private readonly LeashOffer _offer;
    private readonly Func<ILeashService?> _svc;
    private LeashIntensity _level = LeashIntensity.Standard;
    private readonly Border _switchHost = new();
    private readonly WrapPanel _allowed = new() { Margin = new Thickness(0, 8, 0, 0), Tag = "leash-ask-allowed" };
    private readonly TextBlock _error;
    private readonly Button _yes;

    /// <summary>offer, accepted, level: raised once an answer went through.</summary>
    public event Action<LeashOffer, bool, LeashIntensity>? Answered;
    public event Action? Dismissed;

    public LeashAskCard(LeashOffer offer, Func<ILeashService?> service)
    {
        _offer = offer;
        _svc = service;
        Background = LeashLook.GlassBrush;
        BorderBrush = LeashLook.GoldDeepBrush;
        BorderThickness = new Thickness(1.5);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(20, 18, 20, 16);
        Width = 380;
        BoxShadow = BoxShadows.Parse("0 0 28 0 #99000000");
        Tag = "leash-ask:" + offer.From.Id;

        var sp = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var av = FriendsDrawer.Avatar(offer.From.Name, 44, null, offer.From.AvatarUrl);
        av.Margin = new Thickness(0, 0, 10, 0);
        DockPanel.SetDock(av, Dock.Left);
        head.Children.Add(av);
        var t = new TextBlock { FontFamily = FriendsDrawer.Display, FontSize = 18, Foreground = FriendsDrawer.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Tag = "leash-ask-title" };
        t.Inlines!.Add(new Run(offer.From.Name) { Foreground = FriendsDrawer.Gold, FontWeight = FontWeight.SemiBold });
        t.Inlines.Add(new Run(" " + Loc.Get("leash_ask_title")));
        var help = LeashLook.Help(LeashExplainRole.Ask);
        help.VerticalAlignment = VerticalAlignment.Top;
        help.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(help, Dock.Right);
        head.Children.Add(help);
        head.Children.Add(t);
        sp.Children.Add(head);

        // WPF: the explainer (four pictures + two lines, 2 x 2) sits above the switch.
        ExplainerSlot.Child = ExplainerFactory?.Invoke(offer)
            ?? new Explain.LeashExplainCard(LeashIntroSide.Leashed, offer.From.Name, Explain.LeashExplainLayout.Grid);
        sp.Children.Add(ExplainerSlot);

        var cap = LeashLook.Caption(Loc.Get("leash_ask_level"));
        cap.Margin = new Thickness(2, 12, 0, 4);
        sp.Children.Add(cap);
        sp.Children.Add(_switchHost);
        sp.Children.Add(_allowed);
        PaintLevel();

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var no = LeashLook.Chunky(Loc.Get("leash_ask_no"), LeashLook.Tone.Ghost, "leash-ask-no", 15);
        no.Click += async (_, _) => await AnswerAsync(false);
        _yes = LeashLook.Chunky(Loc.Get("leash_ask_yes"), LeashLook.Tone.Gold, "leash-ask-yes", 15);
        _yes.Margin = new Thickness(10, 0, 0, 0);
        _yes.Click += async (_, _) => await AnswerAsync(true);
        // The "read first" gate: on the first ask, Put it on waits until the explainer was read.
        if (ExplainerSlot.Child is Explain.LeashExplainCard intro
            && LeashIntroRule.ShouldExplain(Explain.LeashExplainer.Settings(), LeashIntroSide.Leashed))
        {
            _yes.IsEnabled = false;
            ToolTip.SetTip(_yes, Loc.Get("leash_explain_read_first"));
            intro.ReadyForAnswer += () => { _yes.IsEnabled = true; ToolTip.SetTip(_yes, null); };
            intro.StartReadClock(alreadySeen: false);
        }
        row.Children.Add(no);
        row.Children.Add(_yes);
        sp.Children.Add(row);

        _error = LeashLook.Wrap(FriendsDrawer.Label("", 11.5, FriendsDrawer.Red));
        _error.Margin = new Thickness(0, 8, 0, 0);
        _error.IsVisible = false;
        _error.Tag = "leash-ask-error";
        sp.Children.Add(_error);
        Child = sp;
    }

    /// <summary>The explainer's room above the switch.</summary>
    public Border ExplainerSlot { get; } = new() { Tag = "leash-explainer-slot" };

    /// <summary>Test seam: builds the slot's content. Null = the real explainer.</summary>
    internal static Func<LeashOffer, Control>? ExplainerFactory { get; set; }

    internal Button PutItOnButton => _yes;

    internal LeashIntensity Level => _level;

    /// <summary>The last answer's result (the snap plays only on a real Done).</summary>
    internal LeashAnswerResult? LastResult { get; private set; }
    internal string? ErrorShown => _error.IsVisible ? _error.Text : null;

    /// <summary>What the card says when an answer did not go through. Null = it went through.</summary>
    internal static string? ErrorKey(LeashAnswerResult r) => r switch
    {
        LeashAnswerResult.Done => null,
        LeashAnswerResult.Gone => "leash_ask_err_gone",
        LeashAnswerResult.Off => "leash_ask_err_off",
        _ => "leash_ask_err_failed",
    };

    private static Control Panel(IBrush accent, string key, bool cut) => new Border
    {
        Background = FriendsDrawer.Raised,
        BorderBrush = cut ? accent : FriendsDrawer.Line2,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(8, 6, 8, 6),
        Margin = new Thickness(0, 0, 6, 6),
        Tag = cut ? "leash-ask-panel-cut" : "leash-ask-panel",
        Child = LeashLook.Wrap(FriendsDrawer.Label(Loc.Get(key), 12, cut ? accent : FriendsDrawer.Muted)),
    };

    private void PaintLevel()
    {
        _switchHost.Child = LeashLook.Segmented(
            new[] { Loc.Get("leash_level_soft"), Loc.Get("leash_level_standard"), Loc.Get("leash_level_strict") },
            (int)_level, i => SetLevel((LeashIntensity)i), "leash-ask-level", 13);
        _allowed.Children.Clear();
        foreach (var k in LeashUiRules.PunishOrder)
        {
            bool on = LeashUiRules.Allowed(k, _level);
            _allowed.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 6, 6),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(on ? Color.FromRgb(0x6B, 0x27, 0x48) : Color.FromRgb(0x3A, 0x2B, 0x54)),
                Background = new SolidColorBrush(on ? Color.FromRgb(0x3A, 0x1A, 0x33) : Color.FromRgb(0x24, 0x18, 0x38)),
                Tag = "leash-allow:" + k.ToString().ToLowerInvariant() + (on ? ":on" : ""),
                Child = FriendsDrawer.Label(Loc.Get(LeashUiRules.Key(k)), 11.5, on ? FriendsDrawer.Text : FriendsDrawer.Dim, null, FontWeight.SemiBold),
            });
        }
    }

    internal void SetLevel(LeashIntensity level)
    {
        bool up = (int)level > (int)_level;
        _level = level;
        PaintLevel();
        if (up)
            foreach (var c in _allowed.Children)
                if (c is Border b && b.Tag is string s && s.EndsWith(":on")) LeashFx.Pop(b);
    }

    internal void Dismiss() => Dismissed?.Invoke();

    internal async Task AnswerAsync(bool accept)
    {
        var wasEnabled = _yes.IsEnabled;
        _yes.IsEnabled = false;
        var result = LeashAnswerResult.Failed;
        try { if (_svc() is { } s) result = await s.AnswerAsync(_offer.From.Id, accept, _level); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] answer failed: {E}", ex.Message); }
        LastResult = result;
        _yes.IsEnabled = wasEnabled && result != LeashAnswerResult.Gone;
        if (ErrorKey(result) is { } key)
        {
            _error.Text = Loc.GetF(key, _offer.From.Name);
            _error.IsVisible = true;
            LeashFx.Denied();
            if (result != LeashAnswerResult.Gone) return;
        }
        if (result == LeashAnswerResult.Done) Explain.LeashAskIntro.Answered();
        Answered?.Invoke(_offer, accept, _level);
    }

    // ---- the overlay -----------------------------------------------------------------

    /// <summary>Test seam: receives the card instead of a window being shown.</summary>
    internal static Action<LeashAskCard>? ShowOverride { get; set; }

    /// <summary>WPF LeashSurfaces.ShowAsk: the ask card in the leash overlay over the owner, the ask
    /// chime, and on Put it on the snap (LeashSnapCard) for this side.</summary>
    internal static void Show(LeashOffer offer, Func<ILeashService?> service, Window? owner)
    {
        var card = new LeashAskCard(offer, service);
        try { service()?.NoteShown(offer.Id); } catch { }
        if (ShowOverride is { } o) { o(card); return; }
        card.Answered += (off, accepted, _) =>
        {
            LeashOverlay.Close();
            if (accepted && card.LastResult == LeashAnswerResult.Done) LeashSnapCard.Show(off.From, LeashOverlay.Me(), owner);
        };
        card.Dismissed += LeashOverlay.Close;
        LeashOverlay.Open(card, card.Dismiss, owner);
        LeashFx.Ask();
    }
}
