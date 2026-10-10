using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// EMI's offers (WPF EmiOffers): an effect whose chip would do nothing is never feasible, a yes
/// fires the effect's moment with <c>fromAsk</c> so she does not speak twice, and the session
/// beats fire once each. The bus sink and the offers head are process-wide, so this shares the
/// session collection with the other tests that swap them.
/// </summary>
[Collection(SessionStatics.Name)]
public sealed class EmiOffersTests : IDisposable
{
    private readonly EmiOffers.HeadSeams? _head = EmiOffers.Head;
    private readonly Action<string, object?>? _sink = EmiDeskBus.Sink;
    private readonly Func<string?> _root = EmiOffers.AssetsRoot;
    private readonly List<(string Id, object? Ctx)> _heard = new();

    public EmiOffersTests() => EmiDeskBus.Sink = (id, ctx) => _heard.Add((id, ctx));

    public void Dispose()
    {
        EmiOffers.Head = _head;
        EmiDeskBus.Sink = _sink;
        EmiOffers.AssetsRoot = _root;
        EmiOffers.ForgetLibrary();
    }

    [Fact]
    public void WithNoHead_OnlyNothingIsOnOffer()
    {
        EmiOffers.Head = null;
        Assert.True(EmiOffers.EffectFeasible("none"));
        foreach (var e in new[] { "open:arcademy", "pinTop:loom", "tour:shortwalk", "book:open", "spiral", "video", "rain", "burst", "shrink", "packs", "assets", "typo", "", null })
            Assert.False(EmiOffers.EffectFeasible(e), e ?? "(null)");
    }

    [Fact]
    public void ADoorIsOfferedOnlyWhileItIsAvailable_AndUnknownVerbsAreRefused()
    {
        var opened = new List<string>();
        bool available = true;
        EmiOffers.Head = new EmiOffers.HeadSeams { TargetAvailable = _ => available, OpenTarget = opened.Add, OpenUrl = _ => { } };

        Assert.True(EmiOffers.EffectFeasible("open:arcademy"));
        Assert.True(EmiOffers.EffectFeasible("packs"));
        Assert.False(EmiOffers.EffectFeasible("pinTop:arcademy"));   // no pin action on this head: no dead chip
        Assert.False(EmiOffers.EffectFeasible("launch:arcademy"));

        available = false;                                           // it locked while she waited
        Assert.False(EmiOffers.EffectFeasible("open:arcademy"));

        EmiOffers.Run("open:arcademy", fromAsk: true);
        Assert.Equal(new[] { "arcademy" }, opened);
    }

    [Fact]
    public void ATourNeedsAWindow_NoSession_NoTourUp_AndAStarter()
    {
        bool window = true, session = false, touring = false, can = true;
        var started = new List<string>();
        EmiOffers.Head = new EmiOffers.HeadSeams
        {
            MainWindowAlive = () => window, SessionRunning = () => session, TutorialActive = () => touring,
            CanStartTour = () => can, StartTour = started.Add,
        };
        bool done = EmiState.HasTourDone(EmiKnockMachine.ShortWalkTour);
        Assert.Equal(!done, EmiOffers.EffectFeasible("tour:shortwalk"));
        Assert.False(EmiOffers.EffectFeasible("tour:nowhere"));

        session = true; Assert.False(EmiOffers.EffectFeasible("tour:shortwalk")); session = false;
        touring = true; Assert.False(EmiOffers.EffectFeasible("tour:shortwalk")); touring = false;
        can = false; Assert.False(EmiOffers.EffectFeasible("tour:shortwalk")); can = true;
        window = false; Assert.False(EmiOffers.EffectFeasible("tour:shortwalk"));

        Assert.Equal("ShortWalk", EmiOffers.TourNameOf(" ShortWalk "));
        Assert.Equal("UpgradeTour", EmiOffers.TourNameOf("upgrade"));
        Assert.Null(EmiOffers.TourNameOf("deeper"));
    }

