// PORTED from ConditioningControlPanel/App.xaml.cs:2238-2348 (7.1.5): Awareness v2's composition root.
// One ledger, one memory, one scorer, one arbiter, one observer, exactly as WPF builds them, plus the
// seams Core cannot reach by itself (probes, the UI-thread poll timer, the avatar's mouth).
//
// Lifecycle (WPF WindowAwarenessService.Start/Stop :399 / :419): the observer starts and stops with the
// legacy title service, through WindowAwarenessService.V2Lifecycle. That one call site is what the avatar
// tube, the awareness dial, the entitlement sweep and the privacy card's pause already use, so the
// observer's timers exist only while the master switch is on, consent is given and tier 1 holds.
// Nothing here runs a thread or a timer on its own: Wire() builds idle objects.
//
// Panic: WPF does not stop the observer on a panic press (no panic path names App.Awareness or
// App.WindowAwareness). This head does the same.
//
// Privacy: window titles, app names and media titles stay in the observer's memory. They reach the AI
// only as a ContextFrame through AwarenessPrivacyRules and the projection inside AwarenessReactionService.
// Nothing in this file logs one.

using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

internal static class AwarenessHead
{
    /// <summary>WPF App.Awareness. Null when construction failed: the legacy pipeline carries on.</summary>
    internal static AwarenessObserver? Observer { get; private set; }

    /// <summary>WPF BarkService.SelfEchoMuteMs sibling for the keyword engine's echo guard.</summary>
    private static bool _wired;

    /// <summary>Builds the pipeline. Idempotent. Starts nothing.</summary>
    internal static void Wire()
    {
        if (_wired) return;
        _wired = true;

        try
        {
            SeedPlatform();

            var ledger = new ActivityLedger();

            // ONE memory instance, shared by the observer and the arbiter, or the recent-line ban list
            // splits across two rings and the companion repeats itself.
            var memory = new StubCompanionMemory();

            // ONE scorer: the arbiter is the only caller of RegisterDelivery, so a second instance
            // leaves the silence budget inert.
            var scorer = new WorthinessScorer();

            // ONE arbiter: the single cooldown ledger every awareness line, bark and keyword comment
            // passes through.
            AwarenessObserver? observerRef = null;
            var arbiter = new ReactionArbiter(
                cooldowns: null,
                scorer: scorer,
                localClock: null,
                speaker: new AvatarAwarenessSpeaker(currentAppId: () => observerRef?.CurrentAppId),
                lineSource: new BrainAwarenessLineSource(),
                memory: memory);

            Observer = new AwarenessObserver(ledger, scorer, arbiter, memory);
            observerRef = Observer;

            // The trust surface's seam: the privacy panel renders the real last frame and erases the
            // real ledger through these. Set before anything can cut a frame.
            AwarenessLive.Ledger = ledger;
            AwarenessLive.Memory = memory;
            AwarenessLive.ResetObserverState = Observer.ResetTransientState;
            AwarenessLive.ResetPacingState = () =>
            {
                arbiter.Cooldowns.Reset();
                scorer.Reset();
            };
            AwarenessLive.ForgetPacingState = id =>
            {
                arbiter.Cooldowns.Forget(id);
                scorer.Forget(id);
            };

            // Retention does not depend on the feature being switched on (WPF :2310): this sweep runs
            // on every launch, creates nothing when there is no file.
            try { ledger.PruneOnDisk(); }
            catch (Exception ex) { Log.Warning(ex, "ActivityLedger: startup retention sweep failed"); }

            // The two one-time initialisers, for every profile (WPF :2317). Both are idempotent.
            try
            {
                var settings = CoreSettings.HasProvider ? CoreSettings.Current : null;
                bool wrote = AwarenessPrivacyRules.EnsureSeeded(settings);
                wrote |= AwarenessIntensityMigration.EnsureMigrated(settings);
                if (wrote) CoreSettings.Save();
            }
            catch (Exception ex) { Log.Warning(ex, "Awareness: deny-group seed / intensity migration failed"); }

            // Engages v2: the legacy mouth (the tube's title handlers) stands down while the routing is
            // active, and barks and keyword comments register against the one cooldown ledger.
            AwarenessV2Routing.Attach(arbiter);
            WindowAwarenessService.V2OwnsReactionsProvider = () => AwarenessV2Routing.IsActive;
            WindowAwarenessService.V2Lifecycle = on =>
            {
                if (on) Observer?.Start(); else Observer?.Stop();
            };

            Log.Information("AwarenessObserver constructed (v2 enabled={Enabled})", AwarenessObserver.IsEnabled);
        }
        catch (Exception ex)
        {
            // An observer that will not build must not take the app down: the legacy pipeline is still
            // there. Detach so the legacy mouth is not left suppressed by a half-built v2.
            Observer = null;
            try { AwarenessV2Routing.Detach(); } catch { }
            WindowAwarenessService.V2OwnsReactionsProvider = null;
            WindowAwarenessService.V2Lifecycle = null;
            AwarenessLive.Ledger = null;
            AwarenessLive.Memory = null;
            AwarenessLive.ResetObserverState = null;
            AwarenessLive.ResetPacingState = null;
            AwarenessLive.ForgetPacingState = null;
            Log.Error(ex, "AwarenessObserver: initialization failed, falling back to legacy awareness");
        }
    }

    /// <summary>WPF App.OnExit :5775-5782: the observer goes before the legacy service.</summary>
    internal static void Shutdown()
    {
        try { Observer?.Dispose(); } catch { }
        Observer = null;
        AwarenessLive.ResetObserverState = null;
        AwarenessLive.Ledger = null;
        AwarenessLive.Memory = null;
        WindowAwarenessService.V2Lifecycle = null;
    }

    private static void SeedPlatform()
    {
        AwarenessPlatform.ForegroundProbeFactory = () => new HeadForegroundProbe();
        AwarenessPlatform.InputProbeFactory = () => new HeadInputProbe();
        AwarenessPlatform.MediaWatcherFactory = MediaAwareness.Create;
        AwarenessPlatform.AppStateProbeFactory = () => new HeadAppStateProbe();
        AwarenessPlatform.PollTimerFactory = (interval, tick) => new AwarenessPollTimer(interval, tick);

        AwarenessHost.Ai = () => App.Ai;
        AwarenessHost.ForegroundTitle = ActiveWindowTitle.Read;
        AwarenessHost.CurrentServiceName = () => App.WindowAwareness.CurrentServiceName;
        AwarenessHost.RecentForegroundApps = () => KeywordTriggerHead.Engine.GetRecentForegroundApps();
        AwarenessHost.MuteKeywordEcho = (line, ms) => KeywordTriggerHead.Engine.MuteKeywordEcho(line, ms);
        AwarenessHost.RaiseAwarenessBark = frame => BarkHead.Engine?.RaiseAwarenessBark(frame) ?? false;
        AwarenessHost.NotifyExternalLineSpoken = () => BarkHead.Engine?.NotifyExternalLineSpoken();
        AwarenessHost.HasAvatar = () => Views.AvatarTube.AvatarTubeWindow.Live != null;
        AwarenessHost.IsCompanionBusy = ms => Views.AvatarTube.AvatarTubeWindow.Live?.IsCompanionBusy(ms) == true;
        AwarenessHost.SpeakAwarenessLine = (line, doubleBounce) =>
        {
            var tube = Views.AvatarTube.AvatarTubeWindow.Live;
            if (tube == null) return false;
            tube.SpeakAwarenessLine(line, doubleBounce);
            return true;
        };
    }
}
