// PORTED from WPF 7.1.5 Controls/Leash/LeashGateCard.cs (304 lines): THE GATE, leashed side. The
// oldest pending punishment stands over the panel at the next idle moment: "Vex says / Lines, five
// times", progress pips (or a bar), the one action, a PUNISHED stamp, the pardon button when a
// token is held. Panic and Cut leash sit on the card ALWAYS, whatever else is going on; neither is
// ever disabled here.
// Flat on this head: the WPF glow Effects are a BoxShadow on the card and nothing on the bar
// (Avalonia effect/cache rule). The stamp lands with the house thud; WPF's rotation swing is not
// ported (the stamp sits at its 10 degree rest).
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using LeashUiRules = ConditioningControlPanel.Controls.Leash.LeashUiRules;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;

public sealed class LeashGateCard : Grid
{
    private Punishment? _p;
    private int _pardons;
    private int _done;
    private int _total;
    private bool _running;
    private bool _unplayable;
    private readonly Border _card = new();
    private readonly StackPanel _body = new();
    private readonly Canvas _fx = new() { IsHitTestVisible = false, ClipToBounds = false };
    private StackPanel? _pipRow;
    private Control? _stamp;

    public event Action<Punishment>? StartRequested;
    public event Action<Punishment>? PardonRequested;
    public event Action? PanicRequested;
    public event Action? CutRequested;
    /// <summary>"Not now" on a video that will not play: the gate stands down (the punishment
    /// stays pending and stays off the gate for the day).</summary>
    public event Action? LaterRequested;

    /// <summary>True while the card shows a punishment video that will not play.</summary>
    public bool ShowsUnplayable => IsUp && _unplayable;

