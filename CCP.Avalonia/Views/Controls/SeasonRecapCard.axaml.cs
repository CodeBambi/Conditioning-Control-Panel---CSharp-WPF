using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// Animated, fully data-bound Season Recap card.
    ///
    /// PORTED from ConditioningControlPanel/Controls/SeasonRecapCard.xaml.cs. Deviations:
    ///  - Spiral spin and holo sweep are Avalonia <see cref="Animation"/>s on the Canvas and the
    ///    Rectangle (TransformAnimator). The foil shimmer animated <c>GradientStop.Offset</c>, which
    ///    is not Animatable here, so it is dropped: the foil sits at its still offsets.
    ///  - The parameterless constructor seeds a sample snapshot with <c>AnimateReveal = false</c>
    ///    so <c>--render-all</c> captures final figures, not a frame of the count-up.
    /// </summary>
    public partial class SeasonRecapCard : UserControl
    {
        private SeasonRecapCardViewModel? _vm;
        private DispatcherTimer? _countTimer;
        private CancellationTokenSource? _ambient;

        private readonly Canvas _spiralCanvas;
        private readonly Rectangle _holo;
        private readonly TextBlock _heroSeasonTime;
        private readonly TextBlock _heroAllTime;
        private readonly TextBlock _statRank;

        // Representative angle for the frozen still: the spiral reads as a spiral, not axis-aligned.
        private const double StillSpiralAngle = 24;

        /// <summary>When false, the card renders at its final values with no count-up.</summary>
        public bool AnimateReveal { get; set; } = true;

        public SeasonRecapCard()
        {
            AvaloniaXamlLoader.Load(this);
            _spiralCanvas = this.FindControl<Canvas>("SpiralCanvas")!;
            _holo = this.FindControl<Rectangle>("Holo")!;
            _heroSeasonTime = this.FindControl<TextBlock>("HeroSeasonTime")!;
            _heroAllTime = this.FindControl<TextBlock>("HeroAllTime")!;
            _statRank = this.FindControl<TextBlock>("StatRank")!;
            Loaded += OnLoaded;
            Unloaded += (_, _) => { _countTimer?.Stop(); StopAmbientLoops(); };

            // Render constructor: sample data so the headless proof draws every string.
            AnimateReveal = false;
            SetViewModel(SeasonRecapCardViewModel.Sample());
        }

        public void SetViewModel(SeasonRecapCardViewModel vm)
        {
            _vm = vm;
            DataContext = vm;
            // Seed the count-up targets immediately so a non-animated render is correct even
            // before Loaded fires.
            SetFinalFigures();
        }

        private void OnLoaded(object? sender, EventArgs e)
        {
            BuildSpiral();
            StartAmbientLoops();

            if (AnimateReveal) RunCountUps();
            else SetFinalFigures();
        }

        // ---------- spiral geometry (two interleaved Archimedean arms, like the mockup) ----------
        private void BuildSpiral()
        {
            if (_spiralCanvas.Children.Count > 0) return; // build once
            double cx = 260, cy = 260, b = 7.4, step = 0.18, turns = 15.5 * Math.PI;

            _spiralCanvas.Children.Add(MakeArm(cx, cy, b, step, turns, 0,
                Color.FromArgb(0x66, 0xB1, 0x8C, 0xFF), 3.2));
            _spiralCanvas.Children.Add(MakeArm(cx, cy, b, step, turns, Math.PI,
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
                var pt = new Point(cx + r * Math.Cos(t + offset), cy + r * Math.Sin(t + offset));
                if (first) { fig.StartPoint = pt; first = false; }
                else fig.Segments!.Add(new LineSegment { Point = pt });
            }
            var geo = new PathGeometry();
            geo.Figures!.Add(fig);

            return new Path
            {
                Data = geo,
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                StrokeJoin = PenLineJoin.Round,
                StrokeLineCap = PenLineCap.Round,
            };
        }

        // ---------- ambient loops ----------
        private void StartAmbientLoops()
        {
            StopAmbientLoops();
            _ambient = new CancellationTokenSource();
            var token = _ambient.Token;

            // Spiral: full rotation every 26s, forever.
            var spin = new Animation { Duration = TimeSpan.FromSeconds(26), IterationCount = IterationCount.Infinite };
            spin.Children.Add(Frame(0d, new Setter(RotateTransform.AngleProperty, 0d)));
            spin.Children.Add(Frame(1d, new Setter(RotateTransform.AngleProperty, 360d)));
            _ = spin.RunAsync(_spiralCanvas, token);

            // Holo sweep: diagonal translate, autoreverse, 6s.
            var sweep = new Animation
            {
                Duration = TimeSpan.FromSeconds(6),
                IterationCount = IterationCount.Infinite,
                PlaybackDirection = PlaybackDirection.Alternate,
                Easing = new SineEaseInOut(),
            };
            sweep.Children.Add(Frame(0d, new Setter(TranslateTransform.XProperty, -200d), new Setter(TranslateTransform.YProperty, -200d)));
            sweep.Children.Add(Frame(1d, new Setter(TranslateTransform.XProperty, 200d), new Setter(TranslateTransform.YProperty, 200d)));
            _ = sweep.RunAsync(_holo, token);

            // ponytail: the WPF foil shimmer drifts two GradientStop offsets; GradientStop is not
            // Animatable on Avalonia, so the foil holds its still offsets (0.35 / 0.65).
        }

        private void StopAmbientLoops()
        {
            _ambient?.Cancel();
            _ambient?.Dispose();
            _ambient = null;
        }

        private static KeyFrame Frame(double cue, params Setter[] setters)
        {
            var f = new KeyFrame { Cue = new Cue(cue) };
            foreach (var s in setters) f.Setters.Add(s);
            return f;
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

                _heroSeasonTime.Text = SeasonRecapCardViewModel.FormatHm(_vm.SeasonMinutes * eased);
                _heroAllTime.Text = SeasonRecapCardViewModel.FormatHm(_vm.AllTimeMinutes * eased);
                _statRank.Text = _vm.PeakRankTarget > 0
                    ? "#" + Math.Max(1, (int)Math.Round(_vm.PeakRankTarget * eased))
                    : _vm.PeakRankText;

                if (p >= 1.0)
                {
                    _countTimer?.Stop();
                    SetFinalFigures();
                }
            };
            // Start from zero so the count-up is visible from the first frame.
            _heroSeasonTime.Text = SeasonRecapCardViewModel.FormatHm(0);
            _heroAllTime.Text = SeasonRecapCardViewModel.FormatHm(0);
            _statRank.Text = _vm.PeakRankTarget > 0 ? "#0" : _vm.PeakRankText;
            _countTimer.Start();
        }

        private void SetFinalFigures()
        {
            if (_vm == null) return;
            _heroSeasonTime.Text = _vm.SeasonTimeText;
            _heroAllTime.Text = _vm.AllTimeText;
            _statRank.Text = _vm.PeakRankText;
        }

        /// <summary>
        /// Freeze every animation to a clean representative frame and set the figures to their
        /// final values. Call this immediately before rendering the card to PNG.
        /// </summary>
        public void PrepareForStill()
        {
            _countTimer?.Stop();
            SetFinalFigures();

            try
            {
                StopAmbientLoops();
                ((RotateTransform)_spiralCanvas.RenderTransform!).Angle = StillSpiralAngle;
                var holo = (TranslateTransform)_holo.RenderTransform!;
                holo.X = 0;
                holo.Y = 0;
            }
            catch { /* freezing is best-effort; a still with default offsets is still fine */ }

            UpdateLayout();
        }
    }

    public class FeatureBadgeViewModel
    {
        public string Label { get; init; } = "";
        public int Count { get; init; }
        /// <summary>ponytail: badge art resolves through ModResourceResolver (WPF head); null
        /// draws the empty badge tile until it moves to Core.</summary>
        public IImage? Image { get; init; }
    }

    /// <summary>
    /// Avalonia twin of ConditioningControlPanel/ViewModels/SeasonRecapViewModel.cs. Same property
    /// names and the same Loc keys; every helper it uses (SeasonNumbering, TitleTiers,
    /// SeasonFeatureKeys, SeasonRecapSnapshot) is already in CCP.Core. Only two things pin the
    /// original to the WPF head: <c>Visibility</c> (bool here) and pack:// image paths (IImage,
    /// null placeholders here).
    /// </summary>
    public class SeasonRecapCardViewModel
    {
        private readonly SeasonRecapSnapshot _s;

        public SeasonRecapCardViewModel(SeasonRecapSnapshot snapshot)
        {
            _s = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        /// <summary>Placeholder data for the headless render.</summary>
        public static SeasonRecapCardViewModel Sample() => new(new SeasonRecapSnapshot
        {
            SeasonKey = "2026-08",
            Handle = "bambi",
            SeasonMinutes = 47 * 60 + 12,
            AllTimeMinutes = 312 * 60 + 5,
            SessionCount = 63,
            PeakRank = 17,
            PeakRankTotal = 1284,
            Percentile = 4,
            DaysActive = 22,
            SeasonLengthDays = 31,
            LongestStreak = 9,
            LifetimePointsSpent = 4200,
            PointsSpentSeason = 950,
            IsSupporter = true,
            IsOg = true,
            FeatureUse = new Dictionary<string, int>
            {
                [SeasonFeatureKeys.Flash] = 812, [SeasonFeatureKeys.Video] = 140,
                [SeasonFeatureKeys.Subliminal] = 96, [SeasonFeatureKeys.Bubbles] = 40,
                [SeasonFeatureKeys.Overlay] = 12, [SeasonFeatureKeys.LockCard] = 7,
                [SeasonFeatureKeys.MindWipe] = 3,
            },
        });

        /// <summary>ponytail: RecapBackgrounds.ForMod names a pack:// resource; null until the art ships in Core.</summary>
        public IImage? BackgroundImage => null;

        // ---------- header ----------
        public int SeasonNumber => SeasonNumbering.ToSeasonNumber(_s.SeasonKey);
        public int NextSeasonNumber => SeasonNumber + 1;
        public string SeasonLabel => Loc.Get("recap_header_season");
        public string SeasonNumberText => SeasonNumber >= 0 ? SeasonNumber.ToString("00") : "--";
        public string StatusText => Loc.Get("recap_status_complete");

        public string DateRangeText
        {
            get
            {
                var (start, end) = SeasonNumbering.DateRange(_s.SeasonKey);
                if (start == DateTime.MinValue) return "";
                string fmt(DateTime d) => d.ToString("MMM dd", CultureInfo.InvariantCulture).ToLowerInvariant();
                return $"{fmt(start)} - {fmt(end)} · {start.Year}";
            }
        }

        // ---------- identity ----------
        public string Handle => string.IsNullOrWhiteSpace(_s.Handle) ? Loc.Get("recap_default_handle") : _s.Handle;
        public string TitleText => Loc.Get(TitleTiers.Resolve(AllTimeHours, _s.Percentile));

        public string SupporterLabel => Loc.Get("recap_badge_supporter");
        public string OgLabel => Loc.Get("recap_badge_og");
        public bool IsSupporter => _s.IsSupporter;
        public bool IsOg => _s.IsOg;
        public bool IsStatusRowVisible => _s.IsSupporter || _s.IsOg;

        // ---------- hero ----------
        public double SeasonMinutes => _s.SeasonMinutes;
        public double AllTimeMinutes => _s.AllTimeMinutes;
        public double AllTimeHours => _s.AllTimeMinutes / 60.0;

        public string SeasonTimeText => FormatHm(_s.SeasonMinutes);
        public string AllTimeText => FormatHm(_s.AllTimeMinutes);
        public string HeroLabel => Loc.Get("recap_hero_label");
        public string AllTimeLabel => Loc.Get("recap_alltime_label");
        public string SessionsSubline => Loc.GetF("recap_sessions_subline", _s.SessionCount);

        // ---------- stats ----------
        public string PeakRankText => _s.PeakRank > 0 ? $"#{_s.PeakRank}" : "—";
        public int PeakRankTarget => _s.PeakRank;
        public string PeakRankOfText => _s.PeakRank > 0 && _s.PeakRankTotal > 0
            ? Loc.GetF("recap_rank_of", _s.PeakRankTotal.ToString("N0"))
            : "";
        public string PercentileText => _s.Percentile > 0 ? Loc.GetF("recap_top_percent", _s.Percentile) : "—";
        public string DaysActiveText => _s.DaysActive.ToString();
        public string DaysActiveOfText => _s.SeasonLengthDays > 0 ? $"/ {_s.SeasonLengthDays}" : "";
        public string LongestStreakText => _s.LongestStreak.ToString();
        public string StreakUnitText => Loc.Get("recap_streak_days");

        public string StatPeakRankLabel => Loc.Get("recap_stat_peak_rank");
        public string StatPercentileLabel => Loc.Get("recap_stat_percentile");
        public string StatDaysActiveLabel => Loc.Get("recap_stat_days_active");
        public string StatStreakLabel => Loc.Get("recap_stat_longest_streak");

        // ---------- prestige strip ----------
        public bool IsPrestigeVisible => _s.LifetimePointsSpent > 0;
        public string PrestigeLineText
        {
            get
            {
                var t = $"✦ {Loc.Get("recap_prestige_label")} {_s.LifetimePointsSpent:N0}";
                if (_s.PointsSpentSeason > 0)
                    t += "  ·  " + Loc.GetF("recap_prestige_delta", _s.PointsSpentSeason);
                return t;
            }
        }

        // ---------- badge row ----------
        public string BadgesTitle => Loc.Get("recap_badges_title");
        public int FeaturesUsedCount => _s.FeatureUse.Count(kv => kv.Value > 0);
        public int FeaturesTotal => _s.FeaturesTotal > 0 ? _s.FeaturesTotal : SeasonFeatureKeys.TotalCount;
        public string FeaturesUsedText => Loc.GetF("recap_features_used", FeaturesUsedCount, FeaturesTotal);

        public IReadOnlyList<FeatureBadgeViewModel> TopBadges =>
            _s.FeatureUse
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                .Take(6)
                .Select(kv =>
                {
                    var def = SeasonFeatureKeys.Find(kv.Key);
                    return new FeatureBadgeViewModel
                    {
                        Label = def != null ? Loc.Get(def.LabelLocKey) : kv.Key,
                        Count = kv.Value,
                    };
                })
                .ToList();

        // ---------- verdict ----------
        private string VerdictKey
        {
            get
            {
                var pct = _s.Percentile;
                if (pct > 0 && pct <= 2) return "recap_verdict_elite";
                if (pct > 0 && pct <= 10) return "recap_verdict_strong";
                if (AllTimeHours >= 50 || _s.SeasonMinutes >= 600) return "recap_verdict_mid";
                return "recap_verdict_gentle";
            }
        }
        public string VerdictBefore => SplitVerdict().before;
        public string VerdictName => Handle;
        public string VerdictAfter => SplitVerdict().after;

        private (string before, string after) SplitVerdict()
        {
            var t = Loc.Get(VerdictKey); // raw template, still contains "{0}"
            const string token = "{0}";
            var idx = t.IndexOf(token, StringComparison.Ordinal);
            if (idx < 0) return (t, "");
            return (t.Substring(0, idx), t.Substring(idx + token.Length));
        }

        // ---------- reset / brand ----------
        public int AllTimeHoursRounded => (int)Math.Round(AllTimeHours, MidpointRounding.AwayFromZero);
        public string ResetBefore => Loc.GetF("recap_reset_before", SeasonNumberText);
        public string ResetBold => Loc.GetF("recap_reset_bold", AllTimeHoursRounded);
        public string ResetAfter => Loc.GetF("recap_reset_after", NextSeasonNumber.ToString("00"));
        public string BrandText => Loc.Get("recap_brand");

        public string SharePrefillText => Loc.GetF(
            "recap_share_prefill",
            SeasonNumberText,
            FormatHmCompact(_s.SeasonMinutes),
            FormatHmCompact(_s.AllTimeMinutes),
            _s.Percentile > 0 ? _s.Percentile.ToString() : "?");

        public string SuggestedFileName => $"cclabs-season-{_s.SeasonKey}.png";

        public static string FormatHm(double totalMinutes)
        {
            var t = Math.Max(0, totalMinutes);
            return Loc.GetF("recap_time_hm", (int)(t / 60), (int)(t % 60));
        }

        public static string FormatHmCompact(double totalMinutes)
        {
            var hours = (int)Math.Round(Math.Max(0, totalMinutes) / 60.0, MidpointRounding.AwayFromZero);
            return $"{hours}h";
        }
    }
}
