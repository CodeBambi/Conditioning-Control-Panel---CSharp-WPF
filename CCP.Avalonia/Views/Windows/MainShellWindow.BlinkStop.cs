// PORTED from ConditioningControlPanel/MainWindow/MainWindow.LabTab.cs (WPF 7.1.5):
// WireRapidBlinkRecalibrateShortcut / TriggerRapidBlinkRecalibrateAsync / StopAllForRecalibration (:156-283),
// gated by Services/Safety/BlinkStopGate (hard rule 6: never more permissive than the panic key).
// Ledger row platform#9 (6-blink stop gesture).
//
// The leash step (WPF LeashOnPanicPress) is the 'leash-task' panic surface, prepended by Platform/LeashTaskHost.
// Deviation, on purpose: the stop pass is the panic registry minus the surfaces WPF's recal stop never
// touched (game windows, the intake, a Chaos descent) and minus the camera, which recalibration needs live.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // The detector enforces a 500 ms cooldown between blinks, so 6 blinks span >= 2.5 s;
        // 3.5 s is the achievable window and far above the natural rate (~1 per 3-4 s).
        internal const int RapidBlinkRecalCount = 6;
        internal const int RapidBlinkRecalWindowMs = 3500;
        private readonly Queue<DateTime> _rapidBlinkTimes = new();
        private bool _rapidBlinkRecalInProgress;

        /// <summary>Panic surfaces the blink stop leaves alone: WPF StopAllForRecalibration never closed
        /// game windows (a Chaos descent is one now) or the intake, and the camera must stay up to recalibrate.</summary>
        internal static readonly HashSet<string> BlinkStopSkips = new() { "intake", "games", "camera" };

        /// <summary>Test seam: what a fired gesture does after the stop pass (the recalibrate offer).</summary>
        internal Func<Task>? BlinkRecalOfferForTests;

        private void InitializeRapidBlinkStop()
        {
            var tracker = WebcamTracker.Instance;
            Action onBlink = () => Dispatcher.UIThread.Post(() => OnRapidBlink(DateTime.UtcNow));
            Opened += (_, _) => tracker.OnBlink += onBlink;
            Closed += (_, _) => tracker.OnBlink -= onBlink;
        }

        /// <summary>The live gate inputs, WPF LabTab.cs:187.</summary>
        internal static BlinkStopGate.Block BlinkStopBlock()
        {
            var s = CoreSettings.Current;
            return BlinkStopGate.Check(
                blinkTrainerRunning: Overlays.BlinkTrainerSession.IsRunning,
                lockdownActive: LockdownActive,
                panicKeyEnabled: s.PanicKeyEnabled,
                strictLockEnabled: s.StrictLockEnabled);
        }

        /// <summary>One blink (UI thread). Returns true when this blink completed a run and fired the stop.</summary>
        internal bool OnRapidBlink(DateTime nowUtc)
        {
            // Opt-in via the toggle on every webcam card; never while a calibration is open (its verify
            // step asks for blinks) or mid-trigger.
            if (!CoreSettings.Current.BlinkRecalibrateShortcutEnabled) return false;
            if (_rapidBlinkRecalInProgress || WebcamCalibrationWindow.IsShowing) return false;

            var block = BlinkStopBlock();
            if (block != BlinkStopGate.Block.None)
            {
                if (_rapidBlinkTimes.Count > 0) Log.Debug("Rapid-blink stop ignored: {Reason}", block);
                _rapidBlinkTimes.Clear();
                return false;
            }

            _rapidBlinkTimes.Enqueue(nowUtc);
            var cutoff = nowUtc.AddMilliseconds(-RapidBlinkRecalWindowMs);
            while (_rapidBlinkTimes.Count > 0 && _rapidBlinkTimes.Peek() < cutoff) _rapidBlinkTimes.Dequeue();
            if (_rapidBlinkTimes.Count < RapidBlinkRecalCount) return false;

            _rapidBlinkTimes.Clear();
            _ = TriggerRapidBlinkRecalibrateAsync();
            return true;
        }

        private async Task TriggerRapidBlinkRecalibrateAsync()
        {
            if (_rapidBlinkRecalInProgress) return;
            _rapidBlinkRecalInProgress = true;
            try
            {
                Log.Information("Rapid 6-blink gesture: stopping all activity and offering recalibration.");
                StopAllForRecalibration(this);
                if (BlinkRecalOfferForTests != null) { await BlinkRecalOfferForTests(); return; }

                // The capture loop may have stopped between the triggering blink and here.
                var tracker = WebcamTracker.Instance;
                if (!tracker.IsRunning) await tracker.StartAsync();
                if (!tracker.IsRunning)
                {
                    Log.Warning("Rapid-blink recal: webcam not running and could not be (re)started; aborting.");
                    return;
                }

                if (!await Dialogs.MessageDialog.ConfirmAsync(this,
                        ConditioningControlPanel.Localization.Loc.Get("blink_stop_recal_title"),
                        ConditioningControlPanel.Localization.Loc.Get("blink_stop_recal_body"),
                        okText: ConditioningControlPanel.Localization.Loc.Get("btn_yes"),
                        cancelText: ConditioningControlPanel.Localization.Loc.Get("btn_no")))
                    return;
                await WebcamCalibrationWindow.ShowDialogWithRecalibrate(this);
            }
            catch (Exception ex) { Log.Warning(ex, "Rapid-blink recalibration failed"); }
            finally
            {
                _rapidBlinkRecalInProgress = false;
                _rapidBlinkTimes.Clear();
            }
        }

        /// <summary>WPF StopAllForRecalibration: the panic stop pass minus <see cref="BlinkStopSkips"/>.
        /// One failing stop never skips the rest.</summary>
        internal static void StopAllForRecalibration(MainShellWindow? shell)
        {
            PanicSurfaces.ArmSafetyHold();   // a panic press by another name: Circe's hold arms too
            PanicSurfaces.SwitchOffKeywordTriggers();   // owner 2026-10-10: and keyword triggers go off until re-enabled
            foreach (var s in PanicSurfaces.All.Where(x => !BlinkStopSkips.Contains(x.Id)))
            {
                try { s.Stop(shell); }
                catch (Exception ex) { Log.Warning(ex, "Blink stop: {Id} failed", s.Id); }
            }
        }
    }
}
