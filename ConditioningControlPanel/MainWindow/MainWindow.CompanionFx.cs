using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    /// <summary>
    /// PR-4b of the FX overhaul: the <b>Companion</b> tab pass.
    ///
    /// One focal loop, per FX plan section 4:
    ///   • hero  - the hero avatar disc breathes 1.00 to 1.015 over 5s. That is the tab's only
    ///     clock, and it is a RenderTransform on the ring Border, so the clip, the pink ring and
    ///     the portrait scale together and no layout runs.
    ///   • micro - a successful provider connection test sweeps ONE sheen across the AI Brain card.
    ///     Event-driven and self-tearing-down: nothing on that card ticks while it sits idle.
    ///
    /// Deliberately NOT here:
    ///   * the avatar tube window. That is a separate top-level window owned by AvatarTubeWindow,
    ///     with its own poses, blinks and z-order plumbing; this file only ever touches the
    ///     Companion TAB's own art.
    ///   * a rare blink/emote accent on the hero. The plan allows one only if an existing emote
    ///     mechanism is reachable from this tab - it is not. RefreshHeroAvatar
    ///     (MainWindow.CompanionTab.cs) knows exactly one image, pose1; every pose-swapping,
    ///     blink-scheduling piece of machinery lives inside AvatarTubeWindow. Building a second
    ///     one here would be a new emote system, which the plan rules out.
    ///
    /// House rules (FX plan section 2) this file obeys:
    ///   * the one ambient clock asks <see cref="CompanionFxOnScreen"/> and
    ///     <see cref="MotionFx.AllowAmbientLoops"/> BEFORE it starts, and parks otherwise;
    ///   * colour only ever from <see cref="FxTheme"/> - the sheen re-tints with the mod;
    ///   * <c>BeginAnimation</c> / <c>ApplyAnimationClock</c> only. Storyboard.SetTargetName
    ///     silently no-ops across the tab UserControl name scope;
    ///   * every callback is wrapped. Decoration may never be why the Companion tab throws.
    /// </summary>
    public partial class MainWindow
    {
        // ---- tuning ----------------------------------------------------------------

        /// <summary>Hero breath: 1.5% on a 96px disc is 0.72px of growth per side, which sits well
        /// inside the hero strip's 16px padding. Anything larger reads as a pulse, not breathing.</summary>
        private const double CompanionHeroBreathScale = 1.015;
        private const double CompanionHeroBreathSeconds = 5.0;

        /// <summary>Corner radius of the AI Brain card, so the sheen's rounded rect matches it.</summary>
        private const double CompanionAiCardCornerRadius = 14;

        /// <summary>One sweep of <see cref="CardSheenAdorner"/> takes 1.3s; the adorner comes off a
        /// beat later so the band has fully left the card before it disappears.</summary>
        private const double CompanionSheenHoldSeconds = 1.75;

        // ---- state -----------------------------------------------------------------

        private bool _companionFxInitialized;
        private ScaleTransform? _companionHeroScale;
        private AnimationClock? _companionHeroClock;

        private CardSheenAdorner? _companionSheen;
        private AdornerLayer? _companionSheenLayer;
        private DispatcherTimer? _companionSheenTimer;

        /// <summary>Whether anyone can currently see the Companion hero: window focused, not
        /// minimised, tab on screen.</summary>
        private bool CompanionFxOnScreen =>
            IsActive && WindowState != WindowState.Minimized && CompanionTab?.IsVisible == true;

        // ============================== lifecycle ==============================

        /// <summary>Called from CompanionTabView's IsVisibleChanged - the tab's own file, so
        /// ShowTab keeps working exactly as it did and still drives the FX.</summary>
        internal void OnCompanionTabVisibilityChanged(bool visible)
        {
            try
            {
                if (visible && !IsIncomingTab("companion")) return;  // the outgoing tab's fade-out re-show
                InitializeCompanionFx();
                ApplyCompanionHeroBreath();
                if (!visible) DetachCompanionSheen();
            }
            catch (Exception ex) { App.Logger?.Debug("OnCompanionTabVisibilityChanged: {E}", ex.Message); }
        }

        private void InitializeCompanionFx()
        {
            if (_companionFxInitialized) return;
            _companionFxInitialized = true;
            try
            {
                Activated += OnCompanionFxWindowStateish;
                Deactivated += OnCompanionFxWindowStateish;
                StateChanged += OnCompanionFxWindowStateish;
                if (App.Mods != null) App.Mods.ModChanged += OnCompanionFxModChanged;
                WatchProviderHealthStatus();
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "InitializeCompanionFx failed"); }
        }

        private void OnCompanionFxWindowStateish(object? sender, EventArgs e)
        {
            try { ApplyCompanionHeroBreath(); }
            catch (Exception ex) { App.Logger?.Debug("OnCompanionFxWindowStateish: {E}", ex.Message); }
        }

        /// <summary>A mod switch re-tints the sheen by dropping it - the adorner samples FxTheme
        /// once, at construction, so nothing has to read a colour per frame.</summary>
        private void OnCompanionFxModChanged(object? sender, ModPackage mod)
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(() => OnCompanionFxModChanged(sender, mod)));
                    return;
                }
                DetachCompanionSheen();
            }
            catch (Exception ex) { App.Logger?.Debug("OnCompanionFxModChanged: {E}", ex.Message); }
        }

        // ============================== hero breath ==============================

        /// <summary>
        /// Starts, parks or drops the hero disc's breath against the current gate. One clock for
        /// both scale axes: they are the same animation, and a paused tab is one Pause() rather
        /// than two teardowns.
        /// </summary>
        private void ApplyCompanionHeroBreath()
        {
            try
            {
                var ring = CompanionTab?.CompanionHeroAvatarRing;
                if (ring == null) return;

                // Off / Reduced / the Performance tier: no clock at all, and the disc goes back to
                // its shipped size rather than freezing part-way through a breath.
                if (!MotionFx.AllowAmbientLoops) { StopCompanionHeroBreath(); return; }

                if (!CompanionFxOnScreen)
                {
                    // Nobody can see it - park rather than tear down, so coming back picks the
                    // breath up where it left off.
                    if (_companionHeroClock?.Controller != null) _companionHeroClock.Controller.Pause();
                    else StopCompanionHeroBreath();
                    return;
                }

                if (_companionHeroClock != null)
                {
                    _companionHeroClock.Controller?.Resume();
                    return;
                }

                if (_companionHeroScale == null)
                {
                    // Never clobber an authored transform - if the ring ever grows one, we simply
                    // decline to breathe it rather than silently breaking its layout.
                    var current = ring.RenderTransform;
                    if (current is ScaleTransform existing) _companionHeroScale = existing;
                    else if (current == null || current == Transform.Identity || current.Value.IsIdentity)
                    {
                        _companionHeroScale = new ScaleTransform(1, 1);
                        ring.RenderTransformOrigin = new Point(0.5, 0.5);
                        ring.RenderTransform = _companionHeroScale;
                    }
                    else return;
                }
                if (_companionHeroScale.IsFrozen) return;

                var anim = new DoubleAnimation(1.0, CompanionHeroBreathScale,
                                               TimeSpan.FromSeconds(CompanionHeroBreathSeconds))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(anim, AmbientFrameRate);
                anim.Freeze();

                var clock = anim.CreateClock();
                _companionHeroScale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, clock);
                _companionHeroScale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, clock);
                _companionHeroClock = clock;
            }
            catch (Exception ex) { App.Logger?.Debug("ApplyCompanionHeroBreath: {E}", ex.Message); }
        }

        private void StopCompanionHeroBreath()
        {
            if (_companionHeroClock == null) return;
            try
            {
                var scale = _companionHeroScale;
                _companionHeroClock = null;
                if (scale == null || scale.IsFrozen) return;
                scale.ApplyAnimationClock(ScaleTransform.ScaleXProperty, null);
                scale.ApplyAnimationClock(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = scale.ScaleY = 1.0;
            }
            catch (Exception ex) { App.Logger?.Debug("StopCompanionHeroBreath: {E}", ex.Message); }
        }

        // ============================== provider-connect sheen ==============================

        /// <summary>
        /// Watches the two provider health-status labels for a connection that came back healthy.
        ///
        /// The test buttons themselves are async and live in another partial, so the success
        /// moment is read where it lands: the status label. A TextBlock has no TextChanged event,
        /// so this uses the DP descriptor - both labels and this window live for the whole session,
        /// so the strong reference it takes costs nothing and is never orphaned.
        /// </summary>
        private void WatchProviderHealthStatus()
        {
            try
            {
                Watch(CompanionTab?.TxtAiHealthStatus);
                Watch(CompanionTab?.TxtOpenAiHealthStatus);
            }
            catch (Exception ex) { App.Logger?.Debug("WatchProviderHealthStatus: {E}", ex.Message); }

            void Watch(TextBlock? label)
            {
                if (label == null) return;
                var descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
                descriptor?.AddValueChanged(label, OnProviderHealthStatusChanged);
            }
        }

        private void OnProviderHealthStatusChanged(object? sender, EventArgs e)
        {
            try
            {
                if (sender is not TextBlock label) return;
                var connected = Loc.Get("label_status_connected");
                if (string.IsNullOrEmpty(connected)) return;
                // Both call sites write "<connected> · <n>ms"; a failure writes the failed string.
                if (label.Text?.StartsWith(connected, StringComparison.Ordinal) != true) return;
                PlayCompanionConnectSheen();
            }
            catch (Exception ex) { App.Logger?.Debug("OnProviderHealthStatusChanged: {E}", ex.Message); }
        }

        /// <summary>
        /// One sheen pass across the AI Brain card, then full teardown. Gated on the interaction
        /// clock rather than the ambient one: this is an event moment, so it survives Reduced and
        /// only disappears at Off.
        /// </summary>
        private void PlayCompanionConnectSheen()
        {
            try
            {
                DetachCompanionSheen();
                if (!MotionFx.AllowTransitions) return;

                var card = CompanionTab?.CompanionAiBrainCard;
                if (card == null || !card.IsVisible) return;

                var layer = AdornerLayer.GetAdornerLayer(card);
                if (layer == null) return;

                var sheen = new CardSheenAdorner(card, CompanionAiCardCornerRadius);
                layer.Add(sheen);
                sheen.Start();
                _companionSheen = sheen;
                _companionSheenLayer = layer;

                _companionSheenTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(CompanionSheenHoldSeconds),
                };
                _companionSheenTimer.Tick += (_, _) => DetachCompanionSheen();
                _companionSheenTimer.Start();
            }
            catch (Exception ex) { App.Logger?.Debug("PlayCompanionConnectSheen: {E}", ex.Message); }
        }

        private void DetachCompanionSheen()
        {
            try
            {
                _companionSheenTimer?.Stop();
                _companionSheenTimer = null;

                var sheen = _companionSheen;
                var layer = _companionSheenLayer;
                _companionSheen = null;
                _companionSheenLayer = null;
                if (sheen == null) return;
                sheen.Stop();
                layer?.Remove(sheen);
            }
            catch (Exception ex) { App.Logger?.Debug("DetachCompanionSheen: {E}", ex.Message); }
        }
    }
}
