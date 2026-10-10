using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// WPF MainWindow.StartEngine / StopEngine arming matrix for the Core-driven features, and the
// card rule: a toggle only saves while stopped and applies live while running.
[Collection(SessionStatics.Name)]
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

    // #872: a preset load stops what it switched off and never starts what it switched on.
    [Fact]
    public void Reconcile_stops_cleared_features_and_starts_nothing()
    {
        var s = CoreSettings.Current;
        s.FlashEnabled = s.LockCardEnabled = true;
        s.SubliminalEnabled = s.BouncingTextEnabled = false;
        try
        {
            CoreEngine.Start();
            Assert.True(CoreFlash.IsRunning && LockCardScheduler.Instance.IsRunning);
            s.FlashEnabled = false;          // the preset cleared flash...
            s.SubliminalEnabled = true;      // ...and set subliminal
            CoreEngine.Reconcile();
            Assert.False(CoreFlash.IsRunning);
            Assert.True(LockCardScheduler.Instance.IsRunning);
            Assert.False(CoreSubliminal.IsRunning);
        }
        finally { CoreEngine.Stop(); }
    }

    // WPF StartStop.cs:293: a system start (the Lockdown Dose keeper) is not a session the user chose,
    // and EMI's engineStarted moment carries the real flag; a press counts and says so.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_system_start_is_not_counted_as_a_session_and_EMI_hears_the_real_flag(bool systemInitiated)
    {
        var s = CoreSettings.Current;
        var sink = ConditioningControlPanel.Services.EmiDesk.EmiDeskBus.Sink;
        var sessions = s.TotalSessions;
        bool? heard = null;
        ConditioningControlPanel.Services.EmiDesk.EmiDeskBus.Sink = (id, ctx) =>
        {
            if (id == "engineStarted") heard = (bool)ctx!.GetType().GetProperty("systemInitiated")!.GetValue(ctx)!;
        };
        try
        {
            CoreEngine.Start(systemInitiated);
            Assert.True(CoreEngine.IsRunning);
            Assert.Equal(sessions + (systemInitiated ? 0 : 1), s.TotalSessions);
            Assert.Equal(systemInitiated, heard);
        }
        finally
        {
            CoreEngine.Stop();
            ConditioningControlPanel.Services.EmiDesk.EmiDeskBus.Sink = sink;
            s.TotalSessions = sessions;
        }
    }
}
