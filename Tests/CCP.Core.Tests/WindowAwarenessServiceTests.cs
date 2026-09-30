using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The legacy title observer, now Core, driven with a fake title source: events, the
/// Avalonia privacy filter (deny list + incognito), and the per-tick entitlement re-check.</summary>
[Collection(SessionStatics.Name)]
public sealed class WindowAwarenessServiceTests : IDisposable
{
    private readonly (Func<bool>?, Func<string?, bool>?) _oldEntitlement =
        (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
    private readonly List<string> _oldDeny = new(CoreSettings.Current.AwarenessDenyList ?? new());
    private readonly bool _oldSeeded = CoreSettings.Current.AwarenessDenySeeded;
    private bool _premium = true;
    private string _title = "";
    private int _reads;

    public WindowAwarenessServiceTests()
    {
        CoreEntitlement.HasPremiumProvider = () => _premium;
        CoreEntitlement.IsFreeTodayProvider = _ => false;
        AwarenessPause.Resume();
        CoreSettings.Current.AwarenessDenySeeded = false;   // seeded groups apply
        CoreSettings.Current.AwarenessDenyList = new List<string> { "hades" };
    }

    public void Dispose()
    {
        (CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = _oldEntitlement;
        CoreSettings.Current.AwarenessDenyList = _oldDeny;
        CoreSettings.Current.AwarenessDenySeeded = _oldSeeded;
    }

    private (WindowAwarenessService Svc, List<ActivityChangedEventArgs> Events) Make(bool filtered)
    {
        var svc = filtered
            ? new WindowAwarenessService(() => { _reads++; return _title; }, WindowAwarenessService.PassesPrivacyRules)
            : new WindowAwarenessService(() => { _reads++; return _title; });
        var events = new List<ActivityChangedEventArgs>();
        svc.ActivityChanged += (_, e) => events.Add(e);
        return (svc, events);
    }

    private static void Show(WindowAwarenessServiceTests t, WindowAwarenessService svc, string title)
    {
        t._title = title;
        svc.PollOnce();
    }

    [Fact]
    public void AKnownWindowRaisesTheCategorisedEvent()
    {
        var (svc, events) = Make(filtered: true);
        using (svc)
        {
            Show(this, svc, "League of Legends");
            var e = Assert.Single(events);
            Assert.Equal(ActivityCategory.Gaming, e.Category);
            Assert.Equal("League of Legends", svc.CurrentServiceName);
        }
    }

    [Theory]
    [InlineData("My Vault | Bitwarden - Mozilla Firefox")]          // seeded password-manager group
    [InlineData("Hades")]                                            // user deny entry
    [InlineData("YouTube - Mozilla Firefox Private Browsing")]      // incognito marker
    [InlineData("YouTube - Google Chrome (Incognito)")]
    public void ADeniedOrIncognitoWindowRaisesNothingAndKeepsNothing(string title)
    {
        var (svc, events) = Make(filtered: true);
        using (svc)
        {
            Show(this, svc, "League of Legends");
            events.Clear();

            Show(this, svc, title);

            Assert.Empty(events);
            Assert.Equal(ActivityCategory.Unknown, svc.CurrentActivity);
            Assert.Equal("", svc.CurrentServiceName);
            Assert.Equal("", svc.CurrentPageTitle);
            Assert.Equal("", svc.CurrentDetectedName);
        }
    }

    [Fact]
    public void WithoutAFilterTheSameTitleIsObserved()
    {
        // The WPF construction: no filter, legacy behaviour unchanged.
        var (svc, events) = Make(filtered: false);
        using (svc)
        {
            Show(this, svc, "YouTube - Mozilla Firefox Private Browsing");
            Assert.Single(events);
        }
    }

    [Fact]
    public void ALapsedAccountIsNotEvenRead()
    {
        var (svc, events) = Make(filtered: true);
        using (svc)
        {
            _premium = false;
            Show(this, svc, "League of Legends");
            Assert.Equal(0, _reads);
            Assert.Empty(events);
        }
    }

    /// <summary>Holds posted ticks until the test runs them, like a busy UI thread.</summary>
    private sealed class QueueContext : System.Threading.SynchronizationContext
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<(System.Threading.SendOrPostCallback, object?)> Posted = new();
        public override void Post(System.Threading.SendOrPostCallback d, object? state) => Posted.Enqueue((d, state));
    }

    [Fact]
    public void ATickQueuedBeforeStopOrRestartNeverRuns()
    {
        var s = CoreSettings.Current;
        var old = (s.AwarenessModeEnabled, s.AwarenessConsentGiven, System.Threading.SynchronizationContext.Current);
        var ctx = new QueueContext();
        var (svc, _) = Make(filtered: true);
        try
        {
            s.AwarenessModeEnabled = true; s.AwarenessConsentGiven = true;
            System.Threading.SynchronizationContext.SetSynchronizationContext(ctx);
            svc.Start();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (ctx.Posted.IsEmpty && DateTime.UtcNow < deadline) System.Threading.Thread.Sleep(50);
            Assert.False(ctx.Posted.IsEmpty);   // the 1.5 s poll queued a tick

            svc.Stop();
            svc.Start();                         // a new poll timer; the queued tick belongs to the old one
            Assert.True(ctx.Posted.TryDequeue(out var stale));
            stale.Item1(stale.Item2);
            Assert.Equal(0, _reads);
        }
        finally
        {
            svc.Dispose();
            System.Threading.SynchronizationContext.SetSynchronizationContext(old.Current);
            (s.AwarenessModeEnabled, s.AwarenessConsentGiven) = (old.AwarenessModeEnabled, old.AwarenessConsentGiven);
        }
    }

    [Fact]
    public void StartNeedsEntitlementAndConsent()
    {
        var s = CoreSettings.Current;
        var old = (s.AwarenessModeEnabled, s.AwarenessConsentGiven);
        var (svc, _) = Make(filtered: true);
        try
        {
            s.AwarenessModeEnabled = true; s.AwarenessConsentGiven = false;
            svc.Start();
            Assert.False(svc.IsRunning);

            s.AwarenessConsentGiven = true; _premium = false;
            svc.Start();
            Assert.False(svc.IsRunning);

            _premium = true;
            svc.Start();
            Assert.True(svc.IsRunning);
        }
        finally
        {
            svc.Dispose();
            (s.AwarenessModeEnabled, s.AwarenessConsentGiven) = old;
        }
    }
}
