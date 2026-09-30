using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Webcam;
using OpenCvSharp;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
using Window = Avalonia.Controls.Window;

namespace CCP.Avalonia.Tests;

/// <summary>A Blink Trainer session (WPF BlinkTrainerService) over a fake frame source - never a real
/// camera - and headless overlay windows: WPF's refusal order, the tile grid, the swap on a blink,
/// Stop, the page's Start/Stop + countdown + live preview, and revoke ending it all.</summary>
public sealed class BlinkTrainerSessionTests
{
    private sealed class BlackFrames : IFrameSource
    {
        public bool Open() => true;
        public bool Read(Mat bgr) { bgr.Create(480, 640, MatType.CV_8UC3); bgr.SetTo(Scalar.All(0)); Thread.Sleep(10); return true; }
        public void Dispose() { }
    }

    [Fact]
    public void Session_RefusesInWpfOrder_TilesSwapsAndStops_AndThePageFollows()
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var s = CoreSettings.Current;
            var old = (s.WebcamConsentGiven, s.WebcamConsentVersion, s.BlinkTrainerFolders.ToArray(), s.BlinkTrainerMixImages, s.DualMonitorEnabled);
            var oldFactory = (WebcamTracker.SourceFactory, BlinkTrainerSession.CreateOverlays);
            var oldPremium = CoreEntitlement.HasPremiumProvider;
            var dir = Directory.CreateTempSubdirectory("ccp-blink-session-").FullName;
            Window? host = null;
            try
            {
                // A 9:16 portrait image: on a 16:9 overlay WPF tiles it ceil(3.16) = 4 across.
                using (var wb = new WriteableBitmap(new PixelSize(90, 160), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
                    wb.Save(Path.Combine(dir, "portrait.png"));
                using (var wb = new WriteableBitmap(new PixelSize(160, 90), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
                    wb.Save(Path.Combine(dir, "wide.png"));

                s.BlinkTrainerMixImages = false;
                s.BlinkTrainerFolders.Clear();
                s.WebcamConsentGiven = false;
                s.WebcamConsentVersion = "";
                CoreEntitlement.HasPremiumProvider = () => true;
                WebcamTracker.SourceFactory = () => new BlackFrames();
                var made = new List<BlinkTrainerSession.OverlayWindow>();
                BlinkTrainerSession.CreateOverlays = _ =>
                {
                    var w = new BlinkTrainerSession.OverlayWindow { Width = 1600, Height = 900 };
                    made.Add(w);
                    return new() { w };
                };

                var tab = new BlinkTrainerTabView { IsVisible = false };
                host = new Window { Width = 1400, Height = 1000, Content = tab };
                host.Show();
                tab.IsVisible = true;
                Dispatcher.UIThread.RunJobs();

                // WPF Start's order: folders, then consent, then a running tracker. Nothing is created.
                Assert.False(BlinkTrainerSession.Start(tab));
                Assert.Equal(Loc.Get("blink_trainer_error_no_folders"), BlinkTrainerSession.LastError);
                s.BlinkTrainerFolders.Add(dir);
                Assert.False(BlinkTrainerSession.Start(tab));
                Assert.Equal(Loc.Get("blink_trainer_error_no_consent"), BlinkTrainerSession.LastError);
                s.WebcamConsentGiven = true;
                s.WebcamConsentVersion = WebcamConsent.ConsentVersion;
                Assert.False(BlinkTrainerSession.Start(tab));
                Assert.Equal(Loc.Get("blink_trainer_error_webcam_not_running"), BlinkTrainerSession.LastError);
                Assert.Empty(made);
                tab.Refresh();
                Assert.Equal(BlinkTrainerStatusState.Error, tab.StatusState);
                Assert.True(tab.FindControl<Button>("BtnBlinkTrainerStartSession")!.IsEnabled);   // WPF: Error lets you retry

                // Tracker up over the fake source, then a session: one overlay, first asset shown.
                Assert.True(WebcamTracker.Instance.Start());
                Assert.True(BlinkTrainerSession.Start(tab));
                Dispatcher.UIThread.RunJobs();
                Assert.True(BlinkTrainerSession.IsRunning);
                Assert.Equal("", BlinkTrainerSession.LastError);
                var ov = Assert.Single(made);
                Assert.Equal(s.BlinkTrainerOpacity / 100.0, ov.Host.Opacity, 3);
                Assert.Single(ov.Host.Children);

                // Each blink swaps; the portrait image tiles 4 across (Core TileGrid), the wide one shows once.
                bool sawGrid = false, sawSingle = false;
                for (int i = 0; i < 20 && !(sawGrid && sawSingle); i++)
                {
                    BlinkTrainerSession.HandleBlink();
                    var child = Assert.Single(ov.Host.Children);
                    if (child is UniformGrid g) { Assert.Equal(4, g.Columns); Assert.Equal(4, g.Children.Count); sawGrid = true; }
                    else { Assert.IsType<Image>(child); sawSingle = true; }
                }
                Assert.True(sawGrid && sawSingle);

                // The page: Running, Stop label, a countdown, and the stage swapping on a blink.
                Assert.Equal(BlinkTrainerStatusState.Running, tab.StatusState);
                var start = tab.FindControl<Button>("BtnBlinkTrainerStartSession")!;
                Assert.True(start.IsEnabled);
                Assert.Equal(Loc.Get("blink_trainer_stop_session"), ((TextBlock)start.Content!).Text);
                Assert.Matches(@"\d\d:\d\d", tab.FindControl<TextBlock>("BlinkTrainerStatusText")!.Text);
                Assert.True(tab.LivePreview);
                Assert.False(tab.DemoRunning);
                tab.OnStagePreviewBlink();
                var a = tab.FindControl<Image>("BlinkTrainerStageImageA")!;
                var b = tab.FindControl<Image>("BlinkTrainerStageImageB")!;
                Assert.Equal(1, Math.Max(a.Opacity, b.Opacity));
                Assert.NotNull((a.Opacity == 1 ? a : b).Source);

                // Stop closes the overlay; revoke of a running session ends it and the tracker.
                BlinkTrainerSession.Stop();
                Assert.False(BlinkTrainerSession.IsRunning);
                Assert.Empty(BlinkTrainerSession.Windows);
                Assert.Equal(BlinkTrainerStatusState.IdleReady, tab.StatusState);
                Assert.True(BlinkTrainerSession.Start(tab));
                tab.RevokeConsent();
                Assert.False(BlinkTrainerSession.IsRunning);
                Assert.False(WebcamTracker.Instance.IsRunning);
                Assert.False(WebcamConsent.IsCurrent(s));
            }
            finally
            {
                BlinkTrainerSession.Stop();
                WebcamTracker.Instance.Stop();
                host?.Close();
                (WebcamTracker.SourceFactory, BlinkTrainerSession.CreateOverlays) = oldFactory;
                CoreEntitlement.HasPremiumProvider = oldPremium;
                (s.WebcamConsentGiven, s.WebcamConsentVersion, _, s.BlinkTrainerMixImages, s.DualMonitorEnabled) = old;
                s.BlinkTrainerFolders.Clear();
                s.BlinkTrainerFolders.AddRange(old.Item3);
                Directory.Delete(dir, true);
            }
        });
    }
}
