using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// WPF MainWindow.StartEngine / StopEngine arming matrix for the Core-driven features, and the
// card rule: a toggle only saves while stopped and applies live while running.
public sealed class CoreEngineTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]   // #668 audio-only: flags On, nothing visual starts
    public void Start_arms_by_saved_flags_and_Stop_disarms_without_touching_them(bool flags, bool audioOnly)
    {
        var s = CoreSettings.Current;
        s.AudioOnlySession = audioOnly;
        var on = flags && !audioOnly;
        s.FlashEnabled = s.SubliminalEnabled = s.LockCardEnabled = s.BouncingTextEnabled = flags;
        int btStart = 0, btStop = 0, hook = 0;
        CoreBouncingText.StartAction = () => btStart++;
        CoreBouncingText.StopAction = () => btStop++;
        CoreEngine.StoppedHook = () => hook++;
        var sessions = s.TotalSessions;
        try
        {
            CoreEngine.Start();
            Assert.True(CoreEngine.IsRunning);
            Assert.Equal(!audioOnly, CoreFlash.IsRunning);   // started unless audio-only; it gates on FlashEnabled itself
            Assert.Equal(on, CoreSubliminal.IsRunning);
            Assert.Equal(on, LockCardScheduler.Instance.IsRunning);
            Assert.Equal((on ? 1 : 0, on ? 0 : 1), (btStart, btStop));
            Assert.Equal(sessions + 1, s.TotalSessions);

            var stopsBefore = btStop;
            CoreEngine.Stop();
            Assert.False(CoreEngine.IsRunning || CoreFlash.IsRunning || CoreSubliminal.IsRunning || LockCardScheduler.Instance.IsRunning);
            Assert.Equal(1, hook);
            Assert.Equal(stopsBefore + 1, btStop);
            Assert.Equal(flags, s.FlashEnabled && s.SubliminalEnabled && s.LockCardEnabled && s.BouncingTextEnabled);
        }
        finally { s.AudioOnlySession = false; CoreEngine.Stop(); CoreEngine.StoppedHook = null; CoreBouncingText.StartAction = CoreBouncingText.StopAction = null; }
    }

    [Fact]
    public void Toggles_do_nothing_while_stopped_and_apply_live_while_running()
    {
        var s = CoreSettings.Current;
        s.FlashEnabled = s.SubliminalEnabled = s.LockCardEnabled = s.BouncingTextEnabled = false;
        int btStart = 0;
        CoreBouncingText.StartAction = () => btStart++;
        CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;
        try
        {
            // Stopped: flags save, nothing arms.
            s.FlashEnabled = s.LockCardEnabled = s.BouncingTextEnabled = true;
            foreach (var k in new[] { "flash", "lockcard", "bouncingtext" }) CoreEngine.ApplyLive(k, true);
            CoreSubliminal.SetEnabled(true);
            Assert.False(CoreFlash.IsRunning || CoreSubliminal.IsRunning || LockCardScheduler.Instance.IsRunning);
            Assert.Equal(0, btStart);
            Assert.True(s.SubliminalEnabled);

            // Running: the same toggles off then on apply at once.
            CoreEngine.Start();
            CoreSubliminal.SetEnabled(false);
            CoreEngine.ApplyLive("flash", false);
            CoreEngine.ApplyLive("lockcard", false);
            Assert.False(CoreFlash.IsRunning || CoreSubliminal.IsRunning || LockCardScheduler.Instance.IsRunning);
            CoreSubliminal.SetEnabled(true);
            CoreEngine.ApplyLive("flash", true);
            CoreEngine.ApplyLive("lockcard", true);
            Assert.True(CoreFlash.IsRunning && CoreSubliminal.IsRunning && LockCardScheduler.Instance.IsRunning);
        }
        finally
        {
            CoreEngine.Stop();
            CoreSession.IsEngineRunningProvider = null;
            CoreBouncingText.StartAction = null;
        }
    }
}
