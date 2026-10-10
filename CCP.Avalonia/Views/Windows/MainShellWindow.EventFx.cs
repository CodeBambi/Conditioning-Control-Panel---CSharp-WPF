// PORTED-AS-A-NOTE from ConditioningControlPanel/MainWindow/MainWindow.EventFx.cs (460 lines).
//
// This one is NOT blocked on the framework, and that is worth stating plainly so a later layer
// does not re-derive it: AmbientFxCanvas.Burst (Controls/AmbientFxCanvas.cs:333) is a full port,
// so the particle burst every Celebrate* method fires can be drawn on this head today. What is
// missing is every CALLER - the progression moments themselves:
//
//   CelebrateLevelUp                <- the XP/level path (MainWindow.ProfileBubble.cs)
//   CelebrateAchievementUnlock      <- MainWindow.AchievementsTab.cs:782 (on-tile reveal + burst: AchievementsTabView.Fx.cs)
//   CelebrateQuestComplete          <- MainWindow.Quests.cs:77 (on-tab burst: QuestsTabView.Fx.cs)
//   CelebrateProgramDayComplete     <- MainWindow.ProgramsTab.cs:1020
//   CelebrateEnhancementPurchase    <- MainWindow.Enhancements.cs:2059
//   CelebratePrestige               <- the prestige rank roll
//
// Not one of those six sites exists on this head. Restoring the six methods now would add ~200
// lines of overlay plumbing that nothing can call and nothing can prove: the whole file is
// "when X happens, burst here", and X does not happen yet. It comes back with the progression
// wiring that fires it, not before - and when it does, EnsureEventBurstLayer is a Panel insert
// into the shell's root grid plus one Burst call, because the canvas is already here.
//
// The one piece that WOULD need thought at that point, named so it is not a surprise: the
// achievement tile reveal (AchievementRevealMs / RevealAchievementTile) composes a blur radius
// with a scale as the tile lands. Avalonia has BlurEffect and can animate it, but a blur
// animation per unlocked tile is a real cost at the Performance tier, and the tier gate
// (EventFxAllowed) is MotionFx + PerformanceProfile, still in the WPF head.
//
// Members dropped (28):
//   private const double BurstBoxPx
//   private const int LevelUpBurstCount
//   private const int AchievementBurstCount
//   private const int QuestBurstCount
//   private const int ProgramDayBurstCount
//   private const int EnhancementBurstCount
//   private const int PrestigeBurstCount
//   private const double PrestigeSheenSeconds
//   private const double PrestigeSheenPeak
//   private const int AchievementRevealMs
//   private const double AchievementRevealBlurRadius
//   private const double AchievementRevealScale
//   private AmbientFxCanvas? _eventBurstLayer
//   private bool _eventBurstLayerFailed
//   private Border? _prestigeRowBorder
//   private static long PrestigeRankNow(…)
//   private bool EventFxAllowed
//   private AmbientFxCanvas? EnsureEventBurstLayer(…)
//   internal bool FireBurstAt(…)
//   private void FireBurstAtFirstVisible(…)
//   internal void CelebrateLevelUp(…)
//   internal void CelebrateAchievementUnlock(…)
//   private void RevealAchievementTile(…)
//   internal void CelebrateQuestComplete(…)
//   private Views.Controls.DailyQuestCard? FindDailyCard(…)
//   internal void CelebrateProgramDayComplete(…)
//   internal void CelebrateEnhancementPurchase(…)
//   internal void CelebratePrestige(…)

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.

        // WPF MainWindow.EventFx.cs.
        internal const double PrestigeSheenSeconds = 1.15;
        internal const double PrestigeSheenPeak = 0.16;

        private global::Avalonia.Threading.DispatcherTimer? _prestigeSheenTween;

        /// <summary>Test seam: the prestige sheen is crossing.</summary>
        internal bool PrestigeSheenRunning => _prestigeSheenTween?.IsEnabled == true;

        /// <summary>Test seam: forces the gate instead of asking the head and the window.</summary>
        internal bool? PrestigeSheenGateOverride { get; set; }

        /// <summary>
        /// WPF CelebratePrestige, the sheen half (SweepSheen, MainWindow.ChromeFx.cs:610): one band
        /// crosses the whole chrome in 1.15 s, sine in-out, from one band-width off the left edge
        /// to a quarter band past the right; its opacity rises to 0.16 by 20%, holds to 75% and
        /// is gone at the end. Refused like WPF EventFxAllowed (particles allowed, window active
        /// and not minimised). The burst half belongs to the Skill Tree (EnhancementsTabView).
        /// </summary>
        internal bool SweepPrestigeSheen()
        {
            try
            {
                bool allowed = PrestigeSheenGateOverride
                    ?? (global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.AllowParticles
                        && IsActive && WindowState != global::Avalonia.Controls.WindowState.Minimized);
                if (!allowed) return false;
                var band = Named<global::Avalonia.Controls.Border>("PrestigeSheen");
                if (band?.Parent is not global::Avalonia.Controls.Control host
                    || band.RenderTransform is not global::Avalonia.Media.TransformGroup group) return false;
                global::Avalonia.Media.TranslateTransform? slide = null;
                foreach (var t in group.Children) if (t is global::Avalonia.Media.TranslateTransform tt) slide = tt;
                double width = host.Bounds.Width;
                if (slide == null || width <= 0) return false;
                double bandWidth = double.IsNaN(band.Width) || band.Width <= 0 ? 80 : band.Width;
                double from = -bandWidth, to = width + bandWidth * 0.25;

                _prestigeSheenTween?.Stop();
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                global::Avalonia.Threading.DispatcherTimer? timer = null;
                void Step(double p)
                {
                    double e = (1 - System.Math.Cos(System.Math.PI * p)) / 2;   // SineEase, EaseInOut
                    slide.X = from + (to - from) * e;
                    double a = p < 0.20 ? p / 0.20 : p <= 0.75 ? 1 : (1 - p) / 0.25;
                    band.Opacity = System.Math.Clamp(PrestigeSheenPeak * a, 0, 1);
                }
                Step(0);
                timer = new global::Avalonia.Threading.DispatcherTimer(System.TimeSpan.FromMilliseconds(16),
                    global::Avalonia.Threading.DispatcherPriority.Render, (_, _) =>
                {
                    double p = System.Math.Clamp(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds / PrestigeSheenSeconds, 0, 1);
                    try { Step(p); } catch { p = 1; }
                    if (p < 1) return;
                    timer!.Stop();
                    band.Opacity = 0;      // parked, holding no clock (WPF ParkSheen)
                    slide.X = 0;
                });
                _prestigeSheenTween = timer;
                timer.Start();
                return true;
            }
            catch (System.Exception ex) { Serilog.Log.Debug("SweepPrestigeSheen: {E}", ex.Message); return false; }
        }
    }
}
