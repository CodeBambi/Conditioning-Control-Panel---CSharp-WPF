// PORTED from WPF 7.1.5 Views/Tabs/ChasterTabView.Fx.cs. Circe's tab, the motion: an ambient fog and
// dust canvas behind the cards, a spiral watermark turning once every 90 s, a sheen crossing the hero,
// the art on a slow drift, the cards arriving in a stagger, and one event vocabulary - a pop, a burst
// and a ring - for every press and every booking. On top of that the lock's own beats: the paper tag
// swings, the title's padlocks breathe, the calendar's marks draw in and tonight's square breathes
// with a comet running the key into it, a turned key throws a ring and lights its rows one after
// another, and the trailer's picture drifts under the app's own floating figure.
// Every number (timings, distances, eases, counts, colours) is WPF's.
//
// How it runs here (the port's FX law):
//   - every endless loop is ONE VisibleBeat on the window's shared 30 fps beat; it parks at rest when
//     the page hides or the motion level forbids ambient loops and re-arms by itself;
//   - every finite beat is an FxTrack run (a function of its age) that ends on its composed state and
//     removes what it added; FxPark finishes the live ones;
//   - nothing wears an Effect: WPF's key glow is a sibling layer of BoxShadows whose Opacity breathes,
//     the comet's glow is its own BoxShadow; opacity never leaves 0..1.
// not ported: GlowDigits (a DropShadowEffect on the hero clock, which sits over the looping ground:
// the port's Effect trap; owed as a cached blurred silhouette).
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaster;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        // WPF ChasterTabView.xaml.cs:39-40
        internal static readonly Color JackpotColour = Color.FromRgb(0xE0, 0xB0, 0x52);
        internal static readonly Color CustomColour = Color.FromRgb(0xC9, 0xA6, 0xFF);

        // WPF Fx.cs:40-68
        private const double SpiralTurns = 6.5, SpiralInnerRadius = 8, SpiralTurnSeconds = 90, SpiralStep = 0.1;   // SpiralFps = 10
        internal const double ArtDriftTo = 1.06, ArtDriftSeconds = 38;
        internal const int PopMs = 380, ShockwaveMs = 480;
        internal const double ShockwaveFrom = 16, ShockwaveTo = 260;
        private const double HeroRadius = 18;
        internal const double TagSwingFrom = -7, TagSwingTo = -3, TagSwingSeconds = 3.6;
        internal const double CalendarBobPx = 3, CalendarBobSeconds = 1.1;
        internal const double CalendarCometEverySeconds = 6.5;
        internal const int CalendarCometMs = 1600, CalendarDrawStepMs = 45, CalendarStrokeMs = 200;
        internal const double RingPulseTo = 1.04, RingPulseSeconds = 2.2;
        internal const double SheetTiltDegrees = -1.5;
        internal const int KeyFlickerStepMs = 28;
        internal const double TrailerDriftTo = 1.09, TrailerDriftSeconds = 7;
        internal const int TrailerFigureEveryMs = 1700;
        internal const int BillOpenMs = 420, BillCloseMs = 260;
        // MotionFx.cs:23-28
        private const double HoverLiftScale = 1.02;
        private const int HoverMs = 150, StaggerMs = 40, StaggerCap = 6;

        internal readonly RotateTransform SpiralTurn = new(), TagSwing = new(-6), SheetTilt = new(SheetTiltDegrees);
        internal readonly ScaleTransform HeroArtScale = new(1, 1), TrailerArtScale = new(1, 1);
        internal readonly TranslateTransform CalendarTagBob = new(), TrailerArtPan = new(), TagDrop = new();
        private readonly List<FxTrack.Run> _fxRuns = new();
        private readonly Dictionary<Control, FxTrack.Run> _pops = new(), _lifts = new();
        private VisibleBeat? _ambientBeat;
        private UniformGrid? _keyGlow;
        private bool _fxStarted, _spiralBuilt;
        private double _beatLast, _spiralAngle, _spiralApplyIn, _cometIn = CalendarCometEverySeconds, _driftT, _trailerT;
        private CardSheenAdorner? _heroSheen;
        private PerimeterCometAdorner? _linkComet;
        private DispatcherTimer? _trailerFigure;
        private ChasterBookedFlash? _trailerFlash;
        private TabPrice? _trailerPrice;
        private Control? _tonightMark, _keyCell;
        private readonly List<Canvas> _calendarDraws = new();
        private FxTrack.Run? _leadCount, _billRun;
        private int _billGeneration;

        internal bool AmbientBeatRunning => _ambientBeat?.IsRunning == true;
        internal Canvas ShockwaveLayer => ShockwaveCanvas;
        internal UniformGrid? KeyGlow => _keyGlow;
        internal int LiveFxRuns => _fxRuns.Count;
        internal bool HeroSheenOn => _heroSheen != null;
        internal bool LinkCometOn => _linkComet != null;

        /// <summary>Lands every finite beat on its end state (the page parked; the tests step past them).</summary>
        internal void FxFinish()
        {
            FxTrack.FinishAll(_fxRuns);
            _pops.Clear();
            _lifts.Clear();
        }

        /// <summary>Once, from the constructor: the hover lifts, the transforms every beat moves, the loop.</summary>
        private void FxInit()
        {
            try
            {
                foreach (var card in new Control[] { StatTab, StatToday, StatRun, BtnPresetGentle, BtnPresetStrict, BtnPresetCirce, BtnPresetCustom })
                {
                    var self = card;
                    self.PointerEntered += (_, _) => FxHoverLift(self, true);
                    self.PointerExited += (_, _) => FxHoverLift(self, false);
                }
                // Every card that pops, lifts or arrives carries a scale AND a slide from the start.
                foreach (var moving in new Control[]
                         {
                             HeroCard, StatTab, StatToday, StatRun, SwitchPill, TxtBalance, ConsentCard,
                             BtnPresetGentle, BtnPresetStrict, BtnPresetCirce, BtnPresetCustom,
                             CostBoard, EarnBoard, JackpotRow, FootRow, TxtTagAmount,
                         }.Concat(FactRow.Children.OfType<Control>()))
                {
                    moving.RenderTransformOrigin = RelativePoint.Center;
                    moving.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } };
                }
                SpiralLayer.RenderTransform = SpiralTurn;
                HeroArt.RenderTransformOrigin = RelativePoint.Center;
                HeroArt.RenderTransform = HeroArtScale;
                // WPF :445-451: the tag hangs from the top of its string
                PaperTag.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
                PaperTag.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), TagSwing, TagDrop } };
                // WPF :515-518: the sheet turns on its pin
                Sheet.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
                Sheet.RenderTransform = SheetTilt;
                Sheet.PointerEntered += (_, _) => FxSheetFlutter();
                CalendarTag.RenderTransformOrigin = RelativePoint.Center;
                CalendarTag.RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), CalendarTagBob } };
                TrailerArt.RenderTransformOrigin = RelativePoint.Center;
                TrailerArt.RenderTransform = new TransformGroup { Children = { TrailerArtScale, TrailerArtPan } };
                TxtRunChevron.RenderTransformOrigin = RelativePoint.Center;
                TxtRunChevron.RenderTransform = new RotateTransform(0);
                ReceiptHost.RenderTransform = new TranslateTransform();
                BuildKeyGlow();

                _ambientBeat = VisibleBeat.Attach(PageRoot, StepAmbient, RestAmbient);
                PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) FxPark(); };
                DetachedFromVisualTree += (_, _) => FxPark();
                Env.MotionGateChanged += OnFxMotionGate;
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] fx init"); }
        }

        /// <summary>The motion level changed while the page shows: the sheen and the key glow follow it.</summary>
        private void OnFxMotionGate()
        {
            if (!IsEffectivelyVisible) return;
            StartHeroSheen();
            FxNudgeKeys(_nudgingKeys);
        }

        /// <summary>Every visit: the loops start (or resume), the cards arrive, the lead digit counts up.</summary>
        private void FxOnShown()
        {
            try
            {
                if (!IsEffectivelyVisible)
                {
                    // a door that calls before the page shows: once more when it has
                    Dispatcher.UIThread.Post(() => { if (IsEffectivelyVisible) FxOnShown(); }, DispatcherPriority.Loaded);
                    return;
                }
                if (!_spiralBuilt)
                {
                    _spiralBuilt = true;
                    SpiralPath.Data = BuildSpiral(SpiralLayer.Width, SpiralTurns, SpiralInnerRadius);
                }
                if (!_fxStarted)
                {
                    _fxStarted = true;
                    Ambient.StartLayers(new AmbientFxConfig
                    {
                        Layers = AmbientFxLayers.FogDrift | AmbientFxLayers.DustField,
                        Intensity = 0.85,
                        FogPuffs = 3,
                    });
                    (TopLevel.GetTopLevel(this) as MainShellWindow)?.RegisterTabFx("chaster", Ambient);
                }
                else Ambient.Resume();

                FxStaggerIn(ArrivingCards());
                _ambientBeat?.Refresh();
                StartHeroSheen();
                FxNudgeKeys(_nudgingKeys);
                HeroTitle.PlayEntry();
                HeroTitle.Start();
                FxCountLead();
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] fx on shown"); }
        }

        private IEnumerable<Control> ArrivingCards()
        {
            yield return HeroCard;
            if (FactRow.IsVisible)
            {
                foreach (var fact in FactRow.Children.OfType<Control>()) yield return fact;
                yield break;
            }
            yield return StatTab;
            yield return StatToday;
            yield return StatRun;
            yield return BtnPresetGentle;
            yield return BtnPresetStrict;
            yield return BtnPresetCirce;
            yield return BtnPresetCustom;
            yield return CostBoard;
            yield return EarnBoard;
            yield return JackpotRow;
            yield return FootRow;
        }

        /// <summary>WPF MotionFx.StaggerIn: fade in from a 10 px rise, 40 ms apart (capped at six steps),
        /// 220 ms / 260 ms on quad out. Transitions off: the end state at once.</summary>
        private void FxStaggerIn(IEnumerable<Control> items)
        {
            var list = items.ToList();
            if (!Env.AllowTransitions)
            {
                foreach (var item in list) { item.Opacity = 1; if (Slide(item) is { } s) s.Y = 0; }
                return;
            }
            double total = (StaggerMs * Math.Min(list.Count - 1, StaggerCap)) + 260;
            FxTrack.Play(total, ms =>
            {
                for (int i = 0; i < list.Count; i++)
                {
                    double at = ms - (StaggerMs * Math.Min(i, StaggerCap));
                    list[i].Opacity = FxTrack.Clamp01(FxTrack.QuadOut(FxTrack.Clamp01(at / 220)));
                    if (Slide(list[i]) is { } slide) slide.Y = 10 * (1 - FxTrack.QuadOut(FxTrack.Clamp01(at / 260)));
                }
            }, null, _fxRuns);
        }

        private static TranslateTransform? Slide(Control c) =>
            (c.RenderTransform as TransformGroup)?.Children.OfType<TranslateTransform>().FirstOrDefault();

        /// <summary>WPF MotionFx.Odometer on the clock's lead number: 0 to its value in 0.9 s, quad out.
        /// The 1 s tick leaves the lead alone while it counts (WPF _leadHeldUntilUtc).</summary>
        private void FxCountLead()
        {
            _leadCount?.Finish();
            _leadCount = null;
            if (!Env.AllowTransitions || _clockNumbers.Count == 0 || !int.TryParse(_clockNumbers[0].Text, out var lead) || lead <= 0) return;
            var block = _clockNumbers[0];
            _leadCount = FxTrack.Play(900, ms => block.Text = Math.Round(lead * FxTrack.QuadOut(ms / 900)).ToString("0"),
                () => { _leadCount = null; PaintHeroClock(); }, _fxRuns);
        }

        /// <summary>True while the count-up owns the lead number.</summary>
        private bool LeadHeld => _leadCount is { IsDone: false };

        // ------------------------------------------------------------------ the key nudge

        /// <summary>WPF's DropShadowEffect on the key row, without an Effect: a layer behind the keys, one
        /// rounded BoxShadow per key in the jackpot gold, whose Opacity the loop breathes.</summary>
        private void BuildKeyGlow()
        {
            if (PresetRow.Parent is not Panel slot) return;
            int at = slot.Children.IndexOf(PresetRow);
            slot.Children.RemoveAt(at);
            _keyGlow = new UniformGrid
            {
                Columns = 2, Rows = 2, Margin = PresetRow.Margin, IsHitTestVisible = false, IsVisible = false, Opacity = 0,
            };
            for (int i = 0; i < 4; i++)
                _keyGlow.Children.Add(new Border { Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(16) });
            slot.Children.Insert(at, new Panel { Children = { _keyGlow, PresetRow } });
        }

        /// <summary>Tab on, nothing switched on: the keys wear a glow that breathes until one is pressed
        /// (0.35 to 0.95 over 1.1 s each way). The glow follows the tier's glow rail, the breathing the
        /// ambient-loop rail; with neither, the hint line alone does the asking. Off: the glow drops.</summary>
        private void FxNudgeKeys(bool on)
        {
            try
            {
                SetupHint.Foreground = on ? new SolidColorBrush(JackpotColour) : Brushes.White;
                PresetRow.Opacity = 1;
                if (_keyGlow == null) return;
                bool glow = on && IsEffectivelyVisible && Env.AllowGlow(Env.CurrentTier);
                if (!glow)
                {
                    _keyGlow.IsVisible = false;
                    _keyGlow.Opacity = 0;
                    foreach (var b in _keyGlow.Children.OfType<Border>()) b.BoxShadow = default;
                    return;
                }
                double blur = Math.Min(26, Env.MaxGlowBlurRadius(Env.CurrentTier));
                var shadow = new BoxShadows(new BoxShadow { Blur = blur, Color = JackpotColour });
                foreach (var b in _keyGlow.Children.OfType<Border>()) b.BoxShadow = shadow;
                _keyGlow.IsVisible = true;
                if (!AmbientBeatRunning) _keyGlow.Opacity = 0.8;   // WPF: the still glow
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] key nudge"); }
        }

        /// <summary>The page hid: park every loop where it is, land every beat, drop every glow.</summary>
        private void FxPark()
        {
            try
            {
                if (_fxStarted) Ambient.Pause();
                _ambientBeat?.Refresh();
                DetachSheen();
                HeroTitle.Stop();
                FxTrailerStop();
                PerimeterCometAdorner.Detach(_linkComet);
                _linkComet = null;
                FxFinish();
                if (_keyGlow != null) { _keyGlow.IsVisible = false; _keyGlow.Opacity = 0; }
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] fx park"); }
        }

        // ------------------------------------------------------------------ the loops (one beat)

        /// <summary>One beat of every endless loop on the page. <paramref name="t"/> restarts at 0 when the
        /// loop re-arms; the spiral and the drift keep their own clocks so they continue where they stopped.</summary>
        private void StepAmbient(double t)
        {
            double dt = Math.Clamp(t - _beatLast, 0, 0.1);
            _beatLast = t;
            _driftT += dt;

            // the spiral: once every 90 s, applied ten times a second (WPF SpiralFps)
            _spiralAngle = (_spiralAngle + (dt * 360 / SpiralTurnSeconds)) % 360;
            if ((_spiralApplyIn -= dt) <= 0) { _spiralApplyIn = SpiralStep; SpiralTurn.Angle = _spiralAngle; }

            // the neon art breathes in and out over half a minute
            HeroArtScale.ScaleX = HeroArtScale.ScaleY = 1 + ((ArtDriftTo - 1) * BeatLoop.Breath(_driftT, ArtDriftSeconds));
            // the paper tag swings a few degrees on its string
            TagSwing.Angle = TagSwingFrom + ((TagSwingTo - TagSwingFrom) * BeatLoop.Breath(_driftT, TagSwingSeconds));

            // tonight: the tag bobs, the ring pulses, and every 6.5 s a comet runs the key into it
            if (_tonightCell != null)
            {
                CalendarTagBob.Y = CalendarBobPx * BeatLoop.Breath(_driftT, CalendarBobSeconds);
                if (_tonightMark?.RenderTransform is ScaleTransform ring && !_pops.ContainsKey(_tonightMark))
                    ring.ScaleX = ring.ScaleY = 1 + ((RingPulseTo - 1) * BeatLoop.Breath(_driftT, RingPulseSeconds));
                if ((_cometIn -= dt) <= 0) { _cometIn = CalendarCometEverySeconds; CalendarComet(); }
            }

            if (_keyGlow is { IsVisible: true }) _keyGlow.Opacity = 0.35 + (0.6 * BeatLoop.Breath(_driftT, 1.1));

            if (_trailerShown)
            {
                _trailerT += dt;
                TrailerArtScale.ScaleX = TrailerArtScale.ScaleY = 1 + ((TrailerDriftTo - 1) * BeatLoop.Breath(_trailerT, TrailerDriftSeconds));
                TrailerArtPan.X = 6 - (12 * BeatLoop.Breath(_trailerT, TrailerDriftSeconds * 1.3));
            }
        }

        /// <summary>The still look (WPF StopArtDrift / StopTagSwing / StopCalendarLoops / FxTrailerStop).
        /// The spiral holds its angle; the next start continues from it.</summary>
        private void RestAmbient()
        {
            _beatLast = 0;
            _cometIn = CalendarCometEverySeconds;
            HeroArtScale.ScaleX = HeroArtScale.ScaleY = 1;
            TagSwing.Angle = (TagSwingFrom + TagSwingTo) / 2;
            CalendarTagBob.Y = 0;
            if (_tonightMark?.RenderTransform is ScaleTransform ring && !_pops.ContainsKey(_tonightMark)) ring.ScaleX = ring.ScaleY = 1;
            TrailerArtScale.ScaleX = TrailerArtScale.ScaleY = 1;
            TrailerArtPan.X = 0;
            if (_keyGlow is { IsVisible: true }) _keyGlow.Opacity = 0.8;
        }

        /// <summary>An Archimedean spiral (r grows linearly with the angle) as one polyline. The launcher's recipe.</summary>
        private static Geometry BuildSpiral(double size, double turns, double innerRadius)
        {
            var geometry = new StreamGeometry();
            double c = size / 2, maxTheta = turns * Math.PI * 2, growth = (c - innerRadius - 4) / maxTheta;
            const double step = Math.PI / 45;
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(c + innerRadius, c), false);
                for (double theta = step; theta <= maxTheta; theta += step)
                {
                    double r = innerRadius + (growth * theta);
                    ctx.LineTo(new Point(c + (r * Math.Cos(theta)), c + (r * Math.Sin(theta))));
                }
                ctx.EndFigure(false);
            }
            return geometry;
        }

        private void StartHeroSheen()
        {
            DetachSheen();
            if (!Env.AllowAmbientLoops || !IsEffectivelyVisible) return;
            _heroSheen = CardSheenAdorner.Attach(HeroCard, HeroRadius);
        }

        private void DetachSheen()
        {
            if (_heroSheen == null) return;
            try { CardSheenAdorner.Detach(_heroSheen); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] sheen"); }
            _heroSheen = null;
        }

        // ------------------------------------------------------------------ the calendar

        /// <summary>A sheet was rebuilt: forget the old squares and their marks.</summary>
        private void FxCalendarReset()
        {
            _tonightMark = null;
            _keyCell = null;
            _calendarDraws.Clear();
            CalendarTagBob.Y = 0;
            _cometIn = CalendarCometEverySeconds;
        }

        /// <summary>The marker draws the crosses one after another, 45 ms apart: each stroke runs its length
        /// (a dash the length of the stroke, its offset pulled to zero, 200 ms quad out, the second stroke
        /// 180 ms behind the first) and the splat lands where the second stroke lifts (90 ms). Reduced
        /// motion and Off get the final state.</summary>
        internal void FxCalendarDrawIn()
        {
            if (Env.Level != MotionLevel.Full || _calendarDraws.Count == 0) return;
            try
            {
                var marks = new List<(Shape Stroke, double Units, double Begin)>();
                var splats = new List<(Ellipse Splat, double Target, double Begin)>();
                int i = 0;
                foreach (var cross in _calendarDraws)
                {
                    double begin = CalendarDrawStepMs * i++;
                    var strokes = cross.Children.OfType<Polyline>().ToList();
                    int s = 0;
                    foreach (var stroke in strokes)
                    {
                        double length = Length(stroke.Points);
                        if (length <= 0 || stroke.StrokeThickness <= 0) continue;
                        double units = length / stroke.StrokeThickness;
                        stroke.StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { units, units };
                        stroke.StrokeDashOffset = units;
                        marks.Add((stroke, units, begin + (CalendarStrokeMs * 0.9 * s++)));
                    }
                    if (cross.Children.OfType<Ellipse>().FirstOrDefault() is { } splat)
                    {
                        splats.Add((splat, splat.Opacity, begin + (CalendarStrokeMs * 0.9 * Math.Max(0, strokes.Count - 1)) + (CalendarStrokeMs * 0.8)));
                        splat.Opacity = 0;
                    }
                }
                double total = (CalendarDrawStepMs * Math.Max(0, i - 1)) + (CalendarStrokeMs * 0.9) + (CalendarStrokeMs * 0.8) + 90 + CalendarStrokeMs;
                FxTrack.Play(total, ms =>
                {
                    foreach (var (stroke, units, begin) in marks)
                        stroke.StrokeDashOffset = units * (1 - FxTrack.QuadOut(FxTrack.Clamp01((ms - begin) / CalendarStrokeMs)));
                    foreach (var (splat, target, begin) in splats)
                        splat.Opacity = FxTrack.Clamp01(target * FxTrack.Clamp01((ms - begin) / 90));
                }, () =>
                {
                    foreach (var (stroke, _, _) in marks) { stroke.StrokeDashArray = null; stroke.StrokeDashOffset = 0; }
                    foreach (var (splat, target, _) in splats) splat.Opacity = target;
                }, _fxRuns);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] calendar draw"); }
        }

        private static double Length(IList<Point> points)
        {
            double length = 0;
            for (int i = 1; i < points.Count; i++)
                length += Math.Sqrt(Math.Pow(points[i].X - points[i - 1].X, 2) + Math.Pow(points[i].Y - points[i - 1].Y, 2));
            return length;
        }

        /// <summary>The pointer crossed the sheet: it flutters on its pin and settles back.</summary>
        internal void FxSheetFlutter() => SheetSwing(-0.5, 300);

        /// <summary>A price landed: the sheet takes a small nudge on its pin.</summary>
        private void FxSheetNudge() => SheetSwing(-2.7, 320);

        private void SheetSwing(double to, int ms)
        {
            if (Env.Level != MotionLevel.Full || !CalendarRow.IsVisible) return;
            FxTrack.Play(ms, at => SheetTilt.Angle = FxTrack.Keys(at / ms,
                    (0, SheetTiltDegrees, null), (0.45, to, FxTrack.QuadOut), (1, SheetTiltDegrees, FxTrack.SineInOut)),
                () => SheetTilt.Angle = SheetTiltDegrees, _fxRuns);
        }

        /// <summary>One comet: a bright dot that runs from the sticker to tonight's square and pops the ring.
        /// It fades in over the first tenth, out over the last 8 %, and takes itself off the canvas.</summary>
        internal void CalendarComet()
        {
            try
            {
                if (!IsEffectivelyVisible || !Env.AllowAmbientLoops || _tonightCell == null || _keyCell == null) return;
                var open = _keyCell;
                if (open.TranslatePoint(new Point(open.Bounds.Width / 2, open.Bounds.Height / 2), ShockwaveCanvas) is not { } from) return;
                if (_tonightCell.TranslatePoint(new Point(_tonightCell.Bounds.Width * 0.3, _tonightCell.Bounds.Height * 0.25), ShockwaveCanvas) is not { } to) return;

                var slide = new TranslateTransform(from.X, from.Y);
                var comet = new Border
                {
                    Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Brushes.White, Opacity = 0,
                    IsHitTestVisible = false, RenderTransform = slide, Margin = new Thickness(-4, -4, 0, 0), Tag = "calendar-comet",
                };
                // WPF: a 12 px DropShadowEffect in the cost red at 0.9. Here the dot's own BoxShadow, no Effect.
                if (Env.AllowGlow(Env.CurrentTier))
                    comet.BoxShadow = new BoxShadows(new BoxShadow { Blur = 12, Color = Color.FromArgb(0xE6, CostColour.R, CostColour.G, CostColour.B) });
                ShockwaveCanvas.Children.Add(comet);
                FxTrack.Play(CalendarCometMs, ms =>
                {
                    double u = ms / CalendarCometMs, e = FxTrack.QuadInOut(u);
                    slide.X = from.X + ((to.X - from.X) * e);
                    slide.Y = from.Y + ((to.Y - from.Y) * e);
                    comet.Opacity = FxTrack.Clamp01(FxTrack.Keys(u, (0, 0, null), (0.1, 1, null), (0.92, 1, null), (1, 0, null)));
                }, () =>
                {
                    ShockwaveCanvas.Children.Remove(comet);
                    if (_tonightMark != null && IsEffectivelyVisible) FxPop(_tonightMark, 1.25);
                }, _fxRuns);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] comet"); }
        }

        // ------------------------------------------------------------------ the trailer

        /// <summary>The trailer opened: the picture drifts (1.09 over 7 s, a 6 px pan over 9.1 s, on the page's
        /// beat), and every 1.7 s the app's own floating figure says the row's price off the plate.</summary>
        private void FxTrailerStart(TabPrice price)
        {
            FxTrailerStop();
            _trailerPrice = price;
            _trailerT = 0;
            try
            {
                // The first figure lands as soon as the popup has a layer; then on a loop.
                _trailerFigure = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(TrailerFigureEveryMs) };
                _trailerFigure.Tick += (_, _) => TrailerFigure();
                _trailerFigure.Start();
                Dispatcher.UIThread.Post(TrailerFigure, DispatcherPriority.Background);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer fx"); }
        }

        /// <summary>One "+0:30" off the plate, the exact figure the padlock throws when this row books.</summary>
        private void TrailerFigure()
        {
            try
            {
                if (!_trailerShown || _trailerPrice is not { } price) return;
                if (BookedFlashPlan.For(price.Id, price.Seconds, Env.Level) is not { } fresh) return;
                _trailerFlash?.Dismiss();
                _trailerFlash = ChasterBookedFlash.Show(TrailerFigureAnchor, fresh);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer figure"); }
        }

        private void FxTrailerStop()
        {
            try
            {
                _trailerFigure?.Stop();
                _trailerFigure = null;
                _trailerFlash?.Dismiss();
                _trailerFlash = null;
                _trailerPrice = null;
                TrailerArtScale.ScaleX = TrailerArtScale.ScaleY = 1;
                TrailerArtPan.X = 0;
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] trailer fx stop"); }
        }

        internal bool TrailerFigureTicking => _trailerFigure?.IsEnabled == true;

        // ------------------------------------------------------------------ the events

        /// <summary>A price landed: the balance pops in the booking's colour, sparks off it, a ring out of its
        /// card, the paper tag jumps on its string, and the title's padlocks take a tug.</summary>
        internal void FxBooked(int seconds, string eventId)
        {
            try
            {
                var colour = eventId == CircesTab.JackpotEventId ? JackpotColour : seconds > 0 ? CostColour : EarnColour;
                FxPop(TxtBalance, 1.14);
                BurstAt(TxtBalance, colour, 36);
                Shockwave(StatTab, colour);
                FxPop(PaperTag, 1.06);
                FxPop(TxtTagAmount, 1.12);
                if (CalendarTag.IsVisible) FxPop(CalendarTag, 1.2);
                if (_tonightMark != null) FxPop(_tonightMark, 1.18);
                FxSheetNudge();
                HeroTitle.Jolt();
                if (HeroTitle.IsVisible && HeroTitle.Padlocks.LastOrDefault() is { } padlock) BurstAt(padlock, CostColour, 18);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] fx booked"); }
        }

        internal void FxPreset(Control tile, Color hue)
        {
            FxPop(tile, 1.05);
            BurstAt(tile, hue, 44);
            Shockwave(tile, hue);
        }

        internal static Color PresetColour(string id) =>
            id == TabPresets.Gentle ? EarnColour : id == TabPresets.Strict ? CostColour : id == TabPresets.Circe ? JackpotColour : CustomColour;

        /// <summary>A key turned: the rows it opens light one after another, top to bottom, 28 ms apart
        /// (to 1.06 by 40 % of 260 ms on quad out, back on quad in-out).</summary>
        internal void FxKeyTurned(IEnumerable<ToggleButton> lit)
        {
            if (!Env.AllowTransitions) return;
            try
            {
                var rows = lit.Select(r => EnsureScale(r)).Where(s => s != null).Select(s => s!).ToList();
                if (rows.Count == 0) return;
                FxTrack.Play((KeyFlickerStepMs * (rows.Count - 1)) + 260, ms =>
                {
                    for (int i = 0; i < rows.Count; i++)
                    {
                        double u = FxTrack.Clamp01((ms - (KeyFlickerStepMs * i)) / 260);
                        rows[i].ScaleX = rows[i].ScaleY = FxTrack.Keys(u, (0, 1, null), (0.4, 1.06, FxTrack.QuadOut), (1, 1, FxTrack.QuadInOut));
                    }
                }, () => { foreach (var s in rows) s.ScaleX = s.ScaleY = 1; }, _fxRuns);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] fx key"); }
        }

        /// <summary>A row answers the click: the pop, and sparks in its board's colour when it goes on.</summary>
        private void FxRow(Control row, bool on)
        {
            FxPop(row, 1.06);
            if (on && (row as ToggleButton)?.BorderBrush is ISolidColorBrush hue) BurstAt(row, hue.Color, 16);
        }

        private void FxSwitch(bool on)
        {
            FxPop(SwitchPill, 1.08);
            if (on) { BurstAt(SwitchPill, CostColour, 30); Shockwave(SwitchPill, CostColour); }
        }

        private void FxConsentShown() => FxStaggerIn(new Control[] { ConsentCard });

        private void FxConsentOk()
        {
            BurstAt(BtnConsentOk, JackpotColour, 44);
            Shockwave(BtnConsentOk, JackpotColour);
            FxPop(SwitchPill, 1.1);
        }

        /// <summary>While the browser is open: a comet laps the hero's rim (one lap in 6 s), so the wait reads as waiting.</summary>
        private void FxLinking(bool on)
        {
            try
            {
                PerimeterCometAdorner.Detach(_linkComet);
                _linkComet = null;
                if (on && IsEffectivelyVisible && Env.AllowAmbientLoops)
                    _linkComet = PerimeterCometAdorner.Attach(HeroCard, HeroRadius, 6);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] fx linking"); }
        }

        /// <summary>The link landed: the hero throws a shower.</summary>
        private void FxLinked()
        {
            BurstAt(HeroCard, JackpotColour, 70);
            Shockwave(HeroCard, JackpotColour);
        }

        // ------------------------------------------------------------------ the bill

        /// <summary>The tag gets a short tug on its string: down 8 px by 30 % of 340 ms (quad out), back with a
        /// little overshoot (BackEase out 0.6). It rides the tag's own translate, so the swing keeps going.</summary>
        private void FxTagTug()
        {
            if (!PaperTag.IsVisible) return;
            FxPop(PaperTag, 1.05);
            if (!Env.AllowTransitions) return;
            FxTrack.Play(340, ms => TagDrop.Y = FxTrack.Keys(ms / 340, (0, 0, null), (0.3, 8, FxTrack.QuadOut), (1, 0, u => FxTrack.BackOut(u, 0.6))),
                () => TagDrop.Y = 0, _fxRuns);
        }

        /// <summary>The bill prints down out of the card instead of appearing: the host grows from nothing to
        /// the receipt's height (clipped, so the paper reveals top first; 420 ms cubic out), fades in over 55 %
        /// of that and settles from 10 px up (BackEase out 0.4). Closing rolls it back up in 260 ms (cubic in).
        /// Transitions off: the end state at once.</summary>
        private void FxBill(bool open)
        {
            int generation = ++_billGeneration;
            double from = ReceiptHost.IsVisible ? ReceiptHost.Bounds.Height : 0;
            double fromOpacity = from > 0 ? ReceiptHost.Opacity : 0;
            var old = _billRun;
            _billRun = null;
            old?.Finish();
            var settle = (TranslateTransform)ReceiptHost.RenderTransform!;
            settle.Y = 0;
            FxChevron(open);

            if (!Env.AllowTransitions || !IsEffectivelyVisible)
            {
                ReceiptHost.Height = double.NaN;
                ReceiptHost.Opacity = 1;
                ReceiptHost.IsVisible = open;
                return;
            }

            if (open)
            {
                ReceiptHost.Height = double.NaN;
                ReceiptHost.IsVisible = true;
                ReceiptHost.Measure(new Size(NumbersPanel.Bounds.Width > 0 ? NumbersPanel.Bounds.Width : double.PositiveInfinity, double.PositiveInfinity));
                double to = Math.Max(0, ReceiptHost.DesiredSize.Height - ReceiptHost.Margin.Top - ReceiptHost.Margin.Bottom);
                ReceiptHost.Height = from;
                _billRun = FxTrack.Play(BillOpenMs, ms =>
                {
                    double u = ms / BillOpenMs;
                    ReceiptHost.Height = from + ((to - from) * FxTrack.CubicOut(u));
                    ReceiptHost.Opacity = FxTrack.Clamp01(fromOpacity + ((1 - fromOpacity) * FxTrack.Clamp01(u / 0.55)));
                    settle.Y = -10 * (1 - FxTrack.BackOut(u, 0.4));
                }, () =>
                {
                    if (generation != _billGeneration) return;
                    ReceiptHost.Height = double.NaN;   // the bill can change length while it is open
                    ReceiptHost.Opacity = 1;
                    settle.Y = 0;
                }, _fxRuns);
                return;
            }

            if (from <= 0) { ReceiptHost.IsVisible = false; ReceiptHost.Height = double.NaN; ReceiptHost.Opacity = 1; return; }
            ReceiptHost.Height = from;
            _billRun = FxTrack.Play(BillCloseMs, ms =>
            {
                double u = ms / BillCloseMs;
                ReceiptHost.Height = from * (1 - FxTrack.CubicIn(u));
                ReceiptHost.Opacity = FxTrack.Clamp01(fromOpacity * (1 - u));
            }, () =>
            {
                if (generation != _billGeneration) return;
                ReceiptHost.IsVisible = false;
                ReceiptHost.Height = double.NaN;
                ReceiptHost.Opacity = 1;
            }, _fxRuns);
        }

        /// <summary>The card's little arrow turns with the bill (cubic out, the bill's own time).</summary>
        private void FxChevron(bool open)
        {
            if (TxtRunChevron.RenderTransform is not RotateTransform turn) return;
            double target = open ? 180 : 0, start = turn.Angle;
            if (!Env.AllowTransitions || !IsEffectivelyVisible) { turn.Angle = target; return; }
            int ms = open ? BillOpenMs : BillCloseMs;
            FxTrack.Play(ms, at => turn.Angle = start + ((target - start) * FxTrack.CubicOut(at / ms)), () => turn.Angle = target, _fxRuns);
        }

        // ------------------------------------------------------------------ the vocabulary

        /// <summary>WPF MotionFx.HoverLift: 1.02 over 150 ms and back, snapped with transitions off.</summary>
        private void FxHoverLift(Control element, bool on)
        {
            if (EnsureScale(element) is not { } scale) return;
            double to = on ? HoverLiftScale : 1, from = scale.ScaleX;
            if (_lifts.Remove(element, out var live)) { live.Finish(); from = scale.ScaleX; }
            if (!Env.AllowTransitions || _pops.ContainsKey(element)) { if (!_pops.ContainsKey(element)) scale.ScaleX = scale.ScaleY = to; return; }
            _lifts[element] = FxTrack.Play(HoverMs, ms => scale.ScaleX = scale.ScaleY = from + ((to - from) * FxTrack.QuadOut(ms / HoverMs)),
                () => _lifts.Remove(element), _fxRuns);
        }

        /// <summary>The house pop: to the peak by 35 % of 380 ms (quad out), back to 1 (quad in-out).</summary>
        internal void FxPop(Control element, double peak)
        {
            if (!Env.AllowTransitions) return;
            if (EnsureScale(element) is not { } scale) return;
            if (_pops.Remove(element, out var live)) live.Finish();
            if (_lifts.Remove(element, out var lift)) lift.Finish();
            double rest = element.IsPointerOver && _liftable.Contains(element) ? HoverLiftScale : 1;
            _pops[element] = FxTrack.Play(PopMs, ms => scale.ScaleX = scale.ScaleY = rest * FxTrack.Pop(ms / PopMs, peak),
                () => { _pops.Remove(element); scale.ScaleX = scale.ScaleY = rest; }, _fxRuns);
        }

        private HashSet<Control> _liftable => _liftableSet ??= new HashSet<Control> { StatTab, StatToday, StatRun, BtnPresetGentle, BtnPresetStrict, BtnPresetCirce, BtnPresetCustom };
        private HashSet<Control>? _liftableSet;

        /// <summary>The scale on an element, made if there is none. A group gets one inserted first so the
        /// stagger's translate keeps working; an element carrying some other transform is left alone.</summary>
        internal static ScaleTransform? EnsureScale(Control element)
        {
            switch (element.RenderTransform)
            {
                case ScaleTransform s:
                    return s;
                case TransformGroup g:
                    if (g.Children.OfType<ScaleTransform>().FirstOrDefault() is { } found) return found;
                    var added = new ScaleTransform(1, 1);
                    g.Children.Insert(0, added);
                    return added;
                case null:
                    var fresh = new ScaleTransform(1, 1);
                    element.RenderTransformOrigin = RelativePoint.Center;
                    element.RenderTransform = fresh;
                    return fresh;
                case TranslateTransform t:
                    var pair = new ScaleTransform(1, 1);
                    element.RenderTransform = new TransformGroup { Children = { pair, t } };
                    return pair;
                default:
                    return null;
            }
        }

        /// <summary>Sparks from the anchor's middle. The spark canvas is SMALL (560 px) and moves to the anchor:
        /// a burst's travel and dot size scale with its canvas (WPF :1160).</summary>
        internal void BurstAt(Control? anchor, Color colour, int count)
        {
            try
            {
                if (anchor == null || !Env.AllowParticles) return;
                if (!anchor.IsEffectivelyVisible || anchor.Bounds.Width <= 0 || anchor.Bounds.Height <= 0) return;
                if (anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2), BurstHost) is not { } centre) return;
                Canvas.SetLeft(BurstLayer, centre.X - (BurstLayer.Width / 2));
                Canvas.SetTop(BurstLayer, centre.Y - (BurstLayer.Height / 2));
                BurstLayer.Burst(BurstLayer.Width / 2, BurstLayer.Height / 2, colour, count);
                LastBurst = (anchor, colour, count);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] burst"); }
        }

        /// <summary>The last burst asked for, for the tests (the particles themselves are the canvas's business).</summary>
        internal (Control Anchor, Color Colour, int Count)? LastBurst { get; private set; }

        /// <summary>Two rings out of the anchor's centre: a bright one and a thin echo 90 ms behind.</summary>
        internal void Shockwave(Control anchor, Color colour)
        {
            try
            {
                if (!Env.AllowTransitions || !anchor.IsEffectivelyVisible || anchor.Bounds.Width <= 0) return;
                if (anchor.TranslatePoint(new Point(anchor.Bounds.Width / 2, anchor.Bounds.Height / 2), ShockwaveCanvas) is not { } centre) return;
                Ring(centre, colour, 0, 2.2, 0.85);
                Ring(centre, colour, 90, 1.5, 0.45);
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[Chaster] shockwave"); }
        }

        /// <summary>One ring: 16 to 260 px over 480 ms (quad out), alpha to 80 % by 35 %, then to nothing; then it
        /// takes itself off the canvas.</summary>
        private void Ring(Point centre, Color colour, int delayMs, double thickness, double alpha)
        {
            double start = ShockwaveFrom / ShockwaveTo;
            var scale = new ScaleTransform(start, start);
            var ring = new Ellipse
            {
                Width = ShockwaveTo, Height = ShockwaveTo, Stroke = new SolidColorBrush(colour), StrokeThickness = thickness,
                Opacity = 0, IsHitTestVisible = false, RenderTransformOrigin = RelativePoint.Center, RenderTransform = scale, Tag = "shock-ring",
            };
            Canvas.SetLeft(ring, centre.X - (ShockwaveTo / 2));
            Canvas.SetTop(ring, centre.Y - (ShockwaveTo / 2));
            ShockwaveCanvas.Children.Add(ring);
            FxTrack.Play(delayMs + ShockwaveMs, ms =>
            {
                double u = (ms - delayMs) / ShockwaveMs;
                if (u < 0) { ring.Opacity = 0; return; }
                scale.ScaleX = scale.ScaleY = start + ((1 - start) * FxTrack.QuadOut(FxTrack.Clamp01(u)));
                ring.Opacity = FxTrack.Clamp01(FxTrack.Keys(u, (0, alpha, null), (0.35, alpha * 0.8, null), (1, 0, null)));
            }, () => ShockwaveCanvas.Children.Remove(ring), _fxRuns);
        }
    }
}
