// PORTED from WPF 7.1.5 Controls/Leash/Explain/LeashExplainCard.cs: the embeddable explainer. Four
// picture panels (a friend offers / you put it on and pick how strict / they see and steer / you
// cut it, any time) and two lines under them; holder and leashed variants differ only in captions
// and lines. Motion: panels stagger in, the strict bars pop, the eye blinks once, the scissors snip
// with a small burst, once on the first attach. Motion Off: nothing moves; the resting picture is
// the finished one. Copy rule (owner): a cut is never priced; the lines say what a cut does.
// Avalonia: every move through TransformTween / opacity 0..1 tweens; no Effect.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash.Explain;

/// <summary>How the four pictures sit: one row (the window) or two by two (the ask card).</summary>
public enum LeashExplainLayout { Row, Grid }

public sealed class LeashExplainCard : Border
{
    public LeashIntroSide Side { get; }
    public LeashExplainLayout Layout { get; }
    public string? OtherName { get; }

    public IReadOnlyList<Border> Panels => _panels;
    public IReadOnlyList<TextBlock> Lines => _lines;

    /// <summary>Raised once, when this card has been on screen for LeashIntroRule.AskReadMs.</summary>
    public event Action? ReadyForAnswer;
    public bool IsReadyForAnswer { get; private set; }

    private readonly List<Border> _panels = new();
    private readonly List<TextBlock> _lines = new();
    private Rectangle[] _bars = Array.Empty<Rectangle>();
    private Control? _eye;
    private LeashExplainArt.Scissors? _scissors;
    private Canvas? _burstLayer;
    private bool _played;
    private DispatcherTimer? _readTimer;

    public LeashExplainCard(LeashIntroSide side, string? name = null, LeashExplainLayout layout = LeashExplainLayout.Row)
    {
        Side = side;
        Layout = layout;
        OtherName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Tag = "leash-explain-card:" + (side == LeashIntroSide.Holder ? "holder" : "leashed");
        Background = Brushes.Transparent;
        VerticalAlignment = VerticalAlignment.Top;
        Child = Build();
        AttachedToVisualTree += (_, _) => Play();
        DetachedFromVisualTree += (_, _) => StopReadClock();
    }

    // ---- copy ------------------------------------------------------------------------------

    public static string StepKey(int step, LeashIntroSide side)
        => $"leash_explain_step{step}_{(side == LeashIntroSide.Holder ? "holder" : "leashed")}";

    public static (string see, string cut) LinesFor(LeashIntroSide side, string? name)
    {
        if (side == LeashIntroSide.Holder)
            return (Loc.Get("leash_explain_holder_see"), Loc.Get("leash_explain_holder_cut"));
        var see = string.IsNullOrWhiteSpace(name)
            ? Loc.Get("leash_explain_leashed_see_anon")
            : Loc.GetF("leash_explain_leashed_see", name.Trim());
        string? key = null;
        try { key = CoreSettings.Current?.PanicKey; } catch { }
        return (see, Loc.GetF("leash_explain_leashed_cut", LeashHoldToCut.KeyLabel(key)));
    }

    // ---- build -------------------------------------------------------------------------------

    private Control Build()
    {
        var root = new StackPanel();
        _burstLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = false };

        var art = new Control[]
        {
            LeashExplainArt.Offer(),
            LeashExplainArt.PutOn(out _bars),
            LeashExplainArt.SeeAndSteer(out _eye),
            (_scissors = LeashExplainArt.Cut()).Canvas,
        };

        var grid = new UniformGrid
        {
            Columns = Layout == LeashExplainLayout.Row ? 4 : 2,
            Rows = Layout == LeashExplainLayout.Row ? 1 : 2,
        };
        for (int i = 0; i < 4; i++)
        {
            var p = Panel(i + 1, art[i], Loc.Get(StepKey(i + 1, Side)), safety: i == 3);
            _panels.Add(p);
            grid.Children.Add(p);
        }
        var stage = new Grid();
        stage.Children.Add(grid);
        stage.Children.Add(_burstLayer);
        root.Children.Add(stage);

