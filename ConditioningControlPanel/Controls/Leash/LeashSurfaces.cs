using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// The leash's app-wide reactions, wired once by the panel (<c>MainWindow.Leash.cs</c>): a new
/// offer brings up the ask card, an accepted offer plays the snap on both sides, a tug wobbles
/// the leashed side's window with a chain jingle, and every other one-shot event says one short
/// line. It re-reads <see cref="LeashLocator.Service"/> on every <see cref="Rebind"/>, because
/// the service is rebuilt on sign in and sign out.
/// </summary>
public static class LeashSurfaces
{
    private static ILeashService? _svc;
    private static Func<Window?> _owner = () => null;
    private static readonly HashSet<string> SeenOffers = new();
    private static readonly List<LeashOffer> WaitingAsks = new();
    private static LeashOverlayWindow? _overlay;
    private static bool _iCut;

    /// <summary>Offers this client sent that nobody has answered yet (the Offer chip reads "offered").
    /// The wire has no outgoing-offer list, so this lives for the session.</summary>
    public static HashSet<string> SentOffers { get; } = new();

    /// <summary>Raised when the leashed side's window should wobble (the host picks the window).</summary>
    public static event Action? TugArrived;

    /// <summary>Raised after the leashed side pressed Cut anywhere (card, gate, tray).</summary>
    public static event Action? CutDone;

    public static void Init(Func<Window?> owner)
    {
        _owner = owner;
        Rebind();
    }

    /// <summary>Picks up a new service (sign in / out) and re-offers any ask that waited.</summary>
    public static void Rebind()
    {
        ILeashService? next = null;
        try { next = LeashLocator.Service(); } catch { }
        if (!ReferenceEquals(next, _svc))
        {
            if (_svc != null)
            {
                _svc.SnapshotChanged -= OnSnapshot;
                _svc.EventArrived -= OnEvent;
            }
            _svc = next;
            SeenOffers.Clear();
            if (_svc != null)
            {
                _svc.SnapshotChanged += OnSnapshot;
                _svc.EventArrived += OnEvent;
                try { OnSnapshot(_svc.Snapshot); } catch { }
            }
        }
        TryShowWaitingAsk();
    }

    private static void OnSnapshot(LeashSnapshot snap)
    {
        foreach (var o in snap.Offers)
        {
            if (!SeenOffers.Add(o.From.Id + "@" + o.At.ToUnixTimeSeconds())) continue;
            WaitingAsks.RemoveAll(w => w.From.Id == o.From.Id);
            WaitingAsks.Add(o);
        }
        WaitingAsks.RemoveAll(w => !snap.Offers.Any(o => o.From.Id == w.From.Id));
        foreach (var h in snap.Holding) SentOffers.Remove(h.Who.Id);
        TryShowWaitingAsk();
    }

    private static void TryShowWaitingAsk()
    {
        if (WaitingAsks.Count == 0 || _overlay != null) return;
        var owner = _owner();
        if (owner == null || !owner.IsVisible || owner.WindowState == WindowState.Minimized) return;
        var o = WaitingAsks[0];
        WaitingAsks.RemoveAt(0);
        ShowAsk(o);
    }

    private static void OnEvent(LeashEvent e)
    {
        try
        {
            switch (e.Kind)
            {
                case LeashEventKind.Tug:
                    LeashFx.Jingle();
                    TugArrived?.Invoke();
                    break;
                case LeashEventKind.Answered when e.Accepted == true:
                    SentOffers.Remove(e.From.Id);
                    ShowSnap(Me(), e.From);
                    return;
                case LeashEventKind.Answered:
                    SentOffers.Remove(e.From.Id);
                    LeashFx.Refused();
                    break;
                case LeashEventKind.Ended when _iCut:
                    _iCut = false;
                    return;
                case LeashEventKind.Ended:
                    // A cut this side pressed already sounded; LeashSfxRules plays a cut once.
                    LeashFx.Cut();
                    break;
                case LeashEventKind.Reward:
                    LeashFx.Gift();
                    break;
                case LeashEventKind.Punish:
                case LeashEventKind.AssignMissed:
                    LeashFx.Scold();
                    break;
                case LeashEventKind.Assign:
                    LeashFx.Assigned();
                    break;
                case LeashEventKind.AssignDone:
                case LeashEventKind.PunishDone:
                    LeashFx.Done();
                    break;
            }
            var key = LeashUiRules.EventKey(e);
            if (key != null) Toast(Loc.GetF(key, e.From.Name), e.Kind is LeashEventKind.Punish or LeashEventKind.AssignMissed ? NotificationType.Warning : NotificationType.Info);
        }
        catch (Exception ex) { App.Logger?.Debug("[Leash] event {K} failed: {E}", e.Kind, ex.Message); }
    }

