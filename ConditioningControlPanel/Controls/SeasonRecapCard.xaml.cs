using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using ConditioningControlPanel.ViewModels;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// Animated, fully data-bound Season Recap card (native WPF rebuild of season-card-mockup.html).
    ///
    /// Ambient loops: foil-edge shimmer, slow-rotating hypno spiral, holographic sweep. On reveal
    /// the two time figures and the rank count up. All of it can be frozen to a clean representative
    /// frame via <see cref="PrepareForStill"/> before the PNG export so the still never captures a
    /// mid-fade or a half-finished count-up.
    /// </summary>
    public partial class SeasonRecapCard : UserControl
    {
        private SeasonRecapViewModel? _vm;
        private DispatcherTimer? _countTimer;

        private Storyboard? _spiralStory;
        private Storyboard? _foilStory;
        private Storyboard? _holoStory;

        // Representative angle/offset for the frozen still — chosen so the spiral reads as a
        // spiral (not axis-aligned) and the holo sits at its brightest center sweep.
        private const double StillSpiralAngle = 24;

        /// <summary>When false, the card renders at its final values with no count-up (used for re-view of stills).</summary>
        public bool AnimateReveal { get; set; } = true;

        public SeasonRecapCard()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        public void SetViewModel(SeasonRecapViewModel vm)
        {
            _vm = vm;
            DataContext = vm;
            // Seed the count-up targets immediately so a non-animated render is correct even
            // before Loaded fires.
            SetFinalFigures();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            BuildSpiral();
            StartAmbientLoops();

            if (AnimateReveal) RunCountUps();
            else SetFinalFigures();
        }

        // ---------- spiral geometry (two interleaved Archimedean arms, like the mockup) ----------
        private void BuildSpiral()
        {
            if (SpiralCanvas.Children.Count > 0) return; // build once
            double cx = 260, cy = 260, b = 7.4, step = 0.18, turns = 15.5 * Math.PI;

            SpiralCanvas.Children.Add(MakeArm(cx, cy, b, step, turns, 0,
                Color.FromArgb(0x66, 0xB1, 0x8C, 0xFF), 3.2));
            SpiralCanvas.Children.Add(MakeArm(cx, cy, b, step, turns, Math.PI,
                Color.FromArgb(0x38, 0xE8, 0x4C, 0xF2), 2.2));
        }

        private static Path MakeArm(double cx, double cy, double b, double step, double turns,
            double offset, Color color, double thickness)
        {
            var fig = new PathFigure { IsClosed = false };
            bool first = true;
            for (double t = 0; t <= turns; t += step)
            {
                double r = 3 + b * t;
                double x = cx + r * Math.Cos(t + offset);
                double y = cy + r * Math.Sin(t + offset);
                var pt = new Point(x, y);
                if (first) { fig.StartPoint = pt; first = false; }
                else fig.Segments.Add(new LineSegment(pt, true));
            }
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            geo.Freeze();

            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return new Path
            {
                Data = geo,
                Stroke = brush,
                StrokeThickness = thickness,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
        }

        // ---------- ambient loops ----------
        private void StartAmbientLoops()
        {
            // Spiral: full rotation every 26s, forever.
            var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(26)))
            { RepeatBehavior = RepeatBehavior.Forever };
            _spiralStory = WrapAndBegin(spin, SpiralRotate, RotateTransform.AngleProperty);

            // Foil shimmer: two middle stops drift, autoreverse, 7s.
            _foilStory = new Storyboard();
            AddOffset(_foilStory, Foil1, 0.20, 0.50, 7);
            AddOffset(_foilStory, Foil2, 0.55, 0.85, 7);
            _foilStory.Begin(this, true);

            // Holo sweep: diagonal translate, autoreverse, 6s.
            _holoStory = new Storyboard();
            AddDouble(_holoStory, HoloTranslate, TranslateTransform.XProperty, -200, 200, 6);
            AddDouble(_holoStory, HoloTranslate, TranslateTransform.YProperty, -200, 200, 6);
            _holoStory.Begin(this, true);
        }

        private Storyboard WrapAndBegin(AnimationTimeline anim, DependencyObject target, DependencyProperty prop)
        {
            var sb = new Storyboard();
            sb.Children.Add(anim);
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, new PropertyPath(prop));
            sb.Begin(this, true);
            return sb;
        }

        private static void AddOffset(Storyboard sb, GradientStop stop, double from, double to, double seconds)
        {
            var a = new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(seconds)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            sb.Children.Add(a);
            Storyboard.SetTarget(a, stop);
            Storyboard.SetTargetProperty(a, new PropertyPath(GradientStop.OffsetProperty));
        }

        private static void AddDouble(Storyboard sb, DependencyObject target, DependencyProperty prop,
            double from, double to, double seconds)
        {
            var a = new DoubleAnimation(from, to, new Duration(TimeSpan.FromSeconds(seconds)))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            sb.Children.Add(a);
            Storyboard.SetTarget(a, target);
            Storyboard.SetTargetProperty(a, new PropertyPath(prop));
        }

        // ---------- count-ups ----------
        private void RunCountUps()
        {
            if (_vm == null) { SetFinalFigures(); return; }

            var start = DateTime.UtcNow;
            var dur = TimeSpan.FromMilliseconds(950);

            _countTimer?.Stop();
            _countTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _countTimer.Tick += (s, e) =>
            {
                var elapsed = DateTime.UtcNow - start;
                double p = Math.Min(1.0, elapsed.TotalMilliseconds / dur.TotalMilliseconds);
                double eased = 1 - Math.Pow(1 - p, 3); // ease-out cubic

                HeroSeasonTime.Text = SeasonRecapViewModel.FormatHm(_vm.SeasonMinutes * eased);
                HeroAllTime.Text = SeasonRecapViewModel.FormatHm(_vm.AllTimeMinutes * eased);
                StatRank.Text = _vm.PeakRankTarget > 0
                    ? "#" + Math.Max(1, (int)Math.Round(_vm.PeakRankTarget * eased))
                    : _vm.PeakRankText;

                if (p >= 1.0)
                {
                    _countTimer?.Stop();
                    SetFinalFigures();
                }
            };
            // Start from zero so the count-up is visible from the first frame.
            HeroSeasonTime.Text = SeasonRecapViewModel.FormatHm(0);
            HeroAllTime.Text = SeasonRecapViewModel.FormatHm(0);
            StatRank.Text = _vm.PeakRankTarget > 0 ? "#0" : _vm.PeakRankText;
            _countTimer.Start();
        }

        private void SetFinalFigures()
        {
            if (_vm == null) return;
            HeroSeasonTime.Text = _vm.SeasonTimeText;
            HeroAllTime.Text = _vm.AllTimeText;
            StatRank.Text = _vm.PeakRankText;
        }

        /// <summary>
        /// Freeze every animation to a clean representative frame and set the figures to their
        /// final values. Call this immediately before rendering the card to PNG so the still is
        /// never captured mid-sweep or mid-count-up.
        /// </summary>
        public void PrepareForStill()
        {
            _countTimer?.Stop();
            SetFinalFigures();

            // Pin the spiral to a representative angle, holo to its bright center, foil to a
            // pleasing spread — then pause so nothing moves during the render.
            try
            {
                _spiralStory?.Pause(this);
                SpiralRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                SpiralRotate.Angle = StillSpiralAngle;

                _holoStory?.Pause(this);
                HoloTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                HoloTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                HoloTranslate.X = 0;
                HoloTranslate.Y = 0;

                _foilStory?.Pause(this);
                Foil1.BeginAnimation(GradientStop.OffsetProperty, null);
                Foil2.BeginAnimation(GradientStop.OffsetProperty, null);
                Foil1.Offset = 0.35;
                Foil2.Offset = 0.65;
            }
            catch { /* freezing is best-effort; a still with default offsets is still fine */ }

            UpdateLayout();
        }
    }
}
