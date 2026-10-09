// PORTED from WPF 7.1.5 Controls/Leash/LeashSnapCard.cs + LeashSurfaces.ShowSnap: THE SNAP, both
// sides. The two avatars, the chain shooting across with the house THUD (340 ms), gold and pink
// sparks where it lands, the collar click, "Leashed.", and a one-screen recap of what each side can
// now do. Shown after Put it on, and replayable from the holder card (r10 calls Replay).
// Motion Off: the chain is simply there and the words read the same.
// Avalonia: no DropShadow Effect (the swinging tag loops under it): a BoxShadow on the card itself.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public sealed class LeashSnapCard : Border
{
    private readonly LeashPerson _holder;
    private readonly LeashPerson _leashed;
    private readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false };
    private Control? _chain;
    private Control? _title;
    private Control? _right;

    public event Action? Closed;

    public LeashSnapCard(LeashPerson holder, LeashPerson leashed)
    {
        _holder = holder;
        _leashed = leashed;
        Width = 380;
        Background = LeashLook.GlassBrush;
        BorderBrush = LeashLook.GoldDeepBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(18);
        BoxShadow = BoxShadows.Parse("0 10 34 0 #99000000");
        Tag = "leash-snap";
        var root = new Grid();
        root.Children.Add(Build());
        root.Children.Add(_fx);
        Child = root;
    }

    internal Canvas FxLayer => _fx;

    private Control Build()
    {
        var sp = new StackPanel();

        var row = new Grid { Height = 84, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        row.Children.Add(FriendsDrawer.Avatar(_holder.Name, 68, null, _holder.AvatarUrl));
        _chain = LeashLook.Chain(190, 14);
        _chain.HorizontalAlignment = HorizontalAlignment.Center;
        _chain.VerticalAlignment = VerticalAlignment.Center;
        _chain.Margin = new Thickness(6, 0, 6, 0);
        _chain.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        var chainHost = new Border { ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center, Child = _chain, Height = 18, Margin = new Thickness(8, 0, 8, 0) };
        Grid.SetColumn(chainHost, 1);
        row.Children.Add(chainHost);
        var rightHost = new Grid();
        _right = FriendsDrawer.Avatar(_leashed.Name, 68, null, _leashed.AvatarUrl);
        rightHost.Children.Add(_right);
        var tag = LeashLook.HeartTag(26);
        tag.HorizontalAlignment = HorizontalAlignment.Left;
        tag.VerticalAlignment = VerticalAlignment.Bottom;
        tag.Margin = new Thickness(-4, 0, 0, -10);
        rightHost.Children.Add(tag);
        LeashFx.Swing(tag);
        Grid.SetColumn(rightHost, 2);
        row.Children.Add(rightHost);
        sp.Children.Add(row);

        var t = FriendsDrawer.Label(Loc.Get("leash_snap_title"), 26, FriendsDrawer.Gold, FriendsDrawer.Display, FontWeight.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 10, 0, 0);
        t.Tag = "leash-snap-title";
        _title = t;
        sp.Children.Add(t);

        var panels = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
        panels.Children.Add(Recap(Loc.GetF("leash_snap_holder", _holder.Name), FriendsDrawer.Gold, false));
        panels.Children.Add(Recap(Loc.GetF("leash_snap_leashed", _leashed.Name), FriendsDrawer.Mint, true));
        sp.Children.Add(panels);

        var ok = LeashLook.Chunky(Loc.Get("leash_snap_ok"), LeashLook.Tone.Pink, "leash-snap-ok", 15);
        ok.Margin = new Thickness(0, 16, 0, 0);
        ok.HorizontalAlignment = HorizontalAlignment.Stretch;
        ok.Click += (_, _) => Close();
        sp.Children.Add(ok);
        return sp;
    }

    internal void Close() => Closed?.Invoke();

    private static Control Recap(string text, IBrush fg, bool cut) => new Border
    {
        Background = cut ? LeashLook.CutPanelBrush : new SolidColorBrush(Color.FromRgb(0x24, 0x18, 0x38)),
        BorderBrush = cut ? LeashLook.MintDeepBrush : Brushes.Transparent,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(10),
        Margin = new Thickness(3),
        Child = LeashLook.Wrap(FriendsDrawer.Label(text, 12, fg, null, FontWeight.SemiBold)),
    };

    /// <summary>Plays the snap. Call once the card is on screen (the sparks need its layout).</summary>
    public void Play()
    {
        LeashFx.Snap();
        double k = LeashFx.Amount;
        if (k <= 0 || _chain == null) return;
        var s = new ScaleTransform(0, 1);
        _chain.RenderTransform = s;
        TransformTween.Run(s, LeashFx.ThudTime, new (double, AvaloniaProperty, double)[]
        {
            (0.0, ScaleTransform.ScaleXProperty, 0.0), (1.0, ScaleTransform.ScaleXProperty, 1.0),
        }, new global::Avalonia.Animation.Easings.CubicEaseOut());
        DispatcherTimer.RunOnce(() =>
        {
            if (_right == null) return;
            LeashFx.Thud(_right, 1.18);
            LeashFx.Sparks(_fx, _right, (int)(22 * k), LeashFx.GoldC, LeashFx.GoldC, LeashFx.PinkC);
        }, LeashFx.ThudTime);
        if (_title != null)
        {
            _title.Opacity = 0;
            DispatcherTimer.RunOnce(() =>
            {
                _title.Opacity = 1;
                LeashFx.Thud(_title, 1.5);
            }, TimeSpan.FromMilliseconds(300));
        }
    }

    /// <summary>WPF LeashSurfaces.ShowSnap: the snap in the leash overlay, played once it is up.</summary>
    internal static LeashSnapCard Show(LeashPerson holder, LeashPerson leashed, Window? owner)
    {
        var card = new LeashSnapCard(holder, leashed);
        card.Closed += LeashOverlay.Close;
        LeashOverlay.Open(card, LeashOverlay.Close, owner);
        Dispatcher.UIThread.Post(card.Play, DispatcherPriority.Normal);
        return card;
    }

    /// <summary>THE REPLAY SEAM for the holder card's "Replay the snap" (WPF:
    /// <c>card.ReplaySnapRequested += held =&gt; LeashSurfaces.ShowSnap(Me(), held.Who)</c>).
    /// <paramref name="from"/> is any control on the panel (its window owns the overlay).</summary>
    internal static void Replay(Control? from, LeashPerson leashed)
    {
        try { Show(LeashOverlay.Me(), leashed, from == null ? null : TopLevel.GetTopLevel(from) as Window); }
        catch (Exception ex) { Serilog.Log.Debug("[Leash] snap replay failed: {E}", ex.Message); }
    }
}
