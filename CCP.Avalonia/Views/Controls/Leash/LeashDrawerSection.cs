// PORTED from WPF 7.1.5 Controls/Leash/LeashDrawerSection.cs: what the friends drawer pins above its
// list. An incoming offer row ("Juno wants to hold your leash" + Look, which opens the ask card),
// the leashed side's own card, then one holder card per account held. Also the source of the Offer
// chip on a friend's card. Cards are kept between repaints so an open menu survives a poll.
// Juice as WPF: LeashFx Pop on the Offer chip, Sent / Denied sounds, the "?" (Offer) beside it.
// The holder's first offer goes through LeashExplainer.BeforeOffer (WPF).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public sealed class LeashDrawerSection : StackPanel
{
    private readonly Func<ILeashService?> _resolve;
    private readonly Func<Window?> _owner;
    private ILeashService? _svc;
    private readonly Dictionary<string, LeashHolderCard> _cards = new();
    private LeashSelfCard? _self;
    private bool _subscribed;

    /// <summary>WPF LeashSurfaces.SentOffers: friends offered this session (the chip reads Sent).</summary>
    internal static HashSet<string> SentOffers { get; } = new();

    /// <summary>Raised after every repaint (the drawer's juice likes to know).</summary>
    public event Action? Changed;

    public LeashDrawerSection(Func<ILeashService?>? service = null, Func<Window?>? owner = null)
    {
        _resolve = service ?? (() => Platform.LeashHead.Service);
        _owner = owner ?? (() => TopLevel.GetTopLevel(this) as Window);
        Tag = "leash-section";
        AttachedToVisualTree += (_, _) => { Rebind(); Render(); };
        DetachedFromVisualTree += (_, _) => Unsubscribe();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) NoteShownIfVisible(); };
        Rebind();
        Render();
    }

    internal ILeashService? Service => _svc;
    internal IReadOnlyCollection<LeashHolderCard> HolderCards => _cards.Values;
    internal LeashSelfCard? SelfCard => _self;

    private void Rebind()
    {
        ILeashService? next = null;
        try { next = _resolve(); } catch { }
        if (ReferenceEquals(next, _svc) && _subscribed) return;
        Unsubscribe();
        _svc = next;
        if (_svc != null)
        {
            _svc.SnapshotChanged += OnSnapshot;
            _svc.ReceiptsChanged += OnReceipts;
            _subscribed = true;
        }
    }

    private void Unsubscribe()
    {
        if (_svc != null && _subscribed)
        {
            _svc.SnapshotChanged -= OnSnapshot;
            _svc.ReceiptsChanged -= OnReceipts;
        }
        _subscribed = false;
    }

    private void OnSnapshot(LeashSnapshot _)
    {
        void Paint() { try { Render(); } catch (Exception ex) { Serilog.Log.Debug("[Leash] section repaint failed: {E}", ex.Message); } }
        if (Dispatcher.UIThread.CheckAccess()) Paint(); else Dispatcher.UIThread.Post(Paint);
    }

    private void OnReceipts()
    {
        void Paint() { try { foreach (var c in _cards.Values) c.Render(); } catch { } }
        if (Dispatcher.UIThread.CheckAccess()) Paint(); else Dispatcher.UIThread.Post(Paint);
    }

    private void NoteShownIfVisible()
    {
        if (IsVisible && this.IsAttachedToVisualTree()) NoteOnScreen();
    }

    /// <summary>Leashed side: what this section puts in front of the player now, reported seen once
    /// (the service dedupes): an offer row, and today's task on the own card.</summary>
    internal void NoteOnScreen()
    {
        var s = _svc;
        if (s == null) return;
        var snap = Snap();
        try
        {
            foreach (var o in snap.Offers) s.NoteShown(o.Id);
            if (snap.Me?.Assignment is { } a) s.NoteShown(a.Aid);
        }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] seen note failed: {E}", ex.Message); }
    }

    private LeashSnapshot Snap()
    {
        try { return _svc?.Available == true ? _svc.Snapshot ?? LeashSnapshot.Empty : LeashSnapshot.Empty; }
        catch { return LeashSnapshot.Empty; }
    }

    internal void Render()
    {
        Children.Clear();
        var snap = Snap();

        foreach (var o in snap.Offers) Children.Add(OfferRow(o));

        if (snap.Me is { } me)
        {
            if (_self == null) _self = new LeashSelfCard(me, () => _svc);
            else _self.Update(me);
            Children.Add(_self);
        }
        else _self = null;

        var keep = new HashSet<string>();
        foreach (var h in snap.Holding)
        {
            keep.Add(h.Who.Id);
            if (!_cards.TryGetValue(h.Who.Id, out var card))
            {
                card = new LeashHolderCard(h, () => _svc);
                _cards[h.Who.Id] = card;
            }
            else card.Update(h);
            Children.Add(card);
        }
        foreach (var gone in _cards.Keys.Where(k => !keep.Contains(k)).ToList()) _cards.Remove(gone);

        IsVisible = Children.Count > 0;
        Changed?.Invoke();
        NoteShownIfVisible();
    }

    private Control OfferRow(LeashOffer o)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        var av = FriendsDrawer.Avatar(o.From.Name, 34, null, o.From.AvatarUrl);
        av.Margin = new Thickness(0, 0, 8, 0);
        g.Children.Add(av);
        var t = new TextBlock { FontSize = 12.5, Foreground = FriendsDrawer.Text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        t.Inlines!.Add(new Run(o.From.Name) { Foreground = FriendsDrawer.Gold, FontWeight = FontWeight.SemiBold });
        t.Inlines.Add(new Run(" " + Loc.Get("leash_ask_title")));
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var look = LeashLook.Chunky(Loc.Get("leash_offer_look"), LeashLook.Tone.Gold, "leash-offer-look", 12);
        look.Margin = new Thickness(6, 0, 0, 0);
        look.VerticalAlignment = VerticalAlignment.Center;
        look.Click += (_, _) => LeashAskCard.Show(o, () => _svc, _owner());
        Grid.SetColumn(look, 2);
        g.Children.Add(look);
        return new Border
        {
            Background = LeashLook.CardBrush,
            BorderBrush = LeashLook.GoldDeepBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 8, 8, 8),
            Margin = new Thickness(0, 4, 0, 4),
            Tag = "leash-offer-row:" + o.From.Id,
            Child = g,
        };
    }

    // ---- the Offer chip on a friend's card ---------------------------------------------

    /// <summary>The chip under a friend's card actions, or null when there is no leash to offer
    /// (service off, signed out). Online, or seen in the last 7 days, can be offered.</summary>
    public Control? OfferChipFor(Friend f)
    {
        Rebind();
        var snap = Snap();
        bool available;
        try { available = _svc?.Available == true; } catch { available = false; }
        var state = LeashUiRules.OfferState(available, snap.Holding.Any(h => h.Who.Id == f.Id),
            SentOffers.Contains(f.Id), f.Online, f.Presence.LastSeen, DateTimeOffset.UtcNow, snap.Holding.Count);
        if (state == LeashUiRules.OfferChip.Hidden) return null;

        IBrush accent = state switch
        {
            LeashUiRules.OfferChip.Sent => FriendsDrawer.Mint,
            LeashUiRules.OfferChip.Greyed => FriendsDrawer.Dim,
            _ => FriendsDrawer.Gold,
        };
        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var label = FriendsDrawer.Label(Loc.Get(state switch
        {
            LeashUiRules.OfferChip.Sent => "leash_offer_sent",
            LeashUiRules.OfferChip.Holding => "leash_offer_holding",
            _ => "leash_offer",
        }), 13, accent, FriendsDrawer.Display, FontWeight.Medium);
        label.Tag = "leash-offer-label";
        content.Children.Add(label);
        if (state == LeashUiRules.OfferChip.Sent && OfferStep(f.Id) is { } step)
        {
            var w = FriendsDrawer.Label("  ·  " + Loc.Get(LeashUiRules.StepKey(step)), 11, FriendsDrawer.Lilac, FriendsDrawer.Mono);
            w.Tag = "leash-offer-step:" + step.ToString().ToLowerInvariant();
            content.Children.Add(w);
        }
        var chip = FriendsDrawer.Pill(content, LeashLook.GoldWashBrush, accent, "leash-offer",
            state == LeashUiRules.OfferChip.Sent ? LeashLook.MintDeepBrush : LeashLook.GoldDeepBrush);
        chip.CornerRadius = new CornerRadius(999);
        chip.Padding = new Thickness(10, 6, 10, 6);
        chip.HorizontalAlignment = HorizontalAlignment.Stretch;
        chip.HorizontalContentAlignment = HorizontalAlignment.Center;
        chip.Cursor = FriendsDrawer.Hand();
        chip.IsEnabled = state == LeashUiRules.OfferChip.Offer;
        if (state == LeashUiRules.OfferChip.Greyed) ToolTip.SetTip(chip, Loc.Get("leash_offer_greyed"));
        chip.Click += async (_, _) =>
        {
            LeashFx.Pop(chip);
            // WPF: the holder's first offer opens the explainer; the offer goes only from its Offer.
            Explain.LeashExplainer.BeforeOffer(TopLevel.GetTopLevel(chip) as Window, f.Name, async () =>
            {
                var r = await OfferAsync(f.Id);
                if (LeashUiRules.IsGood(r)) label.Text = Loc.Get("leash_offer_sent");
                else
                {
                    label.Text = Loc.Get(LeashUiRules.ResultKey(r));
                    chip.IsEnabled = false;
                }
            });
            await Task.CompletedTask;
        };
        // WPF: the chip and the round "?" (Offer) side by side.
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        row.Children.Add(chip);
        var help = LeashLook.Help(LeashExplainRole.Offer);
        help.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(help, 1);
        row.Children.Add(help);
        return new Border { Margin = new Thickness(0, 0, 0, 6), Tag = "leash-offer-chip:" + state.ToString().ToLowerInvariant(), Child = row };
    }

    private LeashStep? OfferStep(string friendId)
    {
        try { return _svc is { } s ? LeashUiRules.OfferStep(s.ReceiptsSupported, s.SentTo(friendId)) : null; }
        catch { return null; }
    }

    /// <summary>THE one place an offer is sent (the chip calls only this).</summary>
    internal async Task<LeashSendStatus> OfferAsync(string friendId)
    {
        LeashSendStatus s;
        try { s = _svc == null ? LeashSendStatus.Off : (await _svc.OfferAsync(friendId)).Status; }
        catch { s = LeashSendStatus.Failed; }
        if (LeashUiRules.IsGood(s)) { SentOffers.Add(friendId); LeashFx.Sent(); }
        else if (s is LeashSendStatus.TooFast or LeashSendStatus.Dnd or LeashSendStatus.Failed) LeashFx.Denied();
        return s;
    }
}
