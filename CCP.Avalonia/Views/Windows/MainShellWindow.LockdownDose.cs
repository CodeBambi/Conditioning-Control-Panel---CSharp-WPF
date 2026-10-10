// PORTED from ConditioningControlPanel/App.xaml.cs:2657-2659 (LockdownDoseKeeper.RecoverIfNeeded, new
// keeper, Install). The keeper itself is Core (Services/Haptics/LockdownDoseKeeper.cs); this is the
// head's half of it: the live shell's wall toggles and engine, the takeover read and the bark.
//
// ponytail: StartEngine here has no systemInitiated flag (WPF uses it to skip the Relapse achievement,
// the session count and the video enhancement prompt on a keeper start). Possession's edge pulse has
// no director on this head, so a conscription is announced by the bark alone.

using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Haptics;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Startup, once, right after the head's LockdownService exists.</summary>
        internal static void InstallLockdownDose(LockdownService lockdown)
        {
            try
            {
                LockdownDoseKeeper.RecoverIfNeeded();
                LockdownDoseKeeper.TakeoverRunningProvider = () => Current?.Autonomy.IsEnabled == true;
                LockdownDoseKeeper.Current?.Dispose();
                var keeper = new LockdownDoseKeeper(lockdown, LockdownDoseHostFor(() => Current));
                keeper.Install();
                LockdownDoseKeeper.Current = keeper;
            }
            catch (Exception ex) { Log.Warning(ex, "Lockdown dose: install failed"); }
        }

        /// <summary>The keeper's host over a shell (the live one at run time, a test's own in tests).</summary>
        internal static LockdownDoseHost LockdownDoseHostFor(Func<MainShellWindow?> shell) => new()
        {
            IsEngineRunning = () => CoreEngine.IsRunning,
            StartEngine = () => shell()?.StartEngine(),
            StopEngine = StopEngine,
            SetWallFeature = (key, on) =>
            {
                if (shell() is not { } sh) throw new InvalidOperationException("no shell");   // kept in the recovery file
                sh.SetWallFeature(key, on);
                sh.RefreshWallActiveStates();
            },
            IsSessionFeatureLockActive = () => shell()?.IsSessionFeatureLockActive == true,
            Bark = (features, round, engineStarted) => CoreBark.Raise("LockdownConscript", new Dictionary<string, object>
            {
                ["features"] = string.IsNullOrWhiteSpace(features) ? "something" : features,
                ["round"] = (double)round,
                ["engine"] = engineStarted ? 1.0 : 0.0,
            }),
            OnUi = a => { if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Post(a); },
            IsAlive = () => shell() != null,
        };
    }
}
