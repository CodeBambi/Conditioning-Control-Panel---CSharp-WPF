using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The tab's motion, ported from WPF: the season title shimmer (MainWindow.Animations.cs:35
    /// StartSeasonTitleShimmer, started by ShowTab("quests"), gated on MotionFx.AllowAmbientLoops), the floating
    /// particles of AnimatedGradientPanel(Wide) (Resources/Theme/MainWindow.xaml:833/954, a Forever storyboard on
    /// IsVisible) and the quest-complete spark burst (MainWindow.EventFx.cs:362 CelebrateQuestComplete).
    /// One 24 fps clock (WPF AmbientFrameRate) runs only while the tab is shown and attached (P01); tests step
    /// <see cref="Time"/> and call <see cref="StepAmbient"/>.
    /// </summary>
    public partial class QuestsTabView
    {
        private const int QuestBurstCount = 85;        // WPF EventFx QuestBurstCount
        private const double StoryboardSeconds = 24;   // the WPF storyboard's natural length: longest leg 12 s, auto-reversed
        private const double ShimmerSeconds = 3, GlowSeconds = 1.5;

        // (ToY, DurY, ToX, DurX) per particle, in the templates' child order (WPF Particle1..6 storyboards).
        private static readonly (double ToY, double DurY, double ToX, double DurX)[] StdDrift =
            { (-60, 8, 15, 6), (-50, 10, -10, 7), (-70, 12, 20, 9), (-55, 9, -15, 11), (-65, 11, 12, 8), (-45, 7, -8, 10) };
        private static readonly (double ToY, double DurY, double ToX, double DurX)[] WideDrift =
            { (-40, 8, 15, 6), (-35, 10, -10, 7), (-50, 12, 20, 9), (-45, 9, -15, 11), (-55, 11, 12, 8), (-30, 7, -8, 10) };

        internal static TimeProvider Time = TimeProvider.System;

        private readonly EventBurstLayer _burst = new();
        private readonly List<Canvas> _particleCanvases = new();
        private DispatcherTimer? _ambient;
        private long _ambientStart;
        private bool _shimmering;
        private bool _rescanParticles = true;

        /// <summary>Test hooks: the ambient clock is live; quest bursts fired.</summary>
        internal bool AmbientRunning => _ambient?.IsEnabled == true;
        internal int Bursts => _burst.Count;
        internal int ParticleScans { get; private set; }

        /// <summary>Starts the clock when the tab is shown and attached, stops and resets it otherwise
        /// (WPF StopSeasonTitleShimmer / StopStoryboard revert to the base values).</summary>
        private void UpdateAmbient(bool attached)
        {
            bool run = IsVisible && attached;
            if (run == AmbientRunning) return;
            if (run)
            {
                _rescanParticles = true;
                _ambientStart = Time.GetTimestamp();
                _ambient ??= new DispatcherTimer(TimeSpan.FromMilliseconds(1000.0 / 24), DispatcherPriority.Render, (_, _) => StepAmbient());
                _ambient.Start();
                return;
            }
            _ambient!.Stop();
            ResetShimmer();
            foreach (var c in _particleCanvases)
                foreach (var e in c.Children)
                    if (e.RenderTransform is TranslateTransform t) { t.X = 0; t.Y = 0; }
        }

        /// <summary>One ambient tick at <see cref="Time"/>'s now. Hidden pieces (the other sub-tab) do no work.</summary>
        internal void StepAmbient()
        {
            double t = Time.GetElapsedTime(_ambientStart).TotalSeconds;
            // Templates apply on first measure, so the roadmap's panel appears only once that sub-tab is shown:
            // rescan on start and when that panel's template lands (QuestsTabView ctor), never per tick (P07).
            if (_rescanParticles)
            {
                ParticleScans++;
                _particleCanvases.Clear();
                _particleCanvases.AddRange(this.GetVisualDescendants().OfType<Canvas>().Where(c => c.Classes.Contains("qparticles")));
                _rescanParticles = _particleCanvases.Count == 0; // a tick before the first layout looks again
            }
            foreach (var c in _particleCanvases)
            {
                if (!c.IsEffectivelyVisible) continue;
                var drift = Equals(c.Tag, "wide") ? WideDrift : StdDrift;
                for (int i = 0; i < c.Children.Count && i < drift.Length; i++)
                    if (c.Children[i].RenderTransform is TranslateTransform tt)
                    {
                        tt.X = Leg(t, drift[i].ToX, drift[i].DurX);
                        tt.Y = Leg(t, drift[i].ToY, drift[i].DurY);
                    }
            }

            StepFixPulse(t);   // QuestsTabView.StreakFix.cs

            if (Env.AllowAmbientLoops && TxtSeasonTitle.IsEffectivelyVisible
                && TxtSeasonTitle.Foreground is LinearGradientBrush brush && TxtSeasonTitle.Effect is DropShadowEffect glow)
            {
                // StartPoint (-1,.5)->(1,.5) and EndPoint (0,.5)->(2,.5) over 3 s, repeating; glow 0.3<->0.9 every 1.5 s.
                double f = t % ShimmerSeconds / ShimmerSeconds;
                brush.StartPoint = new RelativePoint(-1 + 2 * f, 0.5, RelativeUnit.Relative);
                brush.EndPoint = new RelativePoint(2 * f, 0.5, RelativeUnit.Relative);
                glow.Opacity = 0.3 + 0.6 * Tri(t % (2 * GlowSeconds) / GlowSeconds);
                _shimmering = true;
            }
            else if (_shimmering) ResetShimmer();
        }

        /// <summary>An auto-reversed leg 0 -> to -> 0 over 2 * dur, then held at 0 until the storyboard repeats.</summary>
        private static double Leg(double t, double to, double dur)
        {
            double u = t % StoryboardSeconds / dur;
            return u >= 2 ? 0 : to * Tri(u);
        }

        private static double Tri(double u) => u <= 1 ? u : 2 - u;

        private void ResetShimmer()
        {
            _shimmering = false;
            if (TxtSeasonTitle.Foreground is LinearGradientBrush brush)
            {
                brush.StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative);
                brush.EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative);
            }
            if (TxtSeasonTitle.Effect is DropShadowEffect glow) glow.Opacity = 0.6;
        }

        /// <summary>WPF CelebrateQuestComplete: sparks at the cap of the bar that just filled - the weekly fill, or the
        /// daily card showing <paramref name="definitionId"/>; an unmeasured fill falls back to its track.
        /// ponytail: off-tab WPF bursts on the Quests nav button instead; that needs the shell's event-burst layer
        /// (shell-event-fx). The popup and chime still announce it.</summary>
        private void CelebrateQuestComplete(QuestType type, string? definitionId)
        {
            if (!IsEffectivelyVisible) return;
            UpdateLayout(); // RefreshQuestUI just resized the fills
            Control? fill, track;
            if (type == QuestType.Weekly) { fill = WeeklyProgressFill; track = WeeklyProgressTrack; }
            else
            {
                var board = App.Quests?.GetDailySlots();
                DailyQuestCard? card = null;
                for (int i = 0; board != null && i < board.Count && i < _dailyCards.Length; i++)
                    if (definitionId != null && board[i].Definition?.Id == definitionId) card = _dailyCards[i];
                fill = card?.ProgressFill;
                track = card?.ProgressTrack ?? DailyCardsGrid;
            }
            if (fill is { Bounds.Width: > 1 } && _burst.Fire(fill, QuestBurstCount, rightEdge: true)) return;
            if (track != null) _burst.Fire(track, QuestBurstCount, rightEdge: true);
        }
    }
}
