using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// What the friends drawer pins above its list: an incoming offer ("Juno wants to hold your
/// leash", with a Look button that opens the ask card), the leashed side's own card, then one
/// holder card per account held. Also the source of the Offer chip on a friend's card. It keeps
/// its own cards between repaints so an open sheet survives a poll.
/// </summary>
public sealed class LeashDrawerSection : StackPanel
{
    private readonly Func<ILeashService?> _resolve;
    private ILeashService? _svc;
    private readonly Dictionary<string, LeashHolderCard> _cards = new();
    private LeashSelfCard? _self;
    private bool _subscribed;

    /// <summary>Raised when the section changed height (the drawer re-measures nothing, but the
    /// suite and the drawer's juice like to know).</summary>
    public event Action? Changed;

    public LeashDrawerSection(Func<ILeashService?>? service = null)
    {
        _resolve = service ?? LeashLocator.Service;
        Tag = "leash-section";
        Loaded += (_, _) => { Rebind(); Render(); };
        Unloaded += (_, _) => Unsubscribe();
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
            _subscribed = true;
        }
    }

    private void Unsubscribe()
    {
        if (_svc != null && _subscribed) _svc.SnapshotChanged -= OnSnapshot;
        _subscribed = false;
    }

    private void OnSnapshot(LeashSnapshot _)
    {
        try { Render(); } catch (Exception ex) { App.Logger?.Debug("[Leash] section repaint failed: {E}", ex.Message); }
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
            if (_self == null)
            {
                _self = new LeashSelfCard(me, () => _svc);
                _self.CutPressed += LeashSurfaces.NoteCut;
            }
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
                card.ReplaySnapRequested += held => LeashSurfaces.ShowSnap(Me(), held.Who);
                _cards[h.Who.Id] = card;
            }
            else card.Update(h);
            Children.Add(card);
        }
        foreach (var gone in _cards.Keys.Where(k => !keep.Contains(k)).ToList()) _cards.Remove(gone);

        Visibility = Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Changed?.Invoke();
    }

    private static LeashPerson Me()
    {
        try { return new LeashPerson("me", string.IsNullOrWhiteSpace(App.UserDisplayName) ? Loc.Get("leash_you") : App.UserDisplayName!.Trim(), null); }
        catch { return new LeashPerson("me", "you", null); }
    }

    private FrameworkElement OfferRow(LeashOffer o)
    {
        var row = new Border
        {
            Background = LeashLook.CardBrush,
            BorderBrush = LeashLook.GoldDeepBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(10, 8, 8, 8),
            Margin = new Thickness(0, 4, 0, 4),
            Tag = "leash-offer-row:" + o.From.Id,
        };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var av = FriendsLook.Avatar(o.From.Name, o.From.AvatarUrl, 34);
        av.Margin = new Thickness(0, 0, 8, 0);
        g.Children.Add(av);
        var t = new TextBlock { FontSize = 12.5, Foreground = FriendsLook.TextBrush, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        t.Inlines.Add(new System.Windows.Documents.Run(o.From.Name) { Foreground = FriendsLook.GoldBrush, FontWeight = FontWeights.SemiBold });
        t.Inlines.Add(new System.Windows.Documents.Run(" " + Loc.Get("leash_ask_title")));
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        var look = LeashLook.Chunky(Loc.Get("leash_offer_look"), LeashLook.Tone.Gold, size: 12);
        look.Margin = new Thickness(6, 0, 0, 0);
        look.VerticalAlignment = VerticalAlignment.Center;
        look.Tag = "leash-offer-look";
        look.Click += (_, _) => LeashSurfaces.ShowAsk(o);
        Grid.SetColumn(look, 2);
        g.Children.Add(look);
        row.Child = g;
        return row;
    }

    // ---- the Offer chip on a friend's card ---------------------------------------------

    /// <summary>The chip under a friend's card actions, or null when there is no leash to offer
    /// (service off, signed out). Online, or seen in the last 7 days, can be offered.</summary>
    public FrameworkElement? OfferChipFor(Friend f)
    {
        Rebind();
        var snap = Snap();
        bool available;
        try { available = _svc?.Available == true; } catch { available = false; }
        var state = LeashUiRules.OfferState(available, snap.Holding.Any(h => h.Who.Id == f.Id),
            LeashSurfaces.SentOffers.Contains(f.Id), f.Online, f.Presence.LastSeen, DateTimeOffset.UtcNow, snap.Holding.Count);
        if (state == LeashUiRules.OfferChip.Hidden) return null;

        var row = new Grid { Margin = new Thickness(0, 0, 0, 6), Tag = "leash-offer-chip:" + state.ToString().ToLowerInvariant() };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        Brush accent = state switch
        {
            LeashUiRules.OfferChip.Sent => FriendsLook.MintBrush,
            LeashUiRules.OfferChip.Greyed => FriendsLook.DimBrush,
            _ => FriendsLook.GoldBrush,
        };
        var ic = LeashLook.Icon("link", accent, 14);
        ic.Margin = new Thickness(0, 0, 7, 0);
        content.Children.Add(ic);
        var label = FriendsLook.Label(Loc.Get(state switch
        {
            LeashUiRules.OfferChip.Sent => "leash_offer_sent",
            LeashUiRules.OfferChip.Holding => "leash_offer_holding",
            _ => "leash_offer",
        }), 13, accent, FriendsLook.Display, FontWeights.Medium);
        label.Tag = "leash-offer-label";
        content.Children.Add(label);
        var chip = FriendsLook.Pill(content, FriendsLook.Frozen(Color.FromArgb(0x22, 0xFF, 0xCF, 0x6B)), accent,
            state == LeashUiRules.OfferChip.Sent ? FriendsLook.Frozen(LeashLook.MintDeep) : LeashLook.GoldDeepBrush, 999, new Thickness(10, 6, 10, 6));
        chip.Tag = "leash-offer";
        chip.IsEnabled = state == LeashUiRules.OfferChip.Offer;
        if (state == LeashUiRules.OfferChip.Greyed) chip.ToolTip = Loc.Get("leash_offer_greyed");
        var result = FriendsLook.Label("", 11, FriendsLook.GoldBrush);
        chip.Click += async (_, _) =>
        {
            LeashFx.Pop(chip);
            Explain.LeashExplainer.BeforeOffer(Window.GetWindow(chip), f.Name, async () =>
            {
                var r = await OfferAsync(f.Id, f.Name);
                if (LeashUiRules.IsGood(r)) label.Text = Loc.Get("leash_offer_sent");
                else
                {
                    label.Text = Loc.Get(LeashUiRules.ResultKey(r));
                    chip.IsEnabled = false;
                }
            });
            await System.Threading.Tasks.Task.CompletedTask;
        };
        row.Children.Add(chip);
        var help = LeashLook.Help(LeashExplainRole.Offer);
        help.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(help, 1);
        row.Children.Add(help);
        return row;
    }

    /// <summary>THE one place an offer is sent (the chip calls only this). The explain lane wraps
    /// it: <c>LeashExplainer.BeforeOffer(owner, friendName, send)</c>.</summary>
    internal async Task<LeashSendStatus> OfferAsync(string friendId, string friendName)
    {
        LeashSendStatus s;
        try { s = _svc == null ? LeashSendStatus.Off : (await _svc.OfferAsync(friendId)).Status; }
        catch { s = LeashSendStatus.Failed; }
        if (LeashUiRules.IsGood(s)) { LeashSurfaces.SentOffers.Add(friendId); LeashFx.Sent(); }
        return s;
    }
}
