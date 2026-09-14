using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Banner;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel
{
    /// <summary>
    /// The Barnum interludes: every so often the release ticker stops scrolling, fades out, and one
    /// line from the pool's "reads" bucket takes the banner for five seconds as a small act.
    ///
    /// <para>The ticker is PAUSED, never restarted. StartMarqueeAnimation rebuilds the repeated
    /// string and begins the loop from zero, so re-entering it to come back would jump the ticker
    /// visibly; the storyboard is controllable instead and the interlude pauses and resumes it.</para>
    ///
    /// <para>Loops are counted off a DispatcherTimer keyed to the loop's own duration rather than
    /// off the storyboard: RepeatBehavior.Forever never raises Completed, and a CurrentTimeInvalidated
    /// handler would run at frame rate forever to serve one tick every forty seconds.</para>
    ///
    /// <para>Gates, all of which must hold: <c>BannerPoolEnabled</c>, <c>MotionFx.AllowTransitions</c>,
    /// and the banner actually on screen. With ambient loops off the ticker is parked static and the
    /// interlude still plays, but as a plain crossfade - no glitch, no flicker. Photosafe
    /// (<c>LockdownPhotosafe</c>, the same switch GlyphRotEffect reads) drops every flicker and
    /// jitter frame on top of that.</para>
    /// </summary>
    public partial class MainWindow
    {
        #region Marquee Interludes

        /// <summary>Ticker fade out / fade back. Part of the act's five to seven seconds.</summary>
        private const double ReadFadeMs = 280;

        /// <summary>
        /// The loop interval used when the ticker is parked static (ambient loops off). There is no
        /// scroll to count, so the beat falls back to a plain clock; the cadence's three minute
        /// floor is what actually spaces the interludes out either way.
        /// </summary>
        private const double ParkedLoopSeconds = 45;

        private MarqueeReadCadence? _readCadence;
        private DispatcherTimer? _readLoopTimer;
        private CancellationTokenSource? _readCts;
        private string? _lastReadEffectId;
        private bool _readPlaying;
        private bool _readHooked;
        private readonly Random _readRng = new();

        /// <summary>
        /// Arms the interlude beat. Called once from InitializeMarqueeBanner; the loop clock itself
        /// is (re)armed by every StartMarqueeAnimation, which is the only thing that knows how long
        /// one loop of the current message takes.
        /// </summary>
        private void InitializeMarqueeReads()
        {
            try
            {
                bool fast = App.Settings?.Current?.MarqueeReadDebugFast == true
                            || string.Equals(Environment.GetEnvironmentVariable("CCP_MARQUEE_READS_FAST"),
                                             "1", StringComparison.Ordinal);

                _readCadence = new MarqueeReadCadence { Fast = fast };
                if (fast) App.Logger?.Information("[MarqueeReads] fast cadence armed (desk mode)");

                if (!_readHooked)
                {
                    _readHooked = true;
                    // Tab switches and window teardown both show up here: an interlude that keeps
                    // animating into a collapsed tab is a leaked clock and a wasted act.
                    SettingsTab.MarqueeRead.IsVisibleChanged += MarqueeRead_IsVisibleChanged;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Marquee interludes failed to arm: {Error}", ex.Message);
            }
        }

        private void MarqueeRead_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is bool visible && visible)
            {
                _readLoopTimer?.Start();
                return;
            }
            CancelMarqueeInterlude();
            _readLoopTimer?.Stop();
        }

        /// <summary>
        /// (Re)arm the loop clock for the message that is now on the ticker.
        /// <paramref name="loopSeconds"/> is the scroll's own period, so one tick really is one
        /// completed loop. Zero or less means the ticker is parked and the beat falls back to
        /// <see cref="ParkedLoopSeconds"/>.
        /// </summary>
        private void ArmMarqueeReadClock(double loopSeconds)
        {
            try
            {
                if (_readCadence == null) return;

                double seconds = loopSeconds > 0.5 ? loopSeconds : ParkedLoopSeconds;
                if (_readLoopTimer == null)
                {
                    _readLoopTimer = new DispatcherTimer(DispatcherPriority.Background);
                    _readLoopTimer.Tick += MarqueeReadLoop_Tick;
                }
                _readLoopTimer.Interval = TimeSpan.FromSeconds(Math.Min(600, seconds));
                _readLoopTimer.Start();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[MarqueeReads] clock not armed: {E}", ex.Message);
            }
        }

        private void MarqueeReadLoop_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (_readPlaying || _readCadence == null) return;
                if (!_readCadence.NoteLoop()) return;
                if (!InterludesAllowed())
                {
                    _readCadence.NoteSkipped();
                    return;
                }
                _ = RunMarqueeInterludeAsync();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[MarqueeReads] loop tick: {E}", ex.Message);
            }
        }

        /// <summary>The standing gates. Cheap enough to re-ask on every candidate loop.</summary>
        private bool InterludesAllowed()
        {
            try
            {
                if (Application.Current?.Dispatcher?.HasShutdownStarted == true) return false;
                if (App.Settings?.Current?.BannerPoolEnabled != true) return false;
                if (!MotionFx.AllowTransitions) return false;
                if (!SettingsTab.MarqueeRead.IsVisible) return false;
                return true;
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "interlude gate");
                return false;
            }
        }

        /// <summary>
        /// Take the stage, play one act, give the ticker back. Every exit path goes through the
        /// restore in the finally: a cancelled interlude that left the ticker at zero opacity is a
        /// banner that is simply blank until the next message arrives.
        /// </summary>
        private async Task RunMarqueeInterludeAsync()
        {
            var settings = App.Settings?.Current;
            if (settings == null || _readCadence == null) return;

            var line = _bannerPool.NextLine(BannerPoolService.ReadsBucket);
            if (string.IsNullOrWhiteSpace(line))
            {
                // Nothing eligible (fresh profile, every read gated out). Skip in silence and let
                // the next loop try again rather than burning the whole four-loop wait.
                _readCadence.NoteSkipped();
                return;
            }

            var beats = MarqueeReadSplit.Split(line);
            var acts = MarqueeReadEffects.Build();
            var byId = acts.ToDictionary(a => a.Id, StringComparer.Ordinal);

            var chosen = MarqueeReadEffects.PickId(
                acts.Select(a => a.Id).ToList(),
                _lastReadEffectId,
                id => byId[id].CanPlay(beats.Body, beats.Tag),
                _readRng.Next);
            if (chosen == null || !byId.TryGetValue(chosen, out var act))
            {
                _readCadence.NoteSkipped();
                return;
            }

            _readPlaying = true;
            _readCadence.NotePlayed();
            _lastReadEffectId = chosen;
            _readLoopTimer?.Stop();

            var cts = new CancellationTokenSource();
            _readCts = cts;
            var ct = cts.Token;

            var host = SettingsTab.MarqueeRead;
            var stage = new MarqueeReadStage(
                host,
                SettingsTab.MarqueeReadTransform,
                photosafe: settings.LockdownPhotosafe,
                allowMotion: MotionFx.AllowTransitions,
                allowAmbient: MotionFx.AllowAmbientLoops);

            try
            {
                App.Logger?.Debug("[MarqueeReads] act {Act} (tag={HasTag})", chosen, beats.HasTag);

                stage.Clear();
                host.Opacity = 0;

                _marqueeStoryboard?.Pause(SettingsTab);
                if (!await PossAnim.ToAsync(SettingsTab.MarqueeCanvas, UIElement.OpacityProperty,
                                            0, ReadFadeMs, ct).ConfigureAwait(true)) return;

                host.Opacity = 1;
                await act.PlayAsync(stage, beats.Body, beats.Tag, ct).ConfigureAwait(true);

                await PossAnim.ToAsync(host, UIElement.OpacityProperty, 0, ReadFadeMs, ct)
                              .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("Marquee interlude failed: {Error}", ex.Message);
            }
            finally
            {
                RestoreTicker(stage);
                _readPlaying = false;
                if (ReferenceEquals(_readCts, cts)) _readCts = null;
                cts.Dispose();
                if (SettingsTab.MarqueeRead.IsVisible) _readLoopTimer?.Start();
            }
        }

        /// <summary>
        /// Hand the banner back. Synchronous and unconditional: this runs on the cancel path too,
        /// where there is no time left to animate anything.
        /// </summary>
        private void RestoreTicker(MarqueeReadStage? stage)
        {
            try
            {
                stage?.Clear();

                var host = SettingsTab.MarqueeRead;
                host.BeginAnimation(UIElement.OpacityProperty, null);
                host.Opacity = 0;

                var canvas = SettingsTab.MarqueeCanvas;
                canvas.BeginAnimation(UIElement.OpacityProperty, null);
                canvas.Opacity = 1;

                _marqueeStoryboard?.Resume(SettingsTab);

                // One sheen pass as the ticker comes back, so the return reads as deliberate.
                SweepBannerSheen(force: true);
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "interlude restore");
            }
        }

        /// <summary>
        /// Stop whatever is on stage right now and put the ticker back. Called from
        /// StartMarqueeAnimation (a server message refresh or a canvas resize rebuilds the string
        /// underneath us), from the visibility hook, and from OnClosing.
        /// </summary>
        internal void CancelMarqueeInterlude()
        {
            try
            {
                var cts = _readCts;
                if (cts != null)
                {
                    _readCts = null;
                    try { cts.Cancel(); }
                    catch (ObjectDisposedException) { /* swallow: the act already finished */ }
                }
                if (_readPlaying)
                {
                    _readPlaying = false;
                    RestoreTicker(null);
                }
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "interlude cancel");
            }
        }

        /// <summary>Teardown from OnClosing: no clocks and no tasks left pointing at this window.</summary>
        private void ShutdownMarqueeReads()
        {
            try
            {
                _readLoopTimer?.Stop();
                _readLoopTimer = null;
                CancelMarqueeInterlude();
                if (_readHooked)
                {
                    _readHooked = false;
                    SettingsTab.MarqueeRead.IsVisibleChanged -= MarqueeRead_IsVisibleChanged;
                }
            }
            catch (Exception ex)
            {
                Diag.Swallowed(ex, "interlude shutdown");
            }
        }

        #endregion
    }
}
