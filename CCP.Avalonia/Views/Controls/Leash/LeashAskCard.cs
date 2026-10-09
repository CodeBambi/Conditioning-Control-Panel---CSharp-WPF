// PORTED from WPF 7.1.5 Controls/Leash/LeashAskCard.cs + LeashSurfaces.ShowAsk / LeashOverlayWindow:
// "{name} wants to hold your leash" with the four plain panels (sees, rewards, tasks, cut), how
// strict (only this side picks), which punishments that level allows, Not now / Put it on, and the
// reason when an answer did not go through. Shown in a borderless overlay window over the owner;
// Escape is "later". No free text (Leash CONTRACT).
// ponytail: the animated explainer (LeashExplainer / LeashAskIntro "read first" gate), LeashFx.Ask
// and the snap card played after Put it on (LeashSnapCard).
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
        head.Children.Add(t);
        sp.Children.Add(head);

        var panels = new global::Avalonia.Controls.Primitives.UniformGrid { Columns = 2, Tag = "leash-explainer-plain" };
        panels.Children.Add(Panel(FriendsDrawer.Lilac, "leash_ask_p_sees", false));
        panels.Children.Add(Panel(FriendsDrawer.Gold, "leash_ask_p_rewards", false));
        panels.Children.Add(Panel(FriendsDrawer.Pink, "leash_ask_p_tasks", false));
        panels.Children.Add(Panel(FriendsDrawer.Mint, "leash_ask_p_cut", true));
        sp.Children.Add(panels);

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

    internal LeashIntensity Level => _level;
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

    internal void SetLevel(LeashIntensity level) { _level = level; PaintLevel(); }

    internal void Dismiss() => Dismissed?.Invoke();

    internal async Task AnswerAsync(bool accept)
    {
        var wasEnabled = _yes.IsEnabled;
        _yes.IsEnabled = false;
        var result = LeashAnswerResult.Failed;
        try { if (_svc() is { } s) result = await s.AnswerAsync(_offer.From.Id, accept, _level); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] answer failed: {E}", ex.Message); }
        _yes.IsEnabled = wasEnabled && result != LeashAnswerResult.Gone;
        if (ErrorKey(result) is { } key)
        {
            _error.Text = Loc.GetF(key, _offer.From.Name);
            _error.IsVisible = true;
            if (result != LeashAnswerResult.Gone) return;
        }
        Answered?.Invoke(_offer, accept, _level);
    }

    // ---- the overlay -----------------------------------------------------------------

    private static Window? _overlay;

    /// <summary>Test seam: receives the card instead of a window being shown.</summary>
    internal static Action<LeashAskCard>? ShowOverride { get; set; }

    /// <summary>WPF LeashSurfaces.ShowAsk: the ask card in a borderless overlay over the owner.</summary>
    internal static void Show(LeashOffer offer, Func<ILeashService?> service, Window? owner)
    {
        var card = new LeashAskCard(offer, service);
        try { service()?.NoteShown(offer.Id); } catch { }
        if (ShowOverride is { } o) { o(card); return; }
        Close();
        var w = new Window
        {
            WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            Background = Brushes.Transparent,
            CanResize = false,
            ShowInTaskbar = false,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Title = Loc.Get("leash_title"),
            Content = new Grid { Margin = new Thickness(40), Children = { card } },
        };
        w.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            card.Dismiss();
        };
        card.Answered += (_, _, _) => Close();
        card.Dismissed += Close;
        _overlay = w;
        try
        {
            if (owner is { IsVisible: true }) w.Show(owner); else w.Show();
            w.Activate();
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] overlay failed: {E}", ex.Message); _overlay = null; }
    }

    private static void Close()
    {
        var o = _overlay;
        _overlay = null;
        try { o?.Close(); } catch { }
    }
}
