using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel
{
    /// <summary>
    /// THE FUSE's candle (CONTRACT-FUSE-0816 §2.2) — from twelve hours out, a small flame stands
    /// beside the tube and keeps the companion company until the ceremony.
    ///
    /// <para><b>THREAD TRAP, and it is the reason this is its own file.</b> The tube can live on its
    /// OWN STA thread (<c>AppSettings.AvatarOwnThread</c>), so its Dispatcher is not necessarily the
    /// main one. The countdown service raises its events on the MAIN dispatcher, which means every
    /// handler here has to hop through <see cref="RunOnAvatar"/> before it touches a single visual —
    /// a direct write would be a cross-thread InvalidOperationException on exactly the machines that
    /// have the flag on. Nothing else in the AvatarTube folder subscribes to a main-thread service,
    /// so there was no existing example to copy; this is the pattern for the next one.</para>
    ///
    /// <para><b>Dormant with the service.</b> No cached ceremony timestamp ⇒ no events ⇒ one
    /// collapsed Grid and no clock. The subscription itself is the only cost.</para>
    ///
    /// <para><b>Reduced motion gets a snapped state.</b> With ambient loops off the candle is a
    /// STATIC flame — lit, at rest, present. It is never simply missing, and it never falls back to
    /// a slower flicker (contract §0, faucet PR #37's lesson).</para>
    /// </summary>
    public partial class AvatarTubeWindow
    {
        private bool _fuseCandleWired;
        private bool _fuseCandleFlickering;

        /// <summary>
        /// Subscribe the candle. Called once from the constructor; unsubscribes itself on Closed so
        /// a detach/reattach cycle cannot leave the service holding a dead window.
        /// </summary>
        private void InitializeDescentFuseCandle()
        {
            try
            {
                var fuse = App.DescentCountdown;
                if (fuse is null || _fuseCandleWired) return;

                _fuseCandleWired = true;
                fuse.PhaseChanged += OnFuseCandlePhaseChanged;
                Closed += (_, _) =>
                {
                    try { fuse.PhaseChanged -= OnFuseCandlePhaseChanged; }
                    catch { /* teardown races are not worth a log line */ }
                };

                ApplyFuseCandlePhase(fuse.LastAnnouncedPhase);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Fuse] Candle could not be wired: {E}", ex.Message);
            }
        }

        private void OnFuseCandlePhaseChanged(object? sender, DescentFusePhaseChangedEventArgs e)
        {
            // Onto the tube's OWN dispatcher — see the thread trap at the top of this file.
            // RunOnAvatar already carries the CLAUDE.md rule-6/8 shutdown guard.
            RunOnAvatar(() =>
            {
                try { ApplyFuseCandlePhase(e.Current); }
                catch (Exception ex) { App.Logger?.Debug("[Fuse] Candle repaint failed: {E}", ex.Message); }
            });
        }

        /// <summary>
        /// The candle's whole state for a phase. Recomputed from scratch, so any arrival (including
        /// the kill switch's jump straight to Dark) lands somewhere correct.
        /// </summary>
        private void ApplyFuseCandlePhase(DescentFusePhase phase)
        {
            var host = FuseCandleHost;
            if (host is null) return;

            var show = phase >= DescentFusePhase.Candle && phase < DescentFusePhase.Zero;
            host.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            if (show) StartFuseCandleFlicker();
            else StopFuseCandleFlicker();
        }

        /// <summary>
        /// The flicker: a slow opacity breath on the halo and a small vertical stretch on the flame,
        /// on two deliberately mismatched periods so the pair never lines up into a pulse. Capped
        /// frame rate, like every other ambient loop in the app.
        /// </summary>
        private void StartFuseCandleFlicker()
        {
            if (_fuseCandleFlickering) return;

            // THE SNAPPED STATE. Ambient loops off (reduced motion, or a performance tier that
            // refuses ambient work) means a still flame at rest — not a missing one, and not a
            // slower flicker.
            if (!MotionFx.AllowAmbientLoops)
            {
                if (FuseCandleHalo != null)
                {
                    FuseCandleHalo.BeginAnimation(UIElement.OpacityProperty, null);
                    FuseCandleHalo.Opacity = 0.5;
                }
                if (FuseCandleFlameScale != null)
                {
                    FuseCandleFlameScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    FuseCandleFlameScale.ScaleX = FuseCandleFlameScale.ScaleY = 1.0;
                }
                return;
            }

            _fuseCandleFlickering = true;

            if (FuseCandleHalo != null)
            {
                var glow = new DoubleAnimation(0.34, 0.62, TimeSpan.FromSeconds(1.7))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(glow, 24);
                FuseCandleHalo.BeginAnimation(UIElement.OpacityProperty, glow);
            }

            if (FuseCandleFlameScale != null)
            {
                // 2.3s against the halo's 1.7s: co-prime enough that the two never sync into a
                // heartbeat, which is what makes it read as a flame instead of a throb.
                var stretch = new DoubleAnimation(0.92, 1.08, TimeSpan.FromSeconds(2.3))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                Timeline.SetDesiredFrameRate(stretch, 24);
                FuseCandleFlameScale.BeginAnimation(ScaleTransform.ScaleYProperty, stretch);
            }
        }

        /// <summary>Put the clocks down and leave the candle at rest. The kill switch's path.</summary>
        private void StopFuseCandleFlicker()
        {
            if (!_fuseCandleFlickering) return;
            _fuseCandleFlickering = false;

            try
            {
                if (FuseCandleHalo != null)
                {
                    FuseCandleHalo.BeginAnimation(UIElement.OpacityProperty, null);
                    FuseCandleHalo.Opacity = 0.5;
                }
                if (FuseCandleFlameScale != null)
                {
                    FuseCandleFlameScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    FuseCandleFlameScale.ScaleX = FuseCandleFlameScale.ScaleY = 1.0;
                }
            }
            catch (Exception ex) { App.Logger?.Debug("[Fuse] Candle teardown failed: {E}", ex.Message); }
        }
    }
}
