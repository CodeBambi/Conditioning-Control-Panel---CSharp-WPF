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
    /// <para>not ported: <c>spiral</c> and <c>rain</c> effects (no timed spiral and no gif rain on
    /// this head yet); asks that need them are
    /// dropped at draw time, exactly as an infeasible effect is in WPF.</para>
    /// </summary>
    internal sealed partial class EmiDeskService
    {
        private static MainShellWindow? Shell =>
            (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainShellWindow;

        /// <summary>Test seam: the tour starter (default Core tutorial seam, null while no tour service is seeded).</summary>
        internal static Func<Action<string>?> TourStarter { get; set; } =
            () => CoreTutorial.StartAction == null ? null : CoreTutorial.Start;

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