    public LeashGateCard()
    {
        IsVisible = false;
        Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x08, 0x05, 0x10));
        Tag = "leash-gate";
        _card.Width = 400;
        _card.HorizontalAlignment = HorizontalAlignment.Center;
        _card.VerticalAlignment = VerticalAlignment.Center;
        _card.CornerRadius = new CornerRadius(18);
        _card.Padding = new Thickness(22, 20, 22, 18);
        _card.BorderBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x5F, 0x7A));
        _card.BorderThickness = new Thickness(1);
        _card.Background = new RadialGradientBrush
        {
            Center = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            GradientOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(0.8, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromRgb(0x3A, 0x10, 0x30), 0), new GradientStop(Color.FromRgb(0x0D, 0x09, 0x14), 1) },
        };
        _card.BoxShadow = new BoxShadows(new BoxShadow { Blur = 30, Color = Color.FromArgb(0x40, 0xFF, 0x5F, 0x7A) });
        var inner = new Grid();
        inner.Children.Add(_body);
        inner.Children.Add(_fx);
        _card.Child = inner;
        Children.Add(_card);
    }

    public bool IsUp => IsVisible;
    public string? Pid => _p?.Pid;
    internal (int Done, int Total) ProgressShown => (_done, _total);
    internal bool Running => _running;

    /// <summary>Shows (or refreshes) the card for <paramref name="p"/>. The stamp lands once per punishment.</summary>
    public void Present(Punishment p, int pardons, bool unplayable = false)
    {
        bool fresh = _p?.Pid != p.Pid || !IsUp;
        if (_p?.Pid != p.Pid) { _done = 0; _total = LeashUiRules.Pips(p); _running = false; }
        _p = p;
        _pardons = pardons;
        _unplayable = unplayable;
        if (unplayable) _running = false;
        Render();
        if (!IsUp)
        {
            IsVisible = true;
            if (LeashFx.Amount > 0) LeashFx.Thud(_card, 1.06);
        }
        if (fresh)
        {
            StampIn();
            // The panel only presents the gate while it is up and idle: the punishment is on screen.
            try { LeashLocator.Service()?.NoteShown(p.Pid); }
            catch (Exception ex) { Serilog.Log.Debug("[Leash] gate seen note failed: {E}", ex.Message); }
        }
    }

    public void Dismiss() => IsVisible = false;

    /// <summary>The runner moved: pips fill, the button relabels, a pip pops.</summary>
    public void SetProgress(int done, int total, bool running)
    {
        bool moved = done > _done;
        _done = done;
        _total = total;
        _running = running;
        Render();
        if (moved && _pipRow != null && done > 0 && done - 1 < _pipRow.Children.Count && _pipRow.Children[done - 1] is Control pip)
        {
            LeashFx.Thud(pip, 1.4);
            LeashFx.Sparks(_fx, pip, (int)(8 * LeashFx.Amount), LeashFx.PinkC, LeashFx.GoldC);
        }
    }

    /// <summary>The runner went idle without finishing: the pips stay, the button is live again.</summary>
    public void StopRunning()
    {
        if (!_running) return;
        _running = false;
        Render();
    }

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
        // The stamp keeps its tilt on the inner plate; the host takes the landing thud.
        _stamp = new Border
        {
            Child = LeashLook.Stamp(Loc.Get("leash_gate_stamp"), Color.FromRgb(0xFF, 0x5F, 0x7A)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
        };
        top.Children.Add(_stamp);
        var from = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        var av = FriendsDrawer.Avatar(p.From.Name, 26, null, p.From.AvatarUrl);
        av.Margin = new Thickness(0, 0, 8, 0);
        from.Children.Add(av);
        var says = FriendsDrawer.Label(Loc.GetF("leash_gate_says", p.From.Name), 13.5, FriendsDrawer.Gold, null, FontWeight.SemiBold);
        says.VerticalAlignment = VerticalAlignment.Center;
        from.Children.Add(says);
        top.Children.Add(from);
        _body.Children.Add(top);

        var (key, arg) = LeashUiRules.GateTitle(p);
        var big = LeashLook.Wrap(FriendsDrawer.Label(Loc.GetF(key, arg), 30, FriendsDrawer.Text, FriendsDrawer.Display, FontWeight.SemiBold));
        big.TextAlignment = TextAlignment.Center;
        big.HorizontalAlignment = HorizontalAlignment.Center;
        big.Margin = new Thickness(0, 12, 0, 0);
        big.Tag = "leash-gate-title";
        _body.Children.Add(big);

        if (_unplayable)
        {
            // The video would not play and the holder could not be told: say so, no Play button,
            // Pardon and Cut stay, "Not now" stands the gate down.
            var why = LeashLook.Wrap(FriendsDrawer.Label(Loc.GetF("leash_gate_unplayable", p.From.Name), 13, FriendsDrawer.Gold, null, FontWeight.SemiBold));
            why.TextAlignment = TextAlignment.Center;
            why.HorizontalAlignment = HorizontalAlignment.Center;
            why.Margin = new Thickness(0, 12, 0, 0);
            why.Tag = "leash-gate-unplayable";
            _body.Children.Add(why);
            var later = LeashLook.Chunky(Loc.Get("leash_gate_later"), LeashLook.Tone.Ghost, "leash-gate-later", 14);
            later.Margin = new Thickness(0, 14, 0, 0);
            later.HorizontalAlignment = HorizontalAlignment.Stretch;
            later.Click += (_, _) => LaterRequested?.Invoke();
            _body.Children.Add(later);
        }
        else
        {
            _body.Children.Add(ProgressView(p));

            var go = LeashLook.Chunky(_running ? Loc.Get("leash_gate_running") : Loc.Get(LeashUiRules.GateActionKey(p, _done)),
                LeashLook.Tone.Pink, "leash-gate-go", 16);
            if (go.Content is TextBlock word)
            {
                go.Content = null;
                var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                var ic = LeashLook.Icon(PunishIcon(p.Kind), LeashLook.InkBrush, 16);
                ic.Margin = new Thickness(0, 0, 7, 0);
                row.Children.Add(ic);
                row.Children.Add(word);
                go.Content = row;
            }
            go.Margin = new Thickness(0, 14, 0, 0);
            go.HorizontalAlignment = HorizontalAlignment.Stretch;
            go.IsEnabled = !_running;
            go.Click += (_, _) => { if (_p != null) StartRequested?.Invoke(_p); };
            _body.Children.Add(go);
        }

        if (_pardons > 0)
        {
            var pardon = LeashLook.Chunky(Loc.GetF("leash_gate_pardon", _pardons), LeashLook.Tone.Ghost, "leash-gate-pardon", 12.5);
            pardon.Margin = new Thickness(0, 8, 0, 0);
            pardon.HorizontalAlignment = HorizontalAlignment.Stretch;
            pardon.Click += (_, _) => { if (_p != null) PardonRequested?.Invoke(_p); };
            _body.Children.Add(pardon);
        }

        var hours = Math.Max(1, (int)Math.Ceiling((p.ExpiresAt - DateTimeOffset.UtcNow).TotalHours));
        var note = FriendsDrawer.Label(Loc.GetF("leash_gate_expires", hours), 11, FriendsDrawer.Dim);
        note.HorizontalAlignment = HorizontalAlignment.Center;
        note.Margin = new Thickness(0, 10, 0, 0);
        _body.Children.Add(note);

        // The two ways out. Always here, never disabled.
        var exits = new UniformGrid { Columns = 2, Margin = new Thickness(0, 14, 0, 0) };
        var panic = Exit(Loc.Get("leash_gate_panic"), FriendsDrawer.Red, "panic", "leash-gate-panic");
        panic.Margin = new Thickness(0, 0, 4, 0);
        panic.Click += (_, _) => PanicRequested?.Invoke();
        var cut = Exit(Loc.Get("leash_cut"), FriendsDrawer.Mint, "scissors", "leash-gate-cut");
        cut.Margin = new Thickness(4, 0, 0, 0);
        cut.Click += (_, _) => CutRequested?.Invoke();
        exits.Children.Add(panic);
        exits.Children.Add(cut);
        _body.Children.Add(exits);
    }

    private static Button Exit(string text, IBrush accent, string icon, string tag)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var ic = LeashLook.Icon(icon, accent, 14);
        ic.Margin = new Thickness(0, 0, 6, 0);
        sp.Children.Add(ic);
        sp.Children.Add(FriendsDrawer.Label(text, 13, accent, FriendsDrawer.Display, FontWeight.SemiBold));
        var b = FriendsDrawer.Pill(sp, new SolidColorBrush(Color.FromRgb(0x2E, 0x20, 0x46)), accent, tag, FriendsDrawer.Line2);
        b.CornerRadius = new CornerRadius(10);
        b.Padding = new Thickness(8, 7, 8, 7);
        b.HorizontalAlignment = HorizontalAlignment.Stretch;
        b.HorizontalContentAlignment = HorizontalAlignment.Center;
        b.Cursor = FriendsDrawer.Hand();
        return b;
    }

    private Control ProgressView(Punishment p)
    {
        int n = LeashUiRules.Pips(p);
        if (n > 0)
        {
            _pipRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0), Tag = "leash-gate-pips" };
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
                    BorderBrush = done ? FriendsDrawer.Pink : new SolidColorBrush(Color.FromRgb(0x4A, 0x35, 0x61)),
                    Background = done ? FriendsDrawer.Pink : Brushes.Transparent,
                    Margin = new Thickness(3, 0, 3, 0),
                    Tag = done ? "leash-pip-done" : "leash-pip",
                };
                if (done) pip.Child = new Viewbox { Margin = new Thickness(5), Child = LeashLook.Icon("lock", LeashLook.InkBrush, 16) };
                _pipRow.Children.Add(pip);
            }
            return _pipRow;
        }
        var track = new Grid { Height = 12, Margin = new Thickness(10, 16, 10, 0), Tag = "leash-gate-bar" };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x1C, 0x40)) });
        double frac = _total > 0 ? Math.Clamp(_done / (double)_total, 0, 1) : 0;
        track.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(LeashFx.PinkC, 0), new GradientStop(LeashFx.GoldC, 1) },
            },
            Width = 356 * frac,
        });
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
        LeashFx.Thud(_stamp, 2.4);   // WPF lands from 1 + 1.4 x amount; Thud scales by the amount itself
    }
}
