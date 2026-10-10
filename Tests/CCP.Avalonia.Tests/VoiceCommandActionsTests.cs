using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class VoiceCommandActionsTests
{
    /// <summary>HC12: the spoken commands WPF has beyond start / stop are in this head's grammar and
    /// drive the real seams: volume and mute write through, the tint and spiral holds hand the
    /// user's switches back on "off" and on panic, a video pause is released again.</summary>
    [Fact]
    public void TheWiderCommandSetIsInTheGrammarAndDrivesTheSeams() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var consent = CoreSettings.Current.MicConsentGiven;
        CoreSettings.Current.MicConsentGiven = false;   // the mic never opens in this test
        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        var s = CoreSettings.Current;
        var saved = (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted, s.SpiralEnabled, s.PinkFilterEnabled, s.PinkFilterOpacity);
        var video = ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance;
        try
        {
            var names = shell.VoiceCmds.Available.Select(i => i.Name).ToList();
            foreach (var n in new[] { "spiral_on", "spiral_off", "pink_on", "pink_off", "mute", "unmute", "louder", "quieter" })
                Assert.Contains(n, names);
            Assert.Equal(global::ConditioningControlPanel.Avalonia.App.Sessions != null, names.Contains("pause") && names.Contains("resume"));
            Assert.Equal(CoreMindWipe.TriggerOnceProvider != null, names.Contains("wipe_once"));
            Assert.Equal(CoreEngine.PopQuiz != null, names.Contains("quiz_once"));
            Assert.Equal(CoreEngine.BubbleCount != null, names.Contains("count_once"));
            Assert.Equal(CoreEngine.Video != null, names.Contains("video_pause"));
            Assert.DoesNotContain("shake_once", names);   // no seam on this head: not offered

            s.MasterVolume = 40;
            shell.VoiceAction("louder")!();
            Assert.Equal(55, s.MasterVolume);
            shell.VoiceAction("quieter")!();
            shell.VoiceAction("mute")!();
            Assert.Equal((0, false, true), (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted));
            shell.VoiceAction("unmute")!();
            Assert.Equal((40, true, false), (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted));

            (s.PinkFilterEnabled, s.PinkFilterOpacity, s.SpiralEnabled) = (false, 10, false);
            shell.VoiceAction("pink_on")!();
            Assert.True(s.PinkFilterEnabled && s.PinkFilterOpacity == 40 && MainShellWindow.VoicePinkHold);
            shell.VoiceAction("pink_off")!();
            Assert.Equal((false, 10, false), (s.PinkFilterEnabled, s.PinkFilterOpacity, MainShellWindow.VoicePinkHold));
            s.PinkFilterOpacity = 45;                     // a stronger live tint is never dimmed
            shell.VoiceAction("pink_on")!();
            Assert.Equal(45, s.PinkFilterOpacity);
            shell.VoiceAction("spiral_on")!();
            Assert.True(s.SpiralEnabled && MainShellWindow.VoiceSpiralHold);
            PanicSurfaces.StopAll("test", shell);         // panic hands both back
            Assert.False(s.SpiralEnabled || s.PinkFilterEnabled || MainShellWindow.VoiceSpiralHold || MainShellWindow.VoicePinkHold);
            Assert.Equal(45, s.PinkFilterOpacity);

            if (shell.VoiceAction("video_pause") is { } pause)
            {
                pause();
                Assert.True(video.ExternalPaused);
                shell.VoiceAction("video_resume")!();
                Assert.False(video.ExternalPaused);
            }
        }
        finally
        {
            shell.DropVoiceHolds();
            video.SetExternalPause(false);
            (s.MasterVolume, s.SubAudioEnabled, s.AvatarMuted, s.SpiralEnabled, s.PinkFilterEnabled, s.PinkFilterOpacity) = saved;
            s.MicConsentGiven = consent;
            CoreSettings.SaveImmediate();
            shell.RequestExit();
        }
    });
}
