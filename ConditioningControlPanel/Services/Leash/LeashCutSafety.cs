using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Leash;

/// <summary>
/// The owner's rule for the cut (2026-09-26): cutting the leash leaves the person with a way out,
/// whatever state the leash left them in. <see cref="Apply"/> runs LOCALLY, the instant the leashed
/// side cuts, BEFORE any server call (<c>LeashService.CutAsync</c> calls it first), and never throws.
///
/// <para>Order matters and is pinned by tests:</para>
/// <list type="number">
/// <item>End Lockdown. Its Deactivate restores the PRE-lockdown Strict Lock / panic values and
/// saves them, so it must run before step 4 or it would undo the release. The crash-recovery file
/// is discarded too, so the next launch cannot restore the old values either.</item>
/// <item>End any remote-control session locally (the server is told afterwards, unawaited).</item>
/// <item>Stop a mandatory video that is running strict-locked, so nobody sits trapped in one.</item>
/// <item>Strict Lock OFF, panic key ON, saved at once, panic hook + checkbox resynced. Always, even
/// when the user turned Strict Lock on themselves.</item>
/// <item>Circe's Tab: leash bookings not yet pushed would be dropped here. OWED (see
/// <see cref="ITargets.DropUnpushedLeashBookings"/>); Chaster time already pushed stays.</item>
/// </list>
/// <para>Each step runs in its own try, so one failing step never stops the ones after it.</para>
/// </summary>
public static class LeashCutSafety
{
    public const string StepLockdown = "lockdown";
    public const string StepRemote = "remote";
    public const string StepVideo = "video";
    public const string StepSettings = "settings";
    public const string StepTab = "tab";

    /// <summary>What the cut touches. <see cref="AppTargets"/> is the real app; tests pass a fake.</summary>
    public interface ITargets
    {
        bool LockdownActive { get; }
        void EndLockdown();
        void DiscardLockdownRecovery();
        bool RemoteActive { get; }
        void EndRemote();
        bool StrictVideoRunning { get; }
        void StopVideo();
        /// <summary>StrictLockEnabled = false, PanicKeyEnabled = true, save now, resync the panic UI.</summary>
        void ReleaseSafetySettings();
        /// <summary>Drop unpushed leash bookings from Circe's Tab. Returns false when it cannot.</summary>
        bool DropUnpushedLeashBookings();
    }

    /// <summary>Run the cut's safety steps against the live app. Never throws. Marshals to the UI
    /// thread when called from elsewhere. Returns the steps that ran, in order.</summary>
    public static IReadOnlyList<string> Apply()
    {
        IReadOnlyList<string> done = Array.Empty<string>();
        try
        {
            Helpers.DispatcherHelper.RunOnUISync(() => done = Apply(AppTargets.Instance));
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "[Leash] Cut safety could not reach the UI thread");
        }
        return done;
    }

    /// <summary>The ordered steps against any <see cref="ITargets"/>. Never throws.</summary>
    public static IReadOnlyList<string> Apply(ITargets t)
    {
        var done = new List<string>(5);
        if (t == null) return done;

        Step(done, StepLockdown, () =>
        {
            if (!t.LockdownActive) { t.DiscardLockdownRecovery(); return false; }
            try { t.EndLockdown(); }
            finally { t.DiscardLockdownRecovery(); }
            return true;
        });
        Step(done, StepRemote, () =>
        {
            if (!t.RemoteActive) return false;
            t.EndRemote();
            return true;
        });
        Step(done, StepVideo, () =>
        {
            if (!t.StrictVideoRunning) return false;
            t.StopVideo();
            return true;
        });
        Step(done, StepSettings, () => { t.ReleaseSafetySettings(); return true; });
        Step(done, StepTab, t.DropUnpushedLeashBookings);

        App.Logger?.Information("[Leash] Cut safety applied: {Steps}", string.Join(", ", done));
        return done;
    }

    private static void Step(List<string> done, string name, Func<bool> run)
    {
        try { if (run()) done.Add(name); }
        catch (Exception ex)
        {
            App.Logger?.Warning("[Leash] Cut safety step {Step} failed: {Error}", name, ex.Message);
        }
    }

    /// <summary>The live app. Every member tolerates a service that is not built yet.</summary>
    public sealed class AppTargets : ITargets
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
            catch (Exception ex) { App.Logger?.Warning("[Leash] Panic-key UI sync failed: {Error}", ex.Message); }
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
