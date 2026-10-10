using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// For You ghost mode (WPF 7.1.5 FypHostService "window mechanics: GHOST MODE" + FypGhostOverlay):
/// the page's clickThrough setting parks the feed and shows a see-through, click-through mirror.
/// The platform is a FAKE here: no test calls dwmapi or creates a native window, and the default
/// seam in a headless process is "unavailable". Pinned: the frames WPF sends, and the safety rules
/// (the mirror dies with its window on panic, on a minimise, on a failed health probe; a platform
/// failure is "unavailable", never a live sheet). Swaps process-wide seams, so it runs alone.
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class FypGhostTests
{
    private sealed class FakeSession : IFypGhostSession
    {
        public int Disposals;
        public bool HealthyAnswer = true;
        public readonly List<double> Opacities = new();
        public readonly List<bool> Mutes = new();
        public bool Live => Disposals == 0;
        public void SetOpacity(double opacity) => Opacities.Add(opacity);
        public void SetMuted(bool muted) => Mutes.Add(muted);
        public bool Healthy() => HealthyAnswer;
        public void Dispose() => Disposals++;
    }

    private sealed class FakePlatform : IFypGhostPlatform
    {
        public string? Refuse;
        public bool Throw;
        public int Enters;
        public double Opacity;
        public bool Muted;
        public Action? Gear, Mute;
        public readonly List<FakeSession> Sessions = new();
        public FakeSession Last => Sessions[^1];

        public IFypGhostSession? Enter(IntPtr sourceHwnd, double opacity, bool muted, Action onGear, Action onMute, out string? reason)
        {
            Enters++;
            if (Throw) throw new InvalidOperationException("boom");
            reason = Refuse;
            if (Refuse != null) return null;
            Opacity = opacity; Muted = muted; Gear = onGear; Mute = onMute;
            var s = new FakeSession();
            Sessions.Add(s);
            return s;
        }
    }

    private sealed class FakeEye : IFypEye
    {
        public bool IsRunning { get; set; }
        public bool Calibrated { get; set; }
        public bool Faulted { get; set; }
        public (double OriginX, double OriginY, double Scale) CalSpace => (0, 0, 1);
        public event Action? OnBlink;
        public event Action? OnEyesClosedLong;
        public event Action<Point>? OnGazeMove;
        public Task<bool> StartAsync() { IsRunning = true; return Task.FromResult(true); }
        public void Stop() => IsRunning = false;
        public void Blink() => OnBlink?.Invoke();
        public void CloseEyes() => OnEyesClosedLong?.Invoke();
        public void Gaze(Point p) => OnGazeMove?.Invoke(p);
    }

    private static void Run(FakePlatform platform, Action<GameWindow, List<JObject>> body, FakeEye? eye = null, Func<Window, Task>? calibrate = null)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            var oldService = CoreSettings.ServiceProvider;
            CoreSettings.ServiceProvider = () => service;
            var oldGate = (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreProgression.AddXPProvider);
            CoreAccount.IsLoggedInProvider = () => true;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.HasLabProvider = () => false;
            CoreProgression.AddXPProvider = (_, _) => { };
            var oldPlatform = GameWindow.FypGhostPlatform;
            var oldEye = (GameWindow.FypEyeSource, GameWindow.FypEyeConsent, GameWindow.FypEyeCalibrate, GameWindow.FypEyeCalibrating);
            var stats = Path.Combine(Path.GetTempPath(), "k24-fyp-ghost-" + Guid.NewGuid().ToString("N") + ".json");
            var prevStats = FypHostService.StatsFilePathOverride;
            var s = CoreSettings.Current;
            GameWindow? w = null;
            try
            {
                // The seam a headless process starts with never reaches the platform.
                Assert.IsType<FypGhostUnavailable>(oldPlatform);
                FypHostService.StatsFilePathOverride = stats;
                GameWindow.FypGhostPlatform = platform;
                GameWindow.FypEyeSource = () => eye;
                GameWindow.FypEyeConsent = _ => Task.FromResult(true);
                GameWindow.FypEyeCalibrate = calibrate ?? (_ => Task.CompletedTask);
                GameWindow.FypEyeCalibrating = () => false;
                s.FypEyeControl = false;
                s.FypEyeGaze = false;
                s.FypMuted = false;
                s.FypWindowOpacity = 0.6;

                w = GameWindow.Launch(GameWindow.FypId)!;
                Dispatcher.UIThread.RunJobs();
                var posted = new List<JObject>();
                w.Posted += json => posted.Add(JObject.Parse(json));
                w.HandleMessage("{\"type\":\"ready\"}");
                posted.Clear();
                body(w, posted);
            }
            finally
            {
                try { w?.Close(); Dispatcher.UIThread.RunJobs(); } catch { }
                GameWindow.FypGhostPlatform = oldPlatform;
                (GameWindow.FypEyeSource, GameWindow.FypEyeConsent, GameWindow.FypEyeCalibrate, GameWindow.FypEyeCalibrating) = oldEye;
                FypHostService.StatsFilePathOverride = prevStats;
                GameWindow.CloseAllForPanic();
                Dispatcher.UIThread.RunJobs();
                (CoreAccount.IsLoggedInProvider, CoreEntitlement.HasPremiumProvider, CoreEntitlement.HasLabProvider, CoreProgression.AddXPProvider) = oldGate;
                CoreSettings.ServiceProvider = oldService;
                service.SealForReset();
                try { File.Delete(stats); } catch { }
            }
        });
    }

    private static void Ghost(GameWindow w, bool on) =>
        w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"clickThrough\",\"value\":" + (on ? "true" : "false") + "}");

    private static string?[] Types(List<JObject> posted) => posted.Select(p => (string?)p["type"]).ToArray();

    // ---- on / off / opacity: WPF's frames -------------------------------------------------------

    [Fact]
    public void On_ParksBehindAMirror_AndSaysNothing_Off_GivesTheWindowBack()
    {
        var p = new FakePlatform();
        Run(p, (w, posted) =>
        {
            Ghost(w, true);
            Assert.Equal(1, p.Enters);
            Assert.True(w.IsFypGhosted);
            Assert.True(GameWindow.IsFypGhostedAny());
            Assert.Equal(0.6, p.Opacity, 3);          // the persisted slider, from the first frame
            Assert.False(p.Muted);
            Assert.Empty(posted);                     // the page already flipped its own toggle

            Ghost(w, true);                           // idempotent: never a second mirror
            Assert.Equal(1, p.Enters);

            // The slider rides the mirror live, and is stored.
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"windowOpacity\",\"value\":0.25}");
            Assert.Equal(0.25, p.Last.Opacities.Single(), 3);
            Assert.Equal(0.25, CoreSettings.Current.FypWindowOpacity, 3);

            Ghost(w, false);
            Assert.Equal(1, p.Last.Disposals);
            Assert.False(w.IsFypGhosted);
            Assert.False(GameWindow.IsFypGhostedAny());
            Assert.Equal("clickThrough", (string?)posted.Single()["type"]);
            Assert.False((bool)posted[0]["on"]!);

            // Off with nothing up is silent, and the slider with no mirror only stores.
            posted.Clear();
            Ghost(w, false);
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"windowOpacity\",\"value\":0.5}");
            Assert.Empty(posted);
            Assert.Equal(1, p.Last.Disposals);
        });
    }

    [Fact]
    public void AMaximisedFeed_IsNormalisedForThePark_AndPutBack()
    {
        var p = new FakePlatform();
        Run(p, (w, posted) =>
        {
            w.WindowState = WindowState.Maximized;
            Ghost(w, true);
            Assert.True(w.IsFypGhosted);                           // normalising is not "the feed was minimised"
            Assert.Equal(WindowState.Normal, w.WindowState);
            Ghost(w, false);
            Assert.Equal(WindowState.Maximized, w.WindowState);
        });
    }

    // ---- failure is "unavailable", never a sheet ------------------------------------------------

    [Theory]
    [InlineData("colorkey-rejected")]
    [InlineData("composition-disabled")]
    [InlineData(FypGhostUnavailable.Reason)]
    public void APlatformThatCannotCompose_AnswersUnavailable_WithItsReason(string reason)
    {
        var p = new FakePlatform { Refuse = reason };
        Run(p, (w, posted) =>
        {
            Ghost(w, true);
            Assert.False(w.IsFypGhosted);
            Assert.Empty(p.Sessions);
            Assert.Equal(new[] { "clickThrough", "ghost-unavailable" }, Types(posted));
            Assert.False((bool)posted[0]["on"]!);
            Assert.Equal(reason, (string?)posted[1]["reason"]);
            Assert.Equal(WindowState.Normal, w.WindowState);
        });
    }

    [Fact]
    public void APlatformThatThrows_AnswersUnavailable_AndTheFeedStaysOpen()
    {
        var p = new FakePlatform { Throw = true };
        Run(p, (w, posted) =>
        {
            Ghost(w, true);
            Assert.False(w.IsFypGhosted);
            Assert.Equal(new[] { "clickThrough", "ghost-unavailable" }, Types(posted));
            Assert.Equal("mirror-threw", (string?)posted[1]["reason"]);
            Assert.True(GameWindow.IsFypOpen());
        });
    }

    [Fact]
    public void WithoutAPlatform_TheAnswerIsTheLinuxOne()
    {
        // The seam every headless process and every Linux desk starts with.
        var session = FypGhostUnavailable.Instance.Enter(IntPtr.Zero, 0.6, false, () => { }, () => { }, out var reason);
        Assert.Null(session);
        Assert.Equal("not on this build", reason);
    }

    // ---- safety: the mirror dies with its window ------------------------------------------------

    [Fact]
    public void Panic_ClosesTheFeedAndItsMirrorOnTheSamePress()
    {
        var p = new FakePlatform();
        Run(p, (w, posted) =>
        {
            Ghost(w, true);
            Assert.True(p.Last.Live);

            GameWindow.CloseAllForPanic();            // PanicSurfaces "games"
            Assert.False(p.Last.Live);                // gone before the dispatcher even turns
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsFypOpen());
            Assert.False(GameWindow.IsFypGhostedAny());
            Assert.Equal(1, p.Last.Disposals);
        });
    }

    [Fact]
    public void ThePagesOwnClose_TakesTheMirrorToo()
    {
        var p = new FakePlatform();
        Run(p, (w, posted) =>
        {
            Ghost(w, true);
            w.HandleMessage("{\"type\":\"close\"}");
            Dispatcher.UIThread.RunJobs();
            Assert.False(p.Last.Live);
            Assert.False(GameWindow.IsFypOpen());
        });
    }

    [Fact]
    public void AMinimise_DropsTheMirror_AndThePageHearsIt()
    {
        var p = new FakePlatform();
        Run(p, (w, posted) =>
        {
            Ghost(w, true);
            w.WindowState = WindowState.Minimized;
            Assert.False(p.Last.Live);
            Assert.False(w.IsFypGhosted);
            Assert.Equal("clickThrough", (string?)posted.Single()["type"]);
            Assert.False((bool)posted[0]["on"]!);
            Assert.True(GameWindow.IsFypOpen());      // the feed itself is only minimised
        });
    }

    [Fact]
    public void ASuspend_DropsEveryMirror_AndTheFeedStaysOpen()
    {
        var p = new FakePlatform();
        Run(p, (w, posted) =>
        {
            Ghost(w, true);
            GameWindow.DropFypGhosts();
            Assert.False(p.Last.Live);
            Assert.Equal("clickThrough", (string?)posted.Single()["type"]);
            Assert.True(GameWindow.IsFypOpen());
        });
    }

    [Fact]
    public void AMirrorThatStopsBeingSeeThrough_ComesDown_AndThePageIsToldWhy()
    {
        var p = new FakePlatform();
        Run(p, (w, posted) =>
        {
            Ghost(w, true);
            w.FypGhostWatchTick();
            Assert.True(p.Last.Live);                 // healthy: nothing happens
            Assert.Empty(posted);

            p.Last.HealthyAnswer = false;             // colour key or click-through lost, or the source froze
            w.FypGhostWatchTick();
            Assert.False(p.Last.Live);
            Assert.False(w.IsFypGhosted);
            Assert.Equal(new[] { "clickThrough", "ghost-unavailable" }, Types(posted));
            Assert.Equal("colorkey-lost", (string?)posted[1]["reason"]);
        });
    }

    [Fact]
    public void APageReinitUnderTheMirror_LeavesGhostMode()
    {
        var p = new FakePlatform();
        Run(p, (w, posted) =>
        {
            Ghost(w, true);
            w.HandleMessage("{\"type\":\"ready\"}");   // the page rebooted: its own toggle is off again
            Assert.False(p.Last.Live);
            Assert.Equal(new[] { "clickThrough", "init" }, Types(posted));
        });
    }

    // ---- the two buttons ------------------------------------------------------------------------

    [Fact]
    public void TheGear_GivesTheWindowBack_AndOpensTheOptions()
    {
        var p = new FakePlatform();
        Run(p, (w, posted) =>
        {
            Ghost(w, true);
            p.Gear!();
            Assert.False(p.Last.Live);
            Assert.Equal(new[] { "clickThrough", "openOptions" }, Types(posted));
        });
    }

    [Fact]
    public void TheSpeaker_FlipsMute_WithoutLeavingGhostMode()
    {
        var p = new FakePlatform();
        Run(p, (w, posted) =>
        {
            Ghost(w, true);
            p.Mute!();
            Assert.True(CoreSettings.Current.FypMuted);
            Assert.True(p.Last.Live);
            Assert.True(p.Last.Mutes.Single());
            Assert.Equal("setMuted", (string?)posted.Single()["type"]);
            Assert.True((bool)posted[0]["on"]!);
            p.Mute!();
            Assert.False(CoreSettings.Current.FypMuted);
            Assert.Equal(new[] { true, false }, p.Last.Mutes);
        });
    }

    // ---- eye control keeps driving the parked page ----------------------------------------------

    [Fact]
    public void EyeControl_KeepsWorkingWhileGhosted_AndCalibrationLeavesGhostMode()
    {
        var p = new FakePlatform();
        var eye = new FakeEye();
        var calibrations = 0;
        bool ghostedAtCalibration = true;
        GameWindow? window = null;
        Run(p, (w, posted) =>
        {
            window = w;
            w.HandleMessage("{\"type\":\"settings-changed\",\"key\":\"eyeControl\",\"value\":true}");
            for (var i = 0; i < 20 && !w.FypEyeTask.IsCompleted; i++) Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            Assert.True(eye.IsRunning);

            Ghost(w, true);
            posted.Clear();
            eye.Blink();
            eye.CloseEyes();
            Assert.Equal(new[] { "blink", "eyesClosed" }, Types(posted));   // the real page still hears them
            Assert.True(p.Last.Live);

            // A parked window cannot host the dot dance: the ghost goes first (WPF :1371).
            posted.Clear();
            w.HandleMessage("{\"type\":\"calibrate\"}");
            for (var i = 0; i < 20 && calibrations == 0; i++) Dispatcher.UIThread.RunJobs();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, calibrations);
            Assert.False(ghostedAtCalibration);
            Assert.False(p.Last.Live);
            Assert.Equal("clickThrough", (string?)posted[0]["type"]);
        }, eye, _ => { calibrations++; ghostedAtCalibration = window!.IsFypGhosted; return Task.CompletedTask; });
    }

    [Fact]
    public void TheParkedFeedKeepsRendering_ChromiumIsToldNotToCallItOccluded()
    {
        // WPF FypHostService :108: without these the mirror freezes on a still frame.
        Assert.Contains("--disable-features=CalculateNativeWinOcclusion", ConditioningControlPanel.Avalonia.Views.Controls.WebHost.WindowsBrowserArguments);
        Assert.Contains("--disable-backgrounding-occluded-windows", ConditioningControlPanel.Avalonia.Views.Controls.WebHost.WindowsBrowserArguments);
    }

    // ---- the head-free decisions (WPF FypGhostFallbackTests) ------------------------------------

    private const int Ok = 0;
    private const int Failed = unchecked((int)0x80004005);

    [Fact]
    public void Diagnose_EverythingHealthy_IsTheOnlyWayToShowTheMirror() =>
        Assert.Null(FypGhostRules.Diagnose(true, true, Ok, 1920, 1040));

    [Fact]
    public void Diagnose_RefusesInOrder_CompositionThenKeyThenRegistrationThenAnEmptySource()
    {
        Assert.Equal("composition-disabled", FypGhostRules.Diagnose(false, false, Failed, 0, 0));
        // THE black screen: a perfect thumbnail over a sheet that never dropped out.
        Assert.Equal("colorkey-rejected", FypGhostRules.Diagnose(true, false, Ok, 1920, 1040));
        Assert.Equal("thumbnail-register-failed-0x80070057", FypGhostRules.Diagnose(true, true, unchecked((int)0x80070057), 1920, 1040));
        Assert.Equal("thumbnail-source-empty", FypGhostRules.Diagnose(true, true, Ok, 0, 1040));
        Assert.Equal("thumbnail-source-empty", FypGhostRules.Diagnose(true, true, Ok, 1920, 0));
    }

    [Fact]
    public void TheMirrorCoversTheMonitor_ByCroppingTheSource_NeverByLetterboxing()
    {
        Assert.Equal((0, 0, 1920, 1080), FypGhostRules.CoverSource(1920, 1080, 1920, 1080));
        // A source wider than the monitor loses columns, symmetrically.
        Assert.Equal((240, 0, 2160, 1080), FypGhostRules.CoverSource(2400, 1080, 1920, 1080));
        // A taller one loses rows.
        Assert.Equal((0, 60, 1920, 1140), FypGhostRules.CoverSource(1920, 1200, 1920, 1080));
        Assert.Equal((0, 0, 0, 0), FypGhostRules.CoverSource(0, 0, 1920, 1080));
    }

    [Fact]
    public void Opacity_IsClampedSoTheMirrorIsNeverInvisibleOrOutOfRange()
    {
        Assert.Equal(255, FypGhostRules.OpacityByte(1.0));
        Assert.Equal(255, FypGhostRules.OpacityByte(7));
        Assert.Equal(255, FypGhostRules.OpacityByte(double.NaN));   // a broken value reads as solid, never as nothing
        Assert.Equal(3, FypGhostRules.OpacityByte(0));              // the 0.01 floor
        Assert.Equal(153, FypGhostRules.OpacityByte(0.6));
    }
}