    [Fact]
    public void TheBookIsNotOfferedWhileOpen_AndAYesSaysItCameFromTheAsk()
    {
        bool open = false;
        int opens = 0;
        EmiOffers.Head = new EmiOffers.HeadSeams { BookOpen = () => open, OpenBook = () => opens++ };

        Assert.True(EmiOffers.EffectFeasible("book:open"));
        Assert.False(EmiOffers.EffectFeasible("book:close"));
        open = true;
        Assert.False(EmiOffers.EffectFeasible("book:open"));

        EmiOffers.Run("book:open", fromAsk: true);
        Assert.Equal(1, opens);
        var fired = Assert.Single(_heard);
        Assert.Equal("effectFired", fired.Id);
        var ctx = EmiLineEngine.ToCtx(fired.Ctx);
        Assert.Equal("book", ctx!["channel"]);
        Assert.Equal(true, ctx["fromAsk"]);                          // the engine stays silent on it (LINES-SCHEMA 5.6)
    }

    [Fact]
    public void AVideoIsOfferedOnlyWithALibrary_AndNeverClaimsAFakeLength()
    {
        var dir = Path.Combine(Path.GetTempPath(), "k13-offers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "videos"));
        try
        {
            var played = new List<string>();
            EmiOffers.AssetsRoot = () => dir;
            EmiOffers.Head = new EmiOffers.HeadSeams { PlayVideo = played.Add, Burst = _ => { } };
            EmiOffers.ForgetLibrary();
            Assert.False(EmiOffers.EffectFeasible("video"));         // an empty folder: never shown, never fizzles
            Assert.False(EmiOffers.EffectFeasible("burst"));

            File.WriteAllBytes(Path.Combine(dir, "videos", "My_First-Clip.mp4"), new byte[] { 0 });
            EmiOffers.ForgetLibrary();
            Assert.True(EmiOffers.EffectFeasible("video"));

            EmiOffers.Run("video", fromAsk: false);
            Assert.Single(played);
            Assert.Equal(new[] { "effectFired", "videoRunning" }, _heard.ConvertAll(h => h.Id));
            var ctx = EmiLineEngine.ToCtx(_heard[1].Ctx);
            Assert.Equal("my first clip", ctx!["target"]);
            Assert.False(ctx.ContainsKey("minutes"));                // unknown length: the {minutes} lines are skipped
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* temp */ }
        }
    }

    [Fact]
    public void ASessionSaysHalfwayAndLastMinuteOnceEach_AndNamesAPhaseChange()
    {
        var runner = new SessionRunner(new SessionLogService());
        var session = new Session
        {
            Id = "k13-beats", Name = "Beats", DurationMinutes = 10,
            Settings = new SessionSettings(),
            Phases = { new SessionPhase { Name = "Warm Up", StartMinute = 0 }, new SessionPhase { Name = "Deep", StartMinute = 6 } },
        };
        try
        {
            runner.Start(session);
            _heard.Clear();

            runner.Tick(TimeSpan.FromMinutes(2));
            Assert.Empty(_heard);
            runner.Tick(TimeSpan.FromMinutes(5.2));
            runner.Tick(TimeSpan.FromMinutes(5.4));
            Assert.Equal(new[] { "sessionHalfway" }, _heard.ConvertAll(h => h.Id));
            Assert.Equal(5, EmiLineEngine.ToCtx(_heard[0].Ctx)!["minutes"]);

            runner.Tick(TimeSpan.FromMinutes(6.5));
            Assert.Equal("sessionPhaseChanged", _heard[^1].Id);
            var phase = EmiLineEngine.ToCtx(_heard[^1].Ctx)!;
            Assert.Equal("deep", phase["target"]);
            Assert.Equal(2, phase["n"]);

            runner.Tick(TimeSpan.FromMinutes(9.2));
            runner.Tick(TimeSpan.FromMinutes(9.5));
            Assert.Equal(new[] { "sessionHalfway", "sessionPhaseChanged", "sessionLastMinute" }, _heard.ConvertAll(h => h.Id));
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop();
            CoreSession.IsSessionRunningProvider = null;
        }
    }
}