    private static LeashPerson Me()
    {
        string name;
        try { name = string.IsNullOrWhiteSpace(App.UserDisplayName) ? Loc.Get("leash_you") : App.UserDisplayName!.Trim(); }
        catch { name = "you"; }
        return new LeashPerson("me", name, null);
    }

    private static void Toast(string text, NotificationType type)
    {
        try { App.Notifications?.Show(text, type); } catch { }
    }

    // ---- the overlays -----------------------------------------------------------------

    public static void ShowAsk(LeashOffer offer)
    {
        var card = new LeashAskCard(offer, LeashLocator.Service);
        card.Answered += (o, accepted, _) =>
        {
            CloseOverlay();
            if (accepted) ShowSnap(o.From, Me());
        };
        card.Dismissed += CloseOverlay;
        Open(card, () => card.Dismiss());
        LeashFx.Ask();
    }

    public static void ShowSnap(LeashPerson holder, LeashPerson leashed)
    {
        var card = new LeashSnapCard(holder, leashed);
        card.Closed += CloseOverlay;
        Open(card, CloseOverlay);
        card.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(card.Play));
    }

    private static void Open(FrameworkElement card, Action onEscape)
    {
        CloseOverlay();
        var owner = _owner();
        _overlay = new LeashOverlayWindow(card, onEscape);
        try
        {
            if (owner != null && owner.IsVisible) _overlay.Owner = owner;
            else _overlay.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _overlay.Show();
            _overlay.Activate();
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("[Leash] overlay failed: {E}", ex.Message);
            _overlay = null;
        }
    }

    private static void CloseOverlay()
    {
        var o = _overlay;
        _overlay = null;
        try { o?.Close(); } catch { }
        TryShowWaitingAsk();
    }

    /// <summary>The one cut every leashed surface calls (card, gate, tray). Never refused; the
    /// service drops the leash locally at once and retries the wire on its own.</summary>
    public static async void Cut()
    {
        NoteCut();
        try { if (LeashLocator.Service() is { } s) await s.CutAsync(); }
        catch (Exception ex) { App.Logger?.Debug("[Leash] cut failed (service retries): {E}", ex.Message); }
    }

    /// <summary>A cut was pressed on a surface that called the service itself (the drawer card).</summary>
    public static void NoteCut()
    {
        _iCut = true;
        try { CutDone?.Invoke(); } catch { }
        Toast(Loc.Get("leash_cut_done"), NotificationType.Success);
        LeashFx.Cut();
    }

    /// <summary>True while this account is on someone's leash (the tray shows Cut leash).</summary>
    public static bool IsLeashed
    {
        get
        {
            try { return LeashLocator.Service()?.Snapshot.Me != null; } catch { return false; }
        }
    }
}

/// <summary>A borderless, see-through window that holds one leash card (the ask, the snap) centred
/// over its owner. Escape and a click outside the card do the card's own "later".</summary>
internal sealed class LeashOverlayWindow : Window
{
    public LeashOverlayWindow(FrameworkElement card, Action onEscape)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = Loc.Get("leash_title");
        var host = new Grid { Margin = new Thickness(40) };
        host.Children.Add(card);
        Content = host;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            onEscape();
        };
        Loaded += (_, _) =>
        {
            if (LeashFx.Amount > 0) LeashFx.Thud(card, 1.08);
        };
    }
}
