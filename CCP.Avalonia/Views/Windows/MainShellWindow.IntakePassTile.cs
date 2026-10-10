// PORTED from WPF 7.1.5 ConditioningControlPanel/MainWindow/MainWindow.UiUpdates.cs, the Intake Pass
// tile (:1300-1925): RefreshIntakePassTile, SetIntakePassFace, ApplyIntakePassFaceState,
// StartIntakePassFlipLoop, HoldIntakePassFace, RunIntakePassSpinPhase, FinishIntakePassSpin,
// CancelIntakePassSpin, and Services/Quiz/IntakeNiche.PassCardImage.
//
// The Home wall's centre logo doubles as the weekly Graded Intake pass card: while a free account has
// an unspent run (IntakePassState.Available, the ONLY state that shows the card, as WPF), the tile
// holds the logo for 8 s, turns on its Y axis to the pass card, holds, and turns back. Premium,
// signed-out and spent-week accounts only ever see the logo.
//
// The turn is the WPF fake: ScaleX 1 -> 0 -> 1 with a 6 degree skew at the thin point, five half
// turns (105, 115, 130, 150, 560 ms, the last one eased), the face swapping at each thin point.
// Every step is a finite TransformTween chained by one-shot timers and a generation token, so
// leaving the page, a state change or Motion Off cancels it at once and leaves no loop behind.
// Motion Off (no ambient loops) shows the card still, as WPF's off-screen branch does.
//
// The click (SettingsTabView.IntakePassFace_MouseLeftButtonDown) and the (?) popover content
// (MainShellWindow.HelpButtons.cs) were already live.
// Not ported: the CTA breath (StartIntakePassCtaPulse, a forever scale loop on the CTA line).

