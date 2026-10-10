using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    /// <summary>
    /// The pieces of WPF <c>DtrhHostService</c> that sit between the web descent and the rest of
    /// the app (lane c2): bark routing (:749), the per-run native metrics folded into
    /// <see cref="DtrhSessionStatsStore"/> (:707), the boot-error door to the classic hub (:943),
    /// and the main window tucked away while the descent owns the screen (:181, :1112).
    ///
    /// <para><c>DtrhHapticDirector</c> is in Core (Services/Haptics) and tapped from
    /// GameWindow.Dtrh.Host.cs. not ported: EMI Desk's dtrhOpened / dtrhClosed moments (the EMI
    /// Desk lane).</para>
    /// </summary>
    internal sealed partial class GameWindow
    {
        // ---- per-run native metrics (WPF DtrhHostService.cs:53-62): local only, never sent ----
        private int _runVideosShown, _runSubliminalsHeard;

        /// <summary>WPF BootFailedThisSession (:90): the page reported boot-error this app session.</summary>
        internal static bool DtrhBootFailedThisSession { get; private set; }

        /// <summary>Seam: what the boot-error path shows the player (tests swap it). The native Chaos run is
        /// retired on this head (owner, 2026-10-10): there is no classic door to fall back to.</summary>
        internal static Action<string, string?> DtrhBootErrorNotice = ShowDtrhBootErrorNotice;

        /// <summary>Seams: tuck / restore the main window (tests swap them). True = it was tucked.</summary>
        internal static Func<bool> DtrhTuckShell = TuckShell;
        internal static Action DtrhRestoreShell = RestoreShell;
        private bool _dtrhTuckedShell;

        private void ResetDtrhRunMetrics() { _runVideosShown = 0; _runSubliminalsHeard = 0; }

        /// <summary>WPF FirePayload (:586): a native whisper during a run is a subliminal heard.</summary>
        internal void NoteDtrhPayloadFired(string? kind)
        {
            if (_runActive && kind == "audio") _runSubliminalsHeard++;
        }

        private void NoteDtrhVideoShown() { if (_runActive) _runVideosShown++; }

        /// <summary>WPF "bark" (:339): never over her VN line; the trigger plays a recorded line or nothing.</summary>
        internal void RouteDtrhBark(JObject o)
        {
            if (_vnSpeaking || _testMode) return;
            try
            {
                var bark = DtrhBarkRouter.Route(o);
                if (bark == null)
                {
                    if ((string?)o["event"] != "effect-fired") Log.Debug("DtrhHost: unrouted bark event '{E}'", (string?)o["event"]);
                    return;
                }
                CoreBark.Raise(bark.Trigger, bark.Values);
            }
            catch (Exception ex) { Log.Debug("DtrhHost.RouteBark: {E}", ex.Message); }
        }

        private void DtrhBarkRunStarted(string difficulty)
        {
            if (_testMode) return;
            try { var b = DtrhBarkRouter.RunStarted(difficulty); CoreBark.Raise(b.Trigger, b.Values); }
            catch (Exception ex) { Log.Debug("DtrhHost bark run-started: {E}", ex.Message); }
        }

        private void DtrhBarkRunCompleted(int xp, string difficulty)
        {
            try { var b = DtrhBarkRouter.RunCompleted(xp, difficulty); CoreBark.Raise(b.Trigger, b.Values); }
            catch (Exception ex) { Log.Debug("DtrhHost bark run-completed: {E}", ex.Message); }
        }

        /// <summary>WPF OnRunEnded (:707): fold the host-measured natives and the payout into the
        /// page's sessionStats and sum it into the lifetime store. Best effort.</summary>
        private void RecordDtrhSessionStats(JObject o, string difficulty, int sparksEarned, double finalXp)
        {
            try
            {
                var js = o["sessionStats"] as JObject ?? new JObject();
                // not ported: watch seconds, skipped videos and voice-line seconds (the scheduler and the
                // bark mouth do not report them on this head yet); they stay 0, as on a run with none.
                js["videoWatchSec"] = 0.0;
                js["videosShown"] = _runVideosShown;
                js["videosSkipped"] = 0;
                js["voicelinesHeard"] = 0;
                js["voiceoverSec"] = 0.0;
                js["subliminalsHeard"] = _runSubliminalsHeard;
                js["sparksEarned"] = sparksEarned;
                js["xpEarned"] = finalXp;
                var life = DtrhSessionStatsStore.Record(js, difficulty);
                Log.Information("DtrhHost: session metrics recorded (run #{Runs}): {Bubbles} bubbles, {Boons} boons",
                    life.Runs, life.BubblesPopped, life.BoonsReceived);
            }
            catch (Exception ex) { Log.Debug("DtrhHost session-metrics record: {E}", ex.Message); }
        }

        /// <summary>WPF OnBootError (:943): the page could not start its renderer. Remember it, close, and
        /// (outside test mode) say so. WPF falls back to its native run here; this head has none.</summary>
        private void OnDtrhBootError(string? msg)
        {
            Log.Warning("DtrhHost: page boot-error: {Msg}", msg);
            DtrhBootFailedThisSession = true;
            bool wasTest = _testMode;
            var title = Title ?? string.Empty;
            Close();
            if (wasTest) return;
            Dispatcher.UIThread.Post(() =>
            {
                try { DtrhBootErrorNotice(title, msg); }
                catch (Exception ex) { Log.Error(ex, "DtrhHost: boot-error notice failed"); }
            });
        }

        /// <summary>A black window the player has to guess about is the worse failure: the same card the
        /// Arcademy shows when its page cannot start.</summary>
        private static void ShowDtrhBootErrorNotice(string title, string? msg)
        {
            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            if (owner == null || !owner.IsVisible) return;
            _ = Dialogs.MessageDialog.ShowAsync(owner, title,
                ConditioningControlPanel.Localization.Loc.GetF("arcademy_boot_error_body", title, msg ?? string.Empty));
        }

        /// <summary>WPF Launch (:181): the main window steps aside while the descent owns the screen.</summary>
        private void TuckShellForDtrh()
        {
            try { _dtrhTuckedShell = DtrhTuckShell(); }
            catch (Exception ex) { Log.Debug("DtrhHost: minimize main window failed: {E}", ex.Message); }
        }

        /// <summary>WPF DisposeAll (:1112): bring it back if we were the one who tucked it.</summary>
        private void RestoreShellAfterDtrh()
        {
            if (!_dtrhTuckedShell) return;
            _dtrhTuckedShell = false;
            try { DtrhRestoreShell(); }
            catch (Exception ex) { Log.Debug("DtrhHost: restore main window failed: {E}", ex.Message); }
        }

        private static MainShellWindow? Shell() =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainShellWindow;

        private static bool TuckShell()
        {
            var shell = Shell();
            if (shell is not { IsVisible: true }) return false;
            shell.Hide();
            return true;
        }

        private static void RestoreShell() => Shell()?.ShowFromTray();
    }
}
