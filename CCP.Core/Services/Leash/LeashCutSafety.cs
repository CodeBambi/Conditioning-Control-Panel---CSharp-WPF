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

    /// <summary>The live app's targets, seeded by each head at startup (WPF LeashCutSafetyApp.AppTargets,
    /// Avalonia LeashHead.Targets). Null = nothing seeded: <see cref="Apply()"/> runs no step.</summary>
    public static ITargets? LiveTargets { get; set; }

    /// <summary>Runs an action synchronously on the head's UI thread (WPF DispatcherHelper.RunOnUISync).</summary>
    public static Action<Action>? RunOnUi { get; set; }

    /// <summary>What the cut touches. Each head's live targets implement it; tests pass a fake.</summary>
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
            var live = LiveTargets;
            if (live == null) { Serilog.Log.Warning("[Leash] Cut safety has no live targets on this head"); return done; }
            (RunOnUi ?? (a => a()))(() => done = Apply(live));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[Leash] Cut safety could not reach the UI thread");
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

        Serilog.Log.Information("[Leash] Cut safety applied: {Steps}", string.Join(", ", done));
        return done;
    }

    private static void Step(List<string> done, string name, Func<bool> run)
    {
        try { if (run()) done.Add(name); }
        catch (Exception ex)
        {
            Serilog.Log.Warning("[Leash] Cut safety step {Step} failed: {Error}", name, ex.Message);
        }
    }

}