using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Quiz;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF IntakePassFaceHoldMs: how long each face rests between turns.</summary>
        internal static int IntakePassFaceHoldMs = 8000;
        private static readonly int[] IntakePassHalfTurnMs = { 105, 115, 130, 150, 560 };
        private const double IntakePassSkewDeg = 6.0;

        private bool _intakePassHooked;
        private bool _intakePassLoopRunning;
        private int _intakePassSpinGen;
        private DispatcherTimer? _intakePassScaleTween, _intakePassSkewTween;

        /// <summary>True while the pass card is the face showing (tests).</summary>
        internal bool IntakePassShowingCard { get; private set; }

        /// <summary>True while the tile is alternating (tests).</summary>
        internal bool IntakePassLoopRunning => _intakePassLoopRunning;

        /// <summary>WPF IntakeNiche.PassCardImage: a mod's own intake/pass_card.png wins, else the
        /// niche card this build ships; null hides the art and leaves the vector card.</summary>
        internal static Bitmap? IntakePassCardImage()
        {
            try
            {
                const string generic = "intake/pass_card.png";
                if (CoreModArt.HasOverride(generic)) return ModArt.TryLoad(generic, 768);
                var niche = IntakeRun.ResolveNiche(CoreMods.ActiveModId, CoreMods.ActiveModPackage?.Manifest?.Tags,
                    CoreSettings.Current.ContentMode == ContentMode.SissyHypno);
                return ModArt.TryLoad($"intake/pass_card_{niche}.png", 768);
            }
            catch { return null; }
        }

        /// <summary>WPF RefreshIntakePassTile: decide the face from the pass state, from scratch.</summary>
        internal void RefreshIntakePassTile()
        {
            try
            {
                if (SettingsPage is not { } dash) return;
                var face = dash.FindControl<Border>("IntakePassFace");
                var logo = dash.FindControl<Border>("LogoFaceLogo");
                if (face == null || logo == null) return;

                if (!_intakePassHooked)
                {
                    _intakePassHooked = true;
                    var pass = App.IntakePass;
                    EventHandler onState = (_, _) => Dispatcher.UIThread.Post(RefreshIntakePassTile);
                    pass.PassStateChanged += onState;
                    Action onMotion = () => Dispatcher.UIThread.Post(RefreshIntakePassTile);
                    AmbientFxCanvas.Env.MotionGateChanged += onMotion;
                    // Leaving Home stops the turn; coming back re-reads the state.
                    dash.PropertyChanged += (_, e) =>
                    {
                        if (e.Property != IsVisibleProperty) return;
                        if (!dash.IsVisible) CancelIntakePassSpin();
                        else RefreshIntakePassTile();
                    };
                    Opened += (_, _) => RefreshIntakePassTile();   // the first paint ran before the page was on screen
                    Closed += (_, _) =>
                    {
                        pass.PassStateChanged -= onState;
                        AmbientFxCanvas.Env.MotionGateChanged -= onMotion;
                        CancelIntakePassSpin();
                    };
                }

                var state = App.IntakePass.State;
                if (state != IntakePassState.Available)
                {
                    CancelIntakePassSpin();
                    SetIntakePassFace(false);
                    return;
                }

                ApplyIntakePassFaceState(state);

                var onScreen = dash.IsVisible && dash.IsEffectivelyVisible;
                if (!onScreen || !AmbientFxCanvas.Env.AllowAmbientLoops)
                {
                    // A hidden page or Motion Off: the card, still. Nothing turns.
                    CancelIntakePassSpin();
                    SetIntakePassFace(true);
                    return;
                }
                if (_intakePassLoopRunning) return;   // already alternating; do not restart it
                StartIntakePassFlipLoop();
            }
            catch (Exception ex) { Log.Debug("RefreshIntakePassTile: {E}", ex.Message); }
        }

        /// <summary>WPF SetIntakePassFace: exactly one face is ever visible.</summary>
        private void SetIntakePassFace(bool showCard)
        {
            if (SettingsPage is not { } dash) return;
            if (dash.FindControl<Border>("IntakePassFace") is { } face) face.IsVisible = showCard;
            if (dash.FindControl<Border>("LogoFaceLogo") is { } logo) logo.IsVisible = !showCard;
            IntakePassShowingCard = showCard;
        }

        /// <summary>WPF ApplyIntakePassFaceState: the niche art and the state's copy.</summary>
        private void ApplyIntakePassFaceState(IntakePassState state)
        {
            if (SettingsPage is not { } dash) return;
            var art = IntakePassCardImage();
            var hasArt = art != null;

            if (dash.FindControl<Border>("IntakePassArt") is { } artHost)
            {
                artHost.Background = hasArt
                    ? new ImageBrush(art) { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Center, AlignmentY = AlignmentY.Center }
                    : null;
                artHost.IsVisible = hasArt;
                artHost.Opacity = state == IntakePassState.Spent ? 0.45 : 1.0;
            }
            if (dash.FindControl<Border>("IntakePassScrim") is { } scrim) scrim.IsVisible = hasArt;
            if (dash.FindControl<StackPanel>("IntakePassVectorBody") is { } vector) vector.IsVisible = !hasArt;

            string headline, body, cta, tip;
            switch (state)
            {
                case IntakePassState.NeedsLogin:
                    headline = Loc.Get("intake_pass_card_headline");
                    body = Loc.Get("intake_pass_card_body_needs_login");
                    cta = Loc.Get("intake_pass_card_cta_needs_login");
                    tip = Loc.Get("tooltip_intake_pass_card_needs_login");
                    break;
                case IntakePassState.Spent:
                {
                    var days = IntakePassService.DaysUntilNextPass;
                    headline = Loc.Get("intake_pass_card_headline_spent");
                    body = Loc.GetF("intake_pass_card_body_spent", days);
                    cta = days <= 1 ? Loc.Get("intake_pass_card_cta_spent_tomorrow") : Loc.GetF("intake_pass_card_cta_spent", days);
                    tip = Loc.GetF("tooltip_intake_pass_card_spent", days);
                    break;
                }
                default:
                    headline = Loc.Get("intake_pass_card_headline");
                    body = Loc.Get("intake_pass_card_body");
                    cta = Loc.Get("intake_pass_card_cta");
                    tip = Loc.Get("tooltip_intake_pass_card");
                    break;
            }

            if (dash.FindControl<TextBlock>("IntakePassHeadline") is { } h) h.Text = headline;
            if (dash.FindControl<TextBlock>("IntakePassBody") is { } b) b.Text = body;
            if (dash.FindControl<TextBlock>("IntakePassCta") is { } c)
            {
                c.Text = cta;
                c.Foreground = state == IntakePassState.Spent && this.TryFindResource("TextMutedBrush", out var muted) && muted is IBrush mb
                    ? mb : Brushes.White;
            }
            if (dash.FindControl<Border>("IntakePassFace") is { } face) ToolTip.SetTip(face, tip);
        }

        // ---- the turn -----------------------------------------------------------------------

        private (ScaleTransform? Scale, SkewTransform? Skew) IntakePassFlipTransforms()
        {
            var group = SettingsPage?.FindControl<Grid>("LogoFlipHost")?.RenderTransform as TransformGroup;
            ScaleTransform? scale = null;
            SkewTransform? skew = null;
            if (group != null)
                foreach (var t in group.Children)
                {
                    if (t is ScaleTransform s) scale = s;
                    else if (t is SkewTransform k) skew = k;
                }
            return (scale, skew);
        }

        /// <summary>WPF CancelIntakePassSpin: a new generation orphans every chained step, and the
        /// tile comes to rest square.</summary>
        private void CancelIntakePassSpin()
        {
            _intakePassSpinGen++;
            _intakePassLoopRunning = false;
            _intakePassScaleTween?.Stop();   // a half-finished turn must not land on the thin point
            _intakePassSkewTween?.Stop();
            _intakePassScaleTween = _intakePassSkewTween = null;
            var (scale, skew) = IntakePassFlipTransforms();
            if (scale != null) scale.ScaleX = 1;
            if (skew != null) skew.AngleY = 0;
        }

        private void StartIntakePassFlipLoop()
        {
            CancelIntakePassSpin();
            var gen = _intakePassSpinGen;
            _intakePassLoopRunning = true;
            SetIntakePassFace(false);   // the logo first, so the first thing the eye catches is the turn
            HoldIntakePassFace(gen);
        }

        private void HoldIntakePassFace(int gen)
        {
            DispatcherTimer.RunOnce(() =>
            {
                if (gen != _intakePassSpinGen) return;
                RunIntakePassSpinPhase(gen, 0);
            }, TimeSpan.FromMilliseconds(Math.Max(1, IntakePassFaceHoldMs)));
        }

        private void RunIntakePassSpinPhase(int gen, int phase)
        {
            if (gen != _intakePassSpinGen) return;
            var (scale, skew) = IntakePassFlipTransforms();
            if (scale == null || skew == null) { _intakePassLoopRunning = false; return; }

            var halfTurn = phase / 2;
            var closing = phase % 2 == 0;
            if (halfTurn >= IntakePassHalfTurnMs.Length)
            {
                // WPF FinishIntakePassSpin: square up, then rest on the face the turn ended on.
                scale.ScaleX = 1;
                skew.AngleY = 0;
                HoldIntakePassFace(gen);
                return;
            }

            var duration = TimeSpan.FromMilliseconds(Math.Max(1, IntakePassHalfTurnMs[halfTurn] / 2));
            Easing? ease = halfTurn >= IntakePassHalfTurnMs.Length - 1
                ? (closing ? new CubicEaseIn() : new CubicEaseOut())
                : null;
            _intakePassScaleTween = TransformTween.Run(scale, duration, new (double, AvaloniaProperty, double)[]
            {
                (0, ScaleTransform.ScaleXProperty, closing ? 1.0 : 0.0), (1, ScaleTransform.ScaleXProperty, closing ? 0.0 : 1.0),
            }, ease);
            _intakePassSkewTween = TransformTween.Run(skew, duration, new (double, AvaloniaProperty, double)[]
            {
                (0, SkewTransform.AngleYProperty, closing ? 0.0 : -IntakePassSkewDeg), (1, SkewTransform.AngleYProperty, closing ? IntakePassSkewDeg : 0.0),
            }, ease);
            DispatcherTimer.RunOnce(() =>
            {
                if (gen != _intakePassSpinGen) return;
                if (closing) SetIntakePassFace(!IntakePassShowingCard);   // swap at the thin point
                RunIntakePassSpinPhase(gen, phase + 1);
            }, duration);
        }
    }
}
