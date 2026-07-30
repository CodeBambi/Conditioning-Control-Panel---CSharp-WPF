using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-3 of the FX overhaul: the *Leaderboard* pass. Everything here decorates
    /// Views/Tabs/LeaderboardTabView.xaml; the jump-to-me flare and the scroll choreography are
    /// older work and stay in MainWindow.Leaderboard.cs (this file only aligns their gating).
    ///
    /// One loop, two event moments, per FX plan section 4:
    ///   • hero  - the #1 podium card breathes the gold glow its DataTrigger already gives it.
    ///     Nothing new is drawn: the trigger's DropShadowEffect is sealed (and therefore frozen),
    ///     so it is cloned onto the card as a live effect and only its Opacity moves.
    ///   • micro - podium entrance stagger (the three cards only - the roster is virtualized and
    ///     must stay that way), and a one-shot tinted wash on rows whose rank actually moved.
    ///
    /// The roster is a Recycling VirtualizingStackPanel, which is why every decoration here is
    /// remembered and stripped: a container that keeps a local Background hands it to whatever row
    /// it is recycled onto next, and a stuck green wash would migrate onto a stranger.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        /// <summary>The template's own #1 glow sits at 0.28; the breath brackets it rather than
        /// brightening the card - the winner should pulse, not get promoted.</summary>
        private const double PodiumGlowMinOpacity = 0.16;
        private const double PodiumGlowMaxOpacity = 0.36;
        private const double PodiumGlowSeconds = 4.2;

        /// <summary>Peak alpha of the rank-change wash. Deliberately faint: it has to read as a
        /// pulse of colour under the text, not as a selection.</summary>
        private const double RankFlashPeakOpacity = 0.30;
        private const int RankFlashMs = 400;
        private const int RankFlashRiseMs = 70;
        private const int RankFlashStaggerMs = 35;

        /// <summary>How many moved rows may flash at once. A board where forty rows shifted would
        /// otherwise fire forty washes and read as a strobe rather than as news.</summary>
        private const int RankFlashMaxRows = 12;

        // ---- state -----------------------------------------------------------------

        private bool _leaderboardFxInitialized;

        /// <summary>The podium card currently carrying our live (unfrozen) glow effect.</summary>
        private FrameworkElement? _lbPodiumGlowCard;

        /// <summary>The glow opacity the template asked for, so parking restores exactly that
        /// rather than a number this file invented.</summary>
        private double _lbPodiumGlowRest = PodiumGlowMaxOpacity;

        /// <summary>Podium cards holding an entrance animation, cleared on the next rebuild.</summary>
        private readonly List<FrameworkElement> _lbPodiumStaggered = new();

        /// <summary>Row containers currently holding a flash brush.</summary>
        private readonly List<FrameworkElement> _lbFlashDecorated = new();

        /// <summary>Set by a real refresh; consumed by the next rebuild. Filter/search/sort
        /// rebuilds do not flash - nothing about the standings changed, only the view of them.</summary>
        private bool _lbRankFlashPending;

        /// <summary>Window focus + tab visibility + reduced-motion + tier: the one ambient gate.</summary>
        private bool LeaderboardAmbientAllowed =>
            IsActive
            && WindowState != WindowState.Minimized
            && LeaderboardTab?.IsVisible == true
            && MotionFx.AllowAmbientLoops;

        // ============================== lifecycle ==============================

        /// <summary>
        /// Wires the tab's FX hooks up once, on its first rebuild. Nothing is created here that a
        /// user who never opens the Leaderboard has to pay for.
        /// </summary>
        private void EnsureLeaderboardFx()
        {
            if (_leaderboardFxInitialized) return;
            _leaderboardFxInitialized = true;
            try
            {
                Activated += OnLeaderboardFxWindowStateish;
                Deactivated += OnLeaderboardFxWindowStateish;
                StateChanged += OnLeaderboardFxWindowStateish;
                if (LeaderboardTab != null)
                    LeaderboardTab.IsVisibleChanged += OnLeaderboardTabVisibilityChanged;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "EnsureLeaderboardFx failed"); }
        }

        private void OnLeaderboardFxWindowStateish(object? sender, EventArgs e) => ApplyLeaderboardFxLoops();

        private void OnLeaderboardTabVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                // Leaving the tab: drop every transient wash before the containers are recycled.
                if (LeaderboardTab?.IsVisible != true) ClearRankFlashes();
                ApplyLeaderboardFxLoops();
            }
            catch (Exception ex) { App.Logger?.Debug("OnLeaderboardTabVisibilityChanged: {E}", ex.Message); }
        }

        /// <summary>Re-evaluates the tab's only ambient loop against the current gate.</summary>
        private void ApplyLeaderboardFxLoops()
        {
            if (!_leaderboardFxInitialized) return;
            try { ApplyPodiumGlowBreath(); }
            catch (Exception ex) { App.Logger?.Debug("ApplyLeaderboardFxLoops: {E}", ex.Message); }
        }

        // ============================== the FX pass ==============================

        /// <summary>
        /// Runs after every rebuild has pushed its new ItemsSource: decorate the podium, and wash
        /// the rows that moved if this rebuild came from a refresh.
        ///
        /// <paramref name="gold"/> is the rank-1 entry (or null when the podium is hidden - a
        /// filtered or searched board has no podium and no hero loop).
        /// </summary>
        private void RunLeaderboardFxPass(Services.LeaderboardEntry? gold, List<Services.LeaderboardEntry>? moved)
        {
            var tab = LeaderboardTab;
            if (tab == null) return;

            bool wantFlash = moved is { Count: > 0 } && MotionFx.AllowTransitions;
            if (gold == null && !wantFlash) { ClearPodiumFx(); return; }

            try
            {
                // Containers are generated during layout, so force one rather than betting on a
                // dispatcher priority. Both halves of the pass share this single call.
                tab.UpdateLayout();
            }
            catch (Exception ex) { App.Logger?.Debug("Leaderboard FX layout: {E}", ex.Message); }

            if (gold != null) ApplyPodiumFx(gold);
            else ClearPodiumFx();

            if (wantFlash) FlashMovedRows(moved!);
        }

        // ============================== podium ==============================

        /// <summary>Entrance stagger on the three podium cards plus the #1 glow breath.</summary>
        private void ApplyPodiumFx(Services.LeaderboardEntry gold)
        {
            var host = LeaderboardTab?.PodiumHost;
            if (host == null) return;

            try
            {
                ClearPodiumFx();

                var cards = new List<FrameworkElement>();
                for (int i = 0; i < host.Items.Count; i++)
                {
                    var container = host.ItemContainerGenerator.ContainerFromIndex(i);
                    if (FindVisualDescendant<Border>(container) is { } card) cards.Add(card);
                }
                if (cards.Count == 0) return;

                // The three podium cards only. The roster below them is virtualized: staggering
                // realised rows would animate whatever happened to be on screen and then do it
                // again for every row that scrolls into view.
                MotionFx.StaggerIn(cards);
                _lbPodiumStaggered.AddRange(cards);

                var goldCard = FindVisualDescendant<Border>(
                    host.ItemContainerGenerator.ContainerFromItem(gold));
                if (goldCard == null) return;
                _lbPodiumGlowCard = goldCard;
                ApplyPodiumGlowBreath();
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyPodiumFx: {E}", ex.Message); }
        }

        /// <summary>
        /// Breathe the #1 card's glow. The card's Effect comes from a DataTemplate.Triggers setter
        /// and is frozen once the template is sealed, so it cannot be animated in place - we clone
        /// it (keeping the podium's own gold, which is a RANK colour and deliberately not the mod
        /// accent) and animate the clone's Opacity only. Blur radius is left alone: an animated
        /// BlurRadius re-rasterises the kernel every frame, which is the expensive part.
        /// </summary>
        private void ApplyPodiumGlowBreath()
        {
            var card = _lbPodiumGlowCard;
            if (card == null) return;
            try
            {
                var tier = PerformanceProfile.CurrentTier;
                if (!PerformanceProfile.AllowGlow(tier) || MotionFx.Level == MotionLevel.Off)
                {
                    ReleasePodiumGlow();
                    return;
                }

                if (card.Effect is not DropShadowEffect effect) return;
                if (effect.IsFrozen)
                {
                    _lbPodiumGlowRest = effect.Opacity;
                    effect = effect.Clone();
                    effect.BlurRadius = Math.Min(effect.BlurRadius, PerformanceProfile.MaxGlowBlurRadius(tier));
                    card.Effect = effect;
                }

                if (!LeaderboardAmbientAllowed)
                {
                    effect.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                    effect.Opacity = _lbPodiumGlowRest;
                    return;
                }

                var anim = new DoubleAnimation(PodiumGlowMinOpacity, PodiumGlowMaxOpacity,
                                               TimeSpan.FromSeconds(PodiumGlowSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                effect.BeginAnimation(DropShadowEffect.OpacityProperty, anim);
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyPodiumGlowBreath: {E}", ex.Message); }
        }

        /// <summary>Hands the card back to its template: clock off, local Effect cleared.</summary>
        private void ReleasePodiumGlow()
        {
            var card = _lbPodiumGlowCard;
            if (card == null) return;
            try
            {
                if (card.Effect is DropShadowEffect effect && !effect.IsFrozen)
                    effect.BeginAnimation(DropShadowEffect.OpacityProperty, null);
                card.ClearValue(UIElement.EffectProperty);
            }
            catch { }
        }

        private void ClearPodiumFx()
        {
            try
            {
                ReleasePodiumGlow();
                _lbPodiumGlowCard = null;

                foreach (var card in _lbPodiumStaggered)
                {
                    card.BeginAnimation(UIElement.OpacityProperty, null);
                    card.Opacity = 1;
                    if (card.RenderTransform is TranslateTransform t)
                    {
                        t.BeginAnimation(TranslateTransform.YProperty, null);
                        t.Y = 0;
                    }
                }
                _lbPodiumStaggered.Clear();
            }
            catch (Exception ex) { App.Logger?.Debug("ClearPodiumFx: {E}", ex.Message); }
        }

        // ============================== rank-change wash ==============================

        /// <summary>
        /// The rows whose delta arrow is about to render as a real move, in display order and
        /// capped. "new" rows are excluded on purpose: a subject appearing for the first time has
        /// not gone anywhere, and on a first-ever refresh that would be the entire board.
        /// </summary>
        private static List<Services.LeaderboardEntry> CollectMovedRows(IEnumerable<object> display)
        {
            var moved = new List<Services.LeaderboardEntry>();
            foreach (var item in display)
            {
                if (item is not Services.LeaderboardEntry entry) continue;
                if (entry.DeltaState is not ("up" or "down")) continue;
                moved.Add(entry);
                if (moved.Count >= RankFlashMaxRows) break;
            }
            return moved;
        }

        /// <summary>
        /// One-shot tinted wash on each moved row that is actually realised - green for a climb,
        /// red for a slide, 400ms, staggered so a run of movers reads as a cascade rather than a
        /// flashbulb. Off-screen movers are skipped outright: nobody is looking at them, and
        /// realising them to decorate them would defeat the roster's virtualization.
        ///
        /// The wash is a private brush assigned to the container's Background - NOT an animation
        /// on the Background property, which would out-rank the ItemContainerStyle's DataTriggers
        /// and hold the row in the wrong colour afterwards. Only the brush's own Opacity moves,
        /// and ClearValue on completion hands the row straight back to its triggers.
        /// </summary>
        private void FlashMovedRows(List<Services.LeaderboardEntry> moved)
        {
            var list = LeaderboardTab?.LstLeaderboard;
            if (list == null) return;

            try
            {
                ClearRankFlashes();

                var up = ThemeColor("SuccessGreen", Colors.LimeGreen);
                var down = ThemeColor("Danger", Colors.OrangeRed);

                int shown = 0;
                foreach (var entry in moved)
                {
                    if (list.ItemContainerGenerator.ContainerFromItem(entry) is not FrameworkElement row) continue;
                    if (!row.IsVisible) continue;

                    var color = entry.DeltaState == "up" ? up : down;
                    FlashRow(row, color, shown * RankFlashStaggerMs);
                    shown++;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("FlashMovedRows: {E}", ex.Message); }
        }

        private void FlashRow(FrameworkElement row, Color color, int delayMs)
        {
            try
            {
                var brush = new SolidColorBrush(color) { Opacity = 0 };
                if (row is Control control) control.Background = brush;
                else if (row is Border border) border.Background = brush;
                else return;
                _lbFlashDecorated.Add(row);

                var wash = new DoubleAnimationUsingKeyFrames
                {
                    BeginTime = TimeSpan.FromMilliseconds(delayMs),
                    Duration = TimeSpan.FromMilliseconds(RankFlashMs),
                    FillBehavior = FillBehavior.Stop,
                };
                wash.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                wash.KeyFrames.Add(new EasingDoubleKeyFrame(RankFlashPeakOpacity,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(RankFlashRiseMs)),
                    new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                wash.KeyFrames.Add(new EasingDoubleKeyFrame(0,
                    KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(RankFlashMs)),
                    new QuadraticEase { EasingMode = EasingMode.EaseIn }));

                wash.Completed += (_, _) =>
                {
                    try
                    {
                        if (Application.Current?.Dispatcher == null) return;
                        if (Application.Current.Dispatcher.HasShutdownStarted) return;
                        StripRowFlash(row);
                        _lbFlashDecorated.Remove(row);
                    }
                    catch { }
                };

                brush.BeginAnimation(Brush.OpacityProperty, wash);
            }
            catch (Exception ex) { App.Logger?.Debug("FlashRow: {E}", ex.Message); }
        }

        private void ClearRankFlashes()
        {
            try
            {
                for (int i = 0; i < _lbFlashDecorated.Count; i++) StripRowFlash(_lbFlashDecorated[i]);
                _lbFlashDecorated.Clear();
            }
            catch (Exception ex) { App.Logger?.Debug("ClearRankFlashes: {E}", ex.Message); }
        }

        /// <summary>
        /// ClearValue rather than "= null": Background is set by the ItemContainerStyle's
        /// triggers, and clearing the LOCAL value is what hands the row back to them intact.
        /// </summary>
        private static void StripRowFlash(FrameworkElement row)
        {
            try
            {
                if (row is Control) row.ClearValue(Control.BackgroundProperty);
                else if (row is Border) row.ClearValue(Border.BackgroundProperty);
            }
            catch { }
        }

        /// <summary>A theme Color by key, with a named-colour fallback (never a literal hex).</summary>
        private static Color ThemeColor(string key, Color fallback)
        {
            try
            {
                if (Application.Current?.Resources[key] is Color c) return c;
            }
            catch { }
            return fallback;
        }
    }
}
