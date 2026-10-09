using System;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>The WPF head's cut targets (LeashCutSafety moved to CCP.Core).</summary>
public static class LeashCutSafetyApp
{
    /// <summary>Seeds Core's LeashCutSafety with this head's targets and UI thread.</summary>
    public static void Seed()
    {
        LeashCutSafety.LiveTargets = AppTargets.Instance;
        LeashCutSafety.RunOnUi = a => Helpers.DispatcherHelper.RunOnUISync(a);
    }

    /// <summary>The live app. Every member tolerates a service that is not built yet.</summary>
    public sealed class AppTargets : LeashCutSafety.ITargets
    {
        public static readonly AppTargets Instance = new();

        public bool LockdownActive => App.Lockdown?.IsActive == true;
        public void EndLockdown() => App.Lockdown?.Deactivate();
        public void DiscardLockdownRecovery() => LockdownService.DiscardRecovery();

        public bool RemoteActive => App.RemoteControl?.IsActive == true;
        public void EndRemote() => App.RemoteControl?.EndSessionNow();

        public bool StrictVideoRunning => App.Video?.IsStrictActive == true;
        public void StopVideo() => App.Video?.ForceCleanup();

        public void ReleaseSafetySettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            s.StrictLockEnabled = false;
            s.PanicKeyEnabled = true;
            App.Settings!.SaveImmediate();
            // Same resync RemoteControlService's enable_panic does: the global hook and the
            // Settings checkbox move with the flag, or the stale checkbox writes false back.
            try { App.MainWindowRef?.SyncNoPanicState(); }
            catch (Exception ex) { Serilog.Log.Warning("[Leash] Panic-key UI sync failed: {Error}", ex.Message); }
        }

        /// <summary>
        /// OWED. Circe's Tab keeps one pooled balance (<c>TabState.BalanceSeconds</c>), not
        /// per-booking rows it can take back: the entry log records what was booked, but nothing
        /// marks which of the balance's seconds were already pushed, so "the unpushed leash part"
        /// cannot be carved out cleanly. The live push also lands within about 30 s of a booking.
        /// Needs a small tab API (leash seconds tracked apart until pushed) from the Chaster side.
        /// </summary>
        public bool DropUnpushedLeashBookings() => false;
    }
}