        var (see, cut) = LinesFor(Side, OtherName);
        var bullets = new StackPanel { Margin = new Thickness(6, Layout == LeashExplainLayout.Row ? 12 : 8, 6, 0) };
        bullets.Children.Add(Bullet(LeashExplainArt.EyeGlyph(), see, "leash-explain-line:see"));
        bullets.Children.Add(Bullet(LeashExplainArt.ScissorsGlyph(), cut, "leash-explain-line:cut"));
        root.Children.Add(bullets);
        return root;
    }

    private Border Panel(int number, Control art, string caption, bool safety)
    {
        bool small = Layout == LeashExplainLayout.Grid;
        var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        body.Children.Add(new Viewbox { Child = art, Stretch = Stretch.Uniform, Height = small ? 58 : 80, Margin = new Thickness(0, small ? 6 : 8, 0, 0) });

        var cap = new TextBlock
        {
            Text = caption,
            FontFamily = FriendsDrawer.Display,
            FontWeight = FontWeight.SemiBold,
            FontSize = small ? 12 : 13.5,
            Foreground = safety ? FriendsDrawer.Mint : FriendsDrawer.Text,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 2, 4, small ? 6 : 10),
            Tag = "leash-explain-caption",
        };
        Grid.SetRow(cap, 1);
        body.Children.Add(cap);

        body.Children.Add(new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = safety ? FriendsDrawer.Mint : FriendsDrawer.Line2,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 6, 0, 0),
            RenderTransform = new RotateTransform(number % 2 == 0 ? 6 : -6),
            Child = new TextBlock
            {
                Text = number.ToString(),
                FontFamily = FriendsDrawer.Mono,
                FontSize = 10.5,
                FontWeight = FontWeight.Bold,
                Foreground = safety ? FriendsDrawer.MintInk : FriendsDrawer.Text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });

        return new Border
        {
            Child = body,
            CornerRadius = new CornerRadius(14),
            Background = LeashLook.GlassBrush,
            BorderBrush = safety ? new SolidColorBrush(Color.FromArgb(0x99, 0x5F, 0xFF, 0xD0)) : FriendsDrawer.Line,
            BorderThickness = new Thickness(safety ? 1.5 : 1),
            Margin = new Thickness(small ? 3 : 4),
            Tag = "leash-explain-panel:" + number,
            RenderTransform = new ScaleTransform(1, 1),
        };
    }

    private Grid Bullet(Control glyph, string text, string tag)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3), Tag = tag, ColumnDefinitions = new ColumnDefinitions("26,*") };
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        glyph.Margin = new Thickness(2, 3, 0, 0);
        g.Children.Add(glyph);
        var t = new TextBlock { Text = text, FontSize = 13, Foreground = FriendsDrawer.Text, TextWrapping = TextWrapping.Wrap, LineHeight = 18 };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        _lines.Add(t);
        return g;
    }

    // ---- the read clock (ask card) ------------------------------------------------------------

    /// <summary>ReadyForAnswer fires once the card has been visible for LeashIntroRule.AskReadMs
    /// (at once with <paramref name="alreadySeen"/>). The first call wins.</summary>
    public void StartReadClock(bool alreadySeen = false)
    {
        if (IsReadyForAnswer || _readTimer != null) return;
        if (LeashIntroRule.PutItOnEnabled(alreadySeen, TimeSpan.Zero)) { MarkReady(); return; }
        _readTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(ReadTickMs), DispatcherPriority.Normal, (_, _) =>
        {
            // Time only counts while the card is actually on screen.
            if (IsEffectivelyVisible && global::Avalonia.VisualTree.VisualExtensions.IsAttachedToVisualTree(this)) _visible += TimeSpan.FromMilliseconds(ReadTickMs);
            if (LeashIntroRule.PutItOnEnabled(false, _visible)) MarkReady();
        });
        _readTimer.Start();
    }

    private const int ReadTickMs = 100;
    private TimeSpan _visible;

    /// <summary>Test seam: advance the visible time as if the card were on screen.</summary>
    internal void AdvanceReadClock(TimeSpan by)
    {
        _visible += by;
        if (LeashIntroRule.PutItOnEnabled(false, _visible)) MarkReady();
    }

    private void MarkReady()
    {
        StopReadClock();
        if (IsReadyForAnswer) return;
        IsReadyForAnswer = true;
        try { ReadyForAnswer?.Invoke(); } catch { }
    }

    private void StopReadClock()
    {
        _readTimer?.Stop();
        _readTimer = null;
    }

    // ---- juice --------------------------------------------------------------------------------

    /// <summary>Run the entrance once (first attach; public for the window).</summary>
    public void Play()
    {
        if (_played) return;
        _played = true;
        if (LeashFx.Amount <= 0) return;   // the resting picture is already the finished one

        for (int i = 0; i < _panels.Count; i++)
        {
            var p = _panels[i];
            p.Opacity = 0;
            var tt = new TranslateTransform(0, 10);
            p.RenderTransform = tt;
            After(i * 70, () =>
            {
                TransformTween.Run(p, TimeSpan.FromMilliseconds(260), new (double, AvaloniaProperty, double)[] { (0.0, OpacityProperty, 0.0), (1.0, OpacityProperty, 1.0) });
                TransformTween.Run(tt, TimeSpan.FromMilliseconds(260), new (double, AvaloniaProperty, double)[] { (0.0, TranslateTransform.YProperty, 10.0), (1.0, TranslateTransform.YProperty, 0.0) }, new CubicEaseOut());
            });
        }
        After(260, PopBars);
        After(520, Blink);
        After(760, Snip);
    }

    private static void After(int ms, Action a) => DispatcherTimer.RunOnce(() => { try { a(); } catch { } }, TimeSpan.FromMilliseconds(Math.Max(1, ms)));

    private void PopBars()
    {
        for (int i = 0; i < _bars.Length; i++)
        {
            if (_bars[i].RenderTransform is not ScaleTransform s) continue;
            s.ScaleY = 0.2;
            After(i * 70, () => TransformTween.Run(s, TimeSpan.FromMilliseconds(340),
                new (double, AvaloniaProperty, double)[] { (0.0, ScaleTransform.ScaleYProperty, 0.2), (1.0, ScaleTransform.ScaleYProperty, 1.0) }, new BackEaseOut()));
        }
    }

    private void Blink()
    {
        if (_eye?.RenderTransform is not ScaleTransform s) return;
        TransformTween.Run(s, TimeSpan.FromMilliseconds(260), new (double, AvaloniaProperty, double)[]
        {
            (0.0, ScaleTransform.ScaleYProperty, 1.0), (0.42, ScaleTransform.ScaleYProperty, 0.1), (1.0, ScaleTransform.ScaleYProperty, 1.0),
        });
    }

    /// <summary>The snip: blades open, close hard, the halves jump apart, the last panel thuds,
    /// a few gold bits fly off the cut.</summary>
    private void Snip()
    {
        if (_scissors == null) return;
        const int open = 160, close = 140, cutAt = open + close;
        double o = open / (double)cutAt;
        TransformTween.Run(_scissors.BladeA, TimeSpan.FromMilliseconds(cutAt), new (double, AvaloniaProperty, double)[]
            { (0.0, RotateTransform.AngleProperty, 0.0), (o, RotateTransform.AngleProperty, -18.0), (1.0, RotateTransform.AngleProperty, 0.0) });
        TransformTween.Run(_scissors.BladeB, TimeSpan.FromMilliseconds(cutAt), new (double, AvaloniaProperty, double)[]
            { (0.0, RotateTransform.AngleProperty, 0.0), (o, RotateTransform.AngleProperty, 18.0), (1.0, RotateTransform.AngleProperty, 0.0) });

        _scissors.LeftHalf.X = 5;
        _scissors.RightHalf.X = -5;
        var left = _scissors.LeftHalf;
        var right = _scissors.RightHalf;
        After(cutAt, () =>
        {
            TransformTween.Run(left, TimeSpan.FromMilliseconds(340), new (double, AvaloniaProperty, double)[] { (0.0, TranslateTransform.XProperty, 5.0), (1.0, TranslateTransform.XProperty, 0.0) }, new BackEaseOut());
            TransformTween.Run(right, TimeSpan.FromMilliseconds(340), new (double, AvaloniaProperty, double)[] { (0.0, TranslateTransform.XProperty, -5.0), (1.0, TranslateTransform.XProperty, 0.0) }, new BackEaseOut());
            if (_panels.Count == 4)
            {
                var ps = new ScaleTransform(1.05, 1.05);
                _panels[3].RenderTransform = ps;
                TransformTween.Run(ps, TimeSpan.FromMilliseconds(340), new (double, AvaloniaProperty, double)[]
                {
                    (0.0, ScaleTransform.ScaleXProperty, 1.05), (0.0, ScaleTransform.ScaleYProperty, 1.05),
                    (1.0, ScaleTransform.ScaleXProperty, 1.0), (1.0, ScaleTransform.ScaleYProperty, 1.0),
                }, new BackEaseOut());
            }
            Burst();
        });
    }

    /// <summary>Six gold bits off the cut, only where the tier allows particles.</summary>
    private void Burst()
    {
        bool particles;
        try { particles = global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowParticles; } catch { particles = false; }
        if (!particles || _burstLayer == null || _scissors == null) return;
        var p = _scissors.Canvas.TranslatePoint(new Point(LeashExplainArt.Scissors.PivotX, LeashExplainArt.Scissors.CutY), _burstLayer);
        if (p is not { } origin) return;
        var rng = new Random();
        for (int i = 0; i < 6; i++)
        {
            var dot = new Ellipse { Width = 3.5, Height = 3.5, Fill = i % 3 == 0 ? FriendsDrawer.Mint : LeashExplainArt.GoldBrush, IsHitTestVisible = false };
            var tt = new TranslateTransform(origin.X - 1.75, origin.Y - 1.75);
            dot.RenderTransform = tt;
            _burstLayer.Children.Add(dot);
            double ang = -Math.PI / 2 + (i - 2.5) * 0.45 + (rng.NextDouble() - 0.5) * 0.3;
            double dist = 16 + rng.NextDouble() * 12;
            var dur = TimeSpan.FromMilliseconds(420 + rng.Next(160));
            TransformTween.Run(tt, dur, new (double, AvaloniaProperty, double)[]
            {
                (0.0, TranslateTransform.XProperty, tt.X), (1.0, TranslateTransform.XProperty, tt.X + Math.Cos(ang) * dist),
                (0.0, TranslateTransform.YProperty, tt.Y), (1.0, TranslateTransform.YProperty, tt.Y + Math.Sin(ang) * dist + 6),
            }, new QuadraticEaseOut());
            TransformTween.Run(dot, dur, new (double, AvaloniaProperty, double)[] { (0.0, OpacityProperty, 1.0), (1.0, OpacityProperty, 0.0) });
            var layer = _burstLayer;
            DispatcherTimer.RunOnce(() => layer.Children.Remove(dot), dur + TimeSpan.FromMilliseconds(40));
        }
    }
}
