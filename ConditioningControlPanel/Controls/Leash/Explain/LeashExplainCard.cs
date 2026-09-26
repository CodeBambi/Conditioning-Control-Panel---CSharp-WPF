using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Controls.Leash.Explain;

/// <summary>How the four pictures sit: one row (the explainer window) or two by two (the
/// 300 px friends drawer, where the ask card carries it above the intensity switch).</summary>
public enum LeashExplainLayout { Row, Grid }

/// <summary>
/// The leash explainer, the embeddable half: four picture panels (a friend offers / you put it
/// on and pick how strict / they see and steer / you cut it, any time) and two lines under them.
/// Holder and leashed variants differ only in the captions and the two lines.
///
/// <para>Motion: the panels stagger in, the strict bars pop, the eye blinks once, and the
/// scissors snip with a small burst, all on the first Loaded. Every clock goes through
/// <see cref="MotionFx"/>; with motion off nothing moves and the resting picture is the finished
/// one (leash on, bar picked, leash cut).</para>
///
/// <para>Copy rule (owner): a cut is never priced and never gated, but Chaster time already
/// pushed to a lock stays on the lock. The lines therefore say what a cut does (the leash comes
/// off, Strict Lock off, panic key on) and nothing about time.</para>
/// </summary>
public sealed class LeashExplainCard : Border
{
    public LeashIntroSide Side { get; }
    public LeashExplainLayout Layout { get; }
    public string? OtherName { get; }

    /// <summary>The four panels, in order, for tests and for anything that wants to point at one.</summary>
    public IReadOnlyList<Border> Panels => _panels;

    /// <summary>The two lines under the pictures.</summary>
    public IReadOnlyList<TextBlock> Lines => _lines;

    /// <summary>Raised once, when this card has been on screen for <see cref="LeashIntroRule.AskReadMs"/>
    /// (immediately if <see cref="StartReadClock"/> is told the side has already seen it).</summary>
    public event Action? ReadyForAnswer;

    /// <summary>True once <see cref="ReadyForAnswer"/> has fired.</summary>
    public bool IsReadyForAnswer { get; private set; }

    private readonly List<Border> _panels = new();
    private readonly List<TextBlock> _lines = new();
    private Rectangle[] _bars = Array.Empty<Rectangle>();
    private FrameworkElement? _eye;
    private LeashExplainArt.Scissors? _scissors;
    private Canvas? _burstLayer;
    private bool _played;
    private DispatcherTimer? _readTimer;

    /// <param name="name">The other person's name, used by the leashed variant's first line.
    /// Null or empty falls back to "They".</param>
    public LeashExplainCard(LeashIntroSide side, string? name = null, LeashExplainLayout layout = LeashExplainLayout.Row)
    {
        Side = side;
        Layout = layout;
        OtherName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Tag = "leash-explain-card:" + (side == LeashIntroSide.Holder ? "holder" : "leashed");
        Background = Brushes.Transparent;
        VerticalAlignment = VerticalAlignment.Top;
        Child = Build();
        Loaded += (_, _) => Play();
        Unloaded += (_, _) => StopReadClock();
    }

    // ---- copy (static so the rule tests can pin it without a UI) ---------------------------

    public static string StepKey(int step, LeashIntroSide side)
        => $"leash_explain_step{step}_{(side == LeashIntroSide.Holder ? "holder" : "leashed")}";

    /// <summary>The two lines for a side. Pure apart from the loc lookup.</summary>
    public static (string see, string cut) LinesFor(LeashIntroSide side, string? name)
    {
        if (side == LeashIntroSide.Holder)
            return (Loc.Get("leash_explain_holder_see"), Loc.Get("leash_explain_holder_cut"));
        var see = string.IsNullOrWhiteSpace(name)
            ? Loc.Get("leash_explain_leashed_see_anon")
            : Loc.GetF("leash_explain_leashed_see", name.Trim());
        return (see, Loc.Get("leash_explain_leashed_cut"));
    }

    // ---- build -------------------------------------------------------------------------------

    private UIElement Build()
    {
        var root = new StackPanel();
        _burstLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = false };

        var art = new FrameworkElement[]
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

