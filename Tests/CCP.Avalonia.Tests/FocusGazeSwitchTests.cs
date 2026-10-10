using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>u1 W3 / G5: Play > Focus Gaze is a real switch. WPF order: the Prime gate, webcam consent, the
/// camera, then the engine; it stays on only when the engine went active. No camera is opened (the frame
/// source is disabled for tests, so a real start reads as "no camera").</summary>
[Collection(RunsAloneCollection.Name)]
public sealed class FocusGazeSwitchTests
{
    private sealed class Fake : IGazeTarget
    {
        public GazeTargetKind Kind => GazeTargetKind.Bubble;
        public object Key => this;
        public (double X, double Y, double W, double H) Bounds => (100, 100, 80, 80);
        public int Activated;
        public void SetDwellProgress(double t01) { }
        public void Activate() => Activated++;
        public void BoostLifetime(int extraMs) { }
    }

    [Fact]
    public async Task TheSwitch_FollowsTheGateConsentCameraAndEngine()
    {
        var s = CoreSettings.Current;
        var old = (s.FocusGazeEnabled, s.WebcamConsentGiven, s.WebcamConsentVersion);
        var oldUse = (s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.BubbleGazePopEnabled, s.VideoGazeClickEnabled);
        var oldLab = CoreEntitlement.HasLabProvider;
        var focus = GazeFocusHead.Instance;
        try
        {
            s.FocusGazeEnabled = false; s.WebcamConsentGiven = false; s.WebcamConsentVersion = "";
            // The per-feature gaze options start the engine by themselves (WPF AnyConsumerOn): off here.
            s.FlashGazePopEnabled = false; s.FlashGazeLingerEnabled = false; s.BubbleGazePopEnabled = false; s.VideoGazeClickEnabled = false;
            await AvaloniaTestDispatcher.RunAsync(async () =>
            {
                if (Application.Current is null)
                    AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                var view = new PlayTabView();
                var box = view.FindControl<CheckBox>("ChkPlayFocusGaze")!;
                var line = view.FindControl<TextBlock>("TxtPlayFocusGazeStatus")!;

                // Not Prime: falls back, nothing saved.
                CoreEntitlement.HasLabProvider = () => false;
                box.IsChecked = true;
                Assert.Equal("tier", await view.LastFocusGazePress!);
                Assert.False(box.IsChecked);
                Assert.False(s.FocusGazeEnabled);

                // Prime, consent declined.
                CoreEntitlement.HasLabProvider = () => true;
                view.AskWebcamConsent = () => Task.FromResult(false);
                box.IsChecked = true;
                Assert.Equal("consent", await view.LastFocusGazePress!);
                Assert.False(box.IsChecked);
                Assert.Equal(Loc.Get("label_focus_gaze_consent_required"), line.Text);
                Assert.False(WebcamTracker.Instance.IsRunning);

                // Consent given, engine able (stands in for tracking + a calibration): on, and it says so.
                view.AskWebcamConsent = () => { s.WebcamConsentGiven = true; s.WebcamConsentVersion = WebcamConsent.ConsentVersion; return Task.FromResult(true); };
                focus.CanRunOverride = () => true;
                box.IsChecked = true;
                Assert.Equal("on", await view.LastFocusGazePress!);
                Assert.True(box.IsChecked);
                Assert.True(s.FocusGazeEnabled);
                Assert.True(focus.IsActive);
                Assert.Equal(Loc.Get("label_focus_gaze_active"), line.Text);

                // The head drives the Core engine: a 600 ms dwell pops the target.
                var target = new Fake();
                var now = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
                focus.TargetsOverride = () => new List<IGazeTarget> { target };
                focus.Now = () => now;
                focus.OnGaze(new Point(140, 140));
                focus.Tick();
                now = now.AddMilliseconds(650);
                focus.Tick();
                Assert.Equal(1, target.Activated);

                // Off is never gated; the engine stands down.
                CoreEntitlement.HasLabProvider = () => false;
                box.IsChecked = false;
                Assert.Equal("off", await view.LastFocusGazePress!);
                Assert.False(s.FocusGazeEnabled);
                Assert.False(focus.IsActive);
                Assert.Equal("", line.Text);
            });
        }
        finally
        {
            focus.MasterEnabled = false;
            focus.Stop();
            focus.CanRunOverride = null; focus.TargetsOverride = null; focus.Now = () => DateTime.UtcNow;
            CoreEntitlement.HasLabProvider = oldLab;
            (s.FocusGazeEnabled, s.WebcamConsentGiven, s.WebcamConsentVersion) = old;
            (s.FlashGazePopEnabled, s.FlashGazeLingerEnabled, s.BubbleGazePopEnabled, s.VideoGazeClickEnabled) = oldUse;
            CoreSettings.SaveImmediate();
        }
    }
}
