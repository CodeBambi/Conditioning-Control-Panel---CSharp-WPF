// PORTED from WPF 7.1.5 Controls/Leash/LeashSurfaces.cs: the app-wide leash surfaces that are not a
// card. The ask card for each new offer (one at a time, only while the panel is up), the snap when
// an offer of ours is accepted, every event's sound (LeashFx) and corner notice (App.Notifications,
// rules in Core LeashUiRules.EventNotice), notices held while the panel is away and said when it
// is back, and the tug wobble request. The one-click cut itself stays LeashHead.Cut (Platform);
// this listens to its CutDone so the cut sounds once and its own Ended event stays quiet.
// Wired from MainShellWindow.InitializeLeash (Init, the 2 s Rebind, the tug wobble).
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;
using NoticeKind = ConditioningControlPanel.Avalonia.Helpers.NotificationType;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public static class LeashSurfaces
{
    private static ILeashService? _svc;
    private static Func<Window?> _owner = () => null;
    private static readonly HashSet<string> SeenOffers = new();
    private static readonly List<LeashOffer> WaitingAsks = new();
    private static readonly List<LeashEvent> HeldNotices = new();
    private static bool _iCut;
    private static bool _cutHooked;

    /// <summary>True while the panel (where toasts draw) is up and not minimised. Tests swap it.</summary>
    internal static Func<bool> PanelShowing = () => _owner() is { IsVisible: true, WindowState: not WindowState.Minimized };

    /// <summary>Test seam: the corner notice (WPF App.Notifications.Show).</summary>
    internal static Action<string, NoticeKind> Toast = (text, kind) =>
    {
        try { App.Notifications.Show(text, kind); } catch { }
    };

    /// <summary>Notices that landed while nothing showed them, oldest first.</summary>
    internal static IReadOnlyList<LeashEvent> Held => HeldNotices;

    /// <summary>Raised when the leashed side's window should wobble (the host picks the window).</summary>
    public static event Action? TugArrived;

    public static void Init(Func<Window?> owner)
    {
        _owner = owner ?? (() => null);
        if (!_cutHooked)
        {
            _cutHooked = true;
            Platform.LeashHead.CutDone += NoteCut;
        }
        Rebind();
    }

    /// <summary>Picks up a new service (sign in / out) and re-offers any ask that waited.</summary>
    public static void Rebind() => Rebind(LeashLocator.Service);

    internal static void Rebind(Func<ILeashService?> resolve)
    {
        ILeashService? next = null;
        try { next = resolve(); } catch { }
        if (!ReferenceEquals(next, _svc))
        {
            if (_svc != null)
            {
                _svc.SnapshotChanged -= OnSnapshotAny;
                _svc.EventArrived -= OnEventAny;
            }
            _svc = next;
            SeenOffers.Clear();
            HeldNotices.Clear();
            WaitingAsks.Clear();
            if (_svc != null)
            {
                _svc.SnapshotChanged += OnSnapshotAny;
                _svc.EventArrived += OnEventAny;
                try { OnSnapshot(_svc.Snapshot); } catch { }
            }
        }
        TryShowWaitingAsk();
        FlushHeldNotices();
    }

    private static void OnSnapshotAny(LeashSnapshot s) => OnUi(() => OnSnapshot(s));
    private static void OnEventAny(LeashEvent e) => OnUi(() => OnEvent(e));

    private static void OnUi(Action a)
    {
        if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Post(a);
    }

    /// <summary>The panel is back: say what landed while it was away (quietly, the sounds already
    /// played) and only now report those items seen.</summary>
    internal static void FlushHeldNotices()
    {
        if (HeldNotices.Count == 0) return;
        bool panel;
        try { panel = PanelShowing(); } catch { panel = false; }
        if (!panel) return;
        var held = HeldNotices.ToList();
        HeldNotices.Clear();
        foreach (var e in held)
        {
            try
            {
                if (LeashUiRules.EventKey(e) is { } key) Toast(Loc.GetF(key, e.From.Name), NoticeType(e));
                _svc?.NoteShown(e.Id);
            }
            catch (Exception ex) { Serilog.Log.Debug("[Leash] held notice failed: {E}", ex.Message); }
        }
    }

    private static NoticeKind NoticeType(LeashEvent e) =>
        e.Kind is LeashEventKind.Punish or LeashEventKind.AssignMissed ? NoticeKind.Warning : NoticeKind.Info;

    private static void OnSnapshot(LeashSnapshot snap)
    {
        foreach (var o in snap.Offers)
        {
            if (!SeenOffers.Add(o.From.Id + "@" + o.At.ToUnixTimeSeconds())) continue;
            WaitingAsks.RemoveAll(w => w.From.Id == o.From.Id);
            WaitingAsks.Add(o);
        }
        WaitingAsks.RemoveAll(w => !snap.Offers.Any(o => o.From.Id == w.From.Id));
        foreach (var h in snap.Holding) LeashDrawerSection.SentOffers.Remove(h.Who.Id);
        TryShowWaitingAsk();
    }

    private static void TryShowWaitingAsk()
    {
        if (WaitingAsks.Count == 0 || LeashOverlay.Current != null) return;
        var owner = _owner();
        if (owner == null || !owner.IsVisible || owner.WindowState == WindowState.Minimized) return;
        var o = WaitingAsks[0];
        WaitingAsks.RemoveAt(0);
        var svc = _svc;
        LeashAskCard.Show(o, () => svc, owner);
    }

    /// <summary>Called when the overlay closes (the next waiting ask may show).</summary>
    internal static void OverlayClosed() => TryShowWaitingAsk();

    internal static void OnEvent(LeashEvent e)
    {
        try
        {
            switch (e.Kind)
            {
                case LeashEventKind.Tug:
                    LeashFx.Jingle();
                    try { TugArrived?.Invoke(); } catch { }
                    break;
                case LeashEventKind.Answered when e.Accepted == true:
                    LeashDrawerSection.SentOffers.Remove(e.From.Id);
                    LeashSnapCard.Show(LeashOverlay.Me(), e.From, _owner());
                    return;
                case LeashEventKind.Answered:
                    LeashDrawerSection.SentOffers.Remove(e.From.Id);
                    LeashFx.Refused();
                    break;
                case LeashEventKind.Ended when _iCut:
                    _iCut = false;
                    return;
                case LeashEventKind.Ended:
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
                case LeashEventKind.PunishSkipped:
                    LeashFx.Refused();
                    break;
            }
            bool panel, wobbles;
            try { panel = PanelShowing(); } catch { panel = false; }
            // A tug is on screen wherever its wobble plays, never with motion off.
            try { wobbles = _owner() != null && LeashFx.Amount > 0; } catch { wobbles = false; }
            var (toast, seen, hold) = LeashUiRules.EventNotice(e.Kind, panel, wobbles);
            var key = LeashUiRules.EventKey(e);
            if (toast && key != null) Toast(Loc.GetF(key, e.From.Name), NoticeType(e));
            if (seen) _svc?.NoteShown(e.Id);
            if (hold)
            {
                HeldNotices.Add(e);
                while (HeldNotices.Count > LeashUiRules.HeldNotices) HeldNotices.RemoveAt(0);
            }
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] event {K} failed: {E}", e.Kind, ex.Message); }
    }

    /// <summary>WPF NoteCut: this side cut (LeashHead.CutDone). The cut sounds once; the Ended event
    /// that follows stays quiet. LeashHead shows the done line.</summary>
    internal static void NoteCut()
    {
        _iCut = true;
        LeashFx.Cut();
    }

    /// <summary>True while this account is on someone's leash.</summary>
    public static bool IsLeashed
    {
        get
        {
            try { return LeashLocator.Service()?.Snapshot.Me != null; } catch { return false; }
        }
    }
}
