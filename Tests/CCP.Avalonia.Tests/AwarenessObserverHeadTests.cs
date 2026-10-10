using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using ConditioningControlPanel.Services.UI;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>Lane w3: the awareness observer on this head (WPF App.xaml.cs:2238 composition root,
/// Services/Awareness/AwarenessProbes.cs). The pure pipeline is pinned in CCP.Core.Tests/Awareness.</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class AwarenessObserverHeadTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    /// <summary>Runs with a private settings object and every awareness gate set as asked.</summary>
    private static Task WithSettings(bool master, bool premium, Action body) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var service = new SettingsService();
        var wasProvider = CoreSettings.ServiceProvider;
        var wasPremium = CoreEntitlement.HasPremiumProvider;
        CoreSettings.ServiceProvider = () => service;
        CoreEntitlement.HasPremiumProvider = () => premium;
        var s = CoreSettings.Current;
        s.UseAwarenessV2 = true;
        s.AwarenessModeEnabled = master;
        s.AwarenessConsentGiven = true;
        s.AwarenessConsentShownV2 = true;
        try { body(); }
        finally
        {
            CoreSettings.ServiceProvider = wasProvider;
            CoreEntitlement.HasPremiumProvider = wasPremium;
        }
        return Task.CompletedTask;
    });

    private sealed class FakeForeground : IForegroundProbe
    {
        public int Reads;
        public ForegroundSample? Read() { Reads++; return null; }
    }

    private sealed class FakeInput : IInputProbe
    {
        public int Starts, Stops;
        public int IdleSeconds => 0;
        public bool IsTypingBurst => false;
        public void Start() => Starts++;
        public void Stop() => Stops++;
        public void Dispose() { }
    }

    private sealed class FakeMedia : IMediaWatcher
    {
        public int Starts, Stops;
        public MediaSample? Current => null;
        public bool IsAvailable => true;
        public void Start() => Starts++;
        public void Stop() => Stops++;
        public void Dispose() { }
    }

    private sealed class NoMic : IMicrophoneProbe { public bool IsInUse(DateTime at) => false; }
    private sealed class NoState : IAppStateProbe { public AppStateSample Read(DateTime at) => AppStateSample.Empty; }

    private sealed class SilentArbiter : IReactionArbiter
    {
        public Task<ArbiterDecision> SubmitAsync(ContextFrame frame, System.Threading.CancellationToken cancellationToken = default) =>
            Task.FromResult(new ArbiterDecision(AwarenessVerdict.Silence, RarityTier.Common, "test"));
        public void RecordExternalLine(ReactionSource source, string? appId = null) { }
        public bool CanSpeak(ReactionSource source, string? appId = null) => false;
    }

    private static (AwarenessObserver Observer, FakeInput Input, FakeMedia Media, List<IDisposable> Timers) NewObserver()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ccp-w3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var ledger = new ActivityLedger(Path.Combine(dir, "awareness_ledger.json"));
        var input = new FakeInput();
        var media = new FakeMedia();
        var observer = new AwarenessObserver(ledger, new WorthinessScorer(), new SilentArbiter(), new StubCompanionMemory(),
            null, new FakeForeground(), input, new NoMic(), media, new NoState(), null);
        return (observer, input, media, new List<IDisposable>());
    }

    private sealed class CountingTimer : IDisposable
    {
        public static int Live;
        public CountingTimer() => Live++;
        public void Dispose() => Live--;
    }

    [Fact]
    public Task TheObserverArmsNothingWhileTheMasterSwitchIsOff() => WithSettings(master: false, premium: true, () =>
    {
        var wasFactory = AwarenessPlatform.PollTimerFactory;
        CountingTimer.Live = 0;
        AwarenessPlatform.PollTimerFactory = (_, _) => new CountingTimer();
        var (observer, input, media, _) = NewObserver();
        try
        {
            observer.Start();
            Assert.False(observer.IsRunning);
            Assert.Equal(0, CountingTimer.Live);
            Assert.Equal(0, input.Starts);
            Assert.Equal(0, media.Starts);
        }
        finally { observer.Dispose(); AwarenessPlatform.PollTimerFactory = wasFactory; }
    });

    [Fact]
    public Task TheObserverNeedsTierOne() => WithSettings(master: true, premium: false, () =>
    {
        var (observer, input, _, _) = NewObserver();
        try
        {
            observer.Start();
            Assert.False(observer.IsRunning);
            Assert.Equal(0, input.Starts);
        }
        finally { observer.Dispose(); }
    });

    [Fact]
    public Task StartArmsOnePollTimerAndTheProbesAndStopDropsThem() => WithSettings(master: true, premium: true, () =>
    {
        var wasFactory = AwarenessPlatform.PollTimerFactory;
        CountingTimer.Live = 0;
        AwarenessPlatform.PollTimerFactory = (_, _) => new CountingTimer();
        var (observer, input, media, _) = NewObserver();
        try
        {
            observer.Start();
            Assert.True(observer.IsRunning);
            Assert.Equal(1, CountingTimer.Live);
            Assert.Equal(1, input.Starts);
            Assert.Equal(1, media.Starts);

            observer.Start();   // a second Start is a no-op, never a second timer
            Assert.Equal(1, CountingTimer.Live);

            observer.Stop();
            Assert.False(observer.IsRunning);
            Assert.Equal(0, CountingTimer.Live);
            Assert.True(input.Stops >= 1);
            Assert.True(media.Stops >= 1);
        }
        finally { observer.Dispose(); AwarenessPlatform.PollTimerFactory = wasFactory; }
    });

    [Fact]
    public Task TheLegacyServiceIsTheOneSwitchThatRunsTheObserver() => WithSettings(master: false, premium: true, () =>
    {
        var wasLifecycle = WindowAwarenessService.V2Lifecycle;
        var wasOwns = WindowAwarenessService.V2OwnsReactionsProvider;
        var wasArbiter = AwarenessV2Routing.Arbiter;
        try
        {
            AwarenessHead.Wire();
            Assert.NotNull(AwarenessHead.Observer);
            Assert.NotNull(WindowAwarenessService.V2Lifecycle);
            Assert.NotNull(AwarenessV2Routing.Arbiter);

            // Built idle: nothing runs until WindowAwareness.Start says so, and with the master switch
            // off that call starts nothing either.
            Assert.False(AwarenessHead.Observer!.IsRunning);
            WindowAwarenessService.V2Lifecycle!(true);
            Assert.False(AwarenessHead.Observer.IsRunning);
            Assert.False(AwarenessV2Routing.IsActive);   // so the legacy mouth is not suppressed
        }
        finally
        {
            WindowAwarenessService.V2Lifecycle?.Invoke(false);
            WindowAwarenessService.V2Lifecycle = wasLifecycle;
            WindowAwarenessService.V2OwnsReactionsProvider = wasOwns;
            AwarenessV2Routing.Attach(wasArbiter);
        }
    });

    // ---- WPF AwarenessReviewFixTests.TheInputProbeSamplesAgainAfterAStopStartCycle, on the head's probe ----

    [Fact]
    public void TheInputProbeSamplesAgainAfterAStopStartCycle()
    {
        using var probe = new HeadInputProbe { SampleSource = () => null };
        probe.Start();
        Assert.True(probe.TimerArmed);
        probe.Stop();
        probe.Start();   // the old guard made the first Stop permanent
        Assert.True(probe.TimerArmed);
        probe.Stop();
    }

    [Fact]
    public void ATypingBurstIsFreshInputWithAStillCursor()
    {
        long tick = 0;
        int x = 10;
        using var probe = new HeadInputProbe();
        probe.SampleSource = () => (tick, x, 5);

        // Mouse movement: fresh input every sample, but the cursor moves. Never a burst.
        for (int i = 0; i < 16; i++) { tick++; x++; probe.Sample(); }
        Assert.False(probe.IsTypingBurst);

        // Typing: fresh input, cursor still. Two typing samples a second over the four-second window.
        for (int i = 0; i < 8; i++) { tick++; probe.Sample(); }
        Assert.True(probe.IsTypingBurst);

        probe.Stop();
        Assert.False(probe.IsTypingBurst);
    }

    // ---- panic: WPF stops screen OCR only in the fallback path and queues its restart ----

    [Fact]
    public void ThePanicFallbackQueuesTheScreenReaderRestart()
    {
        var wasPost = PanicWatchdog.PostToUi;
        var queued = new List<Action>();
        PanicWatchdog.PostToUi = queued.Add;
        try
        {
            PanicWatchdog.Teardown();
            Assert.False(ScreenOcrService.IsRunning);
            Assert.Single(queued);   // the restart waits for the UI thread, as WPF QueuePanicFallbackRecovery
        }
        finally { PanicWatchdog.PostToUi = wasPost; }
    }
}