    private Border Panel(int number, FrameworkElement art, string caption, bool safety)
    {
        bool small = Layout == LeashExplainLayout.Grid;
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var pic = new Viewbox { Child = art, Stretch = Stretch.Uniform, Height = small ? 58 : 80, Margin = new Thickness(0, small ? 6 : 8, 0, 0) };
        body.Children.Add(pic);

        var cap = new TextBlock
        {
            Text = caption,
            FontFamily = FriendsLook.Display,
            FontWeight = FontWeights.SemiBold,
            FontSize = small ? 12 : 13.5,
            Foreground = safety ? FriendsLook.MintBrush : FriendsLook.TextBrush,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(4, 2, 4, small ? 6 : 10),
            Tag = "leash-explain-caption",
        };
        Grid.SetRow(cap, 1);
        body.Children.Add(cap);

        // The step number, a small mono tag pinned to the corner, tilted a hair like a stamp.
        var num = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = safety ? FriendsLook.MintBrush : FriendsLook.Frozen(FriendsLook.Line2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(6, 6, 0, 0),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(number % 2 == 0 ? 6 : -6),
            Child = new TextBlock
            {
                Text = number.ToString(),
                FontFamily = FriendsLook.Mono,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = safety ? FriendsLook.MintInkBrush : FriendsLook.TextBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        body.Children.Add(num);

        var mint = FriendsLook.Mint;
        return new Border
        {
            Child = body,
            CornerRadius = new CornerRadius(14),
            Background = FriendsLook.GlassBrush,
            BorderBrush = safety
                ? FriendsLook.Frozen(Color.FromArgb(0x99, mint.R, mint.G, mint.B))
                : FriendsLook.LineBrush,
            BorderThickness = new Thickness(safety ? 1.5 : 1),
            Margin = new Thickness(small ? 3 : 4),
            SnapsToDevicePixels = true,
            Tag = "leash-explain-panel:" + number,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1),
        };
    }

    private Grid Bullet(FrameworkElement glyph, string text, string tag)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3), Tag = tag };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        glyph.VerticalAlignment = VerticalAlignment.Top;
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        glyph.Margin = new Thickness(2, 3, 0, 0);
        g.Children.Add(glyph);
        var t = new TextBlock
        {
            Text = text,
            FontFamily = FriendsLook.Body,
            FontSize = 13,
            Foreground = FriendsLook.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 18,
        };
        Grid.SetColumn(t, 1);
        g.Children.Add(t);
        _lines.Add(t);
        return g;
    }

    // ---- the read clock (ask card) ------------------------------------------------------------

    /// <summary>
    /// Start the ask card's read clock: <see cref="ReadyForAnswer"/> fires once the card has been
    /// visible for <see cref="LeashIntroRule.AskReadMs"/>. With <paramref name="alreadySeen"/> it
    /// fires at once. Safe to call more than once; the first call wins.
    /// </summary>
    public void StartReadClock(bool alreadySeen = false)
    {
        if (IsReadyForAnswer || _readTimer != null) return;
        if (LeashIntroRule.PutItOnEnabled(alreadySeen, TimeSpan.Zero)) { MarkReady(); return; }
        _readTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(ReadTickMs) };
        _readTimer.Tick += (_, _) =>
        {
            // Time only counts while the card is actually on screen.
            if (IsVisible) _visible += TimeSpan.FromMilliseconds(ReadTickMs);
            if (LeashIntroRule.PutItOnEnabled(false, _visible)) MarkReady();
        };
        _readTimer.Start();
    }

    private const int ReadTickMs = 100;
    private TimeSpan _visible;

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

    /// <summary>Run the entrance once. Called on the first Loaded; public for the window.</summary>
    public void Play()
    {
        if (_played) return;
        _played = true;
        if (!MotionFx.AllowTransitions) return; // the resting picture is already the finished one

        MotionFx.StaggerIn(_panels);
        After(260, PopBars);
        After(520, Blink);
        After(760, Snip);
    }

    private void After(int ms, Action a)
    {
        var t = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); try { a(); } catch { } };
        t.Start();
    }

    private static readonly IEasingFunction Thud = new BackEase { Amplitude = 0.55, EasingMode = EasingMode.EaseOut };

    private void PopBars()
    {
        for (int i = 0; i < _bars.Length; i++)
        {
            if (_bars[i].RenderTransform is not ScaleTransform s) continue;
            var grow = new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(340))
            {
                BeginTime = TimeSpan.FromMilliseconds(i * 70),
                EasingFunction = Thud,
            };
            s.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
    }

    private void Blink()
    {
        if (_eye?.RenderTransform is not ScaleTransform s) return;
        var blink = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(260) };
        blink.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        blink.KeyFrames.Add(new EasingDoubleKeyFrame(0.1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(110))));
        blink.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260))));
        s.BeginAnimation(ScaleTransform.ScaleYProperty, blink);
    }

    /// <summary>The snip: blades open, close hard, the two halves of the leash jump apart, the
    /// last panel takes a small thud, and a few gold bits fly off the cut.</summary>
    private void Snip()
    {
        if (_scissors == null) return;
        var open = TimeSpan.FromMilliseconds(160);
        var close = TimeSpan.FromMilliseconds(140);
        AnimateAngle(_scissors.BladeA, -18, open, close);
        AnimateAngle(_scissors.BladeB, 18, open, close);

        var cutAt = open + close;
        Slide(_scissors.LeftHalf, 5, cutAt);
        Slide(_scissors.RightHalf, -5, cutAt);

        if (_panels.Count == 4 && _panels[3].RenderTransform is ScaleTransform ps)
        {
            var thud = new DoubleAnimation(1.05, 1, TimeSpan.FromMilliseconds(340))
            {
                BeginTime = cutAt,
                EasingFunction = new BackEase { Amplitude = 0.8, EasingMode = EasingMode.EaseOut },
            };
            ps.BeginAnimation(ScaleTransform.ScaleXProperty, thud);
            ps.BeginAnimation(ScaleTransform.ScaleYProperty, thud);
        }
        After((int)cutAt.TotalMilliseconds, Burst);
    }

    private static void AnimateAngle(RotateTransform t, double openAngle, TimeSpan open, TimeSpan close)
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = open + close };
        a.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(openAngle, KeyTime.FromTimeSpan(open),
            new SineEase { EasingMode = EasingMode.EaseOut }));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(open + close),
            new PowerEase { Power = 3, EasingMode = EasingMode.EaseIn }));
        t.BeginAnimation(RotateTransform.AngleProperty, a);
    }

    /// <summary>The halves start joined (shifted by <paramref name="joinedOffset"/>) and spring to
    /// their resting place at the cut.</summary>
    private static void Slide(TranslateTransform t, double joinedOffset, TimeSpan at)
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = at + TimeSpan.FromMilliseconds(340) };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(joinedOffset, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(joinedOffset, KeyTime.FromTimeSpan(at)));
        a.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(at + TimeSpan.FromMilliseconds(340)), Thud));
        t.BeginAnimation(TranslateTransform.XProperty, a);
    }

    /// <summary>Six gold bits off the cut. Particles only where the tier allows them.</summary>
    private void Burst()
    {
        if (!MotionFx.AllowParticles || _burstLayer == null || _scissors == null || _panels.Count < 4) return;
        Point origin;
        try
        {
            origin = _scissors.Canvas.TransformToAncestor(_burstLayer)
                .Transform(new Point(LeashExplainArt.Scissors.PivotX, LeashExplainArt.Scissors.CutY));
        }
        catch { return; }

        var rng = new Random();
        for (int i = 0; i < 6; i++)
        {
            var dot = new Ellipse { Width = 3.5, Height = 3.5, Fill = i % 3 == 0 ? FriendsLook.MintBrush : LeashExplainArt.GoldBrush };
            var tt = new TranslateTransform(origin.X - 1.75, origin.Y - 1.75);
            dot.RenderTransform = tt;
            _burstLayer.Children.Add(dot);
            double ang = -Math.PI / 2 + (i - 2.5) * 0.45 + (rng.NextDouble() - 0.5) * 0.3;
            double dist = 16 + rng.NextDouble() * 12;
            var dur = TimeSpan.FromMilliseconds(420 + rng.Next(160));
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tt.X + Math.Cos(ang) * dist, dur) { EasingFunction = ease });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(tt.Y + Math.Sin(ang) * dist + 6, dur) { EasingFunction = ease });
            var fade = new DoubleAnimation(1, 0, dur);
            var captured = dot;
            fade.Completed += (_, _) => _burstLayer.Children.Remove(captured);
            dot.BeginAnimation(OpacityProperty, fade);
        }
    }
}
