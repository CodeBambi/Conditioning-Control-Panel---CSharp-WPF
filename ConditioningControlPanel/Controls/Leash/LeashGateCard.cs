using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash;

/// <summary>
/// THE GATE, leashed side: the oldest pending punishment stands over the panel at the next idle
/// moment. "Vex says / Lines, five times", progress pips (or a bar), the one action, a PUNISHED
/// stamp, the pardon button when a token is held. Panic and Cut leash sit on the card ALWAYS,
/// whatever else is going on; neither is ever disabled here.
/// </summary>
public sealed class LeashGateCard : Grid
{
    private Punishment? _p;
    private int _pardons;
    private int _done;
    private int _total;
    private bool _running;
    private readonly Border _card = new();
    private readonly StackPanel _body = new();
    private readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false };

    public event Action<Punishment>? StartRequested;
    public event Action<Punishment>? PardonRequested;
    public event Action? PanicRequested;
    public event Action? CutRequested;

    public LeashGateCard()
    {
        Visibility = Visibility.Collapsed;
        Background = FriendsLook.Frozen(Color.FromArgb(0xD8, 0x08, 0x05, 0x10));
        Tag = "leash-gate";
        _card.Width = 400;
        _card.HorizontalAlignment = HorizontalAlignment.Center;
        _card.VerticalAlignment = VerticalAlignment.Center;
        _card.CornerRadius = new CornerRadius(18);
        _card.Padding = new Thickness(22, 20, 22, 18);
        _card.BorderBrush = FriendsLook.Frozen(Color.FromArgb(0x88, 0xFF, 0x5F, 0x7A));
        _card.BorderThickness = new Thickness(1);
        _card.Background = new RadialGradientBrush(FriendsLook.Rgb(0x3A, 0x10, 0x30), FriendsLook.Rgb(0x0D, 0x09, 0x14))
        {
            Center = new Point(0.5, 0),
            GradientOrigin = new Point(0.5, 0),
            RadiusX = 0.9,
            RadiusY = 0.8,
        };
        _card.Effect = FriendsLook.Glow(FriendsLook.Red, 30, 0.25);
        var inner = new Grid();
        inner.Children.Add(_body);
        inner.Children.Add(_fx);
        _card.Child = inner;
        Children.Add(_card);
    }

    public bool IsUp => Visibility == Visibility.Visible;
    public string? Pid => _p?.Pid;
    internal (int Done, int Total) ProgressShown => (_done, _total);

    /// <summary>Shows (or refreshes) the card for <paramref name="p"/>. The stamp lands once per punishment.</summary>
    public void Present(Punishment p, int pardons)
    {
        bool fresh = _p?.Pid != p.Pid || !IsUp;
        if (_p?.Pid != p.Pid) { _done = 0; _total = LeashUiRules.Pips(p); _running = false; }
        _p = p;
        _pardons = pardons;
        Render();
        if (!IsUp)
        {
            Visibility = Visibility.Visible;
            if (LeashFx.Amount > 0)
            {
                Opacity = 0;
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
                LeashFx.Thud(_card, 1.06);
            }
        }
        if (fresh) StampIn();
    }

    public void Dismiss()
    {
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        Visibility = Visibility.Collapsed;
    }

    /// <summary>The runner moved: pips fill, the button relabels, a pip pops.</summary>
    public void SetProgress(int done, int total, bool running)
    {
        bool moved = done > _done;
        _done = done;
        _total = total;
        _running = running;
        Render();
        if (moved && _pipRow != null && done - 1 < _pipRow.Children.Count && done > 0 && _pipRow.Children[done - 1] is FrameworkElement pip)
        {
            LeashFx.Thud(pip, 1.4);
            LeashFx.Sparks(_fx, pip, (int)(8 * LeashFx.Amount), FriendsLook.Pink, FriendsLook.Gold);
        }
    }

    private StackPanel? _pipRow;
    private Border? _stamp;

    private void Render()
    {
        _body.Children.Clear();
        _pipRow = null;
        if (_p is not { } p) return;

        var top = new Grid();
        var help = LeashLook.Help(LeashExplainRole.Gate);
        help.HorizontalAlignment = HorizontalAlignment.Left;
        help.VerticalAlignment = VerticalAlignment.Top;
        top.Children.Add(help);
        _stamp = LeashLook.Stamp(Loc.Get("leash_gate_stamp"), FriendsLook.Red);
        _stamp.HorizontalAlignment = HorizontalAlignment.Right;
        _stamp.VerticalAlignment = VerticalAlignment.Top;
        top.Children.Add(_stamp);
        var from = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var av = FriendsLook.Avatar(p.From.Name, p.From.AvatarUrl, 26);
        av.Margin = new Thickness(0, 0, 8, 0);
        from.Children.Add(av);
        from.Children.Add(FriendsLook.Label(Loc.GetF("leash_gate_says", p.From.Name), 13.5, FriendsLook.GoldBrush, null, FontWeights.SemiBold));
        top.Children.Add(from);
        _body.Children.Add(top);

        var (key, arg) = LeashUiRules.GateTitle(p);
        var big = LeashLook.Wrap(FriendsLook.Label(Loc.GetF(key, arg), 30, FriendsLook.TextBrush, FriendsLook.Display, FontWeights.SemiBold));
        big.TextAlignment = TextAlignment.Center;
        big.HorizontalAlignment = HorizontalAlignment.Center;
        big.Margin = new Thickness(0, 12, 0, 0);
        big.Tag = "leash-gate-title";
        _body.Children.Add(big);

        _body.Children.Add(ProgressView(p));

        var go = LeashLook.Chunky(_running ? Loc.Get("leash_gate_running") : Loc.Get(LeashUiRules.GateActionKey(p, _done)),
            LeashLook.Tone.Pink, PunishIcon(p.Kind), size: 16);
        go.Margin = new Thickness(0, 14, 0, 0);
        go.Tag = "leash-gate-go";
        go.IsEnabled = !_running;
        go.Click += (_, _) => { if (_p != null) StartRequested?.Invoke(_p); };
        _body.Children.Add(go);

        if (_pardons > 0)
        {
            var pardon = LeashLook.Chunky(Loc.GetF("leash_gate_pardon", _pardons), LeashLook.Tone.Ghost, "star", size: 12.5);
            pardon.Margin = new Thickness(0, 8, 0, 0);
            pardon.Tag = "leash-gate-pardon";
            pardon.Click += (_, _) => { if (_p != null) PardonRequested?.Invoke(_p); };
            _body.Children.Add(pardon);
        }

        var hours = Math.Max(1, (int)Math.Ceiling((p.ExpiresAt - DateTimeOffset.UtcNow).TotalHours));
        var note = FriendsLook.Label(Loc.GetF("leash_gate_expires", hours), 11, FriendsLook.DimBrush);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.Margin = new Thickness(0, 10, 0, 0);
        _body.Children.Add(note);

        // The two ways out. Always here, never disabled.
        var exits = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, 0, 0) };
        var panic = Exit(Loc.Get("leash_gate_panic"), FriendsLook.RedBrush, "panic", "leash-gate-panic");
        panic.Margin = new Thickness(0, 0, 4, 0);
        panic.Click += (_, _) => PanicRequested?.Invoke();
        var cut = Exit(Loc.Get("leash_cut"), FriendsLook.MintBrush, "scissors", "leash-gate-cut");
        cut.Margin = new Thickness(4, 0, 0, 0);
        cut.Click += (_, _) => CutRequested?.Invoke();
        exits.Children.Add(panic);
        exits.Children.Add(cut);
        _body.Children.Add(exits);
    }

    private static Button Exit(string text, Brush accent, string icon, string tag)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var ic = LeashLook.Icon(icon, accent, 14);
        ic.Margin = new Thickness(0, 0, 6, 0);
        sp.Children.Add(ic);
        sp.Children.Add(FriendsLook.Label(text, 13, accent, FriendsLook.Display, FontWeights.SemiBold));
        var b = FriendsLook.Pill(sp, FriendsLook.Frozen(FriendsLook.Rgb(0x2E, 0x20, 0x46)), accent, FriendsLook.Line2Brush, 10, new Thickness(8, 7, 8, 7));
        b.Tag = tag;
        return b;
    }

    private FrameworkElement ProgressView(Punishment p)
    {
        if (LeashUiRules.Pips(p) > 0)
        {
            _pipRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), Tag = "leash-gate-pips" };
            int n = LeashUiRules.Pips(p);
            double w = n > 6 ? 24 : 32;
            for (int i = 0; i < n; i++)
            {
                bool done = i < _done;
                var pip = new Border
                {
                    Width = w,
                    Height = w * 1.3,
                    CornerRadius = new CornerRadius(7),
                    BorderThickness = new Thickness(2),
                    BorderBrush = done ? FriendsLook.PinkBrush : FriendsLook.Frozen(FriendsLook.Rgb(0x4A, 0x35, 0x61)),
                    Background = done ? FriendsLook.PinkBrush : Brushes.Transparent,
                    Margin = new Thickness(3, 0, 3, 0),
                    Tag = done ? "leash-pip-done" : "leash-pip",
                };
                if (done) pip.Child = new Viewbox { Margin = new Thickness(5), Child = (UIElement)LeashLook.Icon("lock", LeashLook.InkBrush, 16) };
                _pipRow.Children.Add(pip);
            }
            return _pipRow;
        }
        var track = new Grid { Height = 12, Margin = new Thickness(10, 16, 10, 0), Tag = "leash-gate-bar" };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = FriendsLook.Frozen(FriendsLook.Rgb(0x2A, 0x1C, 0x40)) });
        double frac = _total > 0 ? Math.Clamp(_done / (double)_total, 0, 1) : 0;
        var fill = new Border
        {
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush(FriendsLook.Pink, FriendsLook.Gold, 0),
            Width = 356 * frac,
            Effect = FriendsLook.Glow(FriendsLook.Pink, 10, 0.6),
        };
        track.Children.Add(fill);
        return track;
    }

    private static string PunishIcon(PunishKind k) => k switch
    {
        PunishKind.Lines => "lock",
        PunishKind.Pink => "eye",
        PunishKind.Bubbles => "bubble",
        PunishKind.Video => "play",
        _ => "clock",
    };

    private void StampIn()
    {
        if (_stamp == null) return;
        LeashFx.Stamp();
        double k = LeashFx.Amount;
        if (k <= 0) return;
        var rot = new RotateTransform(10);
        var sc = new ScaleTransform(1, 1);
        var g = new TransformGroup();
        g.Children.Add(sc);
        g.Children.Add(rot);
        _stamp.RenderTransform = g;
        var a = new DoubleAnimation(1 + 1.4 * k, 1, BezierEase.Thud) { EasingFunction = new BezierEase(), BeginTime = TimeSpan.FromMilliseconds(180) };
        sc.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        sc.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-14 * k, 10, BezierEase.Thud) { EasingFunction = new BezierEase(), BeginTime = TimeSpan.FromMilliseconds(180) });
    }
}
