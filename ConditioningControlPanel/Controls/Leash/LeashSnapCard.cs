using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// THE SNAP, both sides: the two avatars, the chain shooting across with the house THUD
/// (340 ms, cubic-bezier(.2,1.5,.4,1)), gold and pink sparks where it lands, the collar click,
/// "Leashed.", and a one-screen recap of what each side can now do. Replayable from the holder
/// card's menu. Motion Off: the chain is simply there and the words read the same.
/// </summary>
public sealed class LeashSnapCard : Border
{
    private readonly LeashPerson _holder;
    private readonly LeashPerson _leashed;
    private readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false };
    private FrameworkElement? _chain;
    private FrameworkElement? _title;
    private FrameworkElement? _right;

    public event Action? Closed;

    public LeashSnapCard(LeashPerson holder, LeashPerson leashed)
    {
        _holder = holder;
        _leashed = leashed;
        Width = 380;
        Background = FriendsLook.GlassBrush;
        BorderBrush = LeashLook.GoldDeepBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(18);
        Tag = "leash-snap";
        Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 34, ShadowDepth = 10, Direction = 270, Opacity = 0.6 };
        var root = new Grid();
        root.Children.Add(Build());
        root.Children.Add(_fx);
        Child = root;
    }

    private FrameworkElement Build()
    {
        var sp = new StackPanel();

        var row = new Grid { Height = 84 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = FriendsLook.Avatar(_holder.Name, _holder.AvatarUrl, 68);
        row.Children.Add(left);
        _chain = LeashLook.Chain(190, 14);
        _chain.HorizontalAlignment = HorizontalAlignment.Center;
        _chain.VerticalAlignment = VerticalAlignment.Center;
        _chain.Margin = new Thickness(6, 0, 6, 0);
        _chain.RenderTransformOrigin = new Point(0, 0.5);
        var chainHost = new Border { ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center, Child = _chain, Height = 18, Margin = new Thickness(8, 0, 8, 0) };
        Grid.SetColumn(chainHost, 1);
        row.Children.Add(chainHost);
        var rightHost = new Grid();
        _right = FriendsLook.Avatar(_leashed.Name, _leashed.AvatarUrl, 68);
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

        var t = FriendsLook.Label(Loc.Get("leash_snap_title"), 26, FriendsLook.GoldBrush, FriendsLook.Display, FontWeights.SemiBold);
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.Margin = new Thickness(0, 10, 0, 0);
        t.Tag = "leash-snap-title";
        _title = t;
        sp.Children.Add(t);

        var panels = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
        panels.Children.Add(Recap(Loc.GetF("leash_snap_holder", _holder.Name), FriendsLook.GoldBrush, false));
        panels.Children.Add(Recap(Loc.GetF("leash_snap_leashed", _leashed.Name), FriendsLook.MintBrush, true));
        sp.Children.Add(panels);

        var ok = LeashLook.Chunky(Loc.Get("leash_snap_ok"), LeashLook.Tone.Pink, size: 15);
        ok.Margin = new Thickness(0, 16, 0, 0);
        ok.Tag = "leash-snap-ok";
        ok.Click += (_, _) => Closed?.Invoke();
        sp.Children.Add(ok);
        return sp;
    }

    private static FrameworkElement Recap(string text, Brush fg, bool cut)
    {
        var b = new Border
        {
            Background = cut ? LeashLook.CutPanelBrush : FriendsLook.Frozen(FriendsLook.Rgb(0x24, 0x18, 0x38)),
            BorderBrush = cut ? FriendsLook.Frozen(LeashLook.MintDeep) : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10),
            Margin = new Thickness(3),
        };
        b.Child = LeashLook.Wrap(FriendsLook.Label(text, 12, fg, null, FontWeights.SemiBold));
        return b;
    }

    /// <summary>Plays the snap. Call once the card is on screen (it needs its layout for the sparks).</summary>
    public void Play()
    {
        LeashFx.Snap();
        double k = LeashFx.Amount;
        if (k <= 0 || _chain == null) return;
        var s = new ScaleTransform(0, 1);
        _chain.RenderTransform = s;
        var grow = new DoubleAnimation(0, 1, BezierEase.Thud) { EasingFunction = new BezierEase() };
        grow.Completed += (_, _) =>
        {
            if (_right != null)
            {
                LeashFx.Thud(_right, 1.18);
                LeashFx.Sparks(_fx, _right, (int)(22 * k), FriendsLook.Gold, FriendsLook.Gold, FriendsLook.Pink);
            }
        };
        s.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        if (_title != null)
        {
            _title.Opacity = 0;
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                _title.Opacity = 1;
                LeashFx.Thud(_title, 1.5);
            };
            t.Start();
        }
    }
}
