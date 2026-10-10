// PORTED from ConditioningControlPanel/App.xaml.cs:2657-2659 (LockdownDoseKeeper.RecoverIfNeeded, new
// keeper, Install). The keeper itself is Core (Services/Haptics/LockdownDoseKeeper.cs); this is the
// head's half of it: the live shell's wall toggles and engine, the takeover read and the bark.
//
// A keeper start is StartEngine(systemInitiated: true); a conscription pulses the window edge through
// the Possession director when it is haunting, and is announced by the bark alone otherwise.
//
// Owner, 2026-10-10: the dose keeper ships as on WPF, default on. No behaviour change here; it is
// still owed one desk run before the port ships.

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
            StartEngine = () => shell()?.StartEngine(systemInitiated: true),   // WPF LockdownDoseKeeper.cs:399
            StopEngine = StopEngine,
            SetWallFeature = (key, on) =>
            {
                if (shell() is not { } sh) throw new InvalidOperationException("no shell");   // kept in the recovery file
                sh.SetWallFeature(key, on);
                sh.RefreshWallActiveStates();
            },
            IsSessionFeatureLockActive = () => shell()?.IsSessionFeatureLockActive == true,
            PulseEdges = strength => ConditioningControlPanel.Services.Possession.PossessionDirector.Current?.PulseEdges(strength),   // WPF LockdownDoseKeeper.cs:380
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
