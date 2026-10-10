using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk
{
    /// <summary>
    /// The head half of her offers: WPF <c>EmiDeskService.cs</c> IsTargetAvailable :1395,
    /// OpenTarget :1427, PinTop :1462, and the things <c>EmiOffers</c> reached for through
    /// <c>App</c>. The rules (feasibility, what each verb does, the moments it fires) are Core
    /// <see cref="EmiOffers"/>; this file only says what this head can do. An action left null is
    /// an effect the engine never offers, so there is no dead chip.
    ///
    /// <para><c>spiral</c> is a timed hold on the app's own spiral overlay (WPF ShowOverlayTimed) and
    /// <c>rain</c> is the gif cascade on its own constants (WPF EmiGifRain). Neither changes a setting:
    /// the hold shows the spiral whatever the user's switch says and lets go by itself. Panic takes both
    /// down: the stop pass releases every spiral hold, and PanicSurfaces.ArmSafetyHold closes her rain.</para>
    /// </summary>
    internal sealed partial class EmiDeskService
    {
        private static MainShellWindow? Shell =>
            (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainShellWindow;

        /// <summary>Test seam: the tour starter (default Core tutorial seam, null while no tour service is seeded).</summary>
        internal static Func<Action<string>?> TourStarter { get; set; } =
            () => CoreTutorial.StartAction == null ? null : CoreTutorial.Start;

        /// <summary>Owner name of her spiral hold and her rain (a teardown never takes somebody else's).</summary>
        internal const string EmiOwner = "emi";
        /// <summary>WPF GifCascadePayload: every dial of her rain except the duration.</summary>
        internal const double RainSpawnRate = 1.67, RainGifSize = 400, RainFallSpeed = 3.6, RainOpacity = 0.9, RainStartScale = 0.45;

        /// <summary>Test seams: where the effects draw from, and the two overlay calls.</summary>
        internal static Func<global::Avalonia.Visual?> EffectHost { get; set; } = () => Shell;
        internal static Action<global::Avalonia.Visual, Overlays.SpiralHold, int> SpiralShow { get; set; } =
            (host, hold, ms) => Overlays.SpiralOverlay.Hold(host, EmiOwner, hold, ms);
        internal static Action<global::Avalonia.Visual, double> RainShow { get; set; } =
            (host, seconds) => Overlays.GifCascadeOverlay.Show(host, EmiOwner, RainSpawnRate, seconds, RainGifSize, RainFallSpeed, RainOpacity, RainStartScale);
        internal static Func<bool> RainBusy { get; set; } = () => Overlays.GifCascadeOverlay.IsRaining;
        internal static Func<bool> HasLocalImages { get; set; } = EmiOffers.HasImages;

        /// <summary>WPF OverlayService.ShowOverlayTimed("spiral", ms, opacity): the spiral for a span, then
        /// back to the user's own switch. Fires <c>overlaySpiralUp</c> as WPF does (:886).</summary>
        internal void SpiralFor(int ms, double opacity)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => SpiralFor(ms, opacity)); return; }
            try
            {
                var host = EffectHost();
                if (host == null) { Log.Debug("[EmiDesk] spiral effect skipped: no window to draw from"); return; }
                int safeMs = Math.Clamp(ms, 500, 60000);
                Fire("overlaySpiralUp", new { channel = "spiral", n = safeMs / 1000 });
                SpiralShow(host, new Overlays.SpiralHold(Math.Clamp(opacity, 0.05, 1.0)), safeMs);
                _window?.RaiseAboveStage();   // WPF ReassertTopmost: she stays over her own effect
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] spiral effect failed"); }
        }

        /// <summary>WPF EmiGifRain.Start: no local images or a rain already up skips; 1..30 seconds.</summary>
        internal void RainFor(TimeSpan duration)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => RainFor(duration)); return; }
            try
            {
                var host = EffectHost();
                if (host == null) { Log.Debug("[EmiDesk] gif rain skipped: no window to draw from"); return; }
                if (!HasLocalImages()) { Log.Debug("[EmiDesk] gif rain skipped: no local images"); return; }
                if (RainBusy()) { Log.Debug("[EmiDesk] gif rain skipped: already raining"); return; }
                double seconds = duration.TotalSeconds;
                if (double.IsNaN(seconds) || seconds <= 0) seconds = 6.0;   // GifCascadePayload.DURATION_SEC
                seconds = Math.Max(1.0, Math.Min(30.0, seconds));
                RainShow(host, seconds);
                Log.Information("[EmiDesk] gif rain for {Seconds:0.#}s", seconds);
                _window?.RaiseAboveStage();
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] gif rain failed"); }
        }

        /// <summary>Her rain down now (panic, her dismiss). Never somebody else's cascade.</summary>
        internal static void StopRain()
        {
            try { Overlays.GifCascadeOverlay.CloseOwned(EmiOwner); }
            catch (Exception ex) { Log.Debug(ex, "[EmiDesk] gif rain stop failed"); }
        }

        private void SeedOffers()
        {
            EmiLineEngine.AskSituationProbe = AskSituationOk;
            EmiLineEngine.EffectFeasibleProbe = EmiOffers.EffectFeasible;
            EmiOffers.Head = BuildOfferSeams();
        }

        internal EmiOffers.HeadSeams BuildOfferSeams() => new()
        {
            TargetAvailable = IsTargetAvailable,
            OpenTarget = OpenTarget,
            PinTop = PinTop,
            MainWindowAlive = () => Shell != null,
            SessionRunning = () => CoreSession.IsSessionRunning,
            TutorialActive = () => CoreTutorial.IsActive,
            StartTour = name => TourStarter()?.Invoke(name),
            CanStartTour = () => TourStarter() != null,
            BookOpen = () => _window?.Book is { IsVisible: true },
            OpenBook = () => _window?.OpenBook(),
            Spiral = SpiralFor,
            Rain = RainFor,
            PlayVideo = path => Overlays.MandatoryVideoOverlay.Instance.Scheduler.Trigger(strictOverride: false, path: path),
            Burst = duration =>
            {
                if (Shell is { } host) Overlays.FlashOverlay.TriggerOnce(host, 4, duration, null);
            },
            CanShrink = () => _window is { } w && w.BodyWidth > EmiDeskWindow.MinBodyWidth + 1,
            Shrink = Shrink,
            OpenUrl = url => _ = Platform.AppUpdater.OpenUrl(Shell, url),
            OpenAssetsTab = () =>
            {
                var mw = Shell;
                if (mw == null) { Log.Debug("[EmiDesk] no main window, cannot open the assets tab"); return; }
                try { mw.ShowFromTray(); } catch (Exception ex) { Log.Debug(ex, "[EmiDesk] ShowFromTray failed"); }
                mw.ShowTab("assets");
            },
        };

        /// <summary>In the catalogue, shown, and not locked (WPF IsTargetAvailable).</summary>
        public bool IsTargetAvailable(string? targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId)) return false;
            try
            {
                var t = EmiTargets.Find(targetId);
                return t != null && t.Available && !t.Locked;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "[EmiDesk] IsTargetAvailable({Target}) failed", targetId);
                return false;
            }
        }

        /// <summary>Open a catalogue door. The door re-checks the tier itself on the way in.</summary>
        public void OpenTarget(string? targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId)) return;
            try
            {
                var t = EmiTargets.Find(targetId);
                if (t == null)
                {
                    Log.Information("[EmiDesk] OpenTarget({Target}) ignored: not in the catalogue", targetId);
                    return;
                }
                if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => OpenTarget(targetId)); return; }
                Log.Information("[EmiDesk] offer opened {Target}", targetId);
                t.Open();
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] OpenTarget({Target}) failed", targetId); }
        }

        /// <summary>Pin a door to the top of her ring (WPF PinTop).</summary>
        public void PinTop(string? targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId)) return;
            try
            {
                if (EmiTargets.Find(targetId) == null)
                {
                    Log.Information("[EmiDesk] PinTop({Target}) ignored: not in the catalogue", targetId);
                    return;
                }
                EmiSuggester.PinToTop(targetId);
                if (Dispatcher.UIThread.CheckAccess()) { if (_window?.RingOpen == true) _window.RebuildRing(); }
                else Dispatcher.UIThread.Post(() => { if (_window?.RingOpen == true) _window.RebuildRing(); });
                Fire("pinAdded", new { target = targetId });
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] PinTop({Target}) failed", targetId); }
        }

        /// <summary>Shrink her to the minimum and snap her to the nearest corner (WPF FireShrink).</summary>
        private void Shrink()
        {
            try
            {
                var win = _window;
                if (win == null) return;
                win.ApplyBodyWidth(EmiDeskWindow.MinBodyWidth);
                win.SnapToNearestCorner();
                win.SavePlacement();
                var s = CoreSettings.Current;
                if (s != null && Math.Abs(s.EmiDeskWidth - EmiDeskWindow.MinBodyWidth) > 0.5)
                {
                    s.EmiDeskWidth = EmiDeskWindow.MinBodyWidth;
                    CoreSettings.Save();
                }
                Fire("resized", new { n = (int)EmiDeskWindow.MinBodyWidth, bigger = false });
            }
            catch (Exception ex) { Log.Warning(ex, "[EmiDesk] shrink effect failed"); }
        }
    }
}
